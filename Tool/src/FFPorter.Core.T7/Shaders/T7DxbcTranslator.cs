using System.Globalization;
using System.Text;
using static FFPorter.Core.T7.Shaders.T7DxbcCode;

namespace FFPorter.Core.T7.Shaders;

public static class T7DxbcTranslator
{
    public const string Version = "t7-dxbc-7";

    public const string VertexVersion = "t7-dxbc-6";

    public static string VersionOf(string stage) => stage == "vs" ? VertexVersion : Version;

    private const uint UnitVectorScale = 0x40002010, UnitVectorBias = 0xBF804020;

    public const uint OitNodes = 9;

    public sealed record Result(string Text, IReadOnlyList<string> Globals);

    public static Result Translate(ReadOnlySpan<byte> dxbc, string stage, IReadOnlyDictionary<int, int>? textureRemap, IReadOnlyDictionary<int, int>? samplerRemap,
        IReadOnlySet<int>? absentTextures = null, IReadOnlySet<string>? keptGlobals = null, T7PassTargets? targets = null)
    {
        var translator = new Translator(dxbc, stage, textureRemap, samplerRemap, absentTextures, keptGlobals, targets);
        return translator.Run();
    }

    public static bool WritesGbuffer(ReadOnlySpan<byte> dxbc) =>
        (T7Dxbc.OutputSignature(dxbc) ?? []).Any(e => e.Semantic.Equals("SV_TARGET", StringComparison.OrdinalIgnoreCase) && e.Index == 2);

    private enum Kind
    {
        Float,
        Int,
        Uint,
    }

    private sealed record ResourceInfo(int Dimension, Kind Kind, int Components, int Stride);

    private sealed class Translator
    {
        private const string Xyzw = "xyzw";

        private readonly Program _program;
        private readonly string _stage;
        private readonly IReadOnlyDictionary<int, int>? _textureRemap, _samplerRemap;
        private readonly IReadOnlySet<int> _absentTextures;
        private readonly IReadOnlyList<T7Dxbc.Resource> _resources;
        private readonly IReadOnlyList<T7Dxbc.ConstantBuffer> _buffers;
        private readonly IReadOnlyList<T7Dxbc.SignatureElement> _inputs, _outputs;

        private readonly StringBuilder _code = new();
        private int _indent = 1, _unique;
        private readonly SortedSet<string> _helpers = new(StringComparer.Ordinal);

        private int _temps, _globalFlags;
        private uint[]? _immediateConstants;
        private readonly SortedDictionary<int, (int Size, int Components)> _indexableTemps = new();
        private readonly SortedDictionary<int, int> _constantBuffers = new();
        private readonly SortedDictionary<int, ResourceInfo> _textures = new();
        private readonly SortedDictionary<int, bool> _samplers = new();
        private readonly SortedDictionary<int, ResourceInfo> _uavs = new();
        private readonly List<(int Register, int Mask, int Mode)> _interpolation = [];
        private readonly SortedSet<int> _inputRegisters = [], _outputRegisters = [], _writtenOutputs = [];
        private bool _discards, _writesDepth, _writesCoverage;

        private readonly T7PassTargets _targets;

        private readonly int _globalsSlot = -1, _globalsRows;
        private readonly List<(T7Dxbc.Variable Variable, int Offset)> _globalsLayout = [];

        private readonly int _oitMaxEntriesSlot = -1, _oitMaxEntriesOffset = -1;

        public Translator(ReadOnlySpan<byte> dxbc, string stage, IReadOnlyDictionary<int, int>? textureRemap, IReadOnlyDictionary<int, int>? samplerRemap,
            IReadOnlySet<int>? absentTextures, IReadOnlySet<string>? keptGlobals, T7PassTargets? targets)
        {
            _program = Parse(dxbc);
            _stage = stage;
            _targets = stage == "ps" ? targets ?? T7PassTargets.Plain : T7PassTargets.Plain;
            if ((stage, _program.Stage) is not (("ps", 0) or ("vs", 1)))
                throw new NotSupportedException($"a {stage} program was expected, the bytecode is program type {_program.Stage}");
            _textureRemap = textureRemap;
            _samplerRemap = samplerRemap;
            _absentTextures = absentTextures ?? new HashSet<int>();
            _resources = T7Dxbc.Resources(dxbc) ?? [];
            _buffers = T7Dxbc.ConstantBuffers(dxbc) ?? [];
            _inputs = T7Dxbc.InputSignature(dxbc) ?? [];
            _outputs = T7Dxbc.OutputSignature(dxbc) ?? [];
            if (_resources.FirstOrDefault(r => r.Type == 0 && r.Name == "$Globals") is { } globals
                && _buffers.FirstOrDefault(b => b.Name == "$Globals") is { } reflected)
            {
                _globalsSlot = globals.Bind;
                (_globalsLayout, int end) = T7GlobalsLayout.Of(reflected.Variables, keptGlobals, stage);
                _globalsRows = (end + 15) / 16;
            }
            if (_resources.FirstOrDefault(r => r.Type == 0 && r.Name == "LightingGlobals") is { } lighting
                && _buffers.FirstOrDefault(b => b.Name == "LightingGlobals")?.Variables.FirstOrDefault(v => v.Name == "oitMaxEntries") is { } entries)
            {
                _oitMaxEntriesSlot = lighting.Bind;
                _oitMaxEntriesOffset = entries.Start;
            }
        }

        public Result Run()
        {
            IReadOnlyList<Instruction> instructions = _program.Instructions;
            for (int i = 0; i < instructions.Count; i++)
            {
                Instruction instruction = instructions[i];
                if (Declaration(instruction))
                    continue;
                Statement(instruction, i + 1 < instructions.Count ? instructions[i + 1] : null);
            }

            if (WritesPs4Gbuffer)
                _helpers.Add("__gbuffer_specular");

            var text = new StringBuilder();
            text.Append("// ").Append(VersionOf(_stage)).Append(": translated from the PC shader model ").Append(_program.Major).Append('.').Append(_program.Minor).Append(" bytecode\n\n");
            Declarations(text);
            foreach (string helper in _helpers)
                text.Append(Helpers[helper]).Append('\n');
            Main(text);
            return new Result(text.ToString(), _globalsLayout.Select(entry => entry.Variable.Name).ToList());
        }


        private bool Declaration(Instruction instruction)
        {
            List<Operand> operands = instruction.Operands;
            switch (instruction.Opcode)
            {
                case Op.DclGlobalFlags:
                    _globalFlags = instruction.Control;
                    return true;
                case Op.DclTemps:
                    _temps = (int)instruction.Extra[0];
                    return true;
                case Op.DclIndexableTemp:
                    _indexableTemps[(int)instruction.Extra[0]] = ((int)instruction.Extra[1], (int)instruction.Extra[2]);
                    return true;
                case Op.CustomData:
                    if (instruction.DataClass == 3)
                        _immediateConstants = instruction.Data;
                    return true;
                case Op.DclConstantBuffer:
                    _constantBuffers[operands[0].Register] = (int)operands[0].Indices[1].Value;
                    return true;
                case Op.DclSampler:
                    _samplers[operands[0].Register] = ((instruction.Control & 0xF) == 1) || _resources.Any(r => r.IsSampler && r.Bind == operands[0].Register && r.IsComparisonSampler);
                    return true;
                case Op.DclResource:
                    _textures[operands[0].Register] = new ResourceInfo(instruction.Control & 0x1F, ReturnKind(instruction.Extra[0]), Components(operands[0].Register, false), 0);
                    return true;
                case Op.DclResourceStructured:
                    _textures[operands[0].Register] = new ResourceInfo(12, Kind.Int, 4, (int)instruction.Extra[0]);
                    return true;
                case Op.DclResourceRaw:
                    _textures[operands[0].Register] = new ResourceInfo(11, Kind.Int, 4, 0);
                    return true;
                case Op.DclUavTyped:
                    _uavs[operands[0].Register] = new ResourceInfo(instruction.Control & 0x1F, ReturnKind(instruction.Extra[0]), Components(operands[0].Register, true), 0);
                    return true;
                case Op.DclUavStructured:
                    _uavs[operands[0].Register] = new ResourceInfo(12, Kind.Int, 4, (int)instruction.Extra[0]);
                    return true;
                case Op.DclUavRaw:
                    _uavs[operands[0].Register] = new ResourceInfo(11, Kind.Int, 4, 0);
                    return true;
                case Op.DclInputPs or Op.DclInputPsSgv or Op.DclInputPsSiv:
                    if (operands[0].Type == OperandType.Input)
                        _interpolation.Add((operands[0].Register, operands[0].Mask, instruction.Control & 0xF));
                    return true;
                case Op.DclInput or Op.DclInputSgv or Op.DclInputSiv or Op.DclOutput or Op.DclOutputSgv or Op.DclOutputSiv or Op.Nop:
                    return true;
                case Op.DclIndexRange or Op.DclThreadGroup or Op.DclGsInputPrimitive or Op.DclGsOutputTopology or Op.DclMaxOutputVertexCount
                    or Op.DclFunctionBody or Op.DclFunctionTable or Op.DclInterface or Op.DclTgsmRaw or Op.DclTgsmStructured:
                    throw Unsupported(instruction);
                default:
                    return false;
            }
        }

