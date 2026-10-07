# IMORTAIS Combat Client

Cliente desktop da guilda **I M O R T A I S** para Albion Online, baseado no projeto open source **AlbionOnline-StatisticsAnalysis** e integrado ao **IMORTAIS War Room**.

> **Versão estável atual:** v0.6.0  
> **Plataforma estável:** Windows 10/11 x64  
> **Runtime:** .NET 10 / WPF  
> **Release oficial:** `IMORTAIS-Combat-Client-Setup-v0.6.0.exe`

O Combat Client reaproveita a captura passiva e os parsers Photon do Statistics Analysis para transformar dados observados no Albion em telemetria operacional da guilda. O cliente **não injeta código no Albion, não controla o personagem e não automatiza ações dentro do jogo**.

---

## v0.6.0

A v0.6.0 consolida três frentes principais:

### Highlights automáticos de abates

- replay buffer H.264 em memória;
- captura da janela do Albion via Windows Graphics Capture;
- encoder H.264 por hardware quando disponível;
- áudio somente do processo do Albion;
- abate pessoal letal pode salvar automaticamente:
  - **45 s antes**;
  - **15 s depois**;
- abates próximos são coalescidos no mesmo clipe;
- teto de **120 s por arquivo**;
- fila e snapshots com limites de pressão de memória;
- quota padrão de **10 GiB** para `IMORTAIS Highlights`;
- salvamento suspenso se não for possível preservar pelo menos 1 GiB livre;
- gravação vem **DESLIGADA por padrão**;
- migração one-shot da v0.6.0 força `GRAVAÇÃO: NÃO` mesmo se uma build experimental antiga tiver deixado `SIM` salvo.

O recurso é experimental. Em Windows 10 a captura pode mostrar a borda amarela do sistema. Ainda não foi validado em larga escala em máquinas fracas ou em todas as condições de ZvZ.

### Telemetria mais resiliente

- contexto de CTA consultado no máximo a cada **30 s**;
- ingest com backoff progressivo de **2 s até 60 s**;
- primeiro sucesso restaura o retry inicial de 2 s;
- HTTP 401 bloqueia reenvio repetitivo com a mesma chave;
- `ctaEventId` retornado pelo backend continua sendo a fonte de verdade;
- outbox persistente mantém eventos através de restart/falha de rede;
- Party snapshots repetidos são ignorados;
- Guild Presence, combate, loot e heartbeat permanecem integrados ao War Room.

### GuildMight experimental

A v0.6.0 também inicia a coleta passiva de Guild Might.

O cliente observa, sem criar requests próprios:

```text
GetGuildMightCategoryOverview
GetGuildMightCategoryContribution
```

São observados **request e response**, porque o identificador da categoria pode estar no request enquanto nomes, Might e demais dados podem aparecer na response.

Os eventos enviados ao War Room usam:

```text
guild_might_probe
```

O payload é saneado e deduplicado antes do envio.

O backend já:

- correlaciona request → response por dispositivo/operação;
- procura automaticamente arrays paralelos de `nomes[]` e `might[]`;
- inspeciona estruturas aninhadas;
- expõe diagnóstico editor-only para validar o layout real do Photon;
- mantém pesos de 07/10/2026 apenas como referência de validação.

**A v0.6.0 ainda não promete ranking/SP definitivo.** Primeiro o layout real será validado com tráfego vivo; depois os probes serão convertidos em snapshots tipados e histórico de contribuição.

---

## IMORTAIS STATUS

A Home IMORTAIS mostra o estado operacional real do cliente:

```text
WAR ROOM: CONECTADO
ALBION: CAPTURANDO
PERSONAGEM: BadMack
CTA: 19:20 · VINCULADO
PARTY: 20 detectados
TELEMETRIA: ENVIANDO / SINCRONIZADA
```

Também há diagnóstico detalhado com:

- versão instalada;
- estado do updater;
- última comunicação com War Room;
- Party snapshot e repetições ignoradas;
- Guild Presence;
- Guild Might Probe;
- outbox;
- erros recentes;
- estado do replay buffer;
- FPS WGC / submit / encoded;
- FrameBusy;
- uso do pool NV12;
- áudio do Albion;
- salvamentos automáticos e pressão de snapshots.

O botão **COPIAR DIAGNÓSTICO** produz um texto seguro para suporte e testes internos.

---

## Fluxo de dados

```text
Albion Online
    ↓
captura passiva socket/Npcap
    ↓
parser Photon / handlers
    ↓
ImortaisEventBridge
    ↓
Channel em memória
    ↓
outbox NDJSON persistente
    ↓ HTTPS
/api/telemetry/ingest
    ↓
Railway / PostgreSQL
    ↓
IMORTAIS War Room
```

O cliente também consulta:

```text
GET /api/telemetry/context
```

para resolver o CTA ativo e manter o contexto operacional sincronizado.

---

## Telemetria enviada

### Party

```text
party_snapshot
```

