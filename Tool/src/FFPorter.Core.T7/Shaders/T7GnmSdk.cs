using System.ComponentModel;
using FFPorter.Core.Common.Tools;

namespace FFPorter.Core.T7.Shaders;

public static class T7GnmSdk
{
    public static bool Available(string? sdkBin = null)
    {
        sdkBin ??= Ps4Sdk.BinDirectory();
        return File.Exists(Path.Combine(sdkBin, Ps4Sdk.GnmxLibraryName)) && File.Exists(Path.Combine(sdkBin, Ps4Sdk.GnmLibraryName));
    }

    public static byte[] FetchShader(ReadOnlySpan<byte> vsHeader, string? sdkBin = null)
    {
        sdkBin = Load(sdkBin);
        byte[] header = new byte[vsHeader.Length + 1];
        vsHeader.CopyTo(header);
        string gnmx = Path.Combine(sdkBin, Ps4Sdk.GnmxLibraryName);
        uint size = GnmSdk.FetchShaderSize(gnmx, header);
        if (size == 0 || size >= 65536)
            throw new InvalidDataException($"the SDK sizes a fetch shader at {size} bytes");
        return GnmSdk.GenerateFetchShader(gnmx, header, size).Fetch;
    }

    public static unsafe uint[] UsageTable(ReadOnlySpan<byte> vsHeader, ReadOnlySpan<byte> psHeader, string? sdkBin = null)
    {
        sdkBin = Load(sdkBin);
        (byte[] exports, int exportCount) = VsExports(vsHeader);
        (byte[] inputs, int inputCount) = PsInputs(psHeader);
        var table = new uint[Math.Max(inputCount, 1)];
        byte[] exportBuffer = [.. exports, 0, 0];
        byte[] inputBuffer = [.. inputs, 0, 0];
        fixed (uint* output = table)
        fixed (byte* vs = exportBuffer)
        fixed (byte* ps = inputBuffer)
            GnmSdk.GenerateUsageTable(Path.Combine(sdkBin, Ps4Sdk.GnmLibraryName), output, vs, (uint)exportCount, ps, (uint)inputCount);
        return table[..inputCount];
    }

    public static int UsageSlotCount(ReadOnlySpan<byte> header) => header[3];

    public static (byte[] Table, int Count) VsInputs(ReadOnlySpan<byte> vsHeader)
    {
        int count = vsHeader[36];
        int start = 40 + 4 * UsageSlotCount(vsHeader);
        return (vsHeader.Slice(start, 4 * count).ToArray(), count);
    }

    public static (byte[] Table, int Count) VsExports(ReadOnlySpan<byte> vsHeader)
    {
        int inputs = vsHeader[36], count = vsHeader[37];
        int start = 40 + 4 * UsageSlotCount(vsHeader) + 4 * inputs;
        return (vsHeader.Slice(start, 2 * count).ToArray(), count);
    }

    public static (byte[] Table, int Count) PsInputs(ReadOnlySpan<byte> psHeader)
    {
        int count = psHeader[56];
        int start = 60 + 4 * UsageSlotCount(psHeader);
        return (psHeader.Slice(start, 2 * count).ToArray(), count);
    }

    public static int CodeSize(ReadOnlySpan<byte> header) => (int)ShaderBinary.CodeSize(header);

    public static (byte[] Header, byte[] Code) SplitShaderBinary(ReadOnlySpan<byte> sb)
    {
        if (ShaderBinary.TrySplit(sb, out byte[] header, out byte[] code, out ShaderBinary.Problem problem))
            return (header, code);
        throw new InvalidDataException(problem == ShaderBinary.Problem.TruncatedCode
            ? "the compiled shader's code is truncated"
            : "the compiled shader has no Shdr header");
    }

    private static string Load(string? sdkBin)
    {
        sdkBin ??= Ps4Sdk.BinDirectory();
        if (!Available(sdkBin))
            throw new FileNotFoundException($"The PS4 SDK host libraries (libSceGnm.dll, libSceGnmx.dll) are not in {sdkBin}");
        try
        {
            GnmSdk.Load(Path.Combine(sdkBin, Ps4Sdk.GnmLibraryName));
            GnmSdk.Load(Path.Combine(sdkBin, Ps4Sdk.GnmxLibraryName));
        }
        catch (Win32Exception error)
        {
            throw new IOException($"The PS4 SDK host libraries in {sdkBin} could not be loaded (error {error.NativeErrorCode})", error);
        }
        return sdkBin;
    }
}
