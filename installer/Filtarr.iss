; Inno Setup 6 script for the Filtarr installer. Build it with scripts\Build-Installer.ps1 (which publishes a self-contained
; release first and passes AppVersion / SourceDir / OutputDir). The wizard asks for the install folder, whether Filtarr runs as
; a Windows service or is started manually, and the web UI port.
;
; Unattended install: Filtarr-Setup-x.y.z.exe /VERYSILENT /SUPPRESSMSGBOXES /DIR="C:\Filtarr" /PORT=5080 /SERVICE=1 /TASKS="firewall"

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #error SourceDir is required: build with scripts\Build-Installer.ps1
#endif
#ifndef OutputDir
  #define OutputDir "output"
#endif
#define AppName "Filtarr"
#define AppExe "Filtarr.Api.exe"

[Setup]
; The AppId lets a newer installer upgrade an existing install in place (same folder, same settings).
AppId={{6B0F2C4E-7A31-4D8B-9C52-1E4A8D3F7B90}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppName}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Registering a service, the firewall rule and Program Files all need administrator rights.
PrivilegesRequired=admin
; Manual mode uses per-user folders (AppData, Startup). With an admin install those resolve to the account that elevated, which is
; the person running the installer in the normal case.
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename=Filtarr-Setup-{#AppVersion}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
; Close a running manual instance (and let setup restart it) instead of failing on locked files.
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut that opens Filtarr"; Flags: unchecked
Name: "startup"; Description: "Start Filtarr when I sign in to Windows (manual mode only; the service always starts with Windows)"; Flags: unchecked
; Off by default: Filtarr has no login, so opening the port lets anyone on your network use it.
Name: "firewall"; Description: "Allow other devices on my network to reach Filtarr (adds a Windows Firewall rule for the port)"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "appsettings.json"; Flags: ignoreversion recursesubdirs createallsubdirs
; The settings file is only written the first time and survives upgrades and uninstalls (it holds the port and data folder).
Source: "{#SourceDir}\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall

[INI]
Filename: "{app}\Open Filtarr.url"; Section: "InternetShortcut"; Key: "URL"; String: "{code:GetUrl}"

[Icons]
Name: "{group}\Open Filtarr"; Filename: "{app}\Open Filtarr.url"
Name: "{group}\Start Filtarr"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Check: not IsServiceMode
Name: "{group}\Uninstall Filtarr"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Filtarr"; Filename: "{app}\Open Filtarr.url"; Tasks: desktopicon
Name: "{userstartup}\Filtarr"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: startup; Check: not IsServiceMode

[Run]
Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Description: "Start Filtarr now"; Flags: nowait postinstall skipifsilent; Check: not IsServiceMode
Filename: "{app}\Open Filtarr.url"; Description: "Open Filtarr in my browser"; Flags: shellexec nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop Filtarr"; Flags: runhidden waituntilterminated; RunOnceId: "StopService"
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden waituntilterminated; RunOnceId: "KillApp"
Filename: "{sys}\sc.exe"; Parameters: "delete Filtarr"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteService"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Filtarr"""; Flags: runhidden waituntilterminated; RunOnceId: "DeleteFirewallRule"

[Code]
const
  ServiceName = 'Filtarr';
  DefaultPort = 5080;
  // Exact lines in the shipped appsettings.json that a fresh install rewrites; Build-Installer.ps1 checks they still exist.
  PortLine = '"Port": 5080';
  DataDirLine = '"DataDirectory": "%LOCALAPPDATA%\\Filtarr"';

var
  ModePage: TInputOptionWizardPage;
  PortPage: TInputQueryWizardPage;
  FreshConfig: Boolean;

function ConfigPath: String;
begin
  Result := AddBackslash(WizardDirValue) + 'appsettings.json';
end;

function RunHidden(const Exe, Params: String; var ResultCode: Integer): Boolean;
begin
  Result := Exec(Exe, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function ServiceExists: Boolean;
var
  Code: Integer;
begin
  Result := RunHidden(ExpandConstant('{sys}\sc.exe'), 'query ' + ServiceName, Code) and (Code = 0);
end;

function IsServiceMode: Boolean;
begin
  Result := (ModePage <> nil) and ModePage.Values[0];
end;

// The port written into appsettings.json by an earlier install.
function ReadConfiguredPort: Integer;
var
  Json: AnsiString;
  S: String;
  P, Q: Integer;
begin
  Result := DefaultPort;
  if LoadStringFromFile(ConfigPath, Json) then
  begin
    S := String(Json);
    P := Pos('"Port":', S);
    if P > 0 then
    begin
      Delete(S, 1, P + 6);
      S := Trim(S);
      Q := 1;
      while (Q <= Length(S)) and (S[Q] >= '0') and (S[Q] <= '9') do
        Q := Q + 1;
      Result := StrToIntDef(Copy(S, 1, Q - 1), DefaultPort);
    end;
  end;
end;

function GetPort: Integer;
begin
  if FreshConfig then
    Result := StrToIntDef(PortPage.Values[0], DefaultPort)
  else
    Result := ReadConfiguredPort;
end;

function GetUrl(Param: String): String;
begin
  Result := 'http://localhost:' + IntToStr(GetPort) + '/';
end;

// Service: machine-wide ProgramData (like Sonarr/Radarr). Manual: the signed-in user's AppData.
function GetDataDir: String;
begin
  if IsServiceMode then
    Result := ExpandConstant('{commonappdata}\Filtarr')
  else
    Result := ExpandConstant('{userappdata}\Filtarr');
end;

procedure InitializeWizard;
begin
  ModePage := CreateInputOptionPage(wpSelectDir, 'Startup mode', 'How should Filtarr run?',
    'Choose how Filtarr should start:', True, False);
  ModePage.Add('Install as a Windows service (recommended) - runs in the background and starts with Windows');
  ModePage.Add('Start manually - run Filtarr from the Start menu when you need it');
  // Keep the existing choice when upgrading; new installs default to the service.
  if ServiceExists or (ExpandConstant('{param:SERVICE|1}') <> '0') then
    ModePage.Values[0] := True
  else
    ModePage.Values[1] := True;

  PortPage := CreateInputQueryPage(ModePage.ID, 'Web interface port', 'Which port should Filtarr listen on?',
    'Filtarr serves its web interface on this port. You can change it later in appsettings.json.');
  PortPage.Add('Port:', False);
  PortPage.Values[0] := ExpandConstant('{param:PORT|5080}');
end;

// An upgrade keeps the existing appsettings.json, so the port is not asked again.
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = PortPage.ID) and FileExists(ConfigPath);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Port: Integer;
begin
  Result := True;
  if CurPageID = PortPage.ID then
  begin
    Port := StrToIntDef(Trim(PortPage.Values[0]), 0);
    if (Port < 1) or (Port > 65535) then
    begin
      MsgBox('Enter a port number between 1 and 65535.', mbError, MB_OK);
      Result := False;
    end
    else
      PortPage.Values[0] := IntToStr(Port);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Result := '';
  FreshConfig := not FileExists(ConfigPath);
  // Release the file locks on the executable before the files are replaced; net stop waits for the service to stop.
  if ServiceExists then
    RunHidden(ExpandConstant('{sys}\net.exe'), 'stop ' + ServiceName, Code);
end;

procedure WriteFreshConfig;
var
  Json: AnsiString;
  S, Dir: String;
begin
  if not LoadStringFromFile(ConfigPath, Json) then
    RaiseException('Could not read ' + ConfigPath);
  S := String(Json);
  Dir := GetDataDir;
  StringChangeEx(Dir, '\', '\\', True);
  if (StringChangeEx(S, PortLine, '"Port": ' + IntToStr(GetPort), True) = 0) or
     (StringChangeEx(S, DataDirLine, '"DataDirectory": "' + Dir + '"', True) = 0) then
    RaiseException('appsettings.json does not have the expected layout.');
  if not SaveStringToFile(ConfigPath, AnsiString(S), False) then
    RaiseException('Could not write ' + ConfigPath);
end;

procedure ConfigureService;
var
  Sc, Exe: String;
  Code: Integer;
begin
  Sc := ExpandConstant('{sys}\sc.exe');
  Exe := ExpandConstant('{app}\{#AppExe}');

  if IsServiceMode then
  begin
    // NetworkService is a low-privilege built-in account; it only needs to write the data folder.
    ForceDirectories(GetDataDir);
    RunHidden(ExpandConstant('{sys}\icacls.exe'), '"' + GetDataDir + '" /grant *S-1-5-20:(OI)(CI)M', Code);

    if ServiceExists then
      RunHidden(Sc, 'config ' + ServiceName + ' binPath= "\"' + Exe + '\"" start= auto obj= "NT AUTHORITY\NetworkService"', Code)
    else
      RunHidden(Sc, 'create ' + ServiceName + ' binPath= "\"' + Exe + '\"" start= auto obj= "NT AUTHORITY\NetworkService" DisplayName= "Filtarr"', Code);
    if Code <> 0 then
      RaiseException('Could not register the Filtarr service (sc.exe exit code ' + IntToStr(Code) + ').');
    RunHidden(Sc, 'description ' + ServiceName + ' "Rule-based episode monitoring for Sonarr"', Code);
    // Restart after a crash (5 s, 5 s, 30 s); the counter resets after a day.
    RunHidden(Sc, 'failure ' + ServiceName + ' reset= 86400 actions= restart/5000/restart/5000/restart/30000', Code);
  end
  else if ServiceExists then
  begin
    // Switched from service to manual on an upgrade.
    RunHidden(Sc, 'delete ' + ServiceName, Code);
  end;
end;

procedure ConfigureFirewall;
var
  Code: Integer;
  Netsh: String;
begin
  Netsh := ExpandConstant('{sys}\netsh.exe');
  RunHidden(Netsh, 'advfirewall firewall delete rule name="Filtarr"', Code);
  if WizardIsTaskSelected('firewall') then
    RunHidden(Netsh, 'advfirewall firewall add rule name="Filtarr" dir=in action=allow protocol=TCP localport=' + IntToStr(GetPort), Code);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    if FreshConfig then
      WriteFreshConfig;
    ConfigureService;
    ConfigureFirewall;
    if IsServiceMode then
    begin
      RunHidden(ExpandConstant('{sys}\net.exe'), 'start ' + ServiceName, Code);
      if Code <> 0 then
        MsgBox('Filtarr was installed but the service did not start (net start exit code ' + IntToStr(Code) + ').' + #13#10 +
          'Check the logs in ' + GetDataDir + '\Logs.', mbError, MB_OK);
    end;
  end;
end;
