# Build-Release.ps1
# Standard release process:
#   1. Git commit (source changes)
#   2. Version increment (in csproj)
#   3. Build project + installer
#   4. GitHub Release
#   5. version.json (commit + push)

param(
    [string]$CommitMessage,
    
    [string]$Version,
    
    [switch]$SkipGit,
    
    [switch]$SkipBuild,
    
    [switch]$NoAutoIncrement,
    
    [switch]$Force,
    
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Continue"
$ProjectDir = Resolve-Path "$PSScriptRoot"
$InstallerDir = Join-Path $ProjectDir "Installer"
$CsprojPath = Join-Path $ProjectDir "QuotixDesktop.csproj"

try {
# 交互式输入 CommitMessage（右键运行时没有参数）
if (-not $CommitMessage) {
    $CommitMessage = Read-Host "请输入提交信息"
}
if (-not $CommitMessage) {
    throw "提交信息不能为空"
}

Write-Host "=== Quotix Release Process ===" -ForegroundColor Cyan
Write-Host "Commit message: $CommitMessage" -ForegroundColor Gray
if ($Version) {
    Write-Host "Version: $Version" -ForegroundColor Gray
}
Write-Host ""

# ========== Step 1: Git Commit ==========
if (-not $SkipGit) {
    Write-Host "Step 1/5: Git commit..." -ForegroundColor Yellow
    
    $isGitRepo = Test-Path (Join-Path $ProjectDir ".git")
    if (-not $isGitRepo) {
        Write-Host "Warning: Not a Git repository, skipping Git operations" -ForegroundColor Yellow
    } else {
        $gitStatus = git -C $ProjectDir status --porcelain
        if ($gitStatus) {
            git -C $ProjectDir add -A
            git -C $ProjectDir commit -m "$CommitMessage"
            if ($LASTEXITCODE -ne 0) {
                throw "Git commit failed"
            }
            Write-Host "Git commit successful" -ForegroundColor Green
        } else {
            Write-Host "No changes to commit" -ForegroundColor Gray
        }
    }
} else {
    Write-Host "Step 1/5: Skipping Git commit" -ForegroundColor Gray
}

# ========== Step 2: Update Version ==========
Write-Host ""
Write-Host "Step 2/5: Checking version..." -ForegroundColor Yellow

# 从 csproj 读取当前版本
[xml]$csprojXml = Get-Content $CsprojPath
$currentVersion = $csprojXml.Project.PropertyGroup.Version

if (-not $Version -and -not $NoAutoIncrement) {
    if ($currentVersion -match '^(\d+)\.(\d+)\.(\d+)$') {
        $major = [int]$Matches[1]
        $minor = [int]$Matches[2]
        $patch = [int]$Matches[3]
        $patch++
        $Version = "$major.$minor.$patch"
        Write-Host "Auto-incremented version: $currentVersion -> $Version" -ForegroundColor Green
    } else {
        Write-Host "Warning: Cannot parse current version '$currentVersion', using default" -ForegroundColor Yellow
        $Version = "1.0.1"
    }
}

if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Invalid version format, should be x.y.z (e.g. 1.0.1)"
    }
    
    if ($Version -eq $currentVersion) {
        Write-Host "Warning: Version $Version is same as current version" -ForegroundColor Yellow
        $existingInstaller = Join-Path $InstallerDir "Out\Quotix_Setup_$Version.exe"
        if (Test-Path $existingInstaller) {
            Write-Host "Warning: Installer already exists: $existingInstaller" -ForegroundColor Yellow
            if (-not $Force) {
                $confirm = Read-Host "Continue and overwrite? (y/N)"
                if ($confirm -ne "y" -and $confirm -ne "Y") {
                    Write-Host "Cancelled" -ForegroundColor Gray
                    return
                }
            }
        }
    }
    
    # 写回 csproj（只更新 Version / FileVersion / InformationalVersion，不动 AssemblyVersion）
    Write-Host "Writing version to csproj: $currentVersion -> $Version" -ForegroundColor Green
    
    $csprojXml = [xml](Get-Content $CsprojPath)
    $csprojXml.Project.PropertyGroup.Version = $Version
    $csprojXml.Project.PropertyGroup.FileVersion = "$Version.0"
    $csprojXml.Project.PropertyGroup.InformationalVersion = $Version
    $csprojXml.Save($CsprojPath)
    
    # 提交版本变更
    if (-not $SkipGit) {
        git -C $ProjectDir add "$CsprojPath"
        git -C $ProjectDir commit -m "Bump version to $Version"
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Warning: Version change commit failed (no changes?)" -ForegroundColor Yellow
        }
    }
    
    Write-Host "Version updated to: $Version" -ForegroundColor Green
} else {
    Write-Host "Keeping current version: $currentVersion" -ForegroundColor Gray
    $Version = $currentVersion
}