        private static Kind ReturnKind(uint returnTypes) => (returnTypes & 0xF) switch
        {
            3 => Kind.Int,
            4 => Kind.Uint,
            1 or 2 or 5 => Kind.Float,
            6 => Kind.Int,
            var other => throw new NotSupportedException($"resource return type {other}"),
        };

        private int Components(int register, bool unorderedAccess)
        {
            T7Dxbc.Resource? resource = _resources.FirstOrDefault(r => (unorderedAccess ? r.IsUnorderedAccess : r.IsTextureRegister)
                && register >= r.Bind && register < r.Bind + r.Count);
            return resource?.Components ?? 4;
        }

        private void Declarations(StringBuilder text)
        {
            IEnumerable<ResourceInfo> declared = _textures.Where(t => !_absentTextures.Contains(t.Key)).Select(t => t.Value);
            foreach (int stride in declared.Concat(_uavs.Values).Where(r => r.Dimension == 12).Select(r => r.Stride).Distinct().Order())
                text.Append($"struct T7Words{stride}\n{{\n  int d[{Math.Max(1, stride / 4)}];\n}};\n\n");

            foreach ((int slot, int rows) in _constantBuffers)
            {
                string name = _resources.FirstOrDefault(r => r.Type == 0 && r.Bind == slot)?.Name ?? "";
                T7Dxbc.ConstantBuffer? reflected = _buffers.FirstOrDefault(b => b.Name == name);
                int size = slot == _globalsSlot ? _globalsRows : Math.Max(rows, reflected == null ? 0 : (reflected.Size + 15) / 16);
                string identifier = name == "$Globals" ? "__GLOBAL_CB__" : Identifier(name, $"ConstantBuffer{slot}");
                text.Append($"ConstantBuffer {identifier} : register(b{slot})\n{{\n  float4 cb{slot}[{Math.Max(1, size)}];\n}};\n\n");
            }

            foreach ((int slot, ResourceInfo info) in _textures)
            {
                if (_absentTextures.Contains(slot))
                    continue;
                int register = _textureRemap != null && _textureRemap.TryGetValue(slot, out int moved) ? moved : slot;
                text.Append($"{TextureType(info, false)} t{slot} : register(t{register});\n");
            }
            foreach ((int slot, bool comparison) in _samplers)
            {
                int register = _samplerRemap != null && _samplerRemap.TryGetValue(slot, out int moved) ? moved : slot;
                text.Append($"{(comparison ? "SamplerComparisonState" : "SamplerState")} s{slot} : register(s{register});\n");
            }
            foreach ((int slot, ResourceInfo info) in _uavs)
                text.Append($"{TextureType(info, true)} u{slot} : register(u{slot});\n");
            if (_textures.Count + _samplers.Count + _uavs.Count > 0)
                text.Append('\n');

            if (_immediateConstants is { Length: > 0 } values)
            {
                int rows = values.Length / 4;
                text.Append($"static const int4 icb[{rows}] =\n{{\n");
                for (int row = 0; row < rows; row++)
                {
                    text.Append("  int4(").Append(string.Join(", ", Enumerable.Range(0, 4).Select(c => IntLiteral(values[4 * row + c])))).Append(')')
                        .Append(row + 1 < rows ? ",\n" : "\n");
                }
                text.Append("};\n\n");
            }
        }

        private static string Identifier(string name, string fallback)
        {
            var identifier = new StringBuilder();
            foreach (char c in name)
                identifier.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
            return identifier.Length == 0 || char.IsAsciiDigit(identifier[0]) ? fallback : identifier.ToString();
        }

        private static string TextureType(ResourceInfo info, bool unorderedAccess)
        {
            string element = TypeName(info.Kind, info.Components);
            string prefix = unorderedAccess ? "RW_" : "";
            return info.Dimension switch
            {
                1 => $"{prefix}DataBuffer<{element}>",
                2 => $"{prefix}Texture1D<{element}>",
                3 => $"{prefix}Texture2D<{element}>",
                4 when !unorderedAccess => $"MS_Texture2D<{element}>",
                5 => $"{prefix}Texture3D<{element}>",
                6 when !unorderedAccess => $"TextureCube<{element}>",
                7 => $"{prefix}Texture1D_Array<{element}>",
                8 => $"{prefix}Texture2D_Array<{element}>",
                9 when !unorderedAccess => $"MS_Texture2D_Array<{element}>",
                10 when !unorderedAccess => $"TextureCube_Array<{element}>",
                11 => $"{prefix}ByteBuffer",
                12 => $"{prefix}RegularBuffer<T7Words{info.Stride}>",
                _ => throw new NotSupportedException($"resource dimension {info.Dimension}"),
            };
        }


        private static SortedDictionary<int, Kind> Varyings(IEnumerable<T7Dxbc.SignatureElement> elements)
        {
            var registers = new SortedDictionary<int, Kind>();
            foreach (T7Dxbc.SignatureElement element in elements)
            {
                if (SystemValue(element) != 0 || element.Register < 0)
                    continue;
                Kind kind = SignatureKind(element.ComponentType);
                registers[element.Register] = registers.TryGetValue(element.Register, out Kind existing) && existing != Kind.Float ? existing : kind;
            }
            return registers;
        }

