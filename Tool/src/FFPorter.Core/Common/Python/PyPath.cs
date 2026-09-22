using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FFPorter.Core.Common.Python;

public static class PyPath
{
    private const char Sep = '\\';
    private const char AltSep = '/';

    private static bool IsSep(char c) => c is Sep or AltSep;


    public static string PathlibString(string path)
    {
        (string drive, string root, List<string> tail) = ParsePath(path);
        return FormatOrDot(drive, root, tail);
    }

    public static string Parent(string path)
    {
        (string drive, string root, List<string> tail) = ParsePath(path);
        if (tail.Count > 0)
            tail.RemoveAt(tail.Count - 1);
        return FormatOrDot(drive, root, tail);
    }

    public static string Name(string path)
    {
        List<string> tail = ParsePath(path).Tail;
        return tail.Count == 0 ? "" : tail[^1];
    }

    public static string Stem(string path)
    {
        string name = Name(path);
        int dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? name[..dot] : name;
    }

    public static bool HasParts(string path) => ParsePath(path).Tail.Count > 0;

    public static bool SamePath(string a, string b) =>
        string.Equals(PyText.Lower(PathlibString(a)), PyText.Lower(PathlibString(b)), StringComparison.Ordinal);

    public static string Resolve(string path)
    {
        string resolved = PathlibString(RealPath(PathlibString(path)));
        if (PyOs.StatOpenError(resolved) == Win32.ErrorCantResolveFilename)
            throw new PyRuntimeError($"Symlink loop from {Repr(resolved)}");
        return resolved;
    }

    private static (string Drive, string Root, List<string> Tail) ParsePath(string path)
    {
        if (path.Length == 0)
            return ("", "", []);
        path = path.Replace(AltSep, Sep);
        (string drive, string root, string rest) = SplitRoot(path);
        if (root.Length == 0 && drive.StartsWith(Sep) && !drive.EndsWith(Sep))
        {
            string[] driveParts = drive.Split(Sep);
            if (driveParts.Length == 4 && !"?.".Contains(driveParts[2], StringComparison.Ordinal))
                root = @"\";
            else if (driveParts.Length == 6)
                root = @"\";
        }
        List<string> tail = rest.Split(Sep).Where(part => part.Length > 0 && part != ".").ToList();
        return (drive, root, tail);
    }

    private static string FormatOrDot(string drive, string root, List<string> tail)
    {
        string text;
        if (drive.Length > 0 || root.Length > 0)
            text = drive + root + string.Join(Sep, tail);
        else if (tail.Count > 0 && SplitRoot(tail[0]).Drive.Length > 0)
            text = "." + Sep + string.Join(Sep, tail);
        else
            text = string.Join(Sep, tail);
        return text.Length == 0 ? "." : text;
    }


