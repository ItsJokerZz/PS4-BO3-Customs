#include "headers.hpp"
#include "t7_maps.hpp"
#include "t7_mapimages.hpp"
#include "t7_lua.hpp"

#include <atomic>

#ifdef _DEBUG
extern "C" {
    int sceKernelInstallExceptionHandler(int signum, void (*handler)(int, void*));
    int sceKernelRaiseException(ScePthread thread, int signum);
    int T7Unity_InstallExceptionHandler(int signum, void (*handler)(int, void*));
    int T7Unity_RaiseException(ScePthread thread, int signum);
}
#endif

namespace T7Maps
{
constexpr uintptr_t kFileOpen     = 0xF29960;
constexpr uintptr_t kMapExists    = 0xF10650;
constexpr uintptr_t kIsMapValid   = 0xDECEB0;
constexpr uintptr_t kDlcBitForMap = 0xDC5480;
constexpr uint32_t  kBaseContentBit = 2;
constexpr uintptr_t kMapContentGate = 0xD8FC8F;
constexpr uintptr_t kMapContentBase = 0xD8FCBF;
constexpr uint8_t   kMapContentReturn = 0x06;
constexpr uint8_t   kMapContentJump = 0x2C;
constexpr uintptr_t kLoadXAssets  = 0x85B5C0;
constexpr uintptr_t kCodeTexture  = 0xBD3FD0;
constexpr uintptr_t kBindTexture  = 0xBDE8C0;
constexpr uintptr_t kImageBlack   = 0x73C8A90;
constexpr uintptr_t kImageClear   = 0x73C8AC0;
constexpr uintptr_t kBlockJob     = 0x7F9EA0;
constexpr uintptr_t kReadRing     = 0x57E79D8;
constexpr uintptr_t kZoneFolder   = 0xCD5CB60;
constexpr uintptr_t kSessionModes = 0xBC20268;

constexpr uintptr_t kGdtMapsTable = 0xE8CA90;
constexpr uintptr_t kPathRemap    = 0xF29E50;
constexpr uintptr_t kLinkXAsset   = 0x857A10;
constexpr uintptr_t kLinkXAssetEnd = 0x858234;
constexpr uintptr_t kZonePriority = 0x858DC0;
constexpr uintptr_t kDemoStart    = 0x10E3A10;
constexpr uintptr_t kGetDvarString = 0x57CB50;
constexpr uintptr_t kScrAddString = 0x791870;
constexpr uintptr_t kScrVmState   = 0x3453C90;
constexpr uintptr_t kScriptStrings = 0x33C3848;
constexpr uint32_t  kFsGameHash   = 0x5BF97A95;
constexpr uintptr_t kPlayViewmodelFx = 0x5B8EF0;
constexpr uintptr_t kClientFxIndex = 0x54ACC0;
constexpr uintptr_t kClientFxDefs = 0x1B0B4E0;
constexpr uintptr_t kScrGetConstString = 0x78BD10;
constexpr uintptr_t kScrError     = 0x78BE10;
constexpr uintptr_t kScrErrorText = 0x3448A80;
constexpr uintptr_t kScrErrorBuffer = 0x35852B0;
constexpr int32_t   kClientFxSlots = 1024;
constexpr uintptr_t kTexturesLoaded = 0xA56D90;
constexpr uintptr_t kScrVmTop       = kScrVmState + 0x20;
constexpr uintptr_t kStreamStarted  = 0x8FD66C0;
constexpr uintptr_t kStreamDone     = 0x8FD66BE;
constexpr uintptr_t kStreamPending  = 0xD32AB84;
constexpr uintptr_t kStreamOverride = 0xD3EA37C;
constexpr int32_t   kVarInteger     = 7;
constexpr uint64_t  kTextureWaitUs  = 180000000;
#ifdef _DEBUG
constexpr uintptr_t kStreamLevelLoad = 0x8FD66BD;
constexpr uintptr_t kImagePool       = 0x45863A0;
constexpr uint64_t  kImagePoolSize   = 45979;
constexpr uintptr_t kPartsRequired   = 0x8F17580;
constexpr uintptr_t kPartsWanted     = 0x8F22980;
constexpr uintptr_t kPartsInMemory   = 0x8F0C180;
constexpr int       kPartWords       = 5748;
constexpr uintptr_t kMeshRequired    = 0x8F44580;
constexpr uintptr_t kMeshWanted      = 0x8F50580;
constexpr uintptr_t kMeshInMemory    = 0x8F46580;
constexpr int       kMeshWords       = 1024;
constexpr uintptr_t kSoundLoaded     = 0xD4B147C;
constexpr uintptr_t kSoundSlots      = 0xD48BE60;
constexpr uintptr_t kSoundSlotSize   = 4720;
constexpr int       kSoundSlotCount  = 32;
constexpr uintptr_t kStreamBlobs     = 0x90396C0;
constexpr int       kStreamBlobCount = 350;
constexpr uintptr_t kOpenMenuRow       = 0x6341CE0;
constexpr uintptr_t kCloseMenuRow      = 0x6341D00;
constexpr uintptr_t kFreezeControlsRow = 0x6341E60;
constexpr uintptr_t kUiVisibilityRow   = 0x63424E0;
constexpr uintptr_t kGetPlayersRow     = 0x6CF9450;
constexpr uintptr_t kScrGetInt         = 0x78FC30;
constexpr uintptr_t kGEntities         = 0x66EAFE0;
constexpr uintptr_t kGEntitySize       = 1264;
constexpr uintptr_t kGEntityClient     = 592;
constexpr uintptr_t kClientControls    = 94212;
constexpr uint32_t  kControlsFrozen    = 4;
constexpr uint32_t  kScriptWatchLogs   = 150;
constexpr uint64_t  kHeartbeatUs       = 2000000;
constexpr uint32_t  kHeartbeats        = 90;
constexpr uintptr_t kThreadHandles     = 0xC97ED00;
constexpr uintptr_t kCodeEnd           = 0x1520000;
constexpr int       kStallSignal       = 30;
constexpr int       kStallThreads      = 14;
constexpr uint64_t  kStallUs           = 4000000;
constexpr uint64_t  kStallGapUs        = 5000000;
constexpr uint32_t  kStallRounds       = 3;
constexpr uint32_t  kStallArmFrames    = 120;
constexpr uint64_t  kStallAnswerUs     = 300000;
constexpr int       kStallRawWords     = 64;
constexpr int       kStallStackWords   = 8192;
constexpr int       kStallFrames       = 32;
constexpr int       kStallReturns      = 24;
constexpr uintptr_t kMainReturn        = 0xDF;
constexpr uintptr_t kThreadReturn      = 0xF3C131;
constexpr uintptr_t kRootScanBytes     = 64 * 1024;
constexpr uintptr_t kFrameScanBytes    = 256 * 1024;
constexpr int       kWalkFrames        = 48;
constexpr uintptr_t kKernelSpan        = 32u << 20;
constexpr size_t    kReportBytes       = 256 * 1024;
constexpr uint32_t  kDvarReadsLogged   = 64;
#endif
constexpr uint64_t  kGateLogUs       = 5000000;
constexpr uint64_t  kGateLogSlowUs   = 20000000;

constexpr int32_t kLevelCommonFlag = 0x200;

constexpr int32_t  kScriptParseTree       = 54;
constexpr uint32_t kUsermapZoneFlags      = 0x100;
constexpr uint32_t kUsermapScriptPriority = 1200;

constexpr int    kMaxPackages   = 128;
constexpr int    kMaxFiles      = 1536;
constexpr int    kMaxZoneCall   = 32;
constexpr int    kMaxListed     = 96;
constexpr size_t kNameMax       = 56;
constexpr size_t kListedNameMax = 96;

static const char k_levelCommon[] = "zm_levelcommon";
static const char k_soundFolder[] = "/app0/zone/";

struct Package
{
    char name[kNameMax];
    char folder[224];
    char alternate[224];
    char preview[kListedNameMax];
    bool map;
};

struct Movie
{
    char name[kListedNameMax];
    char file[kListedNameMax];
    char folder[224];
    int package;
};

struct File
{
    char rel[128];
    char path[224];
    int package;
};

struct ZoneInfo
{
    const char* name;
    int32_t allocFlags;
    int32_t freeFlags;
    int32_t allocSlot;
    int32_t freeSlot;
    uint64_t buffer;
    uint64_t bufferSize;
};

using Hook_t = uint64_t (*)(uint64_t, uint64_t, uint64_t, uint64_t, uint64_t, uint64_t,
                            double, double, double, double, double, double, double, double);

static uintptr_t g_base = 0;
static Package   g_packages[kMaxPackages];
static int       g_packageCount = 0;
static File      g_files[kMaxFiles];
static int       g_fileCount = 0;
static Movie     g_movies[64];
static int       g_movieCount = 0;
static int       g_levelCommon = -1;
static bool      g_buildMatched = false;
static int       g_listedMaps = 0;
static bool      g_hooksInstalled = false;
static bool      g_rankLowered = false;

static std::atomic<int32_t> g_listReaders{0};
static std::atomic<bool>    g_listWriting{false};

struct ListReader
{
    ListReader()
    {
        for (;;)
        {
            g_listReaders.fetch_add(1);

            if (!g_listWriting.load())
                return;

            g_listReaders.fetch_sub(1);

            while (g_listWriting.load())
                sceKernelUsleep(100);
        }
    }

    ~ListReader()
    {
        g_listReaders.fetch_sub(1);
    }
};

static void BeginListWrite()
{
    g_listWriting.store(true);

    while (g_listReaders.load() != 0)
        sceKernelUsleep(100);
}

static void EndListWrite()
{
    g_listWriting.store(false);
}

static Detour g_openDetour{};
static void*  g_openOriginal = nullptr;
static Detour g_existsDetour{};
static void*  g_existsOriginal = nullptr;
static Detour g_validDetour{};
static void*  g_validOriginal = nullptr;
static Detour g_dlcBitDetour{};
static void*  g_dlcBitOriginal = nullptr;
static Detour g_loadDetour{};
static void*  g_loadOriginal = nullptr;
static Detour g_blockDetour{};
static void*  g_blockOriginal = nullptr;
static Detour g_codeTextureDetour{};
static void*  g_codeTextureOriginal = nullptr;
static Detour g_mapsTableDetour{};
static void*  g_mapsTableOriginal = nullptr;
static Detour g_pathRemapDetour{};
static void*  g_pathRemapOriginal = nullptr;
static Detour g_linkDetour{};
static void*  g_linkOriginal = nullptr;
static Detour g_priorityDetour{};
static void*  g_priorityOriginal = nullptr;
static Detour g_demoStartDetour{};
static void*  g_demoStartOriginal = nullptr;
static Detour g_getDvarStringDetour{};
static void*  g_getDvarStringOriginal = nullptr;
static Detour g_viewmodelFxDetour{};
static void*  g_viewmodelFxOriginal = nullptr;
static Detour g_texturesDetour{};
static void*  g_texturesOriginal = nullptr;

static std::atomic<bool>     g_usermapLevel{false};
static std::atomic<uint64_t> g_textureWaitStart{0};
static std::atomic<bool>     g_texturesForced{false};
static std::atomic<uint64_t> g_gateLogged{0};
static std::atomic<uint32_t> g_gateLogs{0};
#ifdef _DEBUG
static void*                 g_openMenuOriginal = nullptr;
static void*                 g_closeMenuOriginal = nullptr;
static void*                 g_freezeOriginal = nullptr;
static void*                 g_uiVisibilityOriginal = nullptr;
static void*                 g_getPlayersOriginal = nullptr;
static std::atomic<uint32_t> g_watchLogs{0};
static std::atomic<uint64_t> g_heartbeatAt{0};
static std::atomic<uint32_t> g_heartbeats{0};
static std::atomic<uint32_t> g_serverCalls{0};
static std::atomic<uint32_t> g_clientFrames{0};
static std::atomic<uint64_t> g_levelStart{0};
static std::atomic<uint64_t> g_lastFrameAt{0};
static std::atomic<uint32_t> g_levelFrames{0};
static int32_t               g_lastFrozen[4] = { -1, -1, -1, -1 };

struct StallSample
{
    int64_t  mcontext;
    uint64_t rip;
    uint64_t rsp;
    uint64_t rbp;
    uint64_t words;
    uint64_t raw[kStallRawWords];
    uint64_t stack[kStallStackWords];
};

using InstallHandler_t = int (*)(int, void (*)(int, void*));
using RaiseException_t = int (*)(ScePthread, int);

static const char* const k_stallThreads[kStallThreads] = {
    "0_Main", "2_Backend", "1_Worker0", "1_Worker1", "1_Worker2", "3_Server", "Database",
    "Stream", "StreamAsync", "Sound Mix", "Sound Buffer", "Video Decode", "Sound Decode", "Netchan",
};

static StallSample            g_stallSample;
static std::atomic<int>       g_stallState{0};
static std::atomic<uintptr_t> g_stallTarget{0};
static uintptr_t              g_sprxStart = 0;
static uintptr_t              g_sprxEnd = 0;
static RaiseException_t       g_raiseException = nullptr;
static std::atomic<uintptr_t> g_mainStackProbe{0};
static char                   g_report[kReportBytes];
static std::atomic<size_t>    g_reportUsed{0};
static uint64_t               g_dvarReadKeys[kDvarReadsLogged];
static std::atomic<uint32_t>  g_dvarReads{0};
#endif
static std::atomic<uint64_t> g_scriptLinker{0};

static bool     g_inLevel = false;

static const uint8_t k_fileOpen[]  = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55,
                                       0x41, 0x54, 0x53, 0x48, 0x83, 0xEC, 0x58, 0x48, 0x8B, 0x05 };
