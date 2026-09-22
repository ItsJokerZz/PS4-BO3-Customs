using FFPorter.Core.T7.Harness;

namespace FFPorter.Core.T7.Link;

public sealed class T7WalkIndex
{
    public readonly record struct Placement(int Block, long BlockOffset, long Size, long FileOffset, int Asset, T7WalkKind Kind);

    public string Label { get; }
    public byte[] Zone { get; }
    public T7Walk Walk { get; }
    public T7ZoneList List { get; }

    private readonly Placement[][] _byBlock = new Placement[T7Header.BlockCount][];
    private readonly long[][] _blockStarts = new long[T7Header.BlockCount][];
    private readonly Placement[] _byFile;
    private readonly long[] _fileStarts;
    private readonly Dictionary<long, (T7PointerKind Kind, T7BlockAddress Target)> _pointers;
    private readonly Dictionary<(int Block, long Offset), int> _aliasOwners = [];
    private readonly Dictionary<int, List<T7BlockAddress>> _aliasesOfAsset = [];

    public T7WalkIndex(string label, byte[] zone, T7Walk walk)
    {
        Label = label;
        Zone = zone;
        Walk = walk;
        List = T7ZoneList.Parse(zone);
        var perBlock = Enumerable.Range(0, T7Header.BlockCount).Select(_ => new List<Placement>()).ToArray();
        var all = new List<Placement>();
        void Add(T7Walk.Span span, int asset)
        {
            var placement = new Placement(span.At.Block, span.At.Offset, span.Size, span.FileOffset, asset, span.Kind);
            all.Add(placement);
            if (span.At.Block >= 0 && span.Size > 0)
                perBlock[span.At.Block].Add(placement);
        }
        foreach (T7Walk.Span span in walk.Preamble)
            Add(span, -1);
        foreach (T7Walk.Asset asset in walk.Assets)
        {
            foreach (T7Walk.Span span in asset.Reads)
                Add(span, asset.Index);
            foreach (T7Walk.Span span in asset.Deferred)
                Add(span, asset.Index);
            foreach ((T7BlockAddress slot, T7BlockAddress _, int _) in asset.AliasSlots)
            {
                _aliasOwners[(slot.Block, slot.Offset)] = asset.Index;
                if (!_aliasesOfAsset.TryGetValue(asset.Index, out List<T7BlockAddress>? slots))
                    _aliasesOfAsset[asset.Index] = slots = [];
                slots.Add(slot);
            }
        }
        for (int b = 0; b < T7Header.BlockCount; b++)
        {
            perBlock[b].Sort((x, y) => x.BlockOffset != y.BlockOffset ? x.BlockOffset.CompareTo(y.BlockOffset) : x.FileOffset.CompareTo(y.FileOffset));
            _byBlock[b] = perBlock[b].ToArray();
            _blockStarts[b] = _byBlock[b].Select(p => p.BlockOffset).ToArray();
        }
        all.RemoveAll(p => p.Size == 0);
        all.Sort((x, y) => x.FileOffset.CompareTo(y.FileOffset));
        _byFile = all.ToArray();
        _fileStarts = _byFile.Select(p => p.FileOffset).ToArray();
        _pointers = new Dictionary<long, (T7PointerKind, T7BlockAddress)>(walk.Pointers.Count);
        foreach (T7Walk.PointerField field in walk.Pointers)
            _pointers[field.FieldFileOffset] = (field.Kind, field.Target);
    }

    public static T7WalkIndex Load(string label, string fastFile, string walkPath)
    {
        T7FastFile.Decoded decoded = T7FastFile.Load(fastFile);
        return new T7WalkIndex(label, decoded.Zone, T7Walk.Read(walkPath));
    }

    public IReadOnlyDictionary<long, (T7PointerKind Kind, T7BlockAddress Target)> Pointers => _pointers;

    private long[]? _sortedPointerFields;

    public IEnumerable<(long Field, T7PointerKind Kind, T7BlockAddress Target)> PointersIn(long start, long size)
    {
        long[] fields = _sortedPointerFields ??= _pointers.Keys.Order().ToArray();
        int i = Array.BinarySearch(fields, start);
        if (i < 0)
            i = ~i;
        for (; i < fields.Length && fields[i] + 8 <= start + size; i++)
        {
            (T7PointerKind kind, T7BlockAddress target) = _pointers[fields[i]];
            yield return (fields[i], kind, target);
        }
    }

