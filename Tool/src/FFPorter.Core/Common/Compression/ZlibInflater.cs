using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace FFPorter.Core.Common.Compression;

public sealed class ZlibException(string message) : Exception(message);

public sealed class InflateResult
{
    public required byte[] Output { get; init; }

    public required bool Eof { get; init; }

    public required int UnusedDataLength { get; init; }

    public required int UnconsumedTailLength { get; init; }

    public required bool OutputLimitReached { get; init; }
}

public static class ZlibInflater
{
    public static InflateResult Decompress(ReadOnlySpan<byte> input, long maxLength = 0, long capacityHint = 0) =>
        Inflate(input, maxLength, capacityHint, raw: false);

    public static InflateResult DecompressRaw(ReadOnlySpan<byte> input, long maxLength = 0, long capacityHint = 0) =>
        Inflate(input, maxLength, capacityHint, raw: true);

    private static InflateResult Inflate(ReadOnlySpan<byte> input, long maxLength, long capacityHint, bool raw)
    {
        long limit = maxLength <= 0 ? long.MaxValue : maxLength;
        var inflater = new Inflater(input, limit, capacityHint, raw);
        Status status = inflater.Run();
        if (status == Status.Error)
            throw new ZlibException(inflater.ErrorCode is int code && inflater.ErrorMessage is null
                ? $"Error {code} while decompressing data"
                : $"Error {inflater.ErrorCode} while decompressing data: {inflater.ErrorMessage}");

        byte[] output = inflater.TakeOutput();
        return new InflateResult
        {
            Output = output,
            Eof = status == Status.StreamEnd,
            UnusedDataLength = status == Status.StreamEnd ? input.Length - inflater.InputPosition : 0,
            UnconsumedTailLength = status == Status.OutputLimit ? input.Length - inflater.InputPosition : 0,
            OutputLimitReached = status == Status.OutputLimit,
        };
    }

    internal enum Status { StreamEnd, Incomplete, OutputLimit, Error, BlockEnd }

