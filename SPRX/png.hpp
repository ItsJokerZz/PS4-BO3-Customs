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
    uint8_t* data;
    size_t rowBytes;
    uint8_t palette[256 * 4];
    bool hasKey;
    uint16_t key[3];
};

bool Png_Decode(const uint8_t* file, size_t size, PngImage* image, const char** error);
void Png_Free(PngImage* image);

void Png_Resample(const PngImage& image, uint8_t* out, int width, int height, size_t strideBytes);
