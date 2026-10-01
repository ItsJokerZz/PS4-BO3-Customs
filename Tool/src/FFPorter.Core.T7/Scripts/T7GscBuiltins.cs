using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FFPorter.Core.T7.Scripts;

public sealed class T7GscBuiltins
{
    private const uint BuiltinSpaceA = 775726276, BuiltinSpaceB = 867341309;
    private const int KindPointer = 1, KindCall = 2, KindMethod = 4;

    private readonly Dictionary<string, Dictionary<uint, (int Min, int Max)>> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, (string Name, string? Ps4, string? Why)> _pcOnly = [];

    public static T7GscBuiltins Load(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        var builtins = new T7GscBuiltins();
        foreach (string kind in (string[])["gsc_function", "gsc_method", "csc_function", "csc_method"])
        {
            var table = new Dictionary<uint, (int, int)>();
            foreach (JsonProperty entry in document.RootElement.GetProperty(kind).EnumerateObject())
                table[uint.Parse(entry.Name, NumberStyles.HexNumber)] = (entry.Value[0].GetInt32(), entry.Value[1].GetInt32());
            builtins._tables[kind] = table;
        }
        foreach (JsonProperty entry in document.RootElement.GetProperty("pc_only").EnumerateObject())
        {
            string? ps4 = entry.Value.TryGetProperty("ps4", out JsonElement replacement) ? replacement.GetString() : null;
            string? why = entry.Value.TryGetProperty("why", out JsonElement reason) ? reason.GetString() : null;
            builtins._pcOnly[uint.Parse(entry.Name, NumberStyles.HexNumber)] = (entry.Value.GetProperty("name").GetString()!, ps4, why);
        }
        return builtins;
    }

    public static uint Hash(string name)
    {
        uint hash = 0x4B9ACE2F;
        foreach (char c in name.ToLowerInvariant())
            hash = (hash ^ c) * 0x01000193;
        return hash * 0x01000193;
    }

    public readonly record struct ScriptExport(uint Name, uint Space, int Parameters, int Flags);

    public static IReadOnlyList<ScriptExport> Exports(ReadOnlySpan<byte> buffer)
    {
        int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer[0x20..]);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(buffer[0x3A..]);
        var exports = new ScriptExport[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = buffer.Slice(offset + 20 * i, 20);
            exports[i] = new ScriptExport(BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]), BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]), entry[16], entry[17]);
        }
        return exports;
    }

    public static IReadOnlyList<string> Includes(ReadOnlySpan<byte> buffer)
    {
        int offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer[0x0C..]);
        var includes = new List<string>();
        for (int i = 0; i < buffer[0x44]; i++)
        {
            ReadOnlySpan<byte> text = buffer[(int)BinaryPrimitives.ReadUInt32LittleEndian(buffer[(offset + 4 * i)..])..];
            int end = text.IndexOf((byte)0);
            if (end > 0)
                includes.Add(Encoding.Latin1.GetString(text[..end]));
        }
        return includes;
    }

    public IReadOnlyList<string> Apply(byte[] buffer, string name, Func<string, IReadOnlyList<ScriptExport>?>? scriptExports = null)
    {
        string side = name.EndsWith(".csc", StringComparison.OrdinalIgnoreCase) ? "csc" : "gsc";
        int importsOffset = (int)Read32(buffer, 0x24);
        int importCount = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(0x3C));
        IReadOnlyList<ScriptExport> exports = Exports(buffer);
        List<IReadOnlyList<ScriptExport>> included = scriptExports == null
            ? []
            : Includes(buffer).Select(include => scriptExports($"{include}.{side}")).OfType<IReadOnlyList<ScriptExport>>().ToList();
        var changes = new List<string>();
        var errors = new List<string>();
        int at = importsOffset;
        for (int i = 0; i < importCount; i++)
        {
            uint function = Read32(buffer, at), space = Read32(buffer, at + 4);
            int references = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(at + 8));
            int parameters = buffer[at + 10], flags = buffer[at + 11], kind = flags & 0xF;
            int entry = at;
            at += 12 + 4 * references;
            if (kind is not (KindPointer or KindCall or KindMethod) || (space is not (BuiltinSpaceA or BuiltinSpaceB) && (flags & 0x20) == 0))
                continue;
            if (Resolves(exports, function, space, parameters, kind, own: true) || included.Any(scripts => Resolves(scripts, function, space, parameters, kind, own: false)))
                continue;
            if (_pcOnly.TryGetValue(function, out var pcOnly))
            {
                if (pcOnly.Ps4 == null)
                {
                    errors.Add($"calls {pcOnly.Name}, a builtin only the PC build has");
                    continue;
                }
                uint replacement = Hash(pcOnly.Ps4);
                if (Find(side, kind, replacement) is not { } range || (kind != KindPointer && (parameters < range.Min || parameters > range.Max)))
                {
                    errors.Add($"calls {pcOnly.Name} with {parameters} arguments, which PS4's {pcOnly.Ps4} cannot take");
                    continue;
                }
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(entry), replacement);
                changes.Add($"{pcOnly.Name} (PC only) now calls {pcOnly.Ps4}" + (pcOnly.Why != null ? $": {pcOnly.Why}" : ""));
                continue;
            }
            if (kind != KindPointer && Find(side, kind, function) is { } builtin && (parameters < builtin.Min || parameters > builtin.Max))
                errors.Add($"calls builtin {function:x8} with {parameters} arguments; PS4's takes {builtin.Min} to {builtin.Max}");
        }
        if (errors.Count > 0)
            throw new InvalidDataException($"script '{name}' {string.Join("; ", errors.Distinct())}. The PS4 linker rejects it and the level fails to load (\"**** 1 script error(s)\")");
        return changes.Distinct().ToList();
    }

    private static bool Resolves(IReadOnlyList<ScriptExport> exports, uint function, uint space, int parameters, int kind, bool own)
    {
        foreach (ScriptExport export in exports)
            if (export.Name == function && export.Space == space && (own || (export.Flags & 4) == 0)
                && (kind == KindPointer || parameters <= export.Parameters || (export.Flags & 0x20) != 0))
                return true;
        return false;
    }

    private (int Min, int Max)? Find(string side, int kind, uint function)
    {
        foreach (string table in kind switch { KindPointer => (string[])["function", "method"], KindCall => ["function"], _ => ["method"] })
            if (_tables[$"{side}_{table}"].TryGetValue(function, out var range))
                return range;
        return null;
    }

    private static uint Read32(byte[] buffer, int at) => BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(at));
}
