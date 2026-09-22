#include "png.hpp"

#include <stdlib.h>
#include <string.h>

namespace
{
constexpr int kFastBits = 9;
constexpr int kFastMask = (1 << kFastBits) - 1;
constexpr int kSymbols = 288;

struct Huffman
{
    uint16_t fast[1 << kFastBits];
    uint16_t firstCode[16];
    int maxCode[17];
    uint16_t firstSymbol[16];
    uint8_t size[kSymbols];
    uint16_t value[kSymbols];
};

struct Inflater
{
    const uint8_t* in;
    const uint8_t* inEnd;
    uint32_t bits;
    int bitCount;
    bool paddedEnd;

    uint8_t* out;
    uint8_t* outStart;
    uint8_t* outEnd;

    Huffman length;
    Huffman distance;
};

int Reverse16(int n)
{
    n = ((n & 0xAAAA) >> 1) | ((n & 0x5555) << 1);
    n = ((n & 0xCCCC) >> 2) | ((n & 0x3333) << 2);
    n = ((n & 0xF0F0) >> 4) | ((n & 0x0F0F) << 4);
    n = ((n & 0xFF00) >> 8) | ((n & 0x00FF) << 8);
    return n;
}

int Reverse(int value, int bits)
{
    return Reverse16(value) >> (16 - bits);
}

bool BuildHuffman(Huffman* h, const uint8_t* lengths, int count)
{
    int sizes[17] = {};
    int nextCode[16] = {};

    memset(h->fast, 0, sizeof(h->fast));

    for (int i = 0; i < count; ++i)
        ++sizes[lengths[i]];

    sizes[0] = 0;

    for (int i = 1; i < 16; ++i)
    {
        if (sizes[i] > (1 << i))
            return false;
    }

    int code = 0;
    int symbol = 0;

    for (int i = 1; i < 16; ++i)
    {
        nextCode[i] = code;
        h->firstCode[i] = (uint16_t)code;
        h->firstSymbol[i] = (uint16_t)symbol;
        code += sizes[i];

        if (sizes[i] && code - 1 >= (1 << i))
            return false;

        h->maxCode[i] = code << (16 - i);
        code <<= 1;
        symbol += sizes[i];
    }

    h->maxCode[16] = 0x10000;

    for (int i = 0; i < count; ++i)
    {
        const int length = lengths[i];

        if (!length)
            continue;

        const int slot = nextCode[length] - h->firstCode[length] + h->firstSymbol[length];

        if (slot >= kSymbols)
            return false;

        h->size[slot] = (uint8_t)length;
        h->value[slot] = (uint16_t)i;

        if (length <= kFastBits)
        {
            const uint16_t entry = (uint16_t)((length << 9) | i);

            for (int j = Reverse(nextCode[length], length); j < (1 << kFastBits); j += 1 << length)
                h->fast[j] = entry;
        }

        ++nextCode[length];
    }

    return true;
}

uint8_t NextByte(Inflater* z)
{
    return z->in < z->inEnd ? *z->in++ : 0;
}

void Fill(Inflater* z)
{
    do
    {
        if (z->bits >= (1u << z->bitCount))
        {
            z->in = z->inEnd;
            return;
        }

        z->bits |= (uint32_t)NextByte(z) << z->bitCount;
        z->bitCount += 8;
    } while (z->bitCount <= 24);
}

uint32_t Take(Inflater* z, int count)
{
    if (z->bitCount < count)
        Fill(z);

    const uint32_t value = z->bits & ((1u << count) - 1);
    z->bits >>= count;
    z->bitCount -= count;
    return value;
}

int DecodeSlow(Inflater* z, const Huffman* h)
{
    const int k = Reverse(z->bits, 16);
    int length = kFastBits + 1;

    while (length < 16 && k >= h->maxCode[length])
        ++length;

    if (length >= 16)
        return -1;

    const int slot = (k >> (16 - length)) - h->firstCode[length] + h->firstSymbol[length];

    if (slot < 0 || slot >= kSymbols || h->size[slot] != length)
        return -1;

    z->bits >>= length;
    z->bitCount -= length;
    return h->value[slot];
}

int Decode(Inflater* z, const Huffman* h)
{
    if (z->bitCount < 16)
    {
        if (z->in >= z->inEnd)
        {
            if (z->paddedEnd)
                return -1;

            z->paddedEnd = true;
            z->bitCount += 16;
        }
        else
        {
            Fill(z);
        }
    }

    const int entry = h->fast[z->bits & kFastMask];

    if (entry)
    {
        const int length = entry >> 9;
        z->bits >>= length;
        z->bitCount -= length;
        return entry & 511;
    }

    return DecodeSlow(z, h);
}

const uint16_t kLengthBase[31] = { 3,  4,  5,  6,  7,  8,  9,  10, 11,  13,  15,  17,  19,  23, 27, 31,
                                   35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258, 0,  0 };
const uint8_t kLengthExtra[31] = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0, 0, 0 };
const uint16_t kDistanceBase[32] = { 1,    2,    3,    4,    5,    7,     9,     13,    17,  25,   33,   49,   65,   97,   129, 193,
                                     257,  385,  513,  769,  1025, 1537,  2049,  3073,  4097, 6145, 8193, 12289, 16385, 24577, 0, 0 };
