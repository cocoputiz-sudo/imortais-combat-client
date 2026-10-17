namespace Imortais.LinuxClient.Models;

public sealed record TelemetryEvent(
    string EventId,
    string Type,
    DateTimeOffset OccurredAt,
    string? PlayerName,
    Dictionary<string, object?> Payload)
{
    public static TelemetryEvent Create(string type, string? playerName, Dictionary<string, object?> payload) =>
        new(Guid.NewGuid().ToString("N"), type, DateTimeOffset.UtcNow, playerName, payload);
}
