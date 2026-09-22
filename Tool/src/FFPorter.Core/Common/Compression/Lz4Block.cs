using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace FFPorter.Core.Common.Compression;

public sealed class Lz4BlockException(string message) : Exception(message);

public static class Lz4Block
{
    public const int MaxInputSize = 0x7E00_0000;

    private const int MinMatch = 4;
    private const int LastLiterals = 5;
    private const int MfLimit = 12;
    private const int MinLength = MfLimit + 1;
    private const int MaxDistance = 65535;
    private const int SkipTrigger = 6;
    private const int HashLog = 12;
    private const ulong Prime5Bytes = 889523592379UL;
    private const int MlBits = 4;
    private const int MlMask = (1 << MlBits) - 1;
    private const int RunMask = (1 << (8 - MlBits)) - 1;
    private const int FastLoopSafeDistance = 64;
    private const int MatchSafeguardDistance = 2 * 8 - MinMatch;
    private const int AccelerationMax = 65537;

    public static int CompressBound(int inputSize) =>
        (uint)inputSize > MaxInputSize ? 0 : inputSize + inputSize / 255 + 16;

    public static byte[] Compress(ReadOnlySpan<byte> source, bool storeSize = true, int acceleration = 1)
    {
        int bound = CompressBound(source.Length);
        if (bound <= 0)
            throw new Lz4BlockException("Input too large for LZ4 API");
        int header = storeSize ? 4 : 0;
        byte[] buffer = new byte[header + bound];
        if (storeSize)
            BinaryPrimitives.WriteInt32LittleEndian(buffer, source.Length);
        int written = CompressBlock(source, buffer.AsSpan(header), Math.Clamp(acceleration, 1, AccelerationMax));
        Array.Resize(ref buffer, header + written);
        return buffer;
    }

    public static int CompressBlock(ReadOnlySpan<byte> source, Span<byte> destination, int acceleration = 1)
    {
        int inputSize = source.Length;
        if (inputSize == 0)
        {
            destination[0] = 0;
            return 1;
        }

        Span<uint> hashTable = stackalloc uint[1 << HashLog];
        int ip = 0, anchor = 0, op = 0;
        int iend = inputSize;
        int mflimitPlusOne = iend - MfLimit + 1;
        int matchLimit = iend - LastLiterals;
        int match = 0;
        int token = 0;

        if (inputSize < MinLength)
            goto LastLiteralsLabel;

        hashTable[(int)Hash5(source, ip)] = 0;
        ip++;
        uint forwardH = Hash5(source, ip);

        while (true)
        {
            {
                int forwardIp = ip;
                int step = 1;
                int searchMatchNb = acceleration << SkipTrigger;
                while (true)
                {
                    uint h = forwardH;
                    int current = forwardIp;
                    int matchIndex = (int)hashTable[(int)h];
                    ip = forwardIp;
                    forwardIp += step;
                    step = searchMatchNb++ >> SkipTrigger;

                    if (forwardIp > mflimitPlusOne)
                        goto LastLiteralsLabel;

                    match = matchIndex;
                    forwardH = Hash5(source, forwardIp);
                    hashTable[(int)h] = (uint)current;

                    if (matchIndex + MaxDistance < current)
                        continue;
                    if (Read32(source, match) == Read32(source, ip))
                        break;
                }
            }

            if (match > 0 && source[ip - 1] == source[match - 1])
            {
                do
                {
                    ip--;
                    match--;
                } while (ip > anchor && match > 0 && source[ip - 1] == source[match - 1]);
            }

            {
                int literalLength = ip - anchor;
                token = op++;
                if (literalLength >= RunMask)
                {
                    int remaining = literalLength - RunMask;
                    destination[token] = RunMask << MlBits;
                    for (; remaining >= 255; remaining -= 255)
                        destination[op++] = 255;
                    destination[op++] = (byte)remaining;
                }
                else
                {
                    destination[token] = (byte)(literalLength << MlBits);
                }
                source.Slice(anchor, literalLength).CopyTo(destination.Slice(op));
                op += literalLength;
            }

        NextMatch:
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(op), (ushort)(ip - match));
            op += 2;
            {
                int matchCode = Count(source, ip + MinMatch, match + MinMatch, matchLimit);
                ip += matchCode + MinMatch;
                if (matchCode >= MlMask)
                {
                    destination[token] += MlMask;
                    matchCode -= MlMask;
                    for (; matchCode >= 255; matchCode -= 255)
                        destination[op++] = 255;
                    destination[op++] = (byte)matchCode;
                }
                else
                {
                    destination[token] += (byte)matchCode;
                }
            }

            anchor = ip;
            if (ip >= mflimitPlusOne)
                break;

            hashTable[(int)Hash5(source, ip - 2)] = (uint)(ip - 2);

            {
                uint h = Hash5(source, ip);
                int current = ip;
                int matchIndex = (int)hashTable[(int)h];
                hashTable[(int)h] = (uint)current;
                if (matchIndex + MaxDistance >= current && Read32(source, matchIndex) == Read32(source, ip))
                {
                    match = matchIndex;
                    token = op++;
                    destination[token] = 0;
                    goto NextMatch;
                }
            }

            forwardH = Hash5(source, ++ip);
        }

