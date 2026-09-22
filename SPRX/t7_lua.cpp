#include "headers.hpp"
#include "t7_lua.hpp"
#include "t7_maps.hpp"

namespace T7Lua
{
constexpr uintptr_t kLuaState        = 0xE9831D0;
constexpr uintptr_t kUiReady         = 0xF7969C8;
constexpr uintptr_t kLockWords       = 0xCF60A50;
constexpr int32_t   kUiLock          = 53;
constexpr uintptr_t kHksLoad         = 0xC41D70;
constexpr uintptr_t kHksBufferReader = 0xC41D50;
constexpr uintptr_t kLuaPcall        = 0xC27C40;
constexpr uintptr_t kLuaGrowStack    = 0xC1BC40;
constexpr uintptr_t kDvarFind        = 0xFB6ED0;
constexpr uintptr_t kDvarString      = 0xFB69C0;
constexpr uintptr_t kDvarSetByName   = 0xFBC5A0;

constexpr size_t kStateGlobal    = 16;
constexpr size_t kStateTop       = 72;
constexpr size_t kStateStackLast = 88;
constexpr size_t kStateStack     = 96;
constexpr size_t kGlobalSharing  = 472;
constexpr size_t kGlobalCompiler = 1384;

constexpr int32_t kSharingOn = 1;

static const char k_probeLua[] =
    "local engine = rawget(_G, 'Engine')\n"
    "local cod = rawget(_G, 'CoD')\n"
    "if rawget(_G, 'BO3CustomsMapTabs') ~= nil or engine == nil or cod == nil then return end\n"
    "if engine.GetCurrentMap == nil or engine.GetCurrentMap() ~= 'core_frontend' then return end\n"
    "if rawget(_G, 'DataSources') == nil or rawget(_G, 'DataSourceHelpers') == nil then return end\n"
    "engine.SetDvar('bo3customs_pending', 1)\n";

static const char k_tickLua[] =
    "local tick = rawget(_G, 'BO3CustomsTick')\n"
    "if tick ~= nil then tick() end\n";

static uintptr_t g_base = 0;
static uint32_t  g_ticks = 0;

extern "C" uint64_t T7Lua_ThreadPointer();

static uintptr_t FindDvar(const char* name)
{
    return ((uintptr_t (*)(const char*))(g_base + kDvarFind))(name);
}

static void SetDvar(const char* name, const char* value)
{
    ((void (*)(const char*, const char*, uint32_t, uint32_t))(g_base + kDvarSetByName))(name, value, 0, 0);
}

static const char* DvarText(uintptr_t dvar)
{
    return ((const char* (*)(uintptr_t))(g_base + kDvarString))(dvar);
}

static bool DvarOn(const char* name)
{
    const uintptr_t dvar = FindDvar(name);
    const char* const text = dvar ? DvarText(dvar) : nullptr;
    return text && atoi(text) != 0;
}

static bool UiTryLock()
{
    const uint64_t tls = T7Lua_ThreadPointer();
    int32_t* const depth = (int32_t*)(tls - 576 + 4 * (uint64_t)kUiLock);

    if (*depth > 0)
    {
        ++(*depth);
        return true;
    }

    volatile int32_t* const word = (volatile int32_t*)(g_base + kLockWords + 4 * (uintptr_t)kUiLock);

    if (!__sync_bool_compare_and_swap(word, 0, 1))
        return false;

    *depth = 1;
    return true;
}

static void UiUnlock()
{
    const uint64_t tls = T7Lua_ThreadPointer();
    int32_t* const depth = (int32_t*)(tls - 576 + 4 * (uint64_t)kUiLock);

    if (--(*depth) == 0)
        *(volatile int32_t*)(g_base + kLockWords + 4 * (uintptr_t)kUiLock) = 0;
}

static bool UiUp()
{
    return (*(const uint8_t*)(g_base + kUiReady) & 1) != 0 && *(const uintptr_t*)(g_base + kLuaState) != 0;
}

struct HksBuffer
{
    const char* data;
    uint64_t    size;
};

static void DropFailure(uintptr_t L, uint64_t topOffset)
{
    *(uint64_t*)(L + kStateTop) = *(const uint64_t*)(L + kStateStack) + topOffset;
}

static bool InMenus()
{
    return !T7Maps_InLevel();
}

static bool RunLua(const char* source, const char* chunk)
{
    const uintptr_t L = *(const uintptr_t*)(g_base + kLuaState);
    const uintptr_t global = L ? *(const uintptr_t*)(L + kStateGlobal) : 0;

    if (!L || !global || !source)
        return false;

    if (*(const uint64_t*)(L + kStateTop) + 32 > *(const uint64_t*)(L + kStateStackLast))
        ((void (*)(uintptr_t, uintptr_t, uint64_t))(g_base + kLuaGrowStack))(L + 24, L, 2);

    const uint64_t topOffset = *(const uint64_t*)(L + kStateTop) - *(const uint64_t*)(L + kStateStack);

    HksBuffer buffer = { source, strlen(source) };
    int32_t* const sharing = (int32_t*)(global + kGlobalSharing);
    const int32_t sharingWas = *sharing;

    *sharing = kSharingOn;

    using Load_t = int32_t (*)(uintptr_t, uintptr_t, uintptr_t, HksBuffer*, const char*);
    const int32_t loaded = ((Load_t)(g_base + kHksLoad))(L, global + kGlobalCompiler, g_base + kHksBufferReader,
                                                         &buffer, chunk);

    *sharing = sharingWas;

    if (loaded != 0)
    {
        DropFailure(L, topOffset);
        return false;
    }

    using Pcall_t = int32_t (*)(uintptr_t, int32_t, int32_t, int32_t);

    if (((Pcall_t)(g_base + kLuaPcall))(L, 0, 0, 0) != 0)
    {
        DropFailure(L, topOffset);
        return false;
    }

    return true;
}

static char* ReadScript(const char* name)
{
    char path[256];
    snprintf(path, sizeof(path), "%s/ui_scripts/%s", Data_Dir(), name);

    const int fd = sceKernelOpen(path, SCE_KERNEL_O_RDONLY, 0);

    if (fd < 0)
        return nullptr;

    constexpr size_t kMax = 256 * 1024;
    char* const text = (char*)malloc(kMax + 1);
    size_t done = 0;

    while (text && done < kMax)
    {
        const int64_t got = sceKernelRead(fd, text + done, kMax - done);

        if (got <= 0)
            break;

        done += (size_t)got;
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

static void InjectScripts()
{
    if (!UiUp() || !UiTryLock())
        return;

    if (UiUp())
    {
        RunLua(k_probeLua, "=bo3customs_probe");

        if (DvarOn("bo3customs_pending"))
        {
            SetDvar("bo3customs_pending", "0");

            char* const script = ReadScript("mapselect.lua");

            if (script)
            {
                RunLua(T7Maps_CustomMapsLua(), "=bo3customs_maps");
                RunLua(script, "@ui_scripts/mapselect.lua");
                free(script);
            }
        }

        RunLua(k_tickLua, "=bo3customs_tick");
    }

    UiUnlock();
}
}

__asm__(
    ".text\n"
    ".globl T7Lua_ThreadPointer\n"
    "T7Lua_ThreadPointer:\n"
    "    movq %fs:0, %rax\n"
    "    retq\n");

void T7Lua_Install(uintptr_t base)
{
    using namespace T7Lua;

    static bool installed = false;

    if (installed || !base)
        return;

    installed = true;
    g_base = base;
}

void T7Lua_Tick()
{
    using namespace T7Lua;

    if (!g_base)
        return;

    if ((++g_ticks % 30) != 0)
        return;

    if (InMenus())
        InjectScripts();
}

bool T7Lua_RunOnTop(uintptr_t L, const char* source, const char* chunk)
{
    using namespace T7Lua;

    const uintptr_t global = L ? *(const uintptr_t*)(L + kStateGlobal) : 0;

    if (!g_base || !L || !global || !source)
        return false;

    if (*(const uint64_t*)(L + kStateTop) + 48 > *(const uint64_t*)(L + kStateStackLast))
        ((void (*)(uintptr_t, uintptr_t, uint64_t))(g_base + kLuaGrowStack))(L + 24, L, 3);

    const uint64_t topOffset = *(const uint64_t*)(L + kStateTop) - *(const uint64_t*)(L + kStateStack);

    if (topOffset < 16)
        return false;

    HksBuffer buffer = { source, strlen(source) };
    int32_t* const sharing = (int32_t*)(global + kGlobalSharing);
    const int32_t sharingWas = *sharing;

    *sharing = kSharingOn;

    using Load_t = int32_t (*)(uintptr_t, uintptr_t, uintptr_t, HksBuffer*, const char*);
    const int32_t loaded = ((Load_t)(g_base + kHksLoad))(L, global + kGlobalCompiler, g_base + kHksBufferReader,
                                                         &buffer, chunk);

    *sharing = sharingWas;

    if (loaded != 0)
    {
        DropFailure(L, topOffset);
        return false;
    }

    const uint64_t top = *(const uint64_t*)(L + kStateTop);
    memcpy((void*)top, (const void*)(top - 32), 16);
    *(uint64_t*)(L + kStateTop) = top + 16;

    using Pcall_t = int32_t (*)(uintptr_t, int32_t, int32_t, int32_t);

    if (((Pcall_t)(g_base + kLuaPcall))(L, 1, 0, 0) != 0)
    {
        DropFailure(L, topOffset);
        return false;
    }

    return true;
}
