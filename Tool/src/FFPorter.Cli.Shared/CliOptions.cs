namespace FFPorter.Cli;

public sealed class CliOptions
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly List<string> _positional = [];
    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith('-') && arg.Length > 1)
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    options._values[arg] = args[++i];
                else
                    options._flags.Add(arg);
            }
            else
            {
                options._positional.Add(arg);
            }
        }
        return options;
    }

    public string Positional(int index, string name) =>
        index < _positional.Count ? _positional[index] : throw new ArgumentException($"missing <{name}>");

    public string? Value(string name) => _values.GetValueOrDefault(name);

    public string Required(string name) => Value(name) ?? throw new ArgumentException($"missing {name}");

    public bool Flag(string name) => _flags.Contains(name);
}
