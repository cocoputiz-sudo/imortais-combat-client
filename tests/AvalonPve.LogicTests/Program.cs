using StatisticsAnalysisTool.Imortais.AvalonPve;

static void Check(bool condition, string message) {
    if (!condition) throw new Exception(message);
}
var now = new DateTime(2026, 10, 30, 21, 0, 0, DateTimeKind.Utc);
var session = new AvalonPveObservationSession();
var dungeon = Guid.NewGuid();

Check(session.Context == AvalonPveContextKind.Unknown, "starts unknown");
session.ChangeContext("RandomDungeon", "Avalon", null, "123");
Check(session.Context != AvalonPveContextKind.AvalonInstance, "no GUID = no instance");
session.ChangeContext("Unknown", "Avalon", dungeon, "123");
Check(session.Context != AvalonPveContextKind.AvalonInstance, "unknown map");
session.ChangeContext("RandomDungeon", "Unknown", dungeon, "123");
Check(session.Context != AvalonPveContextKind.AvalonInstance, "unknown mode");
session.ChangeContext("AvalonTunnel", "Avalon", dungeon, "123");
Check(session.Context != AvalonPveContextKind.AvalonInstance, "road must not be a DG");
session.ChangeContext("RandomDungeon", "Avalon", dungeon, "123");
Check(session.Context == AvalonPveContextKind.AvalonInstance, "confirmed dungeon");
Check(session.ObserveDamage("dps", 100, 44, 400, 123, false, now).Count == 0, "provisional mob excluded");
Check(session.ObserveDamage("dps", 100, 0, 400, 123, true, now).Count == 0, "unknown mob index excluded");
Check(session.ObserveDamage("dps", 0, 44, 400, 123, true, now).Count == 0, "unknown target excluded");
Check(session.Flush(now, true).Count == 0, "unconfirmed events never stored");

session.ObserveDamage("dps", 100, 44, 400, 123, true, now);
session.ObserveDamage("dps", 100, 44, 300, 123, true, now.AddMilliseconds(200));
Check(session.Flush(now.AddSeconds(1)).Count == 0, "bounded window not expired");
var first = session.Flush(now.AddSeconds(5));
Check(first.Count == 1 && first[0].MobDamage == 700, "observed mob damage");
Check(first[0].Hits.Count == 2 && first[0].ObservedMs == 200, "raw evidence duration");
Check(first[0].Coverage == "partial" && first[0].TargetKind == "confirmed_mob_spawn", "never claims full coverage");
Check(first[0].DungeonInstanceId == dungeon.ToString("D"), "instance identity");
Check(first[0].WindowId.Length == 32, "unique window reference");

session.ObserveDamage("dps", 100, 44, 500, 11, true, now.AddSeconds(6));
var drained = session.ChangeContext("Other", "Unknown", null, "world");
Check(drained.Count == 1, "flush before leaving dungeon");
Check(session.Context == AvalonPveContextKind.Other, "other map");
session.ObserveDamage("dps", 100, 44, 900, 11, true, now.AddSeconds(7));
Check(session.Flush(now.AddSeconds(12), true).Count == 0, "no spill into ZvZ");

session.ChangeContext("RandomDungeon", "Avalon", dungeon, "floor1");
session.ObserveDamage("dps", 100, 44, 100, 11, true, now.AddSeconds(13));
Check(session.ChangeContext("RandomDungeon", "Avalon", dungeon, "floor2").Count == 1, "floor transition isolates windows");
session.ObserveDamage("dps", 200, 44, 200, 12, true, now.AddSeconds(14));
Check(session.Flush(now.AddSeconds(20), true).Single().ClusterIndex == "floor2", "new floor kept distinct");
session.Clear();
Check(session.Context == AvalonPveContextKind.Unknown && session.Flush(now.AddSeconds(30), true).Count == 0, "restart clears state");
Console.WriteLine("PASS Avalon PvE: fail closed, mob proof, bounded windows, floor transition and opt-in isolation");