        private void Main(StringBuilder text)
        {
            var parameters = new List<string>();
            var copies = new List<string>();
            Dictionary<int, (int Xyz, bool W)> unitVectors = PackedUnitVectorInputs();
            SortedSet<int> inputRegisters = _inputRegisters;
            if (_stage == "ps")
            {
                SortedDictionary<int, Kind> varyings = Varyings(_inputs);
                int last = varyings.Count == 0 ? -1 : varyings.Keys.Max();
                for (int r = 0; r <= last; r++)
                {
                    if (!varyings.TryGetValue(r, out Kind kind))
                    {
                        parameters.Add($"float4 in_r{r} : TEXCOORD{r}");
                        continue;
                    }
                    inputRegisters.Add(r);
                    parameters.Add($"{InterpolationModifier(r, kind)}{TypeName(kind, 4)} in_r{r} : TEXCOORD{r}");
                    copies.Add($"v{r} = {Convert($"in_r{r}", kind, Kind.Int, 4)};");
                }
            }
            for (int i = 0; i < _inputs.Count; i++)
            {
                T7Dxbc.SignatureElement element = _inputs[i];
                string name = $"in{i}";
                int[] components = MaskBits(element.Mask);
                int width = components.Length;
                string register = element.Register >= 0 ? $"v{element.Register}.{Swizzle(components)}" : "";
                if (_stage == "ps" && SystemValue(element) == 0)
                    continue;
                if (element.Register >= 0)
                    inputRegisters.Add(element.Register);
                switch (SystemValue(element))
                {
                    case 1:
                        parameters.Add($"float{width} {name} : S_POSITION");
                        copies.Add($"{register} = asint({name});");
                        continue;
                    case 9:
                        parameters.Add($"bool {name} : S_FRONT_FACE");
                        copies.Add($"{register} = {name} ? -1 : 0;");
                        continue;
                    case 6 or 7 or 8 or 10:
                        string system = element.SystemValue switch { 6 => "S_VERTEX_ID", 7 => "S_PRIMITIVE_ID", 8 => "S_INSTANCE_ID", _ => "S_SAMPLE_INDEX" };
                        parameters.Add($"uint {name} : {system}");
                        copies.Add($"{register} = (int)({name});");
                        continue;
                    case 0:
                        break;
                    default:
                        throw new NotSupportedException($"input system value {element.SystemValue} ({element.Semantic})");
                }
                Kind kind = SignatureKind(element.ComponentType);
                string type = TypeName(kind, width);
                if (_stage == "vs" && kind == Kind.Uint && width == 1 && element.Semantic.Equals("TEXCOORD", StringComparison.OrdinalIgnoreCase) && element.Index == 15)
                {
                    parameters.Add($"uint {name} : S_INSTANCE_ID");
                    copies.Add($"{register} = (int)({name});");
                    continue;
                }
                parameters.Add($"{type} {name} : {element.Semantic.ToUpperInvariant()}{element.Index}");
                copies.Add($"{register} = {Convert(name, kind, Kind.Int, width)};");
                if (unitVectors.TryGetValue(element.Register, out (int Xyz, bool W) packed) && kind == Kind.Float)
                {
                    string xyz = Swizzle(MaskBits(packed.Xyz & element.Mask));
                    string v = $"v{element.Register}";
                    if (xyz.Length > 0)
                        copies.Add($"{v}.{xyz} = asint(asfloat({v}.{xyz}) * 0.49951124f + 0.50048876f);");
                    if (packed.W && (element.Mask & 8) != 0)
                        copies.Add($"{v}.w = asint((asfloat({v}.w) != 0.0f) ? 1.0f : 0.0f);");
                }
            }

            SortedSet<int> outputRegisters = _outputRegisters;
            var writes = new List<string>();
            if (_stage == "vs")
            {
                SortedDictionary<int, Kind> varyings = Varyings(_outputs);
                int last = varyings.Count == 0 ? -1 : varyings.Keys.Max();
                for (int r = 0; r <= last; r++)
                {
                    if (!varyings.TryGetValue(r, out Kind kind) || !_writtenOutputs.Contains(r))
                    {
                        parameters.Add($"out float4 out_r{r} : TEXCOORD{r}");
                        continue;
                    }
                    outputRegisters.Add(r);
                    parameters.Add($"out {TypeName(kind, 4)} out_r{r} : TEXCOORD{r}");
                    writes.Add($"out_r{r} = {Convert($"o{r}", Kind.Int, kind, 4)};");
                }
            }
            bool gbuffer = WritesPs4Gbuffer;
            bool hasTarget3 = _outputs.Any(e => IsTarget(e, 3));
            for (int i = 0; i < _outputs.Count; i++)
            {
                T7Dxbc.SignatureElement element = _outputs[i];
                string name = $"out{i}";
                int[] components = MaskBits(element.Mask);
                int width = components.Length;
                Kind kind = SignatureKind(element.ComponentType);
                string register = $"o{element.Register}.{Swizzle(components)}";
                switch (SystemValue(element))
                {
                    case 1:
                        parameters.Add($"out float{width} {name} : S_POSITION");
                        outputRegisters.Add(element.Register);
                        writes.Add($"{name} = asfloat({register});");
                        break;
                    case 64:
                        parameters.Add($"out {TypeName(kind, width)} {name} : S_TARGET_OUTPUT{element.Index}");
                        outputRegisters.Add(element.Register);
                        if (gbuffer && element.Index == 2)
                        {
                            writes.Add($"{name} = __gbuffer_specular(asfloat(o{element.Register}), gbSpecular, gbCaptured, {(_targets.Decal ? "1.0f" : "0.0f")});");
                            if (_targets.Decal && !hasTarget3)
                            {
                                parameters.Add("out float4 out_decal : S_TARGET_OUTPUT3");
                                writes.Add($"out_decal = float4(0.0f, 0.0f, 0.0f, asfloat(o{element.Register}.w));");
                            }
                        }
                        else
                        {
                            writes.Add($"{name} = {Convert(register, Kind.Int, kind, width)};");
                        }
                        break;
                    case 65:
                        parameters.Add($"out float {name} : S_DEPTH_OUTPUT");
                        _writesDepth = true;
                        writes.Add($"{name} = asfloat(oDepth);");
                        break;
                    case 66:
                        parameters.Add($"out uint {name} : S_COVERAGE");
                        _writesCoverage = true;
                        writes.Add($"{name} = (uint)(oMask);");
                        break;
                    case 0 when _stage == "vs":
                        break;
                    case 0:
                        parameters.Add($"out {TypeName(kind, width)} {name} : {element.Semantic.ToUpperInvariant()}{element.Index}");
                        outputRegisters.Add(element.Register);
                        writes.Add($"{name} = {Convert(register, Kind.Int, kind, width)};");
                        break;
                    default:
                        throw new NotSupportedException($"output system value {element.SystemValue} ({element.Semantic})");
                }
            }

            if (_stage == "ps" && (_globalFlags & 4) != 0 && !_discards && !_writesDepth)
                text.Append("[FORCE_EARLY_DEPTH_STENCIL]\n");
            text.Append("void main(").Append(string.Join(",\n  ", parameters)).Append(")\n{\n");
            if (_temps > 0)
                text.Append("  int4 ").Append(string.Join(", ", Enumerable.Range(0, _temps).Select(r => $"r{r} = 0"))).Append(";\n");
            foreach ((int register, (int size, _)) in _indexableTemps)
                text.Append($"  int4 x{register}[{size}];\n");
            if (inputRegisters.Count > 0)
                text.Append("  int4 ").Append(string.Join(", ", inputRegisters.Select(r => $"v{r} = 0"))).Append(";\n");
            if (outputRegisters.Count > 0)
                text.Append("  int4 ").Append(string.Join(", ", outputRegisters.Select(r => $"o{r} = 0"))).Append(";\n");
            if (_writesDepth)
                text.Append("  int oDepth = 0;\n");
            if (_writesCoverage)
                text.Append("  int oMask = 0;\n");
            if (_targets.Gbuffer)
                text.Append("  float3 gbSpecular = 0.0f;\n  float gbCaptured = 0.0f;\n");
            foreach (string copy in copies)
                text.Append("  ").Append(copy).Append('\n');
            text.Append(_code.Replace("RETURN", string.Join(" ", writes)));
            text.Append("}\n");
        }

        private Dictionary<int, (int Xyz, bool W)> PackedUnitVectorInputs()
        {
            var xyz = new Dictionary<int, int>();
            var w = new HashSet<int>();
            if (_stage != "vs")
                return [];
            foreach (Instruction instruction in _program.Instructions)
            {
                if (instruction.Opcode != Op.Mad || instruction.Operands.Count != 4)
                    continue;
                Operand destination = instruction.Operands[0], source = instruction.Operands[1], scale = instruction.Operands[2], bias = instruction.Operands[3];
                if (source.Type != OperandType.Input || source.Register < 0 || source.Modifier != 0
                    || scale.Type != OperandType.Immediate32 || bias.Type != OperandType.Immediate32)
                    continue;
                foreach (int c in MaskComponents(destination))
                {
                    uint s = scale.Values[scale.Values.Length == 1 ? 0 : c], b = bias.Values[bias.Values.Length == 1 ? 0 : c];
                    int component = source.Swizzle[c];
                    if (s == UnitVectorScale && b == UnitVectorBias && component < 3)
                        xyz[source.Register] = xyz.GetValueOrDefault(source.Register) | (1 << component);
                    else if (s == 0x40000000 && b == 0xBF800000 && component == 3)
                        w.Add(source.Register);
                }
            }
            return xyz.ToDictionary(p => p.Key, p => (p.Value, w.Contains(p.Key)));
        }

        private static int SystemValue(T7Dxbc.SignatureElement element)
        {
            if (element.SystemValue != 0)
                return element.SystemValue;
            string name = element.Semantic.ToUpperInvariant();
            return name switch
            {
                "SV_POSITION" => 1,
                "SV_VERTEXID" => 6,
                "SV_PRIMITIVEID" => 7,
                "SV_INSTANCEID" => 8,
                "SV_ISFRONTFACE" => 9,
                "SV_SAMPLEINDEX" => 10,
                "SV_TARGET" => 64,
                "SV_DEPTH" => 65,
                "SV_COVERAGE" => 66,
                _ when name.StartsWith("SV_", StringComparison.Ordinal) => -1,
                _ => 0,
            };
        }

        private string InterpolationModifier(int register, Kind kind)
        {
            if (kind != Kind.Float)
                return "nointerp ";
            int mode = _interpolation.FirstOrDefault(i => i.Register == register).Mode;
            return mode switch
            {
                1 => "nointerp ",
                3 => "centroid ",
                4 => "nopersp ",
                5 => "nopersp centroid ",
                6 => "sample ",
                7 => "nopersp sample ",
                _ => "",
            };
        }

        private static Kind SignatureKind(int componentType) => componentType switch
        {
            1 => Kind.Uint,
            2 => Kind.Int,
            3 => Kind.Float,
            _ => throw new NotSupportedException($"signature component type {componentType}"),
        };


        private void Emit(string line) => _code.Append(' ', 2 * _indent).Append(line).Append('\n');

        private string Unique() => $"q{_unique++}";

        private static NotSupportedException Unsupported(Instruction instruction) => new($"the {instruction.Name} instruction is not translated");