    private ref struct Inflater
    {
        private readonly ReadOnlySpan<byte> _in;
        private readonly long _limit;
        private readonly bool _raw;
        private int _inPos;
        private ulong _bits;
        private int _nbits;
        private byte[] _out;
        private int _outPos;
        private HuffmanTable? _lit;
        private HuffmanTable? _dist;
        private HuffmanTable? _codes;

        public int? ErrorCode;
        public string? ErrorMessage;

        public Inflater(ReadOnlySpan<byte> input, long limit, long capacityHint, bool raw = false)
        {
            _in = input;
            _limit = limit;
            _raw = raw;
            long initial = capacityHint > 0 ? capacityHint : Math.Max(64L << 10, input.Length * 4L);
            initial = Math.Min(initial, Math.Min(limit, Array.MaxLength));
            _out = new byte[(int)Math.Max(initial, 0)];
        }

        public readonly int InputPosition => _inPos - (_nbits >> 3);

        public byte[] TakeOutput()
        {
            if (_out.Length != _outPos)
                Array.Resize(ref _out, _outPos);
            return _out;
        }

        public Status Run()
        {
            if (_raw)
                return Blocks();
            if (!Need(16))
                return Status.Incomplete;
            uint cmf = Peek(8);
            uint flg = (uint)(_bits >> 8) & 0xFF;
            if (((cmf << 8) + flg) % 31 != 0)
                return Fail("incorrect header check");
            if ((cmf & 15) != 8)
                return Fail("unknown compression method");
            if ((cmf >> 4) + 8 > 15)
                return Fail("invalid window size");
            Drop(16);
            if ((flg & 0x20) != 0)
            {
                if (!Need(32))
                    return Status.Incomplete;
                ErrorCode = 2;
                return Status.Error;
            }

            return Blocks();
        }

        private Status Blocks()
        {
            bool last = false;
            while (!last)
            {
                if (!Need(3))
                    return Status.Incomplete;
                last = (_bits & 1) != 0;
                uint type = (uint)(_bits >> 1) & 3;
                Drop(3);
                Status status = type switch
                {
                    0 => StoredBlock(),
                    1 => HuffmanBlock(FixedTables.Literal, FixedTables.Distance),
                    2 => DynamicBlock(),
                    _ => Fail("invalid block type"),
                };
                if (status != Status.BlockEnd)
                    return status;
            }

            AlignAndReturnBytes();
            if (_raw)
                return Status.StreamEnd;
            if (_in.Length - _inPos < 4)
                return Status.Incomplete;
            uint expected = BinaryPrimitives.ReadUInt32BigEndian(_in.Slice(_inPos, 4));
            _inPos += 4;
            if (expected != Adler32.Compute(_out.AsSpan(0, _outPos)))
                return Fail("incorrect data check");
            return Status.StreamEnd;
        }

        private Status StoredBlock()
        {
            AlignAndReturnBytes();
            if (_in.Length - _inPos < 4)
                return Status.Incomplete;
            uint lengths = BinaryPrimitives.ReadUInt32LittleEndian(_in.Slice(_inPos, 4));
            if ((lengths & 0xFFFF) != ((lengths >> 16) ^ 0xFFFF))
                return Fail("invalid stored block lengths");
            _inPos += 4;
            int length = (int)(lengths & 0xFFFF);
            while (length > 0)
            {
                int have = _in.Length - _inPos;
                long left = _limit - _outPos;
                int copy = length;
                if (copy > have)
                    copy = have;
                if (copy > left)
                    copy = (int)left;
                if (copy == 0)
                    return left == 0 ? Status.OutputLimit : Status.Incomplete;
                EnsureCapacity((long)_outPos + copy);
                _in.Slice(_inPos, copy).CopyTo(_out.AsSpan(_outPos));
                _inPos += copy;
                _outPos += copy;
                length -= copy;
            }
            return Status.BlockEnd;
        }

        private Status DynamicBlock()
        {
            if (!Need(14))
                return Status.Incomplete;
            int nlen = (int)Peek(5) + 257;
            Drop(5);
            int ndist = (int)Peek(5) + 1;
            Drop(5);
            int ncode = (int)Peek(4) + 4;
            Drop(4);
            if (nlen > 286 || ndist > 30)
                return Fail("too many length or distance symbols");

            Span<byte> codeLengths = stackalloc byte[19];
            codeLengths.Clear();
            for (int i = 0; i < ncode; i++)
            {
                if (!Need(3))
                    return Status.Incomplete;
                codeLengths[CodeLengthOrder[i]] = (byte)Peek(3);
                Drop(3);
            }
            _codes ??= new HuffmanTable(19);
            if (!_codes.Build(codeLengths, TableKind.Codes))
                return Fail("invalid code lengths set");

            int total = nlen + ndist;
            Span<byte> lengths = stackalloc byte[286 + 30];
            int have = 0;
            while (have < total)
            {
                int symbol = DecodeSymbol(_codes);
                if (symbol < 0)
                    return Status.Incomplete;
                if (symbol < 16)
                {
                    lengths[have++] = (byte)symbol;
                    continue;
                }
                int repeat;
                byte value;
                if (symbol == 16)
                {
                    if (!Need(2))
                        return Status.Incomplete;
                    if (have == 0)
                        return Fail("invalid bit length repeat");
                    value = lengths[have - 1];
                    repeat = 3 + (int)Peek(2);
                    Drop(2);
                }
                else if (symbol == 17)
                {
                    if (!Need(3))
                        return Status.Incomplete;
                    value = 0;
                    repeat = 3 + (int)Peek(3);
                    Drop(3);
                }
                else
                {
                    if (!Need(7))
                        return Status.Incomplete;
                    value = 0;
                    repeat = 11 + (int)Peek(7);
                    Drop(7);
                }
                if (have + repeat > total)
                    return Fail("invalid bit length repeat");
                lengths.Slice(have, repeat).Fill(value);
                have += repeat;
            }

            if (lengths[256] == 0)
                return Fail("invalid code -- missing end-of-block");
            _lit ??= new HuffmanTable(288);
            if (!_lit.Build(lengths[..nlen], TableKind.Lengths))
                return Fail("invalid literal/lengths set");
            _dist ??= new HuffmanTable(32);
            if (!_dist.Build(lengths.Slice(nlen, ndist), TableKind.Distances))
                return Fail("invalid distances set");
            return HuffmanBlock(_lit, _dist);
        }

        private Status HuffmanBlock(HuffmanTable literal, HuffmanTable distance)
        {
            while (true)
            {
                if (FastSymbols(literal, distance, out Status fastStatus))
                    return fastStatus;

                int symbol = DecodeSymbol(literal);
                if (symbol < 0)
                    return Status.Incomplete;
                if (symbol < 256)
                {
                    if (_outPos >= _limit)
                        return Status.OutputLimit;
                    if (_outPos >= _out.Length)
                        EnsureCapacity((long)_outPos + 1);
                    _out[_outPos++] = (byte)symbol;
                    continue;
                }
                if (symbol == 256)
                    return Status.BlockEnd;
                if (symbol >= 286)
                    return Fail("invalid literal/length code");

                int index = symbol - 257;
                int length = LengthBase[index];
                int extra = LengthExtra[index];
                if (extra > 0)
                {
                    if (!Need(extra))
                        return Status.Incomplete;
                    length += (int)Peek(extra);
                    Drop(extra);
                }

                int distanceSymbol = DecodeSymbol(distance);
                if (distanceSymbol < 0)
                    return Status.Incomplete;
                if (distanceSymbol >= 30)
                    return Fail("invalid distance code");
                int back = DistanceBase[distanceSymbol];
                int distanceExtra = DistanceExtra[distanceSymbol];
                if (distanceExtra > 0)
                {
                    if (!Need(distanceExtra))
                        return Status.Incomplete;
                    back += (int)Peek(distanceExtra);
                    Drop(distanceExtra);
                }

                if (_outPos >= _limit)
                    return Status.OutputLimit;
                if (back > _outPos)
                    return Fail("invalid distance too far back");
                long room = _limit - _outPos;
                int count = room < length ? (int)room : length;
                EnsureCapacity((long)_outPos + count);
                OverlapCopy.Forward(_out, _outPos, back, count);
                _outPos += count;
                if (count < length)
                    return Status.OutputLimit;
            }
        }

        private bool FastSymbols(HuffmanTable literal, HuffmanTable distance, out Status status)
        {
            status = Status.BlockEnd;
            ReadOnlySpan<byte> input = _in;
            byte[] output = _out;
            int[] literalFast = literal.Fast;
            int[] distanceFast = distance.Fast;
            long room = Math.Min(_limit, output.Length);
            int outEnd = (int)Math.Min(room - 258, int.MaxValue);
            int inEnd = input.Length - 16;
            ulong bits = _bits;
            int nbits = _nbits;
            int inPos = _inPos;
            int outPos = _outPos;
            bool finished = false;

            while (outPos <= outEnd && inPos <= inEnd)
            {
                if (nbits < 48)
                {
                    bits |= BinaryPrimitives.ReadUInt64LittleEndian(input.Slice(inPos, 8)) << nbits;
                    int bytes = (63 - nbits) >> 3;
                    inPos += bytes;
                    nbits += bytes << 3;
                }

                int entry = literalFast[(int)(bits & HuffmanTable.FastMask)];
                int symbol;
                if (entry >= 0)
                {
                    int length = entry & 15;
                    bits >>= length;
                    nbits -= length;
                    symbol = entry >> 4;
                }
                else
                {
                    (_bits, _nbits, _inPos) = (bits, nbits, inPos);
                    symbol = DecodeLong(literal);
                    (bits, nbits, inPos) = (_bits, _nbits, _inPos);
                }

                if (symbol < 256)
                {
                    output[outPos++] = (byte)symbol;
                    continue;
                }
                if (symbol == 256)
                {
                    finished = true;
                    break;
                }
                if (symbol >= 286)
                {
                    status = Fail("invalid literal/length code");
                    finished = true;
                    break;
                }

                int index = symbol - 257;
                int matchLength = LengthBase[index];
                int extra = LengthExtra[index];
                if (extra > 0)
                {
                    matchLength += (int)(bits & ((1UL << extra) - 1));
                    bits >>= extra;
                    nbits -= extra;
                }

                entry = distanceFast[(int)(bits & HuffmanTable.FastMask)];
                int distanceSymbol;
                if (entry >= 0)
                {
                    int length = entry & 15;
                    bits >>= length;
                    nbits -= length;
                    distanceSymbol = entry >> 4;
                }
                else
                {
                    (_bits, _nbits, _inPos) = (bits, nbits, inPos);
                    distanceSymbol = DecodeLong(distance);
                    (bits, nbits, inPos) = (_bits, _nbits, _inPos);
                }
                if (distanceSymbol >= 30)
                {
                    status = Fail("invalid distance code");
                    finished = true;
                    break;
                }
                int back = DistanceBase[distanceSymbol];
                int distanceExtra = DistanceExtra[distanceSymbol];
                if (distanceExtra > 0)
                {
                    back += (int)(bits & ((1UL << distanceExtra) - 1));
                    bits >>= distanceExtra;
                    nbits -= distanceExtra;
                }
                if (back > outPos)
                {
                    status = Fail("invalid distance too far back");
                    finished = true;
                    break;
                }

                int from = outPos - back;
                if (back >= matchLength)
                {
                    output.AsSpan(from, matchLength).CopyTo(output.AsSpan(outPos, matchLength));
                }
                else if (back == 1)
                {
                    output.AsSpan(outPos, matchLength).Fill(output[from]);
                }
                else
                {
                    for (int i = 0; i < matchLength; i++)
                        output[outPos + i] = output[from + i];
                }
                outPos += matchLength;
            }

            (_bits, _nbits, _inPos, _outPos) = (bits, nbits, inPos, outPos);
            return finished;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int DecodeSymbol(HuffmanTable table)
        {
            if (_nbits < HuffmanTable.MaxBits)
                Refill();
            int entry = table.Fast[(int)(_bits & HuffmanTable.FastMask)];
            if (entry >= 0)
            {
                int length = entry & 15;
                if (length > _nbits)
                    return -1;
                Drop(length);
                return entry >> 4;
            }
            return DecodeLong(table);
        }

        private int DecodeLong(HuffmanTable table)
        {
            int code = 0, first = 0, index = 0;
            for (int length = 1; length <= HuffmanTable.MaxBits; length++)
            {
                if (length > _nbits)
                    return -1;
                code |= (int)((_bits >> (length - 1)) & 1);
                int count = table.Count[length];
                if (code - count < first)
                {
                    Drop(length);
                    return table.Symbols[index + (code - first)];
                }
                index += count;
                first += count;
                first <<= 1;
                code <<= 1;
            }
            return HuffmanTable.InvalidSymbol;
        }

        private Status Fail(string message)
        {
            ErrorCode = -3;
            ErrorMessage = message;
            return Status.Error;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Need(int count)
        {
            if (_nbits < count)
                Refill();
            return _nbits >= count;
        }

        private void Refill()
        {
            if (_inPos + 8 <= _in.Length)
            {
                _bits |= BinaryPrimitives.ReadUInt64LittleEndian(_in.Slice(_inPos, 8)) << _nbits;
                int bytes = (63 - _nbits) >> 3;
                _inPos += bytes;
                _nbits += bytes << 3;
                _bits &= _nbits >= 64 ? ulong.MaxValue : (1UL << _nbits) - 1;
                return;
            }
            while (_nbits <= 56 && _inPos < _in.Length)
            {
                _bits |= (ulong)_in[_inPos++] << _nbits;
                _nbits += 8;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly uint Peek(int count) => (uint)(_bits & ((1UL << count) - 1));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Drop(int count)
        {
            _bits >>= count;
            _nbits -= count;
        }

        private void AlignAndReturnBytes()
        {
            Drop(_nbits & 7);
            _inPos -= _nbits >> 3;
            _bits = 0;
            _nbits = 0;
        }

        private void EnsureCapacity(long needed)
        {
            if (needed <= _out.Length)
                return;
            if (needed > Array.MaxLength)
                throw new ZlibOutputTooLargeException(needed);
            long grown = Math.Max(needed, Math.Min((long)_out.Length * 2, Array.MaxLength));
            grown = Math.Min(grown, Math.Min(_limit, Array.MaxLength));
            Array.Resize(ref _out, (int)Math.Max(grown, needed));
        }
    }

    private static readonly byte[] CodeLengthOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];

    private static readonly ushort[] LengthBase =
        [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];

    private static readonly byte[] LengthExtra =
        [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];

    private static readonly ushort[] DistanceBase =
        [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577];

    private static readonly byte[] DistanceExtra =
        [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];

    internal enum TableKind { Codes, Lengths, Distances }

    private static class FixedTables
    {
        public static readonly HuffmanTable Literal = BuildLiteral();
        public static readonly HuffmanTable Distance = BuildDistance();

        private static HuffmanTable BuildLiteral()
        {
            var lengths = new byte[288];
            lengths.AsSpan(0, 144).Fill(8);
            lengths.AsSpan(144, 112).Fill(9);
            lengths.AsSpan(256, 24).Fill(7);
            lengths.AsSpan(280, 8).Fill(8);
            var table = new HuffmanTable(288);
            table.Build(lengths, TableKind.Lengths);
            return table;
        }

        private static HuffmanTable BuildDistance()
        {
            var lengths = new byte[32];
            lengths.AsSpan().Fill(5);
            var table = new HuffmanTable(32);
            table.Build(lengths, TableKind.Distances);
            return table;
        }
    }

    internal sealed class HuffmanTable(int maxSymbols)
    {
        public const int MaxBits = 15;
        public const int FastBits = 10;
        public const int FastSize = 1 << FastBits;
        public const ulong FastMask = FastSize - 1;
        public const int InvalidSymbol = 0x7FF;
        private const int LongCode = -1;
        private const int InvalidEntry = (InvalidSymbol << 4) | 1;

        public readonly int[] Fast = new int[FastSize];
        public readonly ushort[] Count = new ushort[MaxBits + 1];
        public readonly ushort[] Symbols = new ushort[maxSymbols];

        public bool Build(ReadOnlySpan<byte> lengths, TableKind kind)
        {
            Span<int> count = stackalloc int[MaxBits + 1];
            count.Clear();
            foreach (byte length in lengths)
                count[length]++;
            count[0] = 0;

            int max = MaxBits;
            while (max >= 1 && count[max] == 0)
                max--;

            Array.Clear(Count);
            if (max == 0)
            {
                Array.Fill(Fast, kind == TableKind.Codes ? (0 << 4) | 1 : InvalidEntry);
                return true;
            }

            int left = 1;
            for (int length = 1; length <= MaxBits; length++)
            {
                left <<= 1;
                left -= count[length];
                if (left < 0)
                    return false;
            }
            if (left > 0 && (kind == TableKind.Codes || max != 1))
                return false;

            Span<int> offsets = stackalloc int[MaxBits + 2];
            offsets.Clear();
            for (int length = 1; length < MaxBits; length++)
                offsets[length + 1] = offsets[length] + count[length];
            for (int symbol = 0; symbol < lengths.Length; symbol++)
            {
                if (lengths[symbol] != 0)
                    Symbols[offsets[lengths[symbol]]++] = (ushort)symbol;
            }
            for (int length = 1; length <= MaxBits; length++)
                Count[length] = (ushort)count[length];

            Array.Fill(Fast, InvalidEntry);
            int code = 0, index = 0;
            for (int length = 1; length <= MaxBits; length++)
            {
                for (int k = 0; k < count[length]; k++)
                {
                    int symbol = Symbols[index++];
                    int reversed = Reverse(code, length);
                    if (length <= FastBits)
                    {
                        for (int slot = reversed; slot < FastSize; slot += 1 << length)
                            Fast[slot] = (symbol << 4) | length;
                    }
                    else
                    {
                        Fast[reversed & (FastSize - 1)] = LongCode;
                    }
                    code++;
                }
                code <<= 1;
            }
            return true;
        }

        private static int Reverse(int code, int length)
        {
            int result = 0;
            for (int i = 0; i < length; i++)
            {
                result = (result << 1) | (code & 1);
                code >>= 1;
            }
            return result;
        }
    }
}

public sealed class ZlibOutputTooLargeException(long needed)
    : Exception($"Decompressed data needs {needed} bytes, more than the {Array.MaxLength}-byte array limit of this build");