    public bool TryFileOffset(T7BlockAddress address, out long fileOffset, long nearFileOffset = long.MaxValue)
    {
        fileOffset = -1;
        if (address.Block < 0 || address.Block >= T7Header.BlockCount)
            return false;
        if (address.Block == 0)
        {
            for (int j = FileIndexBefore(nearFileOffset); j >= 0; j--)
            {
                Placement p = _byFile[j];
                if (p.Block == 0 && p.BlockOffset <= address.Offset && address.Offset < p.BlockOffset + p.Size)
                {
                    fileOffset = p.FileOffset + (address.Offset - p.BlockOffset);
                    return true;
                }
            }
            return false;
        }
        Placement[] placements = _byBlock[address.Block];
        int i = Array.BinarySearch(_blockStarts[address.Block], address.Offset);
        if (i < 0)
            i = ~i - 1;
        else
            while (i + 1 < placements.Length && placements[i + 1].BlockOffset == address.Offset)
                i++;
        if (i < 0)
            return false;
        Placement found = placements[i];
        if (address.Offset >= found.BlockOffset + found.Size)
            return false;
        fileOffset = found.FileOffset + (address.Offset - found.BlockOffset);
        return true;
    }

    public bool TryAliasOwner(T7BlockAddress address, out int asset) => _aliasOwners.TryGetValue((address.Block, address.Offset), out asset);

    public bool TryAliasHeader(T7BlockAddress slot, out T7BlockAddress header)
    {
        if (_aliasOwners.TryGetValue((slot.Block, slot.Offset), out int asset))
        {
            foreach ((T7BlockAddress s, T7BlockAddress h, int _) in Walk.Assets[asset].AliasSlots)
            {
                if (s == slot)
                {
                    header = h;
                    return true;
                }
            }
        }
        header = T7BlockAddress.None;
        return false;
    }

    public bool TryReferencedName(long field, out int type, out string name)
    {
        type = -1;
        name = "";
        if (!_pointers.TryGetValue(field, out var pointer))
            return false;
        T7Walk.Registration registration;
        if (pointer.Kind is T7PointerKind.Inline or T7PointerKind.InlineAlias)
        {
            if (!TryInlineRegistration(field, out registration))
                return false;
        }
        else if (!(pointer.Kind == T7PointerKind.PackedAlias && TryAliasRegistration(pointer.Target, out registration)))
        {
            if (!TryFileOffset(pointer.Target, out long cell, field))
                return false;
            long tableStart = List.AssetTableOffset;
            if (cell >= tableStart && cell < tableStart + 16L * List.Assets.Count)
            {
                T7Walk.Asset asset = Walk.Assets[(int)((cell - tableStart) / 16)];
                if (asset.Registrations.Count == 0)
                    return false;
                registration = asset.Registrations[^1];
            }
            else if (!TryInlineRegistration(cell, out registration))
            {
                return false;
            }
        }
        (type, name) = (registration.Type, registration.Name);
        return !string.IsNullOrEmpty(name);
    }

    public bool TryFollow(long field, out long fileOffset)
    {
        fileOffset = -1;
        return _pointers.TryGetValue(field, out var pointer) && TryFileOffset(pointer.Target, out fileOffset, field);
    }

    public bool TryInlineRegistration(long fieldFileOffset, out T7Walk.Registration registration) =>
        TryInlineRegistration(fieldFileOffset, out _, out registration);

    public bool TryInlineRegistration(long fieldFileOffset, out int asset, out T7Walk.Registration registration)
    {
        if (Walk.InlineRegistrations.TryGetValue(fieldFileOffset, out var at) && at.Asset >= 0 && at.Asset < Walk.Assets.Count
            && at.Registration >= 0 && at.Registration < Walk.Assets[at.Asset].Registrations.Count)
        {
            asset = at.Asset;
            registration = Walk.Assets[at.Asset].Registrations[at.Registration];
            return true;
        }
        asset = -1;
        registration = default;
        return false;
    }

    public bool TryAliasRegistration(T7BlockAddress slot, out T7Walk.Registration registration) => TryAliasRegistration(slot, out _, out registration);

    public bool TryAliasRegistration(T7BlockAddress slot, out int owner, out T7Walk.Registration registration)
    {
        owner = _aliasOwners.GetValueOrDefault((slot.Block, slot.Offset), -1);
        if (owner >= 0)
        {
            T7Walk.Asset asset = Walk.Assets[owner];
            foreach ((T7BlockAddress s, T7BlockAddress _, int index) in asset.AliasSlots)
            {
                if (s == slot && index >= 0 && index < asset.Registrations.Count)
                {
                    registration = asset.Registrations[index];
                    return true;
                }
            }
        }
        registration = default;
        return false;
    }

    private Dictionary<(int Block, long Offset), List<T7Walk.Registration>>? _registrationsByHeader;
    private Dictionary<(int Type, string Name), T7BlockAddress>? _headerCells;

