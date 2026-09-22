using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FFPorter.Core.T7.Streams;

public delegate List<(uint LogicalOffset, byte[] Bytes)> T7PayloadTransform(XPakIndexRecord? record, List<(uint LogicalOffset, byte[] Bytes)> parts);

public sealed class T7StreamPlan
{
    private readonly Dictionary<ulong, (T7PayloadTransform Transform, string Signature)> _transforms = [];
    private readonly Dictionary<ulong, string> _owners = [];

    public string? Owner { get; set; }

    public void Register(ulong pcKey, T7PayloadTransform transform, string signature)
    {
        _transforms[pcKey] = (transform, signature);
        if (Owner != null)
            _owners[pcKey] = Owner;
    }


    private readonly Dictionary<(string Type, string Name, long Size), (T7PayloadTransform Transform, string Signature)> _named = [];
    private readonly Dictionary<(string Type, long Size), (T7PayloadTransform Transform, string Signature, bool Ambiguous)> _shapes = [];

    public void RegisterNamed(string? type, string? name, long size, T7PayloadTransform transform, string signature, bool byShape = false)
    {
        if (type is not { Length: > 0 })
            return;
        if (name is { Length: > 0 })
            _named[(type, name, size)] = (transform, signature);
        if (!byShape)
            return;
        _shapes[(type, size)] = _shapes.TryGetValue((type, size), out var shape) && shape.Signature != signature
            ? (shape.Transform, shape.Signature, true)
            : (transform, signature, shape.Ambiguous);
    }

    public bool TryGetNamed(string? type, string? name, long size, out T7PayloadTransform transform)
    {
        transform = null!;
        if (type is not { Length: > 0 })
            return false;
        if (name is { Length: > 0 } && _named.TryGetValue((type, name, size), out var entry))
        {
            transform = entry.Transform;
            return true;
        }
        if (!_shapes.TryGetValue((type, size), out var shape) || shape.Ambiguous)
            return false;
        transform = shape.Transform;
        return true;
    }

    public string? OwnerOf(ulong pcKey) => _owners.GetValueOrDefault(pcKey);

    public bool TryGet(ulong pcKey, out T7PayloadTransform transform)
    {
        bool found = _transforms.TryGetValue(pcKey, out var entry);
        transform = entry.Transform;
        return found;
    }

    public int Count => _transforms.Count;

    public IEnumerable<ulong> Keys => _transforms.Keys;

    public string Signature()
    {
        var text = new StringBuilder();
        foreach ((ulong key, (T7PayloadTransform _, string signature)) in _transforms.OrderBy(p => p.Key))
            text.Append(key.ToString("x16")).Append('=').Append(signature).Append('\n');
        foreach (((string type, string name, long size), (T7PayloadTransform _, string signature)) in
            _named.OrderBy(p => p.Key.Type, StringComparer.Ordinal).ThenBy(p => p.Key.Name, StringComparer.Ordinal).ThenBy(p => p.Key.Size))
        {
            text.Append(type).Append('/').Append(name).Append('/').Append(size).Append('=').Append(signature).Append('\n');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}

public sealed class T7StreamMap
{
    public sealed record Item(ulong PcKey, ulong Ps4Key, long PcSize, long Ps4Size, string Type, string? Name, string Source, IReadOnlyList<long>? Ps4PartSizes = null);

    private readonly Dictionary<ulong, Item> _items = [];

    public void Add(Item item) => _items[item.PcKey] = item;

    public bool TryGet(ulong pcKey, out Item item) => _items.TryGetValue(pcKey, out item!);

    public IReadOnlyCollection<Item> Items => _items.Values;

    public T7StreamMap Copy()
    {
        var copy = new T7StreamMap();
        foreach (Item item in _items.Values)
            copy.Add(item);
        return copy;
    }

    public void Merge(T7StreamMap other)
    {
        foreach (Item item in other.Items)
        {
            if (!_items.TryGetValue(item.PcKey, out Item? existing) || (existing.Source == Unresolved && item.Source != Unresolved))
                _items[item.PcKey] = item;
        }
    }

    public const string Unresolved = "unresolved";

    public string Signature() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
        _items.Values.Where(i => i.Source != Unresolved).OrderBy(i => i.PcKey).Select(i => $"{i.PcKey:x16}>{i.Ps4Key:x16}")))));

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(_items.Values.OrderBy(i => i.PcKey).Select(i => new
    {
        pc_key = $"{i.PcKey:x16}",
        ps4_key = $"{i.Ps4Key:x16}",
        i.PcSize,
        i.Ps4Size,
        i.Type,
        i.Name,
        i.Source,
    }), new JsonSerializerOptions { WriteIndented = true }));
}

