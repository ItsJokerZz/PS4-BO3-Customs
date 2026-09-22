using System.Runtime.InteropServices;
using System.Text;
using FFPorter.Core.Common.Python;

namespace FFPorter.Core.Common.Tools;

public sealed class ToolLog : IDisposable
{
    private const uint GenericWrite = 0x40000000;
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x1, FileShareWrite = 0x2;
    private const uint CreateAlways = 2, OpenExisting = 3;
    private const uint StartfUseStdHandles = 0x100;
    private const uint CreateNoWindow = 0x08000000;
    private const uint Infinite = 0xFFFFFFFF;

    private readonly string _path;
    private readonly string _temporary;
    private IntPtr _handle;
    private bool _published;

    private ToolLog(string path, string temporary, IntPtr handle)
    {
        _path = path;
        _temporary = temporary;
        _handle = handle;
    }

    public string Path => _path;

    public static ToolLog Create(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string temporary = $"{path}.{Guid.NewGuid():N}"[..Math.Min(path.Length + 9, path.Length + 9)] + ".tmp";
        var security = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = true,
        };
        IntPtr handle = CreateFileW(temporary, GenericWrite | GenericRead, FileShareRead | FileShareWrite,
            ref security, CreateAlways, 0, IntPtr.Zero);
        if (handle == new IntPtr(-1))
            throw PyOSError.FromWinError(Marshal.GetLastPInvokeError(), temporary);
        return new ToolLog(path, temporary, handle);
    }

    public int Run(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, bool check = false,
        string? workingDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);

        var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), InheritHandle = true };
        IntPtr input = CreateFileW("NUL", GenericRead, FileShareRead | FileShareWrite, ref security, OpenExisting, 0, IntPtr.Zero);
        if (input == new IntPtr(-1))
            throw PyOSError.FromWinError(Marshal.GetLastPInvokeError(), "NUL");
        try
        {
            var startup = new StartupInfo
            {
                Size = Marshal.SizeOf<StartupInfo>(),
                Flags = StartfUseStdHandles,
                StdInput = input,
                StdOutput = _handle,
                StdError = _handle,
            };
            string commandLine = CommandLine(executable, arguments);
            if (!CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, true, CreateNoWindow, IntPtr.Zero,
                workingDirectory, ref startup, out ProcessInformation created))
            {
                throw PyOSError.FromWinError(Marshal.GetLastPInvokeError(), executable);
            }
            try
            {
                uint milliseconds = timeout == Timeout.InfiniteTimeSpan ? Infinite : (uint)timeout.TotalMilliseconds;
                if (WaitForSingleObject(created.Process, milliseconds) != 0)
                {
                    TerminateProcess(created.Process, 1);
                    WaitForSingleObject(created.Process, 5000);
                    throw new ToolFailedException(
                        $"Command '{Repr(executable, arguments)}' timed out after {Seconds(timeout)} seconds");
                }
                GetExitCodeProcess(created.Process, out uint code);
                int exit = unchecked((int)code);
                if (check && exit != 0)
                {
                    throw new ToolFailedException(
                        $"Command '{Repr(executable, arguments)}' returned non-zero exit status {exit}.");
                }
                return exit;
            }
            finally
            {
                CloseHandle(created.Thread);
                CloseHandle(created.Process);
            }
        }
        finally
        {
            CloseHandle(input);
        }
    }

    public void Commit()
    {
        if (_published)
            return;
        Close();
        try
        {
            PyOs.Replace(_temporary, _path);
        }
        catch (Exception locked) when (locked is IOException or PyOSError or UnauthorizedAccessException)
        {
            File.Copy(_temporary, _path, overwrite: true);
            try
            {
                File.Delete(_temporary);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }
        }
        _published = true;
    }

    public void Dispose()
    {
        if (_published)
            return;
        try
        {
            Commit();
        }
        catch (Exception failure) when (failure is IOException or PyOSError or UnauthorizedAccessException)
        {
            Close();
            try
            {
                File.Delete(_temporary);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public static string Repr(string executable, IReadOnlyList<string> arguments) =>
        "[" + string.Join(", ", ((string[])[executable, .. arguments]).Select(PyText.Repr)) + "]";

    private static string Seconds(TimeSpan timeout) =>
        PyJson.FloatRepr(timeout.TotalSeconds) is string text && text.EndsWith(".0", StringComparison.Ordinal)
            ? text[..^2] : PyJson.FloatRepr(timeout.TotalSeconds);

    public static string CommandLine(string executable, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        var line = new StringBuilder();
        foreach (string argument in (string[])[executable, .. arguments])
        {
            if (line.Length > 0)
                line.Append(' ');
            Quote(line, argument);
        }
        return line.ToString();
    }

    private static void Quote(StringBuilder line, string argument)
    {
        bool needed = argument.Length == 0 || argument.AsSpan().ContainsAny(' ', '\t', '"');
        if (!needed)
        {
            line.Append(argument);
            return;
        }
        line.Append('"');
        int backslashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            if (c == '"')
            {
                line.Append('\\', backslashes * 2 + 1);
                backslashes = 0;
            }
            else if (backslashes > 0)
            {
                line.Append('\\', backslashes);
                backslashes = 0;
            }
            line.Append(c);
        }
        line.Append('\\', backslashes * 2);
        line.Append('"');
    }

    private void Close()
    {
        if (_handle == IntPtr.Zero)
            return;
        CloseHandle(_handle);
        _handle = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr Descriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute;
        public uint Flags;
        public short ShowWindow;
        public short Reserved2Length;
        public IntPtr Reserved2;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string name, uint access, uint share, ref SecurityAttributes security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(string? application, string commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string? currentDirectory,
        ref StartupInfo startup, out ProcessInformation information);

    [DllImport("kernel32", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint code);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint code);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}

