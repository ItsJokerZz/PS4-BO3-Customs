using System.Diagnostics.CodeAnalysis;
using FFPorter.Core.Common.Python;

namespace FFPorter.Core.Common.Native;

public static class NativeExitCodes
{
    public const int Ok = 0;

    public const int Error = 1;

    public const int Fail = 2;

    public const int NativeFault = 3;

    public const int AddressSpaceBusy = 4;
}

public delegate int NativeTask(NativeTaskContext context);

public sealed class NativeTaskContext(IReadOnlyList<string> arguments, TextWriter output, TextWriter error)
{
    public IReadOnlyList<string> Arguments { get; } = arguments;

    public TextWriter Out { get; } = output;

    public TextWriter Error { get; } = error;
}

public static class NativeTasks
{
    private static readonly Dictionary<string, NativeTask> Tasks = new(StringComparer.Ordinal)
    {
        [NativeSelfTest.TaskName] = NativeSelfTest.Run,
    };

    public static void Register(string name, NativeTask task)
    {
        lock (Tasks)
            Tasks[name] = task;
    }

    public static IEnumerable<string> Names
    {
        get
        {
            lock (Tasks)
                return [.. Tasks.Keys];
        }
    }

    public static bool TryGet(string name, [NotNullWhen(true)] out NativeTask? task)
    {
        lock (Tasks)
            return Tasks.TryGetValue(name, out task);
    }
}

public static class NativeChild
{
    public const string Verb = "__native";

    private const int TaskStackSize = 64 << 20;

    public static int Main(IReadOnlyList<string> arguments, TextWriter output, TextWriter error)
    {
        Kernel32.SetErrorMode(Kernel32.HarnessErrorMode);
        if (arguments.Count == 0 || !NativeTasks.TryGet(arguments[0], out NativeTask? task))
        {
            string name = arguments.Count == 0 ? "(none)" : PyText.Repr(arguments[0]);
            error.WriteLine($"ffport: unknown native task {name}; known: {string.Join(", ", NativeTasks.Names)}");
            return NativeExitCodes.Fail;
        }

        var context = new NativeTaskContext(arguments.Skip(1).ToArray(), output, error);
        int code = NativeExitCodes.Error;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                code = task(context);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }, TaskStackSize)
        {
            Name = "ffport native task",
        };
        thread.Start();
        thread.Join();

        NativeHost? host = NativeHost.Current;
        if (failure != null)
        {
            host?.WriteErrorIfLogOpen(failure.ToString());
            error.WriteLine(failure.ToString());
            code = NativeExitCodes.Error;
        }
        JsonlLog.CloseAll();
        return code;
    }
}