public sealed class T7Ps4StreamIndex
{
    private readonly Dictionary<(string Type, string Name, string Model, int Tag), (ulong Key, string Text, int Rank)> _records = [];
    private readonly Dictionary<ulong, string> _texts = [];
    private readonly HashSet<(string Type, string Name, string Model, int Tag)> _ambiguous = [];

    public int Count => _records.Count;

    public string Signature { get; private set; } = "";

    public static T7Ps4StreamIndex Load(IEnumerable<string> xpaks, Action<string>? log = null)
    {
        var index = new T7Ps4StreamIndex();
        var stamps = new StringBuilder();
        foreach (string path in xpaks)
        {
            try
            {
                var info = new FileInfo(path);
                stamps.Append($"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}\n");
                using var xpak = new XPak(path);
                bool indexOnly = xpak.Data.Count == 0;
                foreach ((ulong key, XPakIndexRecord record) in xpak.Index)
                {
                    int rank = xpak.TryFind(key, out _) ? 2 : indexOnly ? 0 : 1;
                    var id = Identity(record);
                    index._texts.TryAdd(key, record.Text);
                    if (!index._records.TryGetValue(id, out var existing) || rank > existing.Rank)
                        index._records[id] = (key, record.Text, rank);
                    else if (existing.Key != key && rank == 2 && existing.Rank == 2)
                        index._ambiguous.Add(id);
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                log?.Invoke($"skipped PS4 xpak {Path.GetFileName(path)}: {exception.Message}");
            }
        }
        index.Signature = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stamps.ToString())));
        return index;
    }

    public bool ContainsKey(ulong key) => _texts.ContainsKey(key);

    public static (string Type, string Name, string Model, int Tag) Identity(XPakIndexRecord record) =>
        (record.Type ?? "", record.Name ?? "", record.Fields.GetValueOrDefault("model") ?? "", (int)(record.Key >> 61));

    public bool TryFind(XPakIndexRecord pcRecord, out ulong key, out string text)
    {
        if (_texts.TryGetValue(pcRecord.Key, out string? same))
        {
            (key, text) = (pcRecord.Key, same);
            return true;
        }
        var id = Identity(pcRecord);
        if (!_ambiguous.Contains(id) && _records.TryGetValue(id, out var found))
        {
            (key, text) = (found.Key, found.Text);
            return true;
        }
        (key, text) = (0, "");
        return false;
    }
}

public sealed class T7PcStreamSource : IDisposable
{
    private readonly List<string> _paths;
    private List<XPak>? _xpaks;
    private readonly Action<string>? _log;

    public T7PcStreamSource(IEnumerable<string> xpaks, Action<string>? log = null)
    {
        _paths = xpaks.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(p => !p.EndsWith("_d.xpak", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        _log = log;
        var stamps = new StringBuilder();
        foreach (string path in _paths)
        {
            var info = new FileInfo(path);
            stamps.Append($"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}\n");
        }
        Signature = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stamps.ToString())));
    }

    public int Count => _paths.Count;

    public string Signature { get; }

    public bool TryRead(ulong key, string? except, out List<(uint LogicalOffset, byte[] Bytes)> parts, out string source)
    {
        _xpaks ??= Open();
        foreach (XPak xpak in _xpaks)
        {
            if (except != null && string.Equals(xpak.Path, except, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!xpak.TryFind(key, out XPak.HashEntry entry))
                continue;
            parts = xpak.ReadParts(entry);
            source = Path.GetFileName(xpak.Path);
            return true;
        }
        (parts, source) = ([], "");
        return false;
    }

    private List<XPak> Open()
    {
        var xpaks = new List<XPak>();
        foreach (string path in _paths)
        {
            try
            {
                xpaks.Add(new XPak(path));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _log?.Invoke($"skipped PC xpak {Path.GetFileName(path)}: {error.Message}");
            }
        }
        return xpaks;
    }

    public void Dispose()
    {
        foreach (XPak xpak in _xpaks ?? [])
            xpak.Dispose();
        _xpaks = null;
    }
}

public sealed class T7StreamConvertOptions
{
    public required string PcXPak { get; init; }
    public required string OutputXPak { get; init; }

    public string? PcIndexXPak { get; init; }
    public string? OutputIndexXPak { get; init; }

    public string? CacheDirectory { get; init; }

