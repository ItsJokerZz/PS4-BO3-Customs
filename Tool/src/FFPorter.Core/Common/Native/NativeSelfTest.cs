using System.Runtime.CompilerServices;
using FFPorter.Core.Common.Python;
using static FFPorter.Core.Common.Native.X64.Reg;

namespace FFPorter.Core.Common.Native;

internal static class NativeSelfTest
{
    public const string TaskName = "selftest";

    public const int Misaligned = 0x0BAD0A11;
    public const int BadRegisters = 0x0BADBAD1;
    public const int BadXmm = 0x0BADBAD2;
    public const ulong SentinelRsi = 0x5151_5151_5151_5151;
    public const ulong SentinelRdi = 0xD1D1_D1D1_D1D1_D1D1;

    public static readonly string[] Scenarios =
    [
        "calls", "callback", "hook", "managed-null", "reserve", "busy",
        "fault-read", "fault-win64", "fault-ud2", "nested-fault", "wild-read", "fail", "throw", "check", "unhandled",
    ];

    public static int Run(NativeTaskContext context)
    {
        if (context.Arguments.Count != 2 || Array.IndexOf(Scenarios, context.Arguments[0]) < 0)
        {
            context.Error.WriteLine($"usage: ffport {NativeChild.Verb} {TaskName} <{string.Join('|', Scenarios)}> <log.jsonl>");
            return NativeExitCodes.Fail;
        }
        string scenario = context.Arguments[0];
        NativeHost host = NativeHost.Create(GuestAbi.SysV);
        host.Log = JsonlLog.Open(context.Arguments[1]);
        host.WriteLog(new JsonMap { ["selftest"] = scenario });

        JsonMap summary;
        if (scenario == "reserve")
        {
            summary = Reserve(host);
        }
        else if (scenario == "busy")
        {
            ulong taken = host.AllocGuarded(0x10000);
            host.ReserveFixed(taken, 0x10000);
            summary = new JsonMap { ["survived"] = true };
        }
        else
        {
            var guest = new GuestProgram(host);
            host.WriteLog(new JsonMap
            {
                ["guest_code"] = NativeHost.Hex(guest.Base),
                ["fault_rip"] = NativeHost.Hex(guest.FaultRip),
                ["ud2_rip"] = NativeHost.Hex(guest.Ud2),
                ["fault_address"] = NativeHost.Hex(guest.Guard),
            });
            summary = scenario switch
            {
                "calls" => Calls(host, guest),
                "callback" => Callback(host, guest),
                "hook" => Hook(host, guest),
                "managed-null" => ManagedNull(host, guest),
                _ => Fault(host, guest, scenario),
            };
        }
        host.WriteLog(new JsonMap { ["passed"] = true });
        context.Out.WriteLine(PyJson.Dumps(summary, indent: null));
        return NativeExitCodes.Ok;
    }

    private static JsonMap Calls(NativeHost host, GuestProgram guest)
    {
        ulong sysvThunk = host.PlaceCode(Thunks.SysVToWin64(guest.Probe, 0x5A));
        ulong win64Thunk = host.PlaceCode(Thunks.Win64ToWin64(guest.Probe, 0x5B));
        return new JsonMap
        {
            ["sum6"] = host.CallSysV(guest.Sum6, 1, 2, 3, 4, 5, 6),
            ["sum6_high"] = host.CallSysV(guest.Sum6, 1UL << 32, 2UL << 32, 3UL << 32, 4UL << 32, 5UL << 32, 6UL << 32),
            ["trampoline_check"] = host.CallWin64(guest.Checker, host.SysVTrampoline, guest.Sum6, 0x1000),
            ["sysv_thunk_probe"] = host.CallSysV(sysvThunk),
            ["win64_thunk_probe"] = host.CallWin64(win64Thunk),
        };
    }

    private static JsonMap Callback(NativeHost host, GuestProgram guest)
    {
        var seen = new List<object?>();
        host.Hook(guest.TargetSysV, args => { seen.Add(Arguments(args)); return Sum(args) + 1000; }, GuestAbi.SysV);
        host.Hook(guest.TargetWin64, args => { seen.Add(Arguments(args)); return Sum(args) + 2000; }, GuestAbi.Win64);
        ulong sysv = host.CallSysV(guest.CallerSysV, 0x100);
        ulong win64 = host.CallWin64(guest.CallerWin64, 0x200);
        host.Hook(guest.TargetSysV, args => host.CallSysV(guest.Sum6, args.A1, args.A2, args.A3, args.A4, args.A5, args.A6), GuestAbi.SysV);
        ulong nested = host.CallSysV(guest.CallerSysV, 0x10);
        return new JsonMap { ["sysv"] = sysv, ["win64"] = win64, ["nested"] = nested, ["arguments"] = seen };
    }

    private static JsonMap Hook(NativeHost host, GuestProgram guest)
    {
        ulong before = host.CallSysV(guest.Outer);
        int calls = 0;
        host.Hook(guest.Inner, _ => { calls++; return 42; });
        ulong after = host.CallSysV(guest.Outer);
        ulong direct = host.CallSysV(guest.Inner);
        return new JsonMap
        {
            ["before"] = before,
            ["after"] = after,
            ["direct"] = direct,
            ["callbacks"] = calls,
            ["patch"] = Convert.ToHexStringLower(host.Read(guest.Inner, 14)),
        };
    }

