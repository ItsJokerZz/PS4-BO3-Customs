using FFPorter.Core.T7.Harness;
using FFPorter.Core.T7.Link;

namespace FFPorter.Core.T7.Port;

public sealed class T7TextureComboFallout
{
    public sealed record SubAsset(T7Walk.Registration Registration, int FirstRead, int EndRead, bool Dropped)
    {
        public int Type => Registration.Type;
        public string Name => Registration.Name;
    }

    public int AssetIndex { get; }
    public T7Walk.Asset Asset { get; }
    public IReadOnlyList<SubAsset> SubAssets { get; }
    public HashSet<int> DroppedEntries { get; } = [];
    public HashSet<long> NullFields { get; } = [];
    public Dictionary<long, SubAsset> RehomeFields { get; } = [];
    public HashSet<long> InlineCopyFields { get; } = [];
    public Dictionary<string, int> Counts { get; } = [];

    private readonly HashSet<T7Walk.Registration> _claimed = [];

    private T7TextureComboFallout(T7Walk.Asset asset, List<SubAsset> subAssets)
    {
        Asset = asset;
        AssetIndex = asset.Index;
        SubAssets = subAssets;
    }

    public bool Claim(SubAsset subAsset) => _claimed.Add(subAsset.Registration);

    public bool Contains(long fileOffset) => fileOffset >= Asset.Start && fileOffset < Asset.End;

    public static bool IsDropped(T7Walk.Registration registration) =>
        (registration.Type == T7AssetTypes.Material && registration.Name.EndsWith("|dup", StringComparison.Ordinal))
        || (registration.Type == T7AssetTypes.Image && !registration.Name.StartsWith(',') && registration.Name.EndsWith("_combo", StringComparison.Ordinal));

    public static List<T7TextureComboFallout> Compute(T7WalkIndex pc)
    {
        var result = new List<T7TextureComboFallout>();
        foreach (T7Walk.Asset asset in pc.Walk.Assets)
        {
            if (asset.Type == T7AssetTypes.TextureCombo && asset.Reads.Count > 0)
                result.Add(ComputeOne(pc, asset));
        }
        return result;
    }

    private static T7TextureComboFallout ComputeOne(T7WalkIndex pc, T7Walk.Asset tc)
    {
        List<T7Walk.Span> reads = tc.Reads;
        long[] offsets = reads.Select(r => r.FileOffset).ToArray();
        var subAssets = new List<SubAsset>();
        var byRegistration = new Dictionary<T7Walk.Registration, SubAsset>();
        foreach (T7Walk.Registration registration in tc.Registrations)
        {
            if (registration.Type == T7AssetTypes.TextureCombo)
                continue;
            int end = Array.BinarySearch(offsets, registration.FilePos);
            end = end < 0 ? ~end : end;
            int first = -1;
            for (int i = end - 1; i >= 0; i--)
            {
                if (reads[i].Kind == T7WalkKind.Read && reads[i].At == registration.Header)
                {
                    first = i;
                    break;
                }
            }
            if (first < 0)
                continue;
            var subAsset = new SubAsset(registration, first, end, IsDropped(registration));
            subAssets.Add(subAsset);
            byRegistration.TryAdd(registration, subAsset);
        }
        var fallout = new T7TextureComboFallout(tc, subAssets);

        var placements = new Dictionary<int, List<(long Offset, long Size, int Read)>>();
        for (int i = 0; i < reads.Count; i++)
        {
            if (reads[i].At.Block < 0)
                continue;
            if (!placements.TryGetValue(reads[i].At.Block, out var list))
                placements[reads[i].At.Block] = list = [];
            list.Add((reads[i].At.Offset, reads[i].Size, i));
        }
        foreach (var list in placements.Values)
            list.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        bool TryReadAt(T7BlockAddress address, out int read, out long fileOffset)
        {
            read = -1;
            fileOffset = -1;
            if (!placements.TryGetValue(address.Block, out var list))
                return false;
            int lo = 0, hi = list.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (list[mid].Offset <= address.Offset)
                {
                    found = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            if (found < 0 || address.Offset >= list[found].Offset + list[found].Size)
                return false;
            read = list[found].Read;
            fileOffset = reads[read].FileOffset + (address.Offset - list[found].Offset);
            return true;
        }
        SubAsset? Innermost(int read)
        {
            SubAsset? best = null;
            foreach (SubAsset s in subAssets)
            {
                if (s.FirstRead <= read && read < s.EndRead && (best == null || s.EndRead - s.FirstRead < best.EndRead - best.FirstRead))
                    best = s;
            }
            return best;
        }

        long tableStart = pc.List.AssetTableOffset, tableEnd = tableStart + 16L * pc.List.Assets.Count;
        void Count(string what) => fallout.Counts[what] = fallout.Counts.GetValueOrDefault(what) + 1;
        foreach ((long field, (T7PointerKind kind, T7BlockAddress target)) in pc.Pointers)
        {
            if (kind is not (T7PointerKind.Packed or T7PointerKind.PackedAlias) || fallout.Contains(field))
                continue;
            bool fromTable = field >= tableStart && field < tableEnd;
            SubAsset? header = null;
            bool classified = false;
            if (kind == T7PointerKind.PackedAlias && pc.TryAliasRegistration(target, out int owner, out T7Walk.Registration slotRegistration) && owner == tc.Index)
            {
                byRegistration.TryGetValue(slotRegistration, out header);
                classified = header != null;
            }
            if (!classified)
            {
                if (!TryReadAt(target, out int read, out long targetFile))
                    continue;
                if (kind == T7PointerKind.PackedAlias)
                {
                    if (pc.TryInlineRegistration(targetFile, out int inlineOwner, out T7Walk.Registration inline) && inlineOwner == tc.Index
                        && byRegistration.TryGetValue(inline, out header))
                    {
                    }
                    else
                    {
                        SubAsset? owning = Innermost(read);
                        if (owning == null || owning.Dropped)
                        {
                            fallout.NullFields.Add(field);
                            Count("null field");
                        }
                        else
                        {
                            fallout.RehomeFields[field] = owning;
                            Count("re-home");
                        }
                        continue;
                    }
                }
                else
                {
                    SubAsset? owning = Innermost(read);
                    if (owning == null)
                    {
                        if (read is 0 or 1)
                        {
                            Count("kept (stub)");
                        }
                        else
                        {
                            fallout.NullFields.Add(field);
                            Count("null field");
                        }
                    }
                    else
                    {
                        fallout.InlineCopyFields.Add(field);
                        Count("inline copy");
                    }
                    continue;
                }
            }
            if (header!.Dropped)
            {
                if (fromTable)
                {
                    fallout.DroppedEntries.Add((int)((field - tableStart) / 16));
                    Count("dropped entry");
                }
                else
                {
                    fallout.NullFields.Add(field);
                    Count("null field");
                }
            }
            else
            {
                fallout.RehomeFields[field] = header;
                Count("re-home");
            }
        }
        return fallout;
    }
}