        private void Statement(Instruction instruction, Instruction? next)
        {
            List<Operand> o = instruction.Operands;
            bool saturate = instruction.Saturate;
            switch (instruction.Opcode)
            {
                case Op.Add: Binary(instruction, Kind.Float, "{0} + {1}"); break;
                case Op.Mul: Binary(instruction, Kind.Float, "{0} * {1}"); break;
                case Op.Div: Binary(instruction, Kind.Float, "{0} / {1}"); break;
                case Op.Min: Binary(instruction, Kind.Float, "min({0}, {1})"); break;
                case Op.Max: Binary(instruction, Kind.Float, "max({0}, {1})"); break;
                case Op.Mad: Ternary(instruction, Kind.Float, "{0} * {1} + {2}"); break;
                case Op.IAdd: Binary(instruction, Kind.Int, "{0} + {1}"); break;
                case Op.IMad: Ternary(instruction, Kind.Int, "{0} * {1} + {2}"); break;
                case Op.UMad: Ternary(instruction, Kind.Uint, "{0} * {1} + {2}"); break;
                case Op.IMin: Binary(instruction, Kind.Int, "min({0}, {1})"); break;
                case Op.IMax: Binary(instruction, Kind.Int, "max({0}, {1})"); break;
                case Op.UMin: Binary(instruction, Kind.Uint, "min({0}, {1})"); break;
                case Op.UMax: Binary(instruction, Kind.Uint, "max({0}, {1})"); break;
                case Op.And: Binary(instruction, Kind.Int, "{0} & {1}"); break;
                case Op.Or: Binary(instruction, Kind.Int, "{0} | {1}"); break;
                case Op.Xor: Binary(instruction, Kind.Int, "{0} ^ {1}"); break;
                case Op.IShl: Binary(instruction, Kind.Int, "{0} << ({1} & 31)"); break;
                case Op.IShr: Binary(instruction, Kind.Int, "{0} >> ({1} & 31)"); break;
                case Op.UShr: Binary(instruction, Kind.Uint, "{0} >> ({1} & 31u)"); break;
                case Op.Not: Unary(instruction, Kind.Int, Kind.Int, "~{0}"); break;
                case Op.INeg: Unary(instruction, Kind.Int, Kind.Int, "-{0}"); break;
                case Op.Exp: Unary(instruction, Kind.Float, Kind.Float, "exp2({0})"); break;
                case Op.Log: Unary(instruction, Kind.Float, Kind.Float, "log2({0})"); break;
                case Op.Sqrt: Unary(instruction, Kind.Float, Kind.Float, "sqrt({0})"); break;
                case Op.Rsq: Unary(instruction, Kind.Float, Kind.Float, "rsqrt({0})"); break;
                case Op.Rcp: Unary(instruction, Kind.Float, Kind.Float, "1.0f / {0}"); break;
                case Op.Frc: Unary(instruction, Kind.Float, Kind.Float, "frac({0})"); break;
                case Op.RoundNe: Unary(instruction, Kind.Float, Kind.Float, "round({0})"); break;
                case Op.RoundNi: Unary(instruction, Kind.Float, Kind.Float, "floor({0})"); break;
                case Op.RoundPi: Unary(instruction, Kind.Float, Kind.Float, "ceil({0})"); break;
                case Op.RoundZ: Unary(instruction, Kind.Float, Kind.Float, "trunc({0})"); break;
                case Op.DerivRtx: Unary(instruction, Kind.Float, Kind.Float, "ddx({0})"); break;
                case Op.DerivRty: Unary(instruction, Kind.Float, Kind.Float, "ddy({0})"); break;
                case Op.DerivRtxCoarse: Unary(instruction, Kind.Float, Kind.Float, "ddx_coarse({0})"); break;
                case Op.DerivRtyCoarse: Unary(instruction, Kind.Float, Kind.Float, "ddy_coarse({0})"); break;
                case Op.DerivRtxFine: Unary(instruction, Kind.Float, Kind.Float, "ddx_fine({0})"); break;
                case Op.DerivRtyFine: Unary(instruction, Kind.Float, Kind.Float, "ddy_fine({0})"); break;
                case Op.ItoF: Unary(instruction, Kind.Int, Kind.Float, "({1})({0})"); break;
                case Op.UtoF: Unary(instruction, Kind.Uint, Kind.Float, "({1})({0})"); break;
                case Op.FtoI: Unary(instruction, Kind.Float, Kind.Int, "({1})({0})"); break;
                case Op.FtoU: Unary(instruction, Kind.Float, Kind.Uint, "({1})({0})"); break;
                case Op.Eq: Compare(instruction, Kind.Float, "=="); break;
                case Op.Ne: Compare(instruction, Kind.Float, "!="); break;
                case Op.Lt: Compare(instruction, Kind.Float, "<"); break;
                case Op.Ge: Compare(instruction, Kind.Float, ">="); break;
                case Op.IEq: Compare(instruction, Kind.Int, "=="); break;
                case Op.INe: Compare(instruction, Kind.Int, "!="); break;
                case Op.ILt: Compare(instruction, Kind.Int, "<"); break;
                case Op.IGe: Compare(instruction, Kind.Int, ">="); break;
                case Op.ULt: Compare(instruction, Kind.Uint, "<"); break;
                case Op.UGe: Compare(instruction, Kind.Uint, ">="); break;
                case Op.Dp2: Dot(instruction, 2); break;
                case Op.Dp3: Dot(instruction, 3); break;
                case Op.Dp4: Dot(instruction, 4); break;
                case Op.F16toF32: PerComponent(instruction, "__f16tof32", 1); break;
                case Op.FirstBitLo: PerComponent(instruction, "__firstbit_lo", 1); break;
                case Op.FirstBitHi: PerComponent(instruction, "__firstbit_hi", 1); break;
                case Op.FirstBitShi: PerComponent(instruction, "__firstbit_shi", 1); break;
                case Op.CountBits: PerComponent(instruction, "__countbits", 1); break;
                case Op.UBfe: PerComponent(instruction, "__ubfe", 3); break;
                case Op.IBfe: PerComponent(instruction, "__ibfe", 3); break;
                case Op.Bfi: PerComponent(instruction, "__bfi", 4); break;

                case Op.Mov:
                {
                    int[] components = MaskComponents(o[0]);
                    Store(o[0], LoadTypeless(o[1], Sources(o[1], components)), Kind.Int, saturate);
                    break;
                }
                case Op.MovC:
                {
                    int[] components = MaskComponents(o[0]);
                    if (_targets.Gbuffer && IsChromaSelect(o, components))
                    {
                        string luma = LoadComponents(o[2], [o[2].Swizzle[components[0]]], Kind.Float);
                        string co = LoadComponents(o[2], [o[2].Swizzle[components[1]]], Kind.Float);
                        string cg = LoadComponents(o[3], [o[3].Swizzle[components[1]]], Kind.Float);
                        Emit($"gbSpecular = float3({luma}, {co}, {cg}); gbCaptured = 1.0f;");
                    }
                    string mask = $"(-({TypeName(Kind.Int, components.Length)})({Load(o[1], components, Kind.Int)} != 0))";
                    Store(o[0], Select(mask, LoadTypeless(o[2], Sources(o[2], components)), LoadTypeless(o[3], Sources(o[3], components))), Kind.Int, saturate);
                    break;
                }
                case Op.SwapC:
                {
                    Emit("{");
                    _indent++;
                    var stores = new List<(Operand Destination, string Value)>();
                    for (int d = 0; d < 2; d++)
                    {
                        if (o[d].Type == OperandType.Null)
                            continue;
                        int[] components = MaskComponents(o[d]);
                        string mask = $"(-({TypeName(Kind.Int, components.Length)})({Load(o[2], components, Kind.Int)} != 0))";
                        string a = LoadTypeless(o[3], Sources(o[3], components)), b = LoadTypeless(o[4], Sources(o[4], components));
                        string temporary = Unique();
                        Emit($"{TypeName(Kind.Int, components.Length)} {temporary} = {(d == 0 ? Select(mask, b, a) : Select(mask, a, b))};");
                        stores.Add((o[d], temporary));
                    }
                    foreach ((Operand destination, string value) in stores)
                        Store(destination, value, Kind.Int);
                    _indent--;
                    Emit("}");
                    break;
                }
                case Op.SinCos:
                    TwoResults(instruction, Kind.Float, [(d, s) => $"sin({s[0]})", (d, s) => $"cos({s[0]})"], 1);
                    break;
                case Op.UDiv:
                    TwoResults(instruction, Kind.Uint, [(d, s) => $"{s[0]} / {s[1]}", (d, s) => $"{s[0]} % {s[1]}"], 2);
                    break;
                case Op.IMul or Op.UMul:
                    if (o[0].Type != OperandType.Null)
                        throw new NotSupportedException($"the high half of {instruction.Name} is not translated");
                    TwoResults(instruction, instruction.Opcode == Op.IMul ? Kind.Int : Kind.Uint, [(d, s) => "", (d, s) => $"{s[0]} * {s[1]}"], 2);
                    break;

                case Op.If:
                    Emit($"if ({LoadFirst(o[0], 1, Kind.Int)} {(instruction.TestNonZero ? "!=" : "==")} 0) {{");
                    _indent++;
                    break;
                case Op.Else:
                    _indent--;
                    Emit("} else {");
                    _indent++;
                    break;
                case Op.EndIf or Op.EndLoop:
                    _indent--;
                    Emit("}");
                    break;
                case Op.Loop:
                    Emit("for (;;) {");
                    _indent++;
                    break;
                case Op.Break:
                    Emit("break;");
                    break;
                case Op.BreakC:
                    Emit($"if ({LoadFirst(o[0], 1, Kind.Int)} {(instruction.TestNonZero ? "!=" : "==")} 0) break;");
                    break;
                case Op.Continue:
                    Emit("continue;");
                    break;
                case Op.ContinueC:
                    Emit($"if ({LoadFirst(o[0], 1, Kind.Int)} {(instruction.TestNonZero ? "!=" : "==")} 0) continue;");
                    break;
                case Op.Switch:
                    Emit($"switch ({LoadFirst(o[0], 1, Kind.Int)}) {{");
                    _indent++;
                    break;
                case Op.Case:
                    Emit($"case {IntLiteral(o[0].Values[0])}:");
                    if (next?.Opcode == Op.EndSwitch)
                        Emit("  break;");
                    break;
                case Op.Default:
                    Emit("default:");
                    if (next?.Opcode == Op.EndSwitch)
                        Emit("  break;");
                    break;
                case Op.EndSwitch:
                    _indent--;
                    Emit("}");
                    break;
                case Op.Ret:
                    Emit("{ RETURN return; }");
                    break;
                case Op.RetC:
                    Emit($"if ({LoadFirst(o[0], 1, Kind.Int)} {(instruction.TestNonZero ? "!=" : "==")} 0) {{ RETURN return; }}");
                    break;
                case Op.Discard:
                    _discards = true;
                    Emit($"if ({LoadFirst(o[0], 1, Kind.Int)} {(instruction.TestNonZero ? "!=" : "==")} 0) discard;");
                    break;

                case Op.Sample or Op.SampleL or Op.SampleB or Op.SampleD or Op.SampleC or Op.SampleCLz:
                    Sample(instruction);
                    break;
                case Op.Lod:
                {
                    (int slot, ResourceInfo info) = Texture(o[2]);
                    if (AbsentRead(o[0], o[2], Kind.Float, saturate))
                        break;
                    string coordinates = LoadFirst(o[1], CoordinateCount(info.Dimension, instruction), Kind.Float);
                    string sampler = $"s{o[3].Register}";
                    string value = $"float4(t{slot}.GetLOD({sampler}, {coordinates}), t{slot}.GetLODUnclamped({sampler}, {coordinates}), 0.0f, 0.0f)";
                    Store(o[0], Pick(value, o[2], MaskComponents(o[0])), Kind.Float, saturate);
                    break;
                }
                case Op.Ld or Op.LdMs:
                    Load(instruction);
                    break;
                case Op.ResInfo:
                {
                    (int slot, ResourceInfo info) = Texture(o[2]);
                    int returnType = instruction.Control & 3;
                    if (AbsentRead(o[0], o[2], returnType == 2 ? Kind.Uint : Kind.Float, instruction.Saturate))
                        break;
                    Emit("{");
                    _indent++;
                    string w = Unique(), h = Unique(), d = Unique(), l = Unique();
                    Emit($"uint {w} = 0u, {h} = 0u, {d} = 0u, {l} = 0u;");
                    string mip = LoadFirst(o[1], 1, Kind.Uint);
                    Emit(info.Dimension switch
                    {
                        2 => $"t{slot}.GetDimensions({mip}, {w}, {l});",
                        3 or 6 or 7 => $"t{slot}.GetDimensions({mip}, {w}, {h}, {l});",
                        5 or 8 or 10 => $"t{slot}.GetDimensions({mip}, {w}, {h}, {d}, {l});",
                        _ => throw new NotSupportedException($"resinfo on resource dimension {info.Dimension}"),
                    });
                    string value = returnType switch
                    {
                        2 => $"uint4({w}, {h}, {d}, {l})",
                        1 => $"float4(1.0f / (float)({w}), 1.0f / (float)({h}), 1.0f / (float)({d}), (float)({l}))",
                        _ => $"float4((float)({w}), (float)({h}), (float)({d}), (float)({l}))",
                    };
                    Store(o[0], Pick(value, o[2], MaskComponents(o[0])), returnType == 2 ? Kind.Uint : Kind.Float, instruction.Saturate);
                    _indent--;
                    Emit("}");
                    break;
                }
                case Op.LdStructured:
                    LoadStructured(instruction);
                    break;
                case Op.LdRaw:
                {
                    Operand buffer = o[2];
                    ResourceInfo? info = buffer.Type == OperandType.Resource ? _textures.GetValueOrDefault(buffer.Register)
                        : buffer.Type == OperandType.UnorderedAccessView ? _uavs.GetValueOrDefault(buffer.Register) : null;
                    if (info is not { Dimension: 11 })
                        throw Unsupported(instruction);
                    if (AbsentRead(o[0], buffer, Kind.Int, saturate))
                        break;
                    string name = $"{(buffer.Type == OperandType.Resource ? "t" : "u")}{buffer.Register}";
                    string offset = LoadFirst(o[1], 1, Kind.Uint);
                    string[] words = MaskComponents(o[0]).Select(c => $"(int)({name}.Load({offset} + {4 * buffer.Swizzle[c]}u))").ToArray();
                    Store(o[0], words.Length == 1 ? words[0] : $"{TypeName(Kind.Int, words.Length)}({string.Join(", ", words)})", Kind.Int, saturate);
                    break;
                }
                case Op.StoreRaw:
                {
                    ResourceInfo? info = _uavs.GetValueOrDefault(o[0].Register);
                    if (info is not { Dimension: 11 })
                        throw Unsupported(instruction);
                    string offset = LoadFirst(o[1], 1, Kind.Uint);
                    int k = 0;
                    foreach (int c in MaskComponents(o[0]))
                        Emit($"u{o[0].Register}.Store({offset} + {4 * k++}u, {LoadComponents(o[2], [o[2].Swizzle[c]], Kind.Uint)});");
                    break;
                }
                case Op.LdUavTyped:
                {
                    ResourceInfo info = _uavs.GetValueOrDefault(o[2].Register) ?? throw Unsupported(instruction);
                    string value = Extend($"u{o[2].Register}[{LoadFirst(o[1], AddressCount(info.Dimension, instruction), Kind.Uint)}]", info.Kind, info.Components);
                    Store(o[0], Pick(value, o[2], MaskComponents(o[0])), info.Kind, saturate);
                    break;
                }
                case Op.StoreUavTyped:
                {
                    ResourceInfo info = _uavs.GetValueOrDefault(o[0].Register) ?? throw Unsupported(instruction);
                    Emit($"u{o[0].Register}[{LoadFirst(o[1], AddressCount(info.Dimension, instruction), Kind.Uint)}] = {LoadFirst(o[2], info.Components, info.Kind)};");
                    break;
                }
                case Op.ImmAtomicIAdd:
                {
                    ResourceInfo info = _uavs.GetValueOrDefault(o[1].Register) ?? throw Unsupported(instruction);
                    Kind kind = info.Kind == Kind.Int ? Kind.Int : Kind.Uint;
                    string address = info.Dimension == 12 || info.Dimension == 11
                        ? throw new NotSupportedException("atomic add on a structured or raw buffer is not translated")
                        : LoadFirst(o[2], AddressCount(info.Dimension, instruction), Kind.Uint);
                    string suffix = kind == Kind.Uint ? "u" : "";
                    string value = LoadFirst(o[3], 1, kind);
                    bool oit = IsOitCounter(o[1].Register);
                    if (oit && ZFeatherFlag(kind) is string flag)
                        value = $"({value} | ({flag} << 16{suffix}))";
                    Emit("{");
                    _indent++;
                    string original = Unique();
                    Emit($"{TypeName(kind, 1)} {original};");
                    Emit($"AtomicAdd(u{o[1].Register}[{address}]{(info.Components > 1 ? ".x" : "")}, {value}, {original});");
                    Store(o[0], oit ? $"({original} & 0xFFFF{suffix})" : original, kind);
                    _indent--;
                    Emit("}");
                    break;
                }
                default:
                    throw Unsupported(instruction);
            }
        }

