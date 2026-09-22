using System;
using System.Collections.Generic;

namespace StatisticsAnalysisTool.Imortais;

public sealed class ImortaisTelemetryEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");
    public string Type { get; init; } = string.Empty;
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public string? PlayerName { get; init; }
    public Dictionary<string, object?> Payload { get; init; } = new();
}
