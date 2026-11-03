// Guild Combat Client Linux v1: from Albion Photon parameters to the War Room, through the SAME
// core as Windows. A local fake War Room receives what the Linux client really sends.
using System.Net;
using System.Text;
using System.Text.Json;
using Imortais.LinuxClient.Models;
using Imortais.LinuxClient.Network;
using Imortais.LinuxClient.Services;
using StatisticsAnalysisTool.Imortais;
using StatisticsAnalysisTool.Network;

static void Require(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); }

// ---- 1) Albion codes: Windows enums == codes verified on Linux after Dragonfire Patch 3 ----
Require((int)EventCodes.Leave == 1 && (int)EventCodes.HealthUpdate == 6 && (int)EventCodes.HealthUpdates == 7, "health codes");
Require((int)EventCodes.NewCharacter == 29 && (int)EventCodes.NewMob == 123, "character/mob codes");
Require((int)EventCodes.KilledPlayer == 163 && (int)EventCodes.Died == 164, "kill/death codes");
Require((int)EventCodes.PartyJoined == 230 && (int)EventCodes.PartyDisbanded == 231
    && (int)EventCodes.PartyPlayerJoined == 232 && (int)EventCodes.PartyPlayerLeft == 234, "party codes");
Require((int)EventCodes.OtherGrabbedLoot == 278, "loot code");
Require((int)OperationCodes.Join == 2 && (int)OperationCodes.ChangeCluster == 41, "operation codes");
Require(GameReceiver.PhotonCode(new() { [252] = 278 }, 252) == 278 && GameReceiver.PhotonCode(new(), 252) == -1
    && GameReceiver.PhotonCode(new() { [252] = "corrupt" }, 252) == -1, "code parsing as Windows AlbionParser");

// ---- 2) Party roster decoding (packed byte[16*N], incomplete never replaces) ----------------
var rosterGuids = Enumerable.Range(1, 20).Select(_ => Guid.NewGuid()).ToArray();
var packed = rosterGuids.SelectMany(x => x.ToByteArray()).ToArray();
var names20 = Enumerable.Range(0, 20).Select(i => $"Member{i:00}").ToArray();
Require(GameReceiver.TryDecodePartyRoster(new() { [8] = packed, [9] = names20 }, out var roster) && roster.Count == 20 && roster[rosterGuids[19]] == "Member19", "packed roster");
Require(!GameReceiver.TryDecodePartyRoster(new() { [8] = packed[..^16], [9] = names20 }, out _), "truncated roster rejected");
Require(!GameReceiver.TryDecodePartyRoster(new() { [8] = packed, [9] = names20.Select((x, i) => i == 3 ? "" : x).ToArray() }, out _), "empty name rejected");
Require(GameReceiver.TryDecodePartyRoster(new() { [4] = rosterGuids.Select(g => (object)g.ToByteArray()).ToArray(), [5] = names20 }, out roster) && roster.Count == 20, "legacy 4/5 roster");

// ---- 3) Alpha pairing migrates once, never overwrites ----------------------------------------
var migrationDir = Path.Combine(Path.GetTempPath(), "gcc-migr-" + Guid.NewGuid().ToString("N"));
var migrated = Path.Combine(migrationDir, "telemetry.json");
var alpha = new ClientSettings { AgentKey = "alpha-key", DeviceId = "linux-pc-user", PlayerName = "Alpha", ServerUrl = "https://cta-imortais.up.railway.app" };
Require(LegacyAlphaMigration.Run(alpha, migrated) is not null, "migration runs");
var migratedCfg = ImortaisTelemetryConfig.LoadFromFile(migrated);
Require(migratedCfg.Enabled && migratedCfg.AgentKey == "alpha-key" && migratedCfg.DeviceId == "linux-pc-user"
    && migratedCfg.ServerUrl == ImortaisProductionEndpoint.BaseUrl && migratedCfg.Pairings?.Count == 1, "migrated pairing, endpoint and device kept");
Require(LegacyAlphaMigration.Run(new ClientSettings { AgentKey = "other" }, migrated) is null
    && ImortaisTelemetryConfig.LoadFromFile(migrated).AgentKey == "alpha-key", "never overwrites a newer pairing");
