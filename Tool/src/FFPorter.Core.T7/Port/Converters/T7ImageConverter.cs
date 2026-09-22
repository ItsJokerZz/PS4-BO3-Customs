using System.Buffers.Binary;
using FFPorter.Core.T7.Formats;
using FFPorter.Core.T7.Harness;
using FFPorter.Core.T7.Link;
using FFPorter.Core.T7.Streams;

namespace FFPorter.Core.T7.Port.Converters;

public sealed class T7ImageConverter : IT7AssetConverter, IT7NestedConverter, IT7StreamPlanner
{
    public const int PcImageSize = 264, Ps4ImageSize = 304;
    private const ulong Inline = ulong.MaxValue, InlineAlias = ulong.MaxValue - 1;

    public string Name => "image";

    public bool TryConvert(T7PortContext context, T7Walk.Asset asset, T7OutputAsset output, out string? reason)
    {
        reason = null;
        if (asset.Type is not (T7AssetTypes.Image or T7AssetTypes.NewLensFlareDef or T7AssetTypes.TextureCombo))
            return false;
        var rewrite = new T7AssetRewrite(context, asset, output);
        try
        {
            switch (asset.Type)
            {
                case T7AssetTypes.Image:
                    ConvertImage(context, rewrite);
                    break;
                case T7AssetTypes.NewLensFlareDef:
                    ConvertKlf(context, rewrite);
                    break;
                default:
                    ConvertTextureCombo(rewrite);
                    break;
            }
            rewrite.Finish();
            return true;
        }
        catch (InvalidDataException error)
        {
            reason = error.Message;
            return false;
        }
    }

    public bool CanConvertNested(int type) => type == T7AssetTypes.Image;

    public bool TryConvertNested(T7PortContext context, T7AssetRewrite rewrite, int type, int endRead, out string? reason)
    {
        reason = null;
        try
        {
            ConvertImage(context, rewrite);
            return true;
        }
        catch (InvalidDataException error)
        {
            reason = error.Message;
            return false;
        }
    }


    public sealed class ImageInfo
    {
        public byte[] Struct { get; }
        public int Parts => Struct[160];
        public int MapType => Struct[162];
        public int Category => Struct[163];
        public int Width => BinaryPrimitives.ReadUInt16LittleEndian(Struct.AsSpan(192));
        public int Height => BinaryPrimitives.ReadUInt16LittleEndian(Struct.AsSpan(194));
        public int TextureWidth => BinaryPrimitives.ReadUInt16LittleEndian(Struct.AsSpan(196));
        public int TextureHeight => BinaryPrimitives.ReadUInt16LittleEndian(Struct.AsSpan(198));
        public int Depth => BinaryPrimitives.ReadUInt16LittleEndian(Struct.AsSpan(200));
        public int Levels => Struct[208];
        public int LowMip => Struct[209];
        public int Streamed => Struct[210];
        public ulong PixelsPointer => BinaryPrimitives.ReadUInt64LittleEndian(Struct.AsSpan(216));
        public ulong LowPointer => BinaryPrimitives.ReadUInt64LittleEndian(Struct.AsSpan(224));
        public uint PixelsSize => BinaryPrimitives.ReadUInt32LittleEndian(Struct.AsSpan(232));
        public uint LowSize => BinaryPrimitives.ReadUInt32LittleEndian(Struct.AsSpan(236));
        public uint Dxgi => BinaryPrimitives.ReadUInt32LittleEndian(Struct.AsSpan(240));
        public ulong NamePointer => BinaryPrimitives.ReadUInt64LittleEndian(Struct.AsSpan(248));

        public bool IsReference => !Struct.AsSpan(0, 248).ContainsAnyExcept((byte)0);

        public ImageInfo(ReadOnlySpan<byte> pcStruct) => Struct = pcStruct.ToArray();

        public int GnmType => MapType switch
        {
            1 => T7Gnm.Texture2d,
            2 => T7Gnm.Texture2dArray,
            3 => T7Gnm.Texture3d,
            4 or 5 => T7Gnm.TextureCube,
            _ => throw new InvalidDataException($"image map type {MapType} has no Gnm texture type"),
        };

        public int Faces => MapType is 4 or 5 ? 6 : 1;

        public T7Gnm.Texture Texture()
        {
            int slices = MapType is 2 or 5 ? Depth : 1;
            return new T7Gnm.Texture(new T7Gnm.Format(Dxgi), GnmType, TextureWidth, TextureHeight, Depth, Levels, slices);
        }

