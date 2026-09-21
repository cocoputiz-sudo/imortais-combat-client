#!/usr/bin/env bash
set -euo pipefail
TARGET="${1:-./upstream}"
[ ! -e "$TARGET" ] || { echo "Destino já existe: $TARGET"; exit 1; }
git clone https://github.com/Triky313/AlbionOnline-StatisticsAnalysis.git "$TARGET"
cd "$TARGET"
git checkout -b imortais-integration
echo "Upstream clonado e branch imortais-integration criada."
