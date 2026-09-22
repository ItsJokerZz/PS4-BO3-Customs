using System.Buffers.Binary;
using System.Text;
using FFPorter.Core.Common.Tools;
using FFPorter.Core.T7.Harness;
using FFPorter.Core.T7.Havok;
using FFPorter.Core.T7.Link;
using FFPorter.Core.T7.Scripts;

namespace FFPorter.Core.T7.Port.Converters;

public sealed class T7MiscConverter : IT7AssetConverter, IT7NestedConverter
{
    private const ulong Inline = ulong.MaxValue, InlineAlias = ulong.MaxValue - 1;
    private const int FxElemDefSize = 608;

    public static readonly HashSet<int> IdentityTypes =
    [
        T7AssetTypes.Weapon, T7AssetTypes.LightDescription, T7AssetTypes.AnimSelectorTableSet, T7AssetTypes.AnimStateMachine,
        T7AssetTypes.BehaviorTree,
    ];

    public string Name => "misc";

    public bool TryConvert(T7PortContext context, T7Walk.Asset asset, T7OutputAsset output, out string? reason)
    {
        reason = null;
        if (asset.Type is not (T7AssetTypes.ScriptParseTree or T7AssetTypes.NavMesh or T7AssetTypes.NavVolume or T7AssetTypes.Fx)
            && !IdentityTypes.Contains(asset.Type)
            && !(T7CopyConverter.IdenticalTypes.Contains(asset.Type) && T7CopyConverter.HasConvertedSubAssets(asset)))
            return false;
        var rewrite = new T7AssetRewrite(context, asset, output);
        try
        {
            switch (asset.Type)
            {
                case T7AssetTypes.ScriptParseTree:
                    Script(context, rewrite);
                    break;
                case T7AssetTypes.NavMesh:
                    Nav(rewrite, 104, 6);
                    break;
                case T7AssetTypes.NavVolume:
                    Nav(rewrite, 72, 4);
                    break;
                case T7AssetTypes.Fx:
                    Fx(context, rewrite);
                    break;
                default:
                    CopyWithSubAssets(context, rewrite, new HashSet<long>(), new HashSet<int>());
                    break;
            }
            foreach (T7Walk.Span deferred in asset.Deferred)
                rewrite.CopyPcDeferred(deferred);
            rewrite.Finish();
            return true;
        }
        catch (InvalidDataException error)
        {
            reason = error.Message;
            return false;
        }
    }


    private static void Script(T7PortContext context, T7AssetRewrite rewrite)
    {
        if (rewrite.Reads.Count != 3)
            throw new InvalidDataException($"{rewrite.Label}: a script parse tree has 3 reads, this one {rewrite.Reads.Count}");
        T7Gsc gsc = context.Gsc ?? throw new InvalidDataException("the GSC opcode map (app/data/t7_gsc/gsc_opcodes.json) is missing");
        T7Walk.Span header = rewrite.Take(24);
        T7StructBuilder headerBuilder = rewrite.Rebuild(header, 24).MoveAll();
        rewrite.Copy();
        T7Walk.Span bufferRead = rewrite.Take();
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(rewrite.Pc.Zone.AsSpan((int)header.FileOffset + 8));
        if (bufferRead.Size != length + 1L)
            throw new InvalidDataException($"{rewrite.Label}: script buffer is {bufferRead.Size} bytes, its header says {length}+1");
        byte[] converted = gsc.Convert(rewrite.Bytes(bufferRead)[..(int)length]);
        if (context.GscBuiltins != null)
        {
            IReadOnlyList<string> changes = context.GscBuiltins.Apply(converted, rewrite.Asset.Name ?? "script");
            foreach (string change in changes)
                context.Log($"script '{rewrite.Asset.Name}': {change}");
            if (changes.Count > 0)
                context.Fidelity?.Raise(T7Issues.BuiltinRewritten, $"{rewrite.Asset.Name}: {changes[0]}");
        }
        if (context.Acts != null)
            converted = ActsPass(context, rewrite.Asset.Name ?? "script", converted);
        context.Fidelity?.Raise(context.Acts == null ? T7Issues.ScriptUnchecked : context.GscRecompile ? T7Issues.ScriptRecompiled : T7Issues.ScriptChecked, rewrite.Asset.Name);
        headerBuilder.U32(8, (uint)converted.Length);
        headerBuilder.Commit();
        T7StructBuilder buffer = rewrite.Rebuild(bufferRead, converted.Length + 1);
        buffer.Write(0, converted).MapSource(0, 0, 1);
        buffer.Commit();
    }

