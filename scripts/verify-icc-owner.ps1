# Local install-time gate. Never print or persist the telemetry AgentKey.
# Exit 0 only for a device token currently registered by the War Room to BadMack.
$ErrorActionPreference = "Stop"
try {
    $configPath = Join-Path $env:LOCALAPPDATA "IMORTAIS Combat Client\telemetry.json"
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) { exit 21 }
    $config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $deviceId = [string]$config.DeviceId
    $token = [string]$config.AgentKey
    if ([string]::IsNullOrWhiteSpace($deviceId) -or [string]::IsNullOrWhiteSpace($token)) { exit 22 }
    if (-not $config.Enabled) { exit 23 }
    # This exact endpoint validates the bearer token against the stored device_id,
    # and responds with the STAFF-BOUND player_name when no playerName query is given.
    $uri = "https://cta-imortais.up.railway.app/api/telemetry/context?deviceId=" +
        [uri]::EscapeDataString($deviceId)
    $data = Invoke-RestMethod -Uri $uri -Method Get -Headers @{ Authorization = "Bearer " + $token } -TimeoutSec 15
    if ($data.ok -ne $true -or [string]$data.playerName -ine "BadMack") { exit 24 }
    exit 0
} catch {
    # Do not emit exception text: some HTTP libraries include request headers.
    exit 25
}
