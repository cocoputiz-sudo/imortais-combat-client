param()

$ErrorActionPreference = "Stop"

$Version = "0.5.1"
$Repo = "cocoputiz-sudo/imortais-combat-client"
$RepoUrl = "https://github.com/$Repo.git"
$WorkRoot = Join-Path $env:TEMP "icc-v051"

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

function Write-Utf8Lf {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [Parameter(Mandatory=$true)][AllowEmptyString()][string]$Text
    )
    $normalized = $Text -replace "`r`n", "`n"
    $normalized = $normalized -replace "`r", "`n"
    $utf8 = New-Object System.Text.UTF8Encoding -ArgumentList $false
    [System.IO.File]::WriteAllText($FilePath, $normalized, $utf8)
}

function Write-Ascii {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [Parameter(Mandatory=$true)][AllowEmptyString()][string]$Text
    )
    [System.IO.File]::WriteAllText($FilePath, $Text, [System.Text.Encoding]::ASCII)
}

function Get-SparkleSignature {
    param([Parameter(Mandatory=$true)][string]$FilePath)

    $out = & $SparkleTool --generate-signature $FilePath
    if ($LASTEXITCODE -ne 0) { throw "Falha ao assinar: $FilePath" }

    foreach ($line in $out) {
        if ($line -match '^Signature:\s*(.+)$') {
            return $Matches[1].Trim()
        }
    }

    throw "O NetSparkle não retornou uma assinatura para: $FilePath"
}

function Verify-SparkleSignature {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [Parameter(Mandatory=$true)][string]$Signature
    )

    $out = & $SparkleTool --verify $FilePath --signature $Signature
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao validar assinatura de: $FilePath"
    }

    if (($out -join "`n") -notmatch 'Signature valid') {
        throw "Assinatura inválida para: $FilePath"
    }
}

$git = Need "git.exe"
$gh = Need "gh.exe"
$dotnet = Need "dotnet.exe"

Write-Host "== PUBLICAÇÃO ICC v$Version ==" -ForegroundColor Cyan
Write-Host "Script revision: v051-r4 (fresh entrypoint)" -ForegroundColor DarkGray
Write-Host "Publicação da v0.5.1 com ativação de telemetria restaurada e Guild Presence Collector." -ForegroundColor DarkGray
Write-Host ""

& $gh auth status
if ($LASTEXITCODE -ne 0) { throw "GitHub CLI não autenticado." }
& $gh auth setup-git | Out-Null

foreach ($p in @($PubKeyFile,$PrivKeyFile)) {
    if (-not (Test-Path -LiteralPath $p)) {
        throw "Chave do updater ausente: $p`nNÃO gere uma chave nova. Restaure o par usado desde a v0.4.4."
    }
}

if (-not (Test-Path -LiteralPath $SparkleTool)) {
    New-Item -ItemType Directory -Force -Path $ToolRoot | Out-Null
    Write-Host "Instalando ferramenta NetSparkle..." -ForegroundColor DarkGray
    & $dotnet tool install --tool-path $ToolRoot NetSparkleUpdater.Tools.AppCastGenerator --version 2.9.0
    if ($LASTEXITCODE -ne 0) { throw "Falha ao instalar NetSparkle AppCastGenerator." }
}

$env:SPARKLE_PUBLIC_KEY = (Get-Content -LiteralPath $PubKeyFile -Raw).Trim()
$env:SPARKLE_PRIVATE_KEY = (Get-Content -LiteralPath $PrivKeyFile -Raw).Trim()

if ([string]::IsNullOrWhiteSpace($env:SPARKLE_PUBLIC_KEY)) { throw "Chave pública vazia." }
if ([string]::IsNullOrWhiteSpace($env:SPARKLE_PRIVATE_KEY)) { throw "Chave privada vazia." }

if (Test-Path -LiteralPath $WorkRoot) {
    Remove-Item -LiteralPath $WorkRoot -Recurse -Force
}

Write-Host "Clonando main..." -ForegroundColor Cyan
& $git clone $RepoUrl $WorkRoot
if ($LASTEXITCODE -ne 0) { throw "Falha ao clonar o repositório." }

Set-Location $WorkRoot