        private bool IsOitCounter(int register) =>
            _resources.Any(r => r.IsUnorderedAccess && r.Bind == register && r.Name.Equals("gOit_Pixels", StringComparison.OrdinalIgnoreCase));

        private string? ZFeatherFlag(Kind kind)
        {
            (T7Dxbc.Variable Variable, int Offset) entry = _globalsLayout.FirstOrDefault(e => e.Variable.Name == "zFeatherComputeSprites");
            if (entry.Variable == null)
                return null;
            string cell = $"cb{_globalsSlot}[{entry.Offset / 16}].{Xyzw[entry.Offset % 16 / 4]}";
            return kind == Kind.Uint ? $"(asuint({cell}) & 1u)" : $"(asint({cell}) & 1)";
        }

        private static string OitNodesLiteral(Kind kind) => kind switch
        {
            Kind.Float => $"asfloat({OitNodes})",
            Kind.Int => OitNodes.ToString(CultureInfo.InvariantCulture),
            _ => OitNodes.ToString(CultureInfo.InvariantCulture) + "u",
        };

        private static bool IsTarget(T7Dxbc.SignatureElement element, int index) => SystemValue(element) == 64 && element.Index == index;

        private bool WritesPs4Gbuffer => _targets.Gbuffer && _outputs.Any(e => IsTarget(e, 0))
            && _outputs.Any(e => IsTarget(e, 2) && SignatureKind(e.ComponentType) == Kind.Float && MaskBits(e.Mask).Length == 4);

