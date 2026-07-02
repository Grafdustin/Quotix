# Test-Version.ps1
# 测试版本号读取功能

$exePath = "D:\桌面\QUO\Quotix\bin\Release\net10.0-windows\win-x64\publish\Quotix.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "错误: 未找到 EXE 文件: $exePath" -ForegroundColor Red
    Write-Host "请先运行 Build-Installer.ps1 或手动发布项目" -ForegroundColor Yellow
    exit 1
}

# 读取程序集版本
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)

Write-Host "=== Quotix 版本信息 ===" -ForegroundColor Cyan
Write-Host "产品名称: $($versionInfo.ProductName)" -ForegroundColor Green
Write-Host "文件版本: $($versionInfo.FileVersion)" -ForegroundColor Green
Write-Host "产品版本: $($versionInfo.ProductVersion)" -ForegroundColor Green
Write-Host "版权信息: $($versionInfo.LegalCopyright)" -ForegroundColor Green

# 读取 csproj 中的版本号
[xml]$csproj = Get-Content "D:\桌面\QUO\Quotix\QuotixDesktop.csproj"
$csprojVersion = $csproj.Project.PropertyGroup.Version

Write-Host "`n.csproj 版本号: $csprojVersion" -ForegroundColor Yellow

# 验证版本号是否一致
if ($versionInfo.ProductVersion -eq $csprojVersion) {
    Write-Host "`n✓ 版本号一致" -ForegroundColor Green
} else {
    Write-Host "`n✗ 版本号不一致" -ForegroundColor Red
    Write-Host "  程序集版本: $($versionInfo.ProductVersion)" -ForegroundColor Red
    Write-Host "  csproj 版本: $csprojVersion" -ForegroundColor Red
}
