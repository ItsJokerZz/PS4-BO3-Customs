using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FFPorter.Core.Common.Native;

namespace FFPorter.Core.T7.Harness;

public abstract class T7NativeWalk
{
    protected const int Blocks = T7Header.BlockCount;
    protected const ulong BlockSlack = 1UL << 20;
    private const ulong HeapSize = 512UL << 20;

    protected readonly string ImageSource;
    protected readonly string Input;
    protected readonly string WalkPath;
    protected readonly int Limit;
    protected readonly TextWriter Output;

    protected NativeHost Host = null!;
    protected byte[] Zone = [];
    protected T7Header Header = null!;
    private T7WalkWriter _walk = null!;
    private StallWatchdog? _watchdog;

    protected readonly ulong[] BlockBase = new ulong[Blocks];
    protected readonly ulong[] BlockSize = new ulong[Blocks];
    private readonly long[] _high = new long[Blocks];
    protected long FilePos;
    protected int CurrentAsset = -1;
    private long _reads;
    private long _registered;
    private readonly List<(long FileOffset, long Size, ulong Destination)> _pending = [];
    private readonly List<(long FileOffset, long Size, ulong Destination)> _assetReads = [];
    private readonly HashSet<long> _pointerFields = [];
    private readonly Dictionary<string, uint> _scriptStrings = new(StringComparer.Ordinal);
    private readonly HashSet<(int Type, string Name)> _lookups = [];
    private readonly List<int> _deferOwners = [];
    protected readonly HashSet<string> Notes = [];
    private ulong _heap;
    private ulong _heapUsed;
    private ulong _dummyAsset;

    protected T7NativeWalk(string imageSource, string input, string walkPath, int limit, TextWriter output)
    {
        ImageSource = imageSource;
        Input = input;
        WalkPath = walkPath;
        Limit = limit;
        Output = output;
    }

    protected abstract string PlatformName { get; }
    protected abstract byte ExpectedPlatform { get; }
    protected abstract GuestAbi Abi { get; }

    protected abstract void ReserveImage();

    protected abstract void MapImage();

    protected abstract void InstallHooks();

    protected abstract void InitStreams(ulong zoneMemoryTable);

    protected abstract int LoadAssetList();

    protected abstract uint CurrentBlockIndex { get; }
    protected abstract ulong StreamPosition { get; }
    protected abstract ulong BlockCursor(int block);
    protected abstract uint StreamDepth { get; }
    protected abstract uint DeferredCount { get; }
    protected abstract (ulong Pointer, ulong Size) Deferred(uint index);
    protected abstract ulong NameGetter(int type);
    protected abstract uint ExpectedDepthBetweenAssets { get; }
    protected abstract bool Aborted { get; }

    protected ulong Call(ulong function, ulong a1 = 0, ulong a2 = 0, ulong a3 = 0, ulong a4 = 0) =>
        Abi == GuestAbi.SysV ? Host.CallSysV(function, a1, a2, a3, a4) : Host.CallWin64(function, a1, a2, a3, a4);

