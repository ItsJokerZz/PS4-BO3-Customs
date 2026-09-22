using System.Buffers.Binary;
using System.Runtime.InteropServices;
using static FFPorter.Core.Common.Native.X64.Reg;

namespace FFPorter.Core.Common.Native;

internal static class Thunks
{
    public const int PatchSize = 14;

    public static byte[] AbsoluteJump(ulong target)
    {
        var patch = new byte[PatchSize];
        patch[0] = 0xFF;
        patch[1] = 0x25;
        BinaryPrimitives.WriteUInt64LittleEndian(patch.AsSpan(6), target);
        return patch;
    }

    public static byte[] SysVToWin64(ulong target, ulong id) => new X64()
        .SubRsp(0x48)
        .StoreRsp(0x20, R8)
        .StoreRsp(0x28, R9)
        .MovImm(Rax, id).StoreRsp(0x30, Rax)
        .Mov(R9, Rcx)
        .Mov(R8, Rdx)
        .Mov(Rdx, Rsi)
        .Mov(Rcx, Rdi)
        .MovImm(Rax, target).Call(Rax)
        .AddRsp(0x48).Ret()
        .ToArray();

    public static byte[] Win64ToWin64(ulong target, ulong id) => new X64()
        .SubRsp(0x48)
        .LoadRsp(Rax, 0x48 + 0x28).StoreRsp(0x20, Rax)
        .LoadRsp(Rax, 0x48 + 0x30).StoreRsp(0x28, Rax)
        .MovImm(Rax, id).StoreRsp(0x30, Rax)
        .MovImm(Rax, target).Call(Rax)
        .AddRsp(0x48).Ret()
        .ToArray();

    public static byte[] Win64ToSysV()
    {
        var x = new X64().Push(Rdi).Push(Rsi).SubRsp(0xB8);
        for (int i = 0; i < 10; i++)
            x.StoreXmm(16 * i, 6 + i);
        const int entry = 0xB8 + 16;
        x.Mov(Rdi, Rcx).Mov(Rsi, Rdx).Mov(Rdx, R8).Mov(Rcx, R9)
            .LoadRsp(R8, entry + 0x28)
            .LoadRsp(R9, entry + 0x30)
            .LoadRsp(Rax, entry + 0x38)
            .Call(Rax);
        for (int i = 0; i < 10; i++)
            x.LoadXmm(6 + i, 16 * i);
        return x.AddRsp(0xB8).Pop(Rsi).Pop(Rdi).Ret().ToArray();
    }

    public static byte[] ExceptionFilter(ulong control, ReadOnlySpan<uint> codes)
    {
        var x = new X64();
        var handle = new X64.Label();
        var pass = new X64.Label();
        x.Raw(0x48, 0x8B, 0x01)
            .Raw(0x8B, 0x00);
        foreach (uint code in codes)
            x.Raw(0x3D).Imm32(code).Je(handle);
        x.Bind(pass).Raw(0x31, 0xC0).Ret();
        x.Bind(handle)
            .MovImm(Rax, control)
            .Raw(0x65, 0x48, 0x8B, 0x14, 0x25, 0x48, 0x00, 0x00, 0x00)
            .Raw(0x48, 0x3B, 0x10).Jne(pass)
            .Raw(0x48, 0x83, 0x78, 0x08, 0x00).Je(pass)
            .Raw(0x48, 0xC7, 0x40, 0x08, 0x00, 0x00, 0x00, 0x00)
            .SubRsp(0x28)
            .Raw(0xFF, 0x50, 0x10)
            .AddRsp(0x28).Ret();
        return x.ToArray();
    }

    public static byte[] Read(int size) => size switch
    {
        1 => [0x0F, 0xB6, 0x01, 0xC3],
        2 => [0x0F, 0xB7, 0x01, 0xC3],
        4 => [0x8B, 0x01, 0xC3],
        8 => [0x48, 0x8B, 0x01, 0xC3],
        _ => throw new ArgumentOutOfRangeException(nameof(size)),
    };

    public static byte[] Write(int size) => size switch
    {
        1 => [0x88, 0x11, 0xC3],
        2 => [0x66, 0x89, 0x11, 0xC3],
        4 => [0x89, 0x11, 0xC3],
        8 => [0x48, 0x89, 0x11, 0xC3],
        _ => throw new ArgumentOutOfRangeException(nameof(size)),
    };
}

internal sealed unsafe class CodeArena
{
    private const int PageSize = 1 << 16;

    private nint _page;
    private int _used = PageSize;

    public nint Place(ReadOnlySpan<byte> code)
    {
        if (code.Length > PageSize)
            throw new ArgumentException("Thunk larger than an arena page", nameof(code));
        int at = (_used + 15) & ~15;
        if (at + code.Length > PageSize)
        {
            _page = Kernel32.VirtualAlloc(0, PageSize, Kernel32.MemCommit | Kernel32.MemReserve, Kernel32.PageExecuteReadWrite);
            if (_page == 0)
                throw new NativeHostException($"VirtualAlloc for host thunks failed: {Marshal.GetLastPInvokeError()}");
            at = 0;
        }
        code.CopyTo(new Span<byte>((void*)(_page + at), code.Length));
        Kernel32.FlushInstructionCache(Kernel32.GetCurrentProcess(), _page + at, (nuint)code.Length);
        _used = at + code.Length;
        return _page + at;
    }
}
