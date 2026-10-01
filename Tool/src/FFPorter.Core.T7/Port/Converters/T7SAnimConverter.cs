using System.Buffers.Binary;
using FFPorter.Core.T7.Harness;
using FFPorter.Core.T7.Link;
using FFPorter.Core.T7.Streams;

namespace FFPorter.Core.T7.Port.Converters;

public sealed class T7SAnimConverter : IT7AssetConverter, IT7NestedConverter, IT7StreamPlanner
{
    public const int HeaderSize = 112, PcTextureSize = 136, Ps4TextureSize = 208, TrailerSize = 12, PcLayoutAt = 0x58, Ps4LayoutAt = 0xA0;

    private const int PcFormat = 10, KeyTag = 7;
    private const int KeyAt = 8, SizeAt = 0x34, InlineAt = 0x38, KeptPrefix = 0x40, FormatAt = PcLayoutAt + 36;

    public string Name => "sanim";

    public bool TryConvert(T7PortContext context, T7Walk.Asset asset, T7OutputAsset output, out string? reason)
    {
        reason = null;
        if (asset.Type != T7AssetTypes.SAnim)
            return false;
        var rewrite = new T7AssetRewrite(context, asset, output);
        if (!TryConvert(rewrite, rewrite.Reads.Count, out reason))
            return false;
        rewrite.Finish();
        output.Origin = Name;
        return true;
    }

    public bool CanConvertNested(int type) => type == T7AssetTypes.SAnim;

    public bool TryConvertNested(T7PortContext context, T7AssetRewrite rewrite, int type, int endRead, out string? reason) =>
        TryConvert(rewrite, endRead, out reason);

    public void PlanStreams(T7PortContext context, T7Walk.Asset asset, T7StreamPlan plan)
    {
        if (asset.Type != T7AssetTypes.SAnim || !TryFindTexture(context.Pc.Zone, asset.Reads, 0, asset.Reads.Count, out int texture, out _))
            return;
        ReadOnlySpan<byte> pcTexture = context.Pc.Zone.AsSpan((int)asset.Reads[texture].FileOffset, PcTextureSize);
        ulong key = BinaryPrimitives.ReadUInt64LittleEndian(pcTexture[KeyAt..]);
        uint pcSize = BinaryPrimitives.ReadUInt32LittleEndian(pcTexture[SizeAt..]);
        if (key == 0 || !TryLayout(pcTexture[PcLayoutAt..], out Layout layout, out _))
            return;
        List<(uint LogicalOffset, byte[] Bytes)> Relayout(XPakIndexRecord? record, List<(uint LogicalOffset, byte[] Bytes)> parts)
        {
            byte[] payload = Formats.T7WrappedItems.Flatten(parts);
            if (payload.Length < pcSize || payload.AsSpan((int)pcSize).ContainsAnyExcept((byte)0))
                return parts;
            return [(parts[0].LogicalOffset, Texels(payload.AsSpan(0, (int)pcSize), layout))];
        }
        string signature = $"sanim-2:{layout}";
        plan.Register(key, Relayout, signature);
        plan.RegisterNamed("sanim", asset.Name, pcSize, Relayout, signature);
    }

