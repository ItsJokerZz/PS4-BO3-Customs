using System.Text.Json;
using FFPorter.Core.Common.Native;
using FFPorter.Core.T7.Harness;

namespace FFPorter.Core.T7.Link;

public sealed class T7Linker
{
    public sealed record Result(byte[] Zone, byte[] FastFile, T7Walk FinalWalk, IReadOnlyList<string> Problems, string FinalFastFilePath);

    private readonly string _loaderDirectory;
    private readonly string _workDirectory;
    private readonly Action<string> _log;

    public T7Linker(string loaderDirectory, string workDirectory, Action<string>? log = null)
    {
        _loaderDirectory = loaderDirectory;
        _workDirectory = workDirectory;
        _log = log ?? (_ => { });
        Directory.CreateDirectory(workDirectory);
    }

    public Action<double, string>? Progress { get; init; }

    public Result Build(T7ZoneBuilder builder, T7Header template, string zoneName, string outputFastFile)
    {
        Progress?.Invoke(0.02, "laying out the zone");
        byte[] zone = builder.Layout();
        _log($"laid out {zone.Length} zone bytes, {builder.Assets.Count} assets, {builder.Fixups.Count} pointer fixups");
        Progress?.Invoke(0.25, "checking the layout with the PS4 game's loader");

        var layoutHeader = new T7Header((byte[])template.Bytes.Clone()) { ZoneName = zoneName, Platform = 2 };
        string layoutFile = Path.Combine(_workDirectory, zoneName + ".layout.ff");
        File.WriteAllBytes(layoutFile, T7FastFile.Encode(layoutHeader, zone, System.IO.Compression.CompressionLevel.Fastest));
        string layoutWalkPath = Path.Combine(_workDirectory, zoneName + ".layout.t7walk");
        Walk(layoutFile, layoutWalkPath, layout: true);
        var layoutWalk = new T7WalkIndex("layout", zone, T7Walk.Read(layoutWalkPath));
        if (layoutWalk.Walk.FinalFilePos != zone.Length)
            throw new InvalidDataException($"layout walk consumed {layoutWalk.Walk.FinalFilePos} of {zone.Length} bytes");
        Progress?.Invoke(0.5, "linking pointers");

        List<string> problems = builder.Link(zone, layoutWalk);
        _log($"linked with {problems.Count} unresolved fixups");
        Progress?.Invoke(0.6, "compressing the fastfile");
        foreach (string problem in problems.Take(20))
            _log("  " + problem);

        var header = new T7Header((byte[])template.Bytes.Clone()) { ZoneName = zoneName, Platform = 2 };
        header.ArchiveChecksum = T7Ps4Loader.ArchiveChecksum(_loaderDirectory);
        long[] high = layoutWalk.Walk.HighWater;
        for (int b = 0; b < T7Header.BlockCount; b++)
        {
            ulong size = b switch
            {
                0 => (ulong)Math.Max(high[b] + 48, (long)template.BlockSize(0)),
                7 => 0,
                8 => template.BlockSize(8),
                _ => (ulong)high[b],
            };
            header.SetBlockSize(b, size);
        }
        byte[] fastFile = T7FastFile.Encode(header, zone);
        File.WriteAllBytes(outputFastFile, fastFile);
        Progress?.Invoke(0.8, "loading the written fastfile with the PS4 game's loader");
        string finalWalkPath = Path.Combine(_workDirectory, Path.GetFileNameWithoutExtension(outputFastFile) + ".ps4.t7walk");
        Walk(outputFastFile, finalWalkPath, layout: false);
        T7Walk finalWalk = T7Walk.Read(finalWalkPath);
        Progress?.Invoke(1, "checked");
        problems.AddRange(T7Ps4Loader.HeaderProblems(header, _loaderDirectory));
        for (int b = 0; b < T7Header.BlockCount; b++)
        {
            if (b is 0 or 7 or 8)
                continue;
            if ((ulong)finalWalk.HighWater[b] != header.BlockSize(b))
                problems.Add($"block {b} high water {finalWalk.HighWater[b]} != declared {header.BlockSize(b)}");
        }
        return new Result(zone, fastFile, finalWalk, problems, outputFastFile);
    }

    private void Walk(string fastFile, string walkPath, bool layout)
    {
        var environment = new Dictionary<string, string?> { ["T7_WALK_LAYOUT"] = layout ? "1" : null, ["T7_WALK_ANY_PLATFORM"] = null };
        NativeProcessResult result = NativeProcess.Run(T7Ps4Loader.TaskName, T7Ps4Loader.ChildArguments(_loaderDirectory, walkPath, fastFile),
            new NativeProcessOptions { Environment = environment });
        if (result.ExitCode != 0)
        {
            string log = File.Exists(walkPath + ".log.jsonl") ? string.Join("\n", File.ReadLines(walkPath + ".log.jsonl").TakeLast(4)) : "";
            throw new InvalidDataException($"PS4 loader walk of {Path.GetFileName(fastFile)} failed ({result.ExitCode}): {result.Stderr.Trim()} {log}");
        }
        _log($"PS4 walk {(layout ? "(layout) " : "")}{Path.GetFileName(fastFile)}: {result.Stdout.Trim()}");
    }
}
