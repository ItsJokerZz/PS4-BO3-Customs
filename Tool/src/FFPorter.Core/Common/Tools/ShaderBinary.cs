using System.Buffers.Binary;

namespace FFPorter.Core.Common.Tools;

public static class ShaderBinary
{
    public enum Problem
    {
        None,
        NoShdrHeader,
        TruncatedHeader,
        TruncatedCode,
    }

    public static long CodeSize(ReadOnlySpan<byte> header) =>
        (BinaryPrimitives.ReadUInt32LittleEndian(header) & 0x7FFFFF) + 16L * BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);

    public static bool TrySplit(ReadOnlySpan<byte> sb, out byte[] header, out byte[] code, out Problem problem)
    {
        header = [];
        code = [];
        int marker = sb.IndexOf("Shdr"u8);
        if (marker < 0)
        {
            problem = Problem.NoShdrHeader;
            return false;
        }
        if (marker + 16 > sb.Length || marker + 16 + sb[marker + 9] * 4 > sb.Length)
        {
            problem = Problem.TruncatedHeader;
            return false;
        }
        int headerSize = sb[marker + 9] * 4;
        ReadOnlySpan<byte> gnmx = sb.Slice(marker + 16, headerSize);
        long codeSize = headerSize >= 8 ? CodeSize(gnmx) : long.MaxValue;
        if (marker + 16L + headerSize + codeSize > sb.Length)
        {
            header = gnmx.ToArray();
            problem = Problem.TruncatedCode;
            return false;
        }
        header = gnmx.ToArray();
        code = sb.Slice(marker + 16 + headerSize, (int)codeSize).ToArray();
        problem = Problem.None;
        return true;
    }
}
