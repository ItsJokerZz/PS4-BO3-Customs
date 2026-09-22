using System.Buffers.Binary;
using System.Text.Json;

namespace FFPorter.Core.T7.Scripts;

public sealed class T7Gsc
{
    public const byte Version = 0x1C;
    private readonly Dictionary<ushort, (string Name, string Operands)> _pc = [];
    private readonly Dictionary<string, ushort> _ps4 = new(StringComparer.Ordinal);

    public static T7Gsc Load(string opcodeMap)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(opcodeMap));
        var gsc = new T7Gsc();
        foreach (JsonElement op in document.RootElement.GetProperty("ops").EnumerateArray())
        {
            string name = op.GetProperty("name").GetString()!;
            string operands = op.GetProperty("operands").GetString() ?? "";
            foreach (JsonElement value in op.GetProperty("pc_v1c_values").EnumerateArray())
                gsc._pc[(ushort)value.GetInt32()] = (name, operands);
            JsonElement ps4 = op.GetProperty("ps4_values");
            if (ps4.GetArrayLength() > 0)
                gsc._ps4[name] = (ushort)ps4.EnumerateArray().Select(v => v.GetInt32()).Min();
        }
        return gsc;
    }

    public byte[] Convert(ReadOnlySpan<byte> pc)
    {
        if (pc.Length < 0x48 || !pc[..7].SequenceEqual((ReadOnlySpan<byte>)[0x80, (byte)'G', (byte)'S', (byte)'C', 0x0D, 0x0A, 0x00]))
            throw new InvalidDataException("not a compiled GSC buffer");
        if (pc[7] != Version)
            throw new InvalidDataException($"GSC version 0x{pc[7]:x2}: only 0x1C (the version both executables load) converts");
        byte[] buffer = pc.ToArray();
        int codeStart = (int)Read32(buffer, 0x14), codeSize = (int)Read32(buffer, 0x30);
        int exportsOffset = (int)Read32(buffer, 0x20), exportCount = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(0x3A));
        int codeEnd = codeStart + codeSize;
        var exports = Enumerable.Range(0, exportCount)
            .Select(i => (Position: exportsOffset + 20 * i, Crc: Read32(buffer, exportsOffset + 20 * i), Offset: (int)Read32(buffer, exportsOffset + 20 * i + 4)))
            .OrderBy(e => e.Offset).ToList();
        var functions = new List<(int Position, int Start, int End)>();
        for (int k = 0; k < exports.Count; k++)
        {
            (int position, uint crc, int start) = exports[k];
            int end = -1;
            if (k + 1 == exports.Count)
            {
                end = codeEnd;
            }
            else
            {
                int next = exports[k + 1].Offset;
                for (int candidate = next - 8; candidate > next - 16; candidate--)
                {
                    if (candidate > start && Crc32(buffer.AsSpan(start, candidate - start)) == crc)
                    {
                        end = candidate;
                        break;
                    }
                }
                if (end < 0)
                    end = LayoutEnd(buffer, start, next);
            }
            if (end < 0)
                throw new InvalidDataException($"GSC export at 0x{start:x}: no function end matches its CRC or the linker's layout");
            functions.Add((position, start, end));
        }
        foreach ((int _, int start, int end) in functions)
            Remap(buffer, start, end);
        foreach ((int position, int start, int end) in functions)
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(position), Crc32(buffer.AsSpan(start, end - start)));
        return buffer;
    }

    private int LayoutEnd(byte[] buffer, int start, int next)
    {
        int zeros = next - 8;
        if (zeros <= start || buffer.AsSpan(zeros, 8).ContainsAnyExcept((byte)0))
            return -1;
        int end = -1;
        int p = start;
        while (true)
        {
            p = (p + 1) & ~1;
            if (p >= zeros || !_pc.TryGetValue(BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(p)), out var op))
                break;
            p = BoundedOperandEnd(buffer, op.Operands, p + 2, zeros);
            if (p > zeros)
                break;
            if (p > zeros - 8 && (op.Name is "End" or "Return"))
                end = p;
        }
        return end;
    }

    private static int BoundedOperandEnd(byte[] buffer, string format, int p, int limit)
    {
        if (format == "locals" && p >= limit)
            return int.MaxValue;
        if (format == "endswitch")
        {
            int q = A4(p);
            if (q + 4 > limit || Read32(buffer, q) > (uint)(limit - q - 4) / 8)
                return int.MaxValue;
        }
        return OperandEnd(buffer, format, p);
    }

    private void Remap(byte[] buffer, int start, int end)
    {
        int p = start;
        while (true)
        {
            p = (p + 1) & ~1;
            if (p >= end)
                break;
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(p));
            if (!_pc.TryGetValue(value, out var op))
                throw new InvalidDataException($"invalid PC GSC opcode 0x{value:x4} at 0x{p:x}");
            if (!_ps4.TryGetValue(op.Name, out ushort ps4))
                throw new InvalidDataException($"GSC opcode {op.Name} has no PS4 value");
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(p), ps4);
            p = OperandEnd(buffer, op.Operands, p + 2);
        }
        if (p != end && ((end + 1) & ~1) != p)
            throw new InvalidDataException($"GSC function 0x{start:x}..0x{end:x} decodes to 0x{p:x}");
    }

    private static int A4(int x) => (x + 3) & ~3;
    private static int A8(int x) => (x + 7) & ~7;

    private static int OperandEnd(byte[] buffer, string format, int p) => format switch
    {
        "" => p,
        "u8" => p + 1,
        "u16" => ((p + 1) & ~1) + 2,
        "u32" or "switch" => A4(p) + 4,
        "u64" => A8(p) + 8,
        "vec" => A4(p) + 12,
        "u8_u32" => A4(p + 1) + 4,
        "u8_u64" => A8(p + 1) + 8,
        "endswitch" => EndSwitch(buffer, p),
        "locals" => Locals(buffer, p),
        _ => throw new InvalidDataException($"unknown GSC operand format '{format}'"),
    };

    private static int EndSwitch(byte[] buffer, int p)
    {
        int q = A4(p);
        uint count = Read32(buffer, q);
        int r = q + 4;
        for (uint i = 0; i < count; i++)
            r = A4(r) + 8;
        return r;
    }

    private static int Locals(byte[] buffer, int p)
    {
        int count = buffer[p];
        int r = p + 1;
        for (int i = 0; i < count; i++)
            r = A4(r) + 4 + 1;
        return r;
    }

    private static uint Read32(byte[] buffer, int at) => BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(at));

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++)
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }
}
