param(
    [string]$ServerUrl = 'https://cta-imortais.up.railway.app',
    [Parameter(Mandatory=$true)][string]$AgentKey,
    [Parameter(Mandatory=$true)][string]$PlayerName,
    [string]$DeviceId = $env:COMPUTERNAME,
    [string]$CtaEventId = ''
)

$dir = Join-Path $env:LOCALAPPDATA 'IMORTAIS Combat Client'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$file = Join-Path $dir 'telemetry.json'

$config = [ordered]@{
    Enabled = $true
    ServerUrl = $ServerUrl
    AgentKey = $AgentKey
    DeviceId = $DeviceId
    PlayerName = $PlayerName
    CtaEventId = $CtaEventId
    BatchIntervalMs = 1000
    MaxBatchSize = 100
}

$config | ConvertTo-Json | Set-Content -Path $file -Encoding UTF8
Write-Host "Configuracao salva em: $file" -ForegroundColor Green
