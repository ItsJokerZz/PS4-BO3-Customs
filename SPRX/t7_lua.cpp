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
constexpr uintptr_t kFindXAsset      = 0x8591B0;
constexpr uintptr_t kFrameLimitSkip  = 0xF0BBF3;
constexpr uintptr_t kSplitNarrow     = 0x6FD736;
constexpr uintptr_t kSplitLayouts    = 0x16094F0;
constexpr uintptr_t kViewValues      = 0x5E28D0;
constexpr uintptr_t kCgArray         = 0x2CFBA40;
constexpr uintptr_t kLocalClients    = 0x35EBFC0;
constexpr uintptr_t kCurrentMap      = 0xCD01DE0;
constexpr uintptr_t kTanf            = 0x121AA40;
constexpr uintptr_t kAtanf           = 0x121AA60;

constexpr uintptr_t kCgSize        = 3418304;
constexpr uintptr_t kCgAnimLens    = 0x2D8964;
constexpr uintptr_t kCgAdsFraction = 0x11ABCC;
constexpr uintptr_t kCgTanHalfFovX = 0x131DB8;
constexpr uintptr_t kCgTanHalfFovY = 0x131DBC;
constexpr uintptr_t kCgMainTanFovY = 0x131DC0;
constexpr uintptr_t kCgFovX        = 0x131DC4;

constexpr size_t  kDvarType     = 0x14;
constexpr size_t  kDvarValue    = 0x20;
constexpr int32_t kDvarTypeBool = 1;

constexpr const char* kSplitScreenProxy = "bo3customs_splitscreen";
constexpr const char* kSplitScreenDvar  = "splitscreen_horizontal";

constexpr float kStockFov = 65.0f;
constexpr float kDegToRad = 0.017453292f;

constexpr int32_t kRawFileType = 47;

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

static const char k_graphicsProbeLua[] =
    "local engine = rawget(_G, 'Engine')\n"
    "local cod = rawget(_G, 'CoD')\n"
    "if rawget(_G, 'BO3CustomsGraphics') ~= nil or engine == nil or type(cod) ~= 'table' then return end\n"
    "if type(rawget(cod, 'OptionsUtility')) ~= 'table' or type(rawget(_G, 'ListHelper_Prepare')) ~= 'function' then return end\n"
    "engine.SetDvar('bo3customs_graphics_pending', 1)\n";

static const char k_restartProbeLua[] =
    "local engine = rawget(_G, 'Engine')\n"
    "if rawget(_G, 'BO3CustomsRestart') ~= nil or engine == nil or type(rawget(_G, 'CoD')) ~= 'table' then return end\n"
    "if type(rawget(_G, 'ListHelper_Prepare')) ~= 'function' then return end\n"
    "engine.SetDvar('bo3customs_restart_pending', 1)\n";

static const char k_mouseProbeLua[] =
    "local engine = rawget(_G, 'Engine')\n"
    "if rawget(_G, 'BO3CustomsMouse') ~= nil or engine == nil or type(rawget(_G, 'LUI')) ~= 'table' then return end\n"
    "engine.SetDvar('bo3customs_mouse_pending', 1)\n";

static const char k_refreshedLua[] =
    "local tabs = rawget(_G, 'BO3CustomsMapTabs')\n"
    "if tabs ~= nil and tabs.Refreshed ~= nil then tabs.Refreshed() end\n";

static const char k_tickLua[] =
    "local tick = rawget(_G, 'BO3CustomsTick')\n"
    "if tick ~= nil then tick() end\n";

