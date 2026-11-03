using System.Collections;
using Imortais.LinuxClient.Services;
using StatisticsAnalysisTool.Imortais;
using StatisticsAnalysisTool.Network;
using StatisticsAnalysisTool.PhotonPackageParser;

namespace Imortais.LinuxClient.Network;

/// <summary>
/// Reads Albion's Photon traffic on Linux and feeds the SAME telemetry core as the Windows
/// client (ImortaisEventBridge). Parameter keys and rules follow the Windows parsers and
/// handlers 1:1; event and operation codes come from the Windows enums (linked source), so a
/// game patch fixed for Windows is fixed here too.
/// </summary>
public sealed class GameReceiver : PhotonParser
{
    private readonly CombatState _meter;
    private readonly object _gate = new();
    private readonly Dictionary<long, Player> _players = new();
    private readonly Dictionary<long, int> _mobs = new();
    private readonly Dictionary<Guid, string> _party = new();
    private long _localObjectId;
    private string _localName = string.Empty;
    private string _localGuild = string.Empty;
    private string _clusterName = string.Empty;

    public LinuxPhotonDiagnostics Diagnostics { get; } = new();
    public event Action<string>? Log;
    public event Action? Changed;

    public GameReceiver(CombatState meter) => _meter = meter;

    // Contract tests feed recorded/synthetic Photon parameters through the real dispatch.
    internal void InjectEvent(Dictionary<byte, object> parameters) => OnEvent(0, parameters);
    internal void InjectResponse(Dictionary<byte, object> parameters, short returnCode = 0) => OnResponse(0, returnCode, string.Empty, parameters);

    public string LocalName { get { lock (_gate) return _localName; } }
    public string LocalGuild { get { lock (_gate) return _localGuild; } }
    public string ClusterName { get { lock (_gate) return _clusterName; } }
    public IReadOnlyList<string> Party { get { lock (_gate) return _party.Values.ToArray(); } }
    public int NearbyPlayers { get { lock (_gate) return _players.Count; } }

    protected override void OnRequest(byte operationCode, Dictionary<byte, object> parameters)
        => ImortaisEventBridge.SetGameDetected(true);

    protected override void OnResponse(byte operationCodeByte, short returnCode, string debugMessage, Dictionary<byte, object> parameters)
    {
        ImortaisEventBridge.SetGameDetected(true);
        var code = PhotonCode(parameters, 253);
        if (code == (int)OperationCodes.Join) OnJoin(parameters);
        else if (code == (int)OperationCodes.ChangeCluster && returnCode == 0) OnChangeCluster(parameters);
    }

    protected override void OnEvent(byte code, Dictionary<byte, object> parameters)
    {
        ImortaisEventBridge.SetGameDetected(true);
        var eventCode = PhotonCode(parameters, 252);
        if (eventCode < 0) return;
        Diagnostics.Observe(eventCode, parameters);
        try
        {
            switch ((EventCodes)eventCode)
            {
                case EventCodes.Leave: OnLeave(parameters); break;
                case EventCodes.NewCharacter: OnNewCharacter(parameters); break;
                case EventCodes.NewMob: OnNewMob(parameters); break;
                case EventCodes.HealthUpdate: OnHealthUpdate(parameters); break;
                case EventCodes.HealthUpdates: OnHealthUpdates(parameters); break;
                case EventCodes.Died: OnDied(parameters); break;
                case EventCodes.PartyJoined: OnPartyJoined(parameters); break;
                case EventCodes.PartyPlayerJoined: OnPartyPlayerJoined(parameters); break;
                case EventCodes.PartyPlayerLeft: OnPartyPlayerLeft(parameters); break;
                case EventCodes.PartyDisbanded: OnPartyDisbanded(); break;
            }
        }
        catch (Exception error)
        {
            // One malformed packet must never stop the capture.
            Log?.Invoke("PARSER  evento " + eventCode + " ignorado: " + error.GetType().Name);
        }
    }

    // ---- login and map ------------------------------------------------------------------

