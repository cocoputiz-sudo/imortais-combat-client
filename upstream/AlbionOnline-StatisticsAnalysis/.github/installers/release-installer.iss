; Nome exibido. AppId, nome do .exe, mutex e nome do instalador continuam os mesmos
; para a atualização automática e o pareamento de quem já tem o client instalado.
#define MyAppName "Guild Combat Client"
#define MyLegacyAppName "IMORTAIS Combat Client"
#define MyAppPublisher "IMORTAIS"
#define MyAppExeName "IMORTAIS-Combat-Client.exe"
#define MyAppId "{{6D7ED979-FC39-4D6A-83A4-6493E2C61A16}}"

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#ifndef MyAppVersionInfo
  #define MyAppVersionInfo "0.0.0.0"
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/cocoputiz-sudo/imortais-combat-client
DefaultDirName={autopf}\Statistics Analysis Tool
DefaultGroupName={#MyAppName}
UsePreviousAppDir=no
DisableDirPage=no
DisableProgramGroupPage=yes
UsePreviousGroup=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma
SolidCompression=yes
WizardStyle=modern
AppMutex=IMORTAISCombatClient_D842B07173EA42BFB36E3FD6C2F1A940
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
OutputDir=..\..
OutputBaseFilename=IMORTAIS-Combat-Client-Setup-v{#MyAppVersion}
SetupIconFile=..\..\src\StatisticsAnalysisTool\imortais-icon.ico
VersionInfoVersion={#MyAppVersionInfo}
VersionInfoProductVersion={#MyAppVersionInfo}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Dirs]
Name: "{app}"; Permissions: users-modify

[Files]
Source: "..\..\src\StatisticsAnalysisTool\bin\Release\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Atalhos com o nome antigo (antes de virar Guild Combat Client).
Type: files; Name: "{autodesktop}\{#MyLegacyAppName}.lnk"
Type: files; Name: "{autoprograms}\{#MyLegacyAppName}\{#MyLegacyAppName}.lnk"
Type: files; Name: "{autoprograms}\{#MyLegacyAppName}\Uninstall {#MyLegacyAppName}.lnk"
Type: dirifempty; Name: "{autoprograms}\{#MyLegacyAppName}"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
