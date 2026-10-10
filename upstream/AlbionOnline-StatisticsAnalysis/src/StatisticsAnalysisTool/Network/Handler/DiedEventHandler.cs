using StatisticsAnalysisTool.Cluster;
using StatisticsAnalysisTool.EventLogging;
using StatisticsAnalysisTool.EventLogging.Notification;
using StatisticsAnalysisTool.Imortais;
using StatisticsAnalysisTool.Imortais.Highlights;
using StatisticsAnalysisTool.Localization;
using StatisticsAnalysisTool.Models.NetworkModel;
using StatisticsAnalysisTool.Network.Events;
using StatisticsAnalysisTool.Network.Manager;
using System;
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
            ImortaisEventBridge.ObservePlayerDeath(
                value.DiedObjectId,
                value.Died,
                value.DiedPlayerGuild,
                value.KillerObjectId,
                value.KilledBy,
                value.KilledByGuild,
                true,
                ClusterController.GetCurrentClusterDisplayName());
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
}