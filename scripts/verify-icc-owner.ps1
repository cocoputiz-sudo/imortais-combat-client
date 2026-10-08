# Owner-bound private installer preflight. Never print/log/save the bearer credential.
# Server must authenticate BOTH a currently active token and its bound device_id.
$ErrorActionPreference = "Stop"
try {
    $configPath = Join-Path $env:LOCALAPPDATA "IMORTAIS Combat Client\telemetry.json"
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) { exit 21 }

    $config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $deviceId = [string]$config.DeviceId
    $token = [string]$config.AgentKey
    if ([string]::IsNullOrWhiteSpace($deviceId) -or [string]::IsNullOrWhiteSpace($token)) { exit 22 }

    # The Enabled preference is deliberately not used as an identity check:
    # an authenticated, paired device remains paired even when telemetry is paused.
    $uri = "https://cta-imortais.up.railway.app/api/telemetry/context?deviceId=" +
        [Uri]::EscapeDataString($deviceId)
    try {
        $data = Invoke-RestMethod -Uri $uri -Method Get -Headers @{
            Authorization = "Bearer " + $token
        } -TimeoutSec 15 -ErrorAction Stop
    } catch {
        $response = $_.Exception.Response
        if ($null -ne $response) {
            try {
                $code = [int]$response.StatusCode
                if ($code -eq 401 -or $code -eq 403) { exit 26 }
            } catch { }
        }
        exit 25
    }
    if ($data.ok -ne $true) { exit 27 }
    # This is the staff-bound player returned by the trusted server, NEVER
    # the easily edited PlayerName from the local telemetry.json.
    if ([string]::IsNullOrWhiteSpace([string]$data.playerName)) { exit 27 }
    if ([string]$data.playerName -ine "BadMack") { exit 24 }
    exit 0
} catch {
    # Avoid emitting any exception or request headers, which may contain tokens.
    exit 25
}
