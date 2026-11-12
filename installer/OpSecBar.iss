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
; The "start when I sign in" checkbox follows the current Run entry on upgrades (see CurPageChanged), not the last setup run.
UsePreviousTasks=no

[Tasks]
Name: "autostart"; Description: "Start OpSec Bar automatically when I sign in to Windows"; GroupDescription: "Startup:"

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
; Start when I sign in: the app reads this Run entry at launch and keeps it in sync with Settings > System from then on.
; The value matches what the app writes itself (AutoStartService.Command). Either way the uninstaller removes it.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "OpSecBar"; ValueData: """{app}\OpSecBar.exe"" --autostart"; Flags: uninsdeletevalue; Tasks: autostart
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "OpSecBar"; Flags: deletevalue dontcreatekey uninsdeletevalue; Tasks: not autostart

[Code]
var
  AutoStartDefaultSet: Boolean;

// Fresh install: the startup checkbox is ticked. Upgrade: it shows whether OpSec Bar starts at sign-in right now,
// so running setup again never turns that on or off by surprise. Silent installs keep the default or /TASKS.
procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpSelectTasks) and not AutoStartDefaultSet and not WizardSilent then
  begin
    AutoStartDefaultSet := True;
    if FileExists(AddBackslash(WizardDirValue) + 'OpSecBar.exe') then
    begin
      if RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'OpSecBar') then
        WizardSelectTasks('autostart')
      else
        WizardSelectTasks('!autostart');
    end;
  end;
end;

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