A lista observada é normalizada, deduplicada e ordenada. O War Room usa esses snapshots para comparar a Party real do Albion com as PTs planejadas no CTA.

### Guild Presence

```text
guild_presence_probe
```

Observa passivamente eventos de guilda e alimenta presença/last-seen sem transformar qualquer pacote isolado em verdade absoluta.

### Guild Might

```text
guild_might_probe
```

Probe experimental das operações de Overview/Contribution de Might, incluindo direção request/response e parâmetros Photon saneados.

### Loot

Eventos de loot incluem, conforme disponível:

- jogador;
- guilda;
- origem;
- item;
- quantidade;
- valor estimado;
- cluster/mapa.

### Damage e Healing

São acumulados localmente e enviados em janelas:

```text
combat_delta
```

com jogador, damage, healing e duração da janela.

### Combate

O cliente observa eventos de combate/morte e alimenta o War Room com dados usados para:

- deaths;
- kills;
- knockouts;
- Kill Fame;
- placares por guilda inimiga;
- análise de desempenho.

O mesmo caminho de morte letal também pode disparar Highlight quando o jogador local é o killer e a gravação estiver habilitada.

---

## Highlights

Pasta padrão:

```text
%USERPROFILE%\Videos\IMORTAIS Highlights
```

Configuração atual:

- replay buffer aproximado de 150 s;
- H.264 Main;
- B-frames desabilitados;
- GOP alvo de 120 frames;
- áudio AAC 48 kHz estéreo;
- pool NV12 limitado;
- snapshots e gravações serializados/protegidos contra crescimento ilimitado.

Nome automático:

```text
AAAA-MM-DD_HH-mm-ss_ABATE_<vitima>.mp4
AAAA-MM-DD_HH-mm-ss_ABATE_<vitima>_xN.mp4
```

Uma sequência de vários abates dentro do pós-roll estende o mesmo clipe em vez de gerar um arquivo por vítima.

---

## Configuração local

Telemetria:

```text
%LOCALAPPDATA%\IMORTAIS Combat Client\telemetry.json
```

Outbox:

```text
%LOCALAPPDATA%\IMORTAIS Combat Client\outbox.ndjson
```

Instalação padrão:

```text
%LOCALAPPDATA%\Programs\IMORTAIS Combat Client
```

Cada instalação trabalha com:

- `DeviceId`;
- personagem;
- token próprio do dispositivo;
- estado de conexão;
- associação ao CTA quando aplicável.

**Nunca distribua a chave mestre de ingestão do backend.** Os jogadores usam tokens próprios de dispositivo.

---

## Outbox e entrega

As threads de captura não fazem I/O de rede ou disco diretamente.

```text
captura
  ↓
Channel em memória
  ↓
append em outbox.ndjson
  ↓
lote FIFO
  ↓
POST /api/telemetry/ingest
  ↓
ACK
  ↓
remoção dos EventIds confirmados
```

Propriedades importantes:

- restart do client não perde eventos já persistidos;
- falha HTTP mantém o lote;
- os EventIds são preservados para deduplicação;
- troca do arquivo usa fluxo temporário/atômico;
- recuperação de `.tmp` ocorre no boot;
- limites padrão: **50.000 eventos / 50 MiB**;
- campos novos de configuração permanecem retrocompatíveis.

---

## Updater

O updater usa o feed oficial deste repositório e releases do GitHub.

Feed de produção:

```text
upstream/AlbionOnline-StatisticsAnalysis/src/StatisticsAnalysisTool/imortais-netsparkle-update-check.xml
```

A v0.6.0 foi validada no fluxo real:

```text
v0.5.9
  ↓ Verificar atualização
NetSparkle detecta v0.6.0
  ↓
validação Ed25519
  ↓
download do instalador
  ↓
fechamento do client
  ↓
instalação por cima
  ↓
relançamento em v0.6.0
```

### Segurança

O instalador distribuído pelo updater é validado por **Ed25519**.

- a chave pública fica incorporada na build;
- a chave privada de publicação não está no repositório;
- a release também publica SHA-256 do instalador;
- uma release existente não deve ser sobrescrita: correções futuras usam versão superior, por exemplo v0.6.1.

A assinatura Ed25519 do updater **não substitui Authenticode**. Como o projeto ainda não possui certificado Code Signing confiável do Windows, o SmartScreen pode exibir aviso de publicador desconhecido.

### Rollback

O NetSparkle não faz downgrade automático.

A release v0.5.9 e seus assets permanecem disponíveis para fallback manual. Se uma correção for necessária após a v0.6.0, o procedimento é publicar uma versão superior, como **v0.6.1**.

---

## Instalação

Para jogadores, use o instalador oficial da release:

```text
IMORTAIS-Combat-Client-Setup-v0.6.0.exe
```

Também são publicados:

```text
IMORTAIS-Combat-Client-Setup-v0.6.0.exe.sha256
IMORTAIS-Combat-Client-v0.6.0-win-x64.zip
```

O instalador é o caminho recomendado. O ZIP é destinado principalmente a diagnóstico/desenvolvimento.

