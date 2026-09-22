using System.Text.Json;
using FFPorter.Core.Common.Fidelity;
using FFPorter.Core.T7.Sound;
using FFPorter.Core.T7.Streams;

namespace FFPorter.Core.T7.Port;

public sealed class T7MapPortOptions
{
    public required string PcMapFastFile { get; init; }
    public required string OutputFolder { get; init; }
    public required string WorkDirectory { get; init; }
    public required string Ps4LoaderDirectory { get; init; }
    public required string PcImage { get; init; }
    public required T7DonorLibrary Donors { get; init; }
    public required Workspace Workspace { get; init; }
    public T7Ps4StreamIndex? Ps4StreamIndex { get; init; }
    public IReadOnlyList<string>? PcStreamXPaks { get; init; }
    public T7PcShaderLibrary? ShaderLibrary { get; init; }
    public string? Acts { get; init; }
    public bool GscRecompile { get; init; }
    public bool Force { get; init; }
    public bool DonorFallbackForAllTypes { get; init; }
    public bool ConvertStreams { get; init; } = true;
    public bool ConvertSound { get; init; } = true;
    public bool ConvertMovies { get; init; } = true;
    public bool ApplyDelta { get; init; } = true;
    public IReadOnlyCollection<string>? Languages { get; init; }
    public Shaders.T7ShaderCompiler? ShaderCompiler { get; init; }
    public Action<string> Log { get; init; } = _ => { };

    public T7Fidelity? Fidelity { get; init; }
}

public sealed record T7MapPortResult(bool Success, IReadOnlyList<string> Outputs, IReadOnlyList<string> Problems, IReadOnlyList<string> Warnings)
{
    public FidelitySnapshot? Fidelity { get; init; }
}

public static class T7MapPort
{
    public sealed record Companions(IReadOnlyList<string> Zones, IReadOnlyList<string> SoundBanks);

