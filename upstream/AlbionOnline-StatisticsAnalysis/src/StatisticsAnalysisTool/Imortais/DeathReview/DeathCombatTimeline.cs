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
    SkillEvidenceSnapshot? SkillEvidence = null);

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
                1, "passive-health-update", deathUtc, victim, victimObjectId,
                killer ?? string.Empty, killerObjectId, cluster,
                (int)_window.TotalSeconds,
                CompleteCombatHistory: false,
                CooldownsVerified: false,
                SkillNamesResolved: false,
                EquipmentAtDeath: equipment,
                IncomingHealthChanges: matching);
        }
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