    private static byte[] ActsPass(T7PortContext context, string name, byte[] ps4)
    {
        string work = Path.Combine(Path.GetTempPath(), "ffport-t7-gsc", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            bool client = name.EndsWith(".csc", StringComparison.OrdinalIgnoreCase);
            string file = Path.Combine(work, client ? "script.cscc" : "script.gscc");
            File.WriteAllBytes(file, ps4);
            string decompiled = Path.Combine(work, "decompiled");
            RunActs(context.Acts!, ["gscd", "-g", "-t", "ps", "--path-output", "-o", decompiled, file], work, name, "decompile");
            string? source = Directory.Exists(decompiled)
                ? Directory.EnumerateFiles(decompiled, "*.gsc", SearchOption.AllDirectories).FirstOrDefault()
                : null;
            if (source == null)
                throw new InvalidDataException($"acts decompiled nothing for script '{name}'");
            if (!context.GscRecompile)
                return ps4;
            string again = Path.Combine(work, client ? "script.csc" : "script.gsc");
            File.Copy(source, again, overwrite: true);
            string output = Path.Combine(work, "recompiled");
            RunActs(context.Acts!, ["gscc", "-g", "bo3", "-p", "ps", "-o", output, again], work, name, "recompile");
            string compiled = output + (client ? ".cscc" : ".gscc");
            return File.Exists(compiled) ? File.ReadAllBytes(compiled) : throw new InvalidDataException($"acts compiled nothing for script '{name}'");
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void RunActs(string tool, IReadOnlyList<string> arguments, string work, string name, string what)
    {
        ProcessRunner.Result result;
        try
        {
            result = ProcessRunner.Run(tool, ["-t", "--noUpdater", .. arguments], work, TimeSpan.FromMinutes(2));
        }
        catch (IOException error)
        {
            throw new InvalidDataException(error.Message, error);
        }
        if (result.TimedOut)
            throw new InvalidDataException($"acts {what} timed out on script '{name}'");
        if (result.ExitCode != 0)
            throw new InvalidDataException($"acts could not {what} script '{name}': {result.Output.Trim()}");
    }


    private static void Nav(T7AssetRewrite rewrite, int headerSize, int blobs)
    {
        T7WalkIndex pc = rewrite.Pc;
        byte[] zone = pc.Zone;
        T7Walk.Span header = rewrite.Take(headerSize);
        T7StructBuilder headerBuilder = rewrite.Rebuild(header, headerSize).MoveAll();
        var byAddress = new Dictionary<T7BlockAddress, int>();
        for (int i = 0; i < rewrite.Reads.Count; i++)
            byAddress.TryAdd(rewrite.Reads[i].At, i);
        var converted = new Dictionary<int, byte[]>();
        for (int b = 0; b < blobs; b++)
        {
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(zone.AsSpan((int)header.FileOffset + 8 + 16 * b));
            long field = header.FileOffset + 16 + 16 * b;
            if (size == 0 || !pc.Pointers.TryGetValue(field, out var pointer))
                continue;
            if (pointer.Kind is not (T7PointerKind.Inline or T7PointerKind.InlineAlias) || !byAddress.TryGetValue(pointer.Target, out int readIndex))
                throw new InvalidDataException($"{rewrite.Label}: nav blob {b} is not loaded inline");
            T7Walk.Span blob = rewrite.Reads[readIndex];
            if (blob.Size != size)
                throw new InvalidDataException($"{rewrite.Label}: nav blob {b} read is {blob.Size} bytes, the header says {size}");
            byte[] ps4 = T7HavokNav.ConvertPackfile(zone.AsSpan((int)blob.FileOffset, (int)blob.Size));
            converted[readIndex] = ps4;
            headerBuilder.U32(8 + 16 * b, (uint)ps4.Length);
        }
        headerBuilder.Commit();
        while (!rewrite.Done)
        {
            int index = rewrite.Position;
            if (converted.TryGetValue(index, out byte[]? ps4))
            {
                T7Walk.Span blob = rewrite.Take();
                T7StructBuilder builder = rewrite.Rebuild(blob, ps4.Length);
                builder.Write(0, ps4).MapSource(0, 0, 1);
                builder.Commit();
            }
            else
            {
                rewrite.Copy();
            }
        }
    }


    private static void Fx(T7PortContext context, T7AssetRewrite rewrite) => Fx(context, rewrite, 0, rewrite.Reads.Count, -1);

    public bool CanConvertNested(int type) => type == T7AssetTypes.Fx;

    public bool TryConvertNested(T7PortContext context, T7AssetRewrite rewrite, int type, int endRead, out string? reason)
    {
        reason = null;
        int first = rewrite.Position;
        var own = T7CopyConverter.AllSpans(rewrite.Asset).FirstOrDefault(s => s.First == first && s.End == endRead && s.Registration.Type == type);
        if (own.End != endRead)
        {
            reason = $"no registration covers reads {first}..{endRead}";
            return false;
        }
        try
        {
            Fx(context, rewrite, first, endRead, own.Index);
            return true;
        }
        catch (InvalidDataException error)
        {
            reason = error.Message;
            return false;
        }
    }

    private static void Fx(T7PortContext context, T7AssetRewrite rewrite, int first, int end, int ownRegistration)
    {
        T7WalkIndex pc = rewrite.Pc;
        byte[] zone = pc.Zone;
        var byAddress = new Dictionary<T7BlockAddress, int>();
        for (int i = end - 1; i >= first; i--)
            byAddress[rewrite.Reads[i].At] = i;
        var nullFields = new HashSet<long>();
        var droppedRegistrations = new HashSet<int>();
        T7Walk.Span root = rewrite.Reads[first];
        if (pc.Pointers.TryGetValue(root.FileOffset + 0x20, out var elements) && elements.Kind is T7PointerKind.Inline or T7PointerKind.InlineAlias
            && byAddress.TryGetValue(elements.Target, out int elementRead) && rewrite.Reads[elementRead].Size % FxElemDefSize == 0)
        {
            T7Walk.Span array = rewrite.Reads[elementRead];
            for (long e = 0; e < array.Size; e += FxElemDefSize)
            {
                long element = array.FileOffset + e;
                byte type = zone[element + 200], count = zone[element + 201];
                long field = element + 0x130;
                var slots = new List<long>();
                if (type == 13 || count > 1)
                {
                    if (!pc.Pointers.TryGetValue(field, out var visuals) || visuals.Kind is not (T7PointerKind.Inline or T7PointerKind.InlineAlias)
                        || !byAddress.TryGetValue(visuals.Target, out int visualsRead))
                        continue;
                    long baseOffset = rewrite.Reads[visualsRead].FileOffset;
                    for (int i = 0; i < count; i++)
                    {
                        if (type == 13)
                            slots.AddRange([baseOffset + 24 * i, baseOffset + 24 * i + 8, baseOffset + 24 * i + 16]);
                        else
                            slots.Add(baseOffset + 64 * i);
                    }
                }
                else if (count == 1)
                {
                    slots.Add(field);
                }
                foreach (long slot in slots)
                {
                    if (BinaryPrimitives.ReadUInt64LittleEndian(zone.AsSpan((int)slot)) == 0)
                        continue;
                    nullFields.Add(slot);
                    if (pc.TryInlineRegistration(slot, out int owner, out T7Walk.Registration registration) && owner == rewrite.Asset.Index)
                        droppedRegistrations.Add(rewrite.Asset.Registrations.IndexOf(registration));
                }
            }
        }
        List<(int First, int End, T7Walk.Registration Registration, int Index)> spans = T7CopyConverter.AllSpans(rewrite.Asset);
        CopyRange(context, rewrite, nullFields, droppedRegistrations, spans, T7CopyConverter.Outermost(spans, first, end, ownRegistration), end);
    }


    private static void CopyWithSubAssets(T7PortContext context, T7AssetRewrite rewrite, HashSet<long> nullFields, HashSet<int> dropped)
    {
        List<(int First, int End, T7Walk.Registration Registration, int Index)> spans = T7CopyConverter.AllSpans(rewrite.Asset);
        CopyRange(context, rewrite, nullFields, dropped, spans, T7CopyConverter.Outermost(spans, 0, rewrite.Reads.Count, -1), rewrite.Reads.Count);
    }

    private static void CopyRange(T7PortContext context, T7AssetRewrite rewrite, HashSet<long> nullFields, HashSet<int> dropped,
        List<(int First, int End, T7Walk.Registration Registration, int Index)> spans,
        List<(int First, int End, T7Walk.Registration Registration, int Index)> outer, int end)
    {
        int next = 0;
        while (!rewrite.Done && rewrite.Position < end)
        {
            int position = rewrite.Position;
            if (next < outer.Count && outer[next].First == position)
            {
                var span = outer[next++];
                int type = span.Registration.Type;
                if (dropped.Contains(span.Index))
                {
                    while (rewrite.Position < span.End)
                        rewrite.Take();
                    continue;
                }
                if (T7CopyConverter.IdenticalTypes.Contains(type))
                {
                    List<(int First, int End, T7Walk.Registration Registration, int Index)> inner = T7CopyConverter.Outermost(spans, span.First, span.End, span.Index);
                    if (inner.Count == 0)
                    {
                        while (rewrite.Position < span.End)
                            rewrite.Copy();
                    }
                    else
                    {
                        CopyRange(context, rewrite, nullFields, dropped, spans, inner, span.End);
                    }
                    continue;
                }
                IT7NestedConverter nested = context.NestedConverter(type)
                    ?? throw new InvalidDataException($"{rewrite.Label}: loads {T7AssetTypes.Name(type)} '{span.Registration.Name}' inline, which has no converter");
                if (!nested.TryConvertNested(context, rewrite, type, span.End, out string? why))
                    throw new InvalidDataException($"{rewrite.Label}: inline {T7AssetTypes.Name(type)} '{span.Registration.Name}': {why}");
                if (rewrite.Position != span.End)
                    throw new InvalidDataException($"{rewrite.Label}: inline {T7AssetTypes.Name(type)} '{span.Registration.Name}' converted to read {rewrite.Position}, it ends at {span.End}");
                continue;
            }
            T7Walk.Span read = rewrite.Peek();
            var nulls = nullFields.Where(f => f >= read.FileOffset && f + 8 <= read.FileOffset + read.Size).ToList();
            if (nulls.Count == 0)
            {
                rewrite.Copy();
                continue;
            }
            rewrite.Take();
            T7StructBuilder builder = rewrite.Rebuild(read, (int)read.Size).MoveAll();
            foreach (long field in nulls)
                builder.Marker((int)(field - read.FileOffset), 0);
            builder.Commit();
        }
    }
}
