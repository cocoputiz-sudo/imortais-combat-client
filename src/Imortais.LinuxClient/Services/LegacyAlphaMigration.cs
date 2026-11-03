using System.Text.Json;
using Imortais.LinuxClient.Models;
using StatisticsAnalysisTool.Imortais;

namespace Imortais.LinuxClient.Services;

/// <summary>
/// The Linux alpha kept its pairing in ~/.config/imortais-combat-client/settings.json. Version 1
/// uses the shared core (telemetry.json in ~/.local/share/IMORTAIS Combat Client, same format
/// as Windows). Runs once, before the core is touched, and never overwrites a newer pairing.
/// </summary>
public static class LegacyAlphaMigration
{
    public static string? Run(ClientSettings legacy, string? telemetryPath = null)
    {
        var target = telemetryPath ?? ImortaisTelemetryConfig.FilePath;
        if (File.Exists(target) || string.IsNullOrWhiteSpace(legacy.AgentKey)) return null;
        var config = new ImortaisTelemetryConfig
        {
            Enabled = true,
            ServerUrl = ImortaisProductionEndpoint.Migrate(string.IsNullOrWhiteSpace(legacy.ServerUrl) ? ImortaisProductionEndpoint.BaseUrl : legacy.ServerUrl.Trim()),
            AgentKey = legacy.AgentKey.Trim(),
            // Keep the alpha device id: the War Room already knows this PC by it.
            DeviceId = string.IsNullOrWhiteSpace(legacy.DeviceId) ? Environment.MachineName : legacy.DeviceId.Trim(),
            PlayerName = legacy.PlayerName?.Trim() ?? string.Empty,
        };
        GuildPairings.Normalize(config);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        ImortaisTelemetryConfig.SaveToFile(config, target);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return "Pareamento da versão alpha migrado para o formato do Windows (" + GuildIdentity.LabelForServer(config.ServerUrl) + ").";
    }
}
