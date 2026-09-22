using System.Runtime.InteropServices;
using System.Text;
using FFPorter.Core.Common.Python;
using Microsoft.Win32.SafeHandles;

namespace FFPorter.Core.Common.Native;

public sealed class JsonlLog : IDisposable
{
    private const int BufferSize = 1 << 16;

    private static readonly List<JsonlLog> OpenLogs = [];

    private readonly FileStream? _stream;
    private readonly AtomicFileWriter? _atomic;
    private readonly byte[] _buffer = new byte[BufferSize];
    private readonly StringBuilder _text = new();
    private int _used;
    private bool _disposed;

    private JsonlLog(string path, FileStream? stream, AtomicFileWriter? atomic)
    {
        Path = path;
        _stream = stream;
        _atomic = atomic;
    }

    public string Path { get; }

    public bool IsAtomic => _atomic != null;

    public bool AutoFlush { get; set; }

    public long Records { get; private set; }

    public static JsonlLog Open(string path)
    {
        DeleteExisting(path);
        SafeFileHandle handle = Win32.Open(path, Win32.GenericWrite, Win32.FileShareRead, Win32.CreateNew, Win32.FileAttributeNormal, out int openError);
        if (handle.IsInvalid)
            throw PyOSError.FromCrt(openError, path);
        return Register(new JsonlLog(path, new FileStream(handle, FileAccess.Write, bufferSize: 0), null));
    }

    public static JsonlLog OpenAtomic(string path)
    {
        DeleteExisting(path);
        DeleteStaleTemporaries(path);
        return Register(new JsonlLog(path, null, AtomicFile.Create(path, createFolder: false)));
    }

    private static void DeleteStaleTemporaries(string path)
    {
        string name = PyPath.Name(path);
        if (name.Length == 0)
            return;
        string folder = PyPath.Parent(path);
        List<string> candidates;
        try
        {
            candidates = [.. Directory.EnumerateFiles(folder.Length == 0 ? "." : folder, name + ".*.tmp")];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return;
        }
        foreach (string candidate in candidates)
        {
            string file = System.IO.Path.GetFileName(candidate);
            if (file.Length != name.Length + 13 || !file.StartsWith(name + ".", StringComparison.Ordinal))
                continue;
            string random = file[(name.Length + 1)..^4];
            if (random.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
                Kernel32.DeleteFile(candidate);
        }
    }

    private static void DeleteExisting(string path)
    {
        if (!Kernel32.DeleteFile(path))
        {
            int error = Marshal.GetLastPInvokeError();
            if (error is not (Win32.ErrorFileNotFound or Win32.ErrorPathNotFound))
                throw PyOSError.FromCrt(error, path);
        }
    }

    private static JsonlLog Register(JsonlLog log)
    {
        lock (OpenLogs)
            OpenLogs.Add(log);
        return log;
    }

    public static void FlushAll()
    {
        foreach (JsonlLog log in Snapshot())
        {
            try
            {
                log.Flush();
            }
            catch (Exception)
            {
            }
        }
    }

    public static void CloseAll()
    {
        foreach (JsonlLog log in Snapshot())
        {
            try
            {
                log.Dispose();
            }
            catch (Exception)
            {
            }
        }
    }

    private static JsonlLog[] Snapshot()
    {
        lock (OpenLogs)
            return [.. OpenLogs];
    }

    public void Write(JsonMap record)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        StringBuilder text = _text;
        text.Clear();
        PyJson.DumpsTo(text, record, indent: null);
        text.Append("\r\n");
        foreach (ReadOnlyMemory<char> chunk in text.GetChunks())
        {
            ReadOnlySpan<char> chars = chunk.Span;
            while (!chars.IsEmpty)
            {
                if (_used == BufferSize)
                    Flush();
                int take = Math.Min(chars.Length, BufferSize - _used);
                if (System.Text.Ascii.FromUtf16(chars[..take], _buffer.AsSpan(_used, take), out int written) != System.Buffers.OperationStatus.Done)
                {
                    _used += written;
                    WriteRestAsUtf8(chars[written..], text, chunk);
                    Records++;
                    if (AutoFlush)
                        Flush();
                    return;
                }
                _used += written;
                chars = chars[take..];
            }
        }
        Records++;
        if (AutoFlush)
            Flush();
    }

    private void WriteRestAsUtf8(ReadOnlySpan<char> rest, StringBuilder text, ReadOnlyMemory<char> current)
    {
        var tail = new StringBuilder().Append(rest);
        bool after = false;
        foreach (ReadOnlyMemory<char> chunk in text.GetChunks())
        {
            if (after)
                tail.Append(chunk.Span);
            else if (chunk.Equals(current))
                after = true;
        }
        Flush();
        WriteOut(Encoding.UTF8.GetBytes(tail.ToString()));
    }

    public void WriteLine(string json)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int most = Encoding.UTF8.GetMaxByteCount(json.Length) + 2;
        if (most > BufferSize - _used)
            Flush();
        if (most > BufferSize)
        {
            WriteOut(Encoding.UTF8.GetBytes(json + "\r\n"));
        }
        else
        {
            _used += Encoding.UTF8.GetBytes(json, _buffer.AsSpan(_used));
            _buffer[_used++] = (byte)'\r';
            _buffer[_used++] = (byte)'\n';
        }
        Records++;
        if (AutoFlush)
            Flush();
    }

    public void Flush()
    {
        if (_disposed || _used == 0)
            return;
        WriteOut(_buffer.AsSpan(0, _used));
        _used = 0;
    }

    private void WriteOut(ReadOnlySpan<byte> bytes)
    {
        if (_atomic != null)
            _atomic.Write(bytes);
        else
            _stream!.Write(bytes);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        lock (OpenLogs)
            OpenLogs.Remove(this);
        try
        {
            Flush();
            _disposed = true;
            if (_atomic != null)
                _atomic.Commit();
        }
        finally
        {
            _disposed = true;
            _atomic?.Dispose();
            _stream?.Dispose();
        }
    }
}
