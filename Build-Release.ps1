# Build-Release.ps1
# Standard release process: Git commit -> Compile -> Build installer -> Create GitHub Release

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
                Write-Host "Error: Git commit failed" -ForegroundColor Red
                exit 1
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

$csprojPath = Join-Path $ProjectDir "QuotixDesktop.csproj"
[xml]$csproj = Get-Content $csprojPath
$currentVersion = $csproj.Project.PropertyGroup.Version

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
    
    Write-Host "Updating version: $currentVersion -> $Version" -ForegroundColor Green
    
    # ✔ 使用 XML DOM 更新版本号（更干净、更可靠）
    $csproj.Project.PropertyGroup.Version = $Version
    $csproj.Project.PropertyGroup.AssemblyVersion = "$Version.0"
    $csproj.Project.PropertyGroup.FileVersion = "$Version.0"
    $csproj.Project.PropertyGroup.InformationalVersion = $Version
    $csproj.Save($csprojPath)
    
    if (-not $SkipGit) {
        git -C $ProjectDir add "$csprojPath"
        git -C $ProjectDir commit -m "Update version to $Version"
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
    
    dotnet publish "$ProjectDir\QuotixDesktop.csproj" -c $Configuration -r win-x64 --self-contained true
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Build failed" -ForegroundColor Red
        exit 1
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
    & "$buildInstallerScript" -Configuration $Configuration -SkipBuild
    
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

# ========== version.json（只在成功后写） ==========
# 生成 build 号（如 1.0.7 → 107）
$build = [int]($Version -replace '\.', '')

# 获取文件大小（字节数）
$fileSizeBytes = (Get-Item $installerPath).Length

# ✔ 使用 [Ordered]@{} 保证 JSON 字段顺序（最佳实践）
$versionInfo = [Ordered]@{
    version      = $Version
    build        = $build
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
    
    # 尝试 Git 推送
    git -C $ProjectDir push origin main 2>$null
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Warning: Git push failed, using GitHub API to update version.json..." -ForegroundColor Yellow
        
        # 使用 GitHub API 直接更新 version.json
        $ghPath = (Get-Command "gh" -ErrorAction SilentlyContinue).Source
        if (-not $ghPath) {
            $ghPath = "D:\GitHub CLI\gh.exe"
        }
        
        $content = Get-Content $versionJsonPath -Raw
        $encodedContent = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($content))
        $fileSha = & $ghPath api "repos/$repoName/contents/Resources/version.json" --jq ".sha" 2>$null
        
        $body = @{
            message = "Update version.json (v$Version)"
            content = $encodedContent
            sha     = $fileSha
        } | ConvertTo-Json
        
        $tempFile = Join-Path $ProjectDir "temp_version_update.json"
        $body | Out-File -FilePath $tempFile -Encoding UTF8
        
        & $ghPath api "repos/$repoName/contents/Resources/version.json" --method PUT --input $tempFile 2>$null
        
        Remove-Item $tempFile -Force 2>$null
        
        Write-Host "version.json updated via GitHub API" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "=== Release process completed ===" -ForegroundColor Green
if ($installerPath) {
    Write-Host "Installer: $installerPath" -ForegroundColor Cyan
}
