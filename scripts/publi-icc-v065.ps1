$ErrorActionPreference = "Stop"

$Version = "0.6.5"
$Repo = "cocoputiz-sudo/imortais-combat-client"
$RepoUrl = "https://github.com/$Repo.git"
$WorkRoot = Join-Path $env:TEMP "icc-v065"

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
Write-Host "Script revision: v065-r1 (atualizador corrigido, mesma assinatura Ed25519)" -ForegroundColor DarkGray
Write-Host "Corrige a passagem do atualizador NetSparkle para o instalador e o encerramento do cliente." -ForegroundColor DarkGray
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


# Publicação só a partir de main e com o PR de escopo fechado integrado.
if(Test-Path -LiteralPath $WorkRoot){Remove-Item -LiteralPath $WorkRoot -Recurse -Force}
& $git clone --single-branch --branch main $RepoUrl $WorkRoot
if($LASTEXITCODE -ne 0){throw "Falha ao clonar main do repositório."}
Push-Location $WorkRoot
try{
    $pr = & $gh pr view 36 --repo $Repo --json mergedAt,mergeCommit | ConvertFrom-Json
    if($LASTEXITCODE -ne 0 -or -not $pr.mergedAt){throw "PR #36 ainda não foi mesclado. NÃO publicar."}
    # Recusar publicacao sem a remocao especifica que realinha o enum Photon.
    $enumPath=Join-Path $WorkRoot "upstream\AlbionOnline-StatisticsAnalysis\src\StatisticsAnalysisTool\Network\EventCodes.cs"
    $photonDiagnosticsPath=Join-Path $WorkRoot "upstream\AlbionOnline-StatisticsAnalysis\src\StatisticsAnalysisTool\Imortais\ImortaisPhotonEventDiagnostics.cs"
    $ordinalTestsPath=Join-Path $WorkRoot "upstream\AlbionOnline-StatisticsAnalysis\src\StatisticsAnalysisTool.UnitTests\Network\Events\EventCodesOrdinalRegressionTests.cs"
    $enumText=[IO.File]::ReadAllText($enumPath)
    if($enumText -match 'InvitedMercenaryToMatch'){throw "Enum ainda tem InvitedMercenaryToMatch; nao publicar"}
    foreach($required in @($photonDiagnosticsPath,$ordinalTestsPath)){
        if(-not (Test-Path -LiteralPath $required)){throw "Diagnostico/regressao Photon ausente: $required"}
    }
    $ProjectDir=Join-Path $WorkRoot "upstream\AlbionOnline-StatisticsAnalysis\src\StatisticsAnalysisTool"
    $ProjectFile=Join-Path $ProjectDir "StatisticsAnalysisTool.csproj"
    $AssemblyFile=Join-Path $ProjectDir "Properties\AssemblyInfo.cs"
    $BridgeFile=Join-Path $ProjectDir "Imortais\ImortaisEventBridge.cs"
    $SettingsFile=Join-Path $ProjectDir "Common\UserSettings\SettingsObject.cs"
    $StorageFile=Join-Path $ProjectDir "Imortais\Highlights\HighlightStorageManager.cs"
    $WindowFile=Join-Path $ProjectDir "Views\MainWindow.xaml"
    $WindowCodeFile=Join-Path $ProjectDir "Views\MainWindow.xaml.cs"
    $AppConfigFile=Join-Path $ProjectDir "App.config"
    $BuildIdentityFile=Join-Path $ProjectDir "Imortais\ImortaisBuildIdentity.cs"
    $AppCastFile=Join-Path $ProjectDir "imortais-netsparkle-update-check.xml"
    $AppCastSigFile="$AppCastFile.signature"
    $ReadmeFile=Join-Path $WorkRoot "README.md"
    $DistDir=Join-Path $WorkRoot "dist\IMORTAIS-Combat-Client"
    $ReleaseDir=Join-Path $WorkRoot "release"
    $InstallerDir=Join-Path $WorkRoot "installer"
    $IconFile=Join-Path $ProjectDir "imortais-icon.ico"
    $IssFile=Join-Path $InstallerDir "icc-v065.iss"
    foreach($p in @($ProjectFile,$AssemblyFile,$BridgeFile,$SettingsFile,$StorageFile,$WindowFile,$WindowCodeFile,$AppConfigFile,$BuildIdentityFile,$AppCastFile,$IconFile)){
        if(-not (Test-Path -LiteralPath $p)){throw "Arquivo obrigatório ausente: $p"}
    }
    $project=[IO.File]::ReadAllText($ProjectFile)
    $assembly=[IO.File]::ReadAllText($AssemblyFile)
    $bridge=[IO.File]::ReadAllText($BridgeFile)
    $settings=[IO.File]::ReadAllText($SettingsFile)
    $storage=[IO.File]::ReadAllText($StorageFile)
    $window=[IO.File]::ReadAllText($WindowFile)
    $windowCode=[IO.File]::ReadAllText($WindowCodeFile)
    $appConfig=[IO.File]::ReadAllText($AppConfigFile)
    $feed=[IO.File]::ReadAllText($AppCastFile)
    if($project -notmatch '<ApplicationVersion>0\.6\.5\.0</ApplicationVersion>'){throw "Projeto não está na versão v0.6.5"}
    if($assembly -notmatch 'AssemblyFileVersion\("0\.6\.5\.0"\)' -or $assembly -notmatch 'AssemblyInformationalVersion\("0\.6\.5"\)'){throw "Assembly não é v0.6.5"}
    if($bridge -notmatch 'ClientVersion\s*=\s*"0\.6\.5"'){throw "Telemetria não está em v0.6.5"}
    if($settings -notmatch 'IsImortaisAutoSaveDeathsEnabled\s*\{\s*get;\s*set;\s*\}\s*=\s*false' -or
        $settings -notmatch 'IsImortaisAutoSaveMassAbatesEnabled\s*\{\s*get;\s*set;\s*\}\s*=\s*false'){throw "Morte e massa devem estar desmarcadas por padrão"}
    if($settings -notmatch 'IsImortaisAutoSaveAbatesEnabled\s*\{\s*get;\s*set;\s*\}\s*=\s*true'){throw "Comportamento anterior Salvar meus abates foi alterado"}
    if($storage -notmatch 'CalculateUsedBytes\(' -or $storage -notmatch 'SearchOption.AllDirectories'){throw "Quota recursiva de highlights ausente"}
    if($window -notmatch 'ImortaisAutoSaveMassAbatesCheckBox' -or $window -notmatch 'ImortaisAutoSaveDeathsCheckBox'){throw "Checkbox de morte/massa não encontrado"}
    if($bridge -notmatch 'credentialMode' -or $bridge -notmatch 'rankingEligible'){throw "Correção de pareamento do PR #35 ausente"}
    if($bridge -notmatch 'GetGuildChallengePoints' -or $bridge -notmatch 'GetGvgSeasonContributionByActivity'){throw "Challenge/temporada ausentes"}
    $stableFeedUrl='https://raw.githubusercontent.com/cocoputiz-sudo/imortais-combat-client/main/upstream/AlbionOnline-StatisticsAnalysis/src/StatisticsAnalysisTool/imortais-netsparkle-update-check.xml'
    if(([regex]::Matches($appConfig,[regex]::Escape($stableFeedUrl))).Count -lt 4){throw "App.config não aponta para o feed IMORTAIS"}
    if($feed -notmatch 'sparkle:version="0\.6\.4"'){throw "Feed atual não é v0.6.4. Recusando sobrescrever outra publicação."}
    $listed=& $gh release list --repo $Repo --limit 100 --json tagName
    if($LASTEXITCODE -ne 0){throw "Não foi possível consultar releases do GitHub"}
    if(@($listed|ConvertFrom-Json|Where-Object {$_.tagName -eq "v$Version"}).Count -gt 0){throw "Release v$Version já existe. Não sobrescrever."}
    $buildSha=(& $git rev-parse HEAD).Trim().ToLowerInvariant()
    if($LASTEXITCODE -ne 0 -or $buildSha -notmatch '^[0-9a-f]{40}$'){throw "HEAD inválido"}
    $identity=[IO.File]::ReadAllText($BuildIdentityFile)
    if($identity -notmatch 'LOCAL-UNSTAMPED'){throw "Identidade de build inesperada"}
    $identity=$identity.Replace("LOCAL-UNSTAMPED",$buildSha)
    Write-Utf8Lf -FilePath $BuildIdentityFile -Text $identity
    Write-Host "Pré-validação v0.6.5 OK · commit $buildSha" -ForegroundColor Green
    Write-Host "Rodando testes de highlights e apresentação do pareamento..." -ForegroundColor Cyan
    & $dotnet run --project (Join-Path $WorkRoot "tests\Highlights.LogicTests\Highlights.LogicTests.csproj") -c Release
    if($LASTEXITCODE -ne 0){throw "Testes da v0.6.5 falharam"}
    $nunit=Join-Path $WorkRoot "upstream\AlbionOnline-StatisticsAnalysis\src\StatisticsAnalysisTool.UnitTests\StatisticsAnalysisTool.UnitTests.csproj"
    & $dotnet test $nunit -c Release --filter "FullyQualifiedName~EventCodesOrdinalRegressionTests|FullyQualifiedName~PartyJoinedEventTests|FullyQualifiedName~PhotonEventDiagnosticsCollectorTests"
    if($LASTEXITCODE -ne 0){throw "Testes Photon da v0.6.5 falharam"}
    & $dotnet build-server shutdown | Out-Null
    Remove-Item -LiteralPath $DistDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $ReleaseDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $DistDir,$ReleaseDir,$InstallerDir | Out-Null
    Write-Host "Compilando release com a chave pública Ed25519 real..." -ForegroundColor Cyan
    & $dotnet publish $ProjectFile -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:LocalInstallerBuild=false -p:RequireNetSparkleEd25519PublicKey=true "-p:NetSparkleEd25519PublicKey=$($env:SPARKLE_PUBLIC_KEY)" -o $DistDir
    if($LASTEXITCODE -ne 0){throw "dotnet publish falhou"}
    $ExeFile=Join-Path $DistDir "IMORTAIS-Combat-Client.exe"
    if(-not (Test-Path -LiteralPath $ExeFile)){throw "Executável não gerado"}
    $fileVersion=(Get-Item $ExeFile).VersionInfo.FileVersion
    if($fileVersion -notlike "0.6.5*"){throw "Executável possui FileVersion inesperada: $fileVersion"}
    Write-Host "Executável: $fileVersion · $buildSha" -ForegroundColor Green

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


    Write-Host "Assinando instalador com a chave Ed25519 existente..." -ForegroundColor Cyan
    $installerSig=Get-SparkleSignature -FilePath $SetupFile
    Verify-SparkleSignature -FilePath $SetupFile -Signature $installerSig
    $HashFile="$SetupFile.sha256"
    $hash=(Get-FileHash -LiteralPath $SetupFile -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Ascii -FilePath $HashFile -Text "$hash  IMORTAIS-Combat-Client-Setup-v$Version.exe"
    $ZipFile=Join-Path $ReleaseDir "IMORTAIS-Combat-Client-v$Version-win-x64.zip"
    Compress-Archive -Path (Join-Path $DistDir "*") -DestinationPath $ZipFile -CompressionLevel Optimal
    $notesPath = Join-Path $WorkRoot "docs\releases\v0.6.5-updater.md"
    if (-not (Test-Path -LiteralPath $notesPath)) { throw "Notas da v0.6.5 ausentes" }
    $notes = Get-Content -LiteralPath $notesPath -Raw -Encoding UTF8
    if ($notes -notmatch 'DEATH REVIEW PRÉ-HABILITADO') { throw "Aviso do piloto core deve constar nas notas" }

    $releaseUrl="https://github.com/$Repo/releases/tag/v$Version"
    Write-Host "Publicando GitHub Release v$Version..." -ForegroundColor Cyan
    # Windows PowerShell 5.1 desmembra o texto multilinha de --notes em argumentos.
    # --notes-file envia um unico caminho; gh le o Markdown em UTF-8 sem globbing.
    $notesFile=Join-Path $ReleaseDir "IMORTAIS-Combat-Client-v$Version-release-notes.md"
    Write-Utf8Lf -FilePath $notesFile -Text $notes
    & $gh release create "v$Version" $SetupFile $HashFile $ZipFile --repo $Repo --target $buildSha --title "IMORTAIS Combat Client v$Version" --notes-file $notesFile
    if($LASTEXITCODE -ne 0){throw "Falha ao criar GitHub Release"}
    $downloadUrl="https://github.com/$Repo/releases/download/v$Version/IMORTAIS-Combat-Client-Setup-v$Version.exe"
    $setupLength=(Get-Item -LiteralPath $SetupFile).Length
    $pubDate=[DateTimeOffset]::UtcNow.ToString("r")
    $appCastText=@"
<?xml version="1.0" encoding="utf-8"?>
<rss xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle" version="2.0">
  <channel>
    <title>IMORTAIS Combat Client</title>
    <description>Atualizações oficiais do IMORTAIS Combat Client.</description>
    <language>pt-BR</language>
    <item>
      <title>IMORTAIS Combat Client v$Version</title>
      <description><![CDATA[<a href="$releaseUrl">Ver notas da versão no GitHub</a>]]></description>
      <pubDate>$pubDate</pubDate>
      <enclosure url="$downloadUrl" sparkle:version="$Version" sparkle:shortVersionString="$Version" sparkle:os="windows-x64" length="$setupLength" type="application/octet-stream" sparkle:edSignature="$installerSig" sparkle:signature="$installerSig" />
    </item>
  </channel>
</rss>
"@
    Write-Utf8Lf -FilePath $AppCastFile -Text $appCastText
    $feedSig=Get-SparkleSignature -FilePath $AppCastFile
    Verify-SparkleSignature -FilePath $AppCastFile -Signature $feedSig
    Write-Utf8Lf -FilePath $AppCastSigFile -Text $feedSig
    if(Test-Path -LiteralPath $ReadmeFile){
        $readmeText=[IO.File]::ReadAllText($ReadmeFile)
        $readmeText=$readmeText.Replace("**Versão estável atual:** v0.6.4","**Versão estável atual:** v0.6.5")
        $readmeText=$readmeText.Replace("IMORTAIS-Combat-Client-Setup-v0.6.4.exe","IMORTAIS-Combat-Client-Setup-v0.6.5.exe")
        Write-Utf8Lf -FilePath $ReadmeFile -Text $readmeText
    }
    & $git add "upstream/AlbionOnline-StatisticsAnalysis/src/StatisticsAnalysisTool/Imortais/ImortaisBuildIdentity.cs" "upstream/AlbionOnline-StatisticsAnalysis/src/StatisticsAnalysisTool/imortais-netsparkle-update-check.xml" "upstream/AlbionOnline-StatisticsAnalysis/src/StatisticsAnalysisTool/imortais-netsparkle-update-check.xml.signature" "README.md"
    if($LASTEXITCODE -ne 0){throw "Falha ao selecionar atualização do feed"}
    & $git commit -m "release: publish signed IMORTAIS Combat Client v0.6.5 feed"
    if($LASTEXITCODE -ne 0){throw "Falha no commit do feed"}
    & $git push origin main
    if($LASTEXITCODE -ne 0){throw "Release criada, mas o feed não foi enviado. NÃO anuncie a atualização até corrigir o push."}
    Write-Host ""
    Write-Host "======================================================" -ForegroundColor Green
    Write-Host "ICC v0.6.5 PUBLICADO COM SUCESSO" -ForegroundColor Green
    Write-Host "======================================================" -ForegroundColor Green
    Write-Host "Commit compilado: $buildSha" -ForegroundColor Green
    Write-Host "SHA256: $hash" -ForegroundColor Green
    Write-Host "Release: $releaseUrl" -ForegroundColor Green
    Write-Host "Feed: $stableFeedUrl" -ForegroundColor Green
    Write-Host "Para clientes v0.6.4 que falham no atualizador, compartilhe o instalador .exe v0.6.5 de GitHub Releases. Sem PowerShell nos clientes." -ForegroundColor Cyan
    Write-Host "Confirme v0.6.5, instalação e preservação do pareamento." -ForegroundColor Cyan
    Write-Host "Confira no cliente v0.6.5 o handoff do NetSparkle nas futuras atualizacoes. O envio de JSON continua LOCAL." -ForegroundColor Cyan
}
finally{
    Pop-Location
    Remove-Item Env:SPARKLE_PRIVATE_KEY -ErrorAction SilentlyContinue
}
