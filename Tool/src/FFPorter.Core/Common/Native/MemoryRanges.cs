namespace FFPorter.Core.Common.Native;

public readonly record struct MemoryRange(ulong Start, ulong End)
{
    public ulong Size => End - Start;

    public bool Contains(ulong address) => address >= Start && address < End;

    public override string ToString() => $"{NativeHost.Hex(Start)}..{NativeHost.Hex(End)}";
}

internal sealed class MemoryRanges
{
    private readonly List<MemoryRange> _ranges = [];
    private int _last = -1;

    public IReadOnlyList<MemoryRange> All => _ranges;

    public void Add(MemoryRange range)
    {
        int lo = 0, hi = _ranges.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            MemoryRange at = _ranges[mid];
            if (at.Start < range.Start || (at.Start == range.Start && at.End <= range.End))
                lo = mid + 1;
            else
                hi = mid;
        }
        _ranges.Insert(lo, range);
        _last = -1;
    }

    public bool TryFind(ulong address, out MemoryRange range)
    {
        int last = _last;
        if ((uint)last < (uint)_ranges.Count && _ranges[last].Contains(address))
        {
            range = _ranges[last];
            return true;
        }
        int lo = 0, hi = _ranges.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (_ranges[mid].Start <= address)
                lo = mid + 1;
            else
                hi = mid;
        }
        int index = lo - 1;
        if (index >= 0 && address < _ranges[index].End)
        {
            _last = index;
            range = _ranges[index];
            return true;
        }
        range = default;
        return false;
    }

    public bool Overlaps(ulong start, ulong end)
    {
        foreach (MemoryRange range in _ranges)
        {
            if (range.Start < end && start < range.End)
                return true;
        }
        return false;
    }
}
