using System.Runtime.InteropServices;
using FFPorter.Core.Common.Hashing;
using FFPorter.Core.Common.Python;

namespace FFPorter.Core.Common.Audio;

[StructLayout(LayoutKind.Sequential)]
internal struct SfInfo
{
    public long Frames;
    public int SampleRate;
    public int Channels;
    public int Format;
    public int Sections;
    public int Seekable;
}

internal sealed unsafe class SndFile
{
    public const int FormatMp3 = 0x230000 | 0x0082;

    public const int ModeRead = 0x10;
    public const int ModeWrite = 0x20;

    public const int SetCompressionLevel = 0x1301;
    public const int SetBitrateMode = 0x1305;
    public const int BitrateModeVariable = 2;

    public const string WorkspaceRelativePath = "tools/audio_deps/soundfile/_soundfile_data/libsndfile_x64.dll";

    public const string ShippedFileName = "libsndfile_x64.dll";

    public const string ShippedPath = "audio/libsndfile_x64.dll";

    private static readonly object Gate = new();
    private static SndFile? _loaded;

    private readonly delegate* unmanaged[Cdecl]<char*, int, SfInfo*, nint> _open;
    private readonly delegate* unmanaged[Cdecl]<nint, int, void*, int, int> _command;
    private readonly delegate* unmanaged[Cdecl]<nint, short*, long, long> _readShort;
    private readonly delegate* unmanaged[Cdecl]<nint, float*, long, long> _readFloat;
    private readonly delegate* unmanaged[Cdecl]<nint, int*, long, long> _writeInt;
    private readonly delegate* unmanaged[Cdecl]<nint, int> _error;
    private readonly delegate* unmanaged[Cdecl]<nint, int> _close;

    private SndFile(string path)
    {
        Path = path;
        nint module = NativeLibrary.Load(path);
        _open = (delegate* unmanaged[Cdecl]<char*, int, SfInfo*, nint>)NativeLibrary.GetExport(module, "sf_wchar_open");
        _command = (delegate* unmanaged[Cdecl]<nint, int, void*, int, int>)NativeLibrary.GetExport(module, "sf_command");
        _readShort = (delegate* unmanaged[Cdecl]<nint, short*, long, long>)NativeLibrary.GetExport(module, "sf_readf_short");
        _readFloat = (delegate* unmanaged[Cdecl]<nint, float*, long, long>)NativeLibrary.GetExport(module, "sf_readf_float");
        _writeInt = (delegate* unmanaged[Cdecl]<nint, int*, long, long>)NativeLibrary.GetExport(module, "sf_writef_int");
        _error = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(module, "sf_error");
        _close = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(module, "sf_close");
    }

    public string Path { get; }

    public string Sha256 => _sha256 ??= Sha256Hex.OfFile(Path);

    private string? _sha256;

    public static SndFile Load(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        string path = LibraryPath(workspace);
        lock (Gate)
        {
            if (_loaded is null || !string.Equals(_loaded.Path, path, StringComparison.OrdinalIgnoreCase))
                _loaded = new SndFile(path);
            return _loaded;
        }
    }

    public static string LibraryPath(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        string inWorkspace = workspace.Join(WorkspaceRelativePath);
        if (PyOs.IsFile(inWorkspace))
            return inWorkspace;
        string beside = System.IO.Path.Combine(AppContext.BaseDirectory, ShippedFileName);
        return PyOs.IsFile(beside) ? beside : Tools.ToolData.Locate(workspace, ShippedPath);
    }

    public nint Open(string path, int mode, ref SfInfo info)
    {
        fixed (char* name = path)
        fixed (SfInfo* pointer = &info)
            return _open(name, mode, pointer);
    }

    public int Command(nint handle, int command, void* data, int size) => _command(handle, command, data, size);

    public long ReadShort(nint handle, Span<short> samples, long frames)
    {
        fixed (short* pointer = samples)
            return _readShort(handle, pointer, frames);
    }

    public long ReadFloat(nint handle, Span<float> samples, long frames)
    {
        fixed (float* pointer = samples)
            return _readFloat(handle, pointer, frames);
    }

    public long WriteInt(nint handle, ReadOnlySpan<int> samples, long frames)
    {
        fixed (int* pointer = samples)
            return _writeInt(handle, pointer, frames);
    }

    public int Error(nint handle) => _error(handle);

    public int Close(nint handle) => _close(handle);

    public static string Temporary(Workspace workspace, string suffix, ReadOnlySpan<byte> content = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        string directory = workspace.Join("analysis/audio_decode");
        PyOs.MakeDirectories(directory);
        for (int attempt = 0; ; attempt++)
        {
            string name = "tmp" + RandomName() + suffix;
            string path = PyPath.Join(directory, name);
            try
            {
                using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                StreamChunks.WriteInChunks(stream, content);
                return path;
            }
            catch (IOException) when (attempt < 100 && File.Exists(path))
            {
            }
        }
    }

    public static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private static string RandomName()
    {
        const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789_";
        char[] name = new char[8];
        for (int i = 0; i < name.Length; i++)
            name[i] = Alphabet[Random.Shared.Next(Alphabet.Length)];
        return new string(name);
    }
}
