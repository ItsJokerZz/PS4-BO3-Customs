using System.Globalization;
using FFPorter.Core.Common.Fidelity;
using FFPorter.Core.T7.Harness;
using FFPorter.Core.T7.Streams;
using static FFPorter.Core.Common.Fidelity.FidelityDimension;
using static FFPorter.Core.Common.Fidelity.FidelityGrade;
using D = FFPorter.Core.Common.Fidelity.FidelityDimension;

namespace FFPorter.Core.T7.Port;

public sealed record T7Issue(string Key, FidelityDimension Dimension, FidelityGrade? Grade, Func<long, string?, string> Text);

public static class T7Issues
{
    private static string N(long count, string singular, string plural) => FidelityTracker.Count(count, singular, plural);
    private static string Were(long count) => count == 1 ? "was" : "were";
    private static string Use(long count, string singular, string plural) => count == 1 ? singular : plural;
    private static string Example(string? sample) => string.IsNullOrEmpty(sample) ? "" : $" (e.g. {sample})";

    public static readonly T7Issue ConstantBufferDiffers = new("material.constants-differ", D.Materials, Approximate,
        (n, s) => $"{N(n, "material", "materials")} packed constant buffers that differ from the PS4 reference zone's{Example(s)}; their shaders read the wrong values");

