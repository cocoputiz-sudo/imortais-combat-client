using System.Reflection;
using System.Runtime.InteropServices;

namespace Imortais.LinuxClient.Services;

/// <summary>
/// The Libpcap wrapper imports "libpcap", which .NET maps to libpcap.so. That name only exists
/// with the developer package (libpcap-dev); Ubuntu ships libpcap.so.0.8. Point the import at
/// the real system library so capture works on a stock Ubuntu.
/// </summary>
public static class NativeLibraries
{
    internal static readonly string[] PcapCandidates =
        ["libpcap.so.0.8", "libpcap.so.1", "libpcap.so", "/lib/x86_64-linux-gnu/libpcap.so.0.8", "/usr/lib/x86_64-linux-gnu/libpcap.so.0.8"];

    private static int _installed;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1) return;
        // The wrapper sets its own DllImport resolver (only one is allowed per assembly), so we
        // hook the fallback that .NET calls only when that resolver and default probing fail.
        System.Runtime.Loader.AssemblyLoadContext.Default.ResolvingUnmanagedDll += Resolve;
    }

    private static IntPtr Resolve(Assembly assembly, string name)
    {
        if (!name.Contains("pcap", StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;
        foreach (var candidate in PcapCandidates)
            if (NativeLibrary.TryLoad(candidate, out var handle)) return handle;
        return IntPtr.Zero; // fall back to the default probing (clear error message)
    }

    public static bool CanLoadPcap() => PcapCandidates.Any(c => { if (!NativeLibrary.TryLoad(c, out var h)) return false; NativeLibrary.Free(h); return true; });
}
