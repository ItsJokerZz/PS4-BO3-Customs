#pragma once

#include <stddef.h>
#include <stdint.h>

struct PngImage
{
    int width;
    int height;
    int colorType;
    int bitDepth;
    int channels;
    bool interlaced;
    uint8_t palette[256 * 4];
    bool hasKey;
    uint16_t key[3];
    const uint8_t* file;
    size_t size;
};

bool Png_Open(const uint8_t* file, size_t size, PngImage* image, const char** error);

bool Png_Scale(const PngImage& image, uint8_t* out, int width, int height, size_t strideBytes, const char** error);
