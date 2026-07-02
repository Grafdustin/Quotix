# Build-Installer.ps1
# 自动化构建安装包脚本
# 从 .csproj 读取版本号并传递给 Inno Setup 编译器

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$TargetFramework = "net10.0-windows",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

Write-Host "=== Quotix 安装包构建脚本 ===" -ForegroundColor Cyan

# 1. 读取版本号 from .csproj
Write-Host "正在读取版本号..." -ForegroundColor Yellow
$csprojPath = Join-Path $PSScriptRoot "..\QuotixDesktop.csproj"
[xml]$csproj = Get-Content $csprojPath
$version = $csproj.Project.PropertyGroup.Version
if (-not $version) {
    $version = "1.0.0"
    Write-Host "警告: .csproj 中未找到 Version，使用默认值: $version" -ForegroundColor Yellow
} else {
    Write-Host "版本号: $version" -ForegroundColor Green
}

# 2. 构建项目 (除非跳过)
if (-not $SkipBuild) {
    Write-Host "正在构建项目..." -ForegroundColor Yellow
    $publishDir = Join-Path $PSScriptRoot "..\bin\$Configuration\$TargetFramework\$Runtime\publish"
    dotnet publish "$csprojPath" -c $Configuration -r $Runtime --self-contained true
    if ($LASTEXITCODE -ne 0) {
        Write-Host "错误: 项目构建失败" -ForegroundColor Red
        exit 1
    }
    Write-Host "项目构建成功" -ForegroundColor Green
} else {
    Write-Host "跳过构建步骤" -ForegroundColor Gray
}

# 3. 准备 Staging 目录
Write-Host "正在准备 Staging 目录..." -ForegroundColor Yellow
$stagingDir = Join-Path $PSScriptRoot "Staging"
$launcherDir = Join-Path $stagingDir "Launcher"

# 清理并重建 Staging 目录
if (Test-Path $stagingDir) {
    Remove-Item $stagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $launcherDir -Force | Out-Null

# 从 publish 目录复制文件
$publishDir = Join-Path $PSScriptRoot "..\bin\$Configuration\$TargetFramework\$Runtime\publish"
Copy-Item "$publishDir\*" $launcherDir -Recurse -Force

# 创建 Data 目录 (空目录，运行时自动创建)
New-Item -ItemType Directory -Path (Join-Path $stagingDir "..\Data") -Force | Out-Null

Write-Host "Staging 目录准备完成" -ForegroundColor Green

# 4. 编译 Inno Setup 脚本
Write-Host "正在编译安装包..." -ForegroundColor Yellow
$issScript = Join-Path $PSScriptRoot "QuotixInstaller.iss"
$outputDir = Join-Path $PSScriptRoot "Out"

# 确保输出目录存在
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

# 查找 ISCC.exe
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
    Write-Host "错误: 未找到 Inno Setup 编译器 (ISCC.exe)" -ForegroundColor Red
    Write-Host "请安装 Inno Setup 6: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
    exit 1
}

Write-Host "使用 Inno Setup 编译器: $iscc" -ForegroundColor Gray

# 执行编译 (传递版本号)
& "$iscc" "/DMyAppVersion=$version" "$issScript"
if ($LASTEXITCODE -ne 0) {
    Write-Host "错误: 安装包编译失败" -ForegroundColor Red
    exit 1
}

# 5. 显示结果
$outputFile = Get-ChildItem (Join-Path $outputDir "Quotix_Setup_*.exe") | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "=== 安装包构建成功 ===" -ForegroundColor Green
Write-Host "版本号: $version" -ForegroundColor Cyan
Write-Host "输出文件: $($outputFile.FullName)" -ForegroundColor Cyan
Write-Host "文件大小: $([math]::Round($outputFile.Length / 1MB, 2)) MB" -ForegroundColor Cyan
