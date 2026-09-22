using System.Buffers.Binary;
using System.Globalization;
using FFPorter.Core.Common.Native;

namespace FFPorter.Core.T7.Harness;

public sealed class T7PcLoader : T7NativeWalk
{
    public const string TaskName = "t7-pc-walk";

    private const ulong DumpBase = 0x7FF614870000;
    private const ulong FallbackBase = 0x140000000;

    private const ulong CurrentBlock = 0x7FF61E187820;
    private const ulong DeferCount = 0x7FF61E187824;
    private const ulong ZoneMemoryPointer = 0x7FF61E187828;
    private const ulong DeferList = 0x7FF61E187830;
    private const ulong BlockCursors = 0x7FF61E1877D0;
    private const ulong StreamPos = 0x7FF61E197838;
    private const ulong StreamDepthSlot = 0x7FF61E197834;
    private const ulong VarXAsset = 0x7FF61DBFF840;
    private const ulong VarXAssetList = 0x7FF61DBFDE68;
    private const ulong XAssetListStatic = 0x7FF61DBFD378;
    private const ulong VarScriptStringList = 0x7FF61DBFF7E8;
    private const ulong VarXStringArray = 0x7FF61DBFF738;
    private const ulong AbortFlag = 0x7FF61DBFD4E0;
    private const ulong NameGetters = 0x7FF617B19720;

    private const ulong DbReadXFile = 0x7FF615C609F0;
    private const ulong DbReadXFileString = 0x7FF615C60B40;
    private const ulong DbAddXAssetHeader = 0x7FF615C8F4E0;
    private const ulong DbInitStreams = 0x7FF615C963E0;
    private const ulong DbPushStreamPos = 0x7FF615C965A0;
    private const ulong DbPopStreamPos = 0x7FF615C96520;
    private const ulong DbAllocStreamPos = 0x7FF615C963B0;
    private const ulong DbConvertOffsetToPointer = 0x7FF615C96670;
    private const ulong DbConvertOffsetToAlias = 0x7FF615C96640;
    private const ulong DbFindXAssetHeader = 0x7FF615C90EF0;
    private const ulong LoadStream = 0x7FF615C966F0;
    private const ulong LoadScriptStringList = 0x7FF615C76380;
    private const ulong LoadXStringArray = 0x7FF615C7FEA0;
    private const ulong LoadXAsset = 0x7FF615C7E220;
    private const ulong ComError = 0x7FF61695C0B0;
    private const ulong SlGetStringOfSize = 0x7FF615B47B40;
    private const ulong SlAddRefToString = 0x7FF615B48810;
    private const ulong DbMarkAssetUsed = 0x7FF615C93BC0;
    private const ulong StreamWrapperRegister = 0x7FF61657B290;

    private static readonly ulong[] RuntimeNoOps =
    [
        0x7FF61658B440,
        0x7FF6164EFBE0, 0x7FF6164EFC80, 0x7FF6164EFDC0,
        0x7FF61651AB50,
        0x7FF616538520,
        0x7FF616538C10,
        0x7FF616537DC0, 0x7FF616537FB0, 0x7FF616538000, 0x7FF616538060, 0x7FF6165380C0,
        0x7FF616538120, 0x7FF616538180, 0x7FF6165381E0, 0x7FF616538240, 0x7FF6165383F0,
        0x7FF616A863C0, 0x7FF616A86440, 0x7FF616A86480,
        0x7FF61696C570,
    ];

    private ulong _imageSize;
    private ulong _base = DumpBase;

    private ulong V(ulong dumpAddress) => dumpAddress - DumpBase + _base;

    private T7PcLoader(string image, string input, string walkPath, int limit, TextWriter output)
        : base(image, input, walkPath, limit, output)
    {
    }

    protected override string PlatformName => "pc";
    protected override byte ExpectedPlatform => 0;
    protected override GuestAbi Abi => GuestAbi.Win64;
    protected override uint ExpectedDepthBetweenAssets => 1;

    public static int Run(NativeTaskContext context)
    {
        (string? image, string? walk, string? input, int limit, string? problem) = ParseArguments(context.Arguments, "--image");
        if (problem != null)
        {
            context.Error.WriteLine($"t7-pc-walk: {problem}");
            context.Error.WriteLine("usage: ffport __native t7-pc-walk --image <BlackOps3 dump.exe> --walk <out.t7walk> [--limit N] -- <zone.ff>");
            return NativeExitCodes.Fail;
        }
        return new T7PcLoader(image!, input!, walk!, limit, context.Out).Execute();
    }

