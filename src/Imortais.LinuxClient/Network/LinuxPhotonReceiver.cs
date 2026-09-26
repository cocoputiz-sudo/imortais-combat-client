using System.Collections;
using Imortais.LinuxClient.Models;
using Imortais.LinuxClient.Services;
using StatisticsAnalysisTool.PhotonPackageParser;

namespace Imortais.LinuxClient.Network;

public sealed class LinuxPhotonReceiver : PhotonParser
{
    private const byte HealthUpdate = 6;
    private const byte HealthUpdates = 7;
    private const byte NewCharacter = 29;
    private const byte Died = 165;
    private const byte PartyJoined = 231;
    private const byte PartyDisbanded = 232;
    private const byte PartyPlayerJoined = 233;
    private const byte PartyPlayerLeft = 235;
    private const byte ChangeClusterOperation = 41;

    private readonly CombatState _state;
    private readonly Outbox _outbox;
    private readonly Func<ClientSettings> _settings;
    private readonly Dictionary<long, PlayerInfo> _entities = new();
    private readonly Dictionary<Guid, string> _partyByGuid = new();
    private readonly object _entityLock = new();

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

        var cluster = GetString(parameters, 0);
        if (string.IsNullOrWhiteSpace(cluster)) return;

        _state.SetCluster(cluster);
        lock (_entityLock) _entities.Clear();
        Log?.Invoke($"MAPA   {cluster}");
    }

    protected override void OnEvent(byte code, Dictionary<byte, object> parameters)
    {
        GameDataDetected?.Invoke();

        switch (code)
        {
            case NewCharacter:
                HandleNewCharacter(parameters);
                break;
            case HealthUpdate:
                HandleHealthUpdate(parameters);
                break;
            case HealthUpdates:
                HandleHealthUpdates(parameters);
                break;
            case PartyJoined:
                HandlePartyJoined(parameters);
                break;
            case PartyPlayerJoined:
                HandlePartyPlayerJoined(parameters);
                break;
            case PartyPlayerLeft:
                HandlePartyPlayerLeft(parameters);
                break;
            case PartyDisbanded:
                _partyByGuid.Clear();
                _state.ClearParty();
                EmitPartySnapshot();
                Log?.Invoke("PARTY  dissolvida");
                PartyChanged?.Invoke();
                break;
            case Died:
                HandleDied(parameters);
                break;
        }
    }

    private void HandleNewCharacter(Dictionary<byte, object> p)
    {
        var objectId = GetLong(p, 0);
        var name = GetString(p, 1);
        if (objectId <= 0 || string.IsNullOrWhiteSpace(name)) return;

        var guild = GetString(p, 8);
        lock (_entityLock) _entities[objectId] = new PlayerInfo(name, guild);
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

    private void HandlePartyJoined(Dictionary<byte, object> p)
    {
        var names = ToStrings(GetValue(p, 9));
        if (names.Count == 0) names = ToStrings(GetValue(p, 5));

        var guids = ToGuids(GetValue(p, 8));
        if (guids.Count == 0) guids = ToGuids(GetValue(p, 4));

        _partyByGuid.Clear();
        for (var i = 0; i < Math.Min(names.Count, guids.Count); i++)
            if (guids[i] != Guid.Empty && !string.IsNullOrWhiteSpace(names[i]))
                _partyByGuid[guids[i]] = names[i];

        _state.SetParty(names);
        EmitPartySnapshot();
        Log?.Invoke($"PARTY  snapshot · {names.Count} membros");
        PartyChanged?.Invoke();
    }

    private void HandlePartyPlayerJoined(Dictionary<byte, object> p)
    {
        var name = GetString(p, 2);
        var guid = ToGuid(GetValue(p, 1));
        if (string.IsNullOrWhiteSpace(name)) return;

        if (guid != Guid.Empty) _partyByGuid[guid] = name;
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
            .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

    private static List<Guid> ToGuids(object? value)
    {
        if (value is byte[] raw && raw.Length >= 16 && raw.Length % 16 == 0)
        {
            var result = new List<Guid>();
            for (var i = 0; i < raw.Length; i += 16) result.Add(new Guid(raw.AsSpan(i, 16)));
            return result;
        }

        return ToList(value).Select(ToGuid).Where(x => x != Guid.Empty).ToList();
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

    private sealed record PlayerInfo(string Name, string Guild);
}