    public int Execute()
    {
        var clock = Stopwatch.StartNew();
        Host = NativeHost.Create(Abi);
        Host.RegisterThreadStack();
        Host.DescribeAddress = DescribeAddress;
        Host.InvalidSpanMessage = (address, size) => $"Invalid memory span {NativeHost.Hex(address)}+{NativeHost.Hex(size)} during asset {CurrentAsset} (file 0x{FilePos:x})";
        ReserveImage();

        T7FastFile.Decoded decoded = T7FastFile.Load(Input);
        Zone = decoded.Zone;
        Header = decoded.Header;
        if (Header.Platform != ExpectedPlatform && Environment.GetEnvironmentVariable("T7_WALK_ANY_PLATFORM") != "1")
            Host.Fail($"t7 {PlatformName} walk reads {PlatformName} zones; {Input} is {Header.PlatformName}");

        _walk = new T7WalkWriter(WalkPath);
        _walk.Header(PlatformName, Zone.Length, Header.BlockSizes);
        Host.BeforeExit = _walk.Flush;
        Host.FaultStackQwords = 48;
        Host.Log = JsonlLog.Open(WalkPath + ".log.jsonl");
        Host.Log.AutoFlush = true;
        Host.WriteLog(new JsonMap { ["input"] = Input, ["zone"] = Header.ZoneName, ["zone_bytes"] = (long)Zone.Length, ["platform"] = PlatformName });
        _watchdog = new StallWatchdog(TimeSpan.FromSeconds(20), text =>
        {
            Console.Error.WriteLine($"t7 {PlatformName} walk: asset {CurrentAsset}: {text}");
            Host.WriteLog(new JsonMap { ["error"] = $"asset {CurrentAsset} (file 0x{FilePos:x}): {text}" });
        });

        MapImage();
        _heap = Host.Alloc(HeapSize);
        _dummyAsset = Host.Alloc(1 << 16);
        ulong table = AllocateBlocks();
        InitStreams(table);
        InstallHooks();

        int loaded = LoadAssetList();
        CheckPointers(_pending);
        _pending.Clear();
        CheckPointers(_preambleReads);
        SampleCursors();
        if (FilePos != Zone.Length)
            Host.Fail($"Walk consumed {FilePos} of {Zone.Length} zone bytes");
        _walk.Final(FilePos, _high);
        _walk.Dispose();
        _watchdog.Dispose();

        var summary = new
        {
            passed = true,
            zone = Header.ZoneName,
            platform = PlatformName,
            zone_bytes = Zone.Length,
            assets = loaded,
            reads = _reads,
            registered = _registered,
            pointer_fields = _pointerFields.Count,
            block_sizes = Header.BlockSizes,
            block_high_water = _high,
            by_name_lookups = _lookups.Count,
            notes = Notes.Order().ToArray(),
            seconds = Math.Round(clock.Elapsed.TotalSeconds, 1),
            walk = WalkPath,
        };
        Output.WriteLine(JsonSerializer.Serialize(summary));
        return NativeExitCodes.Ok;
    }


    private ulong AllocateBlocks()
    {
        ulong table = Host.Alloc(16 * Blocks);
        bool layout = Environment.GetEnvironmentVariable("T7_WALK_LAYOUT") == "1";
        for (int i = 0; i < Blocks; i++)
        {
            BlockSize[i] = layout && i is not (7 or 8) ? Math.Max(Header.BlockSize(i), (ulong)Zone.Length + (64UL << 20)) : Header.BlockSize(i);
            BlockBase[i] = i switch
            {
                7 => 0x88888888UL,
                8 => 0,
                _ => Host.AllocGuarded(Math.Max(BlockSize[i] + BlockSlack, BlockSlack)),
            };
            Host.SetQ(table + (ulong)i * 16, BlockBase[i]);
            Host.SetQ(table + (ulong)i * 16 + 8, BlockSize[i]);
        }
        return table;
    }

    private string? DescribeAddress(ulong address)
    {
        T7BlockAddress at = Locate(address);
        if (at.Block < 0)
            return null;
        for (int i = _assetWrites.Count - 1; i >= 0; i--)
        {
            (ulong destination, ulong size, long fileOffset) = _assetWrites[i];
            if (address >= destination && address < destination + size)
                return $"block {at.Block} +0x{at.Offset:x}, zone file 0x{fileOffset + (long)(address - destination):x}";
        }
        return $"block {at.Block} +0x{at.Offset:x}";
    }

    protected T7BlockAddress Locate(ulong address)
    {
        for (int i = 0; i < Blocks; i++)
        {
            if (BlockBase[i] > 0x100000000 && address >= BlockBase[i] && address < BlockBase[i] + BlockSize[i] + BlockSlack)
                return new T7BlockAddress(i, (long)(address - BlockBase[i]));
        }
        return T7BlockAddress.None;
    }