        public T7Gnm.Texture LowTexture() =>
            new(new T7Gnm.Format(Dxgi), T7Gnm.Texture2d, Math.Max(TextureWidth >> LowMip, 1), Math.Max(TextureHeight >> LowMip, 1), 1, 1, 1);
    }

    public static byte[] ConvertMips(ImageInfo info, ReadOnlySpan<byte> pc, int firstMip, int endMip, T7Gnm.Texture? texture, out long consumed)
    {
        T7Gnm.Texture t = texture ?? info.Texture();
        (List<T7Gnm.Mip> mips, _) = t.Layout();
        if (endMip > mips.Count)
            throw new InvalidDataException($"image mips {firstMip}..{endMip} exceed its {mips.Count}-mip Gnm layout");
        int slices = t.ArraySliceCount;
        int faces = texture == null ? info.Faces : 1;
        int count = texture == null && info.MapType is 2 or 5 ? info.Depth : 1;
        long size = 0;
        for (int m = firstMip; m < endMip; m++)
            size += slices * mips[m].SizePerSlice;
        var output = new byte[size];
        long baseOffset = 0, pcOffset = 0;
        T7Gnm.Format format = t.Format;
        for (int m = firstMip; m < endMip; m++)
        {
            T7Gnm.Mip mip = mips[m];
            int mw = Math.Max(t.Width >> m, 1), mh = Math.Max(t.Height >> m, 1), md = Math.Max(t.Depth >> m, 1);
            long surface = T7Gnm.PcSurfaceSize(info.Dxgi, mw, mh, md);
            for (int i = 0; i < count; i++)
            {
                for (int face = 0; face < faces; face++)
                {
                    int slice = t.IsCube ? i * 6 + face : i;
                    if (pcOffset + surface > pc.Length)
                        throw new InvalidDataException($"image pixel data ends at {pc.Length} bytes; mip {m} needs {pcOffset + surface}");
                    Span<byte> destination = output.AsSpan((int)(baseOffset + slice * mip.SizePerSlice), (int)mip.SizePerSlice);
                    ReadOnlySpan<byte> source = pc.Slice((int)pcOffset, (int)surface);
                    if (format.IsBc)
                        T7Gnm.TileThin(source, (mw + 3) / 4, (mh + 3) / 4, md, mip.Pitch, mip.Height, mip.Depth, format.ElementBytes, destination);
                    else
                        T7Gnm.TileLinear(source, mw, mh, md, mip.Pitch, mip.Height, format.ElementBytes, destination);
                    pcOffset += surface;
                }
            }
            baseOffset += slices * mip.SizePerSlice;
        }
        consumed = pcOffset;
        return output;
    }

    public static List<int> ImageStructReads(T7Walk.Asset asset)
    {
        var result = new List<int>();
        foreach (T7Walk.Registration registration in asset.Registrations)
        {
            if (registration.Type != T7AssetTypes.Image)
                continue;
            for (int i = asset.Reads.Count - 1; i >= 0; i--)
            {
                T7Walk.Span read = asset.Reads[i];
                if (read.FileOffset >= registration.FilePos)
                    continue;
                if (read.Kind == T7WalkKind.Read && read.Size == PcImageSize && read.At == registration.Header)
                {
                    result.Add(i);
                    break;
                }
            }
        }
        result.Sort();
        return result;
    }

    public void PlanStreams(T7PortContext context, T7Walk.Asset asset, T7StreamPlan plan)
    {
        foreach (int index in ImageStructReads(asset))
        {
            var info = new ImageInfo(context.Pc.Zone.AsSpan((int)asset.Reads[index].FileOffset, PcImageSize));
            if (info.IsReference || info.Parts == 0 || !T7Gnm.IsKnownDxgi(info.Dxgi))
                continue;
            int previous = 0;
            for (int part = 0; part < info.Parts && part < 4; part++)
            {
                uint levelCountAndSize = BinaryPrimitives.ReadUInt32LittleEndian(info.Struct.AsSpan(part * 40));
                ulong key = BinaryPrimitives.ReadUInt64LittleEndian(info.Struct.AsSpan(part * 40 + 8));
                int cumulative = (int)(levelCountAndSize & 0xF);
                int first = info.Levels - cumulative, end = info.Levels - previous;
                previous = cumulative;
                if (key == 0 || first < 0 || end <= first)
                    continue;
                List<string> warnings = context.Warnings;
                T7Fidelity? fidelity = context.Fidelity;
                plan.Register(key, (record, parts) =>
                {
                    byte[] payload = parts.Count == 1 ? parts[0].Bytes : parts.SelectMany(p => p.Bytes).ToArray();
                    byte[] converted = ConvertMips(info, payload, first, end, null, out long used);
                    if (payload.AsSpan((int)Math.Min(used, payload.Length)).ContainsAnyExcept((byte)0))
                    {
                        if (warnings.Count < 5000)
                            warnings.Add($"image part {key:x16} ('{record?.Name}') has non-zero PC data after its mips (dropped)");
                        fidelity?.RaiseStreamItem(T7Issues.ImageTailDropped, record?.Name);
                    }
                    return [(parts.Count > 0 ? parts[0].LogicalOffset : 0u, converted)];
                }, $"image-gnm-1:{Convert.ToHexString(info.Struct.AsSpan(160, 104))}:{first}:{end}");
            }
        }
    }

