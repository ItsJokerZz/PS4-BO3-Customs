using System.Buffers.Binary;
using System.Text;
using FFPorter.Core.T7.Harness;

namespace FFPorter.Core.T7.Link;

public abstract record T7Target;

public sealed record T7SourceTarget(T7WalkIndex Source, T7BlockAddress Address, long ReferencingFileOffset) : T7Target;

public sealed record T7AliasTarget(int OutputAsset, int Slot = 0) : T7Target;

public sealed record T7OutputTarget(long OutputFileOffset) : T7Target;

public sealed record T7SourceFileTarget(T7WalkIndex Source, long FileOffset) : T7Target;

public sealed record T7PoolTarget(string Key) : T7Target;

public readonly record struct T7Fixup(long Offset, T7Target Target);

public abstract record T7Piece
{
    public abstract long Length { get; }
}

public sealed record T7CopyPiece(T7WalkIndex Source, long FileOffset, long Size) : T7Piece
{
    public override long Length => Size;
}

public readonly record struct T7PieceSpan(long SourceOffset, long Size, long PieceOffset);

public sealed record T7DataPiece(byte[] Data, IReadOnlyList<T7Fixup> Fixups, T7WalkIndex? Source = null, IReadOnlyList<T7PieceSpan>? Spans = null) : T7Piece
{
    public override long Length => Data.Length;

    public IReadOnlyList<(string Key, long Offset)>? Pools { get; init; }
}

public sealed class T7OutputAsset
{
    public int Type;
    public ulong HeaderMarker = ulong.MaxValue;
    public T7Target? HeaderTarget;
    public readonly List<T7Piece> Main = [];
    public readonly List<T7Piece> Deferred = [];
    public string? Name;
    public string Origin = "";
}

public sealed class T7ZoneBuilder
{
    public const ulong Placeholder = 0x5000000000000001;

    public List<string?> ScriptStrings { get; } = [];
    public List<T7OutputAsset> Assets { get; } = [];

    private readonly List<(long Field, T7Target Target)> _fixups = [];
    private readonly Dictionary<T7WalkIndex, List<(long Source, long Size, long Output)>> _spans = [];
    private readonly Dictionary<(T7WalkIndex, int), int> _assetMap = [];
    private readonly Dictionary<(T7WalkIndex, int, int), (int Asset, int Slot)> _slotMap = [];

    public IReadOnlyList<(long Field, T7Target Target)> Fixups => _fixups;

    public HashSet<(T7WalkIndex Source, long Field)> NullFields { get; } = [];

    public void MapAsset(T7WalkIndex source, int sourceAsset, int outputAsset) => _assetMap[(source, sourceAsset)] = outputAsset;

    private readonly Dictionary<T7WalkIndex, IReadOnlyList<int>> _stringMaps = [];

    public void MapScriptStrings(T7WalkIndex source, IReadOnlyList<int> outputIndexOfSourceString) => _stringMaps[source] = outputIndexOfSourceString;

    public int AddScriptString(string? text)
    {
        if (text != null)
        {
            if (_stringIndex.TryGetValue(text, out int existing))
                return existing;
            _stringIndex[text] = ScriptStrings.Count;
        }
        ScriptStrings.Add(text);
        return ScriptStrings.Count - 1;
    }

    private readonly Dictionary<string, int> _stringIndex = new(StringComparer.Ordinal);

    public void MapSpan(T7WalkIndex source, long sourceOffset, long size, long outputOffset)
    {
        if (!_spans.TryGetValue(source, out var list))
            _spans[source] = list = [];
        list.Add((sourceOffset, size, outputOffset));
    }

    private readonly Dictionary<string, long> _pools = new(StringComparer.Ordinal);

