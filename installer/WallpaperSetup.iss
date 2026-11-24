; WallpaperSuite Inno Setup 6 installer script
; Build: iscc installer\WallpaperSetup.iss

#define MyAppName "WallpaperSuite"
#define MyAppVersion "0.5.1"
#define MyAppPublisher "WallpaperSuite"
#define MyAppExeName "walld.exe"

[Setup]
AppID={{4A7B2C1D-9E3F-4A5B-8C7D-6E5F4A3B2C1D}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\WallpaperSuite
DefaultGroupName={#MyAppName}
PrivilegesRequired=none
PrivilegesRequiredOverridesAllowed=dialog
ChangesAssociations=no
Compression=lzma2
SolidCompression=yes
OutputDir=Output
OutputBaseFilename=WallpaperSetup-{#MyAppVersion}
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\assets\icon.ico
AllowNoIcons=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start with Windows"; GroupDescription: "Startup options:"

[Files]
Source: "..\dist\WallpaperSuite\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WallpaperSuite"; ValueData: """{app}\{#MyAppExeName}"" --background"; Tasks: autostart; Flags: uninsdeletevalue preservestringtype

[Run]
; Start the tray daemon when setup finishes. A [Run] entry expands {app} itself
; (the old [Code] Exec call passed the literal text "{app}" and never launched).
; Not skipped in silent mode, so /VERYSILENT installs also start the daemon.
Filename: "{app}\{#MyAppExeName}"; Parameters: "--background"; WorkingDir: "{app}"; Description: "Start {#MyAppName} now"; Flags: nowait postinstall runasoriginaluser

[Code]
// Constants such as {app} are NOT expanded inside string literals in [Code];
// they must go through ExpandConstant(). Exec() also runs the program directly
// (no shell), so shell syntax like "2>nul" must never be passed as arguments.
procedure StopRunningApp();
var
  ResultCode: Integer;
  AppDir, Daemon, TaskKill: String;
begin
  AppDir := ExpandConstant('{app}');
  Daemon := AddBackslash(AppDir) + '{#MyAppExeName}';
  TaskKill := ExpandConstant('{sys}\taskkill.exe');

  // Ask a running daemon to quit cleanly first so its tray icon is removed.
  // "walld.exe quit" only forwards the request, so give the daemon a moment to exit.
  if FileExists(Daemon) then
  begin
    Exec(Daemon, 'quit', AppDir, SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1500);
  end;

  // Force-close anything still holding files open (no-op when nothing is running).
  Exec(TaskKill, '/f /im {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(TaskKill, '/f /im wallpicker.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(TaskKill, '/f /im wallvideo.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Upgrade: stop the running copy before its files are replaced.
  if CurStep = ssInstall then
    StopRunningApp();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // Runs after the user confirmed the uninstall (UninstallInitialize ran before the prompt).
  // %APPDATA%\WallpaperPicker (config, logs, thumbnails) is always kept.
  if CurUninstallStep = usUninstall then
    StopRunningApp();
end;