    public static string ConvertImage(T7PortContext context, T7AssetRewrite rewrite)
    {
        T7Walk.Span structRead = rewrite.Take(PcImageSize);
        var info = new ImageInfo(rewrite.Bytes(structRead));
        T7StructBuilder image = rewrite.Rebuild(structRead, Ps4ImageSize);
        image.CarryPointer(248, 288).Move(256, 296, 8);
        string name = "";
        if (info.NamePointer == Inline)
        {
            T7Walk.Span nameRead = rewrite.Peek();
            name = System.Text.Encoding.Latin1.GetString(rewrite.Bytes(nameRead)[..^1]);
            rewrite.Copy();
        }
        if (info.IsReference)
        {
            image.Commit();
            return name;
        }
        T7Gnm.Texture texture = info.Texture();
        image.Write(0, StreamedParts(context, info, texture, name));
        image.Move(160, 160, 8);
        image.Write(168, texture.Header());
        if (info.LowPointer != 0 || info.LowSize != 0)
            image.Write(200, info.LowTexture().Header());
        image.Move(184, 232, 4);
        image.U8(236, 1).U8(237, 0);
        image.U16(238, (ushort)info.Width).U16(240, (ushort)info.Height);
        image.U16(242, (ushort)(info.MapType == 1 && info.Category == 2 ? 0 : info.Depth));
        image.U8(244, 0).U8(245, 1);
        image.Move(208, 248, 4);
        image.CarryPointer(216, 256).CarryPointer(224, 264);
        if (info.PixelsSize != 0)
            image.U32(272, (uint)texture.TotalSize);
        if (info.LowSize != 0)
            image.U32(276, (uint)info.LowTexture().TotalSize);
        image.U32(280, (uint)texture.Format.Ps4);
        image.Commit();

        if (info.Streamed == 0 && info.PixelsPointer == Inline && info.PixelsSize != 0)
        {
            T7Walk.Span pixels = rewrite.Take(info.PixelsSize);
            byte[] converted = ConvertMips(info, rewrite.Bytes(pixels), 0, info.Levels, null, out _);
            T7StructBuilder data = rewrite.Rebuild(pixels, converted.Length, deferred: info.Category != 5);
            data.Write(0, converted).MapSource(0, 0, (int)Math.Min(1, pixels.Size));
            data.Commit();
        }
        else if (info.PixelsPointer is not (0 or Inline) && info.Streamed == 0)
        {
            context.Warn(T7Issues.ImageSharesPixels, $"image '{name}': pixel data is shared with another image (kept)", name);
        }
        if (info.LowPointer == Inline && info.LowSize != 0)
        {
            T7Walk.Span low = rewrite.Take(info.LowSize);
            byte[] converted = ConvertMips(info, rewrite.Bytes(low), 0, 1, info.LowTexture(), out _);
            T7StructBuilder data = rewrite.Rebuild(low, converted.Length);
            data.Write(0, converted).MapSource(0, 0, (int)Math.Min(1, low.Size));
            data.Commit();
        }
        return name;
    }

