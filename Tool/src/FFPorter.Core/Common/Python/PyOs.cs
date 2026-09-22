using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FFPorter.Core.Common.Python;

public static class PyOs
{
    public static void Mkdir(string path)
    {
        if (!Win32.CreateDirectory(path, IntPtr.Zero))
            throw PyOSError.FromWinError(Marshal.GetLastPInvokeError(), path);
    }

    public static void MakeDirectories(string path) => MakeDirectory(PyPath.PathlibString(path), parents: true);

    private static void MakeDirectory(string path, bool parents)
    {
        try
        {
            Mkdir(path);
        }
        catch (PyOSError error) when (error.IsFileNotFound)
        {
            if (!parents || !PyPath.HasParts(path))
                throw;
            MakeDirectory(PyPath.Parent(path), parents: true);
            MakeDirectory(path, parents: false);
        }
        catch (PyOSError) when (IsDir(path))
        {
        }
    }

    public static bool Exists(string path) => Stat(path) != Kind.Missing;

    public static bool IsDir(string path) => Stat(path) == Kind.Directory;

    public static bool IsFile(string path) => Stat(path) == Kind.Regular;

    public static long StatSize(string path)
    {
        SafeFileHandle handle = Win32.Open(path, Win32.FileReadAttributes, 0, Win32.OpenExisting, Win32.FileFlagBackupSemantics, out int error);
        try
        {
            if (handle.IsInvalid)
            {
                switch (error)
                {
                    case Win32.ErrorAccessDenied or Win32.ErrorSharingViolation:
                        return SizeFromDirectoryEntry(path, error);
                    case Win32.ErrorInvalidParameter:
                        handle.Dispose();
                        handle = Win32.Open(path, Win32.FileReadAttributes | Win32.GenericRead, Win32.FileShareRead | Win32.FileShareWrite,
                            Win32.OpenExisting, Win32.FileFlagBackupSemantics, out _);
                        break;
                    case Win32.ErrorCantAccessFile:
                        handle.Dispose();
                        handle = Win32.Open(path, Win32.FileReadAttributes, 0, Win32.OpenExisting,
                            Win32.FileFlagBackupSemantics | Win32.FileFlagOpenReparsePoint, out _);
                        break;
                }
                if (handle.IsInvalid)
                    throw PyOSError.FromWinError(error, path);
            }
            uint type = Win32.GetFileType(handle);
            if (type != Win32.FileTypeDisk)
            {
                int typeError = Marshal.GetLastPInvokeError();
                if (type == 0 && typeError != 0)
                    throw PyOSError.FromWinError(typeError, path);
                return 0;
            }
            if (!Win32.GetFileInformationByHandle(handle, out Win32.ByHandleFileInformation info))
            {
                int infoError = Marshal.GetLastPInvokeError();
                if (infoError is Win32.ErrorInvalidParameter or Win32.ErrorInvalidFunction or Win32.ErrorNotSupported)
                    return 0;
                throw PyOSError.FromWinError(infoError, path);
            }
            return ((long)info.SizeHigh << 32) | info.SizeLow;
        }
        finally
        {
            handle.Dispose();
        }
    }

    private static long SizeFromDirectoryEntry(string path, int openError)
    {
        IntPtr find = Win32.FindFirstFile(path, out Win32.FindData data);
        if (find == Win32.InvalidHandleValue)
        {
            int findError = Marshal.GetLastPInvokeError();
            bool missing = findError is Win32.ErrorFileNotFound or Win32.ErrorPathNotFound or Win32.ErrorNotReady or Win32.ErrorBadNetName;
            throw PyOSError.FromWinError(missing ? findError : openError, path);
        }
        Win32.FindClose(find);
        if ((data.FileAttributes & Win32.FileAttributeReparsePoint) != 0)
            throw PyOSError.FromWinError(openError, path);
        return ((long)data.SizeHigh << 32) | data.SizeLow;
    }

