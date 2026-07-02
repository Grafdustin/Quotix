# Quotix 项目长期记忆

## Git 规范
- 每次代码改动后自动 `git commit`，提交信息用中文简述改动内容
- 分支：`main`
- 排除：数据库文件（`*.db`/`*.sqlite`）、编译产物（`bin/`/`obj/`）、IDE 缓存（`.vs/`）

## 发布流程（重要！）
**每次修改完源代码后，必须执行以下标准流程：**
1. **Git 提交**：`git add -A` + `git commit -m "描述"`
2. **编译项目**：`dotnet publish -c Release -r win-x64 --self-contained true`
3. **发布安装包**：运行 `Installer/Build-Installer.ps1` 或使用 `Build-Release.ps1`

### ISCC.exe 路径（已确认）
- 位置：`C:\Users\Evans\AppData\Local\Programs\Inno Setup 6\ISCC.exe`
- 已写入 `Installer/Build-Installer.ps1`，无需每次查找

### 快速发布命令
```powershell
# 完整流程（推荐）
.\Build-Release.ps1 -CommitMessage "描述本次更改"

# 仅构建安装包（已编译好的情况下）
.\Installer\Build-Installer.ps1 -SkipBuild

# 更新版本号并发布
.\Build-Release.ps1 -CommitMessage "发布新版本" -Version "1.0.1"
```

## 项目结构
- **Quotix/**：主项目目录
  - `QuotixDesktop.csproj`：项目文件
  - `Installer/`：安装包构建脚本和 Inno Setup 配置
  - `Resources/`：图标和模板文件
  - `Models/`：数据模型
  - `ViewModels/`：MVVM 视图模型
  - `Views/`：WPF 视图
  - `Services/`：业务服务
  - `Repositories/`：数据访问层
  - `Common/`：通用工具类

## 版本号管理
- 版本号定义在 `QuotixDesktop.csproj` 的 `<Version>` 标签
- 格式：`1.0.0`（语义化版本）
- 安装包文件名会自动包含版本号：`Quotix_Setup_1.0.0.exe`

## 代码风格
- 所有 XML 文档注释使用中文
- 使用 `<summary>`, `<param>`, `<remarks>` 等标准标签
- 注释应简洁明了，说明"为什么"而不是"做什么"

## 技术栈
- .NET 10.0 (net10.0-windows)
- WPF + WPF-UI (Fluent Design)
- SQLite (Microsoft.Data.Sqlite)
- ClosedXML (Excel 导入导出)
- CommunityToolkit.Mvvm (MVVM 框架)
- Inno Setup (安装包制作)
