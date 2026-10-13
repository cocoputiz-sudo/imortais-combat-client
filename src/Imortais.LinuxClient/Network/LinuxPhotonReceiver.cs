using System.Collections;
using Imortais.LinuxClient.Models;
using Imortais.LinuxClient.Services;
using StatisticsAnalysisTool.PhotonPackageParser;

namespace Imortais.LinuxClient.Network;

public sealed class LinuxPhotonReceiver : PhotonParser
{
    private const byte ChangeClusterOperation = 41;
    private readonly CombatState _state;
    private readonly Outbox _outbox;
    private readonly Func<ClientSettings> _settings;
    private readonly Dictionary<long, PlayerInfo> _entities = new();
    private readonly Dictionary<Guid, string> _partyByGuid = new();
    private readonly object _entityLock = new();
    public LinuxPhotonDiagnostics Diagnostics { get; } = new();

    public event Action<string>? Log;
    public event Action? PartyChanged;
    public event Action? CombatChanged;
    public event Action? GameDataDetected;

    public LinuxPhotonReceiver(CombatState state, Outbox outbox, Func<ClientSettings> settings)
    {
        _state = state;
        _outbox = outbox;
        _settings = settings;
    }

    protected override void OnRequest(byte operationCode, Dictionary<byte, object> parameters)
    {
        GameDataDetected?.Invoke();
    }

    protected override void OnResponse(byte operationCode, short returnCode, string debugMessage, Dictionary<byte, object> parameters)
    {
        GameDataDetected?.Invoke();
        if (operationCode != ChangeClusterOperation || returnCode != 0) return;

        var clusterIndex = GetString(parameters, 0).Trim();
        if (string.IsNullOrWhiteSpace(clusterIndex)) return;

        // O Windows resolve ChangeCluster.Index por WorldData antes de publicar
        // zone_change. O Linux faz a mesma conversão com a tabela compacta embutida.
        var clusterName = AlbionWorldMapNames.Resolve(clusterIndex).Trim();
        _state.SetCluster(clusterName);
        lock (_entityLock) _entities.Clear();

        _ = _outbox.AddAsync(TelemetryEvent.Create(
            "zone_change",
            _settings().PlayerName,
            LinuxTelemetryPayloads.ZoneChange(clusterIndex, clusterName)));

        Log?.Invoke($"MAPA   {clusterName} ({clusterIndex})");
    }

    protected override void OnEvent(byte code, Dictionary<byte, object> parameters)
    {
        GameDataDetected?.Invoke();

        var eventCode = LinuxPhotonEventCodes.Resolve(code, parameters);
        Diagnostics.Observe(eventCode, parameters);
        switch (eventCode)
        {
            case LinuxPhotonEventCodes.Leave:
                HandleLeave(parameters);
                break;
            case LinuxPhotonEventCodes.NewCharacter:
                HandleNewCharacter(parameters);
                break;
            case LinuxPhotonEventCodes.HealthUpdate:
                HandleHealthUpdate(parameters);
                break;
            case LinuxPhotonEventCodes.HealthUpdates:
                HandleHealthUpdates(parameters);
                break;
            case LinuxPhotonEventCodes.PartyJoined:
                HandlePartyJoined(parameters);
                break;
            case LinuxPhotonEventCodes.PartyPlayerJoined:
                HandlePartyPlayerJoined(parameters);
                break;
            case LinuxPhotonEventCodes.PartyPlayerLeft:
                HandlePartyPlayerLeft(parameters);
                break;
            case LinuxPhotonEventCodes.PartyDisbanded:
                _partyByGuid.Clear();
                _state.ClearParty();
                EmitPartySnapshot();
                Log?.Invoke("PARTY  dissolvida");
                PartyChanged?.Invoke();
                break;
            case LinuxPhotonEventCodes.Died:
                HandleDied(parameters);
                break;
            case LinuxPhotonEventCodes.KilledPlayer:
                HandleKilledPlayer(parameters);
                break;
            case LinuxPhotonEventCodes.OtherGrabbedLoot:
                HandleGrabbedLoot(parameters);
                break;
        }
    }

