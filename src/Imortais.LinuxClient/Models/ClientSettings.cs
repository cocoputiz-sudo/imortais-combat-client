namespace Imortais.LinuxClient.Models;

public sealed class ClientSettings
{
    public string ServerUrl { get; set; } = "https://cta-imortais.up.railway.app";
    public string AgentKey { get; set; } = string.Empty;
    public string DeviceId { get; set; } = BuildDeviceId();
    public string PlayerName { get; set; } = string.Empty;
    public string? CurrentCtaId { get; set; }
    public bool StartCaptureAutomatically { get; set; } = true;
    public bool StartWithDesktop { get; set; } = false;

    private static string BuildDeviceId()
    {
        var machine = Environment.MachineName.Trim().ToLowerInvariant();
        var user = Environment.UserName.Trim().ToLowerInvariant();
        return $"linux-{machine}-{user}";
    }
}
