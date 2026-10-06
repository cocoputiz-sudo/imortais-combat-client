# IMORTAIS Combat Client para Ubuntu

A versão Linux é um cliente nativo em .NET 10 + Avalonia. Ela não tenta executar WPF por Wine.

## Interface

A GUI possui:

- **IMORTAIS / Início**: Albion, War Room, captura, jogador, CTA, mapa, party e outbox.
- **Registro**: eventos de captura, parser, party, mapa, mortes e War Room.
- **Medidor de dano**: dano, cura, kills e mortes por jogador.
- **Party**: formação observada pelos eventos Photon.
- **Configurações**: servidor, jogador, Device ID, captura automática e autostart do Ubuntu.

## Captura

O cliente usa **libpcap** e o mesmo parser Photon multiplataforma existente no repositório.

Eventos já ligados na v0.1.1:

- NewCharacter
- Leave
- HealthUpdate
- HealthUpdates
- PartyJoined
- PartyPlayerJoined
- PartyPlayerLeft
- PartyDisbanded
- Died
- ChangeCluster response

A telemetria usa os mesmos tipos esperados pelo War Room:

- client_heartbeat
- party_snapshot
- combat_delta
- player_death_observed
- zone_change
- player_presence_snapshot

## Checklist de validação real — Ubuntu 22.04 / 24.04

Antes de integrar o cliente Linux à `main`, validar **o mesmo pacote .deb** nos dois sistemas quando possível:

1. **Instalação do .deb**
   - instalar com `sudo apt install ./IMORTAIS-Combat-Client-v0.1.1-linux-x64.deb`;
   - confirmar que o atalho **IMORTAIS Combat Client** aparece no menu;
   - abrir pelo menu, sem executar o aplicativo com `sudo`.

2. **GUI**
   - confirmar as cinco abas: **IMORTAIS / Início**, **Registro**, **Medidor de dano**, **Party** e **Configurações**;
   - confirmar que a tela não congela ao iniciar/parar captura.

3. **Captura sem root**
   - em **Início**, clicar em **INICIAR CAPTURA**;
   - confirmar `Captura ● ATIVA` e ao menos uma interface aberta;
   - jogar/mover no Albion e confirmar que **Rede / Photon** aumenta sem iniciar o programa como root.

4. **Heartbeat no War Room**
   - ativar o dispositivo pelo código de 6 dígitos;
   - no painel **Dispositivos** do War Room, confirmar heartbeat recente do device Linux;
   - conferir que o heartbeat mostra captura ativa, interfaces, datagramas Photon e cluster.

5. **Party snapshot**
   - entrar/sair de uma party ou alterar membros;
   - confirmar no ingest/War Room a chegada de `party_snapshot` do device Linux.

6. **Presença na área — paridade v0.5.9**
   - deixar uma CTA ativa no contexto do cliente;
   - aproximar-se de jogadores de guildas diferentes;
   - confirmar a chegada de `player_presence_snapshot` a cada ~15 s;
   - verificar payload com `cluster`, `players[].objectId`, `players[].playerId`, `name`, `guild`, `alliance`, `observedCount` e `snapshotIntervalMs: 15000`;
   - sair do alcance de um jogador e confirmar que, após `Leave`, ele deixa de aparecer nos snapshots seguintes;
   - sem CTA ativo, confirmar que não são gerados snapshots de presença.

7. **Mudança de zona**
   - trocar de mapa/cluster;
   - confirmar atualização do mapa na GUI;
   - confirmar chegada de `zone_change` ao War Room;
   - confirmar que os jogadores observados do mapa anterior não continuam no próximo snapshot.

8. **Fragmentação IPv4**
   - acompanhar **Rede / Photon** na tela inicial;
   - confirmar que o contador `frag` aumenta quando houver pacotes fragmentados;
   - quando ocorrer remontagem, confirmar aumento do contador `remont.`;
   - no heartbeat, conferir `ipv4Fragments` e `ipv4Reassemblies`.

9. **Sanidade final**
   - confirmar `combat_delta` durante dano/cura;
   - confirmar `player_death_observed` quando houver morte relevante;
   - fechar/reabrir o cliente e confirmar que ativação/configuração permanecem salvas.

## Build

Em Ubuntu:

```bash
sudo apt update
sudo apt install -y libpcap-dev libcap2-bin dpkg-dev
bash scripts/build-linux-deb.sh 0.1.1
```

O pacote será criado em:

```
dist/linux/IMORTAIS-Combat-Client-v0.1.1-linux-x64.deb
```

## Instalação

```bash
sudo apt install ./IMORTAIS-Combat-Client-v0.1.1-linux-x64.deb
```

Depois procure **IMORTAIS Combat Client** no menu de aplicativos do Ubuntu. O pacote instala o arquivo `.desktop` e o ícone da guilda.

O `postinst` concede apenas as capabilities de captura ao executável:

```
cap_net_raw,cap_net_admin
```

O aplicativo não precisa ser executado inteiro como root.

## Dados locais

Configuração:

```
~/.config/imortais-combat-client/settings.json
```

Outbox:

```
~/.local/state/imortais-combat-client/outbox.ndjson
```

## Instalação para o jogador

O jogador não precisa instalar Npcap, .NET, libpcap manualmente nem executar comandos de diagnóstico. O pacote `.deb` é autocontido para .NET, declara as dependências nativas e o `postinst` aplica e valida automaticamente `cap_net_raw` e `cap_net_admin`.

A tela inicial mostra o diagnóstico de captura em tempo real: quantidade de interfaces abertas, pacotes recebidos, datagramas Photon, fragmentos IPv4 e remontagens. Esses mesmos contadores seguem no `client_heartbeat`, permitindo diagnóstico remoto pelo War Room sem pedir terminal ao jogador.

## Captura v0.1.1

A captura Linux agora remonta IPv4 fragmentado antes de entregar o UDP ao parser Photon. O filtro inclui fragmentos posteriores, que não carregam o cabeçalho UDP. Também há suporte de leitura para Ethernet II, VLAN simples, raw IP, Linux cooked capture SLL e SLL2, reduzindo diferenças entre drivers e interfaces no Ubuntu.