    public static readonly T7Issue ConstantBufferFromReference = new("material.cb-reference", Materials, Good,
        (n, s) => $"{N(n, "material", "materials")} took constant buffers from the PS4 reference zones, because no PC shader describes {Use(n, "it", "them")}{Example(s)}");
    public static readonly T7Issue TechniqueSetCompiled = new("techset.compiled", Materials, Strong,
        (n, _) => $"{N(n, "technique set", "technique sets")} {Were(n)} compiled for PS4 from {Use(n, "its", "their")} PC shaders (DXBC to PSSL)");
    public static readonly T7Issue TechniqueSetDonor = new("techset.donor", Materials, Strong,
        (n, _) => $"{N(n, "technique set", "technique sets")} came from the PS4 reference zones");
    public static readonly T7Issue VertexUsageUnknown = new("techset.vertex-usage", Materials, Approximate,
        (n, s) => $"{N(n, "technique set", "technique sets")} {Use(n, "uses", "use")} vertex streams with no known PS4 meaning, kept in PC order{Example(s)}");
    public static readonly T7Issue DupMaterialsLeftOut = new("material.dup", Materials, null,
        (n, _) => $"{N(n, "PC-only \"|dup\" material copy", "PC-only \"|dup\" material copies")} left out, as the PS4 build does");

    public static readonly T7Issue TextureComboLeftOut = new("texturecombo.dropped", Textures, null,
        (n, _) => $"{N(n, "PC-only texture combo entry", "PC-only texture combo entries")} left out, as the PS4 build does");
    public static readonly T7Issue ImageTailDropped = new("image.tail", Textures, Strong,
        (n, _) => $"{N(n, "streamed image part", "streamed image parts")} carried extra PC data after {Use(n, "its", "their")} mips, which was dropped");
    public static readonly T7Issue ImageSharesPixels = new("image.shared", Textures, null,
        (n, _) => $"{N(n, "image shares", "images share")} pixel data with another image");
    public static readonly T7Issue ImagePartSize = new("image.part-size", Textures, Approximate,
        (n, s) => $"{N(n, "image has a streamed part", "images have streamed parts")} whose size differs from the PS4 layout{Example(s)}");
    public static readonly T7Issue SAnimLayoutKept = new("sanim.layout", Textures, Approximate,
        (n, s) => $"{N(n, "animated texture is", "animated textures are")} laid out in a way this port has no PS4 rule for; {Use(n, "its", "their")} PC texels were kept{Example(s)}");

    public static readonly T7Issue MeshesNotPlanned = new("xmodel.unplanned", Geometry, Approximate,
        (n, s) => $"the streamed meshes of {N(n, "model", "models")} could not be planned and keep the PC vertex format{Example(s)}");
    public static readonly T7Issue MeshSizeMismatch = new("xmodel.mesh-size", Geometry, Approximate,
        (n, _) => $"{N(n, "streamed mesh", "streamed meshes")} did not match {Use(n, "its", "their")} surface description and {Were(n)} copied unchanged");
    public static readonly T7Issue TexelDensityScale = new("xmodel.texel-density", Geometry, null,
        (_, _) => "LOD texel densities use a fixed PS4 scale (the console linker's per-material factor is not stored in PC zones)");

    public static readonly T7Issue WorldNotPlanned = new("gfxworld.unplanned", World, Approximate,
        (_, s) => $"the world's streamed items could not be planned and keep their PC form{Example(s)}");
    public static readonly T7Issue WrappedItemSize = new("gfxworld.wrapped", World, Good,
        (n, _) => $"{N(n, "wrapped world item has", "wrapped world items have")} no image dimensions; the PC logical sizes are kept");
    public static readonly T7Issue WorldTexelDensity = new("gfxworld.texel-density", World, null,
        (_, _) => "surface texel densities use the same fixed PS4 scale as models");
    public static readonly T7Issue MovieEncoded = new("movie.encoded", World, Strong,
        (n, s) => $"{N(n, "movie", "movies")} re-encoded as 1920x1080 H.264 for the PS4 decoder{Example(s)}");
    public static readonly T7Issue MovieCopied = new("movie.copied", World, null,
        (n, _) => $"{N(n, "movie was", "movies were")} already PS4-ready and copied as {Use(n, "it is", "they are")}");
    public static readonly T7Issue MovieFailed = new("movie.failed", World, Missing,
        (n, s) => $"{N(n, "movie", "movies")} could not be converted{Example(s)}");

    public static readonly T7Issue SoundReencoded = new("sound.reencoded", D.Sound, Strong,
        (n, _) => $"{N(n, "sound", "sounds")} re-encoded from FLAC to MP3, the only format the PS4 plays, at the highest quality it decodes");
    public static readonly T7Issue SoundResampled = new("sound.resampled", D.Sound, Strong,
        (n, _) => $"{N(n, "sound", "sounds")} resampled to 48 kHz");
    public static readonly T7Issue LoopRetimed = new("sound.loop", D.Sound, Strong,
        (n, _) => $"{N(n, "loop", "loops")} stretched to whole MP3 frames so {Use(n, "it repeats", "they repeat")} seamlessly");
    public static readonly T7Issue AliasNamesByRule = new("sound.external-names", D.Sound, Good,
        (n, _) => $"{N(n, "alias plays", "aliases play")} files from the base game's banks, renamed by the PS4 naming rule");
    public static readonly T7Issue AliasIdMismatch = new("sound.id-mismatch", D.Sound, Approximate,
        (n, _) => $"{N(n, "alias file name has", "alias file names have")} no matching asset id and {Were(n)} left unchanged");
    public static readonly T7Issue SoundBankFailed = new("sound.bank-failed", D.Sound, Missing,
        (n, s) => $"{N(n, "sound bank", "sound banks")} could not be converted{Example(s)}");
    public static readonly T7Issue SoundOff = new("sound.off", D.Sound, Missing,
        (n, _) => $"sound bank conversion is off, so {N(n, "bank", "banks")} of the map's own sounds will not play");

    public static readonly T7Issue ScriptUnchecked = new("script.unchecked", D.Scripts, Strong,
        (n, _) => $"{N(n, "script was", "scripts were")} remapped to PS4 opcodes but not checked, because acts was not found");
    public static readonly T7Issue ScriptChecked = new("script.checked", D.Scripts, null,
        (n, _) => $"{N(n, "script was", "scripts were")} remapped to PS4 opcodes and checked by decompiling with acts");
    public static readonly T7Issue ScriptRecompiled = new("script.recompiled", D.Scripts, Strong,
        (n, _) => $"{N(n, "script was", "scripts were")} decompiled and recompiled with acts");
    public static readonly T7Issue BuiltinRewritten = new("script.builtin", D.Scripts, Good,
        (n, s) => $"{N(n, "script calls", "scripts call")} PC-only builtins, now pointed at PS4 stand-ins{Example(s)}");
    public static readonly T7Issue BuiltinsUnchecked = new("script.builtins-missing", D.Scripts, null,
        (_, _) => "the PS4 builtin table is missing, so calls to PC-only builtins were not checked");

    private static readonly Dictionary<(string Key, FidelityDimension Dimension), T7Issue> Made = [];

    private static T7Issue Make(string key, FidelityDimension dimension, FidelityGrade? grade, Func<long, string?, string> text)
    {
        lock (Made)
        {
            if (!Made.TryGetValue((key, dimension), out T7Issue? issue))
                Made[(key, dimension)] = issue = new T7Issue(key, dimension, grade, text);
            return issue;
        }
    }

    public static T7Issue Failed(FidelityDimension dimension) => Make("asset.failed", dimension, Missing,
        (n, s) => $"{N(n, "asset", "assets")} could not be converted{Example(s)}");

    public static T7Issue Donor(FidelityDimension dimension) => Make("asset.donor", dimension, Good,
        (n, _) => $"{N(n, "asset was", "assets were")} replaced by same-named retail PS4 {Use(n, "asset", "assets")}");

    public static T7Issue LoaderChecked(FidelityDimension dimension) => Make("asset.loader-checked", dimension, Strong,
        (n, s) => $"{N(n, "asset", "assets")} of types no PS4 sample was compared for{Example(s)} {Were(n)} copied; the PS4 loader reads {Use(n, "it", "each")} exactly as the PC loader does");

    public static T7Issue StreamKeyUnknown(FidelityDimension dimension) => Make("stream.unknown-key", dimension, Weak,
        (n, _) => $"{N(n, "streamed item the zone uses is", "streamed items the zone uses are")} in no converted xpak or PS4 index; the PC {Use(n, "key is", "keys are")} kept, so {Use(n, "it", "they")} may not load");

    internal static T7Issue StreamUnresolved(FidelityDimension dimension, string singular, string plural) => Make($"stream.unresolved.{plural}", dimension, Weak,
        (n, _) => $"{N(n, singular, plural)} the map uses {Use(n, "is", "are")} stored in xpaks that were not converted and no PS4 index knows {Use(n, "it", "them")}; "
            + (dimension == Textures ? $"{Use(n, "it keeps its", "they keep their")} PC keys, so {Use(n, "it", "they")} can show blurry or as checkerboards" : $"{Use(n, "it keeps its", "they keep their")} PC keys, so {Use(n, "it", "they")} may not load"));

    internal static T7Issue StreamUndescribed(FidelityDimension dimension, string singular, string plural) => Make($"stream.undescribed.{plural}", dimension, Good,
        (n, _) => $"{N(n, singular, plural)} stored in the map's xpaks {Use(n, "is", "are")} described by no asset it loads; {Use(n, "it was", "they were")} copied unchanged");

    internal static T7Issue StreamUnchanged(FidelityDimension dimension, string singular, string plural) => Make($"stream.unchanged.{plural}", dimension, Approximate,
        (n, _) => $"{N(n, singular, plural)} {Were(n)} copied unchanged; there is no PC to PS4 rule for {Use(n, "it", "them")} yet");

    internal static T7Issue StreamConvertFailed(FidelityDimension dimension, string singular, string plural) => Make($"stream.failed.{plural}", dimension, Approximate,
        (n, _) => $"{N(n, singular, plural)} did not fit {Use(n, "its", "their")} PS4 rule and {Were(n)} copied unchanged; the map converts, but {Use(n, "it", "they")} can look wrong");

    internal static T7Issue StreamKeyNotHash(FidelityDimension dimension) => Make("stream.key-not-hash", dimension, Approximate,
        (n, _) => $"{N(n, "streamed item has a key", "streamed items have keys")} that {Use(n, "is not a payload hash", "are not payload hashes")}; the PC {Use(n, "key is", "keys are")} kept");

    internal static T7Issue StreamBundled(FidelityDimension dimension, string singular, string plural) => Make($"stream.bundled.{plural}", dimension, null,
        (n, _) => $"{N(n, singular, plural)} the map borrows from the base game {Were(n)} converted into its xpaks");
}

