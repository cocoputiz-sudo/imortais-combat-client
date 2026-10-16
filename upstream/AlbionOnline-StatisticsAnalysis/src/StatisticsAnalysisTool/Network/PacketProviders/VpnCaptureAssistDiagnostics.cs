#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace StatisticsAnalysisTool.Network.PacketProviders;

/// <summary>
/// Conservative VPN capture policy: observing silence is NOT permission to
/// restart capture. Photon inactivity can be normal in Albion (loading, AFK).
/// </summary>
internal static class VpnCaptureHealthPolicy
{
    public static string Describe(bool enabled, bool gameDetected,
        DateTime nowUtc, DateTime? lastCandidateUtc, DateTime? lastDecodedUtc)
    {
        if (!enabled) return "VPN: modo assistido desligado";
        if (!gameDetected) return "VPN: aguardando Albion (sem reabrir interfaces)";
        if (lastCandidateUtc is null) return "VPN: aguardando pacotes candidatos nas interfaces";
        if (lastDecodedUtc is null) return "VPN: há pacotes candidatos, mas nenhum evento Photon confirmado";
        if (nowUtc - lastDecodedUtc.Value > TimeSpan.FromSeconds(30))
            return "VPN: decodificação inativa; verificar rota antes de recuperar";
        return "VPN: eventos Photon observados (sem ação necessária)";
    }
}

internal sealed record VpnCaptureDiagnosticsSnapshot(
    bool Enabled,
    string State,
    string? SelectedAdapter,
    DateTime? LastCandidateUtc,
    DateTime? LastDecodedEventUtc,
    int CandidateAdapterCount,
    long CandidatePackets,
    long DecodedEvents,
    long NetworkChanges,
    long UserRescans,
    long RecoveryAttempts,
    long ProbeStarts,
    long ProbeConfirmed,
    long ProbeReverted,
    long CapturedFrames,
    long CapturedUdp,
    long CapturedNonStandardUdpPorts,
    long CapturedTcp,
    DateTime? LastCapturedFrameUtc,
    int OpenedInterfaces,
    bool PacketFilterConfigured,
    bool? AlbionProcessPresent);

