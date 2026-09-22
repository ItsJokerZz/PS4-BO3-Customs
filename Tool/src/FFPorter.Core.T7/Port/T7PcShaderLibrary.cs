using System.Buffers.Binary;
using System.Text.Json;
using FFPorter.Core.Common.Native;
using FFPorter.Core.T7.Harness;
using FFPorter.Core.T7.Link;
using FFPorter.Core.T7.Shaders;

namespace FFPorter.Core.T7.Port;

public sealed class T7PcShaderLibrary
{
    private const int PassSize = 88, MaxPasses = 8, Techniques = 12;
    private const string ReflectionCacheVersion = "t7-pc-reflection-2";
    private static readonly Dictionary<string, int> StageField = new() { ["vs"] = 24, ["ps"] = 32, ["gs"] = 40, ["vs2"] = 48, ["hs"] = 56, ["ds"] = 64 };

    public sealed class Reflection : Dictionary<(int Technique, int Pass, string Stage), IReadOnlyList<T7Dxbc.Variable>>
    {
    }

    private readonly Dictionary<string, Reflection> _techsets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _catalog = new(StringComparer.Ordinal);
    private readonly HashSet<string> _read = new(StringComparer.OrdinalIgnoreCase);
    private string _pcImage = "", _cacheDirectory = "";
    private Action<string> _log = _ => { };

    public int CataloguedNames => _catalog.Count;

    public static T7PcShaderLibrary Create(IEnumerable<string> pcZones, string pcImage, string cacheDirectory, Action<string> log)
    {
        var library = new T7PcShaderLibrary { _pcImage = pcImage, _cacheDirectory = cacheDirectory, _log = log };
        Directory.CreateDirectory(cacheDirectory);
        int count = 0;
        foreach (string file in pcZones.Where(IsPcZone).OrderBy(f => new FileInfo(f).Length))
        {
            foreach (string name in T7DonorLibrary.HashedNames(file, cacheDirectory, log))
            {
                if (!library._catalog.TryGetValue(name.TrimStart(','), out List<string>? files))
                    library._catalog[name.TrimStart(',')] = files = [];
                files.Add(file);
            }
            count++;
        }
        if (count > 0)
            log($"PC shader catalog: {count} zones, {library._catalog.Count} technique set names");
        return library;
    }