public sealed class T7Fidelity
{
    private readonly List<(string Key, double Weight)> _phases = [];
    private readonly Dictionary<string, int> _phaseIndex = new(StringComparer.OrdinalIgnoreCase);
    private double[] _before = [0];
    private int _phase = -1;
    private T7Walk.Asset? _asset;
    private readonly Dictionary<string, (T7Issue Issue, string? Sample, long Count)> _assetIssues = new(StringComparer.Ordinal);
    private readonly Dictionary<(FidelityDimension Dimension, FidelityGrade Grade), long> _pendingStreamGrades = [];

    public T7Fidelity(string map, Action<FidelitySnapshot>? emit) => Tracker = new FidelityTracker("t7", map, emit);

    public FidelityTracker Tracker { get; }

    public int ZonesNotWritten { get; private set; }

    public void ZoneNotWritten() => ZonesNotWritten++;


    public void Plan(IEnumerable<string> soundBanks, IEnumerable<(string Zone, string? XPak)> zones, IEnumerable<string> movies)
    {
        static double Megabytes(string? path) => path != null && File.Exists(path) ? new FileInfo(path).Length / 1048576.0 : 0;
        _phases.Clear();
        _phaseIndex.Clear();
        void Add(string key, double weight)
        {
            _phaseIndex[key] = _phases.Count;
            _phases.Add((key, weight));
        }
        foreach (string bank in soundBanks)
            Add("bank:" + Path.GetFileName(bank) + ":" + Path.GetFileName(Path.GetDirectoryName(bank)), 1 + Megabytes(bank) * 0.3);
        foreach ((string zone, string? xpak) in zones)
        {
            string name = Path.GetFileName(zone);
            double ff = Megabytes(zone);
            Add($"zone:{name}:walk", 1 + ff * 0.03);
            Add($"zone:{name}:streams", xpak != null ? 1 + Megabytes(xpak) * 0.05 : 0);
            Add($"zone:{name}:assets", 1 + ff * 0.25);
            Add($"zone:{name}:link", 1 + ff * 0.2);
        }
        foreach (string movie in movies)
            Add("movie:" + Path.GetFileName(movie), 10 + Megabytes(movie));
        _before = new double[_phases.Count + 1];
        for (int i = 0; i < _phases.Count; i++)
            _before[i + 1] = _before[i] + _phases[i].Weight;
    }

