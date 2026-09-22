using System.IO;
using FFPorter.Desktop.Models;

namespace FFPorter.Desktop;

public sealed class T7Edition : Edition
{
    public override string Codename => "t7";
    public override string Title => "PS4 .FF Porter — Black Ops III";
    public override string Subtitle => "Drop Black Ops III maps, mods, weapons and zones. The files that belong with them and the tools are found automatically.";
    public override string GameName => "Black Ops III";
    public override string GameLabel => "BO3";
    public override string SettingsFile => "ui.t7.json";
    public override string GameFolderName => "Black Ops III game files";
    public override string GameFolderHint => "Pick your Black Ops III folder, or the zone folder inside it - the one holding base.xpak. A map's textures and technique sets are completed from it.";
    public override string GameFolderOption => "--pc-reference";

    public override void PrepareTools(Action<string> log) =>
        Core.T7.Scripts.ActsInstall.Ensure(Core.Workspace.Locate(), log);

    public override bool TryGameFolder(string chosen, out string? folder)
    {
        folder = null;
        if (string.IsNullOrWhiteSpace(chosen))
            return false;
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(chosen));
        foreach (string candidate in (string[])[root, Path.Combine(root, "zone")])
        {
            if (File.Exists(Path.Combine(candidate, "base.xpak")))
            {
                folder = candidate;
                return true;
            }
        }
        return false;
    }

    public override bool Accepts(string file, out string? why)
    {
        why = null;
        byte[] head = Fastfiles.Head(file, 16);
        if (Fastfiles.Game(head) != GameName)
        {
            why = head.Length == 0 ? "the file could not be read" : Fastfiles.Skipped(head, GameName);
            return false;
        }
        if (head.Length < 16 || head[14] != 0)
        {
            why = "a Black Ops III fastfile that is not a PC zone (already converted?)";
            return false;
        }
        return true;
    }

    public override IEnumerable<Job> Jobs(IReadOnlyList<string> files)
    {
        static string? BaseOf(string file)
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            string folder = Path.GetDirectoryName(file)!;
            return stem.Length > 3 && stem[2] == '_' && char.IsAsciiLetter(stem[0]) && char.IsAsciiLetter(stem[1]) && File.Exists(Path.Combine(folder, stem[3..] + ".ff"))
                ? Path.Combine(folder, stem[3..] + ".ff")
                : null;
        }
        var mains = new SortedSet<string>(files.Select(file => BaseOf(file) ?? file), StringComparer.OrdinalIgnoreCase);
        foreach (string main in mains)
        {
            string folder = Path.GetDirectoryName(main)!;
            string stem = Path.GetFileNameWithoutExtension(main);
            var localized = Directory.EnumerateFiles(folder, "??_" + stem + ".ff").Where(f => BaseOf(f) != null).Order(StringComparer.OrdinalIgnoreCase).ToList();
            var streams = Directory.EnumerateFiles(folder, "*.xpak")
                .Where(f => Path.GetFileNameWithoutExtension(f) is { } x && (x.Equals(stem, StringComparison.OrdinalIgnoreCase) || x.EndsWith("_" + stem, StringComparison.OrdinalIgnoreCase)
                    || x.Equals(stem + "_d", StringComparison.OrdinalIgnoreCase) || x.EndsWith("_" + stem + "_d", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            string sound = Path.Combine(folder, "snd");
            var banks = Directory.Exists(sound) ? Directory.EnumerateFiles(sound, stem + ".*.sab*", SearchOption.AllDirectories).ToList() : [];
            long length = new[] { main }.Concat(localized).Concat(streams).Concat(banks).Sum(f => new FileInfo(f).Length);
            string lower = stem.ToLowerInvariant();
            string kind = folder.Split(Path.DirectorySeparatorChar).Contains("mods", StringComparer.OrdinalIgnoreCase) || lower.EndsWith("_mod") ? "Mod"
                : (lower.StartsWith("zm_") || lower.StartsWith("mp_") || lower.StartsWith("cp_")) && !lower.Contains("common") && !lower.EndsWith("_patch") ? "Map"
                : "Zone";
            yield return new Job
            {
                Kind = kind, Name = stem, MainFile = main, CompanionZones = localized,
                CompanionSummary = $"{localized.Count} language zone{(localized.Count == 1 ? "" : "s")}",
                Streams = streams.Count, SoundBanks = banks.Count, Length = length,
            };
        }
    }

    public override int RunBackend(string[] arguments, TextWriter output, TextWriter error) =>
        Cli.T7Cli.Run(arguments.Length > 0 && arguments[0] == Codename ? arguments[1..] : arguments, output, error);
}
