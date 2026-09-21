using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Imortais.Bridge.Models;

namespace Imortais.Bridge.Services;
public sealed class TelemetrySender : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly Outbox _outbox;
    public TelemetrySender(Outbox outbox) => _outbox = outbox;
    public async Task<(bool Ok,string Message)> FlushAsync(BridgeSettings s, CancellationToken ct=default)
    {
        if (string.IsNullOrWhiteSpace(s.RailwayBaseUrl) || string.IsNullOrWhiteSpace(s.ApiKey)) return (false,"Configure servidor e chave.");
        var events=await _outbox.PeekAsync(100); if(events.Count==0) return (true,"Fila vazia");
        using var req=new HttpRequestMessage(HttpMethod.Post, s.RailwayBaseUrl.TrimEnd('/')+"/api/telemetry/ingest");
        req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",s.ApiKey);
        req.Content=JsonContent.Create(new {
            device=new { deviceId=s.DeviceId, guildId=s.GuildId, playerName=s.PlayerName, version="0.2.0" },
            sessionId=s.SessionId, ctaEventId=s.CtaEventId, events
        });
        try {
            using var res=await _http.SendAsync(req,ct); var body=await res.Content.ReadAsStringAsync(ct);
            if(!res.IsSuccessStatusCode) return(false,$"HTTP {(int)res.StatusCode}: {body}");
            await _outbox.AckAsync(events.Select(x=>x.EventId).ToHashSet()); return(true,$"{events.Count} evento(s) enviados");
        } catch(Exception ex) { return(false,ex.Message); }
    }
    public void Dispose()=>_http.Dispose();
}
