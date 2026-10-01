#include "png.hpp"

#include <stdlib.h>
#include <string.h>

namespace
{
constexpr int kFastBits = 9;
constexpr int kFastMask = (1 << kFastBits) - 1;
constexpr int kSymbols = 288;
constexpr uint32_t kWindowSize = 1u << 16;
constexpr uint32_t kWindowMask = kWindowSize - 1;
constexpr uint64_t kFlushAt = 1u << 15;
constexpr int kMaxSide = 8192;

const int kPassX[7] = { 0, 4, 0, 2, 0, 1, 0 };
const int kPassY[7] = { 0, 0, 4, 0, 2, 0, 1 };
const int kPassStepX[7] = { 8, 8, 4, 4, 2, 2, 1 };
const int kPassStepY[7] = { 8, 8, 8, 4, 4, 2, 2 };

struct Huffman
{
    uint16_t fast[1 << kFastBits];
    uint16_t firstCode[16];
    int maxCode[17];
    uint16_t firstSymbol[16];
    uint8_t size[kSymbols];
    uint16_t value[kSymbols];
};

struct Scaler
{
    int sourceWidth;
    int sourceHeight;
    int width;
    int height;
    uint8_t* out;
    size_t stride;
    int* columnStart;
    int* columnEnd;
    uint32_t* sums;
    int row;
    int* firstColumn;
    int* lastColumn;
    int* firstRow;
    int* lastRow;
};

struct Rows
{
    const PngImage* image;
    Scaler* scaler;
    int pass;
    int passWidth;
    int passHeight;
    size_t passBytes;
    int y;
    size_t filled;
    size_t bpp;
    uint8_t* line;
    uint8_t* prior;
    uint8_t* rgba;
    bool started;
    bool done;
    bool badFilter;
};

struct Inflater
{
    const uint8_t* file;
    size_t size;
    size_t chunk;
    const uint8_t* in;
    const uint8_t* inEnd;
    bool dry;
    uint32_t bits;
    int bitCount;
    bool paddedEnd;
    uint64_t total;
    uint64_t flushed;
    Rows* rows;
    Huffman length;
    Huffman distance;
    uint8_t window[kWindowSize];
};

uint32_t Big32(const uint8_t* p)
{
    return ((uint32_t)p[0] << 24) | ((uint32_t)p[1] << 16) | ((uint32_t)p[2] << 8) | p[3];
}

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

int RowStart(const Scaler* s, int row)
{
    return (int)((int64_t)row * s->sourceHeight / s->height);
}

int RowEnd(const Scaler* s, int row)
{
    const int start = RowStart(s, row);
    const int end = (int)((int64_t)(row + 1) * s->sourceHeight / s->height);
    return end > start ? end : start + 1;
}

void AddRow(const Scaler* s, uint32_t* sums, const uint8_t* rgba)
{
    for (int tx = 0; tx < s->width; ++tx)
    {
        uint32_t* const sum = sums + (size_t)tx * 4;

        for (int x = s->columnStart[tx]; x < s->columnEnd[tx]; ++x)
        {
            const uint8_t* const pixel = rgba + (size_t)x * 4;
            sum[0] += pixel[0];
            sum[1] += pixel[1];
            sum[2] += pixel[2];
            sum[3] += pixel[3];
        }
    }
}

void FinishRow(const Scaler* s, int row, uint32_t* sums)
{
    const uint32_t rows = (uint32_t)(RowEnd(s, row) - RowStart(s, row));
    uint8_t* const destination = s->out + (size_t)row * s->stride;

    for (int tx = 0; tx < s->width; ++tx)
    {
        const uint32_t count = (uint32_t)(s->columnEnd[tx] - s->columnStart[tx]) * rows;
        uint32_t* const sum = sums + (size_t)tx * 4;

        for (int c = 0; c < 4; ++c)
        {
            destination[(size_t)tx * 4 + (size_t)c] = (uint8_t)((sum[c] + count / 2) / count);
            sum[c] = 0;
        }
    }
}

void ScaleRow(Scaler* s, int y, const uint8_t* rgba)
{
    while (s->row < s->height)
    {
        if (y < RowStart(s, s->row))
            return;

        AddRow(s, s->sums, rgba);

        if (y + 1 < RowEnd(s, s->row))
            return;

        FinishRow(s, s->row, s->sums);
        ++s->row;
    }
}

void ScalePixel(Scaler* s, int x, int y, const uint8_t* pixel)
{
    if (s->firstColumn[x] < 0 || s->firstRow[y] < 0)
        return;

    for (int ty = s->firstRow[y]; ty <= s->lastRow[y]; ++ty)
    {
        uint32_t* const row = s->sums + (size_t)ty * (size_t)s->width * 4;

        for (int tx = s->firstColumn[x]; tx <= s->lastColumn[x]; ++tx)
        {
            uint32_t* const sum = row + (size_t)tx * 4;
            sum[0] += pixel[0];
            sum[1] += pixel[1];
            sum[2] += pixel[2];
            sum[3] += pixel[3];
        }
    }
}

void Cover(int* first, int* last, int length, int from, int to, int target)
{
    for (int i = from; i < to && i < length; ++i)
    {
        if (first[i] < 0)
            first[i] = target;

        last[i] = target;
    }
}

void FreeScaler(Scaler* s)
{
    free(s->columnStart);
    free(s->columnEnd);
    free(s->sums);
    free(s->firstColumn);
    free(s->lastColumn);
    free(s->firstRow);
    free(s->lastRow);
}

bool MakeScaler(Scaler* s, const PngImage& image, uint8_t* out, int width, int height, size_t stride)
{
    memset(s, 0, sizeof(*s));
    s->sourceWidth = image.width;
    s->sourceHeight = image.height;
    s->width = width;
    s->height = height;
    s->out = out;
    s->stride = stride;
    s->columnStart = (int*)malloc(sizeof(int) * (size_t)width);
    s->columnEnd = (int*)malloc(sizeof(int) * (size_t)width);
    s->sums = (uint32_t*)calloc((size_t)width * (image.interlaced ? (size_t)height : 1) * 4, sizeof(uint32_t));

    if (!s->columnStart || !s->columnEnd || !s->sums)
        return false;

    for (int tx = 0; tx < width; ++tx)
    {
        const int start = (int)((int64_t)tx * image.width / width);
        const int end = (int)((int64_t)(tx + 1) * image.width / width);
        s->columnStart[tx] = start;
        s->columnEnd[tx] = end > start ? end : start + 1;
    }

    if (!image.interlaced)
        return true;

    s->firstColumn = (int*)malloc(sizeof(int) * (size_t)image.width);
    s->lastColumn = (int*)malloc(sizeof(int) * (size_t)image.width);
    s->firstRow = (int*)malloc(sizeof(int) * (size_t)image.height);
    s->lastRow = (int*)malloc(sizeof(int) * (size_t)image.height);

    if (!s->firstColumn || !s->lastColumn || !s->firstRow || !s->lastRow)
        return false;

    memset(s->firstColumn, 0xFF, sizeof(int) * (size_t)image.width);
    memset(s->firstRow, 0xFF, sizeof(int) * (size_t)image.height);

    for (int tx = 0; tx < width; ++tx)
        Cover(s->firstColumn, s->lastColumn, image.width, s->columnStart[tx], s->columnEnd[tx], tx);

    for (int ty = 0; ty < height; ++ty)
        Cover(s->firstRow, s->lastRow, image.height, RowStart(s, ty), RowEnd(s, ty), ty);

    return true;
}

unsigned Sample(const PngImage& image, const uint8_t* row, int x, int c)
{
    switch (image.bitDepth)
    {
    case 16:
    {
        const size_t at = ((size_t)x * (size_t)image.channels + (size_t)c) * 2;
        return ((unsigned)row[at] << 8) | row[at + 1];
    }
    case 8:
        return row[(size_t)x * (size_t)image.channels + (size_t)c];
    default:
    {
        const size_t bit = (size_t)x * (size_t)image.bitDepth;
        return (unsigned)(row[bit >> 3] >> (8 - image.bitDepth - (int)(bit & 7))) & ((1u << image.bitDepth) - 1);
    }
    }
}

uint8_t Eight(const PngImage& image, unsigned sample)
{
    switch (image.bitDepth)
    {
    case 16: return (uint8_t)(sample >> 8);
    case 4: return (uint8_t)(sample * 17);
    case 2: return (uint8_t)(sample * 85);
    case 1: return (uint8_t)(sample * 255);
    default: return (uint8_t)sample;
    }
}

void Pixel(const PngImage& image, const uint8_t* row, int x, uint8_t* rgba)
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
        memcpy(rgba, image.palette + (size_t)Sample(image, row, x, 0) * 4, 4);
        break;
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

bool Unfilter(uint8_t filter, uint8_t* row, const uint8_t* prior, size_t bytes, size_t bpp)
{
    switch (filter)
    {
    case 0:
        break;
    case 1:
        for (size_t i = bpp; i < bytes; ++i)
            row[i] = (uint8_t)(row[i] + row[i - bpp]);
        break;
    case 2:
        if (prior)
        {
            for (size_t i = 0; i < bytes; ++i)
                row[i] = (uint8_t)(row[i] + prior[i]);
        }
        break;
    case 3:
        for (size_t i = 0; i < bytes; ++i)
        {
            const int left = i >= bpp ? row[i - bpp] : 0;
            const int up = prior ? prior[i] : 0;
            row[i] = (uint8_t)(row[i] + ((left + up) >> 1));
        }
        break;
    case 4:
        for (size_t i = 0; i < bytes; ++i)
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

    return true;
}

bool StartPass(Rows* r, int pass)
{
    const PngImage& image = *r->image;

    for (; pass < (image.interlaced ? 7 : 1); ++pass)
    {
        int width = image.width;
        int height = image.height;

        if (image.interlaced)
        {
            width = image.width > kPassX[pass] ? (image.width - kPassX[pass] + kPassStepX[pass] - 1) / kPassStepX[pass] : 0;
            height = image.height > kPassY[pass] ? (image.height - kPassY[pass] + kPassStepY[pass] - 1) / kPassStepY[pass] : 0;
        }

        if (width > 0 && height > 0)
        {
            r->pass = pass;
            r->passWidth = width;
            r->passHeight = height;
            r->passBytes = ((size_t)width * (size_t)image.channels * (size_t)image.bitDepth + 7) / 8;
            r->y = 0;
            r->filled = 0;
            r->started = false;
            return true;
        }
    }

    r->done = true;
    return false;
}

bool FinishLine(Rows* r)
{
    const PngImage& image = *r->image;
    uint8_t* const row = r->line + 1;

    if (!Unfilter(r->line[0], row, r->started ? r->prior + 1 : nullptr, r->passBytes, r->bpp))
    {
        r->badFilter = true;
        return false;
    }

    for (int i = 0; i < r->passWidth; ++i)
        Pixel(image, row, i, r->rgba + (size_t)i * 4);

    if (!image.interlaced)
    {
        ScaleRow(r->scaler, r->y, r->rgba);
    }
    else
    {
        const int y = kPassY[r->pass] + r->y * kPassStepY[r->pass];

        for (int i = 0; i < r->passWidth; ++i)
            ScalePixel(r->scaler, kPassX[r->pass] + i * kPassStepX[r->pass], y, r->rgba + (size_t)i * 4);
    }

    uint8_t* const swap = r->prior;
    r->prior = r->line;
    r->line = swap;
    r->started = true;
    r->filled = 0;

    if (++r->y == r->passHeight)
        StartPass(r, r->pass + 1);

    return true;
}

bool TakeBytes(Rows* r, const uint8_t* data, size_t count)
{
    while (count)
    {
        if (r->done)
            return false;

        const size_t need = r->passBytes + 1 - r->filled;
        const size_t take = count < need ? count : need;
        memcpy(r->line + r->filled, data, take);
        r->filled += take;
        data += take;
        count -= take;

        if (r->filled == r->passBytes + 1 && !FinishLine(r))
            return false;
    }

    return true;
}

bool NextData(Inflater* z)
{
    while (z->chunk + 12 <= z->size)
    {
        const size_t at = z->chunk;
        const uint32_t length = Big32(z->file + at);

        if (length > z->size - at - 12)
            break;

        z->chunk = at + 12 + (size_t)length;

        if (memcmp(z->file + at + 4, "IEND", 4) == 0)
            break;

        if (memcmp(z->file + at + 4, "IDAT", 4) == 0 && length)
        {
            z->in = z->file + at + 8;
            z->inEnd = z->in + length;
            return true;
        }
    }

    z->chunk = z->size;
    z->dry = true;
    return false;
}

bool HasInput(Inflater* z)
{
    return z->in < z->inEnd || (!z->dry && NextData(z));
}

uint8_t NextByte(Inflater* z)
{
    return HasInput(z) ? *z->in++ : 0;
}

void Fill(Inflater* z)
{
    do
    {
        if (z->bits >= (1u << z->bitCount))
        {
            z->in = z->inEnd;
            z->dry = true;
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
        if (!HasInput(z))
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

void Put(Inflater* z, uint8_t value)
{
    z->window[(uint32_t)z->total & kWindowMask] = value;
    ++z->total;
}

bool Flush(Inflater* z)
{
    while (z->flushed < z->total)
    {
        const uint32_t from = (uint32_t)z->flushed & kWindowMask;
        uint64_t count = z->total - z->flushed;

        if (count > kWindowSize - from)
            count = kWindowSize - from;

        if (!TakeBytes(z->rows, z->window + from, (size_t)count))
            return false;

        z->flushed += count;
    }

    return true;
}

const uint16_t kLengthBase[31] = { 3,  4,  5,  6,  7,  8,  9,  10, 11,  13,  15,  17,  19,  23, 27, 31,
                                   35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258, 0,  0 };
const uint8_t kLengthExtra[31] = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0, 0, 0 };
const uint16_t kDistanceBase[32] = { 1,    2,    3,    4,    5,    7,     9,     13,    17,  25,   33,   49,   65,   97,   129, 193,
                                     257,  385,  513,  769,  1025, 1537,  2049,  3073,  4097, 6145, 8193, 12289, 16385, 24577, 0, 0 };
const uint8_t kDistanceExtra[32] = { 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 0, 0 };

bool HuffmanBlock(Inflater* z)
{
    for (;;)
    {
        if (z->total - z->flushed >= kFlushAt && !Flush(z))
            return false;

        int symbol = Decode(z, &z->length);

        if (symbol < 0)
            return false;

        if (symbol < 256)
        {
            Put(z, (uint8_t)symbol);
            continue;
        }

        if (symbol == 256)
            return !(z->paddedEnd && z->bitCount < 16);

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

        if ((uint64_t)distance > z->total)
            return false;

        uint32_t from = (uint32_t)(z->total - (uint64_t)distance);

        while (length--)
            Put(z, z->window[from++ & kWindowMask]);
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
    {
        if (!HasInput(z))
            return false;

        header[k++] = *z->in++;
    }

    const int length = header[0] | (header[1] << 8);
    const int inverse = header[2] | (header[3] << 8);

    if (inverse != (length ^ 0xFFFF))
        return false;

    for (int i = 0; i < length; ++i)
    {
        if (!HasInput(z))
            return false;

        Put(z, *z->in++);

        if (z->total - z->flushed >= kFlushAt && !Flush(z))
            return false;
    }

    return true;
}

bool Inflate(Inflater* z)
{
    const int cmf = NextByte(z);
    const int flags = NextByte(z);
    bool ok = (cmf & 15) == 8 && (cmf * 256 + flags) % 31 == 0 && !(flags & 32);

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

    return ok && Flush(z);
}

bool DepthAllowed(int colorType, int bitDepth)
{
    switch (colorType)
    {
    case 0: return bitDepth == 1 || bitDepth == 2 || bitDepth == 4 || bitDepth == 8 || bitDepth == 16;
    case 3: return bitDepth == 1 || bitDepth == 2 || bitDepth == 4 || bitDepth == 8;
    default: return bitDepth == 8 || bitDepth == 16;
    }
}
}

bool Png_Open(const uint8_t* file, size_t size, PngImage* image, const char** error)
{
    static const uint8_t kSignature[8] = { 0x89, 'P', 'N', 'G', '\r', '\n', 0x1A, '\n' };

    const char* failure = nullptr;
    const char*& why = error ? *error : failure;

    memset(image, 0, sizeof(*image));

    if (!file || size < 8 + 25 || memcmp(file, kSignature, 8) != 0)
    {
        why = "not a PNG file";
        return false;
    }

    for (int i = 0; i < 256; ++i)
        image->palette[i * 4 + 3] = 255;

    bool header = false;
    bool data = false;

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

            if (body[12] > 1)
            {
                why = "unknown interlace method";
                return false;
            }

            image->interlaced = body[12] == 1;
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
            data = data || length > 0;
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

    if (!DepthAllowed(image->colorType, image->bitDepth))
    {
        why = "a bit depth PNG does not allow for its color type";
        return false;
    }

    if (image->width <= 0 || image->height <= 0 || image->width > kMaxSide || image->height > kMaxSide)
    {
        why = "the picture is larger than 8192x8192";
        return false;
    }

    if (!data)
    {
        why = "no IDAT chunk";
        return false;
    }

    image->file = file;
    image->size = size;
    return true;
}

bool Png_Scale(const PngImage& image, uint8_t* out, int width, int height, size_t strideBytes, const char** error)
{
    const char* failure = nullptr;
    const char*& why = error ? *error : failure;

    if (!image.file || !out || width <= 0 || height <= 0 || strideBytes < (size_t)width * 4)
    {
        why = "nothing to scale";
        return false;
    }

    const size_t rowBytes = ((size_t)image.width * (size_t)image.channels * (size_t)image.bitDepth + 7) / 8;
    const size_t pixelBytes = ((size_t)image.channels * (size_t)image.bitDepth + 7) / 8;

    Scaler scaler;
    Rows rows;
    memset(&rows, 0, sizeof(rows));
    rows.image = &image;
    rows.scaler = &scaler;
    rows.bpp = pixelBytes ? pixelBytes : 1;
    rows.line = (uint8_t*)malloc(rowBytes + 1);
    rows.prior = (uint8_t*)malloc(rowBytes + 1);
    rows.rgba = (uint8_t*)malloc((size_t)image.width * 4);
    Inflater* const z = (Inflater*)malloc(sizeof(Inflater));
    const bool scaling = MakeScaler(&scaler, image, out, width, height, strideBytes);
    bool ok = scaling && rows.line && rows.prior && rows.rgba && z;

    if (!ok)
    {
        why = "out of memory";
    }
    else
    {
        StartPass(&rows, 0);
        memset(z, 0, offsetof(Inflater, window));
        z->file = image.file;
        z->size = image.size;
        z->chunk = 8;
        z->rows = &rows;
        ok = Inflate(z) && rows.done;

        if (!ok)
        {
            why = rows.badFilter ? "a scanline has an unknown filter" : "the image data is corrupt";
        }
        else if (image.interlaced)
        {
            for (int ty = 0; ty < height; ++ty)
                FinishRow(&scaler, ty, scaler.sums + (size_t)ty * (size_t)width * 4);
        }
        else if (scaler.row != height)
        {
            why = "the image data is corrupt";
            ok = false;
        }
    }

    free(z);
    free(rows.line);
    free(rows.prior);
    free(rows.rgba);
    FreeScaler(&scaler);
    return ok;
}
