using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace FFPorter.Core.T7;

public static class T7FastFileDelta
{
    public const int BaseHeaderOffset = T7Header.Size;
    public const int RecordOffset = 2 * T7Header.Size;
    private const int RecordSize = 0x28;

    private enum Op : byte { Noop, Add, Run, Copy }

    private readonly record struct Half(Op Type, byte Size, byte Mode);

    private static readonly (Half First, Half Second)[] CodeTable = BuildCodeTable();

    public static string? FindFor(string fastFile)
    {
        string delta = Path.ChangeExtension(fastFile, ".fd");
        if (!File.Exists(delta))
            return null;
        byte[] header = ReadBytes(fastFile, 0, T7Header.Size);
        byte[] baseHeader = ReadBytes(delta, BaseHeaderOffset, T7Header.Size);
        return header.Length == T7Header.Size && header.AsSpan().SequenceEqual(baseHeader) ? delta : null;
    }

    public static string CacheKey(string fastFile)
    {
        var info = new FileInfo(fastFile);
        var text = new StringBuilder($"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
        var delta = new FileInfo(Path.ChangeExtension(fastFile, ".fd"));
        if (delta.Exists)
            text.Append($"|fd|{delta.Length}|{delta.LastWriteTimeUtc.Ticks}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    public static T7FastFile.Decoded LoadPatched(string fastFile)
    {
        T7FastFile.Decoded decoded = T7FastFile.Load(fastFile);
        string? delta = FindFor(fastFile);
        if (delta == null)
            return decoded;
        (T7Header header, byte[] zone) = Apply(decoded.Zone, File.ReadAllBytes(delta));
        return new T7FastFile.Decoded(header, zone, decoded.Blocks);
    }

    public static string Resolve(string fastFile, string cacheDirectory, Action<string>? log = null)
    {
        string? delta = FindFor(fastFile);
        if (delta == null)
            return fastFile;
        string folder = Path.Combine(cacheDirectory, "fd", CacheKey(fastFile));
        string patched = Path.Combine(folder, Path.GetFileName(fastFile));
        if (File.Exists(patched))
            return patched;
        log?.Invoke($"applying {Path.GetFileName(delta)} to {Path.GetFileName(fastFile)}");
        T7FastFile.Decoded decoded = T7FastFile.Load(fastFile);
        (T7Header header, byte[] zone) = Apply(decoded.Zone, File.ReadAllBytes(delta));
        Directory.CreateDirectory(folder);
        string temporary = patched + ".tmp";
        File.WriteAllBytes(temporary, T7FastFile.Encode(header, zone, CompressionLevel.Fastest));
        File.Move(temporary, patched, overwrite: true);
        return patched;
    }

    public static (T7Header Header, byte[] Zone) Apply(ReadOnlySpan<byte> baseZone, ReadOnlySpan<byte> fd)
    {
        if (fd.Length < RecordOffset + RecordSize)
            throw new InvalidDataException("A .fd is at least 0x4B8 bytes");
        var header = T7Header.Parse(fd[..T7Header.Size]);
        ulong deltaOffset = BinaryPrimitives.ReadUInt64LittleEndian(fd[RecordOffset..]);
        if (deltaOffset < (ulong)(RecordOffset + RecordSize) || deltaOffset >= (ulong)fd.Length)
            throw new InvalidDataException($".fd delta offset 0x{deltaOffset:x} is outside the file");
        if (header.ZoneSize > (ulong)Array.MaxLength)
            throw new InvalidDataException($"patched zone of {header.ZoneSize} bytes is too large");
        byte[] delta = Inflate(fd[(int)deltaOffset..]);
        var zone = new byte[header.ZoneSize];
        long written = Decode(delta, baseZone, zone);
        if (written != zone.Length)
            throw new InvalidDataException($".fd produced {written} bytes; the patched header declares {zone.Length}");
        return (header, zone);
    }

    private static long Decode(ReadOnlySpan<byte> delta, ReadOnlySpan<byte> source, Span<byte> output)
    {
        if (delta.Length < 5 || !delta[..4].SequenceEqual((ReadOnlySpan<byte>)[0xD6, 0xC3, 0xC4, 0x00]))
            throw new InvalidDataException(".fd delta is not VCDIFF");
        if ((delta[4] & 0x03) != 0)
            throw new InvalidDataException($".fd delta header indicator 0x{delta[4]:x2} asks for secondary compression or a custom code table");
        int p = 5;
        long produced = 0;
        int windows = 0;
        Span<long> near = stackalloc long[4];
        var same = new long[768];
        while (p < delta.Length)
        {
            byte indicator = delta[p++];
            int segmentMode = indicator & 3;
            long segmentSize = 0, segmentPosition = 0;
            if (segmentMode == 3)
                throw new InvalidDataException($".fd window {windows} has both source and target segments");
            if (segmentMode != 0)
            {
                segmentSize = Varint(delta, ref p);
                segmentPosition = Varint(delta, ref p);
                if (segmentMode == 1 && (indicator & 4) != 0)
                    Varint(delta, ref p);
            }
            long deltaLength = Varint(delta, ref p);
            int deltaStart = p;
            long targetLength = Varint(delta, ref p);
            if (delta[p++] != 0)
                throw new InvalidDataException($".fd window {windows} compresses its sections");
            int dataLength = checked((int)Varint(delta, ref p));
            int instructionLength = checked((int)Varint(delta, ref p));
            int addressLength = checked((int)Varint(delta, ref p));
            int data = p, instructions = data + dataLength, addresses = instructions + instructionLength;
            int dataEnd = instructions, instructionsEnd = addresses, addressesEnd = addresses + addressLength;
            if ((long)addressesEnd != deltaStart + deltaLength || addressesEnd > delta.Length)
                throw new InvalidDataException($".fd window {windows} sections do not add up");
            if (produced + targetLength > output.Length)
                throw new InvalidDataException($".fd window {windows} writes past the patched zone");

            ReadOnlySpan<byte> segment;
            if (segmentMode == 1)
            {
                if (segmentPosition + segmentSize > source.Length)
                    throw new InvalidDataException($".fd window {windows} copies past the base zone");
                segment = source.Slice((int)segmentPosition, (int)segmentSize);
            }
            else if (segmentMode == 2)
            {
                if (segmentPosition + segmentSize > produced)
                    throw new InvalidDataException($".fd window {windows} copies from target data not produced yet");
                segment = output.Slice((int)segmentPosition, (int)segmentSize);
            }
            else
            {
                segment = [];
            }

            Span<byte> target = output.Slice((int)produced, (int)targetLength);
            int t = 0;
            near.Clear();
            Array.Clear(same);
            int nextNear = 0;
            while (instructions < instructionsEnd)
            {
                (Half first, Half second) = CodeTable[delta[instructions++]];
                foreach (Half half in (ReadOnlySpan<Half>)[first, second])
                {
                    if (half.Type == Op.Noop)
                        continue;
                    int size = half.Size != 0 ? half.Size : checked((int)Varint(delta, ref instructions));
                    if (t + size > target.Length)
                        throw new InvalidDataException($".fd window {windows} overruns its target");
                    if (half.Type != Op.Copy && data + (half.Type == Op.Add ? size : 1) > dataEnd)
                        throw new InvalidDataException($".fd window {windows} reads past its data section");
                    switch (half.Type)
                    {
                        case Op.Add:
                            delta.Slice(data, size).CopyTo(target[t..]);
                            data += size;
                            break;
                        case Op.Run:
                            target.Slice(t, size).Fill(delta[data++]);
                            break;
                        default:
                        {
                            long here = segmentSize + t, address;
                            switch (half.Mode)
                            {
                                case 0:
                                    address = Varint(delta, ref addresses);
                                    break;
                                case 1:
                                    address = here - Varint(delta, ref addresses);
                                    break;
                                case < 6:
                                    address = near[half.Mode - 2] + Varint(delta, ref addresses);
                                    break;
                                default:
                                    address = same[(half.Mode - 6) * 256 + delta[addresses++]];
                                    break;
                            }
                            if (addresses > addressesEnd || address < 0)
                                throw new InvalidDataException($".fd window {windows} has a bad copy address");
                            near[nextNear] = address;
                            nextNear = (nextNear + 1) & 3;
                            same[address % 768] = address;
                            if (address < segmentSize)
                            {
                                if (address + size > segmentSize)
                                    throw new InvalidDataException($".fd window {windows} copy spans the source segment end");
                                segment.Slice((int)address, size).CopyTo(target[t..]);
                            }
                            else
                            {
                                int from = (int)(address - segmentSize);
                                if (from >= t)
                                    throw new InvalidDataException($".fd window {windows} copies target bytes not decoded yet");
                                if (from + size <= t)
                                {
                                    target.Slice(from, size).CopyTo(target[t..]);
                                }
                                else
                                {
                                    for (int k = 0; k < size; k++)
                                        target[t + k] = target[from + k];
                                }
                            }
                            break;
                        }
                    }
                    t += size;
                }
            }
            if (t != target.Length)
                throw new InvalidDataException($".fd window {windows} decoded {t} of {target.Length} bytes");
            p = addressesEnd;
            if ((indicator & 8) != 0)
            {
                uint expected = BinaryPrimitives.ReadUInt32LittleEndian(delta[p..]);
                p += 4;
                if (XxHash32(target) != expected)
                    throw new InvalidDataException($".fd window {windows} checksum mismatch (the base zone is not the one the patch was made for)");
            }
            produced += targetLength;
            windows++;
        }
        return produced;
    }

    private static long Varint(ReadOnlySpan<byte> data, ref int position)
    {
        long value = 0;
        for (int i = 0; i < 10; i++)
        {
            byte c = data[position++];
            value = (value << 7) | (uint)(c & 0x7F);
            if (c < 0x80)
                return value;
        }
        throw new InvalidDataException(".fd varint is too long");
    }

    private static (Half, Half)[] BuildCodeTable()
    {
        var table = new List<(Half, Half)> { (new Half(Op.Run, 0, 0), default) };
        foreach (int size in (int[])[0, .. Enumerable.Range(1, 17)])
            table.Add((new Half(Op.Add, (byte)size, 0), default));
        for (int mode = 0; mode < 9; mode++)
            foreach (int size in (int[])[0, .. Enumerable.Range(4, 15)])
                table.Add((new Half(Op.Copy, (byte)size, (byte)mode), default));
        for (int mode = 0; mode < 6; mode++)
            for (int add = 1; add <= 4; add++)
                for (int copy = 4; copy <= 6; copy++)
                    table.Add((new Half(Op.Add, (byte)add, 0), new Half(Op.Copy, (byte)copy, (byte)mode)));
        for (int mode = 6; mode < 9; mode++)
            for (int add = 1; add <= 4; add++)
                table.Add((new Half(Op.Add, (byte)add, 0), new Half(Op.Copy, 4, (byte)mode)));
        for (int mode = 0; mode < 9; mode++)
            table.Add((new Half(Op.Copy, 4, (byte)mode), new Half(Op.Add, 1, 0)));
        if (table.Count != 256)
            throw new InvalidOperationException($"VCDIFF code table has {table.Count} entries");
        return table.ToArray();
    }

    private static uint XxHash32(ReadOnlySpan<byte> data)
    {
        const uint P1 = 2654435761U, P2 = 2246822519U, P3 = 3266489917U, P4 = 668265263U, P5 = 374761393U;
        int length = data.Length, i = 0;
        uint h;
        if (length >= 16)
        {
            uint v1 = unchecked(P1 + P2), v2 = P2, v3 = 0, v4 = unchecked(0 - P1);
            for (; i + 16 <= length; i += 16)
            {
                v1 = BitOperations.RotateLeft(v1 + BinaryPrimitives.ReadUInt32LittleEndian(data[i..]) * P2, 13) * P1;
                v2 = BitOperations.RotateLeft(v2 + BinaryPrimitives.ReadUInt32LittleEndian(data[(i + 4)..]) * P2, 13) * P1;
                v3 = BitOperations.RotateLeft(v3 + BinaryPrimitives.ReadUInt32LittleEndian(data[(i + 8)..]) * P2, 13) * P1;
                v4 = BitOperations.RotateLeft(v4 + BinaryPrimitives.ReadUInt32LittleEndian(data[(i + 12)..]) * P2, 13) * P1;
            }
            h = BitOperations.RotateLeft(v1, 1) + BitOperations.RotateLeft(v2, 7) + BitOperations.RotateLeft(v3, 12) + BitOperations.RotateLeft(v4, 18);
        }
        else
        {
            h = P5;
        }
        h += (uint)length;
        for (; i + 4 <= length; i += 4)
            h = BitOperations.RotateLeft(h + BinaryPrimitives.ReadUInt32LittleEndian(data[i..]) * P3, 17) * P4;
        for (; i < length; i++)
            h = BitOperations.RotateLeft(h + data[i] * P5, 11) * P1;
        h ^= h >> 15;
        h *= P2;
        h ^= h >> 13;
        h *= P3;
        h ^= h >> 16;
        return h;
    }

    private static byte[] Inflate(ReadOnlySpan<byte> compressed)
    {
        using var input = new MemoryStream(compressed.ToArray());
        using var inflater = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(compressed.Length * 4);
        inflater.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] ReadBytes(string path, long offset, int count)
    {
        using FileStream stream = File.OpenRead(path);
        if (stream.Length < offset + count)
            return [];
        stream.Position = offset;
        var buffer = new byte[count];
        stream.ReadExactly(buffer);
        return buffer;
    }
}