static const char k_menuWatchLua[] =
    "local G = _G\n"
    "if rawget(G, 'BO3CustomsMenuWatch') ~= nil then return end\n"
    "local cod = rawget(G, 'CoD')\n"
    "if type(cod) ~= 'table' then return end\n"
    "local menu = cod.Menu\n"
    "if type(menu) ~= 'table' then return end\n"
    "local add = menu.AddToCurrMenuNameList\n"
    "local drop = menu.RemoveFromCurrMenuNameList\n"
    "if type(add) ~= 'function' or type(drop) ~= 'function' then return end\n"
    "rawset(G, 'BO3CustomsMenuWatch', 'trying')\n"
    "rawset(G, 'BO3CustomsPreGame', 0)\n"
    "rawset(G, 'BO3CustomsBlocking', 0)\n"
    "local function named(...)\n"
    "  for i = 1, select('#', ...) do\n"
    "    local v = select(i, ...)\n"
    "    if type(v) == 'string' then return v end\n"
    "  end\n"
    "  return nil\n"
    "end\n"
    "local function kind(name)\n"
    "  if string.find(name, 'PreGame', 1, true) ~= nil then return 'BO3CustomsPreGame' end\n"
    "  if string.find(name, 'StartMenu', 1, true) ~= nil\n"
    "    or string.find(name, 'Pause', 1, true) ~= nil\n"
    "    or string.find(name, 'Options', 1, true) ~= nil then return 'BO3CustomsBlocking' end\n"
    "  return nil\n"
    "end\n"
    "local function bump(name, by)\n"
    "  local key = kind(name)\n"
    "  if key ~= nil then\n"
    "    local n = (rawget(G, key) or 0) + by\n"
    "    if n < 0 then n = 0 end\n"
    "    rawset(G, key, n)\n"
    "    local apply = rawget(G, 'BO3CustomsApplyIsPC')\n"
    "    if apply ~= nil then apply() end\n"
    "  end\n"
    "end\n"
    "local ok = pcall(function()\n"
    "  menu.AddToCurrMenuNameList = function(...)\n"
    "    local name = named(...)\n"
    "    if name ~= nil then bump(name, 1) end\n"
    "    return add(...)\n"
    "  end\n"
    "  menu.RemoveFromCurrMenuNameList = function(...)\n"
    "    local name = named(...)\n"
    "    if name ~= nil then bump(name, -1) end\n"
    "    return drop(...)\n"
    "  end\n"
    "end)\n"
    "rawset(G, 'BO3CustomsMenuWatch', ok and 'on' or 'failed')\n";

static const char k_isPcLua[] =
    "local G = _G\n"
    "local apply = rawget(G, 'BO3CustomsApplyIsPC')\n"
    "if apply == nil then\n"
    "  apply = function()\n"
    "    local engine = rawget(G, 'Engine')\n"
    "    local cod = rawget(G, 'CoD')\n"
    "    if engine == nil or engine.GetCurrentMap == nil or type(cod) ~= 'table' then return end\n"
    "    if rawget(G, 'BO3CustomsIsPCWas') == nil then rawset(G, 'BO3CustomsIsPCWas', { was = cod.isPC }) end\n"
    "    local saved = rawget(G, 'BO3CustomsIsPCWas')\n"
    "    local inMap = tostring(engine.GetCurrentMap()) ~= 'core_frontend'\n"
    "    local pre = rawget(G, 'BO3CustomsPreGame') or 0\n"
    "    local block = rawget(G, 'BO3CustomsBlocking') or 0\n"
    "    if inMap and pre > 0 and block == 0 then rawset(cod, 'isPC', true)\n"
    "    else rawset(cod, 'isPC', saved.was) end\n"
    "  end\n"
    "  rawset(G, 'BO3CustomsApplyIsPC', apply)\n"
    "end\n"
    "apply()\n";

static const char k_pcUtilityLua[] =
    "local G = _G\n"
    "if rawget(G, 'BO3CustomsPCUtility') ~= nil then return end\n"
    "local cod = rawget(G, 'CoD')\n"
    "if type(cod) ~= 'table' then return end\n"
    "rawset(G, 'BO3CustomsPCUtility', true)\n"
    "local ok, err = pcall(require, 'ui.t7.utility.pcutility')\n"
    "local util = rawget(cod, 'PCUtil')\n"
    "if type(util) ~= 'table' then util = rawget(G, 'PCUtil') end\n"
    "if type(util) == 'table' then\n"
    "  rawset(G, 'PCUtil', util)\n"
    "  rawset(cod, 'PCUtil', util)\n"
    "end\n";

static const char k_pcUtilLua[] =
    "local cod = rawget(_G, 'CoD')\n"
    "local have = rawget(_G, 'PCUtil')\n"
    "if have ~= nil then\n"
    "  if type(cod) == 'table' and rawget(cod, 'PCUtil') == nil then rawset(cod, 'PCUtil', have) end\n"
    "  return\n"
    "end\n"
    "local util = {}\n"
    "setmetatable(util, { __index = function(t, k)\n"
    "  local made = {}\n"
    "  rawset(t, k, made)\n"
    "  return made\n"
    "end })\n"
    "rawset(_G, 'PCUtil', util)\n"
    "if type(cod) == 'table' then rawset(cod, 'PCUtil', util) end\n";

