# Guild Combat Client para Linux · plano até a versão estável

Objetivo: o Linux fazer o básico da guilda **igual ao Windows**: dano e cura (com PvP/PvE), abates, mortes com equipamento, party e número de jogadores nas PTs, nome do mapa, guilda do personagem e pareamento com várias guildas, sem um segundo código que precise ser reescrito a cada mudança.

## Decisão

A alpha Linux (PRs #29, #39, #40, #57) é uma reimplementação separada (v0.2.0-alpha, de outubro) com remetente, fila, pareamento e formatos próprios. Ela ficou para trás do Windows (sem várias guildas, sem guilda no heartbeat, sem classificação de alvos, sem morte com equipamento). Manter dois códigos é o retrabalho que queremos evitar.

**O Linux passa a usar o mesmo núcleo do Windows.** `src/Imortais.ClientCore` compila, sem cópia, os mesmos arquivos do Windows (`ImortaisEventBridge`, pareamentos, identidade da guilda, configuração, fila, classificação de alvos, captura PvE). Tudo o que chega ao War Room sai do mesmo código nos dois sistemas.

## Situação (02/11/2026)

Etapas 1 a 5 concluídas: versão 1.0.0 gerada, instalada e aberta num Ubuntu 24.04 limpo como usuário comum, com captura ativa. Falta a etapa 6 (teste em jogo real). Ver `docs/LINUX-CLIENT.md`.

## Etapas

1. **Núcleo compartilhado** (este PR). O núcleo compila para Linux e um teste o executa no Ubuntu contra um War Room local: heartbeat, mapa, party, combate com PvP/PvE, token, e recusa de personagem de outra guilda. A parte Windows da captura PvE virou um adaptador (`AvalonPveCapture.Windows.cs`), sem mudar o comportamento no Windows.
2. **Leitura dos eventos do jogo compartilhada.** Os leitores de pacote do Windows (`Network/Events/*`) que só dependem de dicionários Photon passam a ser compilados também pelo Linux. Os poucos que tocam WPF ganham o mesmo tipo de adaptador.
3. **Linux ligado ao núcleo.** A alpha Linux troca remetente, fila e pareamento próprios pelo núcleo e chama as mesmas entradas que os manipuladores do Windows: entrada no mundo (guilda), troca de mapa, party, jogadores próximos, dano e cura, morte e abate com equipamento e IP, loot, sondas de Guild Might. A captura (libpcap) e o parser Photon já são multiplataforma.
4. **Interface Linux** (Avalonia) com o essencial do Windows: aba GUILD (escolher guilda, código, confirmador verde/vermelho, guildas pareadas), estado do War Room, mapa e party, opção do Top DPS.
5. **Pacote e atualização.** `.deb` com permissões de captura, atualização pelo app com checagem SHA-256 e assinatura Ed25519 (as mesmas chaves do Windows), migração das configurações da alpha sem perder pareamento.
6. **Homologação em jogo real** no Ubuntu 22.04 e 24.04 (uma CTA, uma DG, troca de personagem entre guildas) comparando lado a lado com um PC Windows no War Room.

O que fica só no Windows por enquanto: Highlights (vídeo) e Death Review.
