// The SAME telemetry core as the Windows client, running on Linux against a local fake War Room:
// configuration path, Bearer auth, batching, payload types and War Room acknowledgements.
using System.Net;
using System.Text;
using System.Text.Json;
using StatisticsAnalysisTool.Imortais;

static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); }

var home = Path.Combine(Path.GetTempPath(), "icc-core-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(home);
// On Linux .NET maps LocalApplicationData to $XDG_DATA_HOME.
Environment.SetEnvironmentVariable("XDG_DATA_HOME", home);
var port = 18000 + Random.Shared.Next(1000);
var dataDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
Check(dataDir == home, "LocalApplicationData must follow XDG_DATA_HOME on Linux: " + dataDir);
var cfgDir = Path.Combine(home, "IMORTAIS Combat Client");
Directory.CreateDirectory(cfgDir);
File.WriteAllText(Path.Combine(cfgDir, "telemetry.json"), JsonSerializer.Serialize(new
{
    Enabled = true, ServerUrl = $"http://127.0.0.1:{port}", AgentKey = "agent-token-test",
    DeviceId = "linux-test-pc", PlayerName = "LinuxTester", BatchIntervalMs = 250,
    GuildDisplayName = "Guilda Teste", OwnGuildNames = new[] { "Guilda Teste" }
}));

var received = new List<JsonElement>();
var auth = new List<string?>();
var listener = new HttpListener();
listener.Prefixes.Add($"http://127.0.0.1:{port}/");
listener.Start();
_ = Task.Run(async () =>
{
    while (listener.IsListening)
    {
        HttpListenerContext ctx;
        try { ctx = await listener.GetContextAsync(); } catch { break; }
        var path = ctx.Request.Url!.AbsolutePath;
        string body;
        using (var r = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) body = await r.ReadToEndAsync();
        lock (received)
        {
            auth.Add(ctx.Request.Headers["Authorization"]);
            if (path == "/api/telemetry/ingest" && body.Length > 0)
                foreach (var e in JsonDocument.Parse(body).RootElement.GetProperty("events").EnumerateArray()) received.Add(e.Clone());
        }
        var reply = path == "/api/telemetry/ingest"
            ? "{\"ok\":true,\"inserted\":1,\"duplicate\":0,\"rejected\":0,\"guild\":{\"name\":\"Guilda Teste\",\"ownGuilds\":[\"Guilda Teste\"]}}"
            : "{\"ok\":true,\"guild\":{\"name\":\"Guilda Teste\",\"ownGuilds\":[\"Guilda Teste\"]}}";
        var bytes = Encoding.UTF8.GetBytes(reply);
        ctx.Response.ContentType = "application/json";
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }
});

ImortaisEventBridge.SetGameDetected(true);
ImortaisEventBridge.SetGameGuild("Guilda Teste");
ImortaisEventBridge.Start();
ImortaisEventBridge.ZoneChange("0000", "Martlock", "SAFE", "PLAYERCITY", null);
ImortaisEventBridge.Damage("LinuxTester", 1234, "Martlock");
ImortaisEventBridge.ClassifiedDamage("LinuxTester", 1000, "player", "Martlock");
ImortaisEventBridge.ClassifiedDamage("LinuxTester", 234, "mob", "Martlock");
ImortaisEventBridge.PartySnapshot(new[] { "LinuxTester", "Amigo" });

var deadline = DateTime.UtcNow.AddSeconds(25);
string[] Types() { lock (received) return received.Select(e => e.GetProperty("type").GetString() ?? "").ToArray(); }
while (DateTime.UtcNow < deadline && !(Types().Contains("combat_delta") && Types().Contains("zone_change")))
    await Task.Delay(200);
var types = Types();
Check(types.Contains("zone_change"), "zone_change sent: " + string.Join(",", types) + " · status " + ImortaisEventBridge.GetStatus());
Check(types.Contains("combat_delta"), "combat_delta sent: " + string.Join(",", types));
lock (received)
{
    Check(auth.All(a => a == "Bearer agent-token-test"), "Bearer token on every request");
    var delta = received.First(e => e.GetProperty("type").GetString() == "combat_delta").GetProperty("payload");
    Check(delta.GetProperty("damage").GetInt64() == 1234, "legacy damage unchanged");
    var byTarget = delta.GetProperty("damageByTarget");
    Check(byTarget.GetProperty("player").GetInt64() == 1000 && byTarget.GetProperty("mob").GetInt64() == 234, "PvP/PvE classification identical to Windows");
    Check(received.All(e => e.GetProperty("playerName").GetString() == "LinuxTester"), "observer name on every event");
}
var outbox = Path.Combine(cfgDir, "outbox.ndjson");
Check(!File.Exists(outbox) || new FileInfo(outbox).Length < 1_000_000, "outbox stays bounded");
int before; lock (received) before = received.Count;
ImortaisEventBridge.SetGameGuild("Outra Guilda");
ImortaisEventBridge.Damage("Alt", 999, "Martlock");
await Task.Delay(2500);
lock (received) Check(!received.Skip(before).Any(e => e.GetProperty("playerName").GetString() == "Alt" || (e.TryGetProperty("payload", out var p) && p.TryGetProperty("player", out var pl) && pl.GetString() == "Alt")), "character of another guild is never sent to this War Room");
Check(ImortaisEventBridge.CurrentGuildLink.State.ToString() != "Match", "guild confirmer turns red for another guild");
listener.Stop();
Console.WriteLine($"PASS: núcleo de telemetria do Windows rodando no Linux ({types.Length} evento(s): {string.Join(", ", types.Distinct())})");
Environment.Exit(0);
