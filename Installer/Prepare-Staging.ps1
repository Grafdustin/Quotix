# Prepare-Staging.ps1
# 将编译输出复制到 Installer\Staging\Launcher\，供 Inno Setup 打包使用

$ErrorActionPreference = "Stop"

$ProjectDir = Resolve-Path "$PSScriptRoot\.."
$StagingDir = "$PSScriptRoot\Staging"
$LauncherDir = "$StagingDir\Launcher"
$SourceDir = "$ProjectDir\bin\Release\net10.0-windows\win-x64\publish"

Write-Host "=== Quotix 安装包文件分阶段 ===" -ForegroundColor Cyan
Write-Host "源目录: $SourceDir"
Write-Host "目标目录: $LauncherDir"

# 1. 清理旧的 Staging
if (Test-Path $StagingDir) {
    Remove-Item $StagingDir -Recurse -Force
    Write-Host "已清理旧 Staging 目录"
}

# 2. 确认编译输出存在
if (-not (Test-Path "$SourceDir\Quotix.exe")) {
    Write-Host "错误：找不到 Quotix.exe，请先编译项目（Release 配置）" -ForegroundColor Red
    exit 1
}

# 3. 复制所有文件到 Staging\Launcher\
New-Item -ItemType Directory -Force -Path $LauncherDir | Out-Null
Copy-Item "$SourceDir\*" -Destination $LauncherDir -Recurse -Force
Write-Host "已复制文件到 Staging\Launcher\" -ForegroundColor Green

# 4. 确认关键文件存在
$files = @(
    "$LauncherDir\Quotix.exe",
    "$LauncherDir\Resources\app.ico",
    "$LauncherDir\Resources\quotation-template.xlsx",
    "$LauncherDir\Resources\quotation.db"
)

$allOk = $true
foreach ($f in $files) {
    if (Test-Path $f) {
        Write-Host "  OK: $($f.Replace($StagingDir, ''))"
    } else {
        Write-Host "  缺失: $($f.Replace($StagingDir, ''))" -ForegroundColor Yellow
        $allOk = $false
    }
}

# 5. 统计文件数
$fileCount = (Get-ChildItem $LauncherDir -Recurse -File).Count
Write-Host ""
Write-Host "=== 分阶段完成 ===" -ForegroundColor Cyan
Write-Host "文件总数: $fileCount"
if ($allOk) {
    Write-Host "所有关键文件已就绪，可以编译安装包了" -ForegroundColor Green
    Write-Host "运行: ISCC.exe /DMyAppVersion=1.0.0 `"$PSScriptRoot\QuotixInstaller.iss`""
} else {
    Write-Host "部分文件缺失，请检查编译输出" -ForegroundColor Yellow
}
