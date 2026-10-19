namespace Imortais.LinuxClient.Services;

public static class LinuxTelemetryPayloads
{
    public static Dictionary<string, object?> ZoneChange(string clusterIndex, string clusterName) =>
        new()
        {
            ["clusterIndex"] = (clusterIndex ?? string.Empty).Trim(),
            ["clusterName"] = (clusterName ?? string.Empty).Trim(),
            ["clusterMode"] = null,
            ["mapType"] = null,
            ["sourceClusterIndex"] = null
        };

    public static Dictionary<string, object?> PlayerPresence(
        string clusterName,
        IReadOnlyList<Dictionary<string, object?>> players) =>
        new()
        {
            ["cluster"] = (clusterName ?? string.Empty).Trim(),
            ["players"] = players,
            ["observedCount"] = players.Count,
            ["snapshotIntervalMs"] = 15000,
            ["source"] = "NewCharacter+Leave"
        };
}
