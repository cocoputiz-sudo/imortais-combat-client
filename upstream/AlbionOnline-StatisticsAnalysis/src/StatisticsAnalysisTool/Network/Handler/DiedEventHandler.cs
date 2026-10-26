using StatisticsAnalysisTool.Cluster;
using StatisticsAnalysisTool.EventLogging;
using StatisticsAnalysisTool.EventLogging.Notification;
using StatisticsAnalysisTool.Imortais;
using StatisticsAnalysisTool.Imortais.Highlights;
using StatisticsAnalysisTool.Imortais.DeathReview;
using StatisticsAnalysisTool.Localization;
using StatisticsAnalysisTool.Models;
using StatisticsAnalysisTool.Models.NetworkModel;
using StatisticsAnalysisTool.Network.Events;
using StatisticsAnalysisTool.Network.Manager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StatisticsAnalysisTool.Network.Handler;

public class DiedEventHandler(TrackingController trackingController) : EventPacketHandler<DiedEvent>((int) EventCodes.Died)
{
    protected override async Task OnActionAsync(DiedEvent value)
    {
        if(value.IsLethal)
        {
            var local=trackingController.EntityController.LocalUserData;
            var localName=local?.Username??string.Empty;
            var localObjectId=local?.UserObjectId;
            var personalAbate=
                (!string.IsNullOrWhiteSpace(localName)&&string.Equals(value.KilledBy,localName,StringComparison.OrdinalIgnoreCase))
                || (localObjectId.HasValue&&localObjectId.Value!=0&&value.KillerObjectId==localObjectId.Value);
            var ownDeath=
                (!string.IsNullOrWhiteSpace(localName)&&string.Equals(value.Died,localName,StringComparison.OrdinalIgnoreCase))
                || (localObjectId.HasValue&&localObjectId.Value!=0&&value.DiedObjectId==localObjectId.Value);
            ImortaisEventBridge.DiedEventLocalDiagnostic(
                value.Died,value.DiedPlayerGuild,value.DiedObjectId,
                value.KilledBy,value.KilledByGuild,value.KillerObjectId,
                ownDeath,personalAbate,
                trackingController.EntityController.IsEntityInParty(value.KillerObjectId)
                    || trackingController.EntityController.IsEntityInParty(value.KilledBy),
                trackingController.EntityController.IsEntityInParty(value.DiedObjectId)
                    || trackingController.EntityController.IsEntityInParty(value.Died),
                ClusterController.GetCurrentClusterDisplayName());

            if (ownDeath && DeathCombatReviewCapture.IsEnabled)
            {
                // Identity is already established by the same name/object-id guard used for highlights.
                // Prefer the tracked local ID over the packet ID when ownership was matched by name.
                var reviewVictimId = localObjectId.GetValueOrDefault() > 0
                    ? localObjectId.Value
                    : value.DiedObjectId;
                var reviewVictimName = string.IsNullOrWhiteSpace(value.Died) ? localName : value.Died;
                var equipped = reviewVictimId > 0
                    ? trackingController.EntityController.GetLastLocalCharacterEquipment()
                        ?? trackingController.EntityController.GetEntity(reviewVictimId)?.Value.CharacterEquipment
                    : null;
                DeathCombatReviewCapture.OnOwnLethalDeath(
                    reviewVictimName, reviewVictimId, value.KilledBy, value.KillerObjectId,
                    ClusterController.GetCurrentClusterDisplayName(), equipped);
            }

            // Death outranks a personal kill: never emit both for the same lethal event.
            if(ownDeath || personalAbate)
            {
                HighlightTriggerService.Publish(
                    ownDeath ? HighlightTriggerKind.Death : HighlightTriggerKind.Abate,
                    value.Died,
                    value.KilledBy,
                    HighlightTriggerService.QpcNow100Ns());
            }
        }

        var relevantToImortais =
            IsImortaisFamilyGuild(value.DiedPlayerGuild)
            || IsImortaisFamilyGuild(value.KilledByGuild)
            || trackingController.EntityController.IsEntityInParty(value.DiedObjectId)
            || trackingController.EntityController.IsEntityInParty(value.KillerObjectId)
            || trackingController.EntityController.IsEntityInParty(value.Died)
            || trackingController.EntityController.IsEntityInParty(value.KilledBy);

        if (value.IsLethal && relevantToImortais)
        {
            var victimEntity = FindPlayer(value.DiedObjectId, value.Died);
            var killerEntity = FindPlayer(value.KillerObjectId, value.KilledBy);
            ImortaisEventBridge.ObservePlayerDeath(
                value.DiedObjectId,
                value.Died,
                value.DiedPlayerGuild,
                value.KillerObjectId,
                value.KilledBy,
                value.KilledByGuild,
                true,
                ClusterController.GetCurrentClusterDisplayName(),
                EquipmentNames(victimEntity?.CharacterEquipment),
                EquipmentNames(killerEntity?.CharacterEquipment),
                victimEntity?.Alliance,
                killerEntity?.Alliance);
        }

        if (trackingController.DungeonController is { } dungeonController)
        {
            await dungeonController.SetDiedIfInDungeonAsync(new DiedObject(value.Died, value.KilledBy, value.KilledByGuild));
        }

        trackingController.PartyController.PlayerHasDied(value.Died);
        trackingController.StatisticController.ResolvePlayerCombatResult(
            value.DiedObjectId,
            value.Died,
            value.KillerObjectId,
            value.KilledBy,
            value.IsLethal);

        if (trackingController.IsKillTrackingEnabled)
        {
            var diedPlayer = trackingController.EntityController.GetEntity(value.DiedObjectId)?.Value
                             ?? trackingController.EntityController.GetEntity(value.Died)?.Value;
            var killerPlayer = trackingController.EntityController.GetEntity(value.KillerObjectId)?.Value
                               ?? trackingController.EntityController.GetEntity(value.KilledBy)?.Value;
            var diedPlayerAlliance = diedPlayer?.Alliance ?? string.Empty;
            var killedByAlliance = killerPlayer?.Alliance ?? string.Empty;
            var clusterName = ClusterController.GetCurrentClusterDisplayName();
            await trackingController.LootController.AddKillDeathAsync(value.Died, value.DiedPlayerGuild, diedPlayerAlliance, value.KilledBy, value.KilledByGuild, killedByAlliance, clusterName);
            await trackingController.AddNotificationAsync(SetKillNotification(value.Died, value.DiedPlayerGuild, diedPlayerAlliance, value.KilledBy, value.KilledByGuild, killedByAlliance, clusterName));
        }
    }