$ProjectDir = Join-Path $WorkRoot "upstream\AlbionOnline-StatisticsAnalysis\src\StatisticsAnalysisTool"
$ProjectFile = Join-Path $ProjectDir "StatisticsAnalysisTool.csproj"
$AssemblyFile = Join-Path $ProjectDir "Properties\AssemblyInfo.cs"
$UpdaterFile = Join-Path $ProjectDir "Updater\AutoUpdateController.cs"
$MainWindowFile = Join-Path $ProjectDir "Views\MainWindow.xaml"
$MainWindowCodeFile = Join-Path $ProjectDir "Views\MainWindow.xaml.cs"
$AppConfigFile = Join-Path $ProjectDir "App.config"
$SettingsControlFile = Join-Path $ProjectDir "UserControls\SettingsControl.xaml"
$SettingsControlCodeFile = Join-Path $ProjectDir "UserControls\SettingsControl.xaml.cs"
$GuildPresenceHandlerFile = Join-Path $ProjectDir "Network\Handler\ImortaisGuildPresenceProbeEventHandler.cs"
$NetworkManagerFile = Join-Path $ProjectDir "Network\NetworkManager.cs"
$AppDataPathsFile = Join-Path $ProjectDir "Common\AppDataPaths.cs"
$IconFile = Join-Path $ProjectDir "imortais-icon.ico"
$AppCastFile = Join-Path $ProjectDir "imortais-netsparkle-update-check.xml"
$AppCastSigFile = "$AppCastFile.signature"

$DistDir = Join-Path $WorkRoot "dist\IMORTAIS-Combat-Client"
$ReleaseDir = Join-Path $WorkRoot "release"
$InstallerDir = Join-Path $WorkRoot "installer"
$IssFile = Join-Path $InstallerDir "icc-v051.iss"

foreach ($p in @(
    $ProjectFile,
    $AssemblyFile,
    $UpdaterFile,
    $MainWindowFile,
    $MainWindowCodeFile,
    $AppConfigFile,
    $SettingsControlFile,
    $SettingsControlCodeFile,
    $GuildPresenceHandlerFile,
    $NetworkManagerFile,
    $AppDataPathsFile,
    $IconFile,
    $AppCastFile
)) {
    if (-not (Test-Path -LiteralPath $p)) {
        throw "Arquivo esperado não encontrado: $p"
    }
}

# Pré-validação do código que realmente será publicado.
$projectText = [System.IO.File]::ReadAllText($ProjectFile)
$assemblyText = [System.IO.File]::ReadAllText($AssemblyFile)
$updaterText = [System.IO.File]::ReadAllText($UpdaterFile)
$windowText = [System.IO.File]::ReadAllText($MainWindowFile)
$windowCodeText = [System.IO.File]::ReadAllText($MainWindowCodeFile)
$appConfigText = [System.IO.File]::ReadAllText($AppConfigFile)
$settingsText = [System.IO.File]::ReadAllText($SettingsControlFile)
$settingsCodeText = [System.IO.File]::ReadAllText($SettingsControlCodeFile)
$guildPresenceHandlerText = [System.IO.File]::ReadAllText($GuildPresenceHandlerFile)
$networkManagerText = [System.IO.File]::ReadAllText($NetworkManagerFile)
$appPathsText = [System.IO.File]::ReadAllText($AppDataPathsFile)
$BridgeFile = Join-Path $ProjectDir "Imortais\ImortaisEventBridge.cs"
$ConfigFile = Join-Path $ProjectDir "Imortais\ImortaisTelemetryConfig.cs"
$LootControllerFile = Join-Path $ProjectDir "Network\Manager\LootController.cs"
$bridgeText = [System.IO.File]::ReadAllText($BridgeFile)
$configText = [System.IO.File]::ReadAllText($ConfigFile)
$lootControllerText = [System.IO.File]::ReadAllText($LootControllerFile)