const uint8_t kDistanceExtra[32] = { 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 0, 0 };

bool HuffmanBlock(Inflater* z)
{
    uint8_t* out = z->out;

    for (;;)
    {
        int symbol = Decode(z, &z->length);

        if (symbol < 0)
            return false;

        if (symbol < 256)
        {
            if (out >= z->outEnd)
                return false;

            *out++ = (uint8_t)symbol;
            continue;
        }

        if (symbol == 256)
        {
            z->out = out;
            return !(z->paddedEnd && z->bitCount < 16);
        }

        symbol -= 257;

        if (symbol >= 29)
            return false;

        int length = kLengthBase[symbol];

        if (kLengthExtra[symbol])
            length += (int)Take(z, kLengthExtra[symbol]);

        symbol = Decode(z, &z->distance);

        if (symbol < 0 || symbol >= 30)
            return false;

        int distance = kDistanceBase[symbol];

        if (kDistanceExtra[symbol])
            distance += (int)Take(z, kDistanceExtra[symbol]);

        if (out - z->outStart < distance || z->outEnd - out < length)
            return false;

        const uint8_t* from = out - distance;

        if (distance == 1)
        {
            memset(out, *from, (size_t)length);
            out += length;
        }
        else
        {
            while (length--)
                *out++ = *from++;
        }
    }
}

bool DynamicTables(Inflater* z)
{
    static const uint8_t kOrder[19] = { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };

    const int literals = (int)Take(z, 5) + 257;
    const int distances = (int)Take(z, 5) + 1;
    const int codeLengths = (int)Take(z, 4) + 4;
    const int total = literals + distances;

    uint8_t codeLengthSizes[19] = {};

    for (int i = 0; i < codeLengths; ++i)
        codeLengthSizes[kOrder[i]] = (uint8_t)Take(z, 3);

    Huffman codeLength;

    if (!BuildHuffman(&codeLength, codeLengthSizes, 19))
        return false;

    uint8_t lengths[286 + 32 + 138];
    int n = 0;

    while (n < total)
    {
        int c = Decode(z, &codeLength);

        if (c < 0 || c >= 19)
            return false;

        if (c < 16)
        {
            lengths[n++] = (uint8_t)c;
            continue;
        }

        uint8_t fill = 0;
        int repeat;

        if (c == 16)
        {
            if (n == 0)
                return false;

            repeat = (int)Take(z, 2) + 3;
            fill = lengths[n - 1];
        }
        else if (c == 17)
        {
            repeat = (int)Take(z, 3) + 3;
        }
        else
        {
            repeat = (int)Take(z, 7) + 11;
        }

        if (total - n < repeat)
            return false;

        memset(lengths + n, fill, (size_t)repeat);
        n += repeat;
    }

    return BuildHuffman(&z->length, lengths, literals) && BuildHuffman(&z->distance, lengths + literals, distances);
}

