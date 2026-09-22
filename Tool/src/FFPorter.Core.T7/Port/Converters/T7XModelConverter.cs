using System.Buffers.Binary;
using FFPorter.Core.T7.Harness;
using FFPorter.Core.T7.Link;
using FFPorter.Core.T7.Streams;

namespace FFPorter.Core.T7.Port.Converters;

public sealed class T7XModelConverter : IT7AssetConverter, IT7StreamPlanner, IT7NestedConverter
{
    public const int PcModelSize = 0x188, Ps4ModelSize = 0x180, PcOnlyField = 0x120;
    public const int MeshRootSize = 0x78, SharedSize = 0x50, SurfaceSize = 0x60, VertexRecordSize = 16;

    public static float TexelDensityScale { get; set; } = 1f / 64f;

    public string Name => "xmodel";

    public bool TryConvert(T7PortContext context, T7Walk.Asset asset, T7OutputAsset output, out string? reason)
    {
        reason = null;
        if (asset.Type is not (T7AssetTypes.XModel or T7AssetTypes.XModelMesh))
            return false;
        var rewrite = new T7AssetRewrite(context, asset, output);
        try
        {
            var traversal = new Traversal(context, asset, rewrite, null);
            if (asset.Type == T7AssetTypes.XModel)
                traversal.Model();
            else
                traversal.Mesh();
            rewrite.Finish();
            return true;
        }
        catch (InvalidDataException error)
        {
            reason = error.Message;
            return false;
        }
    }

    public bool CanConvertNested(int type) => type is T7AssetTypes.XModel or T7AssetTypes.XModelMesh;

    public bool TryConvertNested(T7PortContext context, T7AssetRewrite rewrite, int type, int endRead, out string? reason)
    {
        reason = null;
        try
        {
            var traversal = new Traversal(context, rewrite.Asset, rewrite, null);
            if (type == T7AssetTypes.XModel)
                traversal.Model();
            else
                traversal.Mesh();
            return true;
        }
        catch (InvalidDataException error)
        {
            reason = error.Message;
            return false;
        }
    }

    public void PlanStreams(T7PortContext context, T7Walk.Asset asset, T7StreamPlan plan)
    {
        if (asset.Reads.Count == 0)
            return;
        var starts = new List<(int First, int Type)>();
        if (asset.Type is T7AssetTypes.XModel or T7AssetTypes.XModelMesh)
            starts.Add((0, asset.Type));
        else
        {
            List<(int First, int End, T7Walk.Registration Registration, int Index)> spans = T7CopyConverter.AllSpans(asset);
            int covered = -1;
            foreach (var span in spans)
            {
                if (span.Registration.Type is not (T7AssetTypes.XModel or T7AssetTypes.XModelMesh) || span.First < covered)
                    continue;
                starts.Add((span.First, span.Registration.Type));
                covered = span.End;
            }
        }
        foreach ((int first, int type) in starts)
        {
            try
            {
                var traversal = new Traversal(context, asset, null, plan, first);
                if (type == T7AssetTypes.XModel)
                    traversal.Model();
                else
                    traversal.Mesh();
            }
            catch (InvalidDataException error)
            {
                context.Warn(T7Issues.MeshesNotPlanned, $"{T7AssetTypes.Name(asset.Type)} '{asset.Name}': streamed meshes not planned ({error.Message})", asset.Name);
            }
        }
    }

    public static uint RepackUnitVector(uint value)
    {
        int x = (int)(value & 0x3FF) - 512, y = (int)((value >> 10) & 0x3FF) - 512, z = (int)((value >> 20) & 0x3FF) - 512;
        double length = Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
        int Component(int c)
        {
            double s = length > 0 ? Math.Round(c / length * 511.0, MidpointRounding.ToEven) : c;
            return Math.Clamp((int)s, -511, 511);
        }
        return (value & 0xC0000000) | ((uint)(Component(x) & 0x3FF)) | ((uint)(Component(y) & 0x3FF) << 10) | ((uint)(Component(z) & 0x3FF) << 20);
    }

    public static void RepackPayload(Span<byte> payload, ReadOnlySpan<byte> shared)
    {
        uint vertexCount = BinaryPrimitives.ReadUInt32LittleEndian(shared[4..]);
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(shared[0x18..]);
        uint vertexOffset = BinaryPrimitives.ReadUInt32LittleEndian(shared[0x20..]);
        if (payload.Length != size)
            throw new InvalidDataException($"mesh payload is {payload.Length} bytes, its shared record says {size}");
        if (vertexOffset + (long)vertexCount * VertexRecordSize > payload.Length)
            throw new InvalidDataException("mesh vertex records run past the payload");
        for (long i = 0; i < vertexCount; i++)
        {
            int at = (int)(vertexOffset + i * VertexRecordSize + 8);
            BinaryPrimitives.WriteUInt32LittleEndian(payload[at..], RepackUnitVector(BinaryPrimitives.ReadUInt32LittleEndian(payload[at..])));
            BinaryPrimitives.WriteUInt32LittleEndian(payload[(at + 4)..], RepackUnitVector(BinaryPrimitives.ReadUInt32LittleEndian(payload[(at + 4)..])));
        }
    }

