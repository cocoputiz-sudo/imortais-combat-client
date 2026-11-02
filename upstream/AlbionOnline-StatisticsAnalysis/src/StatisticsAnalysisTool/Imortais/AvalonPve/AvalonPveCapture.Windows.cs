using StatisticsAnalysisTool.Cluster;
using StatisticsAnalysisTool.Dungeon;
using System;

namespace StatisticsAnalysisTool.Imortais.AvalonPve;

// Windows adapter: converts the WPF ClusterInfo/DungeonMode into the portable text form.
public static partial class AvalonPveCapture
{
    public static void OnClusterTransition(ClusterInfo? cluster, Guid? joinMapGuid)
        => OnClusterTransition(cluster?.MapType.ToString(), cluster?.Index, joinMapGuid);

    public static void ObserveMobDamage(
        ClusterInfo? cluster, DungeonMode observedDungeonMode,
        string? player, long targetObjectId, int mobIndex,
        int amount, int causingSpellIndex, bool confirmedMobSpawn)
        => ObserveMobDamage(cluster?.MapType.ToString(), cluster?.Index,
            cluster?.ClusterInfoFullyAvailable == true, observedDungeonMode.ToString(),
            player, targetObjectId, mobIndex, amount, causingSpellIndex, confirmedMobSpawn);
}
