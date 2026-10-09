param(
  [Parameter(Mandatory=$true)][string]$AppDir,
  [Parameter(Mandatory=$true)][string]$OutDir
)
$ErrorActionPreference = "Stop"
$exe = Join-Path $AppDir "IMORTAIS-Combat-Client.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "Application EXE missing" }
$ver = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
if ($ver -notlike "0.6.1*") { throw "Expected v0.6.1 executable; got $ver" }

function Find-Inno {
  $cmd = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }
  $pf86 = [Environment]::GetEnvironmentVariable("ProgramFiles(x86)")
  $candidates = @(
    (Join-Path $pf86 "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
  )
  return $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
$iscc = Find-Inno
if (-not $iscc -and (Get-Command "choco.exe" -ErrorAction SilentlyContinue)) {
  & choco.exe install innosetup -y --no-progress --limit-output
  if ($LASTEXITCODE -ne 0) { throw "Failed to install Inno Setup" }
  $iscc = Find-Inno
}
if (-not $iscc) { throw "Inno Setup 6 missing" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$iss = @'
#define AppName "IMORTAIS Combat Client"
#define AppVersion "0.6.1"
#define AppExe "IMORTAIS-Combat-Client.exe"
#define AppSource "__APP_SOURCE__"
#define InstallerOutput "__OUTPUT__"
[Setup]
; Match the official v0.6.0 Inno identity: upgrades the existing installation.
AppId={{D842B071-73EA-42BF-B36E-3FD6C2F1A940}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=IMORTAIS
DefaultDirName={localappdata}\Programs\IMORTAIS Combat Client
UsePreviousAppDir=yes
DefaultGroupName=IMORTAIS Combat Client
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#InstallerOutput}
OutputBaseFilename=IMORTAIS-Combat-Client-Setup-v0.6.1
VersionInfoVersion=0.6.1.0
VersionInfoProductVersion=0.6.1.0
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=IMORTAIS Combat Client
AppMutex=IMORTAISCombatClient_D842B07173EA42BFB36E3FD6C2F1A940
CloseApplications=no
RestartApplications=no
[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autoprograms}\IMORTAIS Combat Client"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\IMORTAIS Combat Client"; Filename: "{app}\{#AppExe}"
[Run]
Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
'@
$iss = $iss.Replace("__APP_SOURCE__", $AppDir.Replace('"','""'))
$iss = $iss.Replace("__OUTPUT__", $OutDir.Replace('"','""'))
$issPath = Join-Path $env:RUNNER_TEMP "icc-v061-candidate.iss"
[System.IO.File]::WriteAllText($issPath,$iss,[System.Text.UTF8Encoding]::new($false))
& $iscc $issPath
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
$setup = Join-Path $OutDir "IMORTAIS-Combat-Client-Setup-v0.6.1.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw "Installer missing" }
$sha = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$sha  IMORTAIS-Combat-Client-Setup-v0.6.1.exe" |
  Set-Content -LiteralPath (Join-Path $OutDir "IMORTAIS-Combat-Client-Setup-v0.6.1.exe.sha256") -Encoding ascii
Write-Host "Candidate installer built, SHA256=$sha"