    private sealed class Traversal(T7PortContext context, T7Walk.Asset asset, T7AssetRewrite? rewrite, T7StreamPlan? plan, int start = 0)
    {
        private const ulong Inline = ulong.MaxValue, InlineAlias = ulong.MaxValue - 1;
        private readonly byte[] _zone = context.Pc.Zone;
        private int _position = start;

        private int Position => rewrite?.Position ?? _position;

        private string Label => $"{T7AssetTypes.Name(asset.Type)} '{asset.Name}'";

        private ulong Q(long at) => BinaryPrimitives.ReadUInt64LittleEndian(_zone.AsSpan((int)at));
        private uint U32(long at) => BinaryPrimitives.ReadUInt32LittleEndian(_zone.AsSpan((int)at));
        private ushort U16(long at) => BinaryPrimitives.ReadUInt16LittleEndian(_zone.AsSpan((int)at));

        private T7Walk.Span Peek(string role, long size, T7WalkKind kind = T7WalkKind.Read)
        {
            if (Position >= asset.Reads.Count)
                throw new InvalidDataException($"{Label}: {role} expected, the PC asset has no reads left");
            T7Walk.Span read = asset.Reads[Position];
            if (read.Kind != kind || (size >= 0 && read.Size != size))
                throw new InvalidDataException($"{Label}: {role} expected ({kind}, {size} bytes); PC read {Position} is ({read.Kind}, {read.Size} bytes)");
            return read;
        }

        private long Copy(string role, long size, T7WalkKind kind = T7WalkKind.Read)
        {
            if (size == 0)
                return -1;
            T7Walk.Span read = Peek(role, size, kind);
            if (rewrite != null)
                rewrite.Copy();
            else
                _position++;
            return read.FileOffset;
        }

        private T7StructBuilder? Rebuild(string role, long size, int outputSize)
        {
            T7Walk.Span read = Peek(role, size);
            if (rewrite == null)
            {
                _position++;
                return null;
            }
            rewrite.Take();
            return rewrite.Rebuild(read, outputSize);
        }

        private void XString(long field)
        {
            if (Q(field) == Inline)
                Copy("name", -1, T7WalkKind.String);
        }

        public void Mesh()
        {
            long root = Peek("mesh root", MeshRootSize).FileOffset;
            T7StructBuilder? rootBuilder = Rebuild("mesh root", MeshRootSize, MeshRootSize)?.MoveAll();
            XString(root);
            long shared = -1;
            if (Q(root + 0x70) == Inline)
                shared = Shared();
            if (Q(root + 0x68) != 0)
            {
                int surfaces = _zone[root + 0x3C];
                long records = Copy("surfaces", (long)SurfaceSize * surfaces);
                for (int s = 0; s < surfaces && records >= 0; s++)
                {
                    long record = records + SurfaceSize * s;
                    if (Q(record + 0x10) == Inline)
                        Shared();
                    if (Q(record + 0x18) != Inline)
                        continue;
                    int lists = _zone[record + 1];
                    long vertexLists = Copy("vertex lists", 16L * lists);
                    for (int v = 0; v < lists && vertexLists >= 0; v++)
                    {
                        if (Q(vertexLists + 16 * v + 8) != Inline)
                            continue;
                        long tree = Copy("collision tree", 0x38);
                        if (Q(tree + 0x20) != 0)
                            Copy("collision tree nodes", 16L * U32(tree + 0x18));
                        if (Q(tree + 0x30) != 0)
                            Copy("collision tree leafs", 2L * U32(tree + 0x28));
                    }
                }
            }
            ulong key = Q(root + 0x48);
            if (key != 0 && shared < 0 && context.Pc.Pointers.TryGetValue(root + 0x70, out var pointer) && pointer.Kind == T7PointerKind.Packed
                && context.Pc.TryFileOffset(pointer.Target, out long at, root + 0x70))
                shared = at;
            if (key != 0 && shared >= 0 && (_zone[shared] & 1) != 0)
            {
                byte[] sharedBytes = _zone.AsSpan((int)shared, SharedSize).ToArray();
                if (plan != null)
                    RegisterStream(key, sharedBytes);
                rootBuilder?.U64(0x48, context.Ps4StreamKey(key, out _, Common.Fidelity.FidelityDimension.Geometry));
            }
            rootBuilder?.Commit();
        }

