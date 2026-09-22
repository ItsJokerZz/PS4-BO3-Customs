using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FFPorter.Core.T7.Formats;

public static class T7Movie
{
    public sealed record Video(string Codec, int Width, int Height, int Profile, int Level, double Fps, bool HasAudio)
    {
        public string? WritingApp { get; init; }

        public string ProfileName => Profile switch { 66 => "Baseline", 77 => "Main", 88 => "Extended", 100 => "High", 110 => "High 10", _ => $"profile {Profile}" };

        public override string ToString() =>
            Codec == AvcCodec
                ? $"H.264 {ProfileName} level {Level / 10}.{Level % 10} {Width}x{Height} {Fps:0.##} fps{(HasAudio ? " with audio" : "")}"
                : $"{Codec} {Width}x{Height}{(HasAudio ? " with audio" : "")}";
    }

    public const int Width = 1920, Height = 1080, FramesPerSecond = 30;

    public const int ReadChunk = 0x140000;

    public const int MaxBlock = ReadChunk - 0x1000;

    internal const string AvcCodec = "V_MPEG4/ISO/AVC", PcmCodec = "A_PCM/INT/BIG";

    internal const uint Ebml = 0x1A45DFA3, Segment = 0x18538067, SeekHead = 0x114D9B74, Seek = 0x4DBB, SeekId = 0x53AB, SeekPosition = 0x53AC,
        Info = 0x1549A966, TimecodeScale = 0x2AD7B1, Duration = 0x4489, MuxingApp = 0x4D80, WritingAppId = 0x5741, Tracks = 0x1654AE6B,
        TrackEntry = 0xAE, TrackNumber = 0xD7, TrackUid = 0x73C5, TrackType = 0x83, FlagLacing = 0x9C, CodecId = 0x86, CodecPrivate = 0x63A2,
        CodecName = 0x258688, DefaultDuration = 0x23E383, ContentEncodings = 0x6D80, VideoElement = 0xE0, AudioElement = 0xE1, PixelWidth = 0xB0,
        PixelHeight = 0xBA, DisplayWidth = 0x54B0, DisplayHeight = 0x54BA, Channels = 0x9F, SamplingFrequency = 0xB5, BitDepth = 0x6264,
        Cluster = 0x1F43B675, Timecode = 0xE7, SimpleBlock = 0xA3, Cues = 0x1C53BB6B, CuePoint = 0xBB, CueTime = 0xB3,
        CueTrackPositions = 0xB7, CueTrack = 0xF7, CueClusterPosition = 0xF1;

    public static Video? Describe(string path)
    {
        byte[] head = ReadHead(path, 8 << 20);
        Video? video = null;
        bool audio = false;
        string? writingApp = null;
        int at = 0;
        while (TryElement(head, ref at, head.Length, out uint id, out int start, out int end))
        {
            if (id != Segment)
            {
                at = end;
                continue;
            }
            int child = start;
            while (TryElement(head, ref child, end, out uint childId, out int childStart, out int childEnd) && childId != Cluster)
            {
                if (childId == Tracks)
                {
                    int entry = childStart;
                    while (TryElement(head, ref entry, childEnd, out uint entryId, out int entryStart, out int entryEnd))
                    {
                        if (entryId != TrackEntry)
                            continue;
                        Video? track = Track(head, entryStart, entryEnd, out bool isAudio);
                        audio |= isAudio;
                        video ??= track;
                    }
                }
                else if (childId == Info)
                {
                    int field = childStart;
                    while (TryElement(head, ref field, childEnd, out uint fieldId, out int fieldStart, out int fieldEnd))
                    {
                        if (fieldId == WritingAppId)
                            writingApp = Encoding.UTF8.GetString(head, fieldStart, fieldEnd - fieldStart).TrimEnd('\0');
                    }
                }
                child = childEnd;
            }
            break;
        }
        return video == null ? null : video with { HasAudio = audio, WritingApp = writingApp };
    }

