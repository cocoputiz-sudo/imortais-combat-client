using System.Text.Json.Nodes;
using Imortais.LinuxClient.Services;
using Imortais.LinuxClient.Models;
static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
const string target = "https://imortais.macknacloud.com.br";
var directory = Path.Combine(Path.GetTempPath(), "imortais-linux-endpoint-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    var path = Path.Combine(directory, "settings.json");
    var original = JsonNode.Parse("""
      {"ServerUrl":"https://cta-imortais.up.railway.app/","AgentKey":"paired-token","DeviceId":"stable-device",
       "PlayerName":"BadMack","CurrentCtaId":"189","StartCaptureAutomatically":false,"StartWithDesktop":true,
       "FuturePreference":{"preserve":42}}
      """)!;
    File.WriteAllText(path, original.ToJsonString());
    var settings = SettingsStore.LoadFromFile(path);
    Check(settings.ServerUrl == target && settings.AgentKey == "paired-token" && settings.DeviceId == "stable-device",
        "Pairing lost during Linux migration");
    var saved = JsonNode.Parse(File.ReadAllText(path))!;
    foreach (var item in original.AsObject().Where(x => x.Key != "ServerUrl"))
        Check(JsonNode.DeepEquals(item.Value, saved[item.Key]), "Setting lost: " + item.Key);
    var first = File.ReadAllText(path);
    SettingsStore.LoadFromFile(path);
    Check(first == File.ReadAllText(path), "Migration not idempotent");
    if (OperatingSystem.IsLinux())
    {
        Check(File.GetUnixFileMode(path) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "Token file not private");
        Check(File.GetUnixFileMode(directory) == (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute), "Directory not private");
    }
    File.WriteAllText(path, original.ToJsonString());
    settings = SettingsStore.LoadFromFile(path, (_, _) => throw new UnauthorizedAccessException());
    Check(settings.AgentKey == "paired-token" && settings.ServerUrl == target, "Write failure reset pairing");
    Check(JsonNode.DeepEquals(original, JsonNode.Parse(File.ReadAllText(path))), "Write failure altered original");
    original["ServerUrl"] = "https://custom.example.test";
    File.WriteAllText(path, original.ToJsonString());
    Check(SettingsStore.LoadFromFile(path).ServerUrl == "https://custom.example.test", "Custom server changed");
    Check(new ClientSettings().ServerUrl == target, "Fresh Linux install uses old URL");
    Console.WriteLine("PASS: Linux persisted pairing/preferences, idempotency, 0600/0700, custom server and write failure");
}
finally { Directory.Delete(directory, true); }
