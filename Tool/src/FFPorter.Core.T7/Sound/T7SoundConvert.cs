using System.Text.Json;
using FFPorter.Core.Common.Audio;

namespace FFPorter.Core.T7.Sound;

public static class T7SoundConvert
{
    public const int FrameSamples = 1152;
    public const int OutputRate = 48000;
    public const string RulesVersion = "t7-sound-3";

    public sealed record Result(T7SoundBank Bank, Dictionary<string, string> RenamedAssets, List<string> Notes)
    {
        public int Reencoded { get; init; }
        public int Resampled { get; init; }
        public int Retimed { get; init; }
        public int Silenced { get; init; }
    }

    private sealed record CacheFile(string Signature, string Bank, Dictionary<string, string> Renamed, List<string> Notes, int Reencoded, int Resampled, int Retimed, int Silenced);

    public static bool IsCached(string pcBank, string cacheFile)
    {
        try
        {
            string metadata = cacheFile + ".t7cache.json";
            if (!File.Exists(metadata) || !File.Exists(cacheFile))
                return false;
            CacheFile? cache = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(metadata));
            return cache != null && cache.Signature == Signature(pcBank) && cache.Bank == Stamp(cacheFile);
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    public static Result? Reuse(string pcBank, string cacheFile, Action<string>? log = null)
    {
        try
        {
            string metadata = cacheFile + ".t7cache.json";
            if (!File.Exists(metadata) || !File.Exists(cacheFile))
                return null;
            CacheFile? cache = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(metadata));
            if (cache == null || cache.Signature != Signature(pcBank) || cache.Bank != Stamp(cacheFile) || cache.Renamed == null || cache.Notes == null)
                return null;
            var result = new Result(T7SoundBank.Parse(File.ReadAllBytes(cacheFile)), new Dictionary<string, string>(cache.Renamed, StringComparer.OrdinalIgnoreCase), cache.Notes)
            {
                Reencoded = cache.Reencoded,
                Resampled = cache.Resampled,
                Retimed = cache.Retimed,
                Silenced = cache.Silenced,
            };
            log?.Invoke($"  {Path.GetFileName(pcBank)} is up to date (reusing the previous conversion)");
            return result;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
        {
            return null;
        }
    }

    public static void Remember(string pcBank, string cacheFile, Result result, Action<string>? log = null)
    {
        string metadata = cacheFile + ".t7cache.json";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            File.Delete(metadata);
            string temporary = cacheFile + ".tmp";
            File.WriteAllBytes(temporary, result.Bank.Build());
            File.Move(temporary, cacheFile, overwrite: true);
            var cache = new CacheFile(Signature(pcBank), Stamp(cacheFile), new Dictionary<string, string>(result.RenamedAssets), result.Notes,
                result.Reencoded, result.Resampled, result.Retimed, result.Silenced);
            File.WriteAllText(metadata, JsonSerializer.Serialize(cache));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log?.Invoke($"  could not write the sound conversion cache: {error.Message}");
        }
    }

    private static string Signature(string pcBank) => $"{RulesVersion}|{Stamp(pcBank)}";