if ($projectText -notmatch '<ApplicationVersion>\s*0\.5\.1\.0\s*</ApplicationVersion>') {
    throw "csproj não está em v0.5.1."
}
if ($assemblyText -notmatch 'AssemblyFileVersion\s*\(\s*"0\.5\.1\.0"\s*\)') {
    throw "AssemblyFileVersion não está em v0.5.1."
}
if ($assemblyText -notmatch 'AssemblyInformationalVersion\s*\(\s*"0\.5\.1"\s*\)') {
    throw "AssemblyInformationalVersion não está em v0.5.1."
}
if ($updaterText -match 'if \(!await IsAppCastSignatureTrustedAsync') {
    throw "O main ainda contém o bloqueio frágil de assinatura do feed."
}
if ($updaterText -notmatch 'IsUpdateItemSignatureTrusted') {
    throw "Validação da assinatura do instalador não encontrada."
}
if ($updaterText -notmatch 'ShowStartupUpdatePromptIfAvailableAsync') {
    throw "A janela automática desacoplada do startup não está presente."
}
if ($updaterText -match 'UpdateCheckSource\.Manual or UpdateCheckSource\.Startup') {
    throw "A lógica antiga de abertura direta da janela no startup ainda está presente."
}
if ($updaterText -notmatch 'DownloadProgressPercentage') {
    throw "GUI de progresso do updater não encontrada."
}
if ($updaterText -notmatch 'RelaunchAfterUpdate\s*=\s*true') {
    throw "Configuração de relaunch automático não está habilitada."
}
if ($windowText -notmatch '<TabItem Header="IMORTAIS"') {
    throw "Home IMORTAIS não encontrada."
}
if ($windowText -notmatch 'ImortaisHomeWarRoomStatusText') {
    throw "Status central do War Room não encontrado na Home IMORTAIS."
}
if ($windowText -notmatch 'ImortaisDiagnosticsOutboxText') {
    throw "Central de Diagnóstico não encontrada na Home IMORTAIS."
}
if ($windowText -notmatch 'ImortaisCheckForUpdateButton') {
    throw "Botão de atualização da Home IMORTAIS não encontrado."
}
if ($windowCodeText -notmatch 'CopyImortaisDiagnostics_Click') {
    throw "Ação Copiar Diagnóstico não encontrada."
}
if ($windowCodeText -notmatch 'LastUpdateCheckStatus') {
    throw "Home IMORTAIS não está lendo o estado do updater."
}
if ($settingsText -notmatch 'UpdateCheckStatusText') {
    throw "Status visual do updater não encontrado em Configurações."
}
if ($settingsText -notmatch 'UpdateCheckProgressBar') {
    throw "Barra de progresso da checagem não encontrada em Configurações."
}
if ($settingsCodeText -notmatch 'RefreshUpdateCheckStatus') {
    throw "Configurações não atualiza o status do updater."
}
if ($updaterText -notmatch 'LastUpdateCheckStatus') {
    throw "AutoUpdateController não expõe diagnóstico da última checagem."
}
$expectedUpdateFeed = 'https://raw.githubusercontent.com/cocoputiz-sudo/imortais-combat-client/main/upstream/AlbionOnline-StatisticsAnalysis/src/StatisticsAnalysisTool/imortais-netsparkle-update-check.xml'
if ([regex]::Matches($appConfigText, [regex]::Escape($expectedUpdateFeed)).Count -lt 4) {
    throw "App.config não aponta todas as configurações de update para o feed IMORTAIS."
}
if ($appConfigText -match 'Triky313/AlbionOnline-StatisticsAnalysis/main/src/StatisticsAnalysisTool/ao-netsparkle') {
    throw "App.config ainda aponta para o feed upstream do Triky313."
}
if ($bridgeText -notmatch 'PendingEvents') {
    throw "BridgeStatus não expõe contagem da outbox."
}
if ($bridgeText -notmatch 'GuildPresenceProbe') {
    throw "Guild Presence Collector não encontrado no bridge."
}
if ($bridgeText -notmatch 'guild_presence_probe') {
    throw "Tipo de telemetria guild_presence_probe não encontrado."
}
if ($guildPresenceHandlerText -notmatch 'ImortaisGuildPresenceProbeEventHandler') {
    throw "Handler de Guild Presence não encontrado."
}
if ($networkManagerText -notmatch 'EventCodes\.GuildUpdate' -or
    $networkManagerText -notmatch 'EventCodes\.GuildPlayerUpdated' -or
    $networkManagerText -notmatch 'EventCodes\.GuildMemberWorldUpdate' -or
    $networkManagerText -notmatch 'EventCodes\.GuildMemberTerritoryUpdate') {
    throw "Eventos do Guild Presence Collector não estão todos registrados."
}
if ($updaterText -notmatch 'ShouldKillParentProcessWhenStartingInstaller\s*=\s*true') {
    throw "Updater não garante explicitamente o fechamento do processo antes do instalador."
}
if ($windowText.IndexOf('<TabItem Header="IMORTAIS"') -gt $windowText.IndexOf('DashboardTabVisibility')) {
    throw "A Home IMORTAIS não é a primeira tela do client."
}
if ($appPathsText -notmatch 'ExecutableFileName = "IMORTAIS-Combat-Client\.exe"') {
    throw "Fallback do executável ainda aponta para o Statistics Analysis."
}
if ($bridgeText -notmatch 'lootedByGuild') {
    throw "Telemetria canônica de guilda no loot não encontrada."
}
if ($bridgeText -notmatch '/api/telemetry/context') {
    throw "Contexto automático de CTA não encontrado."
}
if ($bridgeText -notmatch 'PersistQueuedEventsAsync') {
    throw "Outbox persistente não encontrada."
}
if ($configText -notmatch 'outbox\.ndjson') {
    throw "Caminho da outbox persistente não encontrado."
}
if ($lootControllerText -notmatch 'lootedByUser\?\.Value\?\.Guild') {
    throw "LootController não está encaminhando a guilda do looter."
}
if ($bridgeText -notmatch 'version = "0.5.1"') {
    throw "Telemetria ainda não anuncia v0.5.1."
}
$UpdaterFile = Join-Path $ProjectDir "Updater\AutoUpdateController.cs"
$updaterText = [System.IO.File]::ReadAllText($UpdaterFile)
if ($updaterText -match 'HttpClientUtils\.IsUrlAccessible') {
    throw "Updater ainda contém o pré-teste HEAD que pode bloquear a checagem."
}
if ($updaterText -notmatch 'ShowStartupUpdatePromptIfAvailableAsync') {
    throw "Updater não separa mais detecção e janela de startup."
}
if ($updaterText -match 'UpdateCheckSource\.Manual or UpdateCheckSource\.Startup') {
    throw "Janela de startup voltou a ficar acoplada à detecção."
}
if (Test-Path (Join-Path $WorkRoot 'src\StatisticsAnalysisTool\Imortais\*.cs')) {
    throw "Cópia viva antiga de ImortaisEventBridge voltou ao repositório."
}
if (Test-Path (Join-Path $WorkRoot 'patched\Network\Manager\*.cs')) {
    throw "Controllers patched antigos voltaram ao caminho ativo."
}

