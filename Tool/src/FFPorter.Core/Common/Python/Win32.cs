using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FFPorter.Core.Common.Python;

internal static partial class Win32
{
    public const int ErrorInvalidFunction = 1;
    public const int ErrorFileNotFound = 2;
    public const int ErrorPathNotFound = 3;
    public const int ErrorAccessDenied = 5;
    public const int ErrorNotReady = 21;
    public const int ErrorSharingViolation = 32;
    public const int ErrorNotSupported = 50;
    public const int ErrorBadNetName = 67;
    public const int ErrorInvalidParameter = 87;
    public const int ErrorInsufficientBuffer = 122;
    public const int ErrorCantAccessFile = 1920;
    public const int ErrorCantResolveFilename = 1921;

    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint FileReadAttributes = 0x80;
    public const uint FileShareRead = 0x1;
    public const uint FileShareWrite = 0x2;
    public const uint CreateNew = 1;
    public const uint OpenExisting = 3;
    public const uint FileAttributeNormal = 0x80;
    public const uint FileAttributeDirectory = 0x10;
    public const uint FileAttributeReparsePoint = 0x400;
    public const uint FileFlagBackupSemantics = 0x02000000;
    public const uint FileFlagOpenReparsePoint = 0x00200000;
    public const uint MoveFileReplaceExisting = 0x1;
    public const uint FsctlGetReparsePoint = 0x000900A8;
    public const uint IoReparseTagMountPoint = 0xA0000003;
    public const uint IoReparseTagSymlink = 0xA000000C;
    public const uint FileTypeDisk = 1;
    public const uint FileTypeChar = 2;
    public const uint FileTypePipe = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerialNumber;
        public uint SizeHigh, SizeLow;
        public uint NumberOfLinks;
        public uint IndexHigh, IndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct FindData
    {
        public uint FileAttributes;
        public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint SizeHigh, SizeLow;
        public uint Reserved0;
        public uint Reserved1;
        public fixed char FileName[260];
        public fixed char AlternateFileName[14];
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    public static unsafe partial uint GetFinalPathNameByHandle(SafeFileHandle file, char* filePath, uint filePathLength, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, void* inBuffer, uint inBufferSize,
        void* outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateDirectoryW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateDirectory(string pathName, IntPtr securityAttributes);

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MoveFileEx(string existingFileName, string newFileName, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFullPathNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static unsafe partial uint GetFullPathName(string fileName, uint bufferLength, char* buffer, IntPtr filePart);

    [LibraryImport("kernel32.dll", EntryPoint = "FormatMessageW", SetLastError = true)]
    private static unsafe partial uint FormatMessageW(uint flags, IntPtr source, uint messageId, uint languageId, char* buffer,
        uint size, IntPtr arguments);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint GetFileType(SafeFileHandle file);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr FindFirstFile(string fileName, out FindData findData);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FindClose(IntPtr findFile);

    public static readonly IntPtr InvalidHandleValue = new(-1);

    public static SafeFileHandle Open(string path, uint access, uint share, uint disposition, uint flags, out int error)
    {
        SafeFileHandle handle = CreateFile(path, access, share, IntPtr.Zero, disposition, flags, IntPtr.Zero);
        error = handle.IsInvalid ? Marshal.GetLastPInvokeError() : 0;
        return handle;
    }

    public static unsafe string FormatMessage(int code)
    {
        const uint FromSystem = 0x1000;
        const uint IgnoreInserts = 0x200;
        const uint DefaultLanguage = 0x0400;
        char[] buffer = new char[32768];
        uint length;
        fixed (char* chars = buffer)
            length = FormatMessageW(FromSystem | IgnoreInserts, IntPtr.Zero, (uint)code, DefaultLanguage, chars, (uint)buffer.Length, IntPtr.Zero);
        if (length == 0)
            return $"Windows Error 0x{code:x}";
        while (length > 0 && (buffer[length - 1] <= ' ' || buffer[length - 1] == '.'))
            length--;
        return new string(buffer, 0, (int)length);
    }
}
