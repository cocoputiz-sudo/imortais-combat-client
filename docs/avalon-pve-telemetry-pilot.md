# Piloto de telemetria PvE da DG Avaloniana (próxima atualização)

**Status: branch de desenvolvimento / PR, não é release, não está em produção.** Base: v0.7.2. A versão pública, instalador, chave de assinatura, README da versão estável e appcast não são alterados.

## Objetivo e limites

Reconhecer **instâncias de DG Avaloniana** com evidências passivas e produzir janelas de dano observado contra **mobs confirmados**. Isso é insumo de diagnóstico, **não** ranking Top DPS verificável e **não** autorização de isenção de fee. Para o War Room comprovar o vencedor é necessária outra etapa de ingestão autenticada, vínculo à DG, deduplicação, cobertura e homologação de combatentes.

Esta etapa não lê memória do jogo, não injeta código, não controla Albion, não envia DeathReview nem vídeo, não altera os filtros do battleboard, CTA ou a forma de atribuir vitórias PvP.

## Comportamento

- Novo flag opt-in `DgavaPveOptInEnabled` em `telemetry.json`. **Padrão: false**; o envio também exige a telemetria geral habilitada e o pareamento ativo. O piloto não está disponível como uma opção no UI nesta etapa. Ativação manual de homologação só com infraestrutura receptora preparada.
- Uma zona só é considerada **candidata** a DG Avaloniana quando: `MapType.RandomDungeon`, `DungeonMode.Avalon` vindo do controlador de dungeon, identificador de instância **não vazio no pacote de entrada atual** e índice de cluster válido. Simples texto Avalon no mapa, ruas/estradas Avalon, GUID antigo e modo desconhecido não qualificam.
- O caminho de dano usa o evento `HealthUpdate/HealthUpdates`, mas aceita somente um autor conhecido do grupo e um target presente na lista de mobs criada por `NewMob`, cujo `MobIndex > 0` e que **não** seja um player. Alvos provisórios/desconhecidos não entram.
- São geradas janelas por jogador, limitadas a 96 observações ou 5 segundos. Cada janela tem GUID do mapa, cluster, horários UTC, dano observado, quantidade, índices dos mobs e spells, e amostras temporais. Todas têm `coverage: partial`, `evidence: passive_photon_observed_not_verified`. Nenhuma janela tem `verified: true` ou `DG eventId` atribuído pelo client.
- Trocas de cluster, andar ou personagem limpam/reconciliam o estado; observações pendentes são descarregadas quando o opt-in continua ativo. Alteração do flag para OFF elimina o buffer, sem novo envio.
- O tipo de evento `dgava_pve_window` usa o outbox usual **somente quando opt-in explicitamente ligado**. O endpoint de ingestão do War Room ainda não reconhece nem certifica esses dados; não habilitar em produção antes de publicar o backend correspondente.
- Um servidor autenticado deverá validar a guilda/dispositivo, a participação e o contexto temporal da DG; desconsiderar fontes sem metadados suficientes, duplicatas e períodos não cobertos. Nunca aceitar `coverage=complete` ou `verified` por confiança em payload do cliente.

## Limitações explícitas

O detector atual de `DungeonMode.Avalon` ainda depende de indícios do próprio controlador do StatisticsAnalysis; mesmo combinado com `RandomDungeon` e o GUID da entrada, precisa de homologação com tráfego real para evitar falso positivo de outros tipos de dungeon. Não há garantia de entrega de todos os hits, nem deduplicação entre `HealthUpdate` e `HealthUpdates` ou entre vários observadores. O DPS exibido em outros componentes do Client **não** deve alimentar a premiação automaticamente.

A telemetria PvP observada dentro de uma DG ainda não é enviada em uma categoria própria por esta PR. O `combat_delta` legado também não foi alterado nesta etapa, para não introduzir regressões de ZvZ sem um filtro correspondente no servidor. Portanto, a **separação completa de PvE/ZvZ permanece pendente**, e ninguém deve declarar que ela está concluída.

## Testes

`dotnet run --project tests/AvalonPve.LogicTests/AvalonPve.LogicTests.csproj -c Release`

A PR acrescenta essa suíte ao pipeline Windows existente. Casos: estradas Avalon, ausência de GUID, modo desconhecido, mob provisório, jogador/target inválido, janelas limitadas, saída/entrada, andar diferente e reinício. A compilação do projeto WPF na CI verifica a integração real.

## Etapa seguinte antes da premiação por Top DPS

1. Backend do War Room reconhece `dgava_pve_window` sem misturar com os agregados ZvZ e rejeita evidência não vinculada.
2. Validar identidade da instância e unir múltiplos andares com sessão real, com controle de simultaneidade.
3. Reconciliar hit duplicado, clock entre clients, perdas e períodos não observados, com evidência objetiva de cobertura de **todos os 20 combatentes**.
4. Só então elevar o dado a confiável para a rotina Top DPS e a isenção do fee. Empate e cobertura parcial não produzem vencedor automático.
5. Homologar com registros reais consentidos, incluindo PvP no interior da DG, sem alteração de permissões ou automações do Albion.

Nenhum merge, publicação de release, alteração de variáveis ou atualização de clientes foi realizada por esta PR.
