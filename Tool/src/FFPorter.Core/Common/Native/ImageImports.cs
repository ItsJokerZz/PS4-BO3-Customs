using System.Reflection;
using System.Runtime.InteropServices;

namespace FFPorter.Core.Common.Native;

public static class ImageImports
{
    public sealed record Result(int Bound, IReadOnlyList<string> Missing);

    public static Result Bind(NativeHost host, ulong imageBase)
    {
        uint pe = host.D(imageBase + 0x3C);
        ulong optional = imageBase + pe + 24;
        if (host.W(optional) != 0x20B)
            return new Result(0, []);
        uint directory = host.D(optional + 112 + 8);
        if (directory == 0)
            return new Result(0, []);

        int bound = 0;
        var missing = new List<(ulong Slot, string Label)>();
        for (ulong descriptor = imageBase + directory; ; descriptor += 20)
        {
            uint names = host.D(descriptor), nameRva = host.D(descriptor + 12), thunks = host.D(descriptor + 16);
            if (nameRva == 0 && thunks == 0)
                break;
            string dll = host.TryCString(imageBase + nameRva) ?? $"dll@{nameRva:x}";
            nint library = NativeLibrary.TryLoad(dll, Assembly.GetExecutingAssembly(), DllImportSearchPath.System32, out nint handle) ? handle : 0;
            for (ulong i = 0; ; i++)
            {
                ulong slot = imageBase + thunks + 8 * i;
                ulong entry = names != 0 ? host.Q(imageBase + names + 8 * i) : host.Q(slot);
                if (entry == 0)
                    break;
                string label;
                nint address = 0;
                if ((entry & (1UL << 63)) != 0)
                {
                    label = $"{dll}!#{entry & 0xFFFF}";
                    if (library != 0)
                        address = Kernel32.GetProcAddressByOrdinal(library, (nint)(entry & 0xFFFF));
                }
                else if (names != 0 && host.TryCString(imageBase + (entry & 0x7FFFFFFF) + 2) is { } name)
                {
                    label = $"{dll}!{name}";
                    if (library != 0 && NativeLibrary.TryGetExport(library, name, out nint export))
                        address = export;
                }
                else
                {
                    label = $"{dll}!import {i}";
                }
                if (address != 0)
                {
                    host.SetQ(slot, (ulong)address);
                    bound++;
                }
                else
                {
                    missing.Add((slot, label));
                }
            }
        }

        if (missing.Count > 0)
        {
            ulong stubs = host.Alloc((ulong)missing.Count * 16, execute: true);
            for (int i = 0; i < missing.Count; i++)
            {
                (ulong slot, string label) = missing[i];
                ulong stub = stubs + (ulong)i * 16;
                host.Hook(stub, _ =>
                {
                    host.Fail($"The loader called {label}, which this machine does not provide");
                    return 0;
                });
                host.SetQ(slot, stub);
            }
        }
        return new Result(bound, missing.Select(m => m.Label).ToList());
    }
}