    public static IReadOnlyList<string> Ps4Problems(string path)
    {
        var problems = new List<string>();
        using SafeFileHandle file = File.OpenHandle(path);
        long length = RandomAccess.GetLength(file);
        byte[] head = new byte[(int)Math.Min(length, ReadChunk)];
        RandomAccess.Read(file, head, 0);
        if (head.Length < 4 || head[0] != 0x1A || head[1] != 0x45 || head[2] != 0xDF || head[3] != 0xA3)
            return ["it is not a Matroska file"];

        long? clusterStart = Header(head, problems, out HeaderInfo info);
        if (clusterStart == null)
            return problems;
        if (info.Tracks.Count == 0)
            problems.Add("it has no tracks");
        else if (info.Tracks.Count > 1)
            problems.Add($"it has {info.Tracks.Count} tracks (retail movies are video only; the PS4 reader treats every block as video)");
        TrackInfo? video = info.Tracks.FirstOrDefault(t => t.Codec == AvcCodec);
        if (video == null)
        {
            problems.Add($"it has no H.264 track ({string.Join(", ", info.Tracks.Select(t => t.Codec))})");
            return problems;
        }
        if (video.Profile is not (66 or 77 or 100) || video.Level > 42)
            problems.Add($"it is H.264 {new Video(AvcCodec, 0, 0, video.Profile, video.Level, 0, false).ProfileName} level {video.Level / 10}.{video.Level % 10} (the PS4 decoder takes Baseline, Main or High up to level 4.2)");
        if (video.Width != Width || video.Height != Height)
            problems.Add($"it is {video.Width}x{video.Height}, not {Width}x{Height}");
        else if (video.DisplayWidth != Width || video.DisplayHeight != Height)
            problems.Add("it has no 1920x1080 display size");
        if (video.DefaultDuration == 0 || Math.Abs(1e9 / video.DefaultDuration - FramesPerSecond) > 0.01)
            problems.Add(video.DefaultDuration == 0 ? "it has no frame duration" : $"it is {1e9 / video.DefaultDuration:0.##} fps, not {FramesPerSecond}");
        if (info.TimecodeScale != 1_000_000)
            problems.Add($"its timecode scale is {info.TimecodeScale} ns (the PS4 reader takes timecodes as milliseconds)");
        if (problems.Count == 0)
            Clusters(file, length, clusterStart.Value, problems);
        return problems;
    }

    private sealed class TrackInfo
    {
        public string Codec = "";
        public int Width, Height, DisplayWidth, DisplayHeight, Profile, Level, NalLengthSize;
        public ulong DefaultDuration;
    }

    private sealed class HeaderInfo
    {
        public List<TrackInfo> Tracks { get; } = [];
        public ulong TimecodeScale = 1_000_000;
    }

    private static long? Header(byte[] head, List<string> problems, out HeaderInfo info)
    {
        info = new HeaderInfo();
        if (!TryVint(head, 4, keepMarker: false, out ulong ebmlSize, out int ebmlSizeLength, out _) || ebmlSize > 0x10000)
        {
            problems.Add("its EBML header is unreadable");
            return null;
        }
        long at = 4 + ebmlSizeLength + (long)ebmlSize, counted = at;
        TrackInfo? track = null;
        while (true)
        {
            if (at >= head.Length || !TryVint(head, at, keepMarker: true, out ulong rawId, out int idLength, out _)
                || !TryVint(head, at + idLength, keepMarker: false, out ulong size, out int sizeLength, out bool unknown))
            {
                problems.Add($"its header does not reach a Cluster within the first {ReadChunk >> 10} KB");
                return null;
            }
            uint id = (uint)rawId;
            long start = at + idLength + sizeLength;
            counted += idLength + sizeLength;
            if (counted > 0x40000)
            {
                problems.Add("its header is longer than 256 KB");
                return null;
            }
            switch (id)
            {
                case Segment or Info or Tracks or VideoElement or AudioElement:
                    at = start;
                    continue;
                case TrackEntry:
                    if (info.Tracks.Count == 2)
                    {
                        problems.Add("it has more than two tracks");
                        return null;
                    }
                    info.Tracks.Add(track = new TrackInfo());
                    at = start;
                    continue;
                case Cluster:
                    return at;
            }
            if (unknown)
            {
                at = start;
                continue;
            }
            long end = start + (long)size;
            if (end > head.Length)
            {
                problems.Add($"element {id:X} at offset {at} runs past the first read request");
                return null;
            }
            bool trackField = id is CodecId or PixelWidth or PixelHeight or DisplayWidth or DisplayHeight or TrackNumber or Channels or SamplingFrequency or BitDepth;
            if (trackField && track == null)
            {
                problems.Add($"element {id:X} at offset {at} is outside a TrackEntry");
                return null;
            }
            ulong number = size <= 8 ? Number(head, start, end) : 0;
            switch (id)
            {
                case CodecId:
                    track!.Codec = Encoding.ASCII.GetString(head, (int)start, (int)size).TrimEnd('\0');
                    if (size > 0x3F || track.Codec is not (AvcCodec or PcmCodec))
                    {
                        problems.Add($"it has a {track.Codec} track (the PS4 reader takes only {AvcCodec} and {PcmCodec})");
                        return null;
                    }
                    break;
                case PixelWidth or PixelHeight or DisplayWidth or DisplayHeight or TrackNumber or Channels or BitDepth when size > 8:
                    problems.Add($"element {id:X} at offset {at} has {size} bytes");
                    return null;
                case PixelWidth:
                    track!.Width = (int)number;
                    break;
                case PixelHeight:
                    track!.Height = (int)number;
                    break;
                case DisplayWidth:
                    track!.DisplayWidth = (int)number;
                    break;
                case DisplayHeight:
                    track!.DisplayHeight = (int)number;
                    break;
                case BitDepth when number != 16:
                    problems.Add($"its audio is {number}-bit (the PS4 reader takes 16-bit PCM)");
                    return null;
                case SamplingFrequency or Duration when size is not (4 or 8):
                    problems.Add($"element {id:X} at offset {at} is not a float");
                    return null;
                case CodecName when size > 0x3F:
                    problems.Add("its codec name is longer than 63 bytes");
                    return null;
                case CodecPrivate:
                    if (track != null)
                        Avcc(head, (int)start, (int)size, track, problems);
                    break;
                case ContentEncodings:
                    problems.Add("its track data is compressed or encrypted (ContentEncodings)");
                    break;
                case TimecodeScale:
                    info.TimecodeScale = number;
                    break;
                case DefaultDuration when track != null:
                    track.DefaultDuration = number;
                    break;
                default:
                    if (!trackField && id is not (CodecPrivate or TimecodeScale or DefaultDuration) && size > 0x10000)
                    {
                        problems.Add($"element {id:X} at offset {at} has {size} bytes (the PS4 reader skips at most 64 KB)");
                        return null;
                    }
                    break;
            }
            if (id != CodecPrivate)
                counted += (long)size;
            at = end;
        }
    }

