#pragma once

#include <stdint.h>

void T7Maps_Install(uintptr_t base);

bool T7Maps_InLevel();

const char* T7Maps_CustomMapsLua();

const char* T7Maps_PreviewFile(const char* map);
