; Build.ps1 supplies verified payload, version, and prerequisite paths.
#ifndef PayloadDir
  #error Build this installer with Installer/Build.ps1.
#endif

[Setup]
AppId={{B8AD941B-5D52-4BDA-B744-80586E33E3E1}
AppName=Q-SYS Mod Manager
AppVersion={#AppVersion}
AppPublisher=Joshua Small
AppPublisherURL=https://github.com/joshua-small/qsys-mod-manager
AppSupportURL=https://github.com/joshua-small/qsys-mod-manager/issues
AppUpdatesURL=https://github.com/joshua-small/qsys-mod-manager/releases
DefaultDirName={code:DefaultDirectory}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
UninstallDisplayIcon={app}\Q-SYS Mod Manager.exe
UninstallDisplayName=Q-SYS Mod Manager
SetupIconFile=..\App\Assets\qmm.ico
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
OutputDir={#OutputDir}
OutputBaseFilename=Q-SYS-Mod-Manager-Setup-{#AppVersion}-win-x64
AppMutex=Global\QsysModManager.Application
SetupMutex=Global\QsysModManager.Setup
CloseApplications=no
RestartApplications=no
RestartIfNeededByRun=no
#ifdef SigningCommand
SignTool=qmm
SignedUninstaller=yes
SignedUninstallerDir={#OutputDir}\signed-uninstaller
#endif

[Tasks]
Name: desktopicon; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#RuntimeFile}"; DestName: "windowsdesktop-runtime.exe"; Flags: dontcopy noencryption nocompression
Source: "{#PayloadDir}\Q-SYS Mod Manager.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\Q-SYS Mod Manager.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\Q-SYS Mod Manager.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\Q-SYS Mod Manager.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\QdsModManager.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\installed-files.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{commonprograms}\Q-SYS Mod Manager"; Filename: "{app}\Q-SYS Mod Manager.exe"; WorkingDir: "{app}"
Name: "{commondesktop}\Q-SYS Mod Manager"; Filename: "{app}\Q-SYS Mod Manager.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Q-SYS Mod Manager.exe"; Description: "Open Q-SYS Mod Manager"; Flags: postinstall nowait skipifsilent runasoriginaluser

[Code]
const
  RegistrationKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{B8AD941B-5D52-4BDA-B744-80586E33E3E1}_is1';
  RuntimeKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
var
  RuntimeRestart: Boolean;

function GetFileAttributes(Path: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function DefaultDirectory(Param: String): String;
begin
  Result := ExpandConstant('{autopf}\Q-SYS Mod Manager');
  { Adopt the pre-installer location in place, retaining packages and backups. }
  if FileExists(ExpandConstant('{autopf}\QSC\QDS Mod Manager\Q-SYS Mod Manager.dll')) or
     FileExists(ExpandConstant('{autopf}\QSC\QDS Mod Manager\QDS Mod Manager.dll')) then
    Result := ExpandConstant('{autopf}\QSC\QDS Mod Manager');
end;

function RuntimeReady: Boolean;
var
  Names: TArrayOfString;
  I: Integer;
  Root: String;
begin
  Result := False;
  { .NET's installed-version keys are in the 32-bit registry view. }
  if not RegQueryStringValue(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64', 'InstallLocation', Root) then Exit;
  if RegGetValueNames(HKLM32, RuntimeKey, Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if (Copy(Names[I], 1, 5) = '10.0.') and
         (StrToIntDef(Copy(Names[I], 6, Length(Names[I])), -1) >= {#RuntimePatch}) and
         FileExists(AddBackslash(Root) + 'shared\Microsoft.WindowsDesktop.App\' + Names[I] + '\PresentationFramework.dll') and
         FileExists(AddBackslash(Root) + 'shared\Microsoft.NETCore.App\' + Names[I] + '\coreclr.dll') then
        Result := True;
end;

function SafePath(Path: String): Boolean;
var
  Parent: String;
  Attributes: LongWord;
begin
  Result := False;
  while Path <> '' do begin
    Attributes := GetFileAttributes(Path);
    if (Attributes <> $FFFFFFFF) and ((Attributes and $400) <> 0) then Exit;
    Parent := ExtractFileDir(RemoveBackslashUnlessRoot(Path));
    if CompareText(Parent, Path) = 0 then Break;
    Path := Parent;
  end;
  Result := True;
end;

function TargetProblem: String;
var
  Target, Registered, Stem: String;
  HighVersion, LowVersion: Cardinal;
  I: Integer;
  Found: TFindRec;
  Probe: TFileStream;
begin
  Result := '';
  Target := RemoveBackslashUnlessRoot(ExpandConstant('{app}'));
  if not SafePath(Target) then begin Result := 'Linked installation folders are not supported.'; Exit; end;
  if (Length(Target) <= 3) or (CompareText(Target, ExpandConstant('{win}')) = 0) or
     (CompareText(Target, ExpandConstant('{autopf}')) = 0) or FileExists(Target + '\Q-Sys Designer.dll') then begin
    Result := 'Choose a separate folder for Q-SYS Mod Manager.'; Exit;
  end;
  if RegQueryStringValue(HKLM64, RegistrationKey, 'InstallLocation', Registered) and
     (CompareText(RemoveBackslashUnlessRoot(Registered), Target) <> 0) then begin
    Result := 'Q-SYS Mod Manager is already installed in ' + Registered + '. Upgrade it there, or uninstall it before choosing another folder.'; Exit;
  end;
  if FindFirst(Target + '\*', Found) then begin
    try
      repeat
        if (Found.Name <> '.') and (Found.Name <> '..') and not SafePath(Target + '\' + Found.Name) then begin
          Result := 'Linked files or folders were found in the installation folder.'; Exit;
        end;
        if (CompareText(ExtractFileExt(Found.Name), '.exe') = 0) or
           (CompareText(ExtractFileExt(Found.Name), '.dll') = 0) then begin
          try
            Probe := TFileStream.Create(Target + '\' + Found.Name, fmOpenRead or fmShareExclusive);
            Probe.Free;
          except
            Result := 'Close Q-SYS Mod Manager before continuing. An application file is in use.'; Exit;
          end;
        end;
      until not FindNext(Found);
    finally FindClose(Found); end;
  end;
  for I := 0 to 1 do begin
    if I = 0 then Stem := 'Q-SYS Mod Manager' else Stem := 'QDS Mod Manager';
    if GetVersionNumbers(Target + '\' + Stem + '.dll', HighVersion, LowVersion) then
      if (HighVersion > {#VersionHigh}) or ((HighVersion = {#VersionHigh}) and (LowVersion > {#VersionLow})) then begin
        Result := 'A newer Q-SYS Mod Manager is already installed. Download the latest Setup to repair it.'; Exit;
      end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := TargetProblem;
  if Result <> '' then Exit;
  if RuntimeReady then begin Log('Compatible .NET 10 Desktop Runtime already installed.'); Exit; end;
  Log('Installing bundled Microsoft .NET Desktop Runtime {#RuntimeVersion}.');
  ExtractTemporaryFile('windowsdesktop-runtime.exe');
  if not Exec(ExpandConstant('{tmp}\windowsdesktop-runtime.exe'), '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then begin
    Result := 'Could not start the .NET Desktop Runtime installer. No manager files were changed.'; Exit;
  end;
  if (ExitCode <> 0) and (ExitCode <> 3010) then begin
    Result := 'The .NET Desktop Runtime installer returned code ' + IntToStr(ExitCode) + '. No manager files were changed.'; Exit;
  end;
  RuntimeRestart := ExitCode = 3010;
  if not RuntimeReady then begin
    NeedsRestart := RuntimeRestart;
    Result := 'The .NET Desktop Runtime is not ready. Restart Windows if requested, then run Setup again.';
  end;
end;

function NeedRestart: Boolean;
begin
  Result := RuntimeRestart;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  I: Integer;
  Suffixes: TArrayOfString;
begin
  if CurStep = ssPostInstall then begin
    { Only remove the four obsolete product filenames after successful install. }
    Suffixes := ['.exe', '.dll', '.deps.json', '.runtimeconfig.json'];
    for I := 0 to GetArrayLength(Suffixes) - 1 do
      if not DeleteFile(ExpandConstant('{app}\QDS Mod Manager') + Suffixes[I]) and
         FileExists(ExpandConstant('{app}\QDS Mod Manager') + Suffixes[I]) then
        RaiseException('Could not remove an obsolete manager file. Close Q-SYS Mod Manager and run Setup again.');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Users: TArrayOfString;
  I: Integer;
  Key, Command, Expected: String;
begin
  if CurUninstallStep <> usUninstall then Exit;
  Expected := '"' + ExpandConstant('{app}\Q-SYS Mod Manager.exe') + '" --background';
  { A machine install can be used by several signed-in users. Only remove this
    installation's exact value; portable copies and other startup entries stay.
    Unloaded user hives are deliberately not mounted or changed. }
  if RegGetSubkeyNames(HKU, '', Users) then
    for I := 0 to GetArrayLength(Users) - 1 do begin
      Key := Users[I] + '\Software\Microsoft\Windows\CurrentVersion\Run';
      if RegQueryStringValue(HKU, Key, 'QsysModManager', Command) and
         (CompareText(Command, Expected) = 0) then
        if not RegDeleteValue(HKU, Key, 'QsysModManager') then
          Log('Could not remove the QMM startup entry for ' + Users[I]);
    end;
end;