    private static bool TryConvert(T7AssetRewrite rewrite, int endRead, out string? reason)
    {
        reason = null;
        if (!TryFindTexture(rewrite.Pc.Zone, rewrite.Reads, rewrite.Position, endRead, out int textureRead, out int dataRead))
        {
            rewrite.CopyTo(endRead);
            return true;
        }
        rewrite.CopyTo(textureRead);
        T7Walk.Span texture = rewrite.Take(PcTextureSize);
        T7Walk.Span data = dataRead < 0 ? default : rewrite.Take();
        ReadOnlySpan<byte> pcTexture = rewrite.Bytes(texture);
        ulong pcKey = BinaryPrimitives.ReadUInt64LittleEndian(pcTexture[KeyAt..]);
        uint pcSize = BinaryPrimitives.ReadUInt32LittleEndian(pcTexture[SizeAt..]);

        byte[]? texels = null;
        uint[] fields;
        ulong ps4Key;
        uint ps4Size;
        if (TryLayout(pcTexture[PcLayoutAt..], out Layout layout, out string? layoutReason))
        {
            fields = Fields(layout);
            ps4Size = layout.Size;
            if (dataRead >= 0)
            {
                texels = Texels(rewrite.Bytes(data), layout);
                ps4Key = XPak.ComputeKey(texels, KeyTag);
            }
            else
            {
                ps4Key = pcKey == 0 ? 0 : rewrite.Context.Ps4StreamKey(pcKey, out _);
            }
        }
        else
        {
            fields = new uint[12];
            for (int i = 0; i < fields.Length; i++)
                fields[i] = BinaryPrimitives.ReadUInt32LittleEndian(pcTexture[(PcLayoutAt + i * 4)..]);
            for (int i = 9; i <= 10; i++)
                fields[i] = Formats.T7Gnm.TryPs4Format(fields[i], out int ps4Format) ? (uint)ps4Format : fields[i];
            ps4Key = pcKey;
            ps4Size = pcSize;
            rewrite.Context.Warn(T7Issues.SAnimLayoutKept, $"{rewrite.Label}: {layoutReason}; the PC texels were kept", rewrite.Asset.Name);
        }

        T7StructBuilder builder = rewrite.Rebuild(texture, Ps4TextureSize).Move(0, 0, KeptPrefix).U64(KeyAt, ps4Key).U32(SizeAt, ps4Size);
        for (int i = 0; i < fields.Length; i++)
            builder.U32(Ps4LayoutAt + i * 4, fields[i]);
        builder.Commit();
        if (dataRead >= 0)
        {
            if (texels != null)
                rewrite.Rebuild(data, texels.Length).Write(0, texels).Commit();
            else
                rewrite.CopySpan(data);
        }
        rewrite.CopyTo(endRead);
        return true;
    }

    private static bool TryFindTexture(byte[] zone, IReadOnlyList<T7Walk.Span> reads, int first, int end, out int texture, out int data)
    {
        texture = data = -1;
        for (int i = first; i < end; i++)
        {
            if (reads[i].Kind != T7WalkKind.Read || reads[i].Size != PcTextureSize)
                continue;
            ReadOnlySpan<byte> candidate = zone.AsSpan((int)reads[i].FileOffset, PcTextureSize);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(candidate[SizeAt..]);
            bool inline = BinaryPrimitives.ReadUInt64LittleEndian(candidate[InlineAt..]) == ulong.MaxValue
                && i + 1 < end && reads[i + 1].Kind == T7WalkKind.Read && reads[i + 1].Size == size;
            if (!inline && BinaryPrimitives.ReadUInt32LittleEndian(candidate[FormatAt..]) != PcFormat)
                continue;
            texture = i;
            data = inline ? i + 1 : -1;
            return true;
        }
        return false;
    }

