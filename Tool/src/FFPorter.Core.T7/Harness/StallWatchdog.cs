using System.Diagnostics;
using System.Runtime.InteropServices;
using FFPorter.Core.Common.Native;

namespace FFPorter.Core.T7.Harness;

public sealed unsafe partial class StallWatchdog : IDisposable
{
    private const uint ThreadAccess = 0x0002 | 0x0008 | 0x0040;
    private const uint ContextFull = 0x0010000B;
    private readonly uint _threadId;
    private readonly Thread _thread;
    private readonly TimeSpan _timeout;
    private readonly Action<string> _report;
    private long _lastTick = Stopwatch.GetTimestamp();
    private volatile bool _stop;

    public StallWatchdog(TimeSpan timeout, Action<string> report)
    {
        _threadId = GetCurrentThreadId();
        _timeout = timeout;
        _report = report;
        _thread = new Thread(Watch) { IsBackground = true, Name = "stall watchdog" };
        _thread.Start();
    }

    public void Progress() => Interlocked.Exchange(ref _lastTick, Stopwatch.GetTimestamp());

    private void Watch()
    {
        while (!_stop)
        {
            Thread.Sleep(1000);
            if (Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastTick)) < _timeout)
                continue;
            nint handle = OpenThread(ThreadAccess, 0, _threadId);
            if (handle == 0)
                return;
            SuspendThread(handle);
            byte* context = (byte*)NativeMemory.AlignedAlloc(1232, 16);
            new Span<byte>(context, 1232).Clear();
            *(uint*)(context + 0x30) = ContextFull;
            string text;
            if (GetThreadContext(handle, context) != 0)
            {
                ulong rip = *(ulong*)(context + 0xF8), rsp = *(ulong*)(context + 0x98);
                var stack = new List<string>();
                for (int i = 0; i < 64; i++)
                {
                    ulong at = rsp + (ulong)(8 * i);
                    if (!Kernel32.IsReadable(at, 8))
                        break;
                    ulong value = *(ulong*)at;
                    if (value is > 0x10000 and < 0x1520000)
                        stack.Add($"0x{value:x}");
                }
                ulong rax = *(ulong*)(context + 0x78), rcx = *(ulong*)(context + 0x80), rdx = *(ulong*)(context + 0x88), rbx = *(ulong*)(context + 0x90);
                ulong rsi = *(ulong*)(context + 0xA8), rdi = *(ulong*)(context + 0xB0);
                text = $"stalled for {_timeout.TotalSeconds:0}s: rip 0x{rip:x} rsp 0x{rsp:x} rax 0x{rax:x} rbx 0x{rbx:x} rcx 0x{rcx:x} rdx 0x{rdx:x} rsi 0x{rsi:x} rdi 0x{rdi:x}; code-range stack values: {string.Join(" ", stack)}";
            }
            else
            {
                text = "stalled; GetThreadContext failed";
            }
            _report(text);
            Kernel32.TerminateProcess(Kernel32.GetCurrentProcess(), NativeExitCodes.Fail);
        }
    }

    public void Dispose() => _stop = true;

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll")]
    private static partial nint OpenThread(uint access, int inherit, uint threadId);

    [LibraryImport("kernel32.dll")]
    private static partial uint SuspendThread(nint thread);

    [LibraryImport("kernel32.dll")]
    private static partial int GetThreadContext(nint thread, byte* context);
}
