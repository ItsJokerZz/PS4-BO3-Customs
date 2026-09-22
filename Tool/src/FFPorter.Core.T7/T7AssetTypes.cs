namespace FFPorter.Core.T7;

public static class T7AssetTypes
{
    public const int PhysPreset = 0, PhysConstraints = 1, DestructibleDef = 2, XAnimParts = 3, XModel = 4, XModelMesh = 5,
        Material = 6, ComputeShaderSet = 7, TechniqueSet = 8, Image = 9, Sound = 10, SoundPatch = 11, ClipMap = 12, ComWorld = 13,
        GameWorld = 14, MapEnts = 15, GfxWorld = 16, LightDef = 17, LensFlareDef = 18, UiMap = 19, Font = 20, FontIcon = 21,
        Localize = 22, Weapon = 23, WeaponDef = 24, WeaponVariant = 25, WeaponFull = 26, CgMediaTable = 27, PlayerSoundsTable = 28,
        PlayerFxTable = 29, SharedWeaponSounds = 30, Attachment = 31, AttachmentUnique = 32, WeaponCamo = 33, CustomizationTable = 34,
        CustomizationTableFeImages = 35, CustomizationTableColor = 36, SndDriverGlobals = 37, Fx = 38, TagFx = 39, NewLensFlareDef = 40,
        ImpactFx = 41, ImpactSound = 42, PlayerCharacter = 43, AiType = 44, Character = 45, XModelAlias = 46, RawFile = 47,
        StringTable = 48, StructuredTable = 49, LeaderboardDef = 50, Ddl = 51, Glasses = 52, TextureList = 53, ScriptParseTree = 54,
        KeyValuePairs = 55, Vehicle = 56, AddonMapEnts = 57, Tracer = 58, Slug = 59, SurfaceFxTable = 60, SurfaceSoundDef = 61,
        FootstepTable = 62, EntityFxImpacts = 63, EntitySoundImpacts = 64, ZBarrier = 65, VehicleFxDef = 66, VehicleSoundDef = 67,
        TypeInfo = 68, ScriptBundle = 69, ScriptBundleList = 70, Rumble = 71, BulletPenetration = 72, LocDmgTable = 73, AimTable = 74,
        AnimSelectorTableSet = 75, AnimMappingTable = 76, AnimStateMachine = 77, BehaviorTree = 78, BehaviorStateMachine = 79, Ttf = 80,
        SAnim = 81, LightDescription = 82, Shellshock = 83, XCam = 84, BgCache = 85, TextureCombo = 86, FlameTable = 87, Bitfield = 88,
        AttachmentCosmeticVariant = 89, MapTable = 90, MapTableLoadingImages = 91, Medal = 92, MedalTable = 93, Objective = 94,
        ObjectiveList = 95, UmbraTome = 96, NavMesh = 97, NavVolume = 98, BinaryHtml = 99, Laser = 100, Beam = 101, StreamerHint = 102,
        Count = 103;

    private static readonly string[] Names =
    [
        "physpreset", "physconstraints", "destructibledef", "xanim", "xmodel", "xmodelmesh", "material", "computeshaderset",
        "techset", "image", "sound", "sound_patch", "col_map", "com_map", "game_map", "map_ents", "gfx_map", "lightdef",
        "lensflaredef", "ui_map", "font", "fonticon", "localize", "weapon", "weapondef", "weaponvariant", "weaponfull",
        "cgmediatable", "playersoundstable", "playerfxtable", "sharedweaponsounds", "attachment", "attachmentunique",
        "weaponcamo", "customizationtable", "customizationtable_feimages", "customizationtablecolor", "snddriverglobals", "fx",
        "tagfx", "klf", "impactsfxtable", "impactsoundstable", "player_character", "aitype", "character", "xmodelalias",
        "rawfile", "stringtable", "structuredtable", "leaderboarddef", "ddl", "glasses", "texturelist", "scriptparsetree",
        "keyvaluepairs", "vehicle", "addon_map_ents", "tracer", "slug", "surfacefxtable", "surfacesounddef", "footsteptable",
        "entityfximpacts", "entitysoundimpacts", "zbarrier", "vehiclefxdef", "vehiclesounddef", "typeinfo", "scriptbundle",
        "scriptbundlelist", "rumble", "bulletpenetration", "locdmgtable", "aimtable", "animselectortable", "animmappingtable",
        "animstatemachine", "behaviortree", "behaviorstatemachine", "ttf", "sanim", "lightdescription", "shellshock", "xcam",
        "bgcache", "texturecombo", "flametable", "bitfield", "attachmentcosmeticvariant", "maptable", "maptableloadingimages",
        "medal", "medaltable", "objective", "objectivelist", "umbra_tome", "navmesh", "navvolume", "binaryhtml", "laser", "beam",
        "streamerhint",
    ];

    public static string Name(long type) => type >= 0 && type < Names.Length ? Names[type] : $"type{type}";

    public static readonly string[] BlockNames =
        ["temp", "runtime_virtual", "runtime_physical", "delay_virtual", "delay_physical", "virtual", "physical", "streamer_reserve", "streamer", "memmapped"];
}
