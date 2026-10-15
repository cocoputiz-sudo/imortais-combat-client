# IMORTAIS Combat Client | Network Recovery para VPN / ExitLag / NoPing / WARP

## Diagnóstico do código atual (14/10/2026)
O cliente já usa o Npcap em múltiplas interfaces, detecta mudança de adaptador/endereço via NetworkChangeMonitor, libera lock inativo em 20 segundos, tenta recuperar após erros do pcap_dispatch e reabre interfaces com backoff. Isso NÃO significa que há compatibilidade universal com VPN.

Pontos críticos:
1. `TrySelectAdapter` trava a captura a partir do primeiro pacote **candidato** (porta UDP ou assinatura inicial) e não a partir de um evento Photon comprovadamente decodificado. A escolha errada pode descartar o tráfego de outra interface.
2. `Dispatch` descarta cedo o tráfego de outras interfaces após o lock; assim, um segundo adaptador pode ter o tráfego útil sem ser examinado.
3. Mudanças da rota dentro de um acelerador podem não gerar alteração de IP/adapter percebida por `NetworkChangeMonitor`.
4. Heartbeat HTTPS ao War Room, processo do Albion aberto e frames de vídeo são sinais INDEPENDENTES: nenhum confirma que o Photon está chegando.
5. A VPN pode encapsular o Photon. Npcap pode ver apenas o túnel criptografado; não devemos afirmar que dados podem ser recuperados em toda configuração, nem tentar contornar a criptografia.

## Fase 1: observabilidade + indicação explícita do jogador (PR experimental)
- Botão `USO VPN: SIM/NÃO` no painel IMORTAIS. Padrão **NÃO**, salvo nas configurações.
- Contagens **locais** de pacotes UDP candidatos, eventos Photon efetivamente decodificados, interfaces observadas, interface travada, alterações de rede, reexames pedidos e tentativas de recuperação.
- Classificação conservadora: sem Albion -> aguardar; sem candidatos -> inconclusivo; candidatos sem decodificação -> suspeita; eventos decodificados recentes -> saudável; período sem eventos -> **verificar**, não reiniciar automaticamente.
- Botão `REEXAMINAR INTERFACES`, sob iniciativa do jogador, usa o worker Npcap e preserva o aplicativo e pareamento. Pode interromper temporariamente a captura. **Não** exige PowerShell.
- A fase 1 não altera regras de seleção automática nem o formato de payload de telemetria ao War Room. Nenhum dado bruto de UDP nem IP/credenciais da VPN é transmitido.
- A opção é indicação contextual, não configura/cria VPN nem altera sistema de roteamento.

## Fase 2: sondagem reversível, piloto v0.6.7
Na v0.6.7, somente quando o jogador declara `USO VPN: SIM` e o jogo está detectado:
- É necessário passar **25 segundos** sem qualquer evento Photon decodificado.
- Outra interface deve ter pelo menos **seis pacotes candidatos recentes**, dentro de uma janela de 12 segundos.
- A sondagem de outra interface dura **até oito segundos** e decodifica eventos de **uma única interface por vez**.
- Um evento Photon realmente decodificado na alternativa confirma a promoção; caso contrário o cliente **volta para a interface anterior**.
- Cooldown de 90 segundos e teto de **três sondagens por hora**. Silêncio isolado não desencadeia reabertura do Npcap.
- O diagnóstico expõe contagens de sondagens iniciadas, confirmadas e revertidas, além dos reexames manuais.
- A reassemblagem IPv4 considera a interface capturada, evitando misturar fragmentos vistos em adaptadores diferentes.

Esse piloto é uma melhoria **heurística**, não garante leitura de payload criptografado. A alternativa só passa a ser considerada funcional quando Photon decodificado comprova o sucesso. Não há mudança em roteamento do Windows, VPN ou interface virtual externa. O teste de campo com as VPNs é obrigatório antes de ampliar a distribuição.

