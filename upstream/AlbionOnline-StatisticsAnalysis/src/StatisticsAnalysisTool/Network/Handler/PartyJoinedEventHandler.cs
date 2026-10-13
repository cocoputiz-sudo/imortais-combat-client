using StatisticsAnalysisTool.Imortais;
using StatisticsAnalysisTool.Network.Events;
using StatisticsAnalysisTool.Network.Manager;
using System.Threading.Tasks;

namespace StatisticsAnalysisTool.Network.Handler;

public class PartyJoinedEventHandler(TrackingController trackingController) : EventPacketHandler<PartyJoinedEvent>((int) EventCodes.PartyJoined)
{
    protected override async Task OnActionAsync(PartyJoinedEvent value)
    {
        ImortaisEventBridge.PartyJoinedObservation(
            value.HasCompleteRoster, value.PartyUsers.Count, value.DecodeDiagnostics);
        if (!value.HasCompleteRoster)
        {
            return;
        }

        await trackingController.EntityController.SetPartyAsync(value.PartyUsers);
    }
}