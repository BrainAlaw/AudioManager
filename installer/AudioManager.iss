#define MyAppName "Audio Manager"
#define MyAppExeName "AudioManager.exe"
#ifndef AppVersion
#define AppVersion "1.0.0"
#endif
#ifndef PublishDir
#define PublishDir "..\artifacts\publish\win-x64"
#endif

[Setup]
AppId={{22A14983-2EF3-42A7-BB61-9307B7A7CC3A}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher=BrainAlaw
AppPublisherURL=https://github.com/BrainAlaw/AudioManager
AppSupportURL=https://github.com/BrainAlaw/AudioManager/issues
AppUpdatesURL=https://github.com/BrainAlaw/AudioManager/releases
DefaultDirName={autopf}\Audio Manager
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=AudioManager-Setup-{#AppVersion}
SetupIconFile=..\src\AudioManager\Resources\Icons\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