        private static bool IsChromaSelect(List<Operand> o, int[] components)
        {
            if (components.Length != 2 || o.Count != 4)
                return false;
            Operand a = o[2], b = o[3];
            return a.Type == OperandType.Temp && b.Type == OperandType.Temp && a.Register >= 0 && a.Register == b.Register && a.Modifier == 0 && b.Modifier == 0
                && a.Swizzle[components[0]] == b.Swizzle[components[0]] && a.Swizzle[components[1]] != b.Swizzle[components[1]]
                && a.Swizzle[components[0]] != a.Swizzle[components[1]] && b.Swizzle[components[0]] != b.Swizzle[components[1]];
        }

        private void Unary(Instruction instruction, Kind source, Kind result, string format)
        {
            int[] components = MaskComponents(instruction.Operands[0]);
            string a = Load(instruction.Operands[1], components, source);
            Store(instruction.Operands[0], "(" + string.Format(CultureInfo.InvariantCulture, format, a, TypeName(result, components.Length)) + ")", result, instruction.Saturate);
        }

        private void Binary(Instruction instruction, Kind kind, string format)
        {
            int[] components = MaskComponents(instruction.Operands[0]);
            string a = Load(instruction.Operands[1], components, kind), b = Load(instruction.Operands[2], components, kind);
            Store(instruction.Operands[0], "(" + string.Format(CultureInfo.InvariantCulture, format, a, b) + ")", kind, instruction.Saturate);
        }

        private void Ternary(Instruction instruction, Kind kind, string format)
        {
            int[] components = MaskComponents(instruction.Operands[0]);
            string a = Load(instruction.Operands[1], components, kind), b = Load(instruction.Operands[2], components, kind),
                c = Load(instruction.Operands[3], components, kind);
            Store(instruction.Operands[0], "(" + string.Format(CultureInfo.InvariantCulture, format, a, b, c) + ")", kind, instruction.Saturate);
        }

        private void Compare(Instruction instruction, Kind kind, string comparison)
        {
            int[] components = MaskComponents(instruction.Operands[0]);
            string a = Load(instruction.Operands[1], components, kind), b = Load(instruction.Operands[2], components, kind);
            Store(instruction.Operands[0], $"(-({TypeName(Kind.Int, components.Length)})({a} {comparison} {b}))", Kind.Int);
        }

        private void Dot(Instruction instruction, int count)
        {
            int[] components = MaskComponents(instruction.Operands[0]);
            string a = LoadFirst(instruction.Operands[1], count, Kind.Float), b = LoadFirst(instruction.Operands[2], count, Kind.Float);
            Store(instruction.Operands[0], Replicate($"dot({a}, {b})", Kind.Float, components.Length), Kind.Float, instruction.Saturate);
        }

        private void PerComponent(Instruction instruction, string helper, int sources)
        {
            _helpers.Add(helper);
            int[] components = MaskComponents(instruction.Operands[0]);
            var values = components.Select(c => $"{helper}({string.Join(", ", Enumerable.Range(1, sources).Select(s => LoadComponents(instruction.Operands[s], [instruction.Operands[s].Swizzle[c]], Kind.Int)))})").ToArray();
            Store(instruction.Operands[0], values.Length == 1 ? values[0] : $"{TypeName(Kind.Int, values.Length)}({string.Join(", ", values)})", Kind.Int);
        }

        private void TwoResults(Instruction instruction, Kind kind, Func<int[], string[], string>[] formats, int sources)
        {
            List<Operand> o = instruction.Operands;
            Emit("{");
            _indent++;
            var stores = new List<(Operand Destination, string Value)>();
            for (int d = 0; d < 2; d++)
            {
                if (o[d].Type == OperandType.Null)
                    continue;
                int[] components = MaskComponents(o[d]);
                string[] loaded = Enumerable.Range(2, sources).Select(s => Load(o[s], components, kind)).ToArray();
                string temporary = Unique();
                Emit($"{TypeName(kind, components.Length)} {temporary} = {formats[d](components, loaded)};");
                stores.Add((o[d], temporary));
            }
            foreach ((Operand destination, string value) in stores)
                Store(destination, value, kind);
            _indent--;
            Emit("}");
        }

        private void Sample(Instruction instruction)
        {
            List<Operand> o = instruction.Operands;
            (int slot, ResourceInfo info) = Texture(o[2]);
            if (AbsentRead(o[0], o[2], info.Kind, instruction.Saturate))
                return;
            int count = CoordinateCount(info.Dimension, instruction);
            string coordinates = LoadFirst(o[1], count, Kind.Float);
            string sampler = $"s{o[3].Register}";
            string offset = Offset(instruction, info.Dimension);
            string texture = $"t{slot}";
            string value = instruction.Opcode switch
            {
                Op.Sample => Extend($"{texture}.Sample({sampler}, {coordinates}{offset})", info.Kind, info.Components),
                Op.SampleL => Extend($"{texture}.SampleLOD({sampler}, {coordinates}, {LoadFirst(o[4], 1, Kind.Float)}{offset})", info.Kind, info.Components),
                Op.SampleB => Extend($"{texture}.SampleBias({sampler}, {coordinates}, {LoadFirst(o[4], 1, Kind.Float)}{offset})", info.Kind, info.Components),
                Op.SampleD => Extend($"{texture}.SampleGradient({sampler}, {coordinates}, {LoadFirst(o[4], count, Kind.Float)}, {LoadFirst(o[5], count, Kind.Float)}{offset})", info.Kind, info.Components),
                Op.SampleC => Extend($"{texture}.SampleCmp({sampler}, {coordinates}, {LoadFirst(o[4], 1, Kind.Float)}{offset})", Kind.Float, 1),
                _ => Extend($"{texture}.SampleCmpLOD0({sampler}, {coordinates}, {LoadFirst(o[4], 1, Kind.Float)}{offset})", Kind.Float, 1),
            };
            Store(o[0], Pick(value, o[2], MaskComponents(o[0])), info.Kind, instruction.Saturate);
        }

        private void Load(Instruction instruction)
        {
            List<Operand> o = instruction.Operands;
            (int slot, ResourceInfo info) = Texture(o[2]);
            if (AbsentRead(o[0], o[2], info.Kind, instruction.Saturate))
                return;
            Operand address = o[1];
            string Coordinates(int count) => LoadComponents(address, address.Swizzle[..count], Kind.Int);
            string mip = LoadComponents(address, [address.Swizzle[3]], Kind.Int);
            string offset = Offset(instruction, info.Dimension);
            string call = info.Dimension switch
            {
                1 => $"t{slot}.Load({Coordinates(1)})",
                2 => $"t{slot}.Load(int2({Coordinates(1)}, {mip}){offset})",
                3 or 7 => $"t{slot}.Load(int3({Coordinates(2)}, {mip}){offset})",
                5 or 8 => $"t{slot}.Load(int4({Coordinates(3)}, {mip}){offset})",
                4 => $"t{slot}.Load({Coordinates(2)}, {LoadFirst(o[3], 1, Kind.Int)}{offset})",
                9 => $"t{slot}.Load({Coordinates(3)}, {LoadFirst(o[3], 1, Kind.Int)}{offset})",
                _ => throw new NotSupportedException($"{instruction.Name} from resource dimension {info.Dimension}"),
            };
            Store(o[0], Pick(Extend(call, info.Kind, info.Components), o[2], MaskComponents(o[0])), info.Kind, instruction.Saturate);
        }

        private void LoadStructured(Instruction instruction)
        {
            List<Operand> o = instruction.Operands;
            Operand buffer = o[3];
            ResourceInfo? info = buffer.Type == OperandType.Resource ? _textures.GetValueOrDefault(buffer.Register)
                : buffer.Type == OperandType.UnorderedAccessView ? _uavs.GetValueOrDefault(buffer.Register) : null;
            if (info is not { Dimension: 12 })
                throw Unsupported(instruction);
            if (AbsentRead(o[0], buffer, Kind.Int, instruction.Saturate))
                return;
            string element = $"{(buffer.Type == OperandType.Resource ? "t" : "u")}{buffer.Register}[{LoadFirst(o[1], 1, Kind.Uint)}]";
            string Word(int component) => o[2].Type == OperandType.Immediate32
                ? $"{element}.d[{o[2].Values[0] / 4 + component}]"
                : $"{element}.d[({LoadFirst(o[2], 1, Kind.Uint)} >> 2u) + {component}u]";
            string[] words = MaskComponents(o[0]).Select(c => Word(buffer.Swizzle[c])).ToArray();
            Store(o[0], words.Length == 1 ? words[0] : $"{TypeName(Kind.Int, words.Length)}({string.Join(", ", words)})", Kind.Int, instruction.Saturate);
        }