Require(LegacyAlphaMigration.Run(new ClientSettings(), Path.Combine(migrationDir, "none.json")) is null, "nothing to migrate without a key");
if (OperatingSystem.IsLinux()) Require((File.GetUnixFileMode(migrated) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0, "pairing file private");
Directory.Delete(migrationDir, true);

// ---- 4) Updater: v1 tags, prerelease only, official URLs, never downgrade ---------------------
Require(LinuxUpdateService.BuildNumberOf("1.0.0") == 10000 && LinuxUpdateService.BuildNumberOf("1.2.3") == 10203 && LinuxUpdateService.BuildNumberOf("x") == 0, "versions");
const string Official = "https://github.com/cocoputiz-sudo/imortais-combat-client/releases/download/";
string Rel(string tag, string ver, bool pre = true, string host = Official)
{
    var deb = $"IMORTAIS-Combat-Client-v{ver}-linux-x64.deb";
    return "{\"draft\":false,\"prerelease\":" + (pre ? "true" : "false") + ",\"tag_name\":\"" + tag + "\",\"assets\":["
        + "{\"name\":\"" + deb + "\",\"browser_download_url\":\"" + host + tag + "/" + deb + "\"},"
        + "{\"name\":\"" + deb + ".sha256\",\"browser_download_url\":\"" + host + tag + "/" + deb + ".sha256\"}]}";
}
using (var doc = JsonDocument.Parse("[" + string.Join(",", Rel("v1.0.1-linux", "1.0.1"), Rel("v1.1.0-linux", "1.1.0", host: "https://evil.example/"),
           Rel("v2.0.0-linux", "2.0.0", pre: false), Rel("v0.2.0-linux-alpha.29", "0.2.0-alpha.1"), Rel("v1.0.2-linux", "1.0.3")) + "]"))
{
    var pick = LinuxUpdateService.SelectLatestRelease(doc.RootElement, LinuxUpdateService.BuildNumberOf("1.0.0"));
    Require(pick?.Version == "1.0.1", "picks the newest official prerelease with matching package: " + pick?.Version);
    Require(LinuxUpdateService.SelectLatestRelease(doc.RootElement, LinuxUpdateService.BuildNumberOf("1.0.1")) is null, "never offers same/older");
}

// ---- 5) End to end: Photon parameters -> shared core -> War Room ------------------------------
var home = Path.Combine(Path.GetTempPath(), "gcc-linux-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("XDG_DATA_HOME", home);
var port = 19000 + Random.Shared.Next(900);
var cfgDir = Path.Combine(home, "IMORTAIS Combat Client");
Directory.CreateDirectory(cfgDir);
File.WriteAllText(Path.Combine(cfgDir, "telemetry.json"), JsonSerializer.Serialize(new
{
    Enabled = true, ServerUrl = $"http://127.0.0.1:{port}", AgentKey = "linux-agent", DeviceId = "linux-test",
    PlayerName = "LinuxCaller", BatchIntervalMs = 250, GuildDisplayName = "IMORTAIS", OwnGuildNames = new[] { "IMORTAIS", "IMORTAIS ACADEMY", "IMORTAIS 2" }
}));
var received = new List<JsonElement>();
var listener = new HttpListener();
listener.Prefixes.Add($"http://127.0.0.1:{port}/");
listener.Start();
_ = Task.Run(async () =>
{
    while (listener.IsListening)
    {
        HttpListenerContext ctx;
        try { ctx = await listener.GetContextAsync(); } catch { break; }
        string body; using (var r = new StreamReader(ctx.Request.InputStream)) body = await r.ReadToEndAsync();
        if (ctx.Request.Url!.AbsolutePath == "/api/telemetry/ingest" && body.Length > 0)
            lock (received) foreach (var e in JsonDocument.Parse(body).RootElement.GetProperty("events").EnumerateArray()) received.Add(e.Clone());
        var bytes = Encoding.UTF8.GetBytes("{\"ok\":true,\"inserted\":1,\"guild\":{\"name\":\"IMORTAIS\",\"ownGuilds\":[\"IMORTAIS\",\"IMORTAIS ACADEMY\",\"IMORTAIS 2\"]}}");
        ctx.Response.ContentType = "application/json"; await ctx.Response.OutputStream.WriteAsync(bytes); ctx.Response.Close();
    }
});

var meter = new CombatState();
var rx = new GameReceiver(meter);
ImortaisEventBridge.Start();
// Login: local player 100 "LinuxCaller" in guild IMORTAIS, on map 0000
rx.InjectResponse(new() { [253] = (short)OperationCodes.Join, [0] = 100L, [2] = "LinuxCaller", [58] = "IMORTAIS", [8] = "0000" });
Require(rx.LocalName == "LinuxCaller" && rx.LocalGuild == "IMORTAIS", "login read");
// Change map
rx.InjectResponse(new() { [253] = (short)OperationCodes.ChangeCluster, [0] = "4000" });
// Party of 3, nearby players and a mob
var g1 = Guid.NewGuid(); var g2 = Guid.NewGuid(); var g3 = Guid.NewGuid();
rx.InjectEvent(new() { [252] = (short)EventCodes.PartyJoined, [8] = new[] { g1, g2, g3 }.SelectMany(g => g.ToByteArray()).ToArray(), [9] = new[] { "LinuxCaller", "Healer", "Tank" } });
rx.InjectEvent(new() { [252] = (short)EventCodes.NewCharacter, [0] = 200L, [1] = "Healer", [7] = g2.ToByteArray(), [8] = "IMORTAIS" });
rx.InjectEvent(new() { [252] = (short)EventCodes.NewCharacter, [0] = 300L, [1] = "Inimigo", [7] = Guid.NewGuid().ToByteArray(), [8] = "OUTRA", [51] = "ALI" });
rx.InjectEvent(new() { [252] = (short)EventCodes.NewCharacter, [0] = 400L, [1] = "Estranho", [7] = Guid.NewGuid().ToByteArray(), [8] = "OUTRA" });
rx.InjectEvent(new() { [252] = (short)EventCodes.NewMob, [0] = 500L, [1] = 77 });
Require(rx.Party.Count == 3 && rx.NearbyPlayers == 3, "party and nearby players");
// Damage: local -> enemy player (PvP), local -> mob (PvE), healer heals, non-party player ignored, self damage ignored
rx.InjectEvent(new() { [252] = (short)EventCodes.HealthUpdate, [0] = 300L, [2] = -1200.4, [6] = 100L, [7] = 5 });
rx.InjectEvent(new() { [252] = (short)EventCodes.HealthUpdates, [0] = 500L, [2] = new[] { -300.0, -200.0 }, [6] = new[] { 100L, 100L } });
rx.InjectEvent(new() { [252] = (short)EventCodes.HealthUpdate, [0] = 100L, [2] = 800.0, [6] = 200L });
rx.InjectEvent(new() { [252] = (short)EventCodes.HealthUpdate, [0] = 300L, [2] = -9999.0, [6] = 400L });
rx.InjectEvent(new() { [252] = (short)EventCodes.HealthUpdate, [0] = 100L, [2] = -50.0, [6] = 100L });
// Lethal kill by the local player; a death of an unrelated player is ignored
rx.InjectEvent(new() { [252] = (short)EventCodes.Died, [1] = 300L, [2] = "Inimigo", [3] = "OUTRA", [9] = 100L, [10] = "LinuxCaller", [11] = "IMORTAIS", [17] = true });
rx.InjectEvent(new() { [252] = (short)EventCodes.Died, [1] = 900L, [2] = "X", [3] = "A", [9] = 901L, [10] = "Y", [11] = "B", [17] = true });
// Malformed packet never breaks capture
rx.InjectEvent(new() { [252] = (short)EventCodes.HealthUpdates, [0] = "lixo", [2] = "x", [6] = 3.5 });

var deadline = DateTime.UtcNow.AddSeconds(25);
bool Has(string type) { lock (received) return received.Any(e => e.GetProperty("type").GetString() == type); }
while (DateTime.UtcNow < deadline && !(Has("combat_delta") && Has("zone_change") && Has("party_snapshot") && Has("player_death_observed")))
    await Task.Delay(200);
lock (received)
{
    var types = received.Select(e => e.GetProperty("type").GetString()).Distinct().ToArray();
    Require(Has("zone_change") && Has("party_snapshot") && Has("combat_delta") && Has("player_death_observed") && Has("client_heartbeat"),
        "events sent: " + string.Join(",", types) + " · " + ImortaisEventBridge.GetStatus());
    var zone = received.Where(e => e.GetProperty("type").GetString() == "zone_change").Select(e => e.GetProperty("payload")).Last();
    Require(zone.GetProperty("clusterIndex").GetString() == "4000", "map index sent: " + zone);
    var party = received.Last(e => e.GetProperty("type").GetString() == "party_snapshot").GetProperty("payload");
    Require(party.GetProperty("members").GetArrayLength() == 3, "party of 3 sent: " + party);
    var deltas = received.Where(e => e.GetProperty("type").GetString() == "combat_delta").Select(e => e.GetProperty("payload")).ToArray();
    var mine = deltas.Where(d => d.GetProperty("player").GetString() == "LinuxCaller").ToArray();
    Require(mine.Sum(d => d.GetProperty("damage").GetInt64()) == 1700, "legacy damage = party damage without self damage: " + string.Join(" | ", mine));
    var byTarget = mine.Where(d => d.TryGetProperty("damageByTarget", out _)).Select(d => d.GetProperty("damageByTarget")).ToArray();
    Require(byTarget.Sum(t => t.GetProperty("player").GetInt64()) == 1200 && byTarget.Sum(t => t.GetProperty("mob").GetInt64()) == 500, "PvP/PvE split as Windows");
    Require(deltas.Where(d => d.GetProperty("player").GetString() == "Healer").Sum(d => d.GetProperty("healing").GetInt64()) == 800, "party healing");
    Require(deltas.All(d => d.GetProperty("player").GetString() != "Estranho"), "non-party player never sent as ours");
    var deaths = received.Where(e => e.GetProperty("type").GetString() == "player_death_observed").Select(e => e.GetProperty("payload")).ToArray();
    Require(deaths.Length == 1 && deaths[0].GetProperty("victim").GetString() == "Inimigo" && deaths[0].GetProperty("killer").GetString() == "LinuxCaller", "only relevant lethal death");
    Require(received.Any(e => e.GetProperty("type").GetString() == "kill"), "personal kill result sent: " + string.Join(",", types));
}
var me = meter.SnapshotPlayers().First(p => p.Name == "LinuxCaller");
Require(me.Damage == 1700 && me.Kills == 1, "local meter");

// Character of ANOTHER guild without pairing: nothing is sent to this War Room
int before; lock (received) before = received.Count;
rx.InjectResponse(new() { [253] = (short)OperationCodes.Join, [0] = 101L, [2] = "AltOutraGuilda", [58] = "OUTRA GUILDA", [8] = "0000" });
rx.InjectEvent(new() { [252] = (short)EventCodes.HealthUpdate, [0] = 500L, [2] = -777.0, [6] = 101L });
await Task.Delay(2500);
lock (received) Require(!received.Skip(before).Any(e => e.ToString().Contains("AltOutraGuilda")), "other guild never sent");
Require(ImortaisEventBridge.CurrentGuildLink.State == GuildLinkState.Mismatch, "guild confirmer red for another guild");
listener.Stop();

// ---- 5b) Regressions found installing the .deb on a stock Ubuntu ------------------------------
// libpcap: the wrapper asks for libpcap.so (only with libpcap-dev); Ubuntu ships libpcap.so.0.8.
Imortais.LinuxClient.Services.NativeLibraries.Install();
if (OperatingSystem.IsLinux() && Imortais.LinuxClient.Services.NativeLibraries.CanLoadPcap())
{
    var devices = Libpcap.Pcap.ListDevices();
    Require(Libpcap.Pcap.Version is not null, "libpcap loads on stock Ubuntu (devices: " + devices.Count + ")");
}
// Data folder: never relative to the current folder, even when ~/.local/share does not exist yet.
var freshHome = Path.Combine(Path.GetTempPath(), "gcc-fresh-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(freshHome);
var savedXdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
var savedHome = Environment.GetEnvironmentVariable("HOME");
Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
Environment.SetEnvironmentVariable("HOME", freshHome);
var root = ImortaisTelemetryConfig.DataRoot();
Require(Path.IsPathRooted(root) && root.StartsWith(freshHome, StringComparison.Ordinal), "data folder under HOME even when missing: " + root);
Require(Path.IsPathRooted(LinuxPaths.ConfigDirectory) && LinuxPaths.ConfigDirectory.StartsWith(freshHome, StringComparison.Ordinal), "config folder under HOME");
Environment.SetEnvironmentVariable("HOME", savedHome);
Environment.SetEnvironmentVariable("XDG_DATA_HOME", savedXdg);
Directory.Delete(freshHome, true);

// ---- 6) Anonymous diagnostics ------------------------------------------------------------------
var diagnostic = new LinuxPhotonDiagnostics();
diagnostic.Observe(278, new() { [252] = 278, [2] = "PRIVATE_PLAYER_NAME", [4] = 987654321L, [9] = Guid.NewGuid().ToByteArray() });
var export = diagnostic.Export();
Require(!export.Contains("PRIVATE_PLAYER_NAME") && !export.Contains("987654321") && export.Contains("byte[16]"), "diagnostics hold only structure");

Console.WriteLine("PASS: Linux v1 · códigos do Windows, party, migração da alpha, atualizador e do pacote do jogo ao War Room pelo núcleo compartilhado");
Environment.Exit(0);
