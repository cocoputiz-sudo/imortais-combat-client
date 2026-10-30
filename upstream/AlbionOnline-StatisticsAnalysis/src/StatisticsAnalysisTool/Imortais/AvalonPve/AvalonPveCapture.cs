using StatisticsAnalysisTool.Cluster;
using StatisticsAnalysisTool.Dungeon;
using System;
using System.Collections.Generic;
using System.Threading;

namespace StatisticsAnalysisTool.Imortais.AvalonPve;

// Strictly additive observation path; default OFF. Never writes DeathReview,
// manipulates Albion, or assigns a score/winner on the client.
public static class AvalonPveCapture
{
    private static readonly AvalonPveObservationSession Session = new();
    private static readonly Timer FlushTimer = new(_ => Flush(), null, 5000, 5000);

    public static void OnClusterTransition(ClusterInfo? cluster)
    {
        if (!ImortaisEventBridge.IsDgavaPveOptInEnabled)
        {
            Session.Clear();
            return;
        }
        // Reset immediately on map change; no name-based Avalon inference.
        Publish(Session.ChangeContext(cluster?.MapType.ToString(), "Unknown", null, cluster?.Index));
    }

    public static void ObserveMobDamage(
        ClusterInfo? cluster, DungeonMode observedDungeonMode,
        string? player, long targetObjectId, int mobIndex,
        int amount, int causingSpellIndex, bool confirmedMobSpawn)
    {
        if (!ImortaisEventBridge.IsDgavaPveOptInEnabled)
        {
            Session.Clear();
            return;
        }
        if (cluster?.ClusterInfoFullyAvailable != true)
        {
            Session.Clear();
            return;
        }
        Publish(Session.ChangeContext(
            cluster.MapType.ToString(), observedDungeonMode.ToString(),
            cluster.Guid, cluster.Index));
        Publish(Session.ObserveDamage(player, targetObjectId, mobIndex,
            amount, causingSpellIndex, confirmedMobSpawn, DateTime.UtcNow));
    }

    public static void OnCharacterTransition() => Session.Clear();

    private static void Flush()
    {
        if (!ImortaisEventBridge.IsDgavaPveOptInEnabled)
        {
            Session.Clear();
            return;
        }
        Publish(Session.Flush(DateTime.UtcNow));
    }

    private static void Publish(IReadOnlyList<AvalonPveWindow> windows)
    {
        foreach (var window in windows)
            ImortaisEventBridge.DgavaPveWindow(window);
    }
}