    public static Companions Find(string pcMapFastFile, IReadOnlyCollection<string>? languages = null)
    {
        string stem = Path.GetFileNameWithoutExtension(pcMapFastFile);
        string folder = Path.GetDirectoryName(Path.GetFullPath(pcMapFastFile))!;
        bool Wanted(string language) => languages == null || language.Equals("all", StringComparison.OrdinalIgnoreCase)
            || languages.Contains(language, StringComparer.OrdinalIgnoreCase);
        var zones = new List<string> { Path.GetFullPath(pcMapFastFile) };
        zones.AddRange(Directory.EnumerateFiles(folder, "*_" + stem + ".ff")
            .Where(f => Path.GetFileName(f).Length == stem.Length + 6 && Wanted(Path.GetFileName(f)[..2]))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        var banks = new List<string>();
        string sound = Path.Combine(folder, "snd");
        if (Directory.Exists(sound))
        {
            banks.AddRange(Directory.EnumerateFiles(sound, stem + ".*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".sabl", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".sabs", StringComparison.OrdinalIgnoreCase))
                .Where(f => Wanted(Path.GetExtension(Path.GetFileNameWithoutExtension(f)).TrimStart('.')))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        }
        return new Companions(zones, banks);
    }

    public static T7MapPortResult Run(T7MapPortOptions options)
    {
        Action<string> log = options.Log;
        string input = Path.GetFullPath(options.PcMapFastFile);
        string folder = Path.GetDirectoryName(input)!;
        string stem = Path.GetFileNameWithoutExtension(input);
        Companions companions = Find(input, options.Languages);
        var outputs = new List<string>();
        var problems = new List<string>();
        var warnings = new List<string>();
        T7Fidelity fidelity = options.Fidelity ?? new T7Fidelity(stem, null);
        Directory.CreateDirectory(options.OutputFolder);
        Directory.CreateDirectory(options.WorkDirectory);

        foreach (string zone in companions.Zones)
        {
            string target = Path.Combine(options.OutputFolder, Path.GetFileName(zone));
            if (File.Exists(target) && !options.Force)
            {
                string problem = $"{target} exists; replace is off";
                fidelity.Tracker.Problem(problem);
                FidelitySnapshot stopped = fidelity.Tracker.Finish(FidelityStates.Failed, $"Nothing was converted: {Path.GetFileName(target)} is already in the output folder and replacing is off.");
                return new T7MapPortResult(false, outputs, [problem], warnings) { Fidelity = stopped };
            }
        }

        string[] roots = UsermapRoots(folder);
        IReadOnlyList<string> movies = options.ConvertMovies && IsUsermap(folder, roots) ? MovieSources(roots) : [];
        fidelity.Plan(options.ConvertSound ? companions.SoundBanks : [],
            companions.Zones.Select(zone => (zone, options.ConvertStreams && File.Exists(Path.ChangeExtension(zone, ".xpak")) ? Path.ChangeExtension(zone, ".xpak") : null)),
            movies);

        var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sharedStreams = new T7StreamMap();
        if (options.ConvertSound && companions.SoundBanks.Count > 0)
            ConvertSoundBanks(options, folder, companions.SoundBanks, renames, outputs, problems, fidelity);
        else if (companions.SoundBanks.Count > 0)
            fidelity.SoundOff(companions.SoundBanks.Count);

        using T7PcStreamSource? pcStreams = options.ConvertStreams && options.PcStreamXPaks != null
            ? new T7PcStreamSource(Directory.EnumerateFiles(folder, "*.xpak").Order(StringComparer.OrdinalIgnoreCase).Concat(options.PcStreamXPaks), log)
            : null;
        foreach (string zone in companions.Zones)
        {
            string zoneStem = Path.GetFileNameWithoutExtension(zone);
            string target = Path.Combine(options.OutputFolder, Path.GetFileName(zone));
            string xpak = Path.Combine(folder, zoneStem + ".xpak");
            string indexXPak = Path.Combine(folder, zoneStem + "_d.xpak");
            bool streams = options.ConvertStreams && File.Exists(xpak);
            T7ZonePortResult result = T7ZonePort.Run(new T7ZonePortOptions
            {
                PcFastFile = zone,
                OutputFastFile = target,
                WorkDirectory = options.WorkDirectory,
                Ps4LoaderDirectory = options.Ps4LoaderDirectory,
                PcImage = options.PcImage,
                Donors = options.Donors,
                Log = log,
                DonorFallbackForAllTypes = options.DonorFallbackForAllTypes,
                PcXPak = streams ? xpak : null,
                OutputXPak = streams ? Path.Combine(options.OutputFolder, zoneStem + ".xpak") : null,
                PcIndexXPak = streams && File.Exists(indexXPak) ? indexXPak : null,
                OutputIndexXPak = streams && File.Exists(indexXPak) ? Path.Combine(options.OutputFolder, zoneStem + "_d.xpak") : null,
                Ps4StreamIndex = options.Ps4StreamIndex,
                PcStreamSource = pcStreams,
                SoundRenames = renames,
                ShaderLibrary = options.ShaderLibrary,
                SharedStreams = zone == companions.Zones[0] ? sharedStreams : sharedStreams.Copy(),
                Acts = options.Acts,
                GscRecompile = options.GscRecompile,
                ApplyDelta = options.ApplyDelta,
                ShaderCompiler = options.ShaderCompiler,
                Fidelity = fidelity,
            });
            log($"{Path.GetFileName(zone)}: {(result.Success ? "converted" : "FAILED")}");
            foreach ((string key, int value) in result.Strategies.OrderBy(p => p.Key))
                log($"  {key} x{value}");
            if (result.Success)
            {
                outputs.Add(target);
                if (streams)
                    outputs.Add(Path.Combine(options.OutputFolder, zoneStem + ".xpak"));
            }
            problems.AddRange(result.Problems.Select(p => $"{zoneStem}: {p}"));
        }
        CopyUsermapFiles(options, roots, outputs);
        if (movies.Count > 0)
            ConvertMovies(options, movies, outputs, warnings, fidelity);

        File.WriteAllText(Workspace.ReportPath(Path.GetFileNameWithoutExtension(input), ".map-port.json"), JsonSerializer.Serialize(new
        {
            source = input,
            zones = companions.Zones,
            sound_banks = companions.SoundBanks,
            outputs,
            renamed_sounds = renames.Count,
            problems,
            warnings,
        }, new JsonSerializerOptions { WriteIndented = true }));
        FidelitySnapshot report = fidelity.Tracker.Finish(problems.Count == 0 ? FidelityStates.Done : FidelityStates.Failed,
            fidelity.Tracker.Headline(built: fidelity.ZonesNotWritten == 0, problems.Count));
        try
        {
            FidelityProtocol.Save(Workspace.ReportPath(stem, ".fidelity.json"), report);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            log($"could not write {stem}.fidelity.json: {error.Message}");
        }
        return new T7MapPortResult(problems.Count == 0, outputs, problems, warnings) { Fidelity = report };
    }

    private static string[] UsermapRoots(string folder) =>
        Path.GetFileName(folder).Equals("zone", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(folder) is { } parent
            ? [folder, parent]
            : [folder];

    private static bool IsUsermap(string folder, string[] roots) =>
        roots.Any(root => File.Exists(Path.Combine(root, "workshop.json")))
        || Path.GetFullPath(folder).Split(Path.DirectorySeparatorChar).Contains("usermaps", StringComparer.OrdinalIgnoreCase);

    private static void CopyUsermapFiles(T7MapPortOptions options, string[] roots, List<string> outputs)
    {
        foreach (string relative in (string[])["workshop.json", "previewimage.png", "loadingimage.png"])
        {
            string? source = roots.Select(root => Path.Combine(root, relative)).FirstOrDefault(File.Exists);
            if (source == null)
                continue;
            string target = Path.Combine(options.OutputFolder, relative);
            File.Copy(source, target, overwrite: true);
            outputs.Add(target);
        }
    }

    private static IReadOnlyList<string> MovieSources(string[] roots)
    {
        string? videos = roots.Select(root => Path.Combine(root, "video")).FirstOrDefault(Directory.Exists);
        if (videos == null)
            return [];
        return Directory.EnumerateFiles(videos)
            .Where(file => Formats.T7MovieTranscoder.Extensions.Contains(Path.GetExtension(file)))
            .GroupBy(file => Path.GetFileNameWithoutExtension(file), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(file => Path.GetExtension(file).Equals(".mkv", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(file => file, StringComparer.OrdinalIgnoreCase).First())
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ConvertMovies(T7MapPortOptions options, IReadOnlyList<string> sources, List<string> outputs, List<string> warnings, T7Fidelity fidelity)
    {
        foreach (string source in sources)
        {
            string relative = Path.Combine("video", Path.GetFileNameWithoutExtension(source) + ".mkv");
            string target = Path.Combine(options.OutputFolder, relative);
            if (Path.GetFullPath(target).Equals(Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
                continue;
            fidelity.Enter("movie:" + Path.GetFileName(source), "Preparing movies", relative);
            try
            {
                Formats.T7MovieTranscoder.Outcome outcome = Formats.T7MovieTranscoder.Prepare(source, target, options.Log);
                outputs.Add(target);
                fidelity.Movie(Path.GetFileName(source), outcome);
            }
            catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                warnings.Add($"{relative} was not converted: {error.Message}");
                fidelity.MovieFailed(Path.GetFileName(source), error.Message);
            }
            fidelity.Step(1);
        }
    }

    private static void ConvertSoundBanks(T7MapPortOptions options, string folder, IReadOnlyList<string> banks, Dictionary<string, string> renames, List<string> outputs, List<string> problems,
        T7Fidelity fidelity)
    {
        Lame lame;
        try
        {
            lame = Lame.Load(options.Workspace);
        }
        catch (FileNotFoundException error)
        {
            problems.Add($"sound banks not converted: {error.Message}");
            fidelity.Tracker.Problem($"sound banks not converted: {error.Message}");
            foreach (string bank in banks)
                fidelity.SoundBankFailed(Path.GetRelativePath(folder, bank), null, error.Message);
            return;
        }
        using (lame)
        {
            foreach (string bank in banks)
            {
                string relative = Path.GetRelativePath(folder, bank);
                string target = Path.Combine(options.OutputFolder, relative);
                options.Log($"sound bank {relative}");
                fidelity.Enter(T7Fidelity.BankPhase(bank), "Converting sound", relative);
                int? entries = null;
                try
                {
                    T7SoundBank pc = T7SoundBank.Parse(File.ReadAllBytes(bank));
                    entries = pc.Entries.Count;
                    T7SoundConvert.Result result = T7SoundConvert.Convert(pc, options.Workspace, lame, options.Log,
                        (done, total) => fidelity.Step((double)done / total, $"{relative} · {T7Fidelity.Of(done, total)} sounds"));
                    foreach ((string from, string to) in result.RenamedAssets)
                        renames[from] = to;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllBytes(target, result.Bank.Build());
                    outputs.Add(target);
                    foreach (string note in result.Notes.Take(10))
                        options.Log($"  {note}");
                    fidelity.SoundBank(pc.Entries.Count, result.Reencoded, result.Resampled, result.Retimed);
                }
                catch (Exception error) when (error is InvalidDataException or IOException or InvalidOperationException)
                {
                    problems.Add($"sound bank {relative}: {error.Message}");
                    fidelity.SoundBankFailed(relative, entries, error.Message);
                    fidelity.Tracker.Problem($"sound bank {relative}: {error.Message}");
                }
                fidelity.Step(1);
            }
        }
    }
}