    private void MeasureWrite(ulong destination, ulong size)
    {
        T7BlockAddress at = Locate(destination);
        if (at.Block >= 0)
            _high[at.Block] = Math.Max(_high[at.Block], at.Offset + (long)size);
    }

    private void SampleCursors()
    {
        uint current = CurrentBlockIndex;
        for (int i = 0; i < Blocks; i++)
        {
            if (BlockBase[i] <= 0x100000000)
                continue;
            ulong cursor = i == current ? StreamPosition : BlockCursor(i);
            if (cursor >= BlockBase[i] && cursor <= BlockBase[i] + BlockSize[i] + BlockSlack)
                _high[i] = Math.Max(_high[i], (long)(cursor - BlockBase[i]));
        }
    }


    protected int LoadAssets(ulong table, uint count, Action<ulong> loadOne)
    {
        CheckPointers(_pending);
        _pending.Clear();
        int total = Limit > 0 ? (int)Math.Min(Limit, count) : (int)count;
        int loaded = 0;
        for (int i = 0; i < total; i++)
        {
            ulong entry = table + (ulong)i * 16;
            int type = (int)Host.D(entry);
            CurrentAsset = i;
            _assetReads.Clear();
            _assetWrites.Clear();
            _assetHeaders.Clear();
            _assetRegistrations.Clear();
            _openCells.Clear();
            _virtualCoverage.Clear();
            _openInlineFields.Clear();
            _openInlineOrder.Clear();
            ulong virtualBefore = StreamPosition;
            _scannedTo = (virtualBefore + 7) & ~7UL;
            uint deferBefore = DeferredCount;
            _walk.BeginAsset(i, type, FilePos);
            Host.WriteLog(new JsonMap { ["asset"] = (long)i, ["type"] = (long)type, ["type_name"] = T7AssetTypes.Name(type), ["file_pos"] = FilePos });
            loadOne(entry);
            if (Aborted)
                Host.Fail($"loader aborted during asset {i}");
            for (uint d = deferBefore; d < DeferredCount; d++)
                _deferOwners.Add(i);
            CheckPointers(_assetReads, final: true);
            _pending.Clear();
            ScanAliasSlots();
            SampleCursors();
            _walk.EndAsset(i, FilePos);
            if (StreamDepth != ExpectedDepthBetweenAssets || CurrentBlockIndex != 5)
                Host.Fail($"unbalanced stream after asset {i} ({T7AssetTypes.Name(type)}): depth {StreamDepth} block {CurrentBlockIndex}");
            loaded++;
        }
        CurrentAsset = -1;
        return loaded;
    }

    private readonly List<(ulong Destination, ulong Size, long FileOffset)> _assetWrites = [];

    protected void NotePackedPointer(ulong fieldAddress, bool alias)
    {
        ulong stored = Host.Q(fieldAddress);
        if (stored == 0 || stored >= 0xFFFFFFFFFFFFFFFE)
            return;
        for (int i = _assetWrites.Count - 1; i >= 0; i--)
        {
            (ulong destination, ulong size, long fileOffset) = _assetWrites[i];
            if (fieldAddress >= destination && fieldAddress + 8 <= destination + size)
            {
                long field = fileOffset + (long)(fieldAddress - destination);
                if (BinaryPrimitives.ReadUInt64LittleEndian(Zone.AsSpan((int)field, 8)) != stored)
                    return;
                ulong packed = stored - 1;
                if (_pointerFields.Add(field))
                    _walk.Pointer(field, alias ? T7PointerKind.PackedAlias : T7PointerKind.Packed, new T7BlockAddress((int)(packed >> 60), (long)(packed & 0x0FFFFFFFFFFFFFFF)));
                return;
            }
        }
    }
    private readonly HashSet<ulong> _assetHeaders = [];

    private readonly List<ulong> _assetRegistrations = [];
    private readonly List<ulong> _openCells = [];
    private readonly List<(ulong Start, ulong End)> _virtualCoverage = [];
    private ulong _scannedTo;

