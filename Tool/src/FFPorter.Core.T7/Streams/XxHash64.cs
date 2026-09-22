using System.Buffers.Binary;
using System.Numerics;

namespace FFPorter.Core.T7.Streams;

public static class XxHash64
{
    private const ulong P1 = 11400714785074694791UL, P2 = 14029467366897019727UL, P3 = 1609587929392839161UL,
        P4 = 9650029242287828579UL, P5 = 2870177450012600261UL;

    public static ulong Hash(ReadOnlySpan<byte> data, ulong seed = 0)
    {
        int length = data.Length, i = 0;
        ulong h;
        if (length >= 32)
        {
            ulong v1 = seed + P1 + P2, v2 = seed + P2, v3 = seed, v4 = seed - P1;
            for (; i + 32 <= length; i += 32)
            {
                v1 = Round(v1, BinaryPrimitives.ReadUInt64LittleEndian(data[i..]));
                v2 = Round(v2, BinaryPrimitives.ReadUInt64LittleEndian(data[(i + 8)..]));
                v3 = Round(v3, BinaryPrimitives.ReadUInt64LittleEndian(data[(i + 16)..]));
                v4 = Round(v4, BinaryPrimitives.ReadUInt64LittleEndian(data[(i + 24)..]));
            }
            h = BitOperations.RotateLeft(v1, 1) + BitOperations.RotateLeft(v2, 7) + BitOperations.RotateLeft(v3, 12) + BitOperations.RotateLeft(v4, 18);
            h = Merge(Merge(Merge(Merge(h, v1), v2), v3), v4);
        }
        else
        {
            h = seed + P5;
        }
        h += (ulong)length;
        for (; i + 8 <= length; i += 8)
            h = BitOperations.RotateLeft(h ^ Round(0, BinaryPrimitives.ReadUInt64LittleEndian(data[i..])), 27) * P1 + P4;
        if (i + 4 <= length)
        {
            h = BitOperations.RotateLeft(h ^ (BinaryPrimitives.ReadUInt32LittleEndian(data[i..]) * P1), 23) * P2 + P3;
            i += 4;
        }
        for (; i < length; i++)
            h = BitOperations.RotateLeft(h ^ (data[i] * P5), 11) * P1;
        h ^= h >> 33;
        h *= P2;
        h ^= h >> 29;
        h *= P3;
        h ^= h >> 32;
        return h;
    }

    private static ulong Round(ulong accumulator, ulong input) => BitOperations.RotateLeft(accumulator + input * P2, 31) * P1;

    private static ulong Merge(ulong accumulator, ulong value) => (accumulator ^ Round(0, value)) * P1 + P4;
}
