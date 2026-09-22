using System.Buffers.Binary;
using System.Text;

namespace FFPorter.Core.T7.Havok;

public static class T7HavokNav
{
    private const byte Fill = 0x7F;

    private static readonly Dictionary<string, (int PcSize, int Ps4Size, (int Start, int End, int To)[] Moves)> ClassRules = new()
    {
        ["hkaiNavMeshGenerationSettings"] = (544, 528, [(0, 12, 0), (16, 20, 12), (32, 544, 16)]),
        ["hkcdStaticAabbTree"] = (32, 24, [(0, 12, 0), (16, 17, 12), (24, 32, 16)]),
        ["hkaiNavMeshClearanceCache"] = (112, 104, [(0, 12, 0), (16, 28, 12), (32, 112, 24)]),
    };

    private static readonly HashSet<string> IdenticalClasses =
    [
        "hkaiUserEdgePairArray", "hkaiDirectedGraphExplicitCost", "hkaiStaticTreeNavMeshQueryMediator", "hkcdStaticTreeDefaultTreeStorage6",
        "hkaiNavMesh", "hkStringObject", "hkaiNavMeshClearanceCacheSeeder", "hkaiNavVolumeGenerationSettings", "hkaiCarver",
        "hkaiPlaneVolume", "hkaiAabbTreeNavVolumeMediator", "hkaiNavVolume",
    ];

    private sealed record Section(string Tag, int HeaderAt, int Absolute, int Local, int Global, int Virtual, int Exports, int Imports, int End);

