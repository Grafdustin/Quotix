# Build-Release.ps1
# Standard release process: Version increment -> Git commit -> Compile -> Build installer -> Create GitHub Release

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
    
    # 方案3：编译成功后自动打 Git tag（版本完全由 Git 控制）
    if (-not $SkipGit) {
        # 检查 tag 是否已存在
        $existingTag = & git -C $ProjectDir tag -l "v$Version" 2>$null
        if ($existingTag) {
            Write-Host "Warning: Tag v$Version already exists, deleting and re-creating..." -ForegroundColor Yellow
            git -C $ProjectDir tag -d "v$Version" 2>$null
        }
        git -C $ProjectDir tag -a "v$Version" -m "Release v$Version"
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Git tag v$Version created" -ForegroundColor Green
        }
    }
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

Write-Host "Ensuring release exists: $tag" -ForegroundColor Cyan

# ✔ 关键优化：先确保 release 存在（不存在就创建）
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

# ✔ 永远走 upload（核心稳定点）
Write-Host "Uploading installer..." -ForegroundColor Cyan

& $ghPath release upload $tag $installerPath `
    --repo $repoName `
    --clobber

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Failed to upload asset" -ForegroundColor Red
    exit 1
}

Write-Host "Release ready!" -ForegroundColor Green

# ========== version.json（标准稳定版格式，只在成功后写） ==========
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

if (-not $SkipGit) {
    git -C $ProjectDir add "$versionJsonPath"
    git -C $ProjectDir commit -m "Update version.json (v$Version)"
    # 临时降低错误级别避免 git push stderr 触发 Stop
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    git -C $ProjectDir push origin main 2>&1
    $ErrorActionPreference = $prevEAP
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Warning: git push failed, please push manually" -ForegroundColor Yellow
    } else {
        Write-Host "Pushed version.json to remote" -ForegroundColor Green
    }
    
    # 推送 Git tag（方案3：版本由 Git 控制）
    Write-Host "Pushing tag v$Version..." -ForegroundColor Cyan
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    git -C $ProjectDir push origin "refs/tags/v$Version" 2>&1
    $ErrorActionPreference = $prevEAP
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Tag v$Version pushed to remote" -ForegroundColor Green
    } else {
        Write-Host "Warning: Tag push failed, please push manually: git push origin v$Version" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "=== Release process completed ===" -ForegroundColor Green
if ($installerPath) {
    Write-Host "Installer: $installerPath" -ForegroundColor Cyan
}
