# IMORTAIS Combat Client — diagnóstico local de Guild Might e Guild Challenge

Este recurso é **experimental** e está disponível no pacote de teste do PR #34. Ele é desativado por padrão.

## Como ativar (Windows, sem linha de comando)

1. Feche o IMORTAIS Combat Client.
2. Pressione **Win + R**, cole `%LOCALAPPDATA%\IMORTAIS Combat Client` e confirme.
3. Abra `telemetry.json` no Bloco de Notas. Se não existir, abra o cliente uma vez e feche-o para criar o arquivo.
4. Dentro do objeto JSON, adicione a propriedade `"GuildProbeLocalDiagnosticsEnabled": true`. Separe as propriedades com vírgulas e preserve as demais configurações e chaves.
5. Salve o arquivo e abra novamente o **build de teste** do Combat Client. Entre no Albion Online (West) e abra o ranking de Guild Challenge e as categorias de Guild Might.

Para desligar, feche o cliente, altere a propriedade para `false` e reinicie. Se a chave não existir, o padrão é **desligado**.

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
