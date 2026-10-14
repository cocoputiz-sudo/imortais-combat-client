# IMORTAIS Combat Review | Habilidades e defensivas (PR único experimental)

## Relação com o Death Combat Timeline

Este trabalho **consolida os antigos planos de PR #42 (Skill Observatory) e #43 (habilidades e cooldowns) em um único PR dependente do #41**. Branch baseada no head do PR #41. Não inclui IA, vídeos, releases, appcast, deploy nem alterações no Railway. Nenhuma informação é fornecida ao jogador durante o combate. A exportação é pós-morte, JSON local e opt-in do #41.

## Viabilidade sequencial e evidência disponível

| Subetapa | Evidência real encontrada no código | Estado | O que entra no PR |
| --- | --- | --- | --- |
| 1. Q/W/E + peito/capacete/botas/poção selecionados | CharacterEquipmentChangedEvent key 7: posições 0/1/2/3/4/5/12; equipamento key 2 | **Viável com ressalva** | Preservar posições originais, inclusive -1 e ausência de seleção; reconhecer somente quando evento pertence ao LocalUserData.UserObjectId. |
| 2. Catalogar skills | SpellData.GetUniqueName, SpellPresentationResolver, arquivos locais do jogo | **Viável como candidato, não confirmação** | Acrescentar ao relatório o nome técnico do índice no catálogo, distinguindo claramente dado ausente/mapeamento não validado. |
| 3. Observar casts | EventCodes enum possui CastStart, CastFinished, CastCancel, CastSpell, CastSpells, CastHit, CastHits, ChannelingUpdate, ResetCooldowns | **Códigos previstos, layout e comportamento não homologados** | Coletar somente códigos + formato/tipo dos campos (até 20 por amostra, até 256 amostras/45 segundos). Não armazenar payload/valores de cast nem atribuir os casts a personagens. |
| 4. Atribuir cast de inimigo | Handlers de HealthUpdate já têm CauserId e CausingSpellIndex, mas NÃO há handlers de eventos Cast* no NetworkManager | **Parcial** | Relacionar nomes candidatos com os índices de danos recebidos; relação entre cast e atacante fica pendente. |
| 5. Cooldowns exatos | GameFileDataSpell.RecastDelay não representa, isoladamente, cooldown resultante; não há histórico confirmado de casts nem modificadores de estado | **Não demonstrado** | Todos os sete slots indicam indeterminado. Não fabricar tempo restante nem julgamento de falha defensiva. |
| 6. Impedimentos (stun/silence etc.) | ActiveSpellEffectsUpdate existe, mas seus campos não são prova da aplicação no jogador alvo | **Não demonstrado** | Não classificar efeitos como impedimentos do usuário nesta fase. |
| 7. Vídeo e IA | Captura de highlight do #41 não recebe timeline correlacionada | **Fora do escopo** | Adiar até homologação e autorização da SBI. |

## Implementação

1. Adicionar SelectedSpellIndices imutável e posicional ao evento CharacterEquipmentChanged, sem alterar o fluxo de equipamentos do Albion.
2. Registrar somente quando o objeto alterado coincide com o ID local confirmado.
3. Amostrar somente as formas dos eventos Photon hipoteticamente de cast (códigos, chaves, tipos/tamanhos), não os valores; limite de 256 eventos dos últimos 45 segundos.
4. Ao morrer, adicionar skillEvidence ao JSON do #41. Cada skill selecionada contém availability=indeterminado, catálogo como candidato e fonte do dado. castAttributionStatus permanece indeterminado.
5. Usar fallback GetLastLocalCharacterEquipment() para o equipamento no instante da morte.
6. Limpar buffer e seleções após a captura para não reutilizar eventos de uma vida em outra.
7. A execução dos testes lógicos existentes e dos novos cenários é parte do workflow de build no PR #41; este PR não precisa criar uma release.

## Privacidade, permissão e desempenho

- Mesma escolha opt-in do Death Review, desligado por padrão.
- Dados de casts: somente formatos, nunca GUIDs, IDs ou valores dos parâmetros de cast. Os nomes e IDs nos eventos de dano continuam sob as regras do PR #41.
- Somente arquivo local por morte; não cria mensagens, sobreposições ou notificações com habilidades de inimigos durante ZvZ.
- Cap de 256 formas recebidas, máximo 20 campos por evento; coletor em memória.
- Não requer acesso à memória interna do Albion, injeção, alteração de pacotes nem operação ativa.
- **O risco de compatibilidade com as regras da SBI permanece**: a versão distribuível exige confirmação formal das permissões para este uso específico de dados de rede pós-combate. Não presume que captura passiva seja automaticamente permitida.

## Testes e critérios de aprovação

Testes de código: dotnet run --project tests/DeathReview.LogicTests/DeathReview.LogicTests.csproj

Testes de campo:
1. Alternar entre três builds e registrar screenshots da barra Q/W/E, peito, capacete, botas e poção. Verificar coincidência dos índices/ícones e nomenclatura do catálogo.
2. Repetir com uma habilidade indisponível/sem seleção para provar que a posição não desloca.
3. Em ambiente controlado, ativar uma habilidade de cada vez, exportar EXPORTAR DIAGNÓSTICO DE EVENTOS do cliente e comparar com o segmento castPacketShapes do Death Review. Identificar códigos reais depois do Dragonfire Patch 3.
4. Avaliar duplicação/mapeamento de HealthUpdate vs HealthUpdates, perda de pacotes e sessão com VPN.
5. Confirmar ausência de telemetria adicional ao servidor, ausência de alertas durante combate e impacto desprezível no FPS.
6. Checar que todos os slots permanecem indeterminado no JSON sem histórico suficiente.

### Regra de progressão
**Só após validar identidade de casters e semântica de cast** adicionar atribuição de habilidade inimiga. **Só após validar eventos e modificadores de cooldown** calcular disponibilidade provável do próprio jogador. **Só após isso** encaminhar os dados para análise multimodal por IA.

**Sem merge, sem distribuição, sem release antes de homologação.**
