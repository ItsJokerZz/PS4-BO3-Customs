using System.Globalization;

namespace FFPorter.Core.Common.Tools;

public static class Ps4Sdk
{
    public const string BinVariable = "FFPORTER_PS4_SDK_BIN";

    public const string SdkVariable = "SCE_ORBIS_SDK_DIR";

    public const string ExpectedVersion = "12.000";

    public const string ShaderCompilerName = "orbis-wave-psslc.exe";
    public const string ImageConverterName = "orbis-image2gnf.exe";
    public const string GnmLibraryName = "libSceGnm.dll";
    public const string GnmxLibraryName = "libSceGnmx.dll";

    public static IReadOnlyList<string> Files { get; } = [ShaderCompilerName, GnmLibraryName, GnmxLibraryName, ImageConverterName];

    public static string DefaultBinDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SCE", "ORBIS SDKs", ExpectedVersion, "host_tools", "bin");

    public static string BinDirectory(Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        if (environment(BinVariable) is { Length: > 0 } bin)
            return bin;
        if (environment(SdkVariable) is { Length: > 0 } sdk)
            return Path.Combine(sdk, "host_tools", "bin");
        string installs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SCE", "ORBIS SDKs");
        if (Directory.Exists(installs))
        {
            string? newest = Directory.EnumerateDirectories(installs)
                .Where(version => Directory.Exists(Path.Combine(version, "host_tools", "bin")))
                .OrderByDescending(version => decimal.TryParse(Path.GetFileName(version), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number) ? number : -1)
                .FirstOrDefault();
            if (newest != null)
                return Path.Combine(newest, "host_tools", "bin");
        }
        return DefaultBinDirectory;
    }

    public static string ShaderCompiler(Func<string, string?>? environment = null) => Path.Combine(BinDirectory(environment), ShaderCompilerName);
}
