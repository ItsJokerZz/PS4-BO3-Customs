#include "headers.hpp"

#define VM_PROT_ALL (PROT_READ | PROT_WRITE | PROT_EXEC)

#define DETOUR_REL32_RANGE 0x7FF00000ULL

#define DETOUR_JMP32_SIZE 5
#define DETOUR_JMP64_SIZE 14

#define DETOUR_MAP_SIZE 0x1000

typedef struct _GHSDK_DetourReloc
{
    size_t Offset;
    size_t Length;
    size_t DispOffset;
    bool HasDisp;
} DetourReloc;

static size_t Detour_DecodeVex(uint64_t Address, bool* OutHasDisp, size_t* OutDispOffset)
{
    const uint8_t* p = (const uint8_t*)Address;
    size_t Pos = 0;
    uint32_t Map = 0;

    *OutHasDisp = false;
    *OutDispOffset = 0;

    if (p[0] == 0xC5)
    {
        Map = 1;
        Pos = 2;
    }
    else if (p[0] == 0xC4)
    {
        Map = p[1] & 0x1F;
        Pos = 3;
    }
    else if (p[0] == 0x62)
    {
        Map = p[1] & 0x07;
        Pos = 4;
    }
    else
    {
        return 0;
    }

    uint8_t Opcode = p[Pos];
    Pos++;

    if (Map == 1 && Opcode == 0x77)
        return Pos;

    uint8_t ModRM = p[Pos];
    Pos++;

    uint8_t Mod = (ModRM >> 6) & 3;
    uint8_t Rm = ModRM & 7;

    if (Mod != 3)
    {
        if (Rm == 4)
        {
            uint8_t Sib = p[Pos];
            Pos++;

            if (Mod == 0 && (Sib & 7) == 5)
                Pos += 4;
        }
        else if (Mod == 0 && Rm == 5)
        {
            *OutHasDisp = true;
            *OutDispOffset = Pos;
            Pos += 4;
        }

        if (Mod == 1)
            Pos += 1;
        else if (Mod == 2)
            Pos += 4;
    }

    if (Map == 3)
    {
        Pos += 1;
    }
    else if (Map == 1 &&
        (Opcode == 0x70 || Opcode == 0x71 || Opcode == 0x72 || Opcode == 0x73 ||
            Opcode == 0xC2 || Opcode == 0xC4 || Opcode == 0xC5 || Opcode == 0xC6))
    {
        Pos += 1;
    }

    return Pos;
}

static size_t Detour_Analyze(uint64_t Address, size_t MinSize,
    DetourReloc* Out, size_t Max, size_t* OutCount)
{
    size_t Total = 0;

    *OutCount = 0;

    if (!Address)
        return 0;

    while (Total < MinSize)
    {
        uint64_t Current = Address + Total;

        bool HasDisp = false;
        size_t DispOffset = 0;
        size_t Length = Detour_DecodeVex(Current, &HasDisp, &DispOffset);

        if (Length == 0)
        {
            hde64s hs;
            Length = hde64_disasm((void*)Current, &hs);

            if (hs.flags & F_ERROR)
            {
                return 0;
            }

            if ((hs.flags & F_RELATIVE) && (hs.flags & F_IMM8))
                return 0;

            if ((hs.flags & F_RELATIVE) && (hs.flags & F_IMM32))
            {
                HasDisp = true;
                DispOffset = Length - 4;
            }
            else if ((hs.flags & F_MODRM) && hs.modrm_mod == 0 && hs.modrm_rm == 5 &&
                (hs.flags & F_DISP32))
            {
                size_t ImmSize = 0;
                if (hs.flags & F_IMM8)  ImmSize = 1;
                if (hs.flags & F_IMM16) ImmSize = 2;
                if (hs.flags & F_IMM32) ImmSize = 4;
                if (hs.flags & F_IMM64) ImmSize = 8;

                HasDisp = true;
                DispOffset = Length - ImmSize - 4;
            }
        }

        if (Total + Length > DETOUR_MAX_PATCH)
            return 0;

        if (*OutCount >= Max)
            return 0;

        DetourReloc& Info = Out[*OutCount];
        Info.Offset = Total;
        Info.Length = Length;
        Info.HasDisp = HasDisp;
        Info.DispOffset = HasDisp ? (Total + DispOffset) : 0;

        (*OutCount)++;
        Total += Length;
    }

    return Total;
}

