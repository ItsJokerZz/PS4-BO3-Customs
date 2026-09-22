using FFPorter.Core.T7.Harness;
using FFPorter.Core.T7.Link;

namespace FFPorter.Core.T7.Port;

public sealed class T7PortContext
{
    public required T7WalkIndex Pc { get; init; }
    public required T7ZoneBuilder Builder { get; init; }
    public required T7DonorLibrary Donors { get; init; }
    public required Action<string> Log { get; init; }

    public int[] OutputIndexOfPc { get; init; } = [];

    public Streams.T7StreamMap Streams { get; } = new();

    public IReadOnlyDictionary<string, string> SoundRenames { get; init; } = new Dictionary<string, string>();

    public T7PcShaderLibrary? ShaderLibrary { get; set; }

    public Shaders.T7ShaderCompiler? ShaderCompiler { get; set; }

    private Dictionary<string, long>? _imageStructs;

    public byte[]? ImageStructByName(string name)
    {
        if (_imageStructs == null)
        {
            _imageStructs = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (T7Walk.Asset asset in Pc.Walk.Assets)
            {
                foreach (T7Walk.Registration registration in asset.Registrations)
                {
                    if (registration.Type != T7AssetTypes.Image || _imageStructs.ContainsKey(registration.Name))
                        continue;
                    for (int i = asset.Reads.Count - 1; i >= 0; i--)
                    {
                        T7Walk.Span read = asset.Reads[i];
                        if (read.FileOffset >= registration.FilePos)
                            continue;
                        if (read.Kind == T7WalkKind.Read && read.Size == 264 && read.At == registration.Header)
                        {
                            _imageStructs[registration.Name] = read.FileOffset;
                            break;
                        }
                    }
                }
            }
        }
        return _imageStructs.TryGetValue(name, out long at) ? Pc.Zone.AsSpan((int)at, 264).ToArray() : null;
    }

    public Scripts.T7Gsc? Gsc { get; set; }

    public Scripts.T7GscBuiltins? GscBuiltins { get; set; }

    public string? Acts { get; set; }

    public bool GscRecompile { get; set; }

    public Dictionary<string, int> StringPools { get; } = new(StringComparer.Ordinal);

    public List<T7TextureComboFallout> Fallouts { get; } = [];

    public T7TextureComboFallout? FalloutOf(long field) => Fallouts.Find(f => f.RehomeFields.ContainsKey(field) || f.InlineCopyFields.Contains(field) || f.NullFields.Contains(field));

    public IReadOnlyList<IT7AssetConverter> Converters { get; set; } = [];

    public IT7NestedConverter? NestedConverter(int type) => Converters.OfType<IT7NestedConverter>().FirstOrDefault(c => c.CanConvertNested(type));

    public List<string> Warnings { get; } = [];

    public T7Fidelity? Fidelity { get; set; }

    public void Warn(T7Issue issue, string warning, string? sample = null)
    {
        Warnings.Add(warning);
        Fidelity?.Raise(issue, sample);
    }

    public ulong Ps4StreamKey(ulong pcKey, out Streams.T7StreamMap.Item? item, Common.Fidelity.FidelityDimension part = Common.Fidelity.FidelityDimension.Textures)
    {
        if (Streams.TryGet(pcKey, out item))
            return item.Ps4Key;
        if (_unknownKeys.Add(pcKey))
            Warn(T7Issues.StreamKeyUnknown(part), $"stream key {pcKey:x16} is in no converted xpak or PS4 index; the PC key is kept");
        item = null;
        return pcKey;
    }

    private readonly HashSet<ulong> _unknownKeys = [];
}

public interface IT7AssetConverter
{
    string Name { get; }

    bool TryConvert(T7PortContext context, T7Walk.Asset asset, T7OutputAsset output, out string? reason);
}

public interface IT7StreamPlanner
{
    void PlanStreams(T7PortContext context, T7Walk.Asset asset, Streams.T7StreamPlan plan);
}

public interface IT7NestedConverter
{
    bool CanConvertNested(int type);

    bool TryConvertNested(T7PortContext context, T7AssetRewrite rewrite, int type, int endRead, out string? reason);
}

