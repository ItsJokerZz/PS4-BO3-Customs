#pragma once

inline const char* const k_driveRoots[] =
{
    "ext0", "ext1", "ext2", "ext3", "ext4", "ext5", "ext6", "ext7",
    "usb0", "usb1", "usb2", "usb3", "usb4", "usb5", "usb6", "usb7",
};

void T7Maps_Install(uintptr_t base);

int T7Maps_MapCount();

bool T7Maps_Refresh();

bool T7Maps_InLevel();

const char* T7Maps_CustomMapsLua();

bool T7Maps_MapFile(const char* map, const char* file, char* path, size_t size);

const char* T7Maps_PreviewFile(const char* map);

void T7Maps_FrameHeartbeat();
