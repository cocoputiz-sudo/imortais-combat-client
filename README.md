# Guild Combat Client

Cliente desktop para Albion Online que roda junto com o jogo e envia ao **War Room** da guilda o que o personagem observa: presença, party, mapa, combate, mortes, loot e Guild Might. Criado pela guilda **IMORTAIS** e baseado no projeto open source **AlbionOnline-StatisticsAnalysis**.

> Antigo **IMORTAIS Combat Client**. Desde a v0.7.0 o mesmo app atende qualquer guilda com War Room.

> **Versão estável atual:** v0.7.2  
> **Plataforma estável:** Windows 10/11 x64  
> **Runtime:** .NET 10 / WPF  
> **Download Windows:** [Baixar instalador](https://github.com/cocoputiz-sudo/imortais-combat-client/releases/latest/download/IMORTAIS-Combat-Client-Setup-v0.7.2.exe)  
> **Notas e arquivos:** [Release estável mais recente](https://github.com/cocoputiz-sudo/imortais-combat-client/releases/latest)  
> **War Rooms:** [IMORTAIS](https://imortais.macknacloud.com.br) · [CAVALEIROS AVALONIANOS](https://cavaleiros.macknacloud.com.br)

O client reaproveita a captura passiva (Npcap) e os parsers Photon do Statistics Analysis. Ele **não injeta código no Albion, não lê a memória do jogo, não controla o personagem e não automatiza ações dentro do jogo**.

---

## Como usar

1. Instale com o instalador acima. Quem já tem o client atualiza pelo botão **ATUALIZAR AGORA**.
2. No War Room da sua guilda, quem gerencia **Dispositivos** gera um código de 6 dígitos.
3. No client, abra a aba **GUILD**, escolha a guilda (**IMORTAIS**, **CAVALEIROS** ou o endereço https do War Room da sua guilda) e digite o código.
4. Abra o Albion. A aba GUILD mostra o estado do War Room, do jogo, do personagem e do CTA.

### Mais de uma guilda no mesmo PC
- Um pareamento por guilda: o mesmo PC pode estar pareado com a IMORTAIS e com a Cavaleiros. Ativar outra guilda adiciona, não substitui.
- O envio segue a **guilda do personagem logado**. Trocar de personagem não exige parear de novo.
- Cada War Room tem chave e fila próprias. Personagem numa guilda sem pareamento: nada é enviado.
- A guilda é registrada assim que o jogo confirma a entrada no mundo, antes de qualquer outro evento, então a lista de membros online do login vai para o War Room certo.

### Confirmador de guilda (aba GUILD)
- **Verde:** a guilda do personagem no jogo é uma das guildas do War Room pareado.
- **Vermelho:** o personagem está em outra guilda; o client mostra as duas e orienta a parear com o War Room certo.
- **Cinza:** aguardando o jogo informar a guilda.

Em **Dispositivos**, o War Room mostra a "Guilda no jogo" de cada PC e também filtra a presença pela guilda informada.

---

## O que o client envia

| Evento | Uso no War Room |
|---|---|
| `party_snapshot` | Compara a Party real do jogo com as PTs escaladas no CTA (auditoria de PT) |
| `guild_presence_probe` | Presença e last-seen dos membros da guilda |
| heartbeat | Estado do dispositivo e guilda do personagem |
| combate e mortes | Kills, deaths, knockouts e placares por guilda; cada morte leva o equipamento e a aliança da vítima e do killer, para estimar o **Kill Fame** na hora e conferir com a API oficial |
| `combat_delta` | Dano e cura por janela |
| loot | Jogador, guilda, origem, item, quantidade, valor estimado e mapa |
| `guild_might_probe` | Guild Might e Guild Challenge, observados passivamente (o client não cria requests) |

O client também consulta `GET /api/telemetry/context` (no máximo a cada 30 s) para saber o CTA ativo. O `ctaEventId` devolvido pelo servidor é a fonte de verdade.

A lista de guildas que contam como "nós" (a guilda principal e as guildas irmãs, se houver) chega do War Room no pareamento e em cada envio.

---

## Highlights (gravação de vídeo)

Experimental e **desligado por padrão**.

- Replay buffer H.264 em memória (cerca de 150 s), captura da janela do Albion por Windows Graphics Capture, encoder por hardware quando disponível e áudio só do processo do Albion.
- Três pastas em `%USERPROFILE%\Videos\IMORTAIS Highlights`:
  - `Abates` (ligado quando a gravação está ativa);
  - `Abates em Massa` (desmarcado por padrão);
  - `Mortes` (desmarcado por padrão).
- Abate pessoal salva **45 s antes e 15 s depois**. Abates próximos viram um clipe só (teto de 120 s por arquivo).
- Nomes: `AAAA-MM-DD_HH-mm-ss_ABATE_<vitima>.mp4`, `..._ABATES_EM_MASSA.mp4`, `..._MORTE_por_<killer>.mp4`.
- Quota padrão de **10 GiB** (somando as subpastas); o salvamento para se não sobrar pelo menos 1 GiB livre.
- No Windows 10 a captura pode mostrar a borda amarela do sistema.

---

## Death Review

Relatório **local** da própria morte, para estudar o uso de defensivas.

- Vem pré-habilitado depois da ciência inicial.
- Guarda os últimos 45 s de dano recebido e o equipamento conhecido em `%LOCALAPPDATA%\IMORTAIS Combat Client\Diagnostics\DeathReview\` (últimos 30 arquivos). O botão **ABRIR DEATH REVIEWS** abre a pasta.
- **NÃO há envio automático** de morte, vídeo ou dados de casts para o War Room nem para qualquer outro lugar.
- Limite atual: o índice de magia registrado ainda não é o nome da skill (Q, W, E, peito, capacete, botas, poção). Identificar isso é a próxima etapa.

Detalhes em [docs/death-combat-review-phase1.md](docs/death-combat-review-phase1.md).

---

## Status e diagnóstico

A aba GUILD mostra o estado real:

```text
WAR ROOM: CONECTADO
ALBION: CAPTURANDO
PERSONAGEM: NomeDoPersonagem
CTA: 19:20 · VINCULADO
PARTY: 20 detectados
TELEMETRIA: ENVIANDO / SINCRONIZADA
```

O diagnóstico detalhado traz versão, updater, última comunicação, party, presença, Guild Might, outbox, erros recentes, replay buffer, áudio, salvamentos e o confirmador de guilda. O botão **COPIAR DIAGNÓSTICO** gera um texto seguro para suporte.

---

## Fluxo de dados

```text
Albion Online
    ↓ captura passiva (Npcap)
parser Photon / handlers
    ↓
ImortaisEventBridge → Channel em memória
    ↓
outbox NDJSON persistente (uma fila por War Room)
    ↓ HTTPS
/api/telemetry/ingest
    ↓
War Room da guilda (MackNa Cloud / PostgreSQL)
```

### Outbox e entrega
- As threads de captura não fazem I/O de rede ou disco.
- Lote FIFO enviado ao War Room e removido só depois do ACK; restart ou falha de rede não perde eventos já gravados.
- Backoff progressivo de 2 s até 60 s; HTTP 401 interrompe reenvio com a mesma chave.
- EventIds preservados para deduplicação; troca de arquivo atômica e recuperação de `.tmp` no boot.
- Limites padrão: **50.000 eventos / 50 MiB**.

---

## Configuração local

| O quê | Onde |
|---|---|
| Instalação | `%LOCALAPPDATA%\Programs\IMORTAIS Combat Client` |
| Configuração e pareamentos | `%LOCALAPPDATA%\IMORTAIS Combat Client\telemetry.json` |
| Fila de envio | `%LOCALAPPDATA%\IMORTAIS Combat Client\outbox.ndjson` |
| Highlights | `%USERPROFILE%\Videos\IMORTAIS Highlights` |
| Death Review | `%LOCALAPPDATA%\IMORTAIS Combat Client\Diagnostics\DeathReview` |

Os nomes de pasta, o executável, o identificador de instalação e o feed continuam com "IMORTAIS" de propósito: assim a atualização acontece no lugar, mantendo AgentKey, DeviceId, pareamentos, servidor, preferências e fila local.

Cada dispositivo usa token próprio. **Nunca distribua a chave mestre de ingestão do War Room.**

---

## Updater

- Feed oficial: `upstream/AlbionOnline-StatisticsAnalysis/src/StatisticsAnalysisTool/imortais-netsparkle-update-check.xml`, com releases do GitHub.
- O instalador é validado por **Ed25519**. A chave pública fica na build; a chave privada fica só no PC de publicação e nunca entra no repositório.
- Cada release publica também o SHA-256 do instalador.
- O NetSparkle não faz downgrade. Correção sempre sai como versão superior; uma release existente nunca é sobrescrita.
- A assinatura Ed25519 não substitui Authenticode: sem certificado Code Signing, o SmartScreen pode avisar "publicador desconhecido".

---

## Linux

Há uma linha nativa para Linux em validação no Ubuntu, separada da release Windows. Ela **não faz parte da release estável**. Os PRs #29, #39, #40, #54, #55 e #57 aguardam essa decisão.

---

## Build para desenvolvimento

```powershell
git clone https://github.com/cocoputiz-sudo/imortais-combat-client.git
cd imortais-combat-client
dotnet build .\upstream\AlbionOnline-StatisticsAnalysis\src\StatisticsAnalysisTool\StatisticsAnalysisTool.csproj -c Release
```

Publish Windows x64 self-contained:

```powershell
dotnet publish .\upstream\AlbionOnline-StatisticsAnalysis\src\StatisticsAnalysisTool\StatisticsAnalysisTool.csproj `
  -c Release -r win-x64 --self-contained true
```

Testes de lógica em `tests/` (ex.: `dotnet run --project tests/GuildIdentity.LogicTests/GuildIdentity.LogicTests.csproj -c Release`). Builds oficiais exigem a chave pública do updater.

---

## Publicação de release

Cada versão tem seus scripts em `scripts/`:

- `build-icc-vXXX-candidate.ps1`: monta o instalador candidato;
- `test-icc-vXXX-upgrade.ps1`: testa a atualização no lugar a partir da versão anterior;
- `publi-icc-vXXX.ps1`: publica a release e o feed assinados;
- `resume-icc-vXXX.ps1`: retoma uma publicação interrompida.

A nota da versão em `docs/releases/` precisa conter "DEATH REVIEW PRÉ-HABILITADO" e "NÃO há envio automático".

A publicação roda no Windows autorizado, com as **chaves de assinatura já existentes** (nunca gerar novas):

```powershell
irm https://raw.githubusercontent.com/cocoputiz-sudo/imortais-combat-client/main/scripts/publi-icc-v0XX.ps1 | iex
```

A v0.7.2 já foi publicada; não rode de novo o publicador dela. Depois de publicar, teste o **ATUALIZAR AGORA** a partir da versão anterior antes de anunciar.

---

## Estrutura do repositório

```text
upstream/AlbionOnline-StatisticsAnalysis/src/StatisticsAnalysisTool/
  Imortais/        # integração com o War Room, highlights, death review, guilda
  Network/         # handlers Photon
  Updater/
  Views/
scripts/           # build, teste de atualização e publicação por versão
docs/              # notas de release e documentos técnicos
tests/             # testes de lógica
legacy/            # histórico não compilável (v0.3 hooks)
src/Imortais.Bridge/  # bridge histórica, só referência
```

O código executável da integração vive em `upstream/.../StatisticsAnalysisTool/`. Não reaplique cópias antigas de `ImortaisEventBridge` sobre o fork.

---

## Backend / War Room

O backend fica em [`imortais-cta-bot`](https://github.com/cocoputiz-sudo/imortais-cta-bot) e roda na MackNa Cloud, um serviço e um banco por guilda. Ele autentica dispositivos, ingere e deduplica a telemetria, associa os eventos ao CTA e alimenta presença, auditoria de PT, combate, Kill Fame, loot, Guild Might e Guild Challenge.

---

## Segurança e escopo

O client é só observacional. Ele não movimenta o personagem, não usa skills, não automatiza combate ou decisões, não modifica a memória do Albion, não injeta código no jogo e não cria requests próprios. A captura observa o tráfego que o próprio cliente do Albion recebe.

Ferramentas de terceiros não são suportadas oficialmente pela Sandbox Interactive. O uso deve acompanhar as regras do Albion Online.

---

## Roadmap

### Entregue
- Telemetria de party, presença, combate, loot e Guild Might; outbox persistente; contexto automático de CTA.
- Updater Ed25519 e diagnóstico central.
- Highlights de abates, abates em massa e morte.
- Mortes com equipamento e aliança (Kill Fame no Battle Report).
- Várias guildas: pareamento por guilda, troca automática pelo personagem, confirmador de guilda e guilda já no login.
- Death Review local.

### Próximos passos
- Death Combat Review: identificar casts inimigos e skills usadas (Q, W, E, peito, capacete, botas, poção).
- Highlights sem gravar o mesmo trecho várias vezes quando abate, abate em massa e morte se sobrepõem.
- Regear automático no War Room a partir das mortes enviadas.
- Assinatura Authenticode.
- Decisão sobre a versão Linux.

---

## Base upstream e licença

Projeto original: `Triky313/AlbionOnline-StatisticsAnalysis`. O código derivado está sob **GPLv3**: ao redistribuir ou modificar, preserve a licença e os créditos e disponibilize o código-fonte correspondente. Veja também `LICENSE-NOTE.md`.

Use sempre a seção **Releases** deste repositório para obter builds oficiais.
