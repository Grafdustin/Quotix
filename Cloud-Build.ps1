# Cloud-Build.ps1
# 一键触发 GitHub Actions 云端构建发布
# 用法：.\Cloud-Build.ps1                    # 自动从 csproj 读取版本号
# 用法：.\Cloud-Build.ps1 -Version "1.0.69"  # 指定版本号
# 用法：.\Cloud-Build.ps1 -Redeploy          # 重新触发构建（版本号不变，重新打 tag）
# 推荐用 PowerShell 7 运行：pwsh -File .\Cloud-Build.ps1

param(
    [string]$Version,
    [string]$CommitMessage,
    [switch]$Redeploy
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ProjectDir = Split-Path $MyInvocation.MyCommand.Path

# ========== Step 1: 读取/设置版本号 ==========
Write-Host "=== Quotix Cloud Build ===" -ForegroundColor Cyan
Write-Host ""

[xml]$csprojXml = Get-Content (Join-Path $ProjectDir "QuotixDesktop.csproj")
$currentVersion = $csprojXml.Project.PropertyGroup.Version

if ($Redeploy) {
    # 重新触发模式：保持版本号不变
    $Version = $currentVersion
    Write-Host "Redeploy mode: version $Version (no changes)" -ForegroundColor Yellow
} elseif (-not $Version) {
    Write-Host "Current version: $currentVersion" -ForegroundColor Yellow
    $inputVersion = Read-Host "Enter new version (Enter to keep $currentVersion)"
    if ($inputVersion) { $Version = $inputVersion } else { $Version = $currentVersion }
}

# 验证版本号格式
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid version format: $Version. Expected: x.y.z"
}

# ========== Step 2: 输入更新日志 ==========
if (-not $CommitMessage) {
    $tempFile = Join-Path $env:TEMP "quotix_changelog.txt"
    $instructions = @"
// 请输入更新日志
// 以 # 开头的行作为章节标题（如：# 新功能）
// 其余行作为章节内容
// 以 // 开头的行会被忽略
// 保存并关闭记事本后脚本继续
"@
    Set-Content $tempFile $instructions -Encoding UTF8
    Write-Host "Opening notepad for changelog..." -ForegroundColor Cyan
    Start-Process notepad.exe $tempFile -Wait

    $lines = Get-Content $tempFile -Encoding UTF8 | Where-Object { $_.Trim() -ne "" -and -not $_.StartsWith("//") }
    $CommitMessage = $lines -join "`n"
    Remove-Item $tempFile -Force -ErrorAction SilentlyContinue

    if (-not $CommitMessage) {
        $CommitMessage = "Update"
    }
}

Write-Host "Version : $Version" -ForegroundColor Cyan
Write-Host "Message : $CommitMessage" -ForegroundColor Cyan
Write-Host ""

# ========== Step 3: 更新 csproj 版本号（仅当版本号变化时）==========
if ($Version -ne $currentVersion) {
    Write-Host "Updating csproj version..." -ForegroundColor Yellow
    $csprojPath = Join-Path $ProjectDir "QuotixDesktop.csproj"
    [xml]$csproj = Get-Content $csprojPath
    $csproj.Project.PropertyGroup.Version = $Version
    $csproj.Project.PropertyGroup.InformationalVersion = $Version
    $csproj.Save($csprojPath)
    Write-Host "csproj updated to $Version" -ForegroundColor Green
} else {
    Write-Host "Version unchanged ($Version), skipping csproj update" -ForegroundColor Yellow
}

# ========== Step 4: Git commit & push ==========
Write-Host "Committing and pushing..." -ForegroundColor Yellow
Set-Location $ProjectDir

git add -A

# 检查是否有文件变动
git diff --cached --quiet 2>&1
$hasChanges = $LASTEXITCODE -ne 0

if ($hasChanges) {
# 有变动，正常 commit
    $allLines = ($CommitMessage -split "`n") | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ -ne "" }
    $title = if ($allLines.Count -gt 0) { $allLines[0] } else { "Update" }
    $body = if ($allLines.Count -gt 1) { ($allLines | Select-Object -Skip 1) -join "`n" } else { "" }

    $commitMsgFile = Join-Path $env:TEMP "quotix_git_commit_msg.txt"
    if ($body) {
        $fullMsg = $title + "`n`n" + $body
        Set-Content -Path $commitMsgFile -Value $fullMsg -Encoding UTF8
    } else {
        Set-Content -Path $commitMsgFile -Value $title -Encoding UTF8
    }
    git commit -F "$commitMsgFile"
    if ($LASTEXITCODE -ne 0) { throw "Git commit failed" }
    Write-Host "Git commit successful" -ForegroundColor Green
} else {
    Write-Host "No file changes, skipping commit" -ForegroundColor Yellow
}

# push main
$gitOutput = git push origin main 2>&1
# 过滤掉 "Everything up-to-date" 的正常输出
if ($LASTEXITCODE -ne 0 -and $gitOutput -notmatch "Everything up-to-date") {
    Write-Host $gitOutput
    throw "Git push failed"
}
Write-Host "Pushed to main" -ForegroundColor Green

# ========== Step 5: 创建并推送 tag 触发 GitHub Actions ==========
Write-Host "Creating tag v$Version to trigger GitHub Actions..." -ForegroundColor Yellow

# 检查 tag 是否已存在，若存在则删除（支持重新触发）
$existingTag = git tag -l "v$Version"
if ($existingTag) {
    Write-Host "Tag v$Version already exists locally, removing..." -ForegroundColor Yellow
    git tag -d "v$Version" 2>&1 | Out-Null
}
# 检查远程 tag 是否存在
$remoteTag = git ls-remote --tags origin "refs/tags/v$Version" 2>&1
if ($remoteTag) {
    Write-Host "Tag v$Version exists on remote, removing..." -ForegroundColor Yellow
    git push origin --delete "v$Version" 2>&1
}

# 创建并推送 tag
git tag "v$Version"
$tagPushOutput = git push origin "v$Version" 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host $tagPushOutput
    throw "Failed to push tag v$Version"
}

Write-Host ""
Write-Host "=== Build triggered! ===" -ForegroundColor Green
Write-Host "Watch progress:" -ForegroundColor Cyan
Write-Host "  https://github.com/Grafdustin/Quotix/actions" -ForegroundColor Blue
Write-Host ""
Write-Host "When ready, download from:" -ForegroundColor Cyan
Write-Host "  https://github.com/Grafdustin/Quotix/releases/tag/v$Version" -ForegroundColor Blue
