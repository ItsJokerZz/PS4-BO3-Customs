using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using FFPorter.Core.Common.Python;

namespace FFPorter.Core.Common.Native;

public enum GuestAbi
{
    SysV,
    Win64,
}

public readonly record struct NativeArgs(ulong A1, ulong A2, ulong A3, ulong A4, ulong A5, ulong A6)
{
    public ulong this[int index] => index switch
    {
        0 => A1,
        1 => A2,
        2 => A3,
        3 => A4,
        4 => A5,
        5 => A6,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };
}

public delegate ulong NativeCallback(NativeArgs args);

public sealed class NativeHostException(string message) : Exception(message);

public sealed unsafe class NativeHost
{
    internal static ReadOnlySpan<uint> FaultCodes => [0xC0000005, 0xC000001D, 0xC0000409, 0x80000003, 0xC0000094, 0xC0000095, 0xC0000096];

    [StructLayout(LayoutKind.Sequential)]
    private struct Control
    {
        public ulong HostThread;
        public ulong GuestMode;
        public nint FaultHandler;
    }

    private static readonly Lock CreateLock = new();
    private static NativeHost? s_current;

    private readonly Control* _control;
    private readonly int _hostThread;
    private readonly MemoryRanges _ranges = new();
    private readonly List<MemoryRange> _reservations = [];
    private readonly List<NativeCallback> _callbacks = [];
    private readonly CodeArena _arena = new();
    private readonly nint _dispatch;
    private readonly nint _trampoline;
    private readonly nint _read8, _read16, _read32, _read64;
    private readonly nint _write8, _write16, _write32, _write64;

    private NativeHost(GuestAbi abi)
    {
        Abi = abi;
        _hostThread = Environment.CurrentManagedThreadId;
        _control = (Control*)NativeMemory.AllocZeroed((nuint)sizeof(Control));
        _control->HostThread = Kernel32.GetCurrentThreadId();
        _control->FaultHandler = (nint)(delegate* unmanaged<nint, int>)&OnGuestFault;
        _dispatch = (nint)(delegate* unmanaged<ulong, ulong, ulong, ulong, ulong, ulong, ulong, ulong>)&Dispatch;
        _trampoline = _arena.Place(Thunks.Win64ToSysV());
        _read8 = _arena.Place(Thunks.Read(1));
        _read16 = _arena.Place(Thunks.Read(2));
        _read32 = _arena.Place(Thunks.Read(4));
        _read64 = _arena.Place(Thunks.Read(8));
        _write8 = _arena.Place(Thunks.Write(1));
        _write16 = _arena.Place(Thunks.Write(2));
        _write32 = _arena.Place(Thunks.Write(4));
        _write64 = _arena.Place(Thunks.Write(8));
        Kernel32.SetErrorMode(Kernel32.HarnessErrorMode);
        nint filter = _arena.Place(Thunks.ExceptionFilter((ulong)_control, FaultCodes));
        if (Kernel32.AddVectoredExceptionHandler(1, filter) == 0)
            throw new NativeHostException("AddVectoredExceptionHandler failed");
    }

    public static NativeHost? Current => s_current;

    public static NativeHost Create(GuestAbi abi)
    {
        lock (CreateLock)
        {
            if (s_current != null)
                throw new InvalidOperationException("A process has one native host");
            s_current = new NativeHost(abi);
            return s_current;
        }
    }

    public GuestAbi Abi { get; }

    public JsonlLog? Log { get; set; }

    public int FaultStackQwords { get; set; } = 4;

    public Func<ulong, Int128, string> InvalidSpanMessage { get; set; } = DefaultSpanMessage;

    public IReadOnlyList<MemoryRange> Ranges => _ranges.All;

    internal ulong SysVTrampoline => (ulong)_trampoline;

    internal ulong PlaceCode(ReadOnlySpan<byte> code) => (ulong)_arena.Place(code);


