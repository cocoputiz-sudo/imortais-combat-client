using StatisticsAnalysisTool.Imortais.DeathReview;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static IncomingHealthChange Hit(DateTime time, long victim, long causer, int index, double delta)
    => new(time, victim, causer, "Enemy", delta, 1200 + delta, index, "Unknown");

var now = new DateTime(2026, 10, 13, 22, 0, 0, DateTimeKind.Utc);
var timeline = new DeathCombatTimeline(TimeSpan.FromSeconds(45), 3);
timeline.Observe(Hit(now.AddSeconds(-50), 1, 10, 2, -100));
timeline.Observe(Hit(now.AddSeconds(-30), 1, 11, 3, -200));
timeline.Observe(Hit(now.AddSeconds(-12), 1, 12, 4, -300));
timeline.Observe(Hit(now.AddSeconds(-3), 1, 13, 5, -400));
timeline.Observe(Hit(now.AddSeconds(-1), 1, 14, 6, -500));
var snapshot = timeline.FreezeAtDeath(now, "BadMack", 1, "Enemy", 14, "TestZone", null);
Check(snapshot != null, "local death should produce a snapshot");
Check(snapshot.IncomingHealthChanges.Count == 3, "buffer must cap events");
Check(snapshot.IncomingHealthChanges[0].CausingSpellIndex == 4, "oldest events should be evicted");
Check(snapshot.IncomingHealthChanges[2].SourceObjectId == 14, "causer ID should be preserved");
Check(!snapshot.CooldownsVerified && !snapshot.SkillNamesResolved && !snapshot.CompleteCombatHistory,
    "incomplete evidence must never claim verified skills/cooldowns");
Check(timeline.FreezeAtDeath(now.AddSeconds(2), "BadMack", 1, "Enemy", 14, null, null) == null,
    "repeated death packet should not create duplicate report");
Console.WriteLine("OK: bounded timeline, spell index, incomplete evidence, duplicate death");

timeline.Observe(Hit(now.AddSeconds(8), 1, 15, 7, -100));
var later = timeline.FreezeAtDeath(now.AddSeconds(12), "BadMack", 1, "Enemy", 15, null, null);
Check(later != null && later.IncomingHealthChanges.Count == 1,
    "new death must contain only events after prior death");
Console.WriteLine("OK: post-death reset and later death");

var swapped = new DeathCombatTimeline(TimeSpan.FromSeconds(45), 8);
swapped.Observe(Hit(now.AddSeconds(-1), 1, 11, 10, -120));
swapped.Observe(Hit(now, 2, 12, 11, -130));
var other = swapped.FreezeAtDeath(now, "SecondCharacter", 2, "Enemy", 12, null, null);
Check(other != null && other.IncomingHealthChanges.Count == 1 && other.IncomingHealthChanges[0].VictimObjectId == 2,
    "events from another local object must not leak into the new snapshot");
Console.WriteLine("OK: changed character identity");

var window = new DeathCombatTimeline(TimeSpan.FromSeconds(45), 100);
window.Observe(Hit(now.AddSeconds(-46), 3, 12, 1, -100));
window.Observe(Hit(now.AddSeconds(-44), 3, 12, 1, -100));
var filtered = window.FreezeAtDeath(now, "Third", 3, "Enemy", 12, null, null);
Check(filtered != null && filtered.IncomingHealthChanges.Count == 1, "time window should be 45 seconds");
Console.WriteLine("OK: 45-second window");

var invalid = new DeathCombatTimeline(TimeSpan.FromSeconds(45), 100);
invalid.Observe(Hit(now, 9, 1, 1, double.NaN));
invalid.Observe(Hit(now, 0, 1, 1, -200));
Check(invalid.FreezeAtDeath(now, "Unknown", 9, "Enemy", 1, null, null)?.IncomingHealthChanges.Count == 0,
    "invalid numeric or zero victim data must be ignored");
Console.WriteLine("OK: invalid numbers and unknown identity");
Console.WriteLine("Death Combat Timeline logic tests passed.");
