using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FFPorter.Core.Common.Tools;

public static unsafe class GnmSdk
{
    private const string FetchSizeExport = "?computeVsFetchShaderSize@Gnmx@sce@@YAIPEBVVsShader@12@@Z";
    private const string FetchExport = "?generateVsFetchShader@Gnmx@sce@@YAXPEAXPEAIPEBVVsShader@12@@Z";
    private const string ApplyModifierExport = "?applyFetchShaderModifier@VsShader@Gnmx@sce@@QEAAXI@Z";
    private const string UsageTableExport = "?generatePsShaderUsageTable@Gnm@sce@@YAXPEAIPEBVVertexExportSemantic@12@IPEBVPixelInputSemantic@12@I@Z";
    private const int LoadWithAlteredSearchPath = 0x00000008;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, nint> Modules = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Fetch> FetchBindings = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, nint> UsageBindings = new(StringComparer.OrdinalIgnoreCase);

    private sealed class Fetch
    {
        public delegate* unmanaged[Cdecl]<byte*, uint> Size;
        public delegate* unmanaged[Cdecl]<byte*, uint*, byte*, void> Generate;
        public delegate* unmanaged[Cdecl]<byte*, uint, void> Apply;
    }

    public static uint FetchShaderSize(string gnmxLibrary, byte[] shader)
    {
        Fetch fetch = FetchOf(gnmxLibrary);
        fixed (byte* pointer = shader)
            return fetch.Size(pointer);
    }

    public static (byte[] Fetch, uint Modifier) GenerateFetchShader(string gnmxLibrary, byte[] shader, uint size)
    {
        Fetch fetch = FetchOf(gnmxLibrary);
        byte[] code = new byte[size];
        uint modifier = 0;
        fixed (byte* pointer = shader)
        {
            fixed (byte* output = code)
                fetch.Generate(output, &modifier, pointer);
            fetch.Apply(pointer, modifier);
        }
        return (code, modifier);
    }

    public static void GenerateUsageTable(string gnmLibrary, uint* table, byte* exports, uint exportCount, byte* inputs, uint inputCount)
    {
        nint export;
        lock (Gate)
        {
            if (!UsageBindings.TryGetValue(gnmLibrary, out export))
            {
                export = NativeLibrary.GetExport(Module(gnmLibrary), UsageTableExport);
                UsageBindings[gnmLibrary] = export;
            }
        }
        ((delegate* unmanaged[Cdecl]<uint*, byte*, uint, byte*, uint, void>)export)(table, exports, exportCount, inputs, inputCount);
    }

    public static void Load(string library)
    {
        lock (Gate)
            Module(library);
    }

    private static Fetch FetchOf(string gnmxLibrary)
    {
        lock (Gate)
        {
            if (!FetchBindings.TryGetValue(gnmxLibrary, out Fetch? fetch))
            {
                nint module = Module(gnmxLibrary);
                fetch = new Fetch
                {
                    Size = (delegate* unmanaged[Cdecl]<byte*, uint>)NativeLibrary.GetExport(module, FetchSizeExport),
                    Generate = (delegate* unmanaged[Cdecl]<byte*, uint*, byte*, void>)NativeLibrary.GetExport(module, FetchExport),
                    Apply = (delegate* unmanaged[Cdecl]<byte*, uint, void>)NativeLibrary.GetExport(module, ApplyModifierExport),
                };
                FetchBindings[gnmxLibrary] = fetch;
            }
            return fetch;
        }
    }

    private static nint Module(string library)
    {
        if (Modules.TryGetValue(library, out nint module))
            return module;
        module = LoadLibraryEx(library, IntPtr.Zero, LoadWithAlteredSearchPath);
        if (module == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        Modules[library] = module;
        return module;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, int flags);
}
