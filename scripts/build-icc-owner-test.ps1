param(
    [Parameter(Mandatory=$true)][string]$AppDir,
    [Parameter(Mandatory=$true)][string]$OutDir
)
$ErrorActionPreference = "Stop"
$verifier = Join-Path $PSScriptRoot "verify-icc-owner.ps1"
$exe = Join-Path $AppDir "IMORTAIS-Combat-Client.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "No published Combat Client EXE" }
if (-not (Test-Path -LiteralPath $verifier)) { throw "Missing device verifier" }
$version = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
if ($version -notlike "0.6.0*") { throw "Only the current v0.6.0 test build is permitted" }

function Find-Inno {
    $cmd = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $pf86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
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
    if ($LASTEXITCODE -ne 0) { throw "Failed to install Inno Setup on CI runner" }
    $iscc = Find-Inno
}
if (-not $iscc) { throw "Inno Setup 6 not available" }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$scriptPath = Join-Path $env:RUNNER_TEMP "icc-one-device-test.iss"
$appSource = $AppDir.Replace('"','""')
$installerOutput = $OutDir.Replace('"','""')
$verifierSource = $verifier.Replace('"','""')
$iss = @'
#define AppName "IMORTAIS Combat Client"
#define AppVersion "0.6.0"
#define AppExe "IMORTAIS-Combat-Client.exe"
#define AppSource "__APP_SOURCE__"
#define InstallerOutput "__OUTPUT__"
#define VerifierSource "__VERIFIER__"
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
OutputBaseFilename=IMORTAIS-Combat-Client-TESTE-BadMack
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
Source: "{#VerifierSource}"; Flags: dontcopy
[Icons]
Name: "{autoprograms}\IMORTAIS Combat Client"; Filename: "{app}\{#AppExe}"
[Run]
Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Flags: nowait skipifsilent
[Code]
function InitializeSetup(): Boolean;
var
  PowerShellPath, VerifyScript, Args: String;
  ExitCode: Integer;
begin
  Result := False;
  try
    ExtractTemporaryFile('verify-icc-owner.ps1');
    VerifyScript := ExpandConstant('{tmp}\verify-icc-owner.ps1');
    PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
    Args := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + VerifyScript + '"';
    if not Exec(PowerShellPath, Args, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    begin
      MsgBox('Nao foi possivel iniciar a validacao do dispositivo no Windows. Nada foi instalado.', mbError, MB_OK);
      Exit;
    end;
    if ExitCode <> 0 then
    begin
      case ExitCode of
        21: MsgBox('Configuracao da telemetria nao encontrada no perfil Windows atual (codigo 21). Abra o Combat Client habitual neste usuario e confirme o pareamento.', mbError, MB_OK);
        22: MsgBox('Dispositivo ou chave de telemetria ausente (codigo 22). O Combat Client precisa estar pareado com o War Room.', mbError, MB_OK);
        24: MsgBox('O War Room autenticou o dispositivo, mas o registro nao esta vinculado ao jogador BadMack (codigo 24). A instalacao permanece bloqueada.', mbError, MB_OK);
        25: MsgBox('Nao foi possivel consultar o War Room com seguranca (codigo 25). Verifique conexao e disponibilidade do servidor.', mbError, MB_OK);
        26: MsgBox('O War Room recusou a autorizacao do dispositivo (codigo 26). O token pode estar expirado, revogado ou vinculado a outro computador.', mbError, MB_OK);
        27: MsgBox('O War Room nao retornou a identidade vinculada ao token (codigo 27). O registro precisa ser validado pela staff.', mbError, MB_OK);
      else
        MsgBox('Falha de autorizacao do dispositivo (codigo ' + IntToStr(ExitCode) + '). Nada foi instalado.', mbError, MB_OK);
      end;
      Exit;
    end;
    Result := True;
  except
    MsgBox('Falha na validacao do dispositivo. Nenhum arquivo foi instalado.', mbError, MB_OK);
  end;
end;
'@
$iss = $iss.Replace("__APP_SOURCE__", $appSource).Replace("__OUTPUT__", $installerOutput).Replace("__VERIFIER__", $verifierSource)
[IO.File]::WriteAllText($scriptPath, $iss, (New-Object System.Text.UTF8Encoding($false)))
& $iscc $scriptPath
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
$installer = Join-Path $OutDir "IMORTAIS-Combat-Client-TESTE-BadMack.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw "Expected installer missing" }
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$installer.sha256", "$hash  $(Split-Path -Leaf $installer)", (New-Object System.Text.UTF8Encoding($false)))
Write-Host "OWNER_TEST_INSTALLER=$installer"
Write-Host "OWNER_TEST_INSTALLER_SHA256=$hash"
