# IMORTAIS Combat Client v0.6.1 — Guild Might de produção

## Telemetria
- Guild Might, via operações Photon GetGuildMightCategoryOverview e
  GetGuildMightCategoryContribution, utiliza exclusivamente o envio normal de
  `guild_might_probe` para `ServerUrl`, autenticado por `AgentKey`.
- O marker Photon de 64 bits é serializado como texto decimal; arrays completos
  (até 10.000 entradas) e valores bytes são preservados.
- Guild Challenge e operações de temporada (`GetGuildChallengePoints`,
  `GetGvgSeasonContributionByActivity` e `GetGvgSeasonRankings`) **são enviadas
  à produção** como `guild_might_probe` pela outbox autenticada, apenas quando
  observadas passivamente. O cliente não consulta Albion ativamente.
- O envio privado de homologação foi **retirado do código**. O client não contém
  URL, token, botão, fila nem worker de homologação.

## Dumps locais opcionais
Abra IMORTAIS → DIAGNÓSTICO → **GUILD DUMPS: DESLIGADO** para
ativar a cópia local de pacotes Photon Guild em:
`%LOCALAPPDATA%\IMORTAIS Combat Client\Diagnostics\guild-probes-YYYYMMDD-00.ndjson`.
A cópia é local e não transmite dados extras. **Might, Challenge e operações de temporada continuam sendo enviados
à produção** pela outbox autenticada, inclusive com dumps locais ligados.
O arquivo NDJSON é uma cópia de diagnóstico, não substitui a transmissão.
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

## Publicação v0.6.1 e atualização assinada
A versão oficial será publicada pelo script `scripts/publi-icc-v061.ps1`
após o merge do PR #34. O script compila a partir da `main`, utiliza
a chave Ed25519 já empregada nas versões anteriores, publica a Release
e atualiza o feed NetSparkle em `main`. O instalador gerado no CI serve
somente para testes, pois desativa a verificação de updates no build local.
A nova Release só está disponível quando **o script do proprietário concluir**
e o feed apontar explicitamente para a v0.6.1.

## Highlights de morte e abates em massa
O salvamento automático da morte do jogador e a categoria de abates em massa
ficam **desmarcados por padrão**. É necessário ativar a gravação em segundo plano
e marcar voluntariamente a caixa **Salvar quando eu MORRER** ou
**Salvar abates em massa**. A opção anterior **Salvar meus abates** permanece
independente e com o mesmo valor inicial da v0.6.0. Quando a opção de massa
estiver desligada, abates pessoais próximos podem continuar gerando um
replay de abates comuns, se a opção anterior estiver ativa. A quota de 10 GiB
inclui `Mortes/`, `Abates/` e `Abates em Massa/`.

O botão **GUILD DUMPS: LIGADO** também ativa uma cópia **local, passiva** de
cada `DiedEvent` letal recebido pelo client, gravada em
`%LOCALAPPDATA%\IMORTAIS Combat Client\Diagnostics\died-events-YYYYMMDD.ndjson`.
Essa cópia pode ajudar a diagnosticar falsos negativos, mas não é requisito
para instalar nem para usar o cliente com as novas opções desmarcadas.

A primeira homologação de morte própria será realizada pelo proprietário
em seu computador, com a opção marcada. A presença de `ownDeath:true` e
`lethal:true` confirma a observação do evento; também é necessário verificar
que o MP4 foi salvo e reproduz corretamente. Se falhar, manter a caixa
desativada e corrigir na versão posterior. Desative dumps após os testes
e proteja nomes e IDs dos jogadores antes de compartilhá-los.
