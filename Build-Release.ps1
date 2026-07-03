# Build-Release.ps1
# Standard release process:
#   1. Version increment (git tag driven)
#   2. Git commit (source changes)
#   3. Build + Installer
#   4. Generate version.json + commit + push to main  ← 关键：在 Tag 之前
#   4.5. Git tag (on commit WITH version.json) + push
#   5. GitHub Release (based on tag)
# Key: version.json is committed BEFORE tag and release, ensuring synchronization

param(
    [Parameter(Mandatory=$true)]
    [string]$CommitMessage,
    
    [string]$Version,
    
    [switch]$SkipGit,
    
    [switch]$SkipBuild,
    
    [switch]$NoAutoIncrement,
    
    [switch]$Force,
    
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ProjectDir = Resolve-Path "$PSScriptRoot"
$InstallerDir = Join-Path $ProjectDir "Installer"

Write-Host "=== Quotix Release Process ===" -ForegroundColor Cyan
Write-Host "Commit message: $CommitMessage" -ForegroundColor Gray
if ($Version) {
    Write-Host "Version: $Version" -ForegroundColor Gray
}
Write-Host ""

# ========== Step 1: Update Version（必须在 Git 提交之前，因为可能会修改文件） ==========
Write-Host "Step 1/5: Checking version..." -ForegroundColor Yellow

# 拉取远程标签以确保最新
& git -C $ProjectDir fetch --tags 2>$null

# 从最新 git tag 获取当前版本（格式：v1.0.27），不再依赖 csproj
$latestTag = & git -C $ProjectDir tag --sort=-v:refname 2>$null | Select-Object -First 1
if ($latestTag -match '^v(\d+\.\d+\.\d+)$') {
    $currentVersion = $Matches[1]
    Write-Host "Latest git tag: $latestTag -> version $currentVersion" -ForegroundColor Cyan
} else {
    # 回退：从 csproj 读取
    $csprojPath = Join-Path $ProjectDir "QuotixDesktop.csproj"
    [xml]$csprojXml = Get-Content $csprojPath
    $currentVersion = $csprojXml.Project.PropertyGroup.Version
    Write-Host "No git tag found, using csproj version: $currentVersion" -ForegroundColor Yellow
}

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
        Write-Host "Error: Invalid version format, should be x.y.z (e.g. 1.0.1)" -ForegroundColor Red
        exit 1
    }
    
    if ($Version -eq $currentVersion) {
        Write-Host "Warning: Version $Version is same as current version" -ForegroundColor Yellow
        $existingInstaller = Join-Path $InstallerDir "Out\Quotix_Setup_$Version.exe"
        if (Test-Path $existingInstaller) {
            Write-Host "Warning: Installer already exists: $existingInstaller" -ForegroundColor Yellow
            if ($Force) {
                Write-Host "Force mode: Overwriting existing installer..." -ForegroundColor Yellow
            } else {
                $confirm = Read-Host "Continue and overwrite? (y/N)"
                if ($confirm -ne "y" -and $confirm -ne "Y") {
                    Write-Host "Cancelled" -ForegroundColor Gray
                    exit 0
                }
            }
        }
    }
    
    Write-Host "Release version: $Version (git tag 驱动，通过 -p:Version 传参)" -ForegroundColor Green
} else {
    Write-Host "Keeping current version: $currentVersion" -ForegroundColor Gray
    $Version = $currentVersion
}

# ========== Step 2: Git Commit（版本递增后提交，因为递增可能修改了 csproj/version.json 等文件） ==========
Write-Host ""
if (-not $SkipGit) {
    Write-Host "Step 2/5: Git commit..." -ForegroundColor Yellow
    
    $isGitRepo = Test-Path (Join-Path $ProjectDir ".git")
    if (-not $isGitRepo) {
        Write-Host "Warning: Not a Git repository, skipping Git operations" -ForegroundColor Yellow
    } else {
        $gitStatus = git -C $ProjectDir status --porcelain
        if ($gitStatus) {
            git -C $ProjectDir add -A
            git -C $ProjectDir commit -m "$CommitMessage"
            if ($LASTEXITCODE -ne 0) {
                Write-Host "Error: Git commit failed" -ForegroundColor Red
                exit 1
            }
            Write-Host "Git commit successful" -ForegroundColor Green
        } else {
            Write-Host "No changes to commit" -ForegroundColor Gray
        }
    }
} else {
    Write-Host "Step 2/5: Skipping Git commit" -ForegroundColor Gray
}

# ========== Step 3: Build Project ==========
if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "Step 3/5: Building project..." -ForegroundColor Yellow
    
    dotnet publish "$ProjectDir\QuotixDesktop.csproj" -c $Configuration -r win-x64 --self-contained true -p:Version=$Version
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Build failed" -ForegroundColor Red
        exit 1
    }
    Write-Host "Build successful" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "Step 3/5: Skipping build" -ForegroundColor Gray
}

# ========== Step 3: Build + Installer ==========
Write-Host ""
Write-Host "Building installer..." -ForegroundColor Yellow

