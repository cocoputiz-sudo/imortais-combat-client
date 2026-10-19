using System.Text.Json;
using Imortais.LinuxClient.Models;

namespace Imortais.LinuxClient.Services;

public sealed class SettingsStore
{
    private static string FilePath => Path.Combine(LinuxPaths.ConfigDirectory, "settings.json");

    public ClientSettings Load()
    {
        return LoadFromFile(FilePath);
    }

    internal static ClientSettings LoadFromFile(string path, Action<ClientSettings, string>? persist = null)
    {
        try
        {
            if (!File.Exists(path)) return new ClientSettings();
            var settings = JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(path)) ?? new ClientSettings();
            var url = ProductionEndpoint.Migrate(settings.ServerUrl);
            if (url != settings.ServerUrl)
            {
                settings.ServerUrl = url;
                try { (persist ?? SaveToFile)(settings, path); }
                catch { /* Retain loaded pairing and direct destination if persistence fails. */ }
            }
            return settings;
        }
        catch
        {
            return new ClientSettings();
        }
    }

    public void Save(ClientSettings settings)
    {
        SaveToFile(settings, FilePath);
        ApplyAutostart(settings.StartWithDesktop);
    }

    internal static void SaveToFile(ClientSettings settings, string path)
    {
        settings.ServerUrl = ProductionEndpoint.Migrate(settings.ServerUrl);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
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
