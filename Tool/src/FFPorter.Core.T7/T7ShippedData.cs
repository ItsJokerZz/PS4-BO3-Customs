using System.Runtime.CompilerServices;
using FFPorter.Core.Common.Tools;

namespace FFPorter.Core.T7;

public static class T7ShippedData
{
    public const string Resource = "FFPorter.Core.T7.Shipped.zip";

    [ModuleInitializer]
    internal static void Register() => ToolData.Register(typeof(T7ShippedData).Assembly, Resource);
}