    private static bool IsPcZone(string file)
    {
        try
        {
            byte[] head = new byte[T7Header.Size];
            using FileStream stream = File.OpenRead(file);
            return stream.Read(head) == head.Length && T7Header.Parse(head).Platform == 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    public void AddZone(T7WalkIndex zone)
    {
        foreach ((string name, Reflection reflection) in Reflect(zone))
            _techsets.TryAdd(name, reflection);
        foreach ((string techsetName, T7Walk.Span header) in TechniqueSetHeaders(zone))
        {
            if (header.Size != Converters.T7TechsetConverter.HeaderSize || Converters.T7TechsetConverter.IsStub(zone.Zone, header))
                continue;
            string name = techsetName.TrimStart(',');
            for (int technique = 0; technique < Techniques; technique++)
            {
                if (!Follow(zone, header.FileOffset + 16 + 8 * technique, out long techniqueAt))
                    continue;
                for (int pass = 0; pass < MaxPasses; pass++)
                {
                    foreach ((string stage, int field) in StageField)
                    {
                        long passAt = techniqueAt + 8 + PassSize * pass;
                        if (passAt + field + 8 > zone.Zone.Length || !Follow(zone, passAt + field, out long shaderAt))
                            continue;
                        uint size = BinaryPrimitives.ReadUInt32LittleEndian(zone.Zone.AsSpan((int)shaderAt + 32));
                        if (size > 0 && Follow(zone, shaderAt + 24, out long program) && program + size <= zone.Zone.Length)
                            _programs.TryAdd((name, technique, pass, stage), zone.Zone.AsSpan((int)program, (int)size).ToArray());
                    }
                }
            }
        }
    }

    private readonly Dictionary<(string Techset, int Technique, int Pass, string Stage), byte[]> _programs = [];

    public byte[]? Program(string techset, int technique, int pass, string stage) =>
        _programs.GetValueOrDefault((techset.TrimStart(','), technique, pass, stage));

    public static Dictionary<string, Reflection> Reflect(T7WalkIndex zone)
    {
        var result = new Dictionary<string, Reflection>(StringComparer.Ordinal);
        var shaders = new Dictionary<long, IReadOnlyList<T7Dxbc.Variable>>();
        foreach ((string techsetName, T7Walk.Span header) in TechniqueSetHeaders(zone))
        {
            if (header.Size != Converters.T7TechsetConverter.HeaderSize || Converters.T7TechsetConverter.IsStub(zone.Zone, header))
                continue;
            var reflection = new Reflection();
            for (int technique = 0; technique < Techniques; technique++)
            {
                if (!Follow(zone, header.FileOffset + 16 + 8 * technique, out long techniqueAt))
                    continue;
                for (int pass = 0; pass < MaxPasses; pass++)
                {
                    long passAt = techniqueAt + 8 + PassSize * pass;
                    foreach ((string stage, int field) in StageField)
                    {
                        if (passAt + field + 8 > zone.Zone.Length || !Follow(zone, passAt + field, out long shaderAt))
                            continue;
                        if (!shaders.TryGetValue(shaderAt, out IReadOnlyList<T7Dxbc.Variable>? variables))
                            shaders[shaderAt] = variables = Globals(zone, shaderAt);
                        reflection[(technique, pass, stage)] = variables;
                    }
                }
            }
            result.TryAdd(techsetName.TrimStart(','), reflection);
        }
        return result;
    }

    public static IEnumerable<(string Name, T7Walk.Span Header)> TechniqueSetHeaders(T7WalkIndex zone)
    {
        foreach (T7Walk.Asset asset in zone.Walk.Assets)
        {
            if (asset.Reads.Count == 0)
                continue;
            if (asset.Type == T7AssetTypes.TechniqueSet)
            {
                if (!string.IsNullOrEmpty(asset.Name))
                    yield return (asset.Name, asset.Reads[0]);
                continue;
            }
            foreach (T7Walk.Registration registration in asset.Registrations)
            {
                if (registration.Type != T7AssetTypes.TechniqueSet || string.IsNullOrEmpty(registration.Name))
                    continue;
                for (int i = asset.Reads.Count - 1; i >= 0; i--)
                {
                    T7Walk.Span read = asset.Reads[i];
                    if (read.FileOffset >= registration.FilePos)
                        continue;
                    if (read.Kind == T7WalkKind.Read && read.Size == Converters.T7TechsetConverter.HeaderSize && read.At == registration.Header)
                    {
                        yield return (registration.Name, read);
                        break;
                    }
                }
            }
        }
    }

    private static IReadOnlyList<T7Dxbc.Variable> Globals(T7WalkIndex zone, long shaderAt)
    {
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(zone.Zone.AsSpan((int)shaderAt + 32));
        if (!Follow(zone, shaderAt + 24, out long program) || size == 0 || program + size > zone.Zone.Length)
            return [];
        IReadOnlyList<T7Dxbc.ConstantBuffer>? buffers = T7Dxbc.ConstantBuffers(zone.Zone.AsSpan((int)program, (int)size));
        return buffers?.FirstOrDefault(b => b.Name == "$Globals")?.Variables ?? [];
    }

    private bool EnsureTechset(string name)
    {
        if (_techsets.ContainsKey(name))
            return true;
        if (!_catalog.TryGetValue(name, out List<string>? files))
            return false;
        foreach (string file in files)
        {
            if (!_read.Add(file))
                continue;
            try
            {
                foreach ((string defined, Reflection reflection) in ZoneReflection(file, name))
                    _techsets.TryAdd(defined, reflection);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                _log($"  skipped {Path.GetFileName(file)}: {error.Message}");
            }
            if (_techsets.ContainsKey(name))
                return true;
        }
        return false;
    }

    private Dictionary<string, Reflection> ZoneReflection(string file, string wanted)
    {
        string stem = $"{Path.GetFileNameWithoutExtension(file)}.{T7DonorLibrary.CacheKey(file)}";
        string cachePath = Path.Combine(_cacheDirectory, "reflection", stem + ".json");
        if (File.Exists(cachePath) && LoadCache(cachePath) is { } cached)
            return cached;
        string source = T7FastFileDelta.Resolve(file, _cacheDirectory, _log);
        string walkPath = Path.Combine(_cacheDirectory, stem + ".pc.t7walk");
        if (!File.Exists(walkPath))
        {
            _log($"walking PC reference zone {Path.GetFileName(file)} for technique set {wanted}");
            string partial = walkPath + ".partial";
            NativeProcessResult result = NativeProcess.Run(T7PcLoader.TaskName, T7PcLoader.ChildArguments(_pcImage, partial, source));
            if (result.ExitCode != 0)
                throw new InvalidDataException($"PC walk failed ({result.ExitCode}) {result.Stderr.Trim()}");
            File.Move(partial, walkPath, overwrite: true);
        }
        Dictionary<string, Reflection> reflected = Reflect(T7WalkIndex.Load(Path.GetFileNameWithoutExtension(file), source, walkPath));
        SaveCache(cachePath, reflected);
        return reflected;
    }

    private static void SaveCache(string path, Dictionary<string, Reflection> techsets)
    {
        var document = new Dictionary<string, object>
        {
            ["version"] = ReflectionCacheVersion,
            ["techsets"] = techsets.ToDictionary(t => t.Key, t => t.Value.Select(e => new object[]
            {
                e.Key.Technique, e.Key.Pass, e.Key.Stage,
                e.Value.Select(v => new object[] { v.Name, v.Start, v.Size, v.Flags, v.Class, v.Type, v.Rows, v.Columns, v.Elements }).ToArray(),
            }).ToArray()),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(document));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    private static Dictionary<string, Reflection>? LoadCache(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("version", out JsonElement version) || version.GetString() != ReflectionCacheVersion)
            return null;
        var result = new Dictionary<string, Reflection>(StringComparer.Ordinal);
        foreach (JsonProperty techset in document.RootElement.GetProperty("techsets").EnumerateObject())
        {
            var reflection = new Reflection();
            foreach (JsonElement entry in techset.Value.EnumerateArray())
            {
                var variables = entry[3].EnumerateArray().Select(v => new T7Dxbc.Variable(v[0].GetString()!, v[1].GetInt32(), v[2].GetInt32(),
                    v[3].GetUInt32(), v[4].GetInt32(), v[5].GetInt32(), v[6].GetInt32(), v[7].GetInt32(), v[8].GetInt32())).ToList();
                reflection[(entry[0].GetInt32(), entry[1].GetInt32(), entry[2].GetString()!)] = variables;
            }
            result[techset.Name] = reflection;
        }
        return result;
    }

    public IReadOnlyList<T7Dxbc.Variable>? Globals(string techset, int technique, int pass, string stage)
    {
        string name = techset.TrimStart(',');
        if (!EnsureTechset(name))
            return null;
        return _techsets[name].TryGetValue((technique, pass, stage), out IReadOnlyList<T7Dxbc.Variable>? variables) ? variables : null;
    }

    private static bool Follow(T7WalkIndex zone, long field, out long target)
    {
        target = -1;
        return field >= 0 && field + 8 <= zone.Zone.Length && BinaryPrimitives.ReadUInt64LittleEndian(zone.Zone.AsSpan((int)field)) != 0
            && zone.TryFollow(field, out target);
    }
}