    public static (string Drive, string Root, string Tail) SplitRoot(string p)
    {
        string normp = p.Replace(AltSep, Sep);
        if (normp.StartsWith(Sep))
        {
            if (normp.Length > 1 && normp[1] == Sep)
            {
                int start = normp.Length >= 8 && string.Equals(normp[..8], @"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? 8 : 2;
                int index = normp.IndexOf(Sep, start);
                if (index == -1)
                    return (p, "", "");
                int index2 = normp.IndexOf(Sep, index + 1);
                if (index2 == -1)
                    return (p, "", "");
                return (p[..index2], p.Substring(index2, 1), p[(index2 + 1)..]);
            }
            return ("", p[..1], p[1..]);
        }
        if (normp.Length > 1 && normp[1] == ':')
        {
            if (normp.Length > 2 && normp[2] == Sep)
                return (p[..2], p.Substring(2, 1), p[3..]);
            return (p[..2], "", p[2..]);
        }
        return ("", "", p);
    }

    public static (string Head, string Tail) Split(string p)
    {
        (string drive, string root, string rest) = SplitRoot(p);
        int i = rest.Length;
        while (i > 0 && !IsSep(rest[i - 1]))
            i--;
        return (drive + root + rest[..i].TrimEnd(Sep, AltSep), rest[i..]);
    }

    public static string Dirname(string p) => Split(p).Head;

    public static bool IsAbs(string s)
    {
        string head = (s.Length > 3 ? s[..3] : s).Replace(AltSep, Sep);
        return head.StartsWith(Sep) || (head.Length == 3 && head[1] == ':' && head[2] == Sep);
    }

    public static string Join(string path, params string[] paths)
    {
        (string resultDrive, string resultRoot, string resultPath) = SplitRoot(path);
        foreach (string p in paths)
        {
            (string pDrive, string pRoot, string pPath) = SplitRoot(p);
            if (pRoot.Length > 0)
            {
                if (pDrive.Length > 0 || resultDrive.Length == 0)
                    resultDrive = pDrive;
                resultRoot = pRoot;
                resultPath = pPath;
                continue;
            }
            if (pDrive.Length > 0 && pDrive != resultDrive)
            {
                if (PyText.Lower(pDrive) != PyText.Lower(resultDrive))
                {
                    resultDrive = pDrive;
                    resultRoot = pRoot;
                    resultPath = pPath;
                    continue;
                }
                resultDrive = pDrive;
            }
            if (resultPath.Length > 0 && !IsSep(resultPath[^1]))
                resultPath += Sep;
            resultPath += pPath;
        }
        if (resultPath.Length > 0 && resultRoot.Length == 0 && resultDrive.Length > 0 && resultDrive[^1] is not (':' or Sep or AltSep))
            return resultDrive + Sep + resultPath;
        return resultDrive + resultRoot + resultPath;
    }

    public static string NormPath(string path)
    {
        if (path.Length == 0)
            return ".";
        int n = path.Length;
        char[] buf = new char[n + 1];
        path.CopyTo(0, buf, 0, n);
        bool SepAt(int i) => i < n && IsSep(buf[i]);
        bool SepOrEnd(int i) => i >= n || IsSep(buf[i]);

        int start = 0;
        int p1 = 0;
        int p2 = 0;
        int minP2 = 0;
        char lastC = '\0';

        if (buf[0] == '.' && SepAt(1))
        {
            start = 2;
            while (SepAt(start))
                start++;
            p1 = p2 = minP2 = start;
            lastC = Sep;
        }
        else if (n > 1 && buf[1] == ':')
        {
            p1 = p2 = minP2 = 2;
            lastC = ':';
        }
        else if (SepAt(0) && SepAt(1))
        {
            int sepCount = 2;
            buf[0] = Sep;
            buf[1] = Sep;
            p1 = p2 = 2;
            for (; p1 < n && sepCount > 0; ++p1)
            {
                if (IsSep(buf[p1]))
                {
                    --sepCount;
                    buf[p2++] = lastC = Sep;
                }
                else
                {
                    buf[p2++] = lastC = buf[p1];
                }
            }
            minP2 = p2 - 1;
        }

        for (; p1 < n; ++p1)
        {
            char c = buf[p1] == AltSep ? Sep : buf[p1];
            if (lastC == Sep)
            {
                if (c == '.')
                {
                    bool sepAt1 = SepOrEnd(p1 + 1);
                    bool sepAt2 = !sepAt1 && SepOrEnd(p1 + 2);
                    if (sepAt2 && buf[p1 + 1] == '.')
                    {
                        int p3 = p2;
                        while (p3 != minP2 && buf[--p3] == Sep)
                        {
                        }
                        while (p3 != minP2 && buf[p3 - 1] != Sep)
                            --p3;
                        if (p2 == minP2 || (buf[p3] == '.' && buf[p3 + 1] == '.' && IsSep(buf[p3 + 2])))
                        {
                            buf[p2++] = '.';
                            buf[p2++] = '.';
                            lastC = '.';
                        }
                        else if (buf[p3] == Sep)
                        {
                            p2 = p3 + 1;
                        }
                        else
                        {
                            p2 = p3;
                        }
                        p1 += 1;
                    }
                    else if (!sepAt1)
                    {
                        buf[p2++] = lastC = c;
                    }
                }
                else if (c != Sep)
                {
                    buf[p2++] = lastC = c;
                }
            }
            else
            {
                buf[p2++] = lastC = c;
            }
        }

        if (p2 != minP2)
        {
            while (--p2 != minP2 && buf[p2] == Sep)
            {
            }
        }
        else
        {
            --p2;
        }
        int length = p2 - start + 1;
        return length <= 0 ? "." : new string(buf, start, length);
    }

    public static string RealPath(string path)
    {
        const string Prefix = @"\\?\";
        const string UncPrefix = @"\\?\UNC\";
        const string NewUncPrefix = @"\\";
        path = NormPath(path);
        string cwd = Directory.GetCurrentDirectory();
        if (NormCase(path) == "nul")
            return @"\\.\NUL";
        bool hadPrefix = path.StartsWith(Prefix, StringComparison.Ordinal);
        if (!hadPrefix && !IsAbs(path))
            path = Join(cwd, path);

        int initialWinError = 0;
        if (TryGetFinalPathName(path, out string final, out int error))
        {
            path = final;
        }
        else
        {
            initialWinError = error;
            path = GetFinalPathNameNonStrict(path);
        }

        if (!hadPrefix && path.StartsWith(Prefix, StringComparison.Ordinal))
        {
            string spath = path.StartsWith(UncPrefix, StringComparison.Ordinal) ? NewUncPrefix + path[UncPrefix.Length..] : path[Prefix.Length..];
            if (TryGetFinalPathName(spath, out string again, out int againError))
            {
                if (again == path)
                    path = spath;
            }
            else if (againError == initialWinError)
            {
                path = spath;
            }
        }
        return path;
    }

    private static readonly int[] NonStrictAllowed = [1, 2, 3, 5, 21, 32, 50, 53, 65, 67, 87, 123, 161, 1920, 1921];

    private static string GetFinalPathNameNonStrict(string path)
    {
        string tail = "";
        while (path.Length > 0)
        {
            if (TryGetFinalPathName(path, out string final, out int error))
                return tail.Length > 0 ? Join(final, tail) : final;
            if (!NonStrictAllowed.Contains(error))
                throw PyOSError.FromWinError(error, path);
            string? linked = ReadLinkDeep(path);
            if (linked != null && linked != path)
                return tail.Length > 0 ? Join(linked, tail) : linked;
            (string head, string name) = Split(path);
            path = head;
            if (path.Length > 0 && name.Length == 0)
                return path + tail;
            tail = tail.Length > 0 ? Join(name, tail) : name;
        }
        return tail;
    }

    private static readonly int[] ReadLinkAllowed = [1, 2, 3, 5, 21, 32, 50, 67, 87, 4390, 4392, 4393];

    private static string? ReadLinkDeep(string path)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (seen.Add(NormCase(path)))
        {
            string oldPath = path;
            (string? target, uint tag, int error) = ReadLink(path);
            if (error != 0)
            {
                if (ReadLinkAllowed.Contains(error))
                    break;
                return null;
            }
            if (target == null)
                break;
            path = target;
            if (!IsAbs(path))
            {
                if (tag != Win32.IoReparseTagSymlink)
                {
                    path = oldPath;
                    break;
                }
                path = NormPath(Join(Dirname(oldPath), path));
            }
        }
        return path;
    }

