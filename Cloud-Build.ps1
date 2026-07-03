# Cloud-Build.ps1
# 一键触发 GitHub Actions 云端构建发布
# 用法：.\Cloud-Build.ps1                    # 自动从 csproj 读取版本号
# 用法：.\Cloud-Build.ps1 -Version "1.0.69"  # 指定版本号

param(
    [string]$Version,
    [string]$CommitMessage
)

$ErrorActionPreference = "Stop"
$ProjectDir = Split-Path $MyInvocation.MyCommand.Path

# ========== Step 1: 读取/设置版本号 ==========
Write-Host "=== Quotix Cloud Build ===" -ForegroundColor Cyan
Write-Host ""

if (-not $Version) {
    [xml]$csprojXml = Get-Content (Join-Path $ProjectDir "QuotixDesktop.csproj")
    $Version = $csprojXml.Project.PropertyGroup.Version
    Write-Host "Current version: $Version" -ForegroundColor Yellow
    $inputVersion = Read-Host "Enter new version (Enter to keep $Version)"
    if ($inputVersion) { $Version = $inputVersion }
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
# 提交信息：第一行标题，后续行作为 body
$commitLines = $CommitMessage -split "`n" | Where-Object { $_.Trim() -ne "" }
$commitTitle = $commitLines[0].Trim()
$commitBody = ($commitLines | Select-Object -Skip 1) -join "`n"

if ($commitBody) {
    git commit -m "$commitTitle" -m "$commitBody"
} else {
    git commit -m "$commitTitle"
}

if ($LASTEXITCODE -ne 0) {
    throw "Git commit failed"
}

git push origin main 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Git push failed"
}
Write-Host "Pushed to main" -ForegroundColor Green

# ========== Step 5: 创建并推送 tag 触发 GitHub Actions ==========
Write-Host "Creating tag v$Version to trigger GitHub Actions..." -ForegroundColor Yellow

git tag "v$Version"
git push origin "v$Version" 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Warning: git push tag failed, maybe tag already exists?" -ForegroundColor Yellow
    git tag -d "v$Version" 2>$null
    git push origin --delete "v$Version" 2>&1
    git tag "v$Version"
    git push origin "v$Version" 2>&1
}

Write-Host ""
Write-Host "=== Cloud build triggered! ===" -ForegroundColor Green
Write-Host "Watch progress: https://github.com/Grafdustin/Quotix/actions" -ForegroundColor Cyan
Write-Host "Release will be at: https://github.com/Grafdustin/Quotix/releases/tag/v$Version" -ForegroundColor Cyan
Write-Host ""
Write-Host "Typical build time: ~2 minutes" -ForegroundColor Gray
