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
CloseApplications=no

; ---- Look: dark, like the OpSec Bar settings window ----------------------------------------------------
; Needs Inno Setup 6.6 or later (dark mode, background colors). The Release workflow's runner has 6.7.1.
WizardStyle=modern dark hidebevels
WizardBackColor=#202020
; Banner on the Welcome and Finished pages, logo in the corner of the other pages: one file per DPI size,
; Setup picks the closest (see installer\images; sizes from the WizardImageFile / WizardSmallImageFile help).
WizardImageFile=images\wizard-202.png,images\wizard-269.png,images\wizard-336.png,images\wizard-403.png,images\wizard-430.png,images\wizard-534.png
WizardSmallImageFile=images\logo-58.png,images\logo-77.png,images\logo-97.png,images\logo-116.png,images\logo-124.png,images\logo-159.png
WizardSmallImageBackColor=none
; Welcome -> Installing -> Finished: there is nothing to choose, so no "Ready to install" page.
DisableWelcomePage=no
DisableReadyPage=yes

[Messages]
SetupWindowTitle=OpSec Bar Setup
WelcomeLabel1=Welcome to OpSec Bar
WelcomeLabel2=OpSec Bar replaces the Windows taskbar with one you can make your own: themes, its own Start menu, a media player, quick settings and more.%n%nIt installs for your account only and doesn't need administrator rights.
InstallingLabel=Please wait while OpSec Bar is installed.
FinishedHeadingLabel=OpSec Bar is ready
FinishedLabelNoIcons=Right-click the bar for its settings.%n%nTo get the Windows taskbar back at any time, exit OpSec Bar or press Ctrl+Alt+Shift+Q.
FinishedLabel=Right-click the bar for its settings. You'll also find OpSec Bar in the Start menu.%n%nTo get the Windows taskbar back at any time, exit OpSec Bar or press Ctrl+Alt+Shift+Q.
ClickFinish=Click Finish to close Setup.

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
// Updating an existing install: say so on the Welcome page.
function IsUpdate: Boolean;
begin
  Result := FileExists(ExpandConstant('{localappdata}\Programs\OpSecBar\OpSecBar.exe'));
end;

procedure InitializeWizard;
begin
  if IsUpdate then
  begin
    WizardForm.WelcomeLabel1.Caption := 'Update OpSec Bar';
    WizardForm.WelcomeLabel2.Caption :=
      'This installs version {#AppVersion} over the one you have.' + #13#10 + #13#10 +
      'Your settings, themes and GIFs are kept. OpSec Bar closes during the update and you can start it again at the end.';
  end;
end;

// Without the Ready page, the Welcome page is the last one before installing: its button installs.
procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpWelcome then
  begin
    if IsUpdate then
      WizardForm.NextButton.Caption := '&Update'
    else
      WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall);
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

// The uninstaller removes the program; the settings, themes, GIFs and log live in %APPDATA%\OpSecBar and are kept unless asked.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\OpSecBar');
    if (not UninstallSilent) and DirExists(DataDir) then
      if MsgBox('Also delete your OpSec Bar settings, themes and GIFs?' + #13#10 + #13#10 + DataDir, mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
