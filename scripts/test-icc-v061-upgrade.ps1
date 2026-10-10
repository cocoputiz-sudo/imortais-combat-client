param([Parameter(Mandatory=$true)][string]$CandidateSetup)
$ErrorActionPreference = "Stop"
$releaseBase = "https://github.com/cocoputiz-sudo/imortais-combat-client/releases/download/v0.6.0"
$oldSetup = Join-Path $env:RUNNER_TEMP "IMORTAIS-Combat-Client-Setup-v0.6.0.exe"
$hashPath = "$oldSetup.sha256"
$installRoot = Join-Path $env:LOCALAPPDATA "Programs\IMORTAIS Combat Client"
$exePath = Join-Path $installRoot "IMORTAIS-Combat-Client.exe"
$configDir = Join-Path $env:LOCALAPPDATA "IMORTAIS Combat Client"
$configPath = Join-Path $configDir "telemetry.json"
Invoke-WebRequest -Uri "$releaseBase/IMORTAIS-Combat-Client-Setup-v0.6.0.exe" -OutFile $oldSetup
Invoke-WebRequest -Uri "$releaseBase/IMORTAIS-Combat-Client-Setup-v0.6.0.exe.sha256" -OutFile $hashPath
$expected = (Get-Content $hashPath -Raw).Trim().Split(' ')[0].ToLowerInvariant()
$actual = (Get-FileHash $oldSetup -Algorithm SHA256).Hash.ToLowerInvariant()
if ($expected -ne $actual) { throw "v0.6.0 release SHA256 mismatch" }
New-Item -ItemType Directory -Force -Path $configDir | Out-Null
$config = '{"Enabled":true,"ServerUrl":"https://cta-imortais.up.railway.app","AgentKey":"test-upgrade-preserved","DeviceId":"qa-upgrade","PlayerName":"TestPlayer","GuildProbeLocalDiagnosticsEnabled":true}'
[IO.File]::WriteAllText($configPath,$config,[Text.UTF8Encoding]::new($false))
$setupArgs = @("/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART","/SP-")
$p = Start-Process -FilePath $oldSetup -ArgumentList $setupArgs -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "v0.6.0 installer returned $($p.ExitCode)" }
if (-not (Test-Path $exePath)) { throw "v0.6.0 not installed at expected stable path" }
if ((Get-Item $exePath).VersionInfo.FileVersion -notlike "0.6.0*") {
 throw "Expected installed v0.6.0 before upgrade"
}
$p = Start-Process -FilePath $CandidateSetup -ArgumentList $setupArgs -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "v0.6.1 installer returned $($p.ExitCode)" }
if ((Get-Item $exePath).VersionInfo.FileVersion -notlike "0.6.1*") {
 throw "Upgrade did not replace v0.6.0 with v0.6.1 in-place"
}
$json = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
if ($json.AgentKey -ne "test-upgrade-preserved" -or $json.DeviceId -ne "qa-upgrade") {
 throw "Upgrade damaged normal telemetry pairing"
}
if ($json.GuildProbeLocalDiagnosticsEnabled -ne $true) {
 throw "Upgrade lost local Guild dumps preference"
}
Write-Host "PASS v0.6.0 -> v0.6.1 in-place upgrade and persisted pairing/preferences"
