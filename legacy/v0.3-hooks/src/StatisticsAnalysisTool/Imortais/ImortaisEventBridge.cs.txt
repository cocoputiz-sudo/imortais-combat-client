using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace StatisticsAnalysisTool.Imortais;

public static class ImortaisEventBridge
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly Channel<ImortaisTelemetryEvent> Queue = Channel.CreateBounded<ImortaisTelemetryEvent>(
        new BoundedChannelOptions(5000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    private static readonly ConcurrentDictionary<string, CombatAccumulator> Combat = new(StringComparer.OrdinalIgnoreCase);
    private static readonly CancellationTokenSource Cts = new();
    private static readonly object StartLock = new();
    private static Task? _worker;
    private static ImortaisTelemetryConfig _config = ImortaisTelemetryConfig.Load();

    public static void ReloadConfig() => _config = ImortaisTelemetryConfig.Load();

    public static void PartySnapshot(IEnumerable<string> members)
    {
        var normalized = members
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Enqueue(new ImortaisTelemetryEvent
        {
            Type = "party_snapshot",
            PlayerName = _config.PlayerName,
            Payload = new Dictionary<string, object?> { ["members"] = normalized }
        });
    }

    public static void Loot(string lootedBy, string lootedFrom, string itemUniqueName, int quantity, double estimatedValue, string? clusterName)
    {
        Enqueue(new ImortaisTelemetryEvent
        {
            Type = "loot",
            PlayerName = lootedBy,
            Payload = new Dictionary<string, object?>
            {
                ["lootedBy"] = lootedBy,
                ["lootedFrom"] = lootedFrom,
                ["item"] = itemUniqueName,
                ["quantity"] = quantity,
                ["estimatedValue"] = estimatedValue,
                ["cluster"] = clusterName
            }
        });
    }

    public static void Damage(string player, int amount)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(player)) return;
        Combat.AddOrUpdate(player,
            _ => new CombatAccumulator { Damage = amount },
            (_, current) => { Interlocked.Add(ref current.Damage, amount); return current; });
        EnsureStarted();
    }

    public static void Healing(string player, int amount)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(player)) return;
        Combat.AddOrUpdate(player,
            _ => new CombatAccumulator { Healing = amount },
            (_, current) => { Interlocked.Add(ref current.Healing, amount); return current; });
        EnsureStarted();
    }

    public static void CombatResult(string result, string diedPlayer, string killerPlayer, bool isLethal)
    {
        var type = result switch
        {
            "Death" => "death",
            "Kill" => "kill",
            "Knockout" => "knockout",
            "KnockedOut" => "knocked_out",
            _ => "combat_result"
        };

        Enqueue(new ImortaisTelemetryEvent
        {
            Type = type,
            PlayerName = type == "death" ? diedPlayer : killerPlayer,
            Payload = new Dictionary<string, object?>
            {
                ["result"] = result,
                ["victim"] = diedPlayer,
                ["killer"] = killerPlayer,
                ["isLethal"] = isLethal
            }
        });
    }

    private static void Enqueue(ImortaisTelemetryEvent evt)
    {
        EnsureStarted();
        if (!_config.Enabled) return;
        Queue.Writer.TryWrite(evt);
    }

    private static void EnsureStarted()
    {
        if (_worker != null) return;
        lock (StartLock)
        {
            _worker ??= Task.Run(() => WorkerAsync(Cts.Token));
        }
    }

    private static async Task WorkerAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Math.Max(250, _config.BatchIntervalMs), token);
                FlushCombatAccumulators();
                if (!_config.Enabled || string.IsNullOrWhiteSpace(_config.AgentKey) || string.IsNullOrWhiteSpace(_config.ServerUrl))
                    continue;

                var batch = new List<ImortaisTelemetryEvent>();
                while (batch.Count < Math.Max(1, _config.MaxBatchSize) && Queue.Reader.TryRead(out var evt))
                    batch.Add(evt);

                if (batch.Count == 0) continue;

                var url = _config.ServerUrl.TrimEnd('/') + "/api/telemetry/ingest";
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.AgentKey);
                req.Content = JsonContent.Create(new
                {
                    device = new
                    {
                        deviceId = _config.DeviceId,
                        playerName = _config.PlayerName,
                        version = "statistics-bridge-v0.3"
                    },
                    ctaEventId = _config.CtaEventId,
                    events = batch
                });

                using var response = await Http.SendAsync(req, token);
                if (!response.IsSuccessStatusCode)
                {
                    foreach (var evt in batch) Queue.Writer.TryWrite(evt);
                    await Task.Delay(2000, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                await Task.Delay(1500, token);
            }
        }
    }

    private static void FlushCombatAccumulators()
    {
        if (!_config.Enabled) return;
        foreach (var pair in Combat.ToArray())
        {
            if (!Combat.TryRemove(pair.Key, out var acc)) continue;
            var damage = Interlocked.Read(ref acc.Damage);
            var healing = Interlocked.Read(ref acc.Healing);
            if (damage <= 0 && healing <= 0) continue;

            Queue.Writer.TryWrite(new ImortaisTelemetryEvent
            {
                Type = "combat_delta",
                PlayerName = pair.Key,
                Payload = new Dictionary<string, object?>
                {
                    ["player"] = pair.Key,
                    ["damage"] = damage,
                    ["healing"] = healing,
                    ["windowMs"] = Math.Max(250, _config.BatchIntervalMs)
                }
            });
        }
    }

    private sealed class CombatAccumulator
    {
        public long Damage;
        public long Healing;
    }
}
