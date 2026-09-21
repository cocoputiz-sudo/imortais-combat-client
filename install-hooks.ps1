param(
    [string]$RepoRoot = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
$upstream = Join-Path $RepoRoot 'upstream\AlbionOnline-StatisticsAnalysis\src\StatisticsAnalysisTool'
if (-not (Test-Path $upstream)) {
    throw "Nao encontrei o upstream em: $upstream"
}

$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $RepoRoot ".imortais-backup-$stamp"
New-Item -ItemType Directory -Force -Path $backup | Out-Null

$files = @(
    'Network\Manager\LootController.cs',
    'Network\Manager\CombatController.cs',
    'Network\Manager\StatisticController.cs',
    'Party\PartyController.cs'
)

foreach ($rel in $files) {
    $dst = Join-Path $upstream $rel
    if (-not (Test-Path $dst)) { throw "Arquivo upstream nao encontrado: $dst" }
    $backupFile = Join-Path $backup $rel
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $backupFile) | Out-Null
    Copy-Item $dst $backupFile -Force

    $src = Join-Path $packageRoot ("patched\" + $rel)
    Copy-Item $src $dst -Force
}

$imortaisDst = Join-Path $upstream 'Imortais'
New-Item -ItemType Directory -Force -Path $imortaisDst | Out-Null
Copy-Item (Join-Path $packageRoot 'src\StatisticsAnalysisTool\Imortais\*.cs') $imortaisDst -Force

Write-Host "Hooks IMORTAIS instalados." -ForegroundColor Green
Write-Host "Backup: $backup" -ForegroundColor Yellow
Write-Host "Agora compile o projeto upstream antes de dar commit." -ForegroundColor Cyan
