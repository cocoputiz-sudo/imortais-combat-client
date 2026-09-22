param(
    [string]$RepoRoot = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
$fork = Join-Path $RepoRoot 'upstream\AlbionOnline-StatisticsAnalysis\src\StatisticsAnalysisTool'
if (-not (Test-Path $fork)) {
    throw "Nao encontrei o fork canonico em: $fork"
}

Write-Host "Nenhuma instalacao de hooks e necessaria." -ForegroundColor Yellow
Write-Host "A integracao IMORTAIS ja vive diretamente no fork canonico:" -ForegroundColor Cyan
Write-Host $fork -ForegroundColor Cyan
Write-Host "Este script e intencionalmente NO-OP e nao copia nem sobrescreve arquivos." -ForegroundColor Green