    public static byte[] ConvertPackfile(ReadOnlySpan<byte> pc)
    {
        byte[] raw = pc.ToArray();
        if (BinaryPrimitives.ReadUInt32LittleEndian(raw) != 0x57E0E057 || BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(4)) != 0x10C0C010)
            throw new InvalidDataException("not a Havok packfile");
        ReadOnlySpan<byte> layout = raw.AsSpan(16, 4);
        if (layout.SequenceEqual((ReadOnlySpan<byte>)[8, 1, 1, 1]))
            return raw;
        if (!layout.SequenceEqual((ReadOnlySpan<byte>)[8, 1, 0, 1]))
            throw new InvalidDataException($"Havok packfile layout rules {Convert.ToHexString(layout)} are not the PC ones");
        int sectionCount = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(20));
        var sections = new List<Section>();
        for (int i = 0; i < sectionCount; i++)
        {
            int at = 64 + 64 * i;
            string tag = Encoding.ASCII.GetString(raw, at, 20).TrimEnd('\0');
            int U(int k) => (int)BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(at + 20 + 4 * k));
            sections.Add(new Section(tag, at, U(0), U(1), U(2), U(3), U(4), U(5), U(6)));
        }
        Section Find(string tag) => sections.Find(s => s.Tag.StartsWith(tag, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Havok packfile has no {tag} section");
        Section classNamesSection = Find("__classnames__"), data = Find("__data__");
        int dataIndex = sections.IndexOf(data);

        var classNames = new Dictionary<int, string>();
        for (int q = 0; q + 5 <= classNamesSection.Local;)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(classNamesSection.Absolute + q)) == 0xFFFFFFFF)
                break;
            int nameStart = classNamesSection.Absolute + q + 5;
            int end = Array.IndexOf(raw, (byte)0, nameStart);
            classNames[q + 5] = Encoding.ASCII.GetString(raw, nameStart, end - nameStart);
            q = end + 1 - classNamesSection.Absolute;
        }
        byte[] body = raw.AsSpan(data.Absolute, data.Local).ToArray();
        var local = new List<(uint Source, uint Target)>();
        for (int o = data.Local; o + 8 <= data.Global; o += 8)
        {
            uint a = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(data.Absolute + o));
            if (a != 0xFFFFFFFF)
                local.Add((a, BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(data.Absolute + o + 4))));
        }
        List<(uint Source, uint Section, uint Target)> Triples(int from, int to)
        {
            var list = new List<(uint, uint, uint)>();
            for (int o = from; o + 12 <= to; o += 12)
            {
                uint a = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(data.Absolute + o));
                if (a != 0xFFFFFFFF)
                    list.Add((a, BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(data.Absolute + o + 4)), BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(data.Absolute + o + 8))));
            }
            return list;
        }
        var global = Triples(data.Global, data.Virtual);
        var objects = Triples(data.Virtual, data.Exports);
        if (data.Exports != data.Imports || data.Imports != data.End)
            throw new InvalidDataException("Havok packfile with exports or imports is not supported");

        var classAt = new Dictionary<int, string>();
        foreach ((uint source, uint _, uint name) in objects)
        {
            string className = classNames.GetValueOrDefault((int)name) ?? throw new InvalidDataException($"Havok object class name at {name} not found");
            if (!ClassRules.ContainsKey(className) && !IdenticalClasses.Contains(className))
                throw new InvalidDataException($"Havok class {className} has no PS4 layout rule");
            classAt[(int)source] = className;
        }
        var bounds = new SortedSet<int> { 0, body.Length };
        foreach (int start in classAt.Keys)
            bounds.Add(start);
        foreach ((uint _, uint target) in local)
            bounds.Add((int)target);
        foreach ((uint _, uint section, uint target) in global)
        {
            if (section == dataIndex)
                bounds.Add((int)target);
        }
        int[] edges = bounds.Where(b => b >= 0 && b <= body.Length).ToArray();

        using var output = new MemoryStream();
        var chunks = new List<(int Start, int End, int To, (int Start, int End, int To)[]? Moves)>();
        for (int c = 0; c + 1 < edges.Length; c++)
        {
            int start = edges[c], end = edges[c + 1];
            if (start % 16 != 0)
                throw new InvalidDataException($"Havok data chunk at {start} is not 16-aligned");
            int to = (int)output.Length;
            if (classAt.TryGetValue(start, out string? className) && ClassRules.TryGetValue(className, out var rule))
            {
                if (end - start < rule.PcSize || body.AsSpan(start + rule.PcSize, end - start - rule.PcSize).ContainsAnyExcept(Fill))
                    throw new InvalidDataException($"Havok {className} object size {end - start} does not match its rule");
                var obj = new byte[rule.Ps4Size];
                foreach ((int a, int b, int t) in rule.Moves)
                    body.AsSpan(start + a, b - a).CopyTo(obj.AsSpan(t));
                output.Write(obj);
                while (output.Length % 16 != 0)
                    output.WriteByte(Fill);
                chunks.Add((start, end, to, rule.Moves));
            }
            else
            {
                output.Write(body, start, end - start);
                chunks.Add((start, end, to, null));
            }
        }
        int Map(uint pcOffset)
        {
            int x = (int)pcOffset;
            foreach ((int start, int end, int to, var moves) in chunks)
            {
                if (!(start <= x && x < end) && !(x == end && end == body.Length))
                    continue;
                if (moves == null)
                    return to + (x - start);
                int r = x - start;
                foreach ((int a, int b, int t) in moves)
                {
                    if (a <= r && r < b)
                        return to + t + (r - a);
                }
                throw new InvalidDataException($"Havok offset {x} falls into dropped padding");
            }
            throw new InvalidDataException($"Havok offset {x} is outside the data section");
        }
        byte[] Table(IEnumerable<uint[]> entries)
        {
            using var table = new MemoryStream();
            foreach (uint[] entry in entries)
                foreach (uint value in entry)
                    table.Write(BitConverter.GetBytes(value));
            while (table.Length % 16 != 0)
                table.WriteByte(0xFF);
            return table.ToArray();
        }
        byte[] localTable = Table(local.Select(f => new[] { (uint)Map(f.Source), (uint)Map(f.Target) }));
        byte[] globalTable = Table(global.Select(f => new[] { (uint)Map(f.Source), f.Section, f.Section == dataIndex ? (uint)Map(f.Target) : f.Target }));
        byte[] virtualTable = Table(objects.Select(f => new[] { (uint)Map(f.Source), f.Section, f.Target }));

        byte[] body4 = output.ToArray();
        var result = new byte[data.Absolute + body4.Length + localTable.Length + globalTable.Length + virtualTable.Length];
        raw.AsSpan(0, data.Absolute).CopyTo(result);
        result[0x12] = 1;
        int localOffset = body4.Length, globalOffset = localOffset + localTable.Length, virtualOffset = globalOffset + globalTable.Length;
        int endOffset = virtualOffset + virtualTable.Length;
        int header = data.HeaderAt + 20;
        uint[] offsets = [(uint)data.Absolute, (uint)localOffset, (uint)globalOffset, (uint)virtualOffset, (uint)endOffset, (uint)endOffset, (uint)endOffset];
        for (int k = 0; k < offsets.Length; k++)
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(header + 4 * k), offsets[k]);
        int cursor = data.Absolute;
        foreach (byte[] part in new[] { body4, localTable, globalTable, virtualTable })
        {
            part.CopyTo(result, cursor);
            cursor += part.Length;
        }
        return result;
    }
}
