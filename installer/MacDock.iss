#define AppVersion "0.5.1"
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
function FirewallRuleName: String;
begin
  Result := 'MacDock FileShare LAN ' + GetSHA256OfString(Lowercase(ExpandConstant('{app}\MacDock.Wpf.exe')));
end;

function HasFirewallRule: Boolean;
var Policy, Rules, Rule: Variant;
begin
  Policy := CreateOleObject('HNetCfg.FwPolicy2');
  Rules := Policy.Rules;
  try
    Rule := Rules.Item(FirewallRuleName);
    Result := True;
  except
    Result := False;
  end;
end;

procedure ConfigureFirewall(Installing: Boolean);
var Args, Match, Options, Verb: String;
    Code, Port: Integer;
begin
  Match := 'name="' + FirewallRuleName + '" program="' + ExpandConstant('{app}\MacDock.Wpf.exe') + '"';
  if Installing then begin
    Port := GetIniInt('server', 'Port', 8787, 1, 65535, ExpandConstant('{localappdata}\MacDock\FileShare\FileShare.ini'));
    Options := 'action=allow enable=yes profile=public,private protocol=TCP localport=' +
      IntToStr(Port) + ' remoteip=LocalSubnet edge=no';
    if HasFirewallRule then
      Args := 'set rule ' + Match + ' dir=in new ' + Options
    else
      Args := 'add rule ' + Match + ' dir=in ' + Options;
  end else begin
    if not HasFirewallRule then Exit;
    Args := 'delete rule ' + Match + ' dir=in';
  end;
  Verb := 'runas';
  if IsAdmin then Verb := 'open';
  if not ShellExec(Verb, ExpandConstant('{sys}\netsh.exe'), 'advfirewall firewall ' + Args,
      ExpandConstant('{sys}'), SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
    RaiseException(Format('MacDock LAN firewall configuration failed (%d).', [Code]));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then ConfigureFirewall(True);
  if (CurStep = ssPostInstall) and RegValueExists(HKCU, RunKey, 'MacDock.Wpf') then
    RegWriteStringValue(HKCU, RunKey, 'MacDock.Wpf', '"' + ExpandConstant('{app}\MacDock.Wpf.exe') + '"');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Command: String;
begin
  if CurUninstallStep = usUninstall then begin
    ConfigureFirewall(False);
    if RegQueryStringValue(HKCU, RunKey, 'MacDock.Wpf', Command) then
      if CompareText(Command, '"' + ExpandConstant('{app}\MacDock.Wpf.exe') + '"') = 0 then
        RegDeleteValue(HKCU, RunKey, 'MacDock.Wpf');
    if RegQueryStringValue(HKCU, RunKey, 'MacDock.FileShare', Command) then
      if CompareText(Command, '"' + ExpandConstant('{app}\MacDock.Wpf.exe') + '"') = 0 then
        RegDeleteValue(HKCU, RunKey, 'MacDock.FileShare');
  end;
end;