static void* Detour_MapNear(uint64_t Anchor, size_t Size)
{
    uint64_t Low = (Anchor > DETOUR_REL32_RANGE) ? (Anchor - DETOUR_REL32_RANGE) : 0x10000ULL;
    uint64_t High = Anchor + DETOUR_REL32_RANGE;

    for (uint64_t Step = 0x100000; Step < DETOUR_REL32_RANGE; Step += 0x100000)
    {
        uint64_t Hints[2];
        Hints[0] = (Anchor + Step) & ~0xFFFFULL;
        Hints[1] = (Anchor > Step) ? ((Anchor - Step) & ~0xFFFFULL) : 0;

        for (int i = 0; i < 2; i++)
        {
            if (Hints[i] < Low || Hints[i] >= High)
                continue;

            void* Addr = 0;
            int res = sceKernelMmap((void*)Hints[i], Size, VM_PROT_ALL, 0x1000 | 0x2, -1, 0, &Addr);

            if (res < 0 || Addr == 0)
                continue;

            if ((uint64_t)Addr >= Low && (uint64_t)Addr < High)
                return Addr;

            sceKernelMunmap(Addr, Size);
        }
    }

    return 0;
}

void* Detour_AllocNear(uint64_t Anchor)
{
    void* Page = Detour_MapNear(Anchor, DETOUR_MAP_SIZE);

    if (!Page)
        return 0;

    if (sceKernelMprotect(Page, DETOUR_MAP_SIZE, VM_PROT_ALL) < 0)
    {
        sceKernelMunmap(Page, DETOUR_MAP_SIZE);
        return 0;
    }

    return Page;
}

static void Detour_BuildJump(uint8_t* Out, uint64_t At, uint64_t Destination, bool Absolute)
{
    if (Absolute)
    {
        Out[0] = 0xFF;
        Out[1] = 0x25;
        Out[2] = 0x00;
        Out[3] = 0x00;
        Out[4] = 0x00;
        Out[5] = 0x00;
        memcpy(Out + 6, &Destination, sizeof(Destination));
    }
    else
    {
        int32_t Displacement = (int32_t)((int64_t)Destination -
            ((int64_t)At + DETOUR_JMP32_SIZE));

        Out[0] = 0xE9;
        memcpy(Out + 1, &Displacement, sizeof(Displacement));
    }
}

static bool Detour_InRel32Range(uint64_t From, uint64_t To)
{
    int64_t Displacement = (int64_t)To - (int64_t)From;
    return Displacement >= INT32_MIN && Displacement <= INT32_MAX;
}

static bool Detour_WriteVerified(void* Address, const uint8_t* Bytes, size_t Size)
{
    sceKernelMprotect(Address, Size, VM_PROT_ALL);
    memcpy(Address, Bytes, Size);

    if (memcmp(Address, Bytes, Size) != 0)
        return false;

    return true;
}

