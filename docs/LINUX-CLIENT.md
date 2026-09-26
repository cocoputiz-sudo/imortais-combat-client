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

Eventos já ligados na v0.1.0:

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
bash scripts/build-linux-deb.sh 0.1.0
```

O pacote será criado em:

```
dist/linux/IMORTAIS-Combat-Client-v0.1.0-linux-x64.deb
```

## Instalação

```bash
sudo apt install ./IMORTAIS-Combat-Client-v0.1.0-linux-x64.deb
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

## Pendência conhecida da v0.1.0

O primeiro corte ignora **fragmentação IPv4 no nível IP**. A fragmentação interna do protocolo Photon continua sendo tratada pelo parser existente. Antes de uma release estável, a remontagem IPv4 usada no cliente Windows deve ser portada para o Linux e validada em CTA real.
