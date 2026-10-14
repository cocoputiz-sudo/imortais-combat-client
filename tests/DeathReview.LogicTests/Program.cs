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


// Unified PR #42+#43: skill selection and cast-observation evidence logic.
var skills = new SkillEvidenceCollector(TimeSpan.FromSeconds(45), 2);
var skillSlots = new int[14] { 101, -1, 303, 404, 505, 606, -1, -1, -1, -1, -1, -1, 1202, -1 };
skills.ObserveOwnSelection(88, skillSlots, now.AddSeconds(-20));
skills.ObserveCastShape(15, "CastStart", now.AddSeconds(-50),
    new Dictionary<string,string> { ["0"] = "number", ["1"] = "byte[16]" });
skills.ObserveCastShape(15, "CastStart", now.AddSeconds(-8),
    new Dictionary<string,string> { ["0"] = "number", ["1"] = "byte[16]" });
skills.ObserveCastShape(20, "CastSpell", now.AddSeconds(-2),
    new Dictionary<string,string> { ["0"] = "number" });
skills.ObserveCastShape(21, "CastHit", now.AddSeconds(-1),
    new Dictionary<string,string> { ["0"] = "number" });
var review = skills.Freeze(now, 88, new[] { 9, 9, 10 },
    index => index == 9 ? "SPELL_CATALOG_CANDIDATE" : "");
Check(review.OwnSelectedSkills.Count == 7, "all seven Q/W/E/armor/helmet/boots/potion slots must exist");
Check(review.OwnSelectedSkills[0].Slot == "Q" && review.OwnSelectedSkills[0].SelectedSpellIndex == 101,
    "Q must preserve slot position");
Check(review.OwnSelectedSkills[1].Slot == "W" && review.OwnSelectedSkills[1].SelectedSpellIndex == null,
    "W -1 must remain unknown instead of shifting E");
Check(review.OwnSelectedSkills[2].Slot == "E" && review.OwnSelectedSkills[2].SelectedSpellIndex == 303,
    "E must remain mapped to index 2");
Check(review.OwnSelectedSkills[3].SelectedSpellIndex == 404, "armor skill must match position 3");
Check(review.OwnSelectedSkills[4].SelectedSpellIndex == 505, "helmet skill must match position 4");
Check(review.OwnSelectedSkills[5].SelectedSpellIndex == 606, "boots skill must match position 5");
Check(review.OwnSelectedSkills[6].SelectedSpellIndex == 1202, "potion must match position 12");
Check(review.OwnSelectedSkills.All(x => x.Availability == "indeterminado"),
    "no slot may be reported available without verified cooldown evidence");
Check(review.CastPacketShapes.Count == 2 && review.CastSamplesTruncated,
    "old captures must expire and overflow must be explicit");
Check(review.CastAttributionStatus.StartsWith("indeterminado"),
    "metadata-only cast shapes must never claim an enemy was identified");
Check(review.DamageSpellCatalogCandidates.Count == 2
      && review.DamageSpellCatalogCandidates[0].Status == "candidato_catalogo_nao_validado"
      && review.DamageSpellCatalogCandidates[1].Status == "indice_sem_correspondencia",
    "game file candidate resolution must never claim validation");
Console.WriteLine("OK: cast shape cap, Q/W/E and defensive slots, no fictional cooldown");

var afterDeath = skills.Freeze(now.AddSeconds(10), 88, Array.Empty<int>(), _ => "");
Check(afterDeath.OwnSelectedSkills.All(x => x.SelectedSpellIndex is null)
      && afterDeath.CastPacketShapes.Count == 0,
    "new life must not inherit stale equipment selections or prior cast samples");
Console.WriteLine("OK: clear evidence after death");

skills.ObserveOwnSelection(88, skillSlots, now.AddSeconds(15));
var switchedActor = skills.Freeze(now.AddSeconds(20), 89, Array.Empty<int>(), _ => "");
Check(switchedActor.OwnSelectedSkills.All(x => x.SelectedSpellIndex is null),
    "local selected skills must not leak to a second object id");
Console.WriteLine("OK: character object identity check");

skills.Reset();
Console.WriteLine("Skill Evidence combined feasibility logic tests passed.");