        private bool AbsentRead(Operand destination, Operand resource, Kind kind, bool saturate)
        {
            if (resource.Type != OperandType.Resource || !_absentTextures.Contains(resource.Register))
                return false;
            string zero = kind == Kind.Float ? "0.0f" : kind == Kind.Uint ? "0u" : "0";
            Store(destination, Pick($"{TypeName(kind, 4)}({string.Join(", ", Enumerable.Repeat(zero, 4))})", resource, MaskComponents(destination)), kind, saturate);
            return true;
        }

        private (int Slot, ResourceInfo Info) Texture(Operand operand)
        {
            if (operand.Type != OperandType.Resource || !_textures.TryGetValue(operand.Register, out ResourceInfo? info))
                throw new NotSupportedException($"operand {T7DxbcCode.Format(operand)} is not a declared texture");
            return (operand.Register, info);
        }

        private static int CoordinateCount(int dimension, Instruction instruction) => dimension switch
        {
            2 => 1,
            3 or 7 => 2,
            5 or 6 or 8 => 3,
            10 => 4,
            _ => throw new NotSupportedException($"{instruction.Name} on resource dimension {dimension}"),
        };

        private static int AddressCount(int dimension, Instruction instruction) => dimension switch
        {
            1 or 2 => 1,
            3 or 7 => 2,
            5 or 8 => 3,
            _ => throw new NotSupportedException($"{instruction.Name} on resource dimension {dimension}"),
        };

        private static string Offset(Instruction instruction, int dimension)
        {
            if (instruction.Offsets.All(v => v == 0))
                return "";
            return dimension switch
            {
                2 or 7 => $", {instruction.Offsets[0]}",
                3 or 8 or 4 or 9 => $", int2({instruction.Offsets[0]}, {instruction.Offsets[1]})",
                5 => $", int3({instruction.Offsets[0]}, {instruction.Offsets[1]}, {instruction.Offsets[2]})",
                _ => throw new NotSupportedException($"{instruction.Name} with texel offsets on resource dimension {dimension}"),
            };
        }


        private static int[] MaskComponents(Operand destination) =>
            destination.Components == 1 ? [0] : Enumerable.Range(0, 4).Where(c => (destination.Mask & (1 << c)) != 0).ToArray();

        private static int[] MaskBits(int mask) => Enumerable.Range(0, 4).Where(c => (mask & (1 << c)) != 0).ToArray();

        private static string Swizzle(IEnumerable<int> components) => string.Concat(components.Select(c => Xyzw[c]));

        private static int[] Sources(Operand operand, int[] destinationComponents) => destinationComponents.Select(c => operand.Swizzle[c]).ToArray();

        private string Load(Operand operand, int[] destinationComponents, Kind kind) => LoadComponents(operand, Sources(operand, destinationComponents), kind);

        private string LoadFirst(Operand operand, int count, Kind kind) => LoadComponents(operand, operand.Swizzle[..count], kind);

        private string LoadTypeless(Operand operand, int[] sourceComponents) => operand.Modifier == 0
            ? LoadComponents(operand, sourceComponents, Kind.Int)
            : Convert(LoadComponents(operand, sourceComponents, Kind.Float), Kind.Float, Kind.Int, sourceComponents.Length);

        private string LoadComponents(Operand operand, int[] components, Kind kind)
        {
            int count = components.Length;
            string expression;
            if (operand.Type == OperandType.Immediate32)
            {
                string[] parts = components.Select(c => Literal(operand.Values[operand.Values.Length == 1 ? 0 : c], kind)).ToArray();
                expression = count == 1 ? parts[0] : $"{TypeName(kind, count)}({string.Join(", ", parts)})";
            }
            else if (operand.Type == OperandType.ConstantBuffer && operand.Register == _globalsSlot && operand.Indices.Length == 2)
            {
                expression = Convert(Globals(operand, components), Kind.Float, kind, count);
            }
            else if (operand.Type == OperandType.ConstantBuffer && operand.Register == _oitMaxEntriesSlot && operand.Indices.Length == 2
                && operand.Indices[1].Relative == null && components.Any(c => IsOitMaxEntries(operand, c)))
            {
                string[] parts = components.Select(c => IsOitMaxEntries(operand, c)
                    ? OitNodesLiteral(kind)
                    : Convert($"cb{operand.Register}[{operand.Indices[1].Value.ToString(CultureInfo.InvariantCulture)}].{Xyzw[c]}", Kind.Float, kind, 1)).ToArray();
                expression = count == 1 ? parts[0] : $"{TypeName(kind, count)}({string.Join(", ", parts)})";
            }
            else
            {
                (string register, Kind storage, bool scalar) = Register(operand);
                string selected = scalar
                    ? count == 1 ? register : $"{TypeName(storage, count)}({string.Join(", ", Enumerable.Repeat(register, count))})"
                    : $"{register}.{Swizzle(components)}";
                expression = Convert(selected, storage, kind, count);
            }
            return operand.Modifier switch
            {
                0 => expression,
                1 when kind == Kind.Uint => $"(({TypeName(Kind.Uint, count)})(-({TypeName(Kind.Int, count)})({expression})))",
                1 => $"(-{expression})",
                2 when kind == Kind.Uint => expression,
                2 => $"abs({expression})",
                _ when kind == Kind.Uint => $"(({TypeName(Kind.Uint, count)})(-({TypeName(Kind.Int, count)})({expression})))",
                _ => $"(-abs({expression}))",
            };
        }

        private bool IsOitMaxEntries(Operand operand, int component) => operand.Indices[1].Value * 16 + component * 4 == _oitMaxEntriesOffset;

        private (string Register, Kind Storage, bool Scalar) Register(Operand operand)
        {
            switch (operand.Type)
            {
                case OperandType.Temp:
                    return ($"r{operand.Register}", Kind.Int, false);
                case OperandType.Input when operand.Indices.Length == 1 && operand.Register >= 0:
                    _inputRegisters.Add(operand.Register);
                    return ($"v{operand.Register}", Kind.Int, false);
                case OperandType.Output when operand.Register >= 0:
                    _outputRegisters.Add(operand.Register);
                    return ($"o{operand.Register}", Kind.Int, false);
                case OperandType.IndexableTemp:
                    return ($"x{operand.Register}[{Index(operand.Indices[1])}]", Kind.Int, false);
                case OperandType.ConstantBuffer when operand.Indices.Length == 2:
                    return ($"cb{operand.Register}[{Index(operand.Indices[1])}]", Kind.Float, false);
                case OperandType.ImmediateConstantBuffer when operand.Indices.Length == 1:
                    return ($"icb[{Index(operand.Indices[0])}]", Kind.Int, false);
                case OperandType.OutputDepth:
                    _writesDepth = true;
                    return ("oDepth", Kind.Int, true);
                case OperandType.OutputCoverageMask:
                    _writesCoverage = true;
                    return ("oMask", Kind.Int, true);
                default:
                    throw new NotSupportedException($"operand {T7DxbcCode.Format(operand)} (type {operand.Type}) is not translated");
            }
        }

        private string Globals(Operand operand, int[] components)
        {
            OperandIndex index = operand.Indices[1];
            string buffer = $"cb{operand.Register}";
            if (index.Relative != null)
            {
                int pc = (int)index.Value * 16 + components[0] * 4;
                (T7Dxbc.Variable Variable, int Offset) entry = _globalsLayout.FirstOrDefault(e => pc >= e.Variable.Start && pc < e.Variable.Start + e.Variable.Size);
                if (entry.Variable == null)
                    return components.Length == 1 ? "0.0f" : $"float{components.Length}({string.Join(", ", Enumerable.Repeat("0.0f", components.Length))})";
                if ((entry.Offset - entry.Variable.Start) % 16 != 0)
                    throw new NotSupportedException($"indexed $Globals read of '{entry.Variable.Name}', which moves by part of a row");
                long row = index.Value + (entry.Offset - entry.Variable.Start) / 16;
                string relative = LoadComponents(index.Relative, [index.Relative.Swizzle[0]], Kind.Int);
                return $"{buffer}[{relative}{(row == 0 ? "" : $" + {row.ToString(CultureInfo.InvariantCulture)}")}].{Swizzle(components)}";
            }
            (int Row, int Column)?[] cells = components.Select(c => GlobalsCell((int)index.Value * 16 + c * 4)).ToArray();
            if (cells.All(c => c != null) && cells.Select(c => c!.Value.Row).Distinct().Count() == 1)
                return $"{buffer}[{cells[0]!.Value.Row}].{string.Concat(cells.Select(c => Xyzw[c!.Value.Column]))}";
            string[] parts = cells.Select(c => c == null ? "0.0f" : $"{buffer}[{c.Value.Row}].{Xyzw[c.Value.Column]}").ToArray();
            return parts.Length == 1 ? parts[0] : $"float{parts.Length}({string.Join(", ", parts)})";
        }

