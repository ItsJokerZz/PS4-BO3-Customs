using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FFPorter.Core.Common.Native;

namespace FFPorter.Core.T7.Harness;

public sealed class T7Ps4Loader : T7NativeWalk
{
    public const string TaskName = "t7-ps4-walk";

    private const ulong ImageStart = 0x10000;
    private const ulong FakeThreadPointer = 0x10080000;
    private const ulong FakeThreadBlockStart = 0x10000000;
    private const ulong FakeThreadBlockSize = 0x100000;

    private const ulong StackGuardSlot = 0x16008B0;
    private const ulong VarXAsset = 0x57E9618;
    private const ulong VarXAssetHeader = 0x57E9610;
    private const ulong VarXAssetList = 0x57E9620;
    private const ulong XAssetListStatic = 0x57E8460;
    private const ulong VarXStringArray = 0x57E8588;
    private const ulong VarScriptStringList = 0x57E8590;
    private const ulong CurrentBlock = 0x5D4A170;
    private const ulong DeferCount = 0x5D4A174;
    private const ulong DeferList = 0x5D4A180;
    private const ulong BlockCursors = 0x5D4A120;
    private const ulong StreamPos = 0x5D5A188;
    private const ulong ZoneMemoryPointer = 0x5D5A180;
    private const ulong StreamStack = 0x5D5A1A0;
    private const ulong StreamDepthSlot = 0x5D5A190;
    private const ulong AbortFlag = 0x57E7AB8;
    private const ulong NameGetters = 0x15C38C0;

    private const ulong DbReadXFile = 0x7FBFD0;
    private const ulong LoadXString = 0x865E40;
    private const ulong LoadXStringArray = 0x7FFE70;
    private const ulong LoadXAsset = 0x8374A0;
    private const ulong DbAddXAsset = 0x857860;
    private const ulong DbLinkXAssetEntry = 0x857A10;
    private const ulong DbFindXAssetHeader = 0x8591B0;
    private const ulong SlGetStringOfSize = 0x783A50;
    private const ulong ComError = 0xEF35B0;
    private const ulong PltStart = 0x121A6A0;
    private const ulong PltEnd = 0x121D380;

    private JsonElement _segments;
    private ulong _imageEnd;
    private readonly Dictionary<ulong, string> _importNames = [];
    private ulong _lowerTable;

    private T7Ps4Loader(string loaderDirectory, string input, string walkPath, int limit, TextWriter output)
        : base(loaderDirectory, input, walkPath, limit, output)
    {
    }

    protected override string PlatformName => "ps4";
    protected override byte ExpectedPlatform => 2;
    protected override GuestAbi Abi => GuestAbi.SysV;
    protected override uint ExpectedDepthBetweenAssets => 1;

    public static int Run(NativeTaskContext context)
    {
        (string? loader, string? walk, string? input, int limit, string? problem) = ParseArguments(context.Arguments, "--loader");
        if (problem != null)
        {
            context.Error.WriteLine($"t7-ps4-walk: {problem}");
            context.Error.WriteLine("usage: ffport __native t7-ps4-walk --loader <t7_ps4_loader dir> --walk <out.t7walk> [--limit N] -- <zone.ff>");
            return NativeExitCodes.Fail;
        }
        return new T7Ps4Loader(loader!, input!, walk!, limit, context.Out).Execute();
    }

    public static IReadOnlyList<string> ChildArguments(string loaderDirectory, string walkPath, string input, int limit = 0)
    {
        var list = new List<string> { "--loader", loaderDirectory, "--walk", walkPath };
        if (limit > 0)
            list.AddRange(["--limit", limit.ToString(CultureInfo.InvariantCulture)]);
        list.AddRange(["--", input]);
        return list;
    }

    private const ulong OwnArchiveChecksum = 0x12ACE80, OtherArchiveChecksums = 0x12B9060;
    private const int OtherArchiveChecksumCount = 10;

    private static readonly byte[] KnownArchiveChecksum = Convert.FromHexString("458F74AF4E509403CBA0EDC2F3C30645");

