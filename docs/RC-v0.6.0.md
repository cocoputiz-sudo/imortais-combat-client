# IMORTAIS Combat Client v0.6.0 RC

## Escopo da RC

A RC combina o PR #31 (estabilidade da telemetria) e o Marco 2 de Highlights aprovado em teste real.

### Highlights experimentais
- gravação em segundo plano vem **DESLIGADA** por padrão;
- abate pessoal letal: 45 s antes + 15 s depois;
- abates dentro de 15 s coalescem no mesmo clipe, teto 120 s;
- captura de áudio somente do Albion;
- Windows 10 pode mostrar borda amarela da captura;
- ainda não medido em ZvZ de grande escala nem em máquina fraca.

### Telemetria
- refresh de contexto no máximo a cada 30 s;
- ingest backoff 2 -> 60 s;
- reset para 2 s no primeiro sucesso;
- HTTP 401 bloqueia reenvio com a mesma chave.

### Might Probe experimental
- observa passivamente `GetGuildMightCategoryOverview` e `GetGuildMightCategoryContribution`;
- não envia requests ao Albion;
- reaproveita a captura Photon já existente no Combat Client;
- envia `guild_might_probe` saneado e deduplicado ao War Room;
- nesta RC ainda não calcula ranking/SP: primeiro validaremos o layout real dos parâmetros.

## Segurança do teste

O feed de produção da main NÃO é alterado pela RC.

A RC local usa:
- feed: `http://127.0.0.1:48159/imortais-netsparkle-v060-rc.xml`;
- instalador servido somente em loopback;
- mesma chave Ed25519 já usada pelo updater oficial;
- verificação obrigatória do arquivo `.signature` do XML pelo helper antes de alterar o config local;
- verificação do Ed25519 e SHA-256 do instalador antes do teste.

O ZIP de CI usa `LocalInstallerBuild=true`, portanto a RC instalada não inicia o loop de atualização em background durante a validação. Isso evita qualquer consulta acidental ao feed de produção enquanto testamos.

## Roteiro funcional da RC final

O caminho NetSparkle 0.5.9 -> 0.6.0 já foi aprovado. Para esta RC final, se a máquina já está em uma RC v0.6.0 anterior, não é necessário repetir o updater: gere o setup local e instale por cima manualmente.

1. confirmar versão v0.6.0 e War Room conectado;
2. confirmar que a migração one-shot deixou **GRAVAÇÃO: NÃO**, mesmo que a RC anterior tivesse gravado SIM nas preferências;
3. ativar gravação e fazer um abate;
4. fazer três abates em menos de 15 s: deve gerar **um único clipe** com contagem;
5. fazer um abate e fechar o Albion logo depois: salvar o trecho disponível;
6. fazer um abate e logo depois colocar **GRAVAÇÃO: NÃO**: salvar o clipe pendente antes de liberar o buffer;
7. abrir no jogo as telas de Might da guilda: Overview e contribuição de pelo menos duas categorias;
8. copiar diagnóstico: deve aparecer `Guild Might Probe` com pelo menos um evento após abrir as telas;
9. enviar diagnóstico + MP4s. Se houver probe, o War Room já armazena o evento `guild_might_probe` para análise do layout.

Nada deste pacote publica release, altera o feed da main ou atualiza o mural.