        private long Shared()
        {
            long shared = Copy("shared surface data", SharedSize);
            if (Q(shared + 0x10) == Inline && (_zone[shared] & 1) == 0)
            {
                uint size = U32(shared + 0x18);
                if (size != 0)
                {
                    T7StructBuilder? payload = Rebuild("mesh payload", size, (int)size);
                    if (payload != null)
                    {
                        payload.MoveAll();
                        RepackPayload(payload.Data, _zone.AsSpan((int)shared, SharedSize));
                        payload.Commit();
                    }
                }
            }
            return shared;
        }

        private void RegisterStream(ulong key, byte[] shared)
        {
            List<string> warnings = context.Warnings;
            T7Fidelity? fidelity = context.Fidelity;
            List<(uint LogicalOffset, byte[] Bytes)> Repack(XPakIndexRecord? record, List<(uint LogicalOffset, byte[] Bytes)> parts)
            {
                int total = parts.Sum(p => p.Bytes.Length);
                if (parts.Count == 0 || total != BinaryPrimitives.ReadUInt32LittleEndian(shared.AsSpan(0x18)))
                {
                    warnings.Add($"streamed mesh {key:x16} ('{record?.Name}') is {total} bytes, its surfaces describe {BinaryPrimitives.ReadUInt32LittleEndian(shared.AsSpan(0x18))}; copied unchanged");
                    fidelity?.RaiseStreamItem(T7Issues.MeshSizeMismatch, record?.Name);
                    return parts;
                }
                var payload = new byte[total];
                int offset = 0;
                foreach ((uint _, byte[] bytes) in parts)
                {
                    bytes.CopyTo(payload, offset);
                    offset += bytes.Length;
                }
                RepackPayload(payload, shared);
                return [(parts[0].LogicalOffset, payload)];
            }
            string signature = "xmodelmesh-normals-1:" + Convert.ToHexString(shared);
            plan!.Register(key, Repack, signature);
            plan.RegisterNamed("mesh", asset.Name, BinaryPrimitives.ReadUInt32LittleEndian(shared.AsSpan(0x18)), Repack, signature);
        }

        private void NestedMesh(long field)
        {
            if (Q(field) >= InlineAlias)
                Mesh();
        }