    public static byte[] ArchiveChecksum(string loaderDirectory) =>
        ReadImage(loaderDirectory, OwnArchiveChecksum, 16) is { } own && own.AsSpan().ContainsAnyExcept((byte)0) ? own : (byte[])KnownArchiveChecksum.Clone();

    public static List<string> HeaderProblems(T7Header header, string loaderDirectory)
    {
        var problems = new List<string>();
        if (header.ServerFlag != 0)
            problems.Add("the header marks a server fastfile (the client refuses it)");
        if (header.Platform != 2)
            problems.Add($"the header platform is {header.PlatformName}, not ps4");
        if (header.Encrypted != 0)
            problems.Add("the header marks the zone encrypted");
        var accepted = new List<byte[]> { ArchiveChecksum(loaderDirectory) };
        if (ReadImage(loaderDirectory, OtherArchiveChecksums, 16 * OtherArchiveChecksumCount) is { } others)
            accepted.AddRange(others.Chunk(16));
        if (!accepted.Exists(c => c.AsSpan().SequenceEqual(header.ArchiveChecksum)))
            problems.Add($"archive checksum {Convert.ToHexString(header.ArchiveChecksum)} is not one the PS4 executable accepts");
        return problems;
    }

    private static byte[]? ReadImage(string loaderDirectory, ulong address, int count)
    {
        try
        {
            using JsonDocument segments = JsonDocument.Parse(File.ReadAllText(Path.Combine(loaderDirectory, "segments.json")));
            foreach (JsonElement segment in segments.RootElement.EnumerateArray())
            {
                ulong start = segment.GetProperty("start").GetUInt64(), end = segment.GetProperty("end").GetUInt64();
                if (segment.GetProperty("file").ValueKind != JsonValueKind.String || address < start || address + (ulong)count > end)
                    continue;
                using FileStream file = File.OpenRead(Path.Combine(loaderDirectory, segment.GetProperty("file").GetString()!));
                file.Position = (long)(address - start);
                var bytes = new byte[count];
                return file.Read(bytes) == count ? bytes : null;
            }
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
        {
        }
        return null;
    }


    protected override void ReserveImage()
    {
        _segments = JsonDocument.Parse(File.ReadAllText(Path.Combine(ImageSource, "segments.json"))).RootElement;
        _imageEnd = (_segments.EnumerateArray().Max(s => s.GetProperty("end").GetUInt64()) + 0xFFFF) & ~0xFFFFUL;
        Host.ReserveFixed(ImageStart, _imageEnd - ImageStart);
        Host.ReserveFixed(FakeThreadBlockStart, FakeThreadBlockSize);
    }

    protected override void MapImage()
    {
        Host.Alloc(_imageEnd - ImageStart, ImageStart, execute: true);
        foreach (JsonElement segment in _segments.EnumerateArray())
        {
            if (segment.GetProperty("file").ValueKind != JsonValueKind.String)
                continue;
            ulong start = segment.GetProperty("start").GetUInt64();
            byte[] data = File.ReadAllBytes(Path.Combine(ImageSource, segment.GetProperty("file").GetString()!));
            ulong skip = start < ImageStart ? ImageStart - start : 0;
            if ((ulong)data.Length > skip)
                Host.Write(start + skip, data.AsSpan((int)skip));
        }
        Host.Alloc(FakeThreadBlockSize, FakeThreadBlockStart);
        Host.SetQ(FakeThreadPointer, FakeThreadPointer);
        PatchThreadPointerAccess();
        ulong guard = Host.Alloc(64);
        Host.SetQ(guard, 0x2B992DDFA23249D6);
        Host.SetQ(StackGuardSlot, guard);
    }

    private void PatchThreadPointerAccess()
    {
        const ulong codeStart = ImageStart, codeEnd = 0x1520000;
        ReadOnlySpan<byte> code = Host.View(codeStart, (long)(codeEnd - codeStart));
        var sites = new List<ulong>();
        for (int i = 0; i + 9 <= code.Length; i++)
        {
            if (code[i] == 0x64 && (code[i + 1] & 0xF0) == 0x40 && code[i + 2] == 0x8B && (code[i + 3] & 0xC7) == 0x04 && code[i + 4] == 0x25)
                sites.Add(codeStart + (ulong)i);
        }
        foreach (ulong site in sites)
        {
            int displacement = (int)Host.D(site + 5);
            byte[] replacement = [Host.B(site + 1), 0x8B, Host.B(site + 3), 0x25, 0, 0, 0, 0, 0x90];
            BinaryPrimitives.WriteInt32LittleEndian(replacement.AsSpan(4), checked((int)((long)FakeThreadPointer + displacement)));
            Host.Write(site, replacement);
        }
        Notes.Add($"patched {sites.Count} thread-pointer accesses");
    }


    protected override void InstallHooks()
    {
        SetUpImports();
        Host.Hook(DbReadXFile, args => ReadFile(args.A1, (uint)args.A2));
        Host.Hook(LoadXString, args =>
        {
            ulong length = ReadZoneString(args.A1);
            if (CurrentBlockIndex != 8)
                Host.SetQ(StreamPos, Host.Q(StreamPos) + length);
            return length;
        });
        Host.Hook(DbAddXAsset, args => { RegisterHeader(args.A1, (int)args.A2); return 0; });
        Host.Hook(DbLinkXAssetEntry, args =>
        {
            ulong entry = args.A1;
            RegisterHeader(entry + 8, (int)Host.D(entry));
            ulong copy = Allocate(32);
            Host.Copy(copy, entry, 16);
            return copy;
        });
        Host.Hook(DbFindXAssetHeader, args => FindAsset((int)args.A1, args.A2));
        Host.Hook(SlGetStringOfSize, args => ScriptStringId(args.A1));
        Host.Hook(ComError, ErrorHook("Com_Error", 3));

        NativeCallback dummyObject = _ => Allocate(4096);
        Host.Hook(0xBDDE90, dummyObject);
        Host.Hook(0xBDA860, dummyObject);
        Host.Hook(0xBDD3A0, dummyObject);
        Host.Hook(0xF79B10, _ => 1);
        Host.Hook(0xB85350, _ => 0);
        Host.Hook(0xC17AD0, _ => 0);
        Host.Hook(0xBFE580, args => { Host.SetD(args.A1, 0xFFFFFFFF); return 0; });
    }

    private void SetUpImports()
    {
        string names = Path.Combine(ImageSource, "imports.json");
        if (File.Exists(names))
        {
            foreach (JsonElement import in JsonDocument.Parse(File.ReadAllText(names)).RootElement.EnumerateArray())
                _importNames[import.GetProperty("ea").GetUInt64()] = import.GetProperty("name").GetString()!;
        }
        for (ulong stub = PltStart; stub < PltEnd; stub += 0x10)
            Host.Hook(stub, ImportHandler(_importNames.GetValueOrDefault(stub, $"import_{stub:x}")));
    }

    private NativeCallback ImportHandler(string name) => name switch
    {
        "memcpy" or "memmove" => args => { Host.Copy(args.A1, args.A2, args.A3); return args.A1; },
        "memset" => args => { Host.Fill(args.A1, (byte)args.A2, args.A3); return args.A1; },
        "memcmp" => args => (ulong)(long)Host.View(args.A1, args.A3).SequenceCompareTo(Host.View(args.A2, args.A3)),
        "strlen" => args => (ulong)CString(args.A1).Length,
        "strcmp" => args => (ulong)(long)CString(args.A1).SequenceCompareTo(CString(args.A2)),
        "strncmp" => args => (ulong)(long)Limit(CString(args.A1), args.A3).SequenceCompareTo(Limit(CString(args.A2), args.A3)),
        "strcpy" => args => { Host.Write(args.A1, [.. CString(args.A2), 0]); return args.A1; },
        "strchr" => args => Find(args.A1, (byte)args.A2, last: false),
        "strrchr" => args => Find(args.A1, (byte)args.A2, last: true),
        "qsort" => Sort,
        "malloc" or "_Znwm" or "_Znam" => args => Allocate(args.A1),
        "calloc" => args => Allocate(args.A1 * args.A2),
        "free" or "_ZdlPv" or "_ZdaPv" => _ => 0,
        "__stack_chk_fail" => _ => { Host.Fail($"stack check failed during asset {CurrentAsset}"); return 0; },
        "snprintf" or "vsnprintf" or "sprintf" or "vsprintf" => args => { Host.SetB(args.A1, 0); return 0; },
        "printf" or "puts" or "putchar" => _ => 0,
        "_Getptolower" => _ => LowerTable(),
        "__cxa_guard_acquire" => args => Host.B(args.A1) == 0 ? 1UL : 0UL,
        "__cxa_guard_release" => args => { Host.SetB(args.A1, 1); return 0; },
        _ when name.StartsWith("sceKernel", StringComparison.Ordinal) || name.StartsWith("scePthread", StringComparison.Ordinal)
            || name.StartsWith("pthread_", StringComparison.Ordinal) => _ => { Notes.Add($"stubbed {name}"); return 0; },
        _ => _ => { Host.Fail($"Unhandled PS4 import {name} called during asset {CurrentAsset}"); return 0; },
    };

    private static new ReadOnlySpan<byte> Limit(ReadOnlySpan<byte> text, ulong limit) => text[..(int)Math.Min((ulong)text.Length, limit)];

    private ulong Find(ulong address, byte value, bool last)
    {
        ReadOnlySpan<byte> text = CString(address);
        if (value == 0)
            return address + (ulong)text.Length;
        int at = last ? text.LastIndexOf(value) : text.IndexOf(value);
        return at < 0 ? 0 : address + (ulong)at;
    }

    private ulong Sort(NativeArgs args)
    {
        ulong start = args.A1, count = args.A2, width = args.A3, comparator = args.A4;
        if (count < 2)
            return 0;
        byte[] data = Host.Read(start, count * width);
        int[] order = Enumerable.Range(0, (int)count).ToArray();
        ulong scratch = Allocate(2 * width);
        Array.Sort(order, (x, y) =>
        {
            Host.Write(scratch, data.AsSpan(x * (int)width, (int)width));
            Host.Write(scratch + width, data.AsSpan(y * (int)width, (int)width));
            return (int)Host.CallSysV(comparator, scratch, scratch + width);
        });
        var sorted = new byte[data.Length];
        for (int i = 0; i < order.Length; i++)
            data.AsSpan(order[i] * (int)width, (int)width).CopyTo(sorted.AsSpan(i * (int)width));
        Host.Write(start, sorted);
        return 0;
    }

    private ulong LowerTable()
    {
        if (_lowerTable == 0)
        {
            _lowerTable = Allocate(2 * 260) + 2;
            for (int c = 0; c < 256; c++)
                Host.SetW(_lowerTable + (ulong)(2 * c), (ushort)(c is >= 'A' and <= 'Z' ? c + 32 : c));
        }
        return _lowerTable;
    }


    protected override uint CurrentBlockIndex => Host.D(CurrentBlock);
    protected override ulong StreamPosition => Host.Q(StreamPos);
    protected override ulong BlockCursor(int block) => Host.Q(BlockCursors + (ulong)block * 8);
    protected override uint StreamDepth => Host.D(StreamDepthSlot);
    protected override uint DeferredCount => Host.D(DeferCount);
    protected override (ulong Pointer, ulong Size) Deferred(uint index) => (Host.Q(DeferList + index * 16UL), Host.Q(DeferList + index * 16UL + 8));
    protected override ulong NameGetter(int type) => Host.Q(NameGetters + (ulong)type * 8);
    protected override bool Aborted => Host.B(AbortFlag) != 0;

    protected override void InitStreams(ulong zoneMemoryTable)
    {
        Host.SetQ(ZoneMemoryPointer, zoneMemoryTable);
        for (int i = 0; i < Blocks; i++)
            Host.SetQ(BlockCursors + (ulong)i * 8, i == 8 ? ulong.MaxValue : BlockBase[i]);
        Host.SetD(CurrentBlock, 0);
        Host.SetQ(StreamPos, BlockBase[0]);
        Host.SetQ(BlockCursors, 0);
        Host.SetD(StreamDepthSlot, 0);
        Host.SetD(DeferCount, 0);
        Host.SetB(AbortFlag, 0);
        Push(5);
    }

    private void Push(uint block)
    {
        uint depth = Host.D(StreamDepthSlot);
        uint current = Host.D(CurrentBlock);
        ulong pos = Host.Q(StreamPos);
        ulong entry = StreamStack + depth * 16UL;
        Host.SetD(entry + 8, current);
        if (current != block)
        {
            Host.SetD(CurrentBlock, block);
            Host.SetQ(BlockCursors + current * 8UL, pos);
            pos = Host.Q(BlockCursors + block * 8UL);
            Host.SetQ(BlockCursors + block * 8UL, 0);
            Host.SetQ(StreamPos, pos);
        }
        Host.SetQ(entry, pos);
        Host.SetD(StreamDepthSlot, depth + 1);
    }

    private void Pop()
    {
        uint current = Host.D(CurrentBlock);
        uint depth = Host.D(StreamDepthSlot) - 1;
        Host.SetD(StreamDepthSlot, depth);
        ulong entry = StreamStack + depth * 16UL;
        if (current == 0)
            Host.SetQ(StreamPos, Host.Q(entry));
        uint previous = Host.D(entry + 8);
        if (current != previous)
        {
            Host.SetD(CurrentBlock, previous);
            Host.SetQ(BlockCursors + current * 8UL, Host.Q(StreamPos));
            Host.SetQ(StreamPos, Host.Q(BlockCursors + previous * 8UL));
            Host.SetQ(BlockCursors + previous * 8UL, 0);
        }
    }

    private void Align(ulong mask)
    {
        if (Host.D(CurrentBlock) != 8)
            Host.SetQ(StreamPos, (Host.Q(StreamPos) + mask) & ~mask);
    }

    protected override int LoadAssetList()
    {
        ReadFile(XAssetListStatic, 48);
        Host.SetQ(VarXAssetList, XAssetListStatic);
        Push(5);
        Push(5);
        Host.SetQ(VarScriptStringList, XAssetListStatic);
        if (Host.Q(XAssetListStatic + 8) != 0)
        {
            Align(7);
            ulong strings = Host.Q(StreamPos);
            Host.SetQ(XAssetListStatic + 8, strings);
            Host.SetQ(VarXStringArray, strings);
            Host.CallSysV(LoadXStringArray, 1, Host.D(XAssetListStatic));
        }
        Pop();
        Pop();
        if (Host.Q(XAssetListStatic + 0x18) != 0)
        {
            Align(3);
            ulong depends = Host.Q(StreamPos);
            Host.SetQ(XAssetListStatic + 0x18, depends);
            Host.SetQ(VarXStringArray, depends);
            Host.CallSysV(LoadXStringArray, 1, Host.D(XAssetListStatic + 0x10));
        }
        int loaded = 0;
        if (Host.Q(XAssetListStatic + 0x28) != 0)
        {
            Align(7);
            ulong table = Host.Q(StreamPos);
            Host.SetQ(XAssetListStatic + 0x28, table);
            Host.SetQ(VarXAsset, table);
            uint count = Host.D(XAssetListStatic + 0x20);
            ReadFile(table, count * 16UL);
            Host.SetQ(StreamPos, table + count * 16UL);
            loaded = LoadAssets(table, count, entry =>
            {
                Host.SetQ(VarXAsset, entry);
                Host.SetQ(VarXAssetHeader, entry + 8);
                Host.CallSysV(LoadXAsset);
            });
        }
        Pop();
        ReadDeferred();
        return loaded;
    }
}