static const char k_globalAliasLua[] =
    "local G = _G\n"
    "local cod, lui, engine = rawget(G, 'CoD'), rawget(G, 'LUI'), rawget(G, 'Engine')\n"
    "local names = { 'OverlayUtility', 'OverlayTypes', 'DataSources', 'DataSourceHelpers', 'Menu',\n"
    "  'OptionsList', 'OptionInfo', 'CategoryFrame', 'ChooseDecal', 'Board', 'UIElement', 'UIImage',\n"
    "  'LUIButton', 'ListSetup', 'GoBack', 'CreateModel', 'GetModel', 'GetModelValue',\n"
    "  'GetModelForController', 'SetModelValue', 'SubscribeToModelAndUpdateState',\n"
    "  'UnsubscribeAndFreeModel', 'Localize', 'Dvar', 'RegisterImage', 'SetOptionValue',\n"
    "  'SendMenuResponse', 'OptionsUtility', 'ButtonPrompts', 'PlayerOptions' }\n"
    "local function pick(name)\n"
    "  local sources = { cod, lui, engine }\n"
    "  for i = 1, #sources do\n"
    "    if type(sources[i]) == 'table' then\n"
    "      local value = rawget(sources[i], name)\n"
    "      if value ~= nil then return value end\n"
    "    end\n"
    "  end\n"
    "  return nil\n"
    "end\n"
    "for i = 1, #names do\n"
    "  if rawget(G, names[i]) == nil then\n"
    "    local value = pick(names[i])\n"
    "    if value ~= nil then\n"
    "      rawset(G, names[i], value)\n"
    "    end\n"
    "  end\n"
    "end\n"
    "local stubs = { 'OverlayTypes', 'DataSources', 'DataSourceHelpers', 'OptionsList', 'OptionInfo',\n"
    "  'CategoryFrame', 'PlayerOptions', 'Overlays' }\n"
    "for i = 1, #stubs do\n"
    "  local name = stubs[i]\n"
    "  if rawget(G, name) == nil then\n"
    "    local shim = {}\n"
    "    setmetatable(shim, { __index = function(t, k)\n"
    "      local value = {}\n"
    "      rawset(t, k, value)\n"
    "      return value\n"
    "    end })\n"
    "    rawset(G, name, shim)\n"
    "  end\n"
    "end\n"
    "local function level()\n"
    "  local e = rawget(G, 'Engine')\n"
    "  if e == nil or e.GetCurrentMap == nil then return nil end\n"
    "  local ok, map = pcall(e.GetCurrentMap)\n"
    "  if ok and type(map) == 'string' and map ~= '' and map ~= 'core_frontend' then return map end\n"
    "  return nil\n"
    "end\n"
    "local function usermaps()\n"
    "  if level() ~= nil then return 'usermaps' end\n"
    "  return ''\n"
    "end\n"
    "local mods = { Mods_IsUsingMods = function() return level() ~= nil end,\n"
    "  Mods_UsingModsUgcName = usermaps,\n"
    "  Mods_UsingModsInternalName = usermaps,\n"
    "  Mods_UsingModsVersion = function() return 0 end,\n"
    "  Mods_IsUsingUsermap = function() return level() ~= nil end,\n"
    "  Mods_UsingUsermapUgcName = function() return level() or '' end }\n"
    "for name, fn in pairs(mods) do\n"
    "  if rawget(G, name) == nil then rawset(G, name, fn) end\n"
    "end\n";

static const char k_zombieSeedLua[] =
    "local cod = rawget(_G, 'CoD')\n"
    "local zombie = nil\n"
    "if type(cod) == 'table' then zombie = rawget(cod, 'Zombie') end\n"
    "if type(zombie) ~= 'table' then return end\n"
    "if zombie.ZM_SUPPORT_BOARDS == nil then zombie.ZM_SUPPORT_BOARDS = { 'white' } end\n"
    "if zombie.ZM_SUPPORT_BOARDS_INDEX == nil then zombie.ZM_SUPPORT_BOARDS_INDEX = 1 end\n";