    private static bool IsImortaisFamilyGuild(string guild)
    {
        var normalized = string.Concat((guild ?? string.Empty).Where(char.IsLetterOrDigit)).ToLowerInvariant();
        return normalized is "imortais" or "imortais2" or "imortaisacademy";
    }

    private static TrackingNotification SetKillNotification(string died, string diedPlayerGuild, string diedPlayerAlliance,
        string killedBy, string killedByGuild, string killedByAlliance, string clusterName)
    {
        var notification = new TrackingNotification(DateTime.Now, new KillNotificationFragment(died, diedPlayerGuild, diedPlayerAlliance,
            killedBy, killedByGuild, killedByAlliance, LocalizationController.Translation("WAS_KILLED_BY")), LoggingFilterType.Kill);
        notification.SetClusterName(clusterName);

        return notification;
    }

    private PlayerGameObject? FindPlayer(long objectId, string? name)
    {
        try
        {
            var entity = objectId > 0 ? trackingController.EntityController.GetEntity(objectId) : null;
            if (entity?.Value == null && !string.IsNullOrWhiteSpace(name))
            {
                entity = trackingController.EntityController.GetEntity(name);
            }
            return entity?.Value;
        }
        catch
        {
            return null;
        }
    }

    // Nomes dos itens equipados (ex.: T8_2H_HAMMER_UNDEAD@2). Falha ao resolver um item não impede o evento.
    private static IReadOnlyDictionary<string, string?>? EquipmentNames(CharacterEquipment? equipment)
    {
        if (equipment == null) return null;
        string? Name(Func<Item> get)
        {
            try { return get()?.UniqueName; } catch { return null; }
        }
        return new Dictionary<string, string?>
        {
            ["mainHand"] = Name(equipment.GetMainHand),
            ["offHand"] = Name(equipment.GetOffHand),
            ["head"] = Name(equipment.GetHead),
            ["chest"] = Name(equipment.GetChest),
            ["shoes"] = Name(equipment.GetShoes),
            ["cape"] = Name(equipment.GetCape),
            ["mount"] = Name(equipment.GetMount)
        };
    }
}