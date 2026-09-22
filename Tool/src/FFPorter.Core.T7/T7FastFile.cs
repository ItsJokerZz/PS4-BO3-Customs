using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace FFPorter.Core.T7;

public sealed class T7Header
{
    public const int Size = 0x248;
    public const uint Version = 0x251;
    public const int BlockCount = 10;
    public const int NameOffset = 0xF8;
    public const int SignatureOffset = 0x138;
    public const int BlockSizesOffset = 0xA8;
    public static ReadOnlySpan<byte> Magic => "TAff0000"u8;

    public byte[] Bytes { get; }

    public T7Header(byte[] bytes)
    {
        if (bytes.Length != Size)
            throw new InvalidDataException($"A T7 header is {Size} bytes");
        Bytes = bytes;
    }

    public static T7Header Parse(ReadOnlySpan<byte> file)
    {
        if (file.Length < Size || !file[..8].SequenceEqual(Magic))
            throw new InvalidDataException("Not a Black Ops 3 fastfile (magic TAff0000 expected)");
        var header = new T7Header(file[..Size].ToArray());
        if (header.FileVersion != Version)
            throw new InvalidDataException($"Unsupported T7 fastfile version 0x{header.FileVersion:x} (expected 0x251)");
        return header;
    }

    public uint FileVersion => BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(8));

    public byte ServerFlag { get => Bytes[12]; set => Bytes[12] = value; }

    public byte Compression { get => Bytes[13]; set => Bytes[13] = value; }

    public byte Platform { get => Bytes[14]; set => Bytes[14] = value; }

    public byte Encrypted { get => Bytes[15]; set => Bytes[15] = value; }

    public string PlatformName => Platform switch { 0 => "pc", 1 => "xb1", 2 => "ps4", _ => $"platform{Platform}" };

    public ulong Timestamp { get => U64(0x10); set => SetU64(0x10, value); }

    public uint Changelist { get => U32(0x18); set => SetU32(0x18, value); }

    public byte[] ArchiveChecksum { get => Bytes[0x1C..0x2C]; set => value.AsSpan(0, 16).CopyTo(Bytes.AsSpan(0x1C)); }

    public string Builder
    {
        get => CString(0x2C, 32);
        set => SetCString(0x2C, 32, value);
    }

    public ulong ZoneSize { get => U64(0x90); set => SetU64(0x90, value); }

    public ulong BlockSize(int block) => U64(BlockSizesOffset + 8 * block);

    public void SetBlockSize(int block, ulong size) => SetU64(BlockSizesOffset + 8 * block, size);

    public ulong[] BlockSizes => Enumerable.Range(0, BlockCount).Select(BlockSize).ToArray();

    public string ZoneName
    {
        get => CString(NameOffset, 64);
        set => SetCString(NameOffset, 64, value);
    }

    public Span<byte> Signature => Bytes.AsSpan(SignatureOffset, 256);

    private ulong U64(int offset) => BinaryPrimitives.ReadUInt64LittleEndian(Bytes.AsSpan(offset));
    private uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(offset));
    private void SetU64(int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Bytes.AsSpan(offset), value);
    private void SetU32(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan(offset), value);

    private string CString(int offset, int length)
    {
        ReadOnlySpan<byte> field = Bytes.AsSpan(offset, length);
        int end = field.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? field : field[..end]);
    }

    private void SetCString(int offset, int length, string value)
    {
        byte[] text = Encoding.ASCII.GetBytes(value);
        if (text.Length >= length)
            throw new ArgumentException($"'{value}' does not fit a {length}-byte field");
        Bytes.AsSpan(offset, length).Clear();
        text.CopyTo(Bytes, offset);
    }
}

public static class T7FastFile
{
    public const int ChunkSize = 0x381B0;
    public const int SectionSize = 0x800000;
    public const int BlockHeaderSize = 16;

    public const int LookaheadPadding = 0x80;

    public sealed record Decoded(T7Header Header, byte[] Zone, int Blocks);