        public void Model()
        {
            long root = Peek("model root", PcModelSize).FileOffset;
            T7StructBuilder? builder = Rebuild("model root", PcModelSize, Ps4ModelSize);
            if (builder != null)
            {
                builder.Move(0, 0, PcOnlyField).Move(PcOnlyField + 8, PcOnlyField, PcModelSize - PcOnlyField - 8);
                builder.Commit();
            }
            int bones = _zone[root + 8], rootBones = _zone[root + 9], cosmetic = U16(root + 10);
            int total = bones + cosmetic;
            XString(root);
            foreach ((int field, string role, long size) in new[]
            {
                (0x10, "bone names", 4L * total), (0x18, "parent list", (long)(total - rootBones)), (0x20, "quats", 8L * (total - rootBones)),
                (0x28, "translations", 16L * (total - rootBones)), (0x30, "part classification", (long)total), (0x38, "base matrices", 32L * total),
            })
            {
                if (Q(root + field) == Inline && size != 0)
                    Copy(role, size);
            }
            for (int lod = 0; lod < 8; lod++)
                NestedMesh(root + 0x88 + 8 * lod);
            if (Q(root + 0xC8) != 0)
            {
                uint lods = U32(root + 0x40);
                long materials = Copy("lod materials", 24L * lods);
                for (int s = 0; s < lods && materials >= 0; s++)
                {
                    long entry = materials + 24 * s;
                    int count = U16(entry);
                    if (Q(entry + 8) != 0)
                    {
                        long pointers = Copy("material pointers", 8L * count);
                        for (int m = 0; m < count && pointers >= 0; m++)
                        {
                            if (Q(pointers + 8 * m) >= InlineAlias)
                                NestedAsset(T7AssetTypes.Material);
                        }
                    }
                    if (Q(entry + 16) != 0 && count != 0)
                    {
                        T7StructBuilder? density = Rebuild("texel density", 4L * count, 4 * count);
                        if (density != null)
                        {
                            density.MoveAll();
                            context.Fidelity?.Raise(T7Issues.TexelDensityScale);
                            for (int m = 0; m < count; m++)
                            {
                                float value = BinaryPrimitives.ReadSingleLittleEndian(density.Data.AsSpan(4 * m));
                                if (value != 0)
                                    BinaryPrimitives.WriteSingleLittleEndian(density.Data.AsSpan(4 * m), value * TexelDensityScale);
                            }
                            density.Commit();
                        }
                    }
                }
            }
            if (Q(root + 0xD8) != 0)
            {
                uint count = U32(root + 0xE0);
                long array = Copy("root 0xD8 array", 48L * count);
                for (int s = 0; s < count && array >= 0; s++)
                {
                    if (Q(array + 48 * s) != 0)
                        Copy("root 0xD8 sub-array", 48L * U32(array + 48 * s + 8));
                }
            }
            uint flags = U32(root + 0x11C);
            ulong boneInfo = Q(root + 0xE8);
            if (boneInfo != 0 && ((flags & 0x10000000) == 0 || boneInfo == Inline) && bones != 0)
                Copy("bone info", 44L * bones);
            NestedMesh(root + 0x110);
            if (Q(root + 0x128) >= InlineAlias)
                NestedAsset(T7AssetTypes.PhysPreset);
            if (Q(root + 0x130) != 0)
            {
                int geoms = _zone[root + 0x118];
                long array = Copy("phys geoms", 16L * geoms);
                for (int s = 0; s < geoms && array >= 0; s++)
                {
                    if (Q(array + 16 * s) == 0)
                        continue;
                    long list = Copy("phys geom list", 24);
                    if (Q(list + 8) == 0)
                        continue;
                    uint count = U32(list);
                    long items = Copy("phys geom items", 72L * count);
                    for (int j = 0; j < count && items >= 0; j++)
                    {
                        if (Q(items + 72 * j) != Inline)
                            continue;
                        long brush = Copy("phys brush", 112);
                        if (Q(brush + 0x20) != 0)
                        {
                            uint sides = U32(brush + 0x1C);
                            long sideArray = Copy("phys brush sides", 16L * sides);
                            for (int t = 0; t < sides && sideArray >= 0; t++)
                            {
                                if (Q(sideArray + 16 * t) == Inline)
                                    Copy("phys brush side", 20);
                            }
                        }
                        if (Q(brush + 0x60) == Inline && U32(brush + 0x58) != 0)
                            Copy("phys brush 0x60", 12L * U32(brush + 0x58));
                        if (Q(brush + 0x68) == Inline && U32(brush + 0x1C) != 0)
                            Copy("phys brush 0x68", 20L * U32(brush + 0x1C));
                    }
                }
            }
            if (Q(root + 0x138) >= InlineAlias)
                NestedAsset(T7AssetTypes.PhysConstraints);
            if (Q(root + 0x148) != 0)
            {
                uint count = U32(root + 0x140);
                long attachments = Copy("attachments", 40L * count);
                for (int s = 0; s < count && attachments >= 0; s++)
                {
                    if (Q(attachments + 40 * s) >= InlineAlias)
                        Model();
                }
            }
        }

        private void NestedAsset(int type)
        {
            T7Walk.Span first = Peek($"nested {T7AssetTypes.Name(type)}", -1);
            T7Walk.Registration? registration = null;
            foreach (T7Walk.Registration candidate in asset.Registrations)
            {
                if (candidate.Type == type && candidate.FilePos > first.FileOffset && candidate.Header == first.At
                    && (registration == null || candidate.FilePos < registration.Value.FilePos))
                    registration = candidate;
            }
            if (registration == null)
                throw new InvalidDataException($"{Label}: nested {T7AssetTypes.Name(type)} at PC read {Position} has no registration");
            int end = Position;
            while (end < asset.Reads.Count && asset.Reads[end].FileOffset < registration.Value.FilePos)
                end++;
            if (rewrite != null && type is not (T7AssetTypes.PhysPreset or T7AssetTypes.PhysConstraints))
            {
                IT7NestedConverter? nested = context.NestedConverter(type)
                    ?? throw new InvalidDataException($"{Label}: embeds {T7AssetTypes.Name(type)} '{registration.Value.Name}', which needs the {T7AssetTypes.Name(type)} converter");
                int start = rewrite.Position;
                if (!nested.TryConvertNested(context, rewrite, type, end, out string? why))
                    throw new InvalidDataException($"{Label}: embedded {T7AssetTypes.Name(type)} '{registration.Value.Name}': {why}");
                if (rewrite.Position != end)
                    throw new InvalidDataException($"{Label}: embedded {T7AssetTypes.Name(type)} '{registration.Value.Name}' converted {rewrite.Position - start} reads of {end - start}");
                return;
            }
            while (Position < end)
            {
                if (rewrite != null)
                    rewrite.Copy();
                else
                    _position++;
            }
        }
    }
}
