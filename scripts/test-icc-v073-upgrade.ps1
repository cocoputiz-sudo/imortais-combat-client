param([Parameter(Mandatory=$true)][string]$CandidateSetup)
$ErrorActionPreference = "Stop"
$releaseBase = "https://github.com/cocoputiz-sudo/imortais-combat-client/releases/download/v0.7.2"
$oldSetup = Join-Path $env:RUNNER_TEMP "IMORTAIS-Combat-Client-Setup-v0.7.2.exe"
$hashPath = "$oldSetup.sha256"
$installRoot = Join-Path $env:LOCALAPPDATA "Programs\IMORTAIS Combat Client"
$exePath = Join-Path $installRoot "IMORTAIS-Combat-Client.exe"
$configDir = Join-Path $env:LOCALAPPDATA "IMORTAIS Combat Client"
$configPath = Join-Path $configDir "telemetry.json"
Invoke-WebRequest -Uri "$releaseBase/IMORTAIS-Combat-Client-Setup-v0.7.2.exe" -OutFile $oldSetup
Invoke-WebRequest -Uri "$releaseBase/IMORTAIS-Combat-Client-Setup-v0.7.2.exe.sha256" -OutFile $hashPath
$expected = (Get-Content $hashPath -Raw).Trim().Split(' ')[0].ToLowerInvariant()
$actual = (Get-FileHash $oldSetup -Algorithm SHA256).Hash.ToLowerInvariant()
if ($expected -ne $actual) { throw "v0.7.2 release SHA256 mismatch" }
New-Item -ItemType Directory -Force -Path $configDir | Out-Null
$config = '{"Enabled":true,"ServerUrl":"https://imortais.macknacloud.com.br","AgentKey":"test-upgrade-preserved","DeviceId":"qa-upgrade","PlayerName":"TestPlayer","GuildProbeLocalDiagnosticsEnabled":true}'
[IO.File]::WriteAllText($configPath,$config,[Text.UTF8Encoding]::new($false))
$setupArgs = @("/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART","/SP-")
$p = Start-Process -FilePath $oldSetup -ArgumentList $setupArgs -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "v0.7.2 installer returned $($p.ExitCode)" }
if (-not (Test-Path $exePath)) { throw "v0.7.2 not installed at expected stable path" }
if ((Get-Item $exePath).VersionInfo.FileVersion -notlike "0.7.2*") {
 throw "Expected installed v0.7.2 before upgrade"
}
$p = Start-Process -FilePath $CandidateSetup -ArgumentList $setupArgs -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "v0.7.3 installer returned $($p.ExitCode)" }
if ((Get-Item $exePath).VersionInfo.FileVersion -notlike "0.7.3*") {
 throw "Upgrade did not replace v0.7.2 with v0.7.3 in-place"
}
$json = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
if ($json.AgentKey -ne "test-upgrade-preserved" -or $json.DeviceId -ne "qa-upgrade") {
 throw "Upgrade damaged normal telemetry pairing"
}
if ($json.ServerUrl -ne "https://imortais.macknacloud.com.br") {
 throw "Upgrade changed the paired War Room server"
}
$menu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
if (-not (Test-Path (Join-Path $menu "Guild Combat Client.lnk"))) { throw "Start menu shortcut 'Guild Combat Client' missing" }
if (Test-Path (Join-Path $menu "IMORTAIS Combat Client.lnk")) { throw "Legacy start menu shortcut was not removed" }
if ($json.GuildProbeLocalDiagnosticsEnabled -ne $true) {
 throw "Upgrade lost local Guild dumps preference"
}
Write-Host "PASS v0.7.2 -> v0.7.3 in-place upgrade, persisted pairing/server/preferences and renamed shortcut"
