param([string]$Target=".\upstream")
$ErrorActionPreference='Stop'
if(Test-Path $Target){ throw "Destino já existe: $Target" }
git clone https://github.com/Triky313/AlbionOnline-StatisticsAnalysis.git $Target
Push-Location $Target
git checkout -b imortais-integration
Pop-Location
Write-Host "Upstream clonado em $Target e branch imortais-integration criada." -ForegroundColor Green
Write-Host "Leia docs/UPSTREAM-INTEGRATION.md antes de copiar os hooks." -ForegroundColor Yellow