public sealed class T7CopyConverter : IT7AssetConverter
{
    public static readonly HashSet<int> LoaderCheckedTypes =
    [
        T7AssetTypes.StreamerHint, T7AssetTypes.Bitfield, T7AssetTypes.CustomizationTableColor, T7AssetTypes.MapTable, T7AssetTypes.LeaderboardDef,
        T7AssetTypes.MedalTable, T7AssetTypes.Medal, T7AssetTypes.ScriptBundleList, T7AssetTypes.VehicleFxDef, T7AssetTypes.CgMediaTable,
        T7AssetTypes.ObjectiveList, T7AssetTypes.LocDmgTable, T7AssetTypes.MapTableLoadingImages, T7AssetTypes.Ddl, T7AssetTypes.FlameTable,
        T7AssetTypes.BehaviorStateMachine, T7AssetTypes.BulletPenetration, T7AssetTypes.CustomizationTableFeImages, T7AssetTypes.LightDef,
        T7AssetTypes.StructuredTable, T7AssetTypes.FontIcon, T7AssetTypes.SndDriverGlobals, T7AssetTypes.SoundPatch,
    ];

    public static readonly HashSet<int> IdenticalTypes =
    [
        T7AssetTypes.PhysPreset, T7AssetTypes.PhysConstraints, T7AssetTypes.DestructibleDef, T7AssetTypes.XAnimParts,
        T7AssetTypes.ClipMap, T7AssetTypes.ComWorld, T7AssetTypes.GameWorld, T7AssetTypes.MapEnts, T7AssetTypes.Glasses,
        T7AssetTypes.ImpactSound, T7AssetTypes.AiType, T7AssetTypes.Character, T7AssetTypes.RawFile, T7AssetTypes.StringTable,
        T7AssetTypes.KeyValuePairs, T7AssetTypes.SurfaceFxTable, T7AssetTypes.SurfaceSoundDef, T7AssetTypes.FootstepTable,
        T7AssetTypes.EntitySoundImpacts, T7AssetTypes.ScriptBundle, T7AssetTypes.AnimMappingTable, T7AssetTypes.XCam,
        T7AssetTypes.BgCache, T7AssetTypes.AttachmentCosmeticVariant, T7AssetTypes.SharedWeaponSounds,
        T7AssetTypes.ZBarrier, T7AssetTypes.XModelAlias, T7AssetTypes.AimTable,
        T7AssetTypes.Attachment, T7AssetTypes.AttachmentUnique, T7AssetTypes.WeaponCamo, T7AssetTypes.Rumble, T7AssetTypes.Tracer,
        T7AssetTypes.ImpactFx, T7AssetTypes.EntityFxImpacts, T7AssetTypes.TagFx, T7AssetTypes.Laser, T7AssetTypes.Shellshock,
        T7AssetTypes.Beam, T7AssetTypes.Objective, T7AssetTypes.Ttf,
        T7AssetTypes.Localize, T7AssetTypes.Vehicle,
        T7AssetTypes.VehicleSoundDef, T7AssetTypes.PlayerSoundsTable, T7AssetTypes.PlayerFxTable, T7AssetTypes.CustomizationTable,
        .. LoaderCheckedTypes,
    ];


    public string Name => "copy";

    public bool TryConvert(T7PortContext context, T7Walk.Asset asset, T7OutputAsset output, out string? reason)
    {
        reason = null;
        if (!IdenticalTypes.Contains(asset.Type) || HasConvertedSubAssets(asset))
            return false;
        CopyWhole(context.Pc, asset, output);
        return true;
    }

    public static bool HasConvertedSubAssets(T7Walk.Asset asset)
    {
        for (int r = 0; r < asset.Registrations.Count - 1; r++)
        {
            if (!IdenticalTypes.Contains(asset.Registrations[r].Type))
                return true;
        }
        return false;
    }

    public static List<(int First, int End, T7Walk.Registration Registration, int Index)> NestedSpans(T7Walk.Asset asset) =>
        Outermost(AllSpans(asset), 0, asset.Reads.Count, -1);

    public static List<(int First, int End, T7Walk.Registration Registration, int Index)> AllSpans(T7Walk.Asset asset)
    {
        var spans = new List<(int First, int End, T7Walk.Registration Registration, int Index)>();
        for (int r = 0; r < asset.Registrations.Count - 1; r++)
        {
            T7Walk.Registration registration = asset.Registrations[r];
            int end = asset.Reads.FindIndex(read => read.FileOffset >= registration.FilePos);
            if (end < 0)
                end = asset.Reads.Count;
            int first = -1;
            for (int i = end - 1; i >= 0; i--)
            {
                if (asset.Reads[i].Kind == T7WalkKind.Read && asset.Reads[i].At == registration.Header)
                {
                    first = i;
                    break;
                }
            }
            if (first >= 0)
                spans.Add((first, end, registration, r));
        }
        spans.Sort((a, b) => a.First != b.First ? a.First.CompareTo(b.First) : b.End.CompareTo(a.End));
        return spans;
    }