    public ulong Alloc(ulong size, ulong? address = null, bool execute = false)
    {
        ulong rounded = (size + 0xFFFF) & ~0xFFFFUL;
        uint protect = execute ? Kernel32.PageExecuteReadWrite : Kernel32.PageReadWrite;
        nint pointer;
        if (address is ulong at)
        {
            if (rounded == 0 || at + rounded < at || _ranges.Overlaps(at, at + rounded))
                throw new NativeHostException($"VirtualAlloc {at} {rounded}: the range is already allocated");
            bool reserved = _reservations.Exists(r => at >= r.Start && at + rounded <= r.End);
            pointer = Kernel32.VirtualAlloc((nint)at, (nuint)rounded, reserved ? Kernel32.MemCommit : Kernel32.MemCommit | Kernel32.MemReserve, protect);
        }
        else
        {
            pointer = Kernel32.VirtualAlloc(0, (nuint)rounded, Kernel32.MemCommit | Kernel32.MemReserve, protect);
        }
        if (pointer == 0 || (address is ulong wanted && (ulong)pointer != wanted))
            throw new NativeHostException($"VirtualAlloc {(address is ulong a ? a.ToString(CultureInfo.InvariantCulture) : "None")} {rounded}: {Marshal.GetLastPInvokeError()}");
        _ranges.Add(new MemoryRange((ulong)pointer, (ulong)pointer + rounded));
        return (ulong)pointer;
    }

    public ulong AllocGuarded(ulong size)
    {
        ulong span = Math.Max((size + 0xFFFF) & ~0xFFFFUL, 0x10000UL);
        nint reserved = Kernel32.VirtualAlloc(0, (nuint)(span + 0x10000), Kernel32.MemReserve, Kernel32.PageNoAccess);
        if (reserved == 0 || Kernel32.VirtualAlloc(reserved, (nuint)span, Kernel32.MemCommit, Kernel32.PageReadWrite) != reserved)
            throw new NativeHostException("Guarded zone allocation failed");
        _ranges.Add(new MemoryRange((ulong)reserved, (ulong)reserved + span));
        return (ulong)reserved;
    }

    public void ReserveFixed(ulong address, ulong size)
    {
        ulong start = address & ~0xFFFFUL;
        ulong end = (address + size + 0xFFFF) & ~0xFFFFUL;
        nint pointer = end > start ? Kernel32.VirtualAlloc((nint)start, (nuint)(end - start), Kernel32.MemReserve, Kernel32.PageNoAccess) : 0;
        if (pointer != (nint)start)
        {
            if (pointer != 0)
                Kernel32.VirtualFree(pointer, 0, Kernel32.MemRelease);
            string message = $"Fixed address range {Hex(start)}..{Hex(end)} is in use in this process; a new process will place its own memory elsewhere";
            Console.Error.WriteLine($"ffport native host: {message}");
            WriteLog(new JsonMap { ["error"] = message });
            Exit(NativeExitCodes.AddressSpaceBusy);
        }
        _reservations.Add(new MemoryRange(start, end));
    }

    public void RegisterExisting(ulong start, ulong end)
    {
        if (end <= start || _ranges.Overlaps(start, end))
            throw new NativeHostException($"Cannot register {Hex(start)}..{Hex(end)}");
        _ranges.Add(new MemoryRange(start, end));
    }

    public void RegisterThreadStack()
    {
        Kernel32.GetCurrentThreadStackLimits(out nuint low, out nuint high);
        RegisterExisting(low, high);
    }

    public bool TryReserveFixed(ulong address, ulong size)
    {
        ulong start = address & ~0xFFFFUL;
        ulong end = (address + size + 0xFFFF) & ~0xFFFFUL;
        nint pointer = end > start ? Kernel32.VirtualAlloc((nint)start, (nuint)(end - start), Kernel32.MemReserve, Kernel32.PageNoAccess) : 0;
        if (pointer != (nint)start)
        {
            if (pointer != 0)
                Kernel32.VirtualFree(pointer, 0, Kernel32.MemRelease);
            return false;
        }
        _reservations.Add(new MemoryRange(start, end));
        return true;
    }

