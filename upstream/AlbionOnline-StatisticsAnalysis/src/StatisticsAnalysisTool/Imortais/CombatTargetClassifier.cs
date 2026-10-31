namespace StatisticsAnalysisTool.Imortais;
/// <summary>Never classify an unknown object as a mob.</summary>
public static class CombatTargetClassifier
{
    public static string Classify(bool isPlayer, bool confirmedMob)
        => isPlayer ? "player" : confirmedMob ? "mob" : "unknown";
}