bool StoredBlock(Inflater* z)
{
    if (z->bitCount & 7)
        Take(z, z->bitCount & 7);

    uint8_t header[4];
    int k = 0;

    while (z->bitCount > 0 && k < 4)
    {
        header[k++] = (uint8_t)(z->bits & 0xFF);
        z->bits >>= 8;
        z->bitCount -= 8;
    }

    if (z->bitCount < 0)
        return false;

    while (k < 4)
        header[k++] = NextByte(z);

    const int length = header[0] | (header[1] << 8);
    const int inverse = header[2] | (header[3] << 8);

    if (inverse != (length ^ 0xFFFF) || z->inEnd - z->in < length || z->outEnd - z->out < length)
        return false;

    memcpy(z->out, z->in, (size_t)length);
    z->in += length;
    z->out += length;
    return true;
}

bool Inflate(const uint8_t* in, size_t inSize, uint8_t* out, size_t outSize)
{
    Inflater* const z = (Inflater*)malloc(sizeof(Inflater));

    if (!z)
        return false;

    memset(z, 0, sizeof(*z));
    z->in = in;
    z->inEnd = in + inSize;
    z->out = out;
    z->outStart = out;
    z->outEnd = out + outSize;

    bool ok = inSize >= 2;

    if (ok)
    {
        const int cmf = NextByte(z);
        const int flags = NextByte(z);
        ok = (cmf & 15) == 8 && (cmf * 256 + flags) % 31 == 0 && !(flags & 32);
    }

    for (bool last = false; ok && !last;)
    {
        last = Take(z, 1) != 0;

        switch (Take(z, 2))
        {
        case 0:
            ok = StoredBlock(z);
            break;
        case 1:
        {
            uint8_t lengths[288];
            uint8_t distances[32];
            memset(lengths, 8, 144);
            memset(lengths + 144, 9, 112);
            memset(lengths + 256, 7, 24);
            memset(lengths + 280, 8, 8);
            memset(distances, 5, sizeof(distances));
            ok = BuildHuffman(&z->length, lengths, 288) && BuildHuffman(&z->distance, distances, 32) && HuffmanBlock(z);
            break;
        }
        case 2:
            ok = DynamicTables(z) && HuffmanBlock(z);
            break;
        default:
            ok = false;
            break;
        }
    }

    ok = ok && z->out == z->outEnd;
    free(z);
    return ok;
}

uint32_t Big32(const uint8_t* p)
{
    return ((uint32_t)p[0] << 24) | ((uint32_t)p[1] << 16) | ((uint32_t)p[2] << 8) | p[3];
}

int Paeth(int a, int b, int c)
{
    const int p = a + b - c;
    const int pa = p > a ? p - a : a - p;
    const int pb = p > b ? p - b : b - p;
    const int pc = p > c ? p - c : c - p;

    if (pa <= pb && pa <= pc)
        return a;

    return pb <= pc ? b : c;
}

bool Unfilter(PngImage* image)
{
    const size_t stride = image->rowBytes;
    const size_t bpp = (size_t)image->channels * (size_t)(image->bitDepth / 8);

    for (int y = 0; y < image->height; ++y)
    {
        uint8_t* const line = image->data + (size_t)y * (stride + 1);
        const uint8_t filter = line[0];
        uint8_t* const row = line + 1;
        const uint8_t* const prior = y ? row - (stride + 1) : nullptr;

        switch (filter)
        {
        case 0:
            break;
        case 1:
            for (size_t i = bpp; i < stride; ++i)
                row[i] = (uint8_t)(row[i] + row[i - bpp]);
            break;
        case 2:
            if (prior)
            {
                for (size_t i = 0; i < stride; ++i)
                    row[i] = (uint8_t)(row[i] + prior[i]);
            }
            break;
        case 3:
            for (size_t i = 0; i < stride; ++i)
            {
                const int left = i >= bpp ? row[i - bpp] : 0;
                const int up = prior ? prior[i] : 0;
                row[i] = (uint8_t)(row[i] + ((left + up) >> 1));
            }
            break;
        case 4:
            for (size_t i = 0; i < stride; ++i)
            {
                const int left = i >= bpp ? row[i - bpp] : 0;
                const int up = prior ? prior[i] : 0;
                const int upLeft = prior && i >= bpp ? prior[i - bpp] : 0;
                row[i] = (uint8_t)(row[i] + Paeth(left, up, upLeft));
            }
            break;
        default:
            return false;
        }
    }

    return true;
}