    private void HandleNewCharacter(Dictionary<byte, object> p)
    {
        var objectId = GetLong(p, 0);
        var name = GetString(p, 1);
        if (objectId <= 0 || string.IsNullOrWhiteSpace(name)) return;

        var playerId = ToGuid(GetValue(p, 7));
        var guild = GetString(p, 8);
        var alliance = GetString(p, 51);
        lock (_entityLock)
            _entities[objectId] = new PlayerInfo(
                name,
                playerId == Guid.Empty ? null : playerId.ToString("D"),
                guild,
                alliance);
    }

    private void HandleLeave(Dictionary<byte, object> p)
    {
        var objectId = GetLong(p, 0);
        if (objectId <= 0) return;
        lock (_entityLock) _entities.Remove(objectId);
    }

    public IReadOnlyList<Dictionary<string, object?>> SnapshotNearbyPlayers()
    {
        lock (_entityLock)
        {
            return _entities
                .OrderBy(x => x.Value.Name, StringComparer.OrdinalIgnoreCase)
                .Take(500)
                .Select(x => new Dictionary<string, object?>
                {
                    ["objectId"] = x.Key,
                    ["playerId"] = x.Value.PlayerId,
                    ["name"] = x.Value.Name,
                    ["guild"] = NullIfEmpty(x.Value.Guild),
                    ["alliance"] = NullIfEmpty(x.Value.Alliance)
                })
                .ToArray();
        }
    }

    private void HandleHealthUpdate(Dictionary<byte, object> p)
    {
        var healthChange = GetDouble(p, 2);
        var causerId = GetLong(p, 6);
        ApplyHealth(causerId, healthChange);
    }

    private void HandleHealthUpdates(Dictionary<byte, object> p)
    {
        var changes = ToList(GetValue(p, 2));
        var causers = ToList(GetValue(p, 6));
        var count = Math.Max(changes.Count, causers.Count);

        for (var i = 0; i < count; i++)
        {
            var healthChange = i < changes.Count ? ToDouble(changes[i]) : 0;
            var causerId = i < causers.Count ? ToLong(causers[i]) : 0;
            ApplyHealth(causerId, healthChange);
        }
    }

    private void ApplyHealth(long causerId, double healthChange)
    {
        if (causerId <= 0 || Math.Abs(healthChange) < 0.5) return;

        PlayerInfo? causer;
        lock (_entityLock) _entities.TryGetValue(causerId, out causer);
        if (causer is null || string.IsNullOrWhiteSpace(causer.Name)) return;

        var amount = (long)Math.Round(Math.Abs(healthChange), MidpointRounding.AwayFromZero);
        if (healthChange < 0) _state.AddDamage(causer.Name, amount);
        else _state.AddHealing(causer.Name, amount);

        CombatChanged?.Invoke();
    }

