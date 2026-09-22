using System.Buffers.Binary;
using System.Text;
using static FFPorter.Core.T7.Formats.T7Movie;

namespace FFPorter.Core.T7.Formats;

public sealed class T7MatroskaWriter : IDisposable
{
    private const long ClusterBytes = 5 << 20, ClusterSpanMs = 30_000;

    private readonly FileStream _file;
    private readonly int _fps;
    private readonly long _segmentSizeAt, _segmentData, _durationAt, _cuesPositionAt;
    private readonly List<(long Frame, bool Key, byte[] Data)> _cluster = [];
    private readonly List<(long Time, long Position)> _cues = [];
    private long _clusterBytes, _endFrame;

    public T7MatroskaWriter(string path, int width, int height, int framesPerSecond, byte[] avcC, string writingApp)
    {
        _fps = framesPerSecond;
        _file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1 << 16);
        var ebml = new MemoryStream();
        Unsigned(ebml, 0x4286, 1);
        Unsigned(ebml, 0x42F7, 1);
        Unsigned(ebml, 0x42F2, 4);
        Unsigned(ebml, 0x42F3, 8);
        Text(ebml, 0x4282, "matroska");
        Unsigned(ebml, 0x4287, 4);
        Unsigned(ebml, 0x4285, 2);
        Master(_file, Ebml, ebml);

        Id(_file, Segment);
        _segmentSizeAt = _file.Position;
        _file.Write([0x01, 0, 0, 0, 0, 0, 0, 0]);
        _segmentData = _file.Position;

        var seekHead = new MemoryStream();
        long[] positionAt = new long[3];
        var info = new MemoryStream();
        Unsigned(info, TimecodeScale, 1_000_000);
        Text(info, MuxingApp, writingApp);
        Text(info, WritingAppId, writingApp);
        Id(info, Duration);
        Size(info, 8);
        long durationInInfo = info.Position;
        info.Write(new byte[8]);

        var video = new MemoryStream();
        Unsigned(video, PixelWidth, (ulong)width);
        Unsigned(video, PixelHeight, (ulong)height);
        Unsigned(video, DisplayWidth, (ulong)width);
        Unsigned(video, DisplayHeight, (ulong)height);
        var entry = new MemoryStream();
        Unsigned(entry, TrackNumber, 1);
        Unsigned(entry, TrackUid, 1);
        Unsigned(entry, FlagLacing, 0);
        Text(entry, CodecId, AvcCodec);
        Unsigned(entry, TrackType, 1);
        Unsigned(entry, DefaultDuration, (ulong)Math.Round(1e9 / framesPerSecond));
        Master(entry, VideoElement, video);
        Binary(entry, CodecPrivate, avcC);
        var tracks = new MemoryStream();
        Master(tracks, TrackEntry, entry);

