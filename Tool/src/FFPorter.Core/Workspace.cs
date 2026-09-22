using FFPorter.Core.Common.Python;
using FFPorter.Core.Common.Tools;

namespace FFPorter.Core;

public sealed class Workspace
{
    private Workspace(string root, bool located)
    {
        Root = root;
        Located = located;
    }

    public string Root { get; }

    public static string WorkDirectory =>
        Environment.GetEnvironmentVariable(WorkVariable) is { Length: > 0 } chosen ? chosen : Path.Combine(AppContext.BaseDirectory, "work");

    public const string WorkVariable = "FFPORTER_WORK";

    public static string ReportDirectory => Path.Combine(WorkDirectory, "reports");

    public static string ReportPath(string name, string extension)
    {
        string folder = ReportDirectory;
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, name + extension);
    }

    public bool Located { get; }

    public string Join(string relative)
    {
        string path = relative.Replace('\\', '/');
        string? data = path == "analysis/h1_loader" || path.StartsWith("analysis/h1_loader/", StringComparison.Ordinal)
            ? "h1_loader" + path["analysis/h1_loader".Length..]
            : path == "tools" || path.StartsWith("tools/", StringComparison.Ordinal) ? path[5..].TrimStart('/') : null;
        if (data is { Length: > 0 } && ToolData.Shipped(data) is string shipped)
            return PyPath.PathlibString(shipped);
        if (data == null && new[] { "PC", "analysis", "port_attempt" }.Any(prefix => path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal)))
            return PyPath.PathlibString(WorkDirectory + "\\" + path);
        if (data != null && UsesPackagedLayout(Root))
            path = "app/data" + (data.Length > 0 ? "/" + data : "");
        return PyPath.PathlibString(Root + "\\" + path);
    }

    public static Workspace Locate(string? explicitRoot = null)
    {
        if (!string.IsNullOrEmpty(explicitRoot))
            return new Workspace(Canonical(explicitRoot), true);

        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            string? found = FindRootUpward(start);
            if (found != null)
                return new Workspace(Canonical(found), true);
        }
        return new Workspace(Canonical(Environment.CurrentDirectory), false);
    }

    private static string Canonical(string path) => PyPath.Resolve(path);

    private static bool UsesPackagedLayout(string root) =>
        Directory.Exists(Path.Combine(root, "app", "data"))
        || File.Exists(Path.Combine(root, ".ffport-workspace"));

    public static string? FindRootUpward(string start)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(start)); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PS4", "mp_shipment", "mp_shipment_load.ff"))
                || UsesPackagedLayout(directory.FullName) || Directory.Exists(Path.Combine(directory.FullName, "tools")))
                return directory.FullName;
        }
        return null;
    }
}
