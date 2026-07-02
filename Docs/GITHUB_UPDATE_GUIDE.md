# GitHub 更新功能 - 操作指南

## ✅ 已完成的配置

### 1. 更新服务 (UpdateService.cs)
- ✅ 从 GitHub Raw 下载 `version.json`
- ✅ 比较本地版本号和远程版本号
- ✅ 打开浏览器下载更新

### 2. 版本 Manifest (Resources/version.json)
- ✅ 存储最新版本信息
- ✅ 包含下载链接、更新内容、文件大小等

### 3. 设置页面集成 (SettingsViewModel.cs)
- ✅ 添加"检查更新"按钮
- ✅ 显示更新状态（检查中、有更新、已是最新）

### 4. GitHub 仓库配置
- ✅ 远程仓库：https://github.com/Grafdustin/Quotix
- ✅ 代码已推送到 main 分支

---

## 🚀 首次发布（手动创建 Release）

### 步骤 1：访问 GitHub Releases 页面
1. 打开：https://github.com/Grafdustin/Quotix
2. 点击右侧 **"Releases"** 链接
3. 点击 **"Create a new release"** 按钮

### 步骤 2：填写发布信息
```
Tag version: v1.0.1
Release title: Quotix v1.0.1
```

**描述内容（可复制）：**
```markdown
## 🎯 更新内容

- 修复版本号显示问题
- 重写所有代码注释为中文 XML 文档
- 优化发布流程（自动递增版本号）
- 添加 GitHub 更新检查功能

## 📦 安装

下载下方的 `Quotix_Setup_1.0.1.exe` 并运行安装。

## 📝 完整更新日志

查看 `Resources/version.json` 获取详细信息。
```

### 步骤 3：上传安装包
1. 点击 **"Attach binaries by dropping them here or selecting them"**
2. 选择文件：`D:\桌面\QUO\Quotix\Installer\Out\Quotix_Setup_1.0.1.exe`
3. 等待上传完成

### 步骤 4：发布
- ✅ 勾选 **"Set as the latest release"**
- ❌ 不勾选 **"Create a draft release"**（除非你想先预览）
- 点击 **"Publish release"**

---

## 🧪 测试更新功能

### 1. 运行应用
```powershell
cd D:\桌面\QUO\Quotix
dotnet run --project QuotixDesktop.csproj
```

### 2. 打开设置页面
- 点击设置图标（⚙️）
- 滚动到最下方，找到 **"更新"** 部分

### 3. 检查更新
- 点击 **"检查更新"** 按钮
- 预期结果：
  - ✅ 如果 GitHub 上有 `version.json`，应用会读取版本信息
  - ✅ 如果有新版本，会弹出对话框提示更新
  - ✅ 点击 "确定" 会打开浏览器下载最新版本

### 4. 验证更新信息
检查对话框是否显示：
- 版本号（如 "1.0.1"）
- 发布日期
- 文件大小
- 更新内容列表

---

## 🔄 后续发布流程

### 标准发布命令（推荐）

```powershell
cd D:\桌面\QUO\Quotix

.\Build-Release.ps1 -CommitMessage "发布v1.0.2" -Version "1.0.2"
```

**这个命令会自动：**
1. ✅ Git 提交代码
2. ✅ 版本号自动递增（`1.0.1` → `1.0.2`）
3. ✅ 编译项目
4. ✅ 生成安装包（`Quotix_Setup_1.0.2.exe`）

**然后你需要手动：**
1. 访问 https://github.com/Grafdustin/Quotix/releases/new
2. 创建新 Release（`v1.0.2`）
3. 上传安装包 `Quotix_Setup_1.0.2.exe`
4. 更新 `Resources\version.json` 中的版本号和下载链接
5. 提交并推送 `version.json`

### 更新 version.json 的示例

```json
{
  "version": "1.0.2",
  "releaseDate": "2026-07-03",
  "releaseNotes": "修复BUG，优化性能",
  "downloadUrl": "https://github.com/Grafdustin/Quotix/releases/download/v1.0.2/Quotix_Setup_1.0.2.exe",
  "fullPackageUrl": "https://github.com/Grafdustin/Quotix/releases/download/v1.0.2/Quotix_Setup_1.0.2.exe",
  "fileSize": "57MB",
  "sha256": "",
  "mandatory": false,
  "whatsNew": [
    "修复了已知的崩溃问题",
    "优化了启动速度",
    "改进了用户界面"
  ]
}
```

---

## 🤖 完全自动化发布（可选）

### 方案 A：使用 GitHub Actions（高级）

已创建两个工作流文件：
1. `.github\workflows\build-and-release.yml` - 自动构建和发布（需要在 Windows runner 上安装 Inno Setup）
2. `.github\workflows\update-version.yml` - 仅更新 version.json

**注意：** 对于 WPF 桌面应用，在 GitHub Actions 上自动构建可能比较复杂。建议先在本地构建，然后使用 GitHub Actions 仅创建 Release。

### 方案 B：使用 GitHub CLI（推荐）

```powershell
# 安装 GitHub CLI
winget install --id GitHub.cli

# 登录 GitHub
gh auth login

# 构建并发布（一次性完成）
.\Build-Release.ps1 -CommitMessage "发布v1.0.2" -Version "1.0.2"

# 创建 Release 并上传安装包
gh release create v1.0.2 "Installer/Out/Quotix_Setup_1.0.2.exe" `
  --title "Quotix v1.0.2" `
  --notes "修复BUG，优化性能"
```

---

## 🔍 故障排除

### 问题 1：检查更新时没有任何反应
**可能原因：**
- 网络连接问题
- `version.json` 文件路径错误
- GitHub Raw URL 不正确

**解决方法：**
1. 检查 `Services\UpdateService.cs` 第 23 行的 URL 是否正确
2. 手动访问 https://raw.githubusercontent.com/Grafdustin/Quotix/main/Resources/version.json 确认能访问
3. 查看应用日志（输出窗口）是否有错误信息

### 问题 2：版本号比较不正确
**可能原因：**
- `version.json` 中的版本号格式不正确
- 本地版本号读取错误

**解决方法：**
1. 确保 `version.json` 中的 `version` 字段格式为 `x.y.z`（如 `1.0.1`）
2. 检查 `AppInfo.cs` 中的 `Version` 属性是否正确

### 问题 3：下载链接打不开
**可能原因：**
- GitHub Release 尚未创建
- 下载链接 URL 不正确

**解决方法：**
1. 确认已创建 GitHub Release 并上传了安装包
2. 检查 `version.json` 中的 `downloadUrl` 是否与实际 Release 的下载链接一致

---

## 📚 参考资料

- **GitHub Releases 文档**：https://docs.github.com/en/repositories/releasing-projects-on-github
- **GitHub Actions 文档**：https://docs.github.com/en/actions
- **GitHub CLI 文档**：https://cli.github.com/manual/

---

## 📝 维护检查清单

每次发布新版本时，确保：
- [ ] 版本号已更新（`.csproj` 和 `version.json`）
- [ ] 安装包已生成（`Installer\Out\`）
- [ ] GitHub Release 已创建
- [ ] 安装包已上传到 Release
- [ ] `version.json` 已更新并推送到 GitHub
- [ ] 测试更新功能正常工作

---

**最后更新：** 2026-07-02
**维护者：** Grafdustin
