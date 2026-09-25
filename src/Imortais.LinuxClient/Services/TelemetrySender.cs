using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Imortais.LinuxClient.Models;

namespace Imortais.LinuxClient.Services;

public sealed class TelemetrySender : IDisposable
{
    public const string ClientVersion = "0.1.0-linux";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly Outbox _outbox;

    public TelemetrySender(Outbox outbox) => _outbox = outbox;

    public async Task<(bool Ok, string Message, string? AgentKey, string? PlayerName)> PairAsync(
        ClientSettings settings, string code, string playerName, CancellationToken ct = default)
    {
        try
        {
            var url = settings.ServerUrl.TrimEnd('/') + "/api/telemetry/pair";
            using var response = await _http.PostAsJsonAsync(url, new
            {
                code = code.Trim(),
                deviceId = settings.DeviceId,
                playerName = playerName.Trim()
            }, ct);

            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return (false, $"HTTP {(int)response.StatusCode}: {body}", null, null);

            using var doc = JsonDocument.Parse(body);
            var token = doc.RootElement.TryGetProperty("token", out var tokenNode) ? tokenNode.GetString() : null;
            var pairedPlayer = doc.RootElement.TryGetProperty("playerName", out var playerNode) ? playerNode.GetString() : playerName;
            return !string.IsNullOrWhiteSpace(token)
                ? (true, "Ativação concluída", token, pairedPlayer)
                : (false, "War Room não retornou token", null, null);
        }
        catch (Exception ex) { return (false, ex.Message, null, null); }
    }

    public async Task<(bool Ok, string Message, string? CtaId, string? CtaTime)> RefreshContextAsync(
        ClientSettings settings, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.AgentKey)) return (false, "Cliente não ativado", null, null);

        try
        {
            var url = settings.ServerUrl.TrimEnd('/') +
                      "/api/telemetry/context?deviceId=" + Uri.EscapeDataString(settings.DeviceId) +
                      "&playerName=" + Uri.EscapeDataString(settings.PlayerName);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AgentKey);
            using var response = await _http.SendAsync(req, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) return (false, $"HTTP {(int)response.StatusCode}", null, null);

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("cta", out var cta) || cta.ValueKind == JsonValueKind.Null)
                return (true, "Sem CTA ativo", null, null);

            var id = cta.TryGetProperty("id", out var idNode) ? idNode.GetString() : null;
            var time = cta.TryGetProperty("time", out var timeNode) ? timeNode.GetString() : null;
            return (true, "Contexto atualizado", id, time);
        }
        catch (Exception ex) { return (false, ex.Message, null, null); }
    }

    public async Task<(bool Ok, string Message)> FlushAsync(ClientSettings settings, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.AgentKey)) return (false, "Cliente não ativado");
        var events = await _outbox.PeekAsync();
        if (events.Count == 0) return (true, "Fila vazia");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, settings.ServerUrl.TrimEnd('/') + "/api/telemetry/ingest");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AgentKey);
            req.Content = JsonContent.Create(new
            {
                device = new
                {
                    deviceId = settings.DeviceId,
                    playerName = settings.PlayerName,
                    version = ClientVersion
                },
                ctaEventId = settings.CurrentCtaId,
                events
            });

            using var response = await _http.SendAsync(req, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) return (false, $"HTTP {(int)response.StatusCode}: {body}");

            await _outbox.AckAsync(events.Select(x => x.EventId).ToHashSet());
            return (true, $"{events.Count} evento(s) enviados");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public void Dispose() => _http.Dispose();
}
