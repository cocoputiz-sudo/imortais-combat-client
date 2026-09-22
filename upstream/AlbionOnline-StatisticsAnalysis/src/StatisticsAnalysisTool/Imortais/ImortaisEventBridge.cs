using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
    private static readonly object StatusLock = new();
    private static bool _warRoomConnected;
    private static DateTime? _lastSuccessfulContactUtc;
    private static string _lastError = string.Empty;
    private static string? _activeCtaTime;

    public sealed record BridgeStatus(
        bool Enabled,
        bool Configured,
        bool WarRoomConnected,
        string PlayerName,
        string? CtaEventId,
        string? CtaTime,
        DateTime? LastSuccessfulContactUtc,
        string LastError);

    public static void Start() => EnsureStarted();

    public static void ReloadConfig()
    {
        _config = ImortaisTelemetryConfig.Load();
        EnsureStarted();
    }

    public static BridgeStatus GetStatus()
    {
        lock (StatusLock)
        {
            var configured = _config.Enabled
                             && !string.IsNullOrWhiteSpace(_config.AgentKey)
                             && !string.IsNullOrWhiteSpace(_config.ServerUrl);

            return new BridgeStatus(
                _config.Enabled,
                configured,
                _warRoomConnected,
                _config.PlayerName,
                _config.CtaEventId,
                _activeCtaTime,
                _lastSuccessfulContactUtc,
                _lastError);
        }
    }

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

    public static void Loot(string lootedBy, string? lootedByGuild, string lootedFrom, string itemUniqueName, int quantity, double estimatedValue, string? clusterName)
    {
        Enqueue(new ImortaisTelemetryEvent
        {
            Type = "loot",
            PlayerName = lootedBy,
            Payload = new Dictionary<string, object?>
            {
                ["lootedBy"] = lootedBy,
                ["lootedByGuild"] = lootedByGuild,
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

    private static void SetConnectionState(bool connected, string error = "")
    {
        lock (StatusLock)
        {
            _warRoomConnected = connected;
            if (connected)
            {
                _lastSuccessfulContactUtc = DateTime.UtcNow;
                _lastError = string.Empty;
            }
            else if (!string.IsNullOrWhiteSpace(error))
            {
                _lastError = error;
            }
        }
    }

    private static void SetCtaContext(string? eventId, string? time)
    {
        var normalizedId = string.IsNullOrWhiteSpace(eventId) ? null : eventId.Trim();
        var normalizedTime = string.IsNullOrWhiteSpace(time) ? null : time.Trim();
        var changed = !string.Equals(_config.CtaEventId, normalizedId, StringComparison.Ordinal);

        _config.CtaEventId = normalizedId;
        lock (StatusLock)
        {
            _activeCtaTime = normalizedTime;
        }

        if (changed)
        {
            try { ImortaisTelemetryConfig.Save(_config); } catch { /* status must never stop capture */ }
        }
    }

    private static async Task RefreshContextAsync(CancellationToken token)
    {
        if (!_config.Enabled
            || string.IsNullOrWhiteSpace(_config.AgentKey)
            || string.IsNullOrWhiteSpace(_config.ServerUrl))
        {
            SetConnectionState(false, "Telemetria não configurada");
            return;
        }

        try
        {
            var url = _config.ServerUrl.TrimEnd('/')
                      + "/api/telemetry/context?deviceId="
                      + Uri.EscapeDataString(_config.DeviceId ?? string.Empty)
                      + "&playerName="
                      + Uri.EscapeDataString(_config.PlayerName ?? string.Empty);

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.AgentKey);
            using var response = await Http.SendAsync(req, token);

            if (!response.IsSuccessStatusCode)
            {
                SetConnectionState(false, $"War Room HTTP {(int)response.StatusCode}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync(token);
            using var doc = JsonDocument.Parse(json);
            string? ctaId = null;
            string? ctaTime = null;

            if (doc.RootElement.TryGetProperty("cta", out var cta)
                && cta.ValueKind == JsonValueKind.Object)
            {
                if (cta.TryGetProperty("id", out var idProp)) ctaId = idProp.GetString();
                if (cta.TryGetProperty("time", out var timeProp)) ctaTime = timeProp.GetString();
            }

            SetCtaContext(ctaId, ctaTime);
            SetConnectionState(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            SetConnectionState(false, e.Message);
        }
    }

    private static async Task WorkerAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Math.Max(250, _config.BatchIntervalMs), token);
                await RefreshContextAsync(token);
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
                        version = "0.4.6"
                    },
                    ctaEventId = _config.CtaEventId,
                    events = batch
                });

                using var response = await Http.SendAsync(req, token);
                if (!response.IsSuccessStatusCode)
                {
                    SetConnectionState(false, $"Ingest HTTP {(int)response.StatusCode}");
                    foreach (var evt in batch) Queue.Writer.TryWrite(evt);
                    await Task.Delay(2000, token);
                    continue;
                }

                try
                {
                    var responseJson = await response.Content.ReadAsStringAsync(token);
                    using var responseDoc = JsonDocument.Parse(responseJson);
                    if (responseDoc.RootElement.TryGetProperty("ctaEventId", out var ctaProp))
                    {
                        var resolvedId = ctaProp.ValueKind == JsonValueKind.String
                            ? ctaProp.GetString()
                            : ctaProp.ToString();
                        if (!string.IsNullOrWhiteSpace(resolvedId))
                            SetCtaContext(resolvedId, _activeCtaTime);
                    }
                }
                catch
                {
                    // O envio foi aceito; falha ao ler o corpo não invalida a conexão.
                }

                SetConnectionState(true);
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
