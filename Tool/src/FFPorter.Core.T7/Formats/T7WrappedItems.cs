using System.Buffers.Binary;

namespace FFPorter.Core.T7.Formats;

public static class T7WrappedItems
{
    public const int KindReflectionProbes = 0, KindProbeVolume = 1, KindSst = 2, KindSkybox = 4;

    public static string? XPakType(int kind) => kind switch
    {
        KindReflectionProbes => "reflectionProbes",
        KindProbeVolume => "probeVolume",
        KindSst => "SST",
        KindSkybox => "skybox",
        _ => null,
    };

    private static readonly Dictionary<uint, (bool Block, int Unit)> Units = new()
    {
        [95] = (true, 16), [96] = (true, 16), [98] = (true, 16), [99] = (true, 16), [71] = (true, 8), [72] = (true, 8),
        [74] = (true, 16), [75] = (true, 16), [77] = (true, 16), [78] = (true, 16), [80] = (true, 8), [83] = (true, 16),
        [2] = (false, 16), [10] = (false, 8), [26] = (false, 4), [28] = (false, 4), [29] = (false, 4), [87] = (false, 4),
        [61] = (false, 1), [56] = (false, 2), [54] = (false, 2), [16] = (false, 8), [24] = (false, 4),
    };

    public sealed record Image(int MapType, int Width, int Height, int Depth, int Levels, uint Dxgi)
    {
        public static Image FromStruct(ReadOnlySpan<byte> pc) => new(pc[162], BinaryPrimitives.ReadUInt16LittleEndian(pc[196..]),
            BinaryPrimitives.ReadUInt16LittleEndian(pc[198..]), BinaryPrimitives.ReadUInt16LittleEndian(pc[200..]), pc[208],
            BinaryPrimitives.ReadUInt32LittleEndian(pc[240..]));

        public IEnumerable<(int Surfaces, int Width, int Height, int Unit)> LevelSurfaces()
        {
            if (!Units.TryGetValue(Dxgi, out var unit))
                throw new InvalidDataException($"wrapped item image format {Dxgi} is not supported");
            for (int m = 0; m < Levels; m++)
            {
                int w = Math.Max(Width >> m, 1), h = Math.Max(Height >> m, 1);
                if (unit.Block)
                {
                    w = (w + 3) / 4;
                    h = (h + 3) / 4;
                }
                int surfaces = MapType switch
                {
                    3 => Math.Max(Depth >> m, 1),
                    4 => 6,
                    5 => 6 * Depth,
                    2 => Depth,
                    _ => 1,
                };
                yield return (surfaces, w, h, unit.Unit);
            }
        }
    }

    private static int Align8(int x) => (x + 7) & ~7;

    private static long NextPow2(long x)
    {
        long p = 1;
        while (p < x)
            p <<= 1;
        return p;
    }

    private static byte[] Tile(ReadOnlySpan<byte> data, int surfaces, int width, int height, int unit)
    {
        int pitch = Align8(width), rows = Align8(height);
        var output = new byte[(long)surfaces * pitch * rows * unit];
        T7Gnm.TileThin(data, width, height, surfaces, pitch, rows, surfaces, unit, output);
        return output;
    }

    public static long Ps4LogicalSize(int kind, IReadOnlyList<Image> images, long pcSize)
    {
        if (kind == KindSst)
            return pcSize;
        if (kind == KindReflectionProbes)
        {
            long offset = 0, last = 0;
            foreach ((int surfaces, int w, int h, int unit) in images[0].LevelSurfaces())
            {
                long size = (long)surfaces * Align8(w) * Align8(h) * unit;
                last = offset + size;
                offset += NextPow2(size);
            }
            return last;
        }
        long total = 0;
        foreach (Image image in images)
            foreach ((int surfaces, int w, int h, int unit) in image.LevelSurfaces())
                total += (long)surfaces * Align8(w) * Align8(h) * unit;
        return total;
    }

    public static List<(uint LogicalOffset, byte[] Bytes)> Convert(int kind, IReadOnlyList<Image> images, ReadOnlySpan<byte> payload)
    {
        if (kind == KindSst)
            return [(0, payload.ToArray())];
        int offset = 0;
        var parts = new List<(uint, byte[])>();
        if (kind == KindReflectionProbes)
        {
            long logical = 0;
            foreach ((int surfaces, int w, int h, int unit) in images[0].LevelSurfaces())
            {
                int size = surfaces * w * h * unit;
                byte[] tiled = Tile(payload.Slice(offset, size), surfaces, w, h, unit);
                offset += size;
                parts.Add(((uint)logical, tiled));
                logical += NextPow2(tiled.Length);
            }
            CheckUsed(payload, offset);
            return parts;
        }
        using var joined = new MemoryStream();
        foreach (Image image in images)
        {
            foreach ((int surfaces, int w, int h, int unit) in image.LevelSurfaces())
            {
                int size = surfaces * w * h * unit;
                joined.Write(Tile(payload.Slice(offset, size), surfaces, w, h, unit));
                offset += size;
            }
        }
        CheckUsed(payload, offset);
        return [(0, joined.ToArray())];
    }

    private static void CheckUsed(ReadOnlySpan<byte> payload, int used)
    {
        if (used > payload.Length)
            throw new InvalidDataException($"wrapped payload is {payload.Length} bytes, its images need {used}");
        if (payload[used..].ContainsAnyExcept((byte)0))
            throw new InvalidDataException($"wrapped payload has {payload.Length - used} unexpected trailing bytes");
    }

    public static byte[] Flatten(List<(uint LogicalOffset, byte[] Bytes)> parts)
    {
        if (parts.Count == 1 && parts[0].LogicalOffset == 0)
            return parts[0].Bytes;
        long end = parts.Max(p => p.LogicalOffset + (long)p.Bytes.Length);
        var output = new byte[end];
        foreach ((uint offset, byte[] bytes) in parts)
            bytes.CopyTo(output, offset);
        return output;
    }
}
