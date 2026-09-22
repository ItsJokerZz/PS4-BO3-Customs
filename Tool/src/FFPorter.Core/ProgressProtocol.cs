namespace FFPorter.Core;

public static class ProgressProtocol
{
    public const string Prefix = "FFPORTER_PROGRESS ";

    public static string Format(int current, int total, string stage) => $"{Prefix}{current}/{total} {stage}";

    public static bool TryParse(string line, out int current, out int total, out string stage)
    {
        current = total = 0;
        stage = "";
        if (!line.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        string[] fields = line.Trim().Split(' ', 3);
        if (fields.Length < 2)
            return false;
        string[] counts = fields[1].Split('/');
        if (counts.Length != 2 || !int.TryParse(counts[0], out current) || !int.TryParse(counts[1], out total) || total <= 0)
            return false;
        stage = fields.Length > 2 ? fields[2] : "";
        return true;
    }
}
