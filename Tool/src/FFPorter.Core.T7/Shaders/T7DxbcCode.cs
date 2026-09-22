using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace FFPorter.Core.T7.Shaders;

public static class T7DxbcCode
{
    public static readonly string[] OpcodeNames =
    [
        "add", "and", "break", "breakc", "call", "callc", "case", "continue", "continuec", "cut", "default", "deriv_rtx", "deriv_rty",
        "discard", "div", "dp2", "dp3", "dp4", "else", "emit", "emitthencut", "endif", "endloop", "endswitch", "eq", "exp", "frc", "ftoi",
        "ftou", "ge", "iadd", "if", "ieq", "ige", "ilt", "imad", "imax", "imin", "imul", "ine", "ineg", "ishl", "ishr", "itof", "label",
        "ld", "ld_ms", "log", "loop", "lt", "mad", "min", "max", "customdata", "mov", "movc", "mul", "ne", "nop", "not", "or", "resinfo",
        "ret", "retc", "round_ne", "round_ni", "round_pi", "round_z", "rsq", "sample", "sample_c", "sample_c_lz", "sample_l", "sample_d",
        "sample_b", "sqrt", "switch", "sincos", "udiv", "ult", "uge", "umul", "umad", "umax", "umin", "ushr", "utof", "xor",
        "dcl_resource", "dcl_constantbuffer", "dcl_sampler", "dcl_indexRange", "dcl_outputtopology", "dcl_inputprimitive",
        "dcl_maxout", "dcl_input", "dcl_input_sgv", "dcl_input_siv", "dcl_input_ps", "dcl_input_ps_sgv", "dcl_input_ps_siv",
        "dcl_output", "dcl_output_sgv", "dcl_output_siv", "dcl_temps", "dcl_indexableTemp", "dcl_globalFlags", "reserved0",
        "lod", "gather4", "sample_pos", "sample_info", "reserved1", "hs_decls", "hs_control_point_phase", "hs_fork_phase",
        "hs_join_phase", "emit_stream", "cut_stream", "emitthencut_stream", "fcall", "bufinfo", "deriv_rtx_coarse", "deriv_rtx_fine",
        "deriv_rty_coarse", "deriv_rty_fine", "gather4_c", "gather4_po", "gather4_po_c", "rcp", "f32tof16", "f16tof32", "uaddc",
        "usubb", "countbits", "firstbit_hi", "firstbit_lo", "firstbit_shi", "ubfe", "ibfe", "bfi", "bfrev", "swapc", "dcl_stream",
        "dcl_function_body", "dcl_function_table", "dcl_interface", "dcl_input_control_point_count",
        "dcl_output_control_point_count", "dcl_tessellator_domain", "dcl_tessellator_partitioning",
        "dcl_tessellator_output_primitive", "dcl_hs_max_tessfactor", "dcl_hs_fork_phase_instance_count",
        "dcl_hs_join_phase_instance_count", "dcl_thread_group", "dcl_uav_typed", "dcl_uav_raw", "dcl_uav_structured",
        "dcl_tgsm_raw", "dcl_tgsm_structured", "dcl_resource_raw", "dcl_resource_structured", "ld_uav_typed", "store_uav_typed",
        "ld_raw", "store_raw", "ld_structured", "store_structured", "atomic_and", "atomic_or", "atomic_xor", "atomic_cmp_store",
        "atomic_iadd", "atomic_imax", "atomic_imin", "atomic_umax", "atomic_umin", "imm_atomic_alloc", "imm_atomic_consume",
        "imm_atomic_iadd", "imm_atomic_and", "imm_atomic_or", "imm_atomic_xor", "imm_atomic_exch", "imm_atomic_cmp_exch",
        "imm_atomic_imax", "imm_atomic_imin", "imm_atomic_umax", "imm_atomic_umin", "sync", "dadd", "dmax", "dmin", "dmul", "deq",
        "dge", "dlt", "dne", "dmov", "dmovc", "dtof", "ftod", "eval_snapped", "eval_sample_index", "eval_centroid",
        "dcl_gs_instance_count", "abort", "debug_break",
    ];

