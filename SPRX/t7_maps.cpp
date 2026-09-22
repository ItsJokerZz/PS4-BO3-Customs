#include "headers.hpp"
#include "t7_maps.hpp"
#include "t7_mapimages.hpp"
#include "t7_lua.hpp"

namespace T7Maps
{
constexpr uintptr_t kFileOpen     = 0xF29960;
constexpr uintptr_t kMapExists    = 0xF10650;
constexpr uintptr_t kIsMapValid   = 0xDECEB0;
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

constexpr int32_t kLevelCommonFlag = 0x200;

constexpr int    kMaxPackages   = 48;
constexpr int    kMaxFiles      = 384;
constexpr int    kMaxZoneCall   = 32;
constexpr int    kMaxListed     = 96;
constexpr size_t kNameMax       = 56;
constexpr size_t kListedNameMax = 96;

static const char k_levelCommon[] = "zm_levelcommon";
static const char k_soundFolder[] = "/app0/zone/";

struct Package
{
    char name[kNameMax];
    char preview[kListedNameMax];
    bool map;
};

struct Movie
{
    char name[kListedNameMax];
    char file[kListedNameMax];
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

static Detour g_openDetour{};
static void*  g_openOriginal = nullptr;
static Detour g_existsDetour{};
static void*  g_existsOriginal = nullptr;
static Detour g_validDetour{};
static void*  g_validOriginal = nullptr;
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
static const uint8_t k_blockJob[]   = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                        0x53, 0x48, 0x83, 0xE4, 0xE0, 0x48, 0x81, 0xEC, 0x20, 0x12, 0x00, 0x00,
                                        0x48, 0x8B, 0x0D };
static const uint8_t k_codeTexture[] = { 0x41, 0x89, 0xC8, 0x45, 0x89, 0xC1, 0x4B, 0x8D, 0x04, 0x49, 0x80, 0xBC,
                                         0x47, 0x80, 0x17, 0x00, 0x00, 0x00, 0x74, 0x54, 0x44, 0x0F, 0xB7, 0x84,
                                         0x47, 0x82, 0x17, 0x00, 0x00, 0x48, 0x8B, 0x0D };
static const uint8_t k_mapsTable[]   = { 0x55, 0x48, 0x89, 0xE5, 0x53, 0x48, 0x83, 0xEC, 0x18, 0x48, 0x8B, 0x1D };
static const uint8_t k_pathRemap[]   = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                         0x53, 0x48, 0x81, 0xEC, 0xF8, 0x05, 0x00, 0x00, 0x48, 0x8B, 0x05 };

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

static int AddPackage(const char* name, bool map)
{
    if (g_packageCount >= kMaxPackages)
        return -1;

    Package& package = g_packages[g_packageCount];
    snprintf(package.name, sizeof(package.name), "%s", name);
    package.map = map;
    return g_packageCount++;
}

static void ScanUsermaps()
{
    char root[224];
    snprintf(root, sizeof(root), "%s/usermaps", Data_Dir());

    static char folders[kMaxPackages][kListedNameMax];
    const int count = ListFolder(root, true, folders, kMaxPackages);

    for (int i = 0; i < count; ++i)
    {
        char folder[224];
        char zone[224];
        SceKernelStat st;

        snprintf(folder, sizeof(folder), "%s/%s", root, folders[i]);
        snprintf(zone, sizeof(zone), "%s/%s.ff", folder, folders[i]);

        if (!IsToken(folders[i]) || sceKernelStat(zone, &st) != 0)
            continue;

        const int package = AddPackage(folders[i], true);

        if (package >= 0)
            ScanPackage(folder, package);
    }
}

