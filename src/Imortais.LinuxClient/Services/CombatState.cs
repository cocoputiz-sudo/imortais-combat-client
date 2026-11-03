using System.Collections.Concurrent;
using Imortais.LinuxClient.Models;

namespace Imortais.LinuxClient.Services;

public sealed class CombatState
{
    private readonly ConcurrentDictionary<string, CombatPlayer> _players = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _partyLock = new();
    private readonly List<string> _party = [];

    public string CurrentCluster { get; private set; } = "Mapa desconhecido";

    public void SetCluster(string cluster) =>
        CurrentCluster = string.IsNullOrWhiteSpace(cluster) ? "Mapa desconhecido" : cluster.Trim();

    public void AddDamage(string player, long amount)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(player)) return;
        var row = _players.GetOrAdd(player.Trim(), x => new CombatPlayer { Name = x });
        Interlocked.Add(ref row.Damage, amount);
        Interlocked.Add(ref row.DamagePending, amount);
    }

    public void AddHealing(string player, long amount)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(player)) return;
        var row = _players.GetOrAdd(player.Trim(), x => new CombatPlayer { Name = x });
        Interlocked.Add(ref row.Healing, amount);
        Interlocked.Add(ref row.HealingPending, amount);
    }

    public void AddKill(string player)
    {
        if (string.IsNullOrWhiteSpace(player)) return;
        var row = _players.GetOrAdd(player.Trim(), x => new CombatPlayer { Name = x });
        Interlocked.Increment(ref row.Kills);
    }

    public void AddDeath(string player)
    {
        if (string.IsNullOrWhiteSpace(player)) return;
        var row = _players.GetOrAdd(player.Trim(), x => new CombatPlayer { Name = x });
        Interlocked.Increment(ref row.Deaths);
    }

    public IReadOnlyList<CombatPlayer> SnapshotPlayers() =>
        _players.Values
            .OrderByDescending(x => x.Damage)
            .ThenByDescending(x => x.Kills)
            .ThenByDescending(x => x.Healing)
            .Select(x => new CombatPlayer
            {
                Name = x.Name,
                Damage = Interlocked.Read(ref x.Damage),
                Healing = Interlocked.Read(ref x.Healing),
                Kills = Volatile.Read(ref x.Kills),
                Deaths = Volatile.Read(ref x.Deaths)
            })
            .ToList();

    public IReadOnlyList<(string Player, long Damage, long Healing)> DrainPending()
    {
        var result = new List<(string, long, long)>();
        foreach (var row in _players.Values)
        {
            var damage = Interlocked.Exchange(ref row.DamagePending, 0);
            var healing = Interlocked.Exchange(ref row.HealingPending, 0);
            if (damage > 0 || healing > 0) result.Add((row.Name, damage, healing));
        }
        return result;
    }

    public void SetParty(IEnumerable<string> names)
    {
        lock (_partyLock)
        {
            _party.Clear();
            _party.AddRange(names.Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
        }
    }

    public void AddPartyMember(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        lock (_partyLock)
        {
            if (!_party.Contains(name, StringComparer.OrdinalIgnoreCase)) _party.Add(name.Trim());
        }
    }

    public void RemovePartyMember(string name)
    {
        lock (_partyLock)
        {
            _party.RemoveAll(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void ClearParty() { lock (_partyLock) _party.Clear(); }

    public IReadOnlyList<string> SnapshotParty()
    {
        lock (_partyLock) return _party.ToList();
    }

    public bool IsPartyMember(string name)
    {
        lock (_partyLock) return _party.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    public void ResetCombat() => _players.Clear();
}
