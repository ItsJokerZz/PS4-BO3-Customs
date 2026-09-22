using System.Buffers.Binary;
using System.Text;

namespace FFPorter.Core.T7.Shaders;

public static class T7Dxbc
{
    public sealed record Variable(string Name, int Start, int Size, uint Flags, int Class, int Type, int Rows, int Columns, int Elements)
    {
        public bool Used => (Flags & 2) != 0;
    }

    public sealed record ConstantBuffer(string Name, int Size, IReadOnlyList<Variable> Variables);

    public sealed record Resource(string Name, int Type, int Bind, int Count, int ReturnType = 0, int Dimension = 0, uint Flags = 0)
    {
        public bool IsSampler => Type == 3;
        public bool IsTextureRegister => Type is 1 or 2 or 5 or 7;
        public bool IsUnorderedAccess => Type is 4 or 6 or 8 or 9 or 10 or 11;
        public int Components => (int)((Flags >> 2) & 3) + 1;
        public bool IsComparisonSampler => (Flags & 2) != 0;
    }

    public sealed record SignatureElement(string Semantic, int Index, int Register, int SystemValue, byte Mask, byte UsedMask, int ComponentType = 3);

    public static IReadOnlyList<ConstantBuffer>? ConstantBuffers(ReadOnlySpan<byte> dxbc)
    {
        ReadOnlySpan<byte> rdef = Chunk(dxbc, "RDEF"u8);
        return rdef.IsEmpty ? null : ParseRdef(rdef);
    }

    public static IReadOnlyList<Resource>? Resources(ReadOnlySpan<byte> dxbc)
    {
        ReadOnlySpan<byte> rdef = Chunk(dxbc, "RDEF"u8);
        if (rdef.IsEmpty)
            return null;
        int count = BinaryPrimitives.ReadInt32LittleEndian(rdef[8..]);
        int offset = BinaryPrimitives.ReadInt32LittleEndian(rdef[12..]);
        var resources = new List<Resource>(count);
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = rdef[(offset + 32 * i)..];
            resources.Add(new Resource(CString(rdef, BinaryPrimitives.ReadInt32LittleEndian(entry)),
                BinaryPrimitives.ReadInt32LittleEndian(entry[4..]), BinaryPrimitives.ReadInt32LittleEndian(entry[20..]),
                Math.Max(1, BinaryPrimitives.ReadInt32LittleEndian(entry[24..])), BinaryPrimitives.ReadInt32LittleEndian(entry[8..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[12..]), BinaryPrimitives.ReadUInt32LittleEndian(entry[28..])));
        }
        return resources;
    }

    public static IReadOnlyList<SignatureElement>? InputSignature(ReadOnlySpan<byte> dxbc) => Signature(dxbc, "ISGN"u8, "ISG1"u8);

    public static IReadOnlyList<SignatureElement>? OutputSignature(ReadOnlySpan<byte> dxbc) => Signature(dxbc, "OSGN"u8, "OSG1"u8);

    private static IReadOnlyList<SignatureElement>? Signature(ReadOnlySpan<byte> dxbc, ReadOnlySpan<byte> tag, ReadOnlySpan<byte> extendedTag)
    {
        bool extended = false;
        ReadOnlySpan<byte> chunk = Chunk(dxbc, tag);
        if (chunk.IsEmpty)
        {
            chunk = Chunk(dxbc, extendedTag);
            extended = true;
        }
        if (chunk.IsEmpty)
            return null;
        int count = BinaryPrimitives.ReadInt32LittleEndian(chunk);
        int size = extended ? 32 : 24;
        var elements = new List<SignatureElement>(count);
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = chunk[(8 + size * i)..];
            if (extended)
                entry = entry[4..];
            elements.Add(new SignatureElement(CString(chunk, BinaryPrimitives.ReadInt32LittleEndian(entry)),
                BinaryPrimitives.ReadInt32LittleEndian(entry[4..]), BinaryPrimitives.ReadInt32LittleEndian(entry[16..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[8..]), entry[20], entry[21], BinaryPrimitives.ReadInt32LittleEndian(entry[12..])));
        }
        return elements;
    }

