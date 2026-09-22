using System.Diagnostics;
using System.Runtime.InteropServices;
using Sdcb.FFmpeg.Raw;

namespace FFPorter.Core.T7.Formats;

public static unsafe class T7MovieTranscoder
{
    public const string WritingApp = "PS4 FF Porter movie 1";

    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".mkv", ".mp4", ".m4v", ".mov", ".webm", ".avi", ".wmv", ".flv", ".mpg", ".mpeg", ".ts" };

    public enum Outcome { Copied, Encoded, UpToDate }

    private const int AvLogError = 16, CodecFlagGlobalHeader = 1 << 22, ProfileHigh = 100, Slices = 8;

    private sealed class State
    {
        public AVFormatContext* Input;
        public AVCodecContext* Decoder, Encoder;
        public AVFilterGraph* Graph;
        public AVFilterContext* Source, Sink;
        public AVPacket* Packet, Encoded;
        public AVFrame* Frame, Filtered;
        public T7MatroskaWriter? Writer;
        public long Frames, ExpectedFrames, LastPts = ffmpeg.AV_NOPTS_VALUE, LargestFrame;
        public string Name = "";
        public Action<string>? Log;
        public Stopwatch Clock = Stopwatch.StartNew();
        public TimeSpan LastReport;
    }

    public static Outcome Prepare(string source, string target, Action<string> log)
    {
        string name = Path.GetFileName(source);
        IReadOnlyList<string> problems = T7Movie.Ps4Problems(source);
        log($"movie {name}: {T7Movie.Describe(source)?.ToString() ?? "not a Matroska video"}");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (problems.Count == 0)
        {
            var from = new FileInfo(source);
            var to = new FileInfo(target);
            if (!to.Exists || to.Length != from.Length || to.LastWriteTimeUtc < from.LastWriteTimeUtc)
                File.Copy(source, target, overwrite: true);
            log("  already PS4-ready, copied as it is");
            return Outcome.Copied;
        }
        if (IsCurrent(source, target))
        {
            log($"  {Path.GetFileName(target)} is up to date (reusing the previous conversion)");
            return Outcome.UpToDate;
        }
        log($"  re-encoding for PS4: {string.Join("; ", problems)}");
        Transcode(source, target, log);
        IReadOnlyList<string> check = T7Movie.Ps4Problems(target);
        if (check.Count > 0)
        {
            File.Delete(target);
            throw new InvalidDataException($"the re-encoded {name} still would not play on PS4: {string.Join("; ", check)}");
        }
        return Outcome.Encoded;
    }

    private static bool IsCurrent(string source, string target)
    {
        var to = new FileInfo(target);
        return to.Exists && to.LastWriteTimeUtc >= File.GetLastWriteTimeUtc(source)
            && T7Movie.Describe(target)?.WritingApp == WritingApp && T7Movie.Ps4Problems(target).Count == 0;
    }

    public static void Transcode(string input, string output, Action<string>? log = null)
    {
        ffmpeg.av_log_set_level(AvLogError);
        var s = new State
        {
            Packet = ffmpeg.av_packet_alloc(), Encoded = ffmpeg.av_packet_alloc(), Frame = ffmpeg.av_frame_alloc(), Filtered = ffmpeg.av_frame_alloc(),
            Name = Path.GetFileName(output), Log = log,
        };
        string temporary = output + ".part";
        try
        {
            AVFormatContext* inputContext = null;
            Check(ffmpeg.avformat_open_input(&inputContext, input, null, null), $"open {Path.GetFileName(input)}");
            s.Input = inputContext;
            Check(ffmpeg.avformat_find_stream_info(s.Input, null), "read the stream info");
            AVCodec* decoderCodec = null;
            int videoIndex = Check(ffmpeg.av_find_best_stream(s.Input, AVMediaType.Video, -1, -1, &decoderCodec, 0), "find a video stream");
            if (decoderCodec == null)
                throw new InvalidDataException($"FFmpeg has no decoder for the video of {Path.GetFileName(input)}");
            AVStream* inputStream = s.Input->streams[videoIndex];
            s.Decoder = ffmpeg.avcodec_alloc_context3(decoderCodec);
            Check(ffmpeg.avcodec_parameters_to_context(s.Decoder, inputStream->codecpar), "set up the decoder");
            s.Decoder->pkt_timebase = inputStream->time_base;
            s.Decoder->thread_count = 0;
            Check(ffmpeg.avcodec_open2(s.Decoder, decoderCodec, null), "open the decoder");
            if (s.Input->duration > 0)
                s.ExpectedFrames = s.Input->duration * T7Movie.FramesPerSecond / ffmpeg.AV_TIME_BASE;

            BuildFilters(s, inputStream);
            byte[] avcC = OpenEncoder(s);
            s.Writer = new T7MatroskaWriter(temporary, T7Movie.Width, T7Movie.Height, T7Movie.FramesPerSecond, avcC, WritingApp);

            while (ffmpeg.av_read_frame(s.Input, s.Packet) >= 0)
            {
                if (s.Packet->stream_index == videoIndex)
                    Decode(s, s.Packet);
                ffmpeg.av_packet_unref(s.Packet);
            }
            Decode(s, null);
            Check(ffmpeg.av_buffersrc_add_frame_flags(s.Source, null, 0), "flush the filters");
            Filtered(s);
            Encode(s, null);
            if (s.Frames == 0)
                throw new InvalidDataException($"{Path.GetFileName(input)} has no video frames");
            s.Writer.Finish();
            s.Writer.Dispose();
            File.Move(temporary, output, overwrite: true);
            log?.Invoke($"  {s.Name}: {s.Frames} frames ({s.Frames / (double)T7Movie.FramesPerSecond:0.#} s) at {T7Movie.Width}x{T7Movie.Height} {T7Movie.FramesPerSecond} fps, "
                + $"{new FileInfo(output).Length / 1048576.0:0.#} MB, largest frame {s.LargestFrame / 1024} KB, took {s.Clock.Elapsed.TotalSeconds:0} s");
        }
        finally
        {
            s.Writer?.Dispose();
            AVFilterGraph* graph = s.Graph;
            ffmpeg.avfilter_graph_free(&graph);
            AVCodecContext* encoder = s.Encoder, decoder = s.Decoder;
            ffmpeg.avcodec_free_context(&encoder);
            ffmpeg.avcodec_free_context(&decoder);
            AVFormatContext* inputContext = s.Input;
            ffmpeg.avformat_close_input(&inputContext);
            AVFrame* frame = s.Frame, filtered = s.Filtered;
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_frame_free(&filtered);
            AVPacket* packet = s.Packet, encoded = s.Encoded;
            ffmpeg.av_packet_free(&packet);
            ffmpeg.av_packet_free(&encoded);
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void BuildFilters(State s, AVStream* inputStream)
    {
        s.Graph = ffmpeg.avfilter_graph_alloc();
        AVCodecContext* decoder = s.Decoder;
        AVRational frameRate = ffmpeg.av_guess_frame_rate(s.Input, inputStream, null);
        AVRational aspect = decoder->sample_aspect_ratio;
        string sourceArgs = $"video_size={decoder->width}x{decoder->height}:pix_fmt={(int)decoder->pix_fmt}"
            + $":time_base={inputStream->time_base.Num}/{inputStream->time_base.Den}"
            + $":pixel_aspect={(aspect.Num > 0 && aspect.Den > 0 ? aspect.Num : 1)}/{(aspect.Num > 0 && aspect.Den > 0 ? aspect.Den : 1)}"
            + $":colorspace={(int)decoder->colorspace}:range={(int)decoder->color_range}"
            + (frameRate.Num > 0 && frameRate.Den > 0 ? $":frame_rate={frameRate.Num}/{frameRate.Den}" : "");
        string matrix = decoder->colorspace switch
        {
            AVColorSpace.Bt709 => "bt709",
            AVColorSpace.Smpte170m or AVColorSpace.Bt470bg => "bt601",
            AVColorSpace.Bt2020Ncl or AVColorSpace.Bt2020Cl => "bt2020",
            AVColorSpace.Fcc => "fcc",
            AVColorSpace.Smpte240m => "smpte240m",
            _ => decoder->height >= 720 ? "bt709" : "bt601",
        };
        AVFilterContext* source = null, sink = null;
        Check(ffmpeg.avfilter_graph_create_filter(&source, ffmpeg.avfilter_get_by_name("buffer"), "in", sourceArgs, null, s.Graph), "create the filter source");
        Check(ffmpeg.avfilter_graph_create_filter(&sink, ffmpeg.avfilter_get_by_name("buffersink"), "out", null, null, s.Graph), "create the filter sink");
        s.Source = source;
        s.Sink = sink;
        AVFilterInOut* outputs = ffmpeg.avfilter_inout_alloc(), inputs = ffmpeg.avfilter_inout_alloc();
        outputs->name = ffmpeg.av_strdup("in");
        outputs->filter_ctx = source;
        inputs->name = ffmpeg.av_strdup("out");
        inputs->filter_ctx = sink;
        string chain = "scale=w='trunc(iw*sar/2)*2':h=ih,setsar=1,"
            + $"scale={T7Movie.Width}:{T7Movie.Height}:force_original_aspect_ratio=decrease:force_divisible_by=2:flags=lanczos"
            + $":in_color_matrix={matrix}:out_color_matrix=bt709:out_range=tv,"
            + $"pad={T7Movie.Width}:{T7Movie.Height}:(ow-iw)/2:(oh-ih)/2:black,setsar=1,fps={T7Movie.FramesPerSecond},format=yuv420p";
        try
        {
            Check(ffmpeg.avfilter_graph_parse_ptr(s.Graph, chain, &inputs, &outputs, null), "build the filters");
        }
        finally
        {
            ffmpeg.avfilter_inout_free(&inputs);
            ffmpeg.avfilter_inout_free(&outputs);
        }
        Check(ffmpeg.avfilter_graph_config(s.Graph, null), "configure the filters");
    }

    private static byte[] OpenEncoder(State s)
    {
        AVCodec* codec = ffmpeg.avcodec_find_encoder_by_name("libx264");
        if (codec == null)
            throw new InvalidDataException("the FFmpeg libraries have no libx264 encoder");
        s.Encoder = ffmpeg.avcodec_alloc_context3(codec);
        AVCodecContext* encoder = s.Encoder;
        encoder->width = T7Movie.Width;
        encoder->height = T7Movie.Height;
        encoder->pix_fmt = AVPixelFormat.Yuv420p;
        encoder->sample_aspect_ratio = new AVRational { Num = 1, Den = 1 };
        encoder->time_base = new AVRational { Num = 1, Den = T7Movie.FramesPerSecond };
        encoder->framerate = new AVRational { Num = T7Movie.FramesPerSecond, Den = 1 };
        encoder->profile = ProfileHigh;
        encoder->level = 40;
        encoder->slices = Slices;
        encoder->thread_count = 0;
        encoder->rc_max_rate = 10_000_000;
        encoder->rc_buffer_size = 8_000_000;
        encoder->flags |= CodecFlagGlobalHeader;
        Check(ffmpeg.av_opt_set(encoder->priv_data, "preset", "medium", 0), "set the encoder preset");
        Check(ffmpeg.av_opt_set(encoder->priv_data, "crf", "20", 0), "set the encoder quality");
        Check(ffmpeg.av_opt_set(encoder->priv_data, "profile", "high", 0), "set the H.264 profile");
        Check(ffmpeg.avcodec_open2(encoder, codec, null), "open the H.264 encoder");
        if (encoder->extradata == null || encoder->extradata_size <= 0)
            throw new InvalidDataException("the H.264 encoder gave no parameter sets");
        return AvcC(new ReadOnlySpan<byte>(encoder->extradata, encoder->extradata_size));
    }

    private static void Decode(State s, AVPacket* packet)
    {
        int result = ffmpeg.avcodec_send_packet(s.Decoder, packet);
        if (result == -ffmpeg.EAGAIN)
        {
            Decoded(s);
            result = ffmpeg.avcodec_send_packet(s.Decoder, packet);
        }
        if (result != ffmpeg.AVERROR_INVALIDDATA && result != ffmpeg.AVERROR_EOF)
            Check(result, "decode a frame");
        Decoded(s);
    }

    private static void Decoded(State s)
    {
        while (true)
        {
            int result = ffmpeg.avcodec_receive_frame(s.Decoder, s.Frame);
            if (result == -ffmpeg.EAGAIN || result == ffmpeg.AVERROR_EOF)
                return;
            Check(result, "decode a frame");
            long pts = s.Frame->best_effort_timestamp;
            if (pts == ffmpeg.AV_NOPTS_VALUE)
                pts = s.LastPts == ffmpeg.AV_NOPTS_VALUE ? 0 : s.LastPts + Math.Max(1, s.Frame->duration);
            s.Frame->pts = s.LastPts = pts;
            Check(ffmpeg.av_buffersrc_add_frame_flags(s.Source, s.Frame, 0), "filter a frame");
            ffmpeg.av_frame_unref(s.Frame);
            Filtered(s);
        }
    }

    private static void Filtered(State s)
    {
        while (true)
        {
            int result = ffmpeg.av_buffersink_get_frame(s.Sink, s.Filtered);
            if (result == -ffmpeg.EAGAIN || result == ffmpeg.AVERROR_EOF)
                return;
            Check(result, "filter a frame");
            s.Filtered->pict_type = AVPictureType.None;
            s.Filtered->pts = s.Frames++;
            Encode(s, s.Filtered);
            ffmpeg.av_frame_unref(s.Filtered);
            if (s.Log != null && s.Clock.Elapsed - s.LastReport >= TimeSpan.FromSeconds(15))
            {
                s.LastReport = s.Clock.Elapsed;
                s.Log($"  {s.Name}: {s.Frames}{(s.ExpectedFrames > 0 ? $"/{s.ExpectedFrames}" : "")} frames");
            }
        }
    }

    private static void Encode(State s, AVFrame* picture)
    {
        Check(ffmpeg.avcodec_send_frame(s.Encoder, picture), "encode a frame");
        while (true)
        {
            int result = ffmpeg.avcodec_receive_packet(s.Encoder, s.Encoded);
            if (result == -ffmpeg.EAGAIN || result == ffmpeg.AVERROR_EOF)
                return;
            Check(result, "encode a frame");
            byte[] data = LengthPrefixed(new ReadOnlySpan<byte>(s.Encoded->data, s.Encoded->size));
            s.LargestFrame = Math.Max(s.LargestFrame, data.Length);
            s.Writer!.Write(s.Encoded->pts, (s.Encoded->flags & 1) != 0, data);
            ffmpeg.av_packet_unref(s.Encoded);
        }
    }

    internal static byte[] AvcC(ReadOnlySpan<byte> annexB)
    {
        byte[]? sps = null, pps = null;
        foreach ((int start, int end) in Nals(annexB))
        {
            int type = annexB[start] & 0x1F;
            if (type == 7)
                sps ??= annexB[start..end].ToArray();
            else if (type == 8)
                pps ??= annexB[start..end].ToArray();
        }
        if (sps == null || pps == null || sps.Length < 4)
            throw new InvalidDataException("the H.264 encoder gave no SPS and PPS");
        var record = new List<byte>(11 + sps.Length + pps.Length) { 1, sps[1], sps[2], sps[3], 0xFF, 0xE1, (byte)(sps.Length >> 8), (byte)sps.Length };
        record.AddRange(sps);
        record.AddRange([1, (byte)(pps.Length >> 8), (byte)pps.Length]);
        record.AddRange(pps);
        return record.ToArray();
    }

    internal static byte[] LengthPrefixed(ReadOnlySpan<byte> annexB)
    {
        List<(int Start, int End)> nals = Nals(annexB);
        if (nals.Count == 0)
            throw new InvalidDataException("the H.264 encoder gave a frame without NAL units");
        var data = new byte[nals.Sum(n => 4 + n.End - n.Start)];
        int at = 0;
        foreach ((int start, int end) in nals)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(at), end - start);
            annexB[start..end].CopyTo(data.AsSpan(at + 4));
            at += 4 + end - start;
        }
        return data;
    }

    private static List<(int Start, int End)> Nals(ReadOnlySpan<byte> annexB)
    {
        var nals = new List<(int, int)>();
        int start = -1;
        for (int i = 0; i + 3 <= annexB.Length;)
        {
            if (annexB[i] == 0 && annexB[i + 1] == 0 && annexB[i + 2] == 1)
            {
                if (start >= 0)
                    nals.Add((start, TrimZeros(annexB, start, i)));
                i += 3;
                start = i;
            }
            else
            {
                i++;
            }
        }
        if (start >= 0 && start < annexB.Length)
            nals.Add((start, TrimZeros(annexB, start, annexB.Length)));
        return nals;

        static int TrimZeros(ReadOnlySpan<byte> data, int start, int end)
        {
            while (end > start && data[end - 1] == 0)
                end--;
            return end;
        }
    }

    private static int Check(int result, string what)
    {
        if (result >= 0)
            return result;
        byte* text = stackalloc byte[256];
        ffmpeg.av_strerror(result, text, 256);
        throw new InvalidDataException($"could not {what}: {Marshal.PtrToStringAnsi((IntPtr)text)}");
    }
}