static const uint8_t k_mapExists[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x54,
                                       0x53, 0x49, 0x89, 0xFE, 0x4D, 0x85, 0xF6, 0x0F, 0x84 };
static const uint8_t k_loadZones[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                       0x53, 0x48, 0x81, 0xEC, 0xC8, 0x0D, 0x00, 0x00, 0x48, 0x8B, 0x05 };
static const uint8_t k_isMapValid[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                        0x53, 0x50, 0x49, 0x89, 0xFC, 0x49, 0x8B, 0x74, 0x24, 0x50, 0x49, 0x3B,
                                        0x74, 0x24, 0x48 };
static const uint8_t k_dlcBitForMap[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                          0x53, 0x50, 0x49, 0x89, 0xFC, 0x45, 0x31, 0xED, 0x41, 0xBE, 0x00, 0x00,
                                          0x00, 0x00, 0xBB, 0x00, 0x00, 0x00, 0x00, 0x49, 0x8B, 0x74, 0x24, 0x50 };
static const uint8_t k_mapContentGate[] = { 0x31, 0xC0, 0xEB, 0x00, 0x31, 0xC0, 0xEB, 0x02 };
static const uint8_t k_mapContentBase[] = { 0x4C, 0x8B, 0x7D, 0xB8, 0x41, 0x89, 0xC6, 0x45, 0x85, 0xF6, 0x0F, 0x84 };
static const uint8_t k_blockJob[]   = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                        0x53, 0x48, 0x83, 0xE4, 0xE0, 0x48, 0x81, 0xEC, 0x20, 0x12, 0x00, 0x00,
                                        0x48, 0x8B, 0x0D };
static const uint8_t k_codeTexture[] = { 0x41, 0x89, 0xC8, 0x45, 0x89, 0xC1, 0x4B, 0x8D, 0x04, 0x49, 0x80, 0xBC,
                                         0x47, 0x80, 0x17, 0x00, 0x00, 0x00, 0x74, 0x54, 0x44, 0x0F, 0xB7, 0x84,
                                         0x47, 0x82, 0x17, 0x00, 0x00, 0x48, 0x8B, 0x0D };
static const uint8_t k_mapsTable[]   = { 0x55, 0x48, 0x89, 0xE5, 0x53, 0x48, 0x83, 0xEC, 0x18, 0x48, 0x8B, 0x1D };
static const uint8_t k_pathRemap[]   = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                         0x53, 0x48, 0x81, 0xEC, 0xF8, 0x05, 0x00, 0x00, 0x48, 0x8B, 0x05 };
static const uint8_t k_linkXAsset[]   = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                         0x53, 0x48, 0x83, 0xEC, 0x38, 0x48, 0x8B, 0x05 };
static const uint8_t k_zonePriority[] = { 0x55, 0x48, 0x89, 0xE5, 0x53, 0x50, 0x8D, 0x0C, 0xFD, 0x00, 0x00, 0x00,
                                          0x00, 0xB8, 0x1B, 0x01, 0x00, 0x00, 0xBB, 0xE9, 0x03, 0x00, 0x00 };
static const uint8_t k_demoStart[]    = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                         0x53, 0x48, 0x83, 0xE4, 0xE0, 0x48, 0x81, 0xEC, 0x40, 0x0C, 0x00, 0x00 };
static const uint8_t k_getDvarString[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                          0x53, 0x50, 0x41, 0x89, 0xFE, 0x4C, 0x8D, 0x25 };
static const uint8_t k_playViewmodelFx[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                            0x53, 0x48, 0x83, 0xEC, 0x18, 0x48, 0x8B, 0x05 };
static const uint8_t k_texturesLoaded[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x54, 0x53, 0x48,
                                           0x83, 0xEC, 0x10, 0x4C, 0x8B, 0x35 };

struct SignaturePatch
{
    uintptr_t      site;
    const uint8_t* expect;
    size_t         expectLength;
    size_t         offset;
    const char*    what;
};

static const uint8_t k_signatureHeader[] = { 0x85, 0xDB, 0x75, 0x09, 0x83, 0xBD, 0x10, 0xD6, 0xFF, 0xFF, 0x01, 0x74, 0x0A,
                                             0x81, 0x05, 0x51, 0xA4, 0xFE, 0x04, 0x00, 0x40, 0x00, 0x00,
                                             0x48, 0x8D, 0x3D };
static const uint8_t k_signaturePatch[]  = { 0x85, 0xDB, 0x75, 0x06, 0x83, 0x7D, 0x90, 0x01, 0x74, 0x0A,
                                             0x81, 0x05, 0xA6, 0xA0, 0xFE, 0x04, 0x00, 0x40, 0x00, 0x00,
                                             0x0F, 0xB6, 0x05 };
static const uint8_t k_nop10[]           = { 0x66, 0x2E, 0x0F, 0x1F, 0x84, 0x00, 0x00, 0x00, 0x00, 0x00 };

static const SignaturePatch k_signaturePatches[] =
{
    { 0x7FF2A4, k_signatureHeader, sizeof(k_signatureHeader), 13, "zone header signature" },
    { 0x7FF652, k_signaturePatch,  sizeof(k_signaturePatch),  10, "patch file signature" },
};

struct PenaltySite
{
    uintptr_t site;
    uint8_t   opcode[4];
    size_t    operand;
};

static const PenaltySite k_placeholderPenalties[] =
{
    { 0x857D47, { 0x41, 0x8D, 0x8E }, 3 },
    { 0x857D5A, { 0x41, 0x81, 0xC5 }, 3 },
    { 0x857DF9, { 0x41, 0x8D, 0x8C, 0x24 }, 4 },
    { 0x857E13, { 0x41, 0x81, 0xC7 }, 3 },
    { 0x857EC8, { 0x41, 0x8D, 0x96 }, 3 },
    { 0x857EDB, { 0x41, 0x81, 0xC7 }, 3 },
    { 0x857F79, { 0x41, 0x8D, 0x8C, 0x24 }, 4 },
    { 0x857F93, { 0x41, 0x81, 0xC7 }, 3 },
};

constexpr int32_t kPlaceholderPenalty    = -1000;
constexpr int32_t kPlaceholderPenaltyLow = -2000;

static bool EqualsNoCase(const char* a, const char* b)
{
    for (;; ++a, ++b)
    {
        const char x = (*a >= 'A' && *a <= 'Z') ? (char)(*a + 32) : *a;
        const char y = (*b >= 'A' && *b <= 'Z') ? (char)(*b + 32) : *b;

        if (x != y)
            return false;

        if (!x)
            return true;
    }
}

static bool EndsWith(const char* text, const char* tail)
{
    const size_t n = strlen(text);
    const size_t m = strlen(tail);
    return n >= m && strcmp(text + n - m, tail) == 0;
}

