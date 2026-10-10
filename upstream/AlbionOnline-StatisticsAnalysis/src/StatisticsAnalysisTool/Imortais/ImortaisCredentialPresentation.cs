namespace StatisticsAnalysisTool.Imortais;

// Server-confirmed credential type. Never infer pairing from the token prefix:
// the master key authenticates telemetry but does not authorize Guild Might.
public static class ImortaisCredentialPresentation
{
    public static (string Text, bool Healthy) Describe(
        bool enabled, bool configured, string? credentialMode, bool rankingEligible)
    {
        if (!enabled) return ("TELEMETRIA DESATIVADA", false);
        if (!configured || credentialMode == "none") return ("SEM CHAVE · PAREIE O CLIENT", false);
        return credentialMode switch
        {
            "paired" when rankingEligible => ("PAREADO · RANKING HABILITADO", true),
            "paired" => ("PAREADO · RANKING NÃO AUTORIZADO", false),
            "master" => ("CHAVE GERAL · PAREIE PARA O RANKING", false),
            _ => ("CHAVE CONFIGURADA · VERIFICANDO PAREAMENTO", false)
        };
    }
}