## Fase 3: correlação de eventos e saúde avançada (PR futuro, após teste)
Modelo de estados a construir:
- S0: Albion não detectado -> nunca interferir no Npcap por silêncio.
- S1: candidato UDP sem evento Photon decodificado -> suspeita, não certeza.
- S2: evento Photon decodificado recentemente -> saudável; preservar adaptador mesmo diante de flutuação não relevante.
- S3: silêncio do Photon + mudança relevante de rota/interface + alternativa com decodificação verificada -> permitir troca automática com janela de observação e cooldown de recuperação.
- S4: tráfego encapsulado sem payload legível -> exibir limitação e pedir ação explícita, sem reinicialização repetitiva.

Critérios obrigatórios:
1. Promover interface apenas mediante **evento Photon validado** e atribuição do pacote à interface; UDP candidato isolado não é suficiente.
2. Não introduzir duplicação de kill/loot/might/party ao observar vários adaptadores (precisaremos correlacionar e deduplicar).
3. Fallback e backoff com jitter, mas **nunca** reiniciar a captura indefinidamente por silêncio de Photon (AFK/map loading/menus).
4. Expor causa explícita de qualquer recuperação: mudança de rota, interface indisponível, tentativa manual, erro persistente, com tempo e resultado.
5. Evitar enumerar ou gravar processos, identidades de VPN, dados pessoais, payloads ou credenciais. Observar somente APIs de rede autorizadas.
6. Nenhuma nova heurística automática em produção antes de comparação A/B e teste em ZvZ.

## Matriz de homologação
| Teste | Ação | Critério |
| --- | --- | --- |
| Sem VPN | Jogar 20 min e entrar/sair de PT | Dados Photon regulares; nenhum reexame manual ou troca espúria |
| ExitLag ativo antes do Albion | Abrir cliente e jogo | Identificar se Npcap vê pacotes candidatos E eventos decodificados |
| NoPing ativo antes do Albion | Mesma rotina | Comparar interface/contadores com ExitLag |
| WARP ativo | Mesma rotina | Identificar se tráfego é visível na interface do túnel; declarar limitação se não for |
| VPN ligada após iniciar ZvZ | Ligar VPN fora de combate primeiro | Não perder conexão HTTPS nem reiniciar processo; diagnosticar mudança de rota |
| VPN desligada durante sessão | Desligar em ambiente controlado | Registrar alterações e novos eventos decodificados, sem duplicação |
| Oscilação de Wi-Fi | Rede cai 2-3 segundos e retorna | Não causar reaberturas repetidas ou falsos resets por um evento isolado |
| Albion aberto, mas sem atividade | Parado ou carregando região | Sem loop de recuperação por silêncio |
| VPN sem interface legível | Tráfego UDP encapsulado | Status informativo; nenhuma promessa falsa de leitura |
| Reexame manual | Clicar somente quando suspeitar da captura | Capturador tenta reabrir, com contador distinto de recuperação automática |
| Multicliente/duas interfaces | WLAN + Ethernet/VPN | Não duplicar dados ou atribuir players incorretamente |

**Para cada teste:** anotar VPN/versão, topologia de interfaces, horário local/UTC, estado de Albion, contador candidatos, contador eventos decodificados, interface ativa, NetworkChanges/Rescans/RecoveryAttempts e presença de eventos conhecidos (party/loot/kill), sempre sem enviar payload bruto.

### Portões de avanço
- Fase 1: build Windows + testes lógicos verdes; revisão do texto e teste manual da interface.
- Fase 2: ao menos um relato comparativo SEM VPN e COM VPN, com diagnóstico exportado e evento comprovadamente decodificado.
- Fase 3: recuperação automática somente após prova de que ao menos um caso real recupera dados e nenhum cenário normal entra em loop.

## Riscos e escopo
A coleta de pacotes e interpretação Photon já existe no cliente, mas precisamos revisar as regras vigentes da SBI antes da distribuição de melhorias de inspeção. Este estudo não autoriza captura de conteúdo de outros aplicativos, nem qualquer tentativa de descriptografar VPN. Não inclui dados de combate enviados automaticamente a novo backend, replay maior, IA, ou mudanças em imagens/UI alheias.

**Fase 1 integrada à sondagem v0.6.7.** Ainda é necessária homologação no Windows real antes de distribuição irrestrita.