    private static JsonMap ManagedNull(NativeHost host, GuestProgram guest)
    {
        int caught = NullDereference();
        host.Hook(guest.TargetSysV, args => { caught += NullDereference(); return Sum(args); }, GuestAbi.SysV);
        ulong result = host.CallSysV(guest.CallerSysV, 0);
        caught += NullDereference();
        return new JsonMap { ["caught"] = caught, ["result"] = result };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? Nothing() => null;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int NullDereference()
    {
        try
        {
            return Nothing()!.Length < 0 ? 2 : 0;
        }
        catch (NullReferenceException)
        {
            return 1;
        }
    }

    private static JsonMap Reserve(NativeHost host)
    {
        host.ReserveFixed(0x10000, 0x14510000);
        host.ReserveFixed(0x140000000, 0x13000000);
        ulong low = host.Alloc(0x10000, 0x10000, execute: true);
        ulong image = host.Alloc(0x100000, 0x140000000, execute: true);
        host.SetQ(low, 0x1122334455667788);
        host.SetQ(image + 8, 0x8877665544332211);
        return new JsonMap
        {
            ["image_base"] = NativeHost.Hex((ulong)Kernel32.GetModuleHandle(null)),
            ["low"] = NativeHost.Hex(low),
            ["image"] = NativeHost.Hex(image),
            ["low_value"] = NativeHost.Hex(host.Q(low)),
            ["image_value"] = NativeHost.Hex(host.Q(image + 8)),
        };
    }

    private static JsonMap Fault(NativeHost host, GuestProgram guest, string scenario)
    {
        switch (scenario)
        {
            case "fault-read":
                host.CallSysV(guest.Faulter);
                break;
            case "fault-win64":
                host.CallWin64(guest.Faulter);
                break;
            case "fault-ud2":
                host.CallSysV(guest.Ud2);
                break;
            case "nested-fault":
                host.Hook(guest.TargetSysV, _ => host.CallSysV(guest.Faulter), GuestAbi.SysV);
                host.CallSysV(guest.CallerSysV, 1);
                break;
            case "wild-read":
                host.Q(guest.Guard);
                break;
            case "fail":
                host.Hook(guest.TargetSysV, _ => { host.Fail("deliberate failure"); return 0; }, GuestAbi.SysV);
                host.CallSysV(guest.CallerSysV, 1);
                break;
            case "throw":
                host.Hook(guest.TargetSysV, _ => throw new InvalidOperationException("deliberate exception in a hook"), GuestAbi.SysV);
                host.CallSysV(guest.CallerSysV, 1);
                break;
            case "check":
                host.Read(guest.Guard, 16);
                break;
            case "unhandled":
                throw new InvalidOperationException("deliberate unhandled error");
        }
        return new JsonMap { ["survived"] = true };
    }

    private static ulong Sum(NativeArgs a) => a.A1 + a.A2 + a.A3 + a.A4 + a.A5 + a.A6;

    private static List<object?> Arguments(NativeArgs a) => [a.A1, a.A2, a.A3, a.A4, a.A5, a.A6];

    private static ulong SentinelXmm(int xmm) => 0xA5A5_0000_C3C3_0000UL | ((ulong)(uint)xmm << 32) | (uint)xmm;

    private sealed class GuestProgram
    {
        private readonly NativeHost _host;
        private ulong _cursor;

        public GuestProgram(NativeHost host)
        {
            _host = host;
            Guard = host.AllocGuarded(0x10000) + 0x10000;
            Base = host.Alloc(0x10000, execute: true);
            host.Fill(Base, 0xCC, 0x10000);
            _cursor = Base;
            Sum6 = Place(Sum6Code());
            Checker = Place(CheckerCode());
            Probe = Place(ProbeCode());
            TargetSysV = Place([0xC3], reserve: 16);
            TargetWin64 = Place([0xC3], reserve: 16);
            CallerSysV = Place(CallerSysVCode(TargetSysV));
            CallerWin64 = Place(CallerWin64Code(TargetWin64));
            Inner = Place([0xB8, 0x07, 0x00, 0x00, 0x00, 0xC3], reserve: 16);
            Outer = Place(new X64().SubRsp(8).MovImm(Rax, Inner).Call(Rax).AddImm32(Rax, 1000).AddRsp(8).Ret().ToArray());
            Faulter = Place(new X64().MovImm(Rax, Guard).Raw(0x48, 0x8B, 0x00).Ret().ToArray());
            FaultRip = Faulter + 10;
            Ud2 = Place([0x0F, 0x0B, 0xC3]);
        }

        public ulong Base { get; }
        public ulong Guard { get; }
        public ulong Sum6 { get; }
        public ulong Checker { get; }
        public ulong Probe { get; }
        public ulong TargetSysV { get; }
        public ulong TargetWin64 { get; }
        public ulong CallerSysV { get; }
        public ulong CallerWin64 { get; }
        public ulong Inner { get; }
        public ulong Outer { get; }
        public ulong Faulter { get; }
        public ulong FaultRip { get; }
        public ulong Ud2 { get; }

        private ulong Place(byte[] code, int reserve = 0)
        {
            ulong at = _cursor;
            _host.Write(at, code);
            _cursor += (ulong)((Math.Max(code.Length, reserve) + 15) & ~15);
            return at;
        }

        private static byte[] Sum6Code()
        {
            var x = new X64();
            var misaligned = new X64.Label();
            x.Mov(Rax, Rsp).Raw(0x83, 0xE0, 0x0F)
                .Raw(0x83, 0xF8, 0x08).Jne(misaligned)
                .Raw(0x48, 0x8D, 0x04, 0x77)
                .Raw(0x4C, 0x6B, 0xD2, 0x03).Add(Rax, R10)
                .Raw(0x48, 0x8D, 0x04, 0x88)
                .Raw(0x4D, 0x6B, 0xD0, 0x05).Add(Rax, R10)
                .Raw(0x4D, 0x6B, 0xD1, 0x06).Add(Rax, R10)
                .Raw(0x31, 0xF6, 0x31, 0xFF);
            for (int xmm = 6; xmm < 16; xmm++)
                x.Pxor(xmm);
            return x.Ret().Bind(misaligned).MovImm32(Rax, Misaligned).Ret().ToArray();
        }

        private static byte[] CheckerCode()
        {
            var x = new X64();
            var badRegisters = new X64.Label();
            var badXmm = new X64.Label();
            var done = new X64.Label();
            x.Push(Rbx).Push(Rsi).Push(Rdi).Push(R12).SubRsp(0xE8);
            for (int i = 0; i < 10; i++)
                x.StoreXmm(0x40 + 16 * i, 6 + i);
            x.Mov(Rbx, Rcx).Mov(R12, Rdx)
                .Lea(Rcx, R8, 1).Lea(Rdx, R8, 2).Lea(R9, R8, 4)
                .Lea(Rax, R8, 5).StoreRsp(0x20, Rax)
                .Lea(Rax, R8, 6).StoreRsp(0x28, Rax)
                .StoreRsp(0x30, R12)
                .Lea(R8, R8, 3)
                .MovImm(Rsi, SentinelRsi).MovImm(Rdi, SentinelRdi);
            for (int xmm = 6; xmm < 16; xmm++)
                x.MovImm(Rax, SentinelXmm(xmm)).MovqToXmm(xmm, Rax);
            x.Call(Rbx).Mov(R12, Rax)
                .MovImm(Rax, SentinelRsi).Cmp(Rsi, Rax).Jne(badRegisters)
                .MovImm(Rax, SentinelRdi).Cmp(Rdi, Rax).Jne(badRegisters);
            for (int xmm = 6; xmm < 16; xmm++)
                x.MovqFromXmm(Rax, xmm).MovImm(Rdx, SentinelXmm(xmm)).Cmp(Rax, Rdx).Jne(badXmm);
            x.Mov(Rax, R12).Jmp(done)
                .Bind(badRegisters).MovImm32(Rax, BadRegisters).Jmp(done)
                .Bind(badXmm).MovImm32(Rax, BadXmm)
                .Bind(done);
            for (int i = 0; i < 10; i++)
                x.LoadXmm(6 + i, 0x40 + 16 * i);
            return x.AddRsp(0xE8).Pop(R12).Pop(Rdi).Pop(Rsi).Pop(Rbx).Ret().ToArray();
        }

        private static byte[] ProbeCode() => new X64()
            .Mov(Rax, Rsp).Raw(0x83, 0xE0, 0x0F)
            .LoadRsp(Rdx, 0x38)
            .Raw(0x48, 0xC1, 0xE2, 0x08)
            .Raw(0x48, 0x09, 0xD0)
            .Ret().ToArray();

        private static byte[] CallerSysVCode(ulong target) => new X64()
            .Push(Rbx).Mov(Rbx, Rdi)
            .Lea(Rdi, Rbx, 1).Lea(Rsi, Rbx, 2).Lea(Rdx, Rbx, 3).Lea(Rcx, Rbx, 4).Lea(R8, Rbx, 5).Lea(R9, Rbx, 6)
            .MovImm(Rax, target).Call(Rax)
            .Add(Rax, Rbx).Pop(Rbx).Ret().ToArray();

        private static byte[] CallerWin64Code(ulong target) => new X64()
            .Push(Rbx).SubRsp(0x30).Mov(Rbx, Rcx)
            .Lea(Rax, Rbx, 5).StoreRsp(0x20, Rax)
            .Lea(Rax, Rbx, 6).StoreRsp(0x28, Rax)
            .Lea(Rcx, Rbx, 1).Lea(Rdx, Rbx, 2).Lea(R8, Rbx, 3).Lea(R9, Rbx, 4)
            .MovImm(Rax, target).Call(Rax)
            .Add(Rax, Rbx).AddRsp(0x30).Pop(Rbx).Ret().ToArray();
    }
}
