#nullable enable
using StatisticsAnalysisTool.Common.UserSettings;
using StatisticsAnalysisTool.Models.NetworkModel;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StatisticsAnalysisTool.Imortais.DeathReview;

/// <summary>
/// Opt-in local-only death review. No telemetry transmission and no writes on Photon threads.
/// </summary>
internal static class DeathCombatReviewCapture
{
    private static readonly DeathCombatTimeline Timeline = new(TimeSpan.FromSeconds(45), 2048);
    private static readonly SemaphoreSlim Writer = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static int _pending;
    private static string? _lastSavedPath;
    private static string? _lastError;

    // One-time notice must be acknowledged before collecting any pilot report.
    public static bool IsEnabled => SettingsController.CurrentSettings.IsImortaisDeathReviewEnabled
        && SettingsController.CurrentSettings.HasSeenImortaisDeathReviewCorePilotNotice;
    public static string? LastSavedPath => Volatile.Read(ref _lastSavedPath);
    public static string? LastError => Volatile.Read(ref _lastError);
    public static string Folder => Path.Combine(ImortaisTelemetryConfig.DirectoryPath, "Diagnostics", "DeathReview");

    public static void Reset()
    {
        Timeline.Reset();
        CombatSkillEvidenceCapture.Reset();
    }

    public static void ObserveLocalHealthChange(
        long localObjectId, long affectedObjectId, long causerId, string? causerName,
        double healthDelta, double? newHealth, int spellIndex, string effectType)
    {
        if (!IsEnabled || localObjectId <= 0 || affectedObjectId != localObjectId) return;
        if (!double.IsFinite(healthDelta)) return;
        var cleanHealth = newHealth.HasValue && double.IsFinite(newHealth.Value) ? newHealth : null;
        Timeline.Observe(new IncomingHealthChange(
            DateTime.UtcNow, affectedObjectId, causerId, causerName,
            healthDelta, cleanHealth, spellIndex, effectType ?? string.Empty));
    }

    public static void OnOwnLethalDeath(
        string victim, long victimObjectId, string killer, long killerObjectId,
        string? cluster, CharacterEquipment? equipment,
        Func<long, CharacterEquipment?>? enemyEquipment = null,
        Func<int, string?>? itemName = null)
    {
        if (!IsEnabled) return;
        var snapshot = Timeline.FreezeAtDeath(
            DateTime.UtcNow, victim, victimObjectId, killer, killerObjectId,
            cluster, SnapshotEquipment(equipment));
        if (snapshot is null) return;
        var skillEvidence = CombatSkillEvidenceCapture.FreezeAtDeath(
            snapshot.DeathUtc, snapshot.VictimObjectId,
            snapshot.IncomingHealthChanges.Select(x => x.CausingSpellIndex).ToArray());
        snapshot = snapshot with { SkillEvidence = skillEvidence };
        try
        {
            snapshot = snapshot with
            {
                EnemyCasts = EnemyCastSummary.Build(
                    snapshot.IncomingHealthChanges, snapshot.KillerObjectId,
                    index => GameFileData.SpellData.GetUniqueName(index),
                    unique => GameFileData.SpellData.GetLocalizationName(unique),
                    id => ToLoadout(enemyEquipment?.Invoke(id), itemName))
            };
        }
        catch (Exception ex) { Volatile.Write(ref _lastError, "Resumo de casts: " + ex.Message); }

        // Bound queued I/O. Never serialize or touch the filesystem inside packet handlers.
        if (Interlocked.Increment(ref _pending) > 2)
        {
            Interlocked.Decrement(ref _pending);
            Volatile.Write(ref _lastError, "Fila de relatórios ocupada; captura descartada.");
            return;
        }

        _ = Task.Run(async () =>
        {
            try { await PersistAsync(snapshot).ConfigureAwait(false); }
            catch (Exception ex) { Volatile.Write(ref _lastError, ex.Message); }
            finally { Interlocked.Decrement(ref _pending); }
        });
    }

    private static ObservedEquipment? SnapshotEquipment(CharacterEquipment? equipment)
    {
        if (equipment is null) return null;
        var spells = equipment.ActiveSpells?
            .Select(x => new ObservedSpellSelection(
                x.SlotType.ToString(), x.Value.ToString() ?? string.Empty,
                x.ItemIndex.ToString() ?? string.Empty))
            .ToArray() ?? [];
        return new ObservedEquipment(
            equipment.MainHand, equipment.OffHand, equipment.Head, equipment.Chest,
            equipment.Shoes, equipment.Cape, equipment.Potion, equipment.BuffFood, spells);
    }

    private static EnemyLoadout? ToLoadout(CharacterEquipment? equipment, Func<int, string?>? itemName)
    {
        if (equipment is null) return null;
        var spells = (equipment.ActiveSpells ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Value >= 0)
            .Select(x => new EnemySpellSlot(x.Key, x.Value)).ToArray();
        string? weapon = null;
        try { weapon = equipment.MainHand > 0 ? itemName?.Invoke(equipment.MainHand) : null; } catch { weapon = null; }
        return new EnemyLoadout(weapon, spells);
    }

    private static async Task PersistAsync(DeathCombatSnapshot snapshot)
    {
        await Writer.WaitAsync().ConfigureAwait(false);
        string? temp = null;
        try
        {
            Directory.CreateDirectory(Folder);
            var stem = $"death-review-{snapshot.DeathUtc:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
            var path = Path.Combine(Folder, stem + ".json");
            temp = path + ".tmp";
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await File.WriteAllTextAsync(temp, json).ConfigureAwait(false);
            File.Move(temp, path);
            temp = null;
            // Readable summary next to the JSON: who hit you, with which button and skill.
            try { await File.WriteAllTextAsync(Path.Combine(Folder, stem + ".txt"), EnemyCastSummary.Format(snapshot, snapshot.EnemyCasts ?? [])).ConfigureAwait(false); }
            catch { /* the JSON report is the source of truth */ }
            Volatile.Write(ref _lastSavedPath, path);
            Volatile.Write(ref _lastError, null);

            // Retain at most 30 local reports; no video or backend storage.
            foreach (var old in Directory.GetFiles(Folder, "death-review-*.json")
                         .OrderByDescending(File.GetLastWriteTimeUtc).Skip(30))
            {
                try { File.Delete(old); File.Delete(Path.ChangeExtension(old, ".txt")); } catch { /* retention retry on next save */ }
            }
        }
        finally
        {
            if (temp is not null) { try { File.Delete(temp); } catch { } }
            Writer.Release();
        }
    }
}
