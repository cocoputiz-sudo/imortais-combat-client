using StatisticsAnalysisTool.Imortais;
using System.Threading.Tasks;

namespace StatisticsAnalysisTool.Network.Handler;

/// <summary>
/// Experimental IMORTAIS collector for guild-presence related Photon events.
/// Payload fields stay opaque until they are validated against real live traffic.
/// </summary>
public sealed class ImortaisGuildPresenceProbeEventHandler : PacketHandler<EventPacket>
{
    private readonly string _eventName;

    public ImortaisGuildPresenceProbeEventHandler(EventCodes eventCode)
        : base((int)eventCode)
    {
        _eventName = eventCode.ToString();
    }

    protected override Task OnHandleAsync(EventPacket packet)
    {
        ImortaisEventBridge.GuildPresenceProbe(_eventName, packet.EventCode, packet.Parameters);
        return Task.CompletedTask;
    }
}
