namespace FFPorter.Core.Common.Python;

public sealed class PyOSError : IOException
{
    private const int ENOENT = 2;
    private const int EIO = 5;
    private const int E2BIG = 7;
    private const int ENOEXEC = 8;
    private const int EBADF = 9;
    private const int ECHILD = 10;
    private const int EAGAIN = 11;
    private const int ENOMEM = 12;
    private const int EACCES = 13;
    private const int EEXIST = 17;
    private const int EXDEV = 18;
    private const int ENOTDIR = 20;
    private const int EINVAL = 22;
    private const int EMFILE = 24;
    private const int ENOSPC = 28;
    private const int EPIPE = 32;
    private const int ENOTEMPTY = 41;
    private const int EILSEQ = 42;

    private PyOSError(string message, int errno, int winError, Exception? inner) : base(message, inner)
    {
        Errno = errno;
        WinError = winError;
    }

    public int Errno { get; }

    public int WinError { get; }

    public bool IsFileNotFound => Errno == ENOENT;

    public bool IsFileExists => Errno == EEXIST;

    public bool IsPermission => Errno == EACCES;

    public static PyOSError FromErrno(int errno, string? filename, Exception? inner = null)
    {
        string text = $"[Errno {errno}] {StrError(errno)}";
        return new PyOSError(filename == null ? text : $"{text}: {PyPath.Repr(filename)}", errno, 0, inner);
    }

    public static PyOSError FromCrt(int win32Error, string? filename, Exception? inner = null) =>
        FromErrno(CrtErrno(win32Error), filename, inner);

    public static PyOSError FromWinError(int win32Error, string? filename, string? filename2 = null, Exception? inner = null)
    {
        string text = $"[WinError {win32Error}] {Win32.FormatMessage(win32Error)}";
        if (filename != null)
            text += filename2 == null ? $": {PyPath.Repr(filename)}" : $": {PyPath.Repr(filename)} -> {PyPath.Repr(filename2)}";
        return new PyOSError(text, WinErrorToErrno(win32Error), win32Error, inner);
    }

    public static T Wrap<T>(string path, Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception error) when (IsFileError(error))
        {
            throw From(error, path);
        }
    }

    public static void Wrap(string path, Action operation) => Wrap(path, () =>
    {
        operation();
        return 0;
    });

    public static PyOSError From(Exception error, string? path)
    {
        if (Win32Code(error) is int code)
            return FromCrt(code, path, error);
        string text = $"[Errno {EIO}] {error.Message.TrimEnd('.')}";
        return new PyOSError(path == null ? text : $"{text}: {PyPath.Repr(path)}", EIO, 0, error);
    }

    public static int? Win32Code(Exception error)
    {
        if (((uint)error.HResult & 0xFFFF0000) == 0x80070000)
            return error.HResult & 0xFFFF;
        return error switch
        {
            FileNotFoundException => 2,
            DirectoryNotFoundException => 3,
            UnauthorizedAccessException => 5,
            PathTooLongException => 206,
            _ => null,
        };
    }

    public static bool IsFileError(Exception error) => error is IOException or UnauthorizedAccessException && error is not PyOSError;

    public static int CrtErrno(int win32Error) => win32Error switch
    {
        1 => EINVAL,
        2 or 3 => ENOENT,
        4 => EMFILE,
        5 => EACCES,
        6 => EBADF,
        7 or 8 or 9 => ENOMEM,
        10 => E2BIG,
        11 => ENOEXEC,
        12 or 13 => EINVAL,
        15 => ENOENT,
        16 => EACCES,
        17 => EXDEV,
        18 => ENOENT,
        33 => EACCES,
        53 => ENOENT,
        65 => EACCES,
        67 => ENOENT,
        80 => EEXIST,
        82 or 83 => EACCES,
        87 => EINVAL,
        89 => EAGAIN,
        108 => EACCES,
        109 => EPIPE,
        112 => ENOSPC,
        114 => EBADF,
        128 or 129 => ECHILD,
        130 => EBADF,
        131 => EINVAL,
        132 => EACCES,
        145 => ENOTEMPTY,
        158 => EACCES,
        161 => ENOENT,
        164 => EAGAIN,
        167 => EACCES,
        183 => EEXIST,
        206 => ENOENT,
        215 => EAGAIN,
        1816 => ENOMEM,
        >= 19 and <= 36 => EACCES,
        >= 188 and <= 202 => ENOEXEC,
        _ => EINVAL,
    };

    public static int WinErrorToErrno(int winError) => winError switch
    {
        2 or 3 or 15 or 18 or 53 or 67 or 161 or 206 => ENOENT,
        10 => E2BIG,
        11 or (>= 188 and <= 202) => ENOEXEC,
        6 or 114 or 130 => EBADF,
        128 or 129 => ECHILD,
        89 or 164 or 215 => EAGAIN,
        7 or 8 or 9 or 1816 => ENOMEM,
        5 or 16 or (>= 19 and <= 36) or 65 or 82 or 83 or 108 or 132 or 158 or 167 => EACCES,
        80 or 183 => EEXIST,
        17 => EXDEV,
        267 => ENOTDIR,
        4 => EMFILE,
        112 => ENOSPC,
        109 or 232 => EPIPE,
        145 => ENOTEMPTY,
        1113 => EILSEQ,
        _ => EINVAL,
    };

    public static string StrError(int errno) => errno switch
    {
        ENOENT => "No such file or directory",
        EIO => "Input/output error",
        E2BIG => "Arg list too long",
        ENOEXEC => "Exec format error",
        EBADF => "Bad file descriptor",
        ECHILD => "No child processes",
        EAGAIN => "Resource temporarily unavailable",
        ENOMEM => "Not enough space",
        EACCES => "Permission denied",
        EEXIST => "File exists",
        EXDEV => "Improper link",
        ENOTDIR => "Not a directory",
        EINVAL => "Invalid argument",
        EMFILE => "Too many open files",
        ENOSPC => "No space left on device",
        EPIPE => "Broken pipe",
        ENOTEMPTY => "Directory not empty",
        EILSEQ => "Illegal byte sequence",
        _ => "Unknown error",
    };
}
