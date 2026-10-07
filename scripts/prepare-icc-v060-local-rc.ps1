param(
    [int]$Port = 48159
)

$ErrorActionPreference = "Stop"
$Version = "0.6.0"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$AppZip = Join-Path $Root "IMORTAIS-Combat-Client-v0.6.0-rc-win-x64.zip"
$Work = Join-Path $Root "_v060-local-rc"
$AppDir = Join-Path $Work "app"
$OutDir = Join-Path $Root "IMORTAIS-v060-local-update-test"
$SigningRoot = Join-Path $env:LOCALAPPDATA "IMORTAIS Combat Client\signing-keys"
$ToolRoot = Join-Path $env:LOCALAPPDATA "IMORTAIS Combat Client\tools\netsparkle"
$PubKeyFile = Join-Path $SigningRoot "NetSparkle_Ed25519.pub"
$PrivKeyFile = Join-Path $SigningRoot "NetSparkle_Ed25519.priv"
$SparkleTool = Join-Path $ToolRoot "netsparkle-generate-appcast.exe"

function Need([string]$Name) {
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if (-not $cmd) { throw "Comando não encontrado: $Name" }
    return $cmd.Source
}
function Find-Iscc {
    $cmd = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
function Write-Utf8Lf([string]$FilePath,[string]$Text) {
    $Text = $Text -replace "`r`n","`n" -replace "`r","`n"
    [IO.File]::WriteAllText($FilePath,$Text,(New-Object Text.UTF8Encoding($false)))
}
function Sign-File([string]$Path) {
    $out = & $SparkleTool --generate-signature $Path
    if ($LASTEXITCODE -ne 0) { throw "Falha ao assinar: $Path" }
    foreach ($line in $out) { if ($line -match '^Signature:\s*(.+)$') { return $Matches[1].Trim() } }
    throw "NetSparkle não retornou assinatura para $Path"
}
function Verify-File([string]$Path,[string]$Signature) {
    $out = & $SparkleTool --verify $Path --signature $Signature
    if ($LASTEXITCODE -ne 0 -or ($out -join "`n") -notmatch 'Signature valid') {
        throw "Assinatura inválida: $Path"
    }
}

if (-not (Test-Path -LiteralPath $AppZip)) { throw "RC ZIP não encontrado ao lado deste script: $AppZip" }
foreach ($p in @($PubKeyFile,$PrivKeyFile)) {
    if (-not (Test-Path -LiteralPath $p)) { throw "Chave do updater ausente: $p. NÃO gere uma chave nova." }
}
$dotnet = Need "dotnet.exe"
$iscc = Find-Iscc
if (-not $iscc) { throw "Inno Setup 6 não encontrado." }
if (-not (Test-Path -LiteralPath $SparkleTool)) {
    New-Item -ItemType Directory -Force -Path $ToolRoot | Out-Null
    & $dotnet tool install --tool-path $ToolRoot NetSparkleUpdater.Tools.AppCastGenerator --version 2.9.0
    if ($LASTEXITCODE -ne 0) { throw "Falha ao instalar ferramenta NetSparkle." }
}
$env:SPARKLE_PUBLIC_KEY = (Get-Content -LiteralPath $PubKeyFile -Raw).Trim()
$env:SPARKLE_PRIVATE_KEY = (Get-Content -LiteralPath $PrivKeyFile -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($env:SPARKLE_PUBLIC_KEY) -or [string]::IsNullOrWhiteSpace($env:SPARKLE_PRIVATE_KEY)) {
    throw "Par de chaves do updater está vazio."
}

Remove-Item -LiteralPath $Work -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $OutDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $AppDir,$OutDir | Out-Null
Expand-Archive -LiteralPath $AppZip -DestinationPath $AppDir -Force

$Exe = Join-Path $AppDir "IMORTAIS-Combat-Client.exe"
if (-not (Test-Path -LiteralPath $Exe)) { throw "Executável RC não encontrado no ZIP." }
$vi = (Get-Item $Exe).VersionInfo
if ($vi.FileVersion -notlike "0.6.0*") { throw "RC ZIP não contém FileVersion 0.6.0: $($vi.FileVersion)" }

$iss = Join-Path $Work "icc-v060-local-rc.iss"
# O publish self-contained não carrega imortais-icon.ico (CopyToOutputDirectory=Never).
# A RC local usa o ícone padrão do Inno; o instalador oficial continua usando o ícone
# do repositório pelo publi-icc-v060.ps1.
$appEsc = $AppDir.Replace('"','""')
$outEsc = $OutDir.Replace('"','""')
$issText = @"
#define AppName "IMORTAIS Combat Client"
#define AppVersion "$Version"
#define AppVersionInfo "0.6.0.0"
#define AppExe "IMORTAIS-Combat-Client.exe"
#define AppSource "$appEsc"
#define InstallerOutput "$outEsc"
[Setup]
AppId={{D842B071-73EA-42BF-B36E-3FD6C2F1A940}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=IMORTAIS
DefaultDirName={localappdata}\Programs\IMORTAIS Combat Client
DefaultGroupName=IMORTAIS Combat Client
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#InstallerOutput}
OutputBaseFilename=IMORTAIS-Combat-Client-Setup-v$Version
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=IMORTAIS Combat Client
VersionInfoVersion={#AppVersionInfo}
VersionInfoProductVersion={#AppVersionInfo}
AppMutex=IMORTAISCombatClient_D842B07173EA42BFB36E3FD6C2F1A940
CloseApplications=no
RestartApplications=no
[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autoprograms}\IMORTAIS Combat Client"; Filename: "{app}\{#AppExe}"
[Run]
Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Flags: nowait skipifsilent
"@
Write-Utf8Lf $iss $issText
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup falhou." }

$Setup = Join-Path $OutDir "IMORTAIS-Combat-Client-Setup-v$Version.exe"
if (-not (Test-Path -LiteralPath $Setup)) { throw "Setup RC não foi gerado." }
$installerSig = Sign-File $Setup
Verify-File $Setup $installerSig
$hash = (Get-FileHash -LiteralPath $Setup -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$Setup.sha256",$hash,[Text.Encoding]::ASCII)

$feedName = "imortais-netsparkle-v060-rc.xml"
$feed = Join-Path $OutDir $feedName
$setupLength = (Get-Item $Setup).Length
$baseUrl = "http://127.0.0.1:$Port"
$pubDate = [DateTimeOffset]::UtcNow.ToString("r")
$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<rss xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle" version="2.0">
  <channel>
    <title>IMORTAIS Combat Client RC LOCAL</title>
    <description>Feed isolado localhost para teste v0.5.9 -> v0.6.0. NÃO PUBLICAR.</description>
    <language>pt-BR</language>
    <item>
      <title>IMORTAIS Combat Client v0.6.0 RC local</title>
      <description><![CDATA[RC local. Highlights experimentais de abates + estabilidade da telemetria.]]></description>
      <pubDate>$pubDate</pubDate>
      <enclosure url="$baseUrl/IMORTAIS-Combat-Client-Setup-v$Version.exe" sparkle:version="$Version" sparkle:shortVersionString="$Version" sparkle:os="windows-x64" length="$setupLength" type="application/octet-stream" sparkle:edSignature="$installerSig" sparkle:signature="$installerSig" />
    </item>
  </channel>
</rss>
"@
Write-Utf8Lf $feed $xml
$feedSig = Sign-File $feed
Verify-File $feed $feedSig
Write-Utf8Lf "$feed.signature" $feedSig

Copy-Item -LiteralPath (Join-Path $Root "test-icc-v060-local-update.ps1") -Destination $OutDir
Copy-Item -LiteralPath (Join-Path $Root "RC-v0.6.0.md") -Destination $OutDir
Write-Host ""
Write-Host "RC LOCAL PREPARADA E ASSINADA" -ForegroundColor Green
Write-Host "Pasta: $OutDir" -ForegroundColor Cyan
Write-Host "Feed:  $baseUrl/$feedName" -ForegroundColor Cyan
Write-Host "SHA-256 setup: $hash" -ForegroundColor DarkGray
Write-Host ""
Write-Host "Próximo passo: execute test-icc-v060-local-update.ps1 dentro dessa pasta." -ForegroundColor Yellow
