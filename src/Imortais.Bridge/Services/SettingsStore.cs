using System.IO;
using System.Text.Json;
using Imortais.Bridge.Models;
namespace Imortais.Bridge.Services;
public sealed class SettingsStore
{
    private readonly string _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IMORTAIS", "CombatClient");
    public SettingsStore() { }
    internal SettingsStore(string directory) { _dir = directory; }
    private string FilePath => Path.Combine(_dir, "settings.json");
    public BridgeSettings Load()
    {
        try
        {
            var settings = File.Exists(FilePath) ? JsonSerializer.Deserialize<BridgeSettings>(File.ReadAllText(FilePath)) ?? new() : new();
            var url = StatisticsAnalysisTool.Imortais.ImortaisProductionEndpoint.Migrate(settings.RailwayBaseUrl);
            if (url != settings.RailwayBaseUrl)
            {
                settings.RailwayBaseUrl = url;
                try { Save(settings); } catch { /* Keep loaded pairing if persistence fails. */ }
            }
            return settings;
        }
        catch { return new(); }
    }
    public void Save(BridgeSettings settings)
    {
        settings.RailwayBaseUrl = StatisticsAnalysisTool.Imortais.ImortaisProductionEndpoint.Migrate(settings.RailwayBaseUrl);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}

