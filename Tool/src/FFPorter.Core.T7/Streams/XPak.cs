using System.Buffers.Binary;
using System.Text;

namespace FFPorter.Core.T7.Streams;

public sealed class XPak : IDisposable
{
    public const int HeaderSize = 0x78;
    public const int BlockHeaderSize = 0x80;
    public const int MaxCommands = 30;
    public const byte CommandRaw = 0x00;
    public const byte CommandLz4 = 0x03;
    public const byte CommandSkip = 0xCF;
    public const int TagWrapped = 7;
    public static ReadOnlySpan<byte> Magic => "KAPI"u8;

    public readonly record struct Section(ulong Count, ulong Offset, ulong Size);
    public readonly record struct HashEntry(ulong Key, ulong Offset, ulong Size);

    private readonly FileStream _file;

    public string Path { get; }
    public ulong FileSize { get; }
    public Section Data { get; }
    public Section HashTable { get; }
    public Section IndexKeys { get; }
    public Section IndexData { get; }
    public IReadOnlyList<HashEntry> Entries { get; }
    private readonly ulong[] _keys;
    private Dictionary<ulong, XPakIndexRecord>? _index;

    public XPak(string path)
    {
        Path = path;
        _file = File.OpenRead(path);
        var header = new byte[HeaderSize];
        _file.ReadExactly(header);
        if (!header.AsSpan(0, 4).SequenceEqual(Magic))
            throw new InvalidDataException($"{path} is not an xpak (KAPI)");
        FileSize = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(0x10));
        Section ReadSection(int at) => new(BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(at)),
            BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(at + 8)), BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(at + 16)));
        Data = ReadSection(0x18);
        HashTable = ReadSection(0x30);
        IndexKeys = ReadSection(0x48);
        IndexData = ReadSection(0x60);
        if (HashTable.Count * 24 != HashTable.Size)
            throw new InvalidDataException($"{path}: hash table size mismatch");
        var raw = new byte[HashTable.Size];
        _file.Position = (long)HashTable.Offset;
        _file.ReadExactly(raw);
        var entries = new HashEntry[HashTable.Count];
        for (int i = 0; i < entries.Length; i++)
            entries[i] = new HashEntry(BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(i * 24)), BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(i * 24 + 8)), BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(i * 24 + 16)));
        Entries = entries;
        _keys = entries.Select(e => e.Key).ToArray();
    }

    public bool TryFind(ulong key, out HashEntry entry)
    {
        int i = Array.BinarySearch(_keys, key);
        entry = i >= 0 ? Entries[i] : default;
        return i >= 0;
    }

    public IReadOnlyDictionary<ulong, XPakIndexRecord> Index
    {
        get
        {
            if (_index != null)
                return _index;
            var index = new Dictionary<ulong, XPakIndexRecord>();
            var raw = new byte[IndexData.Size];
            _file.Position = (long)IndexData.Offset;
            _file.ReadExactly(raw);
            for (int at = 0; at + 16 <= raw.Length;)
            {
                ulong key = BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(at));
                int length = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(at + 8)));
                index[key] = new XPakIndexRecord(key, Encoding.Latin1.GetString(raw, at + 16, length));
                at += 16 + length;
            }
            return _index = index;
        }
    }

    public List<(uint LogicalOffset, byte[] Bytes)> ReadParts(HashEntry entry)
    {
        var runs = new List<(uint, MemoryStream)>();
        long position = (long)(Data.Offset + entry.Offset);
        long end = position + (long)entry.Size;
        var header = new byte[BlockHeaderSize];
        while (position < end)
        {
            _file.Position = position;
            _file.ReadExactly(header);
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(header);
            uint logical = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
            if (count == 0 || count > MaxCommands)
                throw new InvalidDataException($"{Path}: bad block header at 0x{position:x}");
            position += BlockHeaderSize;
            for (int c = 0; c < count; c++)
            {
                uint command = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8 + 4 * c));
                int type = (int)(command >> 24);
                int length = (int)(command & 0xFFFFFF);
                if (type == CommandSkip)
                {
                    position += length;
                    continue;
                }
                if (type is not (CommandRaw or CommandLz4))
                    throw new InvalidDataException($"{Path}: unsupported xpak command 0x{type:x2} at 0x{position:x}");
                var bytes = new byte[length];
                _file.Position = position;
                _file.ReadExactly(bytes);
                position += length;
                if (type == CommandLz4)
                    bytes = Common.Compression.Lz4Block.DecompressUnsized(bytes, Path, position - length);
                if (runs.Count == 0 || runs[^1].Item1 + runs[^1].Item2.Length != logical)
                    runs.Add((logical, new MemoryStream()));
                runs[^1].Item2.Write(bytes);
                logical += (uint)bytes.Length;
            }
            if (position < end)
                position = (position + BlockHeaderSize - 1) & ~(long)(BlockHeaderSize - 1);
        }
        if (position != end)
            throw new InvalidDataException($"{Path}: block chain overruns entry {entry.Key:x16}");
        return runs.Select(r => (r.Item1, r.Item2.ToArray())).ToList();
    }

    public byte[] ReadPayload(HashEntry entry)
    {
        List<(uint LogicalOffset, byte[] Bytes)> parts = ReadParts(entry);
        var output = new byte[parts.Sum(p => p.Bytes.Length)];
        int at = 0;
        foreach ((uint _, byte[] bytes) in parts)
        {
            bytes.CopyTo(output, at);
            at += bytes.Length;
        }
        return output;
    }

    public static ulong ComputeKey(ReadOnlySpan<byte> payload, int tag) => (XxHash64.Hash(payload) & 0x1FFFFFFFFFFFFFFFUL) | ((ulong)tag << 61);

    public static ulong ComputeKey(IEnumerable<byte[]> parts, int tag)
    {
        ulong hash = 0;
        foreach (byte[] part in parts)
            hash = XxHash64.Hash(part, hash);
        return (hash & 0x1FFFFFFFFFFFFFFFUL) | ((ulong)tag << 61);
    }

    public void Dispose() => _file.Dispose();
}

