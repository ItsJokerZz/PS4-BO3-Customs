namespace FFPorter.Core.T7.Shaders;

public sealed record T7PassTargets(bool Gbuffer, bool Decal, uint MrtFormat)
{
    public const uint DefaultMrtFormat = 0x44444444;

    public static readonly T7PassTargets Plain = new(false, false, DefaultMrtFormat);

    public static T7PassTargets For(string? setName, int technique) => technique switch
    {
        3 => new(true, setName?.Contains("decal", StringComparison.OrdinalIgnoreCase) == true, DefaultMrtFormat),
        4 => new(true, false, DefaultMrtFormat),
        9 => new(false, false, 0x44444442),
        _ => Plain,
    };

    public string Key => this == Plain ? "" : $"|p{(Gbuffer ? 1 : 0)}{(Decal ? 1 : 0)}{MrtFormat:x8}";
}