---

## Linux

Existe uma linha nativa Linux em desenvolvimento/preview, separada da release Windows.

Ela **não faz parte da v0.6.0 estável** e continua em validação real no Ubuntu antes de ser anunciada para uso geral.

---

## Build para desenvolvimento

Pré-requisito:

```powershell
dotnet --version
```

Clone:

```powershell
git clone https://github.com/cocoputiz-sudo/imortais-combat-client.git
cd imortais-combat-client
```

Projeto WPF:

```text
upstream\AlbionOnline-StatisticsAnalysis\src\StatisticsAnalysisTool\StatisticsAnalysisTool.csproj
```

Build:

```powershell
dotnet build .\upstream\AlbionOnline-StatisticsAnalysis\src\StatisticsAnalysisTool\StatisticsAnalysisTool.csproj -c Release
```

Publish Windows x64 self-contained:

```powershell
dotnet publish .\upstream\AlbionOnline-StatisticsAnalysis\src\StatisticsAnalysisTool\StatisticsAnalysisTool.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true
```

Builds oficiais exigem a chave pública do updater.

---

## Publicação de release

A v0.6.0 usa:

```powershell
.\scripts\publi-icc-v060.ps1
```

O script oficial valida, entre outros pontos:

- metadados de versão;
- `ClientVersion` da telemetria;
- gravação OFF por padrão;
- Highlights e proteções de pressão;
- refresh de contexto em 30 s;
- backoff de ingest;
- GuildMight request/response;
- assinatura Ed25519;
- tamanho/hash do instalador;
- inexistência prévia da tag estável antes de publicar.

O script histórico `publi-icc-v059.ps1` não deve ser usado para publicar a v0.6.0.

---

## Estrutura do repositório

```text
upstream/
  AlbionOnline-StatisticsAnalysis/
    src/
      StatisticsAnalysisTool/
        Imortais/        # integração IMORTAIS
        Network/         # handlers IMORTAIS no app WPF
        Updater/
        Views/
        Common/

scripts/
docs/
tests/

legacy/
  v0.3-hooks/           # histórico não compilável

src/
  Imortais.Bridge/      # bridge histórica/de referência
```

### Fonte única de verdade

O código executável da integração IMORTAIS vive em:

```text
upstream/AlbionOnline-StatisticsAnalysis/src/StatisticsAnalysisTool/
```

Não reaplique cópias antigas de `ImortaisEventBridge` ou controllers sobre o fork.

---

## Backend / War Room

O backend `imortais-cta-bot` é responsável por:

- autenticar dispositivos;
- ingerir e deduplicar telemetria;
- associar eventos ao CTA;
- Party / attendance;
- Guild Presence;
- Guild Might experimental;
- combate, mortes, Kill Fame e placares;
- Loot Logger;
- contexto do CTA;
- diagnóstico de dispositivos;
- histórico operacional.

No GuildMight, o backend atual correlaciona request/response e tenta descobrir automaticamente o layout real dos parâmetros antes de promover os probes a snapshots definitivos.

---

## Segurança e escopo

O IMORTAIS Combat Client é observacional.

O projeto não pretende:

- movimentar personagem;
- executar skills;
- automatizar combate;
- automatizar decisões;
- modificar memória do Albion;
- injetar código no processo do jogo;
- criar requests de Might por conta própria.

A captura observa tráfego que o próprio cliente Albion produz/recebe.

Ferramentas de terceiros não são oficialmente suportadas pela Sandbox Interactive. O uso deve acompanhar as regras e alterações do Albion Online.

---

## Roadmap

### Entregue na v0.6.0

- telemetria Party/Loot/Combat;
- Guild Presence;
- outbox persistente;
- contexto automático de CTA;
- diagnóstico central;
- updater Ed25519;
- Highlights automáticos de abates;
- replay buffer com áudio do Albion;
- hardening de memória/fila/disco dos Highlights;
- polling reduzido e backoff de ingest;
- GuildMight passivo request/response.

### Próximos passos

- transformar GuildMight Probe em snapshots tipados;
- mapear categoria → nível/meta/SP;
- histórico de Might por jogador e período;
- ranking de contribuição e SP estimado;
- contribuição incremental por snapshots;
- segmentação de combate por fight;
- histórico consolidado por temporada;
- assinatura Authenticode;
- continuidade da validação Linux.

---

## Base upstream e licença

Projeto original:

```text
Triky313/AlbionOnline-StatisticsAnalysis
```

O código derivado do Statistics Analysis está sujeito à **GPLv3**.

Ao redistribuir ou modificar a parte derivada do upstream:

- preserve a licença aplicável;
- preserve os créditos;
- disponibilize o código-fonte correspondente conforme as obrigações da GPLv3.

Consulte também:

```text
LICENSE-NOTE.md
```

---

## Releases

Use sempre a seção **Releases** deste repositório para obter builds oficiais.

Para jogadores, prefira o instalador da release estável mais recente em vez de builds locais ou arquivos compartilhados fora do fluxo oficial.
