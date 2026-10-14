using Imortais.LinuxClient.Services;
using Imortais.LinuxClient.Network;
using System.Text.Json;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var resolved = AlbionWorldMapNames.Resolve("3004");
Require(resolved == "Martlock", $"3004 deveria resolver para Martlock, recebido: {resolved}");

var tunnelResolved = AlbionWorldMapNames.Resolve("TNL-001");
Require(
    tunnelResolved == "Ouyos-Aoeuam",
    $"TNL-001 deveria resolver para Ouyos-Aoeuam, recebido: {tunnelResolved}"
);

var hellgateResolved = AlbionWorldMapNames.Resolve("HELLGATE-01-10v10-01");
Require(
    hellgateResolved == "The Plains",
    $"HELLGATE-01-10v10-01 deveria resolver para The Plains, recebido: {hellgateResolved}"
);

var zone = LinuxTelemetryPayloads.ZoneChange("3004", resolved);
Require((string?)zone["clusterIndex"] == "3004", "zone_change.clusterIndex deve preservar o índice cru");
Require((string?)zone["clusterName"] == "Martlock", "zone_change.clusterName deve conter o nome resolvido");
Require(!Equals(zone["clusterName"], zone["clusterIndex"]), "clusterName não pode repetir índice numérico quando há nome");

var players = new List<Dictionary<string, object?>>
{
    new()
    {
        ["objectId"] = 123L,
        ["playerId"] = "00000000-0000-0000-0000-000000000001",
        ["name"] = "BadMack",
        ["guild"] = "IMORTAIS",
        ["alliance"] = null
    }
};
var presence = LinuxTelemetryPayloads.PlayerPresence(resolved, players);
Require((string?)presence["cluster"] == "Martlock", "player_presence_snapshot.cluster deve conter o nome resolvido");
Require(Convert.ToInt32(presence["observedCount"]) == 1, "observedCount deve refletir a lista");
Require(Convert.ToInt32(presence["snapshotIntervalMs"]) == 15000, "snapshotIntervalMs deve ser 15000");
Require((string?)presence["source"] == "NewCharacter+Leave", "source deve preservar o contrato");
Require(ReferenceEquals(presence["players"], players), "players deve ser o snapshot construído");

Console.WriteLine("✅ Linux telemetry contract: índices numéricos, TNL e HELLGATE resolvem para UniqueName e a telemetria usa nome de mapa.");


// Regression from Dragonfire Patch 3: 0..123 unchanged, all later event ordinals -1.
Require(LinuxPhotonEventCodes.NewMob == 123 && LinuxPhotonEventCodes.GuildPlayerUpdated == 104,
    "Pre-patch values cannot shift");
Require(LinuxPhotonEventCodes.ClusterInfoUpdate == 139 && LinuxPhotonEventCodes.TimeSync == 159,
    "Post-patch anchors must match the capture");
Require(LinuxPhotonEventCodes.PartyJoined == 230 && LinuxPhotonEventCodes.PartyDisbanded == 231
    && LinuxPhotonEventCodes.PartyPlayerJoined == 232 && LinuxPhotonEventCodes.PartyPlayerLeft == 234,
    "Party codes must match Dragonfire Patch 3");
Require(LinuxPhotonEventCodes.KilledPlayer == 163 && LinuxPhotonEventCodes.Died == 164
    && LinuxPhotonEventCodes.OtherGrabbedLoot == 278
    && LinuxPhotonEventCodes.FriendOnlineStatus == 297
    && LinuxPhotonEventCodes.FeatureSwitchInfo == 503,
    "Combat, loot and trailing event anchors must match Dragonfire Patch 3");

Require(LinuxPhotonEventCodes.Resolve(22, new Dictionary<byte, object>
    { [252] = 278 }) == LinuxPhotonEventCodes.OtherGrabbedLoot,
    "Photon packetCode(byte) must not truncate extended 252 event codes");
Require(LinuxPhotonEventCodes.Resolve(230, new Dictionary<byte, object>()) == 230,
    "Missing 252 should preserve legacy low byte event code");
Require(LinuxPhotonEventCodes.Resolve(0, new Dictionary<byte, object> { [252] = "corrupt" }) == -1,
    "Invalid extended event code must not be routed to an unrelated handler");

var rosterGuids = Enumerable.Range(1, 20).Select(_ => Guid.NewGuid()).ToArray();
var packedGuids = rosterGuids.SelectMany(x => x.ToByteArray()).ToArray();
var rosterNames = Enumerable.Range(0, 20).Select(i => $"Member{i:00}").ToArray();
var partyPacket = new Dictionary<byte, object>
{
    [8] = packedGuids, [9] = rosterNames, [252] = 230
};
Require(LinuxPhotonReceiver.TryDecodePartyRoster(partyPacket, out var roster)
    && roster.Count == 20 && roster[rosterGuids[19]] == "Member19",
    "Packed Photon byte[320] must decode 20 complete party members");
partyPacket[8] = packedGuids.AsSpan(0, packedGuids.Length - 16).ToArray();
Require(!LinuxPhotonReceiver.TryDecodePartyRoster(partyPacket, out _),
    "Truncated roster must not evict the existing party");
partyPacket[8] = packedGuids;
partyPacket[9] = rosterNames.Select((x, i) => i == 3 ? "" : x).ToArray();
Require(!LinuxPhotonReceiver.TryDecodePartyRoster(partyPacket, out _),
    "Empty member name must invalidate entire party roster");
