using StatisticsAnalysisTool.Cluster;
using StatisticsAnalysisTool.Enumerations;
using StatisticsAnalysisTool.Imortais;
using StatisticsAnalysisTool.Models.NetworkModel;
using StatisticsAnalysisTool.Network.Events;
using StatisticsAnalysisTool.Network.Manager;
using System;
using System.Threading.Tasks;

namespace StatisticsAnalysisTool.Network.Handler;

public class NewCharacterEventHandler(TrackingController trackingController) : EventPacketHandler<NewCharacterEvent>((int) EventCodes.NewCharacter)
{
    protected override async Task OnActionAsync(NewCharacterEvent value)
    {
        if (value.ObjectId is { } presenceObjectId && !string.IsNullOrWhiteSpace(value.Name))
        {
            ImortaisEventBridge.NearbyPlayerObserved(
                presenceObjectId,
                value.Guid,
                value.Name,
                value.GuildName,
                value.AllianceName,
                ClusterController.GetCurrentClusterDisplayName());
        }

        if (value.Guid != null && value.ObjectId != null)
        {
            trackingController.EntityController.AddEntity(new Entity
            {
                ObjectId = value.ObjectId,
                UserGuid = value.Guid ?? Guid.Empty,
                Name = value.Name,
                Guild = value.GuildName,
                Alliance = value.AllianceName,
                CharacterEquipment = value.CharacterEquipment,
                ObjectType = GameObjectType.Player,
                ObjectSubType = GameObjectSubType.Player
            });
        }

        await Task.CompletedTask;
    }
}