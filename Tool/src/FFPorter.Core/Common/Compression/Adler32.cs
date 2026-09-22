namespace FFPorter.Core.Common.Compression;

public static class Adler32
{
    private const uint Base = 65521;
    private const int NMax = 5552;

    public static uint Compute(ReadOnlySpan<byte> data, uint adler = 1)
    {
        uint a = adler & 0xFFFF;
        uint b = adler >> 16;
        while (data.Length > 0)
        {
            int n = Math.Min(NMax, data.Length);
            ReadOnlySpan<byte> block = data[..n];
            int i = 0;
            for (; i + 8 <= block.Length; i += 8)
            {
                a += block[i]; b += a;
                a += block[i + 1]; b += a;
                a += block[i + 2]; b += a;
                a += block[i + 3]; b += a;
                a += block[i + 4]; b += a;
                a += block[i + 5]; b += a;
                a += block[i + 6]; b += a;
                a += block[i + 7]; b += a;
            }
            for (; i < block.Length; i++)
            {
                a += block[i];
                b += a;
            }
            a %= Base;
            b %= Base;
            data = data[n..];
        }
        return (b << 16) | a;
    }
}
