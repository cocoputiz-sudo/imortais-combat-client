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

// Confirmador: guilda no jogo x War Room pareado.
var link = GuildIdentity.DescribeLink("Cavaleiros Avalonianos", cav, "CAVALEIROS");
Check(link.State == GuildLinkState.Match && link.Linked == true, "Cavaleiros no War Room da Cavaleiros: vinculado");
link = GuildIdentity.DescribeLink("IMORTAIS", cav, "CAVALEIROS");
Check(link.State == GuildLinkState.Mismatch && link.Linked == false && link.Text.Contains("IMORTAIS") && link.Text.Contains("CAVALEIROS"), "IMORTAIS no War Room da Cavaleiros: guilda diferente");
link = GuildIdentity.DescribeLink("Imortais Academy", null, "IMORTAIS");
Check(link.State == GuildLinkState.Match, "família IMORTAIS sem resposta do servidor: vinculado");
link = GuildIdentity.DescribeLink("", cav, "CAVALEIROS");
Check(link.State == GuildLinkState.Waiting && link.Linked == null, "sem guilda no jogo: aguardando");
link = GuildIdentity.DescribeLink("Qualquer", Array.Empty<string>(), "CAVALEIROS");
Check(link.State == GuildLinkState.Unknown && link.Linked == null, "servidor sem guildas próprias: indefinido");

// Pareamentos por guilda: o destino segue a guilda do personagem logado.
const string Imo = "https://imortais.macknacloud.com.br", Cav = "https://cavaleiros.macknacloud.com.br";
var cfg = new ImortaisTelemetryConfig { ServerUrl = Imo, AgentKey = "k-imo", PlayerName = "BadMack",
    OwnGuildNames = new List<string> { "IMORTAIS", "IMORTAIS ACADEMY", "IMORTAIS 2" }, OutboxPath = ImortaisTelemetryConfig.DefaultOutboxPath };
Check(GuildPairings.Normalize(cfg) && cfg.Pairings!.Count == 1, "pareamento antigo vira o primeiro da lista");
Check(cfg.Pairings![0].OutboxPath == ImortaisTelemetryConfig.DefaultOutboxPath, "primeiro pareamento mantém a fila original");
Check(!GuildPairings.Normalize(cfg), "normalizar de novo não muda nada");
var cavPairing = GuildPairings.Upsert(cfg, new GuildPairing { ServerUrl = Cav, AgentKey = "k-cav", PlayerName = "VickDigarista",
    GuildDisplayName = "Cavaleiros Avalonianos", OwnGuildNames = new List<string> { "Cavaleiros Avalonianos" } });
Check(cfg.Pairings!.Count == 2, "parear com outra guilda adiciona");
Check(cavPairing.OutboxPath != null && cavPairing.OutboxPath != ImortaisTelemetryConfig.DefaultOutboxPath && cavPairing.OutboxPath.Contains("cavaleiros"), "fila própria da Cavaleiros");
Check(GuildPairings.SelectFor(cfg, "Imortais Academy")?.AgentKey == "k-imo", "personagem da IMORTAIS ACADEMY vai para a IMORTAIS");
Check(GuildPairings.SelectFor(cfg, "IMORTAIS 2")?.AgentKey == "k-imo", "qualquer personagem da família IMORTAIS vai para a IMORTAIS");
Check(GuildPairings.SelectFor(cfg, "cavaleiros avalonianos")?.AgentKey == "k-cav", "personagem da Cavaleiros vai para a Cavaleiros");
Check(GuildPairings.SelectFor(cfg, "ARCH") == null, "guilda sem pareamento: nenhum destino");
Check(GuildPairings.SelectFor(cfg, null) == null, "sem guilda no jogo: nenhum destino");
GuildPairings.Activate(cfg, GuildPairings.SelectFor(cfg, "Cavaleiros Avalonianos")!);
Check(cfg.ServerUrl == Cav && cfg.AgentKey == "k-cav" && cfg.OutboxPath == cavPairing.OutboxPath && cfg.PlayerName == "VickDigarista", "ativar troca destino, chave e fila");
Check(GuildPairings.Active(cfg)?.AgentKey == "k-cav", "ativo identificado");
cfg.OwnGuildNames = new List<string> { "Cavaleiros Avalonianos", "Cavaleiros Academy" };
GuildPairings.SyncActive(cfg);
Check(cfg.Pairings!.First(p => p.AgentKey == "k-cav").OwnGuildNames!.Count == 2, "guildas novas do War Room ativo ficam no pareamento dele");
Check(cfg.Pairings!.First(p => p.AgentKey == "k-imo").OwnGuildNames!.Count == 3, "o outro pareamento não muda");
GuildPairings.Upsert(cfg, new GuildPairing { ServerUrl = Cav + "/", AgentKey = "k-cav2", OwnGuildNames = new List<string> { "Cavaleiros Avalonianos" } });
Check(cfg.Pairings!.Count == 2 && cfg.Pairings.Any(p => p.AgentKey == "k-cav2") && cfg.Pairings.First(p => p.AgentKey == "k-cav2").OutboxPath == cavPairing.OutboxPath,
    "parear de novo com o mesmo War Room substitui a chave e mantém a fila");
Check(GuildPairings.Remove(cfg, Cav) && cfg.Pairings!.Count == 1, "remover pareamento");
var legacy = new ImortaisTelemetryConfig { ServerUrl = Imo, AgentKey = "old", OwnGuildNames = null };
GuildPairings.Normalize(legacy);
Check(GuildPairings.SelectFor(legacy, "IMORTAIS")?.AgentKey == "old", "instalação antiga sem guildas informadas: família IMORTAIS");
Check(GuildPairings.SelectFor(legacy, "Cavaleiros Avalonianos") == null, "instalação antiga não manda Cavaleiros para a IMORTAIS");
var empty = new ImortaisTelemetryConfig { AgentKey = "" };
Check(!GuildPairings.Normalize(empty) && empty.Pairings!.Count == 0, "sem chave: nenhum pareamento");

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
