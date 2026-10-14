using StatisticsAnalysisTool.Imortais.DeathReview;
using StatisticsAnalysisTool.Network.Events;
using StatisticsAnalysisTool.Network.Manager;
using System.Threading.Tasks;

namespace StatisticsAnalysisTool.Network.Handler;

public class CharacterEquipmentChangedEventHandler(TrackingController trackingController) : EventPacketHandler<CharacterEquipmentChangedEvent>((int) EventCodes.CharacterEquipmentChanged)
{
    protected override async Task OnActionAsync(CharacterEquipmentChangedEvent value)
    {
        if (value.ObjectId != null)
        {
            var localObjectId = trackingController.EntityController.LocalUserData.UserObjectId.GetValueOrDefault();
            if (localObjectId > 0 && value.ObjectId.Value == localObjectId)
            {
                CombatSkillEvidenceCapture.ObserveOwnEquipmentSelection(
                    localObjectId, value.ObjectId.Value, value.SelectedSpellIndices);
            }
            await trackingController.EntityController.SetCharacterEquipmentAsync((long) value.ObjectId, value.CharacterEquipment);
        }
    }
}