    private static void Avcc(byte[] data, int start, int size, TrackInfo track, List<string> problems)
    {
        if (size < 8)
        {
            problems.Add("its CodecPrivate is not an avcC record");
            return;
        }
        track.Profile = data[start + 1];
        track.Level = data[start + 3];
        track.NalLengthSize = (data[start + 4] & 3) + 1;
        int spsCount = data[start + 5] & 0x1F, spsLength = data[start + 6] << 8 | data[start + 7];
        int ppsAt = start + 8 + spsLength;
        if (ppsAt + 3 > start + size)
        {
            problems.Add("its avcC record is truncated");
            return;
        }
        int ppsCount = data[ppsAt], ppsLength = data[ppsAt + 1] << 8 | data[ppsAt + 2];
        int used = 11 + spsLength + ppsLength;
        if (spsCount != 1 || ppsCount != 1)
            problems.Add($"its avcC record has {spsCount} SPS and {ppsCount} PPS (the PS4 reader takes one of each)");
        else if (used != size)
            problems.Add($"its avcC record has {size - used} bytes after the PPS (the PS4 reader would read them as the next element)");
        if (track.NalLengthSize != 4)
            problems.Add($"its NAL units have {track.NalLengthSize}-byte lengths (the PS4 reader rewrites 4-byte lengths)");
    }

    private static void Clusters(SafeFileHandle file, long length, long at, List<string> problems)
    {
        Span<byte> header = stackalloc byte[16];
        Span<byte> nalLength = stackalloc byte[4];
        long? stopped = null;
        uint stoppedId = 0;
        int blocks = 0;
        while (at < length)
        {
            int read = RandomAccess.Read(file, header, at);
            byte[] bytes = header[..read].ToArray();
            if (!TryVint(bytes, 0, keepMarker: true, out ulong rawId, out int idLength, out _)
                || !TryVint(bytes, idLength, keepMarker: false, out ulong size, out int sizeLength, out bool unknown))
            {
                problems.Add($"unreadable element at offset {at}");
                return;
            }
            uint id = (uint)rawId;
            long start = at + idLength + sizeLength;
            if (id is Cluster or Timecode or SimpleBlock && stopped != null)
            {
                problems.Add(stoppedId == Cues
                    ? $"its Cues at offset {stopped} come before more frames (the PS4 reader ends the movie there)"
                    : $"element {stoppedId:X} at offset {stopped} comes before more frames (the PS4 reader stops there)");
                return;
            }
            if (id == Cluster)
            {
                at = start;
                continue;
            }
            if (unknown)
            {
                problems.Add($"element {id:X} at offset {at} has an unknown size");
                return;
            }
            long end = start + (long)size;
            if (id == SimpleBlock)
            {
                blocks++;
                if (end - at > MaxBlock)
                {
                    problems.Add($"a frame at offset {at} takes {end - at} bytes (the PS4 reads movies {ReadChunk >> 10} KB at a time)");
                    return;
                }
                if (!TryVint(bytes, (int)(start - at), keepMarker: false, out _, out int trackLength, out _) || start - at + trackLength + 3 > read)
                {
                    problems.Add($"unreadable block at offset {at}");
                    return;
                }
                int flags = bytes[start - at + trackLength + 2];
                short relative = (short)(bytes[start - at + trackLength] << 8 | bytes[start - at + trackLength + 1]);
                if ((flags & 0x06) != 0)
                {
                    problems.Add($"the block at offset {at} is laced");
                    return;
                }
                if (relative < 0)
                {
                    problems.Add($"the block at offset {at} is timed before its cluster");
                    return;
                }
                long nal = start + trackLength + 3;
                while (nal < end)
                {
                    if (nal + 4 > end || RandomAccess.Read(file, nalLength, nal) < 4)
                    {
                        problems.Add($"the block at offset {at} does not hold whole NAL units");
                        return;
                    }
                    nal += 4 + ((long)nalLength[0] << 24 | (long)nalLength[1] << 16 | (long)nalLength[2] << 8 | nalLength[3]);
                }
                if (nal != end)
                {
                    problems.Add($"the NAL lengths in the block at offset {at} overrun it");
                    return;
                }
            }
            else if (id != Timecode)
            {
                stopped ??= at;
                stoppedId = stopped == at ? id : stoppedId;
            }
            else if (size > 8)
            {
                problems.Add($"the cluster timecode at offset {at} has {size} bytes");
                return;
            }
            at = end;
        }
        if (blocks == 0)
            problems.Add("it has no frames");
    }