$installerPath = $null
$buildInstallerScript = Join-Path $InstallerDir "Build-Installer.ps1"
if (Test-Path $buildInstallerScript) {
    & "$buildInstallerScript" -Configuration $Configuration -SkipBuild -Version $Version
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Installer build failed" -ForegroundColor Red
        exit 1
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

# ========== Step 4: Generate version.json + Push（必须在 Tag 和 Release 之前） ==========
Write-Host ""
Write-Host "Step 4/5: Generating version.json..." -ForegroundColor Yellow

$repoName = "Grafdustin/Quotix"
$fileSizeBytes = (Get-Item $installerPath).Length

# 从版本号计算 build 值（如 1.0.25 → 1025）
$buildNumber = [int]($Version -replace '\.', '').Substring(0, [Math]::Min(4, ($Version -replace '\.', '').Length))

$versionInfo = [Ordered]@{
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

# 验证 version.json 内容
$jsonContent = Get-Content $versionJsonPath -Raw | ConvertFrom-Json
Write-Host "  verified: version=$($jsonContent.version), changelog=$($jsonContent.changelog -join '; ')" -ForegroundColor Gray

if (-not $SkipGit) {
    # 提交 version.json
    git -C $ProjectDir add "$versionJsonPath"
    git -C $ProjectDir commit -m "Update version.json (v$Version)"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Warning: version.json commit failed (no changes?)" -ForegroundColor Yellow
    } else {
        Write-Host "version.json committed" -ForegroundColor Green
    }
    
    # 推送 version.json 到远程（确保 raw.githubusercontent.com 能读到）
    Write-Host "Pushing version.json to remote..." -ForegroundColor Cyan
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    git -C $ProjectDir push origin main 2>&1
    $ErrorActionPreference = $prevEAP
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Failed to push version.json, aborting release" -ForegroundColor Red
        exit 1
    }
    Write-Host "version.json pushed to remote" -ForegroundColor Green
    
    # 验证远程 HEAD 确认推送成功
    $remoteHead = & git -C $ProjectDir rev-parse HEAD 2>$null
    Write-Host "  remote HEAD: $($remoteHead.Substring(0, 7))" -ForegroundColor Gray
}

# ========== Step 4.5: Create Git Tag（在 version.json 的 commit 上打 tag） ==========
Write-Host ""
Write-Host "Step 4.5/5: Creating Git tag..." -ForegroundColor Yellow

if (-not $SkipGit) {
    $existingTag = & git -C $ProjectDir tag -l "v$Version" 2>$null
    if ($existingTag) {
        Write-Host "Warning: Tag v$Version already exists, deleting and re-creating..." -ForegroundColor Yellow
        git -C $ProjectDir tag -d "v$Version" 2>$null
        git -C $ProjectDir push origin --delete "refs/tags/v$Version" 2>$null
    }
    git -C $ProjectDir tag -a "v$Version" -m "Release v$Version"
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Git tag v$Version created (on commit with version.json)" -ForegroundColor Green
    }
    
    # 推送 tag
    Write-Host "Pushing tag v$Version..." -ForegroundColor Cyan
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    git -C $ProjectDir push origin "refs/tags/v$Version" 2>&1
    $ErrorActionPreference = $prevEAP
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Tag v$Version pushed to remote" -ForegroundColor Green
    } else {
        Write-Host "Error: Tag push failed, aborting release" -ForegroundColor Red
        exit 1
    }
}

# ========== Step 5: Create GitHub Release（此时 Tag 已指向含 version.json 的 commit） ==========
Write-Host ""
Write-Host "Step 5/5: Creating GitHub Release..." -ForegroundColor Yellow

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
    Write-Host "Error: GitHub CLI not found" -ForegroundColor Red
    exit 1
}

# 登录检查
& $ghPath auth status 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: GitHub CLI not authenticated" -ForegroundColor Red
    exit 1
}

# 安装包检查
if (-not $installerPath -or -not (Test-Path $installerPath)) {
    Write-Host "Error: Installer not found" -ForegroundColor Red
    exit 1
}

Write-Host "Tag: $tag (points to commit with version.json)" -ForegroundColor Cyan

# 创建 Release（如果不存在）
& $ghPath release view $tag --repo $repoName 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Release not found, creating..." -ForegroundColor Yellow

    & $ghPath release create $tag `
        --title "Quotix $tag" `
        --notes "$CommitMessage" `
        --repo $repoName

    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Failed to create release" -ForegroundColor Red
        exit 1
    }
}

# 上传安装包
Write-Host "Uploading installer..." -ForegroundColor Cyan

& $ghPath release upload $tag $installerPath `
    --repo $repoName `
    --clobber

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Failed to upload asset" -ForegroundColor Red
    exit 1
}

Write-Host "Release ready!" -ForegroundColor Green

Write-Host ""
Write-Host "=== Release process completed ===" -ForegroundColor Green
Write-Host "version.json: v$Version (pushed to main before release)" -ForegroundColor Cyan
if ($installerPath) {
    Write-Host "Installer: $installerPath" -ForegroundColor Cyan
}