    public static string BankPhase(string bank) => "bank:" + Path.GetFileName(bank) + ":" + Path.GetFileName(Path.GetDirectoryName(bank));

    public static string ZonePhase(string zone, string step) => $"zone:{Path.GetFileName(zone)}:{step}";

    public void Enter(string phase, string stage, string detail = "")
    {
        if (_phaseIndex.TryGetValue(phase, out int index))
        {
            _phase = index;
            Tracker.Progress(Fraction(0));
        }
        Tracker.Stage(stage, detail);
    }

    public void Step(double fraction, string? detail = null)
    {
        if (detail != null)
            Tracker.Detail(detail);
        Tracker.Progress(Fraction(Math.Clamp(fraction, 0, 1)));
    }

    private double Fraction(double withinPhase)
    {
        double total = _before[^1];
        if (_phase < 0 || total <= 0)
            return 0;
        return (_before[_phase] + _phases[_phase].Weight * withinPhase) / total;
    }


    public static FidelityDimension Dimension(int type) => type switch
    {
        T7AssetTypes.XModel or T7AssetTypes.XModelMesh or T7AssetTypes.XModelAlias => Geometry,
        T7AssetTypes.Image or T7AssetTypes.TextureCombo or T7AssetTypes.TextureList or T7AssetTypes.SAnim or T7AssetTypes.MapTableLoadingImages
            or T7AssetTypes.CustomizationTableFeImages => Textures,
        T7AssetTypes.Material or T7AssetTypes.TechniqueSet or T7AssetTypes.ComputeShaderSet => Materials,
        T7AssetTypes.ComWorld or T7AssetTypes.LightDef or T7AssetTypes.LightDescription => Lighting,
        T7AssetTypes.ClipMap or T7AssetTypes.PhysPreset or T7AssetTypes.PhysConstraints or T7AssetTypes.Glasses or T7AssetTypes.NavMesh
            or T7AssetTypes.NavVolume => Collision,
        T7AssetTypes.Fx or T7AssetTypes.TagFx or T7AssetTypes.NewLensFlareDef or T7AssetTypes.LensFlareDef or T7AssetTypes.ImpactFx
            or T7AssetTypes.EntityFxImpacts or T7AssetTypes.SurfaceFxTable or T7AssetTypes.Tracer or T7AssetTypes.Laser or T7AssetTypes.Beam
            or T7AssetTypes.Shellshock or T7AssetTypes.VehicleFxDef or T7AssetTypes.PlayerFxTable or T7AssetTypes.FlameTable => Fx,
        T7AssetTypes.Sound or T7AssetTypes.SoundPatch or T7AssetTypes.SndDriverGlobals or T7AssetTypes.ImpactSound or T7AssetTypes.SurfaceSoundDef
            or T7AssetTypes.FootstepTable or T7AssetTypes.EntitySoundImpacts or T7AssetTypes.SharedWeaponSounds or T7AssetTypes.VehicleSoundDef
            or T7AssetTypes.PlayerSoundsTable => D.Sound,
        T7AssetTypes.ScriptParseTree or T7AssetTypes.RawFile or T7AssetTypes.StringTable or T7AssetTypes.StructuredTable or T7AssetTypes.KeyValuePairs
            or T7AssetTypes.Localize or T7AssetTypes.Ddl or T7AssetTypes.TypeInfo or T7AssetTypes.ScriptBundle or T7AssetTypes.ScriptBundleList
            or T7AssetTypes.Bitfield or T7AssetTypes.UiMap or T7AssetTypes.Font or T7AssetTypes.FontIcon or T7AssetTypes.Ttf or T7AssetTypes.BinaryHtml
            or T7AssetTypes.CgMediaTable or T7AssetTypes.LeaderboardDef or T7AssetTypes.Slug or T7AssetTypes.MapTable or T7AssetTypes.Medal
            or T7AssetTypes.MedalTable or T7AssetTypes.BgCache => D.Scripts,
        T7AssetTypes.GfxWorld or T7AssetTypes.GameWorld or T7AssetTypes.UmbraTome or T7AssetTypes.StreamerHint => World,
        _ => Entities,
    };

