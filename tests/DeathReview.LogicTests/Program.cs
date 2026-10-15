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


// PR #50: Forensic v2 regression grounded in the two real 14/10 reports,
// but simulated without real player identifiers or any packet contents.
var zvzDeath = new DateTime(2026, 10, 14, 18, 52, 24, DateTimeKind.Utc).AddMilliseconds(272);
var zvz = new DeathCombatTimeline(TimeSpan.FromSeconds(45), 2048);
zvz.Observe(new IncomingHealthChange(zvzDeath.AddMilliseconds(-104),
    42299, 44556, "ObservedSource", -337, 165, 2905, "Magic"));
var zvzSnapshot = zvz.FreezeAtDeath(zvzDeath, "LocalCharacter", 42299, "KillerCredit", 40687, "Test", null);
Check(zvzSnapshot is not null && zvzSnapshot.SchemaVersion == 3 && zvzSnapshot.TerminalEvidence is not null,
    "forensic v3 must be explicit, additive and serializable");
Check(zvzSnapshot.TerminalEvidence.TerminalHealthObservationStatus == "positive_health_before_death"
      && zvzSnapshot.TerminalEvidence.LastObservedHealth == 165,
    "positive observed HP before Died must not be converted to an invented zero-HP hit");
Check(zvzSnapshot.TerminalEvidence.KillerHadObservedNegativeDelta == false
      && zvzSnapshot.TerminalEvidence.FinalBlowAttributionStatus.Contains("unverified") == false,
    "killer credit must not falsely attribute the observed negative delta to another player");

var soloDeath = new DateTime(2026, 10, 14, 9, 40, 19, DateTimeKind.Utc).AddMilliseconds(196);
var solo = new DeathCombatTimeline(TimeSpan.FromSeconds(45), 100);
solo.Observe(new IncomingHealthChange(soloDeath.AddMilliseconds(-1500), 4481, 4372,
    "OtherSource", -77, 38, 3227, "Magic"));
solo.Observe(new IncomingHealthChange(soloDeath.AddMilliseconds(-532), 4481, 3191,
    null, -27, 11, 8289, "Physical"));
var soloSnapshot = solo.FreezeAtDeath(soloDeath, "LocalCharacter", 4481,
    "KillerCredit", 4372, "Test", null);
Check(soloSnapshot?.TerminalEvidence?.LastObservedHealth == 11 &&
      soloSnapshot.TerminalEvidence.KillerHadObservedNegativeDelta == true &&
      soloSnapshot.TerminalEvidence.FinalBlowAttributionStatus.Contains("lethal_hit_unverified"),
    "killer damage candidate is evidence, never a verified final blow");

var burst = new SkillEvidenceCollector(TimeSpan.FromSeconds(45), 256);
var castStart = new DateTime(2026, 10, 14, 18, 51, 40, DateTimeKind.Utc);
for (var second = 0; second < 45; second++)
{
    for (var sample = 0; sample < 800; sample++)
    {
        var at = castStart.AddSeconds(second).AddMilliseconds(sample / 4d);
        burst.ObserveCastShape((short)(sample % 7 + 16),
            sample % 7 == 0 ? "CastSpell" : "CastHit", at,
            new Dictionary<string, string> { ["0"] = "number", ["252"] = "number" });
    }
}
var forensicSkills = burst.Freeze(zvzDeath, 42299, Array.Empty<int>(), _ => null);
Check(forensicSkills.Schema == "experimental-post-death-v2",
    "forensic cast format must distinguish v1 capped FIFO from v2 sparse histogram");
Check(forensicSkills.CastPacketShapes.Count <= 256
      && forensicSkills.CastCoverageBySecond is not null
      && forensicSkills.CastCoverageBySecond.Count >= 44,
    "dense last seconds must not evict 45s of history");
Check(forensicSkills.TotalCastPacketsObserved == 45 * 800
      && forensicSkills.TotalCastPacketsOmitted > 0
      && forensicSkills.CastSamplesTruncated,
    "v2 must report full cast volume and explicitly disclose omitted samples");
Check(forensicSkills.CastPacketShapes.Min(x => x.ReceivedUtc) <= zvzDeath.AddSeconds(-43)
      && forensicSkills.CastPacketShapes.Max(x => x.ReceivedUtc) >= zvzDeath.AddSeconds(-1),
    "sparse samples should span both the start and end of a 45-second ZvZ");
Check(forensicSkills.CastCoverageBySecond.All(x => x.ObservedPackets == 800
      && x.RetainedSamples <= 6 && x.OmittedSamples > 0),
    "per-second bounded counters must retain observable pressure information");

