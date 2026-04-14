; RewriteTool Inno Setup Script
; Produces a single setup.exe installer

#define MyAppName "RewriteTool"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Varun Ramesh"
#define MyAppURL "https://github.com/varunr89/rewrite-tool"
#define MyAppExeName "RewriteTool.exe"

[Setup]
AppId={{B7E3F1A2-4D5C-4E8F-9A1B-3C6D7E8F9A0B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputBaseFilename=RewriteToolSetup
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=lowest
OutputDir=installer
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startupentry"; Description: "Start {#MyAppName} when Windows starts"; GroupDescription: "Additional options:"

[Files]
; Main application and all dependencies
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Registry]
; Auto-start on login (only if user selected the task)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startupentry

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillApp"