        foreach ((uint target, int index) in new[] { (Info, 0), (Tracks, 1), (Cues, 2) })
        {
            var seek = new MemoryStream();
            Binary(seek, SeekId, BigEndianId(target));
            Id(seek, SeekPosition);
            Size(seek, 8);
            positionAt[index] = seek.Position;
            seek.Write(new byte[8]);
            long seekStart = seekHead.Position + IdLength(Seek) + 1;
            Master(seekHead, Seek, seek);
            positionAt[index] += seekStart;
        }
        long seekHeadData = _file.Position + IdLength(SeekHead) + SizeLength(seekHead.Length);
        Master(_file, SeekHead, seekHead);
        long infoAt = _file.Position;
        long infoData = infoAt + IdLength(Info) + SizeLength(info.Length);
        Master(_file, Info, info);
        long tracksAt = _file.Position;
        Master(_file, Tracks, tracks);
        _durationAt = infoData + durationInInfo;
        _cuesPositionAt = seekHeadData + positionAt[2];
        Patch(seekHeadData + positionAt[0], (ulong)(infoAt - _segmentData));
        Patch(seekHeadData + positionAt[1], (ulong)(tracksAt - _segmentData));
    }

    public void Write(long frame, bool key, byte[] data)
    {
        if (data.Length + 16 > MaxBlock)
            throw new InvalidDataException($"frame {frame} is {data.Length} bytes; the PS4 reads movies {ReadChunk >> 10} KB at a time");
        if (_cluster.Count > 0)
        {
            long low = Math.Min(frame, _cluster.Min(f => f.Frame)), high = Math.Max(frame, _cluster.Max(f => f.Frame));
            if (key || _clusterBytes + data.Length > ClusterBytes || Milliseconds(high) - Milliseconds(low) > ClusterSpanMs)
                FlushCluster();
        }
        _cluster.Add((frame, key, data));
        _clusterBytes += data.Length;
        _endFrame = Math.Max(_endFrame, frame + 1);
    }

    public void Finish()
    {
        FlushCluster();
        long cuesAt = _file.Position;
        var cues = new MemoryStream();
        foreach ((long time, long position) in _cues)
        {
            var positions = new MemoryStream();
            Unsigned(positions, CueTrack, 1);
            Unsigned(positions, CueClusterPosition, (ulong)position);
            var point = new MemoryStream();
            Unsigned(point, CueTime, (ulong)time);
            Master(point, CueTrackPositions, positions);
            Master(cues, CuePoint, point);
        }
        Master(_file, Cues, cues);
        Patch(_cuesPositionAt, (ulong)(cuesAt - _segmentData));
        _file.Position = _durationAt;
        Span<byte> duration = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(duration, Milliseconds(_endFrame));
        _file.Write(duration);
        Patch(_segmentSizeAt + 1, (ulong)(_file.Length - _segmentData), 7);
        _file.Flush();
    }

    public void Dispose() => _file.Dispose();

    private long Milliseconds(long frame) => (frame * 1000 + _fps / 2) / _fps;

    private void FlushCluster()
    {
        if (_cluster.Count == 0)
            return;
        long time = Milliseconds(_cluster.Min(f => f.Frame));
        var timecode = new MemoryStream();
        Unsigned(timecode, Timecode, (ulong)time);
        long body = timecode.Length;
        foreach ((_, _, byte[] data) in _cluster)
            body += IdLength(SimpleBlock) + SizeLength(data.Length + 4) + 4 + data.Length;
        long position = _file.Position - _segmentData;
        if (_cluster[0].Key)
            _cues.Add((time, position));
        Id(_file, Cluster);
        Size(_file, body);
        timecode.WriteTo(_file);
        Span<byte> blockHeader = stackalloc byte[4];
        foreach ((long frame, bool key, byte[] data) in _cluster)
        {
            Id(_file, SimpleBlock);
            Size(_file, data.Length + 4);
            blockHeader[0] = 0x81;
            BinaryPrimitives.WriteInt16BigEndian(blockHeader[1..], checked((short)(Milliseconds(frame) - time)));
            blockHeader[3] = (byte)(key ? 0x80 : 0);
            _file.Write(blockHeader);
            _file.Write(data);
        }
        _cluster.Clear();
        _clusterBytes = 0;
    }

    private void Patch(long at, ulong value, int bytes = 8)
    {
        long back = _file.Position;
        _file.Position = at;
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
        _file.Write(buffer[(8 - bytes)..]);
        _file.Position = back;
    }

    private static byte[] BigEndianId(uint id)
    {
        int length = IdLength(id);
        var bytes = new byte[length];
        for (int i = 0; i < length; i++)
            bytes[i] = (byte)(id >> (8 * (length - 1 - i)));
        return bytes;
    }

    private static int IdLength(uint id) => id > 0xFFFFFF ? 4 : id > 0xFFFF ? 3 : id > 0xFF ? 2 : 1;

    private static int SizeLength(long size)
    {
        int length = 1;
        while (length < 8 && size >= (1L << (7 * length)) - 1)
            length++;
        return length;
    }

    private static void Id(Stream stream, uint id) => stream.Write(BigEndianId(id));

    private static void Size(Stream stream, long size)
    {
        int length = SizeLength(size);
        ulong value = (ulong)size | (1UL << (7 * length));
        for (int i = length - 1; i >= 0; i--)
            stream.WriteByte((byte)(value >> (8 * i)));
    }

    private static void Master(Stream stream, uint id, MemoryStream body)
    {
        Id(stream, id);
        Size(stream, body.Length);
        body.WriteTo(stream);
    }

    private static void Binary(Stream stream, uint id, byte[] data)
    {
        Id(stream, id);
        Size(stream, data.Length);
        stream.Write(data);
    }

    private static void Text(Stream stream, uint id, string text) => Binary(stream, id, Encoding.ASCII.GetBytes(text));

    private static void Unsigned(Stream stream, uint id, ulong value)
    {
        int length = 1;
        while (length < 8 && value >> (8 * length) != 0)
            length++;
        Id(stream, id);
        Size(stream, length);
        for (int i = length - 1; i >= 0; i--)
            stream.WriteByte((byte)(value >> (8 * i)));
    }
}
