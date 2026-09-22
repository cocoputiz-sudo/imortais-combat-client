using StatisticsAnalysisTool.Imortais;
using System.Threading.Tasks;

namespace StatisticsAnalysisTool.Network.Handler;

/// <summary>
/// Experimental IMORTAIS collector for guild-presence related Photon events.
/// The Albion protocol fields are intentionally treated as opaque until we
/// validate their live shape on multiple clients. Only the selected guild
/// events are forwarded to the War Room as bounded diagnostic probes.
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
