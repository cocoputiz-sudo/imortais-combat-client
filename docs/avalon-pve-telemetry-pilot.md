# Top DPS da DG Avaloniana · captura de dano em mobs

**Estado:** captura publicada na v0.7.3 (desligada por padrão). A partir da próxima versão, a opção **Participar da apuração do Top DPS** na aba GUILD liga a captura e o envio com um clique. O War Room faz a apuração e premia o vencedor ao encerrar a DG.

## O que o Client faz

- Só conta dano causado pela sua party em **mobs confirmados** (alvo criado como mob pelo jogo, com índice de mob válido; nunca jogador, nunca alvo desconhecido).
- Só dentro de **dungeon de Avalon**: tipo de mapa RandomDungeon, modo de dungeon Avalon e identificador de instância presente no pacote de entrada atual. Estradas de Avalon e nomes de mapa não bastam.
- Agrupa os golpes em janelas de até 5 segundos ou 96 golpes por jogador, com horário, instância, mob, habilidade e dano de cada golpe.
- Troca de mapa, andar ou personagem fecha as janelas abertas.
- Não lê memória do jogo, não injeta código, não controla o Albion. Não envia vídeo, morte nem casts.

## Opção na aba GUILD

**Participar da apuração do Top DPS** liga, juntos, os dois controles de `telemetry.json`:

- `DgavaPveOptInEnabled`: captura e cópia local em `Diagnostics/AvalonPve/dgava-pve-AAAAMMDD.ndjson` (até 8 MiB por dia, 7 arquivos).
- `DgavaPveUploadEnabled`: envio das janelas ao War Room da guilda do personagem logado.

Nada é enviado sem a opção ligada, sem telemetria ativa e sem o PC estar pareado com a guilda. Desligar apaga o que estava em memória, sem novo envio.

## O que o War Room faz com isso

O Client nunca manda placar, vencedor, "verificado" ou "completo"; esses campos são recusados. O War Room:

1. recebe as janelas só de dispositivo pareado com a guilda;
2. liga as janelas aos 20 combatentes da PT final pelo nome do personagem, no horário da DG e só nas instâncias onde vários combatentes da DG estavam;
3. não soma o mesmo golpe visto por vários PCs;
4. ao encerrar a DG, apura o maior dano em mobs e isenta o fee do vencedor.

Quanto mais participantes com a opção ligada, melhor a cobertura. Um único PC já vê o dano da party inteira.

## Testes

`dotnet run --project tests/AvalonPve.LogicTests/AvalonPve.LogicTests.csproj -c Release`

Casos: estradas de Avalon, ausência de identificador de instância, modo desconhecido, mob provisório, jogador ou alvo inválido, janelas limitadas, saída e entrada, troca de andar e reinício.
