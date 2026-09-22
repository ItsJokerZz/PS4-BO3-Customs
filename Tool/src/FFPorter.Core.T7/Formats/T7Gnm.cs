using System.Buffers.Binary;

namespace FFPorter.Core.T7.Formats;

public static class T7Gnm
{
    public const int TileThin1d = 13, TileLinearAligned = 8;
    public const int Texture2d = 9, Texture3d = 10, TextureCube = 11, Texture2dArray = 13;

    private static readonly uint[] Ps4DataFormat =
    [
        0, 0, 0, 0x00924001, 0x00204001, 0x00004401, 0x00204002, 0x00204002, 0x00004402, 0x00204702, 0x0022c003, 0x0022c103,
        0x0032e010, 0x00f2e013, 0x00204704, 0x00204704, 0x0022c005, 0x0022c105, 0x0022c705, 0x003ac722, 0x00fac00a, 0x00fac10a,
        0x00f2e00a, 0x00fac90a, 0x00fac009, 0x00f2e009, 0x003ac706, 0x0022c70b, 0x00fac00c, 0x00fac70c, 0x00fac70e, 0x00fac023,
        0x00fac923, 0x00fac024, 0x00fac924, 0x00fac025, 0x00fac925, 0x00204026, 0x0022c027, 0x003ac028, 0x003ac128, 0x00fac029,
        0x00fac929, 0x00fac40e, 0x00004404, 0x0002c40b,
    ];

    private static readonly Dictionary<uint, int> DxgiToPs4 = new()
    {
        [45] = 2, [65] = 3, [61] = 4, [62] = 5, [55] = 6, [56] = 7, [57] = 8, [54] = 9, [49] = 10, [51] = 11, [85] = 12, [115] = 13,
        [40] = 14, [41] = 15, [35] = 16, [37] = 17, [34] = 18, [67] = 19, [28] = 20, [31] = 21, [87] = 22, [29] = 23, [24] = 24,
        [26] = 26, [16] = 27, [11] = 28, [10] = 29, [2] = 30, [71] = 31, [72] = 32, [74] = 33, [75] = 34, [77] = 35, [78] = 36,
        [80] = 37, [83] = 38, [95] = 39, [96] = 40, [98] = 41, [99] = 42, [3] = 43, [42] = 44, [17] = 45,
    };

    private static readonly Dictionary<int, (int Bits, int Texels)> SurfaceBits = new()
    {
        [0x01] = (8, 1), [0x02] = (16, 1), [0x03] = (16, 1), [0x04] = (32, 1), [0x05] = (32, 1), [0x06] = (32, 1), [0x07] = (32, 1),
        [0x08] = (32, 1), [0x09] = (32, 1), [0x0a] = (32, 1), [0x0b] = (64, 1), [0x0c] = (64, 1), [0x0d] = (96, 1), [0x0e] = (128, 1),
        [0x10] = (16, 1), [0x11] = (16, 1), [0x12] = (16, 1), [0x13] = (16, 1), [0x22] = (32, 1),
        [0x23] = (64, 16), [0x24] = (128, 16), [0x25] = (128, 16), [0x26] = (64, 16), [0x27] = (128, 16), [0x28] = (128, 16), [0x29] = (128, 16),
    };

    private static readonly HashSet<uint> DxgiBc8 = [70, 71, 72, 79, 80];
    private static readonly HashSet<uint> DxgiBc16 = [73, 74, 75, 76, 77, 78, 82, 83, 94, 95, 96, 97, 98, 99];
    private static readonly Dictionary<uint, int> DxgiTexelBytes = new()
    {
        [2] = 16, [3] = 16, [10] = 8, [11] = 8, [16] = 8, [17] = 8, [19] = 8, [20] = 8,
        [23] = 4, [24] = 4, [26] = 4, [27] = 4, [28] = 4, [29] = 4, [31] = 4, [34] = 4, [35] = 4, [37] = 4, [39] = 4, [40] = 4, [41] = 4,
        [42] = 4, [44] = 4, [46] = 4, [67] = 4, [88] = 4,
        [49] = 2, [51] = 2, [53] = 2, [54] = 2, [55] = 2, [56] = 2, [57] = 2, [85] = 2, [115] = 2, [61] = 1, [62] = 1, [65] = 1,
    };

    public static bool IsKnownDxgi(uint dxgi) => DxgiToPs4.ContainsKey(dxgi);

    public static long PcSurfaceSize(uint dxgi, int width, int height, int depth = 1)
    {
        if (DxgiBc8.Contains(dxgi))
            return 8L * depth * ((height + 3) >> 2) * ((width + 3) >> 2);
        if (DxgiBc16.Contains(dxgi))
            return 16L * depth * ((height + 3) >> 2) * ((width + 3) >> 2);
        return (long)DxgiTexelBytes.GetValueOrDefault(dxgi) * width * height * depth;
    }

