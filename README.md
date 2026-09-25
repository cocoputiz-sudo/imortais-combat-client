# IMORTAIS Combat Client

Cliente desktop da guilda **I M O R T A I S** para Albion Online, baseado no projeto open source **AlbionOnline-StatisticsAnalysis** e integrado ao **IMORTAIS War Room**.

O objetivo do projeto é aproveitar a captura passiva e os parsers já existentes do Statistics Analysis para transformar dados observados no jogo em telemetria útil para organização de CTA, conferência de party, loot e desempenho de combate.

> **Versão publicada atual:** v0.5.5  
> **Plataforma:** Windows 10/11 x64  
> **Runtime/build:** .NET 10 / WPF

---

## Estado atual

O projeto deixou de ser apenas uma bridge de demonstração. Hoje o fork do Statistics Analysis já contém a integração IMORTAIS diretamente no cliente desktop.

A linha v0.4.9 inclui:

- identidade visual **IMORTAIS COMBAT CLIENT**;
- ícone próprio no executável, janela e instalador;
- detecção normal do personagem, guilda, mapa e dados do Albion;
- painel **IMORTAIS STATUS** no cabeçalho;
- conexão ao War Room;
- resolução automática do CTA ativo;
- envio de Party, Loot, Damage, Healing e resultados de combate;
- telemetria agregada para reduzir volume de eventos;
- autenticação por token do dispositivo;
- updater próprio baseado em releases do repositório IMORTAIS;
- validação Ed25519 do instalador de atualização;
- janela de atualização com progresso de download;
- aviso antes do fechamento do client;
- instalação e relançamento do Combat Client.

A captura continua sendo passiva. O projeto não injeta código no Albion e não automatiza ações do jogador.

---

## IMORTAIS STATUS

O cabeçalho do cliente mostra o estado operacional da integração:

```text
IMORTAIS STATUS

● WAR ROOM: conectado
● ALBION: capturando
● CTA: 21:20 · vinculado
● PARTY: 20 jogadores
● TELEMETRIA: enviando / sincronizada
```

Esses indicadores são alimentados pelo estado real do cliente e pela comunicação com o War Room.

O contexto do CTA é consultado automaticamente no backend usando o jogador e o dispositivo configurados. Quando um CTA é identificado, o ID é mantido localmente para que os eventos enviados sejam associados ao CTA correto.

---

## Fluxo de dados

```text
Albion Online
    ↓
Statistics Analysis
(socket/Npcap + parser Photon)
    ↓
Party / Loot / Combat handlers
    ↓
ImortaisEventBridge
    ↓
fila local em memória
    ↓ HTTPS
/api/telemetry/ingest
    ↓
Railway / PostgreSQL
    ↓
IMORTAIS War Room
```

O client também consulta:

```text
GET /api/telemetry/context
```

para descobrir o CTA ativo e manter o estado exibido no painel.

---

## Eventos enviados

### Party

O snapshot da party observada é enviado como:

```text
party_snapshot
```

A lista é normalizada, deduplicada e ordenada antes do envio.

No War Room isso é usado para comparar a party real do Albion com as PTs planejadas no CTA.

### Loot

O evento de loot inclui, entre outros dados:

- jogador que pegou o item;
- guilda do jogador;
- origem do loot;
- item;
- quantidade;
- valor estimado;
- mapa/cluster.

A guilda observada também é enviada para permitir validações relacionadas a **I M O R T A I S** no backend.

### Damage e Healing

Damage e Healing são acumulados localmente por jogador e enviados em janelas de tempo como:

```text
combat_delta
```

Cada delta contém:

```text
player
damage
healing
windowMs
```

Isso evita enviar um request separado para cada hit ou cura.

### Resultados de combate

O client também envia eventos como:

```text
death
kill
knockout
knocked_out
combat_result
```

com vítima, killer e informação de letalidade quando disponível.

---

## Uso no War Room

A telemetria do Combat Client alimenta atualmente módulos como:

- validação do CTA;
- comparação entre PT planejada e Party real;
- identificação de jogador faltando;
- identificação de jogador em PT errada;
- Loot Logger por CTA;
- ranking de Top DPS;
- ranking de Top Heal;
- mortes e resultados de combate;
- status de dispositivos conectados.

Os registros operacionais de Loot e Combat podem ser consultados no War Room por CTA durante a janela de retenção configurada no backend.

---

## Configuração local da telemetria

O arquivo local fica em:

```text
%LOCALAPPDATA%\IMORTAIS Combat Client\telemetry.json
```

Estrutura atual:

```json
{
  "Enabled": true,
  "ServerUrl": "https://cta-imortais.up.railway.app",
  "AgentKey": "TOKEN_DO_DISPOSITIVO",
  "DeviceId": "NOME_DO_PC",
  "PlayerName": "NomeDoPersonagem",
  "CtaEventId": null,
  "BatchIntervalMs": 1000,
  "MaxBatchSize": 100,
  "OutboxPath": "%LOCALAPPDATA%\\IMORTAIS Combat Client\\outbox.ndjson",
  "MaxOutboxEvents": 50000,
  "MaxOutboxBytes": 52428800
}
```

O `AgentKey` deve ser um token destinado ao dispositivo.

**Não distribua a chave mestre do Railway para os jogadores.** A credencial mestre de ingestão deve permanecer somente no backend.

O script auxiliar existente pode gravar a configuração:

```powershell
.\configure-telemetry.ps1 `
  -AgentKey "TOKEN_DO_DISPOSITIVO" `
  -PlayerName "NomeDoPersonagem"
```

O servidor padrão já é:

```text
https://cta-imortais.up.railway.app
```

---

## Dispositivos e pareamento

A arquitetura do War Room trabalha com dispositivos individualizados.

Cada instalação possui:

- `DeviceId`;
- personagem;
- token próprio;
- estado de conexão;
- associação ao CTA quando aplicável.

O War Room possui fluxo de gerenciamento de dispositivos e pareamento. Tokens de dispositivos podem ser revogados sem expor ou substituir a chave mestre do servidor.

---

## Fila, outbox e envio

A captura continua usando um `Channel` em memória como **hot buffer**. As threads que observam Party, Loot e combate nunca fazem I/O em disco nem rede.

O worker persiste os eventos em:

```text
%LOCALAPPDATA%\IMORTAIS Combat Client\outbox.ndjson
```

A outbox usa NDJSON, um evento por linha, preservando o `EventId` original.

Fluxo:

```text
captura
  ↓
Channel em memória
  ↓
append na outbox em disco
  ↓
lê lote da cabeça (FIFO)
  ↓
POST /api/telemetry/ingest
  ↓
ACK HTTP de sucesso
  ↓
remove exatamente os EventIds confirmados
```

Regras de durabilidade:

- reiniciar ou derrubar o client não descarta eventos já persistidos;
- falha HTTP não remove eventos da outbox;
- não há reenfileiramento do batch no Channel após falha de rede;
- um crash depois do POST e antes do ACK local pode causar reenvio, mas mantém o mesmo `EventId` para deduplicação no servidor;
- remoção após ACK reescreve sobreviventes em `outbox.ndjson.tmp` e faz troca atômica por `File.Replace`/Move;
- arquivo `.tmp` deixado por interrupção é recuperado no boot;
- o disco é limitado por `MaxOutboxEvents` e `MaxOutboxBytes`;
- defaults: 50.000 eventos / 50 MB;
- ao exceder o teto, somente os eventos mais antigos são descartados e o estado registra aviso;
- campos novos do `telemetry.json` são opcionais e configs antigas continuam válidas.

---

## Atualizador

O updater usa o feed hospedado neste próprio repositório e releases do GitHub.

A arquitetura da v0.4.6 foi ajustada para evitar o problema de bootstrap encontrado nas versões v0.4.4 e v0.4.5.

### Segurança do updater

O instalador de atualização é assinado com **Ed25519** para o fluxo interno do Combat Client.