static bool IsToken(const char* name)
{
    size_t n = 0;

    for (; name[n]; ++n)
    {
        const char c = name[n];

        if (n >= kNameMax - 1 || !((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
            return false;
    }

    return n > 0;
}

static bool IsZoneFile(const char* name)
{
    return EndsWith(name, ".ff") || EndsWith(name, ".fd") || EndsWith(name, ".xpak") || EndsWith(name, ".sabl") ||
           EndsWith(name, ".sabs");
}

static int ListFolder(const char* folder, bool folders, char names[][kListedNameMax], int capacity)
{
    const int fd = sceKernelOpen(folder, SCE_KERNEL_O_RDONLY | SCE_KERNEL_O_DIRECTORY, 0);

    if (fd < 0)
        return 0;

    static char buffer[0x10000];
    int count = 0;

    for (;;)
    {
        const int n = sceKernelGetdents(fd, buffer, sizeof(buffer));

        if (n <= 0)
            break;

        for (int at = 0; at + 8 <= n;)
        {
            const SceKernelDirent* const entry = (const SceKernelDirent*)(buffer + at);

            if (entry->d_reclen == 0)
                break;

            at += entry->d_reclen;

            if (entry->d_fileno == 0 || entry->d_name[0] == '.')
                continue;

            if ((entry->d_type == SCE_KERNEL_DT_DIR) != folders || strlen(entry->d_name) >= kListedNameMax)
                continue;

            if (count < capacity)
                snprintf(names[count++], kListedNameMax, "%s", entry->d_name);
        }
    }

    sceKernelClose(fd);
    return count;
}

static void AddFile(const char* rel, const char* folder, int package)
{
    for (int i = 0; i < g_fileCount; ++i)
    {
        if (strcmp(g_files[i].rel, rel) == 0)
        {
            return;
        }
    }

    if (g_fileCount >= kMaxFiles)
        return;

    File& file = g_files[g_fileCount++];
    snprintf(file.rel, sizeof(file.rel), "%s", rel);
    snprintf(file.path, sizeof(file.path), "%s/%s", folder, rel);
    file.package = package;
}

static void ScanPackage(const char* folder, int package)
{
    const char* const name = g_packages[package].name;
    static char files[kMaxListed][kListedNameMax];
    static char languages[16][kListedNameMax];

    const int fileCount = ListFolder(folder, false, files, kMaxListed);

    for (int i = 0; i < fileCount; ++i)
    {
        if (IsZoneFile(files[i]) && strstr(files[i], name))
            AddFile(files[i], folder, package);
    }

    char sounds[224];
    snprintf(sounds, sizeof(sounds), "%s/snd", folder);

    const int languageCount = ListFolder(sounds, true, languages, 16);

    for (int l = 0; l < languageCount; ++l)
    {
        char language[224];
        snprintf(language, sizeof(language), "%s/%s", sounds, languages[l]);

        const int bankCount = ListFolder(language, false, files, kMaxListed);

        for (int i = 0; i < bankCount; ++i)
        {
            if (IsZoneFile(files[i]) && strstr(files[i], name))
            {
                char rel[128];
                snprintf(rel, sizeof(rel), "snd/%s/%s", languages[l], files[i]);
                AddFile(rel, folder, package);
            }
        }
    }
}

static int AddPackage(const char* name, bool map, const char* folder, const char* alternate)
{
    if (g_packageCount >= kMaxPackages)
        return -1;

    for (int i = 0; i < g_packageCount; ++i)
    {
        if (EqualsNoCase(name, g_packages[i].name))
            return -1;
    }

    Package& package = g_packages[g_packageCount];
    snprintf(package.name, sizeof(package.name), "%s", name);
    snprintf(package.folder, sizeof(package.folder), "%s", folder);
    snprintf(package.alternate, sizeof(package.alternate), "%s", alternate);
    package.preview[0] = 0;
    package.map = map;
    return g_packageCount++;
}

static bool FindPackageFile(const Package& package, const char* rel, char* out, size_t size)
{
    SceKernelStat st;
    snprintf(out, size, "%s/%s", package.folder, rel);

    if (sceKernelStat(out, &st) == 0)
        return true;

    if (!package.alternate[0])
        return false;

    snprintf(out, size, "%s/%s", package.alternate, rel);
    return sceKernelStat(out, &st) == 0;
}

static void ScanUsermaps(const char* data)
{
    char root[224];
    snprintf(root, sizeof(root), "%s/usermaps", data);

    static char folders[kMaxPackages][kListedNameMax];
    const int count = ListFolder(root, true, folders, kMaxPackages);

    for (int i = 0; i < count; ++i)
    {
        char folder[224];
        char zoneFolder[224];
        char zone[256];
        SceKernelStat st;

        snprintf(folder, sizeof(folder), "%s/%s", root, folders[i]);
        snprintf(zoneFolder, sizeof(zoneFolder), "%s/zone", folder);

        const bool valid = IsToken(folders[i]);
        snprintf(zone, sizeof(zone), "%s/%s.ff", folder, folders[i]);
        const bool atRoot = valid && sceKernelStat(zone, &st) == 0;
        snprintf(zone, sizeof(zone), "%s/%s.ff", zoneFolder, folders[i]);
        const bool inZone = valid && !atRoot && sceKernelStat(zone, &st) == 0;

        if (!atRoot && !inZone)
            continue;

        const int package = atRoot ? AddPackage(folders[i], true, folder, zoneFolder)
                                   : AddPackage(folders[i], true, zoneFolder, folder);

        if (package < 0)
            continue;

        ScanPackage(g_packages[package].folder, package);
        ScanPackage(g_packages[package].alternate, package);
    }
}

static void ScanSharedZones(const char* data)
{
    char folder[224];
    snprintf(folder, sizeof(folder), "%s/zone", data);

    static char files[kMaxListed][kListedNameMax];
    const int count = ListFolder(folder, false, files, kMaxListed);

    for (int i = 0; i < count; ++i)
    {
        if (!EndsWith(files[i], ".ff"))
            continue;

        char zone[kListedNameMax];
        snprintf(zone, sizeof(zone), "%s", files[i]);
        zone[strlen(zone) - 3] = 0;

        if (!IsToken(zone))
            continue;

        bool localized = false;

        if (strlen(zone) > 3 && zone[2] == '_')
        {
            char original[kListedNameMax];
            snprintf(original, sizeof(original), "%s.ff", zone + 3);

            for (int j = 0; j < count && !localized; ++j)
                localized = strcmp(files[j], original) == 0;
        }

        if (localized)
            continue;

        const int package = AddPackage(zone, false, folder, "");

        if (package < 0)
            continue;

        ScanPackage(folder, package);

        if (strcmp(zone, k_levelCommon) == 0)
            g_levelCommon = package;
    }
}

static const char* Relative(const char* path)
{
    const char* const folder = (const char*)(g_base + kZoneFolder);
    const size_t n = strnlen(folder, 260);

    if (n && n < 260 && strncmp(path, folder, n) == 0 && (path[n] == '/' || path[n] == '\\'))
        return path + n + 1;

    if (strncmp(path, k_soundFolder, sizeof(k_soundFolder) - 1) == 0)
        return path + sizeof(k_soundFolder) - 1;

    return nullptr;
}

static bool LanguageShape(const char* rel, char* out, size_t size)
{
    static const char* const k_languages[] = { "all", "bp", "ct", "cz", "du", "ea", "en", "es", "fr",
                                               "ge", "it", "ja", "ko", "nl", "pl", "po", "pt", "ru", "sp" };
    constexpr size_t kCount = sizeof(k_languages) / sizeof(k_languages[0]);

    if (strncmp(rel, "snd/", 4) == 0)
    {
        const char* const slash = strchr(rel + 4, '/');

        if (slash == nullptr)
            return false;

        const size_t length = (size_t)(slash - (rel + 4));

        for (size_t i = 0; i < kCount; ++i)
        {
            if (strlen(k_languages[i]) != length || strncmp(rel + 4, k_languages[i], length) != 0)
                continue;

            char tail[128];
            snprintf(tail, sizeof(tail), "%s", slash + 1);
            char* const dot = strchr(tail, '.');

            if (dot == nullptr)
                return false;

            const char* const rest = strchr(dot + 1, '.');

            if (rest == nullptr)
                return false;

            *dot = 0;
            snprintf(out, size, "snd/*/%s.*%s", tail, rest);
            return true;
        }

        return false;
    }

    const char* const underscore = strchr(rel, '_');

    if (underscore == nullptr)
        return false;

    const size_t length = (size_t)(underscore - rel);

    for (size_t i = 0; i < kCount; ++i)
    {
        if (strlen(k_languages[i]) == length && strncmp(rel, k_languages[i], length) == 0)
        {
            snprintf(out, size, "*%s", underscore);
            return true;
        }
    }

    return false;
}

static int LanguageCompanion(const char* rel)
{
    char wanted[128];

    if (!LanguageShape(rel, wanted, sizeof(wanted)))
        return -1;

    for (int i = 0; i < g_fileCount; ++i)
    {
        char have[128];

        if (LanguageShape(g_files[i].rel, have, sizeof(have)) && strcmp(have, wanted) == 0)
            return i;
    }

    return -1;
}

static uint64_t FileOpen_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                           double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const char* const path = (const char*)a1;

    if (path)
    {
        const char* const rel = Relative(path);

        if (rel)
        {
            ListReader reading;
            int found = -1;

            for (int i = 0; i < g_fileCount; ++i)
            {
                if (strcmp(g_files[i].rel, rel) == 0)
                {
                    found = i;
                    break;
                }
            }

            if (found < 0)
                found = LanguageCompanion(rel);

            if (found >= 0)
            {
                a1 = (uint64_t)g_files[found].path;
            }
        }
    }

    return ((Hook_t)g_openOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static int MapPackage(const char* name)
{
    for (int i = 0; i < g_packageCount; ++i)
    {
        if (g_packages[i].map && EqualsNoCase(name, g_packages[i].name))
            return i;
    }

    return -1;
}

static uint64_t MapExists_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                            double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const char* const map = (const char*)a1;

    if (map && *map)
    {
        ListReader reading;
        const int package = MapPackage(map);

        if (package >= 0)
            return 1;
    }

    return ((Hook_t)g_existsOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static uint64_t IsMapValid_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                             double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uint64_t at = *(uint64_t*)(a1 + 80);

    if (at < *(uint64_t*)(a1 + 72) && (*(uint32_t*)at & 0xF) == 4 && *(uint64_t*)(at + 8))
    {
        const uint64_t string = *(uint64_t*)(at + 8);
        ListReader reading;
        const int package = (*(uint64_t*)(string + 8) & 0x3FFFFFFFFFFFFFFFull) < kNameMax
                                ? MapPackage((const char*)(string + 20))
                                : -1;

        if (package >= 0)
        {
            const uint64_t top = *(uint64_t*)(a1 + 72);
            *(uint32_t*)top = 1;
            *(uint32_t*)(top + 8) = 1;
            *(uint64_t*)(a1 + 72) = top + 16;
            return 1;
        }
    }

    return ((Hook_t)g_validOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static uint64_t DlcBitForMap_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                               double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uint64_t at = *(uint64_t*)(a1 + 80);

    if (at < *(uint64_t*)(a1 + 72) && (*(uint32_t*)at & 0xF) == 4 && *(uint64_t*)(at + 8))
    {
        const uint64_t string = *(uint64_t*)(at + 8);
        ListReader reading;
        const int package = (*(uint64_t*)(string + 8) & 0x3FFFFFFFFFFFFFFFull) < kNameMax
                                ? MapPackage((const char*)(string + 20))
                                : -1;

        if (package >= 0)
        {
            const uint64_t top = *(uint64_t*)(a1 + 72);
            *(float*)(top + 8) = (float)kBaseContentBit;
            *(uint32_t*)top = 3;
            *(uint64_t*)(a1 + 72) = top + 16;
            return 1;
        }
    }

    return ((Hook_t)g_dlcBitOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static uint64_t LinkXAsset_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                             double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    bool owner = false;

    if (g_usermapLevel.load(std::memory_order_relaxed) && a1 && *(const int32_t*)a1 == kScriptParseTree)
    {
        uint64_t idle = 0;
        owner = g_scriptLinker.compare_exchange_strong(idle, (uint64_t)scePthreadSelf(), std::memory_order_acq_rel);
    }

    const uint64_t result = ((Hook_t)g_linkOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);

    if (owner)
        g_scriptLinker.store(0, std::memory_order_release);

    return result;
}

static uint64_t ZonePriority_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                               double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uint64_t priority = ((Hook_t)g_priorityOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);

    if ((uint32_t)a1 != kUsermapZoneFlags || g_scriptLinker.load(std::memory_order_acquire) == 0)
        return priority;

    const uintptr_t from = (uintptr_t)__builtin_return_address(0) - g_base;

    if (from <= kLinkXAsset || from >= kLinkXAssetEnd ||
        g_scriptLinker.load(std::memory_order_relaxed) != (uint64_t)scePthreadSelf())
        return priority;

    return kUsermapScriptPriority;
}

static bool AsksForFsGame(uint32_t inst)
{
    if (inst > 1)
        return false;

    const uintptr_t vm = g_base + kScrVmState + 0x8A38 * (uintptr_t)inst;

    if (*(const uint32_t*)(vm + 0x34) < 1)
        return false;

    const uintptr_t top = *(const uintptr_t*)(vm + 0x20);

    if (!top)
        return false;

    const uint32_t value = *(const uint32_t*)top;
    const uint32_t type = *(const uint32_t*)(top + 8);

    if (type == 5 || type == 7)
        return value == kFsGameHash;

    if (type != 2 || !value)
        return false;

    const uintptr_t table = *(const uintptr_t*)(g_base + kScriptStrings);
    return table && EqualsNoCase((const char*)(table + 28 * (uintptr_t)value + 4), "fs_game");
}

#ifdef _DEBUG
static void DescribeScriptValue(uintptr_t slot, char* out, size_t size)
{
    if (!slot || !RangeReadable(slot, 12))
    {
        snprintf(out, size, "(nothing)");
        return;
    }

    const uint32_t value = *(const uint32_t*)slot;
    const uint32_t type = *(const uint32_t*)(slot + 8);
    const uintptr_t table = *(const uintptr_t*)(g_base + kScriptStrings);

    if (type == 2 && value && table)
        snprintf(out, size, "\"%.120s\"", (const char*)(table + 28 * (uintptr_t)value + 4));
    else if (type == 5 || type == 7)
        snprintf(out, size, "hash %08X", value);
    else
        snprintf(out, size, "type %u value %u", type, value);
}

static bool FirstDvarRead(uint64_t key)
{
    const uint32_t count = g_dvarReads.load(std::memory_order_acquire);

    for (uint32_t i = 0; i < count && i < kDvarReadsLogged; ++i)
    {
        if (g_dvarReadKeys[i] == key)
            return false;
    }

    if (count >= kDvarReadsLogged)
        return false;

    g_dvarReadKeys[count] = key;
    g_dvarReads.store(count + 1, std::memory_order_release);
    return true;
}
#endif

static uint64_t GetDvarString_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                                double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
#ifdef _DEBUG
    const uint32_t inst = (uint32_t)a1;
    const uintptr_t vm = g_base + kScrVmState + 0x8A38 * (uintptr_t)(inst <= 1 ? inst : 0);
    const uintptr_t argument = inst <= 1 && *(const uint32_t*)(vm + 0x34) >= 1 ? *(const uintptr_t*)(vm + 0x20) : 0;
    const bool watch = g_usermapLevel.load(std::memory_order_relaxed) && argument &&
                       FirstDvarRead(((uint64_t)inst << 40) | ((uint64_t)*(const uint32_t*)(argument + 8) << 32) |
                                     *(const uint32_t*)argument);
    char name[160] = "";

    if (watch)
        DescribeScriptValue(argument, name, sizeof(name));
#endif

    if (g_usermapLevel.load(std::memory_order_relaxed) && AsksForFsGame((uint32_t)a1))
    {
        ((void (*)(uint32_t, const char*))(g_base + kScrAddString))((uint32_t)a1, "usermaps");
#ifdef _DEBUG
        if (watch)
            T7Log_Write("[Dvar] %s script read GetDvarString(%s) = \"usermaps\" (answered by BO3-Customs)", inst == 1 ? "client" : "server",
                        name);
#endif
        return 0;
    }

    const uint64_t result = ((Hook_t)g_getDvarStringOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);

#ifdef _DEBUG
    if (watch)
    {
        char value[160];
        DescribeScriptValue(*(const uintptr_t*)(vm + 0x20), value, sizeof(value));
        T7Log_Write("[Dvar] %s script read GetDvarString(%s) = %s", inst == 1 ? "client" : "server", name, value);
    }
#endif

    return result;
}

static void ScriptError(uint32_t inst, const char* text)
{
    uint64_t* const pending = (uint64_t*)(g_base + kScrErrorText) + 14 * (uintptr_t)inst + 2;

    if (!*pending)
    {
        char* const buffer = (char*)(g_base + kScrErrorBuffer);
        snprintf(buffer, 1024, "%s", text);
        *pending = (uint64_t)buffer;
    }

    *(uint8_t*)(g_base + kScrVmState + 0x8A38 * (uintptr_t)inst + 42) = 0;
    ((void (*)(uint32_t))(g_base + kScrError))(inst);
}

static uint64_t PlayViewmodelFx_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                                  double x0, double x1, double x2, double x3, double x4, double x5, double x6,
                                  double x7)
{
    const uint32_t inst = (uint32_t)a1;
    const uint32_t id = ((uint32_t (*)(uint32_t, uint32_t))(g_base + kScrGetConstString))(inst, 1);
    const uintptr_t strings = *(const uintptr_t*)(g_base + kScriptStrings);
    const char* const name = id && strings ? (const char*)(strings + 28 * (uintptr_t)id + 4) : nullptr;
    const int32_t index = ((int32_t (*)(const char*))(g_base + kClientFxIndex))(name);

    if (index <= 0 || index >= kClientFxSlots || !*(const uint64_t*)(g_base + kClientFxDefs + 16 * (uintptr_t)index))
    {
        char text[256];
        snprintf(text, sizeof(text), "PlayViewmodelFX %s is not precached by the linker", name ? name : "");
        ScriptError(inst, text);
        return 0;
    }

    return ((Hook_t)g_viewmodelFxOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

#ifdef _DEBUG
static void Appendf(char* out, size_t size, size_t* used, const char* format, ...)
{
    if (*used + 1 >= size)
        return;

    va_list args;
    va_start(args, format);
    const int wrote = vsnprintf(out + *used, size - *used, format, args);
    va_end(args);

    if (wrote > 0)
        *used = *used + (size_t)wrote < size ? *used + (size_t)wrote : size - 1;
}
#endif

static void LogStreamGate(uint64_t waited, bool details)
{
#ifdef _DEBUG
    const uint32_t* const required = (const uint32_t*)(g_base + kPartsRequired);
    const uint32_t* const wanted = (const uint32_t*)(g_base + kPartsWanted);
    const uint32_t* const inMemory = (const uint32_t*)(g_base + kPartsInMemory);
    const uintptr_t pool = *(const uintptr_t*)(g_base + kImagePool);
    const bool poolReadable = pool && RangeReadable(pool, kImagePoolSize * 304);
    uint32_t requiredParts = 0;
    uint32_t requiredLoaded = 0;
    uint32_t wantedParts = 0;
    uint32_t wantedLoaded = 0;
    uint64_t requiredBytes = 0;
    uint64_t loadedBytes = 0;
    uint32_t missing[65] = {};
    uint32_t shown[12];
    int shownCount = 0;

    for (int w = 0; w < kPartWords; ++w)
    {
        requiredParts += (uint32_t)__builtin_popcount(required[w]);
        requiredLoaded += (uint32_t)__builtin_popcount(required[w] & inMemory[w]);
        wantedParts += (uint32_t)__builtin_popcount(wanted[w]);
        wantedLoaded += (uint32_t)__builtin_popcount(wanted[w] & inMemory[w]);

        for (uint32_t bits = poolReadable ? required[w] : 0; bits; bits &= bits - 1)
        {
            const uint32_t bit = (uint32_t)w * 32 + (uint32_t)__builtin_ctz(bits);
            const uint64_t image = bit >> 2;
            const uint64_t part = bit & 3;

            if (image >= kImagePoolSize)
                continue;

            const uintptr_t entry = pool + 304 * image;
            const uint64_t size = *(const uint64_t*)(entry + 24 + 40 * part);
            const uint64_t flags = *(const uint64_t*)(entry + 32 + 40 * part);

            requiredBytes += size;

            if (inMemory[w] & (1u << (bit & 31)))
            {
                loadedBytes += size;
                continue;
            }

            ++missing[(flags & 0x80) ? (flags & 0x3F) : 64];

            if (shownCount < 12)
                shown[shownCount++] = bit;
        }
    }

    const uint32_t* const meshNeed = (const uint32_t*)(g_base + kMeshRequired);
    const uint32_t* const meshWant = (const uint32_t*)(g_base + kMeshWanted);
    const uint32_t* const meshIn = (const uint32_t*)(g_base + kMeshInMemory);
    uint32_t meshRequired = 0;
    uint32_t meshRequiredLoaded = 0;
    uint32_t meshWanted = 0;
    uint32_t meshWantedLoaded = 0;

    for (int w = 0; w < kMeshWords; ++w)
    {
        meshRequired += (uint32_t)__builtin_popcount(meshNeed[w]);
        meshRequiredLoaded += (uint32_t)__builtin_popcount(meshNeed[w] & meshIn[w]);
        meshWanted += (uint32_t)__builtin_popcount(meshWant[w]);
        meshWantedLoaded += (uint32_t)__builtin_popcount(meshWant[w] & meshIn[w]);
    }

    uint32_t blobCount[5] = {};
    uint32_t blobLoaded[5] = {};
    uint64_t blobBytes[5] = {};
    uint64_t blobLoadedBytes[5] = {};

    for (int i = 0; i < kStreamBlobCount; ++i)
    {
        const uintptr_t entry = g_base + kStreamBlobs + 56 * (uintptr_t)i;
        const uint32_t state = *(const uint32_t*)(entry + 32);

        if (!state)
            continue;

        const uint32_t category = *(const uint8_t*)(entry + 37) < 5 ? *(const uint8_t*)(entry + 37) : 3;
        const uintptr_t item = *(const uintptr_t*)(entry + 48);
        const uint64_t bytes = item >= 0x10000 && item < 0x800000000000ull ? *(const uint32_t*)(item + 52) : 0;

        ++blobCount[category];
        blobBytes[category] += bytes;

        if (state >= 2)
        {
            ++blobLoaded[category];
            blobLoadedBytes[category] += bytes;
        }
    }

    static const char* const k_blobNames[5] = { "probe", "volume", "sun shadow tree", "other", "sky" };
    char text[1400];
    size_t used = 0;

    Appendf(text, sizeof(text), &used, "[Stream] %llu s: level load %u, started %u, done %u | required image parts %u of %u in memory "
            "(%llu of %llu MB), wanted %u of %u | required mesh parts %u of %u, wanted %u of %u | sound banks %.3f loaded | blobs:",
            (unsigned long long)(waited / 1000000), *(const uint8_t*)(g_base + kStreamLevelLoad),
            *(const uint8_t*)(g_base + kStreamStarted), *(const uint8_t*)(g_base + kStreamDone), requiredLoaded,
            requiredParts, (unsigned long long)(loadedBytes >> 20), (unsigned long long)(requiredBytes >> 20), wantedLoaded,
            wantedParts, meshRequiredLoaded, meshRequired, meshWantedLoaded, meshWanted,
            (double)*(const float*)(g_base + kSoundLoaded));

    for (int c = 0; c < 5; ++c)
    {
        if (blobCount[c])
            Appendf(text, sizeof(text), &used, " %s %u of %u (%llu of %llu MB)", k_blobNames[c], blobLoaded[c], blobCount[c],
                    (unsigned long long)(blobLoadedBytes[c] >> 20), (unsigned long long)(blobBytes[c] >> 20));
    }

    T7Log_Write("%s", text);

    used = 0;
    Appendf(text, sizeof(text), &used, "[Stream]   required parts not in memory, by xpak:");

    for (int x = 0; x < 64; ++x)
    {
        if (missing[x])
            Appendf(text, sizeof(text), &used, " %d x%u", x, missing[x]);
    }

    if (missing[64])
        Appendf(text, sizeof(text), &used, " none x%u", missing[64]);

    if (requiredLoaded < requiredParts)
        T7Log_Write("%s", text);

    used = 0;
    Appendf(text, sizeof(text), &used, "[Stream]   sound bank slots:");

    for (int i = 0; i < kSoundSlotCount; ++i)
    {
        const uintptr_t slot = g_base + kSoundSlots + kSoundSlotSize * (uintptr_t)i;

        if (*(const uint64_t*)slot)
            Appendf(text, sizeof(text), &used, " [%d] state %u, %d/%d", i, *(const uint32_t*)(slot + 4716),
                    *(const int32_t*)(slot + 4680), *(const int32_t*)(slot + 4684));
    }

    T7Log_Write("%s", text);

    if (!details)
        return;

    for (int i = 0; i < shownCount; ++i)
    {
        const uint64_t image = shown[i] >> 2;
        const uint64_t part = shown[i] & 3;
        const uintptr_t entry = pool + 304 * image;
        const char* const name = *(const char* const*)(entry + 288);
        const uint64_t flags = *(const uint64_t*)(entry + 32 + 40 * part);

        T7Log_Write("[Stream]   not in memory: image %llu '%s' part %llu, %llu bytes, %s %llu at offset 0x%llX, key %llX",
                    (unsigned long long)image, name && RangeReadable((uintptr_t)name, 1) ? name : "?", (unsigned long long)part,
                    (unsigned long long)*(const uint64_t*)(entry + 24 + 40 * part), (flags & 0x80) ? "xpak" : "NOT IN AN XPAK, slot",
                    (unsigned long long)(flags & 0x3F), (unsigned long long)*(const uint64_t*)(entry + 16 + 40 * part),
                    (unsigned long long)*(const uint64_t*)(entry + 8 + 40 * part));
    }
#else
    (void)waited;
    (void)details;
#endif
}

#ifdef _DEBUG
static const char* ScriptStringArgument(uint32_t index)
{
    const uint32_t id = ((uint32_t (*)(uint32_t, uint32_t))(g_base + kScrGetConstString))(0, index);
    const uintptr_t strings = *(const uintptr_t*)(g_base + kScriptStrings);
    return id && strings ? (const char*)(strings + 28 * (uintptr_t)id + 4) : "?";
}

static int PlayerFrozen(uint32_t entity)
{
    if (entity >= 4)
        return -1;

    const uintptr_t client = *(const uintptr_t*)(g_base + kGEntities + kGEntitySize * entity + kGEntityClient);

    if (!client || !RangeReadable(client + kClientControls, 4))
        return -1;

    return (*(const uint32_t*)(client + kClientControls) & kControlsFrozen) ? 1 : 0;
}

static const char* FrozenText(int frozen)
{
    return frozen == 1 ? "frozen" : frozen == 0 ? "free to move" : "not in the game";
}

static bool WatchScripts()
{
    return g_usermapLevel.load(std::memory_order_relaxed) &&
           g_watchLogs.fetch_add(1, std::memory_order_relaxed) < kScriptWatchLogs;
}

static uint64_t OpenMenu_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6, double x0,
                           double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    if (WatchScripts())
        T7Log_Write("[Script] OpenMenu \"%s\" for player %u", ScriptStringArgument(0), (uint32_t)a2);

    return ((Hook_t)g_openMenuOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static uint64_t CloseMenu_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6, double x0,
                            double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    if (WatchScripts())
        T7Log_Write("[Script] CloseMenu \"%s\" for player %u", ScriptStringArgument(0), (uint32_t)a2);

    return ((Hook_t)g_closeMenuOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static uint64_t UiVisibility_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6, double x0,
                               double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    if (WatchScripts())
        T7Log_Write("[Script] SetClientUIVisibilityFlag \"%s\" %d for player %u", ScriptStringArgument(0),
                    ((int32_t (*)(uint32_t, uint32_t))(g_base + kScrGetInt))(0, 1), (uint32_t)a2);

    return ((Hook_t)g_uiVisibilityOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static uint64_t FreezeControls_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                                 double x0, double x1, double x2, double x3, double x4, double x5, double x6,
                                 double x7)
{
    const uint64_t result = ((Hook_t)g_freezeOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
    const uint32_t entity = (uint32_t)a2;

    if (entity < 4 && g_usermapLevel.load(std::memory_order_relaxed))
    {
        const int frozen = PlayerFrozen(entity);

        if (frozen != g_lastFrozen[entity] && WatchScripts())
        {
            g_lastFrozen[entity] = frozen;
            T7Log_Write("[Script] FreezeControls: player %u is now %s", entity, FrozenText(frozen));
        }
    }

    return result;
}

static uint64_t GetPlayers_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6, double x0,
                             double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    g_serverCalls.fetch_add(1, std::memory_order_relaxed);
    return ((Hook_t)g_getPlayersOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static void CountBlobs(uint32_t count[5], uint32_t loaded[5])
{
    for (int i = 0; i < kStreamBlobCount; ++i)
    {
        const uintptr_t entry = g_base + kStreamBlobs + 56 * (uintptr_t)i;
        const uint32_t state = *(const uint32_t*)(entry + 32);

        if (!state)
            continue;

        const uint32_t category = *(const uint8_t*)(entry + 37) < 5 ? *(const uint8_t*)(entry + 37) : 3;
        ++count[category];

        if (state >= 2)
            ++loaded[category];
    }
}

static void StallSignal(int, void* context)
{
    if (!context || (uintptr_t)scePthreadSelf() != g_stallTarget.load(std::memory_order_acquire))
        return;

    int expected = 1;

    if (!g_stallState.compare_exchange_strong(expected, 3, std::memory_order_acq_rel))
        return;

    const uint64_t* const words = (const uint64_t*)context;
    StallSample& sample = g_stallSample;
    memcpy(sample.raw, words, sizeof(sample.raw));
    sample.mcontext = -1;
    sample.words = 0;

    for (int i = 21; i + 3 < kStallRawWords; ++i)
    {
        if (words[i] == 0x43 && words[i + 3] == 0x3B)
        {
            sample.mcontext = i - 21;
            break;
        }
    }

    if (sample.mcontext >= 0)
    {
        sample.rip = words[sample.mcontext + 20];
        sample.rbp = words[sample.mcontext + 9];
        sample.rsp = words[sample.mcontext + 23];

        const uintptr_t rsp = sample.rsp & ~(uintptr_t)7;
        SceKernelVirtualQueryInfo info;

        if (rsp >= 0x10000 && rsp < 0x800000000000ull && sceKernelVirtualQuery((void*)rsp, 0, &info, sizeof(info)) >= 0 &&
            (info.protection & 1) && (uintptr_t)info.start <= rsp && rsp < (uintptr_t)info.end)
        {
            size_t bytes = (uintptr_t)info.end - rsp;

            if (bytes > sizeof(sample.stack))
                bytes = sizeof(sample.stack);

            memcpy(sample.stack, (const void*)rsp, bytes);
            sample.words = bytes / 8;
        }
    }

    g_stallState.store(2, std::memory_order_release);
}

static bool CallPrecedes(uint64_t value)
{
    if (value < g_base + 16 || value >= g_base + kCodeEnd)
        return false;

    const uint8_t* const p = (const uint8_t*)value;
    return p[-5] == 0xE8 || (p[-2] == 0xFF && ((p[-1] & 0xF8) == 0xD0 || (p[-1] & 0xF8) == 0x10)) ||
           (p[-3] == 0xFF && ((p[-2] & 0xF8) == 0x50 || p[-2] == 0x14)) || (p[-4] == 0xFF && p[-3] == 0x54) ||
           (p[-6] == 0xFF && ((p[-5] & 0xF8) == 0x90 || p[-5] == 0x15)) || (p[-7] == 0xFF && p[-6] == 0x94);
}

static void AppendAddress(char* out, size_t size, size_t* used, uint64_t value)
{
    SceKernelVirtualQueryInfo info;

    if (value >= g_base && value < g_base + kCodeEnd)
        Appendf(out, size, used, " %llX", (unsigned long long)(value - g_base));
    else if (value >= g_sprxStart && value < g_sprxEnd)
        Appendf(out, size, used, " sprx+%llX", (unsigned long long)(value - g_sprxStart));
    else if (value >= 0x10000 && value < 0x800000000000ull && sceKernelVirtualQuery((void*)value, 0, &info, sizeof(info)) >= 0 &&
             (uintptr_t)info.start <= value && value < (uintptr_t)info.end)
        Appendf(out, size, used, " %.32s+%llX", info.name, (unsigned long long)(value - (uintptr_t)info.start));
    else
        Appendf(out, size, used, " ?%llX", (unsigned long long)value);
}

static void ReportLine(const char* format, ...)
{
    char line[2048];
    const uint64_t now = sceKernelGetProcessTime();
    const int stamp = snprintf(line, sizeof(line), "[%02u:%02u.%03u] ", (unsigned)(now / 60000000ull), (unsigned)(now / 1000000ull % 60),
                               (unsigned)(now / 1000ull % 1000));

    if (stamp <= 0)
        return;

    const size_t room = sizeof(line) - (size_t)stamp - 1;
    va_list args;
    va_start(args, format);
    const int body = vsnprintf(line + stamp, room, format, args);
    va_end(args);

    if (body < 0)
        return;

    size_t length = (size_t)stamp + ((size_t)body < room ? (size_t)body : room - 1);
    line[length++] = '\n';

    const size_t at = g_reportUsed.load(std::memory_order_relaxed);

    if (at + length > sizeof(g_report))
        return;

    memcpy(g_report + at, line, length);
    g_reportUsed.store(at + length, std::memory_order_release);
}

static void RaiseThread()
{
    const ScePthread self = scePthreadSelf();
    scePthreadSetprio(self, SCE_KERNEL_PRIO_FIFO_HIGHEST);

    if (scePthreadSetaffinity(self, 0x7F) != 0)
        scePthreadSetaffinity(self, 0x3F);
}

static void LogStallSample(const char* name)
{
    const StallSample& sample = g_stallSample;
    char line[1900];
    size_t used = 0;

    if (sample.mcontext < 0)
    {
        Appendf(line, sizeof(line), &used, "[Stall] %s: no registers found in its context; first words:", name);

        for (int i = 0; i < kStallRawWords; ++i)
            Appendf(line, sizeof(line), &used, " %llX", (unsigned long long)sample.raw[i]);

        ReportLine("%s", line);
        return;
    }

    Appendf(line, sizeof(line), &used, "[Stall] %s: at", name);
    AppendAddress(line, sizeof(line), &used, sample.rip);
    Appendf(line, sizeof(line), &used, " (rsp %llX, %llu KB of stack read); frame chain:", (unsigned long long)sample.rsp,
            (unsigned long long)(sample.words / 128));

    const uintptr_t rsp = sample.rsp & ~(uintptr_t)7;
    uintptr_t rbp = sample.rbp;

    for (int frame = 0; frame < kStallFrames; ++frame)
    {
        if (rbp < rsp || (rbp & 7) || (rbp - rsp) / 8 + 1 >= sample.words)
            break;

        const uint64_t at = (rbp - rsp) / 8;
        AppendAddress(line, sizeof(line), &used, sample.stack[at + 1]);

        if (sample.stack[at] <= rbp)
            break;

        rbp = sample.stack[at];
    }

    ReportLine("%s", line);

    used = 0;
    Appendf(line, sizeof(line), &used, "[Stall] %s: game calls on its stack (return@offset):", name);
    int shown = 0;

    for (uint64_t i = 0; i < sample.words && shown < kStallReturns; ++i)
    {
        if (!CallPrecedes(sample.stack[i]))
            continue;

        Appendf(line, sizeof(line), &used, " %llX@%llX", (unsigned long long)(sample.stack[i] - g_base), (unsigned long long)(i * 8));
        ++shown;
    }

    ReportLine("%s", line);
}

static bool ThreadStack(int index, uintptr_t thread, uintptr_t* low, uintptr_t* high)
{
    ScePthreadAttr attr;
    void* address = nullptr;
    size_t size = 0;

    if (scePthreadAttrInit(&attr) == 0)
    {
        if (scePthreadAttrGet((ScePthread)thread, &attr) != 0 || scePthreadAttrGetstack(&attr, &address, &size) != 0)
        {
            address = nullptr;
            size = 0;
        }

        scePthreadAttrDestroy(&attr);
    }

    const uintptr_t start = (uintptr_t)address;
    uintptr_t probe = start && size ? start + size - 8 : 0;
    const uintptr_t mainProbe = index == 0 ? g_mainStackProbe.load(std::memory_order_relaxed) : 0;

    if (mainProbe && (!probe || mainProbe < start || mainProbe >= start + size))
        probe = mainProbe;

    SceKernelVirtualQueryInfo info;

    if (!probe || sceKernelVirtualQuery((void*)probe, 0, &info, sizeof(info)) < 0 || !(info.protection & 1))
        return false;

    *low = (uintptr_t)info.start;
    *high = (uintptr_t)info.end;

    if (probe != mainProbe)
    {
        if (start > *low)
            *low = start;

        if (start + size < *high)
            *high = start + size;
    }

    return *high > *low + 64;
}

static bool InSprx(uintptr_t value)
{
    return g_sprxStart && value >= g_sprxStart && value < g_sprxEnd;
}

static uintptr_t FindChild(uintptr_t frame, uintptr_t low, uintptr_t high, bool foreign)
{
    const uintptr_t stop = frame - low > kFrameScanBytes ? frame - kFrameScanBytes : low;

    for (uintptr_t slot = frame - 16; slot >= stop; slot -= 8)
    {
        if (*(const uintptr_t*)slot != frame)
            continue;

        const uintptr_t ret = *(const uintptr_t*)(slot + 8);
        const bool code = CallPrecedes(ret) || InSprx(ret);
        const bool other = ret >= 0x10000 && ret < 0x800000000000ull && (ret < low || ret >= high) &&
                           !(ret >= g_base && ret < g_base + kCodeEnd) && !InSprx(ret);

        if (foreign ? other : code)
            return slot;
    }

    return 0;
}

static void LogStackWalk(int index, uintptr_t thread)
{
    uintptr_t low = 0;
    uintptr_t high = 0;

    if (!ThreadStack(index, thread, &low, &high))
    {
        ReportLine("[Stall] %s: its stack could not be found", k_stallThreads[index]);
        return;
    }

    const uintptr_t rootReturn = g_base + (index == 0 ? kMainReturn : kThreadReturn);
    const uintptr_t floor = high - low > kRootScanBytes * 16 ? high - kRootScanBytes * 16 : low;
    uintptr_t frame = 0;

    for (uintptr_t slot = (high - 16) & ~(uintptr_t)7; slot >= floor; slot -= 8)
    {
        const uintptr_t saved = *(const uintptr_t*)slot;

        if (*(const uintptr_t*)(slot + 8) == rootReturn && saved > slot && saved < high)
        {
            frame = slot;
            break;
        }
    }

    char line[1900];
    size_t used = 0;
    Appendf(line, sizeof(line), &used, "[Stall] %s: stack %llX-%llX, live calls from its start:", k_stallThreads[index],
            (unsigned long long)low, (unsigned long long)high);

    if (!frame)
    {
        Appendf(line, sizeof(line), &used, " start frame not found");
        ReportLine("%s", line);
        return;
    }

    for (int frames = 0; frames < kWalkFrames; ++frames)
    {
        uintptr_t child = FindChild(frame, low, high, false);
        const bool last = !child;

        if (last)
            child = FindChild(frame, low, high, true);

        if (!child)
            break;

        AppendAddress(line, sizeof(line), &used, *(const uintptr_t*)(child + 8));
        frame = child;

        if (last)
            break;
    }

    Appendf(line, sizeof(line), &used, " (innermost frame %llX, %llu bytes below the top)", (unsigned long long)frame,
            (unsigned long long)(high - frame));
    ReportLine("%s", line);
}

static void SampleStalledThreads(uint32_t round, uint64_t stalledMs)
{
    uint32_t count[5] = {};
    uint32_t loaded[5] = {};
    CountBlobs(count, loaded);

    ReportLine("[Stall] round %u: no client frame for %llu ms; %u GetPlayers calls since the last heartbeat; streamer flags %u %u; "
                "blobs loaded: probe %u of %u, volume %u of %u, sun shadow tree %u of %u, sky %u of %u",
                round, (unsigned long long)stalledMs, g_serverCalls.load(std::memory_order_relaxed),
                *(const uint8_t*)(g_base + kStreamStarted), *(const uint8_t*)(g_base + kStreamDone), loaded[0], count[0], loaded[1],
                count[1], loaded[2], count[2], loaded[4], count[4]);

    for (int i = 0; i < kStallThreads; ++i)
    {
        const uintptr_t thread = *(const uintptr_t*)(g_base + kThreadHandles + 8 * (uintptr_t)i);

        if (!thread)
            continue;

        LogStackWalk(i, thread);

        if (!g_raiseException)
            continue;

        g_stallTarget.store(thread, std::memory_order_release);
        g_stallState.store(1, std::memory_order_release);

        const int raised = g_raiseException((ScePthread)thread, kStallSignal);
        uint64_t waited = 0;

        while (raised == 0 && g_stallState.load(std::memory_order_acquire) != 2 && waited < kStallAnswerUs)
        {
            sceKernelUsleep(2000);
            waited += 2000;
        }

        int expected = 1;

        if (g_stallState.compare_exchange_strong(expected, 0, std::memory_order_acq_rel))
        {
            if (raised != 0)
                ReportLine("[Stall] %s: could not be signalled (0x%08X)", k_stallThreads[i], (uint32_t)raised);
            else
                ReportLine("[Stall] %s: did not answer within %llu ms", k_stallThreads[i], (unsigned long long)(kStallAnswerUs / 1000));

            continue;
        }

        while (g_stallState.load(std::memory_order_acquire) == 3)
            sceKernelUsleep(1000);

        LogStallSample(k_stallThreads[i]);
        g_stallState.store(0, std::memory_order_release);
    }

    g_stallTarget.store(0, std::memory_order_release);
}

static void* StallWatch(void*)
{
    RaiseThread();

    uint64_t level = 0;
    uint64_t lastRound = 0;
    uint32_t rounds = 0;

    for (;;)
    {
        sceKernelUsleep(250000);

        const uint64_t start = g_levelStart.load(std::memory_order_relaxed);

        if (start != level)
        {
            level = start;
            lastRound = 0;
            rounds = 0;
        }

        if (!start || !*(volatile bool*)&g_inLevel || !g_usermapLevel.load(std::memory_order_relaxed) || rounds >= kStallRounds ||
            g_levelFrames.load(std::memory_order_relaxed) < kStallArmFrames)
            continue;

        const uint64_t now = sceKernelGetProcessTime();
        const uint64_t last = g_lastFrameAt.load(std::memory_order_relaxed);

        if (!last || now - last < kStallUs || (lastRound && now - lastRound < kStallGapUs))
            continue;

        lastRound = now;
        SampleStalledThreads(++rounds, (now - last) / 1000);
    }

    return nullptr;
}

static void* StallMirror(void*)
{
    RaiseThread();

    size_t written = 0;

    for (;;)
    {
        sceKernelUsleep(500000);

        const size_t used = g_reportUsed.load(std::memory_order_acquire);

        while (written < used)
        {
            const char* const line = g_report + written;
            const char* const end = (const char*)memchr(line, '\n', used - written);

            if (!end)
                break;

            T7Log_Write("%.*s", (int)(end - line), line);
            written = (size_t)(end - g_report) + 1;
        }
    }

    return nullptr;
}

static uintptr_t ImportTarget(uintptr_t target)
{
    if (InSprx(target) && target + 6 <= g_sprxEnd)
    {
        const uint8_t* const stub = (const uint8_t*)target;

        if (stub[0] != 0xFF || stub[1] != 0x25)
            return 0;

        const uintptr_t slot = target + 6 + (intptr_t)*(const int32_t*)(stub + 2);
        target = RangeReadable(slot, 8) ? *(const uintptr_t*)slot : 0;
    }

    if (target < 0x10000 || target >= 0x800000000000ull || InSprx(target) || (target >= g_base && target < g_base + kCodeEnd))
        return 0;

    return target;
}

static bool Near(uintptr_t a, uintptr_t b)
{
    return (a > b ? a - b : b - a) < kKernelSpan;
}

static bool UsablePair(uintptr_t install, uintptr_t raise, uintptr_t self)
{
    return install && raise && self && install != raise && Near(install, self) && Near(raise, self);
}

static void StartStallWatch()
{
    static bool started = false;

    if (started)
        return;

    started = true;

    SceKernelVirtualQueryInfo info;

    if (sceKernelVirtualQuery((void*)&StallSignal, 0, &info, sizeof(info)) >= 0 && (info.protection & 4))
    {
        g_sprxStart = (uintptr_t)info.start;
        g_sprxEnd = (uintptr_t)info.end;
    }

    const uintptr_t self = g_sprxStart ? ImportTarget((uintptr_t)&scePthreadSelf) : 0;
    const uintptr_t unityInstall = g_sprxStart ? ImportTarget((uintptr_t)&T7Unity_InstallExceptionHandler) : 0;
    const uintptr_t unityRaise = g_sprxStart ? ImportTarget((uintptr_t)&T7Unity_RaiseException) : 0;
    const uintptr_t kernelInstall = g_sprxStart ? ImportTarget((uintptr_t)&sceKernelInstallExceptionHandler) : 0;
    const uintptr_t kernelRaise = g_sprxStart ? ImportTarget((uintptr_t)&sceKernelRaiseException) : 0;

    T7Log_Write("[Stall] this module %llX-%llX; scePthreadSelf %llX; exception functions: libkernel_unity %llX %llX, libkernel %llX %llX",
                (unsigned long long)g_sprxStart, (unsigned long long)g_sprxEnd, (unsigned long long)self,
                (unsigned long long)unityInstall, (unsigned long long)unityRaise, (unsigned long long)kernelInstall,
                (unsigned long long)kernelRaise);

    uintptr_t install = 0;
    uintptr_t raise = 0;
    const char* from = nullptr;

    if (UsablePair(unityInstall, unityRaise, self))
    {
        install = unityInstall;
        raise = unityRaise;
        from = "libkernel_unity";
    }
    else if (UsablePair(kernelInstall, kernelRaise, self))
    {
        install = kernelInstall;
        raise = kernelRaise;
        from = "libkernel";
    }

    if (install)
    {
        const int installed = ((InstallHandler_t)install)(kStallSignal, StallSignal);

        if (installed == 0)
            g_raiseException = (RaiseException_t)raise;
        else
            T7Log_Write("[Stall] installing the exception handler through %s failed (0x%08X)", from, (uint32_t)installed);
    }

    ScePthread thread;
    const int created = scePthreadCreate(&thread, nullptr, StallWatch, nullptr, "BO3-Customs Stall");
    const int mirrored = scePthreadCreate(&thread, nullptr, StallMirror, nullptr, "BO3-Customs Stall Log");
    const char* const state = created == 0 && mirrored == 0 ? "running" : "NOT started";

    if (g_raiseException)
        T7Log_Write("[Stall] watchdog %s (threads are sampled by a signal through %s, plus stack walks)", state, from);
    else
        T7Log_Write("[Stall] watchdog %s (threads are sampled by stack walks only)", state);
}

static void FrameHeartbeat()
{
    g_clientFrames.fetch_add(1, std::memory_order_relaxed);
    g_levelFrames.fetch_add(1, std::memory_order_relaxed);

    const uint64_t now = sceKernelGetProcessTime();
    g_lastFrameAt.store(now, std::memory_order_relaxed);

    if (!g_mainStackProbe.load(std::memory_order_relaxed))
        g_mainStackProbe.store((uintptr_t)__builtin_frame_address(0), std::memory_order_relaxed);

    if (!g_usermapLevel.load(std::memory_order_relaxed) || g_heartbeats.load(std::memory_order_relaxed) >= kHeartbeats)
        return;

    uint64_t at = g_heartbeatAt.load(std::memory_order_relaxed);

    if (!at)
    {
        g_heartbeatAt.compare_exchange_strong(at, now, std::memory_order_relaxed);
        return;
    }

    if (now - at < kHeartbeatUs || !g_heartbeatAt.compare_exchange_strong(at, now, std::memory_order_relaxed))
        return;

    g_heartbeats.fetch_add(1, std::memory_order_relaxed);

    uint32_t count[5] = {};
    uint32_t loaded[5] = {};
    CountBlobs(count, loaded);

    const uint64_t start = g_levelStart.load(std::memory_order_relaxed);

    T7Log_Write("[Frame] %llu s into the level: %u client frames and %u GetPlayers calls in %llu ms; player 0 is %s; streamer "
                "flags %u %u; blobs loaded: probe %u of %u, volume %u of %u, sun shadow tree %u of %u, sky %u of %u",
                (unsigned long long)(start ? (now - start) / 1000000 : 0), g_clientFrames.exchange(0, std::memory_order_relaxed),
                g_serverCalls.exchange(0, std::memory_order_relaxed), (unsigned long long)((now - at) / 1000),
                FrozenText(PlayerFrozen(0)), *(const uint8_t*)(g_base + kStreamStarted), *(const uint8_t*)(g_base + kStreamDone),
                loaded[0], count[0], loaded[1], count[1], loaded[2], count[2], loaded[4], count[4]);
}

static bool WatchBuiltin(uintptr_t row, uint32_t hash, uintptr_t handler, void* hook, void** original)
{
    if (*original)
        return true;

    if (!RangeReadable(g_base + row, 32) || *(const uint32_t*)(g_base + row) != hash ||
        *(const uintptr_t*)(g_base + row + 16) != g_base + handler)
        return false;

    *original = (void*)(g_base + handler);
    *(uintptr_t*)(g_base + row + 16) = (uintptr_t)hook;
    return true;
}

static void WatchBuiltins(bool report)
{
    const struct
    {
        uintptr_t row;
        uint32_t  hash;
        uintptr_t handler;
        void*     hook;
        void**    original;
    } builtins[] =
    {
        { kOpenMenuRow, 0x4AA9CAAC, 0x9A9640, (void*)OpenMenu_h, &g_openMenuOriginal },
        { kCloseMenuRow, 0x49876A9E, 0x9A9AA0, (void*)CloseMenu_h, &g_closeMenuOriginal },
        { kFreezeControlsRow, 0xFAF8F736, 0x9ABDC0, (void*)FreezeControls_h, &g_freezeOriginal },
        { kUiVisibilityRow, 0x1B947E2A, 0x9B1640, (void*)UiVisibility_h, &g_uiVisibilityOriginal },
        { kGetPlayersRow, 0x3F10449F, 0xA4BF80, (void*)GetPlayers_h, &g_getPlayersOriginal },
    };

    for (const auto& builtin : builtins)
    {
        if (!WatchBuiltin(builtin.row, builtin.hash, builtin.handler, builtin.hook, builtin.original) && report)
            T7Log_Write("[Script] builtin %08X is not where this build expects it - not watched", builtin.hash);
    }
}
#endif

static uint64_t TexturesLoaded_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                                 double x0, double x1, double x2, double x3, double x4, double x5, double x6,
                                 double x7)
{
    const uint64_t result =
        ((Hook_t)g_texturesOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);

    if (!g_usermapLevel.load(std::memory_order_relaxed))
        return result;

    const uintptr_t top = *(const uintptr_t*)(g_base + kScrVmTop);

    if (!top || !RangeReadable(top, 16) || *(const int32_t*)(top + 8) != kVarInteger)
        return result;

    int32_t* const value = (int32_t*)top;
    const uint64_t now = sceKernelGetProcessTime();
    uint64_t start = g_textureWaitStart.load(std::memory_order_relaxed);

    if (*value != 0)
    {
        if (start && !g_texturesForced.load(std::memory_order_relaxed))
            T7Log_Write("[Stream] the level's textures loaded after %llu ms",
                        (unsigned long long)((now - start) / 1000));

        g_textureWaitStart.store(0, std::memory_order_relaxed);
        return result;
    }

    if (!start)
    {
        g_textureWaitStart.store(now, std::memory_order_relaxed);
        g_gateLogged.store(now, std::memory_order_relaxed);
        g_gateLogs.store(0, std::memory_order_relaxed);
        return result;
    }

    const uint32_t logs = g_gateLogs.load(std::memory_order_relaxed);

    if (now - g_gateLogged.load(std::memory_order_relaxed) >= (logs < 12 ? kGateLogUs : kGateLogSlowUs))
    {
        g_gateLogged.store(now, std::memory_order_relaxed);
        g_gateLogs.store(logs + 1, std::memory_order_relaxed);
        LogStreamGate(now - start, logs % 6 == 1);
    }

    if (now - start < kTextureWaitUs)
        return result;

    *value = 1;

    if (!g_texturesForced.exchange(true, std::memory_order_relaxed))
    {
        T7Log_Write("[Stream] textures still loading after %llu s (streamer flags %u %u, pending %d, override %u) - "
                    "the level starts and they keep streaming in",
                    (unsigned long long)(kTextureWaitUs / 1000000), *(const uint8_t*)(g_base + kStreamStarted),
                    *(const uint8_t*)(g_base + kStreamDone), *(const int32_t*)(g_base + kStreamPending),
                    *(const uint8_t*)(g_base + kStreamOverride));
        LogStreamGate(now - start, true);
    }

    return result;
}

static uint64_t DemoStart_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                            double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    if (g_usermapLevel.load(std::memory_order_relaxed))
        return 0;

    return ((Hook_t)g_demoStartOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static uint64_t LoadXAssets_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                              double x0, double x1, double x2, double x3, double x4, double x5, double x6,
                              double x7)
{
    const ZoneInfo* const zones = (const ZoneInfo*)a1;
    const int count = (int)(uint32_t)a2;
    const bool zombies = (*(const uint32_t*)(g_base + kSessionModes) & 0xF) == 0;

    if (zones && count > 0 && count <= kMaxZoneCall)
    {
        ListReader reading;
        bool custom = false;
        bool level = false;

        for (int i = 0; i < count; ++i)
        {
            if (zones[i].allocFlags & 0x800000)
                g_inLevel = false;
            else if (zones[i].allocFlags & 0x100)
                g_inLevel = true;
            else if (zones[i].freeFlags & 0x100)
                g_inLevel = false;

            custom = custom || (zones[i].name && MapPackage(zones[i].name) >= 0);
            level = level || (zones[i].name && (zones[i].allocFlags & 0x100));
        }

        if (level)
        {
            g_usermapLevel.store(custom, std::memory_order_relaxed);
            g_textureWaitStart.store(0, std::memory_order_relaxed);
            g_texturesForced.store(false, std::memory_order_relaxed);
#ifdef _DEBUG
            g_watchLogs.store(0, std::memory_order_relaxed);
            g_heartbeatAt.store(0, std::memory_order_relaxed);
            g_heartbeats.store(0, std::memory_order_relaxed);
            g_serverCalls.store(0, std::memory_order_relaxed);
            g_clientFrames.store(0, std::memory_order_relaxed);
            g_levelFrames.store(0, std::memory_order_relaxed);
            g_lastFrameAt.store(0, std::memory_order_relaxed);
            g_levelStart.store(sceKernelGetProcessTime(), std::memory_order_relaxed);
            g_dvarReads.store(0, std::memory_order_release);

            for (int32_t& frozen : g_lastFrozen)
                frozen = -1;
#endif
            T7Log_NewLevel();
#ifdef _DEBUG
            WatchBuiltins(true);
#endif
        }

        for (int i = 0; i < count; ++i)
            T7Log_Zone(zones[i].name, zones[i].allocFlags, zones[i].freeFlags);
    }

    int at = -1;

    if (zones && count > 0 && count <= kMaxZoneCall && zombies)
    {
        ListReader reading;

        if (g_levelCommon >= 0)
        {
            for (int i = 0; i < count; ++i)
            {
                const char* const name = zones[i].name;

                if (!name)
                    continue;

                if (EqualsNoCase(name, k_levelCommon))
                {
                    at = -1;
                    break;
                }

                if (at < 0 && MapPackage(name) >= 0)
                    at = i;
            }
        }
    }

    if (at >= 0)
    {
        T7Log_Write("[Zones] %s goes in front of %s", k_levelCommon, zones[at].name);

        ZoneInfo list[kMaxZoneCall + 1];

        memcpy(list, zones, (size_t)at * sizeof(ZoneInfo));
        list[at] = zones[at];
        list[at].name = k_levelCommon;
        list[at].allocFlags |= kLevelCommonFlag;
        memcpy(list + at + 1, zones + at, (size_t)(count - at) * sizeof(ZoneInfo));

        return ((Hook_t)g_loadOriginal)((uint64_t)list, (uint64_t)(count + 1), a3, a4, a5, a6, x0, x1, x2, x3, x4,
                                         x5, x6, x7);
    }

    return ((Hook_t)g_loadOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static uint64_t BlockJob_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                           double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uint64_t slot = a1 ? *(uint64_t*)(a1 + 16) : 0;

    if (slot)
    {
        const uint64_t data = *(uint64_t*)(slot + 0x40020);
        const uint32_t unpacked = *(uint32_t*)(slot + 0x40028);
        const uint32_t hashed = *(uint32_t*)(slot + 0x4002C);
        const uint32_t stored = *(uint32_t*)(slot + 0x40030);
        const uint64_t ring = *(uint64_t*)(g_base + kReadRing);

        const bool outside = hashed && (data < ring || data + hashed > ring + 0xA00000);
        const bool odd = hashed > stored + 3 || unpacked > 0x40000;

        if (outside || odd)
        {
            *(uint32_t*)(slot + 0x4002C) = 0;
            *(uint32_t*)(slot + 0x40028) = 0;
        }
    }

    return ((Hook_t)g_blockOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static bool CodeImageSet(uint64_t image)
{
    return image >= 0x10000 && image < 0x0000800000000000ULL;
}

static uint64_t CodeTexture_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                              double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uint32_t index = (uint32_t)a4;

    if (a1 && index < 99 && !*(uint8_t*)(a1 + 6016 + 6 * (uint64_t)index) &&
        !CodeImageSet(*(uint64_t*)(a1 + 4432 + 8 * (uint64_t)index)) &&
        !CodeImageSet(*(uint64_t*)(a1 + 5224 + 8 * (uint64_t)index)))
    {
        uint64_t image = *(uint64_t*)(g_base + kImageClear);

        if (!image)
            image = *(uint64_t*)(g_base + kImageBlack);

        if (image)
        {
            const uint64_t texture = image + (*(uint8_t*)(image + 164) ? 200 : 168);
            return ((Hook_t)(g_base + kBindTexture))(a1, a2, a3, texture, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
        }
    }

    return ((Hook_t)g_codeTextureOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static char g_customMapsLua[131072];

static char* ReadSmallFile(const char* path, size_t max)
{
    const int fd = sceKernelOpen(path, SCE_KERNEL_O_RDONLY, 0);

    if (fd < 0)
        return nullptr;

    char* const text = (char*)malloc(max + 1);
    size_t done = 0;

    while (text && done < max)
    {
        const int64_t n = sceKernelRead(fd, text + done, max - done);

        if (n <= 0)
            break;

        done += (size_t)n;
    }

    sceKernelClose(fd);

    if (!text || !done)
    {
        free(text);
        return nullptr;
    }

    text[done] = 0;
    return text;
}

static size_t Utf8(uint32_t code, char* out)
{
    if (code < 0x80)
    {
        out[0] = (char)code;
        return 1;
    }

    if (code < 0x800)
    {
        out[0] = (char)(0xC0 | (code >> 6));
        out[1] = (char)(0x80 | (code & 0x3F));
        return 2;
    }

    out[0] = (char)(0xE0 | (code >> 12));
    out[1] = (char)(0x80 | ((code >> 6) & 0x3F));
    out[2] = (char)(0x80 | (code & 0x3F));
    return 3;
}

static bool JsonString(const char* json, const char* key, char* out, size_t size)
{
    char quoted[64];
    snprintf(quoted, sizeof(quoted), "\"%s\"", key);

    const char* at = strstr(json, quoted);

    if (!at || size == 0)
        return false;

    at += strlen(quoted);

    while (*at == ' ' || *at == '\t' || *at == '\r' || *at == '\n' || *at == ':')
        ++at;

    if (*at != '"')
        return false;

    ++at;
    size_t used = 0;

    for (; *at && *at != '"' && used + 4 < size; ++at)
    {
        if (*at != '\\')
        {
            out[used++] = *at;
            continue;
        }

        ++at;

        switch (*at)
        {
        case 'n':  out[used++] = '\n'; break;
        case 't':  out[used++] = ' '; break;
        case 'r':  break;
        case 'u':
        {
            uint32_t code = 0;

            for (int i = 1; i <= 4; ++i)
            {
                const char c = at[i];
                code = code * 16 + (uint32_t)(c >= '0' && c <= '9' ? c - '0' :
                                              c >= 'a' && c <= 'f' ? c - 'a' + 10 :
                                              c >= 'A' && c <= 'F' ? c - 'A' + 10 : 0);
            }

            at += 4;
            used += Utf8(code, out + used);
            break;
        }
        case 0:    --at; break;
        default:   out[used++] = *at; break;
        }
    }

    out[used] = 0;
    return true;
}

static void AppendLuaString(char* out, size_t size, size_t* used, const char* text)
{
    if (*used + 3 >= size)
        return;

    out[(*used)++] = '"';

    for (const unsigned char* c = (const unsigned char*)text; *c && *used + 4 < size; ++c)
    {
        if (*c == '"' || *c == '\\')
        {
            out[(*used)++] = '\\';
            out[(*used)++] = (char)*c;
        }
        else if (*c == '\n')
        {
            out[(*used)++] = '\\';
            out[(*used)++] = 'n';
        }
        else if (*c >= 0x20)
        {
            out[(*used)++] = (char)*c;
        }
    }

    out[(*used)++] = '"';
    out[*used] = 0;
}

static void Append(char* out, size_t size, size_t* used, const char* text)
{
    const int n = snprintf(out + *used, size - *used, "%s", text);

    if (n > 0)
        *used = *used + (size_t)n < size ? *used + (size_t)n : size - 1;
}

static bool MoviePlayable(const char* path, char* why, size_t whySize)
{
    const int fd = sceKernelOpen(path, SCE_KERNEL_O_RDONLY, 0);

    if (fd < 0)
    {
        snprintf(why, whySize, "it could not be opened");
        return false;
    }

    static uint8_t data[64 * 1024];
    size_t size = 0;

    while (size < sizeof(data))
    {
        const int64_t n = sceKernelRead(fd, data + size, sizeof(data) - size);

        if (n <= 0)
            break;

        size += (size_t)n;
    }

    sceKernelClose(fd);

    const auto readVint = [&](size_t at, bool keepMarker, uint64_t* value) -> size_t
    {
        if (at >= size)
            return 0;

        const uint8_t first = data[at];
        size_t length = 1;

        for (uint8_t mask = 0x80; length <= 8 && !(first & mask); mask >>= 1)
            ++length;

        if (length > 8 || at + length > size)
            return 0;

        uint64_t result = keepMarker ? first : (first & (0xFF >> length));

        for (size_t i = 1; i < length; ++i)
            result = result << 8 | data[at + i];

        *value = result;
        return length;
    };

    char codec[32] = "";
    uint64_t width = 0;
    uint64_t height = 0;
    int profile = -1;
    int level = -1;

    size_t at = 0;
    size_t end = size;
    int tracks = 0;

    while (at < end)
    {
        uint64_t id = 0;
        uint64_t length = 0;
        const size_t idLength = readVint(at, true, &id);
        const size_t sizeLength = idLength ? readVint(at + idLength, false, &length) : 0;

        if (!idLength || !sizeLength)
            break;

        const size_t body = at + idLength + sizeLength;
        const bool unknown = length >= (1ull << (7 * sizeLength)) - 1;
        const size_t bodyEnd = unknown || body + length > end ? end : body + (size_t)length;

        if (id == 0x1F43B675 || (id == 0xAE && ++tracks > 1))
            break;

        if (id == 0x18538067 || id == 0x1654AE6B || id == 0xAE || id == 0xE0)
        {
            at = body;

            if (id == 0x18538067)
                end = bodyEnd;

            continue;
        }

        if (id == 0x86 && !codec[0])
            snprintf(codec, sizeof(codec), "%.*s", (int)(bodyEnd - body), (const char*)data + body);
        else if (id == 0xB0)
            for (size_t i = body; i < bodyEnd; ++i) width = width << 8 | data[i];
        else if (id == 0xBA)
            for (size_t i = body; i < bodyEnd; ++i) height = height << 8 | data[i];
        else if (id == 0x63A2 && bodyEnd - body >= 4 && data[body] == 1)
        {
            profile = data[body + 1];
            level = data[body + 3];
        }

        at = bodyEnd;
    }

    if (strcmp(codec, "V_MPEG4/ISO/AVC") != 0)
    {
        snprintf(why, whySize, "its video is %s, not H.264", codec[0] ? codec : "unreadable");
        return false;
    }

    if (!width || !height || width > 1920 || height > 1080)
    {
        snprintf(why, whySize, "it is %llux%llu", (unsigned long long)width, (unsigned long long)height);
        return false;
    }

    if (profile >= 0 && ((profile != 66 && profile != 77 && profile != 100) || level > 42))
    {
        snprintf(why, whySize, "it is H.264 profile %d level %d.%d", profile, level / 10, level % 10);
        return false;
    }

    snprintf(why, whySize, "H.264 %llux%llu", (unsigned long long)width, (unsigned long long)height);
    return true;
}

static void ScanMovies()
{
    static char names[64][kListedNameMax];

    for (int i = 0; i < g_packageCount; ++i)
    {
        if (!g_packages[i].map)
            continue;

        for (int place = 0; place < 2; ++place)
        {
            const char* const base = place == 0 ? g_packages[i].folder : g_packages[i].alternate;

            if (!base[0])
                continue;

            char folder[224];
            snprintf(folder, sizeof(folder), "%s/video", base);

            const int count = ListFolder(folder, false, names, 64);

            for (int n = 0; n < count; ++n)
            {
                const size_t length = strlen(names[n]);

                if (length <= 4 || !EqualsNoCase(names[n] + length - 4, ".mkv"))
                    continue;

                bool taken = false;

                for (int m = 0; m < g_movieCount && !taken; ++m)
                    taken = strlen(g_movies[m].name) == length - 4 && strncmp(g_movies[m].file, names[n], length - 4) == 0;

                if (taken || g_movieCount >= (int)(sizeof(g_movies) / sizeof(g_movies[0])))
                    continue;

                char path[320];
                char why[160];
                snprintf(path, sizeof(path), "%s/%s", folder, names[n]);

                if (!MoviePlayable(path, why, sizeof(why)))
                    continue;

                Movie& movie = g_movies[g_movieCount++];
                snprintf(movie.file, sizeof(movie.file), "%s", names[n]);
                snprintf(movie.name, sizeof(movie.name), "%.*s", (int)(length - 4), names[n]);
                snprintf(movie.folder, sizeof(movie.folder), "%s", folder);
                movie.package = i;
            }
        }
    }
}

static void FindPreview(Package& package, const char* json)
{
    char path[320];

    package.preview[0] = 0;

    if (FindPackageFile(package, "previewimage.png", path, sizeof(path)))
    {
        snprintf(package.preview, sizeof(package.preview), "previewimage.png");
        return;
    }

    char thumbnail[260];

    if (!json || !JsonString(json, "Thumbnail", thumbnail, sizeof(thumbnail)))
        return;

    const char* file = thumbnail;

    for (const char* c = thumbnail; *c; ++c)
    {
        if (*c == '/' || *c == '\\')
            file = c + 1;
    }

    const size_t length = strlen(file);

    if (length <= 4 || length >= sizeof(package.preview) || !EqualsNoCase(file + length - 4, ".png"))
        return;

    if (FindPackageFile(package, file, path, sizeof(path)))
        snprintf(package.preview, sizeof(package.preview), "%s", file);
}

static void BuildCustomMapsLua()
{
    char* const out = g_customMapsLua;
    const size_t size = sizeof(g_customMapsLua);
    size_t used = 0;

    out[0] = 0;
    Append(out, size, &used, "rawset(_G, \"BO3CustomMaps\", {\n");

    for (int i = 0; i < g_packageCount; ++i)
    {
        Package& package = g_packages[i];
        const bool zombies = strncmp(package.name, "zm_", 3) == 0;
        const bool multiplayer = strncmp(package.name, "mp_", 3) == 0;

        if (!package.map || (!zombies && !multiplayer))
            continue;

        ++g_listedMaps;

        char path[320];
        FindPackageFile(package, "workshop.json", path, sizeof(path));

        char title[160];
        char description[640];
        snprintf(title, sizeof(title), "%s", package.name);
        description[0] = 0;

        char* const json = ReadSmallFile(path, 64 * 1024);

        if (json)
        {
            if (!JsonString(json, "Title", title, sizeof(title)) || !title[0])
                snprintf(title, sizeof(title), "%s", package.name);

            JsonString(json, "Description", description, sizeof(description));
        }

        FindPreview(package, json);
        free(json);

        char picture[320];
        char previewName[96];
        char loadingName[96];

        const bool preview = package.preview[0] != 0;
        const bool loading = FindPackageFile(package, "loadingimage.png", picture, sizeof(picture));
        snprintf(previewName, sizeof(previewName), T7_USERMAP_PREVIEW_PREFIX "%s", package.name);
        snprintf(loadingName, sizeof(loadingName), T7_USERMAP_LOADING_PREFIX "%s", package.name);

        Append(out, size, &used, "  { id = ");
        AppendLuaString(out, size, &used, package.name);
        Append(out, size, &used, zombies ? ", mode = \"zm\"" : ", mode = \"mp\"");
        Append(out, size, &used, ", name = ");
        AppendLuaString(out, size, &used, title);
        Append(out, size, &used, ", desc = ");
        AppendLuaString(out, size, &used, description);

        if (preview)
        {
            Append(out, size, &used, ", preview = ");
            AppendLuaString(out, size, &used, previewName);
        }

        if (loading)
        {
            Append(out, size, &used, ", loading = ");
            AppendLuaString(out, size, &used, loadingName);
        }

        char movieName[kNameMax + 8];
        snprintf(movieName, sizeof(movieName), "%s_load", package.name);
        bool movie = false;

        for (int m = 0; m < g_movieCount && !movie; ++m)
            movie = g_movies[m].package == i && EqualsNoCase(g_movies[m].name, movieName);

        if (movie)
        {
            Append(out, size, &used, ", movie = ");
            AppendLuaString(out, size, &used, movieName);
        }

        Append(out, size, &used, " },\n");
    }

    Append(out, size, &used, "})\n");

    if (used + 2 >= size)
        snprintf(out, size, "rawset(_G, \"BO3CustomMaps\", {})\n");
}

static char g_mapsTableLua[144000];

static void BuildMapsTableLua()
{
    char path[256];
    snprintf(path, sizeof(path), "%s/ui_scripts/maptable.lua", "/data/BO3-Customs");

    char* const source = ReadSmallFile(path, 64 * 1024);

    if (!source)
    {
        g_mapsTableLua[0] = 0;
        return;
    }

    snprintf(g_mapsTableLua, sizeof(g_mapsTableLua), "%s%s", g_customMapsLua, source);
    free(source);
}

static uint64_t MapsTable_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                            double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uint64_t results = ((Hook_t)g_mapsTableOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);

    if ((uint32_t)results == 1 && a1 && g_mapsTableLua[0])
        T7Lua_RunOnTop(a1, g_mapsTableLua, "=bo3customs_maptable");

    return results;
}

static bool SameNoCase(const char* a, const char* b, size_t length)
{
    for (size_t i = 0; i < length; ++i)
    {
        char x = a[i];
        char y = b[i];

        if (x >= 'A' && x <= 'Z')
            x = (char)(x + 32);

        if (y >= 'A' && y <= 'Z')
            y = (char)(y + 32);

        if (x != y || !x)
            return x == y && i + 1 == length;
    }

    return true;
}

static uint64_t PathRemap_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                            double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uint64_t result = ((Hook_t)g_pathRemapOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
    const char* const path = (const char*)a1;
    char* const out = (char*)a2;
    const char* const slash = path ? strrchr(path, '/') : nullptr;

    if (!out || !slash || slash - path < 6 || strncmp(slash - 6, "/video", 6) != 0)
        return result;

    const char* const file = slash + 1;
    const size_t length = strlen(file);

    if (length <= 4 || !SameNoCase(file + length - 4, ".mkv", 4))
        return result;

    ListReader reading;

    for (int i = 0; i < g_movieCount; ++i)
    {
        Movie& movie = g_movies[i];
        const size_t movieLength = strlen(movie.name);

        if (movieLength != length - 4 || !SameNoCase(file, movie.name, movieLength))
            continue;

        snprintf(out, 256, "%s/%s", movie.folder, movie.file);
        break;
    }

    return result;
}

static bool RemoveSignaturePenalty(uintptr_t base, const SignaturePatch& patch)
{
    const uintptr_t at = base + patch.site;

    if (!RangeReadable(at, patch.expectLength))
        return false;

    const uint8_t* const code = (const uint8_t*)at;
    const size_t tail = patch.offset + sizeof(k_nop10);
    const bool around = memcmp(code, patch.expect, patch.offset) == 0 &&
                        memcmp(code + tail, patch.expect + tail, patch.expectLength - tail) == 0;

    if (around && memcmp(code + patch.offset, k_nop10, sizeof(k_nop10)) == 0)
        return true;

    if (!around || memcmp(code + patch.offset, patch.expect + patch.offset, sizeof(k_nop10)) != 0)
        return false;

    const uintptr_t first = (at + patch.offset) & ~0x3FFFull;
    const uintptr_t last = (at + tail - 1) & ~0x3FFFull;

    if (sceKernelMprotect((const void*)first, last - first + 0x4000, 7) < 0)
        return false;

    memcpy((void*)(at + patch.offset), k_nop10, sizeof(k_nop10));
    return true;
}

static bool TreatUnknownMapsAsBase(uintptr_t base)
{
    const uintptr_t gate = base + kMapContentGate;
    const uintptr_t target = base + kMapContentBase;

    if (!RangeReadable(gate, sizeof(k_mapContentGate)) || !RangeReadable(target, sizeof(k_mapContentBase)) ||
        memcmp((const void*)target, k_mapContentBase, sizeof(k_mapContentBase)) != 0 ||
        memcmp((const void*)gate, k_mapContentGate, 3) != 0 || memcmp((const void*)(gate + 4), k_mapContentGate + 4, 4) != 0 ||
        gate + 4 + kMapContentJump != target)
        return false;

    uint8_t* const jump = (uint8_t*)(gate + 3);

    if (*jump == kMapContentJump)
        return true;

    if (*jump != kMapContentReturn || sceKernelMprotect((const void*)((uintptr_t)jump & ~0x3FFFull), 0x4000, 7) < 0)
        return false;

    *jump = kMapContentJump;
    return true;
}

static bool LowerPlaceholderRank(uintptr_t base)
{
    for (const PenaltySite& site : k_placeholderPenalties)
    {
        const uintptr_t at = base + site.site;

        if (!RangeReadable(at, site.operand + sizeof(int32_t)) || memcmp((const void*)at, site.opcode, site.operand) != 0)
            return false;

        int32_t value;
        memcpy(&value, (const void*)(at + site.operand), sizeof(value));

        if (value != kPlaceholderPenalty && value != kPlaceholderPenaltyLow)
            return false;
    }

    const uintptr_t first = (base + k_placeholderPenalties[0].site) & ~0x3FFFull;
    const PenaltySite& tail = k_placeholderPenalties[sizeof(k_placeholderPenalties) / sizeof(k_placeholderPenalties[0]) - 1];
    const uintptr_t last = (base + tail.site + tail.operand + sizeof(int32_t) - 1) & ~0x3FFFull;

    if (sceKernelMprotect((const void*)first, last - first + 0x4000, 7) < 0)
        return false;

    for (const PenaltySite& site : k_placeholderPenalties)
        memcpy((void*)(base + site.site + site.operand), &kPlaceholderPenaltyLow, sizeof(int32_t));

    return true;
}

struct Prologue
{
    uintptr_t      offset;
    const char*    what;
    const uint8_t* bytes;
    size_t         size;
};

static bool BuildMatches(uintptr_t base)
{
    static const Prologue checks[] =
    {
        { kFileOpen,    "file open",       k_fileOpen,   sizeof(k_fileOpen) },
        { kMapExists,   "map check",       k_mapExists,  sizeof(k_mapExists) },
        { kIsMapValid,  "Engine.IsMapValid", k_isMapValid, sizeof(k_isMapValid) },
        { kLoadXAssets, "DB_LoadXAssets",  k_loadZones,  sizeof(k_loadZones) },
    };

    for (const Prologue& check : checks)
    {
        const uintptr_t at = base + check.offset;

        if (!RangeReadable(at, check.size) || memcmp((const void*)at, check.bytes, check.size) != 0)
            return false;
    }

    return true;
}

static void ScanAll()
{
    g_packageCount = 0;
    g_fileCount = 0;
    g_movieCount = 0;
    g_levelCommon = -1;
    g_listedMaps = 0;

    ScanUsermaps("/data/BO3-Customs");
    ScanSharedZones("/data/BO3-Customs");

    for (const char* const drive : k_driveRoots)
    {
        char data[64];
        snprintf(data, sizeof(data), "/%s/BO3-Customs", drive);
        ScanUsermaps(data);
        ScanSharedZones(data);
    }

    ScanMovies();
    BuildCustomMapsLua();
    BuildMapsTableLua();
}

static void InstallHooks(uintptr_t base)
{
    if (g_levelCommon >= 0 && !g_rankLowered)
    {
        g_rankLowered = true;
        LowerPlaceholderRank(base);
    }

    if (g_hooksInstalled)
        return;

    g_hooksInstalled = true;

    for (const SignaturePatch& patch : k_signaturePatches)
        RemoveSignaturePenalty(base, patch);

    Detour_Attach(&g_openDetour, (uint64_t)(base + kFileOpen), (void*)FileOpen_h, &g_openOriginal);
    Detour_Attach(&g_existsDetour, (uint64_t)(base + kMapExists), (void*)MapExists_h, &g_existsOriginal);
    Detour_Attach(&g_validDetour, (uint64_t)(base + kIsMapValid), (void*)IsMapValid_h, &g_validOriginal);
    Detour_Attach(&g_loadDetour, (uint64_t)(base + kLoadXAssets), (void*)LoadXAssets_h, &g_loadOriginal);

    if (RangeReadable(base + kDlcBitForMap, sizeof(k_dlcBitForMap)) &&
        memcmp((const void*)(base + kDlcBitForMap), k_dlcBitForMap, sizeof(k_dlcBitForMap)) == 0)
    {
        Detour_Attach(&g_dlcBitDetour, (uint64_t)(base + kDlcBitForMap), (void*)DlcBitForMap_h, &g_dlcBitOriginal);
    }

    TreatUnknownMapsAsBase(base);

    if (RangeReadable(base + kBlockJob, sizeof(k_blockJob)) &&
        memcmp((const void*)(base + kBlockJob), k_blockJob, sizeof(k_blockJob)) == 0)
    {
        Detour_Attach(&g_blockDetour, (uint64_t)(base + kBlockJob), (void*)BlockJob_h, &g_blockOriginal);
    }

    if (RangeReadable(base + kCodeTexture, sizeof(k_codeTexture)) &&
        memcmp((const void*)(base + kCodeTexture), k_codeTexture, sizeof(k_codeTexture)) == 0)
    {
        Detour_Attach(&g_codeTextureDetour, (uint64_t)(base + kCodeTexture), (void*)CodeTexture_h,
                      &g_codeTextureOriginal);
    }

    const struct
    {
        uintptr_t      offset;
        const uint8_t* bytes;
        size_t         size;
        Detour*        detour;
        void*          hook;
        void**         original;
    } loadingHooks[] =
    {
        { kGdtMapsTable, k_mapsTable,    sizeof(k_mapsTable),    &g_mapsTableDetour,    (void*)MapsTable_h,    &g_mapsTableOriginal },
        { kPathRemap,    k_pathRemap,    sizeof(k_pathRemap),    &g_pathRemapDetour,    (void*)PathRemap_h,    &g_pathRemapOriginal },
    };

    for (const auto& hook : loadingHooks)
    {
        if (RangeReadable(base + hook.offset, hook.size) &&
            memcmp((const void*)(base + hook.offset), hook.bytes, hook.size) == 0)
        {
            Detour_Attach(hook.detour, (uint64_t)(base + hook.offset), hook.hook, hook.original);
        }
    }

    if (RangeReadable(base + kLinkXAsset, sizeof(k_linkXAsset)) &&
        memcmp((const void*)(base + kLinkXAsset), k_linkXAsset, sizeof(k_linkXAsset)) == 0 &&
        RangeReadable(base + kZonePriority, sizeof(k_zonePriority)) &&
        memcmp((const void*)(base + kZonePriority), k_zonePriority, sizeof(k_zonePriority)) == 0)
    {
        Detour_Attach(&g_priorityDetour, (uint64_t)(base + kZonePriority), (void*)ZonePriority_h, &g_priorityOriginal);

        if (g_priorityOriginal)
            Detour_Attach(&g_linkDetour, (uint64_t)(base + kLinkXAsset), (void*)LinkXAsset_h, &g_linkOriginal);
    }

    if (RangeReadable(base + kDemoStart, sizeof(k_demoStart)) &&
        memcmp((const void*)(base + kDemoStart), k_demoStart, sizeof(k_demoStart)) == 0)
    {
        Detour_Attach(&g_demoStartDetour, (uint64_t)(base + kDemoStart), (void*)DemoStart_h, &g_demoStartOriginal);
    }

    if (RangeReadable(base + kGetDvarString, sizeof(k_getDvarString)) &&
        memcmp((const void*)(base + kGetDvarString), k_getDvarString, sizeof(k_getDvarString)) == 0)
    {
        Detour_Attach(&g_getDvarStringDetour, (uint64_t)(base + kGetDvarString), (void*)GetDvarString_h,
                      &g_getDvarStringOriginal);
    }

    if (RangeReadable(base + kPlayViewmodelFx, sizeof(k_playViewmodelFx)) &&
        memcmp((const void*)(base + kPlayViewmodelFx), k_playViewmodelFx, sizeof(k_playViewmodelFx)) == 0)
    {
        Detour_Attach(&g_viewmodelFxDetour, (uint64_t)(base + kPlayViewmodelFx), (void*)PlayViewmodelFx_h,
                      &g_viewmodelFxOriginal);
    }

    if (RangeReadable(base + kTexturesLoaded, sizeof(k_texturesLoaded)) &&
        memcmp((const void*)(base + kTexturesLoaded), k_texturesLoaded, sizeof(k_texturesLoaded)) == 0)
    {
        Detour_Attach(&g_texturesDetour, (uint64_t)(base + kTexturesLoaded), (void*)TexturesLoaded_h,
                      &g_texturesOriginal);
    }

#ifdef _DEBUG
    WatchBuiltins(false);
    StartStallWatch();
#endif
}
}

void T7Maps_Install(uintptr_t base)
{
    using namespace T7Maps;

    static bool installed = false;

    if (installed || !base)
        return;

    installed = true;

    if (!BuildMatches(base))
        return;

    g_buildMatched = true;
    g_base = base;

    ScanAll();

    if (g_packageCount)
        InstallHooks(base);
}

bool T7Maps_Refresh()
{
    using namespace T7Maps;

    if (!g_buildMatched || !g_base || g_inLevel)
        return false;

    BeginListWrite();
    ScanAll();
    EndListWrite();

    if (g_packageCount)
        InstallHooks(g_base);

    return true;
}

int T7Maps_MapCount()
{
    return T7Maps::g_buildMatched ? T7Maps::g_listedMaps : -1;
}

bool T7Maps_InLevel()
{
    return T7Maps::g_inLevel;
}

const char* T7Maps_CustomMapsLua()
{
    return T7Maps::g_customMapsLua[0] ? T7Maps::g_customMapsLua : "rawset(_G, \"BO3CustomMaps\", {})\n";
}

bool T7Maps_MapFile(const char* map, const char* file, char* path, size_t size)
{
    using namespace T7Maps;

    ListReader reading;
    const int package = g_base && map && file ? MapPackage(map) : -1;
    return package >= 0 && FindPackageFile(g_packages[package], file, path, size);
}

const char* T7Maps_PreviewFile(const char* map)
{
    using namespace T7Maps;

    ListReader reading;
    const int package = g_base && map ? MapPackage(map) : -1;
    return package >= 0 && g_packages[package].preview[0] ? g_packages[package].preview : nullptr;
}

void T7Maps_FrameHeartbeat()
{
#ifdef _DEBUG
    using namespace T7Maps;

    if (g_base)
        FrameHeartbeat();
#endif
}