    private ulong VirtualCursor => CurrentBlockIndex == 5 ? StreamPosition : BlockCursor(5);

    private void NoteVirtualWrite(ulong destination, ulong size)
    {
        if (BlockBase[5] <= 0x100000000 || destination < BlockBase[5] || destination >= BlockBase[5] + BlockSize[5] + BlockSlack)
            return;
        var range = (destination, destination + size);
        if (_virtualCoverage.Count == 0 || _virtualCoverage[^1].Start <= destination)
        {
            _virtualCoverage.Add(range);
            return;
        }
        int at = _virtualCoverage.BinarySearch(range, Comparer<(ulong Start, ulong End)>.Create((a, b) => a.Start.CompareTo(b.Start)));
        _virtualCoverage.Insert(at < 0 ? ~at : at, range);
    }

    private bool Covered(ulong cell)
    {
        int lo = 0, hi = _virtualCoverage.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (_virtualCoverage[mid].Start < cell + 8)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        for (int i = found; i >= 0 && i > found - 64; i--)
        {
            if (_virtualCoverage[i].End > cell)
                return true;
        }
        return false;
    }

    private void ScanAliasSlots()
    {
        if (BlockBase[5] <= 0x100000000 || CurrentAsset < 0)
            return;
        ulong cursor = VirtualCursor;
        for (; _scannedTo + 8 <= cursor; _scannedTo += 8)
        {
            if (!Covered(_scannedTo))
                _openCells.Add(_scannedTo);
        }
        for (int i = _openCells.Count - 1; i >= 0; i--)
        {
            ulong cell = _openCells[i];
            ulong value = Host.Q(cell);
            if (value == 0 && !Covered(cell))
                continue;
            _openCells.RemoveAt(i);
            if (value == 0 || !_assetHeaders.Contains(value))
                continue;
            int ordinal = _assetRegistrations.LastIndexOf(value);
            _walk.AliasSlot(CurrentAsset, Locate(cell), Locate(value), ordinal);
        }
    }

    protected void ReadDeferred()
    {
        uint count = DeferredCount;
        for (uint d = 0; d < count; d++)
        {
            (ulong pointer, ulong size) = Deferred(d);
            int owner = d < _deferOwners.Count ? _deferOwners[(int)d] : -1;
            long at = FilePos;
            CopyZone(pointer, size);
            _walk.Read(T7WalkKind.DeferRead, owner, at, (long)size, Locate(pointer));
        }
    }


    protected ulong ReadFile(ulong destination, ulong size)
    {
        long at = FilePos;
        CopyZone(destination, size);
        _reads++;
        _watchdog?.Progress();
        _walk.Read(T7WalkKind.Read, CurrentAsset, at, (long)size, Locate(destination));
        if (size >= 8)
        {
            _pending.Add((at, (long)size, destination));
            _assetReads.Add((at, (long)size, destination));
            if (CurrentAsset < 0)
                _preambleReads.Add((at, (long)size, destination));
        }
        return 0;
    }

    private readonly List<(long FileOffset, long Size, ulong Destination)> _preambleReads = [];

    protected void CopyZone(ulong destination, ulong size)
    {
        if ((ulong)FilePos + size > (ulong)Zone.Length)
            Host.Fail($"zone over-read at 0x{FilePos:x} (+0x{size:x}) during asset {CurrentAsset}");
        if (size != 0)
        {
            if (_openInlineOrder.Exists(o => o.Address + 8 > destination && o.Address < destination + size))
            {
                CheckOpenInlineFields(0, final: true);
                _openInlineOrder.RemoveAll(o => o.Address + 8 > destination && o.Address < destination + size && _openInlineFields.Remove(o));
            }
            Host.Write(destination, Zone.AsSpan((int)FilePos, (int)size));
            MeasureWrite(destination, size);
            _assetWrites.Add((destination, size, FilePos));
            NoteVirtualWrite(destination, size);
        }
        FilePos += (long)size;
    }