    public static int NextPow2(int x)
    {
        uint v = (uint)(x - 1);
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        return (int)(v + 1);
    }

    private static int Align(int x, int a) => (x + a - 1) & ~(a - 1);

    public sealed class Format
    {
        public uint Dxgi { get; }
        public int Ps4 { get; }
        public uint DataFormat { get; }
        public int Surface { get; }
        public int ChannelType { get; }
        public int Swizzle { get; }
        public int TotalBits { get; }
        public int Texels { get; }
        public bool IsBc => Texels > 1;
        public int BitsPerFragment => TotalBits / Texels;
        public int ElementBytes => TotalBits / 8;

        public Format(uint dxgi)
        {
            if (!DxgiToPs4.TryGetValue(dxgi, out int ps4))
                throw new InvalidDataException($"DXGI format {dxgi} has no PS4 texture format");
            Dxgi = dxgi;
            Ps4 = ps4;
            DataFormat = Ps4DataFormat[ps4];
            Surface = (int)(DataFormat & 0xFF);
            ChannelType = (int)((DataFormat >> 8) & 0xF);
            Swizzle = (int)((DataFormat >> 12) & 0xFFF);
            if (!SurfaceBits.TryGetValue(Surface, out var bits))
                throw new InvalidDataException($"DXGI format {dxgi} (PS4 {ps4}) has no texture surface layout");
            (TotalBits, Texels) = bits;
        }
    }

    public static (int Pitch, int Height, int Depth, long Size) SurfaceInfo(Format format, int tile, int width, int height, int depth, int mip, int basePitch, bool pow2Pad, bool cube, bool volume)
    {
        int bits = format.BitsPerFragment;
        int lw = width, lh = height, ld = volume ? depth : 1, request = basePitch, expand = 1;
        if (format.IsBc)
        {
            expand = 4;
            bits *= 16;
            if (mip == 0)
            {
                lw = Align(lw, 4);
                lh = Align(lh, 4);
                request = Align(request, 4);
            }
        }
        if (mip > 0)
            lw = Math.Max(basePitch >> mip, 1);
        lw = Math.Max((lw + expand - 1) / expand, 1);
        lh = Math.Max((lh + expand - 1) / expand, 1);
        request = Math.Max((request + expand - 1) / expand, 1);
        if (pow2Pad)
        {
            lw = NextPow2(lw);
            lh = NextPow2(lh);
            ld = NextPow2(ld);
            request = NextPow2(request);
        }
        int dimensions = cube && mip == 0 ? 2 : 0;
        int pitch = mip == 0 && basePitch > 0 ? request : lw;
        int outHeight = lh, outDepth = ld;
        if (mip > 0 && cube)
            dimensions = ld > 1 ? 3 : 2;
        if (dimensions == 0)
            dimensions = 3;
        if (tile == TileLinearAligned)
        {
            int bytesPerElement = (bits + 7) / 8;
            int pitchAlign = Math.Max(8, 64 / bytesPerElement);
            pitch = Align(pitch, pitchAlign);
            if (dimensions > 2 && cube)
                outDepth = NextPow2(outDepth);
            int sliceAlign = Math.Max(64, 256 / bytesPerElement);
            while ((long)pitch * outHeight % sliceAlign != 0)
                pitch += pitchAlign;
            long sliceBytes = ((long)pitch * outHeight * bits + 7) / 8;
            return (pitch, outHeight, outDepth, sliceBytes * outDepth);
        }
        if (tile == TileThin1d)
        {
            pitch = Align(pitch, 8);
            if (dimensions > 1)
                outHeight = Align(outHeight, 8);
            if (dimensions > 2 && cube)
                outDepth = NextPow2(outDepth);
            long logical = ((long)pitch * outHeight * bits + 7) / 8;
            while (logical % 256 != 0)
            {
                pitch += 8;
                logical = ((long)pitch * outHeight * bits + 7) / 8;
            }
            return (pitch, outHeight, outDepth, logical * outDepth);
        }
        throw new NotSupportedException($"Gnm tile mode {tile}");
    }

    public readonly record struct Mip(int Level, long Offset, long SizePerSlice, int Pitch, int Height, int Depth);

    public sealed class Texture
    {
        public Format Format { get; }
        public int Type { get; }
        public int Width { get; }
        public int Height { get; }
        public int Depth { get; }
        public int Levels { get; }
        public int Slices { get; }
        public int Tile { get; }
        public bool Pow2Pad => Levels > 1;
        public bool IsCube => Type == TextureCube;
        public bool IsVolume => Type == Texture3d;
        public int Pitch { get; }

