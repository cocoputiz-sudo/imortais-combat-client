namespace Imortais.Bridge.Models;
public sealed record TelemetryEvent(
    string EventId,
    string Type,
    DateTimeOffset OccurredAt,
    string? PlayerName,
    object Payload
)
{
    public static TelemetryEvent Create(string type, string? playerName, object payload) =>
        new(Guid.NewGuid().ToString("N"), type, DateTimeOffset.UtcNow, playerName, payload);
}
