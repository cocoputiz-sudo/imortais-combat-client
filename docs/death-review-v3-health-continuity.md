# Death Review Forensic v3: continuidade de HP (candidato técnico)

## Motivo

Os Death Reviews locais v2 de 15/10/2026 confirmaram cobertura de casts praticamente
por toda a janela de 45 segundos em ZvZ (2.714 e 4.679 pacotes), mas algumas
mudanças entre leituras de HP absoluto não coincidiam com o `healthDelta` isolado.
A evidência de crédito de abate continua distinta da evidência de golpe fatal.

## Regra nova e cautelas

`healthContinuity` compara duas **leituras absolutas consecutivas** de
`healthAfter`. Calcula:

`expectedHealth = previousHealth + sum(healthDelta recebidos depois do primeiro HP, inclusive o do próximo HP)`

`unexplainedDifference = observedHealth - expectedHealth`

Sem leituras absolutas suficientes, o resultado fica explicitamente
`no_absolute_health_observed` ou `insufficient_comparable_health_observations`.
Uma leitura sem `healthAfter` não é descartada da soma dos deltas, mas
deltas soltos no início/fim são contados em `unpairedDeltaEvents`.

Admite tolerância conservadora de `max(1 HP, 0,1% do HP absoluto/esperado)`
para arredondamento. Relata número de transições comparáveis, quantidade de
diferenças e até **64 exemplos**, sem perder a contagem total caso haja mais.

Cada discrepância conserva apenas horário UTC de recepção do cliente,
HP anterior, HP esperado, HP observado, soma dos deltas, diferença,
tempo entre leituras e número de eventos incluídos. Não registra payload
bruto, parâmetros Photon, tráfego completo nem habilidades adicionais.

**A diferença não prova dano, cura, mudança de HP máximo, buff, debuff ou
pacote perdido.** Em especial, não pode ser somada ao dano recebido. Este
diagnóstico não utiliza tempo de servidor e não reconstitui o golpe final.

## Compatibilidade e isolamento

- `schemaVersion=3` só para novos relatórios; a seção
  `skillEvidence.schema=experimental-post-death-v2` permanece intacta.
- Campos antigos `incomingHealthChanges`, `terminalEvidence`,
  `skillEvidence` e identificação da morte continuam presentes.
- Relatórios antigos v1/v2 são arquivos locais válidos e não são reescritos.
- Não muda captura Npcap, recuperação ExitLag/NoPing/WARP, Highlights,
  pareamento, regra CTA, War Room, appcast nem publicador.
- Mantém aceite do aviso, opt-out, **sem upload automático**, retenção de
  até 30 JSONs locais e gravação assíncrona fora das threads Photon.

## Plano de homologação

1. CI Windows: compilação, lógica Death Review, Photon, Highlights,
   recuperação VPN, NetSparkle e teste de instalação existente.
2. Testes sintéticos: HP normal; vários deltas antes do próximo HP;
   leitura absoluta que salta sem delta compatível; arredondamento;
   observação única; estouro do limite de 64 exemplos; morte seguinte.
3. Homologação com JSON e vídeo de mortes novas, incluindo ZvZ intensa:
   conferir as diferenças marcadas e se o HP final permanece positivo
   quando o pacote de golpe letal não foi visto.
4. Só depois preparar **uma versão/publicação separada**, assinada no
   Windows autorizado e homologada pelo botão Atualizar agora.

**Este PR é somente o algoritmo e regressões: não é release pública.**
