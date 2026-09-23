using System.Text.Json;
using FFPorter.Core;
using FFPorter.Core.Common.Native;
using FFPorter.Core.T7;
using FFPorter.Core.T7.Harness;
using Options = FFPorter.Cli.CliOptions;

namespace FFPorter.Cli;

public static class T7Cli
{
    public const string Usage = """
        usage: ffport t7 <command> [options]
          info <zone.ff>                         header, block sizes and asset counts
          decode <zone.ff> -o <out.zone> [--apply-fd]   write the decompressed zone (with the .fd patch beside it applied)
          apply-fd <zone.ff> -o <patched.ff>     apply the .fd patch beside a zone and write the patched fastfile
          ps4-walk <zone.ff> [-o <walk>] [--limit N]   walk a PS4 zone through the PS4 loader
          pc-walk <zone.ff> [-o <walk>] [--image <BlackOps3 dump>] [--limit N]   walk a PC zone through the PC loader
          relink-identity <ps4 zone.ff> [--work <dir>]   rebuild a PS4 zone through the linker (must be byte-identical)
          convert <pc zone.ff> -o <folder> [--donors <PS4 zone folder>] [--pc-reference <PC BO3 zone folder>] [--ps4-index <PS4 xpak folder>] [--image <dump>]
                  [--force] [--keep-work] [--donor-all] [--no-xpak] [--no-sound] [--no-fd] [--languages en[,fr...]] [--no-shader-compile]
                  [--acts <acts.exe>] [--gsc-recompile] [--no-gsc-check] [--no-bundle-streams] [--no-movies] [--progress]
                                                 convert a PC map (zones, xpaks, sound banks, usermap movies) for PS4; .fd
                                                 patches beside PC and reference zones are applied first unless --no-fd;
                                                 streamed items the map only references are converted into its xpaks from
                                                 the --pc-reference folder's xpaks unless a PS4 index knows them. Ends with
                                                 the port fidelity table (also written as <map>.fidelity.json); --progress
                                                 also prints "FFPORTER_FIDELITY {json}" snapshots while it runs (the desktop app)
          movie <video> [-o <out.mkv>]           list what keeps a movie from playing on PS4; with -o write it PS4-ready
                                                 (copied, or re-encoded to H.264 High 1920x1080 30 fps like retail)
          game-zones <pc zone.ff>                the Black Ops III zone folder a conversion of the zone uses (above the zone,
                                                 remembered from an earlier zone, or a Steam install); one above is remembered
          zone-compare <converted ps4.ff> <retail ps4.ff> [--type N]   per-asset differences from a retail zone
          xpak-convert <pc.xpak> -o <ps4.xpak> [--index <pc _d.xpak> --index-out <ps4 _d.xpak>] [--ps4-index <folder|xpak>] [--compare <retail ps4.xpak>]
                                                 convert streamed data (images; other items need their zone)
          xpak-verify <file.xpak>                recompute keys and rebuild; must be byte-identical
          techset-args <converted ps4.ff> <retail ps4.ff> [--name X] [--all]   pass arguments of shared technique sets, side by side
          material-buffers <converted ps4.ff> <retail ps4.ff> [--name X] [--all]   constant buffers of shared materials, slot by slot
          world-layers <zone.ff> --walk <t7walk> -o <file> [--apply-fd]   write the world's per-vertex layer data (colour, uv, normal, tangent)
          shader-dump <ps4.ff> --techset X -o <folder>   write every program of a PS4 technique set (Gnmx header + GPU code per technique/pass/stage)
          material-textures <zone.ff> --walk <t7walk> --name X [--pc] [--apply-fd]   a material's texture table, entry by entry, with the loads around it
          material-globals <pc zone.ff> --walk <pc t7walk> --name X [--apply-fd]   a PC material's $Globals values by variable name (technique 3, pass 0)
          material-fields <zone.ff> [--name X] [--pc --walk <pc t7walk> [--apply-fd]]   every material root: info bytes 0x08..0x30, tail 0x258..0x298, technique set
          techset-fields <ps4.ff> [--name X]     every PS4 technique set header (112 bytes) as hex
          image-parts <ps4.ff> [--name X]        the streamed parts (keys, levels) and format of every PS4 image loaded
          xpak-index <file.xpak> [--name <substring>]   the index record of every stored item, as the xpak spells it
          xpak-extract <file.xpak> --name <substring> -o <folder>   the stored payload of every matching item
          xpak-compare <converted.xpak> <retail ps4.xpak>   count the catalog items whose keys (content hashes) match retail
          sound-convert <pc .sabl/.sabs> -o <ps4 bank> [--compare <stock ps4 bank>]
          shader-pssl <program.dxbc> [-o <out.pssl>] [--disassemble] [--technique N [--set <name>]]
                                                 translate a PC shader program to PSSL (or list its bytecode); with a
                                                 technique, pixel programs write that pass's PS4 render targets
          shader-check <folder> [--cache <dir>]  translate and compile every <folder>/*/source.dxbc for PS4
        """;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            stdout.Write(Usage);
            return args.Length == 0 ? 2 : 0;
        }
        try
        {
            var options = Options.Parse(args[1..]);
            return args[0] switch
            {
                "info" => Info(options, stdout),
                "decode" => Decode(options, stdout),
                "apply-fd" => ApplyFd(options, stdout),
                "ps4-walk" => Ps4Walk(options, stdout, stderr),
                "pc-walk" => PcWalk(options, stdout, stderr),
                "relink-identity" => RelinkIdentity(options, stdout, stderr),
                "convert" => Convert(options, stdout, stderr),
                "movie" => Movie(options, stdout),
                "xpak-verify" => XPakVerify(options, stdout),
                "zone-compare" => ZoneCompare(options, stdout, stderr),
                "game-zones" => GameZones(options, stdout),
                "techset-args" => TechsetArgs(options, stdout, stderr),
                "image-parts" => ImageParts(options, stdout, stderr),
                "material-buffers" => MaterialBuffers(options, stdout, stderr),
                "material-fields" => MaterialFields(options, stdout, stderr),
                "techset-fields" => TechsetFields(options, stdout, stderr),
                "world-layers" => WorldLayers(options, stdout),
                "shader-dump" => ShaderDump(options, stdout, stderr),
                "material-textures" => MaterialTextures(options, stdout),
                "material-globals" => MaterialGlobals(options, stdout),
                "xpak-index" => XPakIndex(options, stdout),
                "xpak-extract" => XPakExtract(options, stdout),
                "xpak-convert" => XPakConvert(options, stdout),
                "xpak-compare" => XPakCompare(options, stdout),
                "sound-convert" => SoundConvert(options, stdout),
                "shader-pssl" => ShaderPssl(options, stdout),
                "shader-check" => ShaderCheck(options, stdout),
                _ => throw new ArgumentException($"unknown t7 command {args[0]}"),
            };
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"ffport t7: {error.Message}");
            return 2;
        }
    }

    private static int Info(Options options, TextWriter stdout)
    {
        string input = options.Positional(0, "zone.ff");
        T7FastFile.Decoded decoded = T7FastFile.Load(input);
        T7Header header = decoded.Header;
        T7ZoneList list = T7ZoneList.Parse(decoded.Zone);
        string? loader = null;
        try
        {
            loader = T7Paths.Ps4Loader(Workspace.Locate(options.Value("--root")));
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
        {
        }
        var record = new
        {
            file = Path.GetFullPath(input),
            zone = header.ZoneName,
            platform = header.PlatformName,
            builder = header.Builder,
            archive_checksum = System.Convert.ToHexString(header.ArchiveChecksum),
            ps4_header_problems = header.Platform == 2 && loader != null && File.Exists(Path.Combine(loader, "segments.json"))
                ? FFPorter.Core.T7.Harness.T7Ps4Loader.HeaderProblems(header, loader) : null,
            fd_patch = T7FastFileDelta.FindFor(input),
            compression = header.Compression,
            zone_bytes = decoded.Zone.Length,
            file_blocks = decoded.Blocks,
            block_sizes = T7AssetTypes.BlockNames.Zip(header.BlockSizes).ToDictionary(p => p.First, p => p.Second),
            script_strings = list.ScriptStrings.Count,
            assets = list.Assets.Count,
            asset_types = list.Assets.GroupBy(a => a.Type).OrderBy(g => g.Key).ToDictionary(g => T7AssetTypes.Name(g.Key), g => g.Count()),
        };
        stdout.WriteLine(JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static int Decode(Options options, TextWriter stdout)
    {
        string input = options.Positional(0, "zone.ff");
        string output = options.Required("-o");
        T7FastFile.Decoded decoded = options.Flag("--apply-fd") ? T7FastFileDelta.LoadPatched(input) : T7FastFile.Load(input);
        File.WriteAllBytes(output, decoded.Zone);
        stdout.WriteLine($"{decoded.Zone.Length} zone bytes -> {Path.GetFullPath(output)}");
        return 0;
    }

    private static int ApplyFd(Options options, TextWriter stdout)
    {
        string input = options.Positional(0, "zone.ff");
        string output = options.Required("-o");
        string delta = T7FastFileDelta.FindFor(input)
            ?? throw new InvalidDataException($"no .fd patch for {Path.GetFileName(input)} (it must sit beside the .ff and name its header as the base)");
        T7FastFile.Decoded decoded = T7FastFile.Load(input);
        (T7Header header, byte[] zone) = T7FastFileDelta.Apply(decoded.Zone, File.ReadAllBytes(delta));
        File.WriteAllBytes(output, T7FastFile.Encode(header, zone));
        stdout.WriteLine($"{Path.GetFileName(delta)}: {decoded.Zone.Length} -> {zone.Length} zone bytes -> {Path.GetFullPath(output)}");
        return 0;
    }

    private static int Ps4Walk(Options options, TextWriter stdout, TextWriter stderr)
    {
        string input = Path.GetFullPath(options.Positional(0, "zone.ff"));
        string walk = Path.GetFullPath(options.Value("-o") ?? Path.ChangeExtension(input, ".ps4.t7walk"));
        int limit = int.Parse(options.Value("--limit") ?? "0");
        string loader = T7Paths.Ps4Loader(Workspace.Locate(options.Value("--root")));
        NativeProcessResult result = NativeProcess.Run(T7Ps4Loader.TaskName, T7Ps4Loader.ChildArguments(loader, walk, input, limit),
            new NativeProcessOptions { OnOutputLine = stdout.WriteLine, OnErrorLine = stderr.WriteLine });
        return result.ExitCode;
    }

    private static int PcWalk(Options options, TextWriter stdout, TextWriter stderr)
    {
        string input = Path.GetFullPath(options.Positional(0, "zone.ff"));
        string walk = Path.GetFullPath(options.Value("-o") ?? Path.ChangeExtension(input, ".pc.t7walk"));
        int limit = int.Parse(options.Value("--limit") ?? "0");
        string image = options.Value("--image") ?? T7Paths.PcImage(Workspace.Locate(options.Value("--root")));
        NativeProcessResult result = NativeProcess.Run(T7PcLoader.TaskName, T7PcLoader.ChildArguments(image, walk, input, limit),
            new NativeProcessOptions { OnOutputLine = stdout.WriteLine, OnErrorLine = stderr.WriteLine });
        return result.ExitCode;
    }

    private static int Convert(Options options, TextWriter stdout, TextWriter stderr)
    {
        string input = Path.GetFullPath(options.Positional(0, "pc map.ff"));
        string output = Path.GetFullPath(options.Required("-o"));
        Workspace workspace = Workspace.Locate(options.Value("--root"));
        string stem = Path.GetFileNameWithoutExtension(input);
        string folder = Path.GetDirectoryName(input)!;
        string work = Path.Combine(T7Paths.Work(workspace), stem);
        string loader = T7Paths.Ps4Loader(workspace);
        string image = options.Value("--image") ?? T7Paths.PcImage(workspace);
        if (!File.Exists(image) && !File.Exists(Path.Combine(image, "segments.json")))
            throw new IOException($"The PC loader data is missing ({image})");
        if (!File.Exists(Path.Combine(loader, "segments.json")))
            throw new IOException($"The PS4 loader data is missing ({loader})");
        Directory.CreateDirectory(output);
        var fidelity = new FFPorter.Core.T7.Port.T7Fidelity(stem, options.Flag("--progress")
            ? snapshot => stdout.WriteLine(FFPorter.Core.Common.Fidelity.FidelityProtocol.Format(snapshot))
            : null);
        fidelity.Tracker.Stage("Loading reference zones", "PS4 and PC reference data");

        var donorFiles = new List<string>();
        var ps4XPaks = new List<string>();
        string? donorOption = options.Value("--donors");
        if (!string.IsNullOrEmpty(donorOption))
        {
            if (Directory.Exists(donorOption))
            {
                donorFiles.AddRange(Directory.EnumerateFiles(donorOption, "*.ff", SearchOption.AllDirectories));
                ps4XPaks.AddRange(Directory.EnumerateFiles(donorOption, "*.xpak", SearchOption.AllDirectories));
            }
            else if (File.Exists(donorOption))
            {
                donorFiles.Add(donorOption);
            }
        }
        string? streamIndexOption = options.Value("--ps4-index");
        if (!string.IsNullOrEmpty(streamIndexOption))
            ps4XPaks.AddRange(Directory.Exists(streamIndexOption) ? Directory.EnumerateFiles(streamIndexOption, "*.xpak", SearchOption.AllDirectories) : [streamIndexOption]);
        var donors = FFPorter.Core.T7.Port.T7DonorLibrary.Load(donorFiles, loader, Path.Combine(T7Paths.Work(workspace), "donor_walks"), stdout.WriteLine);
        FFPorter.Core.T7.Streams.T7Ps4StreamIndex? streamIndex = null;
        if (ps4XPaks.Count > 0)
        {
            streamIndex = FFPorter.Core.T7.Streams.T7Ps4StreamIndex.Load(ps4XPaks.Distinct(StringComparer.OrdinalIgnoreCase), stdout.WriteLine);
            stdout.WriteLine($"PS4 stream catalogs: {ps4XPaks.Count} xpaks, {streamIndex.Count} items");
        }

        var pcReference = new List<string>();
        string? pcReferenceOption = options.Value("--pc-reference");
        if (string.IsNullOrEmpty(pcReferenceOption))
        {
            pcReferenceOption = T7Paths.FindGameZones(input, out string? found);
            stdout.WriteLine(pcReferenceOption != null
                ? $"PC game zones: {pcReferenceOption} ({found})"
                : "PC game zones: not found (not above the zone, not found with an earlier zone, no Steam install)");
            if (pcReferenceOption == null && donorFiles.Count == 0)
            {
                const string why = "Black Ops III's game files were not found, and the zone's materials need the game's shaders. "
                    + "Convert a map or zone from inside the game folder once (its zone folder is remembered), or pass --pc-reference <BO3 zone folder>.";
                stderr.WriteLine(why);
                fidelity.Tracker.Problem(why);
                FFPorter.Core.Common.Fidelity.FidelitySnapshot stopped = fidelity.Tracker.Finish(FFPorter.Core.Common.Fidelity.FidelityStates.Failed,
                    "Nothing was converted: Black Ops III's game files (its zone folder) were not found.");
                WriteFidelity(stopped, stdout);
                stdout.WriteLine("map conversion FAILED (1 problem)");
                return 1;
            }
        }
        if (!string.IsNullOrEmpty(pcReferenceOption))
            pcReference.AddRange(Directory.Exists(pcReferenceOption) ? Directory.EnumerateFiles(pcReferenceOption, "*.ff", SearchOption.AllDirectories) : [pcReferenceOption]);
        List<string>? pcStreamXPaks = null;
        if (!options.Flag("--no-bundle-streams") && !string.IsNullOrEmpty(pcReferenceOption) && Directory.Exists(pcReferenceOption))
            pcStreamXPaks = Directory.EnumerateFiles(pcReferenceOption, "*.xpak", SearchOption.AllDirectories).ToList();
        var shaders = FFPorter.Core.T7.Port.T7PcShaderLibrary.Create(pcReference, image, Path.Combine(T7Paths.Work(workspace), "pc_walks"), stdout.WriteLine);
        FFPorter.Core.T7.Shaders.T7ShaderCompiler? compiler = null;
        if (!options.Flag("--no-shader-compile"))
        {
            compiler = FFPorter.Core.T7.Shaders.T7ShaderCompiler.TryCreate(workspace, Path.Combine(T7Paths.Work(workspace), "shader_cache"), stdout.WriteLine, out string? why);
            stdout.WriteLine(compiler != null
                ? "technique sets with shaders are compiled for PS4 (no PS4 game files needed)"
                : $"technique sets with shaders need PS4 reference zones: {why}");
        }

        var result = FFPorter.Core.T7.Port.T7MapPort.Run(new FFPorter.Core.T7.Port.T7MapPortOptions
        {
            PcMapFastFile = input,
            OutputFolder = output,
            WorkDirectory = work,
            Ps4LoaderDirectory = loader,
            PcImage = image,
            Donors = donors,
            Workspace = workspace,
            Ps4StreamIndex = streamIndex,
            PcStreamXPaks = pcStreamXPaks,
            ShaderLibrary = shaders,
            Acts = options.Flag("--no-gsc-check") ? null : options.Value("--acts") ?? options.Value("--gsc-tool") ?? FFPorter.Core.T7.Scripts.ActsInstall.Ensure(workspace, stdout.WriteLine),
            GscRecompile = options.Flag("--gsc-recompile"),
            Force = options.Flag("--force"),
            DonorFallbackForAllTypes = options.Flag("--donor-all"),
            ConvertStreams = !options.Flag("--no-xpak"),
            ConvertSound = !options.Flag("--no-sound"),
            ConvertMovies = !options.Flag("--no-movies"),
            ApplyDelta = !options.Flag("--no-fd"),
            Languages = options.Value("--languages")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            ShaderCompiler = compiler,
            Log = stdout.WriteLine,
            Fidelity = fidelity,
        });
        foreach (string problem in result.Problems.Take(80))
            stderr.WriteLine(problem);
        foreach (string warning in result.Warnings)
            stdout.WriteLine($"warning: {warning}");
        if (result.Fidelity != null)
            WriteFidelity(result.Fidelity, stdout);
        bool ok = result.Success;
        stdout.WriteLine(ok ? $"map converted: {result.Outputs.Count} files in {output}" : $"map conversion FAILED ({result.Problems.Count} problems)");
        if (!options.Flag("--keep-work") && ok)
        {
            foreach (string file in Directory.EnumerateFiles(work, "*.layout.*"))
                File.Delete(file);
        }
        return ok ? 0 : 1;
    }

    private static void WriteFidelity(FFPorter.Core.Common.Fidelity.FidelitySnapshot report, TextWriter stdout) => CliFidelity.Write(report, stdout);

    private static int Movie(Options options, TextWriter stdout)
    {
        string input = Path.GetFullPath(options.Positional(0, "video"));
        stdout.WriteLine($"{Path.GetFileName(input)}: {FFPorter.Core.T7.Formats.T7Movie.Describe(input)?.ToString() ?? "not a Matroska video"}");
        IReadOnlyList<string> problems = FFPorter.Core.T7.Formats.T7Movie.Ps4Problems(input);
        foreach (string problem in problems)
            stdout.WriteLine($"  {problem}");
        if (problems.Count == 0)
            stdout.WriteLine("  PS4-ready");
        string? output = options.Value("-o");
        if (output == null)
            return problems.Count == 0 ? 0 : 1;
        output = Path.GetFullPath(output);
        var outcome = FFPorter.Core.T7.Formats.T7MovieTranscoder.Prepare(input, output, stdout.WriteLine);
        stdout.WriteLine($"{outcome switch { FFPorter.Core.T7.Formats.T7MovieTranscoder.Outcome.Copied => "copied", FFPorter.Core.T7.Formats.T7MovieTranscoder.Outcome.UpToDate => "up to date", _ => "re-encoded" }}: {output}");
        return 0;
    }

    private static int ShaderPssl(Options options, TextWriter stdout)
    {
        byte[] dxbc = File.ReadAllBytes(options.Positional(0, "program.dxbc"));
        FFPorter.Core.T7.Shaders.T7DxbcCode.Program program = FFPorter.Core.T7.Shaders.T7DxbcCode.Parse(dxbc);
        string text;
        if (options.Flag("--disassemble"))
            text = FFPorter.Core.T7.Shaders.T7DxbcCode.Disassemble(program);
        else
            text = FFPorter.Core.T7.Shaders.T7DxbcTranslator.Translate(dxbc, program.Stage == 1 ? "vs" : "ps", null, null,
                targets: options.Value("--technique") is string technique
                    ? FFPorter.Core.T7.Shaders.T7PassTargets.For(options.Value("--set"), int.Parse(technique)) : null).Text;
        string? output = options.Value("-o");
        if (output != null)
            File.WriteAllText(output, text);
        else
            stdout.Write(text);
        return 0;
    }

    private static int ShaderCheck(Options options, TextWriter stdout)
    {
        string folder = options.Positional(0, "folder");
        Workspace workspace = Workspace.Locate(options.Value("--root"));
        string cache = options.Value("--cache") ?? Path.Combine(T7Paths.Work(workspace), "shader_cache");
        var messages = new System.Collections.Concurrent.ConcurrentBag<string>();
        FFPorter.Core.T7.Shaders.T7ShaderCompiler compiler = FFPorter.Core.T7.Shaders.T7ShaderCompiler.TryCreate(workspace, cache, messages.Add, out string? why)
            ?? throw new IOException($"the shader compiler is unavailable: {why}");
        string[] programs = Directory.EnumerateFiles(folder, "source.dxbc", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        int passed = 0;
        Parallel.ForEach(programs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, path =>
        {
            byte[] dxbc = File.ReadAllBytes(path);
            string stage;
            try
            {
                stage = FFPorter.Core.T7.Shaders.T7DxbcCode.Parse(dxbc).Stage == 1 ? "vs" : "ps";
                compiler.Compile(stage, dxbc, null, null);
                Interlocked.Increment(ref passed);
            }
            catch (Exception error) when (error is InvalidDataException or IOException or NotSupportedException)
            {
                failures.Add($"{Path.GetFileName(Path.GetDirectoryName(path))}: {error.Message}");
            }
        });
        foreach (string message in messages.Order(StringComparer.Ordinal))
            stdout.WriteLine(message);
        foreach (string failure in failures.Order(StringComparer.Ordinal))
            stdout.WriteLine("FAILED " + failure);
        stdout.WriteLine($"{passed}/{programs.Length} programs compiled ({compiler.Compiled} compiled now, {compiler.CacheHits} cached, {compiler.Decompiled} from decompiled HLSL)");
        return failures.IsEmpty ? 0 : 1;
    }

    private static int SoundConvert(Options options, TextWriter stdout)
    {
        string input = Path.GetFullPath(options.Positional(0, "pc bank"));
        string output = Path.GetFullPath(options.Required("-o"));
        Workspace workspace = Workspace.Locate(options.Value("--root"));
        var pc = FFPorter.Core.T7.Sound.T7SoundBank.Parse(File.ReadAllBytes(input));
        using var lame = FFPorter.Core.T7.Sound.Lame.Load(workspace);
        stdout.WriteLine($"LAME: {lame.LibraryPath}");
        var result = FFPorter.Core.T7.Sound.T7SoundConvert.Convert(pc, workspace, lame, stdout.WriteLine);
        byte[] bytes = result.Bank.Build();
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllBytes(output, bytes);
        stdout.WriteLine($"wrote {output}: {result.Bank.Entries.Count} entries, {bytes.Length} bytes (PC {new FileInfo(input).Length})");
        string? compare = options.Value("--compare");
        if (compare != null)
        {
            var stock = FFPorter.Core.T7.Sound.T7SoundBank.Parse(File.ReadAllBytes(compare));
            int names = 0, ids = 0, frames = 0, sameFrameCount = 0;
            for (int i = 0; i < Math.Min(stock.Entries.Count, result.Bank.Entries.Count); i++)
            {
                var a = result.Bank.Entries[i];
                var b = stock.Entries[i];
                names += a.Name == b.Name ? 1 : 0;
                ids += a.Id == b.Id ? 1 : 0;
                frames += a.FrameCount == b.FrameCount ? 1 : 0;
                sameFrameCount += FFPorter.Core.T7.Sound.T7SoundConvert.Mp3Frames(a.Data).Count == FFPorter.Core.T7.Sound.T7SoundConvert.Mp3Frames(b.Data).Count ? 1 : 0;
            }
            stdout.WriteLine($"vs stock PS4 ({stock.Entries.Count} entries, {new FileInfo(compare).Length} bytes): names {names}, ids {ids}, frameCount {frames}, MP3 frame counts {sameFrameCount}");
        }
        return 0;
    }

    private static int XPakConvert(Options options, TextWriter stdout)
    {
        string input = Path.GetFullPath(options.Positional(0, "pc.xpak"));
        string output = Path.GetFullPath(options.Required("-o"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        FFPorter.Core.T7.Streams.T7Ps4StreamIndex? ps4Index = null;
        string? indexOption = options.Value("--ps4-index");
        if (indexOption != null)
        {
            IEnumerable<string> files = Directory.Exists(indexOption) ? Directory.EnumerateFiles(indexOption, "*.xpak", SearchOption.AllDirectories) : [indexOption];
            ps4Index = FFPorter.Core.T7.Streams.T7Ps4StreamIndex.Load(files, stdout.WriteLine);
            stdout.WriteLine($"PS4 index: {ps4Index.Count} records");
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = FFPorter.Core.T7.Streams.T7StreamConvert.Run(new FFPorter.Core.T7.Streams.T7StreamConvertOptions
        {
            PcXPak = input,
            OutputXPak = output,
            PcIndexXPak = options.Value("--index"),
            OutputIndexXPak = options.Value("--index-out"),
            Ps4Index = ps4Index,
            Log = stdout.WriteLine,
        });
        stdout.WriteLine($"wrote {output} ({new FileInfo(output).Length} bytes) in {watch.Elapsed.TotalSeconds:F1}s");
        foreach ((string what, int count) in result.Counts.OrderBy(p => p.Key))
            stdout.WriteLine($"  {what} x{count}");
        foreach (string warning in result.Warnings.GroupBy(w => w.Split(' ')[0] + " " + (w.Contains("another xpak") ? "external" : w.Contains("unchanged") ? "unchanged" : "other")).Select(g => $"{g.Key}: {g.Count()} warnings, e.g. {g.First()}"))
            stdout.WriteLine($"  warning {warning}");
        result.Map.Save(Path.ChangeExtension(output, ".keys.json"));

        string? compare = options.Value("--compare");
        if (compare != null)
            CompareXPaks(output, compare, stdout);
        return 0;
    }

    private static int XPakCompare(Options options, TextWriter stdout)
    {
        CompareXPaks(options.Positional(0, "converted.xpak"), options.Positional(1, "retail ps4.xpak"), stdout);
        return 0;
    }

    private static void CompareXPaks(string convertedPath, string retailPath, TextWriter stdout)
    {
        using var retail = new FFPorter.Core.T7.Streams.XPak(retailPath);
        using var converted = new FFPorter.Core.T7.Streams.XPak(convertedPath);
        var retailById = new Dictionary<(string, string, string, int), ulong>();
        foreach ((ulong key, var record) in retail.Index)
            retailById.TryAdd(FFPorter.Core.T7.Streams.T7Ps4StreamIndex.Identity(record), key);
        var stats = new Dictionary<string, int>();
        void Count(string what) => stats[what] = stats.GetValueOrDefault(what) + 1;
        foreach ((ulong key, var record) in converted.Index)
        {
            string type = record.Type ?? "?";
            if (!retailById.TryGetValue(FFPorter.Core.T7.Streams.T7Ps4StreamIndex.Identity(record), out ulong retailKey))
            {
                Count($"{type}: not in retail");
                continue;
            }
            bool stored = converted.TryFind(key, out _);
            bool retailStored = retail.TryFind(retailKey, out _);
            string where = stored ? (retailStored ? "stored" : "stored here, external in retail") : (retailStored ? "external here, stored in retail" : "external");
            Count($"{type} {where}: key {(key == retailKey ? "matches" : "differs")}");
            if (key == retailKey && record.Text != retail.Index[retailKey].Text)
                Count($"{type} {where}: index text differs");
        }
        foreach ((string what, int count) in stats.OrderBy(p => p.Key))
            stdout.WriteLine($"  vs retail: {what} x{count}");
    }

    private static int ZoneCompare(Options options, TextWriter stdout, TextWriter stderr)
    {
        string ours = Path.GetFullPath(options.Positional(0, "converted ps4.ff"));
        string retail = Path.GetFullPath(options.Positional(1, "retail ps4.ff"));
        Workspace workspace = Workspace.Locate(options.Value("--root"));
        string loader = FFPorter.Core.T7.T7Paths.Ps4Loader(workspace);
        string work = Path.Combine(FFPorter.Core.T7.T7Paths.Work(workspace), "compare");
        Directory.CreateDirectory(work);
        int shown = int.Parse(options.Value("--limit") ?? "12");
        int? only = options.Value("--type") is string text ? int.Parse(text) : null;
        string? named = options.Value("--name");

        FFPorter.Core.T7.Link.T7WalkIndex Walk(string zone, string label) => CompareWalk(workspace, zone, label, stderr);

        var mine = Walk(ours, "ours");
        var theirs = Walk(retail, "retail");
        var byName = new Dictionary<(int Type, string Name), FFPorter.Core.T7.Harness.T7Walk.Asset>();
        foreach (var asset in theirs.Walk.Assets)
            foreach (var registration in asset.Registrations)
                byName.TryAdd((registration.Type, registration.Name), asset);

        var same = new SortedDictionary<int, int>();
        var differ = new SortedDictionary<int, int>();
        var offsets = new Dictionary<int, int>();
        bool ranges = options.Flag("--ranges");
        var missing = new SortedDictionary<int, int>();
        var examples = new List<string>();
        foreach (var asset in mine.Walk.Assets)
        {
            foreach (var registration in asset.Registrations)
            {
                int type = registration.Type;
                if (only != null && type != only)
                    continue;
                if (named != null && !registration.Name.Contains(named, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!byName.TryGetValue((type, registration.Name), out var reference))
                {
                    missing[type] = missing.GetValueOrDefault(type) + 1;
                    continue;
                }
                ReadOnlySpan<byte> a = mine.Zone.AsSpan((int)asset.Start, (int)(asset.End - asset.Start));
                ReadOnlySpan<byte> b = theirs.Zone.AsSpan((int)reference.Start, (int)(reference.End - reference.Start));
                var skip = new HashSet<long>();
                foreach ((long field, _, _) in mine.PointersIn(asset.Start, a.Length))
                    for (long i = 0; i < 8; i++)
                        skip.Add(field - asset.Start + i);
                foreach ((long field, _, _) in theirs.PointersIn(reference.Start, b.Length))
                    for (long i = 0; i < 8; i++)
                        skip.Add(field - reference.Start + i);
                int at = -1;
                for (long i = 0; i < Math.Min(a.Length, b.Length); i++)
                {
                    if (a[(int)i] != b[(int)i] && !skip.Contains(i))
                    {
                        at = (int)i;
                        break;
                    }
                }
                if (at < 0 && a.Length == b.Length)
                {
                    same[type] = same.GetValueOrDefault(type) + 1;
                    continue;
                }
                differ[type] = differ.GetValueOrDefault(type) + 1;
                var runs = new List<(int From, int To)>();
                if (a.Length == b.Length && (only != null || ranges))
                {
                    for (int i = 0; i < a.Length; i++)
                    {
                        if (a[i] == b[i] || skip.Contains(i))
                            continue;
                        offsets[i] = offsets.GetValueOrDefault(i) + 1;
                        if (runs.Count > 0 && runs[^1].To >= i - 4)
                            runs[^1] = (runs[^1].From, i);
                        else
                            runs.Add((i, i));
                    }
                }
                if (options.Flag("--reads") && examples.Count < shown)
                {
                    var lines = new System.Text.StringBuilder();
                    lines.Append($"  {FFPorter.Core.T7.T7AssetTypes.Name(type),-16} {registration.Name} ({asset.Reads.Count} vs {reference.Reads.Count} reads)");
                    int reported = 0;
                    for (int r = 0; r < Math.Min(asset.Reads.Count, reference.Reads.Count) && reported < 60; r++)
                    {
                        var x = asset.Reads[r];
                        var y = reference.Reads[r];
                        if (options.Value("--dump") is string dumpFolder)
                        {
                            Directory.CreateDirectory(dumpFolder);
                            File.WriteAllBytes(Path.Combine(dumpFolder, $"read{r}.ours.bin"), mine.Zone.AsSpan((int)x.FileOffset, (int)x.Size).ToArray());
                            File.WriteAllBytes(Path.Combine(dumpFolder, $"read{r}.retail.bin"), theirs.Zone.AsSpan((int)y.FileOffset, (int)y.Size).ToArray());
                        }
                        if (x.Kind != y.Kind || x.Size != y.Size)
                        {
                            lines.Append($"{Environment.NewLine}    read {r}: {x.Kind} {x.Size} vs {y.Kind} {y.Size}");
                            reported++;
                            continue;
                        }
                        ReadOnlySpan<byte> u = mine.Zone.AsSpan((int)x.FileOffset, (int)x.Size);
                        ReadOnlySpan<byte> v = theirs.Zone.AsSpan((int)y.FileOffset, (int)y.Size);
                        if (u.SequenceEqual(v))
                            continue;
                        var mask = new HashSet<long>();
                        foreach ((long field, _, _) in mine.PointersIn(x.FileOffset, x.Size))
                            for (long i = 0; i < 8; i++)
                                mask.Add(field - x.FileOffset + i);
                        foreach ((long field, _, _) in theirs.PointersIn(y.FileOffset, y.Size))
                            for (long i = 0; i < 8; i++)
                                mask.Add(field - y.FileOffset + i);
                        int count = 0, first = -1;
                        for (int i = 0; i < u.Length; i++)
                        {
                            if (u[i] != v[i] && !mask.Contains(i))
                            {
                                count++;
                                if (first < 0)
                                    first = i;
                            }
                        }
                        if (count == 0)
                            continue;
                        int lo = Math.Max(0, first - 4), hi = Math.Min(u.Length, first + 20);
                        lines.Append($"{Environment.NewLine}    read {r} ({x.Kind} {x.Size} bytes at {x.FileOffset - asset.Start}): {count} bytes differ, first at {first}"
                            + $"{Environment.NewLine}        ours   {System.Convert.ToHexString(u[lo..hi])}{Environment.NewLine}        retail {System.Convert.ToHexString(v[lo..hi])}");
                        reported++;
                    }
                    examples.Add(lines.ToString());
                    continue;
                }
                if (ranges && examples.Count < shown)
                {
                    var lines = new System.Text.StringBuilder();
                    lines.Append($"  {FFPorter.Core.T7.T7AssetTypes.Name(type),-16} {registration.Name} ({a.Length} vs {b.Length} bytes, {runs.Count} differing ranges)");
                    foreach ((int from, int to) in runs.Take(40))
                    {
                        int lo = Math.Max(0, from - 2), hi = Math.Min(a.Length, to + 3);
                        lines.Append($"{Environment.NewLine}      {from,6}  ours {System.Convert.ToHexString(a[lo..hi])}  retail {System.Convert.ToHexString(b[lo..hi])}");
                    }
                    examples.Add(lines.ToString());
                    continue;
                }
                if (examples.Count < shown)
                {
                    if (at < 0)
                        at = Math.Min(a.Length, b.Length);
                    string Hex(ReadOnlySpan<byte> data) => System.Convert.ToHexString(data[Math.Max(0, Math.Min(at - 4, data.Length))..Math.Min(data.Length, at + 12)]);
                    examples.Add($"  {FFPorter.Core.T7.T7AssetTypes.Name(type),-16} {registration.Name} ({a.Length} vs {b.Length} bytes, first difference at {at})"
                        + $"{Environment.NewLine}      ours   {Hex(a)}{Environment.NewLine}      retail {Hex(b)}");
                }
            }
        }

        stdout.WriteLine($"{Path.GetFileName(ours)} vs {Path.GetFileName(retail)}");
        foreach (int type in same.Keys.Concat(differ.Keys).Concat(missing.Keys).Distinct().Order())
        {
            stdout.WriteLine($"  {FFPorter.Core.T7.T7AssetTypes.Name(type),-16} same {same.GetValueOrDefault(type),6}   differ {differ.GetValueOrDefault(type),6}   not in retail {missing.GetValueOrDefault(type),6}");
        }
        if (offsets.Count > 0)
        {
            stdout.WriteLine("offsets where same-sized assets differ (offset: assets):");
            foreach (var pair in offsets.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).Take(60))
                stdout.WriteLine($"  {pair.Key,6}: {pair.Value}");
        }
        if (examples.Count > 0)
        {
            stdout.WriteLine("differences:");
            foreach (string example in examples)
                stdout.WriteLine(example);
        }
        return differ.Values.Sum() == 0 ? 0 : 1;
    }

    private static int TechsetArgs(Options options, TextWriter stdout, TextWriter stderr)
    {
        string ours = Path.GetFullPath(options.Positional(0, "converted ps4.ff"));
        string retail = Path.GetFullPath(options.Positional(1, "retail ps4.ff"));
        Workspace workspace = Workspace.Locate(options.Value("--root"));
        string? named = options.Value("--name");
        bool all = options.Flag("--all");
        var mine = CompareWalk(workspace, ours, "ours", stderr);
        var theirs = CompareWalk(workspace, retail, "retail", stderr);

        static Dictionary<string, long> Sets(FFPorter.Core.T7.Link.T7WalkIndex index)
        {
            var result = new Dictionary<string, long>();
            foreach (var asset in index.Walk.Assets)
            {
                foreach (var registration in asset.Registrations)
                {
                    if (registration.Type != FFPorter.Core.T7.T7AssetTypes.TechniqueSet)
                        continue;
                    foreach (var read in asset.Reads)
                    {
                        if (read.Kind == FFPorter.Core.T7.Harness.T7WalkKind.Read && read.Size == 112 && read.At == registration.Header && read.FileOffset < registration.FilePos)
                        {
                            int hash = registration.Name.IndexOf('#');
                            result.TryAdd(hash < 0 ? registration.Name : registration.Name[..hash], read.FileOffset);
                        }
                    }
                }
            }
            return result;
        }

        static string Text(FFPorter.Core.T7.Link.T7WalkIndex index, long field)
        {
            if (!index.TryFollow(field, out long at))
                return "-";
            int end = Array.IndexOf(index.Zone, (byte)0, (int)at);
            return System.Text.Encoding.Latin1.GetString(index.Zone, (int)at, Math.Max(0, end - (int)at));
        }

        static List<string> Pass(FFPorter.Core.T7.Link.T7WalkIndex index, long set, int technique, int pass)
        {
            var lines = new List<string>();
            byte[] zone = index.Zone;
            long field = set + 16 + 8 * technique;
            if (BitConverter.ToUInt64(zone, (int)field) == 0 || !index.TryFollow(field, out long at))
                return lines;
            long p = at + 8 + 104 * pass;
            string Shader(long pointer) => index.TryFollow(pointer, out long shader) ? Text(index, shader) : "-";
            string decl = "-";
            if (index.TryFollow(p + 8, out long declAt))
                decl = string.Join(",", zone.AsSpan((int)declAt + 1, zone[declAt]).ToArray());
            lines.Add($"vs {Shader(p + 24)}  ps {Shader(p + 32)}  decl {decl}");
            int count = zone[p + 88];
            if (count > 0 && index.TryFollow(p + 96, out long args))
            {
                for (int i = 0; i < count; i++)
                {
                    long e = args + 16 * i;
                    ushort type = BitConverter.ToUInt16(zone, (int)e);
                    ushort slot = BitConverter.ToUInt16(zone, (int)e + 2);
                    ushort size = BitConverter.ToUInt16(zone, (int)e + 4);
                    ushort buffer = BitConverter.ToUInt16(zone, (int)e + 6);
                    uint value = BitConverter.ToUInt32(zone, (int)e + 8);
                    string kind = (type >> 8) switch { 0 => "literal", 1 => "mtlconst", 2 => "mtltex", 3 => "mtlsamp", 4 => "codeconst", 5 => "codetex", 6 => "codesamp", _ => $"kind{type >> 8}" };
                    string stage = (type & 0xFF) switch { 0 => "vs", 1 => "ps", 2 => "gs", _ => $"s{type & 0xFF}" };
                    lines.Add($"  {stage} {kind,-9} slot {slot,3} size {size,3} buf {buffer,3} value {(type >> 8 == 0 ? "(literal)" : value.ToString("x8"))}");
                }
            }
            return lines;
        }

        var mineSets = Sets(mine);
        var theirSets = Sets(theirs);
        if (options.Flag("--list"))
        {
            foreach (string name in mineSets.Keys.Order(StringComparer.Ordinal))
                stdout.WriteLine($"ours   {name}");
            foreach (string name in theirSets.Keys.Order(StringComparer.Ordinal))
                stdout.WriteLine($"retail {name}");
        }
        int compared = 0, differing = 0;
        foreach ((string name, long set) in mineSets.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (named != null && !name.Contains(named, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!theirSets.TryGetValue(name, out long reference))
                continue;
            for (int t = 0; t < 12; t++)
            {
                for (int p = 0; p < 2; p++)
                {
                    List<string> a = Pass(mine, set, t, p), b = Pass(theirs, reference, t, p);
                    if (a.Count == 0 && b.Count == 0)
                        continue;
                    compared++;
                    bool same = a.Skip(1).SequenceEqual(b.Skip(1)) && (a.Count == 0 || b.Count == 0 || a[0][a[0].IndexOf("decl", StringComparison.Ordinal)..] == b[0][b[0].IndexOf("decl", StringComparison.Ordinal)..]);
                    if (!same)
                        differing++;
                    if (same && !all)
                        continue;
                    stdout.WriteLine($"{name} technique {t} pass {p}{(same ? "" : "  DIFFERS")}");
                    stdout.WriteLine($"  ours   {(a.Count > 0 ? a[0] : "(none)")}");
                    stdout.WriteLine($"  retail {(b.Count > 0 ? b[0] : "(none)")}");
                    int rows = Math.Max(a.Count, b.Count);
                    for (int i = 1; i < rows; i++)
                    {
                        string left = i < a.Count ? a[i] : "";
                        string right = i < b.Count ? b[i] : "";
                        stdout.WriteLine($"  {(left == right ? " " : "*")} {left,-62} | {right}");
                    }
                }
            }
        }
        stdout.WriteLine($"{compared} passes compared, {differing} with different arguments ({mineSets.Count} sets ours, {theirSets.Count} retail)");
        return differing == 0 ? 0 : 1;
    }

    private static int MaterialBuffers(Options options, TextWriter stdout, TextWriter stderr)
    {
        string ours = Path.GetFullPath(options.Positional(0, "converted ps4.ff"));
        string retail = Path.GetFullPath(options.Positional(1, "retail ps4.ff"));
        Workspace workspace = Workspace.Locate(options.Value("--root"));
        string? named = options.Value("--name");
        bool all = options.Flag("--all");
        var mine = CompareWalk(workspace, ours, "ours", stderr);
        var theirs = CompareWalk(workspace, retail, "retail", stderr);

        static Dictionary<string, long> Roots(FFPorter.Core.T7.Link.T7WalkIndex index)
        {
            var result = new Dictionary<string, long>();
            foreach (var asset in index.Walk.Assets)
            {
                foreach (var registration in asset.Registrations)
                {
                    if (registration.Type != FFPorter.Core.T7.T7AssetTypes.Material)
                        continue;
                    foreach (var read in asset.Reads)
                    {
                        if (read.Kind == FFPorter.Core.T7.Harness.T7WalkKind.Read && read.Size == 664 && read.At == registration.Header && read.FileOffset < registration.FilePos)
                        {
                            result.TryAdd(registration.Name, read.FileOffset);
                            break;
                        }
                    }
                }
            }
            return result;
        }

        static (int Size, byte[] Data)? Slot(FFPorter.Core.T7.Link.T7WalkIndex index, long root, int i, int j, int k)
        {
            long field = root + 0x28 + i * 48 + j * 16 + k * 8;
            if (BitConverter.ToUInt64(index.Zone, (int)field) == 0 || !index.TryFollow(field, out long buffer))
                return null;
            int size = (int)BitConverter.ToUInt32(index.Zone, (int)buffer + 16);
            if (size == 0 || !index.TryFollow(buffer + 8, out long data))
                return (size, []);
            return (size, index.Zone.AsSpan((int)data, size).ToArray());
        }

        static string Techset(FFPorter.Core.T7.Link.T7WalkIndex index, long root) =>
            index.TryReferencedName(root + 0x270, out _, out string name) ? name : "?";

        static string Floats(byte[] data)
        {
            var text = new System.Text.StringBuilder();
            for (int o = 0; o + 4 <= data.Length; o += 4)
            {
                if (o > 0)
                    text.Append(o % 16 == 0 ? " | " : " ");
                text.Append(BitConverter.ToSingle(data, o).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        var mineRoots = Roots(mine);
        var theirRoots = Roots(theirs);
        int materials = 0, differing = 0;
        foreach ((string name, long root) in mineRoots.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (named != null && !name.Contains(named, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!theirRoots.TryGetValue(name, out long reference))
                continue;
            materials++;
            var lines = new List<string>();
            bool differs = false;
            for (int i = 0; i < 12; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        var a = Slot(mine, root, i, j, k);
                        var b = Slot(theirs, reference, i, j, k);
                        if (a == null && b == null)
                            continue;
                        bool same = a != null && b != null && a.Value.Size == b.Value.Size && a.Value.Data.AsSpan().SequenceEqual(b.Value.Data);
                        differs |= !same;
                        if (same && !all)
                            continue;
                        lines.Add($"    t{i} {(j == 1 ? "vs" : j == 2 ? "ps" : "s" + j)} p{k}{(same ? "" : "  DIFFERS")}");
                        lines.Add($"      ours   {(a == null ? "(none)" : $"{a.Value.Size,3}: {Floats(a.Value.Data)}")}");
                        lines.Add($"      retail {(b == null ? "(none)" : $"{b.Value.Size,3}: {Floats(b.Value.Data)}")}");
                    }
                }
            }
            if (differs)
                differing++;
            if (options.Flag("--textures"))
            {
                foreach ((string label, FFPorter.Core.T7.Link.T7WalkIndex index, long at) in new[] { ("ours", mine, root), ("retail", theirs, reference) })
                {
                    int count = index.Zone[at + 0x268];
                    if (count == 0 || !index.TryFollow(at + 0x278, out long table))
                        continue;
                    for (int e = 0; e < count; e++)
                    {
                        long entry = table + 32 * e;
                        string image = "?";
                        if (index.Pointers.TryGetValue(entry, out var pointer))
                        {
                            image = $"{pointer.Kind} {pointer.Target}";
                            if (pointer.Kind == FFPorter.Core.T7.Harness.T7PointerKind.PackedAlias && index.TryAliasHeader(pointer.Target, out var header))
                                image += $" -> header {header}";
                        }
                        if (index.TryReferencedName(entry, out _, out string imageName))
                            image += $"  [{imageName}]";
                        lines.Add($"    {label,-6} texture {e}: {System.Convert.ToHexString(index.Zone.AsSpan((int)entry + 8, 24))}  {image}");
                    }
                }
            }
            if (lines.Count == 0)
                continue;
            stdout.WriteLine($"{name}  techset ours {Techset(mine, root)}  retail {Techset(theirs, reference)}");
            foreach (string line in lines)
                stdout.WriteLine(line);
        }
        stdout.WriteLine($"{materials} materials in both, {differing} with different constant buffers");
        return differing == 0 ? 0 : 1;
    }

    private static int WorldLayers(Options options, TextWriter stdout)
    {
        string zone = Path.GetFullPath(options.Positional(0, "zone.ff"));
        T7FastFile.Decoded decoded = options.Flag("--apply-fd") ? FFPorter.Core.T7.T7FastFileDelta.LoadPatched(zone) : T7FastFile.Load(zone);
        var index = new FFPorter.Core.T7.Link.T7WalkIndex("layers", decoded.Zone, T7Walk.Read(options.Required("--walk")));
        foreach (var asset in index.Walk.Assets)
        {
            if (asset.Type != T7AssetTypes.GfxWorld || asset.Reads.Count == 0)
                continue;
            long root = asset.Reads[0].FileOffset;
            uint vertices = BitConverter.ToUInt32(index.Zone, (int)root + 620);
            uint layers = BitConverter.ToUInt32(index.Zone, (int)root + 640);
            foreach (var read in asset.Reads)
            {
                if (read.Kind != T7WalkKind.Read || read.Size != layers)
                    continue;
                File.WriteAllBytes(options.Required("-o"), index.Zone.AsSpan((int)read.FileOffset, (int)read.Size).ToArray());
                stdout.WriteLine($"{asset.Name}: {layers} layer bytes ({vertices} vertex bytes) at {read.FileOffset}");
                return 0;
            }
            stdout.WriteLine($"{asset.Name}: no read of {layers} bytes");
            return 1;
        }
        stdout.WriteLine("no world in this zone");
        return 1;
    }

    private static int ShaderDump(Options options, TextWriter stdout, TextWriter stderr)
    {
        string zone = Path.GetFullPath(options.Positional(0, "ps4.ff"));
        Workspace workspace = Workspace.Locate(options.Value("--root"));
        string wanted = options.Required("--techset");
        string folder = Path.GetFullPath(options.Required("-o"));
        var index = CompareWalk(workspace, zone, "shaders", stderr);
        byte[] data = index.Zone;
        int written = 0;
        bool Resolve(long field, out long target)
        {
            if (index.Pointers.TryGetValue(field, out var pointer) && pointer.Kind == FFPorter.Core.T7.Harness.T7PointerKind.PackedAlias
                && index.TryAliasHeader(pointer.Target, out var header))
                return index.TryFileOffset(header, out target, field);
            return index.TryFollow(field, out target);
        }
        foreach (var asset in index.Walk.Assets)
        {
            foreach (var registration in asset.Registrations)
            {
                if (registration.Type != T7AssetTypes.TechniqueSet)
                    continue;
                int hash = registration.Name.IndexOf('#');
                string bare = hash < 0 ? registration.Name : registration.Name[..hash];
                if (bare != wanted && wanted != "*")
                    continue;
                if (wanted == "*")
                    folder = Path.Combine(Path.GetFullPath(options.Required("-o")), bare.Replace('/', '_').Replace(',', '_'));
                foreach (var read in asset.Reads)
                {
                    if (read.Kind != T7WalkKind.Read || read.Size != 112 || read.At != registration.Header || read.FileOffset >= registration.FilePos)
                        continue;
                    for (int t = 0; t < 12; t++)
                    {
                        long field = read.FileOffset + 16 + 8 * t;
                        if (BitConverter.ToUInt64(data, (int)field) == 0 || !Resolve(field, out long technique))
                            continue;
                        for (int p = 0; p < 2; p++)
                        {
                            foreach ((int offset, string stage) in new[] { (24, "vs"), (32, "ps") })
                            {
                                if (!Resolve(technique + 8 + 104 * p + offset, out long shader))
                                    continue;
                                if (!Resolve(shader + 32, out long code) || !Resolve(shader + 48, out long header))
                                    continue;
                                long codeSize = (long)BitConverter.ToUInt64(data, (int)shader + 40);
                                long headerSize = (long)BitConverter.ToUInt64(data, (int)shader + 56);
                                if (codeSize <= 0 || headerSize <= 0 || code + codeSize > data.Length || header + headerSize > data.Length)
                                {
                                    if (options.Flag("--debug"))
                                    {
                                        long passField = technique + 8 + 104 * p + offset;
                                        string Describe(long f) => index.Pointers.TryGetValue(f, out var pt) ? $"{pt.Kind} {pt.Target}" : $"raw {BitConverter.ToUInt64(data, (int)f):x16}";
                                        stdout.WriteLine($"    technique field {Describe(field)} -> {technique}; pass shader field {Describe(passField)} -> {shader}; code field {Describe(shader + 32)} header field {Describe(shader + 48)}");
                                        stdout.WriteLine($"    shader bytes {System.Convert.ToHexString(data.AsSpan((int)shader - 8, 80))}");
                                        foreach (var rd in index.Walk.Assets[index.AssetAt(shader)].Reads.Where(r => r.FileOffset <= shader + 64 && r.FileOffset + r.Size > shader - 16))
                                            stdout.WriteLine($"    read {rd.Kind} {rd.Size} at {rd.FileOffset} -> {rd.At}");
                                    }
                                    stdout.WriteLine($"  t{t} p{p} {stage}: program outside the zone ({codeSize} + {headerSize} bytes)");
                                    continue;
                                }
                                string stem = Path.Combine(folder, $"t{t}_p{p}_{stage}");
                                Directory.CreateDirectory(folder);
                                File.WriteAllBytes(stem + ".header.bin", data.AsSpan((int)header, (int)headerSize).ToArray());
                                File.WriteAllBytes(stem + ".code.bin", data.AsSpan((int)code, (int)codeSize).ToArray());
                                written++;
                            }
                        }
                    }
                    stdout.WriteLine($"{registration.Name}: {written} programs written to {folder}");
                    if (wanted != "*")
                        return written > 0 ? 0 : 1;
                    break;
                }
            }
        }
        if (wanted == "*")
            return 0;
        stdout.WriteLine($"no technique set '{wanted}' with programs in {Path.GetFileName(zone)}");
        return 1;
    }

    private static int MaterialTextures(Options options, TextWriter stdout)
    {
        string zone = Path.GetFullPath(options.Positional(0, "zone.ff"));
        bool pc = options.Flag("--pc");
        T7FastFile.Decoded decoded = options.Flag("--apply-fd") ? FFPorter.Core.T7.T7FastFileDelta.LoadPatched(zone) : T7FastFile.Load(zone);
        var index = new FFPorter.Core.T7.Link.T7WalkIndex("textures", decoded.Zone, T7Walk.Read(options.Required("--walk")));
        string wanted = options.Required("--name");
        int rootSize = pc ? 672 : 664, countAt = pc ? 0x270 : 0x268, tableAt = pc ? 0x280 : 0x278;
        foreach (var asset in index.Walk.Assets)
        {
            foreach (var registration in asset.Registrations)
            {
                if (registration.Type != T7AssetTypes.Material || !registration.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                    continue;
                long root = -1;
                foreach (var read in asset.Reads)
                    if (read.Kind == T7WalkKind.Read && read.Size == rootSize && read.At == registration.Header && read.FileOffset < registration.FilePos)
                        root = read.FileOffset;
                if (root < 0)
                    continue;
                string techset = index.TryReferencedName(root + (pc ? 0x278 : 0x270), out _, out string techsetName) ? techsetName : "?";
                stdout.WriteLine($"{registration.Name} (asset {asset.Index}, root at {root}) techset {techset}");
                if (options.Flag("--brief"))
                    continue;
                int count = index.Zone[root + countAt];
                long tableField = root + tableAt;
                stdout.WriteLine($"  table pointer {BitConverter.ToUInt64(index.Zone, (int)tableField):x16}, {count} entries");
                if (index.TryFollow(tableField, out long table))
                {
                    for (int e = 0; e < count; e++)
                    {
                        long entry = table + 32 * e;
                        string text = $"  entry {e} at {entry}: value {BitConverter.ToUInt64(index.Zone, (int)entry):x16} hash {BitConverter.ToUInt32(index.Zone, (int)entry + 8):x8} rest {System.Convert.ToHexString(index.Zone.AsSpan((int)entry + 12, 20))}";
                        if (index.Pointers.TryGetValue(entry, out var pointer))
                            text += $" -> {pointer.Kind} {pointer.Target}";
                        if (index.TryReferencedName(entry, out _, out string name))
                            text += $" [{name}]";
                        stdout.WriteLine(text);
                    }
                }
                foreach (var other in asset.Registrations)
                    stdout.WriteLine($"  registration {T7AssetTypes.Name(other.Type)} {other.Name} header {other.Header} filepos {other.FilePos}");
                foreach (var read in asset.Reads.Where(r => r.FileOffset >= root - 1 && r.FileOffset < registration.FilePos).Take(60))
                    stdout.WriteLine($"    read {read.Kind} {read.Size} at {read.FileOffset} -> {read.At}");
            }
        }
        return 0;
    }

    private static int MaterialGlobals(Options options, TextWriter stdout)
    {
        string zone = Path.GetFullPath(options.Positional(0, "pc zone.ff"));
        T7FastFile.Decoded decoded = options.Flag("--apply-fd") ? FFPorter.Core.T7.T7FastFileDelta.LoadPatched(zone) : T7FastFile.Load(zone);
        var index = new FFPorter.Core.T7.Link.T7WalkIndex("globals", decoded.Zone, T7Walk.Read(options.Required("--walk")));
        var library = FFPorter.Core.T7.Port.T7PcShaderLibrary.Create([], "", Path.Combine(T7Paths.Work(Workspace.Locate(options.Value("--root"))), "pc_walks"), _ => { });
        library.AddZone(index);
        string wanted = options.Required("--name");
        int technique = int.Parse(options.Value("--technique") ?? "3");
        foreach (var asset in index.Walk.Assets)
        {
            foreach (var registration in asset.Registrations)
            {
                if (registration.Type != T7AssetTypes.Material || !registration.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                    continue;
                long root = -1;
                foreach (var read in asset.Reads)
                    if (read.Kind == T7WalkKind.Read && read.Size == 672 && read.At == registration.Header && read.FileOffset < registration.FilePos)
                        root = read.FileOffset;
                if (root < 0 || !index.TryReferencedName(root + 0x278, out _, out string techset))
                    continue;
                long field = root + 0x30 + technique * 48 + 2 * 16;
                stdout.WriteLine($"{registration.Name}  techset {techset}");
                if (BitConverter.ToUInt64(index.Zone, (int)field) == 0 || !index.TryFollow(field, out long buffer))
                {
                    stdout.WriteLine("  no pixel buffer");
                    continue;
                }
                uint size = BitConverter.ToUInt32(index.Zone, (int)buffer + 48);
                long data = BitConverter.ToUInt64(index.Zone, (int)buffer + 40) == ulong.MaxValue ? buffer + 72
                    : index.TryFollow(buffer + 40, out long followed) ? followed : -1;
                var variables = library.Globals(techset, technique, 0, "ps");
                if (data < 0 || variables == null)
                {
                    stdout.WriteLine("  no data or reflection");
                    continue;
                }
                foreach (var variable in variables)
                {
                    var values = new List<string>();
                    for (int o = 0; o + 4 <= variable.Size && variable.Start + o + 4 <= size; o += 4)
                        values.Add(BitConverter.ToSingle(index.Zone, (int)(data + variable.Start + o)).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                    stdout.WriteLine($"  {(variable.Used ? "used" : "    ")} {variable.Name,-28} {string.Join(" ", values)}");
                }
            }
        }
        return 0;
    }

    private static int ImageParts(Options options, TextWriter stdout, TextWriter stderr)
    {
        string zone = Path.GetFullPath(options.Positional(0, "ps4.ff"));
        Workspace workspace = Workspace.Locate(options.Value("--root"));
        string? named = options.Value("--name");
        var index = CompareWalk(workspace, zone, "parts", stderr);
        var seen = new HashSet<long>();
        foreach (var asset in index.Walk.Assets)
        {
            foreach (var registration in asset.Registrations)
            {
                if (registration.Type != FFPorter.Core.T7.T7AssetTypes.Image)
                    continue;
                if (named != null && !registration.Name.Contains(named, StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var read in asset.Reads)
                {
                    if (read.Kind != FFPorter.Core.T7.Harness.T7WalkKind.Read || read.Size != 304 || read.At != registration.Header || read.FileOffset >= registration.FilePos || !seen.Add(read.FileOffset))
                        continue;
                    ReadOnlySpan<byte> image = index.Zone.AsSpan((int)read.FileOffset, 304);
                    var parts = new List<string>();
                    for (int part = 0; part < 4; part++)
                    {
                        uint levels = BitConverter.ToUInt32(image[(part * 40)..]);
                        ulong key = BitConverter.ToUInt64(image[(part * 40 + 8)..]);
                        if (levels != 0 || key != 0)
                            parts.Add($"{levels & 0xF}:{levels >> 4}:{key:x16}");
                    }
                    stdout.WriteLine($"{registration.Name,-60} {BitConverter.ToUInt16(image[238..])}x{BitConverter.ToUInt16(image[240..])} format {BitConverter.ToUInt32(image[280..]):x8} levels {image[248]} parts {string.Join(' ', parts)}");
                }
            }
        }
        return 0;
    }

    private static int MaterialFields(Options options, TextWriter stdout, TextWriter stderr)
    {
        string zone = Path.GetFullPath(options.Positional(0, "ps4.ff"));
        Workspace workspace = Workspace.Locate(options.Value("--root"));
        string? named = options.Value("--name");
        bool pc = options.Flag("--pc");
        int size = pc ? 672 : 664, techsetField = pc ? 0x278 : 0x270;
        var index = pc
            ? new FFPorter.Core.T7.Link.T7WalkIndex("fields", (options.Flag("--apply-fd") ? FFPorter.Core.T7.T7FastFileDelta.LoadPatched(zone) : T7FastFile.Load(zone)).Zone,
                T7Walk.Read(options.Required("--walk")))
            : CompareWalk(workspace, zone, "fields", stderr);
        var seen = new HashSet<long>();
        foreach (var asset in index.Walk.Assets)
        {
            foreach (var registration in asset.Registrations)
            {
                if (registration.Type != FFPorter.Core.T7.T7AssetTypes.Material)
                    continue;
                if (named != null && !registration.Name.Contains(named, StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var read in asset.Reads)
                {
                    if (read.Kind != FFPorter.Core.T7.Harness.T7WalkKind.Read || read.Size != size || read.At != registration.Header || read.FileOffset >= registration.FilePos || !seen.Add(read.FileOffset))
                        continue;
                    ReadOnlySpan<byte> material = index.Zone.AsSpan((int)read.FileOffset, size);
                    string techset = index.TryReferencedName(read.FileOffset + techsetField, out _, out string referenced) ? referenced : "?";
                    stdout.WriteLine($"{registration.Name,-64} {System.Convert.ToHexString(material[0x08..0x30])} {System.Convert.ToHexString(material[0x258..0x298])} {techset}");
                }
            }
        }
        return 0;
    }

    private static int TechsetFields(Options options, TextWriter stdout, TextWriter stderr)
    {
        string zone = Path.GetFullPath(options.Positional(0, "ps4.ff"));
        Workspace workspace = Workspace.Locate(options.Value("--root"));
        string? named = options.Value("--name");
        var index = CompareWalk(workspace, zone, "fields", stderr);
        var seen = new HashSet<long>();
        foreach (var asset in index.Walk.Assets)
        {
            foreach (var registration in asset.Registrations)
            {
                if (registration.Type != FFPorter.Core.T7.T7AssetTypes.TechniqueSet)
                    continue;
                if (named != null && !registration.Name.Contains(named, StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var read in asset.Reads)
                {
                    if (read.Kind != FFPorter.Core.T7.Harness.T7WalkKind.Read || read.Size != 112 || read.At != registration.Header || read.FileOffset >= registration.FilePos || !seen.Add(read.FileOffset))
                        continue;
                    stdout.WriteLine($"{registration.Name,-72} {System.Convert.ToHexString(index.Zone.AsSpan((int)read.FileOffset, 112))}");
                    for (int t = 0; t < 12; t++)
                    {
                        long field = read.FileOffset + 16 + 8 * t;
                        if (BitConverter.ToUInt64(index.Zone, (int)field) == 0 || !index.TryFollow(field, out long technique))
                            continue;
                        if (BitConverter.ToUInt64(index.Zone, (int)technique + 216) == 0 || !index.TryFollow(technique + 216, out long state))
                            continue;
                        stdout.WriteLine($"  t{t} state {System.Convert.ToHexString(index.Zone.AsSpan((int)state, 112))}");
                    }
                }
            }
        }
        return 0;
    }

    private static FFPorter.Core.T7.Link.T7WalkIndex CompareWalk(Workspace workspace, string zone, string label, TextWriter stderr)
    {
        string loader = FFPorter.Core.T7.T7Paths.Ps4Loader(workspace);
        string work = Path.Combine(FFPorter.Core.T7.T7Paths.Work(workspace), "compare");
        Directory.CreateDirectory(work);
        var info = new FileInfo(zone);
        string identity = System.Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}")))[..12];
        string walk = Path.Combine(work, Path.GetFileNameWithoutExtension(zone) + "." + label + "." + identity + ".ps4.t7walk");
        if (!File.Exists(walk))
        {
            NativeProcessResult result = NativeProcess.Run(FFPorter.Core.T7.Harness.T7Ps4Loader.TaskName,
                FFPorter.Core.T7.Harness.T7Ps4Loader.ChildArguments(loader, walk, zone, 0),
                new NativeProcessOptions { OnErrorLine = stderr.WriteLine });
            if (result.ExitCode != 0)
                throw new InvalidDataException($"the PS4 loader could not walk {Path.GetFileName(zone)} ({result.ExitCode})");
        }
        return FFPorter.Core.T7.Link.T7WalkIndex.Load(label, zone, walk);
    }

    private static int GameZones(Options options, TextWriter stdout)
    {
        string zone = Path.GetFullPath(options.Positional(0, "pc zone.ff"));
        string? folder = T7Paths.FindGameZones(zone, out string? source);
        stdout.WriteLine(folder != null ? $"{folder} ({source})" : "not found (not above the zone, not found with an earlier zone, no Steam install)");
        return folder != null ? 0 : 1;
    }

    private static int XPakExtract(Options options, TextWriter stdout)
    {
        string input = Path.GetFullPath(options.Positional(0, "file.xpak"));
        string folder = Path.GetFullPath(options.Required("-o"));
        string? filter = options.Value("--name");
        Directory.CreateDirectory(folder);
        using var pak = new FFPorter.Core.T7.Streams.XPak(input);
        int written = 0;
        foreach (var entry in pak.Entries.OrderBy(e => e.Offset))
        {
            pak.Index.TryGetValue(entry.Key, out var record);
            string name = record?.Name ?? entry.Key.ToString("x16");
            if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            byte[] payload = pak.ReadParts(entry).SelectMany(p => p.Bytes).ToArray();
            string safe = string.Join('_', name.Split(Path.GetInvalidFileNameChars()));
            File.WriteAllBytes(Path.Combine(folder, $"{safe}.{entry.Key:x16}.bin"), payload);
            stdout.WriteLine($"{entry.Key:x16} {payload.Length,10} {name}");
            written++;
        }
        stdout.WriteLine($"{written} item(s) written to {folder}");
        return 0;
    }

    private static int XPakIndex(Options options, TextWriter stdout)
    {
        string input = Path.GetFullPath(options.Positional(0, "file.xpak"));
        string? filter = options.Value("--name");
        using var pak = new FFPorter.Core.T7.Streams.XPak(input);
        int shown = 0;
        foreach (var record in pak.Index.Values.OrderBy(r => r.Name, StringComparer.Ordinal))
        {
            if (filter != null && (record.Name is null || !record.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                continue;
            stdout.WriteLine($"--- {record.Key:x16} ---");
            stdout.WriteLine(record.Text.TrimEnd());
            shown++;
        }
        stdout.WriteLine($"{shown} of {pak.Index.Count} index records");
        return 0;
    }

    private static int XPakVerify(Options options, TextWriter stdout)
    {
        string input = Path.GetFullPath(options.Positional(0, "file.xpak"));
        using var pak = new FFPorter.Core.T7.Streams.XPak(input);
        int badKeys = 0;
        var entries = new List<(ulong Key, byte[] Stored)>();
        foreach (var entry in pak.Entries.OrderBy(e => e.Offset))
        {
            var parts = pak.ReadParts(entry);
            byte[] payload = parts.SelectMany(p => p.Bytes).ToArray();
            if (FFPorter.Core.T7.Streams.XPak.ComputeKey(payload, (int)(entry.Key >> 61)) != entry.Key)
                badKeys++;
            if (pak.Index.TryGetValue(entry.Key, out var record) && record.Parts.Count > 1 && record.Parts.Sum(p => p.Size) == payload.Length)
            {
                parts = [];
                long at = 0;
                foreach ((long offset, long size) in record.Parts)
                {
                    parts.Add(((uint)offset, payload.AsSpan((int)at, (int)size).ToArray()));
                    at += size;
                }
            }
            entries.Add((entry.Key, FFPorter.Core.T7.Streams.XPakWriter.BuildEntry(parts)));
        }
        string rebuilt = Path.Combine(Path.GetTempPath(), Path.GetFileName(input) + ".rebuilt");
        FFPorter.Core.T7.Streams.XPakWriter.Write(rebuilt, entries, pak.Index.Values.Select(r => (r.Key, r.Text)));
        bool same;
        using (FileStream a = File.OpenRead(input), b = File.OpenRead(rebuilt))
        {
            same = a.Length == b.Length;
            var bufferA = new byte[1 << 20];
            var bufferB = new byte[1 << 20];
            while (same)
            {
                int readA = a.Read(bufferA), readB = b.Read(bufferB);
                if (readA != readB || !bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
                    same = false;
                if (readA == 0)
                    break;
            }
        }
        File.Delete(rebuilt);
        stdout.WriteLine($"{Path.GetFileName(input)}: {pak.Entries.Count} entries, {pak.Index.Count} index records, {badKeys} keys not reproduced, rebuild {(same ? "byte-identical" : "DIFFERENT")}");
        return same ? 0 : 1;
    }

    private static int RelinkIdentity(Options options, TextWriter stdout, TextWriter stderr)
    {
        string input = Path.GetFullPath(options.Positional(0, "ps4 zone.ff"));
        Workspace workspace = Workspace.Locate(options.Value("--root"));
        string loader = T7Paths.Ps4Loader(workspace);
        string work = Path.GetFullPath(options.Value("--work") ?? Path.Combine(T7Paths.Work(workspace), "relink"));
        Directory.CreateDirectory(work);
        string walkPath = options.Value("--walk") ?? Path.Combine(work, Path.GetFileNameWithoutExtension(input) + ".source.ps4.t7walk");
        if (!File.Exists(walkPath))
        {
            NativeProcessResult walked = NativeProcess.Run(T7Ps4Loader.TaskName, T7Ps4Loader.ChildArguments(loader, walkPath, input));
            if (walked.ExitCode != 0)
            {
                stderr.WriteLine(walked.Stderr);
                return walked.ExitCode;
            }
        }
        T7FastFile.Decoded decoded = T7FastFile.Load(input);
        var source = new FFPorter.Core.T7.Link.T7WalkIndex("source", decoded.Zone, FFPorter.Core.T7.Harness.T7Walk.Read(walkPath));
        var builder = new FFPorter.Core.T7.Link.T7ZoneBuilder();
        builder.MapScriptStrings(source, source.List.ScriptStrings.Select(builder.AddScriptString).ToArray());
        for (int i = 0; i < source.Walk.Assets.Count; i++)
        {
            FFPorter.Core.T7.Harness.T7Walk.Asset asset = source.Walk.Assets[i];
            var output = new FFPorter.Core.T7.Link.T7OutputAsset { Type = asset.Type, Name = asset.Name, Origin = "source" };
            long cell = source.List.AssetTableOffset + 16L * i + 8;
            ulong marker = source.List.Assets[i].Header;
            if (source.Pointers.TryGetValue(cell, out var pointer) && pointer.Kind is FFPorter.Core.T7.Harness.T7PointerKind.Packed or FFPorter.Core.T7.Harness.T7PointerKind.PackedAlias)
                output.HeaderTarget = new FFPorter.Core.T7.Link.T7SourceTarget(source, pointer.Target, cell);
            else
                output.HeaderMarker = marker;
            if (asset.End > asset.Start)
                output.Main.Add(new FFPorter.Core.T7.Link.T7CopyPiece(source, asset.Start, asset.End - asset.Start));
            foreach (var span in asset.Deferred)
                output.Deferred.Add(new FFPorter.Core.T7.Link.T7CopyPiece(source, span.FileOffset, span.Size));
            builder.Assets.Add(output);
            builder.MapAsset(source, i, i);
        }
        var linker = new FFPorter.Core.T7.Link.T7Linker(loader, work, stdout.WriteLine);
        string outFile = Path.Combine(work, Path.GetFileNameWithoutExtension(input) + ".relinked.ff");
        var result = linker.Build(builder, decoded.Header, decoded.Header.ZoneName, outFile);
        int mismatches = 0;
        long first = -1;
        if (result.Zone.Length != decoded.Zone.Length)
            stdout.WriteLine($"zone length differs: {result.Zone.Length} vs {decoded.Zone.Length}");
        for (int i = 0; i < Math.Min(result.Zone.Length, decoded.Zone.Length); i++)
        {
            if (result.Zone[i] != decoded.Zone[i])
            {
                if (first < 0)
                    first = i;
                mismatches++;
            }
        }
        stdout.WriteLine($"identity relink: {mismatches} differing bytes (first at 0x{first:x}); {result.Problems.Count} problems");
        foreach (string problem in result.Problems.Take(20))
            stdout.WriteLine("  " + problem);
        return mismatches == 0 && result.Problems.Count == 0 ? 0 : 1;
    }

}
