param()
# Recuperacao da v0.7.0: reutiliza o instalador ja compilado e assinado.
$ErrorActionPreference="Stop"
$Version="0.7.0"
$Repo="cocoputiz-sudo/imortais-combat-client"
$BuildRoot=Join-Path $env:TEMP "icc-v070"
$ReleaseDir=Join-Path $BuildRoot "release"
$SetupFile=Join-Path $ReleaseDir "IMORTAIS-Combat-Client-Setup-v0.7.0.exe"
$HashFile="$SetupFile.sha256"
$ZipFile=Join-Path $ReleaseDir "IMORTAIS-Combat-Client-v0.7.0-win-x64.zip"
$AppExe=Join-Path $BuildRoot "dist\IMORTAIS-Combat-Client\IMORTAIS-Combat-Client.exe"
$BuildIdentity=Join-Path $BuildRoot "upstream\AlbionOnline-StatisticsAnalysis\src\StatisticsAnalysisTool\Imortais\ImortaisBuildIdentity.cs"
$KeyDir=Join-Path $env:LOCALAPPDATA "IMORTAIS Combat Client\signing-keys"
$PubKeyFile=Join-Path $KeyDir "NetSparkle_Ed25519.pub"
$PrivKeyFile=Join-Path $KeyDir "NetSparkle_Ed25519.priv"
$SparkleTool=Join-Path $env:LOCALAPPDATA "IMORTAIS Combat Client\tools\netsparkle\netsparkle-generate-appcast.exe"
$FeedPath="upstream/AlbionOnline-StatisticsAnalysis/src/StatisticsAnalysisTool/imortais-netsparkle-update-check.xml"
$IdentityPath="upstream/AlbionOnline-StatisticsAnalysis/src/StatisticsAnalysisTool/Imortais/ImortaisBuildIdentity.cs"
$FeedRoot=Join-Path $env:TEMP ("icc-v070-feed-"+$PID)
function Assert-File([string]$f){if(!(Test-Path -LiteralPath $f -PathType Leaf)){throw "Arquivo ausente: $f"}}
function Save-Text([string]$f,[string]$s){$s=[regex]::Replace($s,'\r\n?',[string][char]10);[IO.File]::WriteAllText($f,$s,[Text.UTF8Encoding]::new($false))}
function Sign-Verify([string]$f){
    $output=& $SparkleTool --generate-signature $f
    if($LASTEXITCODE -ne 0){throw "Assinatura falhou: $f"}
    $line=@($output|Where-Object{$_ -match '^Signature:\s*(.+)$'}|Select-Object -First 1)
    if(!$line){throw "Nenhuma assinatura: $f"}
    $matched=[regex]::Match([string]$line[0], '^Signature:\s*(.+)$')
    if(!$matched.Success){throw "Formato de assinatura inesperado: $f"}
    $sig=$matched.Groups[1].Value.Trim()
    $verify=& $SparkleTool --verify $f --signature $sig
    if($LASTEXITCODE -ne 0 -or ($verify -join [char]10) -notmatch 'Signature valid'){throw "Assinatura invalida: $f"}
    return $sig
}
$git=(Get-Command git.exe -ErrorAction Stop).Source
$gh=(Get-Command gh.exe -ErrorAction Stop).Source
foreach($f in @($SetupFile,$HashFile,$ZipFile,$AppExe,$BuildIdentity,$PubKeyFile,$PrivKeyFile,$SparkleTool)){Assert-File $f}
if((Get-Item $AppExe).VersionInfo.FileVersion -notlike "0.7.0*"){throw "Executavel local nao e v0.7.0"}
$sha=(& $git -C $BuildRoot rev-parse HEAD).Trim().ToLowerInvariant()
if($LASTEXITCODE -ne 0 -or $sha -notmatch '^[0-9a-f]{40}$'){throw "SHA de build invalido"}
$idText=[IO.File]::ReadAllText($BuildIdentity)
if(!$idText.Contains($sha) -or !$idText.Contains('"v0.7.0"')){throw "Build e identidade de commit diferentes"}
$want=((Get-Content -LiteralPath $HashFile -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
$actual=(Get-FileHash -LiteralPath $SetupFile -Algorithm SHA256).Hash.ToLowerInvariant()
if($want -ne $actual){throw "Hash do instalador diverge. Parando."}
Write-Host "Artefatos v0.7.0 conferidos: SHA256 e commit $sha" -ForegroundColor Green
& $gh auth status
if($LASTEXITCODE -ne 0){throw "GitHub CLI nao autenticado"}
& $gh auth setup-git | Out-Null
if($LASTEXITCODE -ne 0){throw "Falha gh auth setup-git"}
$list=& $gh release list --repo $Repo --limit 100 --json tagName
if($LASTEXITCODE -ne 0){throw "Falha listando releases"}
if(@($list|ConvertFrom-Json|Where-Object{$_.tagName -eq "v$Version"}).Count -gt 0){throw "Release v$Version ja existe; nao duplicar"}
& $git clone --single-branch --branch main "https://github.com/$Repo.git" $FeedRoot
if($LASTEXITCODE -ne 0){throw "Falha clonar main"}
$FeedFile=Join-Path $FeedRoot ($FeedPath.Replace('/','\'))
$FeedSigFile="$FeedFile.signature"
$MainIdentity=Join-Path $FeedRoot ($IdentityPath.Replace('/','\'))
$ReadmeFile=Join-Path $FeedRoot "README.md"
foreach($f in @($FeedFile,$FeedSigFile,$MainIdentity,$ReadmeFile)){Assert-File $f}
if(([IO.File]::ReadAllText($FeedFile)) -notmatch 'sparkle:version="0\.6\.8"'){throw "Feed publico nao esta na v0.6.8"}
$mainIdText=[IO.File]::ReadAllText($MainIdentity)
if(!$mainIdText.Contains("LOCAL-UNSTAMPED")){throw "Identidade da main inesperada"}
try{
  $env:SPARKLE_PUBLIC_KEY=(Get-Content $PubKeyFile -Raw).Trim()
  $env:SPARKLE_PRIVATE_KEY=(Get-Content $PrivKeyFile -Raw).Trim()
  if(!$env:SPARKLE_PUBLIC_KEY -or !$env:SPARKLE_PRIVATE_KEY){throw "Chaves originais vazias"}
  $installerSig=Sign-Verify $SetupFile
  Write-Host "Assinatura existente conferida." -ForegroundColor Green
  $notesPath=Join-Path $FeedRoot "docs\\releases\\v0.7.0-guild-combat-client.md"
  if(-not(Test-Path -LiteralPath $notesPath)){throw "Notas v0.7.0 ausentes"}
  $notes=Get-Content -LiteralPath $notesPath -Raw -Encoding UTF8

  $notesFile=Join-Path $ReleaseDir "IMORTAIS-Combat-Client-v0.7.0-notes.md"
  Save-Text $notesFile $notes
  Write-Host "Publicando release GitHub com --notes-file..." -ForegroundColor Cyan
  & $gh release create "v$Version" $SetupFile $HashFile $ZipFile --repo $Repo --target $sha --title "Guild Combat Client v$Version" --notes-file $notesFile
  if($LASTEXITCODE -ne 0){throw "Falha criando release. Feed nao alterado."}
  $release=& $gh release view "v$Version" --repo $Repo --json tagName,assets
  if($LASTEXITCODE -ne 0){throw "Release criada mas nao verificavel"}
  $info=$release|ConvertFrom-Json
  $names=@($info.assets|ForEach-Object{$_.name})
  foreach($f in @($SetupFile,$HashFile,$ZipFile)){if($names -notcontains (Split-Path $f -Leaf)){throw "Asset ausente na release: $f"}}
  $releaseUrl="https://github.com/$Repo/releases/tag/v$Version"
  $downloadUrl="https://github.com/$Repo/releases/download/v$Version/IMORTAIS-Combat-Client-Setup-v$Version.exe"
  $setupLength=(Get-Item $SetupFile).Length
  $pubDate=[DateTimeOffset]::UtcNow.ToString("r")
  $xml=@"
<?xml version="1.0" encoding="utf-8"?>
<rss xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle" version="2.0">
  <channel>
    <title>Guild Combat Client</title>
    <description>Atualizações oficiais do Guild Combat Client.</description>
    <language>pt-BR</language>
    <item>
      <title>Guild Combat Client v$Version</title>
      <description><![CDATA[<a href="$releaseUrl">Ver notas da versão no GitHub</a>]]></description>
      <pubDate>$pubDate</pubDate>
      <enclosure url="$downloadUrl" sparkle:version="$Version" sparkle:shortVersionString="$Version" sparkle:os="windows-x64" length="$setupLength" type="application/octet-stream" sparkle:edSignature="$installerSig" sparkle:signature="$installerSig" />
    </item>
  </channel>
</rss>
"@
  Save-Text $FeedFile $xml
  $feedSig=Sign-Verify $FeedFile
  Save-Text $FeedSigFile $feedSig
  Save-Text $MainIdentity ($mainIdText.Replace("LOCAL-UNSTAMPED",$sha))
  $readme=[IO.File]::ReadAllText($ReadmeFile)
  $readme=$readme.Replace("**Versão estável atual:** v0.6.8","**Versão estável atual:** v0.7.0")
  $readme=$readme.Replace("IMORTAIS-Combat-Client-Setup-v0.6.8.exe","IMORTAIS-Combat-Client-Setup-v0.7.0.exe")
  Save-Text $ReadmeFile $readme
  & $git -C $FeedRoot add $FeedPath "$FeedPath.signature" $IdentityPath README.md
  if($LASTEXITCODE -ne 0){throw "Falha git add"}
  & $git -C $FeedRoot commit -m "release: publish signed Guild Combat Client v0.7.0 feed"
  if($LASTEXITCODE -ne 0){throw "Falha git commit"}
  & $git -C $FeedRoot push origin main
  if($LASTEXITCODE -ne 0){throw "Release criada, mas feed nao publicado. NAO anunciar ainda."}
  Write-Host "v0.7.0 ASSINADA, RELEASE E UPDATER PUBLICADOS COM SUCESSO" -ForegroundColor Green
  Write-Host "Build: $sha" -ForegroundColor Green
  Write-Host "SHA256: $actual" -ForegroundColor Green
  Write-Host "Release: $releaseUrl" -ForegroundColor Green
  Write-Host "Feed: https://raw.githubusercontent.com/$Repo/main/$FeedPath" -ForegroundColor Green
}
finally{
  Remove-Item Env:SPARKLE_PRIVATE_KEY -ErrorAction SilentlyContinue
  Remove-Item Env:SPARKLE_PUBLIC_KEY -ErrorAction SilentlyContinue
}