        private (int Row, int Column)? GlobalsCell(int pcOffset)
        {
            foreach ((T7Dxbc.Variable variable, int offset) in _globalsLayout)
            {
                if (pcOffset >= variable.Start && pcOffset < variable.Start + variable.Size)
                {
                    int ps4 = offset + pcOffset - variable.Start;
                    return (ps4 / 16, ps4 % 16 / 4);
                }
            }
            return null;
        }

        private string Index(OperandIndex index)
        {
            if (index.Relative == null)
                return index.Value.ToString(CultureInfo.InvariantCulture);
            string relative = LoadComponents(index.Relative, [index.Relative.Swizzle[0]], Kind.Int);
            return index.Value == 0 ? relative : $"{relative} + {index.Value.ToString(CultureInfo.InvariantCulture)}";
        }

        private void Store(Operand destination, string expression, Kind kind, bool saturate = false)
        {
            if (destination.Type == OperandType.Null)
                return;
            int[] components = MaskComponents(destination);
            if (saturate)
            {
                expression = $"saturate({Convert(expression, kind, Kind.Float, components.Length)})";
                kind = Kind.Float;
            }
            (string register, Kind storage, bool scalar) = Register(destination);
            if (destination.Type == OperandType.Output && destination.Register >= 0)
                _writtenOutputs.Add(destination.Register);
            Emit($"{(scalar ? register : $"{register}.{Swizzle(components)}")} = {Convert(expression, kind, storage, components.Length)};");
        }

        private static string Pick(string value, Operand resource, int[] destinationComponents) =>
            $"({value}).{Swizzle(destinationComponents.Select(c => resource.Swizzle[c]))}";

        private static string Extend(string value, Kind kind, int components)
        {
            if (components >= 4)
                return value;
            string zero = kind == Kind.Float ? "0.0f" : kind == Kind.Uint ? "0u" : "0";
            return $"{TypeName(kind, 4)}({value}, {string.Join(", ", Enumerable.Repeat(zero, 4 - components))})";
        }

        private static string Select(string mask, string whenSet, string otherwise) => $"(({whenSet} & {mask}) | ({otherwise} & ~{mask}))";

        private static string Replicate(string scalar, Kind kind, int count) =>
            count == 1 ? scalar : $"(({TypeName(kind, count)})({scalar}))";

        private static string TypeName(Kind kind, int count) =>
            (kind switch { Kind.Float => "float", Kind.Int => "int", _ => "uint" }) + (count == 1 ? "" : count.ToString(CultureInfo.InvariantCulture));

        private static string Convert(string expression, Kind from, Kind to, int count)
        {
            if (from == to)
                return expression;
            return (from, to) switch
            {
                (_, Kind.Float) => $"asfloat({expression})",
                (Kind.Float, Kind.Int) => $"asint({expression})",
                (Kind.Float, Kind.Uint) => $"asuint({expression})",
                _ => $"(({TypeName(to, count)})({expression}))",
            };
        }

        private static string Literal(uint bits, Kind kind)
        {
            switch (kind)
            {
                case Kind.Float:
                {
                    float value = BitConverter.Int32BitsToSingle(unchecked((int)bits));
                    if (!float.IsFinite(value))
                        return $"asfloat({IntLiteral(bits)})";
                    if (value == 0)
                        return (bits & 0x80000000) != 0 ? "(-0.0f)" : "0.0f";
                    string text = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture).Replace('E', 'e');
                    if (!text.Contains('.') && !text.Contains('e'))
                        text += ".0";
                    return value < 0 ? $"(-{text}f)" : text + "f";
                }
                case Kind.Int:
                    return IntLiteral(bits);
                default:
                    return bits.ToString(CultureInfo.InvariantCulture) + "u";
            }
        }

        private static string IntLiteral(uint bits)
        {
            int value = unchecked((int)bits);
            return value == int.MinValue ? "(-2147483647 - 1)" : value < 0 ? $"({value.ToString(CultureInfo.InvariantCulture)})" : value.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static readonly Dictionary<string, string> Helpers = new(StringComparer.Ordinal)
    {
        ["__gbuffer_specular"] = """
            float4 __gbuffer_specular(float4 pc, float3 ycocg, float captured, float premultiplied)
            {
              float alpha = premultiplied > 0.5f ? pc.w : 1.0f;
              float inverse = abs(alpha) > 1.0e-6f ? 1.0f / alpha : 0.0f;
              float y = pc.x * inverse;
              float co = (pc.y * inverse - 0.5f) * 2.0f;
              float cg = co;
              if (captured > 0.5f)
              {
                y = ycocg.x;
                co = ycocg.y;
                cg = ycocg.z;
              }
              float t = y - 0.5f * cg;
              float g = cg + t;
              float b = t - 0.5f * co;
              float r = b + co;
              return float4(float3(r, g, b) * alpha, pc.z);
            }

            """,
        ["__f16tof32"] = """
            int __f16tof32(int value)
            {
              uint bits = (uint)value & 0xFFFFu;
              uint sign = (bits >> 15u) << 31u;
              uint exponent = (bits >> 10u) & 0x1Fu;
              uint mantissa = bits & 0x3FFu;
              if (exponent == 0u)
                return (int)((uint)asint((float)mantissa * 5.9604644775390625e-8f) | sign);
              if (exponent == 31u)
                return (int)(sign | 0x7F800000u | (mantissa << 13u));
              return (int)(sign | ((exponent + 112u) << 23u) | (mantissa << 13u));
            }

            """,
        ["__ubfe"] = """
            int __ubfe(int width, int offset, int value)
            {
              uint w = (uint)width & 31u;
              uint o = (uint)offset & 31u;
              if (w == 0u)
                return 0;
              if (w + o < 32u)
                return (int)(((uint)value << (32u - (w + o))) >> (32u - w));
              return (int)((uint)value >> o);
            }

            """,
        ["__ibfe"] = """
            int __ibfe(int width, int offset, int value)
            {
              uint w = (uint)width & 31u;
              uint o = (uint)offset & 31u;
              if (w == 0u)
                return 0;
              if (w + o < 32u)
                return (value << (int)(32u - (w + o))) >> (int)(32u - w);
              return value >> (int)o;
            }

            """,
        ["__bfi"] = """
            int __bfi(int width, int offset, int value, int destination)
            {
              uint w = (uint)width & 31u;
              uint o = (uint)offset & 31u;
              uint mask = (~(0xFFFFFFFFu << w)) << o;
              return (int)((((uint)value << o) & mask) | ((uint)destination & ~mask));
            }

            """,
        ["__firstbit_lo"] = """
            int __firstbit_lo(int value)
            {
              uint v = (uint)value;
              if (v == 0u)
                return -1;
              int n = 0;
              if ((v & 0xFFFFu) == 0u) { n += 16; v >>= 16u; }
              if ((v & 0xFFu) == 0u) { n += 8; v >>= 8u; }
              if ((v & 0xFu) == 0u) { n += 4; v >>= 4u; }
              if ((v & 0x3u) == 0u) { n += 2; v >>= 2u; }
              if ((v & 0x1u) == 0u) { n += 1; }
              return n;
            }

            """,
        ["__firstbit_hi"] = """
            int __firstbit_hi(int value)
            {
              uint v = (uint)value;
              if (v == 0u)
                return -1;
              int n = 0;
              if ((v & 0xFFFF0000u) == 0u) { n += 16; v <<= 16u; }
              if ((v & 0xFF000000u) == 0u) { n += 8; v <<= 8u; }
              if ((v & 0xF0000000u) == 0u) { n += 4; v <<= 4u; }
              if ((v & 0xC0000000u) == 0u) { n += 2; v <<= 2u; }
              if ((v & 0x80000000u) == 0u) { n += 1; }
              return n;
            }

            """,
        ["__firstbit_shi"] = """
            int __firstbit_shi(int value)
            {
              uint v = value < 0 ? ~(uint)value : (uint)value;
              if (v == 0u)
                return -1;
              int n = 0;
              if ((v & 0xFFFF0000u) == 0u) { n += 16; v <<= 16u; }
              if ((v & 0xFF000000u) == 0u) { n += 8; v <<= 8u; }
              if ((v & 0xF0000000u) == 0u) { n += 4; v <<= 4u; }
              if ((v & 0xC0000000u) == 0u) { n += 2; v <<= 2u; }
              if ((v & 0x80000000u) == 0u) { n += 1; }
              return n;
            }

            """,
        ["__countbits"] = """
            int __countbits(int value)
            {
              uint v = (uint)value;
              v = v - ((v >> 1u) & 0x55555555u);
              v = (v & 0x33333333u) + ((v >> 2u) & 0x33333333u);
              return (int)((((v + (v >> 4u)) & 0x0F0F0F0Fu) * 0x01010101u) >> 24u);
            }

            """,
    };
}
