#ifndef AppVersion
  #define AppVersion "0.0.1-dev"
#endif

#if Copy(AppVersion, 1, 1) == "v"
  #define NormalizedAppVersion Copy(AppVersion, 2)
#else
  #define NormalizedAppVersion AppVersion
#endif

#ifdef UiAccessCertificateThumbprint
  #define PublishedSource "src\publish_uiaccess"
#else
  #define PublishedSource "src\publish_full"
#endif

#define PublishedExecutable SourcePath + "\" + PublishedSource + "\BASpark.exe"
#if FileExists(PublishedExecutable)
  #define PublishedAppVersion GetStringFileInfo(PublishedExecutable, "ProductVersion")
  #if PublishedAppVersion != NormalizedAppVersion
    #pragma error "Published BASpark.exe ProductVersion (" + PublishedAppVersion + ") does not match installer AppVersion (" + NormalizedAppVersion + ")"
  #endif
#endif

#ifdef UiAccessCertificateThumbprint
  #define UiAccessCertificateFile SourcePath + "\" + PublishedSource + "\BASpark.UIAccess.cer"
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
#ifdef UiAccessCertificateThumbprint
DisableDirPage=yes
UsePreviousAppDir=no
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "japanese"; MessagesFile: "Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

#ifdef UiAccessCertificateThumbprint
[CustomMessages]
UiAccessCertificatePrompt=To draw effects above Windows Start, notification center, and calendar, Setup must trust a local BASpark code-signing certificate on this computer. The private key is not included. Continue?
UiAccessCertificateRequired=Certificate trust is required for the UIAccess build. Setup made no changes.
UiAccessCertificateSilentRequired=Silent installation requires /ACCEPTUIACCESSCERT=1.
UiAccessCertificateInstallFailed=Failed to trust the BASpark UIAccess signing certificate. Setup made no application changes.
UiAccessSecureLocationRequired=The UIAccess build must be installed in Program Files\BASpark.
UiAccessPreviousLocationRequired=An existing BASpark installation was found outside Program Files\BASpark. Uninstall it before installing the UIAccess build.
chinesesimplified.UiAccessCertificatePrompt=为了让特效覆盖 Windows 开始菜单、通知中心和日历，安装程序需要在此计算机信任仅用于 BASpark 的本地代码签名证书。安装包不包含私钥。是否继续？
chinesesimplified.UiAccessCertificateRequired=UIAccess 版本必须信任代码签名证书，安装程序尚未修改应用文件。
chinesesimplified.UiAccessCertificateSilentRequired=静默安装必须添加 /ACCEPTUIACCESSCERT=1。
chinesesimplified.UiAccessCertificateInstallFailed=无法信任 BASpark UIAccess 签名证书，安装程序尚未修改应用文件。
chinesesimplified.UiAccessSecureLocationRequired=UIAccess 版本必须安装在 Program Files\BASpark 目录中。
chinesesimplified.UiAccessPreviousLocationRequired=检测到 BASpark 旧版本安装在 Program Files\BASpark 之外。请先卸载旧版本，再安装 UIAccess 版本。
japanese.UiAccessCertificatePrompt=Windows のスタート、通知センター、カレンダーより上にエフェクトを表示するため、このコンピューターで BASpark 専用のローカルコード署名証明書を信頼する必要があります。秘密鍵は含まれていません。続行しますか？
japanese.UiAccessCertificateRequired=UIAccess 版ではコード署名証明書の信頼が必要です。アプリケーションファイルは変更されていません。
japanese.UiAccessCertificateSilentRequired=サイレントインストールには /ACCEPTUIACCESSCERT=1 が必要です。
japanese.UiAccessCertificateInstallFailed=BASpark UIAccess 署名証明書を信頼できませんでした。アプリケーションファイルは変更されていません。
japanese.UiAccessSecureLocationRequired=UIAccess 版は Program Files\BASpark にインストールする必要があります。
japanese.UiAccessPreviousLocationRequired=Program Files\BASpark 以外に既存の BASpark が見つかりました。UIAccess 版をインストールする前に、既存版をアンインストールしてください。
#endif

[Files]
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
Root: HKCU; Subkey: "Software\BASpark"; Flags: uninsdeletekey

[UninstallRun]
Filename: "taskkill"; Parameters: "/F /IM BASpark.exe /T"; Flags: runhidden

[Run]
#ifdef UiAccessCertificateThumbprint
Filename: "{app}\BASpark.exe"; Description: "{cm:LaunchProgram,BASpark}"; Flags: nowait postinstall skipifsilent shellexec
#else
Filename: "{app}\BASpark.exe"; Description: "{cm:LaunchProgram,BASpark}"; Flags: nowait postinstall skipifsilent
#endif

[Code]
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

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  AppDirectoryNormalized: String;
  ConsentGranted: Boolean;
  TargetDirectoryNormalized: String;
begin
  Result := '';
  if not TryNormalizePath(ExpandConstant('{autopf}\BASpark'),
       TargetDirectoryNormalized) or
     not TryNormalizePath(WizardDirValue, AppDirectoryNormalized) or
     (CompareText(AppDirectoryNormalized, TargetDirectoryNormalized) <> 0) then
  begin
    Result := ExpandConstant('{cm:UiAccessSecureLocationRequired}');
    Exit;
  end;

  if RegistryViewHasConflictingInstall(True,
       TargetDirectoryNormalized) or
     RegistryViewHasConflictingInstall(False,
       TargetDirectoryNormalized) then
  begin
    Result := ExpandConstant('{cm:UiAccessPreviousLocationRequired}');
    Exit;
  end;

  if WizardSilent then
  begin
    ConsentGranted := CompareText(
      ExpandConstant('{param:ACCEPTUIACCESSCERT|0}'), '1') = 0;
    if not ConsentGranted then
    begin
      Result := ExpandConstant('{cm:UiAccessCertificateSilentRequired}');
      Exit;
    end;
  end
  else
    ConsentGranted := MsgBox(
      ExpandConstant('{cm:UiAccessCertificatePrompt}'), mbConfirmation,
      MB_YESNO) = IDYES;

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

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
#ifdef UiAccessCertificateThumbprint
  if CurUninstallStep = usDone then
    CleanupOwnedUiAccessCertificates('');
#endif
end;
