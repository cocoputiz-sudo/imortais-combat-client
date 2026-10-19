using System.Text.Json.Nodes;
using StatisticsAnalysisTool.Imortais;
using Imortais.Bridge.Services;

static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
const string old = "https://cta-imortais.up.railway.app";
const string target = "https://imortais.macknacloud.com.br";
foreach (var value in new[] {old, old + "/", "https://CTA-IMORTAIS.up.railway.app:443/"})
    Check(ImortaisProductionEndpoint.Migrate(value) == target, "Old production origin not migrated");
foreach (var value in new[] {"", "http://cta-imortais.up.railway.app", old + ":8443", old + "/custom",
    old + "?key=private", old + "#custom", "https://user@cta-imortais.up.railway.app",
    old + ".evil.example", "https://example.test", target, "not a url"})
    Check(ImortaisProductionEndpoint.Migrate(value) == value, "Custom/malformed endpoint changed");
var directory = Path.Combine(Path.GetTempPath(), "imortais-endpoint-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var path = Path.Combine(directory, "telemetry.json");
    var outbox = Path.Combine(directory, "outbox.ndjson");
    File.WriteAllText(outbox, "{\"eventId\":\"pending-1\"}\n");
    var original = JsonNode.Parse("""
        {"Enabled":false,"ServerUrl":"https://cta-imortais.up.railway.app/","AgentKey":"paired-token",
        "DeviceId":"stable-device","PlayerName":"BadMack","CtaEventId":"189","MaxBatchSize":17,
        "GuildProbeLocalDiagnosticsEnabled":true,"CustomPreference":{"preserve":42}}
        """)!.AsObject();
    original["OutboxPath"] = outbox;
    File.WriteAllText(path, original.ToJsonString());
    var loaded = ImortaisTelemetryConfig.LoadFromFile(path);
    Check(loaded.ServerUrl == target && loaded.AgentKey == "paired-token" && loaded.DeviceId == "stable-device",
        "Loaded pairing lost");
    Check(!loaded.Enabled && loaded.GuildProbeLocalDiagnosticsEnabled && loaded.MaxBatchSize == 17,
        "Consent/preferences changed");
    var saved = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    foreach (var item in original.Where(x => x.Key != "ServerUrl"))
        Check(JsonNode.DeepEquals(item.Value, saved[item.Key]), "Persisted property lost: " + item.Key);
    var first = File.ReadAllBytes(path);
    ImortaisTelemetryConfig.LoadFromFile(path);
    Check(first.SequenceEqual(File.ReadAllBytes(path)), "Second load rewrites configuration");
    Check(File.ReadAllText(outbox) == "{\"eventId\":\"pending-1\"}\n", "Outbox changed");
    File.WriteAllText(path, original.ToJsonString());
    loaded = ImortaisTelemetryConfig.LoadFromFile(path, (_, _) => throw new UnauthorizedAccessException());
    Check(loaded.ServerUrl == target && loaded.AgentKey == "paired-token" && loaded.DeviceId == "stable-device",
        "Failed persistence reset loaded credentials");
    Check(JsonNode.DeepEquals(original, JsonNode.Parse(File.ReadAllText(path))), "Failed save changed file");
    original["HomologGuildToken"] = "obsolete-qa";
    original["HomologGuildUploadEnabled"] = true;
    File.WriteAllText(path, original.ToJsonString());
    ImortaisTelemetryConfig.LoadFromFile(path);
    saved = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    Check(!saved.ContainsKey("HomologGuildToken") && !saved.ContainsKey("HomologGuildUploadEnabled")
        && saved["AgentKey"]!.GetValue<string>() == "paired-token", "QA cleanup regressed");
    var store = new SettingsStore(directory);
    var settingsPath = Path.Combine(directory, "settings.json");
    File.WriteAllText(settingsPath, """
        {"RailwayBaseUrl":"https://cta-imortais.up.railway.app","ApiKey":"bridge-key",
         "DeviceId":"bridge-device","SessionId":"stable-session","FutureSetting":99}
        """);
    var bridge = store.Load();
    Check(bridge.RailwayBaseUrl == target && bridge.ApiKey == "bridge-key" && bridge.SessionId == "stable-session",
        "Bridge migration lost pairing/session");
    Check(JsonNode.Parse(File.ReadAllText(settingsPath))!["FutureSetting"]!.GetValue<int>() == 99,
        "Bridge migration lost unknown field");
    Check(new ImortaisTelemetryConfig().ServerUrl == target, "Fresh install uses old endpoint");
    Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "Temporary files leaked");
    Console.WriteLine("PASS: endpoint migration, persisted pairing/preferences, outbox, idempotency, write failure and bridge");
}
finally { Directory.Delete(directory, recursive: true); }