    public static IReadOnlyList<string> ChildArguments(string image, string walkPath, string input, int limit = 0)
    {
        var list = new List<string> { "--image", image, "--walk", walkPath };
        if (limit > 0)
            list.AddRange(["--limit", limit.ToString(CultureInfo.InvariantCulture)]);
        list.AddRange(["--", input]);
        return list;
    }


    protected override void ReserveImage()
    {
        using FileStream file = File.OpenRead(ImageSource);
        var head = new byte[0x1000];
        file.ReadExactly(head);
        int pe = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(0x3C));
        _imageSize = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(pe + 24 + 56));
        ulong recordedBase = BinaryPrimitives.ReadUInt64LittleEndian(head.AsSpan(pe + 24 + 24));
        if (recordedBase != DumpBase)
            throw new InvalidDataException($"{ImageSource} was dumped at 0x{recordedBase:x}; this walk expects the 0x{DumpBase:x} dump");
        ulong span = (_imageSize + 0xFFFF) & ~0xFFFFUL;
        if (Environment.GetEnvironmentVariable("T7_PC_FORCE_REBASE") == "1" || !Host.TryReserveFixed(DumpBase, span))
        {
            _base = FallbackBase;
            Host.ReserveFixed(FallbackBase, span);
            Notes.Add($"image rebased to 0x{FallbackBase:x}");
        }
    }

    protected override void MapImage()
    {
        Host.Alloc((_imageSize + 0xFFFF) & ~0xFFFFUL, _base, execute: true);
        using FileStream file = File.OpenRead(ImageSource);
        var head = new byte[0x1000];
        file.ReadExactly(head);
        Host.Write(_base, head);
        int pe = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(0x3C));
        int sections = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(pe + 6));
        int optional = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(pe + 20));
        int table = pe + 24 + optional;
        var dataSections = new List<(ulong Start, ulong Size)>();
        for (int s = 0; s < sections; s++)
        {
            ReadOnlySpan<byte> section = head.AsSpan(table + 40 * s, 40);
            uint virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(section[8..]);
            uint virtualAddress = BinaryPrimitives.ReadUInt32LittleEndian(section[12..]);
            uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(section[16..]);
            uint rawPointer = BinaryPrimitives.ReadUInt32LittleEndian(section[20..]);
            uint characteristics = BinaryPrimitives.ReadUInt32LittleEndian(section[36..]);
            long length = Math.Min(Math.Min(rawSize, Math.Max(rawSize, virtualSize)), file.Length - rawPointer);
            file.Position = rawPointer;
            Host.Load(_base + virtualAddress, file, length);
            if ((characteristics & 0x20000000) == 0)
                dataSections.Add((_base + virtualAddress, (ulong)length));
        }
        if (_base != DumpBase)
            Rebase(head, pe, dataSections);
    }

    private unsafe void Rebase(byte[] head, int pe, List<(ulong Start, ulong Size)> dataSections)
    {
        ulong delta = _base - DumpBase;
        ulong oldEnd = DumpBase + _imageSize;
        uint relocRva = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(pe + 24 + 152));
        uint relocSize = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(pe + 24 + 156));
        int applied = 0;
        for (ulong block = _base + relocRva; block < _base + relocRva + relocSize;)
        {
            uint page = Host.D(block);
            uint size = Host.D(block + 4);
            if (size < 8)
                break;
            for (ulong entry = block + 8; entry < block + size; entry += 2)
            {
                ushort value = Host.W(entry);
                if (value >> 12 != 10)
                    continue;
                ulong site = _base + page + (ulong)(value & 0xFFF);
                ulong target = Host.Q(site);
                if (target >= DumpBase && target < oldEnd)
                {
                    Host.SetQ(site, target + delta);
                    applied++;
                }
            }
            block += size;
        }
        int scanned = 0;
        foreach ((ulong start, ulong length) in dataSections)
        {
            byte* at = (byte*)start;
            for (ulong i = 0; i + 8 <= length; i += 8)
            {
                ulong value = *(ulong*)(at + i);
                if (value >= DumpBase && value < oldEnd)
                {
                    *(ulong*)(at + i) = value + delta;
                    scanned++;
                }
            }
        }
        Notes.Add($"rebased {applied} relocations and {scanned} data pointers");
    }


    protected override void InstallHooks()
    {
        Host.Hook(V(DbReadXFile), args => ReadFile(args.A1, (uint)args.A2));
        Host.Hook(V(DbReadXFileString), args => ReadZoneString(args.A1));
        Host.Hook(V(DbAddXAssetHeader), args =>
        {
            ulong slot = Allocate(8);
            Host.SetQ(slot, args.A2);
            RegisterHeader(slot, (int)args.A1);
            return args.A2;
        });
        Host.Hook(V(ComError), ErrorHook("Com_Error", 3));
        Host.Hook(V(DbConvertOffsetToPointer), args =>
        {
            NotePackedPointer(args.A1, alias: false);
            ulong packed = Host.Q(args.A1) - 1;
            ulong table = Host.Q(V(ZoneMemoryPointer));
            Host.SetQ(args.A1, Host.Q(table + 16 * (packed >> 60)) + (packed & 0x0FFFFFFFFFFFFFFF));
            return table;
        });
        Host.Hook(V(DbConvertOffsetToAlias), args =>
        {
            NotePackedPointer(args.A1, alias: true);
            ulong packed = Host.Q(args.A1) - 1;
            ulong table = Host.Q(V(ZoneMemoryPointer));
            ulong value = Host.Q(Host.Q(table + 16 * (packed >> 60)) + (packed & 0x0FFFFFFFFFFFFFFF));
            Host.SetQ(args.A1, value);
            return value;
        });
        Host.Hook(V(DbFindXAssetHeader), args => FindAsset((int)args.A1, args.A2));
        Host.Hook(V(SlGetStringOfSize), args => ScriptStringId(args.A1));
        Host.Hook(V(SlAddRefToString), _ => 0);
        Host.Hook(V(DbMarkAssetUsed), _ => 0);
        foreach (ulong address in RuntimeNoOps)
            Host.Hook(V(address), _ => 0);
        Host.Hook(V(StreamWrapperRegister), args => { Host.SetD(args.A1, 0xFFFFFFFF); return 0; });
    }


    protected override uint CurrentBlockIndex => Host.D(V(CurrentBlock));
    protected override ulong StreamPosition => Host.Q(V(StreamPos));
    protected override ulong BlockCursor(int block) => Host.Q(V(BlockCursors) + (ulong)block * 8);
    protected override uint StreamDepth => Host.D(V(StreamDepthSlot));
    protected override uint DeferredCount => Host.D(V(DeferCount));
    protected override (ulong Pointer, ulong Size) Deferred(uint index) => (Host.Q(V(DeferList) + index * 16UL), Host.Q(V(DeferList) + index * 16UL + 8));
    protected override ulong NameGetter(int type) => Host.Q(V(NameGetters) + (ulong)type * 8);
    protected override bool Aborted => Host.B(V(AbortFlag)) != 0;

    protected override void InitStreams(ulong zoneMemoryTable)
    {
        Host.CallWin64(V(DbInitStreams), zoneMemoryTable);
        Host.SetB(V(AbortFlag), 0);
    }

    protected override int LoadAssetList()
    {
        ulong list = V(XAssetListStatic);
        Host.CallWin64(V(DbPushStreamPos), 5);
        Host.SetQ(V(VarXAssetList), list);
        ReadFile(list, 48);
        Host.CallWin64(V(DbPushStreamPos), 5);
        Host.SetQ(V(VarScriptStringList), list);
        Host.CallWin64(V(LoadScriptStringList), 0);
        Host.CallWin64(V(DbPopStreamPos));
        if (Host.Q(list + 0x18) != 0)
        {
            ulong depends = Host.CallWin64(V(DbAllocStreamPos), 3);
            Host.SetQ(list + 0x18, depends);
            Host.SetQ(V(VarXStringArray), depends);
            Host.CallWin64(V(LoadXStringArray), 1, Host.D(list + 0x10));
        }
        int loaded = 0;
        if (Host.Q(list + 0x28) != 0)
        {
            ulong table = Host.CallWin64(V(DbAllocStreamPos), 7);
            Host.SetQ(list + 0x28, table);
            Host.SetQ(V(VarXAsset), table);
            uint count = Host.D(list + 0x20);
            Host.CallWin64(V(LoadStream), 1, table, count * 16UL);
            loaded = LoadAssets(table, count, entry =>
            {
                Host.SetQ(V(VarXAsset), entry);
                Host.CallWin64(V(LoadXAsset), 0);
            });
        }
        Host.CallWin64(V(DbPopStreamPos));
        ReadDeferred();
        return loaded;
    }
}
