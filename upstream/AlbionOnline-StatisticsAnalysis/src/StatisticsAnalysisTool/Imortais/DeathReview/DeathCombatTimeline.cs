#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace StatisticsAnalysisTool.Imortais.DeathReview;

// The spell index is evidence from a HealthUpdate, not an identified Q/W/E ability.
internal sealed record IncomingHealthChange(
    DateTime ObservedUtc,
    long VictimObjectId,
    long SourceObjectId,
    string? SourceName,
    double HealthDelta,
    double? HealthAfter,
    int CausingSpellIndex,
    string EffectType);

// Evidence statuses, not a claim to have observed the lethal hit.
internal sealed record DeathTerminalEvidence(
    string TerminalHealthObservationStatus,
    DateTime? LastObservedHealthUtc,
    double? LastObservedHealth,
    double? LastObservedHealthDelta,
    double? LastHealthObservationAgeMilliseconds,
    bool? KillerHadObservedNegativeDelta,
    string FinalBlowAttributionStatus,
    int ObservedNegativeDeltas,
    int ObservedPositiveDeltas);

// Differences between absolute HP readings and the sum of received deltas.
 // These are gaps in OBSERVATION, not proof of damage, healing or max-HP changes.
internal sealed record HealthContinuityGap(
    DateTime PreviousObservationUtc,
    DateTime CurrentObservationUtc,
    double PreviousHealth,
    double SumObservedHealthDeltas,
    double ExpectedHealth,
    double ObservedHealth,
    double UnexplainedDifference,
    double ElapsedMilliseconds,
    int DeltaEventsIncluded,
    string EvidenceStatus);

internal sealed record HealthContinuityEvidence(
    string Status,
    int AbsoluteHealthObservations,
    int ComparableTransitions,
    int UnexplainedTransitions,
    int UnpairedDeltaEvents,
    bool FindingsTruncated,
    IReadOnlyList<HealthContinuityGap> Findings);

internal sealed record ObservedSpellSelection(string Slot, string Value, string ItemIndex);

internal sealed record ObservedEquipment(
    int MainHand, int OffHand, int Head, int Chest, int Shoes,
    int Cape, int Potion, int Food, IReadOnlyList<ObservedSpellSelection> ActiveSpells);

internal sealed record DeathCombatSnapshot(
    int SchemaVersion,
    string Source,
    DateTime DeathUtc,
    string Victim,
    long VictimObjectId,
    string Killer,
    long KillerObjectId,
    string? Cluster,
    int WindowSeconds,
    bool CompleteCombatHistory,
    bool CooldownsVerified,
    bool SkillNamesResolved,
    ObservedEquipment? EquipmentAtDeath,
    IReadOnlyList<IncomingHealthChange> IncomingHealthChanges,
    SkillEvidenceSnapshot? SkillEvidence = null,
    DeathTerminalEvidence? TerminalEvidence = null,
    HealthContinuityEvidence? HealthContinuity = null);

/// <summary>
/// Thread-safe, RAM-only, victim-scoped bounded timeline. No disk or network I/O.
/// ObservedUtc is CLIENT reception time, not authoritative Photon event time.
/// </summary>
internal sealed class DeathCombatTimeline
{
    private readonly object _gate = new();
    private readonly Queue<IncomingHealthChange> _events = new();
    private readonly TimeSpan _window;
    private readonly int _maxEvents;
    private long _trackedVictimId;
    private DateTime _lastDeathUtc = DateTime.MinValue;
    private long _lastDeathVictimId;

    public DeathCombatTimeline(TimeSpan window, int maxEvents)
    {
        if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
        if (maxEvents <= 0) throw new ArgumentOutOfRangeException(nameof(maxEvents));
        _window = window;
        _maxEvents = maxEvents;
    }

    public void Observe(IncomingHealthChange observation)
    {
        if (observation.VictimObjectId <= 0 || !double.IsFinite(observation.HealthDelta)) return;
        lock (_gate)
        {
            if (_trackedVictimId != observation.VictimObjectId)
            {
                _events.Clear();
                _trackedVictimId = observation.VictimObjectId;
            }
            // Network callbacks can be asynchronous; keep observations in receive order.
            _events.Enqueue(observation);
            Prune(observation.ObservedUtc);
        }
    }

    public DeathCombatSnapshot? FreezeAtDeath(
        DateTime deathUtc, string victim, long victimObjectId,
        string killer, long killerObjectId, string? cluster,
        ObservedEquipment? equipment)
    {
        if (string.IsNullOrWhiteSpace(victim)) return null;
        lock (_gate)
        {
            if (victimObjectId == _lastDeathVictimId
                && deathUtc >= _lastDeathUtc
                && deathUtc - _lastDeathUtc < TimeSpan.FromSeconds(8))
                return null;

            _lastDeathVictimId = victimObjectId;
            _lastDeathUtc = deathUtc;
            Prune(deathUtc);
            var matching = victimObjectId > 0 && _trackedVictimId == victimObjectId
                ? _events.Where(e => e.ObservedUtc <= deathUtc && e.VictimObjectId == victimObjectId).ToArray()
                : Array.Empty<IncomingHealthChange>();
            _events.Clear();
            _trackedVictimId = 0;

            return new DeathCombatSnapshot(
                3, "passive-health-update", deathUtc, victim, victimObjectId,
                killer ?? string.Empty, killerObjectId, cluster,
                (int)_window.TotalSeconds,
                CompleteCombatHistory: false,
                CooldownsVerified: false,
                SkillNamesResolved: false,
                EquipmentAtDeath: equipment,
                IncomingHealthChanges: matching,
                TerminalEvidence: AnalyzeTerminalEvidence(matching, deathUtc, killerObjectId),
                HealthContinuity: AnalyzeHealthContinuity(matching));
        }
    }

