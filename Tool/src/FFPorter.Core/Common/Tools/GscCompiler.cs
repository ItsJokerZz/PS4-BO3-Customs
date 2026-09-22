using System.Text.Json;

namespace FFPorter.Core.Common.Tools;

public sealed record GscCompilerLocation(string Path, string Source, string? SettingsError)
{
    public bool Exists => File.Exists(Path);

    public string CompanionDll => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path) ?? "", GscCompiler.CompanionDllName);

    public bool CompanionDllExists => File.Exists(CompanionDll);
}

public static class GscCompiler
{
    public const string Variable = "FFPORTER_GSC_COMPILER";
    public const string CompanionDllName = "acts-common.dll";
    public const string ExecutableName = "gsc-tool.exe";

    public const string WorkspaceSettingsFile = "desktop-settings.json";

    public static string DesktopSettingsPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "H1FFStudio", "settings.json");

    public static string DownloadsBuild =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "GSC-Tool-PS4-main", "build", "bin", "x64", "release", ExecutableName);

    public static GscCompilerLocation Locate(Workspace? workspace, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        if (environment(Variable) is { Length: > 0 } variable)
            return new GscCompilerLocation(Path.GetFullPath(variable), Variable, null);

        string? error = null;
        if (TryReadDesktopSettings(DesktopSettingsPath, ref error) is string saved)
            return new GscCompilerLocation(saved, "desktop app settings", null);
        if (workspace != null && TryReadWorkspaceSettings(Path.Combine(workspace.Root, WorkspaceSettingsFile), ref error) is string legacy)
            return new GscCompilerLocation(legacy, WorkspaceSettingsFile, null);

        string beside = Path.Combine(AppContext.BaseDirectory, ExecutableName);
        if (File.Exists(beside))
            return new GscCompilerLocation(beside, "beside the converter", error);
        foreach (string folder in (environment("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                string candidate = Path.Combine(folder, ExecutableName);
                if (File.Exists(candidate))
                    return new GscCompilerLocation(Path.GetFullPath(candidate), "PATH", error);
            }
            catch (ArgumentException)
            {
            }
        }
        return new GscCompilerLocation(DownloadsBuild, "default", error);
    }

    private static string? TryReadDesktopSettings(string path, ref string? error)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) is { Length: >= 2 } saved && !string.IsNullOrWhiteSpace(saved[1])
                ? Path.GetFullPath(saved[1])
                : null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            error = failure.Message;
            return null;
        }
    }

    private static string? TryReadWorkspaceSettings(string path, ref string? error)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("gscCompiler", out JsonElement value)
                && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } saved
                ? Path.GetFullPath(saved)
                : null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            error = failure.Message;
            return null;
        }
    }
}
