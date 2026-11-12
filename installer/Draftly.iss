; Inno Setup script for Draftly (Inno Setup 6.3 or newer)
; Build: run ..\build.ps1, or compile this file with ISCC.exe after publishing the exe.
;
; Installs to Program Files for all users by default (asks for administrator permission).
; The first page also offers "Install for me only", which needs no administrator rights.
;
; The Bytehawk Studio Launcher can run this setup silently:
;   Draftly-<version>-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOICONS /DIR="<folder>"

#define MyAppName "Draftly"
#ifndef MyAppVersion
  #define MyAppVersion "1.3.0"
#endif
#define MyAppPublisher "Bytehawk Studio"
#define MyAppExeName "Draftly.exe"
#define MyAppUrl "https://github.com/adamglassner/draftly"
#ifndef PublishDir
  #define PublishDir "..\dist\publish"
#endif

[Setup]
AppId={{6B1F3C2E-8D4A-4F7B-9C1E-D7A2F5B83E19}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
AppCopyright=© 2026 {#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename=Draftly-{#MyAppVersion}-Setup
SetupIconFile=..\assets\draftly.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ChangesAssociations=yes
; Ask to close Draftly if it's running (Windows Restart Manager)
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
; Show the version number on the first and last pages
WelcomeLabel1=Welcome to the [name/ver] Setup Wizard
FinishedHeadingLabel=[name/ver] is installed

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "assocrtf"; Description: "Open &Rich Text files (.rtf) with Draftly"; GroupDescription: "File types (optional; leave unticked to skip):"; Flags: unchecked
Name: "assoctxt"; Description: "Open &text files (.txt) with Draftly"; GroupDescription: "File types (optional; leave unticked to skip):"; Flags: unchecked
Name: "assocmd"; Description: "Open &Markdown files (.md) with Draftly"; GroupDescription: "File types (optional; leave unticked to skip):"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; The icon is embedded in Draftly.exe, so shortcuts use the exe's own icon.
; Straight into the Start menu's app list (no subfolder), like other Windows 10/11 apps.
; /NOICONS (used by the Bytehawk Studio Launcher) skips it.
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "Write and edit documents"; Check: not NoIconsRequested
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "Write and edit documents"; Tasks: desktopicon

[Registry]
; Register Draftly as an application so it shows up in "Open with" and Settings > Default apps.
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#MyAppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".rtf"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".txt"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".docx"; ValueData: ""

Root: HKA; Subkey: "Software\Classes\Draftly.rtf"; ValueType: string; ValueData: "Rich Text Document"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Draftly.rtf\DefaultIcon"; ValueType: string; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\Draftly.rtf\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Draftly.txt"; ValueType: string; ValueData: "Text Document"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Draftly.txt\DefaultIcon"; ValueType: string; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\Draftly.txt\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Draftly.md"; ValueType: string; ValueData: "Markdown Document"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Draftly.md\DefaultIcon"; ValueType: string; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\Draftly.md\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

Root: HKA; Subkey: "Software\Bytehawk Studio\Draftly\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#MyAppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Bytehawk Studio\Draftly\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "A modern rich text word processor"
Root: HKA; Subkey: "Software\Bytehawk Studio\Draftly\Capabilities\FileAssociations"; ValueType: string; ValueName: ".rtf"; ValueData: "Draftly.rtf"
Root: HKA; Subkey: "Software\Bytehawk Studio\Draftly\Capabilities\FileAssociations"; ValueType: string; ValueName: ".txt"; ValueData: "Draftly.txt"
Root: HKA; Subkey: "Software\Bytehawk Studio\Draftly\Capabilities\FileAssociations"; ValueType: string; ValueName: ".md"; ValueData: "Draftly.md"
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "Draftly"; ValueData: "Software\Bytehawk Studio\Draftly\Capabilities"; Flags: uninsdeletevalue

; Optional file types (the checkboxes). Draftly is added to the type's "Open with" list.
; Windows 10/11 don't let an installer silently change your default app, so Setup then
; offers to open Settings > Default apps for Draftly, where one click makes it the default.
Root: HKA; Subkey: "Software\Classes\.rtf\OpenWithProgids"; ValueType: string; ValueName: "Draftly.rtf"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assocrtf
Root: HKA; Subkey: "Software\Classes\.txt\OpenWithProgids"; ValueType: string; ValueName: "Draftly.txt"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoctxt
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "Draftly.md"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assocmd

[Run]
Filename: "ms-settings:defaultapps?registeredAppMachine=Draftly"; Description: "Open Default apps to make Draftly the default for the file types you chose"; Flags: shellexec nowait postinstall skipifsilent unchecked; Tasks: assocrtf assoctxt assocmd; Check: IsAdminInstallMode
Filename: "ms-settings:defaultapps?registeredAppUser=Draftly"; Description: "Open Default apps to make Draftly the default for the file types you chose"; Flags: shellexec nowait postinstall skipifsilent unchecked; Tasks: assocrtf assoctxt assocmd; Check: not IsAdminInstallMode
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
; Draftly's built-in updater runs this setup with /SILENT /RELAUNCH: open Draftly again afterwards, as the user (not as admin).
Filename: "{app}\{#MyAppExeName}"; Parameters: "--updated"; Flags: nowait runasoriginaluser; Check: RelaunchRequested

[UninstallRun]
; Removes the per-user "Open with" entries Draftly adds when you click "Make Draftly the default" in its Settings.
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister-file-types"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterFileTypes"

; No [UninstallDelete]: uninstalling removes the program, shortcuts and registry entries.
; Your documents and settings (%APPDATA%\Draftly) are left alone.

[Code]
{ /NOICONS: the launcher installs Draftly without Start menu entries. Inno Setup only honours it
  on the Start Menu folder page, which Draftly doesn't show, so check the command line directly. }
function RelaunchRequested: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/RELAUNCH') = 0 then
      Result := True;
end;

function NoIconsRequested: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/NOICONS') = 0 then
      Result := True;
end;
