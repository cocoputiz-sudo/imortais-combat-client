# Criando seu repositório GitHub

Depois de extrair esta pasta:

```bash
git init
git add .
git commit -m "feat: bootstrap IMORTAIS combat client telemetry bridge"
git branch -M main
git remote add origin https://github.com/SEU_USUARIO/imortais-combat-client.git
git push -u origin main
```

Se preferir manter o upstream dentro do projeto como referência local, execute depois:

```bash
./scripts/bootstrap-upstream.sh
```

A pasta `upstream/` está no `.gitignore` de propósito. Para um fork real, o caminho ideal é criar o fork de `Triky313/AlbionOnline-StatisticsAnalysis` no GitHub e portar a pasta `src/Imortais.Bridge` para esse fork, preservando a GPLv3.