static void ScanSharedZones()
{
    char folder[224];
    snprintf(folder, sizeof(folder), "%s/zone", Data_Dir());

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

        const int package = AddPackage(zone, false);

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

static uint64_t FileOpen_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                           double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const char* const path = (const char*)a1;

    if (path)
    {
        const char* const rel = Relative(path);

        if (rel)
        {
            int found = -1;

            for (int i = 0; i < g_fileCount; ++i)
            {
                if (strcmp(g_files[i].rel, rel) == 0)
                {
                    found = i;
                    break;
                }
            }

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

static uint64_t LoadXAssets_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                              double x0, double x1, double x2, double x3, double x4, double x5, double x6,
                              double x7)
{
    const ZoneInfo* const zones = (const ZoneInfo*)a1;
    const int count = (int)(uint32_t)a2;
    const bool zombies = (*(const uint32_t*)(g_base + kSessionModes) & 0xF) == 0;

    if (zones && count > 0 && count <= kMaxZoneCall)
    {
        for (int i = 0; i < count; ++i)
        {
            if (zones[i].allocFlags & 0x800000)
                g_inLevel = false;
            else if (zones[i].allocFlags & 0x100)
                g_inLevel = true;
            else if (zones[i].freeFlags & 0x100)
                g_inLevel = false;
        }
    }

    if (zones && count > 0 && count <= kMaxZoneCall && g_levelCommon >= 0 && zombies)
    {
        int at = -1;

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

        if (at >= 0)
        {
            ZoneInfo list[kMaxZoneCall + 1];

            memcpy(list, zones, (size_t)at * sizeof(ZoneInfo));
            list[at] = zones[at];
            list[at].name = k_levelCommon;
            list[at].allocFlags |= kLevelCommonFlag;
            memcpy(list + at + 1, zones + at, (size_t)(count - at) * sizeof(ZoneInfo));

            return ((Hook_t)g_loadOriginal)((uint64_t)list, (uint64_t)(count + 1), a3, a4, a5, a6, x0, x1, x2, x3,
                                             x4, x5, x6, x7);
        }
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

static uint64_t CodeTexture_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                              double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uint32_t index = (uint32_t)a4;

    if (a1 && index < 99 && !*(uint8_t*)(a1 + 6016 + 6 * (uint64_t)index) &&
        !*(uint64_t*)(a1 + 4432 + 8 * (uint64_t)index) && !*(uint64_t*)(a1 + 5224 + 8 * (uint64_t)index))
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

static char g_customMapsLua[65536] = "rawset(_G, \"BO3CustomMaps\", {})\n";

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

        char folder[224];
        snprintf(folder, sizeof(folder), "%s/usermaps/%s/video", Data_Dir(), g_packages[i].name);

        const int count = ListFolder(folder, false, names, 64);

        for (int n = 0; n < count; ++n)
        {
            const size_t length = strlen(names[n]);

            if (length <= 4 || !EqualsNoCase(names[n] + length - 4, ".mkv"))
                continue;

            char path[320];
            char why[160];
            snprintf(path, sizeof(path), "%s/%s", folder, names[n]);

            if (!MoviePlayable(path, why, sizeof(why)))
                continue;

            bool taken = false;

            for (int m = 0; m < g_movieCount && !taken; ++m)
                taken = strlen(g_movies[m].name) == length - 4 && strncmp(g_movies[m].file, names[n], length - 4) == 0;

            if (taken || g_movieCount >= (int)(sizeof(g_movies) / sizeof(g_movies[0])))
                continue;

            Movie& movie = g_movies[g_movieCount++];
            snprintf(movie.file, sizeof(movie.file), "%s", names[n]);
            snprintf(movie.name, sizeof(movie.name), "%.*s", (int)(length - 4), names[n]);
            movie.package = i;
        }
    }
}

static void FindPreview(Package& package, const char* json)
{
    SceKernelStat st;
    char path[320];

    package.preview[0] = 0;
    snprintf(path, sizeof(path), "%s/usermaps/%s/previewimage.png", Data_Dir(), package.name);

    if (sceKernelStat(path, &st) == 0)
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

    snprintf(path, sizeof(path), "%s/usermaps/%s/%s", Data_Dir(), package.name, file);

    if (sceKernelStat(path, &st) == 0)
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

        char path[224];
        snprintf(path, sizeof(path), "%s/usermaps/%s/workshop.json", Data_Dir(), package.name);

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

        SceKernelStat st;
        char picture[224];
        char previewName[96];
        char loadingName[96];

        const bool preview = package.preview[0] != 0;
        snprintf(picture, sizeof(picture), "%s/usermaps/%s/loadingimage.png", Data_Dir(), package.name);
        const bool loading = sceKernelStat(picture, &st) == 0;
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

static char g_mapsTableLua[72000];

static void BuildMapsTableLua()
{
    char path[256];
    snprintf(path, sizeof(path), "%s/ui_scripts/maptable.lua", Data_Dir());

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

    for (int i = 0; i < g_movieCount; ++i)
    {
        Movie& movie = g_movies[i];
        const size_t movieLength = strlen(movie.name);

        if (movieLength != length - 4 || !SameNoCase(file, movie.name, movieLength))
            continue;

        snprintf(out, 256, "%s/usermaps/%s/video/%s", Data_Dir(), g_packages[movie.package].name, movie.file);
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

    g_base = base;

    ScanUsermaps();
    ScanSharedZones();
    ScanMovies();
    BuildCustomMapsLua();
    BuildMapsTableLua();

    if (!g_packageCount)
    {
        g_base = 0;
        return;
    }

    for (const SignaturePatch& patch : k_signaturePatches)
        RemoveSignaturePenalty(base, patch);

    Detour_Attach(&g_openDetour, (uint64_t)(base + kFileOpen), (void*)FileOpen_h, &g_openOriginal);
    Detour_Attach(&g_existsDetour, (uint64_t)(base + kMapExists), (void*)MapExists_h, &g_existsOriginal);
    Detour_Attach(&g_validDetour, (uint64_t)(base + kIsMapValid), (void*)IsMapValid_h, &g_validOriginal);
    Detour_Attach(&g_loadDetour, (uint64_t)(base + kLoadXAssets), (void*)LoadXAssets_h, &g_loadOriginal);

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
}

bool T7Maps_InLevel()
{
    return T7Maps::g_inLevel;
}

const char* T7Maps_CustomMapsLua()
{
    return T7Maps::g_customMapsLua;
}

const char* T7Maps_PreviewFile(const char* map)
{
    using namespace T7Maps;

    const int package = g_base && map ? MapPackage(map) : -1;
    return package >= 0 && g_packages[package].preview[0] ? g_packages[package].preview : nullptr;
}
