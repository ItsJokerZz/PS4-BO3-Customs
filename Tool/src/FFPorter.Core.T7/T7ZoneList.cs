using System.Buffers.Binary;
using System.Text;

namespace FFPorter.Core.T7;

public sealed class T7ZoneList
{
    public readonly record struct Entry(int Type, ulong Header);

    public List<string?> ScriptStrings { get; } = [];
    public List<(int Cell, int Text)> ScriptStringOffsets { get; } = [];
    public List<string> Depends { get; } = [];
    public List<Entry> Assets { get; } = [];

    public int AssetTableOffset { get; private set; }

    public int DataOffset { get; private set; }

    public static T7ZoneList Parse(ReadOnlySpan<byte> zone)
    {
        var list = new T7ZoneList();
        int stringCount = BinaryPrimitives.ReadInt32LittleEndian(zone);
        ulong strings = BinaryPrimitives.ReadUInt64LittleEndian(zone[8..]);
        int dependCount = BinaryPrimitives.ReadInt32LittleEndian(zone[0x10..]);
        ulong depends = BinaryPrimitives.ReadUInt64LittleEndian(zone[0x18..]);
        int assetCount = BinaryPrimitives.ReadInt32LittleEndian(zone[0x20..]);
        ulong assets = BinaryPrimitives.ReadUInt64LittleEndian(zone[0x28..]);
        int position = 48;
        if (strings != 0)
            position = ReadStrings(zone, position, stringCount, list.ScriptStrings, list.ScriptStringOffsets);
        if (depends != 0)
        {
            var names = new List<string?>();
            position = ReadStrings(zone, position, dependCount, names, []);
            list.Depends.AddRange(names.Select(n => n ?? ""));
        }
        list.AssetTableOffset = position;
        if (assets != 0)
        {
            for (int i = 0; i < assetCount; i++)
                list.Assets.Add(new Entry(BinaryPrimitives.ReadInt32LittleEndian(zone[(position + 16 * i)..]), BinaryPrimitives.ReadUInt64LittleEndian(zone[(position + 16 * i + 8)..])));
            position += 16 * assetCount;
        }
        list.DataOffset = position;
        return list;
    }

    private static int ReadStrings(ReadOnlySpan<byte> zone, int position, int count, List<string?> into, List<(int Cell, int Text)> offsets)
    {
        int table = position;
        position += 8 * count;
        for (int i = 0; i < count; i++)
        {
            ulong pointer = BinaryPrimitives.ReadUInt64LittleEndian(zone[(table + 8 * i)..]);
            if (pointer == ulong.MaxValue)
            {
                int end = zone[position..].IndexOf((byte)0);
                into.Add(Encoding.UTF8.GetString(zone.Slice(position, end)));
                offsets.Add((table + 8 * i, position));
                position += end + 1;
            }
            else
            {
                into.Add(null);
                offsets.Add((table + 8 * i, -1));
            }
        }
        return position;
    }
}