    public bool TryRegion(ulong address, out MemoryRange range) => _ranges.TryFind(address, out range);

    public MemoryRange? Region(ulong address) => _ranges.TryFind(address, out MemoryRange range) ? range : null;

    public void Check(ulong address, long size)
    {
        if (size < 0)
            Fail(InvalidSpanMessage(address, size));
        Check(address, (ulong)size);
    }

    public void Check(ulong address, ulong size)
    {
        if (!_ranges.TryFind(address, out MemoryRange range) || size > range.End - address)
            Fail(InvalidSpanMessage(address, size));
    }

    public byte[] Read(ulong address, long size) => View(address, size).ToArray();

    public byte[] Read(ulong address, ulong size) => View(address, size).ToArray();

    public ReadOnlySpan<byte> View(ulong address, long size)
    {
        Check(address, size);
        return new ReadOnlySpan<byte>((void*)address, checked((int)size));
    }

    public ReadOnlySpan<byte> View(ulong address, ulong size)
    {
        Check(address, size);
        return new ReadOnlySpan<byte>((void*)address, checked((int)size));
    }

    public void Write(ulong address, ReadOnlySpan<byte> data)
    {
        Check(address, (long)data.Length);
        data.CopyTo(new Span<byte>((void*)address, data.Length));
    }

    public void Fill(ulong address, byte value, ulong size)
    {
        Check(address, size);
        NativeMemory.Fill((void*)address, (nuint)size, value);
    }

    public void Copy(ulong destination, ulong source, ulong size)
    {
        Check(source, size);
        Check(destination, size);
        NativeMemory.Copy((void*)source, (void*)destination, (nuint)size);
    }

    public long Load(ulong address, Stream source, long count)
    {
        Check(address, count);
        long total = 0;
        while (total < count)
        {
            int chunk = (int)Math.Min(count - total, 1 << 24);
            int read = source.Read(new Span<byte>((void*)(address + (ulong)total), chunk));
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }


    public byte B(ulong address) => Direct(address, 1) ? *(byte*)address : (byte)Guarded(_read8, address, 0);

    public ushort W(ulong address) => Direct(address, 2) ? Unsafe.ReadUnaligned<ushort>((void*)address) : (ushort)Guarded(_read16, address, 0);

    public uint D(ulong address) => Direct(address, 4) ? Unsafe.ReadUnaligned<uint>((void*)address) : (uint)Guarded(_read32, address, 0);

    public ulong Q(ulong address) => Direct(address, 8) ? Unsafe.ReadUnaligned<ulong>((void*)address) : Guarded(_read64, address, 0);

    public void SetB(ulong address, byte value)
    {
        if (Direct(address, 1))
            *(byte*)address = value;
        else
            Guarded(_write8, address, value);
    }

    public void SetW(ulong address, ushort value)
    {
        if (Direct(address, 2))
            Unsafe.WriteUnaligned((void*)address, value);
        else
            Guarded(_write16, address, value);
    }

    public void SetD(ulong address, uint value)
    {
        if (Direct(address, 4))
            Unsafe.WriteUnaligned((void*)address, value);
        else
            Guarded(_write32, address, value);
    }

    public void SetQ(ulong address, ulong value)
    {
        if (Direct(address, 8))
            Unsafe.WriteUnaligned((void*)address, value);
        else
            Guarded(_write64, address, value);
    }

    private bool Direct(ulong address, ulong size) => _ranges.TryFind(address, out MemoryRange range) && size <= range.End - address;

    private ulong Guarded(nint stub, ulong address, ulong value)
    {
        ulong mode = EnterGuest();
        try
        {
            return ((delegate* unmanaged<ulong, ulong, ulong>)stub)(address, value);
        }
        finally
        {
            _control->GuestMode = mode;
        }
    }

    public string CString(ulong address, int limit = 4096)
    {
        if (!_ranges.TryFind(address, out MemoryRange range))
            Fail($"Invalid string address {Hex(address)}");
        ReadOnlySpan<byte> data = new((void*)address, (int)Math.Min((ulong)Math.Max(limit, 0), range.End - address));
        int end = data.IndexOf((byte)0);
        if (end < 0)
            Fail("Unterminated string");
        return DecodeUtf8(data[..end]);
    }

    public string? TryCString(ulong address, int limit = 4096)
    {
        if (!_ranges.TryFind(address, out MemoryRange range))
            return null;
        ReadOnlySpan<byte> data = new((void*)address, (int)Math.Min((ulong)Math.Max(limit, 0), range.End - address));
        int end = data.IndexOf((byte)0);
        return end < 0 ? null : DecodeUtf8(data[..end]);
    }

    public static string DecodeUtf8(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);


    public void Hook(ulong address, NativeCallback callback) => Hook(address, callback, Abi);

    public void Hook(ulong address, NativeCallback callback, GuestAbi abi)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ulong id = (ulong)_callbacks.Count;
        _callbacks.Add(callback);
        byte[] thunk = abi == GuestAbi.SysV ? Thunks.SysVToWin64((ulong)_dispatch, id) : Thunks.Win64ToWin64((ulong)_dispatch, id);
        nint entry = _arena.Place(thunk);
        Write(address, Thunks.AbsoluteJump((ulong)entry));
        Kernel32.FlushInstructionCache(Kernel32.GetCurrentProcess(), (nint)address, Thunks.PatchSize);
    }

