using System.Buffers.Binary;
using FFPorter.Core.T7.Harness;
using FFPorter.Core.T7.Link;

namespace FFPorter.Core.T7.Port;

public sealed class T7AssetRewrite
{
    private readonly T7PortContext _context;
    private readonly T7Walk.Asset _asset;
    private readonly T7OutputAsset _output;
    private long _copyStart = -1, _copyEnd = -1;
    private bool _copyDeferred;

    public T7AssetRewrite(T7PortContext context, T7Walk.Asset asset, T7OutputAsset output)
    {
        _context = context;
        _asset = asset;
        _output = output;
    }

    public T7AssetRewrite(T7PortContext context, T7Walk.Asset asset, T7OutputAsset output, int startRead) : this(context, asset, output)
    {
        Position = startRead;
    }

    public void FinishAt(int endRead)
    {
        if (Position != endRead)
            throw new InvalidDataException($"{Label}: converted reads up to {Position}, the sub-asset ends at {endRead}");
        if (_pending > 0)
            throw new InvalidOperationException($"{Label}: {_pending} rebuilt reads were never committed");
        Flush();
    }

    public void CopySpan(T7Walk.Span read) => AppendCopy(read.FileOffset, read.Size, deferred: false);

    public void FlushPending() => Flush();

    public T7PortContext Context => _context;
    public T7WalkIndex Pc => _context.Pc;
    public T7Walk.Asset Asset => _asset;
    public T7OutputAsset Output => _output;

    public void InsertCopy(T7WalkIndex source, long fileOffset, long size)
    {
        Flush();
        if (size > 0)
            _output.Main.Add(new T7CopyPiece(source, fileOffset, size));
    }
    public IReadOnlyList<T7Walk.Span> Reads => _asset.Reads;

    public int Position { get; private set; }

    public bool Done => Position >= _asset.Reads.Count;

    public T7Walk.Span Peek(int ahead = 0)
    {
        int i = Position + ahead;
        if (i >= _asset.Reads.Count)
            throw new InvalidDataException($"{Label}: read {i} wanted, the PC asset has {_asset.Reads.Count}");
        return _asset.Reads[i];
    }

    public bool HasAhead(int ahead) => Position + ahead < _asset.Reads.Count;

    public string Label => $"{T7AssetTypes.Name(_asset.Type)} '{_asset.Name}'";

    public ReadOnlySpan<byte> Bytes(T7Walk.Span read) => Pc.Zone.AsSpan((int)read.FileOffset, (int)read.Size);

    public T7Walk.Span Take()
    {
        T7Walk.Span read = Peek();
        Position++;
        return read;
    }

    public T7Walk.Span Take(long size)
    {
        T7Walk.Span read = Peek();
        if (read.Size != size)
            throw new InvalidDataException($"{Label}: PC read {Position} at 0x{read.FileOffset:x} is {read.Size} bytes, {size} expected");
        Position++;
        return read;
    }

    public void Copy(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            T7Walk.Span read = Take();
            AppendCopy(read.FileOffset, read.Size, deferred: false);
        }
    }

    public void CopyChecked(long size)
    {
        T7Walk.Span read = Take(size);
        AppendCopy(read.FileOffset, read.Size, deferred: false);
    }

    public void CopyTo(int end)
    {
        while (Position < end)
            Copy();
    }

    public void CopyRest() => CopyTo(_asset.Reads.Count);

    public T7Walk.Span CopyDeferred()
    {
        T7Walk.Span read = Take();
        AppendCopy(read.FileOffset, read.Size, deferred: true);
        return read;
    }

    public void CopyPcDeferred(T7Walk.Span read) => AppendCopy(read.FileOffset, read.Size, deferred: true);

    public T7StructBuilder Rebuild(int outputSize, bool deferred = false) => Rebuild(Take(), outputSize, deferred);

    public T7StructBuilder Rebuild(T7Walk.Span read, int outputSize, bool deferred = false) => new(this, read.FileOffset, (int)read.Size, outputSize, deferred);

    public T7StructBuilder Insert(int outputSize, bool deferred = false) => new(this, -1, 0, outputSize, deferred);

    public void Insert(byte[] data, bool deferred = false)
    {
        Flush();
        (deferred ? _output.Deferred : _output.Main).Add(new T7DataPiece(data, []));
    }

    public T7Walk.Span Take(T7WalkKind kind)
    {
        T7Walk.Span read = Peek();
        if (read.Kind != kind)
            throw new InvalidDataException($"{Label}: PC read {Position} at 0x{read.FileOffset:x} is a {read.Kind}, a {kind} was expected");
        Position++;
        return read;
    }

    public void Finish()
    {
        if (!Done)
            throw new InvalidDataException($"{Label}: {Reads.Count - Position} PC reads left unconverted (next at 0x{Peek().FileOffset:x}, {Peek().Size} bytes)");
        if (_pending > 0)
            throw new InvalidOperationException($"{Label}: {_pending} rebuilt reads were never committed");
        Flush();
    }

    private int _pending;

    internal (List<T7Piece> List, int Index) Reserve(bool deferred)
    {
        Flush();
        List<T7Piece> list = deferred ? _output.Deferred : _output.Main;
        list.Add(T7PendingPiece.Instance);
        _pending++;
        return (list, list.Count - 1);
    }

    internal void Fill((List<T7Piece> List, int Index) slot, T7DataPiece piece)
    {
        slot.List[slot.Index] = piece;
        _pending--;
    }

    private void AppendCopy(long start, long size, bool deferred)
    {
        if (size == 0)
            return;
        if (_copyStart >= 0 && _copyEnd == start && _copyDeferred == deferred)
        {
            _copyEnd = start + size;
            return;
        }
        Flush();
        (_copyStart, _copyEnd, _copyDeferred) = (start, start + size, deferred);
    }

    private void Flush()
    {
        if (_copyStart < 0)
            return;
        (_copyDeferred ? _output.Deferred : _output.Main).Add(new T7CopyPiece(Pc, _copyStart, _copyEnd - _copyStart));
        _copyStart = _copyEnd = -1;
    }
}

