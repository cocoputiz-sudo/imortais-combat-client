using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace StatisticsAnalysisTool.Imortais;

// Guild Combat Client: qual guilda/War Room este PC atende e quais guildas do Albion contam como "nós".
// Sem informação do servidor, o padrão continua sendo a família IMORTAIS (instalações antigas).
public static class GuildIdentity
{
    public static readonly IReadOnlyList<string> DefaultOwnGuilds = new[] { "IMORTAIS", "IMORTAIS ACADEMY", "IMORTAIS 2" };

    public static readonly IReadOnlyList<KnownGuildServer> KnownServers = new[]
    {
        new KnownGuildServer("IMORTAIS", "https://imortais.macknacloud.com.br"),
        new KnownGuildServer("CAVALEIROS", "https://cavaleiros.macknacloud.com.br")
    };

    public static string Normalize(string? value)
    {
        return string.Concat((value ?? string.Empty).Where(char.IsLetterOrDigit)).ToLowerInvariant();
    }

    // ownGuilds == null: servidor ainda não informou (usa a família IMORTAIS).
    // ownGuilds vazio: servidor informou que não há guilda própria (só a party conta).
    public static bool IsOwnGuild(string? guild, IReadOnlyCollection<string>? ownGuilds)
    {
        var normalized = Normalize(guild);
        if (normalized.Length == 0) return false;
        var list = ownGuilds ?? DefaultOwnGuilds;
        return list.Any(x => Normalize(x) == normalized);
    }

    // Aceita o nome de uma guilda conhecida ou um endereço https do War Room.
    public static string? ResolveServerUrl(string? input)
    {
        var text = (input ?? string.Empty).Trim();
        if (text.Length == 0) return null;
        var known = KnownServers.FirstOrDefault(x =>
            string.Equals(x.Name, text, StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.Url, text.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
        if (known != null) return known.Url;
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.UserInfo.Length == 0
            && uri.Query.Length == 0 && uri.Fragment.Length == 0
            && (uri.AbsolutePath == "/" || uri.AbsolutePath.Length == 0))
        {
            return uri.GetLeftPart(UriPartial.Authority);
        }
        return null;
    }

    public static string LabelForServer(string? serverUrl)
    {
        var known = KnownServers.FirstOrDefault(x => string.Equals(x.Url, (serverUrl ?? string.Empty).TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
        return known?.Name ?? (serverUrl ?? string.Empty).Trim();
    }

    public static IReadOnlyList<string>? CleanList(IEnumerable<string?>? names)
    {
        if (names == null) return null;
        return names.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();
    }
}

public static class GuildIdentityResponse
{
    // Lê { guild: { name, ownGuilds: [...] } } da resposta do War Room. Retorna true se mudou algo.
    public static bool Apply(ImortaisTelemetryConfig config, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("guild", out var guild)
            || guild.ValueKind != JsonValueKind.Object)
            return false;

        var changed = false;
        if (guild.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
        {
            var name = nameProp.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(name) && name != config.GuildDisplayName)
            {
                config.GuildDisplayName = name;
                changed = true;
            }
        }

        if (guild.TryGetProperty("ownGuilds", out var ownProp) && ownProp.ValueKind == JsonValueKind.Array)
        {
            var list = GuildIdentity.CleanList(ownProp.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()))!.ToList();
            if (config.OwnGuildNames == null || !config.OwnGuildNames.SequenceEqual(list))
            {
                config.OwnGuildNames = list;
                changed = true;
            }
        }
        return changed;
    }
}

public sealed record KnownGuildServer(string Name, string Url);
