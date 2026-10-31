# Classificação de dano sem substituir o medidor existente

O Combat Client já calculava dano PvP e PvE. A novidade é identificar o **alvo de cada HealthUpdate** e transmitir essa informação no `combat_delta` existente, sem quebrar clientes anteriores.

`damage` permanece igual ao medidor e respeita o filtro visual "somente dano a jogadores". `damageByTarget` traz `player`, `mob` e `unknown` como campos opcionais, além de `targetClassificationVersion=1` e `targetClassificationEvidence=passive_observed_not_complete`. A soma classificada pode divergir do `damage` legado, pois a classificação passa antes do filtro e os flushes podem cair em janelas adjacentes. **Não substituir damage por essa soma.**

Somente um player identificado é PvP; somente mob confirmado via NewMob e não provisório é PvE; objetos sem confirmação são desconhecidos. A classificação PvE não significa DG Avaloniana e não concede nenhum fee.

O `dgava_pve_window` experimental continua opt-in, desativado por padrão. Agora identifica no envelope `PlayerName` do observador pareado, mantendo `actor` como membro da party observado no payload. Capturas PvE não dependem do filtro visual. O reset por mapa e os contadores históricos permanecem inalterados.
