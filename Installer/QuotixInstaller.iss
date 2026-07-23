; Quotix 安装包脚本
; 用 Inno Setup 6 编译：ISCC.exe /DMyAppVersion=1.0.0 QuotixInstaller.iss
; 编译前先运行 Prepare-Staging.ps1 准备文件

[Setup]
AppName=Quotix
AppVersion={#MyAppVersion}
AppPublisher=Grafdustin
AppPublisherURL=https://github.com/Grafdustin/Quotix
DefaultDirName={localappdata}\Programs\Quotix
DefaultGroupName=Quotix
OutputDir=.\Out
OutputBaseFilename=Quotix_Setup_{#MyAppVersion}
SetupIconFile=.\Staging\Launcher\Resources\app.ico
Compression=lzma
SolidCompression=yes
PrivilegesRequired=lowest
DisableDirPage=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; 文件版本信息（与产品版本同步）
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany=Grafdustin
VersionInfoCopyright=Copyright (C) 2026 Grafdustin
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
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; ── Launcher：主程序 + 所有 DLL 和运行时文件 ──
Source: ".\Staging\Launcher\*"; DestDir: "{app}\Launcher"; Flags: ignoreversion recursesubdirs

[Dirs]
; ── Data：运行时数据目录（数据库、设置、日志）──
Name: "{app}\Data"

[Icons]
Name: "{group}\Quotix"; Filename: "{app}\Launcher\Quotix.exe"; IconFilename: "{app}\Launcher\Resources\app.ico"
Name: "{group}\卸载 Quotix"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Quotix"; Filename: "{app}\Launcher\Quotix.exe"; IconFilename: "{app}\Launcher\Resources\app.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\Launcher\Quotix.exe"; Description: "{cm:LaunchProgram,Quotix}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]

[Code]
// 卸载时可选删除数据
// 在卸载确认后、文件删除前询问用户（usAppMutexCheck 是卸载开始前的第一个步骤）
var
  g_DeleteData: Boolean;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  msg: string;
  res: Integer;
begin
  // 在卸载开始前的步骤中询问（仅执行一次）
  if (CurUninstallStep = usAppMutexCheck) and (not g_DeleteData) then
  begin
    // 静默卸载模式下默认不删除数据
    if UninstallSilent() then
    begin
      g_DeleteData := False;
      Exit;
    end;

    msg := '即将卸载 Quotix。' + #13#10#13#10 +
          '是否同时删除用户数据？' + #13#10 +
          '（包括数据库、设置、日志等个人数据）' + #13#10#13#10 +
          '选择"是"：删除所有数据' + #13#10 +
          '选择"否"：保留数据（重新安装后可继续使用）' + #13#10 +
          '选择"取消"：中止卸载';

    res := MsgBox(msg, mbConfirmation, MB_YESNOCANCEL or MB_DEFBUTTON2);

    if res = IDYES then
    begin
      g_DeleteData := True;    // 删除数据
    end
    else if res = IDNO then
    begin
      g_DeleteData := False;   // 保留数据
    end
    else  // IDCANCEL 或关闭对话框
    begin
      Abort();  // 中止卸载
    end;
  end;

  // 在卸载文件阶段处理数据目录
  if (CurUninstallStep = usUninstall) and g_DeleteData then
  begin
    // 删除安装目录的 Data 文件夹
    if DirExists(ExpandConstant('{app}\Data')) then
      DelTree(ExpandConstant('{app}\Data'), True, True, True);

    // 删除可能的残留目录
    DelTree(ExpandConstant('{localappdata}\Programs\Quotix'), True, True, True);
    DelTree(ExpandConstant('{localappdata}\Quotix'), True, True, True);
  end;
end;

