using StatisticsAnalysisTool.Imortais.DeathReview;
using StatisticsAnalysisTool.Models.NetworkModel;
using StatisticsAnalysisTool.Network.Events;
using StatisticsAnalysisTool.Network.Manager;
using System.Threading.Tasks;

namespace StatisticsAnalysisTool.Network.Handler;

public class HealthUpdatesEventHandler(TrackingController trackingController) : EventPacketHandler<HealthUpdatesEvent>((int) EventCodes.HealthUpdates)
{
    protected override async Task OnActionAsync(HealthUpdatesEvent value)
    {
        var reviewEnabled = DeathCombatReviewCapture.IsEnabled;
        var localId = reviewEnabled ? (trackingController.EntityController.LocalUserData.UserObjectId ?? 0) : 0;
        foreach (HealthUpdate healthUpdate in value.HealthUpdates)
        {
            if (localId > 0 && healthUpdate.AffectedObjectId == localId)
            {
                var source = healthUpdate.CauserId > 0
                    ? trackingController.EntityController.GetEntity(healthUpdate.CauserId)?.Value.Name
                    : null;
                DeathCombatReviewCapture.ObserveLocalHealthChange(
                    localId, healthUpdate.AffectedObjectId, healthUpdate.CauserId, source,
                    healthUpdate.HealthChange,
                    healthUpdate.HasNewHealthValue ? healthUpdate.NewHealthValue : null,
                    healthUpdate.CausingSpellIndex, healthUpdate.EffectType.ToString());
            }
            var mob = trackingController.CombatController.CombatEventTracker.GetKnownMobOrDefault(healthUpdate.AffectedObjectId);
            trackingController.MobKillController.TrackLocalPlayerMobDamage(healthUpdate.AffectedObjectId, healthUpdate.CauserId, healthUpdate.HealthChange);

            if (healthUpdate.HealthChange < 0 && !healthUpdate.HasNewHealthValue)
            {
                trackingController.MobKillController.TryAddMobKill(healthUpdate.AffectedObjectId, mob, healthUpdate.HealthChange, healthUpdate.HasNewHealthValue);
            }

            await trackingController.CombatController.AddDamage(healthUpdate.AffectedObjectId, healthUpdate.CauserId, healthUpdate.HealthChange, healthUpdate.NewHealthValue, healthUpdate.CausingSpellIndex, healthUpdate.EffectType);
            await trackingController.CombatController.AddTakenDamage(healthUpdate.AffectedObjectId, healthUpdate.CauserId, healthUpdate.HealthChange, healthUpdate.NewHealthValue, healthUpdate.CausingSpellIndex);
        }
    }
}