    protected ulong ReadZoneString(ulong destination)
    {
        int end = Array.IndexOf(Zone, (byte)0, (int)FilePos);
        if (end < 0)
            Host.Fail($"unterminated zone string at 0x{FilePos:x}");
        long at = FilePos;
        ulong length = (ulong)(end - FilePos + 1);
        CopyZone(destination, length);
        _reads++;
        _walk.Read(T7WalkKind.String, CurrentAsset, at, (long)length, Locate(destination));
        return length;
    }


    protected void RegisterHeader(ulong slot, int type)
    {
        ulong header = Host.Q(slot);
        string? name = null;
        if (type >= 0 && type < T7AssetTypes.Count)
        {
            ulong getter = NameGetter(type);
            if (getter != 0)
            {
                ulong pointer = Call(getter, slot);
                if (pointer != 0 && Host.TryRegion(pointer, out _))
                    name = TryText(pointer);
            }
        }
        _registered++;
        ScanAliasSlots();
        if (header != 0)
            _assetHeaders.Add(header);
        CheckPointers(_pending, incoming: header);
        _pending.Clear();
        _walk.Register(CurrentAsset, type, name, Locate(header), FilePos);
        if (CurrentAsset >= 0)
            _assetRegistrations.Add(header);
    }

    protected ulong FindAsset(int type, ulong namePointer)
    {
        _lookups.Add((type, TryText(namePointer)));
        return _dummyAsset;
    }

    protected uint ScriptStringId(ulong text)
    {
        string value = TryText(text);
        if (!_scriptStrings.TryGetValue(value, out uint id))
            _scriptStrings[value] = id = (uint)_scriptStrings.Count + 1;
        return id;
    }


    private readonly HashSet<(long Field, ulong Address)> _openInlineFields = [];
    private readonly List<(long Field, ulong Address)> _openInlineOrder = [];

    private bool SettleInline(long field, ulong stored, ulong now, ulong incoming, bool final)
    {
        int ordinal = -1;
        if (CurrentAsset >= 0 && incoming != 0 && now == incoming)
            ordinal = _assetRegistrations.Count;
        else if (CurrentAsset >= 0 && _assetHeaders.Contains(now))
        {
            if (!final)
                return false;
            ordinal = _assetRegistrations.LastIndexOf(now);
        }
        T7BlockAddress target = Locate(now);
        if (target.Block < 0 || !_pointerFields.Add(field))
            return true;
        _walk.Pointer(field, stored == ulong.MaxValue ? T7PointerKind.Inline : T7PointerKind.InlineAlias, target);
        if (ordinal >= 0)
            _walk.InlineRegistration(CurrentAsset, field, ordinal);
        return true;
    }

    private void CheckOpenInlineFields(ulong incoming, bool final)
    {
        for (int i = 0; i < _openInlineOrder.Count;)
        {
            (long field, ulong address) = _openInlineOrder[i];
            ulong stored = BinaryPrimitives.ReadUInt64LittleEndian(Zone.AsSpan((int)field, 8));
            ulong now = Host.TryRegion(address, out _) ? Host.Q(address) : stored;
            if (now == stored || !SettleInline(field, stored, now, incoming, final))
            {
                i++;
                continue;
            }
            _openInlineOrder.RemoveAt(i);
            _openInlineFields.Remove((field, address));
        }
    }

