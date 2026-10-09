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
    private static readonly ConcurrentDictionary<string, string> LastGuildMightProbePayloads = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> GuildMightOperationsSeen = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> LastPartySnapshotPayloads = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> GuildPresencePlayersSeen = new(StringComparer.OrdinalIgnoreCase);
    // Presence Collector separado do combate: NewCharacter abre a presença local e
    // Leave a remove. O War Room recebe snapshots periódicos para deduplicar observers.
    private static readonly ConcurrentDictionary<long, NearbyPlayerPresence> NearbyPlayers = new();
    private static readonly ConcurrentQueue<string> RecentActivity = new();
    private static readonly CancellationTokenSource Cts = new();
    private static readonly object StartLock = new();
    private static readonly object StatusLock = new();
    private static readonly object GuildProbeLocalDumpLock = new();
    private static readonly object PartySnapshotLock = new();
    private static readonly SemaphoreSlim OutboxLock = new(1, 1);
    private static readonly JsonSerializerOptions OutboxJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PlayerPresenceSnapshotInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ContextRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InitialIngestRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxIngestRetryDelay = TimeSpan.FromSeconds(60);
    private const string ClientVersion = "0.6.1";
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
    private static DateTime _lastContextRefreshAttemptUtc = DateTime.MinValue;
    private static DateTime _lastPlayerPresenceSnapshotEnqueuedUtc = DateTime.MinValue;
    private static string? _currentPresenceCluster;
    private static int _gameDetectedState = -1;
    private static long _partySnapshotDeduplicatedCount;
    private static long _guildPresenceProbeCount;
    private static DateTime? _lastGuildPresenceProbeAtUtc;
    private static long _guildMightProbeCount;
    private static DateTime? _lastGuildMightProbeAtUtc;

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
        long GuildMightProbeCount,
        int GuildMightOperationCount,
        DateTime? LastGuildMightProbeAtUtc,
        IReadOnlyList<string> RecentActivity);

    private sealed record NearbyPlayerPresence(
        long ObjectId,
        string? PlayerId,
        string Name,
        string? Guild,
        string? Alliance,
        string? Cluster);

    public sealed record PartyEquipmentSnapshot(
        string? MainHand,
        string? OffHand,
        string? Head,
        string? Chest,
        string? Shoes,
        string? Bag,
        string? Cape,
        string? Mount,
        string? Potion,
        string? Food);

    public sealed record PartyMemberSnapshot(
        string Name,
        double ItemPower,
        bool Inspected,
        PartyEquipmentSnapshot Equipment);

    public static void Start() => EnsureStarted();

    public static bool IsGuildProbeLocalDiagnosticsEnabled => _config.GuildProbeLocalDiagnosticsEnabled;
    public static void SetHomologGuildToken(string token)
    {
        // The token is never printed in diagnostics or recent activity.
        if (_config.HomologGuildUploadEnabled)
            throw new InvalidOperationException("Desative o envio de homologação antes de trocar a chave.");
        var value=(token??string.Empty).Trim();
        if (!value.StartsWith("imt_", StringComparison.Ordinal) || value.Length < 20)
            throw new ArgumentException("Token da homologação inválido.");
        var previous=_config.HomologGuildToken;
        _config.HomologGuildToken=value;
        try { ImortaisTelemetryConfig.Save(_config); }
        catch { _config.HomologGuildToken=previous; throw; }
        AddActivity("HOMOLOG GUILD: token de teste configurado");
    }

    public static bool IsHomologGuildUploadEnabled => _config.HomologGuildUploadEnabled;
    public static long HomologGuildAcceptedCount => Interlocked.Read(ref _homologGuildAcceptedCount);
    public static long HomologGuildRejectedCount => Interlocked.Read(ref _homologGuildRejectedCount);
    // Only this literal QA host is permitted; user-controlled production ServerUrl is never changed.
    private const string HomologGuildIngestUrl =
        "https://war-room-might-homolog-homologacao.up.railway.app/api/telemetry/ingest";
    private static readonly SemaphoreSlim HomologGuildGate = new(2, 2);
    private static long _homologGuildAcceptedCount, _homologGuildRejectedCount;

    public static void SetHomologGuildUploadEnabled(bool enabled)
    {
        var cfg = _config;
        if (enabled && string.IsNullOrWhiteSpace(cfg.HomologGuildToken))
            throw new InvalidOperationException("Configure HomologGuildToken no telemetry.json antes de ativar.");
        cfg.HomologGuildUploadEnabled = enabled;
        try { ImortaisTelemetryConfig.Save(cfg); }
        catch { cfg.HomologGuildUploadEnabled = !enabled; throw; }
        AddActivity(enabled ? "HOMOLOG GUILD: ATIVADO (somente QA)" : "HOMOLOG GUILD: DESLIGADO");
    }

    private static async Task SendGuildProbeToHomologAsync(string direction, string operationName,
        int operationCode, Dictionary<string, object?> parameters)
    {
        if (!await HomologGuildGate.WaitAsync(0)) { Interlocked.Increment(ref _homologGuildRejectedCount); return; }
        try
        {
            var cfg = _config;
            if (!cfg.HomologGuildUploadEnabled || string.IsNullOrWhiteSpace(cfg.HomologGuildToken)) return;
            var evt = new ImortaisTelemetryEvent {
                Type = "guild_might_probe", PlayerName = cfg.PlayerName,
                Payload = new Dictionary<string, object?> {
                    ["direction"]=direction, ["operationName"]=operationName,
                    ["operationCode"]=operationCode, ["parameters"]=parameters, ["probeVersion"]=3 }
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, HomologGuildIngestUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.HomologGuildToken);
            req.Content = JsonContent.Create(new {
                device = new { deviceId=cfg.DeviceId, playerName=cfg.PlayerName, version=ClientVersion },
                events=new[]{evt}
            });
            using var resp=await Http.SendAsync(req);
            if (resp.IsSuccessStatusCode) Interlocked.Increment(ref _homologGuildAcceptedCount);
            else { Interlocked.Increment(ref _homologGuildRejectedCount);
                AddActivity($"HOMOLOG GUILD HTTP {(int)resp.StatusCode} {operationName}"); }
        }
        catch (Exception e) {
            Interlocked.Increment(ref _homologGuildRejectedCount);
            AddActivity($"HOMOLOG GUILD ERRO {e.GetType().Name}");
        }
        finally { HomologGuildGate.Release(); }
    }


    /// <summary>Opt-in switch; only these three guild probes are redirected to the local dump.</summary>
    public static void SetGuildProbeLocalDiagnosticsEnabled(bool enabled)
    {
        lock (GuildProbeLocalDumpLock)
        {
            var current = _config;
            if (current.GuildProbeLocalDiagnosticsEnabled == enabled) return;
            current.GuildProbeLocalDiagnosticsEnabled = enabled;
            try
            {
                ImortaisTelemetryConfig.Save(current);
            }
            catch
            {
                current.GuildProbeLocalDiagnosticsEnabled = !enabled;
                throw;
            }
        }
        AddActivity(enabled
            ? "GUILD DIAGNÓSTICO LOCAL ATIVADO (sem upload de Guild Might)"
            : "GUILD DIAGNÓSTICO LOCAL DESATIVADO");
    }



    public static void ReloadConfig()
    {
        _config = ImortaisTelemetryConfig.Load();
        _lastContextRefreshAttemptUtc = DateTime.MinValue;
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
                Interlocked.Read(ref _guildMightProbeCount),
                GuildMightOperationsSeen.Count,
                _lastGuildMightProbeAtUtc,
                RecentActivity.ToArray());
        }
    }

    public static void PartySnapshot(IEnumerable<PartyMemberSnapshot> members)
    {
        static string? CleanItem(string? uniqueName)
        {
            return string.IsNullOrWhiteSpace(uniqueName) ? null : uniqueName.Trim();
        }

        var normalizedStates = members
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => new PartyMemberSnapshot(
                x.Name.Trim(),
                Math.Round(Math.Max(0, x.ItemPower), 1),
                x.Inspected,
                new PartyEquipmentSnapshot(
                    CleanItem(x.Equipment?.MainHand),
                    CleanItem(x.Equipment?.OffHand),
                    CleanItem(x.Equipment?.Head),
                    CleanItem(x.Equipment?.Chest),
                    CleanItem(x.Equipment?.Shoes),
                    CleanItem(x.Equipment?.Bag),
                    CleanItem(x.Equipment?.Cape),
                    CleanItem(x.Equipment?.Mount),
                    CleanItem(x.Equipment?.Potion),
                    CleanItem(x.Equipment?.Food))))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var normalizedNames = normalizedStates.Select(x => x.Name).ToArray();

        // O fingerprint inclui o equipamento. Assim, se a composição da PT continua
        // igual mas alguém troca uma peça, o War Room recebe um novo snapshot.
        var memberStates = normalizedStates.Select(x => new Dictionary<string, object?>
        {
            ["name"] = x.Name,
            ["itemPower"] = x.ItemPower,
            ["inspected"] = x.Inspected,
            ["equipment"] = new Dictionary<string, object?>
            {
                ["mainHand"] = x.Equipment.MainHand,
                ["offHand"] = x.Equipment.OffHand,
                ["head"] = x.Equipment.Head,
                ["chest"] = x.Equipment.Chest,
                ["shoes"] = x.Equipment.Shoes,
                ["bag"] = x.Equipment.Bag,
                ["cape"] = x.Equipment.Cape,
                ["mount"] = x.Equipment.Mount,
                ["potion"] = x.Equipment.Potion,
                ["food"] = x.Equipment.Food
            }
        }).ToArray();

        var fingerprint = JsonSerializer.Serialize(memberStates);

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
                    Payload = new Dictionary<string, object?>
                    {
                        // Mantém o formato antigo para roteamento e compatibilidade.
                        ["members"] = normalizedNames,
                        // V2: estado visual do equipamento, sem julgamento certo/errado.
                        ["memberStates"] = memberStates
                    }
                }))
            {
                return;
            }

            LastPartySnapshotPayloads[PartySnapshotFingerprintKey] = fingerprint;
            lock (StatusLock)
            {
                _lastPartySnapshotAtUtc = DateTime.UtcNow;
                _lastPartyMemberCount = normalizedNames.Length;
            }
            AddActivity($"PARTY snapshot enviado · {normalizedNames.Length} membro{(normalizedNames.Length == 1 ? string.Empty : "s")} · equipamentos");
        }
    }

    // Compatibilidade para qualquer chamada legada que ainda envie somente nomes.
    public static void PartySnapshot(IEnumerable<string> members)
    {
        PartySnapshot(
            members
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => new PartyMemberSnapshot(
                    x.Trim(),
                    0,
                    false,
                    new PartyEquipmentSnapshot(null, null, null, null, null, null, null, null, null, null))));
    }

    public static void SetGameDetected(bool detected)
    {
        Volatile.Write(ref _gameDetectedState, detected ? 1 : 0);
    }

    public static void NearbyPlayerObserved(
        long objectId,
        Guid? playerId,
        string? playerName,
        string? guildName,
        string? allianceName,
        string? clusterName)
    {
        if (objectId <= 0 || string.IsNullOrWhiteSpace(playerName))
        {
            return;
        }

        var name = playerName.Trim();
        var guild = string.IsNullOrWhiteSpace(guildName) ? null : guildName.Trim();
        var alliance = string.IsNullOrWhiteSpace(allianceName) ? null : allianceName.Trim();
        var cluster = string.IsNullOrWhiteSpace(clusterName)
            ? _currentPresenceCluster
            : clusterName.Trim();

        NearbyPlayers[objectId] = new NearbyPlayerPresence(
            objectId,
            playerId?.ToString("D"),
            name,
            guild,
            alliance,
            cluster);
    }

    public static void NearbyPlayerLeft(long objectId)
    {
        if (objectId > 0)
        {
            NearbyPlayers.TryRemove(objectId, out _);
        }
    }

    public static void ZoneChange(
        string? clusterIndex,
        string? clusterName,
        string? clusterMode,
        string? mapType,
        string? sourceClusterIndex)
    {
        var index = string.IsNullOrWhiteSpace(clusterIndex) ? null : clusterIndex.Trim();
        var name = string.IsNullOrWhiteSpace(clusterName) ? null : clusterName.Trim();

        if (string.IsNullOrWhiteSpace(index) && string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _currentPresenceCluster = name ?? index;
        NearbyPlayers.Clear();

        if (Enqueue(new ImortaisTelemetryEvent
            {
                Type = "zone_change",
                PlayerName = _config.PlayerName,
                Payload = new Dictionary<string, object?>
                {
                    ["clusterIndex"] = index,
                    ["clusterName"] = name,
                    ["clusterMode"] = string.IsNullOrWhiteSpace(clusterMode) ? null : clusterMode.Trim(),
                    ["mapType"] = string.IsNullOrWhiteSpace(mapType) ? null : mapType.Trim(),
                    ["sourceClusterIndex"] = string.IsNullOrWhiteSpace(sourceClusterIndex) ? null : sourceClusterIndex.Trim()
                }
            }))
        {
            AddActivity($"ZONE {(name ?? index ?? "?")}");
        }
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

    public static void GuildMightProbe(string direction, string operationName, int operationCode, IReadOnlyDictionary<byte, object> parameters)
    {
        if ((direction != "request" && direction != "response")
            || string.IsNullOrWhiteSpace(operationName)
            || parameters == null)
        {
            return;
        }

        // QA opt-in is a separate authenticated HTTPS path, never the production
        // outbox or _config.ServerUrl; local raw dumps can remain enabled.
        if (_config.HomologGuildUploadEnabled &&
            (operationName=="GetGuildChallengePoints" ||
             operationName=="GetGuildMightCategoryOverview" ||
             operationName=="GetGuildMightCategoryContribution" ||
             operationName=="GetGvgSeasonContributionByActivity" ||
             operationName=="GetGvgSeasonRankings"))
        {
            if (_config.GuildProbeLocalDiagnosticsEnabled)
                WriteGuildProbeLocalDump(direction, operationName, operationCode, parameters);
            var raw = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var pair in parameters.OrderBy(p => p.Key))
                raw[pair.Key.ToString()] = LosslessGuildField(operationName,pair.Key,pair.Value,0,10000,16,true);
            _ = SendGuildProbeToHomologAsync(direction, operationName, operationCode, raw);
            return;
        }

        // Default diagnostic mode remains strictly local: no enqueue/outbox/upload.
        if (_config.GuildProbeLocalDiagnosticsEnabled)
        {
            WriteGuildProbeLocalDump(direction, operationName, operationCode, parameters);
            return;
        }

        // New season operations and Challenge are strictly QA-only. Never send
        // these to the production outbox, even if local diagnostics are off.
        if (operationName == "GetGuildChallengePoints"
            || operationName == "GetGvgSeasonContributionByActivity"
            || operationName == "GetGvgSeasonRankings")
        {
            return;
        }

        var normalizedParameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in parameters.OrderBy(x => x.Key).Take(96))
        {
            if (pair.Key == 253) continue;
            // The guild can have more than 400 players; preserve complete parallel lists.
            normalizedParameters[pair.Key.ToString()] = LosslessGuildField(operationName,pair.Key,pair.Value,0,1000,5);
        }

        var payload = new Dictionary<string, object?>
        {
            ["direction"] = direction,
            ["operationName"] = operationName,
            ["operationCode"] = operationCode,
            ["parameters"] = normalizedParameters,
            ["probeVersion"] = 2
        };

        var fingerprintKey = direction + ":" + operationName;
        var fingerprint = JsonSerializer.Serialize(payload);
        if (LastGuildMightProbePayloads.TryGetValue(fingerprintKey, out var previous)
            && string.Equals(previous, fingerprint, StringComparison.Ordinal))
        {
            return;
        }

        LastGuildMightProbePayloads[fingerprintKey] = fingerprint;

        if (Enqueue(new ImortaisTelemetryEvent
            {
                Type = "guild_might_probe",
                PlayerName = _config.PlayerName,
                Payload = payload
            }))
        {
            GuildMightOperationsSeen.TryAdd(operationName, 0);
            Interlocked.Increment(ref _guildMightProbeCount);
            lock (StatusLock)
            {
                _lastGuildMightProbeAtUtc = DateTime.UtcNow;
            }

            AddActivity($"MIGHT {direction.ToUpperInvariant()} {operationName} · {normalizedParameters.Count} params");
        }
    }


    private static object? LosslessGuildField(string operation, byte key, object? value,
        int depth, int maxItems, int maxDepth, bool fullBinary = false)
    {
        // Photon server snapshot IDs are 64-bit .NET ticks and exceed the exact
        // integer range of JavaScript. Encode only the identity fields as text.
        bool isMarker = (operation == "GetGuildMightCategoryOverview" && key == 1)
            || (operation == "GetGuildMightCategoryContribution" && key == 2)
            || (operation == "GetGuildChallengePoints" && key == 1);
        if (isMarker && value is long ticks)
            return ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (isMarker && value is ulong unsignedTicks)
            return unsignedTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return SanitizePhotonValue(value, depth, maxItems, maxDepth, fullBinary);
    }

    private static void WriteGuildProbeLocalDump(
        string direction,
        string operationName,
        int operationCode,
        IReadOnlyDictionary<byte, object> parameters)
    {
        if (operationName != "GetGuildChallengePoints"
            && operationName != "GetGuildMightCategoryOverview"
            && operationName != "GetGuildMightCategoryContribution"
            && operationName != "GetGvgSeasonContributionByActivity"
            && operationName != "GetGvgSeasonRankings")
        {
            return;
        }

        try
        {
            // Decoded but otherwise unfiltered Photon parameters: preserve all parameter
            // keys, nested dictionaries, parallel arrays, and full binary values locally.
            var rawParameters = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var pair in parameters.OrderBy(pair => pair.Key))
            {
                rawParameters[pair.Key.ToString()] = LosslessGuildField(
                    operationName,pair.Key,pair.Value,0,10000,16,true);
            }

            var record = new Dictionary<string, object?>
            {
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["direction"] = direction,
                ["operationName"] = operationName,
                ["operationCode"] = operationCode,
                ["parameters"] = rawParameters,
                ["captureSource"] = "local-decoded-photon"
            };
            var line = JsonSerializer.Serialize(record) + Environment.NewLine;
            var directory = Path.Combine(ImortaisTelemetryConfig.DirectoryPath, "Diagnostics");
            var day = DateTime.UtcNow.ToString("yyyyMMdd");
            lock (GuildProbeLocalDumpLock)
            {
                Directory.CreateDirectory(directory);
                // The local capture file is rotated at approximately 25 MiB.
                const long maxFileBytes = 25L * 1024 * 1024;
                string? filePath = null;
                for (var index = 0; index < 100; index++)
                {
                    var candidate = Path.Combine(directory, $"guild-probes-{day}-{index:D2}.ndjson");
                    if (!File.Exists(candidate) || new FileInfo(candidate).Length < maxFileBytes)
                    {
                        filePath = candidate;
                        break;
                    }
                }
                if (filePath == null)
                {
                    AddActivity("GUILD DIAGNÓSTICO: limite diário de arquivos atingido");
                    return;
                }
                File.AppendAllText(filePath, line, Utf8NoBom);
            }
            AddActivity($"GUILD DIAGNÓSTICO LOCAL {direction.ToUpperInvariant()} {operationName}");
        }
        catch (Exception e)
        {
            // Disk errors never fall back to network telemetry.
            AddActivity($"GUILD DIAGNÓSTICO: erro de escrita local ({e.GetType().Name})");
        }
    }

    private static object? SanitizePhotonValue(object? value, int depth, int maxItems = 400, int maxDepth = 5, bool fullBinary = false)
    {
        if (value == null) return null;
        if (depth >= maxDepth) return "<max-depth>";

        switch (value)
        {
            case string text:
                return fullBinary || text.Length <= 512 ? text : text[..512];
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
                return fullBinary
                    ? new Dictionary<string, object?> { ["kind"] = "bytes", ["length"] = bytes.Length, ["base64"] = Convert.ToBase64String(bytes) }
                    : new Dictionary<string, object?> { ["kind"] = "bytes", ["length"] = bytes.Length, ["previewBase64"] = Convert.ToBase64String(bytes.Take(64).ToArray()) };
            case IDictionary dictionary:
            {
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                var count = 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (count++ >= (fullBinary ? maxItems : Math.Min(256, maxItems))) break;
                    result[entry.Key?.ToString() ?? "null"] = SanitizePhotonValue(entry.Value, depth + 1, maxItems, maxDepth, fullBinary);
                }
                return result;
            }
            case Array array:
            {
                var result = new List<object?>();
                var count = Math.Min(array.Length, maxItems);
                for (var i = 0; i < count; i++)
                {
                    result.Add(SanitizePhotonValue(array.GetValue(i), depth + 1, maxItems, maxDepth, fullBinary));
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
                    if (count++ >= maxItems)
                    {
                        result.Add("<truncated>");
                        break;
                    }
                    result.Add(SanitizePhotonValue(item, depth + 1, maxItems, maxDepth, fullBinary));
                }
                return result;
            }
            default:
            {
                var text = value.ToString() ?? value.GetType().FullName ?? "unknown";
                return fullBinary || text.Length <= 512 ? text : text[..512];
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

    public static void ObservePlayerDeath(
        long victimObjectId,
        string victim,
        string? victimGuild,
        long killerObjectId,
        string killer,
        string? killerGuild,
        bool isLethal,
        string? clusterName = null)
    {
        if (!isLethal || string.IsNullOrWhiteSpace(victim) || string.IsNullOrWhiteSpace(killer)) return;

        Enqueue(new ImortaisTelemetryEvent
        {
            Type = "player_death_observed",
            PlayerName = victim.Trim(),
            Payload = new Dictionary<string, object?>
            {
                ["victimObjectId"] = victimObjectId > 0 ? victimObjectId : null,
                ["victim"] = victim.Trim(),
                ["victimGuild"] = string.IsNullOrWhiteSpace(victimGuild) ? null : victimGuild.Trim(),
                ["killerObjectId"] = killerObjectId > 0 ? killerObjectId : null,
                ["killer"] = killer.Trim(),
                ["killerGuild"] = string.IsNullOrWhiteSpace(killerGuild) ? null : killerGuild.Trim(),
                ["isLethal"] = true,
                ["cluster"] = string.IsNullOrWhiteSpace(clusterName) ? null : clusterName.Trim(),
                ["source"] = "DiedEvent"
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

    private static async Task RefreshContextIfDueAsync(CancellationToken token)
    {
        var now = DateTime.UtcNow;
        if (now - _lastContextRefreshAttemptUtc < ContextRefreshInterval) return;

        _lastContextRefreshAttemptUtc = now;
        await RefreshContextAsync(token);
    }

    private static TimeSpan NextIngestRetryDelay(TimeSpan current)
    {
        var nextSeconds = Math.Min(MaxIngestRetryDelay.TotalSeconds, current.TotalSeconds * 2);
        return TimeSpan.FromSeconds(Math.Max(InitialIngestRetryDelay.TotalSeconds, nextSeconds));
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
                SetConnectionState(
                    false,
                    response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                        ? "War Room HTTP 401 · reative o client"
                        : $"War Room HTTP {(int)response.StatusCode}");
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

    private static void TryEnqueuePlayerPresenceSnapshot()
    {
        var configured = _config.Enabled
                         && !string.IsNullOrWhiteSpace(_config.AgentKey)
                         && !string.IsNullOrWhiteSpace(_config.ServerUrl);
        if (!configured || string.IsNullOrWhiteSpace(_config.CtaEventId)) return;

        var now = DateTime.UtcNow;
        if (now - _lastPlayerPresenceSnapshotEnqueuedUtc < PlayerPresenceSnapshotInterval) return;

        var cluster = _currentPresenceCluster;
        if (string.IsNullOrWhiteSpace(cluster)) return;

        var players = NearbyPlayers.Values
            .Where(x => string.Equals(x.Cluster, cluster, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .Select(x => new Dictionary<string, object?>
            {
                ["objectId"] = x.ObjectId,
                ["playerId"] = x.PlayerId,
                ["name"] = x.Name,
                ["guild"] = x.Guild,
                ["alliance"] = x.Alliance
            })
            .ToArray();

        if (Enqueue(new ImortaisTelemetryEvent
            {
                Type = "player_presence_snapshot",
                PlayerName = _config.PlayerName,
                Payload = new Dictionary<string, object?>
                {
                    ["cluster"] = cluster,
                    ["players"] = players,
                    ["observedCount"] = players.Length,
                    ["snapshotIntervalMs"] = (int)PlayerPresenceSnapshotInterval.TotalMilliseconds,
                    ["source"] = "NewCharacter+Leave"
                }
            }))
        {
            _lastPlayerPresenceSnapshotEnqueuedUtc = now;
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
        var ingestRetryDelay = InitialIngestRetryDelay;
        string? blockedAgentKey = null;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Math.Max(250, _config.BatchIntervalMs), token);
                await RefreshContextIfDueAsync(token);
                FlushCombatAccumulators();
                TryEnqueueHeartbeat();
                TryEnqueuePlayerPresenceSnapshot();

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

                if (!string.IsNullOrWhiteSpace(blockedAgentKey))
                {
                    if (string.Equals(blockedAgentKey, _config.AgentKey, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    blockedAgentKey = null;
                    ingestRetryDelay = InitialIngestRetryDelay;
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
                    // O backend e a fonte de verdade para rotear cada lote ao CTA correto.
                    // Nao envie o CtaEventId salvo localmente como vinculacao autoritativa:
                    // ele pode ter sido resolvido em um CTA anterior e ficar stale entre CTAs.
                    events = batch
                });

                HttpResponseMessage response;
                try
                {
                    response = await Http.SendAsync(req, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    SetConnectionState(false, $"Ingest: {e.Message}");
                    await Task.Delay(ingestRetryDelay, token);
                    ingestRetryDelay = NextIngestRetryDelay(ingestRetryDelay);
                    continue;
                }

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        {
                            blockedAgentKey = _config.AgentKey;
                            SetConnectionState(false, "Ingest HTTP 401 · reative o client");
                            continue;
                        }

                        SetConnectionState(false, $"Ingest HTTP {(int)response.StatusCode}");
                        await Task.Delay(ingestRetryDelay, token);
                        ingestRetryDelay = NextIngestRetryDelay(ingestRetryDelay);
                        continue;
                    }

                    ingestRetryDelay = InitialIngestRetryDelay;

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
