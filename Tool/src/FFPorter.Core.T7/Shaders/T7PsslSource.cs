using System.Text;
using System.Text.RegularExpressions;

namespace FFPorter.Core.T7.Shaders;

public static class T7PsslSource
{
    public sealed record Result(string Text, IReadOnlyList<string> Globals);

    private static readonly (string Hlsl, string Pssl)[] Words =
    [
        ("RWStructuredBuffer", "RW_RegularBuffer"), ("AppendStructuredBuffer", "AppendRegularBuffer"),
        ("ConsumeStructuredBuffer", "ConsumeRegularBuffer"), ("StructuredBuffer", "RegularBuffer"), ("RWBuffer", "RW_DataBuffer"),
        ("ByteAddressBuffer", "ByteBuffer"), ("RWByteAddressBuffer", "RW_ByteBuffer"),
        ("RWTexture1DArray", "RW_Texture1D_Array"), ("RWTexture2DArray", "RW_Texture2D_Array"),
        ("RWTexture1D", "RW_Texture1D"), ("RWTexture2D", "RW_Texture2D"), ("RWTexture3D", "RW_Texture3D"),
        ("Texture1DArray", "Texture1D_Array"), ("Texture2DArray", "Texture2D_Array"), ("TextureCubeArray", "TextureCube_Array"),
        ("Texture2DMSArray", "MS_Texture2D_Array"), ("Texture2DMS", "MS_Texture2D"),
        ("nointerpolation", "nointerp"), ("noperspective", "nopersp"), ("cbuffer", "ConstantBuffer"),
    ];

    private static readonly (string Hlsl, string Pssl)[] Functions =
    [
        ("firstbitlow", "FirstSetBit_Lo"), ("firstbithigh", "FirstSetBit_Hi"), ("countbits", "CountSetBits"), ("reversebits", "ReverseBits"),
    ];

    private static readonly Dictionary<string, string> Semantics = new(StringComparer.Ordinal)
    {
        ["SV_POSITION"] = "S_POSITION", ["SV_TARGET"] = "S_TARGET_OUTPUT", ["SV_DEPTH"] = "S_DEPTH_OUTPUT",
        ["SV_VERTEXID"] = "S_VERTEX_ID", ["SV_INSTANCEID"] = "S_INSTANCE_ID", ["SV_ISFRONTFACE"] = "S_FRONT_FACE",
        ["SV_PRIMITIVEID"] = "S_PRIMITIVE_ID", ["SV_COVERAGE"] = "S_COVERAGE", ["SV_SAMPLEINDEX"] = "S_SAMPLE_INDEX",
    };