    // Não substituir um roster íntegro por uma lista parcial ou truncada.
    public static bool TryDecodePartyRoster(Dictionary<byte, object> p, out Dictionary<Guid, string> roster)
    {
        roster = new Dictionary<Guid, string>();
        foreach (var (guidsKey, namesKey) in new (byte, byte)[] { (8, 9), (4, 5) })
        {
            var rawNames = GetValue(p, namesKey);
            var rawGuids = GetValue(p, guidsKey);
            if (rawNames is null || rawGuids is null) continue;
            var names = ToStrings(rawNames);
            var guids = ToGuids(rawGuids);
            if (names.Count == 0 || names.Count != guids.Count || names.Any(string.IsNullOrWhiteSpace) || guids.Any(g => g == Guid.Empty)
                || guids.Distinct().Count() != guids.Count
                || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
                continue;
            var candidate = new Dictionary<Guid, string>();
            for (var i = 0; i < names.Count; i++) candidate.Add(guids[i], names[i]);
            roster = candidate;
            return true;
        }
        return false;
    }

    private void HandlePartyJoined(Dictionary<byte, object> p)
    {
        if (!TryDecodePartyRoster(p, out var roster))
        {
            Log?.Invoke("PARTY  roster incompleto/ilegível ignorado");
            return;
        }

        var current = _state.SnapshotParty();
        var changed = !current.ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(roster.Values);
        _partyByGuid.Clear();
        foreach (var member in roster) _partyByGuid.Add(member.Key, member.Value);
        _state.SetParty(roster.Values);
        if (changed) EmitPartySnapshot();
        Log?.Invoke($"PARTY  snapshot completo · {roster.Count} membros");
        PartyChanged?.Invoke();
    }

    private void HandlePartyPlayerJoined(Dictionary<byte, object> p)
    {
        var name = GetString(p, 2);
        var guid = ToGuid(GetValue(p, 1));
        if (string.IsNullOrWhiteSpace(name) || guid == Guid.Empty) return;

        _partyByGuid[guid] = name;
        _state.AddPartyMember(name);
        EmitPartySnapshot();
        Log?.Invoke($"PARTY  + {name}");
        PartyChanged?.Invoke();
    }

    private void HandlePartyPlayerLeft(Dictionary<byte, object> p)
    {
        var guid = ToGuid(GetValue(p, 1));
        if (guid == Guid.Empty || !_partyByGuid.Remove(guid, out var name)) return;

        _state.RemovePartyMember(name);
        EmitPartySnapshot();
        Log?.Invoke($"PARTY  - {name}");
        PartyChanged?.Invoke();
    }

    // KilledPlayer é um candidato de abate. A confirmação letal vem de Died,
    // evitando contar o mesmo kill duas vezes quando ambos os eventos chegarem.
    private void HandleKilledPlayer(Dictionary<byte, object> p)
    {
        var killerObjectId = GetLong(p, 0);
        var victim = GetString(p, 2);
        PlayerInfo? killer;
        lock (_entityLock) _entities.TryGetValue(killerObjectId, out killer);
        if (killer is not null && (_state.IsPartyMember(killer.Name) || IsImortaisFamily(killer.Guild)))
            Log?.Invoke($"KILL   candidato observado · {victim}");
    }

    private void HandleGrabbedLoot(Dictionary<byte, object> p)
    {
        // Outro loot no Albion: 1=corpo, 2=looter, 3=prata, 4=itemIndex, 5=quantidade.
        if (GetBool(p, 3)) return; // dinheiro não é item.
        var looter = GetString(p, 2);
        var lootedFrom = GetString(p, 1);
        var itemIndex = GetLong(p, 4);
        var quantity = GetLong(p, 5);
        if (string.IsNullOrWhiteSpace(looter) || itemIndex <= 0 || quantity <= 0 || quantity > int.MaxValue) return;
        string? guild;
        lock (_entityLock)
            guild = _entities.Values.FirstOrDefault(x => string.Equals(x.Name, looter, StringComparison.OrdinalIgnoreCase))?.Guild;
        // O alpha ainda não contém o catálogo AODP de itens/valor: identificar pelo índice
        // é melhor do que enviar uma descrição ou um preço inventados.
        _ = _outbox.AddAsync(TelemetryEvent.Create("loot", looter,
            new Dictionary<string, object?>
            {
                ["lootedBy"] = looter,
                ["lootedByGuild"] = string.IsNullOrWhiteSpace(guild) ? null : guild,
                ["lootedFrom"] = lootedFrom,
                ["item"] = $"ITEM_INDEX_{itemIndex}",
                ["itemIndex"] = itemIndex,
                ["quantity"] = quantity,
                ["estimatedValue"] = 0,
                ["cluster"] = _state.CurrentCluster,
                ["source"] = "OtherGrabbedLoot-linux-alpha"
            }));
        Log?.Invoke($"LOOT   {looter} · item {itemIndex} x{quantity}");
    }

    public void ResendPartyForCtaChange()
    {
        if (_state.SnapshotParty().Count > 0)
            EmitPartySnapshot();
    }

    private void HandleDied(Dictionary<byte, object> p)
    {
        var victimObjectId = GetLong(p, 1);
        var victim = GetString(p, 2);
        var victimGuild = GetString(p, 3);
        var killerObjectId = GetLong(p, 9);
        var killer = GetString(p, 10);
        var killerGuild = GetString(p, 11);
        var lethal = GetBool(p, 17);

        if (!lethal || string.IsNullOrWhiteSpace(victim) || string.IsNullOrWhiteSpace(killer)) return;

        var relevant =
            IsImortaisFamily(victimGuild) ||
            IsImortaisFamily(killerGuild) ||
            _state.IsPartyMember(victim) ||
            _state.IsPartyMember(killer);

        if (!relevant) return;

        if (IsOurs(killer, killerGuild)) _state.AddKill(killer);
        if (IsOurs(victim, victimGuild)) _state.AddDeath(victim);

        _ = _outbox.AddAsync(TelemetryEvent.Create(
            "player_death_observed",
            victim,
            new Dictionary<string, object?>
            {
                ["victimObjectId"] = victimObjectId > 0 ? victimObjectId : null,
                ["victim"] = victim,
                ["victimGuild"] = NullIfEmpty(victimGuild),
                ["killerObjectId"] = killerObjectId > 0 ? killerObjectId : null,
                ["killer"] = killer,
                ["killerGuild"] = NullIfEmpty(killerGuild),
                ["isLethal"] = true,
                ["cluster"] = _state.CurrentCluster,
                ["source"] = "DiedEvent-linux"
            }));

        Log?.Invoke($"DEATH  {victim} ← {killer}");
        CombatChanged?.Invoke();
    }

    private void EmitPartySnapshot()
    {
        var members = _state.SnapshotParty().ToArray();
        _ = _outbox.AddAsync(TelemetryEvent.Create(
            "party_snapshot",
            _settings().PlayerName,
            new Dictionary<string, object?> { ["members"] = members }));
    }

    private bool IsOurs(string player, string guild) =>
        IsImortaisFamily(guild) || _state.IsPartyMember(player);

    private static bool IsImortaisFamily(string? guild)
    {
        var normalized = new string((guild ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return normalized is "imortais" or "imortais2" or "imortaisacademy";
    }

    private static object? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static object? GetValue(Dictionary<byte, object> p, byte key) =>
        p.TryGetValue(key, out var value) ? value : null;

    private static string GetString(Dictionary<byte, object> p, byte key) =>
        p.TryGetValue(key, out var v) ? Convert.ToString(v)?.Trim() ?? string.Empty : string.Empty;

    private static long GetLong(Dictionary<byte, object> p, byte key) =>
        p.TryGetValue(key, out var v) ? ToLong(v) : 0;

    private static double GetDouble(Dictionary<byte, object> p, byte key) =>
        p.TryGetValue(key, out var v) ? ToDouble(v) : 0;

    private static bool GetBool(Dictionary<byte, object> p, byte key)
    {
        if (!p.TryGetValue(key, out var v) || v is null) return false;
        try { return Convert.ToBoolean(v); } catch { return false; }
    }

    private static long ToLong(object? value)
    {
        if (value is null) return 0;
        try { return Convert.ToInt64(value); } catch { return 0; }
    }

    private static double ToDouble(object? value)
    {
        if (value is null) return 0;
        try { return Convert.ToDouble(value); } catch { return 0; }
    }

    private static List<object?> ToList(object? value)
    {
        if (value is null) return [];
        if (value is string) return [value];
        if (value is IEnumerable sequence)
        {
            var result = new List<object?>();
            foreach (var item in sequence) result.Add(item);
            return result;
        }
        return [value];
    }

    private static List<string> ToStrings(object? value) =>
        ToList(value).Select(x => Convert.ToString(x)?.Trim() ?? string.Empty)
            .ToList();

    private static List<Guid> ToGuids(object? value)
    {
        if (value is byte[] raw && raw.Length >= 16 && raw.Length % 16 == 0)
        {
            var result = new List<Guid>();
            for (var i = 0; i < raw.Length; i += 16) result.Add(new Guid(raw.AsSpan(i, 16)));
            return result;
        }

        return ToList(value).Select(ToGuid).ToList();
    }

    private static Guid ToGuid(object? value)
    {
        try
        {
            if (value is Guid g) return g;
            if (value is byte[] bytes && bytes.Length == 16) return new Guid(bytes);
            return Guid.TryParse(Convert.ToString(value), out var parsed) ? parsed : Guid.Empty;
        }
        catch { return Guid.Empty; }
    }

    private sealed record PlayerInfo(string Name, string? PlayerId, string Guild, string Alliance);
}