    public static List<(int First, int End, T7Walk.Registration Registration, int Index)> Outermost(
        List<(int First, int End, T7Walk.Registration Registration, int Index)> spans, int first, int end, int except)
    {
        var outer = new List<(int First, int End, T7Walk.Registration Registration, int Index)>();
        foreach (var span in spans)
        {
            if (span.Index == except || span.First < first || span.End > end)
                continue;
            if (outer.Count > 0 && span.First < outer[^1].End)
                continue;
            outer.Add(span);
        }
        return outer;
    }

    public static List<T7Walk.Span> OwnReads(T7Walk.Asset asset)
    {
        var converted = AllSpans(asset).Where(s => !IdenticalTypes.Contains(s.Registration.Type)).ToList();
        var reads = new List<T7Walk.Span>(asset.Reads.Count);
        for (int i = 0; i < asset.Reads.Count; i++)
        {
            if (!converted.Any(s => i >= s.First && i < s.End))
                reads.Add(asset.Reads[i]);
        }
        return reads;
    }

    public static void CopyWhole(T7WalkIndex source, T7Walk.Asset asset, T7OutputAsset output)
    {
        if (asset.End > asset.Start)
            output.Main.Add(new T7CopyPiece(source, asset.Start, asset.End - asset.Start));
        foreach (T7Walk.Span span in asset.Deferred)
            output.Deferred.Add(new T7CopyPiece(source, span.FileOffset, span.Size));
    }
}

public sealed class T7DonorConverter : IT7AssetConverter
{
    private readonly HashSet<int> _types;

    public T7DonorConverter(IEnumerable<int> types) => _types = [.. types];

    public string Name => "donor";

    public bool TryConvert(T7PortContext context, T7Walk.Asset asset, T7OutputAsset output, out string? reason)
    {
        reason = null;
        if (!_types.Contains(asset.Type))
            return false;
        string? name = asset.Name;
        if (string.IsNullOrEmpty(name))
        {
            reason = "asset has no name";
            return false;
        }
        if (!context.Donors.TryFind(asset.Type, T7Names.ToPs4(asset.Type, name), out T7WalkIndex? donor, out T7Walk.Asset? donorAsset))
        {
            reason = $"no PS4 donor {T7AssetTypes.Name(asset.Type)} '{T7Names.ToPs4(asset.Type, name)}'";
            return false;
        }
        T7CopyConverter.CopyWhole(donor!, donorAsset!, output);
        output.Origin = $"donor {donor!.Label}#{donorAsset!.Index}";
        context.Builder.MapAsset(donor, donorAsset.Index, context.OutputIndexOfPc[asset.Index]);
        AlignReads(context.Builder, context.Pc, asset, donor, donorAsset);
        foreach (T7TextureComboFallout fallout in context.Fallouts)
        {
            foreach ((long field, T7TextureComboFallout.SubAsset subAsset) in fallout.RehomeFields)
            {
                if (field >= asset.Start && field < asset.End)
                    fallout.Claim(subAsset);
            }
        }
        return true;
    }

    public static void AlignReads(T7ZoneBuilder builder, T7WalkIndex from, T7Walk.Asset fromAsset, T7WalkIndex to, T7Walk.Asset toAsset)
    {
        List<T7Walk.Span> a = fromAsset.Reads, b = toAsset.Reads;
        if (a.Count == 0 || b.Count == 0 || (long)a.Count * b.Count > 25_000_000)
            return;
        var lengths = new int[a.Count + 1, b.Count + 1];
        for (int i = a.Count - 1; i >= 0; i--)
            for (int j = b.Count - 1; j >= 0; j--)
                lengths[i, j] = a[i].Kind == b[j].Kind && a[i].Size == b[j].Size ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        for (int i = 0, j = 0; i < a.Count && j < b.Count;)
        {
            if (a[i].Kind == b[j].Kind && a[i].Size == b[j].Size)
            {
                builder.MapEquivalent(from, a[i].FileOffset, a[i].Size, to, b[j].FileOffset);
                i++;
                j++;
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }
    }
}

public static class T7Names
{
    public const uint TechsetPlatformDelta = 0x01473F41;

    public static string ToPs4(int type, string name)
    {
        if (type is T7AssetTypes.TechniqueSet or T7AssetTypes.ComputeShaderSet && TrySplitHash(name, out string stem, out uint hash))
            return $"{stem}#{unchecked(hash - TechsetPlatformDelta):x8}";
        return name;
    }

    private static bool TrySplitHash(string name, out string stem, out uint hash)
    {
        int at = name.LastIndexOf('#');
        stem = at > 0 ? name[..at] : name;
        hash = 0;
        return at > 0 && name.Length - at == 9 && uint.TryParse(name.AsSpan(at + 1), System.Globalization.NumberStyles.HexNumber, null, out hash);
    }
}
