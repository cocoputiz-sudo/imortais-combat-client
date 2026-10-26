using System.Text.Json;
using StatisticsAnalysisTool.Imortais;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FALHOU: " + message);
}

// Sem resposta do War Room: família IMORTAIS (instalações antigas continuam iguais).
Check(GuildIdentity.IsOwnGuild("Imortais Academy", null), "padrão inclui IMORTAIS ACADEMY");
Check(GuildIdentity.IsOwnGuild("IMORTAIS 2", null), "padrão inclui IMORTAIS 2");
Check(!GuildIdentity.IsOwnGuild("ARCH", null), "ARCH não é nossa");
Check(!GuildIdentity.IsOwnGuild("", null), "vazio não é nosso");

// War Room da Cavaleiros informou as guildas dela.
var cav = new[] { "Cavaleiros Avalonianos" };
Check(GuildIdentity.IsOwnGuild("cavaleiros avalonianos", cav), "Cavaleiros é nossa");
Check(!GuildIdentity.IsOwnGuild("IMORTAIS", cav), "IMORTAIS é de fora na Cavaleiros");
Check(!GuildIdentity.IsOwnGuild("IMORTAIS", Array.Empty<string>()), "lista vazia: nenhuma guilda própria");

// Seletor de guilda.
Check(GuildIdentity.ResolveServerUrl("imortais") == "https://imortais.macknacloud.com.br", "nome conhecido");
Check(GuildIdentity.ResolveServerUrl("CAVALEIROS") == "https://cavaleiros.macknacloud.com.br", "Cavaleiros conhecida");
Check(GuildIdentity.ResolveServerUrl("https://warroom.exemplo.com/") == "https://warroom.exemplo.com", "endereço próprio");
Check(GuildIdentity.ResolveServerUrl("http://inseguro.com") == null, "http recusado");
Check(GuildIdentity.ResolveServerUrl("https://a.com/caminho") == null, "caminho recusado");
Check(GuildIdentity.ResolveServerUrl("https://user:pw@a.com") == null, "credencial na URL recusada");
Check(GuildIdentity.ResolveServerUrl("qualquer coisa") == null, "texto solto recusado");
Check(GuildIdentity.LabelForServer("https://cavaleiros.macknacloud.com.br/") == "CAVALEIROS", "rótulo do servidor");

// Resposta do War Room aplicada na configuração.
var config = new ImortaisTelemetryConfig();
using (var doc = JsonDocument.Parse("{\"ok\":true,\"guild\":{\"key\":\"cavaleiros\",\"name\":\"CAVALEIROS\",\"ownGuilds\":[\"Cavaleiros Avalonianos\",\" \",\"cavaleiros avalonianos\"]}}"))
{
    Check(GuildIdentityResponse.Apply(config, doc.RootElement), "primeira resposta muda a configuração");
    Check(config.GuildDisplayName == "CAVALEIROS", "nome da guilda");
    Check(config.OwnGuildNames!.Count == 1, "lista limpa e sem duplicata");
    Check(!GuildIdentityResponse.Apply(config, doc.RootElement), "mesma resposta não regrava");
}
using (var doc = JsonDocument.Parse("{\"ok\":true}"))
{
    Check(!GuildIdentityResponse.Apply(config, doc.RootElement), "servidor antigo sem campo guild não muda nada");
    Check(config.OwnGuildNames!.Count == 1, "mantém o último valor");
}
using (var doc = JsonDocument.Parse("{\"guild\":{\"name\":\"X\",\"ownGuilds\":[]}}"))
{
    Check(GuildIdentityResponse.Apply(config, doc.RootElement), "lista vazia é informação");
    Check(config.OwnGuildNames!.Count == 0, "nenhuma guilda própria");
}

Console.WriteLine("GuildIdentity logic tests: OK");
