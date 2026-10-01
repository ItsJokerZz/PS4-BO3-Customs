using System.IO;
using System.Text.Json;

namespace FFPorter.Desktop.Models;

public sealed class AppSettings : Observable
{
    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS4 FF Porter");

    private static readonly string LegacyFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "H1FFStudio");

    public static string DefaultOutput { get; } = Path.Combine(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), "exports");

    public static string DefaultCatalogUrl { get; } = Decode("aHR0cHM6Ly9wdWItYTJhZDVlYTYyMzk4NDg2ODhmY2U5ZDUzOTNiZDA2MjgucjIuZGV2L3Q3L2NhdGFsb2cuanNvbg==");

    public const string DefaultFtpRemotePath = "/data/BO3-Customs/usermaps";
    public const int DefaultFtpPort = 2121;

    private static string Decode(string value) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value));

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
    private string? _catalogUrl;
    private string _ftpHost = "";
    private int _ftpPort = DefaultFtpPort;
    private string _ftpUser = "anonymous";
    private string _ftpPassword = "";
    private string _ftpRemotePath = DefaultFtpRemotePath;

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

    public string CatalogUrl => string.IsNullOrWhiteSpace(_catalogUrl) ? DefaultCatalogUrl : _catalogUrl!;

    public void SetCatalogUrl(string? url)
    {
        string? chosen = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        if (string.Equals(chosen, _catalogUrl, StringComparison.OrdinalIgnoreCase))
            return;
        _catalogUrl = chosen;
        Raise(nameof(CatalogUrl));
        Save();
    }

    public string FtpHost => _ftpHost;
    public int FtpPort => _ftpPort;
    public string FtpUser => _ftpUser;
    public string FtpPassword => _ftpPassword;
    public string FtpRemotePath => _ftpRemotePath;

    public string FtpTarget => FtpHost.Length == 0 ? "console not set" : $"{FtpUser}@{FtpHost}:{FtpPort}{FtpRemotePath}";

    public bool HasConsole => FtpHost.Length > 0;

    public void SetConsole(string host, int port, string user, string password, string remotePath)
    {
        _ftpHost = host.Trim();
        _ftpPort = port is > 0 and <= 65535 ? port : DefaultFtpPort;
        _ftpUser = string.IsNullOrWhiteSpace(user) ? "anonymous" : user.Trim();
        _ftpPassword = password;
        _ftpRemotePath = "/" + remotePath.Replace('\\', '/').Trim('/');
        if (_ftpRemotePath.Length == 0)
            _ftpRemotePath = DefaultFtpRemotePath;
        Raise(nameof(FtpHost));
        Raise(nameof(FtpPort));
        Raise(nameof(FtpUser));
        Raise(nameof(FtpPassword));
        Raise(nameof(FtpRemotePath));
        Raise(nameof(FtpTarget));
        Raise(nameof(HasConsole));
        Save();
    }

    private sealed class UiFile
    {
        public string? Output { get; set; }
        public string? GameFolder { get; set; }
        public string? CatalogUrl { get; set; }
        public string? FtpHost { get; set; }
        public int? FtpPort { get; set; }
        public string? FtpUser { get; set; }
        public string? FtpPassword { get; set; }
        public string? FtpRemotePath { get; set; }
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
        settings._catalogUrl = string.IsNullOrWhiteSpace(saved?.CatalogUrl) ? null : saved!.CatalogUrl!.Trim();
        settings._ftpHost = saved?.FtpHost?.Trim() ?? "";
        settings._ftpPort = saved?.FtpPort is > 0 and <= 65535 ? saved.FtpPort.Value : DefaultFtpPort;
        settings._ftpUser = string.IsNullOrWhiteSpace(saved?.FtpUser) ? "anonymous" : saved!.FtpUser!.Trim();
        settings._ftpPassword = saved?.FtpPassword ?? "";
        settings._ftpRemotePath = string.IsNullOrWhiteSpace(saved?.FtpRemotePath)
            ? DefaultFtpRemotePath
            : "/" + saved!.FtpRemotePath!.Replace('\\', '/').Trim('/');
        return settings;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(UiPath, JsonSerializer.Serialize(new UiFile
            {
                Output = _outputOverride,
                GameFolder = _gameFolder,
                CatalogUrl = _catalogUrl,
                FtpHost = _ftpHost.Length > 0 ? _ftpHost : null,
                FtpPort = _ftpPort,
                FtpUser = _ftpUser,
                FtpPassword = _ftpPassword.Length > 0 ? _ftpPassword : null,
                FtpRemotePath = _ftpRemotePath,
            }, new JsonSerializerOptions { WriteIndented = true }));
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