/// <summary>
/// Local aggregate diagnostics only. No packet contents, addresses, player IDs
/// or VPN provider credentials are stored or transmitted.
/// </summary>
internal static class VpnCaptureAssistDiagnostics
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, DateTime> CandidateAdapters = new(StringComparer.OrdinalIgnoreCase);
    private static long _lastCandidateTicks;
    private static long _lastDecodedTicks;
    private static long _candidatePackets;
    private static long _decodedEvents;
    private static long _networkChanges;
    private static long _userRescans;
    private static long _recoveryAttempts;
    private static long _probeStarts;
    private static long _probeConfirmed;
    private static long _probeReverted;
    private static string? _selectedAdapter;
    private static long _ingressFrames;
    private static long _ingressUdp;
    private static long _ingressOtherUdpPorts;
    private static long _ingressTcp;
    private static long _lastIngressTicks;
    private static int _openedInterfaces;
    private static int _packetFilterConfigured;
    private static int _captureFilterKind; // 0 none, 1 strict/custom, 2 broad UDP pilot
    // 0 = not checked, 1 = checked without process, 2 = Albion process observed.
    private static int _albionProcessPresence;

    /// <summary>Local, content-free ingress counts. No IP, MAC, payload or routes.</summary>
    public static void ResetIngress()
    {
        Interlocked.Exchange(ref _ingressFrames, 0);
        Interlocked.Exchange(ref _ingressUdp, 0);
        Interlocked.Exchange(ref _ingressOtherUdpPorts, 0);
        Interlocked.Exchange(ref _ingressTcp, 0);
        Interlocked.Exchange(ref _lastIngressTicks, 0);
        Interlocked.Exchange(ref _openedInterfaces, 0);
        Interlocked.Exchange(ref _packetFilterConfigured, 0);
        Interlocked.Exchange(ref _captureFilterKind, 0);
        Interlocked.Exchange(ref _albionProcessPresence, 0);
    }

    public static void ObserveOpenedInterfaces(int count) =>
        Interlocked.Exchange(ref _openedInterfaces, Math.Max(0, count));
    public static void ObservePacketFilter(string? appliedFilter)
    {
        var configured = !string.IsNullOrWhiteSpace(appliedFilter);
        Interlocked.Exchange(ref _packetFilterConfigured, configured ? 1 : 0);
        Interlocked.Exchange(ref _captureFilterKind,
            !configured ? 0 :
            string.Equals(appliedFilter, VpnIngressFilterPolicy.BroadUdpFilter,
                StringComparison.OrdinalIgnoreCase) ? 2 : 1);
    }
    public static string CaptureFilterMode => Volatile.Read(ref _captureFilterKind) switch
    {
        0 => "SEM FILTRO",
        2 => "UDP TODAS AS PORTAS (PILOTO, somente captura local)",
        _ => "FILTRO RESTRITO/PERSONALIZADO"
    };
    public static void ObserveAlbionProcessPresence(bool observed) =>
        Interlocked.Exchange(ref _albionProcessPresence, observed ? 2 : 1);
    public static void ObserveIngressFrame()
    {
        Interlocked.Increment(ref _ingressFrames);
        Interlocked.Exchange(ref _lastIngressTicks, DateTime.UtcNow.Ticks);
    }
    public static void ObserveIngressUdp(bool usesKnownPhotonPort)
    {
        Interlocked.Increment(ref _ingressUdp);
        if (!usesKnownPhotonPort) Interlocked.Increment(ref _ingressOtherUdpPorts);
    }
    public static void ObserveIngressTcp() => Interlocked.Increment(ref _ingressTcp);

    internal static string DescribeIngress(VpnCaptureDiagnosticsSnapshot snap)
    {
        if (!snap.Enabled) return "Contagem de entrada Npcap desativada (USO VPN: NÃO)";
        if (snap.OpenedInterfaces == 0) return "Nenhuma interface Npcap aberta; verificar configuração e driver";
        if (snap.CapturedFrames == 0)
            return "Nenhum quadro entrou no Npcap; verificar interfaces/filtro, sem inferir criptografia";
        if (snap.CapturedUdp == 0)
            return "Npcap vê quadros, mas nenhum UDP no parser; pode ser rota, encapsulamento ou filtro";
        if (snap.CandidatePackets == 0)
            return "Npcap vê UDP, mas nenhum candidato Photon; porta diversa/túnel é hipótese, não prova";
        if (snap.DecodedEvents == 0)
            return "Há candidatos UDP, mas nenhum evento Photon decodificado";
        return "Entradas Npcap e Photon observadas; conferir horário do último evento";
    }

    public static void ObserveCandidate(string adapter)
    {
        Interlocked.Exchange(ref _lastCandidateTicks, DateTime.UtcNow.Ticks);
        Interlocked.Increment(ref _candidatePackets);
        if (string.IsNullOrWhiteSpace(adapter)) return;
        lock (Gate)
        {
            // Strict bound. A VPN that repeatedly creates adapters must not
            // cause unbounded diagnostic allocations.
            if (CandidateAdapters.ContainsKey(adapter) || CandidateAdapters.Count < 12)
                CandidateAdapters[adapter] = DateTime.UtcNow;
        }
    }

    public static void ObserveDecodedEvent(short code, Dictionary<byte, object> parameters)
    {
        Interlocked.Exchange(ref _lastDecodedTicks, DateTime.UtcNow.Ticks);
        Interlocked.Increment(ref _decodedEvents);
    }

    public static void ObserveSelectedAdapter(string? adapter)
    {
        lock (Gate) _selectedAdapter = adapter;
    }

    public static void ObserveNetworkChange() => Interlocked.Increment(ref _networkChanges);
    public static void ObserveUserRescan() => Interlocked.Increment(ref _userRescans);
    public static void ObserveRecoveryAttempt() => Interlocked.Increment(ref _recoveryAttempts);
    public static void ObserveProbeStarted() => Interlocked.Increment(ref _probeStarts);
    public static void ObserveProbeConfirmed() => Interlocked.Increment(ref _probeConfirmed);
    public static void ObserveProbeReverted() => Interlocked.Increment(ref _probeReverted);

    public static VpnCaptureDiagnosticsSnapshot Snapshot(bool enabled, bool gameDetected)
    {
        var now = DateTime.UtcNow;
        var candidate = ReadUtc(ref _lastCandidateTicks);
        var decoded = ReadUtc(ref _lastDecodedTicks);
        string? selected;
        int count;
        lock (Gate)
        {
            selected = _selectedAdapter;
            count = CandidateAdapters.Count;
        }
        var processState = Volatile.Read(ref _albionProcessPresence);
        bool? gameProcess = processState == 0 ? null : processState == 2;
        return new VpnCaptureDiagnosticsSnapshot(
            enabled,
            VpnCaptureHealthPolicy.Describe(enabled, gameDetected || (enabled && gameProcess == true),
                now, candidate, decoded),
            selected,
            candidate,
            decoded,
            count,
            Interlocked.Read(ref _candidatePackets),
            Interlocked.Read(ref _decodedEvents),
            Interlocked.Read(ref _networkChanges),
            Interlocked.Read(ref _userRescans),
            Interlocked.Read(ref _recoveryAttempts),
            Interlocked.Read(ref _probeStarts),
            Interlocked.Read(ref _probeConfirmed),
            Interlocked.Read(ref _probeReverted),
            Interlocked.Read(ref _ingressFrames),
            Interlocked.Read(ref _ingressUdp),
            Interlocked.Read(ref _ingressOtherUdpPorts),
            Interlocked.Read(ref _ingressTcp),
            ReadUtc(ref _lastIngressTicks),
            Volatile.Read(ref _openedInterfaces),
            Volatile.Read(ref _packetFilterConfigured) == 1,
            gameProcess);
    }

    private static DateTime? ReadUtc(ref long ticks)
    {
        var value = Interlocked.Read(ref ticks);
        return value > 0 ? new DateTime(value, DateTimeKind.Utc) : null;
    }
}
