using System.Buffers.Binary;
using System.Text;
using FFPorter.Core.T7.Harness;
using FFPorter.Core.T7.Link;
using FFPorter.Core.T7.Sound;

namespace FFPorter.Core.T7.Port.Converters;

public sealed class T7SoundBankConverter : IT7AssetConverter
{
    private const int AliasSize = 0xD8;
    private const int AliasFileField = 0x40;
    private const int AliasFlagsField = 0x68;
    private const uint LoadTypeMask = 0x3000;
    private const uint LoadTypeLoaded = 0x1000;
    private const uint LoadTypeStreamed = 0x2000;

    public string Name => "soundbank";

    public bool TryConvert(T7PortContext context, T7Walk.Asset asset, T7OutputAsset output, out string? reason)
    {
        reason = null;
        if (asset.Type != T7AssetTypes.Sound)
            return false;
        byte[] zone = context.Pc.Zone;
        var files = new Dictionary<long, (string Pc, string Ps4)>();
        var renamed = new Dictionary<long, (string Pc, string Ps4)>();
        int external = 0;
        foreach (T7Walk.Span read in asset.Reads)
        {
            if (read.Kind != T7WalkKind.String || read.Size < 2)
                continue;
            string text = Encoding.Latin1.GetString(zone, (int)read.FileOffset, (int)read.Size - 1);
            if (!text.EndsWith(".snd", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!context.SoundRenames.TryGetValue(text, out string? ps4))
            {
                ps4 = T7SoundBank.Ps4AssetName(text);
                external++;
            }
            files[read.FileOffset] = (text, ps4);
            if (!string.Equals(ps4, text, StringComparison.Ordinal))
                renamed[read.FileOffset] = (text, ps4);
        }
        if (external > 0)
        {
            context.Warnings.Add($"sound bank '{asset.Name}': {external} alias file names are not in this map's banks (game banks); renamed by the PS4 naming rule");
            context.Fidelity?.Raise(T7Issues.AliasNamesByRule, count: external);
        }

        var aliases = new HashSet<long>();
        if (context.StreamedSounds.Count > 0)
        {
            foreach (T7Walk.Span read in asset.Reads)
            {
                if (read.Kind != T7WalkKind.Read || read.Size < AliasSize || read.Size % AliasSize != 0)
                    continue;
                for (long at = read.FileOffset; at < read.FileOffset + read.Size; at += AliasSize)
                    aliases.Add(at);
            }
        }

        var ids = new SortedDictionary<long, uint>();
        int mismatched = 0, streamed = 0, streamedParts = 0;
        foreach ((long field, T7PointerKind _, T7BlockAddress target) in context.Pc.PointersIn(asset.Start, asset.End - asset.Start))
        {
            if (!context.Pc.TryFileOffset(target, out long at, field) || !files.TryGetValue(at, out var names) || field < 8)
                continue;
            bool rename = renamed.ContainsKey(at);
            uint current = BinaryPrimitives.ReadUInt32LittleEndian(zone.AsSpan((int)field - 8));
            if (current != T7SoundBank.HashName(names.Pc))
            {
                mismatched += rename ? 1 : 0;
                continue;
            }
            uint id = T7SoundBank.HashName(names.Ps4);
            if (rename)
                ids[field - 8] = id;
            if (!context.StreamedSounds.Contains(id))
                continue;
            long alias = field - AliasFileField;
            if (!aliases.Contains(alias))
            {
                streamedParts++;
                continue;
            }
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(zone.AsSpan((int)(alias + AliasFlagsField)));
            if ((flags & LoadTypeMask) != LoadTypeLoaded)
                continue;
            ids[alias + AliasFlagsField] = flags & ~LoadTypeMask | LoadTypeStreamed;
            context.SwitchedSounds.Add(id);
            streamed++;
        }
        if (mismatched > 0)
        {
            context.Warnings.Add($"sound bank '{asset.Name}': {mismatched} file name pointers without the expected asset id before them (left unchanged)");
            context.Fidelity?.Raise(T7Issues.AliasIdMismatch, count: mismatched);
        }
        if (streamed > 0)
            context.Log($"  sound bank '{asset.Name}': {streamed} aliases switched from loaded to streamed");
        if (streamedParts > 0)
            context.Warnings.Add($"sound bank '{asset.Name}': {streamedParts} sustain or release parts use sounds that now stream; the PS4 streams only an alias's main sound, so those parts will not play");

        var rewrite = new T7AssetRewrite(context, asset, output);
        while (!rewrite.Done)
        {
            T7Walk.Span read = rewrite.Peek();
            if (read.Kind == T7WalkKind.String && renamed.TryGetValue(read.FileOffset, out var names))
            {
                rewrite.Take();
                byte[] text = Encoding.Latin1.GetBytes(names.Ps4 + "\0");
                T7StructBuilder builder = rewrite.Rebuild(read, text.Length);
                builder.Write(0, text).MapSource(0, 0, (int)Math.Min(read.Size, text.Length));
                builder.Commit();
                continue;
            }
            long end = read.FileOffset + read.Size;
            var patches = ids.Where(p => p.Key >= read.FileOffset && p.Key + 4 <= end).ToList();
            if (patches.Count == 0)
            {
                rewrite.Copy();
                continue;
            }
            rewrite.Take();
            T7StructBuilder record = rewrite.Rebuild(read, (int)read.Size).MoveAll();
            foreach ((long idField, uint id) in patches)
                record.U32((int)(idField - read.FileOffset), id);
            record.Commit();
        }
        rewrite.Finish();
        output.Origin = renamed.Count > 0 ? "soundbank (renamed files)" : "soundbank";
        return true;
    }
}
