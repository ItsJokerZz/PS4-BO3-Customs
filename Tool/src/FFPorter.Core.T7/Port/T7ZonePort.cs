using System.Text.Json;
using FFPorter.Core.Common.Native;
using FFPorter.Core.T7.Harness;
using FFPorter.Core.T7.Link;
using FFPorter.Core.T7.Port.Converters;

namespace FFPorter.Core.T7.Port;

public sealed class T7ZonePortOptions
{
    public required string PcFastFile { get; init; }
    public required string OutputFastFile { get; init; }
    public required string WorkDirectory { get; init; }
    public required string Ps4LoaderDirectory { get; init; }
    public required string PcImage { get; init; }
    public required T7DonorLibrary Donors { get; init; }
    public Action<string> Log { get; init; } = _ => { };

    public bool DropUnreferenced { get; init; }

    public bool DonorFallbackForAllTypes { get; init; }

    public string? PcXPak { get; init; }
    public string? PcIndexXPak { get; init; }
    public string? OutputXPak { get; init; }
    public string? OutputIndexXPak { get; init; }

    public Streams.T7Ps4StreamIndex? Ps4StreamIndex { get; init; }

    public Streams.T7PcStreamSource? PcStreamSource { get; init; }

    public IReadOnlyDictionary<string, string>? SoundRenames { get; init; }

    public T7PcShaderLibrary? ShaderLibrary { get; init; }

    public Streams.T7StreamMap? SharedStreams { get; init; }

    public string? Acts { get; init; }
    public bool GscRecompile { get; init; }

    public bool ApplyDelta { get; init; } = true;

    public Shaders.T7ShaderCompiler? ShaderCompiler { get; init; }

    public T7Fidelity? Fidelity { get; init; }
}

public sealed record T7ZonePortResult(bool Success, string OutputFastFile, IReadOnlyList<string> Problems, IReadOnlyDictionary<string, int> Strategies);

public static class T7ZonePort
{
    public static List<IT7AssetConverter> Converters { get; } =
        [new T7CopyConverter(), new T7SoundBankConverter(), new T7XModelConverter(), new T7ImageConverter(), new T7TechsetConverter(), new T7MaterialConverter(), new T7MiscConverter(), new T7GfxWorldConverter(), new T7SAnimConverter()];

