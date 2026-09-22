using System.Diagnostics;
using System.Text;

namespace FFPorter.Core.Common.Native;

public sealed record NativeProcessOptions
{
    public string? HostExecutable { get; init; }

    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string?>? Environment { get; init; }

    public TimeSpan? Timeout { get; init; }

    public int MaxAttempts { get; init; } = 3;

    public Action<string>? OnOutputLine { get; init; }

    public Action<string>? OnErrorLine { get; init; }

    public CancellationToken Cancellation { get; init; }
}

public sealed record NativeProcessResult(int ExitCode, string Stdout, string Stderr, TimeSpan Elapsed, int Attempts, bool Killed)
{
    public bool Passed => ExitCode == NativeExitCodes.Ok && !Killed;

    public bool NativeFault => ExitCode == NativeExitCodes.NativeFault;
}

public static class NativeProcess
{
    public const string HostExecutableName = "ffport.exe";

    public const string HostVariable = "FFPORTER_NATIVE_HOST";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static string LocateHost(string? explicitPath = null)
    {
        if (!string.IsNullOrEmpty(explicitPath))
            return explicitPath;
        string? variable = System.Environment.GetEnvironmentVariable(HostVariable);
        if (!string.IsNullOrEmpty(variable))
            return variable;
        string? process = System.Environment.ProcessPath;
        if (process != null && string.Equals(Path.GetFileName(process), HostExecutableName, StringComparison.OrdinalIgnoreCase))
            return process;
        string beside = Path.Combine(AppContext.BaseDirectory, HostExecutableName);
        if (File.Exists(beside))
            return beside;
        throw new FileNotFoundException($"{HostExecutableName} was not found beside {AppContext.BaseDirectory}; native tasks run in that child process (or set {HostVariable})", beside);
    }

    public static NativeProcessResult Run(string task, IEnumerable<string> arguments, NativeProcessOptions? options = null)
    {
        options ??= new NativeProcessOptions();
        string host = LocateHost(options.HostExecutable);
        string[] commandLine = [NativeChild.Verb, task, .. arguments];
        int attempts = Math.Max(1, options.MaxAttempts);
        for (int attempt = 1; ; attempt++)
        {
            NativeProcessResult result = RunOnce(host, commandLine, options, attempt);
            if (result.ExitCode != NativeExitCodes.AddressSpaceBusy || result.Killed || attempt >= attempts)
                return result;
        }
    }

    private static NativeProcessResult RunOnce(string host, string[] commandLine, NativeProcessOptions options, int attempt)
    {
        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            WorkingDirectory = options.WorkingDirectory ?? System.Environment.CurrentDirectory,
        };
        foreach (string argument in commandLine)
            start.ArgumentList.Add(argument);
        if (options.Environment != null)
        {
            foreach ((string key, string? value) in options.Environment)
            {
                if (value == null)
                    start.Environment.Remove(key);
                else
                    start.Environment[key] = value;
            }
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, line) => Collect(line.Data, stdout, options.OnOutputLine);
        process.ErrorDataReceived += (_, line) => Collect(line.Data, stderr, options.OnErrorLine);
        var clock = Stopwatch.StartNew();
        if (!process.Start())
            throw new NativeHostException($"Could not start {host}");
        ChildJob.Assign(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        bool exited = Wait(process, options);
        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        }
        process.WaitForExit();
        int code = exited ? process.ExitCode : -1;
        return new NativeProcessResult(code, Text(stdout), Text(stderr), clock.Elapsed, attempt, !exited);
    }

    private static bool Wait(Process process, NativeProcessOptions options)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(options.Cancellation);
        if (options.Timeout is TimeSpan timeout)
            stop.CancelAfter(timeout);
        try
        {
            process.WaitForExitAsync(stop.Token).GetAwaiter().GetResult();
            return true;
        }
        catch (OperationCanceledException)
        {
            return process.HasExited;
        }
    }

    private static void Collect(string? line, StringBuilder text, Action<string>? callback)
    {
        if (line == null)
            return;
        lock (text)
            text.Append(line).Append('\n');
        callback?.Invoke(line);
    }

    private static string Text(StringBuilder text)
    {
        lock (text)
            return text.ToString();
    }

    private static unsafe class ChildJob
    {
        private static readonly Lazy<nint> Job = new(Create);

        public static void Assign(Process process)
        {
            nint job = Job.Value;
            if (job != 0)
                Kernel32.AssignProcessToJobObject(job, process.Handle);
        }

        private static nint Create()
        {
            nint job = Kernel32.CreateJobObject(0, null);
            if (job == 0)
                return 0;
            byte* information = stackalloc byte[144];
            new Span<byte>(information, 144).Clear();
            *(uint*)(information + 16) = Kernel32.JobObjectLimitKillOnJobClose | Kernel32.JobObjectLimitDieOnUnhandledException;
            if (!Kernel32.SetInformationJobObject(job, Kernel32.JobObjectExtendedLimitInformation, information, 144))
            {
                Kernel32.CloseHandle(job);
                return 0;
            }
            return job;
        }
    }
}