    private static readonly Dictionary<int, (string Singular, string Plural)> Labels = new()
    {
        [T7AssetTypes.PhysPreset] = ("physics preset", "physics presets"),
        [T7AssetTypes.PhysConstraints] = ("physics constraint set", "physics constraint sets"),
        [T7AssetTypes.DestructibleDef] = ("destructible", "destructibles"),
        [T7AssetTypes.XAnimParts] = ("animation", "animations"),
        [T7AssetTypes.XModel] = ("model", "models"),
        [T7AssetTypes.XModelMesh] = ("mesh", "meshes"),
        [T7AssetTypes.Material] = ("material", "materials"),
        [T7AssetTypes.ComputeShaderSet] = ("compute shader set", "compute shader sets"),
        [T7AssetTypes.TechniqueSet] = ("technique set", "technique sets"),
        [T7AssetTypes.Image] = ("image", "images"),
        [T7AssetTypes.Sound] = ("alias bank", "alias banks"),
        [T7AssetTypes.ClipMap] = ("collision map", "collision maps"),
        [T7AssetTypes.ComWorld] = ("primary light list", "primary light lists"),
        [T7AssetTypes.GameWorld] = ("game world", "game worlds"),
        [T7AssetTypes.MapEnts] = ("entity list", "entity lists"),
        [T7AssetTypes.GfxWorld] = ("world", "worlds"),
        [T7AssetTypes.LightDef] = ("light definition", "light definitions"),
        [T7AssetTypes.Localize] = ("localized string", "localized strings"),
        [T7AssetTypes.Weapon] = ("weapon", "weapons"),
        [T7AssetTypes.Fx] = ("effect", "effects"),
        [T7AssetTypes.TagFx] = ("tag effect", "tag effects"),
        [T7AssetTypes.NewLensFlareDef] = ("lens flare", "lens flares"),
        [T7AssetTypes.ImpactFx] = ("impact effect table", "impact effect tables"),
        [T7AssetTypes.ImpactSound] = ("impact sound table", "impact sound tables"),
        [T7AssetTypes.AiType] = ("AI type", "AI types"),
        [T7AssetTypes.Character] = ("character", "characters"),
        [T7AssetTypes.XModelAlias] = ("model alias", "model aliases"),
        [T7AssetTypes.RawFile] = ("raw file", "raw files"),
        [T7AssetTypes.StringTable] = ("string table", "string tables"),
        [T7AssetTypes.ScriptParseTree] = ("script", "scripts"),
        [T7AssetTypes.KeyValuePairs] = ("key/value list", "key/value lists"),
        [T7AssetTypes.Vehicle] = ("vehicle", "vehicles"),
        [T7AssetTypes.Tracer] = ("tracer", "tracers"),
        [T7AssetTypes.SurfaceFxTable] = ("surface effect table", "surface effect tables"),
        [T7AssetTypes.SurfaceSoundDef] = ("surface sound", "surface sounds"),
        [T7AssetTypes.FootstepTable] = ("footstep table", "footstep tables"),
        [T7AssetTypes.EntityFxImpacts] = ("entity impact effect set", "entity impact effect sets"),
        [T7AssetTypes.EntitySoundImpacts] = ("entity impact sound set", "entity impact sound sets"),
        [T7AssetTypes.ZBarrier] = ("barrier", "barriers"),
        [T7AssetTypes.VehicleSoundDef] = ("vehicle sound set", "vehicle sound sets"),
        [T7AssetTypes.ScriptBundle] = ("script bundle", "script bundles"),
        [T7AssetTypes.Rumble] = ("rumble", "rumbles"),
        [T7AssetTypes.AimTable] = ("aim table", "aim tables"),
        [T7AssetTypes.AnimSelectorTableSet] = ("animation selector table", "animation selector tables"),
        [T7AssetTypes.AnimMappingTable] = ("animation mapping table", "animation mapping tables"),
        [T7AssetTypes.AnimStateMachine] = ("animation state machine", "animation state machines"),
        [T7AssetTypes.BehaviorTree] = ("behavior tree", "behavior trees"),
        [T7AssetTypes.BehaviorStateMachine] = ("behavior state machine", "behavior state machines"),
        [T7AssetTypes.SAnim] = ("animated texture", "animated textures"),
        [T7AssetTypes.LightDescription] = ("light description", "light descriptions"),
        [T7AssetTypes.XCam] = ("camera animation", "camera animations"),
        [T7AssetTypes.BgCache] = ("precache list", "precache lists"),
        [T7AssetTypes.TextureCombo] = ("texture combo", "texture combos"),
        [T7AssetTypes.AttachmentCosmeticVariant] = ("attachment variant", "attachment variants"),
        [T7AssetTypes.Attachment] = ("attachment", "attachments"),
        [T7AssetTypes.WeaponCamo] = ("weapon camo", "weapon camos"),
        [T7AssetTypes.NavMesh] = ("nav mesh", "nav meshes"),
        [T7AssetTypes.NavVolume] = ("nav volume", "nav volumes"),
        [T7AssetTypes.CustomizationTable] = ("customization table", "customization tables"),
        [T7AssetTypes.PlayerSoundsTable] = ("player sound table", "player sound tables"),
        [T7AssetTypes.PlayerFxTable] = ("player effect table", "player effect tables"),
        [T7AssetTypes.SharedWeaponSounds] = ("shared weapon sound set", "shared weapon sound sets"),
        [T7AssetTypes.Glasses] = ("glass set", "glass sets"),
        [T7AssetTypes.UmbraTome] = ("visibility tome", "visibility tomes"),
    };

