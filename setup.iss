#ifndef AppVersion
  #define AppVersion "1.6.2-beta"
#endif

#if Copy(AppVersion, 1, 1) == "v"
  #define NormalizedAppVersion Copy(AppVersion, 2)
#else
  #define NormalizedAppVersion AppVersion
#endif

#define PublishedExecutable SourcePath + "\src\publish_full\BASpark.exe"
#if FileExists(PublishedExecutable)
  #define PublishedAppVersion GetStringFileInfo(PublishedExecutable, "ProductVersion")
  #if PublishedAppVersion != NormalizedAppVersion
    #pragma error "Published BASpark.exe ProductVersion (" + PublishedAppVersion + ") does not match installer AppVersion (" + NormalizedAppVersion + ")"
  #endif
#endif

[Setup]
AppId={{B0A2C1D4-E3F5-4A6B-9C8D-7E1F2A3B4C5D}}
AppName=BASpark
AppVersion={#AppVersion}
AppPublisher="BASpark Project"
DefaultDirName={autopf}\BASpark
DefaultGroupName=BASpark
AllowNoIcons=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
AppMutex=Global\BASpark_SingleInstance_Mutex
CloseApplications=yes
SetupIconFile=src\app.ico
UninstallDisplayIcon={app}\BASpark.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
OutputDir=dist
OutputBaseFilename=BASpark_Installer_{#AppVersion}_x64
ShowLanguageDialog=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "japanese"; MessagesFile: "Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "src\publish_full\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\BASpark"; Filename: "{app}\BASpark.exe"
Name: "{autodesktop}\BASpark"; Filename: "{app}\BASpark.exe"; Tasks: desktopicon

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM BASpark.exe /T"; Flags: runhidden; RunOnceId: "StopBASpark"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN BASparkAutoStart /F"; Flags: runhidden; RunOnceId: "RemoveBASparkAutoStartTask"

[Run]
Filename: "{app}\BASpark.exe"; Description: "{cm:LaunchProgram,BASpark}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CleanupInstalledAutoStartEntry;
var
  UserHives: TArrayOfString;
  I: Integer;
  RunKey: String;
  RunCommand: String;
  ExpectedCommand: String;
begin
  RunKey := 'Software\Microsoft\Windows\CurrentVersion\Run';
  ExpectedCommand := '"' + ExpandConstant('{app}\BASpark.exe') + '" --autostart';

  if not RegGetSubkeyNames(HKEY_USERS, '', UserHives) then
    Exit;

  for I := 0 to GetArrayLength(UserHives) - 1 do
    if RegQueryStringValue(HKEY_USERS, UserHives[I] + '\' + RunKey, 'BASpark', RunCommand) and
       (CompareText(RunCommand, ExpectedCommand) = 0) then
    begin
      RegDeleteValue(HKEY_USERS, UserHives[I] + '\' + RunKey, 'BASpark');
      Log('Removed BASpark auto-start entry for user hive ' + UserHives[I]);
    end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    CleanupInstalledAutoStartEntry;
end;