A chave pública fica incorporada na build. A chave privada usada para publicar novas versões **não faz parte do repositório**.

Ela deve permanecer armazenada com segurança fora do GitHub.

A assinatura Ed25519 do updater não é a mesma coisa que assinatura **Authenticode** do Windows.

### SmartScreen / Windows Defender

O projeto ainda não possui assinatura Authenticode de um publicador confiável do Windows.

Por isso, em máquinas novas, o Microsoft Defender SmartScreen pode exibir aviso de aplicativo desconhecido durante a instalação.

Isso não significa, por si só, que o arquivo foi detectado como malware. Uma próxima etapa do projeto é adicionar assinatura Authenticode/Code Signing ao executável e ao instalador.

### Migração e teste do updater

As versões v0.4.4 e v0.4.5 possuíam uma validação adicional do próprio appcast que se mostrou frágil e impedia a oferta de atualização em alguns cenários.

Por isso:

```text
v0.4.4 / v0.4.5
        ↓
instalação manual da v0.4.6
        ↓
v0.4.6 detecta v0.4.7 pelo updater
        ↓
v0.4.7 restaura a Home IMORTAIS e mantém o novo fluxo
```

A v0.4.6 mantém a validação Ed25519 do instalador baixado e deixa de depender daquele bloqueio adicional do feed. A v0.4.7 é a primeira versão usada para validar esse novo fluxo de ponta a ponta.

A v0.4.8 restaura a separação entre detecção de atualização e interface: o detector roda primeiro, registra a atualização disponível e só depois agenda a janela IMORTAIS no dispatcher da UI. Também remove o pré-teste HEAD do appcast que podia impedir silenciosamente a checagem real do NetSparkle.

---

## Instalação para jogadores

Use preferencialmente o instalador publicado em **Releases**:

```text
IMORTAIS-Combat-Client-Setup-v0.4.7.exe
```

Também são publicados:

```text
IMORTAIS-Combat-Client-Setup-v0.4.7.exe.sha256
IMORTAIS-Combat-Client-v0.4.7-win-x64.zip
```

Para usuários em v0.4.6, a v0.4.7 deve ser recebida pelo updater interno. Usuários em v0.4.4/v0.4.5 ainda devem migrar manualmente para v0.4.6 ou mais recente.

O diretório padrão de instalação é:

```text
%LOCALAPPDATA%\Programs\IMORTAIS Combat Client
```

---

## Build para desenvolvimento

Pré-requisito:

```powershell
dotnet --version
```

O projeto atualmente utiliza .NET 10.

Clone:

```powershell
git clone https://github.com/cocoputiz-sudo/imortais-combat-client.git
cd imortais-combat-client
```

Projeto WPF integrado:

```text
upstream\AlbionOnline-StatisticsAnalysis\src\StatisticsAnalysisTool\StatisticsAnalysisTool.csproj
```

Build:

```powershell
dotnet build .\upstream\AlbionOnline-StatisticsAnalysis\src\StatisticsAnalysisTool\StatisticsAnalysisTool.csproj -c Release
```

Publish win-x64 self-contained:

```powershell
dotnet publish .\upstream\AlbionOnline-StatisticsAnalysis\src\StatisticsAnalysisTool\StatisticsAnalysisTool.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true
```

Builds oficiais de Release exigem a chave pública do updater.

---

## Estrutura do repositório

```text
upstream/
  AlbionOnline-StatisticsAnalysis/
    src/
      StatisticsAnalysisTool/
        Imortais/              # integração IMORTAIS CANÔNICA
        Network/Manager/       # controllers canônicos
        Party/                 # PartyController canônico
        Updater/
        Views/
        Styles/

legacy/
  v0.3-hooks/                 # snapshots congelados, não compiláveis

src/
  Imortais.Bridge/            # bridge histórica/de referência

docs/
scripts/

dist/                         # gerado, gitignored
installer/                    # gerado, gitignored
release/                      # gerado, gitignored
```

### Fonte única de verdade

