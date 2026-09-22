using System.Buffers.Binary;
using System.Text;
using FFPorter.Core.T7.Harness;
using FFPorter.Core.T7.Link;
using FFPorter.Core.T7.Shaders;

namespace FFPorter.Core.T7.Port.Converters;

public sealed class T7MaterialConverter : IT7AssetConverter, IT7NestedConverter
{
    public const int PcSize = 672, Ps4Size = 664, PcBufferSize = 72, Ps4BufferSize = 40;
    private const ulong Inline = ulong.MaxValue, InlineAlias = ulong.MaxValue - 1;

    public string Name => "material";

    public bool TryConvert(T7PortContext context, T7Walk.Asset asset, T7OutputAsset output, out string? reason)
    {
        reason = null;
        if (asset.Type != T7AssetTypes.Material)
            return false;
        var rewrite = new T7AssetRewrite(context, asset, output);
        try
        {
            Convert(context, rewrite);
            rewrite.Finish();
            return true;
        }
        catch (InvalidDataException error)
        {
            reason = error.Message;
            return false;
        }
    }

    public bool CanConvertNested(int type) => type == T7AssetTypes.Material;

    public bool TryConvertNested(T7PortContext context, T7AssetRewrite rewrite, int type, int endRead, out string? reason)
    {
        reason = null;
        try
        {
            Convert(context, rewrite);
            return true;
        }
        catch (InvalidDataException error)
        {
            reason = error.Message;
            return false;
        }
    }

    public static void Convert(T7PortContext context, T7AssetRewrite rewrite)
    {
        T7WalkIndex pc = context.Pc;
        byte[] zone = pc.Zone;
        ulong Q(long at) => BinaryPrimitives.ReadUInt64LittleEndian(zone.AsSpan((int)at));

        T7Walk.Span rootRead = rewrite.Take(PcSize);
        long root = rootRead.FileOffset;
        T7StructBuilder material = rewrite.Rebuild(rootRead, Ps4Size);
        material.Move(0, 0, 0x20).Move(0x24, 0x20, 8).Move(0x30, 0x28, 0x270);
        string materialName = "";
        if (Q(root) == Inline)
        {
            T7Walk.Span nameRead = rewrite.Peek();
            materialName = Encoding.Latin1.GetString(rewrite.Bytes(nameRead)[..^1]);
            rewrite.Copy();
        }
        string? techset = pc.TryReferencedName(root + 0x278, out _, out string referenced) ? referenced : null;

        for (int i = 0; i < 12; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                for (int k = 0; k < 2; k++)
                {
                    long field = root + 0x30 + i * 48 + j * 16 + k * 8;
                    ulong value = Q(field);
                    if (value == 0)
                        continue;
                    if (value == InlineAlias)
                        throw new InvalidDataException($"{rewrite.Label}: constant buffer slot ({i},{j},{k}) uses an alias marker");
                    ConstantBuffer(context, rewrite, material, root, (int)(field - root - 8), field, value == Inline, techset, i, j, k, materialName);
                }
            }
        }

        ulong techsetValue = Q(root + 0x278);
        if (techsetValue is Inline or InlineAlias)
            InlineTechset(context, rewrite, techset);

        Textures(context, rewrite, material, root);
        Table(context, rewrite, material, root + 0x288, 0x280, 8 * zone[root + 0x271]);
        Table(context, rewrite, material, root + 0x290, 0x288, 32 * zone[root + 0x272]);