inline unsigned Sample(const PngImage& image, const uint8_t* row, int x, int c)
{
    if (image.bitDepth == 16)
    {
        const size_t at = ((size_t)x * (size_t)image.channels + (size_t)c) * 2;
        return ((unsigned)row[at] << 8) | row[at + 1];
    }

    return row[(size_t)x * (size_t)image.channels + (size_t)c];
}

inline uint8_t Eight(const PngImage& image, unsigned sample)
{
    return image.bitDepth == 16 ? (uint8_t)(sample >> 8) : (uint8_t)sample;
}

inline void Pixel(const PngImage& image, const uint8_t* row, int x, unsigned* rgba)
{
    switch (image.colorType)
    {
    case 0:
    {
        const unsigned g = Sample(image, row, x, 0);
        rgba[0] = rgba[1] = rgba[2] = Eight(image, g);
        rgba[3] = image.hasKey && g == image.key[0] ? 0 : 255;
        break;
    }
    case 2:
    {
        const unsigned r = Sample(image, row, x, 0);
        const unsigned g = Sample(image, row, x, 1);
        const unsigned b = Sample(image, row, x, 2);
        rgba[0] = Eight(image, r);
        rgba[1] = Eight(image, g);
        rgba[2] = Eight(image, b);
        rgba[3] = image.hasKey && r == image.key[0] && g == image.key[1] && b == image.key[2] ? 0 : 255;
        break;
    }
    case 3:
    {
        const uint8_t* const entry = image.palette + (size_t)row[x] * 4;
        rgba[0] = entry[0];
        rgba[1] = entry[1];
        rgba[2] = entry[2];
        rgba[3] = entry[3];
        break;
    }
    case 4:
        rgba[0] = rgba[1] = rgba[2] = Eight(image, Sample(image, row, x, 0));
        rgba[3] = Eight(image, Sample(image, row, x, 1));
        break;
    default:
        rgba[0] = Eight(image, Sample(image, row, x, 0));
        rgba[1] = Eight(image, Sample(image, row, x, 1));
        rgba[2] = Eight(image, Sample(image, row, x, 2));
        rgba[3] = Eight(image, Sample(image, row, x, 3));
        break;
    }
}
}