    private static byte[] StreamedParts(T7PortContext context, ImageInfo info, T7Gnm.Texture texture, string name)
    {
        var output = new byte[160];
        if (info.Parts == 0)
            return output;
        (List<T7Gnm.Mip> mips, _) = texture.Layout();
        int slices = texture.ArraySliceCount;
        var mipSizes = mips.Select(m => slices * m.SizePerSlice).ToArray();
        int previous = 0;
        for (int part = 0; part < info.Parts && part < 4; part++)
        {
            ReadOnlySpan<byte> pc = info.Struct.AsSpan(part * 40, 40);
            uint levelCountAndSize = BinaryPrimitives.ReadUInt32LittleEndian(pc);
            int cumulative = (int)(levelCountAndSize & 0xF);
            ulong key = BinaryPrimitives.ReadUInt64LittleEndian(pc[8..]);
            long ps4Cumulative = 0;
            for (int m = Math.Max(0, info.Levels - cumulative); m < mipSizes.Length; m++)
                ps4Cumulative += mipSizes[m];
            T7StreamMap.Item? item = null;
            ulong ps4Key = key == 0 ? 0 : context.Ps4StreamKey(key, out item);
            if (item?.Ps4PartSizes is { Count: 1 } sizes)
            {
                long expected = 0;
                for (int m = Math.Max(0, info.Levels - cumulative); m < Math.Min(info.Levels - previous, mipSizes.Length); m++)
                    expected += mipSizes[m];
                if (sizes[0] != expected && item.Source == "zone transform")
                    context.Warn(T7Issues.ImagePartSize, $"image '{name}' part {part}: converted xpak part is {sizes[0]} bytes, the Gnm layout says {expected}", name);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(part * 40), (uint)(ps4Cumulative << 4) | (uint)cumulative);
            pc.Slice(4, 4).CopyTo(output.AsSpan(part * 40 + 4));
            BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(part * 40 + 8), ps4Key);
            pc[16..].CopyTo(output.AsSpan(part * 40 + 16));
            previous = cumulative;
        }
        return output;
    }

    private static void ImagePointer(T7PortContext context, T7AssetRewrite rewrite, ulong value)
    {
        if (value is Inline or InlineAlias)
            ConvertImage(context, rewrite);
    }


    private const int PcKlfRoot = 168, Ps4KlfRoot = 152, PcKlfElement = 496, Ps4KlfElement = 488;

    private static void ConvertKlf(T7PortContext context, T7AssetRewrite rewrite)
    {
        byte[] zone = context.Pc.Zone;
        T7Walk.Span rootRead = rewrite.Take(PcKlfRoot);
        long root = rootRead.FileOffset;
        T7StructBuilder rootBuilder = rewrite.Rebuild(rootRead, Ps4KlfRoot);
        rootBuilder.Move(0, 0, 48).Move(88, 80, 16).Move(144, 128, 24).Commit();
        ulong Q(long at) => BinaryPrimitives.ReadUInt64LittleEndian(zone.AsSpan((int)at));
        if (Q(root) == Inline)
            rewrite.Copy();
        int elements = BinaryPrimitives.ReadUInt16LittleEndian(zone.AsSpan((int)root + 144));
        int second = BinaryPrimitives.ReadUInt16LittleEndian(zone.AsSpan((int)root + 146));
        if (Q(root + 8) != 0 && elements != 0)
        {
            T7Walk.Span elementRead = rewrite.Take(PcKlfElement * elements);
            T7StructBuilder builder = rewrite.Rebuild(elementRead, Ps4KlfElement * elements);
            builder.Elements(PcKlfElement, Ps4KlfElement, e => e.Move(0, 0, 440).Move(480, 472, 16));
            builder.Commit();
            for (int i = 0; i < elements; i++)
            {
                long element = elementRead.FileOffset + PcKlfElement * i;
                if (Q(element) == Inline)
                    rewrite.Copy();
                KlfBufferData(rewrite, zone, element + 424);
                ImagePointer(context, rewrite, Q(element + 480));
            }
        }
        if (Q(root + 16) == Inline && elements != 0)
            rewrite.Copy();
        if (Q(root + 24) == Inline && second != 0)
            rewrite.Copy();
        KlfBufferData(rewrite, zone, root + 32);
        KlfBufferData(rewrite, zone, root + 88);
        ImagePointer(context, rewrite, Q(root + 152));
    }

    private static void KlfBufferData(T7AssetRewrite rewrite, byte[] zone, long buffer)
    {
        if (BinaryPrimitives.ReadUInt64LittleEndian(zone.AsSpan((int)buffer)) != Inline)
            return;
        long size = (long)BinaryPrimitives.ReadUInt32LittleEndian(zone.AsSpan((int)buffer + 8)) * BinaryPrimitives.ReadUInt32LittleEndian(zone.AsSpan((int)buffer + 12));
        if (size != 0)
            rewrite.CopyChecked(size);
    }


    private static void ConvertTextureCombo(T7AssetRewrite rewrite)
    {
        T7Walk.Span rootRead = rewrite.Take(128);
        byte[] zone = rewrite.Pc.Zone;
        T7StructBuilder root = rewrite.Rebuild(rootRead, 128);
        root.Move(0, 0, 8).Move(120, 120, 8).Commit();
        if (BinaryPrimitives.ReadUInt64LittleEndian(zone.AsSpan((int)rootRead.FileOffset)) == Inline)
            rewrite.Copy();
        while (!rewrite.Done)
            rewrite.Take();
    }
}
