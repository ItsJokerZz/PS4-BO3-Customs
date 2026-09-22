using System.Runtime.CompilerServices;

namespace FFPorter.Core.Common.Tools;

public static class ShippedData
{
    public const string Resource = "FFPorter.Core.Shipped.zip";

    [ModuleInitializer]
    internal static void Register() => ToolData.Register(typeof(ShippedData).Assembly, Resource);
}
