# Integração com AlbionOnline-StatisticsAnalysis

Este repositório contém a camada IMORTAIS pronta: GUI, fila offline, modelo de eventos e envio HTTP para o Railway. O parser/captura do Albion deve vir do projeto upstream GPLv3.

## Objetivo

Conectar os pontos em que o Statistics Analysis **já sabe** que ocorreu um evento aos métodos de `ImortaisTelemetry`:

```csharp
await telemetry.PartySnapshot(memberNames);
await telemetry.Damage(sourceName, damage, spellName, targetName);
await telemetry.Healing(sourceName, heal, spellName, targetName);
await telemetry.Death(playerName, killerName);
await telemetry.Loot(looterName, itemUniqueName, quantity, sourceKind, sourceName, estimatedValue);
```

## Estratégia segura de merge

1. Clone/fork o upstream e mantenha a licença GPLv3 e créditos.
2. Localize os handlers que já alimentam **Party**, **Damage Meter** e **Loot Logger**. Não replique o decoder Photon.
3. No mesmo ponto em que o modelo local é atualizado, publique uma cópia normalizada para `ImortaisTelemetry`.
4. Não bloqueie a thread de captura com HTTP. `ImortaisTelemetry` grava primeiro na outbox local; o envio é assíncrono.
5. Não envie payload bruto de pacote. Envie somente eventos normalizados necessários ao War Room.

## Campos que queremos do upstream

### Party
- nome do membro
- snapshot completo quando houver mudança
- join/leave quando disponível

### Loot
- looter
- item unique name/id
- quantidade
- enchant/quality se disponível
- origem: player corpse / mob / chest / system / unknown
- nome/id da origem se disponível
- timestamp

### Combate
- source player
- target
- amount
- spell/ability se disponível
- damage/healing
- death e killer quando disponíveis

## O que NÃO fazer

- não modificar o cliente do Albion;
- não injetar pacotes;
- não automatizar gameplay;
- não transformar isso em overlay que revele informação fora do que o cliente observa;
- não enviar segredos do Railway em commits.

## Por que os hooks não estão hard-coded aqui

O upstream muda com frequência e os nomes/classes dos handlers podem mudar. A integração deve ser feita contra o commit que você realmente forkou. Isso evita um patch aparentemente “pronto” que compila contra outra revisão e quebra silenciosamente o parser.
