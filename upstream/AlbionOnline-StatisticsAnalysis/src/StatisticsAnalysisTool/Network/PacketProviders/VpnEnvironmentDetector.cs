#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace StatisticsAnalysisTool.Network.PacketProviders;

/// <summary>Observed adapter metadata only. No addresses, routes or packet data.</summary>
internal sealed record VpnInterfaceObservation(string Name, string Description, bool IsUp);

/// <summary>
/// Evidence of software/virtual interfaces, not proof that Albion uses a VPN.
/// Only these aggregate labels are exposed to UI diagnostics.
/// </summary>
internal sealed record VpnEnvironmentSnapshot(
    DateTime CheckedUtc,
    bool ProcessInspectionAvailable,
    bool InterfaceInspectionAvailable,
    IReadOnlyList<string> RunningProducts,
    IReadOnlyList<string> UpTunnelInterfaces)
{
    internal bool HasSignals => RunningProducts.Count > 0 || UpTunnelInterfaces.Count > 0;

    internal string Describe()
    {
        if (CheckedUtc == DateTime.MinValue)
            return "Detecção local em andamento; ainda não há resultado";

        var parts = new List<string>();
        if (RunningProducts.Count > 0)
            parts.Add("programa reconhecido: " + string.Join(", ", RunningProducts));
        if (UpTunnelInterfaces.Count > 0)
            parts.Add("adaptador de túnel disponível: " + string.Join(", ", UpTunnelInterfaces));
        if (parts.Count == 0)
            parts.Add("nenhum sinal conhecido (VPN não descartada)");
        if (!ProcessInspectionAvailable || !InterfaceInspectionAvailable)
            parts.Add("inspeção parcial");
        return string.Join(" · ", parts) + " · uso pelo Albion NÃO confirmado";
    }
}

/// <summary>
/// Read-only, local, cached heuristic. A process or an Up adapter does NOT prove
/// an active encrypted tunnel or an optimized Albion route. In particular WARP
/// may be running in DNS-only mode. Does not change capture settings or routing.
/// </summary>
internal static class VpnEnvironmentDetector
{
    private static readonly object Gate = new();
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private static VpnEnvironmentSnapshot _last = new(DateTime.MinValue, false, false, [], []);
    private static DateTime _nextRefreshUtc = DateTime.MinValue;
    private static bool _refreshing;

    /// <summary>Never scan processes or interfaces on the UI thread.</summary>
    internal static VpnEnvironmentSnapshot Snapshot()
    {
        lock (Gate)
        {
            if (!_refreshing && DateTime.UtcNow >= _nextRefreshUtc)
            {
                _refreshing = true;
                _nextRefreshUtc = DateTime.UtcNow + RefreshInterval;
                _ = Task.Run(() =>
                {
                    VpnEnvironmentSnapshot result;
                    try { result = Inspect(); }
                    catch { result = new VpnEnvironmentSnapshot(DateTime.UtcNow, false, false, [], []); }
                    lock (Gate)
                    {
                        _last = result;
                        _refreshing = false;
                    }
                });
            }
            return _last;
        }
    }

    private static VpnEnvironmentSnapshot Inspect()
    {
        var processNames = new List<string>();
        var adapters = new List<VpnInterfaceObservation>();
        bool processesOk = false, adaptersOk = false;
        try
        {
            var processes = Process.GetProcesses();
            processesOk = true;
            foreach (var process in processes)
            {
                try
                {
                    // Do not retain arbitrary process names, paths or arguments.
                    var name = process.ProcessName;
                    if (RecognizeProductProcess(name) is not null)
                        processNames.Add(name);
                }
                catch (InvalidOperationException) { /* terminated mid-scan */ }
                catch (System.ComponentModel.Win32Exception) { /* access denied */ }
                finally { process.Dispose(); }
            }
        }
        catch { /* process enumeration unavailable; do not claim absence */ }

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                // No addresses, MACs, interface IDs, DNS or routes collected.
                adapters.Add(new VpnInterfaceObservation(
                    nic.Name, nic.Description,
                    nic.OperationalStatus == OperationalStatus.Up));
            }
            adaptersOk = true;
        }
        catch { /* interface inspection unavailable; do not claim absence */ }

        return Classify(processNames, adapters, DateTime.UtcNow, processesOk, adaptersOk);
    }

    /// <summary>Deterministic, offline classification for regression tests.</summary>
    internal static VpnEnvironmentSnapshot Classify(
        IEnumerable<string> processNames,
        IEnumerable<VpnInterfaceObservation> adapters,
        DateTime utc,
        bool processInspectionAvailable = true,
        bool interfaceInspectionAvailable = true)
    {
        var products = (processNames ?? Array.Empty<string>())
            .Select(RecognizeProductProcess)
            .Where(x => x is not null)
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .Take(8)
            .ToArray();
        var tunnels = (adapters ?? Array.Empty<VpnInterfaceObservation>())
            .Where(x => x is not null && x.IsUp)
            .Select(x => RecognizeTunnelInterface(x.Name, x.Description))
            .Where(x => x is not null)
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .Take(8)
            .ToArray();
        return new VpnEnvironmentSnapshot(utc, processInspectionAvailable,
            interfaceInspectionAvailable, products, tunnels);
    }

    private static string? RecognizeProductProcess(string? processName)
    {
        // Exact normalized executable names: no fuzzy matching against random
        // unrelated programs and no assertion that the tunnel is connected.
        return processName?.Trim().ToLowerInvariant() switch
        {
            "exitlag" or "exitlagservice" or "exitlagclient" => "ExitLag",
            "noping" or "nopingclient" or "nopingservice" => "NoPing",
            "cloudflare warp" or "cloudflarewarp" or "warp-svc" => "Cloudflare WARP",
            "wireguard" or "wireguard-manager" => "WireGuard",
            "openvpn-gui" or "openvpnconnect" or "openvpnconnectclient" => "OpenVPN",
            "protonvpn" or "protonvpn.client" => "Proton VPN",
            "mullvad vpn" or "mullvad-vpn" => "Mullvad VPN",
            _ => null
        };
    }

    private static string? RecognizeTunnelInterface(string? name, string? description)
    {
        var descriptionAndName = (name ?? string.Empty) + " " + (description ?? string.Empty);
        // No broad 'virtual' match: Hyper-V, Wi-Fi Direct and Docker aren't VPNs.
        if (Contains(descriptionAndName, "Cloudflare WARP")) return "Cloudflare WARP";
        if (Contains(descriptionAndName, "NordLynx")) return "NordLynx";
        if (Contains(descriptionAndName, "WireGuard")) return "WireGuard";
        if (Contains(descriptionAndName, "OpenVPN")) return "OpenVPN";
        if (Contains(descriptionAndName, "Wintun")) return "Wintun (genérico)";
        if (Contains(descriptionAndName, "TAP-Windows") || Contains(descriptionAndName, "TAP-Win32"))
            return "TAP (genérico)";
        return null;
    }

    private static bool Contains(string text, string token) =>
        text.Contains(token, StringComparison.OrdinalIgnoreCase);
}
