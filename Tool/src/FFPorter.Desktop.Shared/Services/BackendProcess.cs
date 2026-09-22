using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FFPorter.Desktop.Services;

public sealed class BackendProcess
{
    private Process? _process;

    public bool IsRunning => _process != null;

    public async Task<int> RunAsync(IEnumerable<string> arguments, string workingDirectory, Action<string> onOutput, Action<string> onError,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory,
        };
        start.ArgumentList.Add("--backend");
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        foreach ((string name, string value) in environment ?? new Dictionary<string, string>())
            start.Environment[name] = value;
        using var process = new Process { StartInfo = start };
        _process = process;
        try
        {
            process.Start();
            Task output = Pump(process.StandardOutput, onOutput), error = Pump(process.StandardError, onError);
            await process.WaitForExitAsync();
            await Task.WhenAll(output, error);
            return process.ExitCode;
        }
        finally
        {
            _process = null;
        }
    }

    public void Cancel()
    {
        try
        {
            if (_process is { HasExited: false } process)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private static async Task Pump(StreamReader reader, Action<string> onLine)
    {
        while (await reader.ReadLineAsync() is { } line)
            onLine(line);
    }
}
