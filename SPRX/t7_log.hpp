#pragma once

#include <stdint.h>

void T7Log_Install(uintptr_t base);

void T7Log_Write(const char* format, ...);

void T7Log_LuiFile(const char* name);

void T7Log_LuiBuffer(const char* name, uintptr_t rawFile);

void T7Log_LuaFailure(uintptr_t L, uint64_t topOffset, const char* chunk, const char* what);

void T7Log_Zone(const char* name, int32_t allocFlags, int32_t freeFlags);

void T7Log_NewLevel();
