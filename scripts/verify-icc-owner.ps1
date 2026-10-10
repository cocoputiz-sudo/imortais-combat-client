param(
    [Parameter(Mandatory=$true)]
    [ValidatePattern('^[A-Fa-f0-9]{64}$')]
    [string]$ExpectedDeviceHash
)

# Only permit an already-paired device with an explicitly allowlisted DeviceId.
# No AgentKey, DeviceId or hardware identifier is printed or written to disk.
$ErrorActionPreference = "Stop"
try {
    $configPath = Join-Path $env:LOCALAPPDATA "IMORTAIS Combat Client\telemetry.json"
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) { exit 21 }

    $config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $deviceId = [string]$config.DeviceId
    $token = [string]$config.AgentKey
    if ([string]::IsNullOrWhiteSpace($deviceId) -or [string]::IsNullOrWhiteSpace($token)) { exit 22 }

    # DeviceId is the identifier already used by the paired Combat Client.
    # It is not asserted to be an immutable hardware serial.
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($deviceId.Trim().ToLowerInvariant())
        $deviceHash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace("-","").ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
    if ($deviceHash -ne $ExpectedDeviceHash.ToLowerInvariant()) { exit 29 }

    # Local player name alone is NOT proof of identity: verify an active token
    # with the server, against the exact bound device ID, below.
    if ([string]$config.PlayerName -ine "BadMack") { exit 28 }

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
    # A missing server-side player name is allowed: some legacy tokens
    # are device-bound but were created without a staff-bound player name.
    # A *different* server-bound player name still blocks the install.
    $verifiedPlayer = [string]$data.playerName
    if (-not [string]::IsNullOrWhiteSpace($verifiedPlayer) -and
        $verifiedPlayer -ine "BadMack") { exit 24 }
    exit 0
} catch {
    # Avoid emitting any exception or request headers, which may contain tokens.
    exit 25
}