    public ulong Call(ulong function, ulong a1 = 0, ulong a2 = 0, ulong a3 = 0, ulong a4 = 0, ulong a5 = 0, ulong a6 = 0) =>
        Abi == GuestAbi.SysV ? CallSysV(function, a1, a2, a3, a4, a5, a6) : CallWin64(function, a1, a2, a3, a4, a5, a6);

    public ulong CallSysV(ulong function, ulong a1 = 0, ulong a2 = 0, ulong a3 = 0, ulong a4 = 0, ulong a5 = 0, ulong a6 = 0)
    {
        ulong mode = EnterGuest();
        try
        {
            return ((delegate* unmanaged<ulong, ulong, ulong, ulong, ulong, ulong, ulong, ulong>)_trampoline)(a1, a2, a3, a4, a5, a6, function);
        }
        finally
        {
            _control->GuestMode = mode;
        }
    }

    public ulong CallWin64(ulong function, ulong a1 = 0, ulong a2 = 0, ulong a3 = 0, ulong a4 = 0, ulong a5 = 0, ulong a6 = 0)
    {
        ulong mode = EnterGuest();
        try
        {
            return ((delegate* unmanaged<ulong, ulong, ulong, ulong, ulong, ulong, ulong>)(nint)function)(a1, a2, a3, a4, a5, a6);
        }
        finally
        {
            _control->GuestMode = mode;
        }
    }

    private ulong EnterGuest()
    {
        if (Environment.CurrentManagedThreadId != _hostThread)
            throw new InvalidOperationException("Guest code runs only on the thread that created the native host");
        ulong mode = _control->GuestMode;
        _control->GuestMode = 1;
        return mode;
    }

    [UnmanagedCallersOnly]
    private static ulong Dispatch(ulong a1, ulong a2, ulong a3, ulong a4, ulong a5, ulong a6, ulong id)
    {
        NativeHost host = s_current!;
        ulong mode = host._control->GuestMode;
        host._control->GuestMode = 0;
        try
        {
            return host._callbacks[(int)id](new NativeArgs(a1, a2, a3, a4, a5, a6));
        }
        catch (Exception error)
        {
            host.Fail(error.ToString());
            return 0;
        }
        finally
        {
            host._control->GuestMode = mode;
        }
    }

