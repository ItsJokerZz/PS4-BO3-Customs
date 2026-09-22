namespace FFPorter.Core.T7.Shaders;

public static class T7GlobalsLayout
{
    private static readonly Dictionary<(string Techset, int Technique, string Stage), string[]> Ps4Kept = new()
    {
        [("mc/water_shore_flow_nonflat", 5, "vs")] = ["useFoam"],
        [("mc/water_shore_flow_nonflat", 6, "vs")] = ["useFoam"],
    };

    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    public static IReadOnlySet<string> Kept(string? techset, int technique, string stage)
    {
        if (techset == null)
            return None;
        string name = techset.TrimStart(',');
        int hash = name.LastIndexOf('#');
        if (hash > 0)
            name = name[..hash];
        return Ps4Kept.TryGetValue((name, technique, stage), out string[]? kept) ? kept.ToHashSet(StringComparer.Ordinal) : None;
    }

    public static (List<(T7Dxbc.Variable Variable, int Offset)> Layout, int End) Of(IEnumerable<T7Dxbc.Variable> variables, IReadOnlySet<string>? kept, string stage)
    {
        List<T7Dxbc.Variable> list = variables.ToList();
        bool keepsUseFoam = stage == "vs" && list.Any(v => v.Used && v.Name.StartsWith("foamMask", StringComparison.Ordinal));
        return T7Dxbc.Pack(list.Where(v => v.Used || kept?.Contains(v.Name) == true || (keepsUseFoam && v.Name == "useFoam")));
    }
}
