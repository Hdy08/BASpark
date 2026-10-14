#ifndef AppVersion
  #define AppVersion "0.0.1-dev"
#endif

#if Copy(AppVersion, 1, 1) == "v"
  #define NormalizedAppVersion Copy(AppVersion, 2)
#else
  #define NormalizedAppVersion AppVersion
#endif

#ifndef PublishedSource
  #ifdef UiAccessCertificateThumbprint
    #define PublishedSource "src\publish_uiaccess"
  #else
    #define PublishedSource "src\publish_full"
  #endif
#endif

#if Pos(":", PublishedSource) > 0
  #define PublishedSourceRoot PublishedSource
#else
  #define PublishedSourceRoot SourcePath + "\" + PublishedSource
#endif

#define PublishedExecutable AddBackslash(PublishedSourceRoot) + "BASpark.exe"
#if FileExists(PublishedExecutable)
  #define PublishedAppVersion GetStringFileInfo(PublishedExecutable, "ProductVersion")
  #if PublishedAppVersion != NormalizedAppVersion
    #pragma error "Published BASpark.exe ProductVersion (" + PublishedAppVersion + ") does not match installer AppVersion (" + NormalizedAppVersion + ")"
  #endif
#endif

#ifdef UiAccessCertificateThumbprint
  #define UiAccessCertificateFile AddBackslash(PublishedSourceRoot) + "BASpark.UIAccess.cer"
  #if !FileExists(UiAccessCertificateFile)
    #pragma error "UIAccess certificate file is missing: " + UiAccessCertificateFile
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
AppMutex={code:GetBASparkAppMutex}
CloseApplications=no
SetupIconFile=src\app.ico
UninstallDisplayIcon={app}\BASpark.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
OutputDir=dist
OutputBaseFilename=BASpark_Installer_{#AppVersion}_x64
ShowLanguageDialog=no
DisableStartupPrompt=yes
#ifdef UiAccessCertificateThumbprint
UsePreviousAppDir=no
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "japanese"; MessagesFile: "Japanese.isl"

[Messages]
english.SetupAppRunningError=Setup has detected that %1 is currently running.%n%nClick OK to close it and continue installation, or Cancel to exit.
chinesesimplified.SetupAppRunningError=安装程序发现 %1 当前正在运行。%n%n点击“确定”关闭正在运行的程序并继续安装，或点击“取消”退出。
japanese.SetupAppRunningError=%1 は現在実行中です。%n%n「OK」をクリックするとアプリケーションを終了してインストールを続行します。「キャンセル」をクリックすると終了します。

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[CustomMessages]
CloseRunningBASparkFailed=Setup could not close BASpark. Close it completely from the system tray, then run Setup again.
chinesesimplified.CloseRunningBASparkFailed=安装程序无法关闭 BASpark。请从系统托盘彻底退出程序，然后重新运行安装程序。
japanese.CloseRunningBASparkFailed=BASpark を終了できませんでした。システムトレイから完全に終了し、インストーラーを再実行してください。
#ifdef UiAccessCertificateThumbprint
UiAccessCertificateConsent=Trust the certificate used only for BASpark to allow the effect overlay to display at the UIAccess level
UiAccessCertificateRequired=Certificate trust is required for the UIAccess build. Setup made no changes.
UiAccessCertificateSilentRequired=Silent installation requires /ACCEPTUIACCESSCERT=1.
UiAccessCertificateInstallFailed=Failed to trust the BASpark UIAccess signing certificate. Setup made no application changes.
UiAccessSecureLocationRequired=The UIAccess build must be installed inside Program Files.
UiAccessPreviousLocationRequired=An existing BASpark installation was found outside Program Files\BASpark. Uninstall it before installing the UIAccess build.
chinesesimplified.UiAccessCertificateConsent=信任仅用于 BASpark 的证书，以允许特效叠加层以 UIAccess 层级显示
chinesesimplified.UiAccessCertificateRequired=UIAccess 版本必须信任代码签名证书，安装程序尚未修改应用文件。
chinesesimplified.UiAccessCertificateSilentRequired=静默安装必须添加 /ACCEPTUIACCESSCERT=1。
chinesesimplified.UiAccessCertificateInstallFailed=无法信任 BASpark UIAccess 签名证书，安装程序尚未修改应用文件。
chinesesimplified.UiAccessSecureLocationRequired=UIAccess 版本必须安装在 Program Files 目录内。
chinesesimplified.UiAccessPreviousLocationRequired=检测到 BASpark 旧版本安装在 Program Files\BASpark 之外。请先卸载旧版本，再安装 UIAccess 版本。
japanese.UiAccessCertificateConsent=BASpark 専用の証明書を信頼し、エフェクトのオーバーレイを UIAccess レベルで表示できるようにする
japanese.UiAccessCertificateRequired=UIAccess 版ではコード署名証明書の信頼が必要です。アプリケーションファイルは変更されていません。
japanese.UiAccessCertificateSilentRequired=サイレントインストールには /ACCEPTUIACCESSCERT=1 が必要です。
japanese.UiAccessCertificateInstallFailed=BASpark UIAccess 署名証明書を信頼できませんでした。アプリケーションファイルは変更されていません。
japanese.UiAccessSecureLocationRequired=UIAccess 版は Program Files 内にインストールする必要があります。
japanese.UiAccessPreviousLocationRequired=Program Files\BASpark 以外に既存の BASpark が見つかりました。UIAccess 版をインストールする前に、既存版をアンインストールしてください。
#endif

[Files]
Source: "{#PublishedSource}\*"; DestDir: "{tmp}\BASpark.Setup"; Flags: dontcopy recursesubdirs createallsubdirs; Excludes: "BASpark.UIAccess.cer"
#ifdef UiAccessCertificateThumbprint
Source: "{#PublishedSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "BASpark.UIAccess.cer"
Source: "{#PublishedSource}\BASpark.UIAccess.cer"; Flags: dontcopy
#else
Source: "{#PublishedSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\BASpark"; Filename: "{app}\BASpark.exe"
Name: "{autodesktop}\BASpark"; Filename: "{app}\BASpark.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\BASpark"; ValueType: string; ValueName: "UiLanguage"; ValueData: "{code:GetInstallerCulture}"; Check: IsNativeInstallerWorker
Root: HKCU; Subkey: "Software\BASpark"; ValueType: dword; ValueName: "AgreedToPrivacy"; ValueData: "1"; Check: IsNativeInstallerWorker

[Run]
#ifdef UiAccessCertificateThumbprint
Filename: "{app}\BASpark.exe"; Description: "{cm:LaunchProgram,BASpark}"; Flags: nowait postinstall skipifsilent shellexec
#else
Filename: "{app}\BASpark.exe"; Description: "{cm:LaunchProgram,BASpark}"; Flags: nowait postinstall skipifsilent
#endif

[Code]
const
  BASparkAppMutex = 'Global\BASpark_SingleInstance_Mutex';

var
  PreservedUserDataFile: String;

function GetBASparkAppMutex(Param: String): String;
begin
  Result := '';
end;

function IsNativeInstallerWorker: Boolean;
begin
  Result := ExpandConstant('{param:BASPARKWORKER|0}') = '1';
end;

function GetInstallerCulture(Param: String): String;
begin
  if ActiveLanguage = 'chinesesimplified' then
    Result := 'zh-CN'
  else if ActiveLanguage = 'japanese' then
    Result := 'ja'
  else
    Result := 'en';
end;

procedure WriteNativeProgress(Value: Integer);
var
  StatusFile: String;
begin
  StatusFile := ExpandConstant('{param:BASPARKSTATUS|}');
  if StatusFile <> '' then
    SaveStringToFile(StatusFile, IntToStr(Value), False);
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
begin
  if MaxProgress > 0 then
    WriteNativeProgress(Round(CurProgress * 100.0 / MaxProgress));
end;

function CloseRunningBASpark: Boolean;
var
  Attempt: Integer;
  ResultCode: Integer;
begin
  Result := True;
  if not CheckForMutexes(BASparkAppMutex) then
    Exit;

  Result := False;
  ResultCode := -1;
  if not Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM BASpark.exe /T',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Log('Failed to start taskkill: ' + IntToStr(ResultCode))
  else
    Log('BASpark shutdown exit code: ' + IntToStr(ResultCode));

  for Attempt := 0 to 99 do
  begin
    if not CheckForMutexes(BASparkAppMutex) then
    begin
      Result := True;
      Exit;
    end;
    Sleep(50);
  end;

  Log(ExpandConstant('{cm:CloseRunningBASparkFailed}'));
end;

function InitializeSetup: Boolean;
var
  Parameters: String;
  ResultCode: Integer;
begin
  if WizardSilent then
  begin
    Result := True;
    Exit;
  end;
  Result := False;
  ExtractTemporaryFiles('{tmp}\BASpark.Setup\*');
  Parameters := '--installer --backend="' + ExpandConstant('{srcexe}') + '"';
#ifdef UiAccessCertificateThumbprint
  Parameters := Parameters + ' --uiaccess=1';
#endif
  if not Exec(ExpandConstant('{tmp}\BASpark.Setup\BASpark.Setup.exe'),
       Parameters, '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
    Log('Could not start the WinUI installer: ' + IntToStr(ResultCode));
end;

function CopyInstallerFrontend(SourceDirectory, TargetDirectory: String): Boolean;
var
  Entry: TFindRec;
  SourceFile: String;
  TargetFile: String;
begin
  Result := ForceDirectories(TargetDirectory);
  if not Result then Exit;
  if FindFirst(AddBackslash(SourceDirectory) + '*', Entry) then
  begin
    try
      repeat
        if (Entry.Name = '.') or (Entry.Name = '..') or
           (Pos('unins', Lowercase(Entry.Name)) = 1) or
           ((Entry.Attributes and $400) <> 0) then Continue;
        SourceFile := AddBackslash(SourceDirectory) + Entry.Name;
        TargetFile := AddBackslash(TargetDirectory) + Entry.Name;
        if (Entry.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          Result := CopyInstallerFrontend(SourceFile, TargetFile)
        else
          Result := FileCopy(SourceFile, TargetFile, False);
        if not Result then Exit;
      until not FindNext(Entry);
    finally
      FindClose(Entry);
    end;
  end;
end;

function InitializeUninstall: Boolean;
var
  FrontendDirectory: String;
  ResultCode: Integer;
begin
  Result := False;
  if not UninstallSilent then
  begin
    FrontendDirectory := AddBackslash(GetEnv('TEMP')) + 'BASpark.Setup\' +
      GetDateTimeString('yyyymmddhhnnsszzz', '', '') + '-' + IntToStr(Random(1000000));
    if not CopyInstallerFrontend(ExpandConstant('{app}'), FrontendDirectory) then Exit;
    if not Exec(AddBackslash(FrontendDirectory) + 'BASpark.Setup.exe',
         '--uninstaller --backend="' + ExpandConstant('{uninstallexe}') +
         '" --install-dir="' + ExpandConstant('{app}') + '"', '', SW_SHOWNORMAL,
         ewNoWait, ResultCode) then
      Log('Could not start the WinUI uninstaller: ' + IntToStr(ResultCode));
    Exit;
  end;
  if not CloseRunningBASpark then Exit;
  if (ExpandConstant('{param:BASPARKKEEPDATA|1}') = '1') and
     RegKeyExists(HKEY_CURRENT_USER, 'Software\BASpark') then
  begin
    PreservedUserDataFile := ExpandConstant('{tmp}\BASpark-UserData.reg');
    if not Exec(ExpandConstant('{sys}\reg.exe'),
         'export "HKCU\Software\BASpark" "' + PreservedUserDataFile + '" /y',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then Exit;
  end;
  Result := True;
end;

#ifdef UiAccessCertificateThumbprint
const
  UiAccessCertificateThumbprint = '{#UiAccessCertificateThumbprint}';
  UiAccessOwnershipRoot = 'SOFTWARE\BASpark\Installer\UIAccessCertificates';
  PreviousInstallKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{B0A2C1D4-E3F5-4A6B-9C8D-7E1F2A3B4C5D}}_is1';

var
  UiAccessCertificateInstalled: Boolean;
  UiAccessPeopleOwnershipAdded: Boolean;
  UiAccessPeopleWasPresent: Boolean;
  UiAccessPublisherOwnershipAdded: Boolean;
  UiAccessPublisherWasPresent: Boolean;
  SetupInstallationCompleted: Boolean;

function CertificateStoreKey(StoreName, Thumbprint: String): String;
begin
  Result := 'SOFTWARE\Microsoft\SystemCertificates\' + StoreName +
    '\Certificates\' + Thumbprint;
end;

function CertificateOwnershipKey(Thumbprint: String): String;
begin
  Result := UiAccessOwnershipRoot + '\' + Thumbprint;
end;

function CertificateExists(StoreName, Thumbprint: String): Boolean;
begin
  Result := RegKeyExists(HKEY_LOCAL_MACHINE_64,
    CertificateStoreKey(StoreName, Thumbprint));
end;

function CertificateOwnershipExists(StoreName, Thumbprint: String): Boolean;
var
  OwnershipValue: Cardinal;
begin
  OwnershipValue := 0;
  if RegQueryDWordValue(HKEY_LOCAL_MACHINE_64,
    CertificateOwnershipKey(Thumbprint), StoreName, OwnershipValue) then
    Result := OwnershipValue = 1
  else
    Result := False;
end;

function RunCertUtil(Parameters: String): Boolean;
var
  ResultCode: Integer;
begin
  ResultCode := -1;
  Result := Exec(ExpandConstant('{sys}\certutil.exe'), Parameters, '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function RemoveUiAccessCertificate(StoreName, Thumbprint: String): Boolean;
begin
  if CertificateExists(StoreName, Thumbprint) then
    RunCertUtil('-delstore "' + StoreName + '" "' + Thumbprint + '"');

  Result := not CertificateExists(StoreName, Thumbprint);
  if not Result then
    Log('Failed to remove UIAccess certificate ' + Thumbprint +
      ' from ' + StoreName);
end;

procedure RemoveEmptyOwnershipKeys(Thumbprint: String);
begin
  RegDeleteKeyIfEmpty(HKEY_LOCAL_MACHINE_64,
    CertificateOwnershipKey(Thumbprint));
  RegDeleteKeyIfEmpty(HKEY_LOCAL_MACHINE_64, UiAccessOwnershipRoot);
  RegDeleteKeyIfEmpty(HKEY_LOCAL_MACHINE_64,
    'SOFTWARE\BASpark\Installer');
  RegDeleteKeyIfEmpty(HKEY_LOCAL_MACHINE_64, 'SOFTWARE\BASpark');
end;

procedure RollbackCurrentUiAccessCertificate;
var
  OwnershipKey: String;
begin
  OwnershipKey := CertificateOwnershipKey(UiAccessCertificateThumbprint);

  if not UiAccessPublisherWasPresent then
    if RemoveUiAccessCertificate('TrustedPublisher',
         UiAccessCertificateThumbprint) then
      if UiAccessPublisherOwnershipAdded then
        RegDeleteValue(HKEY_LOCAL_MACHINE_64, OwnershipKey,
          'TrustedPublisher');

  if not UiAccessPeopleWasPresent then
    if RemoveUiAccessCertificate('TrustedPeople',
         UiAccessCertificateThumbprint) then
      if UiAccessPeopleOwnershipAdded then
        RegDeleteValue(HKEY_LOCAL_MACHINE_64, OwnershipKey,
          'TrustedPeople');

  RemoveEmptyOwnershipKeys(UiAccessCertificateThumbprint);
end;

function InstallUiAccessCertificate: Boolean;
var
  CertificatePath: String;
  OwnershipKey: String;
begin
  Result := False;
  UiAccessPeopleOwnershipAdded := False;
  UiAccessPublisherOwnershipAdded := False;
  UiAccessPeopleWasPresent := CertificateExists('TrustedPeople',
    UiAccessCertificateThumbprint);
  UiAccessPublisherWasPresent := CertificateExists('TrustedPublisher',
    UiAccessCertificateThumbprint);
  OwnershipKey := CertificateOwnershipKey(UiAccessCertificateThumbprint);

  try
    ExtractTemporaryFile('BASpark.UIAccess.cer');
  except
    Exit;
  end;

  CertificatePath := ExpandConstant('{tmp}\BASpark.UIAccess.cer');
  if not RunCertUtil('-addstore -f "TrustedPeople" "' + CertificatePath + '"') then
    Exit;

  if not UiAccessPeopleWasPresent and
     not CertificateOwnershipExists('TrustedPeople',
       UiAccessCertificateThumbprint) then
  begin
    if not RegWriteDWordValue(HKEY_LOCAL_MACHINE_64, OwnershipKey,
      'TrustedPeople', 1) then
    begin
      RollbackCurrentUiAccessCertificate;
      Exit;
    end;
    UiAccessPeopleOwnershipAdded := True;
  end;

  if not RunCertUtil('-addstore -f "TrustedPublisher" "' +
    CertificatePath + '"') then
  begin
    RollbackCurrentUiAccessCertificate;
    Exit;
  end;

  if not UiAccessPublisherWasPresent and
     not CertificateOwnershipExists('TrustedPublisher',
       UiAccessCertificateThumbprint) then
  begin
    if not RegWriteDWordValue(HKEY_LOCAL_MACHINE_64, OwnershipKey,
      'TrustedPublisher', 1) then
    begin
      RollbackCurrentUiAccessCertificate;
      Exit;
    end;
    UiAccessPublisherOwnershipAdded := True;
  end;

  UiAccessCertificateInstalled := True;
  Result := True;
end;

function TryNormalizePath(Path: String; var NormalizedPath: String): Boolean;
begin
  Result := False;
  NormalizedPath := '';
  if (Path = '') or PathHasInvalidCharacters(Path, True) then
    Exit;

  try
    NormalizedPath := RemoveBackslashUnlessRoot(ExpandFileName(Path));
  except
    Exit;
  end;

  Result := NormalizedPath <> '';
end;

function RegistryViewHasConflictingInstall(Is64BitView: Boolean;
  TargetDirectory: String): Boolean;
var
  InstallLocationRead: Boolean;
  InstallKeyExists: Boolean;
  PreviousDirectory: String;
  PreviousDirectoryNormalized: String;
begin
  Result := False;
  if Is64BitView then
  begin
    InstallKeyExists := RegKeyExists(HKEY_LOCAL_MACHINE_64,
      PreviousInstallKey);
    if InstallKeyExists then
      InstallLocationRead := RegQueryStringValue(HKEY_LOCAL_MACHINE_64,
        PreviousInstallKey, 'InstallLocation', PreviousDirectory)
    else
      InstallLocationRead := False;
  end
  else
  begin
    InstallKeyExists := RegKeyExists(HKEY_LOCAL_MACHINE_32,
      PreviousInstallKey);
    if InstallKeyExists then
      InstallLocationRead := RegQueryStringValue(HKEY_LOCAL_MACHINE_32,
        PreviousInstallKey, 'InstallLocation', PreviousDirectory)
    else
      InstallLocationRead := False;
  end;

  if not InstallKeyExists then
    Exit;

  if not InstallLocationRead or
     not TryNormalizePath(PreviousDirectory,
       PreviousDirectoryNormalized) then
  begin
    Log('Existing BASpark uninstall entry has no valid install location');
    Result := True;
    Exit;
  end;

  if CompareText(PreviousDirectoryNormalized, TargetDirectory) <> 0 then
  begin
    Log('Existing BASpark installation uses a different directory: ' +
      PreviousDirectoryNormalized);
    Result := True;
  end;
end;

function IsValidCertificateThumbprint(Thumbprint: String): Boolean;
var
  I: Integer;
  C: Char;
begin
  Result := Length(Thumbprint) = 40;
  if not Result then
    Exit;

  for I := 1 to Length(Thumbprint) do
  begin
    C := Thumbprint[I];
    if not (((C >= '0') and (C <= '9')) or
            ((C >= 'A') and (C <= 'F')) or
            ((C >= 'a') and (C <= 'f'))) then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

procedure RemoveOwnedCertificate(StoreName, Thumbprint: String);
var
  OwnershipKey: String;
begin
  OwnershipKey := CertificateOwnershipKey(Thumbprint);
  if not CertificateOwnershipExists(StoreName, Thumbprint) then
    Exit;

  if RemoveUiAccessCertificate(StoreName, Thumbprint) then
    RegDeleteValue(HKEY_LOCAL_MACHINE_64, OwnershipKey, StoreName);
end;

procedure CleanupOwnedUiAccessCertificates(ExcludedThumbprint: String);
var
  I: Integer;
  Thumbprints: TArrayOfString;
begin
  if not RegGetSubkeyNames(HKEY_LOCAL_MACHINE_64,
    UiAccessOwnershipRoot, Thumbprints) then
    Exit;

  for I := 0 to GetArrayLength(Thumbprints) - 1 do
  begin
    if (ExcludedThumbprint <> '') and
       (CompareText(Thumbprints[I], ExcludedThumbprint) = 0) then
      Continue;

    if IsValidCertificateThumbprint(Thumbprints[I]) then
    begin
      RemoveOwnedCertificate('TrustedPublisher', Thumbprints[I]);
      RemoveOwnedCertificate('TrustedPeople', Thumbprints[I]);
      RemoveEmptyOwnershipKeys(Thumbprints[I]);
    end
    else
      Log('Ignoring invalid UIAccess certificate ownership key: ' +
        Thumbprints[I]);
  end;
end;

function PrepareUiAccessInstallation: String;
var
  AppDirectoryNormalized: String;
  ConsentGranted: Boolean;
  TargetDirectoryNormalized: String;
begin
  Result := '';
  if not TryNormalizePath(ExpandConstant('{autopf}\BASpark'),
       TargetDirectoryNormalized) or
     not TryNormalizePath(WizardDirValue, AppDirectoryNormalized) or
     (Pos(Lowercase(AddBackslash(ExpandConstant('{autopf}'))),
       Lowercase(AddBackslash(AppDirectoryNormalized))) <> 1) then
  begin
    Result := ExpandConstant('{cm:UiAccessSecureLocationRequired}');
    Exit;
  end;

  if RegistryViewHasConflictingInstall(True,
       AppDirectoryNormalized) or
     RegistryViewHasConflictingInstall(False,
       AppDirectoryNormalized) then
  begin
    Result := ExpandConstant('{cm:UiAccessPreviousLocationRequired}');
    Exit;
  end;

  ConsentGranted := CompareText(
    ExpandConstant('{param:ACCEPTUIACCESSCERT|0}'), '1') = 0;

  if not ConsentGranted then
  begin
    Result := ExpandConstant('{cm:UiAccessCertificateRequired}');
    Exit;
  end;

  if not InstallUiAccessCertificate then
    Result := ExpandConstant('{cm:UiAccessCertificateInstallFailed}');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssDone then
  begin
    SetupInstallationCompleted := True;
    CleanupOwnedUiAccessCertificates(UiAccessCertificateThumbprint);
  end;
end;

procedure DeinitializeSetup;
begin
  if UiAccessCertificateInstalled and not SetupInstallationCompleted then
    RollbackCurrentUiAccessCertificate;
end;
#endif

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not CloseRunningBASpark then
  begin
    Result := ExpandConstant('{cm:CloseRunningBASparkFailed}');
    Exit;
  end;
#ifdef UiAccessCertificateThumbprint
  Result := PrepareUiAccessInstallation;
#endif
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then WriteNativeProgress(10);
  if CurUninstallStep = usPostUninstall then
  begin
    WriteNativeProgress(90);
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'BASpark');
    Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN "BASparkAutoStart" /F',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
  if CurUninstallStep = usDone then
  begin
    if PreservedUserDataFile <> '' then
      Exec(ExpandConstant('{sys}\reg.exe'), 'import "' + PreservedUserDataFile + '"',
        '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
    else if ExpandConstant('{param:BASPARKKEEPDATA|1}') = '0' then
    begin
      RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\BASpark');
      DelTree(ExpandConstant('{localappdata}\BASpark'), True, True, True);
    end;
    WriteNativeProgress(100);
  end;
#ifdef UiAccessCertificateThumbprint
  if CurUninstallStep = usDone then
    CleanupOwnedUiAccessCertificates('');
#endif
end;