    private static string Stamp(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}" : "-";
    }

    public static Result Convert(T7SoundBank pc, Workspace workspace, Lame lame, Action<string>? log = null, Action<int, int>? progress = null)
    {
        var ps4 = new T7SoundBank
        {
            Version = pc.Version,
            DependencyCount = pc.DependencyCount,
            Unknown1C = pc.Unknown1C,
            BankChecksum = (byte[])pc.BankChecksum.Clone(),
            Dependencies = [.. pc.Dependencies],
            Zone = pc.Zone,
            Platform = "orbis",
            Language = pc.Language,
            PlatformByte = 0x13,
        };
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();
        int reencoded = 0, resampled = 0, retimed = 0, silenced = 0;
        SndFile sndfile = SndFile.Load(workspace);
        for (int i = 0; i < pc.Entries.Count; i++)
        {
            T7SoundBank.Entry source = pc.Entries[i];
            byte[] mp3;
            uint frameCount = source.FrameCount;
            byte channels = source.Channels;
            switch (source.Format)
            {
                case T7SoundBank.FormatMp3:
                    mp3 = source.Data;
                    break;
                case T7SoundBank.FormatFlac when source.Data.Length == 0:
                    channels = Math.Max((byte)1, source.Channels);
                    mp3 = lame.Encode(new short[FrameSamples * channels], channels, OutputRate);
                    frameCount = FrameSamples;
                    notes.Add($"'{source.Name}' has no audio in the PC bank, so it plays as silence");
                    reencoded++;
                    silenced++;
                    break;
                case T7SoundBank.FormatFlac:
                    mp3 = EncodeEntry(source, workspace, sndfile, lame, notes, out bool wasResampled, out bool wasRetimed, out bool wasSilent);
                    reencoded++;
                    resampled += wasResampled ? 1 : 0;
                    retimed += wasRetimed ? 1 : 0;
                    if (wasSilent)
                    {
                        channels = Math.Max((byte)1, source.Channels);
                        frameCount = FrameSamples;
                        silenced++;
                    }
                    break;
                default:
                    throw new InvalidDataException($"sound '{Describe(source)}' uses format {source.Format}; only FLAC (8) and MP3 (5) are converted");
            }
            bool named = source.Name.Length > 0;
            string name = named ? T7SoundBank.Ps4AssetName(source.Name) : "";
            if (named)
                renamed[source.Name] = name;
            ps4.Entries.Add(new T7SoundBank.Entry
            {
                Id = named ? T7SoundBank.HashName(name) : source.Id,
                Data = mp3,
                FrameCount = frameCount,
                Unknown0C = 0,
                RateIndex = 6,
                Channels = channels,
                Looping = source.Looping,
                Format = T7SoundBank.FormatMp3,
                Meta = (byte[])source.Meta.Clone(),
                SourceChecksum = (byte[])source.SourceChecksum.Clone(),
                Name = name,
            });
            if (log != null && (i % 50 == 0 || i == pc.Entries.Count - 1))
                log($"  sound {i + 1}/{pc.Entries.Count}: {(named ? name : Describe(source))} ({source.Data.Length} -> {mp3.Length} bytes)");
            progress?.Invoke(i + 1, pc.Entries.Count);
        }
        return new Result(ps4, renamed, notes) { Reencoded = reencoded, Resampled = resampled, Retimed = retimed, Silenced = silenced };
    }

    private static string Describe(T7SoundBank.Entry entry) => entry.Name.Length > 0 ? entry.Name : $"#{entry.Id:X8}";

    private static byte[] EncodeEntry(T7SoundBank.Entry entry, Workspace workspace, SndFile sndfile, Lame lame, List<string> notes, out bool resampled, out bool retimed, out bool silent)
    {
        (short[] pcm, int channels, int rate) = DecodeFlac(entry.Data, workspace, sndfile);
        resampled = false;
        retimed = false;
        silent = channels <= 0 || pcm.Length < channels;
        if (silent)
        {
            int silentChannels = Math.Max(1, (int)entry.Channels);
            notes.Add($"'{Describe(entry)}' decodes to no audio, so it plays as silence");
            return lame.Encode(new short[FrameSamples * silentChannels], silentChannels, OutputRate);
        }
        if (channels != entry.Channels)
            throw new InvalidDataException($"sound '{Describe(entry)}' decodes to {channels} channels, the entry says {entry.Channels}");
        int frames = pcm.Length / channels;
        resampled = rate != OutputRate;
        retimed = false;
        if (rate != OutputRate)
        {
            pcm = ResamplePeriodic(pcm, channels, Math.Max(1, (int)Math.Round((double)frames * OutputRate / rate)), periodic: entry.Looping != 0);
            notes.Add($"resampled '{entry.Name}' from {rate} Hz");
            frames = pcm.Length / channels;
        }
        if (entry.Looping == 0)
            return lame.Encode(pcm, channels, OutputRate);

        int loopFrames = Math.Max(1, (int)Math.Round((double)frames / FrameSamples));
        int loopLength = loopFrames * FrameSamples;
        retimed = loopLength != frames;
        short[] loop = loopLength == frames ? pcm : ResamplePeriodic(pcm, channels, loopLength, periodic: true);
        var feed = new short[(576 + loopLength + 2304) * channels];
        loop.AsSpan((loopLength - 576) * channels, 576 * channels).CopyTo(feed);
        loop.AsSpan(0, loopLength * channels).CopyTo(feed.AsSpan(576 * channels));
        loop.AsSpan(0, 2304 * channels).CopyTo(feed.AsSpan((576 + loopLength) * channels));
        byte[] encoded = lame.Encode(feed, channels, OutputRate);
        List<(int Offset, int Length)> mp3Frames = Mp3Frames(encoded);
        if (mp3Frames.Count < loopFrames + 2)
            throw new InvalidDataException($"loop '{entry.Name}' encoded to {mp3Frames.Count} frames, {loopFrames + 2} needed");
        int start = mp3Frames[1].Offset;
        int end = mp3Frames[loopFrames + 1].Offset;
        return encoded.AsSpan(start, end - start).ToArray();
    }

    private static (short[] Pcm, int Channels, int Rate) DecodeFlac(byte[] flac, Workspace workspace, SndFile sndfile)
    {
        string path = SndFile.Temporary(workspace, ".flac", flac);
        try
        {
            var info = new SfInfo();
            nint handle = sndfile.Open(path, SndFile.ModeRead, ref info);
            if (handle == 0)
                throw new InvalidDataException($"libsndfile could not open the FLAC payload at {path}: {sndfile.ErrorText(0)}");
            try
            {
                int channels = info.Channels;
                if (channels <= 0)
                    return ([], channels, info.SampleRate);
                const long chunkFrames = 1 << 16;
                long maxFrames = Array.MaxLength / channels;
                bool known = info.Frames > 0 && info.Frames <= maxFrames;
                var pcm = new short[(known ? info.Frames : chunkFrames) * channels];
                long total = 0;
                while (true)
                {
                    long room = pcm.Length / channels - total;
                    if (room == 0)
                    {
                        if (known || pcm.Length / channels >= maxFrames)
                            break;
                        Array.Resize(ref pcm, (int)(Math.Min(maxFrames, pcm.Length / channels * 2L) * channels));
                        room = pcm.Length / channels - total;
                    }
                    long read = sndfile.ReadShort(handle, pcm.AsSpan((int)(total * channels)), room);
                    if (read <= 0)
                        break;
                    total += read;
                }
                if (total * channels != pcm.Length)
                    Array.Resize(ref pcm, (int)(total * channels));
                return (pcm, channels, info.SampleRate);
            }
            finally
            {
                sndfile.Close(handle);
            }
        }
        finally
        {
            SndFile.Delete(path);
        }
    }

    public static short[] ResamplePeriodic(short[] pcm, int channels, int targetFrames, bool periodic)
    {
        if (channels <= 0 || targetFrames <= 0)
            return [];
        int frames = pcm.Length / channels;
        if (frames == 0)
            return new short[targetFrames * channels];
        const int half = 24, taps = 2 * half, phases = 1024;
        const double beta = 8.6;
        double ratio = (double)frames / targetFrames;
        double cutoff = Math.Min(1.0, 1.0 / ratio);
        var kernel = new double[(phases + 1) * taps];
        double kaiserNorm = BesselI0(beta);
        for (int p = 0; p <= phases; p++)
        {
            double fraction = (double)p / phases, total = 0;
            for (int tap = 0; tap < taps; tap++)
            {
                double x = tap - half + 1 - fraction;
                double t = x / half;
                double window = Math.Abs(t) >= 1 ? 0 : BesselI0(beta * Math.Sqrt(1 - t * t)) / kaiserNorm;
                double argument = Math.PI * x * cutoff;
                double sinc = Math.Abs(argument) < 1e-12 ? 1 : Math.Sin(argument) / argument;
                kernel[p * taps + tap] = sinc * window;
                total += sinc * window;
            }
            for (int tap = 0; tap < taps; tap++)
                kernel[p * taps + tap] /= total;
        }
        var output = new short[targetFrames * channels];
        for (int n = 0; n < targetFrames; n++)
        {
            double position = n * ratio;
            int center = (int)Math.Floor(position);
            int phase = (int)Math.Round((position - center) * phases);
            int row = phase * taps;
            for (int c = 0; c < channels; c++)
            {
                double sum = 0;
                for (int tap = 0; tap < taps; tap++)
                {
                    int index = center + tap - half + 1;
                    if (periodic)
                        index = ((index % frames) + frames) % frames;
                    else
                        index = Math.Clamp(index, 0, frames - 1);
                    sum += pcm[index * channels + c] * kernel[row + tap];
                }
                output[n * channels + c] = (short)Math.Clamp(Math.Round(sum), short.MinValue, short.MaxValue);
            }
        }
        return output;
    }

    private static double BesselI0(double x)
    {
        double sum = 1, term = 1, half = x / 2;
        for (int k = 1; k < 50; k++)
        {
            term *= half / k;
            double squared = term * term;
            sum += squared;
            if (squared < 1e-12 * sum)
                break;
        }
        return sum;
    }

    public static List<(int Offset, int Length)> Mp3Frames(ReadOnlySpan<byte> data)
    {
        int[] mpeg1Rates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0];
        int[] mpeg2Rates = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0];
        var frames = new List<(int, int)>();
        for (int at = 0; at + 4 <= data.Length;)
        {
            if (data[at] != 0xFF || (data[at + 1] & 0xE0) != 0xE0)
                break;
            int version = (data[at + 1] >> 3) & 3, layer = (data[at + 1] >> 1) & 3;
            int bitrateIndex = data[at + 2] >> 4, rateIndex = (data[at + 2] >> 2) & 3;
            if (version == 1 || layer != 1 || bitrateIndex is 0 or 15 || rateIndex == 3)
                break;
            bool mpeg1 = version == 3;
            int sampleRate = version switch { 3 => new[] { 44100, 48000, 32000 }[rateIndex], 2 => new[] { 22050, 24000, 16000 }[rateIndex], _ => new[] { 11025, 12000, 8000 }[rateIndex] };
            int kbps = mpeg1 ? mpeg1Rates[bitrateIndex] : mpeg2Rates[bitrateIndex];
            int length = (mpeg1 ? 144000 : 72000) * kbps / sampleRate + ((data[at + 2] >> 1) & 1);
            frames.Add((at, length));
            at += length;
        }
        return frames;
    }
}