partyPacket[9] = rosterNames;
partyPacket[8] = rosterGuids.Select(g => (object)g.ToByteArray()).ToArray();
Require(LinuxPhotonReceiver.TryDecodePartyRoster(partyPacket, out roster) && roster.Count == 20,
    "Object[] of GUID bytes must decode");
partyPacket[8] = null!;
Require(!LinuxPhotonReceiver.TryDecodePartyRoster(partyPacket, out _),
    "Missing GUIDs may not erase party");

var diagnostic = new LinuxPhotonDiagnostics();
const string privateName = "PRIVATE_PLAYER_NAME";
diagnostic.Observe(278, new Dictionary<byte, object>
{
    [252] = 278,
    [2] = privateName,
    [4] = 987654321L,
    [9] = Guid.NewGuid().ToByteArray()
});
diagnostic.Observe(278, new Dictionary<byte, object> { [252] = 278, [2] = "OTHER" });
using (var json = JsonDocument.Parse(diagnostic.Export()))
{
    Require(json.RootElement.GetProperty("totalEvents").GetInt64() == 2,
        "Diagnostics count every raw event");
    var evt = json.RootElement.GetProperty("observed").EnumerateArray().Single();
    Require(evt.GetProperty("count").GetInt64() == 2,
        "Diagnostics only saves first signature, accumulates count");
}
Require(!diagnostic.Export().Contains(privateName)
        && !diagnostic.Export().Contains("987654321")
        && diagnostic.Export().Contains("byte[16]"),
    "Diagnostics must contain only structure and never payload values or names");

Console.WriteLine("PASS: Linux alpha patch codes 252, packed/incomplete party and anonymous diagnostics");

// Linux alpha 2: diagnostics export must work without a desktop portal.
var diagnosticSummary = diagnostic.GetSummary();
Require(diagnosticSummary.Total == 2 && diagnosticSummary.Distinct == 1
        && diagnosticSummary.LootEvents == 2 && diagnosticSummary.PartyEvents == 0,
    "Live Photon counters must match sampled events");

var fakeHome = Path.Combine(Path.GetTempPath(), "imortais-linux-diag-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(Path.Combine(fakeHome, "Downloads"));
    var content = diagnostic.Export();
    var diagnosticPath = await LinuxDiagnosticExport.SaveFallbackAsync(content, fakeHome);
    Require(File.Exists(diagnosticPath) && File.ReadAllText(diagnosticPath) == content,
        "Portal-free diagnostic fallback must create valid JSON");
    Require(diagnosticPath.StartsWith(Path.Combine(fakeHome, "Downloads", "IMORTAIS-Diagnostics"), StringComparison.Ordinal),
        "Diagnostics should be easy for a player to locate in Downloads");
    if (OperatingSystem.IsLinux())
        Require((File.GetUnixFileMode(diagnosticPath) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0,
            "Diagnostic file must not be world-readable");
}
finally
{
    if (Directory.Exists(fakeHome)) Directory.Delete(fakeHome, recursive: true);
}

// Linux update resolver: official release, newer run, correct package + SHA asset.
const string package = "IMORTAIS-Combat-Client-v0.2.0-alpha.2-linux-x64.deb";
const string officialRelease = "https://github.com/cocoputiz-sudo/imortais-combat-client/releases/download/v0.2.0-linux-alpha.222/";
var releaseJson = """
[
  {"draft":false,"prerelease":true,"tag_name":"v0.2.0-linux-alpha.222",
   "assets":[
     {"name":"IMORTAIS-Combat-Client-v0.2.0-alpha.2-linux-x64.deb","browser_download_url":"https://github.com/cocoputiz-sudo/imortais-combat-client/releases/download/v0.2.0-linux-alpha.222/IMORTAIS-Combat-Client-v0.2.0-alpha.2-linux-x64.deb"},
     {"name":"IMORTAIS-Combat-Client-v0.2.0-alpha.2-linux-x64.deb.sha256","browser_download_url":"https://github.com/cocoputiz-sudo/imortais-combat-client/releases/download/v0.2.0-linux-alpha.222/IMORTAIS-Combat-Client-v0.2.0-alpha.2-linux-x64.deb.sha256"}
   ]},
  {"draft":false,"prerelease":true,"tag_name":"v0.2.0-linux-alpha.333",
   "assets":[
     {"name":"IMORTAIS-Combat-Client-v0.2.0-alpha.2-linux-x64.deb","browser_download_url":"https://evil.example/fake.deb"},
     {"name":"IMORTAIS-Combat-Client-v0.2.0-alpha.2-linux-x64.deb.sha256","browser_download_url":"https://evil.example/fake.sha256"}
   ]}
]
""";
using (var doc = JsonDocument.Parse(releaseJson))
{
    Require(LinuxUpdateService.SelectLatestRelease(doc.RootElement, 221)?.BuildNumber == 222,
        "Updater must select newer official release and reject untrusted download host");
    Require(LinuxUpdateService.SelectLatestRelease(doc.RootElement, 222) is null,
        "Updater must not reinstall the running build");
}
Require(LinuxUpdateService.ParseExpectedSha256(new string('a', 64) + "  " + package + "\n", package)
    == new string('a', 64), "SHA-256 parser accepts only the matching package");
try
{
    LinuxUpdateService.ParseExpectedSha256("bad_hash  " + package, package);
    throw new Exception("Expected invalid SHA rejection");
}
catch (InvalidDataException) { }

Console.WriteLine("PASS: Linux alpha 2 diagnostic fallback, session counters and verified release resolver");
