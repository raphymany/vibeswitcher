; VibeSwitcher per-user installer (Inno Setup 6)
; Build: iscc installer\VibeSwitcher.iss   (expects the published app in ..\publish)
;
; Design notes:
; - Per-user install ({localappdata}\Programs) with PrivilegesRequired=lowest:
;   no UAC prompt on install, update, or uninstall.
; - AppMutex matches the fixed-name mutex the app holds (SingleInstanceHelper),
;   so setup prompts to close a running instance instead of failing mid-copy.
; - Uninstall keeps %APPDATA%\VibeSwitcher (profiles/config). The ONLY path that
;   deletes it is the explicit /DELETEDATA=1 uninstaller parameter, passed by the
;   in-app uninstall flow after the user opts in. No wildcards anywhere.

#define MyAppName "VibeSwitcher"
#define MyAppExeName "VibeSwitcher.exe"
#define MyAppPublisher "Raphael Mansour"
#define MyAppURL "https://github.com/raphymany/vibeswitcher"
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif
#define MyAppVersion GetVersionNumbersString(SourceDir + "\" + MyAppExeName)

[Setup]
AppId={{8B6F2C49-5A0E-4D7B-9C1F-3D2A47E0B5C1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=output
OutputBaseFilename=VibeSwitcher-Setup
SetupIconFile=..\VibeSwitcher\Resources\Icons\vs-icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
AppMutex=Local\VibeSwitcher_App
CloseApplications=yes
MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "desktopicon";  Description: "Create a &desktop shortcut"; Flags: unchecked
Name: "startupentry"; Description: "Start {#MyAppName} when Windows starts"; Flags: unchecked

[Files]
; Single-file self-contained publish — exactly one exe, listed explicitly so stray
; files in the publish folder (or the .pdb) can never ride along.
Source: "{#SourceDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; AppUserModelID matches the ID the app sets via SetCurrentProcessExplicitAppUserModelID, so
; Windows resolves toast/notification icon attribution to the shortcut's (our) icon.
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; AppUserModelID: "RaphaelMansour.VibeSwitcher"
Name: "{userdesktop}\{#MyAppName}";  Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; AppUserModelID: "RaphaelMansour.VibeSwitcher"

[Registry]
; Optional install task — writes the exact value name the app's StartupService manages.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "VibeSwitcher"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: startupentry; \
    Flags: uninsdeletevalue
; Ensure the Run value is removed on uninstall even when it was enabled later from
; inside the app (dontcreatekey = writes nothing at install time).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; \
    ValueName: "VibeSwitcher"; Flags: uninsdeletevalue dontcreatekey
; The app registers its AppUserModelId (toast/notification attribution) under HKCU at runtime;
; remove that key on uninstall so nothing is left behind (dontcreatekey = writes nothing at install).
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\RaphaelMansour.VibeSwitcher"; \
    Flags: uninsdeletekey dontcreatekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; \
    Flags: nowait postinstall skipifsilent

[Code]
// The in-app uninstall flow passes /DELETEDATA=1 after the user explicitly opts in.
// Interactive uninstalls (Windows Settings / Apps) always KEEP the data.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and
     (ExpandConstant('{param:DELETEDATA|0}') = '1') then
    DelTree(ExpandConstant('{userappdata}\{#MyAppName}'), True, True, True);
end;