    public bool TryRegistrationAt(T7BlockAddress header, long cellFileOffset, out int type, out string? name)
    {
        _registrationsByHeader ??= BuildRegistrations();
        type = -1;
        name = null;
        if (!_registrationsByHeader.TryGetValue((header.Block, header.Offset), out var list))
            return false;
        int lo = 0, hi = list.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (list[mid].FilePos <= cellFileOffset)
                lo = mid + 1;
            else
                hi = mid;
        }
        T7Walk.Registration found = lo < list.Count ? list[lo] : list[^1];
        if (header.Block != 0 && list.Count > 0)
            found = list[0];
        (type, name) = (found.Type, found.Name);
        return true;
    }

    private Dictionary<(int, long), List<T7Walk.Registration>> BuildRegistrations()
    {
        var map = new Dictionary<(int, long), List<T7Walk.Registration>>();
        foreach (T7Walk.Asset asset in Walk.Assets)
        {
            foreach (T7Walk.Registration registration in asset.Registrations)
            {
                if (!map.TryGetValue((registration.Header.Block, registration.Header.Offset), out var list))
                    map[(registration.Header.Block, registration.Header.Offset)] = list = [];
                list.Add(registration);
            }
        }
        foreach (var list in map.Values)
            list.Sort((a, b) => a.FilePos.CompareTo(b.FilePos));
        return map;
    }

    public bool TryHeaderCell(int type, string name, out T7BlockAddress cell)
    {
        _headerCells ??= BuildHeaderCells();
        return _headerCells.TryGetValue((type, name), out cell);
    }

    private Dictionary<(int, string), T7BlockAddress> BuildHeaderCells()
    {
        _registrationsByHeader ??= BuildRegistrations();
        var cells = new Dictionary<(int, string), T7BlockAddress>();
        foreach (T7Walk.Asset asset in Walk.Assets)
        {
            foreach ((T7BlockAddress slot, T7BlockAddress header, int index) in asset.AliasSlots)
            {
                if (index >= 0 && index < asset.Registrations.Count)
                {
                    T7Walk.Registration known = asset.Registrations[index];
                    if (!string.IsNullOrEmpty(known.Name))
                        cells.TryAdd((known.Type, known.Name), slot);
                    continue;
                }
                if (TryRegistrationAt(header, asset.Start, out int type, out string? name) && name != null)
                    cells.TryAdd((type, name), slot);
            }
        }
        foreach ((long field, (T7PointerKind kind, T7BlockAddress target)) in _pointers.OrderBy(p => p.Key))
        {
            if (kind is not (T7PointerKind.Inline or T7PointerKind.InlineAlias))
                continue;
            int type;
            string? name;
            if (TryInlineRegistration(field, out T7Walk.Registration exact))
                (type, name) = (exact.Type, exact.Name);
            else if (Walk.InlineRegistrations.Count > 0 || !TryRegistrationAt(target, field, out type, out name))
                continue;
            if (string.IsNullOrEmpty(name) || cells.ContainsKey((type, name)))
                continue;
            if (TryBlockAddress(field, out T7BlockAddress cell) && cell.Block != 0)
                cells.TryAdd((type, name), cell);
        }
        for (int i = 0; i < Walk.Assets.Count && i < List.Assets.Count; i++)
        {
            T7Walk.Asset asset = Walk.Assets[i];
            if (asset.Registrations.Count == 0)
                continue;
            T7Walk.Registration root = asset.Registrations[^1];
            if (!cells.ContainsKey((root.Type, root.Name)) && TryBlockAddress(List.AssetTableOffset + 16L * i + 8, out T7BlockAddress cell))
                cells[(root.Type, root.Name)] = cell;
        }
        return cells;
    }

    public IReadOnlyList<T7BlockAddress> AliasSlotsOf(int asset) => _aliasesOfAsset.TryGetValue(asset, out List<T7BlockAddress>? slots) ? slots : [];

    public bool TryBlockAddress(long fileOffset, out T7BlockAddress address)
    {
        address = T7BlockAddress.None;
        int i = Array.BinarySearch(_fileStarts, fileOffset);
        if (i < 0)
            i = ~i - 1;
        if (i < 0)
            return false;
        Placement p = _byFile[i];
        if (fileOffset >= p.FileOffset + p.Size || p.Block < 0)
            return false;
        address = new T7BlockAddress(p.Block, p.BlockOffset + (fileOffset - p.FileOffset));
        return true;
    }

    private int FileIndexBefore(long fileOffset)
    {
        if (fileOffset == long.MaxValue)
            return _byFile.Length - 1;
        int i = Array.BinarySearch(_fileStarts, fileOffset);
        return i < 0 ? ~i - 1 : i - 1;
    }

    public int AssetAt(long fileOffset)
    {
        int i = Array.BinarySearch(_fileStarts, fileOffset);
        if (i < 0)
            i = ~i - 1;
        return i >= 0 && fileOffset < _byFile[i].FileOffset + _byFile[i].Size ? _byFile[i].Asset : -2;
    }
}