bool Png_Decode(const uint8_t* file, size_t size, PngImage* image, const char** error)
{
    static const uint8_t kSignature[8] = { 0x89, 'P', 'N', 'G', '\r', '\n', 0x1A, '\n' };

    const char* failure = nullptr;
    const char*& why = error ? *error : failure;

    memset(image, 0, sizeof(*image));

    if (size < 8 + 25 || memcmp(file, kSignature, 8) != 0)
    {
        why = "not a PNG file";
        return false;
    }

    for (int i = 0; i < 256; ++i)
        image->palette[i * 4 + 3] = 255;

    bool header = false;
    size_t compressed = 0;

    for (size_t at = 8; at + 12 <= size;)
    {
        const uint32_t length = Big32(file + at);
        const uint8_t* const type = file + at + 4;
        const uint8_t* const body = file + at + 8;

        if (length > size - at - 12)
        {
            why = "a chunk runs past the end of the file";
            return false;
        }

        if (memcmp(type, "IHDR", 4) == 0 && length >= 13)
        {
            image->width = (int)Big32(body);
            image->height = (int)Big32(body + 4);
            image->bitDepth = body[8];
            image->colorType = body[9];

            if (body[10] != 0 || body[11] != 0)
            {
                why = "unknown compression or filter method";
                return false;
            }

            if (body[12] != 0)
            {
                why = "interlaced PNGs are not supported";
                return false;
            }

            header = true;
        }
        else if (memcmp(type, "PLTE", 4) == 0)
        {
            for (uint32_t i = 0; i < length / 3 && i < 256; ++i)
                memcpy(image->palette + i * 4, body + i * 3, 3);
        }
        else if (memcmp(type, "tRNS", 4) == 0)
        {
            if (image->colorType == 3)
            {
                for (uint32_t i = 0; i < length && i < 256; ++i)
                    image->palette[i * 4 + 3] = body[i];
            }
            else if (image->colorType == 0 && length >= 2)
            {
                image->hasKey = true;
                image->key[0] = (uint16_t)((body[0] << 8) | body[1]);
            }
            else if (image->colorType == 2 && length >= 6)
            {
                image->hasKey = true;

                for (int c = 0; c < 3; ++c)
                    image->key[c] = (uint16_t)((body[c * 2] << 8) | body[c * 2 + 1]);
            }
        }
        else if (memcmp(type, "IDAT", 4) == 0)
        {
            compressed += length;
        }
        else if (memcmp(type, "IEND", 4) == 0)
        {
            break;
        }

        at += 12 + (size_t)length;
    }

    if (!header)
    {
        why = "no IHDR chunk";
        return false;
    }

    switch (image->colorType)
    {
    case 0: image->channels = 1; break;
    case 2: image->channels = 3; break;
    case 3: image->channels = 1; break;
    case 4: image->channels = 2; break;
    case 6: image->channels = 4; break;
    default:
        why = "unknown color type";
        return false;
    }

    if (!(image->bitDepth == 8 || (image->bitDepth == 16 && image->colorType != 3)))
    {
        why = "only 8 and 16 bit PNGs are supported";
        return false;
    }

    if (image->width <= 0 || image->height <= 0 || image->width > 8192 || image->height > 8192)
    {
        why = "the picture is larger than 8192x8192";
        return false;
    }

    image->rowBytes = (size_t)image->width * (size_t)image->channels * (size_t)(image->bitDepth / 8);
    const size_t raw = (image->rowBytes + 1) * (size_t)image->height;

    if (raw > 96u * 1024 * 1024)
    {
        why = "the picture needs more than 96 MB unpacked";
        return false;
    }

    if (!compressed)
    {
        why = "no IDAT chunk";
        return false;
    }

    uint8_t* const stream = (uint8_t*)malloc(compressed);
    image->data = (uint8_t*)malloc(raw);

    if (!stream || !image->data)
    {
        free(stream);
        Png_Free(image);
        why = "out of memory";
        return false;
    }

    size_t used = 0;

    for (size_t at = 8; at + 12 <= size;)
    {
        const uint32_t length = Big32(file + at);

        if (memcmp(file + at + 4, "IDAT", 4) == 0)
        {
            memcpy(stream + used, file + at + 8, length);
            used += length;
        }
        else if (memcmp(file + at + 4, "IEND", 4) == 0)
        {
            break;
        }

        at += 12 + (size_t)length;
    }

    const bool inflated = Inflate(stream, used, image->data, raw);
    free(stream);

    if (!inflated)
    {
        Png_Free(image);
        why = "the image data is corrupt";
        return false;
    }

    if (!Unfilter(image))
    {
        Png_Free(image);
        why = "a scanline has an unknown filter";
        return false;
    }

    return true;
}

void Png_Free(PngImage* image)
{
    free(image->data);
    image->data = nullptr;
}

void Png_Resample(const PngImage& image, uint8_t* out, int width, int height, size_t strideBytes)
{
    const size_t line = image.rowBytes + 1;

    for (int ty = 0; ty < height; ++ty)
    {
        int y0 = (int)((int64_t)ty * image.height / height);
        int y1 = (int)((int64_t)(ty + 1) * image.height / height);

        if (y1 <= y0)
            y1 = y0 + 1;

        uint8_t* const destination = out + (size_t)ty * strideBytes;

        for (int tx = 0; tx < width; ++tx)
        {
            int x0 = (int)((int64_t)tx * image.width / width);
            int x1 = (int)((int64_t)(tx + 1) * image.width / width);

            if (x1 <= x0)
                x1 = x0 + 1;

            unsigned sum[4] = {};
            unsigned count = 0;

            for (int y = y0; y < y1; ++y)
            {
                const uint8_t* const row = image.data + (size_t)y * line + 1;

                for (int x = x0; x < x1; ++x)
                {
                    unsigned rgba[4];
                    Pixel(image, row, x, rgba);
                    sum[0] += rgba[0];
                    sum[1] += rgba[1];
                    sum[2] += rgba[2];
                    sum[3] += rgba[3];
                    ++count;
                }
            }

            for (int c = 0; c < 4; ++c)
                destination[(size_t)tx * 4 + (size_t)c] = (uint8_t)((sum[c] + count / 2) / count);
        }

        if (strideBytes > (size_t)width * 4)
            memset(destination + (size_t)width * 4, 0, strideBytes - (size_t)width * 4);
    }
}