    internal static DeathTerminalEvidence AnalyzeTerminalEvidence(
        IReadOnlyList<IncomingHealthChange> changes, DateTime deathUtc, long killerObjectId)
    {
        var last = changes.LastOrDefault(x =>
            x.HealthAfter.HasValue && double.IsFinite(x.HealthAfter.Value));
        var finalHealth = last?.HealthAfter;
        var status = !finalHealth.HasValue
            ? "no_observed_health"
            : finalHealth.Value <= 0 ? "nonpositive_health_observed" : "positive_health_before_death";

        // A positive value immediately before the Died event is NOT proof the
        // game failed to apply fatal damage. The network may omit the last hit.
        var negative = changes.Where(x => x.HealthDelta < 0).ToArray();
        bool? killerEvidence = killerObjectId > 0
            ? negative.Any(x => x.SourceObjectId == killerObjectId)
            : null;
        var attribution = killerEvidence == true
            ? "killer_has_negative_delta_but_lethal_hit_unverified"
            : "killer_credit_without_matching_observed_negative_delta";

        return new DeathTerminalEvidence(
            status,
            last?.ObservedUtc,
            finalHealth,
            last?.HealthDelta,
            last is null ? null
                : Math.Max(0, (deathUtc - last.ObservedUtc).TotalMilliseconds),
            killerEvidence,
            attribution,
            negative.Length,
            changes.Count(x => x.HealthDelta > 0));
    }

    /// <summary>
    /// Compare successive absolute HP readings against ALL finite healthDelta
    /// values received between them (including the delta on the later reading).
    /// Missing Photon observations, max-HP changes and effects cannot be
    /// distinguished here, so unexplained differences are never damage totals.
    /// </summary>
    internal static HealthContinuityEvidence AnalyzeHealthContinuity(
        IReadOnlyList<IncomingHealthChange> changes, int maxFindings = 64)
    {
        if (maxFindings < 1) throw new ArgumentOutOfRangeException(nameof(maxFindings));
        var findings = new List<HealthContinuityGap>();
        IncomingHealthChange? anchor = null;
        var absoluteReadings = 0;
        var comparable = 0;
        var unexplained = 0;
        var unpaired = 0;
        var deltaEventsSinceAnchor = 0;
        double observedDeltasSinceAnchor = 0;

        foreach (var current in changes)
        {
            if (!double.IsFinite(current.HealthDelta)) continue;
            var hasAbsolute = current.HealthAfter is double value && double.IsFinite(value);
            if (anchor is null)
            {
                if (hasAbsolute)
                {
                    anchor = current;
                    absoluteReadings++;
                }
                else
                {
                    unpaired++;
                }
                continue;
            }

            // A packet without absolute HP can still explain part of the next
            // HP transition. Do not compare against only the latest delta.
            observedDeltasSinceAnchor += current.HealthDelta;
            deltaEventsSinceAnchor++;
            if (!hasAbsolute) continue;

            absoluteReadings++;
            var previous = anchor.HealthAfter!.Value;
            var expected = previous + observedDeltasSinceAnchor;
            if (double.IsFinite(expected))
            {
                comparable++;
                var difference = value - expected;
                // Floating-point/rounded observations should not create gaps.
                var tolerance = Math.Max(1.0, Math.Max(Math.Abs(expected), Math.Abs(value)) * 0.001);
                if (Math.Abs(difference) > tolerance)
                {
                    unexplained++;
                    if (findings.Count < maxFindings)
                    {
                        findings.Add(new HealthContinuityGap(
                            anchor.ObservedUtc, current.ObservedUtc, previous,
                            observedDeltasSinceAnchor, expected, value, difference,
                            Math.Max(0, (current.ObservedUtc - anchor.ObservedUtc).TotalMilliseconds),
                            deltaEventsSinceAnchor, "unexplained_by_observed_deltas"));
                    }
                }
            }
            else
            {
                // Overflowed arithmetic cannot support a credible comparison.
                unpaired += deltaEventsSinceAnchor;
            }

            anchor = current;
            observedDeltasSinceAnchor = 0;
            deltaEventsSinceAnchor = 0;
        }

        // No later absolute reading: do not classify these deltas as a gap.
        unpaired += deltaEventsSinceAnchor;
        var status = absoluteReadings == 0 ? "no_absolute_health_observed"
            : comparable == 0 ? "insufficient_comparable_health_observations"
            : unexplained > 0 ? "unexplained_transitions_observed"
            : "no_unexplained_transitions_in_observed_samples";
        return new HealthContinuityEvidence(
            status, absoluteReadings, comparable, unexplained, unpaired,
            unexplained > findings.Count, findings);
    }

    public void Reset()
    {
        lock (_gate)
        {
            _events.Clear();
            _trackedVictimId = 0;
        }
    }

    private void Prune(DateTime now)
    {
        var cutoff = now - _window;
        while (_events.Count > 0 && (_events.Peek().ObservedUtc < cutoff || _events.Count > _maxEvents))
            _events.Dequeue();
    }
}
