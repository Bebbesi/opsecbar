; Inno Setup script for the OpSec Bar installer (OpSecBar-Setup.exe).
; Built by the Release workflow:  ISCC /DAppVersion=1.2.0 installer\OpSecBar.iss
; Per-user install, no administrator rights: same location the in-app updater and scripts\install.ps1 use.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{8E1B6C52-6C0B-4E0B-9A53-0B5E7C2F4A11}
AppName=OpSec Bar
AppVersion={#AppVersion}
AppPublisher=Bebbesi
AppPublisherURL=https://github.com/Bebbesi/opsecbar
AppSupportURL=https://github.com/Bebbesi/opsecbar/issues
DefaultDirName={localappdata}\Programs\OpSecBar
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..
OutputBaseFilename=OpSecBar-Setup
SetupIconFile=..\src\OpSecBar\Assets\opsecbar.ico
UninstallDisplayIcon={app}\OpSecBar.exe
UninstallDisplayName=OpSec Bar
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no

[Files]
Source: "..\dist\OpSecBar\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{userprograms}\OpSec Bar"; Filename: "{app}\OpSecBar.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\OpSecBar.exe"; Description: "Start OpSec Bar"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Close the bar cleanly and bring the Windows taskbar back before the files are removed.
Filename: "{app}\OpSecBar.exe"; Parameters: "--restore"; RunOnceId: "RestoreTaskbar"; Flags: runhidden

[Registry]
; The app writes this value itself when "Start when I sign in" is on; the uninstaller removes it.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "OpSecBar"; Flags: dontcreatekey uninsdeletevalue

[Code]
// Upgrading over a running copy: ask it to exit (which restores the Windows taskbar), then make sure no process is left.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  if FileExists(ExpandConstant('{app}\OpSecBar.exe')) then
    Exec(ExpandConstant('{app}\OpSecBar.exe'), '--restore', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/im OpSecBar.exe /f', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(800);
  Result := '';
end;
