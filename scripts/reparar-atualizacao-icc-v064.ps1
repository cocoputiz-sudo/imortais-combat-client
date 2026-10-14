$ErrorActionPreference = "Stop"
# Recuperacao pontual do handoff do NetSparkle v0.6.3 -> v0.6.4.
# Compativel com: irm https://raw.githubusercontent.com/.../scripts/reparar-atualizacao-icc-v064.ps1 | iex
# Baixa SOMENTE a release oficial ja publicada e confirma o SHA256 antes de executar.

$repo = "cocoputiz-sudo/imortais-combat-client"
$version = "0.6.4"
$exeName = "IMORTAIS-Combat-Client.exe"
$asset = "IMORTAIS-Combat-Client-Setup-v$version.exe"
$releaseUrl = "https://github.com/$repo/releases/download/v$version"
$session = Join-Path $env:TEMP ("icc-repair-v064-" + [guid]::NewGuid().ToString("N"))
$setup = Join-Path $session $asset
$shaFile = "$setup.sha256"
$log = Join-Path $session "inno-install.log"
$installDefault = Join-Path $env:LOCALAPPDATA "Programs\IMORTAIS Combat Client"

function Find-IccInstallation {
    $bases = @(
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall",
        "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall",
        "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    )
    foreach ($base in $bases) {
        try {
            $keys = Get-ItemProperty -Path (Join-Path $base "*") -ErrorAction SilentlyContinue
            foreach ($key in $keys) {
                if ($key.DisplayName -ne "IMORTAIS Combat Client") { continue }
                $candidate = [string]$key.InstallLocation
                if ($candidate -and (Test-Path -LiteralPath (Join-Path $candidate "IMORTAIS-Combat-Client.exe"))) {
                    return $candidate
                }
            }
        } catch { }
    }
    return $installDefault
}

Write-Host "IMORTAIS Combat Client - reparo de atualizacao v$version" -ForegroundColor Cyan
Write-Host "Nao apaga configuracoes, pareamento, videos ou relatorios." -ForegroundColor Gray
$running = @(Get-Process -Name "IMORTAIS-Combat-Client" -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Warning "O Combat Client ainda esta em execucao (inclusive na bandeja)."
    throw "Feche COMPLETAMENTE o Combat Client e execute o mesmo comando novamente. Nenhum arquivo foi alterado."
}

New-Item -ItemType Directory -Force -Path $session | Out-Null
Write-Host "Baixando o instalador oficial do GitHub..." -ForegroundColor Cyan
Invoke-WebRequest -UseBasicParsing -Uri "$releaseUrl/$asset" -OutFile $setup
Invoke-WebRequest -UseBasicParsing -Uri "$releaseUrl/$asset.sha256" -OutFile $shaFile

$expectedText = [IO.File]::ReadAllText($shaFile).Trim()
if ($expectedText -notmatch '^([0-9a-fA-F]{64})(?:\s+\*?[^\s]+)?$') {
    throw "SHA256 oficial invalido. O instalador NAO sera executado."
}
$expectedHash = $Matches[1].ToLowerInvariant()
$actualHash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $expectedHash) {
    throw "SHA256 do download difere do publicado. O instalador NAO sera executado."
}
Write-Host "SHA256 confirmado: $actualHash" -ForegroundColor Green

$previousInstall = Find-IccInstallation
Write-Host "Pasta de instalacao antes do reparo: $previousInstall" -ForegroundColor Gray
$arguments = @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/LOG=`"$log`"")
Write-Host "Executando o instalador oficial e aguardando a conclusao..." -ForegroundColor Cyan
$process = Start-Process -FilePath $setup -ArgumentList $arguments -Wait -PassThru
if ($process.ExitCode -ne 0) {
    throw "Instalador terminou com codigo $($process.ExitCode). Confira $log"
}

$installedDir = Find-IccInstallation
$installedExe = Join-Path $installedDir $exeName
if (-not (Test-Path -LiteralPath $installedExe)) {
    $fallbackExe = Join-Path $previousInstall $exeName
    if (Test-Path -LiteralPath $fallbackExe) { $installedExe = $fallbackExe }
}
if (-not (Test-Path -LiteralPath $installedExe)) {
    throw "Instalador terminou, mas o executavel nao foi localizado. Confira $log"
}
$foundVersion = (Get-Item -LiteralPath $installedExe).VersionInfo.FileVersion
if ($foundVersion -notlike "0.6.4*") {
    throw "Instalacao NAO confirmada: no disco esta $foundVersion em $installedExe. Confira $log"
}

Write-Host "ATUALIZACAO CONFIRMADA: v$foundVersion" -ForegroundColor Green
Write-Host "Executavel: $installedExe" -ForegroundColor Gray
Write-Host "Abrindo o Combat Client..." -ForegroundColor Cyan
Start-Process -FilePath $installedExe -WorkingDirectory (Split-Path -Parent $installedExe)
Write-Host "Reparo concluido. Configuracoes e pareamento foram preservados." -ForegroundColor Green
