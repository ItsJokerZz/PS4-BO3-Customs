using System.IO;

namespace FFPorter.Desktop.Models;

public static class Fastfiles
{
    private static ReadOnlySpan<byte> BlackOps3Magic => "TAff"u8;
    private static ReadOnlySpan<byte> ModernWarfareUnsigned => "S1ffu100"u8;
    private static ReadOnlySpan<byte> ModernWarfareSigned => "S1ff0100"u8;

    public static string? Game(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 4 && head[..4].SequenceEqual(BlackOps3Magic))
            return "Black Ops III";
        if (head.Length >= 8 && (head[..8].SequenceEqual(ModernWarfareUnsigned) || head[..8].SequenceEqual(ModernWarfareSigned)))
            return "MW Remastered";
        return null;
    }

    public static byte[] Head(string file, int bytes = 4096)
    {
        try
        {
            using FileStream stream = File.OpenRead(file);
            var head = new byte[bytes];
            int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            return head[..read];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static string Skipped(ReadOnlySpan<byte> head, string thisGame) => Game(head) switch
    {
        null => $"not a {thisGame} PC fastfile",
        string other when other != thisGame => $"a {other} fastfile; the {other} converter takes that one",
        _ => $"not a {thisGame} PC fastfile",
    };
}