void* Detour_Attach(Detour* This, uint64_t FunctionPtr, void* HookPtr, void** OutStub)
{
    if (OutStub)
        *OutStub = 0;

    if (!This || !FunctionPtr || !HookPtr)
        return 0;

    if (This->Installed)
    {

        if (OutStub)
            *OutStub = This->StubPtr;

        return This->StubPtr;
    }

    {
        static uint64_t hooked[96];
        static int hookedCount = 0;

        for (int i = 0; i < hookedCount; ++i)
        {
            if (hooked[i] != FunctionPtr)
                continue;

            return 0;
        }

        if (hookedCount < (int)(sizeof(hooked) / sizeof(hooked[0])))
            hooked[hookedCount++] = FunctionPtr;
    }

    memset(This, 0, sizeof(*This));

    DetourReloc Relocs[DETOUR_MAX_PATCH];
    size_t RelocCount = 0;

    bool Absolute = true;
    size_t PatchSize = Detour_Analyze(FunctionPtr, DETOUR_JMP64_SIZE,
        Relocs, DETOUR_MAX_PATCH, &RelocCount);

    if (PatchSize < DETOUR_JMP64_SIZE)
    {

        Absolute = false;
        PatchSize = Detour_Analyze(FunctionPtr, DETOUR_JMP32_SIZE,
            Relocs, DETOUR_MAX_PATCH, &RelocCount);

        if (PatchSize < DETOUR_JMP32_SIZE)
            return 0;
    }

    void* MapPtr = Detour_MapNear(FunctionPtr, DETOUR_MAP_SIZE);

    if (!MapPtr)
    {
        int res = sceKernelMmap(0, DETOUR_MAP_SIZE, VM_PROT_ALL, 0x1000 | 0x2, -1, 0, &MapPtr);

        if (res < 0 || MapPtr == 0)
            return 0;
    }

    uint8_t* Stub = (uint8_t*)MapPtr;
    uint8_t* JumpBack = Stub + PatchSize;
    uint8_t* Bridge = JumpBack + DETOUR_JMP64_SIZE;

    bool DirectRel32 = Detour_InRel32Range(FunctionPtr + DETOUR_JMP32_SIZE, (uint64_t)HookPtr);
    bool BridgeReachable = Detour_InRel32Range(FunctionPtr + DETOUR_JMP32_SIZE, (uint64_t)Bridge);

    if (!Absolute && !DirectRel32 && !BridgeReachable)
    {
        sceKernelMunmap(MapPtr, DETOUR_MAP_SIZE);
        return 0;
    }

    memcpy(This->Original, (void*)FunctionPtr, PatchSize);

    memcpy(Stub, (void*)FunctionPtr, PatchSize);

    for (size_t i = 0; i < RelocCount; i++)
    {
        if (!Relocs[i].HasDisp)
            continue;

        int32_t OldDisp = 0;
        memcpy(&OldDisp, (void*)(FunctionPtr + Relocs[i].DispOffset), sizeof(OldDisp));

        uint64_t Target = FunctionPtr + Relocs[i].Offset + Relocs[i].Length + (int64_t)OldDisp;
        int64_t NewDisp = (int64_t)Target -
            (int64_t)((uint64_t)Stub + Relocs[i].Offset + Relocs[i].Length);

        if (NewDisp < INT32_MIN || NewDisp > INT32_MAX)
        {
            sceKernelMunmap(MapPtr, DETOUR_MAP_SIZE);
            return 0;
        }

        int32_t Written = (int32_t)NewDisp;
        memcpy(Stub + Relocs[i].DispOffset, &Written, sizeof(Written));
    }

    uint8_t JumpBytes[DETOUR_JMP64_SIZE];

    Detour_BuildJump(JumpBytes, (uint64_t)JumpBack, FunctionPtr + PatchSize, true);
    if (!Detour_WriteVerified(JumpBack, JumpBytes, DETOUR_JMP64_SIZE))
    {
        sceKernelMunmap(MapPtr, DETOUR_MAP_SIZE);
        return 0;
    }

    Detour_BuildJump(JumpBytes, (uint64_t)Bridge, (uint64_t)HookPtr, true);
    if (!Detour_WriteVerified(Bridge, JumpBytes, DETOUR_JMP64_SIZE))
    {
        sceKernelMunmap(MapPtr, DETOUR_MAP_SIZE);
        return 0;
    }

    uint8_t Patch[DETOUR_MAX_PATCH];
    memset(Patch, 0x90, PatchSize);

    uint64_t JumpTarget = Absolute ? (uint64_t)HookPtr
        : (DirectRel32 ? (uint64_t)HookPtr : (uint64_t)Bridge);

    Detour_BuildJump(Patch, FunctionPtr, JumpTarget, Absolute);

    if (OutStub)
        *OutStub = Stub;

    if (!Detour_WriteVerified((void*)FunctionPtr, Patch, PatchSize))
    {
        if (OutStub)
            *OutStub = 0;

        sceKernelMunmap(MapPtr, DETOUR_MAP_SIZE);
        return 0;
    }

    This->FunctionPtr = (void*)FunctionPtr;
    This->HookPtr = HookPtr;
    This->StubPtr = Stub;
    This->MapPtr = MapPtr;
    This->MapSize = DETOUR_MAP_SIZE;
    This->PatchSize = PatchSize;
    This->Installed = true;

    return Stub;
}

void Detour_Detach(Detour* This)
{
    if (!This || !This->Installed)
        return;

    Detour_WriteVerified(This->FunctionPtr, This->Original, This->PatchSize);

    sceKernelUsleep(20000);

    sceKernelMunmap(This->MapPtr, This->MapSize);

    memset(This, 0, sizeof(*This));
}
