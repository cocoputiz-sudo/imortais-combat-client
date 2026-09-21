# IMORTAIS Combat Client / Statistics Bridge

Primeiro esboço do cliente desktop da IMORTAIS para unir o **AlbionOnline-StatisticsAnalysis** ao **War Room / Railway**.

## Estado desta versão

Esta versão já entrega a camada IMORTAIS completa de transporte:

- GUI WPF para Windows;
- configuração de Railway, chave, jogador, device e CTA;
- fila offline persistente;
- eventos de Party, Damage, Healing, Death e Loot;
- envio em lote para `POST /api/telemetry/ingest`;
- retentativa automática a cada 5 segundos;
- UUID por evento para permitir deduplicação no servidor;
- botão de DEMO para validar PC → Railway → PostgreSQL antes de integrar o parser;
- API C# (`ImortaisTelemetry`) pronta para ser chamada pelos handlers do Statistics Analysis.

**Importante:** este repositório não copia o parser Photon do Statistics Analysis. A captura real do Albion entra quando este projeto for unido ao fork GPLv3 do upstream. Isso é intencional: não faz sentido reescrever o parser que o projeto original já mantém.

## Base upstream

Projeto original: `Triky313/AlbionOnline-StatisticsAnalysis`.

O upstream atual requer Windows 10+ e .NET 10 Desktop Runtime e suporta tracking por socket ou Npcap. Ele é GPLv3. Ao criar o fork IMORTAIS, mantenha os créditos e a licença aplicável ao código derivado.

## 1. Rodar esta camada IMORTAIS

Pré-requisito de desenvolvimento:

```powershell
dotnet --version
```

Use .NET 10 SDK.

Depois:

```powershell
git clone URL_DO_SEU_REPOSITORIO
cd imortais-statistics-bridge
dotnet restore
dotnet run --project src/Imortais.Bridge/Imortais.Bridge.csproj
```

Na GUI informe:

- Servidor: `https://SEU-PROJETO.up.railway.app`
- Chave: valor de `TELEMETRY_INGEST_KEY` do Railway
- Jogador: seu nick
- Device ID: nome único desse PC
- CTA Event ID: opcional

Clique **DEMO**. Se o backend do War Room já tiver `/api/telemetry/ingest`, os eventos devem aparecer no PostgreSQL/monitor de telemetria.

## 2. Variável no Railway

No Railway:

1. abra o projeto;
2. abra o serviço do bot/War Room;
3. entre em **Variables**;
4. clique **New Variable**;
5. crie `TELEMETRY_INGEST_KEY`;
6. use uma chave longa e aleatória;
7. faça redeploy se não ocorrer automaticamente.

Para gerar uma chave no seu PC:

```powershell
node -e "console.log(require('crypto').randomBytes(48).toString('hex'))"
```

Nunca commite essa chave no GitHub.

## 3. Criar o fork do Statistics Analysis

Você pode criar um fork no GitHub ou clonar o original em uma pasta local:

```powershell
./scripts/bootstrap-upstream.ps1
```

Ou Bash:

```bash
./scripts/bootstrap-upstream.sh
```

O script cria a branch `imortais-integration`.

Depois siga `docs/UPSTREAM-INTEGRATION.md`.

## 4. Como os dados fluem

```text
Albion Online
    ↓
Statistics Analysis (socket/Npcap + Photon parser)
    ↓
handlers já existentes de Party / Damage / Loot
    ↓
ImortaisTelemetry
    ↓
outbox local
    ↓ HTTPS
Railway /api/telemetry/ingest
    ↓
PostgreSQL
    ↓
War Room
```

## 5. Contrato de eventos

### Party

```csharp
await telemetry.PartySnapshot(new[] { "BadMack", "Mitrius", "Isahel" });
```

### Damage

```csharp
await telemetry.Damage("BadMack", 38420, "ability-name", "TargetName");
```

### Healing

```csharp
await telemetry.Healing("Isahel", 21450, "holy-spell", "BadMack");
```

### Death

```csharp
await telemetry.Death("Mitrius", "EnemyName");
```

### Loot

```csharp
await telemetry.Loot(
    "NegaumBlack",
    "T8_2H_DUALSWORD",
    1,
    "player_corpse",
    "VictimName",
    1240000
);
```

## 6. O que o servidor deve fazer

O Railway deve:

- autenticar o agente;
- registrar device/session;
- salvar eventos;
- deduplicar eventos repetidos;
- reconciliar observações de vários callers;
- cruzar Party observada com PT1/PT2/PT3 etc. do CTA;
- alimentar Confirmação pelo Jogo;
- alimentar Loot Logger;
- agregar Damage/Healing por fight/CTA.

## 7. Multi-caller

A arquitetura já aceita vários PCs. Cada instalação deve ter `DeviceId` distinto. A próxima evolução recomendada é trocar a chave mestre compartilhada por tokens individuais de dispositivo gerados pelo War Room.

## 8. Build do EXE

```powershell
dotnet publish src/Imortais.Bridge/Imortais.Bridge.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Saída em algo semelhante a:

```text
src/Imortais.Bridge/bin/Release/net10.0-windows/win-x64/publish/
```

## 9. Próximo marco

O próximo commit deve ser feito **dentro do fork real do Statistics Analysis**, conectando seus handlers existentes de Party, Damage e Loot aos cinco métodos de `ImortaisTelemetry`. Depois disso a GUI deixa de depender do botão DEMO e passa a mostrar dados reais do jogo.

## Segurança e escopo

O objetivo é observação passiva do tráfego já recebido pelo cliente, sem modificar o Albion e sem automatizar ações no jogo. Ferramentas de terceiros não são oficialmente suportadas pela SBI; use com cautela e acompanhe mudanças nas regras do jogo.
