using System.Buffers.Binary;
using System.Text;
using FFPorter.Core.T7.Harness;
using FFPorter.Core.T7.Link;

namespace FFPorter.Core.T7.Port.Converters;

public sealed class T7TechsetConverter : IT7AssetConverter
{
    public const int HeaderSize = 112;

    public string Name => "techset";

    public bool TryConvert(T7PortContext context, T7Walk.Asset asset, T7OutputAsset output, out string? reason)
    {
        reason = null;
        if (asset.Type != T7AssetTypes.TechniqueSet || asset.Reads.Count == 0)
            return false;
        bool stub = IsStub(context.Pc.Zone, asset.Reads[0]);
        if (!stub && context.ShaderCompiler == null)
            return false;
        var rewrite = new T7AssetRewrite(context, asset, output);
        try
        {
            if (stub)
                ConvertStub(rewrite);
            else
                T7TechsetBuilder.Convert(context, rewrite);
            rewrite.Finish();
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or IOException)
        {
            reason = error.Message;
            return false;
        }
    }

    public static bool IsStub(byte[] zone, T7Walk.Span header)
    {
        if (header.Kind != T7WalkKind.Read || header.Size != HeaderSize)
            return false;
        for (int t = 0; t < 12; t++)
        {
            if (BinaryPrimitives.ReadUInt64LittleEndian(zone.AsSpan((int)header.FileOffset + 16 + 8 * t)) != 0)
                return false;
        }
        return true;
    }

    public static void ConvertStub(T7AssetRewrite rewrite)
    {
        T7Walk.Span header = rewrite.Take(HeaderSize);
        if (!IsStub(rewrite.Pc.Zone, header))
            throw new InvalidDataException($"{rewrite.Label}: technique set has techniques; only ',name' references convert");
        rewrite.CopySpan(header);
        if (BinaryPrimitives.ReadUInt64LittleEndian(rewrite.Pc.Zone.AsSpan((int)header.FileOffset)) != ulong.MaxValue)
            return;
        T7Walk.Span nameRead = rewrite.Take(T7WalkKind.String);
        string name = Encoding.Latin1.GetString(rewrite.Bytes(nameRead)[..^1]);
        byte[] renamed = Encoding.Latin1.GetBytes(T7Names.ToPs4(T7AssetTypes.TechniqueSet, name) + "\0");
        T7StructBuilder builder = rewrite.Rebuild(nameRead, renamed.Length);
        builder.Write(0, renamed).MapSource(0, 0, (int)Math.Min(nameRead.Size, renamed.Length));
        builder.Commit();
    }
}