    public static ReadOnlySpan<byte> Code(ReadOnlySpan<byte> dxbc)
    {
        ReadOnlySpan<byte> code = Chunk(dxbc, "SHEX"u8);
        return code.IsEmpty ? Chunk(dxbc, "SHDR"u8) : code;
    }

    private static ReadOnlySpan<byte> Chunk(ReadOnlySpan<byte> dxbc, ReadOnlySpan<byte> tag)
    {
        if (dxbc.Length < 32 || !dxbc[..4].SequenceEqual("DXBC"u8))
            return [];
        int chunks = BinaryPrimitives.ReadInt32LittleEndian(dxbc[28..]);
        for (int i = 0; i < chunks && 32 + 4 * i + 4 <= dxbc.Length; i++)
        {
            int at = BinaryPrimitives.ReadInt32LittleEndian(dxbc[(32 + 4 * i)..]);
            if (at < 0 || at + 8 > dxbc.Length || !dxbc.Slice(at, 4).SequenceEqual(tag))
                continue;
            int size = BinaryPrimitives.ReadInt32LittleEndian(dxbc[(at + 4)..]);
            return dxbc.Slice(at + 8, Math.Min(size, dxbc.Length - at - 8));
        }
        return [];
    }

    private static List<ConstantBuffer> ParseRdef(ReadOnlySpan<byte> rdef)
    {
        int bufferCount = BinaryPrimitives.ReadInt32LittleEndian(rdef);
        int bufferOffset = BinaryPrimitives.ReadInt32LittleEndian(rdef[4..]);
        var buffers = new List<ConstantBuffer>(bufferCount);
        for (int i = 0; i < bufferCount; i++)
        {
            ReadOnlySpan<byte> record = rdef[(bufferOffset + 24 * i)..];
            int nameOffset = BinaryPrimitives.ReadInt32LittleEndian(record);
            int variableCount = BinaryPrimitives.ReadInt32LittleEndian(record[4..]);
            int variableOffset = BinaryPrimitives.ReadInt32LittleEndian(record[8..]);
            int size = BinaryPrimitives.ReadInt32LittleEndian(record[12..]);
            var variables = new List<Variable>(variableCount);
            for (int v = 0; v < variableCount; v++)
            {
                ReadOnlySpan<byte> entry = rdef[(variableOffset + 40 * v)..];
                int typeOffset = BinaryPrimitives.ReadInt32LittleEndian(entry[16..]);
                ReadOnlySpan<byte> type = rdef[typeOffset..];
                variables.Add(new Variable(
                    CString(rdef, BinaryPrimitives.ReadInt32LittleEndian(entry)),
                    BinaryPrimitives.ReadInt32LittleEndian(entry[4..]),
                    BinaryPrimitives.ReadInt32LittleEndian(entry[8..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(type),
                    BinaryPrimitives.ReadUInt16LittleEndian(type[2..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(type[4..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(type[6..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(type[8..])));
            }
            buffers.Add(new ConstantBuffer(CString(rdef, nameOffset), size, variables));
        }
        return buffers;
    }

    private static string CString(ReadOnlySpan<byte> data, int offset)
    {
        ReadOnlySpan<byte> rest = data[offset..];
        int end = rest.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? rest : rest[..end]);
    }

    public static (List<(Variable Variable, int Offset)> Layout, int End) Pack(IEnumerable<Variable> variables)
    {
        int position = 0;
        bool forceNewRow = false;
        var layout = new List<(Variable, int)>();
        foreach (Variable variable in variables)
        {
            bool isArray = variable.Elements > 0, isStruct = variable.Class == 5, isMatrix = variable.Class is 2 or 3;
            if (position % 4 != 0)
                position = (position + 3) & ~3;
            int rowLeft = position % 16 != 0 ? 16 - position % 16 : 16;
            if ((forceNewRow || isArray || isStruct || isMatrix || variable.Size > rowLeft) && position % 16 != 0)
                position = (position + 15) & ~15;
            layout.Add((variable, position));
            position += variable.Size;
            forceNewRow = isArray || isStruct;
        }
        return (layout, position);
    }
}