    internal static bool TryConvertTexture(ReadOnlySpan<byte> pcTexture, ReadOnlySpan<byte> pcData, out byte[] ps4Texture, out byte[] ps4Data, out string? reason)
    {
        ps4Texture = [];
        ps4Data = [];
        if (pcTexture.Length != PcTextureSize || BinaryPrimitives.ReadUInt64LittleEndian(pcTexture[InlineAt..]) != ulong.MaxValue
            || BinaryPrimitives.ReadUInt32LittleEndian(pcTexture[SizeAt..]) != pcData.Length)
        {
            reason = $"the {pcTexture.Length}-byte texture does not hold its {pcData.Length} bytes of data inline";
            return false;
        }
        if (!TryLayout(pcTexture[PcLayoutAt..], out Layout layout, out reason))
            return false;
        ps4Data = Texels(pcData, layout);
        ps4Texture = new byte[Ps4TextureSize];
        pcTexture[..KeptPrefix].CopyTo(ps4Texture);
        BinaryPrimitives.WriteUInt64LittleEndian(ps4Texture.AsSpan(KeyAt), XPak.ComputeKey(ps4Data, KeyTag));
        BinaryPrimitives.WriteUInt32LittleEndian(ps4Texture.AsSpan(SizeAt), (uint)ps4Data.Length);
        uint[] fields = Fields(layout);
        for (int i = 0; i < fields.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(ps4Texture.AsSpan(Ps4LayoutAt + i * 4), fields[i]);
        return true;
    }

    private readonly record struct Layout(uint Width, uint Frames, uint BaseRow, uint FrameRow, uint SecondRow, uint SecondBlock, uint Size,
        uint PcBase, uint PcFrameRow, uint PcSecondRow, uint PcSecondBlock, uint Format, uint SecondFormat);

    private static uint[] Fields(Layout layout) =>
        [layout.BaseRow, layout.FrameRow, layout.SecondRow, 0, layout.BaseRow, layout.SecondBlock, layout.Width, layout.Frames, layout.Size, layout.Format, layout.SecondFormat, 0];

    private static bool TryLayout(ReadOnlySpan<byte> pc, out Layout layout, out string? reason)
    {
        layout = default;
        reason = null;
        var f = new uint[12];
        for (int i = 0; i < f.Length; i++)
            f[i] = BinaryPrimitives.ReadUInt32LittleEndian(pc[(i * 4)..]);
        uint baseRow = f[0], frameRow = f[1], secondRow = f[2], zero = f[3], pcBase = f[4], second = f[5], width = f[6], frames = f[7], size = f[8], format = f[9], format2 = f[10];
        static uint Align(uint value, uint to) => (value + to - 1) / to * to;
        uint texel = BlockTexel(format), texel2 = BlockTexel(format2);
        if (texel == 0 || texel2 == 0 || !Formats.T7Gnm.TryPs4Format(format, out int ps4Format) || !Formats.T7Gnm.TryPs4Format(format2, out int ps4Format2))
        {
            reason = $"texture format {format}/{format2} (only 8- and 16-byte uncompressed formats are known)";
            return false;
        }
        if (baseRow != width * 16 || frameRow != width * texel || secondRow != width * texel2 || zero != 0 || pcBase != Align(baseRow, 256)
            || second != Align(pcBase + frames * frameRow, 256) || size != second + Align(frames * secondRow, 256) || width == 0 || frames == 0)
        {
            reason = $"texture layout {width}x{frames} (rows {baseRow}/{frameRow}/{secondRow}, blocks at {pcBase} and {second}, {size} bytes) does not follow the PC padding rules";
            return false;
        }
        uint ps4Base = Align(width, 64) * 16;
        uint pitch = Align(width, 64 / Gcd(frames, 64));
        uint ps4Frame = pitch * texel, ps4SecondRow = pitch * texel2;
        uint ps4Second = ps4Base + frames * ps4Frame;
        layout = new Layout(width, frames, ps4Base, ps4Frame, ps4SecondRow, ps4Second, ps4Second + frames * ps4SecondRow,
            pcBase, frameRow, secondRow, second, (uint)ps4Format, (uint)ps4Format2);
        return true;

        static uint Gcd(uint a, uint b)
        {
            while (b != 0)
                (a, b) = (b, a % b);
            return a;
        }

        static uint BlockTexel(uint format) => Formats.T7Gnm.TexelBytes(format) is int bytes and (8 or 16) ? (uint)bytes : 0;
    }

    private static byte[] Texels(ReadOnlySpan<byte> pc, Layout layout)
    {
        var ps4 = new byte[layout.Size];
        pc[..((int)layout.Width * 16)].CopyTo(ps4);
        int pcRow = (int)layout.PcFrameRow, pcSecondRow = (int)layout.PcSecondRow;
        for (int k = 0; k < layout.Frames; k++)
        {
            pc.Slice((int)layout.PcBase + k * pcRow, pcRow).CopyTo(ps4.AsSpan((int)layout.BaseRow + k * (int)layout.FrameRow));
            pc.Slice((int)layout.PcSecondBlock + k * pcSecondRow, pcSecondRow).CopyTo(ps4.AsSpan((int)layout.SecondBlock + k * (int)layout.SecondRow));
        }
        return ps4;
    }
}
