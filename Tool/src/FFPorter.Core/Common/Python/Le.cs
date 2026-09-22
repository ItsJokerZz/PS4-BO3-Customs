using System.Buffers.Binary;

namespace FFPorter.Core.Common.Python;

public sealed class StructError(string message) : Exception(message);

public static class Le
{
    public static ushort U16(ReadOnlySpan<byte> data, long offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(Field(data, offset, 2));

    public static uint U32(ReadOnlySpan<byte> data, long offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(Field(data, offset, 4));

    public static ulong U64(ReadOnlySpan<byte> data, long offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(Field(data, offset, 8));

    private static ReadOnlySpan<byte> Field(ReadOnlySpan<byte> data, long offset, int size)
    {
        if (offset < 0)
        {
            if (offset + size > 0)
                throw new StructError($"not enough data to unpack {size} bytes at offset {offset}");
            if (offset + data.Length < 0)
                throw new StructError($"offset {offset} out of range for {data.Length}-byte buffer");
            offset += data.Length;
        }
        if (data.Length - offset < size)
            throw new StructError($"unpack_from requires a buffer of at least {offset + size} bytes for unpacking {size} bytes at offset {offset} (actual buffer size is {data.Length})");
        return data.Slice((int)offset, size);
    }
}
