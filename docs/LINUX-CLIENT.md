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
- Leave

A telemetria usa os mesmos tipos esperados pelo War Room:

- client_heartbeat
- party_snapshot
- combat_delta
- player_death_observed
- zone_change
- player_presence_snapshot

`player_presence_snapshot` é emitido a cada 15 s durante CTA ativo, com até 500 jogadores observados pelo `NewCharacter`, removidos pelo `Leave`. A troca de mapa limpa imediatamente o conjunto local antes do próximo snapshot.

### Nome do mapa

O `ChangeCluster` entrega um índice interno (por exemplo, `3004`). No cliente Windows esse índice passa por `WorldData.GetUniqueNameOrDefault()` antes de `ImortaisEventBridge.ZoneChange`, resultando no nome legível do mapa. O Linux replica essa resolução com uma tabela compacta índice → nome embutida no cliente, gerada a partir do `formatted/world.json` do projeto ao-data/ao-bin-dumps.

Assim, para `3004`, o Linux envia `Martlock` em `zone_change.clusterName` e em `player_presence_snapshot.cluster`, preservando `3004` apenas em `zone_change.clusterIndex`.

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


## Checklist de teste real · Ubuntu 22.04 / 24.04

Execute este checklist **antes de mesclar o cliente Linux na main**:

- [ ] Instalar o pacote `.deb` em Ubuntu 22.04 LTS.
- [ ] Instalar o mesmo pacote `.deb` em Ubuntu 24.04 LTS.
- [ ] Abrir pelo menu de aplicativos sem `sudo` e confirmar que a GUI inicia.
- [ ] Confirmar as 5 abas: **IMORTAIS / Início**, **Registro**, **Medidor de dano**, **Party** e **Configurações**.
- [ ] Confirmar que a captura inicia sem root e que o executável possui somente `cap_net_raw,cap_net_admin`.
- [ ] Abrir Albion Online e confirmar na aba Início que **Rede / Photon** passa a registrar datagramas.
- [ ] No War Room > **Dispositivos**, confirmar heartbeat do Linux com versão, captura ativa, nome do player e CTA atual.
- [ ] Entrar/sair de party e confirmar `party_snapshot` chegando ao War Room.
- [ ] Durante CTA ativo, aproximar-se de outros jogadores e confirmar `player_presence_snapshot` chegando a cada ~15 s.
- [ ] Trocar de mapa e confirmar `zone_change` imediatamente e que o snapshot seguinte não contém jogadores do mapa anterior.
- [ ] Confirmar que o painel **Forças observadas** contabiliza um observer Linux sem duplicar jogadores vistos também por Windows.
- [ ] Confirmar no heartbeat/GUI que **fragmentos IPv4** e **remontagens IPv4** aumentam quando houver tráfego fragmentado.
- [ ] Confirmar `player_death_observed`, dano/cura e Medidor de dano durante uma fight real.
- [ ] Fechar e reabrir o cliente e confirmar persistência de ativação/configuração e reenvio do outbox pendente.

Somente após esse teste real a integração deve ser mesclada.