    LastLiteralsLabel:
        {
            int lastRun = iend - anchor;
            if (lastRun >= RunMask)
            {
                int accumulator = lastRun - RunMask;
                destination[op++] = RunMask << MlBits;
                for (; accumulator >= 255; accumulator -= 255)
                    destination[op++] = 255;
                destination[op++] = (byte)accumulator;
            }
            else
            {
                destination[op++] = (byte)(lastRun << MlBits);
            }
            source.Slice(anchor, lastRun).CopyTo(destination.Slice(op));
            op += lastRun;
        }
        return op;
    }

    public static byte[] Decompress(ReadOnlySpan<byte> source, int uncompressedSize)
    {
        byte[] output = new byte[uncompressedSize];
        int written = DecompressInto(source, output);
        if (written != uncompressedSize)
            Array.Resize(ref output, written);
        return output;
    }

    public static byte[] DecompressUnsized(ReadOnlySpan<byte> input, string path, long at)
    {
        var output = new byte[Math.Max(64, input.Length * 4)];
        int ip = 0, op = 0;
        void Ensure(int needed)
        {
            if (op + needed > output.Length)
                Array.Resize(ref output, Math.Max(output.Length * 2, op + needed));
        }
        int Length(ReadOnlySpan<byte> source, int start, ref int position)
        {
            int length = start;
            if (start != 15)
                return length;
            byte more;
            do
            {
                if (position >= source.Length)
                    throw new InvalidDataException($"{path}: truncated LZ4 length at 0x{at:x}");
                more = source[position++];
                length += more;
            }
            while (more == 255);
            return length;
        }
        while (ip < input.Length)
        {
            byte token = input[ip++];
            int literals = Length(input, token >> 4, ref ip);
            if (ip + literals > input.Length)
                throw new InvalidDataException($"{path}: LZ4 literals run past the block at 0x{at:x}");
            Ensure(literals);
            input.Slice(ip, literals).CopyTo(output.AsSpan(op));
            ip += literals;
            op += literals;
            if (ip >= input.Length)
                break;
            if (ip + 2 > input.Length)
                throw new InvalidDataException($"{path}: truncated LZ4 match at 0x{at:x}");
            int offset = input[ip] | (input[ip + 1] << 8);
            ip += 2;
            int match = Length(input, token & 15, ref ip) + 4;
            if (offset == 0 || offset > op)
                throw new InvalidDataException($"{path}: bad LZ4 match offset at 0x{at:x}");
            Ensure(match);
            for (int i = 0; i < match; i++)
                output[op + i] = output[op - offset + i];
            op += match;
        }
        Array.Resize(ref output, op);
        return output;
    }

    public static int DecompressInto(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        int result = DecompressSafe(source, destination);
        if (result < 0)
            throw new Lz4BlockException($"Decompression failed: corrupt input or insufficient space in destination buffer. Error code: {-(long)result}");
        return result;
    }

    public static int DecompressSafe(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        int srcSize = src.Length;
        int outputSize = dst.Length;
        int iend = srcSize;
        int oend = outputSize;
        int shortiend = iend - 14 - 2;
        int shortoend = oend - 14 - 18;
        int ip = 0, op = 0;
        int token;
        long length;
        long cpy;
        long addl;
        int offset;
        int match;

        if (outputSize == 0)
            return srcSize == 1 && src[0] == 0 ? 0 : -1;
        if (srcSize == 0)
            return -1;

        if (oend - op < FastLoopSafeDistance)
            goto SafeDecode;

    FastLoop:
        token = src[ip++];
        length = token >> MlBits;
        if (length == RunMask)
        {
            if (!ReadVariableLength(src, ref ip, iend - RunMask, true, out addl))
                goto OutputError;
            length += addl;
            cpy = op + length;
            if (cpy > oend - 32 || ip + length > iend - 32)
                goto SafeLiteralCopy;
            src.Slice(ip, (int)length).CopyTo(dst.Slice(op));
            ip += (int)length;
            op = (int)cpy;
        }
        else
        {
            cpy = op + length;
            if (ip > iend - (16 + 1))
                goto SafeLiteralCopy;
            src.Slice(ip, (int)length).CopyTo(dst.Slice(op));
            ip += (int)length;
            op = (int)cpy;
        }

        offset = src[ip] | (src[ip + 1] << 8);
        ip += 2;
        match = op - offset;
        length = token & MlMask;

        if (length == MlMask)
        {
            if (!ReadVariableLength(src, ref ip, iend - LastLiterals + 1, false, out addl))
                goto OutputError;
            length += addl + MinMatch;
            if (match < 0)
                goto OutputError;
            if (op + length >= oend - FastLoopSafeDistance)
                goto SafeMatchCopy;
        }
        else
        {
            length += MinMatch;
            if (op + length >= oend - FastLoopSafeDistance)
                goto SafeMatchCopy;
            if (match >= 0 && offset >= 8)
            {
                OverlapCopy.Forward(dst, op, offset, (int)length);
                op += (int)length;
                goto FastLoop;
            }
        }

        if (match < 0)
            goto OutputError;
        OverlapCopy.Forward(dst, op, offset, (int)length);
        op += (int)length;
        goto FastLoop;

    SafeDecode:
        token = src[ip++];
        length = token >> MlBits;

        if (length != RunMask && ip < shortiend && op <= shortoend)
        {
            src.Slice(ip, (int)length).CopyTo(dst.Slice(op));
            op += (int)length;
            ip += (int)length;
            length = token & MlMask;
            offset = src[ip] | (src[ip + 1] << 8);
            ip += 2;
            match = op - offset;
            if (length != MlMask && offset >= 8 && match >= 0)
            {
                OverlapCopy.Forward(dst, op, offset, (int)length + MinMatch);
                op += (int)length + MinMatch;
                goto SafeDecode;
            }
            goto CopyMatchLength;
        }

        if (length == RunMask)
        {
            if (!ReadVariableLength(src, ref ip, iend - RunMask, true, out addl))
                goto OutputError;
            length += addl;
        }
        cpy = op + length;

    SafeLiteralCopy:
        if (cpy > oend - MfLimit || ip + length > iend - (2 + 1 + LastLiterals))
        {
            if (ip + length != iend || cpy > oend)
                goto OutputError;
            src.Slice(ip, (int)length).CopyTo(dst.Slice(op));
            ip += (int)length;
            op += (int)length;
            return op;
        }
        src.Slice(ip, (int)length).CopyTo(dst.Slice(op));
        ip += (int)length;
        op = (int)cpy;

        offset = src[ip] | (src[ip + 1] << 8);
        ip += 2;
        match = op - offset;
        length = token & MlMask;

    CopyMatchLength:
        if (length == MlMask)
        {
            if (!ReadVariableLength(src, ref ip, iend - LastLiterals + 1, false, out addl))
                goto OutputError;
            length += addl;
        }
        length += MinMatch;

    SafeMatchCopy:
        if (match < 0)
            goto OutputError;
        cpy = op + length;
        if (cpy > oend - MatchSafeguardDistance && cpy > oend - LastLiterals)
            goto OutputError;
        OverlapCopy.Forward(dst, op, offset, (int)length);
        op = (int)cpy;
        goto SafeDecode;

    OutputError:
        return -ip - 1;
    }

    private static bool ReadVariableLength(ReadOnlySpan<byte> src, ref int ip, int limit, bool initialCheck, out long length)
    {
        length = 0;
        if (initialCheck && ip >= limit)
            return false;
        uint s;
        do
        {
            s = src[ip];
            ip++;
            length += s;
            if (ip > limit)
                return false;
        } while (s == 255);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Hash5(ReadOnlySpan<byte> source, int position) =>
        (uint)(((BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(position)) << 24) * Prime5Bytes) >> (64 - HashLog));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Read32(ReadOnlySpan<byte> source, int position) =>
        BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(position));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Count(ReadOnlySpan<byte> source, int pIn, int pMatch, int pInLimit)
    {
        int available = pInLimit - pIn;
        return available <= 0 ? 0 : source.Slice(pIn, available).CommonPrefixLength(source.Slice(pMatch, available));
    }
}
