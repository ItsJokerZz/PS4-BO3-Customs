namespace FFPorter.Core.Common.Compression;

internal static class OverlapCopy
{
    public static void Forward(Span<byte> buffer, int destination, int distance, int length)
    {
        if (distance == 0)
        {
            buffer.Slice(destination, length).Clear();
            return;
        }
        int source = destination - distance;
        if (distance >= length)
        {
            buffer.Slice(source, length).CopyTo(buffer.Slice(destination));
            return;
        }
        int written = 0;
        while (written < length)
        {
            int count = Math.Min(distance + written, length - written);
            buffer.Slice(source, count).CopyTo(buffer.Slice(destination + written));
            written += count;
        }
    }
}
