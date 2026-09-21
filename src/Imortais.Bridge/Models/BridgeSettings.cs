namespace Imortais.Bridge.Models;
public sealed class BridgeSettings
{
    public string RailwayBaseUrl { get; set; } = "https://cta-imortais.up.railway.app";
    public string ApiKey { get; set; } = "";
    public string DeviceId { get; set; } = Environment.MachineName.ToLowerInvariant();
    public string PlayerName { get; set; } = "";
    public string GuildId { get; set; } = "683411304408416285";
    public long? CtaEventId { get; set; }
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
}