static uintptr_t g_base = 0;
static uint32_t  g_ticks = 0;
static uintptr_t g_pcUtilState = 0;
static uintptr_t g_mouseState = 0;

static Detour g_findAssetDetour{};
static void*  g_findAssetOriginal = nullptr;

using Passthrough_t = uint64_t (*)(uint64_t, uint64_t, uint64_t, uint64_t, uint64_t, uint64_t,
                                   double, double, double, double, double, double, double, double);

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
        T7Log_LuaFailure(L, topOffset, chunk, "compile");
        DropFailure(L, topOffset);
        return false;
    }

    using Pcall_t = int32_t (*)(uintptr_t, int32_t, int32_t, int32_t);

    if (((Pcall_t)(g_base + kLuaPcall))(L, 0, 0, 0) != 0)
    {
        T7Log_LuaFailure(L, topOffset, chunk, "run");
        DropFailure(L, topOffset);
        return false;
    }

    return true;
}

static char* ReadScript(const char* name)
{
    char path[256];
    snprintf(path, sizeof(path), "%s/ui_scripts/%s", "/data/BO3-Customs", name);

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

        if (DvarOn("bo3customs_refresh"))
        {
            SetDvar("bo3customs_refresh", "0");

            if (T7Maps_Refresh())
            {
                RunLua(T7Maps_CustomMapsLua(), "=bo3customs_maps");
                RunLua(k_refreshedLua, "=bo3customs_refreshed");
            }
        }

        RunLua(k_tickLua, "=bo3customs_tick");
    }

    UiUnlock();
}

struct LuiFile
{
    const char* asset;
    char*       data;
    int32_t     length;
    bool        tried;
};

struct RawFileAsset
{
    const char* name;
    int32_t     length;
    int32_t     padding;
    const char* buffer;
};

static const char* const k_graphicsDvars[] = { "r_vsync",
                                               "com_maxfps",
                                               "cg_drawFPS",
                                               "cg_fov",
                                               "r_modelLodLimit",
                                               "r_lightingSunShadowDisableDynamicDraw",
                                               "r_sssblurEnable",
                                               "r_volumetric_lighting_enabled",
                                               "r_aaTechnique",
                                               "r_ssaoTechnique",
                                               "r_motionBlurMode",
                                               kSplitScreenProxy };
constexpr int kGraphicsCount = sizeof(k_graphicsDvars) / sizeof(k_graphicsDvars[0]);

static char g_graphics[kGraphicsCount][32] = {};
static bool g_graphicsLoaded = false;

static void GraphicsPath(char* out, size_t size)
{
    snprintf(out, size, "%s/graphics.cfg", "/data/BO3-Customs");
}

static void LoadGraphics()
{
    g_graphicsLoaded = true;

    char path[256];
    GraphicsPath(path, sizeof(path));

    const int fd = sceKernelOpen(path, SCE_KERNEL_O_RDONLY, 0);

    if (fd < 0)
        return;

    char text[2048];
    const int64_t got = sceKernelRead(fd, text, sizeof(text) - 1);
    sceKernelClose(fd);

    if (got <= 0)
        return;

    text[got] = 0;

    for (char* line = text; line && *line;)
    {
        char* next = strchr(line, '\n');

        if (next)
            *next++ = 0;

        char* const space = strchr(line, ' ');

        if (space && line[0] != '/')
        {
            *space = 0;
            char* const value = space + 1;
            size_t n = strlen(value);

            while (n > 0 && (value[n - 1] == '\r' || value[n - 1] == ' '))
                value[--n] = 0;

            for (int i = 0; i < kGraphicsCount; ++i)
            {
                if (strcmp(line, k_graphicsDvars[i]) == 0)
                    snprintf(g_graphics[i], sizeof(g_graphics[i]), "%s", value);
            }
        }

        line = next;
    }
}

