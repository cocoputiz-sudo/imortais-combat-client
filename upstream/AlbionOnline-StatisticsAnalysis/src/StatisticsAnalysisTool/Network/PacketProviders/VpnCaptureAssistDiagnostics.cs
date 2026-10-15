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
    long ProbeReverted);

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
        return new VpnCaptureDiagnosticsSnapshot(
            enabled,
            VpnCaptureHealthPolicy.Describe(enabled, gameDetected, now, candidate, decoded),
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
            Interlocked.Read(ref _probeReverted));
    }

    private static DateTime? ReadUtc(ref long ticks)
    {
        var value = Interlocked.Read(ref ticks);
        return value > 0 ? new DateTime(value, DateTimeKind.Utc) : null;
    }
}
