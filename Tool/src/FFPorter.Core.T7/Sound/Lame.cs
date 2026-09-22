using System.Runtime.InteropServices;

namespace FFPorter.Core.T7.Sound;

public sealed unsafe class Lame : IDisposable
{
    private readonly nint _library;
    private readonly delegate* unmanaged[Cdecl]<nint> _init;
    private readonly delegate* unmanaged[Cdecl]<nint, int, int> _setInRate, _setOutRate, _setChannels, _setMode, _setVbr, _setReservoir, _setVbrTag;
    private readonly delegate* unmanaged[Cdecl]<nint, float, int> _setVbrQuality;
    private readonly delegate* unmanaged[Cdecl]<nint, int> _initParams, _close;
    private readonly delegate* unmanaged[Cdecl]<nint, short*, short*, int, byte*, int, int> _encode;
    private readonly delegate* unmanaged[Cdecl]<nint, short*, int, byte*, int, int> _encodeInterleaved;
    private readonly delegate* unmanaged[Cdecl]<nint, byte*, int, int> _flush;

    public string LibraryPath { get; }

    private Lame(string path)
    {
        LibraryPath = path;
        _library = NativeLibrary.Load(path);
        nint Export(string name) => NativeLibrary.GetExport(_library, name);
        _init = (delegate* unmanaged[Cdecl]<nint>)Export("lame_init");
        _setInRate = (delegate* unmanaged[Cdecl]<nint, int, int>)Export("lame_set_in_samplerate");
        _setOutRate = (delegate* unmanaged[Cdecl]<nint, int, int>)Export("lame_set_out_samplerate");
        _setChannels = (delegate* unmanaged[Cdecl]<nint, int, int>)Export("lame_set_num_channels");
        _setMode = (delegate* unmanaged[Cdecl]<nint, int, int>)Export("lame_set_mode");
        _setVbr = (delegate* unmanaged[Cdecl]<nint, int, int>)Export("lame_set_VBR");
        _setReservoir = (delegate* unmanaged[Cdecl]<nint, int, int>)Export("lame_set_disable_reservoir");
        _setVbrTag = (delegate* unmanaged[Cdecl]<nint, int, int>)Export("lame_set_bWriteVbrTag");
        _setVbrQuality = (delegate* unmanaged[Cdecl]<nint, float, int>)Export("lame_set_VBR_quality");
        _initParams = (delegate* unmanaged[Cdecl]<nint, int>)Export("lame_init_params");
        _close = (delegate* unmanaged[Cdecl]<nint, int>)Export("lame_close");
        _encode = (delegate* unmanaged[Cdecl]<nint, short*, short*, int, byte*, int, int>)Export("lame_encode_buffer");
        _encodeInterleaved = (delegate* unmanaged[Cdecl]<nint, short*, int, byte*, int, int>)Export("lame_encode_buffer_interleaved");
        _flush = (delegate* unmanaged[Cdecl]<nint, byte*, int, int>)Export("lame_encode_flush");
    }

    public static IEnumerable<string> Candidates(Workspace? workspace)
    {
        string? variable = Environment.GetEnvironmentVariable("T7_LAME_DLL");
        if (!string.IsNullOrEmpty(variable))
            yield return variable;
        yield return Path.Combine(AppContext.BaseDirectory, "libmp3lame64.dll");
        yield return Common.Tools.ToolData.Locate(workspace, "t7_audio/libmp3lame64.dll");
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(programs, "Unity", "Editor", "Data", "Tools", "FSBTool", "x64", "libmp3lame64.dll");
        string hub = Path.Combine(programs, "Unity", "Hub", "Editor");
        if (Directory.Exists(hub))
        {
            foreach (string version in Directory.EnumerateDirectories(hub))
                yield return Path.Combine(version, "Editor", "Data", "Tools", "FSBTool", "x64", "libmp3lame64.dll");
        }
    }

    public static Lame Load(Workspace? workspace)
    {
        foreach (string candidate in Candidates(workspace))
        {
            if (File.Exists(candidate))
                return new Lame(candidate);
        }
        throw new FileNotFoundException("libmp3lame64.dll was not found (set T7_LAME_DLL, or place it in app/data/t7_audio). PS4 sound banks need an MP3 encoder that can disable the bit reservoir.");
    }

    public byte[] Encode(ReadOnlySpan<short> pcm, int channels, int sampleRate, float vbrQuality = 0.0f)
    {
        nint state = _init();
        if (state == 0)
            throw new InvalidOperationException("lame_init failed");
        try
        {
            _setInRate(state, sampleRate);
            _setOutRate(state, sampleRate);
            _setChannels(state, channels);
            _setMode(state, channels == 1 ? 3 : 1);
            _setVbr(state, 4);
            _setVbrQuality(state, vbrQuality);
            _setReservoir(state, 1);
            _setVbrTag(state, 0);
            if (_initParams(state) < 0)
                throw new InvalidOperationException("lame_init_params failed");
            int frames = pcm.Length / channels;
            int capacity = (int)(1.25 * frames + 7200) + 16384;
            var buffer = new byte[capacity];
            int written;
            fixed (short* samples = pcm)
            fixed (byte* output = buffer)
            {
                written = channels == 1 ? _encode(state, samples, samples, frames, output, capacity) : _encodeInterleaved(state, samples, frames, output, capacity);
                if (written < 0)
                    throw new InvalidOperationException($"LAME encode error {written}");
                int flushed = _flush(state, output + written, capacity - written);
                if (flushed < 0)
                    throw new InvalidOperationException($"LAME flush error {flushed}");
                written += flushed;
            }
            return buffer.AsSpan(0, written).ToArray();
        }
        finally
        {
            _close(state);
        }
    }

    public void Dispose() => NativeLibrary.Free(_library);
}
