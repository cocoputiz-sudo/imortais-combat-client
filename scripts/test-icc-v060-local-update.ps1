param(
    [int]$Port = 48159,
    [int]$TimeoutMinutes = 30
)

$ErrorActionPreference = "Stop"
$VersionFrom = "0.5.9"
$VersionTo = "0.6.0"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$FeedName = "imortais-netsparkle-v060-rc.xml"
$Feed = Join-Path $Root $FeedName
$FeedSigFile = "$Feed.signature"
$Setup = Join-Path $Root "IMORTAIS-Combat-Client-Setup-v0.6.0.exe"
$SetupHashFile = "$Setup.sha256"
$InstallDir = Join-Path $env:LOCALAPPDATA "Programs\IMORTAIS Combat Client"
$InstalledExe = Join-Path $InstallDir "IMORTAIS-Combat-Client.exe"
$ProductionFeed = "https://raw.githubusercontent.com/cocoputiz-sudo/imortais-combat-client/main/upstream/AlbionOnline-StatisticsAnalysis/src/StatisticsAnalysisTool/imortais-netsparkle-update-check.xml"
$LocalFeed = "http://127.0.0.1:$Port/$FeedName"
$SigningRoot = Join-Path $env:LOCALAPPDATA "IMORTAIS Combat Client\signing-keys"
$ToolRoot = Join-Path $env:LOCALAPPDATA "IMORTAIS Combat Client\tools\netsparkle"
$PubKeyFile = Join-Path $SigningRoot "NetSparkle_Ed25519.pub"
$PrivKeyFile = Join-Path $SigningRoot "NetSparkle_Ed25519.priv"
$SparkleTool = Join-Path $ToolRoot "netsparkle-generate-appcast.exe"
$backups = @()
$serverJob = $null
$success = $false

function Verify-File([string]$Path,[string]$Signature) {
    $out = & $SparkleTool --verify $Path --signature $Signature
    if ($LASTEXITCODE -ne 0 -or ($out -join "`n") -notmatch 'Signature valid') {
        throw "Assinatura inválida: $Path"
    }
}
function Restore-Configs {
    foreach ($b in $backups) {
        if (Test-Path -LiteralPath $b.Backup) {
            Copy-Item -LiteralPath $b.Backup -Destination $b.Path -Force
        }
    }
}
function Get-Version([string]$Exe) {
    if (-not (Test-Path -LiteralPath $Exe)) { return "" }
    return (Get-Item -LiteralPath $Exe).VersionInfo.FileVersion
}

foreach ($p in @($Feed,$FeedSigFile,$Setup,$SetupHashFile,$PubKeyFile,$PrivKeyFile,$SparkleTool,$InstalledExe)) {
    if (-not (Test-Path -LiteralPath $p)) { throw "Arquivo obrigatório ausente: $p" }
}
$env:SPARKLE_PUBLIC_KEY = (Get-Content -LiteralPath $PubKeyFile -Raw).Trim()
$env:SPARKLE_PRIVATE_KEY = (Get-Content -LiteralPath $PrivKeyFile -Raw).Trim()

# Exigência explícita da RC: verificar a assinatura separada do XML antes de servir qualquer byte.
$feedSig = (Get-Content -LiteralPath $FeedSigFile -Raw).Trim()
Verify-File $Feed $feedSig
Write-Host "✅ .signature do XML RC verificada." -ForegroundColor Green

[xml]$feedXml = Get-Content -LiteralPath $Feed -Raw
$enclosure = $feedXml.rss.channel.item.enclosure
if ($enclosure.url -ne "http://127.0.0.1:$Port/IMORTAIS-Combat-Client-Setup-v0.6.0.exe") { throw "Feed RC não está preso ao loopback esperado." }
if ($enclosure.version -ne "0.6.0") { throw "Feed RC não anuncia 0.6.0." }
if ([long]$enclosure.length -ne (Get-Item $Setup).Length) { throw "Length do enclosure não confere." }
Verify-File $Setup ([string]$enclosure.edSignature)
Write-Host "✅ assinatura Ed25519 do instalador verificada." -ForegroundColor Green

$expectedHash = (Get-Content -LiteralPath $SetupHashFile -Raw).Trim().ToLowerInvariant()
$actualHash = (Get-FileHash -LiteralPath $Setup -Algorithm SHA256).Hash.ToLowerInvariant()
if ($expectedHash -ne $actualHash) { throw "SHA-256 do setup não confere." }
Write-Host "✅ SHA-256 do instalador confere." -ForegroundColor Green

$installedVersion = Get-Version $InstalledExe
if ($installedVersion -notlike "0.5.9*") { throw "A máquina de teste precisa começar em v0.5.9. Encontrado: $installedVersion" }

$configFiles = Get-ChildItem -LiteralPath $InstallDir -Filter "*.config" -File | Where-Object {
    (Get-Content -LiteralPath $_.FullName -Raw) -like "*$ProductionFeed*"
}
if (-not $configFiles) { throw "Nenhum config instalado contém o feed de produção; não vou alterar outro arquivo por aproximação." }