    public static int StatOpenError(string path)
    {
        using SafeFileHandle handle = Win32.Open(path, Win32.FileReadAttributes, 0, Win32.OpenExisting, Win32.FileFlagBackupSemantics, out int error);
        return error;
    }

    public static void Replace(string source, string destination)
    {
        if (!Win32.MoveFileEx(source, destination, Win32.MoveFileReplaceExisting))
            throw PyOSError.FromWinError(Marshal.GetLastPInvokeError(), source, destination);
    }

    public static string AbsPath(string path)
    {
        if (TryGetFullPathName(PyPath.NormPath(path), out string full))
            return full;
        if (!PyPath.IsAbs(path))
        {
            (string drive, string root, string rest) = PyPath.SplitRoot(path);
            if (drive.Length > 0 || root.Length > 0)
                path = TryGetFullPathName(drive + root, out string prefix) ? PyPath.Join(prefix, rest) : drive + @"\" + rest;
            else
                path = PyPath.Join(Directory.GetCurrentDirectory(), path);
        }
        return PyPath.NormPath(path);
    }

    private static unsafe bool TryGetFullPathName(string path, out string full)
    {
        full = "";
        char[] buffer = new char[260];
        while (true)
        {
            uint length;
            fixed (char* chars = buffer)
                length = Win32.GetFullPathName(path, (uint)buffer.Length, chars, IntPtr.Zero);
            if (length == 0)
                return false;
            if (length < buffer.Length)
            {
                full = new string(buffer, 0, (int)length);
                return true;
            }
            buffer = new char[length];
        }
    }

    private enum Kind
    {
        Missing,
        Directory,
        Regular,
        Other,
    }

    private static Kind Stat(string path)
    {
        SafeFileHandle handle = Win32.Open(path, Win32.FileReadAttributes, 0, Win32.OpenExisting, Win32.FileFlagBackupSemantics, out int error);
        try
        {
            if (handle.IsInvalid)
            {
                switch (error)
                {
                    case Win32.ErrorAccessDenied or Win32.ErrorSharingViolation:
                        return FromDirectoryEntry(path);
                    case Win32.ErrorInvalidParameter:
                        handle.Dispose();
                        handle = Win32.Open(path, Win32.FileReadAttributes | Win32.GenericRead, Win32.FileShareRead | Win32.FileShareWrite,
                            Win32.OpenExisting, Win32.FileFlagBackupSemantics, out _);
                        break;
                    case Win32.ErrorCantAccessFile:
                        handle.Dispose();
                        handle = Win32.Open(path, Win32.FileReadAttributes, 0, Win32.OpenExisting,
                            Win32.FileFlagBackupSemantics | Win32.FileFlagOpenReparsePoint, out _);
                        break;
                    default:
                        return Kind.Missing;
                }
                if (handle.IsInvalid)
                    return Kind.Missing;
            }
            uint type = Win32.GetFileType(handle);
            if (type != Win32.FileTypeDisk)
                return type == 0 && Marshal.GetLastPInvokeError() != 0 ? Kind.Missing : Kind.Other;
            if (!Win32.GetFileInformationByHandle(handle, out Win32.ByHandleFileInformation info))
                return Kind.Missing;
            return (info.FileAttributes & Win32.FileAttributeDirectory) != 0 ? Kind.Directory : Kind.Regular;
        }
        finally
        {
            handle.Dispose();
        }
    }

    private static Kind FromDirectoryEntry(string path)
    {
        IntPtr find = Win32.FindFirstFile(path, out Win32.FindData data);
        if (find == Win32.InvalidHandleValue)
            return Kind.Missing;
        Win32.FindClose(find);
        if ((data.FileAttributes & Win32.FileAttributeReparsePoint) != 0)
            return Kind.Missing;
        return (data.FileAttributes & Win32.FileAttributeDirectory) != 0 ? Kind.Directory : Kind.Regular;
    }
}
