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

internal sealed record SkillEvidenceSnapshot(
    string Schema,
    DateTime? LastEquipmentSelectionUtc,
    long? EquipmentSelectionObjectId,
    IReadOnlyList<SkillChoiceEvidence> OwnSelectedSkills,
    IReadOnlyList<CastShapeEvidence> CastPacketShapes,
    bool CastSamplesTruncated,
    IReadOnlyList<SpellCatalogCandidate> DamageSpellCatalogCandidates,
    string CooldownAvailabilityStatus,
    string CastAttributionStatus);

/// <summary>
/// Evidence for post-death reports, NOT realtime combat decisions.
/// Raw Photon cast parameters are never retained: only key/types for protocol validation.
/// </summary>
internal sealed class SkillEvidenceCollector
{
    private readonly object _sync = new();
    private readonly Queue<CastShapeEvidence> _samples = new();
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
            var bounded = shapes.Take(20).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            _samples.Enqueue(new CastShapeEvidence(receivedUtc, code, expectedEventName, bounded));
            Prune(receivedUtc);
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

            var snapshot = new SkillEvidenceSnapshot(
                "experimental-post-death-v1",
                choiceIsCurrentActor ? _selectionUtc : null,
                choiceIsCurrentActor ? _selectionObjectId : null,
                choices,
                _samples.Where(s => s.ReceivedUtc <= deathUtc
                                    && s.ReceivedUtc >= deathUtc - _window).ToArray(),
                _truncated,
                candidates,
                "indeterminado: sem historico de casts/cooldowns validado",
                "indeterminado: assinaturas Photon sem IDs de atores nem nomes verificados");

            // Clear per-death evidence so the next life cannot reuse a stale selection.
            _samples.Clear();
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
            _samples.Clear();
            _truncated = false;
            _selectionObjectId = 0;
            _selectionUtc = null;
            _selectedSpellIndices = null;
        }
    }

    private void Prune(DateTime now)
    {
        var cutoff = now - _window;
        while (_samples.Count > 0 && _samples.Peek().ReceivedUtc < cutoff)
            _samples.Dequeue();
        while (_samples.Count > _maxSamples)
        {
            _samples.Dequeue();
            _truncated = true;
        }
    }

    private static string? NullIfBlank(string? candidate) =>
        string.IsNullOrWhiteSpace(candidate) ? null : candidate;
}