try {
    Get-Process "IMORTAIS-Combat-Client" -ErrorAction SilentlyContinue | Stop-Process -Force
    foreach ($cfg in $configFiles) {
        $backup = "$($cfg.FullName).v060-rc-backup"
        Copy-Item -LiteralPath $cfg.FullName -Destination $backup -Force
        $backups += [pscustomobject]@{Path=$cfg.FullName;Backup=$backup}
        $txt = Get-Content -LiteralPath $cfg.FullName -Raw
        $txt = $txt.Replace($ProductionFeed,$LocalFeed)
        [IO.File]::WriteAllText($cfg.FullName,$txt,(New-Object Text.UTF8Encoding($false)))
    }
    Write-Host "✅ feed alterado SOMENTE na instalação local v0.5.9." -ForegroundColor Green

    $serverRoot = $Root
    $serverJob = Start-Job -ArgumentList $serverRoot,$Port -ScriptBlock {
        param($root,$port)
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$port)
        $listener.Start()
        try {
            while ($true) {
                $client = $listener.AcceptTcpClient()
                try {
                    $stream = $client.GetStream()
                    $reader = New-Object IO.StreamReader($stream,[Text.Encoding]::ASCII,$false,1024,$true)
                    $request = $reader.ReadLine()
                    while (($line=$reader.ReadLine()) -ne $null -and $line -ne "") {}
                    if ($request -notmatch '^GET\s+/([^ ?]+)') {
                        $body=[Text.Encoding]::UTF8.GetBytes("method not allowed")
                        $head=[Text.Encoding]::ASCII.GetBytes("HTTP/1.1 405 Method Not Allowed`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n")
                        $stream.Write($head);$stream.Write($body);continue
                    }
                    $name=[Uri]::UnescapeDataString($Matches[1])
                    if ($name -notin @("imortais-netsparkle-v060-rc.xml","imortais-netsparkle-v060-rc.xml.signature","IMORTAIS-Combat-Client-Setup-v0.6.0.exe","IMORTAIS-Combat-Client-Setup-v0.6.0.exe.sha256")) {
                        $body=[Text.Encoding]::UTF8.GetBytes("not found")
                        $head=[Text.Encoding]::ASCII.GetBytes("HTTP/1.1 404 Not Found`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n")
                        $stream.Write($head);$stream.Write($body);continue
                    }
                    $path=Join-Path $root $name
                    $bytes=[IO.File]::ReadAllBytes($path)
                    $type=if($name.EndsWith(".xml")){"application/rss+xml"}else{"application/octet-stream"}
                    $head=[Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Type: $type`r`nContent-Length: $($bytes.Length)`r`nCache-Control: no-store`r`nConnection: close`r`n`r`n")
                    $stream.Write($head);$stream.Write($bytes)
                } finally { $client.Dispose() }
            }
        } finally { $listener.Stop() }
    }

    Start-Sleep -Milliseconds 500
    if ($serverJob.State -ne "Running") {
        Receive-Job $serverJob -Keep | Out-String | Write-Host
        throw "Servidor localhost não iniciou."
    }
    Write-Host "✅ feed RC servido somente em 127.0.0.1:$Port." -ForegroundColor Green
    Write-Host ""
    Write-Host "Abrindo v0.5.9. No client, use VERIFICAR ATUALIZAÇÃO e aceite v0.6.0." -ForegroundColor Yellow
    Start-Process -FilePath $InstalledExe

    $deadline=(Get-Date).AddMinutes($TimeoutMinutes)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        $v=Get-Version $InstalledExe
        if ($v -like "0.6.0*") {
            $success=$true
            break
        }
    }
    if (-not $success) { throw "Timeout: v0.6.0 não foi instalada em $TimeoutMinutes minutos." }

    Write-Host "✅ instalação atualizada para $(Get-Version $InstalledExe)." -ForegroundColor Green
    Start-Sleep -Seconds 2
    $localStillPresent=$false
    Get-ChildItem -LiteralPath $InstallDir -Filter "*.config" -File | ForEach-Object {
        if ((Get-Content -LiteralPath $_.FullName -Raw) -like "*127.0.0.1:$Port*") { $localStillPresent=$true }
    }
    if ($localStillPresent) { throw "v0.6.0 foi instalada, mas algum config ainda aponta para localhost." }
    Write-Host "✅ v0.6.0 restaurou o feed normal; localhost não ficou persistido." -ForegroundColor Green
    Write-Host ""
    Write-Host "UPDATER RC PASSOU: detecção -> assinatura -> download -> fechamento -> instalação -> relançamento." -ForegroundColor Green
}
finally {
    if ($serverJob) { Stop-Job $serverJob -ErrorAction SilentlyContinue; Remove-Job $serverJob -Force -ErrorAction SilentlyContinue }
    if (-not $success) {
        Restore-Configs
        Write-Host "Configs v0.5.9 restaurados após teste interrompido/falho." -ForegroundColor Yellow
    }
}