    public static (string Singular, string Plural) Label(int type)
    {
        if (Labels.TryGetValue(type, out var label))
            return label;
        string name = T7AssetTypes.Name(type);
        return (name + " asset", name + " assets");
    }

    public static (FidelityDimension Dimension, string Singular, string Plural) StreamItem(string type) => type switch
    {
        "image" => (Textures, "streamed texture", "streamed textures"),
        "sanim" => (Textures, "streamed animated texture", "streamed animated textures"),
        "mesh" => (Geometry, "streamed mesh", "streamed meshes"),
        "probeVolume" => (Lighting, "probe volume", "probe volumes"),
        "reflectionProbes" => (Lighting, "reflection probe set", "reflection probe sets"),
        "skybox" => (World, "skybox", "skyboxes"),
        "SST" => (World, "streaming surface tree", "streaming surface trees"),
        _ => (World, "streamed item", "streamed items"),
    };

    private static (FidelityDimension Dimension, string Singular, string Plural) PlannedItem(string? owner) => owner switch
    {
        "xmodel" => (Geometry, "streamed mesh", "streamed meshes"),
        "image" => (Textures, "streamed texture", "streamed textures"),
        "sanim" => (Textures, "streamed animated texture", "streamed animated textures"),
        _ => (World, "streamed item", "streamed items"),
    };


    public void BeginAsset(T7Walk.Asset asset)
    {
        _asset = asset;
        _assetIssues.Clear();
    }

    public void Raise(T7Issue issue, string? sample = null, long count = 1)
    {
        if (_asset != null)
        {
            _assetIssues.TryAdd($"{issue.Key}|{issue.Dimension}|{sample}", (issue, sample, count));
            return;
        }
        Tracker.Note(issue.Dimension, issue.Key, issue.Grade, count, issue.Text, sample);
    }

    public void RaiseStreamItem(T7Issue issue, string? sample = null)
    {
        if (issue.Grade is FidelityGrade grade)
            _pendingStreamGrades[(issue.Dimension, grade)] = _pendingStreamGrades.GetValueOrDefault((issue.Dimension, grade)) + 1;
        Tracker.Note(issue.Dimension, issue.Key, issue.Grade, 1, issue.Text, sample);
    }

