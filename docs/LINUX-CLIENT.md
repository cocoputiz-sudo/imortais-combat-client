# Guild Combat Client para Linux (Ubuntu)

**Versão 1.0.0.** Cliente nativo (.NET 10 + Avalonia) que usa o **mesmo núcleo de envio do Windows**: pareamento por guilda, envio para o War Room da guilda do personagem logado, fila e formatos idênticos. Não usa Wine.

## Instalar

1. Baixe `IMORTAIS-Combat-Client-v1.0.0-linux-x64.deb` (release **Guild Combat Client Linux** no GitHub, marcada como pré-release).
2. Abra o arquivo pelo instalador do Ubuntu (ou `sudo apt install ./IMORTAIS-Combat-Client-v1.0.0-linux-x64.deb`) e autorize.
3. Abra **Guild Combat Client** no menu de aplicativos.
4. Aba **GUILD**: escolha a guilda, digite o código de 6 dígitos gerado no War Room e clique em **ATIVAR TELEMETRIA**.
5. Abra o Albion. Na aba **Início** o Albion passa para **DETECTADO**, aparecem personagem, guilda, mapa e party.

O instalador aplica sozinho a permissão de captura (`cap_net_raw,cap_net_admin`), então o programa roda sem `sudo`. Não precisa instalar .NET nem `libpcap-dev`.

Quem tinha a alpha: instalar a 1.0.0 substitui a alpha e o pareamento é migrado (não precisa parear de novo).

## O que envia ao War Room (igual ao Windows)

- Entrada no mundo com a guilda do personagem (escolhe o War Room certo; outra guilda sem pareamento não envia nada).
- Mapa, party completa, jogadores por perto (presença).
- Dano e cura da party, com a separação PvP / mob / desconhecido.
- Mortes letais que envolvem a guilda ou a party, e abate/morte do próprio personagem.

## Ainda só no Windows

- Highlights (vídeo), Death Review e captura do Top DPS da DG Avaloniana.
- Equipamento na morte e nome dos itens de loot (dependem do catálogo de itens do jogo). Kill Fame continua vindo da API oficial.
- Sondas de Guild Might.

## Onde ficam os arquivos

- Pareamentos e fila: `~/.local/share/IMORTAIS Combat Client/` (mesmo formato do Windows).
- Preferências: `~/.config/imortais-combat-client/settings.json`.
- Diagnóstico (aba Registro): exporta só a estrutura dos pacotes, sem nomes nem valores.

## Atualizar

Aba **Configurações → Atualizações**: o app consulta as pré-releases `vX.Y.Z-linux` do repositório oficial, confere o SHA-256 e pede autorização pela janela do Ubuntu.

## Para quem mantém

- Códigos de evento e operação vêm dos arquivos do Windows (`Network/EventCodes.cs`, `Network/OperationCodes.cs`): um patch do Albion corrigido no Windows vale para o Linux.
- Testes: `dotnet run --project src/Imortais.LinuxClient.ContractTests -c Release` (do pacote do jogo ao War Room num servidor local) e `tests/ClientCore.LogicTests`.
- Pacote: `bash scripts/build-linux-deb.sh 1.0.0`. Publicação: workflow **Linux client** → *Run workflow* na `main` com a versão.
