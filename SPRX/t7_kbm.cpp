#include "headers.hpp"
#include "t7_kbm.hpp"
#include "t7_kbm_strings.hpp"

#include <mouse.h>
#include <libime.h>
#include <libsysmodule.h>
#include <user_service.h>

namespace T7Kbm
{
constexpr uintptr_t kImeHandler      = 0xECA350;
constexpr uintptr_t kInputFrame      = 0xEC9520;
constexpr uintptr_t kCreateCmd       = 0x798330;
constexpr uintptr_t kKeyEvent        = 0x7B77C0;
constexpr uintptr_t kExecBinding     = 0x79DCB0;
constexpr uintptr_t kSetLastInput    = 0xEE86E0;
constexpr uintptr_t kInputNotify     = 0xEE8590;
constexpr uintptr_t kPadBindings     = 0x966A30;
constexpr uintptr_t kPadNative       = 0xE81AC0;
constexpr uintptr_t kMouseNative     = 0xE81B90;
constexpr uintptr_t kBindingText     = 0xFA3280;
constexpr uintptr_t kSpecialText     = 0xFA35D0;
constexpr uintptr_t kKeyName         = 0x7BC9F0;
constexpr uintptr_t kLocalize        = 0xF8E430;
constexpr uintptr_t kCbufAddText     = 0xEE44C0;
constexpr uintptr_t kDvarInt         = 0xFB72D0;
constexpr uintptr_t kLanguageDvar    = 0xCC3DEF0;
constexpr uintptr_t kDvarFind        = 0xFB6ED0;
constexpr uintptr_t kDvarString      = 0xFB69C0;
constexpr uintptr_t kDvarSetByName   = 0xFBC5A0;
constexpr uintptr_t kFrameTime       = 0xBC20570;
constexpr uintptr_t kLocalClientMap  = 0xBBFFBB0;
constexpr uintptr_t kPrimaryClient   = 0x161DA80;
constexpr uintptr_t kGamepads        = 0xB8DC1E0;
constexpr uintptr_t kClientUi        = 0x35E7DE0;
constexpr uintptr_t kClientActive    = 0x35EBFC8;
constexpr uintptr_t kKButtons        = 0x3592B80;
constexpr uintptr_t kKeyStates       = 0x35E2C20;
constexpr uintptr_t kInputLock       = 0xB233890;
constexpr uintptr_t kClsFrameTime    = 0x35EC0F4;
constexpr uintptr_t kCgArray         = 0x2CFBA40;
constexpr uintptr_t kLuaState        = 0xE9831D0;
constexpr uintptr_t kLuiEventBegin   = 0x11CC0A0;
constexpr uintptr_t kLuiEventEnd     = 0x11CE220;
constexpr uintptr_t kLuiRoot         = 0x11C7060;
constexpr uintptr_t kLuiString       = 0xDA7820;
constexpr uintptr_t kLuiFloat        = 0xDA7710;
constexpr uintptr_t kLuiInt          = 0xDA73E0;
constexpr uintptr_t kLuiBool         = 0xDA6FD0;
constexpr uintptr_t kRootSplit       = 0x16132C0;
constexpr uintptr_t kRootShared      = 0xB23349C;
constexpr uintptr_t kRootPerPlayer   = 0xB2334C0;

constexpr uintptr_t kRootPerPlayerSize = 176;
constexpr uintptr_t kRootPerPlayerName = 140;
constexpr uintptr_t kRootPerPlayerUsed = 172;
constexpr uintptr_t kRootWidth         = 0x90;
constexpr uintptr_t kRootHeight        = 0x94;
constexpr uintptr_t kCursorX           = 0xC;
constexpr uintptr_t kCursorY           = 0x10;
constexpr size_t    kEventPopulated    = 53;

constexpr uintptr_t kLocalClientMapSize = 32;
constexpr uintptr_t kMapClient          = 4;
constexpr uintptr_t kMapController      = 8;
constexpr uintptr_t kMapLastInput       = 0x18;
constexpr uintptr_t kClientUiSize       = 0x1078;
constexpr uintptr_t kClientActiveSize   = 0x197A40;
constexpr uintptr_t kKButtonsSize       = 0x468;
constexpr uintptr_t kKeyStatesSize      = 0x100C;
constexpr uintptr_t kInputLockSize      = 0x14;
constexpr uintptr_t kCgSize             = 3418304;
constexpr uintptr_t kGamepadSize        = 0x80;
constexpr uintptr_t kPadButtons         = 0x2;
constexpr uintptr_t kPadPrevious        = 0x4;
constexpr uintptr_t kPadLeftTrigger     = 0x8;
constexpr uintptr_t kPadRightTrigger    = 0xC;
constexpr uintptr_t kPadSticks          = 0x18;
constexpr uintptr_t kPadUser            = 0x64;

constexpr uintptr_t kStateTop   = 0x48;
constexpr uintptr_t kStateBase  = 0x50;
constexpr uint32_t  kTypeBool   = 1;
constexpr uint32_t  kTypeNumber = 3;

constexpr uintptr_t kClPlayerFlags = 0xC0;
constexpr uintptr_t kClViewMode    = 0x10C;
constexpr uintptr_t kClLookBlock   = 0xA34;
constexpr uintptr_t kClViewPitch   = 0xB8D8;
constexpr uintptr_t kClViewYaw     = 0xB8DC;
constexpr uint64_t  kClNoLookFlags = 0x400000010000000ULL;
constexpr int32_t   kClFrozenView  = 63;

constexpr uintptr_t kCgAdsFraction = 0x11ABCC;
constexpr uintptr_t kCgTanHalfFovY = 0x131DBC;

constexpr uintptr_t kKeyBindings = 0x10;
constexpr uintptr_t kKeyStride   = 16;

constexpr uintptr_t kController = 0;

static const char kConfigPath[] = "/data/BO3-Customs/kbm.cfg";

constexpr int32_t kCatchMenus       = 0x18;
constexpr int32_t kCatchLui         = 0x10;
constexpr int32_t kConnectionActive = 11;

constexpr int32_t kInputPad         = 0;
constexpr int32_t kInputMouseMove   = 1;
constexpr int32_t kInputMouseButton = 2;
constexpr int32_t kInputKeyboard    = 3;
constexpr int32_t kInputRemotePad   = 4;
constexpr uint32_t kInputSwitchMs   = 200;

constexpr int32_t kPadA     = 1;
constexpr int32_t kPadB     = 2;
constexpr int32_t kPadUp    = 22;
constexpr int32_t kPadDown  = 23;
constexpr int32_t kPadLeft  = 24;
constexpr int32_t kPadRight = 25;

constexpr int32_t kKeyTab       = 9;
constexpr int32_t kKeyEnter     = 13;
constexpr int32_t kKeyEscape    = 27;
constexpr int32_t kKeySpace     = 32;
constexpr int32_t kKeyConsole   = '`';
constexpr int32_t kKeyBackspace = 127;
constexpr int32_t kKeyPause     = 153;
constexpr int32_t kKeyUp        = 154;
constexpr int32_t kKeyDown      = 155;
constexpr int32_t kKeyLeft      = 156;
constexpr int32_t kKeyRight     = 157;
constexpr int32_t kKeyCtrl      = 159;
constexpr int32_t kKeyShift     = 160;
constexpr int32_t kKeyKpUp      = 183;
constexpr int32_t kKeyKpLeft    = 185;
constexpr int32_t kKeyKpRight   = 187;
constexpr int32_t kKeyKpDown    = 189;
constexpr int32_t kKeyKpEnter   = 191;
constexpr int32_t kKeyMouse1    = 200;
constexpr int32_t kKeyMouse2    = 201;
constexpr int32_t kKeyMouse3    = 202;
constexpr int32_t kKeyWheelDown = 205;
constexpr int32_t kKeyWheelUp   = 206;
constexpr uint8_t kSwallowed    = 255;

constexpr int32_t kBindFrag  = 5;
constexpr int32_t kBindSmoke = 7;

constexpr int32_t  kMouseLoggedOut  = (int32_t)0x803B0101;
constexpr int32_t  kMouseRecords    = 16;
constexpr int32_t  kMouseButtons    = 5;
constexpr uint32_t kMouseButtonMask = 0x1F;
constexpr int32_t  kMouseMoveReport = 3;
constexpr uint32_t kRetryFrames     = 120;
constexpr uint32_t kSettingsFrames  = 30;
constexpr uint32_t kOpenLogs        = 6;
constexpr uint32_t kProneHoldMs     = 500;

constexpr float kDefaultSensitivity = 5.0f;
constexpr float kDegreesPerCount    = 0.022f;
constexpr float kMinZoomScale       = 0.05f;
constexpr float kCursorPerCountX    = 1.0f / 2560.0f;
constexpr float kCursorPerCountY    = 1.0f / 1440.0f;
constexpr float kCursorHidden       = -1.0f;
constexpr float kStickActive        = 0.25f;
constexpr float kTriggerActive      = 0.1f;
constexpr float kAccelRange         = 50.0f;
constexpr float kFilterRange        = 20.0f;
constexpr float kFilterMost         = 0.5f;

static const uint8_t k_imeHandler[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x53, 0x50, 0x48, 0x89, 0xF3 };
static const uint8_t k_inputFrame[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                        0x53, 0x48, 0x83, 0xE4, 0xE0, 0x48, 0x81, 0xEC, 0x60, 0x01, 0x00, 0x00 };
static const uint8_t k_createCmd[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                       0x53, 0x48, 0x81, 0xEC, 0x88, 0x01, 0x00, 0x00 };
static const uint8_t k_keyEvent[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                      0x53, 0x48, 0x83, 0xEC, 0x38, 0x41, 0x89, 0xF6 };
static const uint8_t k_execBinding[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                         0x53, 0x48, 0x83, 0xEC, 0x18, 0x41, 0x89, 0xFD };
static const uint8_t k_setLastInput[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x54, 0x53, 0x41,
                                          0x89, 0xFE };
static const uint8_t k_inputNotify[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                         0x53, 0x48, 0x83, 0xEC, 0x68, 0x41, 0x89, 0xF6 };
static const uint8_t k_padBindings[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x56, 0x53, 0x89, 0xFB, 0xBF, 0x30, 0x00,
                                         0x00, 0x00 };
static const uint8_t k_lastInputNative[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x53, 0x48, 0x83, 0xEC,
                                             0x18 };
static const uint8_t k_bindingText[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                         0x53, 0x48, 0x81, 0xEC, 0x28, 0x01, 0x00, 0x00 };
static const uint8_t k_specialText[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                         0x53, 0x48, 0x81, 0xEC, 0x88, 0x00, 0x00, 0x00 };
static const uint8_t k_keyName[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x53, 0x50, 0x41, 0x89, 0xFE };
static const uint8_t k_localize[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x53, 0x50, 0x49, 0x89, 0xFE };
static const uint8_t k_cbufAddText[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                         0x53, 0x48, 0x83, 0xEC, 0x18 };
static const uint8_t k_dvarInt[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x56, 0x53, 0x48, 0x89, 0xFB, 0x31, 0xC0,
                                     0x48, 0x85, 0xDB };
static const uint8_t k_luiEventBegin[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                           0x53, 0x48, 0x83, 0xEC, 0x48 };
static const uint8_t k_luiEventEnd[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x54, 0x53, 0x48,
                                         0x89, 0xFB, 0x80, 0x7B, 0x34, 0x00 };
static const uint8_t k_luiRoot[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x54, 0x53, 0x48,
                                     0x89, 0xF3, 0x49, 0x89, 0xFE };
static const uint8_t k_luiString[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x54, 0x53, 0x48,
                                       0x83, 0xEC, 0x20 };
static const uint8_t k_luiField[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x53, 0x48, 0x83, 0xEC, 0x18 };

struct KButton
{
    int32_t  down[2];
    uint32_t downtime;
    uint32_t msec;
    uint8_t  active;
    uint8_t  wasPressed;
    uint8_t  reserved[2];
    float    value;
};

static_assert(sizeof(KButton) == 24, "kbutton_t is 24 bytes");

struct alignas(16) LuiEvent
{
    uint8_t bytes[128];
};

enum CommandKind : uint8_t
{
    kKindNone,
    kKindEngine,
    kKindButton,
    kKindScores,
    kKindProne,
    kKindHero,
    kKindWeapPrev,
};

struct Command
{
    const char* name;
    CommandKind kind;
    int32_t     value;
};

static const Command k_commands[] =
{
    { "",                    kKindNone,     0 },
    { "+forward",            kKindButton,   48 },
    { "+back",               kKindButton,   72 },
    { "+moveleft",           kKindButton,   144 },
    { "+moveright",          kKindButton,   168 },
    { "+left",               kKindButton,   0 },
    { "+right",              kKindButton,   24 },
    { "+lookup",             kKindButton,   96 },
    { "+lookdown",           kKindButton,   120 },
    { "+strafe",             kKindButton,   192 },
    { "+gostand",            kKindEngine,   27 },
    { "+stance",             kKindEngine,   25 },
    { "gocrouch",            kKindEngine,   25 },
    { "togglecrouch",        kKindEngine,   25 },
    { "+movedown",           kKindEngine,   25 },
    { "slide",               kKindEngine,   25 },
    { "goprone",             kKindProne,    25 },
    { "toggleprone",         kKindProne,    25 },
    { "+prone",              kKindProne,    25 },
    { "+attack",             kKindEngine,   1 },
    { "+speed_throw",        kKindEngine,   13 },
    { "+toggleads_throw",    kKindEngine,   15 },
    { "+melee",              kKindEngine,   3 },
    { "+melee_zoom",         kKindEngine,   35 },
    { "+changezoom",         kKindEngine,   37 },
    { "+weapnext_inventory", kKindEngine,   39 },
    { "weapnext",            kKindEngine,   60 },
    { "weapprev",            kKindWeapPrev, 0 },
    { "+weaphero",           kKindHero,     0 },
    { "+activate",           kKindEngine,   11 },
    { "+reload",             kKindEngine,   11 },
    { "+usereload",          kKindEngine,   11 },
    { "+sprint",             kKindEngine,   9 },
    { "+breath_sprint",      kKindEngine,   9 },
    { "+holdbreath",         kKindEngine,   9 },
    { "+frag",               kKindEngine,   5 },
    { "+smoke",              kKindEngine,   7 },
    { "+actionslot 1",       kKindEngine,   17 },
    { "+actionslot 2",       kKindEngine,   19 },
    { "+actionslot 3",       kKindEngine,   21 },
    { "+actionslot 4",       kKindEngine,   23 },
    { "+scores",             kKindScores,   63 },
    { "togglescores",        kKindEngine,   63 },
    { "inventory",           kKindEngine,   62 },
    { "pause",               kKindEngine,   61 },
    { "togglemenu",          kKindEngine,   59 },
    { "screenshotjpeg",      kKindNone,     0 },
    { "+talk",               kKindNone,     0 },
    { "chatmodepublic",      kKindNone,     0 },
    { "chatmodeteam",        kKindNone,     0 },
    { "chatmodeparty",       kKindNone,     0 },
    { "+mlook",              kKindNone,     0 },
    { "centerview",          kKindNone,     0 },
};

constexpr int32_t kCommandCount = (int32_t)(sizeof(k_commands) / sizeof(k_commands[0]));

struct DefaultBind
{
    uint8_t     key;
    const char* command;
};

static const DefaultBind k_defaults[] =
{
    { 'w', "+forward" },
    { 's', "+back" },
    { 'a', "+moveleft" },
    { 'd', "+moveright" },
    { kKeyShift, "+breath_sprint" },
    { kKeyMouse1, "+attack" },
    { kKeyMouse2, "+toggleads_throw" },
    { 'v', "+melee" },
    { '1', "+actionslot 1" },
    { '2', "+actionslot 2" },
    { '5', "+actionslot 3" },
    { '3', "+actionslot 4" },
    { kKeyWheelUp, "weapnext" },
    { kKeyWheelDown, "weapprev" },
    { 'x', "+weapnext_inventory" },
    { 'q', "+weaphero" },
    { kKeyMouse3, "+frag" },
    { 'g', "+frag" },
    { '4', "+smoke" },
    { 'f', "+activate" },
    { 'r', "+reload" },
    { kKeyTab, "+scores" },
    { kKeySpace, "+gostand" },
    { kKeyCtrl, "toggleprone" },
    { 'c', "+stance" },
    { kKeyPause, "pause" },
};

struct Alias
{
    const char* from;
    const char* to;
};

static const Alias k_aliases[] =
{
    { "+usereload", "+activate" },
    { "+usereload", "+reload" },
    { "+activate", "+usereload" },
    { "+reload", "+usereload" },
    { "+speed_throw", "+toggleads_throw" },
    { "+toggleads_throw", "+speed_throw" },
    { "gocrouch", "+stance" },
    { "+stance", "gocrouch" },
    { "+stance", "togglecrouch" },
    { "togglecrouch", "+stance" },
    { "togglescores", "+scores" },
    { "+scores", "togglescores" },
    { "+breath_sprint", "+sprint" },
    { "+sprint", "+breath_sprint" },
    { "+holdbreath", "+breath_sprint" },
    { "+melee_breath", "+breath_sprint" },
    { "+melee_zoom", "+melee" },
    { "goprone", "toggleprone" },
    { "toggleprone", "goprone" },
    { "+prone", "toggleprone" },
};

static const Alias k_specialRedirects[] =
{
    { "+ability", "+weaphero" },
    { "+cybercore_left", "+moveleft" },
    { "+cybercore_right", "+moveright" },
    { "+dwattack_left", "+attack" },
    { "+dwattack_right", "+speed_throw" },
    { "+lsleft", "+moveleft" },
    { "+lsright", "+moveright" },
    { "+lsup", "+forward" },
    { "+lsdown", "+back" },
};

struct KeyName
{
    uint8_t     key;
    const char* name;
};

static const KeyName k_keyNames[] =
{
    { 9, "TAB" }, { 13, "ENTER" }, { 27, "ESCAPE" }, { 32, "SPACE" }, { 127, "BACKSPACE" },
    { 151, "CAPSLOCK" }, { 153, "PAUSE" }, { 154, "UPARROW" }, { 155, "DOWNARROW" }, { 156, "LEFTARROW" },
    { 157, "RIGHTARROW" }, { 158, "ALT" }, { 159, "CTRL" }, { 160, "SHIFT" }, { 161, "INS" }, { 162, "DEL" },
    { 163, "PGDN" }, { 164, "PGUP" }, { 165, "HOME" }, { 166, "END" },
    { 167, "F1" }, { 168, "F2" }, { 169, "F3" }, { 170, "F4" }, { 171, "F5" }, { 172, "F6" },
    { 173, "F7" }, { 174, "F8" }, { 175, "F9" }, { 176, "F10" }, { 177, "F11" }, { 178, "F12" },
    { 182, "KP_HOME" }, { 183, "KP_UPARROW" }, { 184, "KP_PGUP" }, { 185, "KP_LEFTARROW" }, { 186, "KP_5" },
    { 187, "KP_RIGHTARROW" }, { 188, "KP_END" }, { 189, "KP_DOWNARROW" }, { 190, "KP_PGDN" },
    { 191, "KP_ENTER" }, { 192, "KP_INS" }, { 193, "KP_DEL" }, { 194, "KP_SLASH" }, { 195, "KP_MINUS" },
    { 196, "KP_PLUS" }, { 197, "KP_NUMLOCK" }, { 198, "KP_STAR" }, { 199, "KP_EQUALS" },
    { 200, "MOUSE1" }, { 201, "MOUSE2" }, { 202, "MOUSE3" }, { 203, "MOUSE4" }, { 204, "MOUSE5" },
    { 205, "MWHEELDOWN" }, { 206, "MWHEELUP" },
};

struct Setting
{
    const char* dvar;
    const char* key;
    const char* fallback;
};

static const Setting k_settings[] =
{
    { "bo3customs_mouse_sensitivity", "sensitivity", "5" },
    { "bo3customs_mouse_invert", "invert", "0" },
    { "bo3customs_mouse_accel", "acceleration", "0" },
    { "bo3customs_mouse_filter", "filter", "0" },
    { "bo3customs_mouse_freelook", "freelook", "1" },
    { "bo3customs_mouse_smooth", "smoothframes", "1" },
};

constexpr int32_t kSettingSensitivity = 0;
constexpr int32_t kSettingInvert      = 1;
constexpr int32_t kSettingAccel       = 2;
constexpr int32_t kSettingFilter      = 3;
constexpr int32_t kSettingFreelook    = 4;

static const char kCaptureDvar[] = "bo3customs_kbm_capture";
static const char kResetDvar[]   = "bo3customs_kbm_reset";

using ImeHandler_t   = void (*)(void*, const SceImeEvent*);
using KeyEvent_t     = void (*)(int32_t, int32_t, int32_t, int32_t, int32_t, uint8_t);
using ExecBinding_t  = void (*)(int32_t, int32_t, int32_t, float);
using Passthrough_t  = uint64_t (*)(uint64_t, uint64_t, uint64_t, uint64_t, uint64_t, uint64_t,
                                   double, double, double, double, double, double, double, double);
using EventBegin_t   = uint64_t (*)(LuiEvent*, uint64_t, const char*, const char*);
using EventEnd_t     = uint64_t (*)(LuiEvent*);
using FindRoot_t     = uintptr_t (*)(const char*, uint64_t);
using FieldString_t  = uint64_t (*)(const char*, const char*, uint64_t);
using FieldFloat_t   = uint64_t (*)(const char*, uint64_t, float);
using FieldInt_t     = uint64_t (*)(const char*, int32_t, uint64_t);
using FieldBool_t    = uint64_t (*)(const char*, uint8_t, uint64_t);
using KeyName_t      = const char* (*)(int32_t, uint32_t);
using Localize_t     = const char* (*)(const char*, int32_t);
using CbufAddText_t  = void (*)(int32_t, const char*);
using InputNotify_t  = void (*)(int32_t, int32_t);
using PadBindings_t  = void (*)(int32_t);

static uintptr_t g_base = 0;
static uint32_t  g_frames = 0;

static Detour g_imeDetour{};
static void*  g_imeOriginal = nullptr;
static Detour g_frameDetour{};
static void*  g_frameOriginal = nullptr;
static Detour g_cmdDetour{};
static void*  g_cmdOriginal = nullptr;
static Detour g_lastInputDetour{};
static void*  g_lastInputOriginal = nullptr;
static Detour g_padNativeDetour{};
static void*  g_padNativeOriginal = nullptr;
static Detour g_mouseNativeDetour{};
static void*  g_mouseNativeOriginal = nullptr;
static Detour g_bindingTextDetour{};
static void*  g_bindingTextOriginal = nullptr;
static Detour g_specialTextDetour{};
static void*  g_specialTextOriginal = nullptr;

static bool     g_mouseReady = false;
static int32_t  g_mouseHandle = -1;
static uint32_t g_mouseButtons = 0;
static uint32_t g_mouseRetryAt = 0;
static float    g_lookX = 0.0f;
static float    g_lookY = 0.0f;
static float    g_smoothX = 0.0f;
static float    g_smoothY = 0.0f;
static float    g_hipTan = 0.0f;
static float    g_sensitivity = kDefaultSensitivity;
static float    g_accel = 0.0f;
static float    g_filter = 0.0f;
static bool     g_invert = false;
static bool     g_freelook = true;
static float    g_cursorX = 0.5f;
static float    g_cursorY = 0.5f;
static bool     g_cursorShown = false;
static uint64_t g_cursorLua = 0;
static bool     g_mouseUsed = false;
static bool     g_luiReady = false;
static bool     g_cbufReady = false;
static bool     g_languageReady = false;
static bool     g_inputReady = false;
static bool     g_textReady = false;
static bool     g_configLoaded = false;
static uint32_t g_openLogs = 0;
static int32_t  g_capture = 0;
static int32_t  g_proneKey = 0;
static uint32_t g_proneDownAt = 0;
static uint32_t g_proneReleaseAt = 0;
static uint32_t g_inputChangedAt[4] = {};
static char     g_savedSettings[256] = {};

static uint8_t g_hidToKey[0xE8] = {};
static uint8_t g_sentAs[256] = {};
static uint8_t g_keyCommand[256] = {};
static uint8_t g_pressed[256] = {};

static bool Matches(uintptr_t address, const uint8_t* bytes, size_t size)
{
    return RangeReadable(address, size) && memcmp((const void*)address, bytes, size) == 0;
}

static void Log(const char* format, ...)
{
    char line[512];
    va_list args;
    va_start(args, format);
    const int length = vsnprintf(line, sizeof(line), format, args);
    va_end(args);

    if (length > 0)
        T7Log_Write("[KBM] %s", line);
}

static void BuildKeyTable()
{
    static const uint8_t k_fixed[][2] =
    {
        { 0x28, kKeyEnter }, { 0x29, kKeyEscape }, { 0x2A, kKeyBackspace }, { 0x2B, kKeyTab }, { 0x2C, kKeySpace },
        { 0x2D, '-' }, { 0x2E, '=' }, { 0x2F, '[' }, { 0x30, ']' }, { 0x31, '\\' }, { 0x33, ';' }, { 0x34, '\'' },
        { 0x35, '`' }, { 0x36, ',' }, { 0x37, '.' }, { 0x38, '/' }, { 0x39, 151 }, { 0x48, kKeyPause },
        { 0x49, 161 }, { 0x4A, 165 }, { 0x4B, 164 }, { 0x4C, 162 }, { 0x4D, 166 }, { 0x4E, 163 },
        { 0x4F, kKeyRight }, { 0x50, kKeyLeft }, { 0x51, kKeyDown }, { 0x52, kKeyUp }, { 0x53, 197 },
        { 0x54, 194 }, { 0x55, 198 }, { 0x56, 195 }, { 0x57, 196 }, { 0x58, kKeyKpEnter }, { 0x59, 188 },
        { 0x5A, kKeyKpDown }, { 0x5B, 190 }, { 0x5C, kKeyKpLeft }, { 0x5D, 186 }, { 0x5E, kKeyKpRight },
        { 0x5F, 182 }, { 0x60, kKeyKpUp }, { 0x61, 184 }, { 0x62, 192 }, { 0x63, 193 }, { 0x67, 199 },
        { 0xE0, kKeyCtrl }, { 0xE1, kKeyShift }, { 0xE2, 158 }, { 0xE4, kKeyCtrl }, { 0xE5, kKeyShift }, { 0xE6, 158 },
    };

    for (int i = 0; i < 26; ++i)
        g_hidToKey[0x04 + i] = (uint8_t)('A' + i);

    for (int i = 0; i < 9; ++i)
        g_hidToKey[0x1E + i] = (uint8_t)('1' + i);

    g_hidToKey[0x27] = '0';

    for (int i = 0; i < 12; ++i)
        g_hidToKey[0x3A + i] = (uint8_t)(167 + i);

    for (const auto& pair : k_fixed)
        g_hidToKey[pair[0]] = pair[1];
}

static int32_t Lower(int32_t key)
{
    return key >= 'A' && key <= 'Z' ? key + ('a' - 'A') : key;
}

static char Upper(char c)
{
    return c >= 'a' && c <= 'z' ? (char)(c - ('a' - 'A')) : c;
}

static bool SameText(const char* a, const char* b)
{
    if (!a || !b)
        return false;

    while (*a && *b)
    {
        if (Upper(*a) != Upper(*b))
            return false;

        ++a;
        ++b;
    }

    return *a == *b;
}

static float Abs(float value)
{
    return value < 0.0f ? -value : value;
}

static float Clamp(float value, float low, float high)
{
    if (!(value > low))
        return low;

    return value > high ? high : value;
}

static uint32_t FrameTime()
{
    return *(const uint32_t*)(g_base + kFrameTime);
}

static uintptr_t MapEntry(int32_t lc)
{
    return g_base + kLocalClientMap + (uintptr_t)lc * kLocalClientMapSize;
}

static int32_t LocalClient()
{
    for (int32_t i = 0; i < 4; ++i)
    {
        const uintptr_t entry = MapEntry(i);

        if (*(const int32_t*)(entry + kMapController) != (int32_t)kController)
            continue;

        const int32_t lc = *(const int32_t*)(entry + kMapClient);

        if (lc >= 0 && lc < 4)
            return lc;
    }

    return 0;
}

static int32_t ControllerOf(int32_t lc)
{
    const uintptr_t entry = MapEntry(lc);
    return *(const int32_t*)(entry + kMapClient) == lc ? *(const int32_t*)(entry + kMapController) : -1;
}

static int32_t LastInputOf(int32_t lc)
{
    if (lc < 0 || lc > 3)
        return kInputPad;

    return *(const int32_t*)(MapEntry(lc) + kMapLastInput);
}

static bool KbmLast(int32_t lc)
{
    const int32_t device = LastInputOf(lc);
    return device == kInputMouseMove || device == kInputMouseButton || device == kInputKeyboard;
}

static uintptr_t ClientUi(int32_t lc)
{
    return g_base + kClientUi + (uintptr_t)lc * kClientUiSize;
}

static bool InMenu(int32_t lc)
{
    return (*(const int32_t*)(ClientUi(lc) + 4) & kCatchMenus) != 0;
}

static bool LuiCatches(int32_t lc)
{
    return (*(const int32_t*)(ClientUi(lc) + 4) & kCatchLui) != 0 && T7Lua_MouseReady();
}

static uintptr_t FindDvar(const char* name)
{
    return ((uintptr_t (*)(const char*))(g_base + kDvarFind))(name);
}

static const char* DvarText(uintptr_t dvar)
{
    return ((const char* (*)(uintptr_t))(g_base + kDvarString))(dvar);
}

static const char* DvarText(const char* name)
{
    const uintptr_t dvar = FindDvar(name);
    return dvar ? DvarText(dvar) : nullptr;
}

static void SetDvar(const char* name, const char* value)
{
    ((void (*)(const char*, const char*, uint32_t, uint32_t))(g_base + kDvarSetByName))(name, value, 0, 0);
}

static float ParseNumber(const char* text)
{
    float value = 0.0f;
    float step = 0.0f;
    float sign = 1.0f;

    if (text && *text == '-')
    {
        sign = -1.0f;
        ++text;
    }

    for (const char* at = text; at && *at; ++at)
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

    return value * sign;
}

static int32_t LanguageGroup()
{
    if (!g_languageReady)
        return 0;

    const uintptr_t dvar = *(const uintptr_t*)(g_base + kLanguageDvar);
    const int32_t index = dvar ? ((int32_t (*)(uintptr_t))(g_base + kDvarInt))(dvar) : 0;

    if (index < 0 || index >= (int32_t)sizeof(T7KbmStrings::k_languageGroup))
        return 0;

    return T7KbmStrings::k_languageGroup[index];
}

static const char* KeyLabel(const char* token)
{
    const int32_t group = LanguageGroup();
    const char* english = nullptr;

    for (const T7KbmStrings::KeyText& entry : T7KbmStrings::k_keyTexts)
    {
        if (strcmp(entry.token, token) != 0)
            continue;

        if (entry.group == group)
            return entry.text;

        if (entry.group == 0)
            english = entry.text;
    }

    return english;
}

static const char* Localize(const char* token)
{
    const char* const text = ((Localize_t)(g_base + kLocalize))(token, 0);

    if (text && strcmp(text, token) != 0)
        return text;

    const char* const label = KeyLabel(token);

    if (label)
        return label;

    return text ? text : token;
}

static void CopyText(char* out, size_t size, const char* text)
{
    snprintf(out, size, "%s", text ? text : "");
}

static int32_t FindCommand(const char* name)
{
    if (!name || !name[0])
        return 0;

    for (int32_t i = 1; i < kCommandCount; ++i)
    {
        if (SameText(k_commands[i].name, name))
            return i;
    }

    return 0;
}

static int32_t KeysFor(int32_t command, int32_t* keys)
{
    if (command <= 0)
        return 0;

    int32_t count = 0;

    for (int32_t key = 1; key < 256 && count < 2; ++key)
    {
        if (g_keyCommand[key] == command)
            keys[count++] = key;
    }

    return count;
}

static int32_t KeysForName(const char* name, int32_t* keys)
{
    int32_t count = KeysFor(FindCommand(name), keys);

    for (const Alias& alias : k_aliases)
    {
        if (count)
            break;

        if (SameText(alias.from, name))
            count = KeysFor(FindCommand(alias.to), keys);
    }

    return count;
}

static void KeyText(int32_t key, char* out, size_t size)
{
    const char* const name = ((KeyName_t)(g_base + kKeyName))(key, 1);
    char raw[64];
    CopyText(raw, sizeof(raw), name);

    if (raw[0] && raw[0] != '^' && raw[1])
        CopyText(out, size, Localize(raw));
    else
        CopyText(out, size, raw);
}

static int32_t KbmBindingText(const char* command, char* out, bool firstOnly)
{
    int32_t keys[2];
    const int32_t count = KeysForName(command, keys);

    if (!count)
    {
        CopyText(out, 256, Localize("KEY_UNBOUND"));
        return 0;
    }

    char first[128];
    KeyText(keys[0], first, sizeof(first));

    if (count == 1 || firstOnly)
    {
        CopyText(out, 256, first);
        return 1;
    }

    char second[128];
    char either[64];
    KeyText(keys[1], second, sizeof(second));
    CopyText(either, sizeof(either), Localize("KEY_OR"));
    snprintf(out, 256, "%s %s %s", first, either, second);
    return 2;
}

static bool KbmSpecialText(const char* command, char* out)
{
    if (SameText(command, "+forward") || SameText(command, "+back") || SameText(command, "+moveleft") ||
        SameText(command, "+moveright"))
    {
        return false;
    }

    for (const Alias& redirect : k_specialRedirects)
    {
        if (SameText(command, redirect.from))
        {
            KbmBindingText(redirect.to, out, false);
            return true;
        }
    }

    char parts[4][128];

    if (SameText(command, "+movestick"))
    {
        KbmBindingText("+forward", parts[0], true);
        KbmBindingText("+back", parts[1], true);
        KbmBindingText("+moveleft", parts[2], true);
        KbmBindingText("+moveright", parts[3], true);
        snprintf(out, 256, "%s,%s,%s,%s", parts[0], parts[1], parts[2], parts[3]);
        return true;
    }

    if (SameText(command, "+strafestick"))
    {
        KbmBindingText("+moveleft", parts[0], true);
        KbmBindingText("+moveright", parts[1], true);
        snprintf(out, 256, "%s,%s", parts[0], parts[1]);
        return true;
    }

    if (SameText(command, "+actionslots"))
    {
        KbmBindingText("+actionslot 1", parts[0], true);
        KbmBindingText("+actionslot 2", parts[1], true);
        KbmBindingText("+actionslot 4", parts[2], true);
        KbmBindingText("+actionslot 3", parts[3], true);
        snprintf(out, 256, "%s,%s,%s,%s", parts[0], parts[1], parts[2], parts[3]);
        return true;
    }

    if (SameText(command, "+lookstick") || SameText(command, "+rsleft") || SameText(command, "+rsright") ||
        SameText(command, "+rsup") || SameText(command, "+rsdown"))
    {
        CopyText(out, 256, "MOUSE");
        return true;
    }

    return false;
}

static const char* CfgKeyName(int32_t key, char* single)
{
    for (const KeyName& entry : k_keyNames)
    {
        if (entry.key == key)
            return entry.name;
    }

    if (key > ' ' && key < 127 && key != '"')
    {
        single[0] = Upper((char)key);
        single[1] = 0;
        return single;
    }

    return nullptr;
}

static int32_t ParseCfgKey(const char* name)
{
    if (!name || !name[0])
        return 0;

    for (const KeyName& entry : k_keyNames)
    {
        if (SameText(entry.name, name))
            return entry.key;
    }

    if (!name[1] && name[0] > ' ' && name[0] < 127)
        return Lower((uint8_t)name[0]);

    return 0;
}

static bool IsPadKey(int32_t key)
{
    return (key >= 1 && key <= 6) || (key >= 14 && key <= 25) || (key >= 28 && key <= 31);
}

static void ResetBinds()
{
    memset(g_keyCommand, 0, sizeof(g_keyCommand));

    for (const DefaultBind& bind : k_defaults)
        g_keyCommand[bind.key] = (uint8_t)FindCommand(bind.command);
}

static void SettingsSnapshot(char* out, size_t size)
{
    size_t used = 0;
    out[0] = 0;

    for (const Setting& setting : k_settings)
    {
        const char* const value = DvarText(setting.dvar);
        const int wrote = snprintf(out + used, size - used, "%s;", value ? value : "");

        if (wrote <= 0 || (size_t)wrote >= size - used)
            break;

        used += (size_t)wrote;
    }
}

static void SaveConfig()
{
    static char text[8192];
    size_t used = 0;
    char single[2];

    for (int32_t key = 1; key < 256; ++key)
    {
        const int32_t command = g_keyCommand[key];
        const char* const name = command ? CfgKeyName(key, single) : nullptr;

        if (!name)
            continue;

        const int wrote = snprintf(text + used, sizeof(text) - used, "bind %s %s\n", name, k_commands[command].name);

        if (wrote <= 0 || (size_t)wrote >= sizeof(text) - used)
            break;

        used += (size_t)wrote;
    }

    for (const Setting& setting : k_settings)
    {
        const char* const value = DvarText(setting.dvar);
        const int wrote = snprintf(text + used, sizeof(text) - used, "set %s %s\n", setting.key,
                                   value && value[0] ? value : setting.fallback);

        if (wrote <= 0 || (size_t)wrote >= sizeof(text) - used)
            break;

        used += (size_t)wrote;
    }

    const int fd = sceKernelOpen(kConfigPath, SCE_KERNEL_O_WRONLY | SCE_KERNEL_O_CREAT | SCE_KERNEL_O_TRUNC, 0777);

    if (fd < 0)
        return;

    sceKernelWrite(fd, text, used);
    sceKernelClose(fd);
    SettingsSnapshot(g_savedSettings, sizeof(g_savedSettings));
}

static char* NextWord(char*& at)
{
    while (*at == ' ' || *at == '\t')
        ++at;

    if (!*at)
        return nullptr;

    char* const start = at;

    while (*at && *at != ' ' && *at != '\t')
        ++at;

    if (*at)
        *at++ = 0;

    return start;
}

static void ApplyConfigLine(char* line, bool& anyBind)
{
    char* at = line;
    char* const verb = NextWord(at);
    char* const name = verb ? NextWord(at) : nullptr;

    if (!name)
        return;

    while (*at == ' ' || *at == '\t')
        ++at;

    size_t length = strlen(at);

    while (length && (at[length - 1] == ' ' || at[length - 1] == '\t' || at[length - 1] == '\r'))
        at[--length] = 0;

    if (SameText(verb, "bind"))
    {
        const int32_t key = ParseCfgKey(name);
        const int32_t command = FindCommand(at);

        if (key > 0 && key < 256 && !IsPadKey(key) && command)
        {
            g_keyCommand[key] = (uint8_t)command;
            anyBind = true;
        }

        return;
    }

    if (SameText(verb, "set") && at[0])
    {
        for (const Setting& setting : k_settings)
        {
            if (SameText(setting.key, name))
                SetDvar(setting.dvar, at);
        }
    }
}

static void LoadConfig()
{
    const int fd = sceKernelOpen(kConfigPath, SCE_KERNEL_O_RDONLY, 0);

    if (fd < 0)
    {
        ResetBinds();
        return;
    }

    static char text[16384];
    size_t used = 0;

    while (used < sizeof(text) - 1)
    {
        const int64_t got = sceKernelRead(fd, text + used, sizeof(text) - 1 - used);

        if (got <= 0)
            break;

        used += (size_t)got;
    }

    sceKernelClose(fd);
    text[used] = 0;

    memset(g_keyCommand, 0, sizeof(g_keyCommand));
    bool anyBind = false;
    char* line = text;

    while (line && *line)
    {
        char* const end = strchr(line, '\n');

        if (end)
            *end = 0;

        ApplyConfigLine(line, anyBind);
        line = end ? end + 1 : nullptr;
    }

    if (!anyBind)
        ResetBinds();
}

static void MakeDvars()
{
    for (const Setting& setting : k_settings)
    {
        if (!FindDvar(setting.dvar))
            SetDvar(setting.dvar, setting.fallback);
    }

    if (!FindDvar(kCaptureDvar))
        SetDvar(kCaptureDvar, "0");

    if (!FindDvar(kResetDvar))
        SetDvar(kResetDvar, "0");
}

static void ReadSettings()
{
    const char* const sensitivity = DvarText(k_settings[kSettingSensitivity].dvar);
    const float value = sensitivity ? ParseNumber(sensitivity) : kDefaultSensitivity;
    g_sensitivity = value > 0.0f ? value : kDefaultSensitivity;

    const char* const invert = DvarText(k_settings[kSettingInvert].dvar);
    g_invert = invert && atoi(invert) != 0;

    const char* const accel = DvarText(k_settings[kSettingAccel].dvar);
    g_accel = accel ? Clamp(ParseNumber(accel), 0.0f, 1.0f) : 0.0f;

    const char* const filter = DvarText(k_settings[kSettingFilter].dvar);
    g_filter = filter ? Clamp(ParseNumber(filter), 0.0f, 10.0f) : 0.0f;

    const char* const freelook = DvarText(k_settings[kSettingFreelook].dvar);
    g_freelook = !freelook || atoi(freelook) != 0;

    char snapshot[256];
    SettingsSnapshot(snapshot, sizeof(snapshot));

    if (strcmp(snapshot, g_savedSettings) != 0)
        SaveConfig();
}

static void SyncEngineBinds()
{
    const uintptr_t keys = g_base + kKeyStates + (uintptr_t)LocalClient() * kKeyStatesSize + kKeyBindings;

    for (int32_t key = 1; key < 256; ++key)
    {
        if (IsPadKey(key) || key == kKeyEscape || key == kKeyConsole || (key >= 'A' && key <= 'Z'))
            continue;

        int32_t* const slot = (int32_t*)(keys + (uintptr_t)key * kKeyStride);
        const Command& command = k_commands[g_keyCommand[key]];
        const int32_t wanted = command.kind == kKindEngine ? command.value : 0;

        if (slot[0] != wanted)
            slot[0] = wanted;

        if (slot[1] != 0)
            slot[1] = 0;
    }
}

static const char* RootName()
{
    if (*(const uint8_t*)(g_base + kRootSplit) == 0)
    {
        const uintptr_t player = g_base + kRootPerPlayer + kController * kRootPerPlayerSize;

        if (*(const uint8_t*)(player + kRootPerPlayerUsed) != 0)
            return (const char*)(player + kRootPerPlayerName);
    }

    return (const char*)(g_base + kRootShared);
}

static void PostEvent(const char* name)
{
    const uint64_t L = *(const uint64_t*)(g_base + kLuaState);

    if (!g_luiReady || !L || !T7Lua_MouseReady())
        return;

    LuiEvent event;
    memset(&event, 0, sizeof(event));

    ((EventBegin_t)(g_base + kLuiEventBegin))(&event, L, RootName(), name);

    if (!event.bytes[kEventPopulated])
    {
        ((FieldInt_t)(g_base + kLuiInt))("controller", (int32_t)kController, L);
        ((FieldBool_t)(g_base + kLuiBool))("immediate", 1, L);
        event.bytes[kEventPopulated] = 1;
    }

    ((EventEnd_t)(g_base + kLuiEventEnd))(&event);
}

static void SendMouseMove(float x, float y)
{
    const uint64_t L = *(const uint64_t*)(g_base + kLuaState);

    if (!g_luiReady || !L || !T7Lua_MouseReady())
        return;

    const char* const root = RootName();
    LuiEvent event;
    memset(&event, 0, sizeof(event));

    ((EventBegin_t)(g_base + kLuiEventBegin))(&event, L, root, "mousemove");

    if (!event.bytes[kEventPopulated])
    {
        const uintptr_t element = ((FindRoot_t)(g_base + kLuiRoot))(root, L);
        const float width = element ? *(const float*)(element + kRootWidth) : 1280.0f;
        const float height = element ? *(const float*)(element + kRootHeight) : 720.0f;

        ((FieldString_t)(g_base + kLuiString))("rootName", root, L);
        ((FieldFloat_t)(g_base + kLuiFloat))("x", L, (x - 0.5f) * width);
        ((FieldFloat_t)(g_base + kLuiFloat))("y", L, (y - 0.5f) * height);
        ((FieldInt_t)(g_base + kLuiInt))("controller", (int32_t)kController, L);
        ((FieldBool_t)(g_base + kLuiBool))("immediate", 1, L);
        ((FieldBool_t)(g_base + kLuiBool))("waitingForKeyBind", g_capture != 0 ? 1 : 0, L);
        event.bytes[kEventPopulated] = 1;
    }

    ((EventEnd_t)(g_base + kLuiEventEnd))(&event);
}

static void HideCursor()
{
    if (!g_cursorShown)
        return;

    g_cursorShown = false;
    SendMouseMove(kCursorHidden, kCursorHidden);
}

static void CancelCapture()
{
    if (!g_capture)
        return;

    g_capture = 0;
    PostEvent("key_bound");
}

static bool PadActive(int32_t controller)
{
    if (controller < 0 || controller > 3)
        return false;

    const uintptr_t pad = g_base + kGamepads + (uintptr_t)controller * kGamepadSize;
    const uint16_t buttons = *(const uint16_t*)(pad + kPadButtons);
    const uint16_t previous = *(const uint16_t*)(pad + kPadPrevious);

    if ((buttons & (uint16_t)~previous) != 0)
        return true;

    for (uintptr_t at = 0; at < 4; ++at)
    {
        if (Abs(*(const float*)(pad + kPadSticks + at * 4)) > kStickActive)
            return true;
    }

    return *(const float*)(pad + kPadLeftTrigger) > kTriggerActive ||
           *(const float*)(pad + kPadRightTrigger) > kTriggerActive;
}

static void SetLastInput(int32_t lc, int32_t device)
{
    if (!g_inputReady || lc < 0 || lc > 3)
        return;

    const int32_t controller = ControllerOf(lc);
    const bool pad = device == kInputPad || device == kInputRemotePad;

    if (pad && !PadActive(controller))
        return;

    int32_t* const last = (int32_t*)(MapEntry(lc) + kMapLastInput);
    const int32_t old = *last;

    if (old == device)
        return;

    const uint32_t now = FrameTime();

    if (device != kInputMouseButton && now - g_inputChangedAt[lc] < kInputSwitchMs)
        return;

    g_inputChangedAt[lc] = now;
    *last = device;

    const bool gamepadSwitch = old == kInputPad || device == kInputPad;
    ((InputNotify_t)(g_base + kInputNotify))(lc, gamepadSwitch ? 1 : 0);

    if (gamepadSwitch)
        ((PadBindings_t)(g_base + kPadBindings))(controller);

    if (pad)
    {
        CancelCapture();
        HideCursor();
    }
}

static uint64_t SetLastInputHook(uint64_t a1, uint64_t a2, uint64_t, uint64_t, uint64_t, uint64_t,
                                 double, double, double, double, double, double, double, double)
{
    const int32_t device = (int32_t)a2;

    if (device == kInputPad || device == kInputRemotePad)
        SetLastInput((int32_t)a1, device);

    return 0;
}

static int32_t ClientForArgument(uintptr_t L)
{
    const uintptr_t first = *(const uintptr_t*)(L + kStateBase);
    const uintptr_t top = *(const uintptr_t*)(L + kStateTop);
    int32_t controller = -1;

    if (first < top && (*(const uint32_t*)first & 0xF) == kTypeNumber)
        controller = (int32_t)*(const float*)(first + 8);

    if (controller < 0)
        return *(const int32_t*)(g_base + kPrimaryClient);

    for (int32_t i = 0; i < 4; ++i)
    {
        const uintptr_t entry = MapEntry(i);

        if (*(const int32_t*)(entry + kMapController) == controller)
            return *(const int32_t*)(entry + kMapClient);
    }

    return -1;
}

static uint64_t PushBool(uintptr_t L, bool value)
{
    uint32_t* const top = *(uint32_t**)(L + kStateTop);
    top[2] = value ? 1u : 0u;
    top[0] = kTypeBool;
    *(uint32_t**)(L + kStateTop) = top + 4;
    return 1;
}

static uint64_t LastInputGamepad(uint64_t L)
{
    const int32_t device = LastInputOf(ClientForArgument((uintptr_t)L));
    return PushBool((uintptr_t)L, device == kInputPad || device == kInputRemotePad);
}

static uint64_t LastInputMouse(uint64_t L)
{
    const int32_t device = LastInputOf(ClientForArgument((uintptr_t)L));
    return PushBool((uintptr_t)L, device == kInputMouseMove || device == kInputMouseButton);
}

static uint64_t BindingText(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                            double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const char* const command = (const char*)a2;
    char* const out = (char*)a3;

    if (!command || !out || !KbmLast((int32_t)a1))
        return ((Passthrough_t)g_bindingTextOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);

    return (uint64_t)(uint32_t)KbmBindingText(command, out, (a5 & 0xFF) != 0);
}

static uint64_t SpecialText(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                            double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const char* const command = (const char*)a2;
    char* const out = (char*)a3;

    if (!command || !out || !KbmLast((int32_t)a1))
        return ((Passthrough_t)g_specialTextOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);

    return KbmSpecialText(command, out) ? 1 : 0;
}

static void SendKey(int32_t lc, int32_t key, int32_t down, bool repeat)
{
    ((KeyEvent_t)(g_base + kKeyEvent))(lc, key, down, (int32_t)FrameTime(), 0, repeat ? 1 : 0);
}

static void Exec(int32_t lc, int32_t command, int32_t key)
{
    ((ExecBinding_t)(g_base + kExecBinding))(lc, command, key, 1.0f);
}

static KButton* Button(int32_t lc, int32_t offset)
{
    return (KButton*)(g_base + kKButtons + (uintptr_t)lc * kKButtonsSize + (uintptr_t)offset);
}

static void PressButton(KButton* button, int32_t key)
{
    button->value = 1.0f;

    if (button->down[0] == key || button->down[1] == key)
        return;

    if (!button->down[0])
        button->down[0] = key;
    else if (!button->down[1])
        button->down[1] = key;
    else
        return;

    if (button->active)
        return;

    button->downtime = FrameTime();
    button->active = 1;
    button->wasPressed = 1;
}

static void ReleaseButton(KButton* button, int32_t key)
{
    if (button->down[0] == key)
        button->down[0] = 0;
    else if (button->down[1] == key)
        button->down[1] = 0;
    else
        return;

    if (button->down[0] || button->down[1])
        return;

    button->active = 0;
    button->msec += FrameTime() - button->downtime;
    button->value = 0.0f;
}

static void RunCommand(int32_t lc, int32_t key, int32_t id, bool down)
{
    const Command& command = k_commands[id];
    const uint32_t now = FrameTime();

    switch (command.kind)
    {
    case kKindButton:
        if (down)
            PressButton(Button(lc, command.value), key);
        else
            ReleaseButton(Button(lc, command.value), key);
        break;
    case kKindScores:
        Exec(lc, command.value, key);
        break;
    case kKindProne:
        if (down)
        {
            if (g_proneKey)
                Exec(lc, command.value + 1, g_proneKey);

            Exec(lc, command.value, key);
            g_proneKey = key;
            g_proneDownAt = now;
            g_proneReleaseAt = 0;
        }
        else if (g_proneKey == key)
        {
            if (now - g_proneDownAt >= kProneHoldMs)
            {
                Exec(lc, command.value + 1, key);
                g_proneKey = 0;
            }
            else
            {
                g_proneReleaseAt = g_proneDownAt + kProneHoldMs;
            }
        }
        break;
    case kKindHero:
        Exec(lc, down ? kBindFrag : kBindFrag + 1, key);
        Exec(lc, down ? kBindSmoke : kBindSmoke + 1, key);
        break;
    case kKindWeapPrev:
        if (down && g_cbufReady)
            ((CbufAddText_t)(g_base + kCbufAddText))(lc, "weapprev\n");
        break;
    default:
        break;
    }
}

static void ReleaseProne(int32_t lc)
{
    if (!g_proneKey || !g_proneReleaseAt || (int32_t)(FrameTime() - g_proneReleaseAt) < 0)
        return;

    Exec(lc, 26, g_proneKey);
    g_proneKey = 0;
    g_proneReleaseAt = 0;
}

static void PollCapture()
{
    static uintptr_t dvar = 0;

    if (!dvar)
        dvar = FindDvar(kCaptureDvar);

    const char* const text = dvar ? DvarText(dvar) : nullptr;

    if (!text || !text[0] || (text[0] == '0' && !text[1]))
        return;

    const int32_t command = FindCommand(text);
    SetDvar(kCaptureDvar, "0");

    if (command)
        g_capture = command;
    else
        PostEvent("key_bound");
}

static void PollReset()
{
    static uintptr_t dvar = 0;

    if (!dvar)
        dvar = FindDvar(kResetDvar);

    const char* const text = dvar ? DvarText(dvar) : nullptr;

    if (!text || atoi(text) == 0)
        return;

    SetDvar(kResetDvar, "0");
    ResetBinds();

    for (const Setting& setting : k_settings)
        SetDvar(setting.dvar, setting.fallback);

    ReadSettings();
    SyncEngineBinds();
    SaveConfig();
    PostEvent("options_refresh");
}

static bool CaptureKey(int32_t key)
{
    PollCapture();

    if (!g_capture)
        return false;

    g_sentAs[key] = kSwallowed;

    if (key == kKeyConsole)
        return true;

    if (key == kKeyEscape)
    {
        CancelCapture();
        return true;
    }

    int32_t keys[2];
    const int32_t count = KeysFor(g_capture, keys);

    if (key == kKeyBackspace || count >= 2)
    {
        for (int32_t i = 0; i < count; ++i)
            g_keyCommand[keys[i]] = 0;
    }

    if (key != kKeyBackspace)
        g_keyCommand[key] = (uint8_t)g_capture;

    g_capture = 0;
    SyncEngineBinds();
    SaveConfig();
    PostEvent("key_bound");
    return true;
}

static int32_t MenuKey(int32_t key, bool mouse)
{
    switch (key)
    {
    case kKeyMouse1:
        return mouse ? key : kPadA;
    case kKeyMouse2:
        return mouse ? key : kPadB;
    case kKeyEnter:
    case kKeyKpEnter:
        return kPadA;
    case kKeyBackspace:
        return kPadB;
    case kKeyUp:
    case kKeyKpUp:
    case kKeyWheelUp:
        return kPadUp;
    case kKeyDown:
    case kKeyKpDown:
    case kKeyWheelDown:
        return kPadDown;
    case kKeyLeft:
    case kKeyKpLeft:
        return kPadLeft;
    case kKeyRight:
    case kKeyKpRight:
        return kPadRight;
    default:
        return key;
    }
}

static void OnKey(int32_t key, bool down, bool repeat)
{
    const int32_t lc = LocalClient();
    const int32_t lower = Lower(key);

    if (lower <= 0 || lower > 255)
        return;

    if (down && !repeat)
    {
        SetLastInput(lc, lower >= kKeyMouse1 ? kInputMouseButton : kInputKeyboard);

        if (!g_sentAs[lower] && CaptureKey(lower))
            return;
    }

    const bool menu = InMenu(lc);

    if (down && repeat)
    {
        const uint8_t sent = g_sentAs[lower];

        if (menu && sent && sent != kSwallowed)
        {
            if (IsPadKey(sent))
                SendKey(lc, sent, 2, false);
            else
                SendKey(lc, sent, 1, true);
        }

        return;
    }

    if (down)
    {
        if (g_sentAs[lower])
            return;

        const int32_t send = menu ? MenuKey(key, LuiCatches(lc)) : key;
        g_sentAs[lower] = (uint8_t)send;
        SendKey(lc, send, 1, false);

        if (menu)
            return;

        g_pressed[lower] = g_keyCommand[lower];

        if (g_pressed[lower])
            RunCommand(lc, lower, g_pressed[lower], true);

        return;
    }

    const uint8_t sent = g_sentAs[lower];
    g_sentAs[lower] = 0;

    if (sent != kSwallowed)
        SendKey(lc, sent ? sent : key, 0, false);

    if (g_pressed[lower])
    {
        RunCommand(lc, lower, g_pressed[lower], false);
        g_pressed[lower] = 0;
    }
}

static void ReleaseKeyboard()
{
    for (int32_t key = 1; key < kKeyMouse1; ++key)
    {
        if (g_sentAs[key])
            OnKey(key, false, false);
    }
}

static void ImeHandler(void* arg, const SceImeEvent* event)
{
    if (event != nullptr && (event->id == SCE_IME_KEYBOARD_EVENT_ABORT ||
                             event->id == SCE_IME_KEYBOARD_EVENT_DISCONNECTION))
    {
        ReleaseKeyboard();
    }

    if (event != nullptr && (event->id == SCE_IME_KEYBOARD_EVENT_KEYCODE_DOWN ||
                             event->id == SCE_IME_KEYBOARD_EVENT_KEYCODE_UP ||
                             event->id == SCE_IME_KEYBOARD_EVENT_KEYCODE_REPEAT))
    {
        const SceImeKeycode& code = event->param.keycode;

        if ((code.status & SCE_IME_KEYCODE_STATE_KEYCODE_VALID) != 0 && code.keycode < sizeof(g_hidToKey))
        {
            const int32_t key = g_hidToKey[code.keycode];

            if (key != 0)
                OnKey(key, event->id != SCE_IME_KEYBOARD_EVENT_KEYCODE_UP,
                      event->id == SCE_IME_KEYBOARD_EVENT_KEYCODE_REPEAT);
        }

        return;
    }

    ((ImeHandler_t)g_imeOriginal)(arg, event);
}

static SceUserServiceUserId MouseUser()
{
    const SceUserServiceUserId padUser =
        *(const int32_t*)(g_base + kGamepads + kController * kGamepadSize + kPadUser);

    if (padUser != SCE_USER_SERVICE_USER_ID_INVALID && padUser != 0)
        return padUser;

    SceUserServiceUserId user = SCE_USER_SERVICE_USER_ID_INVALID;

    if (sceUserServiceGetInitialUser(&user) == 0 && user != SCE_USER_SERVICE_USER_ID_INVALID)
        return user;

    SceUserServiceLoginUserIdList list;
    memset(&list, 0, sizeof(list));

    if (sceUserServiceGetLoginUserIdList(&list) == 0)
    {
        for (int i = 0; i < SCE_USER_SERVICE_MAX_LOGIN_USERS; ++i)
        {
            if (list.userId[i] != SCE_USER_SERVICE_USER_ID_INVALID)
                return list.userId[i];
        }
    }

    return SCE_USER_SERVICE_USER_ID_INVALID;
}

static void SetMouseButtons(uint32_t buttons)
{
    const uint32_t changed = buttons ^ g_mouseButtons;
    g_mouseButtons = buttons;

    for (int32_t i = 0; i < kMouseButtons; ++i)
    {
        if (changed & (1u << i))
            OnKey(kKeyMouse1 + i, ((buttons >> i) & 1u) != 0, false);
    }
}

static void Wheel(int32_t key)
{
    OnKey(key, true, false);
    OnKey(key, false, false);
}

static float Clamp01(float value)
{
    return Clamp(value, 0.0f, 1.0f);
}

static void MoveCursor(int32_t dx, int32_t dy, bool menu, bool kbm)
{
    if (!menu || !kbm)
    {
        HideCursor();
        return;
    }

    if (!T7Lua_MouseReady())
        return;

    const uint64_t L = *(const uint64_t*)(g_base + kLuaState);
    const bool moved = dx != 0 || dy != 0;

    if (!moved && (!g_mouseUsed || (g_cursorShown && L == g_cursorLua)))
        return;

    g_cursorX = Clamp01(g_cursorX + (float)dx * kCursorPerCountX);
    g_cursorY = Clamp01(g_cursorY + (float)dy * kCursorPerCountY);

    const uintptr_t cursor = g_base + kInputLock + kController * kInputLockSize;
    *(float*)(cursor + kCursorX) = g_cursorX;
    *(float*)(cursor + kCursorY) = g_cursorY;

    SendMouseMove(g_cursorX, g_cursorY);
    g_cursorShown = true;
    g_cursorLua = L;
}

static void PollMouse(int32_t lc)
{
    if (!g_mouseReady)
        return;

    if (g_mouseHandle < 0)
    {
        if (g_frames < g_mouseRetryAt)
            return;

        g_mouseRetryAt = g_frames + kRetryFrames;

        const SceUserServiceUserId user = MouseUser();

        if (user == SCE_USER_SERVICE_USER_ID_INVALID)
            return;

        SceMouseOpenParam param;
        memset(&param, 0, sizeof(param));
        param.behaviorFlag = SCE_MOUSE_OPEN_PARAM_MERGED;

        const int handle = sceMouseOpen(user, SCE_MOUSE_PORT_TYPE_STANDARD, 0, &param);

        if (handle < 0 && g_openLogs < kOpenLogs)
        {
            ++g_openLogs;
            Log("sceMouseOpen(user 0x%08X) = 0x%08X", (uint32_t)user, (uint32_t)handle);
        }

        if (handle >= 0)
        {
            g_mouseHandle = handle;
            Notify("BO3 Customs: mouse connected");
        }

        return;
    }

    SceMouseData data[kMouseRecords];
    const int count = sceMouseRead(g_mouseHandle, data, kMouseRecords);

    if (count == kMouseLoggedOut)
    {
        sceMouseClose(g_mouseHandle);
        g_mouseHandle = -1;
        SetMouseButtons(0);
        return;
    }

    int32_t dx = 0;
    int32_t dy = 0;

    for (int i = 0; i < count; ++i)
    {
        const SceMouseData& record = data[i];
        uint32_t buttons = 0;
        int32_t wheel = 0;

        if (record.connected && (record.buttons & SCE_MOUSE_BUTTON_INTERCEPTED) == 0)
        {
            dx += record.xAxis;
            dy += record.yAxis;
            buttons = record.buttons & kMouseButtonMask;
            wheel = record.wheel;

            if (record.xAxis || record.yAxis || buttons || wheel)
                g_mouseUsed = true;
        }

        SetMouseButtons(buttons);

        if (wheel > 0)
            Wheel(kKeyWheelUp);
        else if (wheel < 0)
            Wheel(kKeyWheelDown);
    }

    if (Abs((float)dx) + Abs((float)dy) >= (float)kMouseMoveReport)
        SetLastInput(lc, kInputMouseButton);

    const bool menu = InMenu(lc);

    if (menu)
    {
        g_lookX = 0.0f;
        g_lookY = 0.0f;
        g_smoothX = 0.0f;
        g_smoothY = 0.0f;
    }
    else
    {
        const float keep = Clamp(g_filter / kFilterRange, 0.0f, kFilterMost);
        g_smoothX = (float)dx * (1.0f - keep) + g_smoothX * keep;
        g_smoothY = (float)dy * (1.0f - keep) + g_smoothY * keep;

        const float speed = Clamp(Abs(g_smoothX) + Abs(g_smoothY), 0.0f, kAccelRange);
        const float boost = 1.0f + g_accel * speed / kAccelRange;
        g_lookX += g_smoothX * boost;
        g_lookY += g_smoothY * boost;
    }

    MoveCursor(dx, dy, menu, KbmLast(lc));
}

static float ZoomScale(int32_t lc)
{
    const uintptr_t clients = *(const uintptr_t*)(g_base + kCgArray);

    if (!clients)
        return 1.0f;

    const uintptr_t cg = clients + (uintptr_t)lc * kCgSize;
    const float tanY = *(const float*)(cg + kCgTanHalfFovY);
    const float ads = *(const float*)(cg + kCgAdsFraction);

    if (!(tanY > 0.0f))
        return 1.0f;

    if (!(ads > 0.001f))
    {
        g_hipTan = tanY;
        return 1.0f;
    }

    if (!(g_hipTan > 0.0f))
        return 1.0f;

    return Clamp(tanY / g_hipTan, kMinZoomScale, 1.0f);
}

static void ApplyLook(int32_t lc)
{
    const float dx = g_lookX;
    const float dy = g_lookY;

    if (dx == 0.0f && dy == 0.0f)
        return;

    if (lc != LocalClient())
        return;

    g_lookX = 0.0f;
    g_lookY = 0.0f;

    const uintptr_t ui = ClientUi(lc);

    if ((*(const int32_t*)(ui + 4) & kCatchMenus) != 0 || *(const int32_t*)(ui + 8) != kConnectionActive)
        return;

    if (*(const int32_t*)(g_base + kClsFrameTime) == 0 ||
        *(const uint8_t*)(g_base + kInputLock + kController * kInputLockSize) != 0)
    {
        return;
    }

    const uintptr_t clients = *(const uintptr_t*)(g_base + kClientActive);

    if (!clients)
        return;

    const uintptr_t cl = clients + (uintptr_t)lc * kClientActiveSize;

    if ((*(const uint64_t*)(cl + kClPlayerFlags) & kClNoLookFlags) != 0 ||
        *(const int32_t*)(cl + kClViewMode) == kClFrozenView || (*(const uint8_t*)(cl + kClLookBlock) & 8) != 0)
    {
        return;
    }

    const float scale = kDegreesPerCount * g_sensitivity * ZoomScale(lc);

    *(float*)(cl + kClViewYaw) -= dx * scale;

    if (g_freelook)
        *(float*)(cl + kClViewPitch) += dy * scale * (g_invert ? -1.0f : 1.0f);
}

static uint64_t InputFrame(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                           double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uint64_t result = ((Passthrough_t)g_frameOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);

    ++g_frames;

    const int32_t lc = LocalClient();

    if (!g_configLoaded)
    {
        g_configLoaded = true;
        MakeDvars();
        LoadConfig();
        SettingsSnapshot(g_savedSettings, sizeof(g_savedSettings));
        ReadSettings();
        SyncEngineBinds();
    }

    PollCapture();
    PollReset();

    if (g_capture && !InMenu(lc))
        CancelCapture();

    ReleaseProne(lc);

    if ((g_frames % kSettingsFrames) == 0)
    {
        ReadSettings();
        SyncEngineBinds();
    }

    PollMouse(lc);
    return result;
}

static uint64_t CreateCmd(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                          double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    ApplyLook((int32_t)a1);
#ifdef _DEBUG
    T7Maps_FrameHeartbeat();
#endif
    return ((Passthrough_t)g_cmdOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static void LoadMouse()
{
    const int loaded = sceSysmoduleLoadModule(SCE_SYSMODULE_MOUSE);

    if (loaded != 0)
    {
        Log("sceSysmoduleLoadModule(mouse) = 0x%08X", (uint32_t)loaded);
        Notify("BO3 Customs: the mouse library did not load (0x%08X)", (uint32_t)loaded);
        return;
    }

    const int init = sceMouseInit();
    g_mouseReady = init == 0;

    if (!g_mouseReady)
    {
        Log("sceMouseInit = 0x%08X", (uint32_t)init);
        Notify("BO3 Customs: the mouse did not start (0x%08X)", (uint32_t)init);
    }
}

static void InstallInput(uintptr_t base)
{
    g_inputReady = Matches(base + kSetLastInput, k_setLastInput, sizeof(k_setLastInput)) &&
                   Matches(base + kInputNotify, k_inputNotify, sizeof(k_inputNotify)) &&
                   Matches(base + kPadBindings, k_padBindings, sizeof(k_padBindings)) &&
                   Matches(base + kPadNative, k_lastInputNative, sizeof(k_lastInputNative)) &&
                   Matches(base + kMouseNative, k_lastInputNative, sizeof(k_lastInputNative));

    if (!g_inputReady)
    {
        Log("last-input functions do not match - prompts stay on the controller");
        return;
    }

    Detour_Attach(&g_lastInputDetour, (uint64_t)(base + kSetLastInput), (void*)SetLastInputHook, &g_lastInputOriginal);
    Detour_Attach(&g_padNativeDetour, (uint64_t)(base + kPadNative), (void*)LastInputGamepad, &g_padNativeOriginal);
    Detour_Attach(&g_mouseNativeDetour, (uint64_t)(base + kMouseNative), (void*)LastInputMouse, &g_mouseNativeOriginal);

    g_textReady = Matches(base + kBindingText, k_bindingText, sizeof(k_bindingText)) &&
                  Matches(base + kSpecialText, k_specialText, sizeof(k_specialText)) &&
                  Matches(base + kKeyName, k_keyName, sizeof(k_keyName)) &&
                  Matches(base + kLocalize, k_localize, sizeof(k_localize));

    if (!g_textReady)
    {
        Log("binding text functions do not match - hints keep the controller buttons");
        return;
    }

    Detour_Attach(&g_bindingTextDetour, (uint64_t)(base + kBindingText), (void*)BindingText, &g_bindingTextOriginal);
    Detour_Attach(&g_specialTextDetour, (uint64_t)(base + kSpecialText), (void*)SpecialText, &g_specialTextOriginal);
}
}

void T7Kbm_Install(uintptr_t base)
{
    using namespace T7Kbm;

    static bool installed = false;

    if (installed || !base)
        return;

    if (!Matches(base + kImeHandler, k_imeHandler, sizeof(k_imeHandler)) ||
        !Matches(base + kInputFrame, k_inputFrame, sizeof(k_inputFrame)) ||
        !Matches(base + kCreateCmd, k_createCmd, sizeof(k_createCmd)) ||
        !Matches(base + kKeyEvent, k_keyEvent, sizeof(k_keyEvent)) ||
        !Matches(base + kExecBinding, k_execBinding, sizeof(k_execBinding)))
    {
        Log("this game build does not match - keyboard and mouse are off");
        return;
    }

    installed = true;
    g_base = base;
    g_cbufReady = Matches(base + kCbufAddText, k_cbufAddText, sizeof(k_cbufAddText));
    g_languageReady = Matches(base + kDvarInt, k_dvarInt, sizeof(k_dvarInt));
    g_luiReady =Matches(base + kLuiEventBegin, k_luiEventBegin, sizeof(k_luiEventBegin)) &&
                 Matches(base + kLuiEventEnd, k_luiEventEnd, sizeof(k_luiEventEnd)) &&
                 Matches(base + kLuiRoot, k_luiRoot, sizeof(k_luiRoot)) &&
                 Matches(base + kLuiString, k_luiString, sizeof(k_luiString)) &&
                 Matches(base + kLuiFloat, k_luiField, sizeof(k_luiField)) &&
                 Matches(base + kLuiInt, k_luiField, sizeof(k_luiField)) &&
                 Matches(base + kLuiBool, k_luiField, sizeof(k_luiField));

    BuildKeyTable();
    ResetBinds();
    LoadMouse();
    InstallInput(base);

    Log("keyboard and mouse installed (menu cursor %s, prompts %s, key text %s)", g_luiReady ? "on" : "off",
        g_inputReady ? "on" : "off", g_textReady ? "on" : "off");

    Detour_Attach(&g_imeDetour, (uint64_t)(base + kImeHandler), (void*)ImeHandler, &g_imeOriginal);
    Detour_Attach(&g_frameDetour, (uint64_t)(base + kInputFrame), (void*)InputFrame, &g_frameOriginal);
    Detour_Attach(&g_cmdDetour, (uint64_t)(base + kCreateCmd), (void*)CreateCmd, &g_cmdOriginal);
}
