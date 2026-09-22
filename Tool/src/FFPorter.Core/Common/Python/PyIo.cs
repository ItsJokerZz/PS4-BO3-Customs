using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FFPorter.Core.Common.Python;

public static class PyIo
{
    public static FileStream OpenRead(string path)
    {
        SafeFileHandle handle = Win32.Open(path, Win32.GenericRead, Win32.FileShareRead | Win32.FileShareWrite,
            Win32.OpenExisting, Win32.FileAttributeNormal, out int error);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw PyOSError.FromCrt(error, path);
        }
        return new FileStream(handle, FileAccess.Read, bufferSize: 0);
    }

    public static byte[] Read(Stream stream, int count)
    {
        byte[] data = new byte[count];
        int read;
        try
        {
            read = stream.ReadAtLeast(data, count, throwOnEndOfStream: false);
        }
        catch (Exception error) when (PyOSError.IsFileError(error))
        {
            throw PyOSError.From(error, null);
        }
        return read == count ? data : data.AsSpan(0, read).ToArray();
    }

    public static byte[] ReadAllBytes(string path)
    {
        using FileStream stream = OpenRead(path);
        long length = stream.CanSeek ? stream.Length : 0;
        if (length > Array.MaxLength)
        {
            throw PyOSError.From(new IOException(
                "The file is too long. This operation is currently limited to supporting files less than 2 gigabytes in size."), path);
        }
        byte[] data = Read(stream, (int)length);
        if (data.Length < length || !stream.CanSeek)
            return data.Length < length ? data : ReadRest(stream, data);
        byte[] more = Read(stream, 1);
        return more.Length == 0 ? data : ReadRest(stream, [.. data, .. more]);
    }

    private static byte[] ReadRest(Stream stream, byte[] start)
    {
        using var all = new MemoryStream();
        all.Write(start);
        while (true)
        {
            byte[] chunk = Read(stream, 1 << 20);
            if (chunk.Length == 0)
                return all.ToArray();
            all.Write(chunk);
        }
    }

    public static string ReadAllText(string path)
    {
        byte[] bytes = ReadAllBytes(path);
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        string text = reader.ReadToEnd();
        return text.Contains('\r') ? text.Replace("\r\n", "\n").Replace('\r', '\n') : text;
    }
}