    private void OnJoin(Dictionary<byte, object> p)
    {
        var name = GetString(p, 2);
        var guild = GetString(p, 58);
        var mapIndex = GetString(p, 8);
        lock (_gate)
        {
            _localObjectId = GetLong(p, 0);
            if (!string.IsNullOrWhiteSpace(name)) _localName = name;
            _localGuild = guild;
            _players.Clear();
            _mobs.Clear();
        }
        // Windows JoinResponseHandler: the guild of the logged character chooses the War Room.
        ImortaisEventBridge.SetGameGuild(guild);
        Log?.Invoke($"LOGIN   {name}{(string.IsNullOrWhiteSpace(guild) ? " · sem guilda" : " · " + guild)}");
        if (!string.IsNullOrWhiteSpace(mapIndex)) EnterCluster(mapIndex);
        Changed?.Invoke();
    }

    private void OnChangeCluster(Dictionary<byte, object> p)
    {
        var index = GetString(p, 0);
        if (string.IsNullOrWhiteSpace(index)) return;
        lock (_gate) { _players.Clear(); _mobs.Clear(); }
        EnterCluster(index);
        Changed?.Invoke();
    }

    private void EnterCluster(string index)
    {
        var name = AlbionWorldMapNames.Resolve(index).Trim();
        if (string.IsNullOrWhiteSpace(name)) name = index;
        lock (_gate) _clusterName = name;
        _meter.SetCluster(name);
        ImortaisEventBridge.ZoneChange(index, name, null, null, null);
        Log?.Invoke($"MAPA    {name} ({index})");
    }

    // ---- players and mobs nearby --------------------------------------------------------

    private void OnNewCharacter(Dictionary<byte, object> p)
    {
        var objectId = GetLong(p, 0);
        var name = GetString(p, 1);
        if (objectId <= 0 || string.IsNullOrWhiteSpace(name)) return;
        var guid = ToGuid(Get(p, 7));
        var guild = GetString(p, 8);
        var alliance = GetString(p, 51);
        lock (_gate) _players[objectId] = new Player(name, guild, alliance);
        ImortaisEventBridge.NearbyPlayerObserved(objectId, guid == Guid.Empty ? null : guid, name,
            NullIfEmpty(guild), NullIfEmpty(alliance), ClusterName);
    }

    private void OnNewMob(Dictionary<byte, object> p)
    {
        var objectId = GetLong(p, 0);
        var mobIndex = (int)GetLong(p, 1);
        if (objectId > 0 && mobIndex > 0) lock (_gate) _mobs[objectId] = mobIndex;
    }

    private void OnLeave(Dictionary<byte, object> p)
    {
        var objectId = GetLong(p, 0);
        if (objectId <= 0) return;
        bool wasPlayer;
        lock (_gate) { wasPlayer = _players.Remove(objectId); _mobs.Remove(objectId); }
        if (wasPlayer) ImortaisEventBridge.NearbyPlayerLeft(objectId);
    }

    // ---- damage and healing -------------------------------------------------------------

    private void OnHealthUpdate(Dictionary<byte, object> p)
        => ApplyHealth(GetLong(p, 0), GetLong(p, 6), GetDouble(p, 2));

    private void OnHealthUpdates(Dictionary<byte, object> p)
    {
        var affected = GetLong(p, 0);
        var changes = ToList(Get(p, 2));
        var causers = ToList(Get(p, 6));
        var count = Math.Min(changes.Count, causers.Count);
        for (var i = 0; i < count; i++) ApplyHealth(affected, ToLong(causers[i]), ToDouble(changes[i]));
    }

    // Same rules as the Windows CombatController: only a PLAYER causer that is in the party
    // (the local player always is), no self damage, classification before any filter.
    internal void ApplyHealth(long affectedId, long causerId, double healthChange)
    {
        if (causerId <= 0 || !double.IsFinite(healthChange)) return;
        var amount = (int)Math.Round(Math.Abs(healthChange), MidpointRounding.AwayFromZero);
        if (amount <= 0) return;
        var isDamage = healthChange < 0;
        if (isDamage && affectedId == causerId) return;

        string causerName, kind = "unknown";
        lock (_gate)
        {
            var isLocal = causerId == _localObjectId && _localObjectId > 0;
            if (isLocal) causerName = _localName;
            else if (_players.TryGetValue(causerId, out var causer) && _party.ContainsValue(causer.Name)) causerName = causer.Name;
            else return;
            if (isDamage)
                kind = _players.ContainsKey(affectedId) || (affectedId == _localObjectId && _localObjectId > 0) ? "player"
                    : _mobs.TryGetValue(affectedId, out var mobIndex) && mobIndex > 0 ? "mob" : "unknown";
        }
        if (string.IsNullOrWhiteSpace(causerName)) return;

        var cluster = ClusterName;
        if (isDamage)
        {
            ImortaisEventBridge.ClassifiedDamage(causerName, amount, kind, cluster);
            ImortaisEventBridge.Damage(causerName, amount, cluster);
            _meter.AddDamage(causerName, amount);
        }
        else
        {
            ImortaisEventBridge.Healing(causerName, amount, cluster);
            _meter.AddHealing(causerName, amount);
        }
        Changed?.Invoke();
    }

