#define AppVersion "0.4.5"
[Setup]
AppId={{CC77C483-91CF-47D6-B644-93B47E1D7C84}
AppName=MacDock
AppVersion={#AppVersion}
AppPublisher=MacDock
AppPublisherURL=https://github.com/sjhdeux0114/MacDock
DefaultDirName={localappdata}\Programs\MacDock
DefaultGroupName=MacDock
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\wpf\publish\v{#AppVersion}\installer
OutputBaseFilename=MacDock-v{#AppVersion}-win-x64-Setup
SetupIconFile=..\wpf\Assets\macdock.ico
UninstallDisplayIcon={app}\MacDock.Wpf.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\wpf\publish\v{#AppVersion}\standalone\MacDock.Wpf.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\MacDock"; Filename: "{app}\MacDock.Wpf.exe"
Name: "{autodesktop}\MacDock"; Filename: "{app}\MacDock.Wpf.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\MacDock.Wpf.exe"; Description: "Launch MacDock"; Flags: nowait postinstall skipifsilent

[Code]
const RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and RegValueExists(HKCU, RunKey, 'MacDock.Wpf') then
    RegWriteStringValue(HKCU, RunKey, 'MacDock.Wpf', '"' + ExpandConstant('{app}\MacDock.Wpf.exe') + '"');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Command: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, RunKey, 'MacDock.Wpf', Command) then
      if CompareText(Command, '"' + ExpandConstant('{app}\MacDock.Wpf.exe') + '"') = 0 then
        RegDeleteValue(HKCU, RunKey, 'MacDock.Wpf');
end;
