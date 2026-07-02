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