static void SaveGraphics()
{
    char path[256];
    char temp[270];
    GraphicsPath(path, sizeof(path));
    snprintf(temp, sizeof(temp), "%s.tmp", path);

    char text[2048];
    int n = 0;

    for (int i = 0; i < kGraphicsCount && n >= 0 && (size_t)n < sizeof(text); ++i)
    {
        if (g_graphics[i][0])
            n += snprintf(text + n, sizeof(text) - (size_t)n, "%s %s\n", k_graphicsDvars[i], g_graphics[i]);
    }

    if (n <= 0 || (size_t)n >= sizeof(text))
        return;

    sceKernelMkdir("/data/BO3-Customs", 0777);

    const int fd = sceKernelOpen(temp, SCE_KERNEL_O_WRONLY | SCE_KERNEL_O_CREAT | SCE_KERNEL_O_TRUNC, 0777);

    if (fd < 0)
        return;

    const int64_t put = sceKernelWrite(fd, text, (size_t)n);
    sceKernelClose(fd);

    if (put != n || sceKernelRename(temp, path) < 0)
        sceKernelUnlink(temp);
}

static const uint8_t k_frameLimitSkip[] = { 0x84, 0xC0, 0x74, 0x20, 0xB8, 0xE8, 0x03, 0x00, 0x00 };

static void PatchFrameLimit()
{
    const uintptr_t at = g_base + kFrameLimitSkip;
    const size_t size = sizeof(k_frameLimitSkip);

    if (!RangeReadable(at, size))
        return;

    const uint8_t* const code = (const uint8_t*)at;
    const bool around = memcmp(code, k_frameLimitSkip, 2) == 0 && memcmp(code + 4, k_frameLimitSkip + 4, size - 4) == 0;

    if (!around || code[2] != 0x74 || code[3] != 0x20)
        return;

    const uintptr_t page = (at + 2) & ~0x3FFFull;

    if (sceKernelMprotect((const void*)page, ((at + 3) & ~0x3FFFull) - page + 0x4000, 7) < 0)
        return;

    *(volatile uint16_t*)(at + 2) = 0x9090;
}

static const uint8_t k_splitNarrow[] = { 0x83, 0xF8, 0x01, 0x75, 0x28, 0xC7, 0x05, 0xEB,
                                         0xBE, 0xF0, 0x00, 0xCD, 0xCC, 0xCC, 0x3D };

static void FillSplitRow(float* row, float y, float h)
{
    if (row[1] == y && row[3] == h && row[2] > 0.0f)
    {
        row[0] = 0.0f;
        row[2] = 1.0f;
    }
}

static void PatchSplitScreen()
{
    const uintptr_t at = g_base + kSplitNarrow;
    const size_t size = sizeof(k_splitNarrow);

    if (!RangeReadable(at, size) || memcmp((const void*)at, k_splitNarrow, size) != 0)
        return;

    const uintptr_t page = (at + 3) & ~0x3FFFull;

    if (sceKernelMprotect((const void*)page, 0x4000, 7) < 0)
        return;

    *(volatile uint8_t*)(at + 3) = 0xEB;

    float* const wide = (float*)(g_base + kSplitLayouts) + 64;

    FillSplitRow(wide + 16, 0.0f, 0.5f);
    FillSplitRow(wide + 20, 0.5f, 0.5f);
    FillSplitRow(wide + 40, 0.5f, 0.5f);
}

static const uint8_t k_viewValues[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55,
                                        0x41, 0x54, 0x53, 0x48, 0x81, 0xEC, 0xB8, 0x01, 0x00, 0x00 };
static const uint8_t k_importStub[] = { 0xFF, 0x25 };

static Detour g_viewDetour{};
static void*  g_viewOriginal = nullptr;
static float  g_viewWiden = 0.0f;
static float  g_viewWidenFor = -1.0f;

using ViewValues_t = uint64_t (*)(uint64_t, uint64_t, uint64_t, uint64_t, uint64_t, uint64_t,
                                  double, double, double, double, double, double, double, double);
using Float_t = float (*)(float);

static float Tan(float x)
{
    return ((Float_t)(g_base + kTanf))(x);
}

static float Atan(float x)
{
    return ((Float_t)(g_base + kAtanf))(x);
}

static float ParseNumber(const char* text)
{
    float value = 0.0f;
    float step = 0.0f;

    for (const char* at = text; *at; ++at)
    {
        if (*at >= '0' && *at <= '9')
        {
            if (step == 0.0f)
            {
                value = value * 10.0f + (float)(*at - '0');
            }
            else
            {
                value += (float)(*at - '0') * step;
                step *= 0.1f;
            }
        }
        else if (*at == '.' && step == 0.0f)
        {
            step = 0.1f;
        }
        else
        {
            break;
        }
    }

    return value;
}

