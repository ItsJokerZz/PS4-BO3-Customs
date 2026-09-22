#pragma once

#define DETOUR_MAX_PATCH 32

typedef struct _GHSDK_Detour
{
    void* FunctionPtr;
    void* HookPtr;
    void* StubPtr;
    void* MapPtr;
    size_t MapSize;
    size_t PatchSize;
    uint8_t Original[DETOUR_MAX_PATCH];
    bool Installed;
} Detour;

void* Detour_Attach(Detour* This, uint64_t FunctionPtr, void* HookPtr, void** OutStub);
void Detour_Detach(Detour* This);

void* Detour_AllocNear(uint64_t Anchor);
