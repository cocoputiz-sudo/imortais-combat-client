using System.IO;
using System.Text.Json;
using Imortais.Bridge.Models;
namespace Imortais.Bridge.Services;
public sealed class SettingsStore
{
    private readonly string _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IMORTAIS", "CombatClient");
    private string FilePath => Path.Combine(_dir, "settings.json");
    public BridgeSettings Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<BridgeSettings>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch { return new(); }
    }
    public void Save(BridgeSettings settings)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}

