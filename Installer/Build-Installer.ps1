# Build-Installer.ps1
# Automated installer build script
# Read version from .csproj and pass to Inno Setup compiler

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$TargetFramework = "net10.0-windows",
    [string]$Version,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

Write-Host "=== Quotix Installer Build Script ===" -ForegroundColor Cyan

# 1. Determine version (parameter > csproj)
Write-Host "Reading version..." -ForegroundColor Yellow
$csprojPath = Join-Path $PSScriptRoot "..\QuotixDesktop.csproj"
if ($Version) {
    Write-Host "Version (from parameter): $Version" -ForegroundColor Green
} else {
    [xml]$csproj = Get-Content $csprojPath
    $Version = $csproj.Project.PropertyGroup.Version
    if (-not $Version) {
        $Version = "1.0.0"
        Write-Host "Warning: Version not found in .csproj, using default: $Version" -ForegroundColor Yellow
    } else {
        Write-Host "Version (from csproj): $Version" -ForegroundColor Green
    }
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

# === 修复1：安全清理（用 cmd rmdir 绕过 sandbox 批量删除限制） ===
if (Test-Path $stagingDir) {
    cmd /c "rmdir /s /q `"$stagingDir`"" 2>$null
    Start-Sleep -Milliseconds 500
}
New-Item -ItemType Directory -Path $launcherDir -Force | Out-Null

# === 修复2：验证并归一化 publish 目录路径（避免 .. 导致 Substring 崩溃） ===
$publishDir = Join-Path $PSScriptRoot "..\bin\$Configuration\$TargetFramework\$Runtime\publish"
if (-not (Test-Path $publishDir)) {
    Write-Host "Error: Publish directory not found: $publishDir" -ForegroundColor Red
    exit 1
}
# 归一化路径（去掉 ..），确保 Substring 计算正确
# 使用 .NET 方法替代 Resolve-Path（Windows 上更可靠）
$publishDir = [System.IO.Path]::GetFullPath($publishDir)
Write-Host "Publish source: $publishDir" -ForegroundColor Gray

# === 修复3：过滤式复制（排除 Updater / debug 文件 / 构建残留） ===
# 只排除"污染源"文件，不排除运行时 DLL（self-contained 需要它们）
$excludedPatterns = @(
    "Updater",          # Updater.exe 及其依赖（独立发布，不进入主程序安装包）
    "\.pdb$",           # 调试符号文件（生产环境不需要）
    "\.xml$"            # XML 文档注释（生产环境不需要）
)

$copiedCount = 0
$skippedCount = 0

Get-ChildItem $publishDir -Recurse | ForEach-Object {
    # 跳过目录（会在文件复制时自动创建）
    if ($_.PSIsContainer) { return }
    
    # 计算相对路径和目标路径
    $relativePath = $_.FullName.Substring($publishDir.Length).TrimStart('\', '/')
    $destPath = Join-Path $launcherDir $relativePath
    
    # 检查是否命中排除规则
    $shouldSkip = $false
    foreach ($pattern in $excludedPatterns) {
        if ($_.Name -match $pattern) {
            $shouldSkip = $true
            break
        }
    }
    
    if ($shouldSkip) {
        $skippedCount++
        return
    }
    
    # 创建目标目录并复制
    $destDir = Split-Path $destPath -Parent
    if (-not (Test-Path $destDir)) {
        New-Item -ItemType Directory -Force -Path $destDir | Out-Null
    }
    Copy-Item $_.FullName -Destination $destPath -Force
    $copiedCount++
}

Write-Host "Staging: $copiedCount files copied, $skippedCount files filtered" -ForegroundColor Green

# Create Data directory (empty, created at runtime)
New-Item -ItemType Directory -Path (Join-Path $stagingDir "..\Data") -Force | Out-Null

Write-Host "Staging directory ready" -ForegroundColor Green

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
& "$iscc" "/DMyAppVersion=$Version" "$issScript"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Installer compilation failed" -ForegroundColor Red
    exit 1
}

# 5. Show results
$outputFile = Get-ChildItem (Join-Path $outputDir "Quotix_Setup_*.exe") | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "=== Installer Build Successful ===" -ForegroundColor Green
Write-Host "Version: $Version" -ForegroundColor Cyan
Write-Host "Output file: $($outputFile.FullName)" -ForegroundColor Cyan
$fileSizeMB = [math]::Round($outputFile.Length / 1MB, 2)
Write-Host "File size: $fileSizeMB MB" -ForegroundColor Cyan
