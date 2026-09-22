#pragma once

#include <stdint.h>
#include <stddef.h>

uint64_t GetBaseAddress();

bool RangeReadable(uintptr_t addr, size_t span);

bool SafeStrStr(uintptr_t addr, const char* target, size_t maxScan = 64);

const char* Data_Dir();

void Notify(const char* fmt, ...);
