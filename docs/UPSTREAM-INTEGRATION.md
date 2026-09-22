# Integração com AlbionOnline-StatisticsAnalysis

## Fonte única de verdade

A integração IMORTAIS já está aplicada diretamente no fork deste repositório.

O único código canônico e executável da integração fica em:

```text
upstream/AlbionOnline-StatisticsAnalysis/src/StatisticsAnalysisTool/
```

Em particular:

```text
Imortais/ImortaisEventBridge.cs
Imortais/ImortaisTelemetryConfig.cs
Imortais/ImortaisTelemetryEvent.cs
Network/Manager/LootController.cs
Network/Manager/CombatController.cs
Network/Manager/StatisticController.cs
Party/PartyController.cs
```

Não copie hooks antigos para dentro do fork. Não execute scripts históricos para "reaplicar" a integração.

O `install-hooks.ps1` da raiz é deliberadamente um **no-op** e existe apenas para impedir uso acidental do instalador antigo.

Snapshots da fase v0.3 foram movidos para `legacy/` com extensão não compilável e são apenas referência histórica.

## Objetivo da integração

O fork aproveita os pontos em que o Statistics Analysis já sabe que ocorreu um evento e publica uma cópia normalizada para o War Room, sem reescrever o parser Photon.

Fluxos canônicos atuais incluem:

- Party snapshot;
- Loot com guilda do looter;
- Damage;
- Healing;
- kill/death/knockout;
- contexto automático de CTA via `GET /api/telemetry/context`;
- status War Room/Albion/CTA/Party/Telemetria no client.

## Regra para futuras mudanças

1. Edite diretamente o fork canônico em `upstream/.../StatisticsAnalysisTool/`.
2. Não crie uma segunda cópia ativa de `ImortaisEventBridge` ou dos controllers.
3. Não bloqueie a thread de captura com HTTP ou I/O em disco.
4. Não altere parser/decoder Photon para implementar telemetria.
5. Preserve os payloads normalizados esperados pelo War Room.
6. Rode build Release do fork antes de concluir qualquer mudança.

## O que NÃO fazer

- não modificar o cliente do Albion;
- não injetar pacotes;
- não automatizar gameplay;
- não movimentar personagem;
- não executar skills;
- não enviar payload bruto de pacote;
- não commitar segredos do Railway;
- não ressuscitar os snapshots de `legacy/` como segundo caminho de produção.

## Licença

O fork deriva de `Triky313/AlbionOnline-StatisticsAnalysis` e deve preservar GPLv3, créditos e disponibilidade do código-fonte correspondente.