static bool InUiLevel()
{
    return strcmp((const char*)(g_base + kCurrentMap), "core_frontend") == 0;
}

static void WidenView(int32_t localClient)
{
    const float widen = g_viewWiden;

    if (widen <= 0.0f || localClient < 0 || localClient >= *(const int32_t*)(g_base + kLocalClients))
        return;

    const uintptr_t clients = *(const uintptr_t*)(g_base + kCgArray);

    if (!clients || InUiLevel())
        return;

    const uintptr_t cg = clients + (uintptr_t)localClient * kCgSize;

    if (*(const float*)(cg + kCgAnimLens) > 0.0f)
        return;

    float ads = *(const float*)(cg + kCgAdsFraction);

    if (!(ads >= 0.0f))
        ads = 0.0f;
    else if (ads > 1.0f)
        ads = 1.0f;

    const float scale = 1.0f + (widen - 1.0f) * (1.0f - ads);

    *(float*)(cg + kCgTanHalfFovX) *= scale;
    *(float*)(cg + kCgTanHalfFovY) *= scale;
    *(float*)(cg + kCgMainTanFovY) *= scale;

    float* const fov = (float*)(cg + kCgFovX);
    *fov = 2.0f * Atan(Tan(*fov * 0.5f * kDegToRad) * scale) / kDegToRad;
}

static uint64_t ViewValues(uint64_t localClient, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                           double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uint64_t result =
        ((ViewValues_t)g_viewOriginal)(localClient, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);

    float fov;
    memcpy(&fov, &x0, sizeof(fov));

    if (fov < 0.0f)
        WidenView((int32_t)localClient);

    return result;
}

static void HookView()
{
    const uintptr_t at = g_base + kViewValues;

    const bool matches = RangeReadable(at, sizeof(k_viewValues)) &&
                         memcmp((const void*)at, k_viewValues, sizeof(k_viewValues)) == 0 &&
                         RangeReadable(g_base + kTanf, sizeof(k_importStub)) &&
                         memcmp((const void*)(g_base + kTanf), k_importStub, sizeof(k_importStub)) == 0 &&
                         RangeReadable(g_base + kAtanf, sizeof(k_importStub)) &&
                         memcmp((const void*)(g_base + kAtanf), k_importStub, sizeof(k_importStub)) == 0;

    if (matches)
        Detour_Attach(&g_viewDetour, (uint64_t)at, (void*)ViewValues, &g_viewOriginal);
}

static void UpdateViewWiden()
{
    const float fov = g_graphics[3][0] ? ParseNumber(g_graphics[3]) : 0.0f;

    if (fov == g_viewWidenFor)
        return;

    g_viewWidenFor = fov;

    if (!g_viewOriginal || fov < 10.0f || fov > 160.0f || fov == kStockFov)
    {
        g_viewWiden = 0.0f;
        return;
    }

    g_viewWiden = Tan(fov * 0.5f * kDegToRad) / Tan(kStockFov * 0.5f * kDegToRad);
}

static bool NameListed(const char* list, const char* name)
{
    const size_t length = strlen(name);

    for (const char* at = list; *at;)
    {
        while (*at == ' ')
            ++at;

        const char* end = at;

        while (*end && *end != ' ')
            ++end;

        if ((size_t)(end - at) == length && strncmp(at, name, length) == 0)
            return true;

        at = end;
    }

    return false;
}

static void ApplySplitScreen()
{
    const uintptr_t proxy = FindDvar(kSplitScreenProxy);
    const uintptr_t dvar = FindDvar(kSplitScreenDvar);

    if (!proxy || !dvar || *(const int32_t*)(dvar + kDvarType) != kDvarTypeBool)
        return;

    const char* const text = DvarText(proxy);
    const uint8_t wanted = text && atoi(text) != 0 ? 1 : 0;

    if (*(const uint8_t*)(dvar + kDvarValue) == wanted)
        return;

    SetDvar(kSplitScreenDvar, wanted ? "1" : "0");
    *(volatile uint8_t*)(dvar + kDvarValue) = wanted;
}

