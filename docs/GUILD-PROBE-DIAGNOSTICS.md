# IMORTAIS Combat Client — diagnóstico local de Guild Might e Guild Challenge

Este recurso é **experimental** e está disponível no pacote de teste do PR #34. Ele é desativado por padrão.

## Como ativar no próprio Combat Client (novo)

1. Abra o Combat Client de teste e acesse a aba **IMORTAIS**.
2. No painel **DIAGNÓSTICO**, clique em **GUILD DUMPS: DESLIGADO**.
3. Confirme a operação. O botão exibirá **GUILD DUMPS: LIGADO**.
4. Abra o Albion West, navegue pelos rankings e clique em **ABRIR PASTA DE DUMPS** para localizar os arquivos gerados.
5. Clique em **GUILD DUMPS: LIGADO** para desativar a captura após o teste.

A preferência é salva no arquivo `telemetry.json` automaticamente. Não é necessário editar arquivos de configuração ou reiniciar o Combat Client.

### Alternativa manual

Se a opção não aparecer em uma versão antiga, o modo ainda pode ser configurado manualmente:
feche o Combat Client, edite `%LOCALAPPDATA%\\IMORTAIS Combat Client\\telemetry.json` e defina
`"GuildProbeLocalDiagnosticsEnabled": true` dentro do objeto JSON, mantendo as vírgulas.
Reabra o cliente. Para desligar, use `false`.

## Onde ficam os dumps?

`%LOCALAPPDATA%\IMORTAIS Combat Client\Diagnostics\guild-probes-YYYYMMDD-00.ndjson`

Arquivos sequenciais `-01`, `-02` etc. são usados para rotação, a aproximadamente 25 MiB por arquivo (até 100 partes por dia). Cada linha é um objeto JSON com `capturedAtUtc`, `direction`, `operationName`, `operationCode` e `parameters`.

O campo `parameters` preserva os parâmetros Photon **já decodificados pelo cliente**, com a estrutura e os valores brutos (incluindo arrays e bytes em base64). Isto **não** é o tráfego UDP criptografado, nem uma captura de pacote PCAP.

## Quais operações são capturadas?

- `GetGuildChallengePoints` (ranking de chavinhas)
- `GetGuildMightCategoryOverview`
- `GetGuildMightCategoryContribution`

O modo de diagnóstico é **local-only para essas três operações**: não chama o envio por telemetria, não utiliza outbox e não envia seus payloads ao servidor, inclusive em caso de erro no disco. Outras funções de telemetria do cliente **continuam funcionando**. Antes da validação de dumps reais, `GetGuildChallengePoints` permanece bloqueado para upload mesmo com o diagnóstico desativado.

## Instruções para o teste

1. Com o diagnóstico habilitado, abra o **Guild Challenge** e visualize o ranking completo.
2. Anote ou capture o nível da guilda e os três primeiros jogadores. A referência dos prints de 08/10/2026 é: nível 62; GiganteCarorra 5.817.978; ESTHER9950 5.072.517; JnK1 4.890.194.
3. Abra **todas as 14 categorias** de Guild Might e, quando disponíveis, seus rankings de contribuição, percorrendo possíveis páginas/scroll.
4. Feche o cliente e envie **apenas os arquivos `guild-probes-*.ndjson`** referentes ao período do teste.

**Privacidade:** os dumps podem conter identificadores de jogadores e valores arbitrários transmitidos pelo Albion. Revise os arquivos antes de compartilhar publicamente. Não envie `telemetry.json` (contém `AgentKey`), `outbox.ndjson` ou capturas de outros dados.

Esta coleta não efetua requests adicionais ao servidor do Albion: observa apenas as operações solicitadas pelo próprio jogo.