    public T7StreamPlan Plan { get; init; } = new();
    public T7Ps4StreamIndex? Ps4Index { get; init; }

    public T7StreamMap? KnownItems { get; init; }

    public T7PcStreamSource? PcSource { get; init; }

    public byte[]? Zone { get; init; }

    public Action<string> Log { get; init; } = _ => { };

    public Action<int, int>? Progress { get; init; }
}

public sealed record T7StreamConvertResult(T7StreamMap Map, IReadOnlyDictionary<string, int> Counts, IReadOnlyList<string> Warnings);

public static class T7StreamConvert
{
    public const string RulesVersion = "t7-streams-8";

    public static T7StreamConvertResult Run(T7StreamConvertOptions options)
    {
        string signature = Signature(options);
        string cachePath = options.CacheDirectory is { Length: > 0 } cacheFolder
            ? Path.Combine(cacheFolder, Path.GetFileName(options.OutputXPak) + ".t7cache.json")
            : options.OutputXPak + ".t7cache.json";
        if (TryLoadCache(cachePath, signature, options, out T7StreamConvertResult? cached))
        {
            options.Log($"  {Path.GetFileName(options.OutputXPak)} is up to date (reusing the previous conversion)");
            return cached!;
        }
        T7StreamConvertResult result = ConvertAll(options);
        SaveCache(cachePath, signature, options, result);
        return result;
    }

    private static string Stamp(string? path)
    {
        if (path == null || !File.Exists(path))
            return "-";
        var info = new FileInfo(path);
        return $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
    }

