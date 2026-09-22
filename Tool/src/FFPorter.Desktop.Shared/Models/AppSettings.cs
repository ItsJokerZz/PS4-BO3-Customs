using System.IO;
using System.Text.Json;

namespace FFPorter.Desktop.Models;

public sealed class AppSettings : Observable
{
    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS4 FF Porter");

    private static readonly string LegacyFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "H1FFStudio");

    public static string DefaultOutput { get; } = Path.Combine(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), "exports");

    private static readonly string UiPath = Path.Combine(Folder, Edition.Current.SettingsFile);

    private static readonly string[] LegacyUiPaths =
    [
        Path.Combine(LegacyFolder, Edition.Current.SettingsFile),
        Path.Combine(LegacyFolder, "ui.json"),
    ];
    private static readonly string T7Path = Path.Combine(LegacyFolder, "t7settings.json");

    public static AppSettings Current { get; } = Load();

    private string? _outputOverride;
    private string? _gameFolder;

    private AppSettings(string workspace) => Workspace = workspace;

    public string Workspace { get; }

    public bool FirstRun { get; private set; }

    public string? GameFolder => _gameFolder;

    public void SetGameFolder(string? folder)
    {
        string? chosen = string.IsNullOrWhiteSpace(folder) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Trim()));
        if (string.Equals(chosen, _gameFolder, StringComparison.OrdinalIgnoreCase))
            return;
        _gameFolder = chosen;
        Raise(nameof(GameFolder));
        Save();
    }

    public string? OutputOverride => _outputOverride;

    public string Output => _outputOverride ?? DefaultOutput;

    public string OutputFor(Job job) => Path.Combine(Output, job.Codename, job.Name);

    public void SetOutput(string? folder)
    {
        string? chosen = string.IsNullOrWhiteSpace(folder) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder.Trim()));
        if (chosen != null && string.Equals(chosen, DefaultOutput, StringComparison.OrdinalIgnoreCase))
            chosen = null;
        if (string.Equals(chosen, _outputOverride, StringComparison.OrdinalIgnoreCase))
            return;
        _outputOverride = chosen;
        Raise(nameof(OutputOverride));
        Raise(nameof(Output));
        Save();
    }

    private sealed class UiFile
    {
        public string? Output { get; set; }
        public string? GameFolder { get; set; }
    }

    private static AppSettings Load()
    {
        string workspace;
        try
        {
            workspace = FFPorter.Core.Workspace.Locate().Root;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            workspace = AppContext.BaseDirectory;
        }
        var settings = new AppSettings(workspace);
        UiFile? saved = File.Exists(UiPath) ? Read<UiFile>(UiPath)
            : LegacyUiPaths.FirstOrDefault(File.Exists) is { } legacy ? Read<UiFile>(legacy)
            : null;
        settings.FirstRun = !File.Exists(UiPath);
        string? output = saved?.Output;
        if (saved?.GameFolder is { Length: > 0 } game && Directory.Exists(game))
            settings._gameFolder = Path.TrimEndingDirectorySeparator(game);
        if (output == null && Read<string[]>(T7Path) is { Length: >= 2 } t7 && !string.IsNullOrWhiteSpace(t7[1]) && Directory.Exists(t7[1].Trim()))
        {
            output = Path.TrimEndingDirectorySeparator(t7[1].Trim());
            if (Path.GetFileName(output).Equals(Edition.Current.Codename, StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(output) is { Length: > 0 } parent)
                output = parent;
        }
        try
        {
            string? chosen = string.IsNullOrWhiteSpace(output) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(output.Trim()));
            settings._outputOverride = chosen != null && !string.Equals(chosen, DefaultOutput, StringComparison.OrdinalIgnoreCase) ? chosen : null;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            settings._outputOverride = null;
        }
        return settings;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(UiPath, JsonSerializer.Serialize(new UiFile { Output = _outputOverride, GameFolder = _gameFolder }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static T? Read<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : null;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
