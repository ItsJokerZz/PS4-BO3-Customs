using System.IO.Compression;
using System.Reflection;

namespace FFPorter.Core.Common.Tools;

public static class ToolData
{
    public const string FolderName = "data";

    private static readonly List<(Assembly Assembly, string Resource)> Archives = [];
    private static readonly object Gate = new();
    private static bool _extracted;

    public static string ShippedRoot => Path.Combine(AppContext.BaseDirectory, FolderName);

    public static string ExtractedRoot => Path.GetFullPath(Path.Combine(Workspace.WorkDirectory, FolderName));

    public static void Register(Assembly assembly, string resource)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (Gate)
        {
            if (Archives.Contains((assembly, resource)))
                return;
            Archives.Add((assembly, resource));
            _extracted = false;
        }
    }

    public static string Locate(Workspace? workspace, string relative)
    {
        string native = relative.Replace('/', Path.DirectorySeparatorChar);
        string shipped = Path.Combine(ShippedRoot, native);
        if (Exists(shipped))
            return shipped;
        string extracted = Path.Combine(Extract(), native);
        if (Exists(extracted))
            return extracted;
        if (workspace != null)
        {
            string packaged = Path.Combine(workspace.Root, "app", FolderName, native);
            if (Exists(packaged))
                return packaged;
        }
        return Embedded ? extracted : shipped;
    }

    public static string? Shipped(string relative)
    {
        string native = relative.Replace('/', Path.DirectorySeparatorChar);
        foreach (string root in (string[])[ShippedRoot, Extract()])
        {
            string path = Path.Combine(root, native);
            if (Exists(path))
                return path;
        }
        return null;
    }

    public static string Extract()
    {
        string root = ExtractedRoot;
        lock (Gate)
        {
            if (_extracted)
                return root;
            foreach ((Assembly assembly, string resource) in Archives)
                Unpack(assembly, resource, root);
            _extracted = true;
        }
        return root;
    }

    private static bool Embedded
    {
        get
        {
            lock (Gate)
                return Archives.Count > 0;
        }
    }

    private static void Unpack(Assembly assembly, string resource, string root)
    {
        using Stream? stream = assembly.GetManifestResourceStream(resource);
        if (stream == null)
            return;
        string marker = Path.Combine(root, ".shipped", $"{resource}.{stream.Length}");
        if (File.Exists(marker))
            return;
        using var gate = new Mutex(false, "Local\\" + resource);
        bool held = false;
        try
        {
            try
            {
                held = gate.WaitOne(TimeSpan.FromMinutes(5));
            }
            catch (AbandonedMutexException)
            {
                held = true;
            }
            if (File.Exists(marker))
                return;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (entry.Name.Length == 0)
                    continue;
                string target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (File.Exists(target) && new FileInfo(target).Length == entry.Length)
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string temporary = target + ".part";
                entry.ExtractToFile(temporary, true);
                File.Move(temporary, target, true);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, assembly.GetName().Name + Environment.NewLine);
        }
        finally
        {
            if (held)
                gate.ReleaseMutex();
        }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
