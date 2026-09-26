namespace Imortais.LinuxClient.Services;

public static class LinuxPaths
{
    public static string ConfigDirectory
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            return !string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(xdg, "imortais-combat-client")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "imortais-combat-client");
        }
    }

    public static string StateDirectory
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            return !string.IsNullOrWhiteSpace(xdg)
                ? Path.Combine(xdg, "imortais-combat-client")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state", "imortais-combat-client");
        }
    }

    public static string AutostartDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart");
}
