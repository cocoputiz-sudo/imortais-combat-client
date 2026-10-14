#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace StatisticsAnalysisTool.Imortais.DeathReview;

internal sealed record SkillChoiceEvidence(
    string Slot, int? SelectedSpellIndex, string? CatalogUniqueName,
    string Availability, string SelectionEvidence);

internal sealed record CastShapeEvidence(
    DateTime ReceivedUtc, short ObservedCode, string ExpectedEventName,
    IReadOnlyDictionary<string, string> FieldShapes);

internal sealed record SpellCatalogCandidate(
    int CausingSpellIndex, string? CatalogUniqueName, string Status);

// Aggregated per-second evidence across the WHOLE 45s window, including
// events omitted from the deliberately sparse, metadata-only sample.
internal sealed record CastSecondSummary(
    DateTime SecondUtc, int ObservedPackets, int RetainedSamples,
    int OmittedSamples, IReadOnlyDictionary<string, int> CountsByEvent);

internal sealed record SkillEvidenceSnapshot(
    string Schema,
    DateTime? LastEquipmentSelectionUtc,
    long? EquipmentSelectionObjectId,
    IReadOnlyList<SkillChoiceEvidence> OwnSelectedSkills,
    IReadOnlyList<CastShapeEvidence> CastPacketShapes,
    bool CastSamplesTruncated,
    IReadOnlyList<SpellCatalogCandidate> DamageSpellCatalogCandidates,
    string CooldownAvailabilityStatus,
    string CastAttributionStatus,
    int TotalCastPacketsObserved = 0,
    int TotalCastPacketsOmitted = 0,
    IReadOnlyList<CastSecondSummary>? CastCoverageBySecond = null);

/// <summary>
/// Evidence for post-death reports, NOT realtime combat decisions.
/// Raw Photon cast parameters are never retained: only key/types for protocol validation.
/// </summary>
internal sealed class SkillEvidenceCollector
{
    private readonly object _sync = new();
    private sealed class CastSecondBucket
    {
        public DateTime SecondUtc { get; init; }
        public int Observed;
        public int Omitted;
        public readonly Dictionary<string, int> ByEvent = new(StringComparer.Ordinal);
        public readonly List<CastShapeEvidence> Samples = new();
    }

    private readonly SortedDictionary<long, CastSecondBucket> _seconds = new();
    private int _retainedSamples;
    private readonly int _samplesPerSecond;
    private readonly TimeSpan _window;
    private readonly int _maxSamples;
    private bool _truncated;
    private long _selectionObjectId;
    private DateTime? _selectionUtc;
    private int[]? _selectedSpellIndices;

    private static readonly (string Slot, int Index)[] Slots =
    {
        ("Q", 0), ("W", 1), ("E", 2),
        ("Peito", 3), ("Capacete", 4), ("Botas", 5),
        ("Poção", 12)
    };

    public SkillEvidenceCollector(TimeSpan window, int maxSamples)
    {
        if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
        if (maxSamples < 1) throw new ArgumentOutOfRangeException(nameof(maxSamples));
        _window = window;
        _maxSamples = maxSamples;
        // 46 buckets can overlap a 45-second sliding window (partial seconds).
        // Avoid a ZvZ burst evicting evidence from the preceding 43 seconds.
        _samplesPerSecond = Math.Max(1, maxSamples / (int)Math.Ceiling(window.TotalSeconds + 1));
    }

    public void ObserveOwnSelection(long ownObjectId, IReadOnlyList<int>? selectedSpells, DateTime receivedUtc)
    {
        if (ownObjectId <= 0 || selectedSpells is null || selectedSpells.Count < 3) return;
        lock (_sync)
        {
            _selectionObjectId = ownObjectId;
            _selectionUtc = receivedUtc;
            // Preserve slot offsets, including -1, instead of using ActiveSpells' compacted list.
            _selectedSpellIndices = selectedSpells.Take(16).ToArray();
        }
    }

    public void ObserveCastShape(short code, string expectedEventName, DateTime receivedUtc, IReadOnlyDictionary<string, string> shapes)
    {
        lock (_sync)
        {
            Prune(receivedUtc);
            var key = receivedUtc.Ticks / TimeSpan.TicksPerSecond;
            if (!_seconds.TryGetValue(key, out var bucket))
            {
                bucket = new CastSecondBucket
                {
                    SecondUtc = new DateTime(key * TimeSpan.TicksPerSecond, DateTimeKind.Utc)
                };
                _seconds.Add(key, bucket);
            }

            bucket.Observed++;
            bucket.ByEvent.TryGetValue(expectedEventName, out var count);
            bucket.ByEvent[expectedEventName] = count + 1;

            // The hourly thousands of CastHit packets need not displace every
            // CastSpell or CastStart in the SAME second. Keep per-second spread.
            if (bucket.Samples.Count >= _samplesPerSecond)
            {
                var duplicate = bucket.Samples.FindIndex(x =>
                    bucket.Samples.Count(y => y.ObservedCode == x.ObservedCode) > 1);
                if (duplicate >= 0 &&
                    !bucket.Samples.Any(x => x.ObservedCode == code))
                {
                    bucket.Samples.RemoveAt(duplicate);
                    _retainedSamples--;
                }
                else
                {
                    bucket.Omitted++;
                    _truncated = true;
                    return;
                }

                bucket.Omitted++;
                _truncated = true;
            }

            var bounded = shapes.Take(20).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            bucket.Samples.Add(new CastShapeEvidence(receivedUtc, code, expectedEventName, bounded));
            _retainedSamples++;
            TrimGlobalBudget();
        }
    }

