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
