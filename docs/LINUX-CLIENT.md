# IMORTAIS Combat Client para Ubuntu

A versão Linux é um cliente nativo em .NET 10 + Avalonia. Ela não tenta executar WPF por Wine.

## Alpha 2: exportação confiável, sessão ao vivo e atualizador (13/10/2026)

**Benefícios para o jogador:**

- A aba **Registro** permite exportar o diagnóstico Photon mesmo se o XDG Desktop Portal do Ubuntu impedir a janela de salvar. O fallback grava com permissões privadas em `~/Downloads/IMORTAIS-Diagnostics` (ou `~/IMORTAIS-Diagnostics` se a pasta Downloads não existir) e mostra o caminho completo na própria interface.
- A aba **Início** mostra novos contadores atualizados em tempo real: quantidade de eventos Photon, códigos distintos e sinais brutos de party, loot e mortes. Esses sinais são eventos recebidos, **não equivalem necessariamente a abates confirmados ou loot contabilizado**.
- Em **Configurações > Atualizações do Combat Client · Linux**, há um botão **Verificar atualizações**. Se houver release Linux Alpha mais recente, o jogador escolhe **Baixar e instalar atualização**. O client confere a integridade SHA-256 do `.deb`, pede autorização gráfica pelo PolicyKit do Ubuntu e instala sem terminal. Depois, é necessário fechar e abrir o aplicativo.
- Em caso de falha ou cancelamento da autorização gráfica, o pacote validado permanece em `~/Downloads/IMORTAIS-Updates` para instalação gráfica manual. O app exibe o caminho e informa o erro.
- A versão indicada na interface e no heartbeat agora é **0.2.0-alpha.2**.

**Primeira transição:** quem estiver na Alpha 1 precisa instalar a Alpha 2 uma vez pelo instalador gráfico, porque a Alpha 1 não tinha atualizador. A partir da Alpha 2, a verificação é feita na interface. O aplicativo também tenta consultar novas versões ao iniciar, mas **não instala silenciosamente sem o clique e a autorização do usuário**.

**Segurança e limitações:** só são consideradas releases Alpha publicadas pelo repositório oficial `cocoputiz-sudo/imortais-combat-client` com pacote `.deb` e SHA-256 associado. A verificação de hash protege a integridade do download, mas não substitui uma assinatura independente da distribuição. A instalação automática depende da disponibilidade do PolicyKit (`pkexec`) e de uma sessão gráfica Ubuntu com agente de autenticação.

**Checklist da Alpha 2:**

- [ ] Exportar JSON com XDG Desktop Portal funcionando normalmente
- [ ] Exportar JSON com portal retornando `AccessDenied`, verificando o arquivo local e o caminho na tela
- [ ] Confirmar que o JSON não contém nomes, tokens e valores de parâmetros
- [ ] Conferir contadores na aba Início durante party, loot e fight
- [ ] Buscar release inexistente, release atual e release mais nova
- [ ] Impedir instalação com SHA-256 inválido ou origem de download não oficial
- [ ] Autorizar instalação via PolicyKit e reiniciar a GUI sem perder ativação/outbox
- [ ] Testar em Ubuntu 22.04 LTS e Ubuntu 24.04 LTS

A Alpha 2 deve ser homologada em jogo real antes de promoção à versão Linux estável. O Windows v0.6.2 e seu feed de atualização não são alterados por este PR.

## Alpha Dragonfire 3 · 13/10/2026

**Objetivo:** restabelecer a captura e o processamento de eventos no Linux após a atualização Dragonfire Patch 3 do Albion (12/10/2026).

### Correções implementadas

- O `PhotonParser` retorna um código de pacote de apenas 8 bits, enquanto o Albion também informa o **código real do evento no parâmetro `252`**. O Linux agora despacha por esse valor completo e rejeita um valor 252 ilegível. Sem isso, `OtherGrabbedLoot = 278` não era acessível.
- Preserva os eventos antigos abaixo de 124 e fixa `ClusterInfoUpdate=139`, `TimeSync=159`, `KilledPlayer=163`, `Died=164`, `PartyJoined=230`, `PartyDisbanded=231`, `PartyPlayerJoined=232`, `PartyPlayerLeft=234`, `OtherGrabbedLoot=278`, `FriendOnlineStatus=297`, `FeatureSwitchInfo=503`.
- A lista completa de PT é **validada antes de substituir a anterior**: nomes e GUIDs devem ter mesma quantidade, sem entradas vazias ou duplicadas, inclusive no formato compactado `byte[16*N]`. Atualiza a UI e a telemetria de forma atômica.
- Reenvia a party atual quando o CTA muda mesmo sem alteração de integrantes.
- Envia `loot` de itens não-prata com looter, índice, quantidade e mapa. **Limitação alpha:** sem catálogo dos nomes/preços, `item` é `ITEM_INDEX_N` e `estimatedValue` é zero. Prata continua fora desse contador.
- Continua capturando `Died` letais relevantes. `KilledPlayer` fica como candidato de abate; não duplica contagem com o evento letal de morte.
- Botão **EXPORTAR DIAGNÓSTICO PHOTON (JSON)** na aba Registro. Guarda apenas contagem por código e assinatura estrutural (chaves/tipos/tamanhos), sem nomes, GUIDs, valores ou tokens.
- Restringe permissões de `settings.json` (inclui token de autenticação) e do `outbox.ndjson` ao usuário local (`0600`).
- Atualiza versão do cliente para `0.2.0-alpha.1-linux`.

