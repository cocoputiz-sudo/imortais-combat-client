# IMORTAIS Statistics Hooks v0.3

Primeiro patch real do AlbionOnline-StatisticsAnalysis para encaminhar eventos ja interpretados ao War Room da IMORTAIS.

## O que este patch captura

- Party: snapshot dos nomes atuais da party apos o `PartyController.UpdatePartyAsync()` atualizar a UI.
- Loot: item, quantidade, quem pegou, de quem veio, valor estimado e mapa, depois dos filtros e da deduplicacao do Loot Logger.
- Damage: agrega dano por jogador e envia deltas em lotes.
- Healing: agrega healing efetivo por jogador e envia deltas em lotes.
- Combat result: kill, death, knockout e knocked out a partir do `PlayerCombatResultResolver` ja usado pelo Statistics Analysis.

## Arquivos alterados

- `Network/Manager/LootController.cs`
- `Network/Manager/CombatController.cs`
- `Network/Manager/StatisticController.cs`
- `Party/PartyController.cs`

Novos arquivos:

- `Imortais/ImortaisTelemetryConfig.cs`
- `Imortais/ImortaisTelemetryEvent.cs`
- `Imortais/ImortaisEventBridge.cs`

## Instalar no seu clone

Coloque esta pasta na raiz do seu repositorio `imortais-statistics-bridge` e rode:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\install-hooks.ps1
```

O script cria backup dos 4 arquivos originais antes de substituir.

## Configuracao local

A integracao fica DESLIGADA por padrao. Ela usa:

`%LOCALAPPDATA%\IMORTAIS Combat Client\telemetry.json`

Para criar a configuracao:

```powershell
.\configure-telemetry.ps1 `
  -AgentKey "SUA_CHAVE" `
  -PlayerName "BadMack"
```

Ou informe tambem o CTA:

```powershell
.\configure-telemetry.ps1 `
  -AgentKey "SUA_CHAVE" `
  -PlayerName "BadMack" `
  -CtaEventId "123"
```

## Endpoint esperado

O cliente envia `POST` para:

`/api/telemetry/ingest`

com header:

`Authorization: Bearer <AgentKey>`

Os eventos principais sao:

- `party_snapshot`
- `loot`
- `combat_delta`
- `kill`
- `death`
- `knockout`
- `knocked_out`

## Importante

Este patch ainda nao altera a GUI original do Statistics Analysis. O objetivo desta v0.3 e validar a captura real e o transporte dos eventos. A GUI simplificada IMORTAIS vem depois que confirmarmos que party, loot e combate chegam corretamente.

Tambem nao foi possivel compilar o upstream neste ambiente, porque o SDK .NET nao esta instalado aqui. Compile no seu Windows antes de dar commit.