    private static byte[] ReadHead(string path, int limit)
    {
        using FileStream stream = File.OpenRead(path);
        var head = new byte[(int)Math.Min(stream.Length, limit)];
        stream.ReadExactly(head);
        return head;
    }

    private static Video? Track(byte[] data, int start, int end, out bool isAudio)
    {
        int type = 0, width = 0, height = 0, profile = 0, level = 0;
        string codec = "";
        double fps = 0;
        int at = start;
        while (TryElement(data, ref at, end, out uint id, out int s, out int e))
        {
            switch (id)
            {
                case TrackType:
                    type = (int)Number(data, s, e);
                    break;
                case CodecId:
                    codec = Encoding.ASCII.GetString(data, s, e - s).TrimEnd('\0');
                    break;
                case CodecPrivate when e - s >= 4:
                    (profile, level) = (data[s + 1], data[s + 3]);
                    break;
                case DefaultDuration:
                    ulong nanoseconds = Number(data, s, e);
                    fps = nanoseconds == 0 ? 0 : 1e9 / nanoseconds;
                    break;
                case VideoElement:
                    int v = s;
                    while (TryElement(data, ref v, e, out uint videoId, out int vs, out int ve))
                    {
                        if (videoId == PixelWidth)
                            width = (int)Number(data, vs, ve);
                        else if (videoId == PixelHeight)
                            height = (int)Number(data, vs, ve);
                    }
                    break;
            }
        }
        isAudio = type == 2;
        return type == 1 ? new Video(codec, width, height, profile, level, fps, false) : null;
    }

    private static bool TryElement(byte[] data, ref int at, int limit, out uint id, out int start, out int end)
    {
        id = 0;
        start = end = at;
        if (at >= limit || !TryVint(data, at, keepMarker: true, out ulong rawId, out int idLength, out _)
            || !TryVint(data, at + idLength, keepMarker: false, out ulong size, out int sizeLength, out bool unknown) || at + idLength + sizeLength > limit)
            return false;
        id = (uint)rawId;
        start = at + idLength + sizeLength;
        end = unknown ? limit : (int)Math.Min((ulong)limit, (ulong)start + size);
        at = end;
        return true;
    }

    private static bool TryVint(byte[] data, long at, bool keepMarker, out ulong value, out int length, out bool unknown)
    {
        value = 0;
        length = 0;
        unknown = false;
        if (at >= data.Length || data[at] == 0)
            return false;
        length = System.Numerics.BitOperations.LeadingZeroCount((uint)data[at]) - 23;
        if (at + length > data.Length)
            return false;
        value = keepMarker ? data[at] : (ulong)(data[at] & (0xFF >> length));
        for (int i = 1; i < length; i++)
            value = (value << 8) | data[at + i];
        unknown = !keepMarker && length == 8 && value == (1UL << 56) - 1;
        return true;
    }

    private static ulong Number(byte[] data, long start, long end)
    {
        ulong value = 0;
        for (long i = start; i < end; i++)
            value = (value << 8) | data[i];
        return value;
    }
}
