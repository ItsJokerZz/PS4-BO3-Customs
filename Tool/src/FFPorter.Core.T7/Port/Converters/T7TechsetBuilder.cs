using System.Buffers.Binary;
using System.Text;
using FFPorter.Core.T7.Harness;
using FFPorter.Core.T7.Link;
using FFPorter.Core.T7.Shaders;

namespace FFPorter.Core.T7.Port.Converters;

public static class T7TechsetBuilder
{
    public const int HeaderSize = 112, PcTechniqueSize = 720, Ps4TechniqueSize = 224, PcPassSize = 88, Ps4PassSize = 104,
        PcShaderSize = 40, Ps4ShaderSize = 64, PcDeclSize = 120, Ps4DeclSize = 2372, StateSize = 112, PcPasses = 8, Ps4Passes = 2;

    private const ulong Inline = ulong.MaxValue, InlineAlias = ulong.MaxValue - 1;

    private static readonly (int Field, string Stage)[] PassOrder = [(24, "vs"), (48, "vs2"), (8, "decl"), (32, "ps"), (40, "gs"), (56, "hs"), (64, "ds")];

    private const int KindConstant = 4, KindCodeTexture = 5;

    private static readonly HashSet<uint> Ps4AbsentCodeTextures = [0x47, 0x48, 0x49, 0x4A];

    private const uint VisibleDecalsCodeTexture = 0x53;

    private static readonly HashSet<uint> Ps4AbsentCodeConstants = [0x010000CE, 0x010000CF];

    private static readonly Dictionary<uint, uint> GbufferDecalStateBits = new() { [0x07104C49] = 0x0FB25649, [0x03104C49] = 0x07105649 };

    private static readonly Dictionary<byte, (string Semantic, int Index)> UsageSemantics = new()
    {
        [0] = ("POSITION", 0), [1] = ("NORMAL", 0), [2] = ("TANGENT", 0), [3] = ("COLOR", 0), [10] = ("TEXCOORD", 0),
        [11] = ("TEXCOORD", 1), [12] = ("TEXCOORD", 2), [13] = ("TEXCOORD", 3),
        [27] = ("SHARD_INDEX", 0), [28] = ("BLENDWEIGHT", 0), [29] = ("BLENDINDICES", 0),
    };

