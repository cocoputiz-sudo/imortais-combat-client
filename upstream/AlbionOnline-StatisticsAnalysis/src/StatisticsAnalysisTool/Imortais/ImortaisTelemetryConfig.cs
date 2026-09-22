using System;
using System.IO;
using System.Text.Json;

namespace StatisticsAnalysisTool.Imortais;

public sealed class ImortaisTelemetryConfig
{
    public const int DefaultMaxOutboxEvents = 50000;
    public const long DefaultMaxOutboxBytes = 50L * 1024 * 1024;

    public bool Enabled { get; set; } = false;
    public string ServerUrl { get; set; } = "https://cta-imortais.up.railway.app";
    public string AgentKey { get; set; } = string.Empty;
    public string DeviceId { get; set; } = Environment.MachineName;
    public string PlayerName { get; set; } = string.Empty;
    public string? CtaEventId { get; set; }
    public int BatchIntervalMs { get; set; } = 1000;
    public int MaxBatchSize { get; set; } = 100;
    public string? OutboxPath { get; set; } = DefaultOutboxPath;
    public int MaxOutboxEvents { get; set; } = DefaultMaxOutboxEvents;
    public long MaxOutboxBytes { get; set; } = DefaultMaxOutboxBytes;

    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IMORTAIS Combat Client");

    public static string FilePath => Path.Combine(DirectoryPath, "telemetry.json");
    public static string DefaultOutboxPath => Path.Combine(DirectoryPath, "outbox.ndjson");

    public static ImortaisTelemetryConfig Load()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            if (!File.Exists(FilePath))
            {
                var created = new ImortaisTelemetryConfig();
                Save(created);
                return created;
            }

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<ImortaisTelemetryConfig>(json) ?? new ImortaisTelemetryConfig();
        }
        catch
        {
            return new ImortaisTelemetryConfig();
        }
    }

    public static void Save(ImortaisTelemetryConfig config)
    {
        Directory.CreateDirectory(DirectoryPath);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