Write-Host "Pré-validação OK." -ForegroundColor Green

& $dotnet build-server shutdown | Out-Null

Remove-Item -LiteralPath $DistDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $ReleaseDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null
New-Item -ItemType Directory -Force -Path $InstallerDir | Out-Null

Write-Host "Limpando build..." -ForegroundColor DarkGray
& $dotnet clean $ProjectFile -c Release | Out-Null

Write-Host "Compilando v$Version..." -ForegroundColor Cyan
& $dotnet build $ProjectFile `
    -c Release `
    --no-incremental `
    -p:RequireNetSparkleEd25519PublicKey=true `
    "-p:NetSparkleEd25519PublicKey=$($env:SPARKLE_PUBLIC_KEY)"
if ($LASTEXITCODE -ne 0) { throw "Build falhou." }

Write-Host "Publicando win-x64 self-contained..." -ForegroundColor Cyan
& $dotnet publish $ProjectFile `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:RequireNetSparkleEd25519PublicKey=true `
    "-p:NetSparkleEd25519PublicKey=$($env:SPARKLE_PUBLIC_KEY)" `
    -o $DistDir
if ($LASTEXITCODE -ne 0) { throw "Publish falhou." }

$ExeFile = Join-Path $DistDir "IMORTAIS-Combat-Client.exe"
if (-not (Test-Path -LiteralPath $ExeFile)) {
    throw "Executável final não encontrado: $ExeFile"
}

$exeInfo = Get-Item -LiteralPath $ExeFile
Write-Host ("Executável: " + $exeInfo.VersionInfo.FileVersion + " / " + $exeInfo.VersionInfo.ProductVersion) -ForegroundColor Green

if ($exeInfo.VersionInfo.FileVersion -notlike "0.5.1*") {
    throw "Executável gerado não tem FileVersion 0.5.1."
}

function Find-Iscc {
    $cmd = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )

    return ($candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1)
}

$iscc = Find-Iscc
if (-not $iscc) { throw "Inno Setup 6 não encontrado." }

$distEsc = $DistDir.Replace('\','\\')
$releaseEsc = $ReleaseDir.Replace('\','\\')
$iconEsc = $IconFile.Replace('\','\\')

$iss = @"
#define AppName "IMORTAIS Combat Client"
#define AppVersion "$Version"
#define AppExe "IMORTAIS-Combat-Client.exe"

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
OutputDir=$releaseEsc
OutputBaseFilename=IMORTAIS-Combat-Client-Setup-v$Version
SetupIconFile=$iconEsc
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=IMORTAIS Combat Client
AppMutex=IMORTAISCombatClient_D842B07173EA42BFB36E3FD6C2F1A940
; NetSparkle already starts the installer from a helper script that waits for
; the Combat Client process to terminate. Letting Inno invoke Restart Manager here
; can stall indefinitely at "Closing applications..." during a silent update.
CloseApplications=no
RestartApplications=no

[Files]
Source: "$distEsc\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\IMORTAIS Combat Client"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\IMORTAIS Combat Client"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[Run]
Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Flags: nowait skipifsilent
"@

Write-Utf8Lf -FilePath $IssFile -Text $iss

Write-Host "Gerando instalador..." -ForegroundColor Cyan
& $iscc $IssFile
if ($LASTEXITCODE -ne 0) { throw "Inno Setup falhou." }

$SetupFile = Join-Path $ReleaseDir "IMORTAIS-Combat-Client-Setup-v$Version.exe"
if (-not (Test-Path -LiteralPath $SetupFile)) {
    throw "Setup não encontrado: $SetupFile"
}

Write-Host "Assinando instalador com Ed25519..." -ForegroundColor Cyan
$installerSig = Get-SparkleSignature -FilePath $SetupFile
Verify-SparkleSignature -FilePath $SetupFile -Signature $installerSig

$HashFile = "$SetupFile.sha256"
$hash = (Get-FileHash -LiteralPath $SetupFile -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Ascii -FilePath $HashFile -Text $hash

$ZipFile = Join-Path $ReleaseDir "IMORTAIS-Combat-Client-v$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $DistDir "*") -DestinationPath $ZipFile -CompressionLevel Optimal

$releaseJson = & $gh release list --repo $Repo --limit 100 --json tagName
if ($LASTEXITCODE -ne 0) { throw "Falha ao consultar releases." }
$releaseList = @($releaseJson | ConvertFrom-Json)
$releaseExists = @($releaseList | Where-Object { $_.tagName -eq "v$Version" }).Count -gt 0

$notes = @"
### Ativação da telemetria
- Restaura na Home IMORTAIS o campo para código de ativação de 6 dígitos.
- Permite parear uma instalação nova diretamente com o War Room.
- Mantém a credencial existente em atualizações: quem já está ativado não precisa parear novamente.

### Guild Presence Collector
- Observa passivamente os eventos Photon `GuildUpdate`, `GuildPlayerUpdated`, `GuildMemberWorldUpdate` e `GuildMemberTerritoryUpdate`.
- Envia probes limitados e deduplicados ao War Room como `guild_presence_probe`.
- Nesta versão os campos permanecem opacos: o cliente NÃO classifica jogadores como online/offline ainda.
- O objetivo é mapear o formato real dos eventos em tráfego ao vivo antes de construir presença e last seen.

### Updater consolidado
- Mantém o feed oficial IMORTAIS no runtime.
- Mantém assinatura Ed25519 do instalador.
- Mantém fechamento do cliente pelo NetSparkle antes do handoff.
- Mantém instalador silencioso sem deadlock do Restart Manager.
- Mantém relançamento automático após a instalação.
- Mantém mutex nomeado para detecção confiável da instância.

### Diagnóstico
- Mantém Central de Diagnóstico, outbox, último contato, último erro e verificação manual de atualização.

### Teste desta versão
A v0.5.1 deve ser recebida automaticamente pelas versões anteriores. Em instalação nova, gere o código no War Room e use a seção ATIVAÇÃO DA TELEMETRIA na Home IMORTAIS.
"@
if ($releaseExists) {
    Write-Host "Release v$Version já existe. Atualizando assets..." -ForegroundColor Yellow
    & $gh release upload "v$Version" $SetupFile $HashFile $ZipFile --repo $Repo --clobber
    if ($LASTEXITCODE -ne 0) { throw "Falha ao atualizar assets." }
} else {
    Write-Host "Criando GitHub Release v$Version..." -ForegroundColor Cyan
    & $gh release create "v$Version" `
        $SetupFile `
        $HashFile `
        $ZipFile `
        --repo $Repo `
        --target main `
        --title "IMORTAIS Combat Client v$Version" `
        --notes $notes
    if ($LASTEXITCODE -ne 0) { throw "Falha ao criar release." }
}