    public static class Op
    {
        public const int Add = 0, And = 1, Break = 2, BreakC = 3, Call = 4, CallC = 5, Case = 6, Continue = 7, ContinueC = 8, Default = 10,
            DerivRtx = 11, DerivRty = 12, Discard = 13, Div = 14, Dp2 = 15, Dp3 = 16, Dp4 = 17, Else = 18, EndIf = 21, EndLoop = 22,
            EndSwitch = 23, Eq = 24, Exp = 25, Frc = 26, FtoI = 27, FtoU = 28, Ge = 29, IAdd = 30, If = 31, IEq = 32, IGe = 33, ILt = 34,
            IMad = 35, IMax = 36, IMin = 37, IMul = 38, INe = 39, INeg = 40, IShl = 41, IShr = 42, ItoF = 43, Label = 44, Ld = 45, LdMs = 46,
            Log = 47, Loop = 48, Lt = 49, Mad = 50, Min = 51, Max = 52, CustomData = 53, Mov = 54, MovC = 55, Mul = 56, Ne = 57, Nop = 58,
            Not = 59, Or = 60, ResInfo = 61, Ret = 62, RetC = 63, RoundNe = 64, RoundNi = 65, RoundPi = 66, RoundZ = 67, Rsq = 68,
            Sample = 69, SampleC = 70, SampleCLz = 71, SampleL = 72, SampleD = 73, SampleB = 74, Sqrt = 75, Switch = 76, SinCos = 77,
            UDiv = 78, ULt = 79, UGe = 80, UMul = 81, UMad = 82, UMax = 83, UMin = 84, UShr = 85, UtoF = 86, Xor = 87,
            DclResource = 88, DclConstantBuffer = 89, DclSampler = 90, DclIndexRange = 91, DclGsOutputTopology = 92,
            DclGsInputPrimitive = 93, DclMaxOutputVertexCount = 94, DclInput = 95, DclInputSgv = 96, DclInputSiv = 97, DclInputPs = 98,
            DclInputPsSgv = 99, DclInputPsSiv = 100, DclOutput = 101, DclOutputSgv = 102, DclOutputSiv = 103, DclTemps = 104,
            DclIndexableTemp = 105, DclGlobalFlags = 106, Lod = 108, Gather4 = 109, SamplePos = 110, SampleInfo = 111, BufInfo = 121,
            DerivRtxCoarse = 122, DerivRtxFine = 123, DerivRtyCoarse = 124, DerivRtyFine = 125, Gather4C = 126, Gather4Po = 127,
            Gather4PoC = 128, Rcp = 129, F32toF16 = 130, F16toF32 = 131, UAddC = 132, USubB = 133, CountBits = 134, FirstBitHi = 135,
            FirstBitLo = 136, FirstBitShi = 137, UBfe = 138, IBfe = 139, Bfi = 140, BfRev = 141, SwapC = 142,
            DclFunctionBody = 144, DclFunctionTable = 145, DclInterface = 146, DclThreadGroup = 155, DclUavTyped = 156, DclUavRaw = 157,
            DclUavStructured = 158, DclTgsmRaw = 159, DclTgsmStructured = 160, DclResourceRaw = 161, DclResourceStructured = 162,
            LdUavTyped = 163, StoreUavTyped = 164, LdRaw = 165, StoreRaw = 166, LdStructured = 167, StoreStructured = 168,
            ImmAtomicIAdd = 180;
    }

    public static class OperandType
    {
        public const int Temp = 0, Input = 1, Output = 2, IndexableTemp = 3, Immediate32 = 4, Immediate64 = 5, Sampler = 6, Resource = 7,
            ConstantBuffer = 8, ImmediateConstantBuffer = 9, Label = 10, InputPrimitiveId = 11, OutputDepth = 12, Null = 13,
            Rasterizer = 14, OutputCoverageMask = 15, UnorderedAccessView = 30;
    }

    public sealed record OperandIndex(long Value, Operand? Relative);

    public sealed class Operand
    {
        public int Type { get; init; }
        public int Components { get; init; }
        public int Selection { get; init; }
        public int Mask { get; init; } = 0xF;
        public int[] Swizzle { get; init; } = [0, 1, 2, 3];
        public int Modifier { get; init; }
        public OperandIndex[] Indices { get; init; } = [];
        public uint[] Values { get; init; } = [];

        public int Register => Indices.Length > 0 && Indices[0].Relative == null ? (int)Indices[0].Value : -1;
    }

    public sealed class Instruction
    {
        public int Opcode { get; init; }
        public uint Token { get; init; }
        public List<Operand> Operands { get; } = [];
        public uint[] Extra { get; set; } = [];
        public int[] Offsets { get; } = new int[3];
        public int ExtendedDimension { get; set; }
        public int ExtendedStride { get; set; }
        public uint ExtendedReturnType { get; set; }
        public uint[]? Data { get; set; }
        public int DataClass { get; set; }

        public string Name => Opcode < OpcodeNames.Length ? OpcodeNames[Opcode] : $"opcode{Opcode}";
        public bool Saturate => (Token & 0x2000) != 0;
        public bool TestNonZero => (Token & 0x40000) != 0;
        public int Control => (int)((Token >> 11) & 0x1FFF);
    }

