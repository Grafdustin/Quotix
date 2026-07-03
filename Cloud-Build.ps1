# Cloud-Build.ps1
# 一键触发 GitHub Actions 云端构建发布
# 用法：.\Cloud-Build.ps1                    # 自动从 csproj 读取版本号
# 用法：.\Cloud-Build.ps1 -Version "1.0.69"  # 指定版本号
# 推荐用 PowerShell 7 运行：pwsh -File .\Cloud-Build.ps1

param(
    [string]$Version,
    [string]$CommitMessage
)

$ErrorActionPreference = "Stop"
# 设置控制台输出编码为 UTF-8，解决中文乱码
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ProjectDir = Split-Path $MyInvocation.MyCommand.Path

# ========== Step 1: 读取/设置版本号 ==========
Write-Host "=== Quotix Cloud Build ===" -ForegroundColor Cyan
Write-Host ""

if (-not $Version) {
    [xml]$csprojXml = Get-Content (Join-Path $ProjectDir "QuotixDesktop.csproj")
    $currentVersion = $csprojXml.Project.PropertyGroup.Version
    Write-Host "Current version: $currentVersion" -ForegroundColor Yellow
    $inputVersion = Read-Host "Enter new version (Enter to keep $currentVersion)"
    if ($inputVersion) { $Version = $inputVersion } else { $Version = $currentVersion }
}

# 验证版本号格式
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid version format: $Version (expected x.y.z)"
}

# ========== Step 2: 输入提交信息 ==========
if (-not $CommitMessage) {
    $tempFile = Join-Path $env:TEMP "quotix_cloudbuild_msg.txt"
    $instructions = @"
// 请输入更新日志
// 以 # 开头的行作为章节标题（如：# 新功能）
// 以 // 开头的行会被忽略
// 保存并关闭记事本后继续
"@
    Set-Content $tempFile $instructions -Encoding UTF8
    Write-Host "Opening notepad for changelog..." -ForegroundColor Cyan
    Start-Process notepad.exe $tempFile -Wait

    $lines = Get-Content $tempFile -Encoding UTF8 | Where-Object { $_.Trim() -ne "" -and -not $_.StartsWith("//") }
    $CommitMessage = $lines -join "`n"
    Remove-Item $tempFile -Force -ErrorAction SilentlyContinue

    if (-not $CommitMessage) {
        throw "Commit message cannot be empty"
    }
}

Write-Host "Version : $Version" -ForegroundColor Green
Write-Host "Message : $CommitMessage" -ForegroundColor Green
Write-Host ""

# ========== Step 3: 更新 csproj 版本号 ==========
Write-Host "Updating csproj version..." -ForegroundColor Yellow
$csprojPath = Join-Path $ProjectDir "QuotixDesktop.csproj"
[xml]$csproj = Get-Content $csprojPath
$csproj.Project.PropertyGroup.Version = $Version
$csproj.Project.PropertyGroup.InformationalVersion = $Version
$csproj.Save($csprojPath)
Write-Host "csproj updated to $Version" -ForegroundColor Green

# ========== Step 4: Git commit & push ==========
Write-Host "Committing and pushing..." -ForegroundColor Yellow
Set-Location $ProjectDir

git add -A

# 解析提交信息：第一行做标题，其余做 body
$allLines = ($CommitMessage -split "`n") | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ -ne "" }
$title = if ($allLines.Count -gt 0) { $allLines[0] } else { "Update" }
$body = if ($allLines.Count -gt 1) { ($allLines | Select-Object -Skip 1) -join "`n" } else { "" }

# 用临时文件传递提交信息，避免 PowerShell 中文编码问题
$commitMsgFile = Join-Path $env:TEMP "quotix_git_commit_msg.txt"
if ($body) {
    $fullMsg = $title + "`n`n" + $body
    Set-Content -Path $commitMsgFile -Value $fullMsg -Encoding UTF8
    git commit -F "$commitMsgFile"
} else {
    Set-Content -Path $commitMsgFile -Value $title -Encoding UTF8
    git commit -F "$commitMsgFile"
}

if ($LASTEXITCODE -ne 0) {
    throw "Git commit failed"
}
Write-Host "Git commit successful" -ForegroundColor Green

# push：用 2>&1 捕获但不让 PowerShell 误判为错误
$gitOutput = git push origin main 2>&1
Write-Host $gitOutput
if ($LASTEXITCODE -ne 0) {
    throw "Git push failed"
}
Write-Host "Pushed to main" -ForegroundColor Green

# ========== Step 5: 创建并推送 tag 触发 GitHub Actions ==========
Write-Host "Creating tag v$Version to trigger GitHub Actions..." -ForegroundColor Yellow

git tag "v$Version" 2>&1 | Out-Null
$tagPushOutput = git push origin "v$Version" 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Warning: tag push failed, retrying..." -ForegroundColor Yellow
    git tag -d "v$Version" 2>$null
    git push origin --delete "v$Version" 2>&1 | Out-Null
    git tag "v$Version" 2>&1 | Out-Null
    git push origin "v$Version" 2>&1 | Out-Null
}

Write-Host ""
Write-Host "=== Cloud build triggered! ===" -ForegroundColor Green
Write-Host "Watch progress: https://github.com/Grafdustin/Quotix/actions" -ForegroundColor Cyan
Write-Host "Release will be at: https://github.com/Grafdustin/Quotix/releases/tag/v$Version" -ForegroundColor Cyan
Write-Host ""
Write-Host "Typical build time: ~2 minutes" -ForegroundColor Gray
