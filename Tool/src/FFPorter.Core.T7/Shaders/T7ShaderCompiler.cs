using System.Security.Cryptography;
using System.Text;
using FFPorter.Core.Common.Tools;

namespace FFPorter.Core.T7.Shaders;

public sealed class T7ShaderCompiler
{
    public const string RulesVersion = "t7-pssl-4";

    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(180);

    public sealed record Program(byte[] Header, byte[] Code, IReadOnlyList<string> Globals);

    private readonly string _decompiler, _compiler, _cache;
    private readonly Action<string> _log;
    private readonly object _gate = new();

    public T7ShaderCompiler(string decompiler, string compiler, string cacheDirectory, Action<string> log)
    {
        _decompiler = decompiler;
        _compiler = compiler;
        _cache = cacheDirectory;
        _log = log;
        Directory.CreateDirectory(cacheDirectory);
    }

    public int Compiled { get; private set; }
    public int CacheHits { get; private set; }
    public int Decompiled { get; private set; }

    public static T7ShaderCompiler? TryCreate(Workspace workspace, string cacheDirectory, Action<string> log, out string? why)
    {
        string sdk = Ps4Sdk.BinDirectory();
        string compiler = Path.Combine(sdk, Ps4Sdk.ShaderCompilerName);
        string decompiler = ShaderDecompiler.Locate(workspace);
        why = !File.Exists(compiler) ? $"the PS4 SDK compiler is not installed ({compiler})"
            : !File.Exists(decompiler) ? $"the DXBC decompiler is missing ({decompiler})"
            : !T7GnmSdk.Available(sdk) ? $"the PS4 SDK host libraries are missing ({sdk})"
            : null;
        return why == null ? new T7ShaderCompiler(decompiler, compiler, cacheDirectory, log) : null;
    }

    public string Decompile(ReadOnlySpan<byte> dxbc)
    {
        string folder = ProgramFolder(dxbc);
        string hlsl = Path.Combine(folder, "source.hlsl");
        lock (_gate)
        {
            if (File.Exists(hlsl))
                return File.ReadAllText(hlsl);
            string source = Path.Combine(folder, "source.dxbc");
            File.WriteAllBytes(source, dxbc.ToArray());
            (int exit, string output) = Run(_decompiler, ["-D", "-V", source], folder);
            if (exit != 0 || !File.Exists(Path.Combine(folder, "source.hlsl")))
                (exit, output) = Run(_decompiler, ["-D", source], folder);
            if (!File.Exists(hlsl))
                throw new InvalidDataException($"the DXBC decompiler could not recover this shader: {Tail(output)}");
            return File.ReadAllText(hlsl);
        }
    }

    public IReadOnlyList<string> Globals(ReadOnlySpan<byte> dxbc)
    {
        string hlsl = Decompile(dxbc);
        return T7PsslSource.Transform(hlsl, "ps", GlobalsReflection(dxbc), null, null).Globals;
    }