    public sealed record Program(int Stage, int Major, int Minor, IReadOnlyList<Instruction> Instructions);

    public static Program Parse(ReadOnlySpan<byte> dxbc)
    {
        ReadOnlySpan<byte> chunk = T7Dxbc.Code(dxbc);
        if (chunk.Length < 8)
            throw new InvalidDataException("the program has no shader code chunk");
        var tokens = new uint[chunk.Length / 4];
        for (int i = 0; i < tokens.Length; i++)
            tokens[i] = BinaryPrimitives.ReadUInt32LittleEndian(chunk[(4 * i)..]);
        uint version = tokens[0];
        int length = (int)Math.Min(tokens[1], (uint)tokens.Length);
        var instructions = new List<Instruction>();
        int position = 2;
        while (position < length)
        {
            uint token = tokens[position];
            int opcode = (int)(token & 0x7FF);
            int size = (int)((token >> 24) & 0x7F);
            if (opcode == Op.CustomData)
            {
                size = (int)tokens[position + 1];
                instructions.Add(new Instruction
                {
                    Opcode = opcode, Token = token, DataClass = (int)(token >> 11),
                    Data = tokens.AsSpan(position + 2, Math.Max(0, size - 2)).ToArray(),
                });
                position += size;
                continue;
            }
            if (size == 0 || position + size > length)
                throw new InvalidDataException($"bad instruction length at token {position}");
            var instruction = new Instruction { Opcode = opcode, Token = token };
            int at = position + 1, end = position + size;
            bool extended = (token & 0x80000000) != 0 && opcode != Op.DclGlobalFlags;
            while (extended && at < end)
            {
                uint ext = tokens[at++];
                switch (ext & 0x3F)
                {
                    case 1:
                        for (int c = 0; c < 3; c++)
                            instruction.Offsets[c] = ((int)(ext >> (9 + 4 * c)) & 0xF) << 28 >> 28;
                        break;
                    case 2:
                        instruction.ExtendedDimension = (int)((ext >> 6) & 0x1F);
                        instruction.ExtendedStride = (int)((ext >> 11) & 0xFFF);
                        break;
                    case 3:
                        instruction.ExtendedReturnType = (ext >> 6) & 0xFFFF;
                        break;
                }
                extended = (ext & 0x80000000) != 0;
            }
            int operands = opcode switch
            {
                Op.DclTemps or Op.DclIndexableTemp or Op.DclGlobalFlags or Op.DclGsOutputTopology or Op.DclGsInputPrimitive
                    or Op.DclMaxOutputVertexCount or Op.DclThreadGroup or Op.DclFunctionBody or Op.DclFunctionTable or Op.DclInterface => 0,
                Op.DclResource or Op.DclConstantBuffer or Op.DclSampler or Op.DclIndexRange or Op.DclInput or Op.DclInputSgv
                    or Op.DclInputSiv or Op.DclInputPs or Op.DclInputPsSgv or Op.DclInputPsSiv or Op.DclOutput or Op.DclOutputSgv
                    or Op.DclOutputSiv or Op.DclUavTyped or Op.DclUavRaw or Op.DclUavStructured or Op.DclTgsmRaw
                    or Op.DclTgsmStructured or Op.DclResourceRaw or Op.DclResourceStructured => 1,
                _ => int.MaxValue,
            };
            while (instruction.Operands.Count < operands && at < end)
                instruction.Operands.Add(ParseOperand(tokens, ref at));
            instruction.Extra = tokens.AsSpan(at, end - at).ToArray();
            instructions.Add(instruction);
            position = end;
        }
        return new Program((int)(version >> 16), (int)((version >> 4) & 0xF), (int)(version & 0xF), instructions);
    }

