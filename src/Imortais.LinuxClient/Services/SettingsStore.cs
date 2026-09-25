using System.Text.Json;
using Imortais.LinuxClient.Models;

namespace Imortais.LinuxClient.Services;

public sealed class SettingsStore
{
    private static string FilePath => Path.Combine(LinuxPaths.ConfigDirectory, "settings.json");

    public ClientSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new ClientSettings();
            return JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(FilePath)) ?? new ClientSettings();
        }
        catch
        {
            return new ClientSettings();
        }
    }

    public void Save(ClientSettings settings)
    {
        Directory.CreateDirectory(LinuxPaths.ConfigDirectory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        ApplyAutostart(settings.StartWithDesktop);
    }

    private static void ApplyAutostart(bool enabled)
    {
        var path = Path.Combine(LinuxPaths.AutostartDirectory, "imortais-combat-client.desktop");
        if (!enabled)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            return;
        }

        Directory.CreateDirectory(LinuxPaths.AutostartDirectory);
        File.WriteAllText(path,
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Name=IMORTAIS Combat Client\n" +
            "Comment=Observer de combate da guilda IMORTAIS\n" +
            "Exec=imortais-combat-client --background\n" +
            "Icon=imortais-combat-client\n" +
            "Terminal=false\n" +
            "X-GNOME-Autostart-enabled=true\n");
    }
}