    public Program Compile(string stage, ReadOnlySpan<byte> dxbc, IReadOnlyDictionary<int, int>? textureRemap, IReadOnlyDictionary<int, int>? samplerRemap,
        IReadOnlySet<int>? absentTextures = null, IReadOnlySet<string>? keptGlobals = null, T7PassTargets? targets = null)
    {
        targets = stage == "ps" ? targets ?? T7PassTargets.Plain : T7PassTargets.Plain;
        string variant = Variant(stage, textureRemap, samplerRemap, absentTextures, keptGlobals, targets);
        string folder = Path.Combine(ProgramFolder(dxbc), variant);
        string headerPath = Path.Combine(folder, "gnmx_header.bin"), codePath = Path.Combine(folder, "gpu_code.bin"), globalsPath = Path.Combine(folder, "globals.txt");
        lock (_gate)
        {
            if (File.Exists(headerPath) && File.Exists(codePath) && File.Exists(globalsPath))
            {
                CacheHits++;
                return new Program(File.ReadAllBytes(headerPath), File.ReadAllBytes(codePath), File.ReadAllLines(globalsPath));
            }
        }
        string text;
        IReadOnlyList<string> globals;
        string programFolder = ProgramFolder(dxbc);
        lock (_gate)
        {
            string original = Path.Combine(programFolder, "source.dxbc");
            if (!File.Exists(original))
                File.WriteAllBytes(original, dxbc.ToArray());
        }
        try
        {
            T7DxbcTranslator.Result translated = T7DxbcTranslator.Translate(dxbc, stage, textureRemap, samplerRemap, absentTextures, keptGlobals, targets);
            (text, globals) = (translated.Text, translated.Globals);
        }
        catch (NotSupportedException unsupported) when (absentTextures is { Count: > 0 })
        {
            throw new InvalidDataException($"shader {Path.GetFileName(programFolder)}: {unsupported.Message}, and it reads resources the PS4 does not bind (t{string.Join(", t", absentTextures.Order())})");
        }
        catch (NotSupportedException unsupported) when (targets.Gbuffer && T7DxbcTranslator.WritesGbuffer(dxbc))
        {
            throw new InvalidDataException($"shader {Path.GetFileName(programFolder)}: {unsupported.Message}, and it writes the G-buffer, which only the bytecode translation lays out for PS4");
        }
        catch (NotSupportedException unsupported) when (GlobalsReflection(dxbc)?.Any(v => v.Used) == true)
        {
            throw new InvalidDataException($"shader {Path.GetFileName(programFolder)}: {unsupported.Message}, and it reads material constants, which only the bytecode translation moves to the PS4 layout");
        }
        catch (NotSupportedException unsupported)
        {
            string hlsl = Decompile(dxbc);
            T7PsslSource.Result pssl = T7PsslSource.Transform(hlsl, stage, GlobalsReflection(dxbc), textureRemap, samplerRemap);
            (text, globals) = (pssl.Text, pssl.Globals);
            lock (_gate)
                Decompiled++;
            _log($"shader {Path.GetFileName(programFolder)}: {unsupported.Message}; compiled from decompiled HLSL instead");
        }
        lock (_variantGates.GetOrAdd(folder, _ => new object()))
        {
            if (File.Exists(headerPath) && File.Exists(codePath) && File.Exists(globalsPath))
            {
                lock (_gate)
                    CacheHits++;
                return new Program(File.ReadAllBytes(headerPath), File.ReadAllBytes(codePath), File.ReadAllLines(globalsPath));
            }
            Directory.CreateDirectory(folder);
            string source = Path.Combine(folder, "shader.pssl"), binary = Path.Combine(folder, "shader.sb");
            File.WriteAllText(source, text);
            File.Delete(binary);
            List<string> arguments = stage switch
            {
                "vs" => ["-profile", "sce_vs_vs_orbis", "-indirect-draw", "-inlinefetchshader", "-max-user-extdata-count", "240"],
                "ps" => ["-profile", "sce_ps_orbis", "-max-user-extdata-count", "240"],
                _ => throw new InvalidDataException($"{stage} shaders are not converted"),
            };
            if (targets.MrtFormat != T7PassTargets.DefaultMrtFormat)
                arguments.AddRange(["-mrtformat", $"0x{targets.MrtFormat:x8}"]);
            arguments.AddRange(["-o", binary, source]);
            (int exit, string output) = Run(_compiler, arguments, folder);
            File.WriteAllText(Path.Combine(folder, "compile.log"), output);
            if (exit != 0 || !File.Exists(binary))
            {
                string errors = string.Join(" | ", output.Split('\n').Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)).Take(3)
                    .Select(l => l.Contains(": error", StringComparison.Ordinal) ? l[(l.IndexOf(": error", StringComparison.Ordinal) + 2)..].Trim() : l.Trim()));
                throw new InvalidDataException($"PSSL compile of a {stage} program failed ({folder}): {errors}");
            }
            (byte[] header, byte[] code) = T7GnmSdk.SplitShaderBinary(File.ReadAllBytes(binary));
            File.WriteAllBytes(headerPath, header);
            File.WriteAllBytes(codePath, code);
            File.WriteAllLines(globalsPath, globals);
            lock (_gate)
                Compiled++;
            return new Program(header, code, globals);
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _variantGates = new(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<T7Dxbc.Variable>? GlobalsReflection(ReadOnlySpan<byte> dxbc) =>
        T7Dxbc.ConstantBuffers(dxbc)?.FirstOrDefault(b => b.Name == "$Globals")?.Variables;

    private string ProgramFolder(ReadOnlySpan<byte> dxbc)
    {
        string folder = Path.Combine(_cache, Convert.ToHexStringLower(SHA256.HashData(dxbc))[..24]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string Variant(string stage, IReadOnlyDictionary<int, int>? textures, IReadOnlyDictionary<int, int>? samplers, IReadOnlySet<int>? absent,
        IReadOnlySet<string>? keptGlobals, T7PassTargets targets)
    {
        var text = new StringBuilder(RulesVersion).Append('|').Append(T7DxbcTranslator.VersionOf(stage)).Append('|').Append(stage);
        foreach ((string label, IReadOnlyDictionary<int, int>? map) in (ReadOnlySpan<(string, IReadOnlyDictionary<int, int>?)>)[("t", textures), ("s", samplers)])
        {
            text.Append('|').Append(label);
            if (map != null)
                foreach ((int from, int to) in map.OrderBy(p => p.Key))
                    text.Append(from).Append('>').Append(to).Append(',');
        }
        if (absent is { Count: > 0 })
            text.Append("|a").Append(string.Join(",", absent.Order()));
        if (keptGlobals is { Count: > 0 })
            text.Append("|k").Append(string.Join(",", keptGlobals.Order(StringComparer.Ordinal)));
        text.Append(targets.Key);
        return stage + "_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    private static (int Exit, string Output) Run(string tool, IReadOnlyList<string> arguments, string directory)
    {
        ProcessRunner.Result result = ProcessRunner.Run(tool, arguments, directory, ToolTimeout);
        if (result.TimedOut)
            throw new IOException($"{Path.GetFileName(tool)} did not finish in {ToolTimeout.TotalSeconds} seconds");
        return (result.ExitCode, result.Output);
    }

    private static string Tail(string text) => text.Length <= 300 ? text.Trim() : text[^300..].Trim();
}
