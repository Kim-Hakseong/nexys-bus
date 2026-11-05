; NexysBus — Inno Setup script. Build: ISCC.exe installer\NexysBus.iss
; Input: ..\bus\dist\app\* (dotnet publish, self-contained win-x64), bootstrapper MicrosoftEdgeWebview2Setup.exe in installer\redist\
#define MyAppName "NexysBus"
#define MyAppVersion "0.4.0-beta.1"
#define MyAppVersionText "0.4.0 beta.1"
#define MyAppPublisher "Nexys"
#define MyAppExeName "NexysBus.exe"
#define MyAppUrl "https://github.com/Kim-Hakseong/nexys-bus"

[Setup]
AppId={{3B9E7A52-1C6D-4D1F-8E0B-NEXYSBUS0010}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersionText}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersionText}
OutputDir=..\bus\dist
OutputBaseFilename=NexysBus-{#MyAppVersion}-setup
SetupIconFile=..\bus\NexysBus.App\Assets\brand\AppIcon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline dialog
ChangesAssociations=yes
LicenseFile=..\installer\LICENSE.txt
VersionInfoVersion=0.1.0.0
VersionInfoProductTextVersion={#MyAppVersionText}
MinVersion=10.0

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "assoc_nxb"; Description: ".nxb 프로젝트를 {#MyAppName}으로 열기"; GroupDescription: "파일 연결 (선택):"; Flags: unchecked
Name: "assoc_mon"; Description: ".mon 버스 캡처를 {#MyAppName}으로 열기"; GroupDescription: "파일 연결 (선택):"; Flags: unchecked

[Files]
Source: "..\bus\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\installer\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "..\installer\redist\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not WebView2Installed

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.nxb\OpenWithProgids"; ValueType: string; ValueName: "NexysBus.nxb"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc_nxb
Root: HKA; Subkey: "Software\Classes\NexysBus.nxb"; ValueType: string; ValueName: ""; ValueData: "NexysBus project"; Flags: uninsdeletekey; Tasks: assoc_nxb
Root: HKA; Subkey: "Software\Classes\NexysBus.nxb\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: assoc_nxb
Root: HKA; Subkey: "Software\Classes\NexysBus.nxb\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: assoc_nxb
Root: HKA; Subkey: "Software\Classes\.mon\OpenWithProgids"; ValueType: string; ValueName: "NexysBus.mon"; ValueData: ""; Flags: uninsdeletevalue; Tasks: assoc_mon
Root: HKA; Subkey: "Software\Classes\NexysBus.mon"; ValueType: string; ValueName: ""; ValueData: "Bus monitor capture"; Flags: uninsdeletekey; Tasks: assoc_mon
Root: HKA; Subkey: "Software\Classes\NexysBus.mon\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: assoc_mon
Root: HKA; Subkey: "Software\Classes\NexysBus.mon\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: assoc_mon

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Microsoft Edge WebView2 런타임 설치 중…"; Check: not WebView2Installed; Flags: waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
function WebView2Installed: Boolean;
var
  pv: string;
begin
  Result := False;
  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', pv) then
    Result := (pv <> '') and (pv <> '0.0.0.0');
  if not Result then
    if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', pv) then
      Result := (pv <> '') and (pv <> '0.0.0.0');
  if not Result then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', pv) then
      Result := (pv <> '') and (pv <> '0.0.0.0');
end;
