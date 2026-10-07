using StatisticsAnalysisTool.Imortais;
using System.Threading.Tasks;

namespace StatisticsAnalysisTool.Network.Handler;

/// <summary>
/// Probe passivo dos requests de Might da guilda. Não cria nem altera requests:
/// apenas observa aqueles que o próprio cliente do Albion enviou.
/// </summary>
public sealed class ImortaisGuildMightProbeRequestHandler : PacketHandler<RequestPacket>
{
    private readonly string _operationName;

    public ImortaisGuildMightProbeRequestHandler(OperationCodes operationCode)
        : base((int) operationCode)
    {
        _operationName = operationCode.ToString();
    }

    protected override Task OnHandleAsync(RequestPacket packet)
    {
        ImortaisEventBridge.GuildMightProbe(
            "request",
            _operationName,
            packet.OperationCode,
            packet.Parameters);
        return Task.CompletedTask;
    }
}
