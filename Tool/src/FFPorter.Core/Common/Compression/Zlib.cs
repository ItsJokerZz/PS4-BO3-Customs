using System.IO.Compression;
using FFPorter.Core.Common.Python;

namespace FFPorter.Core.Common.Compression;

public static class Zlib
{
    public static InflateResult Decompress(ReadOnlySpan<byte> data, long maxLength = 0, long capacityHint = 0) =>
        ZlibInflater.Decompress(data, maxLength, capacityHint);

    public static byte[] Compress(ReadOnlySpan<byte> data, int level = 6)
    {
        var output = new MemoryStream(capacity: (int)Math.Min(Array.MaxLength, data.Length / 2L + 4096));
        CompressTo(output, data, level);
        return output.ToArray();
    }

    public static void CompressTo(Stream destination, ReadOnlySpan<byte> data, int level = 6)
    {
        if (data.IsEmpty)
        {
            destination.Write(EmptyStream(level));
            return;
        }
        using var stream = new ZLibStream(destination, new ZLibCompressionOptions { CompressionLevel = level }, leaveOpen: true);
        stream.WriteInChunks(data);
    }

    public static byte[] EmptyStream(int level)
    {
        if (level == -1)
            level = 6;
        byte flags = level switch
        {
            < 2 => 0x01,
            < 6 => 0x5E,
            6 => 0x9C,
            _ => 0xDA,
        };
        return level == 0
            ? [0x78, flags, 0x01, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x01]
            : [0x78, flags, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01];
    }
}
