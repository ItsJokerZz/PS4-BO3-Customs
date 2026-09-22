#include "headers.hpp"

namespace
{
uint64_t g_execStart = 0;
uint64_t g_execEnd = 0;
uint64_t g_moduleEnd = 0;

enum NotificationType
{
    kNotificationRequest = 0,
    kNotificationRequestWithIcon = 1,
};

struct NotificationRequest
{
    NotificationType type;
    int32_t reqId;
    int32_t priority;
    int32_t msgId;
    int32_t targetId;
    int32_t userId;
    int32_t unk1;
    int32_t unk2;
    int32_t appId;
    int32_t errorNum;
    int32_t unk3;
    uint8_t useIconImageUri;
    char message[1024];
    char iconUri[1024];
    char unk[1024];
};

using SendNotification_t = int (*)(int, void*, size_t, int);

SendNotification_t g_sendNotification = nullptr;
bool g_notificationReady = false;
}

extern "C"
{
int sceKernelSendNotificationRequest(int device, void* request, size_t size, int block);
int mdbg_service(int command, void* arg1, void* arg2);
}

uint64_t GetBaseAddress()
{
    static uint64_t cached = 0;

    if (cached)
        return cached;

    SceKernelVirtualQueryInfo info;
    void* address = nullptr;

    while (sceKernelVirtualQuery(address, SCE_KERNEL_VQ_FIND_NEXT, &info, sizeof(info)) >= 0)
    {
        const uintptr_t start = (uintptr_t)info.start;
        const uintptr_t end = (uintptr_t)info.end;

        if (end <= start)
            break;

        address = (void*)end;

        if (info.protection != 5 || strcmp(info.name, "executable") != 0)
            continue;

        cached = start;
        g_execStart = start;
        g_execEnd = end;
        g_moduleEnd = end;

        SceKernelVirtualQueryInfo next;
        uintptr_t probe = end;

        for (int hop = 0; hop < 16; ++hop)
        {
            if (sceKernelVirtualQuery((void*)probe, 0, &next, sizeof(next)) < 0)
                break;

            const uintptr_t ns = (uintptr_t)next.start;
            const uintptr_t ne = (uintptr_t)next.end;

            if (ne <= ns || ns > g_moduleEnd || (next.protection & 1) == 0)
                break;

            g_moduleEnd = ne;
            probe = ne;
        }

        return cached;
    }

    return 0;
}

static uintptr_t ReadableEnd(uintptr_t addr, size_t need)
{
    if (addr < 0x10000 || addr >= 0x0000800000000000ULL || addr + need < addr)
        return 0;

    SceKernelVirtualQueryInfo info;
    uintptr_t end = 0;
    uintptr_t probe = addr;

    for (int hop = 0; hop < 8; ++hop)
    {
        if (sceKernelVirtualQuery((void*)probe, 0, &info, sizeof(info)) < 0)
            break;

        if ((info.protection & 1) == 0)
            break;

        const uintptr_t start = (uintptr_t)info.start;
        const uintptr_t stop = (uintptr_t)info.end;

        if (stop <= start || probe < start || probe >= stop || (end && start > end))
            break;

        end = stop;
        probe = stop;

        if (end >= addr + need)
            break;
    }

    return end;
}

bool RangeReadable(uintptr_t addr, size_t span)
{
    if (!addr || addr + span < addr)
        return false;

    const uintptr_t end = ReadableEnd(addr, span);
    return end != 0 && (addr + span) <= end;
}

bool SafeStrStr(uintptr_t addr, const char* target, size_t maxScan)
{
    if (addr < 0x10000 || addr >= 0x0000800000000000ULL || !target)
        return false;

    const size_t length = strlen(target);

    if (!length || length > maxScan)
        return false;

    const uintptr_t end = ReadableEnd(addr, maxScan);

    if (!end || end <= addr)
        return false;

    size_t available = (size_t)(end - addr);

    if (available > maxScan)
        available = maxScan;

    if (available < length)
        return false;

    const char* const text = (const char*)addr;

    for (size_t i = 0; i + length <= available; ++i)
    {
        if (text[i] == 0)
            return false;

        size_t j = 0;

        while (j < length && text[i + j] == target[j])
            ++j;

        if (j == length)
            return true;
    }

    return false;
}

const char* Data_Dir()
{
    return BO3_ROOT_DIR;
}

void Notify(const char* fmt, ...)
{
    if (!g_notificationReady)
    {
        g_notificationReady = true;

        void* address = nullptr;

        if (sceKernelDlsym(0x2001, "sceKernelSendNotificationRequest", &address) == 0 && address)
            g_sendNotification = (SendNotification_t)address;
    }

    NotificationRequest request{};

    va_list va;
    va_start(va, fmt);
    vsnprintf(request.message, sizeof(request.message), fmt, va);
    va_end(va);

    request.type = kNotificationRequest;
    request.targetId = -1;
    request.useIconImageUri = 1;
    snprintf(request.iconUri, sizeof(request.iconUri), "%s", "cxml://psnotification/tex_icon_ribbon");

    if (g_sendNotification)
        g_sendNotification(0, &request, sizeof(request), 0);
    else
        sceKernelSendNotificationRequest(0, &request, sizeof(request), 0);
}
