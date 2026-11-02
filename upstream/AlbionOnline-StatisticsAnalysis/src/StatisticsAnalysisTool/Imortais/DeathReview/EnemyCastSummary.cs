#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace StatisticsAnalysisTool.Imortais.DeathReview;

// Which button an enemy pressed: from the slot order the game sends in the equipment packet
// (Q, W, E, chest, helmet, boots, potion, food). Null when that packet was not seen.
internal sealed record EnemySpellSlot(string Key, int SpellIndex);

internal sealed record EnemyLoadout(string? Weapon, IReadOnlyList<EnemySpellSlot> Spells);

internal sealed record EnemyCastLine(
    int SpellIndex, string SpellName, string Key, string KeySource,
    int Hits, double Damage, double Healing, DateTime FirstUtc, DateTime LastUtc);

internal sealed record EnemyCastSource(
    long SourceObjectId, string Name, string? Weapon, double Damage, int Hits,
    bool IsKiller, IReadOnlyList<EnemyCastLine> Casts);

/// <summary>
/// Pure, testable summary of what hit the player before dying. No game, file or network access:
/// the caller passes resolvers for spell names and enemy loadouts.
/// </summary>
internal static class EnemyCastSummary
{
    public const string KeyUnknown = "?";

    public static IReadOnlyList<EnemyCastSource> Build(
        IReadOnlyList<IncomingHealthChange> changes,
        long killerObjectId,
        Func<int, string> spellUniqueName,
        Func<string, string> spellDisplayName,
        Func<long, EnemyLoadout?> loadout)
    {
        var result = new List<EnemyCastSource>();
        foreach (var group in changes.Where(c => c.SourceObjectId > 0 && c.SourceObjectId != c.VictimObjectId)
                     .GroupBy(c => c.SourceObjectId))
        {
            var gear = SafeLoadout(loadout, group.Key);
            var lines = group.GroupBy(c => c.CausingSpellIndex).Select(spell =>
            {
                var unique = SafeName(spellUniqueName, spell.Key);
                var (key, source) = ResolveKey(spell.Key, unique, gear);
                return new EnemyCastLine(
                    spell.Key,
                    string.IsNullOrWhiteSpace(unique) ? $"habilidade #{spell.Key}" : SafeDisplay(spellDisplayName, unique),
                    key, source,
                    spell.Count(),
                    spell.Where(x => x.HealthDelta < 0).Sum(x => -x.HealthDelta),
                    spell.Where(x => x.HealthDelta > 0).Sum(x => x.HealthDelta),
                    spell.Min(x => x.ObservedUtc), spell.Max(x => x.ObservedUtc));
            }).OrderByDescending(l => l.Damage).ThenBy(l => l.FirstUtc).ToArray();
            var name = group.Select(x => x.SourceName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? $"objeto {group.Key}";
            result.Add(new EnemyCastSource(group.Key, name, gear?.Weapon,
                lines.Sum(l => l.Damage), lines.Sum(l => l.Hits), group.Key == killerObjectId, lines));
        }
        return result.OrderByDescending(s => s.IsKiller).ThenByDescending(s => s.Damage).ToArray();
    }

    internal static (string Key, string Source) ResolveKey(int spellIndex, string? uniqueName, EnemyLoadout? gear)
    {
        var slot = gear?.Spells.FirstOrDefault(s => s.SpellIndex == spellIndex);
        if (slot is not null) return (slot.Key, "equipamento");
        var u = (uniqueName ?? string.Empty).ToUpperInvariant();
        if (u.Contains("AUTO_ATTACK") || u.Contains("AUTOATTACK")) return ("Ataque básico", "nome");
        if (u.Contains("POTION")) return ("Poção", "nome");
        if (u.Contains("FOOD") || u.Contains("MEAL")) return ("Comida", "nome");
        if (u.Contains("PASSIVE")) return ("Passiva", "nome");
        return (KeyUnknown, "não identificado");
    }

    public static string Format(DeathCombatSnapshot snapshot, IReadOnlyList<EnemyCastSource> sources)
    {
        var br = CultureInfo.GetCultureInfo("pt-BR");
        var local = snapshot.DeathUtc.ToLocalTime();
        var sb = new StringBuilder();
        sb.AppendLine($"Morte de {snapshot.Victim} em {snapshot.Cluster ?? "mapa desconhecido"} às {local:HH:mm:ss} ({local:dd/MM/yyyy})");
        sb.AppendLine($"Abatido por: {(string.IsNullOrWhiteSpace(snapshot.Killer) ? "desconhecido" : snapshot.Killer)}");
        sb.AppendLine($"Janela analisada: últimos {snapshot.WindowSeconds} s · {snapshot.IncomingHealthChanges.Count} registros de vida observados (pode faltar o golpe final).");
        sb.AppendLine();
        if (sources.Count == 0) sb.AppendLine("Nenhum golpe inimigo observado nesta janela.");
        foreach (var s in sources)
        {
            sb.AppendLine($"{(s.IsKiller ? "★ " : "")}{s.Name}{(string.IsNullOrWhiteSpace(s.Weapon) ? "" : " · " + s.Weapon)} — {s.Damage.ToString("N0", br)} de dano em {s.Hits} golpe(s)");
            foreach (var c in s.Casts)
            {
                var key = c.Key == KeyUnknown ? "tecla ?" : c.Key;
                var amount = c.Damage > 0 ? $"{c.Damage.ToString("N0", br)} de dano" : c.Healing > 0 ? $"{c.Healing.ToString("N0", br)} de cura" : "sem dano";
                sb.AppendLine($"   [{key}] {c.SpellName}: {c.Hits}x, {amount}, {Seconds(snapshot.DeathUtc, c.FirstUtc)} a {Seconds(snapshot.DeathUtc, c.LastUtc)} antes da morte");
            }
        }
        sb.AppendLine();
        sb.AppendLine("Teclas: Q, W, E = arma · R = peito · D = capacete · F = botas. \"tecla ?\" = o Client não viu o equipamento daquele jogador.");
        return sb.ToString();
    }

    private static string Seconds(DateTime death, DateTime at)
        => Math.Max(0, (death - at).TotalSeconds).ToString("0.0", CultureInfo.GetCultureInfo("pt-BR")) + " s";

    private static EnemyLoadout? SafeLoadout(Func<long, EnemyLoadout?> f, long id) { try { return f(id); } catch { return null; } }
    private static string SafeName(Func<int, string> f, int i) { try { return f(i) ?? string.Empty; } catch { return string.Empty; } }
    private static string SafeDisplay(Func<string, string> f, string u) { try { var n = f(u); return string.IsNullOrWhiteSpace(n) ? u : n; } catch { return u; } }
}