static void PollGraphics()
{
    if (!g_graphicsLoaded)
        LoadGraphics();

    UpdateViewWiden();

    const uintptr_t save = FindDvar("bo3customs_graphics_save");
    const char* const pending = save ? DvarText(save) : nullptr;

    if (pending && *pending && strcmp(pending, "0") != 0)
    {
        char names[512];
        snprintf(names, sizeof(names), "%s", pending);
        SetDvar("bo3customs_graphics_save", "0");

        const bool all = atoi(names) != 0;

        for (int i = 0; i < kGraphicsCount; ++i)
        {
            if (!all && !NameListed(names, k_graphicsDvars[i]))
                continue;

            const uintptr_t dvar = FindDvar(k_graphicsDvars[i]);
            const char* const text = dvar ? DvarText(dvar) : nullptr;

            if (text && *text)
                snprintf(g_graphics[i], sizeof(g_graphics[i]), "%s", text);
        }

        SaveGraphics();
        UpdateViewWiden();
        ApplySplitScreen();
        return;
    }

    for (int i = 0; i < kGraphicsCount; ++i)
    {
        if (!g_graphics[i][0])
            continue;

        const uintptr_t dvar = FindDvar(k_graphicsDvars[i]);
        const char* const text = dvar ? DvarText(dvar) : nullptr;

        if (!dvar && strncmp(k_graphicsDvars[i], "bo3customs_", 11) == 0)
            SetDvar(k_graphicsDvars[i], g_graphics[i]);
        else if (text && strcmp(text, g_graphics[i]) != 0)
            SetDvar(k_graphicsDvars[i], g_graphics[i]);
    }

    ApplySplitScreen();
}

static LuiFile g_luiFiles[] =
{
    { "ui/t7/utility/pcutility.lua", nullptr, 0, false },
};

static RawFileAsset g_luiAssets[sizeof(g_luiFiles) / sizeof(g_luiFiles[0])] = {};

constexpr size_t kLuiFileCount = sizeof(g_luiFiles) / sizeof(g_luiFiles[0]);

static const RawFileAsset* LuiFileFor(const char* name)
{
    for (size_t i = 0; i < kLuiFileCount; ++i)
    {
        LuiFile& file = g_luiFiles[i];

        if (strcmp(name, file.asset) != 0)
            continue;

        if (!file.tried)
        {
            file.tried = true;

            char path[256];
            snprintf(path, sizeof(path), "%s/lui/%s", "/data/BO3-Customs", file.asset);

            const int fd = sceKernelOpen(path, SCE_KERNEL_O_RDONLY, 0);

            if (fd < 0)
                return nullptr;

            constexpr int32_t kMax = 1024 * 1024;
            char* const body = (char*)malloc(kMax);
            int32_t used = 0;

            while (body != nullptr && used < kMax)
            {
                const int64_t read = sceKernelRead(fd, body + used, kMax - used);

                if (read <= 0)
                    break;

                used += (int32_t)read;
            }

            sceKernelClose(fd);

            if (body == nullptr || used == 0)
            {
                free(body);
                return nullptr;
            }

            file.data = body;
            file.length = used;
            g_luiAssets[i].name = file.asset;
            g_luiAssets[i].length = used;
            g_luiAssets[i].padding = 0;
            g_luiAssets[i].buffer = body;
        }

        return file.data != nullptr ? &g_luiAssets[i] : nullptr;
    }

    return nullptr;
}

static uint64_t FindXAsset(uint64_t type, uint64_t name, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                           double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    char asset[192];
    bool lua = false;

    if ((int32_t)type == kRawFileType && name != 0 && RangeReadable(name, 16))
    {
        const char* const text = (const char*)name;
        size_t length = 0;

        while (length < sizeof(asset) - 1 && text[length] != 0)
            ++length;

        if (length > 4 && memcmp(text + length - 4, ".lua", 4) == 0)
        {
            memcpy(asset, text, length);
            asset[length] = 0;
            lua = true;
            T7Log_LuiFile(asset);

            if (const RawFileAsset* const ours = LuiFileFor(asset))
            {
                T7Log_LuiBuffer(asset, (uintptr_t)ours);
                return (uint64_t)ours;
            }
        }
    }

    const uint64_t result =
        ((Passthrough_t)g_findAssetOriginal)(type, name, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);

    if (lua)
        T7Log_LuiBuffer(asset, (uintptr_t)result);

    return result;
}

