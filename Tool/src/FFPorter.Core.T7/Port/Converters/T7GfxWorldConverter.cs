using System.Buffers.Binary;
using System.Text;
using FFPorter.Core.T7.Formats;
using FFPorter.Core.T7.Harness;
using FFPorter.Core.T7.Link;
using FFPorter.Core.T7.Streams;

namespace FFPorter.Core.T7.Port.Converters;

public sealed class T7GfxWorldConverter : IT7AssetConverter, IT7StreamPlanner
{
    public const int WorldSize = 8256;

    public string Name => "gfxworld";

    public bool TryConvert(T7PortContext context, T7Walk.Asset asset, T7OutputAsset output, out string? reason)
    {
        reason = null;
        if (asset.Type != T7AssetTypes.GfxWorld)
            return false;
        var rewrite = new T7AssetRewrite(context, asset, output);
        try
        {
            new Traversal(context, asset, rewrite, null).World();
            foreach (T7Walk.Span deferred in asset.Deferred)
                rewrite.CopyPcDeferred(deferred);
            rewrite.Finish();
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
        if (asset.Type != T7AssetTypes.GfxWorld || asset.Reads.Count == 0)
            return;
        try
        {
            new Traversal(context, asset, null, plan).World();
        }
        catch (InvalidDataException error)
        {
            context.Warn(T7Issues.WorldNotPlanned, $"gfx_map '{asset.Name}': streamed world items not planned ({error.Message})", error.Message);
        }
    }

    private sealed class Traversal(T7PortContext context, T7Walk.Asset asset, T7AssetRewrite? rewrite, T7StreamPlan? plan)
    {
        private const ulong Inline = ulong.MaxValue, InlineAlias = ulong.MaxValue - 1;
        private readonly byte[] _zone = context.Pc.Zone;
        private int _position;

        private int Position => rewrite?.Position ?? _position;
        private string Label => $"gfx_map '{asset.Name}'";

        private ulong Q(long at) => BinaryPrimitives.ReadUInt64LittleEndian(_zone.AsSpan((int)at));
        private uint U32(long at) => BinaryPrimitives.ReadUInt32LittleEndian(_zone.AsSpan((int)at));
        private ushort U16(long at) => BinaryPrimitives.ReadUInt16LittleEndian(_zone.AsSpan((int)at));

        private T7Walk.Span Peek(string role, long size, T7WalkKind kind = T7WalkKind.Read)
        {
            if (Position >= asset.Reads.Count)
                throw new InvalidDataException($"{Label}: {role} expected, no PC reads left");
            T7Walk.Span read = asset.Reads[Position];
            if (read.Kind != kind || (size >= 0 && read.Size != size))
                throw new InvalidDataException($"{Label}: {role} expected ({kind}, {size} bytes); PC read {Position} is ({read.Kind}, {read.Size} bytes)");
            return read;
        }

        private void Skip()
        {
            if (rewrite != null)
                rewrite.Take();
            else
                _position++;
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

        private (long FileOffset, T7StructBuilder? Builder) Rebuild(string role, long size, int outputSize)
        {
            T7Walk.Span read = Peek(role, size);
            if (rewrite == null)
            {
                _position++;
                return (read.FileOffset, null);
            }
            rewrite.Take();
            return (read.FileOffset, rewrite.Rebuild(read, outputSize));
        }

        private string? XString(long field)
        {
            if (Q(field) == Inline)
            {
                T7Walk.Span read = Peek("string", -1, T7WalkKind.String);
                string text = Encoding.Latin1.GetString(_zone, (int)read.FileOffset, (int)read.Size - 1);
                Copy("string", -1, T7WalkKind.String);
                return text;
            }
            if (Q(field) != 0 && context.Pc.TryFollow(field, out long at))
            {
                int end = Array.IndexOf(_zone, (byte)0, (int)at);
                return end > at ? Encoding.Latin1.GetString(_zone, (int)at, end - (int)at) : null;
            }
            return null;
        }


        private byte[]? ImageStruct(long field)
        {
            ulong value = Q(field);
            if (value == 0)
                return null;
            if (value is Inline or InlineAlias)
                return _zone.AsSpan((int)Peek("image", T7ImageConverter.PcImageSize).FileOffset, T7ImageConverter.PcImageSize).ToArray();
            if (!context.Pc.TryReferencedName(field, out int type, out string name) || type != T7AssetTypes.Image)
                return null;
            return context.ImageStructByName(name);
        }

        private void ImagePointer(long field)
        {
            if (Q(field) is not (Inline or InlineAlias))
                return;
            if (rewrite != null)
            {
                T7ImageConverter.ConvertImage(context, rewrite);
                return;
            }
            long image = Peek("image", T7ImageConverter.PcImageSize).FileOffset;
            _position++;
            if (Q(image + 248) == Inline)
                _position++;
            if (_zone[image + 210] == 0 && Q(image + 216) == Inline && U32(image + 232) != 0)
                _position++;
            if (Q(image + 224) == Inline && U32(image + 236) != 0)
                _position++;
        }

        private void MaterialPointer(long field)
        {
            if (Q(field) is not (Inline or InlineAlias))
                return;
            T7Walk.Span first = Peek("material", 672);
            T7Walk.Registration? registration = null;
            foreach (T7Walk.Registration candidate in asset.Registrations)
            {
                if (candidate.Type == T7AssetTypes.Material && candidate.FilePos > first.FileOffset && candidate.Header == first.At
                    && (registration == null || candidate.FilePos < registration.Value.FilePos))
                    registration = candidate;
            }
            if (registration == null)
                throw new InvalidDataException($"{Label}: embedded material at PC read {Position} has no registration");
            int end = asset.Reads.FindIndex(Position, r => r.FileOffset >= registration.Value.FilePos);
            if (end < 0)
                end = asset.Reads.Count;
            if (rewrite == null)
            {
                _position = end;
                return;
            }
            var converter = context.NestedConverter(T7AssetTypes.Material)
                ?? throw new InvalidDataException($"{Label}: embedded material '{registration.Value.Name}' needs the material converter");
            if (!converter.TryConvertNested(context, rewrite, T7AssetTypes.Material, end, out string? why))
                throw new InvalidDataException($"{Label}: embedded material '{registration.Value.Name}': {why}");
            if (rewrite.Position != end)
                throw new InvalidDataException($"{Label}: embedded material '{registration.Value.Name}' converted to read {rewrite.Position}, it ends at {end}");
        }

        private void XModelPointer(long field)
        {
            if (Q(field) is Inline or InlineAlias)
                throw new InvalidDataException($"{Label}: a model loaded inside the world is not supported (not found in retail maps)");
        }


        public void World()
        {
            (long root, T7StructBuilder? rootBuilder) = Rebuild("world", WorldSize, WorldSize);
            if (rootBuilder != null)
            {
                rootBuilder.MoveAll();
                foreach (int flag in (int[])[876, 892, 908, 924])
                    rootBuilder.U32(flag, 1);
                rootBuilder.Commit();
            }
            XString(root);
            XString(root + 8);
            if (Q(root + 40) != 0)
                Copy("stream tree nodes", 48L * U32(root + 32));
            if (Q(root + 56) != 0)
                Copy("stream tree leaf refs", 4L * U32(root + 48));
            if (Q(root + 224) != 0)
                Copy("array +224", 32L * U32(root + 220));
            if (Q(root + 240) != 0)
            {
                uint volumes = U32(root + 232);
                long array = Copy("lighting volumes", 4536L * volumes);
                for (int k = 0; k < volumes && array >= 0; k++)
                    Volume(array + 4536L * k);
            }
            foreach ((int pointer, int count, int size, int sub) in new[]
            {
                (256, 248, 16, 0), (272, 264, 424, 1), (288, 280, 16, 0), (304, 296, 108, 0), (320, 312, 16, 0), (336, 328, 624, 0),
                (352, 344, 16, 0), (368, 360, 48, 0), (384, 376, 16, 0), (400, 392, 60, 0), (416, 408, 16, 0), (432, 424, 120, 2),
                (448, 440, 16, 0), (464, 456, 12, 0), (480, 472, 16, 0), (496, 488, 48, 0), (512, 504, 16, 0), (528, 520, 144, 0),
            })
            {
                if (Q(root + pointer) == 0)
                    continue;
                uint n = U32(root + count);
                long array = Copy($"array +{pointer}", (long)size * n);
                for (int k = 0; k < n && array >= 0 && sub != 0; k++)
                {
                    long element = array + size * k;
                    if (sub == 1)
                    {
                        for (int j = 0; j < 4; j++)
                        {
                            if (Q(element + 8 + 104 * j + 88) == Inline)
                                Sst();
                        }
                    }
                    else
                    {
                        for (int j = 0; j < 6; j++)
                            ImagePointer(element + 72 + 8 * j);
                    }
                }
            }
            if (Q(root + 560) == Inline)
                Copy("dpvs planes", 20L * U32(root + 16));
            if (Q(root + 568) != 0)
                Copy("dpvs nodes", 4L * U32(root + 20));
            if (Q(root + 592) != 0)
            {
                uint cells = U32(root + 552);
                long array = Copy("cells", 56L * cells);
                for (int k = 0; k < cells && array >= 0; k++)
                    Cell(array + 56L * k);
            }
            ImagePointer(root + 608);
            Geometry(root);
            if (Q(root + 696) != 0)
                Copy("brush models", 64L * U32(root + 688));
            if (Q(root + 736) != 0)
            {
                uint count = U32(root + 732);
                long array = Copy("material memory", 16L * count);
                for (int k = 0; k < count && array >= 0; k++)
                    MaterialPointer(array + 16L * k);
            }
            Dpvs(root);
            foreach (int offset in (int[])[1312, 1320, 1328, 1336])
                MaterialPointer(root + offset);
            if (Q(root + 1352) != 0)
                Copy("occluders", 68L * U32(root + 1344));
            foreach ((int pointer, int count) in new[] { (1376, 1368), (1392, 1388) })
            {
                if (Q(root + pointer) == 0)
                    continue;
                uint n = U32(root + count);
                long array = Copy($"array40 +{pointer}", 40L * n);
                for (int k = 0; k < n && array >= 0; k++)
                {
                    long element = array + 40L * k;
                    if (Q(element + 8) != 0)
                        Copy("array40 data", U32(element));
                    if (Q(element + 32) != 0)
                        Copy("array40 records", 40L * U32(element + 24));
                }
            }
            if (Q(root + 1408) != 0)
            {
                uint n = U32(root + 1400);
                long array = Copy("records +1408", 8L * n);
                for (int k = 0; k < n && array >= 0; k++)
                {
                    if (Q(array + 8L * k) is Inline or InlineAlias)
                        throw new InvalidDataException($"{Label}: inline world +1408 records are not supported (their PS4 layout is unverified)");
                }
            }
            foreach ((int pointer, int count, int size) in new[] { (1424, 1416, 20), (1440, 1432, 84), (1456, 1448, 360), (1472, 1464, 16) })
            {
                if (Q(root + pointer) != 0)
                    Copy($"array +{pointer}", (long)size * U32(root + count));
            }
            if (Q(root + 1488) != 0)
            {
                uint n = U32(root + 1480);
                (long decals, T7StructBuilder? builder) = Rebuild("decals", 184L * n, (int)(184 * n));
                if (builder != null)
                {
                    builder.MoveAll();
                    context.Fidelity?.Raise(T7Issues.WorldTexelDensity);
                    for (int k = 0; k < n; k++)
                        ScaleFloat(builder, 184 * k + 168);
                    builder.Commit();
                }
                for (int k = 0; k < n; k++)
                    MaterialPointer(decals + 184L * k + 152);
            }
            ImagePointer(root + 1496);
            if (Q(root + 1512) != 0)
            {
                uint n = U32(root + 1508);
                long array = Copy("records 192", 192L * n);
                for (int k = 0; k < n && array >= 0; k++)
                    Record192(array + 192L * k);
            }
            foreach ((int pointer, int count, int size) in new[]
            {
                (5656, 5648, 64), (5672, 5664, 144), (5688, 5680, 144), (6984, 6976, 384), (7000, 6992, 464), (7016, 7008, 464),
                (7048, 7040, 64), (7064, 7056, 144), (7080, 7072, 144), (8184, 8176, 336), (8200, 8192, 688), (8216, 8208, 688),
                (8232, 8224, 164), (8248, 8240, 164),
            })
            {
                if (Q(root + pointer) != 0)
                    Copy($"array +{pointer}", (long)size * U32(root + count));
            }
        }

        private static void ScaleFloat(T7StructBuilder builder, int at)
        {
            float value = BinaryPrimitives.ReadSingleLittleEndian(builder.Data.AsSpan(at));
            BinaryPrimitives.WriteSingleLittleEndian(builder.Data.AsSpan(at), value * T7XModelConverter.TexelDensityScale);
        }

        private void Volume(long volume)
        {
            for (int k = 0; k < 4; k++)
            {
                if (Q(volume + 1056 + 112 * k + 104) == Inline)
                    Sst();
            }
            for (int k = 0; k < 4; k++)
            {
                long s = volume + 1504 + 48 * k;
                if (Q(s + 24) == Inline)
                    Copy("sst min/max", 2L * U32(s + 16));
                ImagePointer(s + 32);
            }
            if (Q(volume + 1704) != 0)
                Copy("volume u16 +1704", 2L * U32(volume + 1696));
            for (int k = 0; k < 4; k++)
            {
                long s = volume + 1712 + 40 * k;
                byte[]? image = ImageStruct(s + 8);
                ImagePointer(s + 8);
                if (Q(s + 16) == Inline)
                {
                    uint n = U32(s + 32) + 1;
                    (long probes, T7StructBuilder? builder) = Rebuild("reflection probes", 328L * n, (int)(336 * n));
                    if (builder != null)
                    {
                        ReadOnlySpan<byte> source = _zone.AsSpan((int)probes, (int)(328 * n));
                        for (int j = 0; j < n; j++)
                        {
                            int p = 328 * j, q = 336 * j;
                            builder.Move(p, q, 320);
                            builder.Write(q + 320, source.Slice(p + 312, 8));
                            builder.Write(q + 328, source.Slice(p + 320, 2));
                            builder.Write(q + 330, source.Slice(p + 320, 2));
                            builder.Move(p + 324, q + 332, 4);
                        }
                        builder.Commit();
                    }
                }
                if (Q(s + 24) == Inline)
                    Copy("reflection probe array", 604L * U32(s + 36));
                if (Q(s) == Inline)
                {
                    (long record, T7StructBuilder? builder) = Rebuild("wrapped record", 64, 64);
                    builder?.MoveAll();
                    Wrapped(builder, 0, record, [image]);
                    builder?.Commit();
                }
            }
            for (int k = 0; k < 4; k++)
            {
                long s = volume + 1872 + 32 * k;
                var images = new List<byte[]?>();
                for (int j = 0; j < 3; j++)
                {
                    images.Add(ImageStruct(s + 8 * j));
                    ImagePointer(s + 8 * j);
                }
                if (Q(s + 24) == Inline)
                {
                    (long record, T7StructBuilder? builder) = Rebuild("wrapped record", 64, 64);
                    builder?.MoveAll();
                    Wrapped(builder, 0, record, images);
                    builder?.Commit();
                }
            }
            for (int k = 0; k < 4; k++)
            {
                long s = volume + 2000 + 24 * k;
                XModelPointer(s);
                if (Q(s + 16) != Inline)
                    continue;
                (long record, T7StructBuilder? builder) = Rebuild("skybox record", 72, 72);
                builder?.MoveAll();
                byte[]? image = ImageStruct(record);
                ImagePointer(record);
                Wrapped(builder, 8, record + 8, [image]);
                builder?.Commit();
            }
            for (int k = 0; k < 4; k++)
            {
                long s = volume + 2096 + 16 * k;
                if (Q(s + 8) != Inline)
                    continue;
                uint n = U32(s);
                long grids = Copy("grids", 80L * n);
                for (int j = 0; j < n && grids >= 0; j++)
                {
                    long f = grids + 80L * j;
                    if (Q(f + 48) != 0)
                        Copy("grid cells", 12L * U16(f + 40) * U16(f + 42) * U16(f + 44));
                    if (Q(f + 64) != 0)
                    {
                        ushort m = U16(f + 56);
                        long children = Copy("grid children", 80L * m);
                        for (int i = 0; i < m && children >= 0; i++)
                        {
                            long g = children + 80L * i;
                            if (Q(g + 64) != 0)
                                Copy("grid child array", 16L * U32(g + 60));
                        }
                    }
                }
            }
        }

        private void Sst()
        {
            (long sst, T7StructBuilder? builder) = Rebuild("shadow tree", 144, 136);
            builder?.Move(0, 0, 128).Move(136, 128, 8);
            if (Q(sst + 64) != 0)
                Copy("shadow tree u32 a", 4L * U32(sst + 84));
            if (Q(sst + 72) != 0)
                Copy("shadow tree u32 b", 4L * U32(sst + 84));
            Wrapped(builder, 0, sst, []);
            builder?.Commit();
            if (Q(sst + 136) == Inline)
            {
                (long buffer, T7StructBuilder? gpu) = Rebuild("GPU buffer", 56, 48);
                gpu?.Move(0, 0, 48).Commit();
                if (Q(buffer) == Inline)
                    Copy("GPU buffer data", (long)U32(buffer + 8) * U32(buffer + 12));
            }
        }

        private void Cell(long cell)
        {
            if (Q(cell + 32) != 0)
            {
                uint n = U32(cell + 24);
                long trees = Copy("cell AABB trees", 48L * n);
                for (int k = 0; k < n && trees >= 0; k++)
                {
                    long f = trees + 48L * k;
                    if (Q(f + 32) == Inline)
                        Copy("cell smodel indexes", 2L * U16(f + 30));
                }
            }
            if (Q(cell + 48) != 0)
            {
                uint n = U32(cell + 40);
                long portals = Copy("cell portals", 120L * n);
                for (int k = 0; k < n && portals >= 0; k++)
                {
                    long f = portals + 120L * k;
                    if (Q(f + 48) == Inline)
                        Cell(Copy("portal cell", 56));
                    if (Q(f + 56) != 0)
                        Copy("portal vertices", 12L * _zone[f + 64]);
                }
            }
        }

        private void Record192(long record)
        {
            if (Q(record + 32) != 0)
                Copy("record192 array40", 40L * U32(record + 28));
            if (Q(record + 48) != 0)
            {
                uint n = U32(record + 40);
                long array = Copy("record192 array32", 32L * n);
                for (int k = 0; k < n && array >= 0; k++)
                {
                    if (Q(array + 32L * k) != 0)
                        Copy("record192 array8", 8L * U32(array + 32L * k + 8));
                }
            }
            if (Q(record + 64) != 0)
            {
                uint n = U32(record + 56);
                long array = Copy("record192 array48", 48L * n);
                for (int k = 0; k < n && array >= 0; k++)
                {
                    long f = array + 48L * k;
                    if (Q(f + 8) != 0)
                        Copy("record192 array48 array8", 8L * U32(f + 16));
                    XString(f + 40);
                }
            }
            MaterialPointer(record + 72);
            XString(record + 80);
            if (Q(record + 144) != 0)
            {
                uint n = U32(record + 136);
                long array = Copy("record192 array72", 72L * n);
                for (int k = 0; k < n && array >= 0; k++)
                    XString(array + 72L * k + 64);
            }
        }

        private void Geometry(long root)
        {
            long draw = root + 600;
            if (Q(draw + 24) != 0)
                Copy("world vertices", U32(draw + 20));
            if (Q(draw + 48) != 0)
            {
                uint size = U32(draw + 40);
                (long _, T7StructBuilder? builder) = Rebuild("world vertex layers", size, (int)size);
                if (builder != null)
                {
                    builder.MoveAll();
                    for (int at = 0; at + 20 <= size; at += 20)
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(builder.Data.AsSpan(at + 12), T7XModelConverter.RepackUnitVector(BinaryPrimitives.ReadUInt32LittleEndian(builder.Data.AsSpan(at + 12))));
                        BinaryPrimitives.WriteUInt32LittleEndian(builder.Data.AsSpan(at + 16), T7XModelConverter.RepackUnitVector(BinaryPrimitives.ReadUInt32LittleEndian(builder.Data.AsSpan(at + 16))));
                    }
                    builder.Commit();
                }
            }
            if (Q(draw + 72) != 0)
                Copy("world indices", 2L * U32(draw + 64));
        }

        private void Dpvs(long root)
        {
            long dpvs = root + 760;
            foreach ((int pointer, int count) in new[] { (272, 8), (280, 4), (296, 288), (304, 292) })
            {
                if (Q(dpvs + pointer) != 0)
                    Copy($"dpvs u16 +{pointer}", 2L * U32(dpvs + count));
            }
            if (Q(dpvs + 312) != 0)
            {
                uint n = U32(root + 24);
                (long surfaces, T7StructBuilder? builder) = Rebuild("surfaces", 96L * n, (int)(96 * n));
                if (builder != null)
                {
                    builder.MoveAll();
                    context.Fidelity?.Raise(T7Issues.WorldTexelDensity);
                    for (int k = 0; k < n; k++)
                        ScaleFloat(builder, 96 * k + 36);
                    builder.Commit();
                }
                for (int k = 0; k < n; k++)
                    MaterialPointer(surfaces + 96L * k + 72);
            }
            if (Q(dpvs + 320) != 0)
            {
                uint n = U32(dpvs + 4);
                long instances = Copy("static model instances", 152L * n);
                for (int k = 0; k < n && instances >= 0; k++)
                {
                    long e = instances + 152L * k;
                    XModelPointer(e + 88);
                    if (Q(e + 120) != 0)
                        Copy("static model instance array", 64L * U32(e + 128));
                }
            }
        }


        private void Wrapped(T7StructBuilder? builder, int at, long record, List<byte[]?> images)
        {
            ulong key = Q(record + 8);
            int kind = _zone[record + 48], location = _zone[record + 49];
            uint size = U32(record + 52);
            string? name = XString(record + 40);
            List<T7WrappedItems.Image>? dimensions = kind == T7WrappedItems.KindSst ? [] : images.All(i => i != null)
                ? images.Select(i => T7WrappedItems.Image.FromStruct(i!)).ToList() : null;
            long ps4Size = size;
            ulong ps4Key = key;
            if (dimensions != null)
            {
                ps4Size = T7WrappedItems.Ps4LogicalSize(kind, dimensions, size);
            }
            else
            {
                context.Warnings.Add($"{Label}: wrapped item '{name}' has no image dimensions; its logical size is kept");
                if (rewrite != null)
                    context.Fidelity?.Raise(T7Issues.WrappedItemSize, name);
            }
            if (location == 0)
            {
                if (plan != null && dimensions != null && kind != T7WrappedItems.KindSst)
                {
                    List<T7WrappedItems.Image> inZone = dimensions;
                    plan.RegisterNamed(T7WrappedItems.XPakType(kind), name, size,
                        (xrecord, parts) => T7WrappedItems.Convert(kind, inZone, parts.Count == 1 ? parts[0].Bytes : parts.SelectMany(p => p.Bytes).ToArray()),
                        $"wrapped-1:{kind}:" + string.Join(",", inZone), byShape: true);
                }
                if (Q(record + 56) != 0)
                {
                    T7Walk.Span data = Peek("wrapped payload", size);
                    if (rewrite != null)
                    {
                        rewrite.Take();
                        List<(uint LogicalOffset, byte[] Bytes)> parts = dimensions != null
                            ? T7WrappedItems.Convert(kind, dimensions, rewrite.Bytes(data))
                            : [(0, rewrite.Bytes(data).ToArray())];
                        byte[] blob = T7WrappedItems.Flatten(parts);
                        T7StructBuilder payload = rewrite.Rebuild(data, blob.Length);
                        payload.Write(0, blob).MapSource(0, 0, kind == T7WrappedItems.KindSst ? (int)size : 1);
                        payload.Commit();
                        ps4Key = XPak.ComputeKey(parts.Select(p => p.Bytes), 7);
                        ps4Size = blob.Length;
                    }
                    else
                    {
                        _position++;
                    }
                }
            }
            else if (kind != T7WrappedItems.KindSst)
            {
                if (plan != null && dimensions != null)
                {
                    List<T7WrappedItems.Image> dims = dimensions;
                    List<(uint LogicalOffset, byte[] Bytes)> Wrap(XPakIndexRecord? xrecord, List<(uint LogicalOffset, byte[] Bytes)> parts)
                    {
                        byte[] payload = parts.Count == 1 ? parts[0].Bytes : parts.SelectMany(p => p.Bytes).ToArray();
                        return T7WrappedItems.Convert(kind, dims, payload);
                    }
                    string signature = $"wrapped-1:{kind}:" + string.Join(",", dims);
                    plan.Register(key, Wrap, signature);
                    plan.RegisterNamed(T7WrappedItems.XPakType(kind), name, size, Wrap, signature, byShape: true);
                }
                if (rewrite != null)
                    ps4Key = context.Ps4StreamKey(key, out _, Common.Fidelity.FidelityDimension.World);
            }
            if (builder != null)
            {
                builder.U64(at + 8, ps4Key);
                builder.U32(at + 52, (uint)ps4Size);
            }
        }
    }
}
