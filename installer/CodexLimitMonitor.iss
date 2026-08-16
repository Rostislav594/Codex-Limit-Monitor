#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#define AppName "Codex Limit Monitor"
#define AppExecutable "CodexLimitMonitor.exe"
#define PublishRoot AddBackslash(SourcePath) + "..\artifacts\publish\win-x64"
#define AppBinaryVersion GetVersionNumbersString(PublishRoot + "\" + AppExecutable)

[Setup]
AppId={{D9EB6989-E2D4-43CE-8E9F-8830FDB7B9D8}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
AppCopyright=Copyright (C) 2026 Codex Limit Monitor contributors
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir={#SourcePath}\..\artifacts\installer
OutputBaseFilename=CodexLimitMonitor-{#AppVersion}-win-x64-setup
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExecutable}
VersionInfoVersion={#AppBinaryVersion}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription={#AppName} installer
CloseApplications=yes
CloseApplicationsFilter={#AppExecutable}
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExecutable}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExecutable}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExecutable}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\CodexLimitMonitor"; ValueType: dword; ValueName: "InstallerShutdownProtocol"; ValueData: 1; Flags: uninsdeletevalue uninsdeletekeyifempty

[UninstallRun]
Filename: "{app}\{#AppExecutable}"; Parameters: "--shutdown-for-update"; WorkingDir: "{app}"; RunOnceId: "ShutdownForUninstall"; Flags: runhidden waituntilterminated skipifdoesntexist

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExecutablePath: String;
  ResultCode: Integer;
begin
  Result := '';
  ExecutablePath := ExpandConstant('{app}\{#AppExecutable}');
  if FileExists(ExecutablePath) and
     RegValueExists(
       HKCU,
       'Software\CodexLimitMonitor',
       'InstallerShutdownProtocol') then
  begin
    if not Exec(
      ExecutablePath,
      '--shutdown-for-update',
      ExpandConstant('{app}'),
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode) then
      Result := 'Не удалось подготовить запущенное приложение к обновлению.'
    else if ResultCode <> 0 then
      Result := 'Приложение не завершилось вовремя. Закройте его и повторите установку.';
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(
      HKCU,
      'Software\Microsoft\Windows\CurrentVersion\Run',
      'Codex Limit Monitor');
end;
