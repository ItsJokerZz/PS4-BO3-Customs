using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace FFPorter.Core.Common.Python;

public static class AtomicFile
{
    public static void Write(string path, ReadOnlySpan<byte> data)
    {
        using AtomicFileWriter file = Create(path, createFolder: true);
        file.Write(data);
        file.Commit();
    }

    public static void WriteBytes(string path, ReadOnlySpan<byte> data)
    {
        using AtomicFileWriter file = Create(path, createFolder: false);
        file.Write(data);
        file.Commit();
    }

    public static AtomicFileWriter Create(string path, bool createFolder = true) => new(path, createFolder);
}

public sealed class AtomicFileWriter : IDisposable
{
    private readonly string _path;
    private readonly string _temporary;
    private readonly bool _writeBytes;
    private FileStream? _stream;
    private bool _committed;

    internal AtomicFileWriter(string path, bool createFolder)
    {
        _path = PyPath.PathlibString(path);
        _writeBytes = !createFolder;
        string parent = PyPath.Parent(_path);
        if (createFolder)
            PyOs.MakeDirectories(parent);

        string folder = PyOs.AbsPath(parent);
        string prefix = PyPath.Name(_path) + ".";
        while (true)
        {
            string candidate = PyPath.Join(folder, prefix + RandomSuffix() + ".tmp");
            SafeFileHandle handle = Win32.Open(candidate, Win32.GenericRead | Win32.GenericWrite, Win32.FileShareRead | Win32.FileShareWrite,
                Win32.CreateNew, Win32.FileAttributeNormal, out int error);
            if (!handle.IsInvalid)
            {
                _temporary = candidate;
                _stream = new FileStream(handle, FileAccess.Write, 1 << 20);
                break;
            }
            handle.Dispose();
            PyOSError failure = PyOSError.FromCrt(error, _writeBytes ? _path : candidate);
            if (failure.IsFileExists)
                continue;
            throw failure;
        }
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        FileStream stream = _stream ?? throw new InvalidOperationException("The file was already committed or discarded.");
        try
        {
            stream.WriteInChunks(data);
        }
        catch (Exception error) when (PyOSError.IsFileError(error))
        {
            throw PyOSError.From(error, null);
        }
    }

    public void Commit()
    {
        FileStream stream = _stream ?? throw new InvalidOperationException("The file was already committed or discarded.");
        _stream = null;
        try
        {
            stream.Dispose();
        }
        catch (Exception error) when (PyOSError.IsFileError(error))
        {
            throw PyOSError.From(error, null);
        }
        if (_writeBytes)
        {
            if (!Win32.MoveFileEx(_temporary, _path, Win32.MoveFileReplaceExisting))
                throw PyOSError.FromCrt(Marshal.GetLastPInvokeError(), _path);
        }
        else
        {
            PyOs.Replace(_temporary, _path);
        }
        _committed = true;
    }

    public void Dispose()
    {
        if (_stream != null)
        {
            try
            {
                _stream.Dispose();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
            _stream = null;
        }
        if (!_committed)
        {
            try
            {
                File.Delete(_temporary);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string RandomSuffix()
    {
        const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789_";
        Span<char> chars = stackalloc char[8];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }
}
