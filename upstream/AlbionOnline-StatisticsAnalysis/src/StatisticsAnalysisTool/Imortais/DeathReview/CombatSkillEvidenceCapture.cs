#nullable enable
using StatisticsAnalysisTool.GameFileData;
using StatisticsAnalysisTool.Network;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StatisticsAnalysisTool.Imortais.DeathReview;

/// <summary>
/// Supplements the private death report. Cast codes are PROVISIONAL until real-game
/// mapping is verified. Never reports available cooldowns or emits live skill alerts.
/// </summary>
internal static class CombatSkillEvidenceCapture
{
    private static readonly SkillEvidenceCollector Collector = new(TimeSpan.FromSeconds(45), 256);
    private static readonly IReadOnlyDictionary<short, string> CastCodes = new Dictionary<short, string>
    {
        [(short)EventCodes.CastStart] = nameof(EventCodes.CastStart),
        [(short)EventCodes.CastFinished] = nameof(EventCodes.CastFinished),
        [(short)EventCodes.CastCancel] = nameof(EventCodes.CastCancel),
        [(short)EventCodes.CastSpell] = nameof(EventCodes.CastSpell),
        [(short)EventCodes.CastSpells] = nameof(EventCodes.CastSpells),
        [(short)EventCodes.CastHit] = nameof(EventCodes.CastHit),
        [(short)EventCodes.CastHits] = nameof(EventCodes.CastHits),
        [(short)EventCodes.ChannelingUpdate] = nameof(EventCodes.ChannelingUpdate),
        [(short)EventCodes.ResetCooldowns] = nameof(EventCodes.ResetCooldowns)
    };

    public static void ObservePacketShape(short code, Dictionary<byte, object> parameters)
    {
        if (!DeathCombatReviewCapture.IsEnabled || parameters is null ||
            !CastCodes.TryGetValue(code, out var expectedName))
            return;

        // Store field shapes only, no values, player IDs or raw Photon payloads.
        var shapes = parameters
            .OrderBy(x => x.Key)
            .Take(20)
            .ToDictionary(
                x => x.Key.ToString(CultureInfo.InvariantCulture),
                x => PhotonEventDiagnosticsCollector.DescribeShape(x.Value),
                StringComparer.Ordinal);
        Collector.ObserveCastShape(code, expectedName, DateTime.UtcNow, shapes);
    }

    public static void ObserveOwnEquipmentSelection(long ownObjectId, long affectedObjectId,
        IReadOnlyList<int>? selectedSpellIndices)
    {
        if (!DeathCombatReviewCapture.IsEnabled ||
            ownObjectId <= 0 || ownObjectId != affectedObjectId) return;
        Collector.ObserveOwnSelection(ownObjectId, selectedSpellIndices, DateTime.UtcNow);
    }

    public static SkillEvidenceSnapshot FreezeAtDeath(DateTime deathUtc, long ownObjectId,
        IReadOnlyCollection<int> damageSpellIndices)
    {
        return Collector.Freeze(
            deathUtc,
            ownObjectId,
            damageSpellIndices,
            index => SpellData.GetUniqueName(index));
    }

    public static void Reset() => Collector.Reset();
}
