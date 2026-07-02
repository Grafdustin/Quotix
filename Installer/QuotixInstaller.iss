; Quotix 安装包脚本
; 用 Inno Setup 6 编译：ISCC.exe /DMyAppVersion=1.0.0 QuotixInstaller.iss
; 编译前先运行 Prepare-Staging.ps1 准备文件

[Setup]
AppName=Quotix
AppVersion={#MyAppVersion}
AppPublisher=Quotix
AppPublisherURL=https://quotix.app
DefaultDirName={autopf}\Quotix
DefaultGroupName=Quotix
OutputDir=.\Out
OutputBaseFilename=Quotix_Setup_{#MyAppVersion}
SetupIconFile=.\Staging\Launcher\Resources\app.ico
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
DisableDirPage=no
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\Launcher\Quotix.exe
UninstallFilesDir={app}\Uninstall
WizardStyle=modern
; 自定义向导图片（需要 BMP 格式）
; WizardImageFile=.\Images\WizardImage.bmp
; WizardSmallImageFile=.\Images\WizardSmall.bmp

[Languages]
Name: "chinese"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; ── Launcher：主程序 + 所有 DLL 和运行时文件 ──
Source: ".\Staging\Launcher\*"; DestDir: "{app}\Launcher"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\Quotix"; Filename: "{app}\Launcher\Quotix.exe"; IconFilename: "{app}\Launcher\Resources\app.ico"
Name: "{group}\卸载 Quotix"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Quotix"; Filename: "{app}\Launcher\Quotix.exe"; IconFilename: "{app}\Launcher\Resources\app.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\Launcher\Quotix.exe"; Description: "{cm:LaunchProgram,Quotix}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DotNetMissing: Boolean;

// 检测 .NET 10 Desktop Runtime (x64) 是否已安装
function IsDotNet10Installed: Boolean;
var
  UninstallKey: string;
  I: Integer;
  Apps: TArrayOfString;
  KeyName: string;
  DisplayName: string;
begin
  Result := False;

  // 方法1：检查注册表卸载项（精确匹配）
  UninstallKey := 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall';
  if RegKeyExists(HKLM, UninstallKey + '\.NETDesktopRuntime.10.0.x64') then
  begin
    Result := True;
    Exit;
  end;

  // 方法2：遍历卸载项查找 .NET 10 Desktop Runtime
  if RegGetSubkeyNames(HKLM, UninstallKey, Apps) then
  begin
    for I := 0 to GetArrayLength(Apps) - 1 do
    begin
      KeyName := Apps[I];
      // 检查注册表项的 DisplayName
      if RegQueryStringValue(HKLM, UninstallKey + '\' + KeyName,
                                 'DisplayName', DisplayName) then
      begin
        if (Pos('.NET Desktop Runtime 10', DisplayName) > 0) or
           (Pos('Microsoft Windows Desktop 10', DisplayName) > 0) or
           (Pos('Microsoft.WindowsDesktop.App 10', DisplayName) > 0) then
        begin
          Result := True;
          Exit;
        end;
      end;
    end;
  end;

  // 方法3：检查文件系统（.NET 10 共享目录）
  if DirExists(ExpandConstant('{commonprograms}\..\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.0')) or
     DirExists('C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.0') then
  begin
    Result := True;
    Exit;
  end;
end;

// 安装开始前检测 .NET 10
function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True; // 允许继续安装

  if not IsDotNet10Installed then
  begin
    DotNetMissing := True;
    if MsgBox(
      '未检测到 .NET 10 Desktop Runtime (x64)。' + #13#10 +
      'Quotix 需要 .NET 10 运行时才能正常工作。' + #13#10#13#10 +
      '是否现在打开下载页面？' + #13#10 +
      '（也可稍后手动安装，但程序可能无法运行）',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open',
                'https://dotnet.microsoft.com/en-us/download/dotnet/10.0',
                '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
  end
  else
  begin
    DotNetMissing := False;
  end;
end;

// 安装完成后提示
procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    if DotNetMissing then
    begin
      MsgBox(
        '.NET 10 Desktop Runtime 尚未安装。' + #13#10 +
        '请访问以下网址下载并安装：' + #13#10 +
        'https://dotnet.microsoft.com/en-us/download/dotnet/10.0',
        mbInformation, MB_OK);
    end;
  end;
end;
