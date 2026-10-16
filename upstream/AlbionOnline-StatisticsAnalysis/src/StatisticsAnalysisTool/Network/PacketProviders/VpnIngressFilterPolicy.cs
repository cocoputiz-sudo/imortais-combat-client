#nullable enable
using System;

namespace StatisticsAnalysisTool.Network.PacketProviders;

/// <summary>
/// Bounded, explicit opt-in broad UDP capture used only in the unsigned
/// portable ExitLag diagnostic. Production and installer builds stay strict.
/// </summary>
internal static class VpnIngressFilterPolicy
{
#if IMORTAIS_VPN_UDP_INGRESS_DIAGNOSTIC
    internal const bool IsDiagnosticPilotBinary = true;
#else
    internal const bool IsDiagnosticPilotBinary = false;
#endif

    internal const string BroadUdpFilter = "(ip and udp) or (ip6 and udp)";

    internal static bool ShouldUseBroadFilter(
        bool diagnosticPilotBinary, bool vpnAssistEnabled,
        string? effectiveFilter, string defaultFilter) =>
        diagnosticPilotBinary && vpnAssistEnabled &&
        string.Equals(effectiveFilter, defaultFilter, StringComparison.OrdinalIgnoreCase);
}
