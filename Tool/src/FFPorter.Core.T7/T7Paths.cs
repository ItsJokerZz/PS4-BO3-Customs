using FFPorter.Core.Common.Tools;

namespace FFPorter.Core.T7;

public static class T7Paths
{
    public static string Ps4Loader(Workspace workspace) => ToolData.Locate(workspace, "t7_ps4_image");

    public static string Gsc(Workspace workspace) => ToolData.Locate(workspace, "t7_gsc");

    public static string Acts(Workspace workspace)
    {
        string? variable = Environment.GetEnvironmentVariable(ActsVariable);
        return !string.IsNullOrEmpty(variable) ? variable : ToolData.Locate(workspace, "acts/acts.exe");
    }

    public const string ActsVariable = "FFPORTER_ACTS";

    public static string PcImage(Workspace workspace)
    {
        string? variable = Environment.GetEnvironmentVariable("T7_PC_DUMP");
        if (!string.IsNullOrEmpty(variable))
            return variable;
        string segments = ToolData.Locate(workspace, "t7_pc_image");
        if (File.Exists(Path.Combine(segments, "segments.json")))
            return segments;
        string data = ToolData.Locate(workspace, "t7_pc_image/BlackOps3_dump.exe");
        if (File.Exists(data))
            return data;
        for (var directory = new DirectoryInfo(workspace.Root); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "Executables", "PC", "BlackOps3_dump.exe");
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException(
            $"The PC loader did not unpack to {segments}. It ships inside this build, so nothing needs dumping from the " +
            "game - it is unpacked on first use. Check the application can write to its own folder, and that nothing " +
            "removed it. A dump of your own still works through T7_PC_DUMP if you have one.", segments);
    }

    public static string Work(Workspace workspace) => Path.Combine(Workspace.WorkDirectory, "t7");

    public static string? FindGameZones(string zoneFile, out string? source)
    {
        for (DirectoryInfo? directory = new FileInfo(Path.GetFullPath(zoneFile)).Directory; directory != null; directory = directory.Parent)
        {
            foreach (string candidate in (string[])[directory.FullName, Path.Combine(directory.FullName, "zone")])
            {
                if (IsGameZones(candidate))
                {
                    RememberGameZones(candidate);
                    source = "above the zone";
                    return candidate;
                }
            }
        }
        if (RememberedGameZones() is string remembered)
        {
            source = "found with an earlier zone";
            return remembered;
        }
        if (SteamGameZones() is string steam)
        {
            source = "Steam install";
            return steam;
        }
        source = null;
        return null;
    }

    public static string? FindGameZones(string zoneFile) => FindGameZones(zoneFile, out _);

    private const string SteamFolder = "Call of Duty Black Ops III";

    private static string RememberedPath => Path.Combine(Workspace.WorkDirectory, "t7", "game_zones.txt");

    private static bool IsGameZones(string folder) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)).Equals("zone", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(folder, "base.xpak"));

    private static void RememberGameZones(string folder)
    {
        try
        {
            if (string.Equals(RememberedGameZones(), folder, StringComparison.OrdinalIgnoreCase))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(RememberedPath)!);
            File.WriteAllText(RememberedPath, folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string? RememberedGameZones()
    {
        try
        {
            string? folder = File.Exists(RememberedPath) ? File.ReadAllText(RememberedPath).Trim() : null;
            return !string.IsNullOrEmpty(folder) && IsGameZones(folder) ? folder : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string? SteamGameZones()
    {
        var libraries = new List<string>();
        foreach ((string key, string value) in (ReadOnlySpan<(string, string)>)[
            (@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath"),
            (@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            (@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath")])
        {
            try
            {
                if (Microsoft.Win32.Registry.GetValue(key, value, null) is not string steam || steam.Length == 0)
                    continue;
                steam = Path.GetFullPath(steam);
                libraries.Add(steam);
                string list = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(list))
                    continue;
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(list), "\"path\"\\s+\"([^\"]+)\""))
                    libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            {
            }
        }
        foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string zone = Path.Combine(library, "steamapps", "common", SteamFolder, "zone");
            if (IsGameZones(zone))
                return zone;
        }
        return null;
    }
}