var nextLife = burst.Freeze(zvzDeath.AddSeconds(20), 42299, Array.Empty<int>(), _ => null);
Check(nextLife.TotalCastPacketsObserved == 0
      && (nextLife.CastCoverageBySecond?.Count ?? 0) == 0,
    "a new life may not inherit forensic casts from a previous death");
Console.WriteLine("PASS: 45-second sparse casts under 36,000 packet ZvZ burst and truthful HP/last-hit reporting");


// Forensic v3: independent HP continuity, not a made-up damage or healing total.
// One delta-only packet MUST be accumulated before the next absolute HP value.
var hpClock = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
var hpEvidence = DeathCombatTimeline.AnalyzeHealthContinuity(new[]
{
    new IncomingHealthChange(hpClock, 314, 9, null, -100, 900, 1, "Damage"),
    new IncomingHealthChange(hpClock.AddMilliseconds(50), 314, 9, null, -50, null, 2, "Damage"),
    new IncomingHealthChange(hpClock.AddMilliseconds(80), 314, 9, null, -100, 750, 3, "Damage"),
    new IncomingHealthChange(hpClock.AddMilliseconds(120), 314, 0, null, 0, 1400, 0, "Unknown"),
    new IncomingHealthChange(hpClock.AddMilliseconds(140), 314, 9, null, -50, null, 4, "Damage")
});
Check(hpEvidence.AbsoluteHealthObservations == 3 && hpEvidence.ComparableTransitions == 2,
    "only absolute HP readings may anchor continuity transitions");
Check(hpEvidence.UnexplainedTransitions == 1 && hpEvidence.Findings.Count == 1,
    "delta-only updates must not cause false gaps, but unexplained jumps must be visible");
Check(Math.Abs(hpEvidence.Findings[0].UnexplainedDifference - 650) < 0.001
      && hpEvidence.Findings[0].DeltaEventsIncluded == 1
      && hpEvidence.Findings[0].EvidenceStatus == "unexplained_by_observed_deltas",
    "HP jump should report the residual, not call it damage or healing");
Check(hpEvidence.UnpairedDeltaEvents == 1 && !hpEvidence.FindingsTruncated,
    "trailing updates without an absolute HP reading must remain unpaired");
Check(DeathCombatTimeline.AnalyzeHealthContinuity(Array.Empty<IncomingHealthChange>()).Status
      == "no_absolute_health_observed",
    "empty capture must not claim continuity");

var preciseHp = DeathCombatTimeline.AnalyzeHealthContinuity(new[]
{
    new IncomingHealthChange(hpClock, 314, 9, null, 0, 477.86, 0, "Unknown"),
    new IncomingHealthChange(hpClock.AddMilliseconds(77), 314, 9, null, -0.1, 477.8, 1, "Unknown")
});
Check(preciseHp.UnexplainedTransitions == 0,
    "sub-HP rounding differences must not be called unexplained damage");

var manyGaps = new List<IncomingHealthChange>
{
    new(hpClock, 314, 0, null, 0, 1000, 0, "Unknown")
};
for (var i = 1; i <= 80; i++)
    manyGaps.Add(new IncomingHealthChange(
        hpClock.AddMilliseconds(i * 5), 314, 0, null, 0, 1000 + 10 * i, 0, "Unknown"));
var capped = DeathCombatTimeline.AnalyzeHealthContinuity(manyGaps, maxFindings: 10);
Check(capped.UnexplainedTransitions == 80 && capped.Findings.Count == 10
      && capped.FindingsTruncated,
    "full anomaly count must be retained with a bounded 10-finding detail budget");

var nextVictim = new DeathCombatTimeline(TimeSpan.FromSeconds(45), 100);
nextVictim.Observe(new IncomingHealthChange(hpClock.AddSeconds(-2),
    11, 91, null, -5, 19.05, 6, "Unknown"));
var deathReport = nextVictim.FreezeAtDeath(hpClock, "Local", 11, "KillerCredit", 92, null, null);
Check(deathReport?.HealthContinuity is not null
      && deathReport.HealthContinuity.Status == "insufficient_comparable_health_observations"
      && deathReport.TerminalEvidence?.KillerHadObservedNegativeDelta == false,
    "one terminal HP and unmatched killer credit must never invent a final blow");

Console.WriteLine("PASS: forensic v3 HP gaps, multi-delta transitions, rounding, budgets and unknown killer");