    public static T7ZonePortResult Run(T7ZonePortOptions options)
    {
        Action<string> log = options.Log;
        T7Fidelity? fidelity = options.Fidelity;
        Directory.CreateDirectory(options.WorkDirectory);
        string stem = Path.GetFileNameWithoutExtension(options.PcFastFile);
        string zoneFile = Path.GetFileName(options.PcFastFile);
        string pcWalk = Path.Combine(options.WorkDirectory, stem + ".pc.t7walk");
        string pcFastFile = options.PcFastFile;
        fidelity?.Enter(T7Fidelity.ZonePhase(options.PcFastFile, "walk"), "Reading the PC zone", $"{zoneFile} with the PC game's loader");
        if (options.ApplyDelta)
        {
            try
            {
                pcFastFile = T7FastFileDelta.Resolve(options.PcFastFile, options.WorkDirectory, message => log($"[{stem}] {message}"));
            }
            catch (InvalidDataException error)
            {
                string problem = $"cannot apply {Path.ChangeExtension(Path.GetFileName(options.PcFastFile), ".fd")}: {error.Message}";
                fidelity?.Tracker.Problem($"{stem}: {problem}");
                fidelity?.ZoneNotWritten();
                return new T7ZonePortResult(false, options.OutputFastFile, [problem], new Dictionary<string, int>());
            }
        }
        log($"[{stem}] reading the PC zone with the PC loader");
        NativeProcessResult walked = NativeProcess.Run(T7PcLoader.TaskName, T7PcLoader.ChildArguments(options.PcImage, pcWalk, pcFastFile),
            new NativeProcessOptions { OnErrorLine = log });
        if (walked.ExitCode != 0)
        {
            string problem = $"PC loader walk failed ({walked.ExitCode}): {walked.Stderr.Trim()}";
            fidelity?.Tracker.Problem($"{stem}: {problem}");
            fidelity?.ZoneNotWritten();
            return new T7ZonePortResult(false, options.OutputFastFile, [problem], new Dictionary<string, int>());
        }
        log($"[{stem}] {walked.Stdout.Trim()}");
        fidelity?.Step(1);

        T7FastFile.Decoded decoded = T7FastFile.Load(pcFastFile);
        var pc = new T7WalkIndex("pc", decoded.Zone, T7Walk.Read(pcWalk));
        options.Donors.PreferredZone = stem;
        var builder = new T7ZoneBuilder { OutputName = T7Names.ToPs4 };
        builder.MapScriptStrings(pc, pc.List.ScriptStrings.Select(builder.AddScriptString).ToArray());

        int count = pc.Walk.Assets.Count;
        var outputIndex = Enumerable.Range(0, count).ToArray();
        var context = new T7PortContext
        {
            Pc = pc,
            Builder = builder,
            Donors = options.Donors,
            Log = log,
            OutputIndexOfPc = outputIndex,
            SoundRenames = options.SoundRenames ?? new Dictionary<string, string>(),
        };
        var converters = new List<IT7AssetConverter>(Converters);
        var donor = new T7DonorConverter(options.DonorFallbackForAllTypes
            ? Enumerable.Range(0, T7AssetTypes.Count)
            : [T7AssetTypes.TechniqueSet, T7AssetTypes.ComputeShaderSet]);
        converters.Add(donor);
        context.Converters = converters;
        context.Fidelity = fidelity;
        context.ShaderLibrary = options.ShaderLibrary;
        context.ShaderLibrary?.AddZone(pc);
        context.ShaderCompiler = options.ShaderCompiler;
        string opcodeMap = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(options.Ps4LoaderDirectory))!, "t7_gsc", "gsc_opcodes.json");
        if (File.Exists(opcodeMap))
            context.Gsc = Scripts.T7Gsc.Load(opcodeMap);
        string builtins = Path.Combine(Path.GetDirectoryName(opcodeMap)!, "ps4_builtins.json");
        if (File.Exists(builtins))
            context.GscBuiltins = Scripts.T7GscBuiltins.Load(builtins);
        else
            context.Warn(T7Issues.BuiltinsUnchecked, $"{builtins} is missing: scripts calling PC-only builtins are not caught");
        context.Acts = options.Acts != null && File.Exists(options.Acts) ? options.Acts : null;
        context.GscRecompile = options.GscRecompile;
        context.Fallouts.AddRange(T7TextureComboFallout.Compute(pc));
        foreach (T7TextureComboFallout fallout in context.Fallouts)
        {
            builder.NullFields.UnionWith(fallout.NullFields.Select(f => (pc, f)));
            log($"[{stem}] texturecombo '{fallout.Asset.Name}': {fallout.SubAssets.Count} sub-assets; " + string.Join(", ", fallout.Counts.OrderBy(p => p.Key).Select(p => $"{p.Key} x{p.Value}")));
        }
        ConvertStreams(options, context, converters, stem);
        if (options.SharedStreams != null)
        {
            context.Streams.Merge(options.SharedStreams);
            options.SharedStreams.Merge(context.Streams);
        }

        T7TechsetBuilder.Precompile(context, message => log($"[{stem}] {message}"));

        var outputs = new T7OutputAsset?[count];
        var failures = new List<(int Index, string Why)>();
        var strategies = new Dictionary<string, int>();
        fidelity?.Enter(T7Fidelity.ZonePhase(options.PcFastFile, "assets"), "Converting assets", $"{zoneFile} · {T7Fidelity.Of(0, count)}");
        for (int i = 0; i < count; i++)
        {
            T7Walk.Asset asset = pc.Walk.Assets[i];
            fidelity?.Step((double)i / count, $"{zoneFile} · {T7Fidelity.Of(i, count)}");
            if (context.Fallouts.Exists(f => f.DroppedEntries.Contains(i)))
            {
                strategies["dropped (texturecombo)"] = strategies.GetValueOrDefault("dropped (texturecombo)") + 1;
                fidelity?.LeftOut(T7Issues.TextureComboLeftOut);
                continue;
            }
            if (asset.Type == T7AssetTypes.Material && asset.Name?.EndsWith("|dup", StringComparison.Ordinal) == true)
            {
                strategies["dropped (|dup material)"] = strategies.GetValueOrDefault("dropped (|dup material)") + 1;
                fidelity?.LeftOut(T7Issues.DupMaterialsLeftOut);
                continue;
            }
            var output = new T7OutputAsset { Type = asset.Type, Name = asset.Name };
            SetHeader(pc, i, output);
            if (asset.Reads.Count == 0 && asset.Deferred.Count == 0)
            {
                long cell = pc.List.AssetTableOffset + 16L * i + 8;
                if (pc.TryReferencedName(cell, out int referencedType, out string referencedName) && referencedType == T7AssetTypes.Material
                    && referencedName.EndsWith("|dup", StringComparison.Ordinal))
                {
                    strategies["dropped (|dup material)"] = strategies.GetValueOrDefault("dropped (|dup material)") + 1;
                    fidelity?.LeftOut(T7Issues.DupMaterialsLeftOut);
                    continue;
                }
                fidelity?.BeginAsset(asset);
                if (TryRehome(context, cell, output, out string? rehomeProblem))
                {
                    strategies["re-homed from texturecombo"] = strategies.GetValueOrDefault("re-homed from texturecombo") + 1;
                    outputs[i] = output;
                    fidelity?.EndAsset(asset, "re-homed", null);
                    continue;
                }
                if (rehomeProblem != null)
                {
                    failures.Add((i, rehomeProblem));
                    fidelity?.EndAsset(asset, null, rehomeProblem);
                    continue;
                }
                fidelity?.AbandonAsset();
                output.Origin = "reference";
                outputs[i] = output;
                strategies["reference"] = strategies.GetValueOrDefault("reference") + 1;
                continue;
            }
            var reasons = new List<string>();
            bool done = false;
            fidelity?.BeginAsset(asset);
            foreach (IT7AssetConverter converter in converters)
            {
                bool converted;
                string? why;
                try
                {
                    converted = converter.TryConvert(context, asset, output, out why);
                }
                catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException))
                {
                    converted = false;
                    why = $"{error.GetType().Name}: {error.Message}";
                }
                if (converted)
                {
                    if (string.IsNullOrEmpty(output.Origin))
                        output.Origin = converter.Name;
                    string key = $"{T7AssetTypes.Name(asset.Type)}:{converter.Name}";
                    strategies[key] = strategies.GetValueOrDefault(key) + 1;
                    done = true;
                    fidelity?.EndAsset(asset, converter.Name, null);
                    break;
                }
                output.Main.Clear();
                output.Deferred.Clear();
                fidelity?.DiscardAssetIssues();
                foreach (string key in context.StringPools.Where(p => p.Value == i).Select(p => p.Key).ToList())
                    context.StringPools.Remove(key);
                if (why != null && converter is not T7CopyConverter)
                    reasons.Add($"{converter.Name}: {why}");
            }
            if (done)
            {
                outputs[i] = output;
            }
            else
            {
                string why = reasons.Count > 0 ? string.Join("; ", reasons) : $"no PC->PS4 converter for {T7AssetTypes.Name(asset.Type)} assets yet";
                failures.Add((i, why));
                fidelity?.EndAsset(asset, null, why);
            }
        }
        fidelity?.Step(1, $"{zoneFile} · {T7Fidelity.Of(count, count)}");

        var problems = new List<string>();
        if (failures.Count > 0)
        {
            HashSet<int> referenced = ReferencedAssets(pc);
            foreach ((int index, string why) in failures)
            {
                T7Walk.Asset asset = pc.Walk.Assets[index];
                string label = $"{T7AssetTypes.Name(asset.Type)} '{asset.Name}' (PC asset {index})";
                if (options.DropUnreferenced && !referenced.Contains(index))
                    context.Warnings.Add($"dropped unreferenced {label}: {why}");
                else
                    problems.Add($"cannot convert {label}: {why}");
            }
        }
        foreach (string warning in context.Warnings.Take(40))
            log($"[{stem}] warning: {warning}");
        string problemReport = Workspace.ReportPath(stem, ".problems.txt");
        File.Delete(problemReport);
        if (problems.Count > 0)
        {
            foreach (string problem in problems.Take(60))
                log($"[{stem}] {problem}");
            File.WriteAllLines(problemReport, problems);
            log($"[{stem}] {problems.Count} problems; the full list is in {problemReport}");
            ReportProblems(fidelity, stem, problems, $"{problems.Count} assets could not be converted, so {zoneFile} was not written");
            fidelity?.ZoneNotWritten();
            return new T7ZonePortResult(false, options.OutputFastFile, problems, strategies);
        }

        int next = 0;
        for (int i = 0; i < count; i++)
            outputIndex[i] = outputs[i] != null ? next++ : -1;
        for (int i = 0; i < count; i++)
        {
            if (outputs[i] == null)
                continue;
            builder.Assets.Add(outputs[i]!);
            builder.MapAsset(pc, i, outputIndex[i]);
        }
        RemapDonors(builder, pc, outputs, outputIndex, options.Donors);

        fidelity?.Enter(T7Fidelity.ZonePhase(options.PcFastFile, "link"), "Linking and checking", $"{zoneFile} with the PS4 game's loader");
        var linker = new T7Linker(options.Ps4LoaderDirectory, options.WorkDirectory, log)
        {
            Progress = fidelity == null ? null : (fraction, step) => fidelity.Step(fraction, $"{zoneFile} · {step}"),
        };
        T7Linker.Result linked;
        try
        {
            linked = linker.Build(builder, decoded.Header, decoded.Header.ZoneName, options.OutputFastFile);
        }
        catch (InvalidDataException error)
        {
            var copied = outputs.Where(o => o != null && o.Origin is "copy" or "misc" && T7CopyConverter.LoaderCheckedTypes.Contains(o.Type))
                .Select(o => T7AssetTypes.Name(o!.Type)).Distinct().Order().ToList();
            string problem = $"the PS4 loader could not read the converted zone: {error.Message}"
                + (copied.Count > 0 ? $" (it copies {string.Join(", ", copied)} assets, whose PS4 layout no sample confirms)" : "");
            log($"[{stem}] {problem}");
            File.WriteAllLines(problemReport, [problem]);
            fidelity?.Tracker.Problem($"{stem}: {problem}");
            fidelity?.ZoneNotWritten();
            return new T7ZonePortResult(false, options.OutputFastFile, [problem], strategies);
        }
        problems.AddRange(linked.Problems);
        problems.AddRange(CheckCopiedAssets(pc, linked.FinalWalk, outputs, outputIndex));
        int staleKeys = CountStaleStreamKeys(linked.Zone, context.Streams);
        if (staleKeys > 0)
            problems.Add($"{staleKeys} PC stream keys are still in the zone (their items changed on PS4; the owning assets' converters must write the PS4 keys)");
        if (problems.Count > 0)
            ReportProblems(fidelity, stem, problems, $"{zoneFile} was written but failed {problems.Count} checks");
        string report = Workspace.ReportPath(stem, ".port.json");
        File.WriteAllText(report, JsonSerializer.Serialize(new
        {
            source = options.PcFastFile,
            output = options.OutputFastFile,
            assets = builder.Assets.Count,
            dropped = context.Warnings,
            strategies,
            problems,
            zone_bytes = linked.Zone.Length,
            ps4_walk_passed = linked.FinalWalk.FinalFilePos == linked.Zone.Length,
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (problems.Count > 0)
            File.WriteAllLines(problemReport, problems);
        log($"[{stem}] wrote {options.OutputFastFile} ({linked.FastFile.Length} bytes); {problems.Count} problems");
        return new T7ZonePortResult(problems.Count == 0, options.OutputFastFile, problems, strategies);
    }

    private static void ReportProblems(T7Fidelity? fidelity, string stem, List<string> problems, string summary) =>
        fidelity?.Tracker.Problem($"{stem}: {summary}");

    private static void ConvertStreams(T7ZonePortOptions options, T7PortContext context, List<IT7AssetConverter> converters, string stem)
    {
        if (options.PcXPak == null || options.OutputXPak == null || !File.Exists(options.PcXPak))
            return;
        T7Fidelity? fidelity = options.Fidelity;
        string xpakFile = Path.GetFileName(options.PcXPak);
        fidelity?.Enter(T7Fidelity.ZonePhase(options.PcFastFile, "streams"), "Converting streamed data", $"{xpakFile} · planning");
        var plan = new Streams.T7StreamPlan();
        foreach (IT7StreamPlanner planner in converters.OfType<IT7StreamPlanner>())
        {
            plan.Owner = (planner as IT7AssetConverter)?.Name;
            foreach (T7Walk.Asset asset in context.Pc.Walk.Assets)
                planner.PlanStreams(context, asset, plan);
        }
        plan.Owner = null;
        options.Log($"[{stem}] converting streamed data {Path.GetFileName(options.PcXPak)} ({plan.Count} zone transforms)");
        Streams.T7StreamConvertResult result = Streams.T7StreamConvert.Run(new Streams.T7StreamConvertOptions
        {
            PcXPak = options.PcXPak,
            OutputXPak = options.OutputXPak,
            PcIndexXPak = options.PcIndexXPak,
            OutputIndexXPak = options.OutputIndexXPak,
            CacheDirectory = options.WorkDirectory,
            Plan = plan,
            Ps4Index = options.Ps4StreamIndex,
            KnownItems = options.SharedStreams,
            PcSource = options.PcStreamSource,
            Zone = context.Pc.Zone,
            Log = options.Log,
            Progress = fidelity == null ? null : (done, total) => fidelity.Step(
                total > 0 ? 0.9 * done / total : 0.9,
                total > 0 ? $"{xpakFile} · {T7Fidelity.Of(done, total)} items" : $"{xpakFile} · {done:N0} referenced items converted in"),
        });
        context.Streams.Merge(result.Map);
        result.Map.Save(Path.Combine(options.WorkDirectory, stem + ".keys.json"));
        foreach ((string what, int count) in result.Counts.OrderBy(p => p.Key))
            options.Log($"[{stem}]   {what} x{count}");
        foreach (IGrouping<string, string> group in result.Warnings.GroupBy(w => w.Contains("another xpak") ? "external" : w.Contains("unchanged") ? "unchanged" : "other"))
            context.Warnings.Add($"streams ({group.Key}): {group.Count()} items, e.g. {group.First()}");
        fidelity?.Streams(result, plan);
        fidelity?.Step(1, $"{xpakFile} · done");
    }

    private static bool TryRehome(T7PortContext context, long cell, T7OutputAsset output, out string? problem)
    {
        problem = null;
        foreach (T7TextureComboFallout fallout in context.Fallouts)
        {
            if (!fallout.RehomeFields.TryGetValue(cell, out T7TextureComboFallout.SubAsset? subAsset) || !fallout.Claim(subAsset))
                continue;
            output.HeaderTarget = null;
            output.HeaderMarker = ulong.MaxValue;
            output.Main.Clear();
            output.Deferred.Clear();
            try
            {
                var rewrite = new T7AssetRewrite(context, fallout.Asset, output, subAsset.FirstRead);
                ConvertSubAsset(context, rewrite, subAsset);
                rewrite.FinishAt(subAsset.EndRead);
                output.Origin = $"re-homed from texturecombo '{fallout.Asset.Name}'";
                return true;
            }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException or IOException)
            {
                problem = $"cannot re-home {T7AssetTypes.Name(subAsset.Type)} '{subAsset.Name}' out of the texturecombo: {error.Message}";
                return false;
            }
        }
        return false;
    }

    public static void ConvertSubAsset(T7PortContext context, T7AssetRewrite rewrite, T7TextureComboFallout.SubAsset subAsset)
    {
        switch (subAsset.Type)
        {
            case T7AssetTypes.Image:
                T7ImageConverter.ConvertImage(context, rewrite);
                break;
            case T7AssetTypes.TechniqueSet:
                if (T7TechsetConverter.IsStub(context.Pc.Zone, rewrite.Peek()) || context.ShaderCompiler == null)
                    T7TechsetConverter.ConvertStub(rewrite);
                else
                    T7TechsetBuilder.Convert(context, rewrite);
                break;
            default:
                if (context.NestedConverter(subAsset.Type) is { } nested)
                {
                    if (!nested.TryConvertNested(context, rewrite, subAsset.Type, subAsset.EndRead, out string? why))
                        throw new InvalidDataException(why ?? "nested conversion failed");
                }
                else
                {
                    throw new InvalidDataException($"no converter for a re-homed {T7AssetTypes.Name(subAsset.Type)}");
                }
                break;
        }
    }

    private static IEnumerable<string> CheckCopiedAssets(T7WalkIndex pc, T7Walk final, T7OutputAsset?[] outputs, int[] outputIndex)
    {
        for (int i = 0; i < outputs.Length; i++)
        {
            if (outputs[i] == null || outputIndex[i] < 0 || outputIndex[i] >= final.Assets.Count)
                continue;
            T7Walk.Asset source = pc.Walk.Assets[i], loaded = final.Assets[outputIndex[i]];
            string? difference;
            if (outputs[i]!.Origin == "copy")
            {
                difference = FirstReadDifference(source.Reads, loaded.Reads) ?? FirstReadDifference(source.Deferred, loaded.Deferred);
            }
            else if (outputs[i]!.Origin == "misc" && (T7CopyConverter.IdenticalTypes.Contains(source.Type) || T7MiscConverter.IdentityTypes.Contains(source.Type)))
            {
                difference = FirstReadDifference(T7CopyConverter.OwnReads(source), T7CopyConverter.OwnReads(loaded));
            }
            else
            {
                continue;
            }
            if (difference != null)
                yield return $"copied {T7AssetTypes.Name(source.Type)} '{source.Name}' loads differently on PS4 ({difference}): the type's layout is not the same on both platforms";
        }
    }

    private static string? FirstReadDifference(List<T7Walk.Span> pc, List<T7Walk.Span> ps4)
    {
        for (int r = 0; r < Math.Max(pc.Count, ps4.Count); r++)
        {
            if (r >= pc.Count || r >= ps4.Count)
                return $"PC loads {pc.Count} reads, PS4 {ps4.Count}";
            if (pc[r].Kind != ps4[r].Kind || pc[r].Size != ps4[r].Size || pc[r].At.Block != ps4[r].At.Block)
                return $"read {r}: PC {pc[r].Kind} of {pc[r].Size} bytes in block {pc[r].At.Block}, PS4 {ps4[r].Kind} of {ps4[r].Size} bytes in block {ps4[r].At.Block}";
        }
        return null;
    }

    private static int CountStaleStreamKeys(byte[] zone, Streams.T7StreamMap streams)
    {
        var changed = new HashSet<ulong>(streams.Items.Where(i => i.PcKey != i.Ps4Key).Select(i => i.PcKey));
        if (changed.Count == 0)
            return 0;
        int found = 0;
        ReadOnlySpan<byte> bytes = zone;
        for (int at = 0; at + 8 <= bytes.Length; at++)
        {
            if (changed.Contains(System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes[at..])))
                found++;
        }
        return found;
    }

    private static void SetHeader(T7WalkIndex pc, int index, T7OutputAsset output)
    {
        long cell = pc.List.AssetTableOffset + 16L * index + 8;
        if (pc.Pointers.TryGetValue(cell, out var pointer) && pointer.Kind is T7PointerKind.Packed or T7PointerKind.PackedAlias)
            output.HeaderTarget = new T7SourceTarget(pc, pointer.Target, cell);
        else
            output.HeaderMarker = pc.List.Assets[index].Header;
    }

    private static void RemapDonors(T7ZoneBuilder builder, T7WalkIndex pc, T7OutputAsset?[] outputs, int[] outputIndex, T7DonorLibrary donors)
    {
        var byName = new Dictionary<(int Type, string Name), int>();
        for (int i = 0; i < outputs.Length; i++)
        {
            T7OutputAsset? output = outputs[i];
            if (output == null || string.IsNullOrEmpty(output.Name))
                continue;
            byName.TryAdd((output.Type, T7Names.ToPs4(output.Type, output.Name)), i);
        }
        var usedZones = new HashSet<string>(outputs.Where(o => o != null && o.Origin.StartsWith("donor ", StringComparison.Ordinal))
            .Select(o => o!.Origin[6..].Split('#')[0]));
        foreach (T7WalkIndex zone in donors.Zones.Where(z => usedZones.Contains(z.Label)))
        {
            foreach (T7Walk.Asset asset in zone.Walk.Assets)
            {
                if (string.IsNullOrEmpty(asset.Name) || !byName.TryGetValue((asset.Type, asset.Name), out int pcIndex))
                    continue;
                builder.MapAsset(zone, asset.Index, outputIndex[pcIndex]);
                if (asset.Reads.Count > 0 && !outputs[pcIndex]!.Origin.StartsWith("donor ", StringComparison.Ordinal))
                    T7DonorConverter.AlignReads(builder, zone, asset, pc, pc.Walk.Assets[pcIndex]);
            }
        }
    }

    private static HashSet<int> ReferencedAssets(T7WalkIndex pc)
    {
        var referenced = new HashSet<int>();
        long tableStart = pc.List.AssetTableOffset, tableEnd = tableStart + 16L * pc.List.Assets.Count;
        foreach ((long field, (T7PointerKind kind, T7BlockAddress target)) in pc.Pointers)
        {
            if (kind is not (T7PointerKind.Packed or T7PointerKind.PackedAlias))
                continue;
            if (pc.TryAliasOwner(target, out int owner))
            {
                referenced.Add(owner);
                continue;
            }
            if (pc.TryFileOffset(target, out long offset, field))
            {
                if (offset >= tableStart && offset < tableEnd)
                    referenced.Add((int)((offset - tableStart) / 16));
                else
                {
                    int asset = pc.AssetAt(offset);
                    int from = pc.AssetAt(field);
                    if (asset >= 0 && asset != from)
                        referenced.Add(asset);
                }
            }
        }
        return referenced;
    }
}