        if (Q(root + 0x298) is Inline or InlineAlias)
            Convert(context, rewrite);
        material.Commit();
    }

    private static void ConstantBuffer(T7PortContext context, T7AssetRewrite rewrite, T7StructBuilder material, long root, int ps4Field, long field,
        bool inlineStruct, string? techset, int technique, int stage, int pass, string materialName)
    {
        T7WalkIndex pc = context.Pc;
        byte[] zone = pc.Zone;
        T7Walk.Span? structRead = null;
        long buffer;
        if (inlineStruct)
        {
            structRead = rewrite.Take(PcBufferSize);
            buffer = structRead.Value.FileOffset;
        }
        else if (!pc.TryFollow(field, out buffer))
        {
            throw new InvalidDataException($"{rewrite.Label}: constant buffer pointer at 0x{field:x} does not resolve");
        }
        ulong dataPointer = BinaryPrimitives.ReadUInt64LittleEndian(zone.AsSpan((int)buffer + 40));
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(zone.AsSpan((int)buffer + 48));
        ulong namePointer = BinaryPrimitives.ReadUInt64LittleEndian(zone.AsSpan((int)buffer + 56));

        byte[] pcData = [];
        T7Walk.Span? dataRead = null;
        long nameAt = -1;
        if (dataPointer != 0 && size != 0)
        {
            if (dataPointer == Inline)
            {
                if (inlineStruct)
                {
                    dataRead = rewrite.Take(size);
                    pcData = rewrite.Bytes(dataRead.Value).ToArray();
                }
                else
                {
                    pcData = zone.AsSpan((int)buffer + PcBufferSize, (int)size).ToArray();
                }
            }
            else if (pc.TryFollow(buffer + 40, out long dataAt))
            {
                pcData = zone.AsSpan((int)dataAt, (int)size).ToArray();
            }
            else
            {
                throw new InvalidDataException($"{rewrite.Label}: constant buffer data pointer does not resolve");
            }
        }
        T7Walk.Span? nameRead = null;
        if (namePointer == Inline)
        {
            if (inlineStruct)
            {
                nameRead = rewrite.Take(T7WalkKind.String);
                nameAt = nameRead.Value.FileOffset;
            }
            else
            {
                nameAt = buffer + PcBufferSize + (dataPointer == Inline ? size : 0);
            }
        }
        else if (namePointer != 0 && !pc.TryFollow(buffer + 56, out nameAt))
        {
            nameAt = -1;
        }
        string? name = nameAt >= 0 ? CString(zone, nameAt) : null;

        byte[] data = BufferData(context, techset, technique, stage, pass, pcData, materialName, root);

        T7StructBuilder cb = structRead is T7Walk.Span read ? rewrite.Rebuild(read, Ps4BufferSize) : rewrite.Insert(Ps4BufferSize);
        if (structRead != null)
            cb.Move(32, 0, 40);
        else
            cb.CopyFile(buffer + 32, 0, 40);
        cb.U32(16, (uint)data.Length);
        string? dataPool = data.Length > 0 ? "cbdata:" + System.Convert.ToHexString(data) : null;
        bool dataPooled = dataPool != null && context.StringPools.ContainsKey(dataPool);
        if (dataPool == null)
            cb.Marker(8, 0);
        else if (dataPooled)
            cb.Pointer(8, new T7PoolTarget(dataPool));
        else
            cb.Marker(8, Inline);
        material.Marker(ps4Field, Inline);

        string? ps4Name = name == "$Globals" ? "__GLOBAL_CB__" : name;
        bool pooled = ps4Name != null && context.StringPools.ContainsKey(ps4Name);
        if (ps4Name == null)
            cb.Marker(24, 0);
        else if (pooled)
            cb.Pointer(24, new T7PoolTarget("string:" + ps4Name));
        else
            cb.Marker(24, Inline);
        cb.Commit();

        if (dataPool != null && !dataPooled)
        {
            T7StructBuilder bytes = dataRead is T7Walk.Span d ? rewrite.Rebuild(d, data.Length) : rewrite.Insert(data.Length);
            bytes.Write(0, data).DefinePool(dataPool);
            if (dataRead != null)
                bytes.MapSource(0, 0, 1);
            bytes.Commit();
            context.StringPools[dataPool] = rewrite.Asset.Index;
        }
        if (ps4Name != null && !pooled)
        {
            byte[] text = Encoding.Latin1.GetBytes(ps4Name + "\0");
            T7StructBuilder str = nameRead is T7Walk.Span n ? rewrite.Rebuild(n, text.Length) : rewrite.Insert(text.Length);
            str.Write(0, text).DefinePool("string:" + ps4Name);
            if (nameRead != null)
                str.MapSource(0, 0, (int)Math.Min(nameRead.Value.Size, text.Length));
            str.Commit();
            context.StringPools[ps4Name] = rewrite.Asset.Index;
        }
    }

    private static string StageName(int stage) => stage switch { 1 => "vs", 2 => "ps", _ => "" };

    private static byte[] BufferData(T7PortContext context, string? techset, int technique, int stage, int pass, byte[] pcData, string materialName, long root)
    {
        string stageName = StageName(stage);
        IReadOnlyList<T7Dxbc.Variable>? variables = techset != null && stageName.Length > 0 ? context.ShaderLibrary?.Globals(techset, technique, pass, stageName) : null;
        if (variables != null)
        {
            (List<(T7Dxbc.Variable Variable, int Offset)> layout, int end) = T7GlobalsLayout.Of(variables, T7GlobalsLayout.Kept(techset, technique, stageName), stageName);
            var output = new byte[end];
            Dictionary<string, byte[]>? values = null;
            foreach ((T7Dxbc.Variable variable, int offset) in layout)
            {
                ReadOnlySpan<byte> bytes;
                if (variable.Used)
                {
                    bytes = Slice(pcData, variable.Start, variable.Size);
                }
                else
                {
                    values ??= MaterialValues(context, root, techset);
                    bytes = values.TryGetValue(variable.Name, out byte[]? found) ? found.AsSpan(0, Math.Min(found.Length, variable.Size)) : [];
                }
                bytes[..Math.Min(bytes.Length, output.Length - offset)].CopyTo(output.AsSpan(offset));
            }
            byte[] packed = output;
            if (TryRetailBuffer(context, materialName, technique, stage, pass, out byte[] reference) && reference.Length > 0
                && !reference.AsSpan().SequenceEqual(packed))
            {
                int at = 0;
                while (at < Math.Min(reference.Length, packed.Length) && reference[at] == packed[at])
                    at++;
                context.Warn(T7Issues.ConstantBufferDiffers,
                    $"material '{materialName}' ({techset}, technique {technique}, {stageName}, pass {pass}): packed {packed.Length} bytes, retail has {reference.Length}, first difference at {at}",
                    materialName);
            }
            return packed;
        }
        if (TryRetailBuffer(context, materialName, technique, stage, pass, out byte[] retail))
        {
            context.Warn(T7Issues.ConstantBufferFromReference, $"material '{materialName}': constant buffer ({technique},{stage},{pass}) taken from the PS4 reference zone (no PC reflection for '{techset}')", materialName);
            return retail;
        }
        throw new InvalidDataException($"no PC shader reflection for technique set '{techset}' (technique {technique}, pass {pass}, {stageName}): add the PC zones that define it");
    }

    private static ReadOnlySpan<byte> Slice(byte[] data, int start, int size)
    {
        if (start >= data.Length || size <= 0)
            return [];
        return data.AsSpan(start, Math.Min(size, data.Length - start));
    }

    private static Dictionary<string, byte[]> MaterialValues(T7PortContext context, long root, string? techset)
    {
        var values = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (techset == null)
            return values;
        T7WalkIndex pc = context.Pc;
        byte[] zone = pc.Zone;
        for (int i = 0; i < 12; i++)
        {
            for (int j = 1; j < 3; j++)
            {
                for (int k = 0; k < 2; k++)
                {
                    long field = root + 0x30 + i * 48 + j * 16 + k * 8;
                    ulong value = BinaryPrimitives.ReadUInt64LittleEndian(zone.AsSpan((int)field));
                    if (value == 0 || !pc.TryFollow(field, out long buffer))
                        continue;
                    uint size = BinaryPrimitives.ReadUInt32LittleEndian(zone.AsSpan((int)buffer + 48));
                    ulong dataPointer = BinaryPrimitives.ReadUInt64LittleEndian(zone.AsSpan((int)buffer + 40));
                    long dataAt = dataPointer == Inline ? buffer + PcBufferSize : pc.TryFollow(buffer + 40, out long followed) ? followed : -1;
                    if (dataAt < 0 || size == 0)
                        continue;
                    IReadOnlyList<T7Dxbc.Variable>? variables = context.ShaderLibrary?.Globals(techset, i, k, StageName(j));
                    if (variables == null)
                        continue;
                    foreach (T7Dxbc.Variable variable in variables.Where(v => v.Used))
                    {
                        byte[] bytes = Slice(zone.AsSpan((int)dataAt, (int)size).ToArray(), variable.Start, variable.Size).ToArray();
                        if (!values.TryGetValue(variable.Name, out byte[]? existing) || bytes.Length > existing.Length)
                            values[variable.Name] = bytes;
                    }
                }
            }
        }
        return values;
    }

    private static bool TryRetailBuffer(T7PortContext context, string materialName, int technique, int stage, int pass, out byte[] data)
    {
        data = [];
        if (string.IsNullOrEmpty(materialName) || !context.Donors.TryFind(T7AssetTypes.Material, materialName, out T7WalkIndex? donor, out T7Walk.Asset? asset) || asset!.Reads.Count == 0)
            return false;
        long root = asset.Reads[0].FileOffset;
        long field = root + 0x28 + technique * 48 + stage * 16 + pass * 8;
        if (BinaryPrimitives.ReadUInt64LittleEndian(donor!.Zone.AsSpan((int)field)) == 0 || !donor.TryFollow(field, out long buffer))
            return false;
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(donor.Zone.AsSpan((int)buffer + 16));
        if (size == 0)
            return true;
        if (!donor.TryFollow(buffer + 8, out long dataAt))
            return false;
        data = donor.Zone.AsSpan((int)dataAt, (int)size).ToArray();
        return true;
    }

    private static string CString(byte[] zone, long at)
    {
        int end = Array.IndexOf(zone, (byte)0, (int)at);
        return Encoding.Latin1.GetString(zone, (int)at, end - (int)at);
    }

    private static void InlineTechset(T7PortContext context, T7AssetRewrite rewrite, string? techset)
    {
        T7Walk.Span header = rewrite.Peek();
        if (T7TechsetConverter.IsStub(context.Pc.Zone, header))
        {
            T7TechsetConverter.ConvertStub(rewrite);
            return;
        }
        if (context.ShaderCompiler != null)
        {
            T7TechsetBuilder.Convert(context, rewrite);
            return;
        }
        if (techset == null)
            throw new InvalidDataException($"{rewrite.Label}: embedded technique set has no name");
        string ps4 = T7Names.ToPs4(T7AssetTypes.TechniqueSet, techset);
        if (!context.Donors.TryFind(T7AssetTypes.TechniqueSet, ps4, out T7WalkIndex? donor, out T7Walk.Asset? donorAsset))
            throw new InvalidDataException($"{rewrite.Label}: embedded technique set '{ps4}' has shaders and no PS4 reference zone provides it");
        T7Walk.Registration? registration = null;
        foreach (T7Walk.Registration candidate in rewrite.Asset.Registrations)
        {
            if (candidate.Type == T7AssetTypes.TechniqueSet && candidate.FilePos > header.FileOffset && candidate.Header == header.At
                && (registration == null || candidate.FilePos < registration.Value.FilePos))
                registration = candidate;
        }
        if (registration == null)
            throw new InvalidDataException($"{rewrite.Label}: embedded technique set '{techset}' has no registration");
        while (!rewrite.Done && rewrite.Peek().FileOffset < registration.Value.FilePos)
            rewrite.Take();
        rewrite.InsertCopy(donor!, donorAsset!.Start, donorAsset.End - donorAsset.Start);
    }

    private static void Textures(T7PortContext context, T7AssetRewrite rewrite, T7StructBuilder material, long root)
    {
        T7WalkIndex pc = context.Pc;
        byte[] zone = pc.Zone;
        long tableField = root + 0x280;
        ulong tablePointer = BinaryPrimitives.ReadUInt64LittleEndian(zone.AsSpan((int)tableField));
        int count = zone[root + 0x270];
        if (tablePointer == 0 || count == 0)
            return;
        if (tablePointer != Inline)
        {
            if (context.FalloutOf(tableField)?.InlineCopyFields.Contains(tableField) == true && pc.TryFollow(tableField, out long shared))
            {
                for (int e = 0; e < count; e++)
                {
                    if (BinaryPrimitives.ReadUInt64LittleEndian(zone.AsSpan((int)shared + 32 * e)) is Inline or InlineAlias)
                        throw new InvalidDataException($"{rewrite.Label}: shared texture table loads images inline; copying it is not supported");
                }
                material.Marker(0x278, Inline);
                T7StructBuilder copy = rewrite.Insert(32 * count);
                copy.CopyFile(shared, 0, 32 * count).Commit();
            }
            return;
        }
        T7Walk.Span tableRead = rewrite.Take(32L * count);
        T7StructBuilder table = rewrite.Rebuild(tableRead, 32 * count).MoveAll();
        var loads = new List<(bool Inline, T7TextureComboFallout? Fallout, T7TextureComboFallout.SubAsset? SubAsset)>();
        for (int e = 0; e < count; e++)
        {
            long entry = tableRead.FileOffset + 32 * e;
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(zone.AsSpan((int)entry));
            if (value is Inline or InlineAlias)
            {
                loads.Add((true, null, null));
                continue;
            }
            T7TextureComboFallout? fallout = context.Fallouts.Find(f => f.RehomeFields.ContainsKey(entry));
            if (fallout != null && fallout.RehomeFields[entry] is { Type: T7AssetTypes.Image } subAsset && fallout.Claim(subAsset))
            {
                if (pc.TryFollow(entry, out long field) && fallout.Contains(field))
                    table.CopyFile(field, 32 * e, 8);
                table.Marker(32 * e, Inline);
                loads.Add((false, fallout, subAsset));
            }
            else
            {
                loads.Add((false, null, null));
            }
        }
        table.Commit();
        foreach ((bool inline, T7TextureComboFallout? fallout, T7TextureComboFallout.SubAsset? subAsset) in loads)
        {
            if (inline)
            {
                T7ImageConverter.ConvertImage(context, rewrite);
            }
            else if (fallout != null && subAsset != null)
            {
                rewrite.FlushPending();
                var rehome = new T7AssetRewrite(context, fallout.Asset, rewrite.Output, subAsset.FirstRead);
                T7ImageConverter.ConvertImage(context, rehome);
                rehome.FinishAt(subAsset.EndRead);
            }
        }
    }

    private static void Table(T7PortContext context, T7AssetRewrite rewrite, T7StructBuilder material, long field, int ps4Field, int size)
    {
        T7WalkIndex pc = context.Pc;
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(pc.Zone.AsSpan((int)field));
        if (value == 0 || size == 0)
            return;
        if (value == Inline)
        {
            rewrite.CopyChecked(size);
            return;
        }
        if (context.FalloutOf(field)?.InlineCopyFields.Contains(field) == true && pc.TryFollow(field, out long shared))
        {
            material.Marker(ps4Field, Inline);
            T7StructBuilder copy = rewrite.Insert(size);
            copy.CopyFile(shared, 0, size).Commit();
        }
    }
}
