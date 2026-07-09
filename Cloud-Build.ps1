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
// 请输入更新日志（无需输入版本号标题，脚本会自动添加 #$Version）
// 以 ## 开头的行作为章节标题（如：## 新功能）
// 其余行作为章节内容
// 以 // 开头的行会被忽略
// 保存并关闭记事本后脚本继续
"@
    Set-Content $tempFile $instructions -Encoding UTF8
    Write-Host "Opening notepad for changelog..." -ForegroundColor Cyan
    Start-Process notepad.exe $tempFile -Wait

    # 读取用户输入（过滤掉空行、以 // 开头的注释行）
    $lines = Get-Content $tempFile -Encoding UTF8 | Where-Object { $_.Trim() -ne "" -and -not $_.StartsWith("//") }

    # 提交日志仅用纯版本号（不带 V / 不带 #）；详细更新说明只写入 latest.yml
    $commitTitle = $Version
    # 其余行作为 commit body
    $commitBody = if ($lines) { $lines -join "`n" } else { "" }
    Remove-Item $tempFile -Force -ErrorAction SilentlyContinue

    if (-not $commitBody) {
        $commitBody = "Release $Version"
    }
} else {
    $commitTitle = $Version
    $commitBody = $CommitMessage
}

Write-Host "Version : $Version" -ForegroundColor Cyan
Write-Host "Title   : $commitTitle" -ForegroundColor Cyan
Write-Host "Body    : $commitBody" -ForegroundColor Cyan
Write-Host ""

# ========== Step 3: 生成 latest.yml ==========
Write-Host "Generating latest.yml..." -ForegroundColor Yellow

$latestYmlPath = Join-Path $ProjectDir "latest.yml"
$ymlContent = "version: $Version`n"
$ymlContent += "changelog: |`n"
if ($commitBody) {
    foreach ($line in ($commitBody -split "`n")) {
        $trimmedLine = $line.Trim()
        if ($trimmedLine -ne "") {
            $ymlContent += "  $trimmedLine`n"
        }
    }
}
Set-Content $latestYmlPath $ymlContent -Encoding UTF8 -NoNewline
Write-Host "latest.yml generated: $latestYmlPath" -ForegroundColor Green
Write-Host $ymlContent -ForegroundColor DarkGray

# ========== Step 4: 更新 csproj 版本号（仅当版本号变化时）==========
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

# ========== Step 5: Git commit & push ==========
Write-Host "Committing and pushing..." -ForegroundColor Yellow
Set-Location $ProjectDir

git add -A

# 检查是否有文件变动
git diff --cached --quiet 2>&1
$hasChanges = $LASTEXITCODE -ne 0

if ($hasChanges) {
    # GitHub 提交日志只保留版本号；详细更新说明已写入 latest.yml，不写入 GitHub commit
    $commitMsgFile = Join-Path $env:TEMP "quotix_git_commit_msg.txt"
    $fullMsg = $commitTitle
    Set-Content -Path $commitMsgFile -Value $fullMsg -Encoding UTF8
    git commit -F "$commitMsgFile"
    if ($LASTEXITCODE -ne 0) { throw "Git commit failed" }
    Write-Host "Git commit successful" -ForegroundColor Green
} else {
    Write-Host "No file changes, skipping commit" -ForegroundColor Yellow
}

# push main（带网络重试：GitHub 偶发连接重置时单次失败不应中断发布）
$pushOk = $false
$maxPushAttempts = 8
for ($attempt = 1; $attempt -le $maxPushAttempts; $attempt++) {
    $gitOutput = git push origin main 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) {
        $pushOk = $true
        break
    }
    # "Everything up-to-date" 视为成功（重跑脚本时本地已领先远程）
    if ($gitOutput -match "Everything up-to-date") {
        $pushOk = $true
        break
    }
    Write-Host "Git push attempt $attempt failed: $gitOutput" -ForegroundColor Yellow
    Start-Sleep -Seconds 3
}
if (-not $pushOk) {
    Write-Host $gitOutput
    throw "Git push failed after $maxPushAttempts attempts"
}
Write-Host "Pushed to main" -ForegroundColor Green

# ========== Step 6: 创建并推送 tag 触发 GitHub Actions ==========
# 检查远程 tag 是否存在（网络调用，失败时不致命）
try {
    $remoteTag = git ls-remote --tags origin "refs/tags/v$Version" 2>$null
} catch {
    $remoteTag = $null
}

# 守卫：本次没有文件变动、且远程 tag 已存在时，无需重复创建/推送，避免触发冗余构建
if (-not $hasChanges -and $remoteTag) {
    Write-Host "版本 $Version 未变更且远程 tag v$Version 已存在，跳过 tag 推送（如需强制重新触发，请先删除该 tag）。" -ForegroundColor Green
} else {
    Write-Host "Creating tag v$Version to trigger GitHub Actions..." -ForegroundColor Yellow

    # 检查本地 tag 是否已存在，若存在则删除（支持重新触发）
    $existingTag = git tag -l "v$Version"
    if ($existingTag) {
        Write-Host "Tag v$Version already exists locally, removing..." -ForegroundColor Yellow
        git tag -d "v$Version" 2>&1 | Out-Null
    }
    # 检查远程 tag 是否存在（上面已获取，避免重复网络调用）
    if ($remoteTag) {
        Write-Host "Tag v$Version exists on remote, removing..." -ForegroundColor Yellow
        git push origin --delete "v$Version" 2>&1 | Out-Null
    }

    # 创建并推送 tag（带网络重试：GitHub 偶发连接重置时单次失败不应中断发布）
    git tag "v$Version"
    $tagOk = $false
    for ($attempt = 1; $attempt -le $maxPushAttempts; $attempt++) {
        $tagPushOutput = git push origin "v$Version" 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0) {
            $tagOk = $true
            break
        }
        Write-Host "Git tag push attempt $attempt failed: $tagPushOutput" -ForegroundColor Yellow
        Start-Sleep -Seconds 3
    }
    if (-not $tagOk) {
        Write-Host $tagPushOutput
        throw "Failed to push tag v$Version after $maxPushAttempts attempts"
    }

    Write-Host ""
    Write-Host "=== Build triggered! ===" -ForegroundColor Green
    Write-Host "Watch progress:" -ForegroundColor Cyan
    Write-Host "  https://github.com/Grafdustin/Quotix/actions" -ForegroundColor Blue
    Write-Host ""
    Write-Host "When ready, download from:" -ForegroundColor Cyan
    Write-Host "  https://github.com/Grafdustin/Quotix/releases/tag/v$Version" -ForegroundColor Blue
}