# ========== Step 3: Build Project ==========
if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "Step 3/5: Building project..." -ForegroundColor Yellow
    
    dotnet publish "$CsprojPath" -c $Configuration -r win-x64 --self-contained true -p:Version=$Version
    
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed"
    }
    Write-Host "Build successful" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "Step 3/5: Skipping build" -ForegroundColor Gray
}

# ========== Step 4: Build Installer ==========
Write-Host ""
Write-Host "Step 4/5: Building installer..." -ForegroundColor Yellow

$installerPath = $null
$buildInstallerScript = Join-Path $InstallerDir "Build-Installer.ps1"
if (Test-Path $buildInstallerScript) {
    & "$buildInstallerScript" -Configuration $Configuration -SkipBuild -Version $Version
    
    if ($LASTEXITCODE -ne 0) {
        throw "Installer build failed"
    }
    
    $outputDir = Join-Path $InstallerDir "Out"
    if (Test-Path $outputDir) {
        $installer = Get-ChildItem (Join-Path $outputDir "Quotix_Setup_*.exe") | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($installer) {
            $installerPath = $installer.FullName
        }
    }
} else {
    Write-Host "Warning: Cannot find Build-Installer.ps1, skipping installer build" -ForegroundColor Yellow
}

# ========== Step 5: Create GitHub Release ==========
Write-Host ""
Write-Host "Step 5/5: Creating GitHub Release..." -ForegroundColor Yellow

$repoName = "Grafdustin/Quotix"
$tag = "v$Version"

# GitHub CLI 路径检测
$ghPath = "gh"
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    $possiblePaths = @(
        "D:\GitHub CLI\gh.exe",
        "$env:LOCALAPPDATA\GitHub CLI\gh.exe",
        "$env:ProgramFiles\GitHub CLI\gh.exe"
    )
    
    foreach ($path in $possiblePaths) {
        if (Test-Path $path) {
            $ghPath = $path
            Write-Host "Found GitHub CLI at: $path" -ForegroundColor Green
            break
        }
    }
}

# 检查 gh
if (-not (Get-Command $ghPath -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI not found"
}

# 登录检查
& $ghPath auth status 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "GitHub CLI not authenticated"
}

# 安装包检查
if (-not $installerPath -or -not (Test-Path $installerPath)) {
    throw "Installer not found"
}

Write-Host "Ensuring release exists: $tag" -ForegroundColor Cyan

# 确保 release 存在（不存在就创建）
& $ghPath release view $tag --repo $repoName 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Release not found, creating..." -ForegroundColor Yellow

    & $ghPath release create $tag `
        --title "Quotix $tag" `
        --notes "$CommitMessage" `
        --repo $repoName

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create release"
    }
}

# 上传安装包
Write-Host "Uploading installer..." -ForegroundColor Cyan

& $ghPath release upload $tag $installerPath `
    --repo $repoName `
    --clobber

if ($LASTEXITCODE -ne 0) {
    throw "Failed to upload asset"
}

Write-Host "Release ready!" -ForegroundColor Green

# ========== version.json（Release 成功后才生成） ==========
Write-Host ""
Write-Host "Generating version.json..." -ForegroundColor Yellow

$fileSizeBytes = (Get-Item $installerPath).Length

# 从版本号计算 build 值（如 1.0.15 → 1015）
$buildNumber = [int]($Version -replace '\.', '')

$versionInfo = @{
    version      = $Version
    build        = $buildNumber
    releaseDate  = Get-Date -Format "yyyy-MM-dd"
    downloadUrl  = "https://github.com/$repoName/releases/download/v$Version/Quotix_Setup_$Version.exe"
    fileSize     = $fileSizeBytes
    mandatory    = $false
    changelog    = @($CommitMessage)
}

$versionJsonPath = Join-Path $ProjectDir "Resources\version.json"
$versionInfo | ConvertTo-Json -Depth 10 | Set-Content $versionJsonPath -Encoding UTF8
Write-Host "version.json written: v$Version (build $buildNumber, $fileSizeBytes bytes)" -ForegroundColor Green

# 提交并推送 version.json
if (-not $SkipGit) {
    git -C $ProjectDir add "$versionJsonPath"
    git -C $ProjectDir commit -m "Update version.json (v$Version)"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Warning: version.json commit failed (no changes?)" -ForegroundColor Yellow
    }
    
    # 推送到远程
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    git -C $ProjectDir push origin main 2>&1
    $ErrorActionPreference = $prevEAP
    Write-Host "version.json pushed to remote" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== Release process completed ===" -ForegroundColor Green
if ($installerPath) {
    Write-Host "Installer: $installerPath" -ForegroundColor Cyan
}
} catch {
    Write-Host ""
    Write-Host "❌ 脚本执行失败：" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
} finally {
    Write-Host ""
    Read-Host "按回车退出"
}