    private static unsafe bool TryGetFinalPathName(string path, out string final, out int error)
    {
        final = "";
        using SafeFileHandle handle = Win32.Open(path, 0, 0, Win32.OpenExisting, Win32.FileFlagBackupSemantics, out error);
        if (handle.IsInvalid)
            return false;
        char[] buffer = new char[260];
        while (true)
        {
            uint length;
            fixed (char* chars = buffer)
                length = Win32.GetFinalPathNameByHandle(handle, chars, (uint)buffer.Length, 0);
            if (length == 0)
            {
                error = Marshal.GetLastPInvokeError();
                return false;
            }
            if (length < buffer.Length)
            {
                final = new string(buffer, 0, (int)length);
                return true;
            }
            buffer = new char[length];
        }
    }

    private static unsafe (string? Target, uint Tag, int Error) ReadLink(string path)
    {
        using SafeFileHandle handle = Win32.Open(path, 0, 0, Win32.OpenExisting,
            Win32.FileFlagOpenReparsePoint | Win32.FileFlagBackupSemantics, out int error);
        if (handle.IsInvalid)
            return (null, 0, error);
        byte[] buffer = new byte[16 * 1024];
        bool ok;
        fixed (byte* data = buffer)
            ok = Win32.DeviceIoControl(handle, Win32.FsctlGetReparsePoint, null, 0, data, (uint)buffer.Length, out _, IntPtr.Zero);
        if (!ok)
            return (null, 0, Marshal.GetLastPInvokeError());
        uint tag = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        int pathBuffer = tag switch
        {
            Win32.IoReparseTagSymlink => 20,
            Win32.IoReparseTagMountPoint => 16,
            _ => -1,
        };
        if (pathBuffer < 0)
            return (null, tag, 0);
        int offset = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(8));
        int length = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(10));
        char[] name = MemoryMarshal.Cast<byte, char>(buffer.AsSpan(pathBuffer + offset, length)).ToArray();
        if (name.Length > 4 && name[0] == '\\' && name[1] == '?' && name[2] == '?' && name[3] == '\\')
            name[1] = '\\';
        return (new string(name), tag, 0);
    }

    private static string NormCase(string path) => path.Replace(AltSep, Sep).ToLowerInvariant();

    public static string Repr(string text) => PyText.Repr(text);
}

public sealed class PyRuntimeError(string message) : Exception($"RuntimeError: {message}");
