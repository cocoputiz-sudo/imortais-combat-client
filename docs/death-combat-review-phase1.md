# Death Combat Review - Etapa 1 (Windows, opt-in, somente local)

Este PR inicia o Death Combat Timeline do IMORTAIS Combat Client, sem IA, sem upload de vídeo e sem novas operações de rede.

## O que captura

Quando o usuário habilita **DEATH REVIEW** na aba de diagnósticos, cada HealthUpdate e HealthUpdates que afeta especificamente o objectId local é observado em um buffer RAM de **45 s / até 2.048 eventos**. Armazena UTC aproximado da recepção, objectId do causador, nome do causador quando conhecido, delta de HP, HP informado, EffectType e \`CausingSpellIndex\` cru.

Em morte letal do próprio personagem (mesmo teste de identidade já usado para highlights), congela o buffer, anexa o equipamento conhecido (índices da arma, capacete, peito, botas, poção, etc., e ActiveSpells se preenchido) e salva um JSON em:

\`%LOCALAPPDATA%\IMORTAIS Combat Client\Diagnostics\DeathReview\death-review-*.json\`

A gravação utiliza uma tarefa em segundo plano, até **2 trabalhos pendentes**, escrita temporária seguida de rename e retenção dos **30 arquivos locais** mais recentes. O botão **ABRIR DEATH REVIEWS** abre a pasta. O diagnóstico copiado inclui estado, último arquivo e erro.

## Segurança e limitações

- **Desligado por padrão**, com confirmação ao habilitar e independente da gravação de vídeo.
- Arquivos contêm nomes, IDs e dados de combate observados; ficam **somente no computador**. Não altera telemetria HTTPS, banco ou outbox do War Room.
- Não lê memória do Albion, não injeta nem controla o jogo.
- \`CausingSpellIndex\` NÃO é um nome de skill nem Q/W/E verificado.
- Dados de equipamento são o último retrato conhecido; não comprovam habilidades selecionadas ou cooldowns.
- Não registra necessariamente todos os casts inimigos nem todos os danos (limite da captura passiva, VPN, range e perda de pacotes).
- O relógio UTC é de recebimento do cliente; não há sincronização exata com vídeo nesta etapa.
- O parser pode receber eventos redundantes; ainda não implementamos deduplicação de HealthUpdate/HealthUpdates.
- Não cria recomendações em tempo real.
- Não habilitar amplamente sem avaliar a política de ferramentas de terceiros da SBI.

## Teste

\`dotnet run --project tests/DeathReview.LogicTests/DeathReview.LogicTests.csproj\`

## Homologação manual

1. Instalar build **de teste**, sem alterar release v0.6.2 e sem appcast.
2. Abrir aba de diagnóstico e verificar DEATH REVIEW desligado.
3. Ativar e confirmar o aviso local.
4. Com Albion aberto, receber dano de jogadores e morrer em ambiente controlado. Abrir a pasta e conferir o JSON.
5. Validar \`victimObjectId\`, dano, \`sourceObjectId\` e \`causingSpellIndex\` contra um vídeo e o recap oficial; não assumir equivalência automática.
6. Desligar e repetir: não deve criar novos relatórios.
7. Conferir que o War Room/outbox não recebe novos tipos de evento e que highlights antigos continuam funcionando.

## Próxima etapa

Homologar mapeamento de cast/skills, seleções de Q/W/E e estados de cooldown, só então integrar com vídeo e IA pós-combate.
