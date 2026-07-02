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
    
    $csprojContent = Get-Content $csprojPath -Encoding UTF8
    $csprojContent = $csprojContent -replace '(?<=<Version>)[^<]+', $Version
    $csprojContent = $csprojContent -replace '(?<=<AssemblyVersion>)[^<]+', "$Version.0"
    $csprojContent = $csprojContent -replace '(?<=<FileVersion>)[^<]+', "$Version.0"
    $csprojContent = $csprojContent -replace '(?<=<InformationalVersion>)[^<]+', $Version
    Set-Content -Path $csprojPath -Value $csprojContent -Encoding UTF8
    
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
$skipRelease = $false

# Temporarily disable "Stop" to allow gh to return non-zero exit codes
$oldErrorAction = $ErrorActionPreference
$ErrorActionPreference = "Continue"

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Host "Warning: GitHub CLI not installed" -ForegroundColor Yellow
    $skipRelease = $true
}

if (-not $skipRelease) {
    & gh auth status 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Warning: Not logged into GitHub CLI" -ForegroundColor Yellow
        $skipRelease = $true
    }
}

if (-not $skipRelease) {
    if (-not $installerPath -or -not (Test-Path $installerPath)) {
        Write-Host "Warning: Installer not found, skipping release" -ForegroundColor Yellow
        $skipRelease = $true
    }
}

if (-not $skipRelease) {
    $tag = "v$Version"

    Write-Host "Creating release $tag..." -ForegroundColor Cyan

    $releaseNotes = "## Release Notes`r`n`r`n$CommitMessage"

    & gh release create $tag $installerPath `
        --title "Quotix $tag" `
        --notes $releaseNotes `
        --repo $repoName 2>&1

    if ($LASTEXITCODE -eq 0) {
        Write-Host "Release created successfully!" -ForegroundColor Green
    }
    else {
        Write-Host "Release may already exist, switching to upload mode..." -ForegroundColor Yellow

        # Do not delete release, just overwrite file (safer)
        & gh release upload $tag $installerPath --clobber --repo $repoName

        if ($LASTEXITCODE -eq 0) {
            Write-Host "Release asset updated successfully!" -ForegroundColor Green
        }
        else {
            Write-Host "Error: Failed to create/update release" -ForegroundColor Red
            $ErrorActionPreference = $oldErrorAction
            exit 1
        }
    }

    # ========== version.json ==========
    $versionInfo = @{
        version      = $Version
        releaseDate  = Get-Date -Format "yyyy-MM-dd"
        releaseNotes = $CommitMessage
        downloadUrl  = "https://github.com/$repoName/releases/download/v$Version/Quotix_Setup_$Version.exe"
        fileSize     = "$([math]::Round((Get-Item $installerPath).Length / 1MB, 2))MB"
        mandatory    = $false
        whatsNew     = @($CommitMessage)
    }

    $versionJsonPath = Join-Path $ProjectDir "Resources\version.json"
    $versionInfo | ConvertTo-Json | Set-Content $versionJsonPath -Encoding UTF8

    if (-not $SkipGit) {
        git -C $ProjectDir add "$versionJsonPath"
        git -C $ProjectDir commit -m "Update version.json (v$Version)"
        git -C $ProjectDir push origin main
    }
}

# Restore original ErrorActionPreference
$ErrorActionPreference = $oldErrorAction

Write-Host ""
Write-Host "=== Release process completed ===" -ForegroundColor Green
if ($installerPath) {
    Write-Host "Installer: $installerPath" -ForegroundColor Cyan
}