    private void CheckPointers(List<(long FileOffset, long Size, ulong Destination)> reads, ulong incoming = 0, bool final = false)
    {
        CheckOpenInlineFields(incoming, final);
        foreach ((long fileOffset, long size, ulong destination) in reads)
        {
            if (!Host.TryRegion(destination, out MemoryRange range) || destination + (ulong)size > range.End)
                continue;
            for (long k = 0; k + 8 <= size; k += 8)
            {
                long field = fileOffset + k;
                ulong stored = BinaryPrimitives.ReadUInt64LittleEndian(Zone.AsSpan((int)field, 8));
                if (stored == 0 || _pointerFields.Contains(field))
                    continue;
                ulong now = Host.Q(destination + (ulong)k);
                if (stored >= 0xFFFFFFFFFFFFFFFE)
                {
                    if ((now == stored || !SettleInline(field, stored, now, incoming, final)) && CurrentAsset >= 0
                        && _openInlineFields.Add((field, destination + (ulong)k)))
                        _openInlineOrder.Add((field, destination + (ulong)k));
                    continue;
                }
                if (now == stored)
                    continue;
                ulong packed = stored - 1;
                int block = (int)(packed >> 60);
                ulong offset = packed & 0x0FFFFFFFFFFFFFFF;
                if (block >= Blocks || block is 7 or 8 || BlockBase[block] <= 0x100000000 || offset > BlockSize[block] + BlockSlack)
                    continue;
                ulong expected = BlockBase[block] + offset;
                if (now == expected)
                {
                    _pointerFields.Add(field);
                    _walk.Pointer(field, T7PointerKind.Packed, new T7BlockAddress(block, (long)offset));
                }
                else if (now > 0x100000000 && Host.TryRegion(now, out _) && Host.TryRegion(expected, out MemoryRange slotRange) && expected + 8 <= slotRange.End && Host.Q(expected) == now)
                {
                    _pointerFields.Add(field);
                    _walk.Pointer(field, T7PointerKind.PackedAlias, new T7BlockAddress(block, (long)offset));
                }
            }
        }
    }


    protected ulong Allocate(ulong size)
    {
        ulong aligned = (_heapUsed + 15) & ~15UL;
        if (aligned + size > HeapSize)
            Host.Fail("harness heap exhausted");
        _heapUsed = aligned + size;
        Host.Fill(_heap + aligned, 0, size);
        return _heap + aligned;
    }

    protected string TryText(ulong address)
    {
        if (!Host.TryRegion(address, out MemoryRange range))
            return $"0x{address:x}";
        ReadOnlySpan<byte> view = Host.View(address, (long)Math.Min(range.End - address, 1024UL));
        int end = view.IndexOf((byte)0);
        return end < 0 ? $"0x{address:x}" : Encoding.UTF8.GetString(view[..end]);
    }

    protected ReadOnlySpan<byte> CString(ulong address)
    {
        if (!Host.TryRegion(address, out MemoryRange range))
            Host.Fail($"invalid string pointer 0x{address:x} during asset {CurrentAsset}");
        ReadOnlySpan<byte> view = Host.View(address, (long)Math.Min(range.End - address, 1UL << 20));
        int end = view.IndexOf((byte)0);
        if (end < 0)
            Host.Fail($"unterminated string at 0x{address:x}");
        return view[..end];
    }

    protected NativeCallback ErrorHook(string label, int formatArgument) => args =>
    {
        ulong format = args[formatArgument];
        Host.Fail($"{label} during asset {CurrentAsset}: {TryText(format).TrimEnd()} [0x{args[formatArgument + 1]:x}, 0x{args[formatArgument + 2]:x}]");
        return 0;
    };

    protected static (string? Image, string? Walk, string? Input, int Limit, string? Problem) ParseArguments(IReadOnlyList<string> a, string imageOption)
    {
        string? image = null, walk = null, input = null;
        int limit = 0;
        for (int i = 0; i < a.Count; i++)
        {
            switch (a[i])
            {
                case var option when option == imageOption: image = a[++i]; break;
                case "--walk": walk = a[++i]; break;
                case "--limit": limit = int.Parse(a[++i], CultureInfo.InvariantCulture); break;
                case "--": input = a[++i]; break;
                default: return (null, null, null, 0, $"unknown argument {a[i]}");
            }
        }
        return image == null || walk == null || input == null ? (null, null, null, 0, "missing arguments") : (image, walk, input, limit, null);
    }
}
