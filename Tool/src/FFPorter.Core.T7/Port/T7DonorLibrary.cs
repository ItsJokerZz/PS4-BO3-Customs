using System.Text;
using FFPorter.Core.Common.Native;
using FFPorter.Core.T7.Harness;
using FFPorter.Core.T7.Link;

namespace FFPorter.Core.T7.Port;

public sealed class T7DonorLibrary
{
    public const int EagerLimit = 8;

    private readonly List<T7WalkIndex> _zones = [];
    private readonly Dictionary<(int Type, string Name), List<(T7WalkIndex Zone, T7Walk.Asset Asset)>> _assets = [];
    private readonly Dictionary<string, List<string>> _catalog = new(StringComparer.Ordinal);
    private readonly HashSet<string> _walkedFiles = new(StringComparer.OrdinalIgnoreCase);
    private string _loaderDirectory = "", _cacheDirectory = "";
    private Action<string> _log = _ => { };

    public IReadOnlyList<T7WalkIndex> Zones => _zones;

    public int AssetCount => _assets.Count;

    public int CataloguedNames => _catalog.Count;

    public static T7DonorLibrary Load(IEnumerable<string> fastFiles, string loaderDirectory, string cacheDirectory, Action<string> log)
    {
        var library = new T7DonorLibrary { _loaderDirectory = loaderDirectory, _cacheDirectory = cacheDirectory, _log = log };
        Directory.CreateDirectory(cacheDirectory);
        List<string> ps4 = fastFiles.Where(IsPs4Zone).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ps4.Count <= EagerLimit)
        {
            foreach (string file in ps4)
                library.WalkFile(file);
            log($"PS4 reference library: {library._zones.Count} zones, {library._assets.Count} named assets");
        }
        else
        {
            int done = 0;
            foreach (string file in ps4.OrderBy(f => new FileInfo(f).Length))
            {
                foreach (string name in library.HashedNames(file))
                {
                    if (!library._catalog.TryGetValue(name, out List<string>? files))
                        library._catalog[name] = files = [];
                    files.Add(file);
                }
                if (++done % 50 == 0 || done == ps4.Count)
                    log($"PS4 reference catalog: {done}/{ps4.Count} zones indexed");
            }
            log($"PS4 reference catalog: {ps4.Count} zones, {library._catalog.Count} hashed asset names (zones are walked on demand)");
        }
        return library;
    }

    private static bool IsPs4Zone(string file)
    {
        try
        {
            byte[] head = new byte[T7Header.Size];
            using FileStream stream = File.OpenRead(file);
            return stream.Read(head) == head.Length && T7Header.Parse(head).Platform == 2;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    internal static string CacheKey(string file) => T7FastFileDelta.CacheKey(file);

    private bool WalkFile(string file)
    {
        if (!_walkedFiles.Add(file))
            return false;
        try
        {
            string source = T7FastFileDelta.Resolve(file, _cacheDirectory, _log);
            string walkPath = Path.Combine(_cacheDirectory, $"{Path.GetFileNameWithoutExtension(file)}.{CacheKey(file)}.ps4.t7walk");
            if (!File.Exists(walkPath))
            {
                _log($"walking PS4 reference zone {Path.GetFileName(file)}");
                string partial = walkPath + ".partial";
                NativeProcessResult result = NativeProcess.Run(T7Ps4Loader.TaskName, T7Ps4Loader.ChildArguments(_loaderDirectory, partial, source));
                if (result.ExitCode != 0)
                {
                    _log($"  skipped {Path.GetFileName(file)}: PS4 walk failed ({result.ExitCode}) {result.Stderr.Trim()}");
                    return false;
                }
                File.Move(partial, walkPath, overwrite: true);
            }
            Add(T7WalkIndex.Load(Path.GetFileNameWithoutExtension(file), source, walkPath));
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _log($"  skipped {Path.GetFileName(file)}: {error.Message}");
            return false;
        }
    }

    private IReadOnlyList<string> HashedNames(string file) => HashedNames(file, _cacheDirectory, _log);

    internal static IReadOnlyList<string> HashedNames(string file, string cacheDirectory, Action<string> log)
    {
        string cache = Path.Combine(cacheDirectory, "names", $"{Path.GetFileNameWithoutExtension(file)}.{CacheKey(file)}.names.txt");
        if (File.Exists(cache))
            return File.ReadAllLines(cache);
        var names = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            byte[] zone = T7FastFileDelta.LoadPatched(file).Zone;
            ReadOnlySpan<byte> span = zone;
            for (int at = span.IndexOf((byte)'#'); at >= 0;)
            {
                if (at + 9 < span.Length && span[at + 9] == 0 && IsHex(span.Slice(at + 1, 8)))
                {
                    int start = at;
                    while (start > 0 && span[start - 1] is >= 0x20 and < 0x7F && at - start < 200)
                        start--;
                    if (start < at)
                        names.Add(Encoding.Latin1.GetString(span[start..(at + 9)]));
                }
                int next = span[(at + 1)..].IndexOf((byte)'#');
                at = next < 0 ? -1 : at + 1 + next;
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            log($"  could not index {Path.GetFileName(file)}: {error.Message}");
            return [];
        }
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        File.WriteAllLines(cache, names.Order(StringComparer.Ordinal));
        return names.ToList();
    }

    private static bool IsHex(ReadOnlySpan<byte> text)
    {
        foreach (byte c in text)
        {
            if (c is not ((>= (byte)'0' and <= (byte)'9') or (>= (byte)'a' and <= (byte)'f') or (>= (byte)'A' and <= (byte)'F')))
                return false;
        }
        return true;
    }

    public void Add(T7WalkIndex zone)
    {
        _zones.Add(zone);
        foreach (T7Walk.Asset asset in zone.Walk.Assets)
        {
            string? name = asset.Name;
            if (string.IsNullOrEmpty(name) || asset.Reads.Count == 0)
                continue;
            if (!_assets.TryGetValue((asset.Type, name), out var candidates))
                _assets[(asset.Type, name)] = candidates = [];
            if (!candidates.Exists(c => c.Zone == zone))
                candidates.Add((zone, asset));
        }
    }

    public string? PreferredZone { get; set; }

    public bool TryFind(int type, string name, out T7WalkIndex? zone, out T7Walk.Asset? asset)
    {
        if (!_assets.ContainsKey((type, name)) && _catalog.TryGetValue(name, out List<string>? files))
        {
            foreach (string file in files)
            {
                if (_walkedFiles.Contains(file))
                    continue;
                WalkFile(file);
                if (_assets.ContainsKey((type, name)))
                    break;
            }
        }
        if (_assets.TryGetValue((type, name), out var candidates) && candidates.Count > 0)
        {
            var found = candidates.Find(c => string.Equals(c.Zone.Label, PreferredZone, StringComparison.OrdinalIgnoreCase));
            if (found.Zone == null)
                found = candidates[0];
            zone = found.Zone;
            asset = found.Asset;
            return true;
        }
        zone = null;
        asset = null;
        return false;
    }
}