### Instalação sem terminal

1. Baixe `IMORTAIS-Combat-Client-v0.2.0-alpha.2-linux-x64.deb` na release **Linux Alpha** do repositório.
2. Abra o pacote pelo instalador gráfico do Ubuntu; autorize sua instalação quando solicitado.
3. Encontre **IMORTAIS Combat Client** no menu de aplicativos. Abra e ative com o código de 6 dígitos gerado no War Room.
4. Se Albion estiver aberto, confira no painel as interfaces, datagramas Photon e jogadores da party.
5. Teste entrar/sair de PT, dano e loot; exporte o JSON pelo botão de diagnóstico se algo não funcionar.

Não é necessário instalar manualmente Npcap, .NET, libpcap nem rodar comandos de captura. Dependências e `cap_net_raw,cap_net_admin` são configuradas pelo `.deb`.

### Limites conhecidos da alpha

- O Linux **ainda não possui** gravação de vídeo/Highlights (é uma funcionalidade Windows).
- Instalação e futuras atualizações usam o instalador gráfico do Ubuntu; o **updater automático interno ainda não foi implementado**. Não anunciar atualização automática como disponível.
- A validação automatizada usa Ubuntu 24.04 e testa parser/contratos, compilação, pacote, SHA256, instalação e capabilities. Falta homologação real com Albion no Ubuntu 22.04 e 24.04, incluindo permissões, envio ao War Room e funcionamento com a PT ZvZ.
- Não há integração das páginas de Guild Might/Challenge do Windows. A alpha prioriza estabilidade de PT, combate, presença, morte e loot.
- Esta alpha não é parte do feed de atualização do Windows v0.6.2 e **não depende de merge da branch Linux na main**.

## Interface

A GUI possui:

- **IMORTAIS / Início**: Albion, War Room, captura, jogador, CTA, mapa, party e outbox.
- **Registro**: eventos de captura, parser, party, mapa, mortes e War Room.
- **Medidor de dano**: dano, cura, kills e mortes por jogador.
- **Party**: formação observada pelos eventos Photon.
- **Configurações**: servidor, jogador, Device ID, captura automática e autostart do Ubuntu.

## Captura

O cliente usa **libpcap** e o mesmo parser Photon multiplataforma existente no repositório.

Eventos já ligados na v0.2.0-alpha.1:

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

O `ChangeCluster` entrega um índice interno. No cliente Windows esse índice passa por `WorldData.GetUniqueNameOrDefault()` antes de `ImortaisEventBridge.ZoneChange`, resultando no nome legível do mapa. O Linux replica a mesma resolução com uma tabela índice → `UniqueName` embutida no cliente.

A tabela Linux contém **todas as 1.427 entradas** do `formatted/world.json`, incluindo índices numéricos e não numéricos (`TNL-*`, `HELLGATE-*`, `DNG-*`, `BLACKBANK-*` etc.). Ela é gerada por `scripts/generate-linux-world-map-names.mjs`.

Fonte reproduzível usada:
- repositório: `ao-data/ao-bin-dumps`;
- arquivo: `formatted/world.json`;
- commit: `585e848493070f2d918f695d0c40fedfe9735ecb`;
- data da fonte: `2026-08-31T11:35:17Z`.

Para regenerar:

```bash
node scripts/generate-linux-world-map-names.mjs
```

O script também aceita `--input caminho/world.json` para geração offline e `--check` para verificar se o arquivo gerado está atualizado.

Exemplos cobertos pelo teste de contrato:
- `3004` → `Martlock`;
- `TNL-001` → `Ouyos-Aoeuam`;
- `HELLGATE-01-10v10-01` → `The Plains`.

A telemetria envia o nome resolvido em `zone_change.clusterName` e em `player_presence_snapshot.cluster`, preservando o valor cru apenas em `zone_change.clusterIndex`.

## Build

Em Ubuntu:

```bash
sudo apt update
sudo apt install -y libpcap-dev libcap2-bin dpkg-dev
bash scripts/build-linux-deb.sh '0.2.0~alpha2' '0.2.0-alpha.2'
```

O pacote será criado em:

```
dist/linux/IMORTAIS-Combat-Client-v0.2.0-alpha.2-linux-x64.deb
```

## Instalação

```bash
sudo apt install ./IMORTAIS-Combat-Client-v0.2.0-alpha.2-linux-x64.deb
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

## Captura v0.2.0-alpha.1

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