public sealed class XPakIndexRecord
{
    public ulong Key { get; }
    public string Text { get; }
    public Dictionary<string, string> Fields { get; } = new(StringComparer.Ordinal);
    public List<(long Offset, long Size)> Parts { get; } = [];

    public XPakIndexRecord(ulong key, string text)
    {
        Key = key;
        Text = text;
        var parts = new SortedDictionary<int, (long Offset, long Size)>();
        foreach (string line in text.Split('\n'))
        {
            int colon = line.IndexOf(": ", StringComparison.Ordinal);
            if (colon < 0)
                continue;
            string field = line[..colon], value = line[(colon + 2)..];
            Fields.TryAdd(field, value);
            if (field.StartsWith("offset", StringComparison.Ordinal) && int.TryParse(field.AsSpan(6), out int n) && long.TryParse(value, out long offset))
                parts[n] = (offset, parts.GetValueOrDefault(n).Size);
            else if (field.StartsWith("size", StringComparison.Ordinal) && int.TryParse(field.AsSpan(4), out n) && long.TryParse(value, out long size))
                parts[n] = (parts.GetValueOrDefault(n).Offset, size);
        }
        Parts.AddRange(parts.Values);
    }

    public string? Name => Fields.GetValueOrDefault("name");
    public string? Type => Fields.GetValueOrDefault("type");
    public int Int(string field, int fallback = 0) => Fields.TryGetValue(field, out string? v) && int.TryParse(v, out int n) ? n : fallback;

    public int TrueWidth => Int("height");
    public int TrueHeight => Int("width");

    public string WithParts(IReadOnlyList<(long Offset, long Size)> parts)
    {
        var lines = new List<string>();
        int written = -1;
        foreach (string line in Text.Split('\n'))
        {
            int colon = line.IndexOf(": ", StringComparison.Ordinal);
            string field = colon < 0 ? "" : line[..colon];
            bool offsetField = field.StartsWith("offset", StringComparison.Ordinal) && int.TryParse(field.AsSpan(6), out _);
            bool sizeField = field.StartsWith("size", StringComparison.Ordinal) && int.TryParse(field.AsSpan(4), out _);
            if (offsetField || sizeField)
            {
                if (written < 0)
                {
                    for (int i = 0; i < parts.Count; i++)
                    {
                        lines.Add($"offset{i}: {parts[i].Offset}");
                        lines.Add($"size{i}: {parts[i].Size}");
                    }
                    written = parts.Count;
                }
                continue;
            }
            lines.Add(line);
        }
        return string.Join("\n", lines);
    }
}

public static class XPakWriter
{
    public const int SectionAlign = 0x8000;
    private const int BigEntryThreshold = 0x38000;
    private const int CommandsPerBlock = 8;
    private const int MaxCommandLength = 0x7FF0;

