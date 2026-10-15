#nullable enable
using System;
using System.Collections.Generic;

namespace StatisticsAnalysisTool.Network.PacketProviders;

/// <summary>
/// Deliberately conservative, bounded probes. A Photon-silent session is
/// not proof of a network fault; a different interface must have substantial
/// fresh candidate traffic first. Probes never run on an unobserved adapter.
/// </summary>
internal static class VpnCaptureRecoveryPolicy
{
    internal static readonly TimeSpan SilentBeforeProbe = TimeSpan.FromSeconds(25);
    internal static readonly TimeSpan CandidateFreshness = TimeSpan.FromSeconds(12);
    internal static readonly TimeSpan ProbeDuration = TimeSpan.FromSeconds(8);
    internal static readonly TimeSpan ProbeCooldown = TimeSpan.FromSeconds(90);
    internal const int MinAlternateCandidates = 6;
    internal const int MaxProbesPerHour = 3;

    internal static bool CanProbe(
        bool enabled,
        DateTime nowUtc,
        DateTime sessionStartedUtc,
        DateTime? lastDecodedUtc,
        DateTime? lastProbeUtc,
        DateTime? alternateLastSeenUtc,
        int alternateCandidates,
        int probesInLastHour)
    {
        if (!enabled || alternateCandidates < MinAlternateCandidates ||
            probesInLastHour >= MaxProbesPerHour ||
            alternateLastSeenUtc is null ||
            nowUtc - alternateLastSeenUtc.Value > CandidateFreshness ||
            nowUtc - alternateLastSeenUtc.Value < TimeSpan.Zero)
            return false;

        // If no event has decoded yet, give the session at least 25s to
        // establish its first live route. Never probe immediately on startup.
        var baseline = lastDecodedUtc.HasValue && lastDecodedUtc.Value > sessionStartedUtc
            ? lastDecodedUtc.Value : sessionStartedUtc;
        if (nowUtc - baseline < SilentBeforeProbe)
            return false;

        return lastProbeUtc is null || nowUtc - lastProbeUtc.Value >= ProbeCooldown;
    }

    internal static bool TrialSucceeded(long decodedEventsBefore, long decodedEventsNow) =>
        decodedEventsNow > decodedEventsBefore;

    internal static bool TrialExpired(DateTime nowUtc, DateTime startedUtc) =>
        nowUtc - startedUtc >= ProbeDuration;
}

internal sealed class VpnCandidateTrafficWindow
{
    private readonly Queue<DateTime> _recent = new();
    public int Count => _recent.Count;
    public DateTime LastSeenUtc { get; private set; }
    public void Observe(DateTime nowUtc)
    {
        LastSeenUtc = nowUtc;
        _recent.Enqueue(nowUtc);
        while (_recent.Count > 0 &&
               nowUtc - _recent.Peek() > VpnCaptureRecoveryPolicy.CandidateFreshness)
            _recent.Dequeue();
        while (_recent.Count > 64)
            _recent.Dequeue();
    }
}