    // ---- deaths and kills ---------------------------------------------------------------

    private void OnDied(Dictionary<byte, object> p)
    {
        var victimId = GetLong(p, 1);
        var victim = GetString(p, 2);
        var victimGuild = GetString(p, 3);
        var killerId = GetLong(p, 9);
        var killer = GetString(p, 10);
        var killerGuild = GetString(p, 11);
        var lethal = GetBool(p, 17);
        if (string.IsNullOrWhiteSpace(victim)) return;

        string local;
        bool relevant;
        string? victimAlliance, killerAlliance;
        lock (_gate)
        {
            local = _localName;
            relevant = ImortaisEventBridge.IsOwnGuild(victimGuild) || ImortaisEventBridge.IsOwnGuild(killerGuild)
                || _party.ContainsValue(victim) || _party.ContainsValue(killer)
                || Same(victim, local) || Same(killer, local);
            victimAlliance = _players.TryGetValue(victimId, out var v) ? NullIfEmpty(v.Alliance) : null;
            killerAlliance = _players.TryGetValue(killerId, out var k) ? NullIfEmpty(k.Alliance) : null;
        }

        // Windows StatisticController: personal result of the logged character.
        var cluster = ClusterName;
        if (Same(victim, local)) ImortaisEventBridge.CombatResult("Death", victim, killer, lethal, cluster);
        else if (Same(killer, local) && lethal) ImortaisEventBridge.CombatResult("Kill", victim, killer, lethal, cluster);

        if (!lethal || !relevant) return;
        // Equipment names need the game's item catalogue (not on Linux yet): Kill Fame then
        // comes from the official API, as for any death without equipment.
        ImortaisEventBridge.ObservePlayerDeath(victimId, victim, NullIfEmpty(victimGuild), killerId, killer,
            NullIfEmpty(killerGuild), true, cluster, null, null, victimAlliance, killerAlliance);
        if (ImortaisEventBridge.IsOwnGuild(killerGuild) || _party.ContainsValue(killer) || Same(killer, local)) _meter.AddKill(killer);
        if (ImortaisEventBridge.IsOwnGuild(victimGuild) || _party.ContainsValue(victim) || Same(victim, local)) _meter.AddDeath(victim);
        Log?.Invoke($"MORTE   {victim} ← {killer}");
        Changed?.Invoke();
    }

    // ---- party --------------------------------------------------------------------------

    private void OnPartyJoined(Dictionary<byte, object> p)
    {
        var ok = TryDecodePartyRoster(p, out var roster);
        ImortaisEventBridge.PartyJoinedObservation(ok, roster.Count, ok ? "linux-roster" : "linux-roster-unreadable");
        if (!ok) { Log?.Invoke("PARTY   lista ilegível ignorada"); return; }
        lock (_gate) { _party.Clear(); foreach (var m in roster) _party[m.Key] = m.Value; }
        PublishParty($"PARTY   {roster.Count} membros");
    }

    private void OnPartyPlayerJoined(Dictionary<byte, object> p)
    {
        var guid = ToGuid(Get(p, 1));
        var name = GetString(p, 2);
        if (guid == Guid.Empty || string.IsNullOrWhiteSpace(name)) return;
        lock (_gate) _party[guid] = name;
        PublishParty("PARTY   + " + name);
    }

    private void OnPartyPlayerLeft(Dictionary<byte, object> p)
    {
        var guid = ToGuid(Get(p, 1));
        string? name;
        lock (_gate) { if (!_party.Remove(guid, out name)) return; }
        PublishParty("PARTY   - " + name);
    }

