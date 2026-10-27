using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;

namespace StatisticsAnalysisTool.Imortais;

public sealed class ImortaisTelemetryConfig
{
    public const int DefaultMaxOutboxEvents = 50000;
    public const long DefaultMaxOutboxBytes = 50L * 1024 * 1024;

    public bool Enabled { get; set; } = false;
    // Opt-in: write raw decoded Photon guild probes locally instead of sending those probes.
    public bool GuildProbeLocalDiagnosticsEnabled { get; set; } = false;

    public string ServerUrl { get; set; } = ImortaisProductionEndpoint.BaseUrl;
    public string AgentKey { get; set; } = string.Empty;
    public string DeviceId { get; set; } = Environment.MachineName;
    public string PlayerName { get; set; } = string.Empty;
    public string? CtaEventId { get; set; }
    public int BatchIntervalMs { get; set; } = 1000;
    public int MaxBatchSize { get; set; } = 100;
    public string? OutboxPath { get; set; } = DefaultOutboxPath;
    public int MaxOutboxEvents { get; set; } = DefaultMaxOutboxEvents;
    public long MaxOutboxBytes { get; set; } = DefaultMaxOutboxBytes;
    // Informados pelo War Room da guilda (pareamento e cada envio). null = ainda não informado.
    public string? GuildDisplayName { get; set; }
    public List<string>? OwnGuildNames { get; set; }
    // Um pareamento por War Room; os campos acima são o destino ativo (escolhido pela guilda no jogo).
    public List<GuildPairing>? Pairings { get; set; }

    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalSettings { get; set; }

    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IMORTAIS Combat Client");

    public static string FilePath => Path.Combine(DirectoryPath, "telemetry.json");
    public static string DefaultOutboxPath => Path.Combine(DirectoryPath, "outbox.ndjson");

    public static ImortaisTelemetryConfig Load() => LoadFromFile(FilePath);

    internal static ImortaisTelemetryConfig LoadFromFile(string path, Action<ImortaisTelemetryConfig, string>? persist = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path))
            {
                var created = new ImortaisTelemetryConfig();
                SaveToFile(created, path);
                return created;
            }

            var json = File.ReadAllText(path);
            var config=JsonSerializer.Deserialize<ImortaisTelemetryConfig>(json) ?? new ImortaisTelemetryConfig();
            var migratedUrl = ImortaisProductionEndpoint.Migrate(config.ServerUrl);
            var changed = migratedUrl != config.ServerUrl;
            config.ServerUrl = migratedUrl;
            // Discard obsolete QA secrets, while retaining unknown user preferences.
            changed |= config.AdditionalSettings?.Remove("HomologGuildToken") == true;
            changed |= config.AdditionalSettings?.Remove("HomologGuildUploadEnabled") == true;
            if (changed)
            {
                try { (persist ?? SaveToFile)(config, path); }
                catch { /* Keep pairing and direct destination in memory if disk is read-only. */ }
            }
            return config;
        }
        catch
        {
            return new ImortaisTelemetryConfig();
        }
    }

    public static void Save(ImortaisTelemetryConfig config) => SaveToFile(config, FilePath);

    internal static void SaveToFile(ImortaisTelemetryConfig config, string path)
    {
        config.ServerUrl = ImortaisProductionEndpoint.Migrate(config.ServerUrl);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

public sealed class GuildPairing
{
    public string ServerUrl { get; set; } = string.Empty;
    public string AgentKey { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string? GuildDisplayName { get; set; }
    public List<string>? OwnGuildNames { get; set; }
    public string? OutboxPath { get; set; }
    public DateTime? PairedAtUtc { get; set; }
}