    public static byte[] BuildEntry(IReadOnlyList<(uint LogicalOffset, byte[] Bytes)> parts)
    {
        using var output = new MemoryStream();
        long partStart = 0;
        for (int p = 0; p < parts.Count; p++)
        {
            if (p > 0)
            {
                long pad = Align(output.Length + XPak.BlockHeaderSize, 0x40000) - output.Length - XPak.BlockHeaderSize;
                var skip = new byte[XPak.BlockHeaderSize];
                BinaryPrimitives.WriteUInt32LittleEndian(skip, 1);
                BinaryPrimitives.WriteUInt32LittleEndian(skip.AsSpan(4), (uint)(output.Length - partStart));
                BinaryPrimitives.WriteUInt32LittleEndian(skip.AsSpan(8), ((uint)XPak.CommandSkip << 24) | (uint)pad);
                output.Write(skip);
                output.Write(Enumerable.Repeat((byte)0xCD, (int)pad).ToArray());
            }
            partStart = output.Length;
            (uint logical, byte[] bytes) = parts[p];
            for (int at = 0; at < bytes.Length;)
            {
                var lengths = new List<int>();
                int cursor = at;
                while (cursor < bytes.Length && lengths.Count < CommandsPerBlock)
                {
                    int length = Math.Min(MaxCommandLength, bytes.Length - cursor);
                    lengths.Add(length);
                    cursor += length;
                }
                var header = new byte[XPak.BlockHeaderSize];
                BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)lengths.Count);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), logical + (uint)at);
                for (int c = 0; c < lengths.Count; c++)
                    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8 + 4 * c), (uint)lengths[c]);
                output.Write(header);
                output.Write(bytes, at, cursor - at);
                at = cursor;
            }
        }
        return output.ToArray();
    }

    public static void Write(string path, IEnumerable<(ulong Key, byte[] Stored)> entries, IEnumerable<(ulong Key, string Text)> index)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        file.Write(new byte[0x200]);
        file.Write(Enumerable.Repeat((byte)0xA7, SectionAlign - 0x200).ToArray());
        var table = new List<(ulong Key, ulong Offset, ulong Size)>();
        long position = 0;
        foreach ((ulong key, byte[] stored) in entries)
        {
            long offset = Align(position, stored.Length >= BigEntryThreshold ? 0x8000 : 0x80);
            if (offset > position)
                file.Write(Enumerable.Repeat((byte)0x93, (int)(offset - position)).ToArray());
            file.Write(stored);
            table.Add((key, (ulong)offset, (ulong)stored.Length));
            position = offset + stored.Length;
        }
        var data = new XPak.Section((ulong)table.Count, SectionAlign, (ulong)position);
        long cursor = PadTo(file, SectionAlign + position);
        table.Sort((a, b) => a.Key.CompareTo(b.Key));
        var raw = new byte[24];
        foreach ((ulong key, ulong offset, ulong size) in table)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(raw, key);
            BinaryPrimitives.WriteUInt64LittleEndian(raw.AsSpan(8), offset);
            BinaryPrimitives.WriteUInt64LittleEndian(raw.AsSpan(16), size);
            file.Write(raw);
        }
        var hashes = new XPak.Section((ulong)table.Count, (ulong)cursor, (ulong)table.Count * 24);
        cursor = PadTo(file, cursor + (long)hashes.Size);
        var records = index.OrderBy(r => r.Key).ToList();
        foreach ((ulong key, string _) in records)
            file.Write(BitConverter.GetBytes(key));
        var keys = new XPak.Section((ulong)records.Count, (ulong)cursor, (ulong)records.Count * 8);
        cursor = PadTo(file, cursor + (long)keys.Size);
        long blobStart = cursor;
        foreach ((ulong key, string text) in records)
        {
            byte[] bytes = Encoding.Latin1.GetBytes(text);
            file.Write(BitConverter.GetBytes(key));
            file.Write(BitConverter.GetBytes((ulong)bytes.Length));
            file.Write(bytes);
            cursor += 16 + bytes.Length;
        }
        var indexData = new XPak.Section((ulong)records.Count, (ulong)blobStart, (ulong)(cursor - blobStart));
        long end = PadTo(file, cursor);
        var header = new byte[XPak.HeaderSize];
        XPak.Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), 10);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(0x10), (ulong)end);
        int at = 0x18;
        foreach (XPak.Section section in new[] { data, hashes, keys, indexData })
        {
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(at), section.Count);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(at + 8), section.Offset);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(at + 16), section.Size);
            at += 24;
        }
        file.Position = 0;
        file.Write(header);
    }

    private static long PadTo(Stream file, long current)
    {
        long next = Align(current, SectionAlign);
        if (next > current)
            file.Write(Enumerable.Repeat((byte)0xA7, (int)(next - current)).ToArray());
        return next;
    }

    public static long Align(long value, long alignment) => (value + alignment - 1) & ~(alignment - 1);
}