    public static void Precompile(T7PortContext context, Action<string> log)
    {
        T7ShaderCompiler? compiler = context.ShaderCompiler;
        if (compiler == null)
            return;
        var jobs = new List<Action>();
        var builder = new Builder(context, null!, compiler);
        foreach (T7Walk.Asset asset in context.Pc.Walk.Assets)
        {
            foreach (T7Walk.Registration registration in asset.Registrations)
            {
                if (registration.Type != T7AssetTypes.TechniqueSet)
                    continue;
                T7Walk.Span? header = null;
                foreach (T7Walk.Span read in asset.Reads)
                {
                    if (read.FileOffset >= registration.FilePos)
                        break;
                    if (read.Kind == T7WalkKind.Read && read.Size == HeaderSize && read.At == registration.Header)
                        header = read;
                }
                if (header is not T7Walk.Span found || T7TechsetConverter.IsStub(context.Pc.Zone, found))
                    continue;
                try
                {
                    jobs.AddRange(builder.CompileJobs(found.FileOffset, registration.Name));
                }
                catch (InvalidDataException)
                {
                }
            }
        }
        if (jobs.Count == 0)
            return;
        int before = compiler.Compiled;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, job =>
        {
            try
            {
                job();
            }
            catch (Exception error) when (error is InvalidDataException or IOException or NotSupportedException)
            {
            }
        });
        log($"shader programs: {jobs.Count} prepared, {compiler.Compiled - before} compiled in {watch.Elapsed.TotalSeconds:0} s");
    }

    public static void Convert(T7PortContext context, T7AssetRewrite rewrite)
    {
        T7ShaderCompiler compiler = context.ShaderCompiler ?? throw new InvalidDataException($"{rewrite.Label}: technique sets with shaders need the PS4 shader compiler");
        var builder = new Builder(context, rewrite, compiler);
        builder.TechniqueSet();
    }

    private sealed class Builder(T7PortContext context, T7AssetRewrite rewrite, T7ShaderCompiler compiler)
    {
        private string Label => (rewrite as T7AssetRewrite)?.Label ?? "technique set";

        public List<Action> CompileJobs(long header, string? setName)
        {
            var jobs = new List<Action>();
            for (int t = 0; t < 12; t++)
            {
                long field = header + 16 + 8 * t;
                if (Q(field) == 0 || !_pc.TryFollow(field, out long technique))
                    continue;
                for (int pass = 0; pass < Ps4Passes; pass++)
                {
                    long pc = technique + 8 + PcPassSize * pass;
                    if (!PassHasShaders(pc))
                        continue;
                    ulong argsPointer = Q(pc + 80);
                    int argCount = _zone[pc + 72];
                    byte[] args = argCount > 0 && argsPointer != 0 && _pc.TryFollow(pc + 80, out long argsAt)
                        ? _zone.AsSpan((int)argsAt, 16 * argCount).ToArray() : [];
                    int index = t;
                    if (StageOf(pc + 24, args, 0) is Stage vs)
                        jobs.Add(() => compiler.Compile("vs", vs.Dxbc, vs.Textures, vs.Samplers, vs.AbsentTextures, T7GlobalsLayout.Kept(setName, index, "vs")));
                    if (StageOf(pc + 32, args, 1) is Stage ps)
                        jobs.Add(() => compiler.Compile("ps", ps.Dxbc, ps.Textures, ps.Samplers, ps.AbsentTextures, T7GlobalsLayout.Kept(setName, index, "ps"),
                            T7PassTargets.For(setName, index)));
                }
            }
            return jobs;
        }

        private readonly T7WalkIndex _pc = context.Pc;
        private readonly byte[] _zone = context.Pc.Zone;

        private ulong Q(long at) => BinaryPrimitives.ReadUInt64LittleEndian(_zone.AsSpan((int)at));
        private uint D(long at) => BinaryPrimitives.ReadUInt32LittleEndian(_zone.AsSpan((int)at));

        private string? _setName;
        private int _technique;

        public void TechniqueSet()
        {
            T7Walk.Span header = rewrite.Take(HeaderSize);
            T7StructBuilder set = rewrite.Rebuild(header, HeaderSize);
            set.MoveAll();
            set.Commit();
            _setName = rewrite.Asset.Type == T7AssetTypes.TechniqueSet ? rewrite.Asset.Name : null;
            if (Q(header.FileOffset) == Inline)
                _setName = RenamedString(name => T7Names.ToPs4(T7AssetTypes.TechniqueSet, name));
            for (int t = 0; t < 12; t++)
            {
                ulong value = Q(header.FileOffset + 16 + 8 * t);
                if (value == InlineAlias)
                    throw new InvalidDataException($"{rewrite.Label}: technique {t} uses an alias marker");
                if (value == Inline)
                    Technique(t);
            }
            context.Fidelity?.Raise(T7Issues.TechniqueSetCompiled, _setName ?? $"technique set at 0x{header.FileOffset:x}");
        }

        private string RenamedString(Func<string, string> rename)
        {
            T7Walk.Span read = rewrite.Take(T7WalkKind.String);
            string text = Encoding.Latin1.GetString(rewrite.Bytes(read)[..^1]);
            byte[] renamed = Encoding.Latin1.GetBytes(rename(text) + "\0");
            T7StructBuilder builder = rewrite.Rebuild(read, renamed.Length);
            builder.Write(0, renamed).MapSource(0, 0, (int)Math.Min(read.Size, renamed.Length));
            builder.Commit();
            return text;
        }

        private static string TechniqueName(string name)
        {
            int hash = name.IndexOf('#');
            int dot = hash < 0 ? -1 : name.IndexOf('.', hash);
            return dot < 0 ? name : T7Names.ToPs4(T7AssetTypes.TechniqueSet, name[..dot]) + name[dot..];
        }

        private void Technique(int index)
        {
            _technique = index;
            T7Walk.Span read = rewrite.Take(PcTechniqueSize);
            long pc = read.FileOffset;
            T7StructBuilder technique = rewrite.Rebuild(read, Ps4TechniqueSize);
            technique.MapSource(0, 0, 1).CarryPointer(0, 0).CarryPointer(712, 216);
            for (int pass = 0; pass < PcPasses; pass++)
                Pass(pc, 8 + PcPassSize * pass, pass < Ps4Passes ? technique : null, 8 + Ps4PassSize * pass);
            if (Q(pc) == Inline)
                RenamedString(TechniqueName);
            if (Q(pc + 712) == Inline)
            {
                T7Walk.Span stateRead = rewrite.Take(StateSize);
                T7StructBuilder state = rewrite.Rebuild(stateRead, StateSize);
                state.MoveAll();
                uint bits = D(stateRead.FileOffset + 8);
                if (index == 3 && GbufferDecalStateBits.TryGetValue(bits, out uint ps4Bits))
                    state.U32(8, ps4Bits);
                state.Commit();
            }
            technique.Commit();
        }

        private sealed record Stage(byte[] Dxbc, IReadOnlyDictionary<int, int>? Textures, IReadOnlyDictionary<int, int>? Samplers, IReadOnlySet<int> AbsentTextures);

        private void Pass(long techniqueAt, int pcRelative, T7StructBuilder? technique, int ps4)
        {
            long pc = techniqueAt + pcRelative;
            ulong argsPointer = Q(pc + 80);
            int argCount = _zone[pc + 72];
            byte[] args = argCount > 0 && argsPointer != 0 && _pc.TryFollow(pc + 80, out long argsAt)
                ? _zone.AsSpan((int)argsAt, 16 * argCount).ToArray() : [];

            T7ShaderCompiler.Program? vs = null, psProgram = null;
            byte[]? fetch = null, decl = null;
            uint[] usage = [];
            bool[]? kept = null;
            if (technique != null)
            {
                Stage? vsStage = null, psStage = null;
                if (PassHasShaders(pc))
                {
                    foreach ((int field, string stage) in PassOrder)
                    {
                        if (Q(pc + field) != 0 && stage is "vs2" or "hs" or "ds")
                            throw new InvalidDataException($"{rewrite.Label}: {stage} shaders are not converted yet");
                    }
                    vsStage = StageOf(pc + 24, args, 0);
                    psStage = StageOf(pc + 32, args, 1);
                    if (vsStage != null)
                    {
                        vs = compiler.Compile("vs", vsStage.Dxbc, vsStage.Textures, vsStage.Samplers, vsStage.AbsentTextures, T7GlobalsLayout.Kept(_setName, _technique, "vs"));
                        fetch = T7GnmSdk.FetchShader(vs.Header);
                        if (Q(pc + 8) != 0 && _pc.TryFollow(pc + 8, out long declAt))
                            decl = Ps4Decl(_zone.AsSpan((int)declAt, PcDeclSize), T7Dxbc.InputSignature(vsStage.Dxbc));
                    }
                    if (psStage != null)
                        psProgram = compiler.Compile("ps", psStage.Dxbc, psStage.Textures, psStage.Samplers, psStage.AbsentTextures, T7GlobalsLayout.Kept(_setName, _technique, "ps"),
                            T7PassTargets.For(_setName, _technique));
                    if (vs != null && psProgram != null)
                        usage = T7GnmSdk.UsageTable(vs.Header, psProgram.Header);
                }
                args = Ps4Args(args, vsStage, psStage, out argCount, out kept);
            }

            if (technique != null)
            {
                technique.Move(pcRelative, ps4, 72).Marker(ps4 + 40, 0);
                technique.U32(ps4 + 72, (uint)usage.Length).U32(ps4 + 76, 0).Marker(ps4 + 80, usage.Length > 0 ? Inline : 0);
                technique.Move(pcRelative + 72, ps4 + 88, 8).U8(ps4 + 88, (byte)argCount);
                if (argCount == 0)
                    technique.Marker(ps4 + 96, 0);
                else
                    technique.CarryPointer(pcRelative + 80, ps4 + 96);
            }

            foreach ((int field, string stage) in PassOrder)
            {
                if (Q(pc + field) != Inline)
                    continue;
                bool emit = technique != null;
                switch (stage)
                {
                    case "decl":
                    {
                        T7Walk.Span read = rewrite.Take(PcDeclSize);
                        if (emit)
                        {
                            T7StructBuilder builder = rewrite.Rebuild(read, Ps4DeclSize);
                            builder.Write(0, decl ?? Ps4Decl(rewrite.Bytes(read), null)).MapSource(0, 0, 1);
                            builder.Commit();
                        }
                        break;
                    }
                    case "vs":
                        Shader(emit ? vs : null, emit ? fetch : null);
                        break;
                    case "ps":
                        Shader(emit ? psProgram : null, null);
                        break;
                    default:
                        Shader(null, null);
                        break;
                }
            }
            if (technique != null && usage.Length > 0)
            {
                var table = new byte[4 * usage.Length];
                for (int i = 0; i < usage.Length; i++)
                    BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(4 * i), usage[i]);
                rewrite.Insert(table);
            }
            if (argsPointer == Inline && _zone[pc + 72] > 0)
                Arguments(pc, technique != null ? args : null, kept);
        }

        private bool PassHasShaders(long pc) => Q(pc + 24) != 0 || Q(pc + 32) != 0;

        private void Arguments(long pc, byte[]? ps4Args, bool[]? kept)
        {
            int count = _zone[pc + 72];
            T7Walk.Span read = rewrite.Take(16L * count);
            ReadOnlySpan<byte> entries = rewrite.Bytes(read);
            if (ps4Args is { Length: > 0 } && kept != null)
            {
                T7StructBuilder builder = rewrite.Rebuild(read, ps4Args.Length);
                builder.Write(0, ps4Args).MapSource(0, 0, 1);
                int index = 0;
                for (int i = 0; i < count; i++)
                {
                    if (!kept[i])
                        continue;
                    if (BinaryPrimitives.ReadUInt16LittleEndian(entries[(16 * i)..]) == 0)
                        builder.CarryPointer(16 * i + 8, 16 * index + 8);
                    index++;
                }
                builder.Commit();
            }
            for (int i = 0; i < count; i++)
            {
                ushort type = BinaryPrimitives.ReadUInt16LittleEndian(entries[(16 * i)..]);
                ulong value = BinaryPrimitives.ReadUInt64LittleEndian(entries[(16 * i + 8)..]);
                if (type != 0 || value != Inline)
                    continue;
                T7Walk.Span literal = rewrite.Take(16);
                if (ps4Args != null && kept?[i] == true)
                    rewrite.CopySpan(literal);
            }
        }

        private void Shader(T7ShaderCompiler.Program? program, byte[]? fetch)
        {
            T7Walk.Span read = rewrite.Take(PcShaderSize);
            long pc = read.FileOffset;
            if (program != null)
            {
                T7StructBuilder shader = rewrite.Rebuild(read, Ps4ShaderSize);
                shader.MapSource(0, 0, 1).CarryPointer(0, 0).CarryPointer(8, 8);
                shader.Marker(16, fetch != null ? Inline : 0).U64(24, (ulong)(fetch?.Length ?? 0));
                shader.Marker(32, Inline).U64(40, (ulong)program.Code.Length);
                shader.Marker(48, Inline).U64(56, (ulong)program.Header.Length);
                shader.Commit();
            }
            if (Q(pc) == Inline)
            {
                if (program != null)
                    rewrite.Copy();
                else
                    rewrite.Take(T7WalkKind.String);
            }
            if (Q(pc + 8) == Inline)
            {
                if (program != null)
                    rewrite.Copy();
                else
                    rewrite.Take(T7WalkKind.String);
            }
            if (Q(pc + 24) != 0)
                rewrite.Take(D(pc + 32));
            if (program != null)
            {
                rewrite.Insert(program.Header);
                rewrite.Insert(program.Code);
                if (fetch != null)
                    rewrite.Insert(fetch);
            }
        }

        private Stage? StageOf(long field, byte[] args, int stage)
        {
            if (Q(field) == 0 || !_pc.TryFollow(field, out long shaderAt))
                return null;
            if (Q(shaderAt + 24) == 0 || !_pc.TryFollow(shaderAt + 24, out long programAt))
                throw new InvalidDataException($"{Label}: a shader has no program");
            byte[] dxbc = _zone.AsSpan((int)programAt, (int)D(shaderAt + 32)).ToArray();
            IReadOnlyList<T7Dxbc.Resource> resources = T7Dxbc.Resources(dxbc) ?? [];
            var materialTextures = new SortedSet<int>();
            var materialSamplers = new SortedSet<int>();
            var absent = new SortedSet<int>();
            var visibleDecals = new List<int>();
            for (int i = 0; i + 16 <= args.Length; i += 16)
            {
                ushort type = BinaryPrimitives.ReadUInt16LittleEndian(args.AsSpan(i));
                ushort slot = BinaryPrimitives.ReadUInt16LittleEndian(args.AsSpan(i + 2));
                if ((type & 0xFF) != stage)
                    continue;
                if (type >> 8 == 2)
                    materialTextures.Add(slot);
                else if (type >> 8 == 3)
                    materialSamplers.Add(slot);
                else if (type >> 8 == KindCodeTexture)
                {
                    uint code = BinaryPrimitives.ReadUInt32LittleEndian(args.AsSpan(i + 8));
                    if (Ps4AbsentCodeTextures.Contains(code))
                        absent.Add(slot);
                    else if (code == VisibleDecalsCodeTexture)
                        visibleDecals.Add(slot);
                }
            }
            if (absent.Count > 0)
                absent.UnionWith(visibleDecals);
            return new Stage(dxbc,
                FreeSlots(resources.Where(r => r.IsTextureRegister).SelectMany(r => Enumerable.Range(r.Bind, r.Count)), materialTextures),
                FreeSlots(resources.Where(r => r.IsSampler).SelectMany(r => Enumerable.Range(r.Bind, r.Count)), materialSamplers),
                absent);
        }

        private static Dictionary<int, int>? FreeSlots(IEnumerable<int> registers, SortedSet<int> material)
        {
            if (material.Count == 0)
                return null;
            var occupied = new HashSet<int>(registers.Where(r => !material.Contains(r)));
            var map = new Dictionary<int, int>();
            int slot = 0;
            foreach (int register in material)
            {
                while (occupied.Contains(slot))
                    slot++;
                map[register] = slot++;
            }
            return map;
        }

        private static byte[] Ps4Args(byte[] args, Stage? vs, Stage? ps, out int count, out bool[] kept)
        {
            using var output = new MemoryStream();
            count = 0;
            kept = new bool[args.Length / 16];
            for (int i = 0; i + 16 <= args.Length; i += 16)
            {
                byte[] entry = args.AsSpan(i, 16).ToArray();
                ushort type = BinaryPrimitives.ReadUInt16LittleEndian(entry);
                int stage = type & 0xFF, kind = type >> 8;
                if (stage == 2)
                    continue;
                Stage? owner = stage == 0 ? vs : ps;
                ushort slot = BinaryPrimitives.ReadUInt16LittleEndian(entry.AsSpan(2));
                uint code = BinaryPrimitives.ReadUInt32LittleEndian(entry.AsSpan(8));
                if ((kind == KindCodeTexture && owner?.AbsentTextures.Contains(slot) == true) || (kind == KindConstant && Ps4AbsentCodeConstants.Contains(code)))
                    continue;
                if (kind == 2 && owner?.Textures?.TryGetValue(slot, out int texture) == true)
                    BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(2), (ushort)texture);
                else if (kind == 3 && owner?.Samplers?.TryGetValue(slot, out int sampler) == true)
                    BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(2), (ushort)sampler);
                if (kind is 2 or 3 or 5 or 6)
                    BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4), 0);
                output.Write(entry);
                kept[i / 16] = true;
                count++;
            }
            return output.ToArray();
        }

        private byte[] Ps4Decl(ReadOnlySpan<byte> pc, IReadOnlyList<T7Dxbc.SignatureElement>? inputs)
        {
            int count = pc[0];
            byte[] usages = pc.Slice(2, count).ToArray();
            var position = new int[count];
            for (int i = 0; i < count; i++)
            {
                position[i] = 1000 + i;
                if (!UsageSemantics.TryGetValue(usages[i], out var semantic))
                {
                    context.Warn(T7Issues.VertexUsageUnknown, $"{rewrite.Label}: vertex stream usage {usages[i]} has no known semantic (kept in PC order)", rewrite.Asset.Name);
                    continue;
                }
                int found = inputs?.ToList().FindIndex(e => string.Equals(e.Semantic, semantic.Semantic, StringComparison.OrdinalIgnoreCase) && e.Index == semantic.Index) ?? -1;
                if (found >= 0)
                    position[i] = found;
            }
            var decl = new byte[Ps4DeclSize];
            decl[0] = (byte)count;
            int k = 1;
            foreach (int i in Enumerable.Range(0, count).OrderBy(i => position[i]).ThenBy(i => i))
                decl[k++] = usages[i];
            return decl;
        }
    }
}