O código executável da integração IMORTAIS vive **somente** em
`upstream/AlbionOnline-StatisticsAnalysis/src/StatisticsAnalysisTool/`.

Não mantenha ou reaplique cópias paralelas de `ImortaisEventBridge` ou dos controllers.
Os arquivos de `legacy/` são históricos, usam extensão não compilável e nunca devem ser copiados por cima do fork.

O `install-hooks.ps1` é deliberadamente um no-op: os hooks já estão incorporados ao fork.

A pasta `src/Imortais.Bridge` representa a bridge criada durante a fase inicial e continua útil apenas como referência.

---

## Backend esperado

O backend do War Room é responsável por:

- autenticar o dispositivo;
- registrar telemetria;
- deduplicar eventos;
- associar eventos ao CTA;
- armazenar Party snapshots;
- agregar dano e cura;
- registrar kills/deaths;
- filtrar e apresentar Loot Logger;
- comparar composição planejada e observada;
- manter histórico operacional;
- fornecer contexto do CTA para o Combat Client.

A chave mestre de ingestão nunca deve ser embutida ou distribuída com o executável.

---

## Segurança e escopo

O IMORTAIS Combat Client foi desenvolvido para observação passiva dos dados que o Statistics Analysis já consegue interpretar.

O projeto não pretende:

- movimentar o personagem;
- executar skills;
- automatizar combate;
- automatizar decisões dentro do jogo;
- modificar o processo do Albion;
- injetar código no cliente do jogo.

Ferramentas de terceiros não são oficialmente suportadas pela Sandbox Interactive. O uso deve sempre acompanhar as regras e alterações do Albion Online.

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

## Situação do roadmap

### Entregue

- integração real com o parser/captura do Statistics Analysis;
- branding IMORTAIS;
- ícone próprio;
- telemetria Party;
- telemetria Loot;
- guilda do looter;
- Damage/Healing agregados;
- kills/deaths/knockouts;
- contexto automático do CTA;
- painel IMORTAIS STATUS;
- integração com War Room;
- updater visual;
- releases próprias no GitHub;
- verificação Ed25519 do instalador;
- outbox persistente e atômica para telemetria offline/restart.

### Em validação

- fluxo automático completo de atualização v0.4.8 → v0.4.9;
- relançamento automático após update em diferentes máquinas;
- comportamento do updater fora da máquina de desenvolvimento.

### v0.4.9 em preparação

- Central de Diagnóstico na Home IMORTAIS;
- versão instalada e estado do updater;
- última checagem de atualização;
- última comunicação bem-sucedida com o War Room;
- quantidade e tamanho da outbox pendente;
- último erro de telemetria;
- botão para verificar atualização na Home;
- botão para copiar diagnóstico seguro para suporte no Discord;
- botão de atualização em Configurações com status visual e progresso da checagem.

A v0.4.9 também serve como teste controlado do fluxo automático v0.4.8 → v0.4.9.

### v0.5.0 experimental · Guild Presence Collector

- observa passivamente `GuildUpdate`, `GuildPlayerUpdated`, `GuildMemberWorldUpdate` e `GuildMemberTerritoryUpdate`;
- envia ao War Room eventos `guild_presence_probe` limitados e deduplicados;
- mantém os campos como dados brutos/normalizados enquanto validamos o protocolo real do Albion;
- não marca jogador como online/offline por inferência ainda;
- permitirá construir `lastSeen`, presença no Albion e o cruzamento Albion × Discord × ping × PT real após a validação dos payloads.

### Próximos passos

- assinatura Authenticode para melhorar a confiança do Windows/SmartScreen;
- segmentação de combate por fight;
- histórico consolidado de desempenho por temporada;
- Loot Comparator real contra depósitos no baú da guilda;
- endurecimento adicional do processo de release;
- evolução do gerenciamento de dispositivos.

---

## Releases

As builds oficiais estão disponíveis na seção **Releases** deste repositório.

Para jogadores, prefira sempre o instalador oficial da release mais recente em vez de builds locais ou arquivos compartilhados fora do fluxo oficial.