public static class T7StreamLayout
{
    public static int BytesPerBlock(string format) => format switch
    {
        "BC1" or "BC1_SRGB" or "BC4" => 8,
        "BC2" or "BC2_SRGB" or "BC3" or "BC3_SRGB" or "BC5" or "BC6H" or "BC6H_SF16" or "BC6H_UF16" or "BC7" or "BC7_SRGB" => 16,
        _ => 0,
    };

    private static int Morton8(int x, int y)
    {
        int m = 0;
        for (int b = 0; b < 3; b++)
        {
            m |= ((x >> b) & 1) << (2 * b);
            m |= ((y >> b) & 1) << (2 * b + 1);
        }
        return m;
    }

    public static byte[] TileSurface(ReadOnlySpan<byte> data, int blocksWide, int blocksHigh, int bytesPerBlock)
    {
        int paddedWide = (blocksWide + 7) & ~7, paddedHigh = (blocksHigh + 7) & ~7;
        var output = new byte[paddedWide * paddedHigh * bytesPerBlock];
        for (int y = 0; y < blocksHigh; y++)
        {
            for (int x = 0; x < blocksWide; x++)
            {
                int destination = ((y >> 3) * (paddedWide / 8) + (x >> 3)) * 64 + Morton8(x & 7, y & 7);
                data.Slice((y * blocksWide + x) * bytesPerBlock, bytesPerBlock).CopyTo(output.AsSpan(destination * bytesPerBlock));
            }
        }
        return output;
    }

    public static byte[] ImagePartToPs4(ReadOnlySpan<byte> data, int bytesPerBlock, int width, int height, int levels)
    {
        using var output = new MemoryStream();
        int offset = 0;
        for (int level = 0; level < levels; level++)
        {
            int w = Math.Max(1, width >> level), h = Math.Max(1, height >> level);
            int bw = (w + 3) / 4, bh = (h + 3) / 4;
            int size = bw * bh * bytesPerBlock;
            output.Write(TileSurface(data.Slice(offset, size), bw, bh, bytesPerBlock));
            offset += size;
        }
        return output.ToArray();
    }

    public static int BytesPerTexel(string format) => format switch
    {
        "R8G8B8A8" or "R8G8B8A8_SRGB" or "B8G8R8A8" or "B8G8R8A8_SRGB" or "R10G10B10A2" or "R11G11B10_FLOAT" or "R32_FLOAT" or "R16G16_FLOAT" => 4,
        "R16G16B16A16" or "R16G16B16A16_FLOAT" or "R32G32_FLOAT" => 8,
        "R8" or "A8" => 1,
        "R16" or "R16_FLOAT" or "R8G8" => 2,
        _ => 0,
    };

    public static byte[] UncompressedPartToPs4(ReadOnlySpan<byte> data, int bytesPerTexel, int width, int height, int levels)
    {
        using var output = new MemoryStream();
        int offset = 0;
        for (int level = 0; level < levels; level++)
        {
            int w = Math.Max(1, width >> level), h = Math.Max(1, height >> level);
            int stride = (w + 15) & ~15, rows = (h + 3) & ~3;
            var region = new byte[stride * rows * bytesPerTexel];
            int rowStep = rows / h;
            for (int y = 0; y < h; y++)
                data.Slice(offset + y * w * bytesPerTexel, w * bytesPerTexel).CopyTo(region.AsSpan(y * rowStep * stride * bytesPerTexel));
            output.Write(region);
            offset += w * h * bytesPerTexel;
        }
        return output.ToArray();
    }

    public static long Ps4ImagePartSize(int bytesPerBlock, int width, int height, int levels)
    {
        long total = 0;
        for (int level = 0; level < levels; level++)
        {
            int bw = (Math.Max(1, width >> level) + 3) / 4, bh = (Math.Max(1, height >> level) + 3) / 4;
            total += (long)((bw + 7) & ~7) * ((bh + 7) & ~7) * bytesPerBlock;
        }
        return total;
    }

    public static uint PackedUnitVectorToPs4(uint value)
    {
        uint output = value & 0xC0000000;
        for (int shift = 0; shift < 30; shift += 10)
            output |= ((((value >> shift) & 0x3FF) - 512) & 0x3FF) << shift;
        return output;
    }
}
