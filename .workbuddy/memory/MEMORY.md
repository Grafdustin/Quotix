# Quotix 项目长期记忆

## 开发与发布分工

**我（WorkBuddy）负责：**
- 编写和修改源代码
- 每次改动后自动 `git commit`
- 编译验证（`dotnet build`），确保无错误
- 测试功能正确性

**用户负责：**
- 运行 `Build-Release.ps1` 发布安装包
- 推送到 GitHub Release
- 上线审核

## 编译规范（重要！）
- **每次编译前，必须先删除 `bin/` 和 `obj/` 文件夹**，确保输出干净无缓存残留
- 命令：`rm -r -Force bin/ obj/` 然后 `dotnet build`
- 编译仅用于验证，不生成安装包

## Git 规范
- 每次代码改动后自动 `git commit`，提交信息用中文简述改动内容
- 分支：`main`
- 排除：数据库文件（`*.db`/`*.sqlite`）、编译产物（`bin/`/`obj/`）、IDE 缓存（`.vs/`）

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
