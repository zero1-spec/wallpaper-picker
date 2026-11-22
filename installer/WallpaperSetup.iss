; WallpaperSuite Inno Setup 6 installer script
; Build: iscc installer\WallpaperSetup.iss

#define MyAppName "WallpaperSuite"
#define MyAppVersion "0.3.0"
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
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64

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

[Code]
var
  QuitResult: Integer;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    // On upgrade: quit any running daemon before installing over it.
    Exec('"{app}\{#MyAppExeName}"', 'quit', '', SW_HIDE, ewWaitUntilTerminated, QuitResult);
  end;

  if CurStep = ssPostInstall then
  begin
    // Launch walld.exe --background after install.
    Exec('"{app}\{#MyAppExeName}"', '--background', '{app}', SW_SHOW, ewNoWait, ResultCode);
  end;
end;

function UninstallInitialize(): Boolean;
var
  ResultCode: Integer;
begin
  // Quit running daemon before uninstall.
  Exec('"{app}\{#MyAppExeName}"', 'quit', '', SW_HIDE, ewWaitUntilTerminated, QuitResult);

  // Close wallpicker.exe if running.
  Exec('taskkill', '/f /im wallpicker.exe 2>nul', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Always keep %APPDATA%\WallpaperPicker (config, logs, thumbnails) by default.

  Result := True;
end;