    private static readonly Regex Semantic = new(@":\s*(SV_[A-Za-z]+)(\d*)\b", RegexOptions.Compiled);
    private static readonly Regex GlobalsBlock = new(@"ConstantBuffer\s+_Globals\s*:\s*register\((b\d+)\)\s*\{(.*?)\n\}", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex GlobalsMember = new(@"^\s*(.*?)\s+(\w+)(\[\d+\])?\s*:\s*packoffset\([^)]*\)\s*;", RegexOptions.Compiled);
    private static readonly Regex TextureRegister = new(@"register\(t(\d+)\)", RegexOptions.Compiled);
    private static readonly Regex SamplerRegister = new(@"register\(s(\d+)\)", RegexOptions.Compiled);
    private static readonly Regex InstanceIndex = new(@"\buint(\s+\w+\s*):\s*TEXCOORD15\b", RegexOptions.Compiled);
    private static readonly Regex MainSignature = new(@"void\s+main\s*\((.*?)\)\s*\{", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Parameter = new(@"^(\s*(?:out\s+|inout\s+|in\s+)?(?:nointerp\s+|nopersp\s+|centroid\s+|sample\s+)*)(float|int|uint|bool|min16float|min10float|min16int|min12int|min16uint)(\d?)(\s+)(\w+)(\s*:.*)$", RegexOptions.Compiled);

    public static Result Transform(string hlsl, string stage, IReadOnlyList<T7Dxbc.Variable>? globalsReflection,
        IReadOnlyDictionary<int, int>? textureRemap, IReadOnlyDictionary<int, int>? samplerRemap)
    {
        string text = hlsl;
        foreach ((string from, string to) in Words)
            text = Regex.Replace(text, $@"\b{from}\b", to);
        text = Regex.Replace(text, @"\bBuffer<", "DataBuffer<");
        text = text.Replace(".SampleLevel(", ".SampleLOD(", StringComparison.Ordinal)
            .Replace(".SampleCmpLevelZero(", ".SampleCmpLOD0(", StringComparison.Ordinal)
            .Replace(".SampleGrad(", ".SampleGradient(", StringComparison.Ordinal)
            .Replace(".CalculateLevelOfDetailUnclamped(", ".GetLODUnclamped(", StringComparison.Ordinal)
            .Replace(".CalculateLevelOfDetail(", ".GetLOD(", StringComparison.Ordinal);
        foreach ((string from, string to) in Functions)
            text = Regex.Replace(text, $@"\b{from}\s*\(", to + "(");
        text = Regex.Replace(text, @"\((u?)int(?:16|14|12|10)\)", "($1int)");
        text = Regex.Replace(text, @"(<<|>>)\s*(r\d+\.[xyzw])\b", "$1 (int)$2");
        text = Semantic.Replace(text, match =>
        {
            string name = match.Groups[1].Value.ToUpperInvariant();
            if (!Semantics.TryGetValue(name, out string? target))
                return match.Value;
            return ": " + target + (name == "SV_TARGET" ? match.Groups[2].Value : "");
        });

        var globals = new List<string>();
        text = GlobalsBlock.Replace(text, match =>
        {
            foreach (string line in match.Groups[2].Value.Split('\n'))
            {
                Match member = GlobalsMember.Match(line);
                if (member.Success)
                    globals.Add(member.Groups[2].Value);
            }
            return $"ConstantBuffer __GLOBAL_CB__ : register({match.Groups[1].Value})\n{{{match.Groups[2].Value}\n}}";
        }, 1);

        if (textureRemap != null)
            text = TextureRegister.Replace(text, m => textureRemap.TryGetValue(int.Parse(m.Groups[1].Value), out int to) ? $"register(t{to})" : m.Value);
        if (samplerRemap != null)
            text = SamplerRegister.Replace(text, m => samplerRemap.TryGetValue(int.Parse(m.Groups[1].Value), out int to) ? $"register(s{to})" : m.Value);
        if (stage == "vs")
            text = InstanceIndex.Replace(text, "uint$1: S_INSTANCE_ID");
        text = WidenParameters(text);
        text = WidenConstants(text);
        text = UnorderedAccess(text);
        return new Result(text, globals);
    }

    private static readonly Regex UavDeclaration = new(@"\b(RW_Texture1D|RW_Texture2D|RW_Texture3D|RW_Texture1D_Array|RW_Texture2D_Array|RW_DataBuffer|RW_RegularBuffer)<(\w+)>\s+(\w+)\s*:\s*register\(u(\d+)\)", RegexOptions.Compiled);
    private static readonly Regex AtomicAddInstruction = new(@"^[ \t]*(?://[^\n]*\n[ \t]*)?imm_atomic_iadd (r\d+\.[xyzw]), u(\d+), (r\d+)\.([xyzw]+), l\((-?\d+)\)[ \t]*\n(?:[ \t]*InterlockedAdd\(dest, imm_value, orig_value\);[ \t]*\n)?", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex StoreInstruction = new(@"^[ \t]*(?://[^\n]*\n[ \t]*)?store_uav_typed u(\d+)\.([xyzw]+), (r\d+)\.([xyzw]+), (r\d+)\.([xyzw]+)[ \t]*$", RegexOptions.Compiled | RegexOptions.Multiline);

    private static string UnorderedAccess(string text)
    {
        var uavs = UavDeclaration.Matches(text).ToDictionary(m => int.Parse(m.Groups[4].Value), m => (Kind: m.Groups[1].Value, Element: m.Groups[2].Value, Name: m.Groups[3].Value));
        string Address(int slot, string register, string swizzle)
        {
            int dimensions = uavs[slot].Kind switch
            {
                "RW_Texture2D" or "RW_Texture1D_Array" => 2,
                "RW_Texture3D" or "RW_Texture2D_Array" => 3,
                _ => 1,
            };
            string components = swizzle[..Math.Min(dimensions, swizzle.Length)];
            return dimensions == 1 ? $"(uint){register}.{components}" : $"(uint{dimensions}){register}.{components}";
        }
        text = AtomicAddInstruction.Replace(text, m =>
        {
            int slot = int.Parse(m.Groups[2].Value);
            if (!uavs.ContainsKey(slot))
                return m.Value;
            return $"  {{ uint __atomicOriginal; AtomicAdd({uavs[slot].Name}[{Address(slot, m.Groups[3].Value, m.Groups[4].Value)}], {m.Groups[5].Value}, __atomicOriginal); {m.Groups[1].Value} = __atomicOriginal; }}\n";
        });
        return StoreInstruction.Replace(text, m =>
        {
            int slot = int.Parse(m.Groups[1].Value);
            if (!uavs.TryGetValue(slot, out var uav))
                return m.Value;
            int size = uav.Element.Length > 0 && char.IsDigit(uav.Element[^1]) ? uav.Element[^1] - '0' : 1;
            string value = m.Groups[6].Value[..Math.Min(size, m.Groups[6].Value.Length)];
            return $"  {uav.Name}[{Address(slot, m.Groups[3].Value, m.Groups[4].Value)}] = ({uav.Element}){m.Groups[5].Value}.{value};";
        });
    }

    private static readonly Regex ConstantMember = new(@"^(\s*)(float|int|uint|bool)(\d?)\s+(\w+)\s*:\s*packoffset\(c(\d+)(?:\.([xyzw]))?\)\s*;", RegexOptions.Compiled | RegexOptions.Multiline);

    private static string WidenConstants(string text)
    {
        var members = ConstantMember.Matches(text).Select(m => (Match: m, Type: m.Groups[2].Value, Count: m.Groups[3].Value.Length == 0 ? 1 : int.Parse(m.Groups[3].Value),
            Name: m.Groups[4].Value, Row: int.Parse(m.Groups[5].Value), Start: m.Groups[6].Success ? "xyzw".IndexOf(m.Groups[6].Value[0]) : 0)).ToList();
        foreach (var member in members)
        {
            int needed = member.Count;
            foreach (Match use in Regex.Matches(text, $@"\b{Regex.Escape(member.Name)}\.([xyzw]+)\b"))
                foreach (char component in use.Groups[1].Value)
                    needed = Math.Max(needed, "xyzw".IndexOf(component) + 1);
            if (needed <= member.Count || member.Start + needed > 4)
                continue;
            var covered = members.Where(o => o.Name != member.Name && o.Row == member.Row && o.Start >= member.Start + member.Count
                && o.Start < member.Start + needed).ToList();
            if (covered.Any(o => o.Type != member.Type || o.Start + o.Count > member.Start + needed))
                continue;
            text = Regex.Replace(text, $@"^(\s*){member.Type}{(member.Count == 1 ? "" : member.Count.ToString())}\s+{member.Name}(\s*:)",
                $"$1{member.Type}{needed} {member.Name}$2", RegexOptions.Multiline);
            foreach (var other in covered)
            {
                text = Regex.Replace(text, $@"^\s*{other.Type}\d?\s+{other.Name}\s*:\s*packoffset\([^)]*\)\s*;\s*\n", "", RegexOptions.Multiline);
                string swizzle = "xyzw".Substring(other.Start - member.Start, other.Count);
                text = Regex.Replace(text, $@"\b{Regex.Escape(other.Name)}\b", $"{member.Name}.{swizzle}");
            }
            return WidenConstants(text);
        }
        return text;
    }

    private static string WidenParameters(string text)
    {
        Match signature = MainSignature.Match(text);
        if (!signature.Success)
            return text;
        string body = text[(signature.Index + signature.Length)..];
        string[] parameters = signature.Groups[1].Value.Split('\n');
        bool changed = false;
        for (int i = 0; i < parameters.Length; i++)
        {
            Match parameter = Parameter.Match(parameters[i]);
            if (!parameter.Success)
                continue;
            int declared = parameter.Groups[3].Value.Length == 0 ? 1 : int.Parse(parameter.Groups[3].Value);
            string name = parameter.Groups[5].Value;
            int needed = declared;
            foreach (Match use in Regex.Matches(body, $@"\b{Regex.Escape(name)}\.([xyzw]+|[rgba]+)\b"))
            {
                foreach (char component in use.Groups[1].Value)
                {
                    int index = "xyzw".IndexOf(component);
                    needed = Math.Max(needed, (index >= 0 ? index : "rgba".IndexOf(component)) + 1);
                }
            }
            if (needed <= declared)
                continue;
            parameters[i] = parameter.Groups[1].Value + parameter.Groups[2].Value + needed + parameter.Groups[4].Value + name + parameter.Groups[6].Value;
            changed = true;
        }
        if (!changed)
            return text;
        Group group = signature.Groups[1];
        return text[..group.Index] + string.Join('\n', parameters) + text[(group.Index + group.Length)..];
    }
}