internal sealed record T7PendingPiece : T7Piece
{
    public static readonly T7PendingPiece Instance = new();
    public override long Length => 0;
}

public sealed class T7StructBuilder
{
    private readonly T7AssetRewrite _owner;
    private readonly List<T7Fixup> _fixups = [];
    private readonly List<T7PieceSpan> _spans = [];
    private readonly (List<T7Piece> List, int Index) _slot;
    private bool _committed;

    internal T7StructBuilder(T7AssetRewrite owner, long sourceOffset, int sourceSize, int outputSize, bool deferred)
    {
        _owner = owner;
        SourceOffset = sourceOffset;
        SourceSize = sourceSize;
        Data = new byte[outputSize];
        _slot = owner.Reserve(deferred);
    }

    public long SourceOffset { get; }
    public int SourceSize { get; }
    public byte[] Data { get; }
    public ReadOnlySpan<byte> Source => SourceOffset < 0 ? [] : _owner.Pc.Zone.AsSpan((int)SourceOffset, SourceSize);

    public T7StructBuilder Move(int from, int to, int size)
    {
        if (size <= 0)
            return this;
        if (SourceOffset < 0 || from < 0 || from + size > SourceSize || to < 0 || to + size > Data.Length)
            throw new ArgumentOutOfRangeException(nameof(size), $"{_owner.Label}: move [{from}, {from + size}) -> {to} outside the {SourceSize}-byte PC read or the {Data.Length}-byte output");
        Source.Slice(from, size).CopyTo(Data.AsSpan(to));
        _spans.Add(new T7PieceSpan(SourceOffset + from, size, to));
        foreach ((long field, T7PointerKind kind, T7BlockAddress target) in _owner.Pc.PointersIn(SourceOffset + from, size))
        {
            if (kind is T7PointerKind.Packed or T7PointerKind.PackedAlias)
                _fixups.Add(new T7Fixup(to + (field - SourceOffset - from), new T7SourceTarget(_owner.Pc, target, field)));
        }
        return this;
    }

    public T7StructBuilder MoveAll() => Move(0, 0, Math.Min(SourceSize, Data.Length));

    public T7StructBuilder Elements(int pcElementSize, int outputElementSize, Action<T7ElementBuilder> element)
    {
        if (pcElementSize <= 0 || SourceSize % pcElementSize != 0)
            throw new InvalidDataException($"{_owner.Label}: PC read of {SourceSize} bytes is not an array of {pcElementSize}-byte elements");
        int count = SourceSize / pcElementSize;
        if (count * outputElementSize != Data.Length)
            throw new InvalidDataException($"{_owner.Label}: {count} elements of {outputElementSize} bytes do not fill a {Data.Length}-byte read");
        for (int i = 0; i < count; i++)
            element(new T7ElementBuilder(this, i, i * pcElementSize, pcElementSize, i * outputElementSize, outputElementSize));
        return this;
    }

    public T7StructBuilder MapSource(int from, int to, int size)
    {
        if (SourceOffset >= 0 && size > 0)
            _spans.Add(new T7PieceSpan(SourceOffset + from, size, to));
        return this;
    }

