using System;
using System.Collections.Generic;
using System.Linq;

namespace StatisticsAnalysisTool.Imortais.AvalonPve;

// No app, network or game dependencies: intentionally testable without Windows.
// An observed hit is NOT a certified PvE damage amount or proof of full combat coverage.
public sealed record AvalonPveHit(
    DateTime ObservedAtUtc, long MobObjectId, int MobIndex, int Amount, int SpellIndex);

public sealed record AvalonPveWindow(
    string WindowId, string DungeonInstanceId, string ClusterIndex, string Actor,
    DateTime StartedAtUtc, DateTime EndedAtUtc, long ObservedMs,
    long MobDamage, IReadOnlyList<AvalonPveHit> Hits,
    string TargetKind, string Coverage, string Evidence);

public enum AvalonPveContextKind { Unknown, Other, AvalonInstance }

public sealed class AvalonPveObservationSession
{
    public const int MaximumSamplesPerWindow = 96;
    public static readonly TimeSpan MaximumWindowAge = TimeSpan.FromSeconds(5);
    private readonly object _sync = new();
    private readonly Dictionary<string, Buffer> _buffers = new(StringComparer.OrdinalIgnoreCase);
    private string _dungeon = string.Empty, _cluster = string.Empty;
    private AvalonPveContextKind _context;

    private sealed class Buffer
    {
        public DateTime First;
        public DateTime Last;
        public readonly List<AvalonPveHit> Hits = new();
        public long Damage;
    }

    public AvalonPveContextKind Context { get { lock (_sync) return _context; } }

    public IReadOnlyList<AvalonPveWindow> ChangeContext(
        string? mapType, string? dungeonMode, Guid? mapGuid, string? clusterIndex)
    {
        var next = mapType == "RandomDungeon" && dungeonMode == "Avalon"
                   && mapGuid is { } guid && guid != Guid.Empty
                   && !string.IsNullOrWhiteSpace(clusterIndex)
            ? AvalonPveContextKind.AvalonInstance
            : string.IsNullOrWhiteSpace(mapType) || mapType == "Unknown"
                || (mapType == "RandomDungeon" && dungeonMode == "Unknown")
                ? AvalonPveContextKind.Unknown : AvalonPveContextKind.Other;
        var nextGuid = next == AvalonPveContextKind.AvalonInstance
            ? mapGuid!.Value.ToString("D") : string.Empty;
        var nextCluster = next == AvalonPveContextKind.AvalonInstance
            ? clusterIndex!.Trim() : string.Empty;

        lock (_sync)
        {
            if (_context == next && _dungeon == nextGuid && _cluster == nextCluster)
                return Array.Empty<AvalonPveWindow>();
            var completed = DrainAll();
            _context = next;
            _dungeon = nextGuid;
            _cluster = nextCluster;
            return completed;
        }
    }

    public IReadOnlyList<AvalonPveWindow> ObserveDamage(
        string? actor, long mobObjectId, int mobIndex, int amount,
        int spellIndex, bool mobConfirmedBySpawn, DateTime seenUtc)
    {
        if (!mobConfirmedBySpawn || mobObjectId <= 0 || mobIndex <= 0 || amount <= 0
            || string.IsNullOrWhiteSpace(actor) || seenUtc.Kind != DateTimeKind.Utc)
            return Array.Empty<AvalonPveWindow>();
        lock (_sync)
        {
            if (_context != AvalonPveContextKind.AvalonInstance)
                return Array.Empty<AvalonPveWindow>();
            var name = actor.Trim();
            var output = new List<AvalonPveWindow>(1);
            if (_buffers.TryGetValue(name, out var previous) &&
                (seenUtc < previous.Last || seenUtc - previous.First >= MaximumWindowAge
                 || previous.Hits.Count >= MaximumSamplesPerWindow))
            {
                output.Add(ToWindow(name, previous));
                _buffers.Remove(name);
            }
            if (!_buffers.TryGetValue(name, out var current))
            {
                current = new Buffer { First = seenUtc, Last = seenUtc };
                _buffers[name] = current;
            }
            // Both HealthUpdate and HealthUpdates may contain overlapping observations.
            // Never assert that an individual Photon observation has been globally deduplicated.
            current.Last = seenUtc;
            current.Damage += amount;
            current.Hits.Add(new AvalonPveHit(seenUtc, mobObjectId, mobIndex, amount, spellIndex));
            return output;
        }
    }

    public IReadOnlyList<AvalonPveWindow> Flush(DateTime nowUtc, bool force = false)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
            return Array.Empty<AvalonPveWindow>();
        lock (_sync)
        {
            var output = new List<AvalonPveWindow>();
            foreach (var pair in _buffers.ToArray())
            {
                if (!force && nowUtc - pair.Value.First < MaximumWindowAge)
                    continue;
                output.Add(ToWindow(pair.Key, pair.Value));
                _buffers.Remove(pair.Key);
            }
            return output;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _buffers.Clear();
            _context = AvalonPveContextKind.Unknown;
            _dungeon = _cluster = string.Empty;
        }
    }

    private IReadOnlyList<AvalonPveWindow> DrainAll()
    {
        var result = _buffers.Select(pair => ToWindow(pair.Key, pair.Value)).ToArray();
        _buffers.Clear();
        return result;
    }

    private AvalonPveWindow ToWindow(string actor, Buffer buffer) =>
        new(Guid.NewGuid().ToString("N"), _dungeon, _cluster, actor,
            buffer.First, buffer.Last,
            Math.Max(0, (long)(buffer.Last - buffer.First).TotalMilliseconds),
            buffer.Damage, buffer.Hits.ToArray(),
            "confirmed_mob_spawn", "partial", "passive_photon_observed_not_verified");
}
