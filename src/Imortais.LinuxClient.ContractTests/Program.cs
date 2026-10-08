using Imortais.LinuxClient.Services;

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