    public T7StructBuilder CarryPointer(int from, int to)
    {
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(Source[from..]);
        BinaryPrimitives.WriteUInt64LittleEndian(Data.AsSpan(to), value);
        if (_owner.Pc.Pointers.TryGetValue(SourceOffset + from, out var pointer) && pointer.Kind is T7PointerKind.Packed or T7PointerKind.PackedAlias)
            _fixups.Add(new T7Fixup(to, new T7SourceTarget(_owner.Pc, pointer.Target, SourceOffset + from)));
        return this;
    }

    public T7StructBuilder Pointer(int at, T7Target target)
    {
        ClearPointer(at);
        _fixups.Add(new T7Fixup(at, target));
        return this;
    }

    public T7StructBuilder ClearPointer(int at)
    {
        _fixups.RemoveAll(f => f.Offset == at);
        return this;
    }

    public T7StructBuilder Marker(int at, ulong value)
    {
        ClearPointer(at);
        return U64(at, value);
    }

    public T7StructBuilder CarryPointerFrom(long pcField, int at)
    {
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_owner.Pc.Zone.AsSpan((int)pcField));
        ClearPointer(at);
        BinaryPrimitives.WriteUInt64LittleEndian(Data.AsSpan(at), value);
        if (_owner.Pc.Pointers.TryGetValue(pcField, out var pointer) && pointer.Kind is T7PointerKind.Packed or T7PointerKind.PackedAlias)
            _fixups.Add(new T7Fixup(at, new T7SourceTarget(_owner.Pc, pointer.Target, pcField)));
        return this;
    }

    public T7StructBuilder CopyFile(long pcFileOffset, int at, int size)
    {
        if (size <= 0)
            return this;
        _owner.Pc.Zone.AsSpan((int)pcFileOffset, size).CopyTo(Data.AsSpan(at));
        _spans.Add(new T7PieceSpan(pcFileOffset, size, at));
        foreach ((long field, T7PointerKind kind, T7BlockAddress target) in _owner.Pc.PointersIn(pcFileOffset, size))
        {
            if (kind is T7PointerKind.Packed or T7PointerKind.PackedAlias)
                _fixups.Add(new T7Fixup(at + (field - pcFileOffset), new T7SourceTarget(_owner.Pc, target, field)));
        }
        return this;
    }

    private List<(string Key, long Offset)>? _pools;

    public T7StructBuilder DefinePool(string key, int at = 0)
    {
        (_pools ??= []).Add((key, at));
        return this;
    }

    public T7StructBuilder Write(int at, ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(Data.AsSpan(at));
        return this;
    }

    public T7StructBuilder U8(int at, byte value) { Data[at] = value; return this; }
    public T7StructBuilder U16(int at, ushort value) { BinaryPrimitives.WriteUInt16LittleEndian(Data.AsSpan(at), value); return this; }
    public T7StructBuilder U32(int at, uint value) { BinaryPrimitives.WriteUInt32LittleEndian(Data.AsSpan(at), value); return this; }
    public T7StructBuilder U64(int at, ulong value) { BinaryPrimitives.WriteUInt64LittleEndian(Data.AsSpan(at), value); return this; }
    public T7StructBuilder Fill(int at, int size, byte value) { Data.AsSpan(at, size).Fill(value); return this; }

    public void Commit()
    {
        if (_committed)
            throw new InvalidOperationException("read already committed");
        _committed = true;
        _owner.Fill(_slot, new T7DataPiece(Data, _fixups, _owner.Pc, _spans) { Pools = _pools });
    }
}

public readonly struct T7ElementBuilder
{
    private readonly T7StructBuilder _parent;

    internal T7ElementBuilder(T7StructBuilder parent, int index, int sourceStart, int sourceSize, int outputStart, int outputSize)
    {
        _parent = parent;
        Index = index;
        SourceStart = sourceStart;
        SourceSize = sourceSize;
        OutputStart = outputStart;
        OutputSize = outputSize;
    }

    public int Index { get; }
    public int SourceStart { get; }
    public int SourceSize { get; }
    public int OutputStart { get; }
    public int OutputSize { get; }
    public ReadOnlySpan<byte> Source => _parent.Source.Slice(SourceStart, SourceSize);
    public Span<byte> Output => _parent.Data.AsSpan(OutputStart, OutputSize);

    public T7ElementBuilder Move(int from, int to, int size) { _parent.Move(SourceStart + from, OutputStart + to, size); return this; }
    public T7ElementBuilder CarryPointer(int from, int to) { _parent.CarryPointer(SourceStart + from, OutputStart + to); return this; }
    public T7ElementBuilder MapSource(int from, int to, int size) { _parent.MapSource(SourceStart + from, OutputStart + to, size); return this; }
    public T7ElementBuilder Pointer(int at, T7Target target) { _parent.Pointer(OutputStart + at, target); return this; }
}