    public static Decoded Decode(ReadOnlySpan<byte> file)
    {
        T7Header header = T7Header.Parse(file);
        if (header.Encrypted != 0)
            throw new InvalidDataException("Encrypted T7 fastfiles are not supported");
        if (header.ZoneSize > (ulong)Array.MaxLength)
            throw new InvalidDataException($"Zone of {header.ZoneSize} bytes is too large");
        var zone = new byte[header.ZoneSize];
        long written = 0;
        int position = T7Header.Size;
        int blocks = 0;
        while (true)
        {
            if (position + BlockHeaderSize > file.Length)
                throw new InvalidDataException($"Truncated block chain at 0x{position:x}");
            uint compressed = BinaryPrimitives.ReadUInt32LittleEndian(file[position..]);
            uint decompressed = BinaryPrimitives.ReadUInt32LittleEndian(file[(position + 4)..]);
            uint aligned = BinaryPrimitives.ReadUInt32LittleEndian(file[(position + 8)..]);
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(file[(position + 12)..]);
            if (compressed == 0 && decompressed == 0 && aligned == 0)
                break;
            if (offset != (uint)position)
                throw new InvalidDataException($"Block at 0x{position:x} records offset 0x{offset:x}");
            if (decompressed == 0)
            {
                position = (position + SectionSize - 1) & ~(SectionSize - 1);
                continue;
            }
            if (written + decompressed > zone.Length || position + BlockHeaderSize + (long)compressed > file.Length)
                throw new InvalidDataException($"Block at 0x{position:x} overruns the zone or file");
            ReadOnlySpan<byte> payload = file.Slice(position + BlockHeaderSize, (int)compressed);
            int got = header.Compression switch
            {
                0 => Copy(payload, zone.AsSpan((int)written, (int)decompressed)),
                1 or 2 => Inflate(payload, zone.AsSpan((int)written, (int)decompressed)),
                _ => throw new InvalidDataException($"Unsupported T7 block compression {header.Compression}"),
            };
            if (got != decompressed)
                throw new InvalidDataException($"Block at 0x{position:x} inflated to {got} bytes, expected {decompressed}");
            written += decompressed;
            position += BlockHeaderSize + (int)aligned;
            blocks++;
        }
        if (written != zone.Length)
            throw new InvalidDataException($"Blocks hold {written} bytes; the header declares {zone.Length}");
        return new Decoded(header, zone, blocks);
    }

    private static int Copy(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        source.CopyTo(destination);
        return source.Length;
    }

    private static unsafe int Inflate(ReadOnlySpan<byte> payload, Span<byte> destination)
    {
        fixed (byte* pointer = payload)
        {
            using var input = new UnmanagedMemoryStream(pointer, payload.Length);
            using var inflater = new ZLibStream(input, CompressionMode.Decompress);
            int total = 0;
            while (total < destination.Length)
            {
                int read = inflater.Read(destination[total..]);
                if (read == 0)
                    break;
                total += read;
            }
            if (inflater.ReadByte() >= 0)
                throw new InvalidDataException("A T7 block inflates past its declared size");
            return total;
        }
    }

    public static byte[] Encode(T7Header template, ReadOnlySpan<byte> zone, CompressionLevel level = CompressionLevel.Optimal)
    {
        var header = new T7Header((byte[])template.Bytes.Clone());
        header.ZoneSize = (ulong)zone.Length;
        header.Compression = 1;
        header.Encrypted = 0;
        header.Signature.Clear();

        using var output = new MemoryStream(zone.Length / 2 + T7Header.Size);
        output.Write(header.Bytes);
        var chunk = new MemoryStream(ChunkSize + 1024);
        for (int start = 0; start < zone.Length; start += ChunkSize)
        {
            int length = Math.Min(ChunkSize, zone.Length - start);
            chunk.SetLength(0);
            using (var deflater = new ZLibStream(chunk, level, leaveOpen: true))
                deflater.Write(zone.Slice(start, length));
            int compressed = (int)chunk.Length;
            int aligned = (compressed + 3) & ~3;
            long position = output.Position;
            long sectionEnd = (position + SectionSize - 1) / SectionSize * SectionSize;
            long after = sectionEnd - (position + BlockHeaderSize + aligned);
            if (position % SectionSize != 0 && (after < 0 || after is > 0 and < BlockHeaderSize))
            {
                WriteBlockHeader(output, (uint)(compressed + BlockHeaderSize), 0, (uint)(compressed + BlockHeaderSize), (uint)position);
                long gap = sectionEnd - output.Position;
                if (gap < 0)
                    throw new InvalidOperationException("Section marker overran the section");
                output.Write(Enumerable.Repeat((byte)0xAF, (int)gap).ToArray());
                position = output.Position;
            }
            if (position > uint.MaxValue)
                throw new InvalidDataException("T7 fastfiles are limited to 4 GiB");
            WriteBlockHeader(output, (uint)compressed, (uint)length, (uint)aligned, (uint)position);
            output.Write(chunk.GetBuffer(), 0, compressed);
            for (int pad = compressed; pad < aligned; pad++)
                output.WriteByte(0);
        }
        output.Write(new byte[BlockHeaderSize + LookaheadPadding]);
        while (output.Length % 0x80 != 0)
            output.WriteByte(0);
        return output.ToArray();
    }

    private static void WriteBlockHeader(Stream output, uint compressed, uint decompressed, uint aligned, uint offset)
    {
        Span<byte> record = stackalloc byte[BlockHeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(record, compressed);
        BinaryPrimitives.WriteUInt32LittleEndian(record[4..], decompressed);
        BinaryPrimitives.WriteUInt32LittleEndian(record[8..], aligned);
        BinaryPrimitives.WriteUInt32LittleEndian(record[12..], offset);
        output.Write(record);
    }

    public static Decoded Load(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        return Decode(data);
    }
}
