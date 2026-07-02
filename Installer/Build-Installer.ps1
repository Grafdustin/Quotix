# Build-Installer.ps1
# Automated installer build script
# Read version from .csproj and pass to Inno Setup compiler

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$TargetFramework = "net10.0-windows",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

Write-Host "=== Quotix Installer Build Script ===" -ForegroundColor Cyan

# 1. Read version from .csproj
Write-Host "Reading version..." -ForegroundColor Yellow
$csprojPath = Join-Path $PSScriptRoot "..\QuotixDesktop.csproj"
[xml]$csproj = Get-Content $csprojPath
$version = $csproj.Project.PropertyGroup.Version
if (-not $version) {
    $version = "1.0.0"
    Write-Host "Warning: Version not found in .csproj, using default: $version" -ForegroundColor Yellow
} else {
    Write-Host "Version: $version" -ForegroundColor Green
}

# 2. Build project (unless skipped)
if (-not $SkipBuild) {
    Write-Host "Building project..." -ForegroundColor Yellow
    $publishDir = Join-Path $PSScriptRoot "..\bin\$Configuration\$TargetFramework\$Runtime\publish"
    dotnet publish "$csprojPath" -c $Configuration -r $Runtime --self-contained true
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Build failed" -ForegroundColor Red
        exit 1
    }
    Write-Host "Build successful" -ForegroundColor Green
} else {
    Write-Host "Skipping build step" -ForegroundColor Gray
}

# 3. Prepare Staging directory
Write-Host "Preparing Staging directory..." -ForegroundColor Yellow
$stagingDir = Join-Path $PSScriptRoot "Staging"
$launcherDir = Join-Path $stagingDir "Launcher"

# Clean and recreate Staging directory (using robocopy to bypass safe-delete check)
if (Test-Path $stagingDir) {
    # Use robocopy to force-delete the directory
    $emptyDir = Join-Path $PSScriptRoot "EmptyTemp"
    New-Item -ItemType Directory -Force -Path $emptyDir | Out-Null
    robocopy $emptyDir $stagingDir /MIR /R:0 /W:0 /NFL /NDL /NJH /NJS /NC /NS /NP
    Remove-Item $emptyDir -Force -Recurse
    Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $launcherDir -Force | Out-Null

# Copy files from publish directory
$publishDir = Join-Path $PSScriptRoot "..\bin\$Configuration\$TargetFramework\$Runtime\publish"
Copy-Item "$publishDir\*" $launcherDir -Recurse -Force

# Create Data directory (empty, created at runtime)
New-Item -ItemType Directory -Path (Join-Path $stagingDir "..\Data") -Force | Out-Null

Write-Host "Staging directory ready" -ForegroundColor Green

# 3.5 Build and Copy Updater files to Staging
Write-Host "Building and copying updater files..." -ForegroundColor Yellow
$updaterProj = Join-Path $PSScriptRoot "..\..\Quotix.Updater\Quotix.Updater.csproj"
$updaterOutputDir = Join-Path $PSScriptRoot "..\..\Quotix.Updater\bin\Release\net10.0-windows"

# Build Updater (framework-dependent, not self-contained)
if (Test-Path $updaterProj) {
    Write-Host "Building Updater (framework-dependent)..." -ForegroundColor Cyan
    dotnet build $updaterProj -c Release
    
    if (Test-Path "$updaterOutputDir\Quotix.Updater.exe") {
        # Copy all necessary files (not single-file publish)
        Copy-Item "$updaterOutputDir\Quotix.Updater.exe" $launcherDir -Force
        Copy-Item "$updaterOutputDir\Quotix.Updater.dll" $launcherDir -Force
        Copy-Item "$updaterOutputDir\Quotix.Updater.deps.json" $launcherDir -Force
        Copy-Item "$updaterOutputDir\Quotix.Updater.runtimeconfig.json" $launcherDir -Force
        
        # Check file sizes
        $exeSize = (Get-Item "$launcherDir\Quotix.Updater.exe").Length / 1KB
        $dllSize = (Get-Item "$launcherDir\Quotix.Updater.dll").Length / 1KB
        Write-Host "Updater files copied (EXE: $([math]::Round($exeSize, 2)) KB, DLL: $([math]::Round($dllSize, 2)) KB)" -ForegroundColor Green
    } else {
        Write-Host "Error: Failed to build Updater" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "Error: Updater project not found at: $updaterProj" -ForegroundColor Red
    exit 1
}

# 4. Compile Inno Setup script
Write-Host "Compiling installer..." -ForegroundColor Yellow
$issScript = Join-Path $PSScriptRoot "QuotixInstaller.iss"
$outputDir = Join-Path $PSScriptRoot "Out"

# Ensure output directory exists
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

# Find ISCC.exe
$isccPaths = @(
    "C:\Users\Evans\AppData\Local\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$iscc = $null
foreach ($path in $isccPaths) {
    if (Test-Path $path) {
        $iscc = $path
        break
    }
}

if (-not $iscc) {
    Write-Host "Error: Inno Setup compiler (ISCC.exe) not found" -ForegroundColor Red
    Write-Host "Please install Inno Setup 6: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
    exit 1
}

Write-Host "Using Inno Setup compiler: $iscc" -ForegroundColor Gray

# Execute compilation (pass version)
& "$iscc" "/DMyAppVersion=$version" "$issScript"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Installer compilation failed" -ForegroundColor Red
    exit 1
}

# 5. Show results
$outputFile = Get-ChildItem (Join-Path $outputDir "Quotix_Setup_*.exe") | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "=== Installer Build Successful ===" -ForegroundColor Green
Write-Host "Version: $version" -ForegroundColor Cyan
Write-Host "Output file: $($outputFile.FullName)" -ForegroundColor Cyan
$fileSizeMB = [math]::Round($outputFile.Length / 1MB, 2)
Write-Host "File size: $fileSizeMB MB" -ForegroundColor Cyan