$setupLength = (Get-Item -LiteralPath $SetupFile).Length
$pubDate = [DateTimeOffset]::UtcNow.ToString("r")
$releaseUrl = "https://github.com/$Repo/releases/tag/v$Version"
$downloadUrl = "https://github.com/$Repo/releases/download/v$Version/IMORTAIS-Combat-Client-Setup-v$Version.exe"

$appCastText = @"
<?xml version="1.0" encoding="utf-8"?>
<rss xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle" version="2.0">
  <channel>
    <title>IMORTAIS Combat Client</title>
    <description>Atualizações oficiais do IMORTAIS Combat Client.</description>
    <language>pt-BR</language>
    <item>
      <title>IMORTAIS Combat Client v$Version</title>
      <description><![CDATA[<a href="$releaseUrl">Ver notas completas da versão no GitHub</a>]]></description>
      <pubDate>$pubDate</pubDate>
      <enclosure url="$downloadUrl" sparkle:version="$Version" sparkle:shortVersionString="$Version" sparkle:os="windows-x64" length="$setupLength" type="application/octet-stream" sparkle:edSignature="$installerSig" sparkle:signature="$installerSig" />
    </item>
  </channel>
</rss>
"@

# Mantemos o appcast canonicalizado em LF. A v0.5.1 usa a assinatura
# Ed25519 do instalador para a decisão de confiança; a assinatura separada do feed
# continua correta apenas para consistência do repositório.
Write-Utf8Lf -FilePath $AppCastFile -Text $appCastText
$appCastSig = Get-SparkleSignature -FilePath $AppCastFile
Verify-SparkleSignature -FilePath $AppCastFile -Signature $appCastSig
Write-Utf8Lf -FilePath $AppCastSigFile -Text $appCastSig

