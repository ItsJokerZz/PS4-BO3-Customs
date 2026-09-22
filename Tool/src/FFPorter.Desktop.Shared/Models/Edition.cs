using System.IO;
using FFPorter.Desktop.Models;

namespace FFPorter.Desktop;

public abstract class Edition
{
    private static Edition? _current;

    public static Edition Current => _current ?? throw new InvalidOperationException("the application did not set its edition");

    public static void Use(Edition edition) => _current = edition;

    public abstract string Codename { get; }

    public abstract string Title { get; }

    public abstract string Subtitle { get; }

    public abstract string GameName { get; }

    public abstract string GameLabel { get; }

    public abstract string SettingsFile { get; }

    public abstract string GameFolderName { get; }

    public abstract string GameFolderHint { get; }

    public abstract string GameFolderOption { get; }

    public abstract bool TryGameFolder(string chosen, out string? folder);

    public virtual void PrepareTools(Action<string> log)
    {
    }

    public abstract bool Accepts(string file, out string? why);

    public abstract IEnumerable<Job> Jobs(IReadOnlyList<string> files);

    public abstract int RunBackend(string[] arguments, TextWriter output, TextWriter error);
}