        public Texture(Format format, int type, int width, int height, int depth, int levels, int slices)
        {
            Format = format;
            Type = type;
            Width = width;
            Height = height;
            Depth = type == Texture3d ? depth : 1;
            Levels = levels;
            Slices = slices;
            Tile = format.IsBc ? TileThin1d : TileLinearAligned;
            (int pitch, _, _, _) = SurfaceInfo(format, Tile, width, height, Depth, 0, 0, Pow2Pad, IsCube, IsVolume);
            Pitch = pitch * (format.IsBc ? 4 : 1);
        }

        public int ArraySliceCount
        {
            get
            {
                int n = IsCube ? Slices * 6 : IsVolume ? 1 : Slices;
                return Pow2Pad ? NextPow2(n) : n;
            }
        }

        public int LastArray => (IsCube ? Slices * 6 : IsVolume ? 1 : Slices) - 1;

        public (List<Mip> Mips, long Total) Layout()
        {
            var mips = new List<Mip>();
            long offset = 0;
            int slices = ArraySliceCount;
            for (int m = 0; m < Levels; m++)
            {
                int lw = Math.Max(Width >> m, 1), lh = Math.Max(Height >> m, 1), ld = Math.Max(Depth >> m, 1);
                (int pitch, int height, int depth, long size) = SurfaceInfo(Format, Tile, lw, lh, ld, m, Pitch, Pow2Pad, IsCube, IsVolume);
                mips.Add(new Mip(m, offset, size, pitch, height, depth));
                offset += slices * size;
                if (lw == 1 && lh == 1 && ld == 1)
                    break;
            }
            return (mips, offset);
        }

        public long TotalSize => Layout().Total;

        public byte[] Header()
        {
            var header = new byte[32];
            uint w1 = (uint)((Format.Surface & 0x3F) << 20) | (uint)(Format.ChannelType << 26);
            uint w2 = (uint)(Width - 1) | (uint)((Height - 1) << 14) | 0x70000000u;
            uint w3 = (uint)Format.Swizzle | (uint)((Levels - 1) << 16) | (uint)(Tile << 20) | (uint)((Pow2Pad ? 1 : 0) << 25) | (1u << 26) | ((uint)Type << 28);
            int depthField = IsVolume ? Depth - 1 : Slices - 1;
            uint w4 = (uint)depthField | (uint)((Pitch - 1) << 13);
            uint w5 = (uint)(LastArray << 13);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), w1);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), w2);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), w3);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), w4);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), w5);
            return header;
        }
    }

    private static readonly int[] Morton = BuildMorton();

    private static int[] BuildMorton()
    {
        var table = new int[64];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                int e = 0;
                for (int b = 0; b < 3; b++)
                {
                    e |= ((x >> b) & 1) << (2 * b);
                    e |= ((y >> b) & 1) << (2 * b + 1);
                }
                table[y * 8 + x] = e;
            }
        }
        return table;
    }

    public static void TileThin(ReadOnlySpan<byte> source, int linearWidth, int linearHeight, int linearDepth, int pitch, int height, int depth, int elementBytes, Span<byte> output)
    {
        int tilesPerRow = pitch / 8;
        long tilesPerSlice = Math.Max((long)tilesPerRow * (height / 8), 1);
        long sliceBytes = tilesPerSlice * 64 * elementBytes;
        long perSlice = (long)linearWidth * linearHeight * elementBytes;
        for (int z = 0; z < linearDepth; z++)
        {
            Span<byte> slice = output.Slice((int)(z * sliceBytes), (int)sliceBytes);
            ReadOnlySpan<byte> src = source.Slice((int)(z * perSlice), (int)perSlice);
            for (int y = 0; y < linearHeight; y++)
            {
                int rowTiles = (y >> 3) * tilesPerRow;
                int mortonRow = (y & 7) * 8;
                for (int x = 0; x < linearWidth; x++)
                {
                    long destination = ((long)(rowTiles + (x >> 3)) * 64 + Morton[mortonRow + (x & 7)]) * elementBytes;
                    src.Slice((y * linearWidth + x) * elementBytes, elementBytes).CopyTo(slice[(int)destination..]);
                }
            }
        }
    }

    public static void TileLinear(ReadOnlySpan<byte> source, int linearWidth, int linearHeight, int linearDepth, int pitch, int height, int elementBytes, Span<byte> output)
    {
        int row = linearWidth * elementBytes;
        for (int z = 0; z < linearDepth; z++)
        {
            for (int y = 0; y < linearHeight; y++)
            {
                int from = (z * linearHeight + y) * row;
                long to = ((long)z * pitch * height + (long)y * pitch) * elementBytes;
                source.Slice(from, row).CopyTo(output[(int)to..]);
            }
        }
    }
}
