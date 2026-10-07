using StatisticsAnalysisTool.Imortais;
using System.Threading.Tasks;

namespace StatisticsAnalysisTool.Network.Handler;

/// <summary>
/// Probe passivo das respostas de Might da guilda. Não envia operação ao Albion;
/// apenas observa respostas que o próprio cliente do jogo solicitou.
/// </summary>
public sealed class ImortaisGuildMightProbeResponseHandler : PacketHandler<ResponsePacket>
{
    private readonly string _operationName;

    public ImortaisGuildMightProbeResponseHandler(OperationCodes operationCode)
        : base((int) operationCode)
    {
        _operationName = operationCode.ToString();
    }

    protected override Task OnHandleAsync(ResponsePacket packet)
    {
        ImortaisEventBridge.GuildMightProbe(
            _operationName,
            packet.OperationCode,
            packet.Parameters);
        return Task.CompletedTask;
    }
}