    public void DiscardAssetIssues() => _assetIssues.Clear();

    public void AbandonAsset()
    {
        _asset = null;
        _assetIssues.Clear();
    }

    public void EndAsset(T7Walk.Asset asset, string? converter, string? failure)
    {
        _asset = null;
        IEnumerable<int> types = asset.Registrations.Count > 0 ? asset.Registrations.Select(r => r.Type) : [asset.Type];
        foreach (IGrouping<int, int> group in types.GroupBy(t => t))
        {
            FidelityDimension dimension = Dimension(group.Key);
            Tracker.Add(dimension, failure == null ? Exact : Missing, group.Count());
            if (failure == null)
            {
                (string singular, string plural) = Label(group.Key);
                Tracker.Content(dimension, singular, plural, group.Count());
            }
        }
        if (failure != null)
        {
            _assetIssues.Clear();
            T7Issue failed = T7Issues.Failed(Dimension(asset.Type));
            Tracker.Note(failed.Dimension, failed.Key, failed.Grade, 1, failed.Text, $"{T7AssetTypes.Name(asset.Type)} '{asset.Name}': {Shorten(failure)}");
            return;
        }
        if (converter == "donor")
        {
            T7Issue donor = asset.Type is T7AssetTypes.TechniqueSet or T7AssetTypes.ComputeShaderSet ? T7Issues.TechniqueSetDonor : T7Issues.Donor(Dimension(asset.Type));
            _assetIssues.TryAdd($"{donor.Key}|{donor.Dimension}|{asset.Name}", (donor, asset.Name, 1));
        }
        if (converter is "copy" or "misc" && T7CopyConverter.LoaderCheckedTypes.Contains(asset.Type))
        {
            T7Issue checkedCopy = T7Issues.LoaderChecked(Dimension(asset.Type));
            _assetIssues.TryAdd($"{checkedCopy.Key}|{checkedCopy.Dimension}|{asset.Name}", (checkedCopy, T7AssetTypes.Name(asset.Type), 1));
        }
        foreach ((T7Issue issue, string? sample, long count) in _assetIssues.Values)
        {
            if (issue.Grade is FidelityGrade grade)
                Tracker.Downgrade(issue.Dimension, grade);
            Tracker.Note(issue.Dimension, issue.Key, issue.Grade, count, issue.Text, sample);
        }
        _assetIssues.Clear();
    }

    public void LeftOut(T7Issue issue) => Tracker.Note(issue.Dimension, issue.Key, issue.Grade, 1, issue.Text);


    public void Streams(T7StreamConvertResult result, T7StreamPlan plan)
    {
        foreach ((string what, int count) in result.Counts)
        {
            int colon = what.IndexOf(": ", StringComparison.Ordinal);
            if (colon < 0)
                continue;
            string type = what[..colon], source = what[(colon + 2)..];
            if (type == "?")
                continue;
            (FidelityDimension dimension, string singular, string plural) = StreamItem(type);
            ScoreStreamSource(dimension, singular, plural, source, count);
        }
        foreach (T7StreamMap.Item item in result.Map.Items.Where(i => i.Type == "?"))
        {
            (FidelityDimension dimension, string singular, string plural) = PlannedItem(plan.OwnerOf(item.PcKey));
            string source = item.Source.StartsWith("bundled from ", StringComparison.Ordinal) && item.Source.IndexOf('(') is int open and > 0
                ? "external, bundled " + item.Source[open..]
                : item.Source;
            ScoreStreamSource(dimension, singular, plural, source, 1);
        }
        foreach (IGrouping<string, string> group in result.Warnings.Where(w => w.Contains("is not its payload hash", StringComparison.Ordinal))
            .GroupBy(w => w.Split(' ', 2)[0]))
        {
            FidelityDimension dimension = StreamItem(group.Key).Dimension;
            T7Issue issue = T7Issues.StreamKeyNotHash(dimension);
            Tracker.Downgrade(dimension, Approximate, group.Count());
            Tracker.Note(dimension, issue.Key, issue.Grade, group.Count(), issue.Text);
        }
        foreach (((FidelityDimension dimension, FidelityGrade grade), long count) in _pendingStreamGrades)
            Tracker.Downgrade(dimension, grade, count);
        _pendingStreamGrades.Clear();
        Tracker.Emit();
    }

