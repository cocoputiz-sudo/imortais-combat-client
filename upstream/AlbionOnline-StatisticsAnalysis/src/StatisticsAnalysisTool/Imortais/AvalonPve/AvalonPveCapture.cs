using System;
using System.Collections.Generic;
using System.Threading;

namespace StatisticsAnalysisTool.Imortais.AvalonPve;

// Strictly additive observation path; default OFF. Never writes DeathReview,
// manipulates Albion, or assigns a score/winner on the client.
// Portable part (Windows and Linux): map data arrives as plain text. The Windows-only
// overloads that take ClusterInfo/DungeonMode live in AvalonPveCapture.Windows.cs.
public static partial class AvalonPveCapture
{
    private static readonly AvalonPveObservationSession Session = new();
    private static readonly Timer FlushTimer = new(_ => Flush(), null, 5000, 5000);
    private static readonly object JoinSync = new();
    private static Guid? _joinGuid;
    private static string? _joinCluster;

    public static void OnClusterTransition(string? mapType, string? clusterIndex, Guid? joinMapGuid)
    {
        // Only the GUID observed in THIS join packet can establish the instance.
        // ClusterInfo.Guid may be stale when the incoming join omits its GUID.
        lock (JoinSync)
        {
            _joinGuid = joinMapGuid is { } guid && guid != Guid.Empty ? guid : null;
            _joinCluster = clusterIndex;
        }
        if (!ImortaisEventBridge.IsDgavaPveOptInEnabled)
        {
            Session.Clear();
            return;
        }
        // Reset immediately on map change; no name-based Avalon inference.
        Publish(Session.ChangeContext(mapType, "Unknown", null, clusterIndex));
    }

    public static void ObserveMobDamage(
        string? mapType, string? clusterIndex, bool clusterFullyAvailable, string? observedDungeonMode,
        string? player, long targetObjectId, int mobIndex,
        int amount, int causingSpellIndex, bool confirmedMobSpawn)
    {
        if (!ImortaisEventBridge.IsDgavaPveOptInEnabled)
        {
            Session.Clear();
            return;
        }
        if (!clusterFullyAvailable || clusterIndex is null)
        {
            Session.Clear();
            return;
        }
        Guid? freshGuid;
        lock (JoinSync)
            freshGuid = string.Equals(_joinCluster, clusterIndex, StringComparison.Ordinal)
                ? _joinGuid : null;
        Publish(Session.ChangeContext(
            mapType, observedDungeonMode,
            freshGuid, clusterIndex));
        Publish(Session.ObserveDamage(player, targetObjectId, mobIndex,
            amount, causingSpellIndex, confirmedMobSpawn, DateTime.UtcNow));
    }

    public static void OnCharacterTransition()
    {
        lock (JoinSync) { _joinGuid = null; _joinCluster = null; }
        Session.Clear();
    }

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
        {
            try
            {
                ImortaisEventBridge.DgavaPveWindow(window);
            }
            catch (Exception error)
            {
                // An experimental capture failure must never break the base combat
                // meter, Photon packet handling or UI, or crash the timer thread.
                System.Diagnostics.Debug.WriteLine(
                    "Avalon PvE telemetry window discarded: " + error.GetType().Name);
            }
        }
    }
}
