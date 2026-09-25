using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace StatisticsAnalysisTool.Imortais;

public static class ImortaisEventBridge
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly Channel<QueuedTelemetryEvent> Queue = Channel.CreateUnbounded<QueuedTelemetryEvent>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    private static readonly ConcurrentDictionary<string, CombatAccumulator> Combat = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> LastGuildPresenceProbePayloads = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> LastPartySnapshotPayloads = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> GuildPresencePlayersSeen = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<string> RecentActivity = new();
    private static readonly CancellationTokenSource Cts = new();
    private static readonly object StartLock = new();
    private static readonly object StatusLock = new();
    private static readonly object PartySnapshotLock = new();
    private static readonly SemaphoreSlim OutboxLock = new(1, 1);
    private static readonly JsonSerializerOptions OutboxJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private const string ClientVersion = "0.5.4";
    private const string PartySnapshotFingerprintKey = "party";

    private static Task? _worker;
    private static ImortaisTelemetryConfig _config = ImortaisTelemetryConfig.Load();
    private static bool _warRoomConnected;
    private static DateTime? _lastSuccessfulContactUtc;
    private static string _lastError = string.Empty;
    private static string _persistenceWarning = string.Empty;
    private static string? _activeCtaTime;
    private static string? _outboxStatePath;
    private static int _outboxEventCount;
    private static long _outboxByteCount;
    private static bool _outboxStateInitialized;
    private static bool _outboxLimitWarningActive;
    private static long _queueSequence;
    private static DateTime? _lastPartySnapshotAtUtc;
    private static int _lastPartyMemberCount;
    private static DateTime _lastHeartbeatEnqueuedUtc = DateTime.MinValue;
    private static int _gameDetectedState = -1;
    private static long _partySnapshotDeduplicatedCount;
    private static long _guildPresenceProbeCount;
    private static DateTime? _lastGuildPresenceProbeAtUtc;

    public sealed record BridgeStatus(
        bool Enabled,
        bool Configured,
        bool WarRoomConnected,
        string PlayerName,
        string? CtaEventId,
        string? CtaTime,
        DateTime? LastSuccessfulContactUtc,
        string LastError,
        int PendingEvents,
        long PendingBytes,
        DateTime? LastHeartbeatEnqueuedUtc,
        DateTime? LastPartySnapshotAtUtc,
        int LastPartyMemberCount,
        long PartySnapshotDeduplicatedCount,
        bool? GameDetected,
        long GuildPresenceProbeCount,
        int GuildPresenceDistinctPlayers,
        DateTime? LastGuildPresenceProbeAtUtc,
        IReadOnlyList<string> RecentActivity);

    public static void Start() => EnsureStarted();

    public static void ReloadConfig()
    {
        _config = ImortaisTelemetryConfig.Load();
        ResetOutboxState();
        EnsureStarted();
    }

    public static async Task<(bool Success, string Message)> PairAsync(string code, string? playerName = null)
    {
        code = (code ?? string.Empty).Trim();
        if (code.Length != 6 || !code.All(char.IsDigit))
            return (false, "O código deve ter 6 números.");

        var config = ImortaisTelemetryConfig.Load();
        if (string.IsNullOrWhiteSpace(config.ServerUrl))
            return (false, "Servidor do War Room não configurado.");

        try
        {
            var url = config.ServerUrl.TrimEnd('/') + "/api/telemetry/pair";
            using var response = await Http.PostAsJsonAsync(url, new
            {
                code,
                deviceId = config.DeviceId,
                playerName = string.IsNullOrWhiteSpace(playerName) ? config.PlayerName : playerName.Trim()
            });

            if (!response.IsSuccessStatusCode)
            {
                return response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? (false, "Código inválido, expirado ou já utilizado.")
                    : (false, $"War Room HTTP {(int)response.StatusCode}.");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("token", out var tokenProp)
                || string.IsNullOrWhiteSpace(tokenProp.GetString()))
                return (false, "O War Room não retornou a chave da telemetria.");

            config.AgentKey = tokenProp.GetString()!;
            config.Enabled = true;
            if (doc.RootElement.TryGetProperty("playerName", out var playerProp)
                && playerProp.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(playerProp.GetString()))
                config.PlayerName = playerProp.GetString()!.Trim();

            ImortaisTelemetryConfig.Save(config);
            ReloadConfig();
            return (true, "Telemetria ativada com sucesso.");
        }
        catch (Exception e)
        {
            return (false, "Falha ao ativar: " + e.Message);
        }
    }

    public static BridgeStatus GetStatus()
    {
        lock (StatusLock)
        {
            var configured = _config.Enabled
                             && !string.IsNullOrWhiteSpace(_config.AgentKey)
                             && !string.IsNullOrWhiteSpace(_config.ServerUrl);

            var gameDetectedState = Volatile.Read(ref _gameDetectedState);
            return new BridgeStatus(
                _config.Enabled,
                configured,
                _warRoomConnected,
                _config.PlayerName,
                _config.CtaEventId,
                _activeCtaTime,
                _lastSuccessfulContactUtc,
                string.IsNullOrWhiteSpace(_persistenceWarning) ? _lastError : _persistenceWarning,
                _outboxEventCount,
                _outboxByteCount,
                _lastHeartbeatEnqueuedUtc == DateTime.MinValue ? null : _lastHeartbeatEnqueuedUtc,
                _lastPartySnapshotAtUtc,
                _lastPartyMemberCount,
                Interlocked.Read(ref _partySnapshotDeduplicatedCount),
                gameDetectedState < 0 ? null : gameDetectedState == 1,
                Interlocked.Read(ref _guildPresenceProbeCount),
                GuildPresencePlayersSeen.Count,
                _lastGuildPresenceProbeAtUtc,
                RecentActivity.ToArray());
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

        var fingerprint = string.Join("|", normalized);

        lock (PartySnapshotLock)
        {
            if (LastPartySnapshotPayloads.TryGetValue(PartySnapshotFingerprintKey, out var previous)
                && string.Equals(previous, fingerprint, StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _partySnapshotDeduplicatedCount);
                return;
            }

            if (!Enqueue(new ImortaisTelemetryEvent
                {
                    Type = "party_snapshot",
                    PlayerName = _config.PlayerName,
                    Payload = new Dictionary<string, object?> { ["members"] = normalized }
                }))
            {
                return;
            }

            LastPartySnapshotPayloads[PartySnapshotFingerprintKey] = fingerprint;
            lock (StatusLock)
            {
                _lastPartySnapshotAtUtc = DateTime.UtcNow;
                _lastPartyMemberCount = normalized.Length;
            }
            AddActivity($"PARTY snapshot enviado · {normalized.Length} membro{(normalized.Length == 1 ? string.Empty : "s")}");
        }
    }

    public static void SetGameDetected(bool detected)
    {
        Volatile.Write(ref _gameDetectedState, detected ? 1 : 0);
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

    public static void GuildPresenceProbe(string eventName, int eventCode, IReadOnlyDictionary<byte, object> parameters)
    {
        if (string.IsNullOrWhiteSpace(eventName) || parameters == null)
        {
            return;
        }

        var normalizedParameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in parameters.OrderBy(x => x.Key).Take(96))
        {
            if (pair.Key == 252) continue;
            normalizedParameters[pair.Key.ToString()] = SanitizePhotonValue(pair.Value, 0);
        }

        var payload = new Dictionary<string, object?>
        {
            ["eventName"] = eventName,
            ["eventCode"] = eventCode,
            ["parameters"] = normalizedParameters
        };

        var fingerprint = JsonSerializer.Serialize(payload);
        if (LastGuildPresenceProbePayloads.TryGetValue(eventName, out var previous)
            && string.Equals(previous, fingerprint, StringComparison.Ordinal))
        {
            return;
        }

        LastGuildPresenceProbePayloads[eventName] = fingerprint;

        if (Enqueue(new ImortaisTelemetryEvent
            {
                Type = "guild_presence_probe",
                PlayerName = _config.PlayerName,
                Payload = payload
            }))
        {
            Interlocked.Increment(ref _guildPresenceProbeCount);
            lock (StatusLock)
            {
                _lastGuildPresenceProbeAtUtc = DateTime.UtcNow;
            }

            if (string.Equals(eventName, "GuildPlayerUpdated", StringComparison.Ordinal)
                && normalizedParameters.TryGetValue("1", out var playerValue))
            {
                var observedPlayer = Convert.ToString(playerValue)?.Trim();
                if (!string.IsNullOrWhiteSpace(observedPlayer))
                {
                    GuildPresencePlayersSeen.TryAdd(observedPlayer, 0);
                    var online = normalizedParameters.TryGetValue("2", out var onlineValue)
                                 && onlineValue is bool flag
                                 && flag;
                    AddActivity($"GUILD {observedPlayer} · {(online ? "ONLINE" : "OFFLINE")}");
                }
            }
        }
    }

    private static object? SanitizePhotonValue(object? value, int depth)
    {
        if (value == null) return null;
        if (depth >= 5) return "<max-depth>";

        switch (value)
        {
            case string text:
                return text.Length <= 512 ? text : text[..512];
            case bool:
            case byte:
            case sbyte:
            case short:
            case ushort:
            case int:
            case uint:
            case long:
            case ulong:
            case float:
            case double:
            case decimal:
                return value;
            case Guid guid:
                return guid.ToString();
            case DateTime dateTime:
                return dateTime.ToUniversalTime().ToString("O");
            case byte[] bytes:
                return new Dictionary<string, object?>
                {
                    ["kind"] = "bytes",
                    ["length"] = bytes.Length,
                    ["previewBase64"] = Convert.ToBase64String(bytes.Take(64).ToArray())
                };
            case IDictionary dictionary:
            {
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                var count = 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (count++ >= 256) break;
                    result[entry.Key?.ToString() ?? "null"] = SanitizePhotonValue(entry.Value, depth + 1);
                }
                return result;
            }
            case Array array:
            {
                var result = new List<object?>();
                var count = Math.Min(array.Length, 400);
                for (var i = 0; i < count; i++)
                {
                    result.Add(SanitizePhotonValue(array.GetValue(i), depth + 1));
                }

                if (array.Length > count)
                {
                    result.Add($"<truncated:{array.Length - count}>");
                }

                return result;
            }
            case IEnumerable enumerable:
            {
                var result = new List<object?>();
                var count = 0;
                foreach (var item in enumerable)
                {
                    if (count++ >= 400)
                    {
                        result.Add("<truncated>");
                        break;
                    }
                    result.Add(SanitizePhotonValue(item, depth + 1));
                }
                return result;
            }
            default:
            {
                var text = value.ToString() ?? value.GetType().FullName ?? "unknown";
                return text.Length <= 512 ? text : text[..512];
            }
        }
    }

    public static void Damage(string player, int amount, string? clusterName = null)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(player)) return;
        var cluster = string.IsNullOrWhiteSpace(clusterName) ? string.Empty : clusterName.Trim();
        var key = $"{cluster}\u001f{player.Trim()}";
        Combat.AddOrUpdate(key,
            _ => new CombatAccumulator { Player = player.Trim(), Cluster = cluster, Damage = amount },
            (_, current) => { Interlocked.Add(ref current.Damage, amount); return current; });
        EnsureStarted();
    }

    public static void Healing(string player, int amount, string? clusterName = null)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(player)) return;
        var cluster = string.IsNullOrWhiteSpace(clusterName) ? string.Empty : clusterName.Trim();
        var key = $"{cluster}\u001f{player.Trim()}";
        Combat.AddOrUpdate(key,
            _ => new CombatAccumulator { Player = player.Trim(), Cluster = cluster, Healing = amount },
            (_, current) => { Interlocked.Add(ref current.Healing, amount); return current; });
        EnsureStarted();
    }

    public static void CombatResult(string result, string diedPlayer, string killerPlayer, bool isLethal, string? clusterName = null)
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
                ["isLethal"] = isLethal,
                ["cluster"] = string.IsNullOrWhiteSpace(clusterName) ? null : clusterName.Trim()
            }
        });
    }

    private static void AddActivity(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        RecentActivity.Enqueue($"{DateTime.Now:HH:mm:ss} · {message.Trim()}");
        while (RecentActivity.Count > 10 && RecentActivity.TryDequeue(out _))
        {
        }
    }

    private static bool Enqueue(ImortaisTelemetryEvent evt)
    {
        EnsureStarted();
        if (!_config.Enabled) return false;
        QueueEvent(evt);
        return true;
    }

    private static void QueueEvent(ImortaisTelemetryEvent evt)
    {
        var sequence = Interlocked.Increment(ref _queueSequence);
        Queue.Writer.TryWrite(new QueuedTelemetryEvent(sequence, evt));
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

    private static void SetPersistenceWarning(string warning)
    {
        lock (StatusLock)
        {
            _persistenceWarning = warning ?? string.Empty;
        }
    }

    private static void ClearTransientPersistenceWarning()
    {
        lock (StatusLock)
        {
            if (!_outboxLimitWarningActive)
            {
                _persistenceWarning = string.Empty;
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
            AddActivity(normalizedId == null
                ? "CTA desvinculado"
                : $"CTA vinculado · {(string.IsNullOrWhiteSpace(normalizedTime) ? "#" + normalizedId : normalizedTime)}");
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

    private static void TryEnqueueHeartbeat()
    {
        var configured = _config.Enabled
                         && !string.IsNullOrWhiteSpace(_config.AgentKey)
                         && !string.IsNullOrWhiteSpace(_config.ServerUrl);
        if (!configured) return;

        var now = DateTime.UtcNow;
        if (now - _lastHeartbeatEnqueuedUtc < HeartbeatInterval) return;

        var status = GetStatus();
        DateTime? lastPartySnapshotAt;
        int lastPartyMemberCount;
        lock (StatusLock)
        {
            lastPartySnapshotAt = _lastPartySnapshotAtUtc;
            lastPartyMemberCount = _lastPartyMemberCount;
        }

        var payload = new Dictionary<string, object?>
        {
            ["deviceId"] = _config.DeviceId,
            ["playerName"] = _config.PlayerName,
            ["version"] = ClientVersion,
            ["currentCtaId"] = _config.CtaEventId,
            ["connected"] = status.WarRoomConnected,
            ["observerStatus"] = status.WarRoomConnected ? "connected" : "reconnecting",
            ["outbox"] = new Dictionary<string, object?>
            {
                ["events"] = status.PendingEvents,
                ["bytes"] = status.PendingBytes
            },
            ["lastPartySnapshotAt"] = lastPartySnapshotAt,
            ["lastPartyMemberCount"] = lastPartyMemberCount
        };

        var gameDetectedState = Volatile.Read(ref _gameDetectedState);
        if (gameDetectedState >= 0)
        {
            payload["gameDetected"] = gameDetectedState == 1;
        }

        if (Enqueue(new ImortaisTelemetryEvent
            {
                Type = "client_heartbeat",
                PlayerName = _config.PlayerName,
                Payload = payload
            }))
        {
            _lastHeartbeatEnqueuedUtc = now;
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
                TryEnqueueHeartbeat();

                if (!await PersistQueuedEventsAsync(token))
                {
                    await Task.Delay(1500, token);
                    continue;
                }

                if (!_config.Enabled
                    || string.IsNullOrWhiteSpace(_config.AgentKey)
                    || string.IsNullOrWhiteSpace(_config.ServerUrl))
                {
                    continue;
                }

                var batch = await ReadOutboxHeadAsync(Math.Max(1, _config.MaxBatchSize), token);
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
                        version = ClientVersion
                    },
                    ctaEventId = _config.CtaEventId,
                    events = batch
                });

                using var response = await Http.SendAsync(req, token);
                if (!response.IsSuccessStatusCode)
                {
                    SetConnectionState(false, $"Ingest HTTP {(int)response.StatusCode}");
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

                await AcknowledgeOutboxAsync(batch.Select(x => x.EventId), token);
                SetConnectionState(true);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                SetPersistenceWarning($"Outbox/worker: {e.Message}");
                await Task.Delay(1500, token);
            }
        }
    }

    private static async Task<bool> PersistQueuedEventsAsync(CancellationToken token)
    {
        var pending = new List<QueuedTelemetryEvent>();
        while (Queue.Reader.TryRead(out var queued))
        {
            pending.Add(queued);
        }

        if (pending.Count == 0)
        {
            return true;
        }

        pending.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));

        try
        {
            await AppendOutboxAsync(pending.Select(x => x.Event).ToArray(), token);
            ClearTransientPersistenceWarning();
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            // A captura nunca toca em disco. Se a persistência falhar, devolvemos
            // os mesmos eventos (com a mesma sequência/EventId) ao Channel e tentamos
            // novamente no próximo ciclo. Novos eventos podem entrar no meio tempo,
            // mas a sequência restaura a ordem antes do próximo append.
            foreach (var queued in pending)
            {
                Queue.Writer.TryWrite(queued);
            }

            SetPersistenceWarning($"Outbox I/O: {e.Message}");
            return false;
        }
    }

    private static async Task AppendOutboxAsync(IReadOnlyCollection<ImortaisTelemetryEvent> events, CancellationToken token)
    {
        if (events.Count == 0) return;

        await OutboxLock.WaitAsync(token);
        try
        {
            var path = ResolveOutboxPath();
            EnsureOutboxDirectory(path);
            RecoverInterruptedRewriteLocked(path);
            await EnsureOutboxStateLockedAsync(path, token);

            await using (var stream = new FileStream(
                             path,
                             FileMode.Append,
                             FileAccess.Write,
                             FileShare.Read,
                             4096,
                             FileOptions.Asynchronous))
            await using (var writer = new StreamWriter(stream, Utf8NoBom) { NewLine = "\n" })
            {
                foreach (var evt in events)
                {
                    var line = JsonSerializer.Serialize(evt);
                    await writer.WriteLineAsync(line.AsMemory(), token);
                }

                await writer.FlushAsync(token);
                stream.Flush(flushToDisk: true);
            }

            _outboxEventCount += events.Count;
            _outboxByteCount = new FileInfo(path).Length;
            await TrimOutboxIfNeededLockedAsync(path, token);
        }
        finally
        {
            OutboxLock.Release();
        }
    }

    private static async Task<List<ImortaisTelemetryEvent>> ReadOutboxHeadAsync(int maxBatchSize, CancellationToken token)
    {
        await OutboxLock.WaitAsync(token);
        try
        {
            var path = ResolveOutboxPath();
            EnsureOutboxDirectory(path);
            RecoverInterruptedRewriteLocked(path);
            await EnsureOutboxStateLockedAsync(path, token);
            await TrimOutboxIfNeededLockedAsync(path, token);

            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                return new List<ImortaisTelemetryEvent>();
            }

            for (var attempt = 0; attempt < 2; attempt++)
            {
                var batch = new List<ImortaisTelemetryEvent>(maxBatchSize);
                var malformed = false;

                await using (var stream = new FileStream(
                                 path,
                                 FileMode.Open,
                                 FileAccess.Read,
                                 FileShare.ReadWrite,
                                 4096,
                                 FileOptions.Asynchronous))
                using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    while (batch.Count < maxBatchSize)
                    {
                        var line = await reader.ReadLineAsync(token);
                        if (line == null) break;
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            var evt = JsonSerializer.Deserialize<ImortaisTelemetryEvent>(line, OutboxJsonOptions);
                            if (evt == null || string.IsNullOrWhiteSpace(evt.EventId))
                            {
                                malformed = true;
                                continue;
                            }

                            batch.Add(evt);
                        }
                        catch (JsonException)
                        {
                            malformed = true;
                        }
                    }
                }

                if (!malformed)
                {
                    return batch;
                }

                await RepairMalformedOutboxLockedAsync(path, token);
            }

            return new List<ImortaisTelemetryEvent>();
        }
        finally
        {
            OutboxLock.Release();
        }
    }

    private static async Task AcknowledgeOutboxAsync(IEnumerable<string> eventIds, CancellationToken token)
    {
        var ids = eventIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);

        if (ids.Count == 0) return;

        await OutboxLock.WaitAsync(token);
        try
        {
            var path = ResolveOutboxPath();
            EnsureOutboxDirectory(path);
            RecoverInterruptedRewriteLocked(path);

            if (!File.Exists(path)) return;

            var lines = await File.ReadAllLinesAsync(path, Encoding.UTF8, token);
            var survivors = new List<string>(lines.Length);
            var removed = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var evt = JsonSerializer.Deserialize<ImortaisTelemetryEvent>(line, OutboxJsonOptions);
                    if (evt != null && ids.Contains(evt.EventId))
                    {
                        removed++;
                        continue;
                    }
                }
                catch (JsonException)
                {
                    // Linha corrompida não pode bloquear a fila para sempre.
                    SetPersistenceWarning("Outbox: linha corrompida removida durante ACK.");
                    continue;
                }

                survivors.Add(line);
            }

            if (removed == 0) return;

            await AtomicRewriteLinesLockedAsync(path, survivors, token);
            _outboxEventCount = survivors.Count;
            _outboxByteCount = File.Exists(path) ? new FileInfo(path).Length : 0;
            _outboxStateInitialized = true;
            _outboxStatePath = path;
        }
        finally
        {
            OutboxLock.Release();
        }
    }

    private static async Task TrimOutboxIfNeededLockedAsync(string path, CancellationToken token)
    {
        var maxEvents = _config.MaxOutboxEvents > 0
            ? _config.MaxOutboxEvents
            : ImortaisTelemetryConfig.DefaultMaxOutboxEvents;
        var maxBytes = _config.MaxOutboxBytes > 0
            ? _config.MaxOutboxBytes
            : ImortaisTelemetryConfig.DefaultMaxOutboxBytes;

        if (_outboxEventCount <= maxEvents && _outboxByteCount <= maxBytes)
        {
            return;
        }

        var lines = (await File.ReadAllLinesAsync(path, Encoding.UTF8, token))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        long totalBytes = lines.Sum(GetNdjsonLineByteCount);
        var removeCount = Math.Max(0, lines.Count - maxEvents);

        while (removeCount < lines.Count && totalBytes > maxBytes)
        {
            totalBytes -= GetNdjsonLineByteCount(lines[removeCount]);
            removeCount++;
        }

        if (removeCount <= 0)
        {
            return;
        }

        var survivors = lines.Skip(removeCount).ToList();
        await AtomicRewriteLinesLockedAsync(path, survivors, token);

        _outboxEventCount = survivors.Count;
        _outboxByteCount = File.Exists(path) ? new FileInfo(path).Length : 0;
        _outboxStateInitialized = true;
        _outboxStatePath = path;

        if (!_outboxLimitWarningActive)
        {
            _outboxLimitWarningActive = true;
            SetPersistenceWarning($"Outbox atingiu o limite; {removeCount} evento(s) mais antigo(s) foram descartados.");
        }
    }

    private static async Task RepairMalformedOutboxLockedAsync(string path, CancellationToken token)
    {
        var lines = await File.ReadAllLinesAsync(path, Encoding.UTF8, token);
        var survivors = new List<string>(lines.Length);
        var removed = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                var evt = JsonSerializer.Deserialize<ImortaisTelemetryEvent>(line, OutboxJsonOptions);
                if (evt == null || string.IsNullOrWhiteSpace(evt.EventId))
                {
                    removed++;
                    continue;
                }

                survivors.Add(line);
            }
            catch (JsonException)
            {
                removed++;
            }
        }

        if (removed <= 0) return;

        await AtomicRewriteLinesLockedAsync(path, survivors, token);
        _outboxEventCount = survivors.Count;
        _outboxByteCount = File.Exists(path) ? new FileInfo(path).Length : 0;
        _outboxStateInitialized = true;
        _outboxStatePath = path;
        SetPersistenceWarning($"Outbox recuperada; {removed} linha(s) inválida(s) removida(s).");
    }

    private static async Task AtomicRewriteLinesLockedAsync(string path, IReadOnlyCollection<string> lines, CancellationToken token)
    {
        var tempPath = path + ".tmp";

        await using (var stream = new FileStream(
                         tempPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         4096,
                         FileOptions.Asynchronous))
        await using (var writer = new StreamWriter(stream, Utf8NoBom) { NewLine = "\n" })
        {
            foreach (var line in lines)
            {
                await writer.WriteLineAsync(line.AsMemory(), token);
            }

            await writer.FlushAsync(token);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
        {
            try
            {
                File.Replace(tempPath, path, null, ignoreMetadataErrors: true);
            }
            catch (PlatformNotSupportedException)
            {
                File.Move(tempPath, path, overwrite: true);
            }
            catch (IOException)
            {
                File.Move(tempPath, path, overwrite: true);
            }
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    private static async Task EnsureOutboxStateLockedAsync(string path, CancellationToken token)
    {
        if (_outboxStateInitialized
            && string.Equals(_outboxStatePath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!File.Exists(path))
        {
            _outboxEventCount = 0;
            _outboxByteCount = 0;
            _outboxStatePath = path;
            _outboxStateInitialized = true;
            return;
        }

        var count = 0;
        await using (var stream = new FileStream(
                         path,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.ReadWrite,
                         4096,
                         FileOptions.Asynchronous))
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            while (await reader.ReadLineAsync(token) is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line)) count++;
            }
        }

        _outboxEventCount = count;
        _outboxByteCount = new FileInfo(path).Length;
        _outboxStatePath = path;
        _outboxStateInitialized = true;
    }

    private static void RecoverInterruptedRewriteLocked(string path)
    {
        var tempPath = path + ".tmp";
        if (!File.Exists(tempPath)) return;

        if (File.Exists(path))
        {
            File.Delete(tempPath);
            return;
        }

        File.Move(tempPath, path);
    }

    private static string ResolveOutboxPath()
    {
        return string.IsNullOrWhiteSpace(_config.OutboxPath)
            ? ImortaisTelemetryConfig.DefaultOutboxPath
            : _config.OutboxPath;
    }

    private static void EnsureOutboxDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static long GetNdjsonLineByteCount(string line)
    {
        return Utf8NoBom.GetByteCount(line) + 1;
    }

    private static void ResetOutboxState()
    {
        _outboxStateInitialized = false;
        _outboxStatePath = null;
        _outboxEventCount = 0;
        _outboxByteCount = 0;
        _outboxLimitWarningActive = false;
        SetPersistenceWarning(string.Empty);
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

            QueueEvent(new ImortaisTelemetryEvent
            {
                Type = "combat_delta",
                PlayerName = acc.Player,
                Payload = new Dictionary<string, object?>
                {
                    ["player"] = acc.Player,
                    ["damage"] = damage,
                    ["healing"] = healing,
                    ["cluster"] = string.IsNullOrWhiteSpace(acc.Cluster) ? null : acc.Cluster,
                    ["windowMs"] = Math.Max(250, _config.BatchIntervalMs)
                }
            });
        }
    }

    private sealed record QueuedTelemetryEvent(long Sequence, ImortaisTelemetryEvent Event);

    private sealed class CombatAccumulator
    {
        public string Player = string.Empty;
        public string Cluster = string.Empty;
        public long Damage;
        public long Healing;
    }
}