    private static string Signature(T7StreamConvertOptions options) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
        RulesVersion, Stamp(options.PcXPak), Stamp(options.PcIndexXPak), options.OutputIndexXPak ?? "-", options.Plan.Signature(), options.Ps4Index?.Signature ?? "-",
        options.PcSource?.Signature ?? "-", options.KnownItems?.Signature() ?? "-"))));

    private sealed record CacheFile(string Signature, string Output, string? IndexOutput, List<CacheItem> Items, Dictionary<string, int> Counts, List<string> Warnings);

    private sealed record CacheItem(ulong Pc, ulong Ps4, long PcSize, long Ps4Size, string Type, string? Name, string Source, List<long>? Parts);

    private static bool TryLoadCache(string path, string signature, T7StreamConvertOptions options, out T7StreamConvertResult? result)
    {
        result = null;
        try
        {
            if (!File.Exists(path))
                return false;
            CacheFile? cache = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path));
            if (cache == null || cache.Signature != signature || cache.Output != Stamp(options.OutputXPak)
                || (options.OutputIndexXPak != null && options.PcIndexXPak != null && File.Exists(options.PcIndexXPak) && cache.IndexOutput != Stamp(options.OutputIndexXPak)))
                return false;
            var map = new T7StreamMap();
            foreach (CacheItem item in cache.Items)
                map.Add(new T7StreamMap.Item(item.Pc, item.Ps4, item.PcSize, item.Ps4Size, item.Type, item.Name, item.Source, item.Parts));
            result = new T7StreamConvertResult(map, cache.Counts, cache.Warnings);
            return true;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static void SaveCache(string path, string signature, T7StreamConvertOptions options, T7StreamConvertResult result)
    {
        try
        {
            var items = result.Map.Items.Select(i => new CacheItem(i.PcKey, i.Ps4Key, i.PcSize, i.Ps4Size, i.Type, i.Name, i.Source, i.Ps4PartSizes?.ToList())).ToList();
            var cache = new CacheFile(signature, Stamp(options.OutputXPak), options.OutputIndexXPak == null ? null : Stamp(options.OutputIndexXPak),
                items, new Dictionary<string, int>(result.Counts), [.. result.Warnings]);
            File.WriteAllText(path, JsonSerializer.Serialize(cache));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            options.Log($"  could not write the conversion cache: {error.Message}");
        }
    }

    private static T7StreamConvertResult ConvertAll(T7StreamConvertOptions options)
    {
        var map = new T7StreamMap();
        var counts = new Dictionary<string, int>();
        var warnings = new List<string>();
        void Count(string what) => counts[what] = counts.GetValueOrDefault(what) + 1;
        var texts = new Dictionary<ulong, string>();

        HashSet<ulong> externalKeys = [];
        HashSet<ulong>? inZone = null;
        bool UsedByZone(ulong pcKey)
        {
            if (options.Plan.TryGet(pcKey, out _))
                return true;
            if (options.Zone == null)
                return false;
            if (inZone == null)
            {
                inZone = [];
                ReadOnlySpan<byte> bytes = options.Zone;
                for (int at = 0; at + 8 <= bytes.Length; at++)
                {
                    ulong value = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes[at..]);
                    if (externalKeys.Contains(value))
                        inZone.Add(value);
                }
            }
            return inZone.Contains(pcKey);
        }

        using (var pc = new XPak(options.PcXPak))
        {
            IReadOnlyDictionary<ulong, XPakIndexRecord> index = pc.Index;
            externalKeys = [.. index.Keys.Where(k => !pc.TryFind(k, out _))];
            string temporary = options.OutputXPak + ".partial";
            var ordered = pc.Entries.OrderBy(e => e.Offset).ToList();
            int done = 0;

            (ulong Key, byte[] Stored) ConvertItem(ulong pcKey, XPakIndexRecord? record, List<(uint LogicalOffset, byte[] Bytes)> parts, string? bundledFrom)
            {
                long pcSize = parts.Sum(p => (long)p.Bytes.Length);
                string type = record?.Type ?? "?";
                List<(uint LogicalOffset, byte[] Bytes)> converted;
                string source;
                try
                {
                    if (options.Plan.TryGet(pcKey, out T7PayloadTransform? transform))
                    {
                        converted = transform(record, parts);
                        source = "zone transform";
                    }
                    else if (options.Plan.TryGetNamed(record?.Type, record?.Name, pcSize, out T7PayloadTransform named))
                    {
                        converted = named(record, parts);
                        source = "zone transform, same-named item";
                    }
                    else
                    {
                        converted = DefaultTransform(record, parts, out source, warnings);
                    }
                }
                catch (Exception error) when (error is InvalidDataException or ArgumentException or IndexOutOfRangeException or NotSupportedException)
                {
                    converted = parts;
                    source = "unchanged (conversion failed)";
                    if (warnings.Count < 5000)
                        warnings.Add($"{type} '{record?.Name ?? pcKey.ToString("x16")}' could not be converted ({error.Message}); it was copied unchanged");
                }
                if (record != null && ReferenceEquals(converted, parts))
                    converted = SplitByRecord(parts, record);
                Count(bundledFrom == null ? $"{type}: {source}" : $"{type}: external, bundled ({source})");
                int tag = (int)(pcKey >> 61);
                byte[] payload = Concat(converted);
                List<(uint LogicalOffset, byte[] Bytes)> pcParts = record != null ? SplitByRecord(parts, record) : parts;
                bool derived = XPak.ComputeKey(pcParts.Select(p => p.Bytes), tag) == pcKey;
                ulong key = derived ? XPak.ComputeKey(converted.Select(p => p.Bytes), tag) : pcKey;
                if (!derived && !source.StartsWith("unchanged", StringComparison.Ordinal))
                    warnings.Add($"{type} '{record?.Name}' ({pcKey:x16}) has a key that is not its payload hash; the PC key is kept");
                List<(long Offset, long Size)> ps4Parts = converted.Select(p => ((long)p.LogicalOffset, (long)p.Bytes.Length)).ToList();
                if (record != null)
                    texts[key] = record.WithParts(ps4Parts);
                string itemSource = bundledFrom == null ? source : $"bundled from {bundledFrom} ({source})";
                map.Add(new T7StreamMap.Item(pcKey, key, pcSize, payload.Length, type, record?.Name, itemSource, ps4Parts.Select(p => p.Size).ToList()));
                return (key, XPakWriter.BuildEntry(converted));
            }

            (ulong Key, byte[] Stored)? Bundle(ulong pcKey, XPakIndexRecord? record)
            {
                try
                {
                    if (!options.PcSource!.TryRead(pcKey, pc.Path, out List<(uint LogicalOffset, byte[] Bytes)> parts, out string from))
                        return null;
                    return ConvertItem(pcKey, record, parts, from);
                }
                catch (InvalidDataException error)
                {
                    if (warnings.Count < 5000)
                        warnings.Add($"{record?.Type ?? "item"} '{record?.Name ?? pcKey.ToString("x16")}' could not be converted in from the PC xpaks ({error.Message}); the PC key is kept");
                    return null;
                }
            }

            IEnumerable<(ulong Key, byte[] Stored)> Entries()
            {
                foreach (XPak.HashEntry entry in ordered)
                {
                    XPakIndexRecord? record = index.GetValueOrDefault(entry.Key);
                    (ulong Key, byte[] Stored) item = ConvertItem(entry.Key, record, pc.ReadParts(entry), null);
                    if (++done % 250 == 0 || done == ordered.Count)
                        options.Log($"  xpak {Path.GetFileName(options.PcXPak)}: {done}/{ordered.Count} items");
                    options.Progress?.Invoke(done, ordered.Count);
                    yield return item;
                }
                if (options.PcSource == null)
                    yield break;
                int bundled = 0;
                long bundledBytes = 0;
                foreach ((ulong pcKey, XPakIndexRecord record) in index.OrderBy(p => p.Key))
                {
                    if (pc.TryFind(pcKey, out _) || map.TryGet(pcKey, out _)
                        || (options.KnownItems != null && options.KnownItems.TryGet(pcKey, out T7StreamMap.Item? known) && known.Source != T7StreamMap.Unresolved)
                        || (options.Ps4Index != null && options.Ps4Index.TryFind(record, out _, out _))
                        || !UsedByZone(pcKey))
                        continue;
                    (ulong Key, byte[] Stored)? item = Bundle(pcKey, record);
                    if (item == null)
                        continue;
                    bundled++;
                    bundledBytes += item.Value.Stored.Length;
                    options.Progress?.Invoke(bundled, -1);
                    yield return item.Value;
                }
                foreach (ulong pcKey in options.Plan.Keys.Where(k => !index.ContainsKey(k)).Order())
                {
                    if (pc.TryFind(pcKey, out _) || map.TryGet(pcKey, out _)
                        || (options.KnownItems != null && options.KnownItems.TryGet(pcKey, out T7StreamMap.Item? known) && known.Source != T7StreamMap.Unresolved)
                        || (options.Ps4Index != null && options.Ps4Index.ContainsKey(pcKey)))
                        continue;
                    (ulong Key, byte[] Stored)? item = Bundle(pcKey, null);
                    if (item == null)
                        continue;
                    bundled++;
                    bundledBytes += item.Value.Stored.Length;
                    options.Progress?.Invoke(bundled, -1);
                    yield return item.Value;
                }
                if (bundled > 0)
                    options.Log($"  xpak {Path.GetFileName(options.PcXPak)}: {bundled} referenced items converted in from the PC xpaks ({bundledBytes / 1048576.0:F1} MB)");
            }

            IEnumerable<(ulong Key, string Text)> Catalog()
            {
                foreach ((ulong pcKey, XPakIndexRecord record) in index)
                    yield return Record(pcKey, record);
            }

            XPakWriter.Write(temporary, Entries(), Catalog());
            File.Move(temporary, options.OutputXPak, overwrite: true);
        }

        if (options.PcIndexXPak != null && options.OutputIndexXPak != null && File.Exists(options.PcIndexXPak))
        {
            List<(ulong Key, string Text)> records;
            using (var catalog = new XPak(options.PcIndexXPak))
                records = catalog.Index.Select(r => Record(r.Key, r.Value)).ToList();
            string temporary = options.OutputIndexXPak + ".partial";
            XPakWriter.Write(temporary, [], records);
            File.Move(temporary, options.OutputIndexXPak, overwrite: true);
        }
        return new T7StreamConvertResult(map, counts, warnings);

        (ulong Key, string Text) Record(ulong pcKey, XPakIndexRecord record)
        {
            if (map.TryGet(pcKey, out T7StreamMap.Item? item) && texts.TryGetValue(item.Ps4Key, out string? text))
                return (item.Ps4Key, text);
            return ResolveExternal(record, options.Ps4Index, options.KnownItems, map, texts, warnings, Count,
                options.PcSource != null && options.Zone != null ? UsedByZone : null);
        }
    }

    private static (ulong Key, string Text) ResolveExternal(XPakIndexRecord record, T7Ps4StreamIndex? ps4Index, T7StreamMap? knownItems, T7StreamMap map,
        Dictionary<ulong, string> texts, List<string> warnings, Action<string> count, Func<ulong, bool>? usedByZone = null)
    {
        if (map.TryGet(record.Key, out T7StreamMap.Item? known))
            return (known.Ps4Key, texts.GetValueOrDefault(known.Ps4Key, record.Text));
        if (knownItems != null && knownItems.TryGet(record.Key, out T7StreamMap.Item? sibling) && sibling.Source != T7StreamMap.Unresolved)
        {
            map.Add(sibling);
            count($"{record.Type}: external, another zone of the map");
            IReadOnlyList<long>? sizes = sibling.Ps4PartSizes;
            string siblingText = sizes != null && sizes.Count == record.Parts.Count
                ? record.WithParts(record.Parts.Select((p, i) => (p.Offset, sizes[i])).ToList()) : record.Text;
            texts[sibling.Ps4Key] = siblingText;
            return (sibling.Ps4Key, siblingText);
        }
        if (ps4Index != null && ps4Index.TryFind(record, out ulong key, out string text))
        {
            map.Add(new T7StreamMap.Item(record.Key, key, record.Parts.Sum(p => p.Size), -1, record.Type ?? "?", record.Name, "ps4 index"));
            texts[key] = text;
            count($"{record.Type}: external, PS4 index");
            return (key, text);
        }
        map.Add(new T7StreamMap.Item(record.Key, record.Key, record.Parts.Sum(p => p.Size), -1, record.Type ?? "?", record.Name, T7StreamMap.Unresolved));
        texts[record.Key] = record.Text;
        if (usedByZone != null && !usedByZone(record.Key))
        {
            count($"{record.Type}: external, not used by the zone");
            return (record.Key, record.Text);
        }
        count($"{record.Type}: external, unresolved");
        if (warnings.Count < 5000)
            warnings.Add($"{record.Type} '{record.Name}' part {record.Key >> 61} is stored in another xpak and no PS4 index knows it; the PC key is kept");
        return (record.Key, record.Text);
    }

    public static List<(uint LogicalOffset, byte[] Bytes)> DefaultTransform(XPakIndexRecord? record, List<(uint LogicalOffset, byte[] Bytes)> parts, out string source, List<string> warnings)
    {
        switch (record?.Type)
        {
            case "image":
            {
                string format = record.Fields.GetValueOrDefault("format") ?? "";
                int levels = record.Int("levels", 1);
                int width = record.TrueWidth, height = record.TrueHeight;
                if (parts.Count != 1)
                    throw new InvalidDataException($"image '{record.Name}' has {parts.Count} stored parts; one expected");
                int bytesPerBlock = T7StreamLayout.BytesPerBlock(format);
                if (bytesPerBlock > 0)
                {
                    source = "tiled";
                    return [(parts[0].LogicalOffset, T7StreamLayout.ImagePartToPs4(parts[0].Bytes, bytesPerBlock, width, height, levels))];
                }
                int bytesPerTexel = T7StreamLayout.BytesPerTexel(format);
                if (bytesPerTexel > 0)
                {
                    source = "texel rows";
                    return [(parts[0].LogicalOffset, T7StreamLayout.UncompressedPartToPs4(parts[0].Bytes, bytesPerTexel, width, height, levels))];
                }
                throw new InvalidDataException($"image '{record.Name}' uses format '{format}', which has no PS4 layout rule");
            }
            case "SST":
                source = "unchanged";
                return parts;
            case "mesh":
                source = "unchanged (undescribed)";
                if (warnings.Count < 5000)
                    warnings.Add($"mesh item '{record?.Name}' is described by no asset of this zone; it was copied unchanged");
                return parts;
            default:
                source = "unchanged (no converter)";
                if (warnings.Count < 5000)
                    warnings.Add($"{record?.Type ?? "unindexed"} item '{record?.Name}' was copied unchanged (no PC->PS4 payload rule registered)");
                return parts;
        }
    }

    public static List<(uint LogicalOffset, byte[] Bytes)> SplitByRecord(List<(uint LogicalOffset, byte[] Bytes)> parts, XPakIndexRecord record)
    {
        if (record.Parts.Count <= 1 || parts.Count != 1 || record.Parts.Sum(p => p.Size) != parts[0].Bytes.Length || record.Parts[0].Offset != parts[0].LogicalOffset)
            return parts;
        long at = 0;
        var split = new List<(uint, byte[])>();
        foreach ((long offset, long size) in record.Parts)
        {
            if (offset != record.Parts[0].Offset + at)
                return parts;
            split.Add(((uint)offset, parts[0].Bytes.AsSpan((int)at, (int)size).ToArray()));
            at += size;
        }
        return split;
    }

    private static byte[] Concat(List<(uint LogicalOffset, byte[] Bytes)> parts)
    {
        if (parts.Count == 1)
            return parts[0].Bytes;
        var output = new byte[parts.Sum(p => p.Bytes.Length)];
        int at = 0;
        foreach ((uint _, byte[] bytes) in parts)
        {
            bytes.CopyTo(output, at);
            at += bytes.Length;
        }
        return output;
    }
}
