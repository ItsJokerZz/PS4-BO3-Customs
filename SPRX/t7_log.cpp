#include "headers.hpp"
#include "t7_log.hpp"

#ifdef _DEBUG

namespace T7Log
{
constexpr uintptr_t kUiLuaError   = 0xDB4050;
constexpr uintptr_t kLuaToLString = 0xC1A9B0;
constexpr uintptr_t kLuaState     = 0xE9831D0;
constexpr uintptr_t kScriptError  = 0x77F080;
constexpr uintptr_t kScrVmPub     = 0x3448A80;
constexpr uintptr_t kErrorDialog  = 0xEF26E0;
constexpr uintptr_t kDefaultEntry = 0x858060;
constexpr uintptr_t kReadName     = 0x57E79A0;
constexpr uintptr_t kAssetEntries = 0x587E710;
constexpr uintptr_t kExitLevel    = 0xA5CEF0;
constexpr uintptr_t kMapBuiltin   = 0xA6C080;
constexpr uintptr_t kCbufAddText  = 0xEE44C0;
constexpr uintptr_t kSvShutdown   = 0xEED990;
constexpr uintptr_t kKillServer   = 0xA5D210;
constexpr uintptr_t kKillServerRow = 0x6CFA450;
constexpr uint32_t  kKillServerHash = 0xB2E858FC;
constexpr uintptr_t kScrCodePos   = 0x3465120;

constexpr int     kAssetEntryCount = 0x25400;
constexpr int32_t kScriptParseTree = 54;

constexpr uintptr_t kStateTop   = 0x48;
constexpr uintptr_t kStateStack = 0x60;

constexpr uint32_t kUiErrorsLogged     = 100;
constexpr uint32_t kScriptErrorsLogged = 200;
constexpr uint32_t kMissingLogged      = 3000;
constexpr uint32_t kCommandsLogged     = 200;
constexpr int      kShutdownNotes      = 8;
constexpr int      kShutdownCallers    = 12;
constexpr int      kMissingSlots       = 8192;
constexpr uint32_t kChainKept          = 12;
constexpr size_t   kNameMax            = 192;
constexpr uint32_t kLuaFilesKept       = 1024;
constexpr uint32_t kFramesMax          = 12;
constexpr uint32_t kCandidatesMax      = 8;
constexpr uint32_t kConstsMax          = 8192;
constexpr uint32_t kSharedHashFiles    = 3;
constexpr int      kWriterWindow       = 24;
constexpr int      kFunctionDepth      = 48;

constexpr uint8_t kOpGetField     = 0;
constexpr uint8_t kOpCallI        = 2;
constexpr uint8_t kOpCallC        = 3;
constexpr uint8_t kOpGetGlobal    = 6;
constexpr uint8_t kOpMove         = 7;
constexpr uint8_t kOpSelf         = 8;
constexpr uint8_t kOpTailCallI    = 22;
constexpr uint8_t kOpTailCallC    = 23;
constexpr uint8_t kOpTailCallM    = 24;
constexpr uint8_t kOpCallM        = 29;
constexpr uint8_t kOpCall         = 30;
constexpr uint8_t kOpTailCall     = 37;
constexpr uint8_t kOpGetUpval     = 38;
constexpr uint8_t kOpTailCallIR1  = 76;
constexpr uint8_t kOpCallIR1      = 77;
constexpr uint8_t kOpGetFieldR1   = 81;
constexpr uint8_t kOpData         = 84;
constexpr uint8_t kOpGetFieldMM   = 96;
constexpr uint8_t kOpGetGlobalMem = 99;

static const char kLogPath[] = "/data/BO3-Customs/log.txt";

static const uint8_t k_uiLuaError[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                        0x53, 0x48, 0x83, 0xEC, 0x58, 0x4C, 0x8B, 0x3D };
static const uint8_t k_scriptError[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                         0x53, 0x48, 0x81, 0xEC, 0x88, 0x00, 0x00, 0x00, 0x4C, 0x8B, 0x3D };
static const uint8_t k_errorDialog[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x54, 0x53, 0x49,
                                         0x89, 0xF7, 0x4C, 0x8D, 0x35 };
static const uint8_t k_defaultEntry[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                          0x53, 0x48, 0x83, 0xEC, 0x18, 0x49, 0x89, 0xF7, 0x41, 0x89, 0xFC, 0xE8 };
static const uint8_t k_exitLevel[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                       0x53, 0x50, 0x8B, 0x05 };
static const uint8_t k_mapBuiltin[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x56, 0x53, 0x31, 0xFF, 0x31, 0xF6, 0x45,
                                        0x31, 0xF6, 0xE8 };
static const uint8_t k_cbufAddText[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                         0x53, 0x48, 0x83, 0xEC, 0x18, 0x4C, 0x8B, 0x25 };
static const uint8_t k_svShutdown[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                        0x53, 0x48, 0x83, 0xE4, 0xE0, 0x48, 0x81, 0xEC, 0xC0, 0x39, 0x00, 0x00 };

static const char* const k_typeNames[] =
{
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
};

constexpr uint32_t kTypeCount = (uint32_t)(sizeof(k_typeNames) / sizeof(k_typeNames[0]));

static const char* const k_pcOnlyGlobals[] =
{
    "AllowGuestSplitScreenOnline", "CheckNavRestrictions", "DisableMouseButtonOnElement", "DisableMouseOnElement",
    "DisableMouseOnMenuElement", "EnableMouseOnElement", "EnableMouseOnMenuElement", "GamepadsConnectedAny",
    "GamepadsConnectedIsActive", "GetAARXPNextLevel", "GetAARXPStarterPackNotice", "GetAARXpEarned",
    "GetGameTypeDisplayString", "HideMouseCursor", "IsChunkDownloading", "IsChunksRestrictedButtonByParty",
    "IsFilterActive", "IsGameModeOwned", "IsRestrictedButtonByParty", "IsServerBrowserEnabled",
    "IsSplitscreenLobbyRoomAvailable", "IsSplitscreenPlayAvailable", "IsSplitscreenPlayerSignedIn",
    "IsStarterPackMaxLevel", "IsStarterPackNotAvailableButton", "IsStarterPackRestrictedButton",
    "IsStarterPackRestrictedButtonByParty", "IsStarterPackWatermarkHidden", "IsSteamServerBrowserEmpty",
    "IsSteamServerBrowserUpdating", "JoinServerBrowser", "LobbySplitscreenToggle", "MapImageToModPreview",
    "Mods_Enabled", "Mods_LoadMod", "Mods_OpenLoadMenu", "Mods_RefreshListMods", "Mods_RefreshListUsermaps",
    "Mods_Unload", "OpenServerBrowser", "OpenServerBrowserFilters",
    "OpenServerSettings", "PaintshopEditAvailable", "PartyMemberMissingContent", "RefreshLobbyServerBrowser",
    "SecondsAsTimePlayedStringShort", "ServerBrowserCancelRequest", "ServerBrowserQuickRefresh",
    "ServerBrowserRefreshServer", "ServerBrowserRequestPlayersList", "ServerBrowserSetFavorite",
    "ServerBrowserToggleFilter", "ServerFiltersEditKeywords", "ServerFiltersHandleKeyboardComplete",
    "ServerSettingsEditDescription", "ServerSettingsEditName", "ServerSettingsEditPassword",
    "ServerSettingsHandleKeyboardComplete", "SetCompetitiveAttachmentSettingsTab", "SetCompetitiveItemSettingsFilter",
    "SetGameSettingsTab", "SetNetworkMode", "SetPregameVoteFilter", "ShouldUnloadMod", "ShowMouseCursor",
    "StarterParckPurchase", "SteamServerIsCurrentServerTypeFavorites", "SteamServerIsCurrentServerTypeHistory",
    "SteamServerSortMatchesHeaderAscending", "SteamServerSortMatchesHeaderDescending", "StoreButtonOpenSteamStore",
    "TruncateString", "TruncateTo32Chars", "TruncateTo64Chars", "UIKeyboardCancel", "UIKeyboardComplete",
    "joinResult",
};

using Passthrough_t = uint64_t (*)(uint64_t, uint64_t, uint64_t, uint64_t, uint64_t, uint64_t,
                                   double, double, double, double, double, double, double, double);
using ToLString_t = const char* (*)(uintptr_t, uint64_t, uint64_t*);

struct LuaFile
{
    char      name[kNameMax];
    uintptr_t data;
    uint32_t  length;
};

struct Frame
{
    uint32_t    hash;
    uint32_t    pc;
    uint32_t    files;
    const char* lastFile;
    char        first[kNameMax];
    char        second[kNameMax];
};

struct Candidate
{
    char text[384];
    bool pcOnly;
};

struct Cursor
{
    const uint8_t* data;
    uint32_t       size;
    uint32_t       at;
    bool           ok;
};

struct Inst
{
    uint8_t  op;
    uint8_t  a;
    uint32_t b;
    uint32_t c;
    bool     ck;
};

static uintptr_t g_base = 0;

static Detour g_uiErrorDetour{};
static void*  g_uiErrorOriginal = nullptr;
static Detour g_scriptErrorDetour{};
static void*  g_scriptErrorOriginal = nullptr;
static Detour g_errorDialogDetour{};
static void*  g_errorDialogOriginal = nullptr;
static Detour g_defaultDetour{};
static void*  g_defaultOriginal = nullptr;
static Detour g_exitLevelDetour{};
static void*  g_exitLevelOriginal = nullptr;
static Detour g_mapBuiltinDetour{};
static void*  g_mapBuiltinOriginal = nullptr;
static Detour g_cbufDetour{};
static void*  g_cbufOriginal = nullptr;
static Detour g_shutdownDetour{};
static void*  g_shutdownOriginal = nullptr;
static void*  g_killServerOriginal = nullptr;

struct ShutdownNote
{
    char      reason[64];
    uintptr_t callers[kShutdownCallers];
    uint32_t  scriptErrors;
};

static ShutdownNote      g_shutdownNotes[kShutdownNotes];
static volatile uint32_t g_shutdownNoted = 0;
static uint32_t          g_shutdownShown = 0;
static volatile uint32_t g_commands = 0;

static volatile int32_t  g_lock = 0;
static volatile uint32_t g_uiErrors = 0;
static volatile uint32_t g_scriptErrors = 0;
static volatile uint32_t g_missingCount = 0;
static volatile uint64_t g_missingSeen[kMissingSlots];

static char     g_lastLui[kNameMax];
static char     g_chain[kChainKept][kNameMax];
static uint32_t g_chainAt = 0;
static uint32_t g_chainShown = 0;
static LuaFile  g_luaFiles[kLuaFilesKept];
static uint32_t g_luaFileCount = 0;

static Frame     s_frames[kFramesMax];
static uint32_t  s_frameCount = 0;
static Candidate s_candidates[kCandidatesMax];
static uint32_t  s_candidateCount = 0;
static uint32_t  s_constAt[kConstsMax];
static uint32_t  s_constCount = 0;
static char      s_chain[kChainKept][kNameMax];

static void Lock()
{
    while (__sync_lock_test_and_set(&g_lock, 1))
    {
    }
}

static void Unlock()
{
    __sync_lock_release(&g_lock);
}

static void Append(const char* text, size_t size)
{
    const int fd = sceKernelOpen(kLogPath, SCE_KERNEL_O_WRONLY | SCE_KERNEL_O_CREAT | SCE_KERNEL_O_APPEND, 0777);

    if (fd < 0)
        return;

    sceKernelWrite(fd, text, size);
    sceKernelClose(fd);
}

static void WriteV(const char* format, va_list args)
{
    char line[2048];
    const uint64_t now = sceKernelGetProcessTime();
    const int stamp = snprintf(line, sizeof(line), "[%02u:%02u.%03u] ", (unsigned)(now / 60000000ull),
                               (unsigned)(now / 1000000ull % 60), (unsigned)(now / 1000ull % 1000));

    if (stamp <= 0)
        return;

    const size_t room = sizeof(line) - (size_t)stamp - 1;
    const int body = vsnprintf(line + stamp, room, format, args);

    if (body < 0)
        return;

    size_t used = (size_t)stamp + ((size_t)body < room ? (size_t)body : room - 1);
    line[used++] = '\n';
    Append(line, used);
}

static const char* ReadableText(uintptr_t p)
{
    if (!p || !RangeReadable(p, 2))
        return nullptr;

    const char* const text = (const char*)p;
    return (text[0] >= 0x20 && text[0] < 0x7F) ? text : nullptr;
}

static const char* TypeName(uint32_t type, char* scratch, size_t size)
{
    if (type < kTypeCount)
        return k_typeNames[type];

    snprintf(scratch, size, "type %u", type);
    return scratch;
}

static bool IsPcOnly(const char* text, uint32_t length)
{
    for (const char* const name : k_pcOnlyGlobals)
    {
        if (strlen(name) == length && memcmp(name, text, length) == 0)
            return true;
    }

    return false;
}

static bool Need(Cursor& c, uint32_t n)
{
    if (!c.ok || n > c.size - c.at)
    {
        c.ok = false;
        return false;
    }

    return true;
}

static uint8_t U8(Cursor& c)
{
    return Need(c, 1) ? c.data[c.at++] : 0;
}

static uint32_t U32(Cursor& c)
{
    if (!Need(c, 4))
        return 0;

    uint32_t value;
    memcpy(&value, c.data + c.at, 4);
    c.at += 4;
    return value;
}

static void Skip(Cursor& c, uint32_t n)
{
    if (Need(c, n))
        c.at += n;
}

static Inst Decode(const uint8_t* p)
{
    Inst in;
    in.a = p[0];
    in.c = p[1];
    in.ck = (p[2] & 1) != 0;
    in.b = (uint32_t)(p[2] >> 1) + ((p[3] & 1) ? 128u : 0u);
    in.op = (uint8_t)(p[3] >> 1);
    return in;
}

static uint32_t Bx(const Inst& in)
{
    return in.b * 512 + in.c + (in.ck ? 256u : 0u);
}

static uint32_t FieldIndex(const Inst& in)
{
    return in.c + (in.ck ? 256u : 0u);
}

static bool IsCall(uint8_t op)
{
    return op == kOpCallI || op == kOpCallC || op == kOpCallM || op == kOpCall || op == kOpCallIR1 ||
           op == kOpTailCallI || op == kOpTailCallC || op == kOpTailCallM || op == kOpTailCall || op == kOpTailCallIR1;
}

static bool IsGlobal(uint8_t op)
{
    return op == kOpGetGlobal || op == kOpGetGlobalMem;
}

static bool IsField(uint8_t op)
{
    return op == kOpGetField || op == kOpGetFieldR1 || op == kOpGetFieldMM;
}

static bool Constant(const Cursor& c, uint32_t index, const char** text, uint32_t* length)
{
    if (index >= s_constCount)
        return false;

    const uint32_t at = s_constAt[index];

    if (at + 9 > c.size || c.data[at] != 4)
        return false;

    uint32_t size;
    memcpy(&size, c.data + at + 1, 4);

    if (size == 0 || size - 1 > c.size - (at + 9))
        return false;

    *text = (const char*)c.data + at + 9;
    *length = size - 1;
    return true;
}

static int WriterOf(const uint8_t* code, int from, uint32_t reg)
{
    for (int i = from - 1; i >= 0 && i >= from - kWriterWindow; --i)
    {
        const Inst in = Decode(code + 4 * i);

        if (in.op == kOpData || in.a != reg)
            continue;

        if (IsGlobal(in.op) || IsField(in.op) || in.op == kOpSelf || in.op == kOpMove || in.op == kOpGetUpval)
            return i;
    }

    return -1;
}

static void BaseName(const Cursor& c, const uint8_t* code, int at, uint32_t reg, char* out, size_t size, int depth)
{
    snprintf(out, size, "?");
    const int writer = WriterOf(code, at, reg);

    if (writer < 0)
        return;

    const Inst in = Decode(code + 4 * writer);
    const char* text;
    uint32_t length;

    if (IsGlobal(in.op) && Constant(c, Bx(in), &text, &length))
    {
        snprintf(out, size, "%.*s", (int)length, text);
    }
    else if (IsField(in.op) && Constant(c, FieldIndex(in), &text, &length))
    {
        char base[96];

        if (depth > 0)
            BaseName(c, code, writer, in.b, base, sizeof(base), depth - 1);
        else
            snprintf(base, sizeof(base), "?");

        snprintf(out, size, "%s.%.*s", base, (int)length, text);
    }
}

static void DescribeCallee(const Cursor& c, const uint8_t* code, int pc, uint32_t reg, char* out, size_t size,
                           bool* pcOnly)
{
    snprintf(out, size, "a value computed earlier");

    for (int hop = 0; hop < 3; ++hop)
    {
        const int writer = WriterOf(code, pc, reg);

        if (writer < 0)
            return;

        const Inst in = Decode(code + 4 * writer);
        const char* text;
        uint32_t length;

        if (IsGlobal(in.op))
        {
            if (Constant(c, Bx(in), &text, &length))
            {
                snprintf(out, size, "the global %.*s", (int)length, text);
                *pcOnly = IsPcOnly(text, length);
            }

            return;
        }

        if (IsField(in.op) || in.op == kOpSelf)
        {
            const uint32_t index = in.op == kOpSelf ? in.c + (in.ck ? 0u : 0x10000u) : FieldIndex(in);
            char base[96];
            BaseName(c, code, writer, in.b, base, sizeof(base), 1);

            if (Constant(c, index, &text, &length))
                snprintf(out, size, "%s%s%.*s", base, in.op == kOpSelf ? ":" : ".", (int)length, text);

            return;
        }

        if (in.op == kOpGetUpval)
        {
            snprintf(out, size, "an upvalue");
            return;
        }

        reg = in.b;
        pc = writer;
    }
}

static void Visit(const Cursor& c, const char* file, uint32_t hash, uint32_t count, uint32_t codeAt)
{
    for (uint32_t i = 0; i < s_frameCount; ++i)
    {
        Frame& frame = s_frames[i];

        if (frame.hash != hash)
            continue;

        if (frame.lastFile != file)
        {
            if (frame.files == 0)
                snprintf(frame.first, kNameMax, "%s", file);
            else if (frame.files == 1)
                snprintf(frame.second, kNameMax, "%s", file);

            frame.lastFile = file;
            ++frame.files;
        }

        if (i != 0 || frame.pc >= count || s_candidateCount >= kCandidatesMax)
            continue;

        const uint8_t* const code = c.data + codeAt;
        const Inst in = Decode(code + 4 * frame.pc);

        if (!IsCall(in.op))
            continue;

        char callee[256];
        bool pcOnly = false;
        DescribeCallee(c, code, (int)frame.pc, in.a, callee, sizeof(callee), &pcOnly);

        Candidate& candidate = s_candidates[s_candidateCount++];
        candidate.pcOnly = pcOnly;
        snprintf(candidate.text, sizeof(candidate.text), "%s in %s%s", callee, file,
                 pcOnly ? " - a PC-only global the PS4 game does not have" : "");
    }
}

static void ParseFunction(Cursor& c, const char* file, int depth)
{
    if (depth > kFunctionDepth)
    {
        c.ok = false;
        return;
    }

    U32(c);
    U32(c);
    U8(c);
    U32(c);
    const uint32_t count = U32(c);
    U32(c);

    const uint32_t pad = 4 - c.at % 4;

    if (pad > 0 && pad < 4)
        Skip(c, pad);

    if (!c.ok || count > (c.size - c.at) / 4)
    {
        c.ok = false;
        return;
    }

    const uint32_t codeAt = c.at;
    Skip(c, count * 4);

    const uint32_t constants = U32(c);

    if (!c.ok || constants > c.size)
    {
        c.ok = false;
        return;
    }

    s_constCount = constants < kConstsMax ? constants : kConstsMax;

    for (uint32_t i = 0; i < constants && c.ok; ++i)
    {
        if (i < kConstsMax)
            s_constAt[i] = c.at;

        switch (U8(c))
        {
        case 0:
            break;
        case 1:
            Skip(c, 1);
            break;
        case 3:
            Skip(c, 4);
            break;
        case 4:
        {
            const uint32_t size = U32(c);
            U32(c);

            if (size == 0)
            {
                c.ok = false;
                break;
            }

            Skip(c, size);
            break;
        }
        case 11:
        case 13:
            Skip(c, 8);
            break;
        default:
            c.ok = false;
            break;
        }
    }

    U32(c);
    const uint32_t hash = U32(c);
    const uint32_t children = U32(c);

    if (!c.ok)
        return;

    Visit(c, file, hash, count, codeAt);

    for (uint32_t i = 0; i < children && c.ok; ++i)
        ParseFunction(c, file, depth + 1);
}

static void ParseFile(const LuaFile& file)
{
    if (!file.data || file.length < 32 || !RangeReadable(file.data, file.length))
        return;

    Cursor c = { (const uint8_t*)file.data, file.length, 0, true };

    if (memcmp(c.data, "\x1bLua", 4) != 0)
        return;

    Skip(c, 14);
    const uint32_t types = U32(c);

    if (!c.ok || types > 64)
        return;

    for (uint32_t i = 0; i < types && c.ok; ++i)
    {
        U32(c);
        Skip(c, U32(c));
    }

    if (c.ok)
        ParseFunction(c, file.name, 0);
}

static void ReadFrames(const char* message)
{
    s_frameCount = 0;

    for (const char* at = message; *at && s_frameCount < kFramesMax; ++at)
    {
        if (*at < '0' || *at > '9' || (at != message && at[-1] >= '0' && at[-1] <= '9'))
            continue;

        const char* end = at;
        uint64_t hash = 0;

        while (*end >= '0' && *end <= '9' && end - at < 11)
        {
            hash = hash * 10 + (uint64_t)(*end - '0');
            ++end;
        }

        if (end - at < 6 || *end != ':' || end[1] < '0' || end[1] > '9' || hash > 0xFFFFFFFFull)
            continue;

        uint32_t pc = 0;

        for (const char* digit = end + 1; *digit >= '0' && *digit <= '9'; ++digit)
            pc = pc * 10 + (uint32_t)(*digit - '0');

        bool seen = false;

        for (uint32_t i = 0; i < s_frameCount; ++i)
            seen = seen || (s_frames[i].hash == (uint32_t)hash && s_frames[i].pc == pc);

        if (!seen)
        {
            Frame& frame = s_frames[s_frameCount++];
            frame.hash = (uint32_t)hash;
            frame.pc = pc;
            frame.files = 0;
            frame.lastFile = nullptr;
            frame.first[0] = 0;
            frame.second[0] = 0;
        }

        at = end;
    }
}

static void Explain(const char* message)
{
    ReadFrames(message);
    s_candidateCount = 0;

    if (!s_frameCount)
        return;

    Lock();

    for (uint32_t i = 0; i < g_luaFileCount; ++i)
        ParseFile(g_luaFiles[i]);

    Unlock();

    for (uint32_t i = 0; i < s_frameCount; ++i)
    {
        const Frame& frame = s_frames[i];

        if (frame.files >= kSharedHashFiles)
            T7Log_Write("[Lua]   %u:%u is in one of %u map Lua files (PC's mod tools give every map function this number)",
                        frame.hash, frame.pc, frame.files);
        else if (frame.files == 2)
            T7Log_Write("[Lua]   %u:%u is in %s or %s (instruction %u of that function)", frame.hash, frame.pc,
                        frame.first, frame.second, frame.pc);
        else if (frame.files == 1)
            T7Log_Write("[Lua]   %u:%u is in %s (instruction %u of that function)", frame.hash, frame.pc, frame.first,
                        frame.pc);
        else
            T7Log_Write("[Lua]   %u:%u is in no Lua file loaded in this level", frame.hash, frame.pc);
    }

    for (int pass = 0; pass < 2; ++pass)
    {
        for (uint32_t i = 0; i < s_candidateCount; ++i)
        {
            if (s_candidates[i].pcOnly == (pass == 0))
                T7Log_Write("[Lua]   the failing call could be to %s", s_candidates[i].text);
        }
    }
}

static uint32_t UiErrorNumber(const char* text)
{
    uint64_t hash = 5381;

    for (const signed char* at = (const signed char*)text; at != nullptr && *at != 0; ++at)
        hash = hash * 33 + (uint64_t)(int64_t)*at;

    return (uint32_t)hash % 100000u;
}

static uint64_t UiLuaError(uint64_t L, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                           double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const char* message = nullptr;

    if (L != 0 && RangeReadable((uintptr_t)L, kStateStack + 8))
    {
        const uint64_t stack = *(const uint64_t*)(L + kStateStack);
        const uint64_t top = *(const uint64_t*)(L + kStateTop);

        if (top >= stack + 16)
            message = ((ToLString_t)(g_base + kLuaToLString))((uintptr_t)L, top - 16, nullptr);
    }

    const uint32_t count = __sync_add_and_fetch(&g_uiErrors, 1);

    if (count <= kUiErrorsLogged)
    {
        const bool ui = L == *(const uint64_t*)(g_base + kLuaState);
        char last[kNameMax];
        uint32_t chainCount = 0;

        Lock();
        memcpy(last, g_lastLui, sizeof(last));

        if (g_chainShown != g_chainAt)
        {
            const uint32_t kept = g_chainAt < kChainKept ? g_chainAt : kChainKept;

            for (uint32_t i = 0; i < kept; ++i)
                memcpy(s_chain[chainCount++], g_chain[(g_chainAt - kept + i) % kChainKept], kNameMax);

            g_chainShown = g_chainAt;
        }

        Unlock();

        T7Log_Write("[Lua] %s Error %u (last Lua file loaded: %s): %s", ui ? "UI" : "LobbyVM",
                    (unsigned)UiErrorNumber(message ? message : ""), last[0] ? last : "none",
                    message ? message : "(no message on the state's stack)");

        for (uint32_t i = 0; i < chainCount; ++i)
            T7Log_Write("[Lua]   loaded %u of %u before it: %s", i + 1, chainCount, s_chain[i]);

        if (message)
            Explain(message);

        if (count == kUiErrorsLogged)
            T7Log_Write("[Lua] %u UI errors this level - the rest are not logged", kUiErrorsLogged);
    }

    return ((Passthrough_t)g_uiErrorOriginal)(L, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static const char* ScriptAt(uintptr_t codePos, uint32_t* offset)
{
    if (!codePos || !RangeReadable(g_base + kAssetEntries, (size_t)kAssetEntryCount * 32))
        return nullptr;

    for (int i = 0; i < kAssetEntryCount; ++i)
    {
        const uintptr_t entry = g_base + kAssetEntries + 32 * (uintptr_t)i;

        if (*(const int32_t*)entry != kScriptParseTree || !*(const uint8_t*)(entry + 0x11))
            continue;

        const uintptr_t header = *(const uintptr_t*)(entry + 8);

        if (!header || !RangeReadable(header, 24))
            continue;

        const uintptr_t buffer = *(const uintptr_t*)(header + 16);
        const uint32_t length = *(const uint32_t*)(header + 8);

        if (codePos >= buffer && codePos < buffer + length)
        {
            *offset = (uint32_t)(codePos - buffer);
            return ReadableText(*(const uintptr_t*)header);
        }
    }

    return nullptr;
}

static uint64_t ScriptError(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                            double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uint32_t count = __sync_add_and_fetch(&g_scriptErrors, 1);

    if (count <= kScriptErrorsLogged)
    {
        const int inst = (int)(uint32_t)a1;
        const uintptr_t raised =
            inst >= 0 && inst < 2 ? *(const uintptr_t*)(g_base + kScrVmPub + 0x70 * (uintptr_t)inst + 0x10) : 0;
        const char* const message = ReadableText(raised);
        const char* const dialog = ReadableText(a4);
        uint32_t offset = 0;
        const char* const script = ScriptAt(a2, &offset);

        T7Log_Write("[Script] %s runtime error %u in %s +0x%X: %s%s%s", inst == 1 ? "client (csc)" : "server (gsc)",
                    count, script ? script : "(no loaded script holds the code position)", offset,
                    message ? message : "(no message)", dialog && dialog != message ? " / " : "",
                    dialog && dialog != message ? dialog : "");

        if (count == kScriptErrorsLogged)
            T7Log_Write("[Script] %u runtime errors this level - the rest are not logged", kScriptErrorsLogged);
    }

    return ((Passthrough_t)g_scriptErrorOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static uint64_t ErrorDialog(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                            double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const char* const message = ReadableText(a2);

    T7Log_Write("[Error] Com_Error %u: %s (%u script runtime error(s) this level)", (uint32_t)a1,
                message ? message : "(no message)", g_scriptErrors);

    return ((Passthrough_t)g_errorDialogOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static void LogScriptCall(const char* what)
{
    const uintptr_t codePos = *(const uintptr_t*)(g_base + kScrCodePos);
    uint32_t offset = 0;
    const char* const script = ScriptAt(codePos, &offset);

    T7Log_Write("[Script] %s called from %s +0x%X (%u script runtime error(s) this level)", what,
                script ? script : "(no loaded script holds the code position)", offset, g_scriptErrors);
}

static uint64_t ExitLevel(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                          double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    LogScriptCall("ExitLevel()");
    return ((Passthrough_t)g_exitLevelOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static uint64_t MapBuiltin(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                           double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    LogScriptCall("map()");
    return ((Passthrough_t)g_mapBuiltinOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static uint64_t KillServer(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                           double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    LogScriptCall("KillServer()");
    return ((Passthrough_t)g_killServerOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static uint64_t CbufAddText(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                            double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const char* const text = ReadableText(a2);

    if (text && __sync_add_and_fetch(&g_commands, 1) <= kCommandsLogged)
    {
        size_t length = strnlen(text, 200);

        while (length && (text[length - 1] == '\n' || text[length - 1] == '\r'))
            --length;

        T7Log_Write("[Cmd] client %d queued: %.*s", (int)(int32_t)a1, (int)length, text);
    }

    return ((Passthrough_t)g_cbufOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static uint64_t SvShutdown(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                           double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uint32_t slot = g_shutdownNoted;

    if (slot < (uint32_t)kShutdownNotes)
    {
        ShutdownNote& note = g_shutdownNotes[slot];
        const char* const reason = (const char*)a1;
        size_t length = 0;

        while (reason && length + 1 < sizeof(note.reason) && reason[length])
        {
            note.reason[length] = reason[length];
            ++length;
        }

        note.reason[length] = 0;
        note.scriptErrors = g_scriptErrors;

        uintptr_t frame = (uintptr_t)__builtin_frame_address(0);
        const uintptr_t low = frame;

        for (int depth = 0; depth < kShutdownCallers; ++depth)
        {
            note.callers[depth] = 0;

            if (!frame || frame < low || frame - low > 0x200000 || (frame & 7) || !RangeReadable(frame, 16))
                continue;

            note.callers[depth] = *(const uintptr_t*)(frame + 8);
            const uintptr_t next = *(const uintptr_t*)frame;
            frame = next > frame ? next : 0;
        }

        __sync_synchronize();
        g_shutdownNoted = slot + 1;
    }

    return ((Passthrough_t)g_shutdownOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static void ShowShutdownNotes()
{
    const uint32_t noted = g_shutdownNoted;

    for (; g_shutdownShown < noted && g_shutdownShown < (uint32_t)kShutdownNotes; ++g_shutdownShown)
    {
        const ShutdownNote& note = g_shutdownNotes[g_shutdownShown];
        char text[400];
        int used = snprintf(text, sizeof(text), "[Server] shutdown \"%s\" (%u script runtime error(s) by then) - called from",
                            note.reason[0] ? note.reason : "(no reason)", note.scriptErrors);

        for (uintptr_t back : note.callers)
        {
            if (!back || used < 0 || (size_t)used >= sizeof(text))
                break;

            if (back > g_base && back - g_base < 0x1600000)
                used += snprintf(text + used, sizeof(text) - (size_t)used, " %llX", (unsigned long long)(back - g_base));
            else
                used += snprintf(text + used, sizeof(text) - (size_t)used, " ?%llX", (unsigned long long)back);
        }

        T7Log_Write("%s", text);
    }
}

static void WatchKillServer()
{
    if (g_killServerOriginal)
        return;

    const uintptr_t row = g_base + kKillServerRow;

    if (!RangeReadable(row, 32) || *(const uint32_t*)row != kKillServerHash || *(const uintptr_t*)(row + 16) != g_base + kKillServer)
    {
        T7Log_Write("[Log] KillServer() is not where this build expects it - not watched");
        return;
    }

    g_killServerOriginal = (void*)(g_base + kKillServer);
    *(uintptr_t*)(row + 16) = (uintptr_t)KillServer;
}

static uint64_t DefaultEntry(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                             double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uint32_t type = (uint32_t)a1;
    const char* const name = ReadableText(a2);

    if (name)
    {
        uint64_t key = 1469598103934665603ull ^ type;

        for (const char* c = name; *c; ++c)
            key = (key ^ (uint8_t)*c) * 1099511628211ull;

        key |= 1;

        bool fresh = false;

        for (int probe = 0; probe < 64; ++probe)
        {
            volatile uint64_t* const slot = &g_missingSeen[(key + (uint64_t)probe) % (uint64_t)kMissingSlots];

            if (*slot == key)
                break;

            if (*slot == 0 && __sync_bool_compare_and_swap(slot, 0ull, key))
            {
                fresh = true;
                break;
            }

            if (*slot == key)
                break;
        }

        if (fresh)
        {
            const uint32_t count = __sync_add_and_fetch(&g_missingCount, 1);

            if (count <= kMissingLogged)
            {
                const uintptr_t reading = *(const uintptr_t*)(g_base + kReadName);
                const char* const zone = ReadableText(reading);
                char scratch[16];

                T7Log_Write("[Asset] missing %s \"%s\" - the game uses its default (loading %s)",
                            TypeName(type, scratch, sizeof(scratch)), name, zone ? zone : "no fastfile");
            }
            else if (count == kMissingLogged + 1)
            {
                T7Log_Write("[Asset] more than %u missing assets this level - the rest are not logged", kMissingLogged);
            }
        }
    }

    return ((Passthrough_t)g_defaultOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static bool Hook(uintptr_t offset, const uint8_t* bytes, size_t size, Detour* detour, void* hook, void** original)
{
    const uintptr_t at = g_base + offset;

    if (!RangeReadable(at, size) || memcmp((const void*)at, bytes, size) != 0)
        return false;

    Detour_Attach(detour, (uint64_t)at, hook, original);
    return *original != nullptr;
}
}

void T7Log_Install(uintptr_t base)
{
    using namespace T7Log;

    static bool installed = false;

    if (installed || !base)
        return;

    installed = true;
    g_base = base;

    const int fd = sceKernelOpen(kLogPath, SCE_KERNEL_O_WRONLY | SCE_KERNEL_O_CREAT | SCE_KERNEL_O_TRUNC, 0777);

    if (fd >= 0)
        sceKernelClose(fd);

    T7Log_Write("BO3-Customs debug log (Debug build of %s %s)", __DATE__, __TIME__);

    const bool ui = Hook(kUiLuaError, k_uiLuaError, sizeof(k_uiLuaError), &g_uiErrorDetour, (void*)UiLuaError,
                         &g_uiErrorOriginal);
    const bool script = Hook(kScriptError, k_scriptError, sizeof(k_scriptError), &g_scriptErrorDetour,
                             (void*)ScriptError, &g_scriptErrorOriginal);
    const bool error = Hook(kErrorDialog, k_errorDialog, sizeof(k_errorDialog), &g_errorDialogDetour,
                            (void*)ErrorDialog, &g_errorDialogOriginal);
    const bool missing = Hook(kDefaultEntry, k_defaultEntry, sizeof(k_defaultEntry), &g_defaultDetour,
                              (void*)DefaultEntry, &g_defaultOriginal);
    const bool exitLevel = Hook(kExitLevel, k_exitLevel, sizeof(k_exitLevel), &g_exitLevelDetour, (void*)ExitLevel,
                                &g_exitLevelOriginal);
    const bool map = Hook(kMapBuiltin, k_mapBuiltin, sizeof(k_mapBuiltin), &g_mapBuiltinDetour, (void*)MapBuiltin,
                          &g_mapBuiltinOriginal);
    const bool commands = Hook(kCbufAddText, k_cbufAddText, sizeof(k_cbufAddText), &g_cbufDetour, (void*)CbufAddText,
                               &g_cbufOriginal);
    const bool shutdowns = Hook(kSvShutdown, k_svShutdown, sizeof(k_svShutdown), &g_shutdownDetour, (void*)SvShutdown,
                                &g_shutdownOriginal);

    T7Log_Write("[Log] Lua errors %s, script runtime errors %s, engine errors %s, missing assets %s",
                ui ? "logged" : "NOT logged", script ? "logged" : "NOT logged", error ? "logged" : "NOT logged",
                missing ? "logged" : "NOT logged");
    T7Log_Write("[Log] ExitLevel() %s, map() %s, console commands %s, server shutdowns %s, KillServer() at the first level load",
                exitLevel ? "logged" : "NOT logged", map ? "logged" : "NOT logged", commands ? "logged" : "NOT logged",
                shutdowns ? "logged" : "NOT logged");
}

void T7Log_Write(const char* format, ...)
{
    using namespace T7Log;

    if (!g_base || !format)
        return;

    va_list args;
    va_start(args, format);
    WriteV(format, args);
    va_end(args);
}

void T7Log_LuiFile(const char* name)
{
    using namespace T7Log;

    if (!g_base || !name || !name[0])
        return;

    Lock();
    snprintf(g_lastLui, sizeof(g_lastLui), "%s", name);
    snprintf(g_chain[g_chainAt++ % kChainKept], kNameMax, "%s", name);
    Unlock();
}

void T7Log_LuiBuffer(const char* name, uintptr_t rawFile)
{
    using namespace T7Log;

    if (!g_base || !name || !name[0] || !rawFile || !RangeReadable(rawFile, 24))
        return;

    const int32_t length = *(const int32_t*)(rawFile + 8);
    const uintptr_t data = *(const uintptr_t*)(rawFile + 16);

    if (length < 32 || !data || !RangeReadable(data, 4) || memcmp((const void*)data, "\x1bLua", 4) != 0)
        return;

    Lock();

    uint32_t slot = g_luaFileCount;

    for (uint32_t i = 0; i < g_luaFileCount; ++i)
    {
        if (strcmp(g_luaFiles[i].name, name) == 0)
        {
            slot = i;
            break;
        }
    }

    if (slot < kLuaFilesKept)
    {
        snprintf(g_luaFiles[slot].name, kNameMax, "%s", name);
        g_luaFiles[slot].data = data;
        g_luaFiles[slot].length = (uint32_t)length;

        if (slot == g_luaFileCount)
            ++g_luaFileCount;
    }

    Unlock();
}

void T7Log_LuaFailure(uintptr_t L, uint64_t topOffset, const char* chunk, const char* what)
{
    using namespace T7Log;

    if (!g_base || !L)
        return;

    const uint64_t stack = *(const uint64_t*)(L + kStateStack);
    const uint64_t top = *(const uint64_t*)(L + kStateTop);
    const char* message = nullptr;

    if (top >= stack + topOffset + 16)
        message = ((ToLString_t)(g_base + kLuaToLString))(L, top - 16, nullptr);

    T7Log_Write("[Lua] %s failed to %s: %s", chunk ? chunk : "chunk", what ? what : "run",
                message ? message : "(no message)");
}

void T7Log_Zone(const char* name, int32_t allocFlags, int32_t freeFlags)
{
    using namespace T7Log;

    if (g_base)
        ShowShutdownNotes();

    T7Log_Write("[Zones] %s %s (flags 0x%X)", allocFlags ? "load" : "unload", name ? name : "(unnamed)",
                (unsigned)(allocFlags ? allocFlags : freeFlags));
}

void T7Log_NewLevel()
{
    using namespace T7Log;

    if (!g_base)
        return;

    WatchKillServer();

    for (int i = 0; i < kMissingSlots; ++i)
        g_missingSeen[i] = 0;

    g_missingCount = 0;
    g_scriptErrors = 0;
    g_uiErrors = 0;
    g_commands = 0;

    Lock();
    g_luaFileCount = 0;
    Unlock();

    T7Log_Write("[Level] ---- a level is loading ----");
}

#else

void T7Log_Install(uintptr_t)
{
}

void T7Log_Write(const char*, ...)
{
}

void T7Log_LuiFile(const char*)
{
}

void T7Log_LuiBuffer(const char*, uintptr_t)
{
}

void T7Log_LuaFailure(uintptr_t, uint64_t, const char*, const char*)
{
}

void T7Log_Zone(const char*, int32_t, int32_t)
{
}

void T7Log_NewLevel()
{
}

#endif
