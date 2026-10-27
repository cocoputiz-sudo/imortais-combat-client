using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StatisticsAnalysisTool.Imortais;

// Um pareamento por War Room. O destino ativo é escolhido pela guilda do personagem
// logado: personagem da IMORTAIS envia para a IMORTAIS, da Cavaleiros para a Cavaleiros.
// Guilda sem pareamento: nada é enviado (nunca manda dado de uma guilda ao War Room de outra).
public static class GuildPairings
{
    static string Key(string? url) => (url ?? string.Empty).Trim().TrimEnd('/').ToLowerInvariant();

    public static bool SameServer(string? a, string? b) => Key(a).Length > 0 && Key(a) == Key(b);

    // Instalações antigas: o pareamento único vira o primeiro da lista, com a fila que já usava.
    public static bool Normalize(ImortaisTelemetryConfig config)
    {
        config.Pairings ??= new List<GuildPairing>();
        var changed = false;
        if (config.Pairings.Count == 0 && !string.IsNullOrWhiteSpace(config.AgentKey) && !string.IsNullOrWhiteSpace(config.ServerUrl))
        {
            config.Pairings.Add(FromActive(config));
            changed = true;
        }
        config.Pairings.RemoveAll(p => string.IsNullOrWhiteSpace(p.ServerUrl) || string.IsNullOrWhiteSpace(p.AgentKey));
        return changed;
    }

    public static GuildPairing FromActive(ImortaisTelemetryConfig config) => new()
    {
        ServerUrl = config.ServerUrl,
        AgentKey = config.AgentKey,
        PlayerName = config.PlayerName,
        GuildDisplayName = config.GuildDisplayName,
        OwnGuildNames = config.OwnGuildNames?.ToList(),
        OutboxPath = string.IsNullOrWhiteSpace(config.OutboxPath) ? ImortaisTelemetryConfig.DefaultOutboxPath : config.OutboxPath,
        PairedAtUtc = null
    };

    // Fila própria por War Room; o primeiro pareamento mantém a fila original.
    public static string OutboxPathFor(ImortaisTelemetryConfig config, string serverUrl)
    {
        var existing = config.Pairings?.FirstOrDefault(p => SameServer(p.ServerUrl, serverUrl));
        if (!string.IsNullOrWhiteSpace(existing?.OutboxPath)) return existing!.OutboxPath!;
        if (config.Pairings == null || config.Pairings.Count == 0) return ImortaisTelemetryConfig.DefaultOutboxPath;
        var host = Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) ? uri.Host : serverUrl;
        var safe = new string(host.Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_').ToArray());
        return Path.Combine(Path.GetDirectoryName(ImortaisTelemetryConfig.DefaultOutboxPath) ?? ImortaisTelemetryConfig.DirectoryPath, "outbox-" + safe + ".ndjson");
    }

    // Parear de novo com o mesmo War Room substitui a chave dele; com outro, adiciona.
    public static GuildPairing Upsert(ImortaisTelemetryConfig config, GuildPairing pairing)
    {
        Normalize(config);
        var list = config.Pairings!;
        var index = list.FindIndex(p => SameServer(p.ServerUrl, pairing.ServerUrl));
        if (string.IsNullOrWhiteSpace(pairing.OutboxPath)) pairing.OutboxPath = OutboxPathFor(config, pairing.ServerUrl);
        if (index >= 0) list[index] = pairing; else list.Add(pairing);
        return pairing;
    }

    public static bool Remove(ImortaisTelemetryConfig config, string serverUrl)
    {
        Normalize(config);
        return config.Pairings!.RemoveAll(p => SameServer(p.ServerUrl, serverUrl)) > 0;
    }

    // Pareamento cujo War Room tem a guilda do personagem. Sem guildas informadas, vale a família IMORTAIS
    // (mesma regra do restante do client para instalações antigas).
    public static GuildPairing? SelectFor(ImortaisTelemetryConfig config, string? gameGuild)
    {
        if (string.IsNullOrWhiteSpace(gameGuild) || config.Pairings == null) return null;
        return config.Pairings.FirstOrDefault(p => GuildIdentity.IsOwnGuild(gameGuild, p.OwnGuildNames));
    }

    public static GuildPairing? Active(ImortaisTelemetryConfig config) =>
        config.Pairings?.FirstOrDefault(p => SameServer(p.ServerUrl, config.ServerUrl) && p.AgentKey == config.AgentKey);

    // Copia o pareamento escolhido para os campos que o envio usa.
    public static void Activate(ImortaisTelemetryConfig config, GuildPairing pairing)
    {
        config.ServerUrl = pairing.ServerUrl;
        config.AgentKey = pairing.AgentKey;
        config.PlayerName = pairing.PlayerName;
        config.GuildDisplayName = pairing.GuildDisplayName;
        config.OwnGuildNames = pairing.OwnGuildNames?.ToList();
        config.OutboxPath = string.IsNullOrWhiteSpace(pairing.OutboxPath) ? OutboxPathFor(config, pairing.ServerUrl) : pairing.OutboxPath;
        config.CtaEventId = null;
    }

    // O War Room ativo informou nome/guildas novas: guarda no pareamento dele.
    public static void SyncActive(ImortaisTelemetryConfig config)
    {
        var active = config.Pairings?.FirstOrDefault(p => SameServer(p.ServerUrl, config.ServerUrl));
        if (active == null) return;
        active.GuildDisplayName = config.GuildDisplayName;
        active.OwnGuildNames = config.OwnGuildNames?.ToList();
    }

    public static string Label(GuildPairing pairing) =>
        string.IsNullOrWhiteSpace(pairing.GuildDisplayName) ? GuildIdentity.LabelForServer(pairing.ServerUrl) : pairing.GuildDisplayName!;
}