    private void ScoreStreamSource(FidelityDimension dimension, string singular, string plural, string source, long count)
    {
        bool bundled = false;
        if (source.StartsWith("external, bundled (", StringComparison.Ordinal) && source.EndsWith(')'))
        {
            bundled = true;
            source = source["external, bundled (".Length..^1];
        }
        switch (source)
        {
            case "external, another zone of the map":
            case "external, not used by the zone":
                return;
            case "external, unresolved":
            {
                Tracker.Add(dimension, Weak, count);
                T7Issue issue = T7Issues.StreamUnresolved(dimension, singular, plural);
                Tracker.Note(dimension, issue.Key, issue.Grade, count, issue.Text);
                break;
            }
            case "unchanged (undescribed)":
            {
                Tracker.Add(dimension, Good, count);
                T7Issue issue = T7Issues.StreamUndescribed(dimension, singular, plural);
                Tracker.Note(dimension, issue.Key, issue.Grade, count, issue.Text);
                break;
            }
            case "unchanged (no converter)":
            {
                Tracker.Add(dimension, Approximate, count);
                T7Issue issue = T7Issues.StreamUnchanged(dimension, singular, plural);
                Tracker.Note(dimension, issue.Key, issue.Grade, count, issue.Text);
                break;
            }
            case "unchanged (conversion failed)":
            {
                Tracker.Add(dimension, Approximate, count);
                T7Issue issue = T7Issues.StreamConvertFailed(dimension, singular, plural);
                Tracker.Note(dimension, issue.Key, issue.Grade, count, issue.Text);
                break;
            }
            default:
                Tracker.Add(dimension, Exact, count);
                break;
        }
        Tracker.Content(dimension, singular, plural, count);
        if (bundled)
        {
            T7Issue issue = T7Issues.StreamBundled(dimension, singular, plural);
            Tracker.Note(dimension, issue.Key, issue.Grade, count, issue.Text);
        }
    }


    public void SoundBank(int entries, int reencoded, int resampled, int retimed)
    {
        Tracker.Add(D.Sound, Exact, entries - reencoded);
        Tracker.Add(D.Sound, Strong, reencoded);
        Tracker.Content(D.Sound, "sound", "sounds", entries);
        Tracker.Note(D.Sound, T7Issues.SoundReencoded.Key, T7Issues.SoundReencoded.Grade, reencoded, T7Issues.SoundReencoded.Text);
        Tracker.Note(D.Sound, T7Issues.SoundResampled.Key, T7Issues.SoundResampled.Grade, resampled, T7Issues.SoundResampled.Text);
        Tracker.Note(D.Sound, T7Issues.LoopRetimed.Key, T7Issues.LoopRetimed.Grade, retimed, T7Issues.LoopRetimed.Text);
    }

    public void SoundBankFailed(string bank, int? entries, string reason)
    {
        Tracker.Add(D.Sound, Missing, Math.Max(1, entries ?? 1));
        Tracker.Note(D.Sound, T7Issues.SoundBankFailed.Key, T7Issues.SoundBankFailed.Grade, 1, T7Issues.SoundBankFailed.Text, $"{bank}: {Shorten(reason)}");
    }

    public void SoundOff(int banks)
    {
        Tracker.Add(D.Sound, Missing, banks);
        Tracker.Note(D.Sound, T7Issues.SoundOff.Key, T7Issues.SoundOff.Grade, banks, T7Issues.SoundOff.Text);
    }

    public void Movie(string name, Formats.T7MovieTranscoder.Outcome outcome)
    {
        T7Issue issue = outcome == Formats.T7MovieTranscoder.Outcome.Copied ? T7Issues.MovieCopied : T7Issues.MovieEncoded;
        Tracker.Add(World, outcome == Formats.T7MovieTranscoder.Outcome.Copied ? Exact : Strong);
        Tracker.Content(World, "movie", "movies");
        Tracker.Note(World, issue.Key, issue.Grade, 1, issue.Text, name);
    }

    public void MovieFailed(string name, string reason)
    {
        Tracker.Add(World, Missing);
        Tracker.Note(World, T7Issues.MovieFailed.Key, T7Issues.MovieFailed.Grade, 1, T7Issues.MovieFailed.Text, $"{name}: {Shorten(reason)}");
    }

    private static string Shorten(string text) => text.Length <= 160 ? text : text[..157] + "...";

    public static string Of(long done, long total) => $"{done.ToString("N0", CultureInfo.InvariantCulture)} of {total.ToString("N0", CultureInfo.InvariantCulture)}";
}