    public Func<ulong, string?>? DescribeAddress { get; set; }

    [UnmanagedCallersOnly]
    private static int OnGuestFault(nint pointers)
    {
        NativeHost? host = s_current;
        nint record = *(nint*)pointers;
        nint context = *(nint*)(pointers + 8);
        uint code = *(uint*)record;
        ulong access = *(ulong*)(record + 40);
        ulong rsp = *(ulong*)(context + 152);
        ulong rip = *(ulong*)(context + 248);
        var stack = new List<object?>();
        int depth = host?.FaultStackQwords ?? 4;
        for (int i = 0; i < depth; i++)
        {
            ulong at = rsp + (ulong)(8 * i);
            if (!Kernel32.IsReadable(at, 8))
                break;
            stack.Add(Hex(*(ulong*)at));
        }
        var fault = new JsonMap
        {
            ["native_exception"] = Hex(code),
            ["rip"] = Hex(rip),
            ["access_address"] = Hex(access),
            ["rsp"] = Hex(rsp),
            ["stack"] = stack,
        };
        string[] names = ["rax", "rcx", "rdx", "rbx", "rsp", "rbp", "rsi", "rdi", "r8", "r9", "r10", "r11", "r12", "r13", "r14", "r15"];
        var registers = new JsonMap();
        for (int r = 0; r < names.Length; r++)
        {
            ulong value = *(ulong*)(context + 120 + 8 * r);
            string? note = null;
            try
            {
                note = host?.DescribeAddress?.Invoke(value);
            }
            catch (Exception)
            {
            }
            registers[names[r]] = note == null ? Hex(value) : $"{Hex(value)} ({note})";
        }
        fault["registers"] = registers;
        try
        {
            string where = host?.Region(rip) is MemoryRange range ? $" (guest range {range}, offset {Hex(rip - range.Start)})" : "";
            Console.Error.WriteLine($"ffport native host: exception {Hex(code)} at {Hex(rip)}{where}, address {Hex(access)}");
            if (host != null)
                host.WriteLog(fault);
            else
                Console.Error.WriteLine(PyJson.Dumps(fault, indent: null));
        }
        catch (Exception)
        {
        }
        if (host != null)
            host.Exit(NativeExitCodes.NativeFault);
        Kernel32.TerminateProcess(Kernel32.GetCurrentProcess(), NativeExitCodes.NativeFault);
        return 0;
    }


    public void WriteLog(JsonMap record)
    {
        if (Log is JsonlLog log)
            log.Write(record);
        else
            Console.Error.WriteLine(PyJson.Dumps(record, indent: null));
    }

    [DoesNotReturn]
    public void Fail(string message)
    {
        WriteLog(new JsonMap { ["error"] = message });
        Exit(NativeExitCodes.Fail);
    }

    public Action? BeforeExit { get; set; }

    [DoesNotReturn]
    public void Exit(int code)
    {
        try
        {
            BeforeExit?.Invoke();
        }
        catch (Exception)
        {
        }
        JsonlLog.CloseAll();
        try
        {
            Console.Out.Flush();
            Console.Error.Flush();
        }
        catch (Exception)
        {
        }
        Kernel32.TerminateProcess(Kernel32.GetCurrentProcess(), (uint)code);
        Environment.Exit(code);
        throw new UnreachableException();
    }

    internal void WriteErrorIfLogOpen(string text)
    {
        if (Log is JsonlLog log)
            log.Write(new JsonMap { ["error"] = text });
    }


    public static string Hex(ulong value) => "0x" + value.ToString("x", CultureInfo.InvariantCulture);

    public static string Hex(Int128 value) =>
        value < 0 ? "-0x" + (-value).ToString("x", CultureInfo.InvariantCulture) : "0x" + value.ToString("x", CultureInfo.InvariantCulture);

    public static string DefaultSpanMessage(ulong address, Int128 size) => $"Invalid memory span {Hex(address)}+{Hex(size)}";
}