    private static Operand ParseOperand(uint[] tokens, ref int at)
    {
        uint token = tokens[at++];
        int components = (int)(token & 3) switch { 0 => 0, 1 => 1, 2 => 4, _ => throw new InvalidDataException("N-component operands are not supported") };
        int type = (int)((token >> 12) & 0xFF);
        int selection = 0, mask = 0xF, modifier = 0;
        int[] swizzle = [0, 1, 2, 3];
        if (components == 4)
        {
            selection = (int)((token >> 2) & 3);
            switch (selection)
            {
                case 0:
                    mask = (int)((token >> 4) & 0xF);
                    if (mask == 0)
                        mask = 0xF;
                    break;
                case 1:
                    for (int i = 0; i < 4; i++)
                        swizzle[i] = (int)((token >> (4 + 2 * i)) & 3);
                    break;
                default:
                    swizzle = [.. Enumerable.Repeat((int)((token >> 4) & 3), 4)];
                    break;
            }
        }
        else if (components == 1)
        {
            mask = 1;
            swizzle = [0, 0, 0, 0];
        }
        bool extended = (token & 0x80000000) != 0;
        while (extended)
        {
            uint ext = tokens[at++];
            if ((ext & 0x3F) == 1)
                modifier = (int)((ext >> 6) & 0xFF);
            extended = (ext & 0x80000000) != 0;
        }
        uint[] values = [];
        if (type == OperandType.Immediate32)
        {
            values = tokens.AsSpan(at, components == 4 ? 4 : 1).ToArray();
            at += values.Length;
        }
        else if (type == OperandType.Immediate64)
            throw new InvalidDataException("64-bit immediates are not supported");
        int dimension = (int)((token >> 20) & 3);
        var indices = new OperandIndex[dimension];
        for (int d = 0; d < dimension; d++)
        {
            int representation = (int)((token >> (22 + 3 * d)) & 7);
            indices[d] = representation switch
            {
                0 => new OperandIndex(tokens[at++], null),
                1 => new OperandIndex(((long)tokens[at++] << 32) | tokens[at++], null),
                2 => new OperandIndex(0, ParseOperand(tokens, ref at)),
                3 => new OperandIndex(tokens[at++], ParseOperand(tokens, ref at)),
                _ => new OperandIndex(((long)tokens[at++] << 32) | tokens[at++], ParseOperand(tokens, ref at)),
            };
        }
        return new Operand { Type = type, Components = components, Selection = selection, Mask = mask, Swizzle = swizzle, Modifier = modifier, Indices = indices, Values = values };
    }

    private static readonly string[] OperandPrefixes =
    [
        "r", "v", "o", "x", "l", "d", "s", "t", "cb", "icb", "label", "vPrim", "oDepth", "null", "rasterizer", "oMask", "m", "fb", "ft",
        "fp", "fi", "fo", "vOutputControlPointID", "vForkInstanceID", "vJoinInstanceID", "vicp", "vocp", "vpc", "vDomain", "this", "u",
    ];

    public static string Disassemble(Program program)
    {
        var text = new StringBuilder();
        foreach (Instruction instruction in program.Instructions)
        {
            text.Append(instruction.Name);
            if (instruction.Saturate)
                text.Append("_sat");
            if (instruction.Opcode is Op.If or Op.BreakC or Op.ContinueC or Op.RetC or Op.Discard or Op.CallC)
                text.Append(instruction.TestNonZero ? "_nz" : "_z");
            if (instruction.Operands.Count > 0)
                text.Append(' ').Append(string.Join(", ", instruction.Operands.Select(Format)));
            text.Append('\n');
        }
        return text.ToString();
    }

    public static string Format(Operand operand)
    {
        var text = new StringBuilder();
        switch (operand.Modifier)
        {
            case 1:
                text.Append('-');
                break;
            case 2:
                text.Append('|');
                break;
            case 3:
                text.Append("-|");
                break;
        }
        if (operand.Type == OperandType.Immediate32)
        {
            text.Append("l(").Append(string.Join(", ", operand.Values.Select(v => "0x" + v.ToString("x8", CultureInfo.InvariantCulture)))).Append(')');
        }
        else
        {
            text.Append(operand.Type < OperandPrefixes.Length ? OperandPrefixes[operand.Type] : $"type{operand.Type}_");
            for (int d = 0; d < operand.Indices.Length; d++)
            {
                OperandIndex index = operand.Indices[d];
                bool bracket = d > 0 || index.Relative != null || operand.Type == OperandType.ImmediateConstantBuffer;
                string value = index.Relative == null ? index.Value.ToString(CultureInfo.InvariantCulture)
                    : index.Value == 0 ? Format(index.Relative) : $"{Format(index.Relative)} + {index.Value}";
                text.Append(bracket ? $"[{value}]" : value);
            }
            if (operand.Components == 4)
            {
                text.Append('.');
                if (operand.Selection == 0)
                {
                    for (int c = 0; c < 4; c++)
                        if ((operand.Mask & (1 << c)) != 0)
                            text.Append("xyzw"[c]);
                }
                else if (operand.Selection == 1)
                    text.Append(string.Concat(operand.Swizzle.Select(c => "xyzw"[c])));
                else
                    text.Append("xyzw"[operand.Swizzle[0]]);
            }
        }
        if (operand.Modifier is 2 or 3)
            text.Append('|');
        return text.ToString();
    }
}
