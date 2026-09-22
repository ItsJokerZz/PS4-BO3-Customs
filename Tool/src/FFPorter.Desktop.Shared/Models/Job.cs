using System.IO;

namespace FFPorter.Desktop.Models;

public sealed class Job : Observable
{
    private string _state = RunStates.Ready, _statusText = "Ready", _stageText = "";
    private double _progress;
    private FidelityView? _report;

    public required string Kind { get; init; }
    public required string Name { get; init; }
    public required string MainFile { get; init; }
    public IReadOnlyList<string> CompanionZones { get; init; } = [];
    public string CompanionSummary { get; init; } = "";
    public int Streams { get; init; }
    public int SoundBanks { get; init; }
    public long Length { get; init; }

    public string GameLabel => Edition.Current.GameLabel;
    public string GameName => Edition.Current.GameName;

    public string Codename => Edition.Current.Codename;

    public string Size => Length >= 1073741824 ? $"{Length / 1073741824d:0.##} GB" : Length >= 1048576 ? $"{Length / 1048576d:0.#} MB" : $"{Length / 1024d:0.#} KB";

    public string Detail
    {
        get
        {
            var parts = new List<string> { $"{GameName} {Kind.ToLowerInvariant()}" };
            if (CompanionZones.Count > 0 && CompanionSummary.Length > 0)
                parts.Add(CompanionSummary);
            if (Streams > 0)
                parts.Add($"{Streams} xpak");
            if (SoundBanks > 0)
                parts.Add($"{SoundBanks} sound bank{(SoundBanks == 1 ? "" : "s")}");
            return string.Join("  ·  ", parts);
        }
    }

    public string State { get => _state; set { if (Set(ref _state, value)) Raise(nameof(IsRunning)); } }
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public string StageText { get => _stageText; set => Set(ref _stageText, value); }
    public double Progress { get => _progress; set => Set(ref _progress, value); }
    public bool IsRunning => State == RunStates.Running;

    public FidelityView? Report { get => _report; set { if (Set(ref _report, value)) Raise(nameof(HasScore)); } }
    public bool HasScore => Report != null;
}

public static class JobScanner
{
    private const int FolderDepth = 3;

    public static List<Job> Scan(IEnumerable<string> paths, Action<string> log)
    {
        Edition edition = Edition.Current;
        var files = new List<string>();
        foreach (string input in paths)
        {
            try
            {
                string path = Path.GetFullPath(input);
                if (Directory.Exists(path))
                    files.AddRange(FastFilesIn(path, FolderDepth));
                else if (File.Exists(path) && path.EndsWith(".ff", StringComparison.OrdinalIgnoreCase))
                    files.Add(path);
                else
                    log($"Skipped {input}: not a .ff file or a folder");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                log($"Skipped {input}: {error.Message}");
            }
        }

        var mine = new List<string>();
        foreach (string file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (edition.Accepts(file, out string? why))
                mine.Add(file);
            else
                log($"Skipped {file}: {why}");
        }
        return [.. edition.Jobs(mine)];
    }

    private static IEnumerable<string> FastFilesIn(string folder, int depth)
    {
        foreach (string file in Directory.EnumerateFiles(folder, "*.ff"))
            yield return file;
        if (depth <= 0)
            yield break;
        foreach (string child in Directory.EnumerateDirectories(folder))
        {
            foreach (string file in FastFilesIn(child, depth - 1))
                yield return file;
        }
    }
}
