# IMORTAIS Combat Client v0.6.1 — Guild Might de produção

## Telemetria
- Guild Might, via operações Photon GetGuildMightCategoryOverview e
  GetGuildMightCategoryContribution, utiliza exclusivamente o envio normal de
  `guild_might_probe` para `ServerUrl`, autenticado por `AgentKey`.
- O marker Photon de 64 bits é serializado como texto decimal; arrays completos
  (até 10.000 entradas) e valores bytes são preservados.
- Guild Challenge e operações de temporada
  `GetGuildChallengePoints`, `GetGvgSeasonContributionByActivity` e
  `GetGvgSeasonRankings` **não são enviadas à produção** nesta versão.
- O envio privado de homologação foi **retirado do código**. O client não contém
  URL, token, botão, fila nem worker de homologação.

## Dumps locais opcionais
Abra IMORTAIS → DIAGNÓSTICO → **GUILD DUMPS: DESLIGADO** para
ativar a cópia local de pacotes Photon Guild em:
`%LOCALAPPDATA%\IMORTAIS Combat Client\Diagnostics\guild-probes-YYYYMMDD-00.ndjson`.
A cópia é local e não transmite dados extras. **Might continua sendo enviado
à produção**, inclusive com dumps ligados. Challenge e Season ficam locais.
Desative após os testes. Não compartilhe `telemetry.json` ou chaves.

## Diagnóstico Might
- **Enviados**: número de eventos Might presentes em tentativas HTTPS
  (retentativas podem aumentar esse valor).
- **Aceitos pelo servidor**: eventos de Might cujos lotes receberam ACK completo
  (inseridos ou reconhecidos como duplicados pelo mesmo EventId).
- **Rejeitados/erros**: tentativas com HTTP de erro ou sem ACK completo. As
  tentativas continuam na fila para nova tentativa; não significa perda.
- **Pendentes**: eventos Might ainda não confirmados, incluindo outbox local.
- Os demais eventos, CTAs, party, combat e highlights continuam normais.

## Pacote candidato
A `v0.6.1` é compilada pelo workflow "Build v0.6.1 Candidate (sem publicacao)".
O arquivo de instalação aparece como **artifact privado da execução**, não
como Release nem atualização automática. O appcast público permanece idêntico
ao da main, e nenhuma publicação deve ocorrer sem autorização explícita.

## Antes de disponibilizar
Validar atualização Inno Setup a partir da v0.6.0, conservação de `AgentKey`
e preferências, transmissão do ranking das 14 categorias, operação dos demais
módulos e integridade do instalador SHA-256.

## Pré-requisito da Parte 2 — captura real de DiedEvent

O botão **GUILD DUMPS: LIGADO** também ativa temporariamente uma cópia
**local, passiva** de cada evento letal `DiedEvent` recebido pelo client.
O arquivo separado fica em
`%LOCALAPPDATA%\IMORTAIS Combat Client\Diagnostics\died-events-YYYYMMDD.ndjson`.

Antes de implementar o trigger de morte própria e abate em massa, conferir
duas ocorrências reais em Albion West, com o cliente de teste ligado:

1. **Morte própria** — morrer no jogo, localizar uma linha com
   `ownDeath:true` e `lethal:true`.
2. **Morte causada por aliado fora da party** — no mesmo cluster/alcance
   observado, testemunhar um abate de aliado fora da party e confirmar
   `killerInParty:false`, `lethal:true` e `killerGuild` coerente.
   O evento deve corresponder ao ocorrido; ausência de evento não pode ser
   mascarada por contagem de killings de party.

Se faltar qualquer cenário, **não ativar nem implementar o novo gatilho
baseado exclusivamente em DiedEvent**. Desligar os dumps após coletar.
Os arquivos podem conter nomes e IDs de jogo; verificar antes de compartilhar.
Isso não prova posição espacial exata, apenas que o evento chegou ao cliente.