static void HookFindXAsset()
{
    static const uint8_t k_findXAsset[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                            0x53, 0x48, 0x83, 0xEC, 0x68, 0x48, 0x8B, 0x05 };

    if (!g_base || !RangeReadable(g_base + kFindXAsset, sizeof(k_findXAsset)) ||
        memcmp((const void*)(g_base + kFindXAsset), k_findXAsset, sizeof(k_findXAsset)) != 0)
        return;

    Detour_Attach(&g_findAssetDetour, (uint64_t)(g_base + kFindXAsset), (void*)FindXAsset, &g_findAssetOriginal);
}

static void AddPcUtil()
{
    const uintptr_t L = g_base != 0 ? *(const uintptr_t*)(g_base + kLuaState) : 0;
    const bool fresh = L != g_pcUtilState;

    if (L == 0 || (!fresh && (g_ticks % 6) != 0) || !UiUp() || !UiTryLock())
        return;

    if (UiUp())
    {
        if (fresh)
            T7Log_Write("[Lua] new UI state for %s", (const char*)(g_base + kCurrentMap));

        RunLua(k_menuWatchLua, "=bo3customs_menuwatch");
        RunLua(k_isPcLua, "=bo3customs_ispc");
        RunLua(k_pcUtilityLua, "=bo3customs_pcutility");
        RunLua(k_pcUtilLua, "=bo3customs_pcutil");
        RunLua(k_globalAliasLua, "=bo3customs_alias");
        RunLua(k_zombieSeedLua, "=bo3customs_seed");
        g_pcUtilState = L;

        RunLua(k_graphicsProbeLua, "=bo3customs_graphics_probe");

        if (DvarOn("bo3customs_graphics_pending"))
        {
            SetDvar("bo3customs_graphics_pending", "0");

            char* const script = ReadScript("graphics.lua");

            if (script)
            {
                RunLua(script, "@ui_scripts/graphics.lua");
                free(script);
            }
        }

        RunLua(k_restartProbeLua, "=bo3customs_restart_probe");

        if (DvarOn("bo3customs_restart_pending"))
        {
            SetDvar("bo3customs_restart_pending", "0");

            char* const script = ReadScript("restart.lua");

            if (script)
            {
                RunLua(script, "@ui_scripts/restart.lua");
                free(script);
            }
        }

        RunLua(k_mouseProbeLua, "=bo3customs_mouse_probe");

        if (DvarOn("bo3customs_mouse_pending"))
        {
            SetDvar("bo3customs_mouse_pending", "0");

            char* const strings = ReadScript("kbm_strings.lua");

            if (strings)
            {
                RunLua(strings, "@ui_scripts/kbm_strings.lua");
                free(strings);
            }

            char* const script = ReadScript("mouse.lua");

            if (script)
            {
                if (RunLua(script, "@ui_scripts/mouse.lua"))
                    g_mouseState = L;

                free(script);
            }

            T7Log_Write("[Lua] our scripts went into the UI state for %s", (const char*)(g_base + kCurrentMap));
        }
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

    HookFindXAsset();
    PatchFrameLimit();
    PatchSplitScreen();
    HookView();
}

void T7Lua_Tick()
{
    using namespace T7Lua;

    if (!g_base)
        return;

    ++g_ticks;

    AddPcUtil();

    if ((g_ticks % 30) != 0)
        return;

    PollGraphics();

    if (InMenus())
        InjectScripts();
}

bool T7Lua_MouseReady()
{
    using namespace T7Lua;

    if (!g_base || !UiUp())
        return false;

    const uintptr_t L = *(const uintptr_t*)(g_base + kLuaState);
    return L != 0 && L == g_mouseState;
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
        T7Log_LuaFailure(L, topOffset, chunk, "compile");
        DropFailure(L, topOffset);
        return false;
    }

    const uint64_t top = *(const uint64_t*)(L + kStateTop);
    memcpy((void*)top, (const void*)(top - 32), 16);
    *(uint64_t*)(L + kStateTop) = top + 16;

    using Pcall_t = int32_t (*)(uintptr_t, int32_t, int32_t, int32_t);

    if (((Pcall_t)(g_base + kLuaPcall))(L, 1, 0, 0) != 0)
    {
        T7Log_LuaFailure(L, topOffset, chunk, "run");
        DropFailure(L, topOffset);
        return false;
    }

    return true;
}
