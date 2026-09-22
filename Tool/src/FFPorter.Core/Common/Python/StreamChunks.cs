namespace FFPorter.Core.Common.Python;

public static class StreamChunks
{
    public const int MaxChunk = 64 << 20;

    public static void WriteInChunks(this Stream stream, ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            int count = Math.Min(MaxChunk, data.Length);
            stream.Write(data[..count]);
            data = data[count..];
        }
    }
}