    public byte[] Layout()
    {
        _fixups.Clear();
        _spans.Clear();
        _pools.Clear();
        var output = new MemoryStream();
        var writer = new BinaryWriter(output);
        writer.Write(ScriptStrings.Count);
        writer.Write(0);
        writer.Write(ScriptStrings.Count > 0 ? ulong.MaxValue : 0UL);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0UL);
        writer.Write(Assets.Count);
        writer.Write(0);
        writer.Write(Assets.Count > 0 ? ulong.MaxValue : 0UL);
        writer.Flush();
        long cells = output.Position;
        foreach (string? text in ScriptStrings)
            writer.Write(text == null ? 0UL : ulong.MaxValue);
        var textOffsets = new long[ScriptStrings.Count];
        for (int i = 0; i < ScriptStrings.Count; i++)
        {
            string? text = ScriptStrings[i];
            writer.Flush();
            textOffsets[i] = text == null ? -1 : output.Position;
            if (text != null)
            {
                writer.Write(Encoding.UTF8.GetBytes(text));
                writer.Write((byte)0);
            }
        }
        foreach ((T7WalkIndex source, IReadOnlyList<int> map) in _stringMaps)
        {
            for (int s = 0; s < map.Count && s < source.List.ScriptStringOffsets.Count; s++)
            {
                int o = map[s];
                if (o < 0)
                    continue;
                (int cell, int textAt) = source.List.ScriptStringOffsets[s];
                MapSpan(source, cell, 8, cells + 8L * o);
                if (textAt >= 0 && textOffsets[o] >= 0)
                    MapSpan(source, textAt, Encoding.UTF8.GetByteCount(ScriptStrings[o]!) + 1, textOffsets[o]);
            }
        }
        writer.Flush();
        long table = output.Position;
        foreach (((T7WalkIndex source, int sourceAsset), int outputAsset) in _assetMap)
            MapSpan(source, source.List.AssetTableOffset + 16L * sourceAsset, 16, table + 16L * outputAsset);
        foreach (T7OutputAsset asset in Assets)
        {
            writer.Write(asset.Type);
            writer.Write(0);
            if (asset.HeaderTarget != null)
            {
                _fixups.Add((output.Position, asset.HeaderTarget));
                writer.Write(Placeholder);
            }
            else
            {
                writer.Write(asset.HeaderMarker);
            }
        }
        writer.Flush();
        foreach (T7OutputAsset asset in Assets)
            foreach (T7Piece piece in asset.Main)
                Emit(output, piece);
        foreach (T7OutputAsset asset in Assets)
            foreach (T7Piece piece in asset.Deferred)
                Emit(output, piece);
        return output.ToArray();
    }

    private void Emit(MemoryStream output, T7Piece piece)
    {
        long at = output.Position;
        switch (piece)
        {
            case T7CopyPiece copy:
            {
                output.Write(copy.Source.Zone, (int)copy.FileOffset, (int)copy.Size);
                MapSpan(copy.Source, copy.FileOffset, copy.Size, at);
                foreach ((long field, T7PointerKind kind, T7BlockAddress target) in copy.Source.PointersIn(copy.FileOffset, copy.Size))
                {
                    if (kind is T7PointerKind.Packed or T7PointerKind.PackedAlias)
                    {
                        long outField = at + (field - copy.FileOffset);
                        _fixups.Add((outField, new T7SourceTarget(copy.Source, target, field)));
                        long save = output.Position;
                        output.Position = outField;
                        output.Write(BitConverter.GetBytes(Placeholder));
                        output.Position = save;
                    }
                }
                break;
            }
            case T7DataPiece data:
            {
                output.Write(data.Data);
                if (data.Source != null && data.Spans != null)
                    foreach (T7PieceSpan span in data.Spans)
                        MapSpan(data.Source, span.SourceOffset, span.Size, at + span.PieceOffset);
                if (data.Pools != null)
                    foreach ((string key, long offset) in data.Pools)
                        _pools.TryAdd(key, at + offset);
                foreach (T7Fixup fixup in data.Fixups)
                {
                    _fixups.Add((at + fixup.Offset, fixup.Target));
                    long save = output.Position;
                    output.Position = at + fixup.Offset;
                    output.Write(BitConverter.GetBytes(Placeholder));
                    output.Position = save;
                }
                break;
            }
            default:
                throw new InvalidOperationException($"{piece.GetType().Name} cannot be laid out (an unfinished rebuilt read)");
        }
    }

    public List<string> Link(byte[] zone, T7WalkIndex output)
    {
        var problems = new List<string>();
        foreach (var list in _spans.Values)
            list.Sort((a, b) => a.Source.CompareTo(b.Source));
        foreach ((long field, T7Target target) in _fixups)
        {
            if (target is T7SourceTarget nulled && NullFields.Contains((nulled.Source, nulled.ReferencingFileOffset)))
            {
                BinaryPrimitives.WriteUInt64LittleEndian(zone.AsSpan((int)field, 8), 0);
                continue;
            }
            if (!TryResolve(target, output, out T7BlockAddress address, out string? why))
            {
                if (problems.Count < 200)
                    problems.Add($"field 0x{field:x} (output asset {output.AssetAt(field)}): {why}");
                continue;
            }
            BinaryPrimitives.WriteUInt64LittleEndian(zone.AsSpan((int)field, 8), address.Packed);
        }
        return problems;
    }

    private bool TryResolve(T7Target target, T7WalkIndex output, out T7BlockAddress address, out string? why)
    {
        address = T7BlockAddress.None;
        why = null;
        switch (target)
        {
            case T7OutputTarget o:
                if (output.TryBlockAddress(o.OutputFileOffset, out address))
                    return true;
                why = $"output offset 0x{o.OutputFileOffset:x} was not read by the loader";
                return false;
            case T7PoolTarget pool:
            {
                if (!_pools.TryGetValue(pool.Key, out long pooled))
                {
                    why = $"no piece defines the pooled data '{pool.Key}'";
                    return false;
                }
                if (output.TryBlockAddress(pooled, out address))
                    return true;
                why = $"pooled data '{pool.Key}' at output offset 0x{pooled:x} was not read by the loader";
                return false;
            }
            case T7SourceFileTarget f:
            {
                if (!TryMapOffset(f.Source, f.FileOffset, out long mapped))
                {
                    why = $"{f.Source.Label} file 0x{f.FileOffset:x} (asset {f.Source.AssetAt(f.FileOffset)}) is not in the output";
                    return false;
                }
                if (output.TryBlockAddress(mapped, out address))
                    return true;
                why = $"output offset 0x{mapped:x} (from {f.Source.Label} 0x{f.FileOffset:x}) was not read by the loader";
                return false;
            }
            case T7AliasTarget a:
            {
                IReadOnlyList<T7BlockAddress> slots = output.AliasSlotsOf(a.OutputAsset);
                if (a.Slot < slots.Count)
                {
                    address = slots[a.Slot];
                    return true;
                }
                why = $"output asset {a.OutputAsset} has {slots.Count} alias slots, slot {a.Slot} wanted";
                return false;
            }
            case T7SourceTarget s:
            {
                if (s.Source.TryAliasOwner(s.Address, out int sourceAsset))
                {
                    int ordinal = IndexOf(s.Source.AliasSlotsOf(sourceAsset), s.Address);
                    if (_assetMap.TryGetValue((s.Source, sourceAsset), out int outputAsset)
                        && TryResolve(new T7AliasTarget(outputAsset, ordinal), output, out address, out why))
                        return true;
                    if (s.Source.TryAliasRegistration(s.Address, out T7Walk.Registration registered) && !string.IsNullOrEmpty(registered.Name)
                        && output.TryHeaderCell(registered.Type, OutputName(registered.Type, registered.Name), out address))
                        return true;
                    if (s.Source.TryAliasHeader(s.Address, out T7BlockAddress header)
                        && s.Source.TryRegistrationAt(header, s.Source.Walk.Assets[sourceAsset].Start, out int type, out string? name)
                        && name != null && output.TryHeaderCell(type, OutputName(type, name), out address))
                        return true;
                    why ??= $"{s.Source.Label} asset {sourceAsset} (alias target) has no output asset";
                    return false;
                }
                if (!s.Source.TryFileOffset(s.Address, out long sourceOffset, s.ReferencingFileOffset))
                {
                    why = $"{s.Source.Label} address {s.Address} holds no file data";
                    return false;
                }
                if (!TryMapOffset(s.Source, sourceOffset, out long outputOffset))
                {
                    if (TryResolveHeaderCell(s.Source, sourceOffset, output, out address))
                        return true;
                    why = $"{s.Source.Label} file 0x{sourceOffset:x} (asset {s.Source.AssetAt(sourceOffset)}) is not in the output";
                    return false;
                }
                if (output.TryBlockAddress(outputOffset, out address))
                    return true;
                why = $"output offset 0x{outputOffset:x} (from {s.Source.Label} 0x{sourceOffset:x}) was not read by the loader";
                return false;
            }
        }
        why = "unknown target";
        return false;
    }

    public Func<int, string, string> OutputName { get; set; } = (_, name) => name;

    private bool TryResolveHeaderCell(T7WalkIndex source, long cellFileOffset, T7WalkIndex output, out T7BlockAddress address)
    {
        address = T7BlockAddress.None;
        if (!source.Pointers.TryGetValue(cellFileOffset, out var cell) || cell.Kind is not (T7PointerKind.Inline or T7PointerKind.InlineAlias))
            return false;
        int type;
        string? name;
        if (source.TryInlineRegistration(cellFileOffset, out T7Walk.Registration exact))
            (type, name) = (exact.Type, exact.Name);
        else if (!source.TryRegistrationAt(cell.Target, cellFileOffset, out type, out name))
            return false;
        if (string.IsNullOrEmpty(name))
            return false;
        return output.TryHeaderCell(type, OutputName(type, name), out address);
    }

    private static int IndexOf(IReadOnlyList<T7BlockAddress> slots, T7BlockAddress address)
    {
        for (int i = 0; i < slots.Count; i++)
            if (slots[i] == address)
                return i;
        return 0;
    }

    private readonly Dictionary<T7WalkIndex, List<(long Start, long Size, T7WalkIndex Other, long OtherStart)>> _equivalents = [];

    public void MapEquivalent(T7WalkIndex source, long start, long size, T7WalkIndex other, long otherStart)
    {
        if (!_equivalents.TryGetValue(source, out var list))
            _equivalents[source] = list = [];
        list.Add((start, size, other, otherStart));
    }

    private bool TryMapOffset(T7WalkIndex source, long offset, out long output)
    {
        if (TryMapDirect(source, offset, out output))
            return true;
        if (_equivalents.TryGetValue(source, out var equivalents))
        {
            foreach ((long start, long size, T7WalkIndex other, long otherStart) in equivalents)
            {
                if (offset >= start && offset < start + size && TryMapDirect(other, otherStart + (offset - start), out output))
                    return true;
            }
        }
        return false;
    }

    private bool TryMapDirect(T7WalkIndex source, long offset, out long output)
    {
        output = -1;
        if (!_spans.TryGetValue(source, out var list))
            return false;
        int lo = 0, hi = list.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (list[mid].Source <= offset)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        for (int i = found; i >= 0 && i > found - 8; i--)
        {
            (long start, long size, long outStart) = list[i];
            if (offset >= start && offset < start + size)
            {
                output = outStart + (offset - start);
                return true;
            }
        }
        return false;
    }
}
