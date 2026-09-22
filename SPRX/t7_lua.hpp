#pragma once

#include <stdint.h>

void T7Lua_Install(uintptr_t base);

void T7Lua_Tick();

bool T7Lua_RunOnTop(uintptr_t L, const char* source, const char* chunk);