    private void OnPartyDisbanded()
    {
        lock (_gate) _party.Clear();
        PublishParty("PARTY   desfeita");
    }

    private void PublishParty(string log)
    {
        var names = Party;
        _meter.SetParty(names);
        ImortaisEventBridge.PartySnapshot(names);
        Log?.Invoke(log);
        Changed?.Invoke();
    }

    // A partial or truncated roster never replaces a complete one.
    public static bool TryDecodePartyRoster(Dictionary<byte, object> p, out Dictionary<Guid, string> roster)
    {
        roster = new Dictionary<Guid, string>();
        foreach (var (guidsKey, namesKey) in new (byte, byte)[] { (8, 9), (4, 5) }) // same order as Windows PartyJoinedEvent (Dragonfire: 8/9 first)
        {
            var rawNames = Get(p, namesKey);
            var rawGuids = Get(p, guidsKey);
            if (rawNames is null || rawGuids is null) continue;
            var names = ToList(rawNames).Select(x => Convert.ToString(x)?.Trim() ?? string.Empty).ToList();
            var guids = ToGuids(rawGuids);
            if (names.Count == 0 || names.Count != guids.Count || names.Any(string.IsNullOrWhiteSpace) || guids.Any(g => g == Guid.Empty)
                || guids.Distinct().Count() != guids.Count || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
                continue;
            for (var i = 0; i < names.Count; i++) roster[guids[i]] = names[i];
            return true;
        }
        return false;
    }

    // ---- helpers ------------------------------------------------------------------------

    // Same as the Windows AlbionParser: the real code is in parameter 252 (events) / 253 (operations).
    internal static int PhotonCode(Dictionary<byte, object> p, byte key)
    {
        if (!p.TryGetValue(key, out var value) || value is null) return -1;
        try { return checked((short)Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)); }
        catch { return -1; }
    }

    private static bool Same(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static string? NullIfEmpty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static object? Get(Dictionary<byte, object> p, byte key) => p.TryGetValue(key, out var v) ? v : null;
    private static string GetString(Dictionary<byte, object> p, byte key) => Convert.ToString(Get(p, key))?.Trim() ?? string.Empty;
    private static long GetLong(Dictionary<byte, object> p, byte key) => ToLong(Get(p, key));
    private static double GetDouble(Dictionary<byte, object> p, byte key) => ToDouble(Get(p, key));
    private static bool GetBool(Dictionary<byte, object> p, byte key)
    {
        var v = Get(p, key);
        try { return v is not null && Convert.ToBoolean(v); } catch { return false; }
    }
    private static long ToLong(object? v) { try { return v is null ? 0 : Convert.ToInt64(v); } catch { return 0; } }
    private static double ToDouble(object? v) { try { return v is null ? 0 : Convert.ToDouble(v); } catch { return 0; } }
    private static List<object?> ToList(object? value)
    {
        if (value is null) return [];
        if (value is string or byte[]) return [value];
        if (value is IEnumerable seq) { var list = new List<object?>(); foreach (var x in seq) list.Add(x); return list; }
        return [value];
    }
    private static List<Guid> ToGuids(object? value)
    {
        if (value is byte[] raw && raw.Length >= 16 && raw.Length % 16 == 0)
        {
            var list = new List<Guid>();
            for (var i = 0; i < raw.Length; i += 16) list.Add(new Guid(raw.AsSpan(i, 16)));
            return list;
        }
        return ToList(value).Select(ToGuid).ToList();
    }
    private static Guid ToGuid(object? value)
    {
        try
        {
            if (value is Guid g) return g;
            if (value is byte[] b && b.Length == 16) return new Guid(b);
            if (value is IEnumerable seq && value is not string)
            {
                var bytes = new List<byte>();
                foreach (var x in seq) bytes.Add(unchecked((byte)Convert.ToSByte(x)));
                if (bytes.Count == 16) return new Guid(bytes.ToArray());
            }
            return Guid.TryParse(Convert.ToString(value), out var parsed) ? parsed : Guid.Empty;
        }
        catch { return Guid.Empty; }
    }

    private sealed record Player(string Name, string Guild, string Alliance);
}
