; The FanPilot installer (item 14c), compiled by scripts\package.ps1 -Installer from the same files as the release zip:
;   ISCC.exe /DAppVersion=0.1.0 /DSourceDir=<the zip's files> /O<dist> installer\FanPilot.iss
; Installs in Program Files and then does what the portable version leaves to scripts: the PawnIO driver (offered,
; checked only without Gigabyte Control Center), the firewall rule (Private profile only), and scripts\autostart.ps1,
; which registers the service (before logon, restarted if it dies, data in %ProgramData%\FanPilot), the Start menu
; shortcut and the tray icon at every logon. An update stops the service and the tray icons, swaps the files and runs
; autostart.ps1 again, the data untouched. Uninstalling undoes all of it and asks whether the data goes too.
#ifndef AppVersion
  #error Pass /DAppVersion=x.y.z (scripts\package.ps1 -Installer does)
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<folder with FanPilot.exe> (scripts\package.ps1 -Installer does)
#endif

[Setup]
AppId={{6F0C3B2E-8D1A-4E57-9B6C-FA27C0D1E8A4}
AppName=FanPilot
AppVersion={#AppVersion}
AppVerName=FanPilot {#AppVersion}
AppPublisher=FanPilot
AppPublisherURL=https://github.com/mateusconrad/FanPilot
AppSupportURL=https://github.com/mateusconrad/FanPilot/issues
DefaultDirName={autopf}\FanPilot
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputBaseFilename=FanPilot-{#AppVersion}-win-x64-setup
SetupIconFile=..\assets\brand\fanpilot.ico
UninstallDisplayIcon={app}\FanPilot.exe
UninstallDisplayName=FanPilot
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; The service and the tray icons are stopped by PrepareToInstall below, not by the Restart Manager.
CloseApplications=no
RestartApplications=no
LicenseFile={#SourceDir}\LICENSE

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[CustomMessages]
en.PawnIoTask=Install the PawnIO driver (LibreHardwareMonitor uses it to control the fans on boards without Gigabyte Control Center)
ptbr.PawnIoTask=Instalar o driver PawnIO (o LibreHardwareMonitor o usa para controlar os fans nas placas sem o Gigabyte Control Center)
en.PawnIoGroup=Motherboard access:
ptbr.PawnIoGroup=Acesso à placa-mãe:
en.InstallingPawnIo=Installing the PawnIO driver (winget)...
ptbr.InstallingPawnIo=Instalando o driver PawnIO (winget)...
en.Registering=Registering the FanPilot service...
ptbr.Registering=Registrando o serviço do FanPilot...
en.RegisterFailed=The FanPilot service could not be registered (code %1). The details are in %2.
ptbr.RegisterFailed=Não foi possível registrar o serviço do FanPilot (código %1). Os detalhes estão em %2.
en.OpenPair=Pair a phone now
ptbr.OpenPair=Parear um celular agora
en.DeleteData=Also delete FanPilot's data (curves, fan names, paired phones and history) in %1?
ptbr.DeleteData=Apagar também os dados do FanPilot (curvas, nomes dos fans, celulares pareados e histórico) em %1?

[Tasks]
; Two entries with one text: checked when there is no Gigabyte Control Center, unchecked when there is (its own
; driver does the job). Neither shows once PawnIO is installed.
Name: "pawnio"; Description: "{cm:PawnIoTask}"; GroupDescription: "{cm:PawnIoGroup}"; Check: PawnIoMissing and not HasGcc
Name: "pawniogcc"; Description: "{cm:PawnIoTask}"; GroupDescription: "{cm:PawnIoGroup}"; Check: PawnIoMissing and HasGcc; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[UninstallDelete]
Type: files; Name: "{app}\install.log"

[Run]
Filename: "http://localhost:{code:Port}/pair"; Description: "{cm:OpenPair}"; Flags: postinstall shellexec nowait skipifsilent

[Code]
const
  DataDirName = 'FanPilot';

function DataDir: String;
begin
  Result := ExpandConstant('{commonappdata}\') + DataDirName;
end;

function PowerShell: String;
begin
  Result := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
end;

// The board vendor's own route (GigabyteInstall.cs looks at the same uninstall entries).
function HasGcc: Boolean;
begin
  Result := RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\GIGABYTE Control Center')
    or RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MBEasyTune')
    or RegKeyExists(HKLM32, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\GIGABYTE Control Center')
    or RegKeyExists(HKLM32, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MBEasyTune');
end;

function PawnIoMissing: Boolean;
begin
  Result := not RegKeyExists(HKLM64, 'SYSTEM\CurrentControlSet\Services\PawnIO');
end;

// The port in the service's config.json ("port": 8790), 8787 without one: the firewall rule and the pairing page
// follow it, as autostart.ps1 does for the shortcut and the tray.
function Port(Param: String): String;
var
  Text: AnsiString;
  S: String;
  I: Integer;
begin
  Result := '8787';
  if not LoadStringFromFile(DataDir + '\config.json', Text) then Exit;
  S := String(Text);
  I := Pos('"port"', S);
  if I = 0 then Exit;
  S := Copy(S, I + 6, 40);
  I := Pos(':', S);
  if I = 0 then Exit;
  S := Trim(Copy(S, I + 1, 40));
  I := 1;
  while (I <= Length(S)) and (S[I] >= '0') and (S[I] <= '9') do I := I + 1;
  S := Copy(S, 1, I - 1);
  if (S <> '') and (StrToIntDef(S, 0) > 0) and (StrToIntDef(S, 0) < 65536) then Result := S;
end;

// Everything it writes, its error included, goes to install.log; 1 when it threw (the scripts stop at the first error).
function RunPowerShell(const Command, Log: String): Integer;
var
  Code: Integer;
begin
  if not Exec(PowerShell, '-NoProfile -ExecutionPolicy Bypass -Command "try { ' + Command + ' *>> ''' + Log + ''' } '
    + 'catch { $_ *>> ''' + Log + '''; exit 1 }; exit 0"', '', SW_HIDE, ewWaitUntilTerminated, Code) then Code := -1;
  Result := Code;
end;

// An update: the running service holds FanPilot.exe, and every session's tray icon FanPilotTray.exe. Stopping the
// service hands the fans back to the BIOS until autostart.ps1 starts it again with the new files.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Exec(PowerShell, '-NoProfile -Command "Stop-Service -Name FanPilot -Force -ErrorAction SilentlyContinue; '
    + 'Stop-Process -Name FanPilotTray -Force -ErrorAction SilentlyContinue"', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Log: String;
  Code: Integer;
begin
  if CurStep <> ssPostInstall then Exit;
  Log := ExpandConstant('{app}\install.log');
  DeleteFile(Log);
  // Before the service starts, so LibreHardwareMonitor finds it on its first run.
  if WizardIsTaskSelected('pawnio') or WizardIsTaskSelected('pawniogcc') then
  begin
    WizardForm.StatusLabel.Caption := CustomMessage('InstallingPawnIo');
    RunPowerShell('winget install --id namazso.PawnIO --exact --silent --accept-package-agreements --accept-source-agreements', Log);
  end;
  WizardForm.StatusLabel.Caption := CustomMessage('Registering');
  RunPowerShell('& ''' + ExpandConstant('{app}\scripts\firewall.ps1') + ''' -Port ' + Port(''), Log);
  Code := RunPowerShell('& ''' + ExpandConstant('{app}\scripts\autostart.ps1') + '''', Log);
  if Code <> 0 then
    SuppressibleMsgBox(FmtMessage(CustomMessage('RegisterFailed'), [IntToStr(Code), Log]), mbError, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    // While the files are still there: the service (fans back to the BIOS), the tray icons and their logon entry, the
    // shortcut; then every FanPilot firewall rule, whatever its port.
    Exec(PowerShell, '-NoProfile -ExecutionPolicy Bypass -Command "& ''' + ExpandConstant('{app}\scripts\autostart.ps1') + ''' -Remove"',
      '', SW_HIDE, ewWaitUntilTerminated, Code);
    Exec(PowerShell, '-NoProfile -Command "Get-NetFirewallRule -DisplayName ''FanPilot (TCP *)'' -ErrorAction SilentlyContinue | Remove-NetFirewallRule"',
      '', SW_HIDE, ewWaitUntilTerminated, Code);
  end;
  // A silent uninstall keeps the data, as an update would need it.
  if (CurUninstallStep = usPostUninstall) and DirExists(DataDir) and not UninstallSilent then
    if MsgBox(FmtMessage(CustomMessage('DeleteData'), [DataDir]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(DataDir, True, True, True);
end;