$ReadmeFile = Join-Path $WorkRoot "README.md"
if (Test-Path -LiteralPath $ReadmeFile) {
    $readmeText = [System.IO.File]::ReadAllText($ReadmeFile)
    $readmeText = $readmeText.Replace("> **Versão publicada atual:** v0.4.9", "> **Versão publicada atual:** v0.5.1")
    Write-Utf8Lf -FilePath $ReadmeFile -Text $readmeText
}

& $git add `
    "upstream/AlbionOnline-StatisticsAnalysis/src/StatisticsAnalysisTool/imortais-netsparkle-update-check.xml" `
    "upstream/AlbionOnline-StatisticsAnalysis/src/StatisticsAnalysisTool/imortais-netsparkle-update-check.xml.signature" `
    "README.md"

$changes = & $git status --porcelain
if ($changes) {
    & $git commit -m "release: publish updater feed for v$Version"
    if ($LASTEXITCODE -ne 0) { throw "Falha ao criar commit do feed." }

    & $git push origin main
    if ($LASTEXITCODE -ne 0) { throw "Release criada, mas falhou o push do feed." }
}

Write-Host ""
Write-Host "==================================================" -ForegroundColor DarkGray
Write-Host "ICC v$Version PUBLICADO COM SUCESSO" -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor DarkGray
Write-Host ""
Write-Host "TESTE CONTROLADO DO UPDATER v0.4.9 -> v0.5.1" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. NÃO instale a v0.5.1 manualmente na máquina de teste." -ForegroundColor Cyan
Write-Host "2. Confirme que o Combat Client instalado está em v0.4.9." -ForegroundColor Cyan
Write-Host "3. Abra a v0.4.9 e use Verificar atualização." -ForegroundColor Cyan
Write-Host "4. Confirme que a janela oferece a v0.5.1." -ForegroundColor Cyan
Write-Host "5. Atualize e confirme download, fechamento, instalação e relançamento." -ForegroundColor Cyan
Write-Host "6. Após reabrir, confirme v0.5.1 e deixe o Albion aberto para coletar Guild Presence probes." -ForegroundColor Cyan
Write-Host ""
Write-Host $releaseUrl -ForegroundColor DarkGray