    public SkillEvidenceSnapshot Freeze(
        DateTime deathUtc, long ownObjectId,
        IReadOnlyCollection<int> observedDamageSpellIndices,
        Func<int, string?> resolveCatalogUniqueName)
    {
        lock (_sync)
        {
            Prune(deathUtc);
            var choiceIsCurrentActor = ownObjectId > 0 && ownObjectId == _selectionObjectId;
            var indices = choiceIsCurrentActor ? _selectedSpellIndices : null;
            var choices = Slots.Select(slot =>
            {
                int? index = indices != null && slot.Index < indices.Length && indices[slot.Index] >= 0
                    ? indices[slot.Index] : null;
                string? name = index.HasValue ? NullIfBlank(resolveCatalogUniqueName(index.Value)) : null;
                return new SkillChoiceEvidence(slot.Slot, index, name,
                    Availability: "indeterminado",
                    SelectionEvidence: index.HasValue
                        ? "CharacterEquipmentChanged.slotIndex"
                        : "sem selecao validada para este slot");
            }).ToArray();

            var candidates = observedDamageSpellIndices
                .Where(index => index >= 0).Distinct().Take(256)
                .Select(index =>
                {
                    var name = NullIfBlank(resolveCatalogUniqueName(index));
                    return new SpellCatalogCandidate(index, name,
                        name is null ? "indice_sem_correspondencia" : "candidato_catalogo_nao_validado");
                }).ToArray();

            var buckets = _seconds.Values.OrderBy(x => x.SecondUtc).ToArray();
            var coverage = buckets.Select(x => new CastSecondSummary(
                x.SecondUtc, x.Observed, x.Samples.Count, x.Omitted,
                new Dictionary<string, int>(x.ByEvent, StringComparer.Ordinal))).ToArray();

            var snapshot = new SkillEvidenceSnapshot(
                "experimental-post-death-v2",
                choiceIsCurrentActor ? _selectionUtc : null,
                choiceIsCurrentActor ? _selectionObjectId : null,
                choices,
                buckets.SelectMany(x => x.Samples)
                    .Where(s => s.ReceivedUtc <= deathUtc
                                && s.ReceivedUtc >= deathUtc - _window)
                    .OrderBy(s => s.ReceivedUtc).ToArray(),
                _truncated,
                candidates,
                "indeterminado: sem historico de casts/cooldowns validado",
                "indeterminado: assinaturas Photon sem IDs de atores nem nomes verificados",
                coverage.Sum(x => x.ObservedPackets),
                coverage.Sum(x => x.OmittedSamples),
                coverage);

            // Clear per-death evidence so the next life cannot reuse a stale selection.
            _seconds.Clear();
            _retainedSamples = 0;
            _truncated = false;
            _selectedSpellIndices = null;
            _selectionObjectId = 0;
            _selectionUtc = null;
            return snapshot;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _seconds.Clear();
            _retainedSamples = 0;
            _truncated = false;
            _selectionObjectId = 0;
            _selectionUtc = null;
            _selectedSpellIndices = null;
        }
    }

    private void Prune(DateTime now)
    {
        // Remove complete old seconds, then filter sub-second boundaries in
        // Freeze. Timestamp boundaries are preserved to the closest second.
        var cutoffSecond = (now - _window).Ticks / TimeSpan.TicksPerSecond;
        while (_seconds.Count > 0 && _seconds.First().Key < cutoffSecond)
        {
            _retainedSamples -= _seconds.First().Value.Samples.Count;
            _seconds.Remove(_seconds.First().Key);
        }
    }

    private void TrimGlobalBudget()
    {
        // For tiny test budgets (< window seconds), keep a hard global cap.
        // Normal 256-sample production has 46 * 5 = 230 maximum, so this
        // is only a guard, never a source of ZvZ history starvation.
        while (_retainedSamples > _maxSamples)
        {
            var oldest = _seconds.Values.First(x => x.Samples.Count > 0);
            oldest.Samples.RemoveAt(0);
            oldest.Omitted++;
            _retainedSamples--;
            _truncated = true;
        }
    }

    private static string? NullIfBlank(string? candidate) =>
        string.IsNullOrWhiteSpace(candidate) ? null : candidate;
}
