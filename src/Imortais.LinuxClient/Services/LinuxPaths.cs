namespace Imortais.LinuxClient.Services;

public static class LinuxPaths
{
    // GetFolderPath(UserProfile) can be "" in some sessions; HOME is the source of truth on Linux.
    // Never return a path relative to the current folder.
    public static string Home
    {
        get
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrWhiteSpace(home) && Path.IsPathRooted(home)) return home;
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.Create);
            return !string.IsNullOrWhiteSpace(profile) && Path.IsPathRooted(profile) ? profile : Path.GetTempPath();
        }
    }

    private static string Xdg(string variable, params string[] fallback)
    {
        var xdg = Environment.GetEnvironmentVariable(variable);
        return !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg) ? xdg : Path.Combine([Home, .. fallback]);
    }

    public static string ConfigDirectory => Path.Combine(Xdg("XDG_CONFIG_HOME", ".config"), "imortais-combat-client");
    public static string StateDirectory => Path.Combine(Xdg("XDG_STATE_HOME", ".local", "state"), "imortais-combat-client");
    public static string AutostartDirectory => Path.Combine(Xdg("XDG_CONFIG_HOME", ".config"), "autostart");
}
