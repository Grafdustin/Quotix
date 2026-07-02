; Quotix 安装包脚本
; 用 Inno Setup 6 编译：ISCC.exe /DMyAppVersion=1.0.0 QuotixInstaller.iss
; 编译前先运行 Prepare-Staging.ps1 准备文件

[Setup]
AppName=Quotix
AppVersion={#MyAppVersion}
AppPublisher=Quotix
AppPublisherURL=https://quotix.app
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
; 卸载时删除 Data 目录
Type: filesandordirs; Name: "{app}\Data"

[Code]
// 卸载时可选删除用户数据
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DeleteData: Boolean;
  DataDir: string;
  AppDataDir: string;
begin
  // 在卸载完成后显示选项
  if CurUninstallStep = usPostUninstall then
  begin
    // 检查是否有数据目录
    DataDir := ExpandConstant('{app}\Data');
    AppDataDir := ExpandConstant('{localappdata}\Programs\Quotix\Data');
    
    // 显示确认对话框
    if MsgBox('是否删除所有用户数据（包括数据库和设置）？' + #13#10 + 
              '选择"是"将删除所有数据，选择"否"将保留数据。',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      // 删除安装目录的 Data 文件夹
      if DirExists(DataDir) then
      begin
        DelTree(DataDir, True, True, True);
      end;
      
      // 删除可能的 AppData 残留
      if DirExists(AppDataDir) then
      begin
        DelTree(AppDataDir, True, True, True);
      end;
      
      // 删除设置文件（可能在其他位置）
      DeleteFile(ExpandConstant('{localappdata}\Quotix\settings.json'));
      DeleteFile(ExpandConstant('{localappdata}\Quotix\error.log'));
      DelTree(ExpandConstant('{localappdata}\Quotix'), True, True, True);
    end;
  end;
end;
