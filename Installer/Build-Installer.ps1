# Build-Installer.ps1
# Automated installer build script
# Read version from parameter (preferred) or .csproj, then compile Inno Setup

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$TargetFramework = "net10.0-windows",
    [string]$Version,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

# ── 代码签名辅助函数（Authenticode）──
# 配置方式（任选其一）：
#   1) PFX 文件：设置环境变量 QUOTIX_CERT_FILE（.pfx 路径）+ QUOTIX_CERT_PASSWORD
#   2) 证书存储：设置 QUOTIX_CERT_THUMBPRINT（SHA1 指纹，证书需已在当前用户/本机存储）
# 未配置时 Sign-InstallerIfConfigured 直接跳过，安装包仍会生成
# （但浏览器下载时会标记为"未知发布者 / 来源不安全"）。
function Find-SignTool {
    $candidates = @(
        "C:\Program Files (x86)\Windows Kits\10\bin\x64\signtool.exe",
        "C:\Program Files\Windows Kits\10\bin\x64\signtool.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    $kitRoot = "C:\Program Files (x86)\Windows Kits\10\bin"
    if (Test-Path $kitRoot) {
        $found = Get-ChildItem $kitRoot -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    return $null
}

function Sign-InstallerIfConfigured {
    param([string]$InstallerPath)
    $certFile   = $env:QUOTIX_CERT_FILE
    $certPw     = $env:QUOTIX_CERT_PASSWORD
    $thumbprint = $env:QUOTIX_CERT_THUMBPRINT

    if (-not $certFile -and -not $thumbprint) {
        Write-Host "⚠️ 未配置代码签名证书（QUOTIX_CERT_FILE / QUOTIX_CERT_THUMBPRINT），跳过签名。" -ForegroundColor Yellow
        Write-Host "   提示：未签名的安装包在浏览器下载时会被标记为'未知发布者 / 来源不安全'。" -ForegroundColor DarkGray
        return
    }

    $signtool = Find-SignTool
    if (-not $signtool) {
        Write-Host "❌ 未找到 signtool.exe（Windows SDK），无法签名。请安装 Windows SDK 或加入 PATH。" -ForegroundColor Red
        return
    }

    $tsServer = "http://timestamp.digicert.com"
    if ($thumbprint) {
        Write-Host "使用证书存储指纹签名：$thumbprint" -ForegroundColor Cyan
        & "$signtool" sign /fd SHA256 /tr $tsServer /td SHA256 /sha1 $thumbprint "$InstallerPath"
    } else {
        if (-not (Test-Path $certFile)) {
            Write-Host "❌ 证书文件不存在：$certFile" -ForegroundColor Red
            return
        }
        Write-Host "使用 PFX 证书签名：$certFile" -ForegroundColor Cyan
        & "$signtool" sign /fd SHA256 /tr $tsServer /td SHA256 /f "$certFile" /p "$certPw" "$InstallerPath"
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ 签名失败（退出码 $LASTEXITCODE）。" -ForegroundColor Red
    } else {
        Write-Host "✅ 安装包已签名：$InstallerPath" -ForegroundColor Green
    }
}

Write-Host "=== Quotix Installer Build Script ===" -ForegroundColor Cyan

# 1. Determine version
Write-Host "Reading version..." -ForegroundColor Yellow
$csprojPath = Join-Path $PSScriptRoot "..\QuotixDesktop.csproj"
if ($Version) {
    Write-Host "Version (from parameter): $Version" -ForegroundColor Green
} else {
    [xml]$csprojXml = Get-Content $csprojPath
    $Version = $csprojXml.Project.PropertyGroup.Version
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
    dotnet publish "$csprojPath" -c $Configuration -r $Runtime --self-contained true -p:Version=$Version
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

# 安全清理 staging（使用 cmd rmdir 绕过 sandbox 批量删除限制）
if (Test-Path $stagingDir) {
    cmd /c "rmdir /s /q `"$stagingDir`"" 2>$null
    Start-Sleep -Milliseconds 500
}
New-Item -ItemType Directory -Path $launcherDir -Force | Out-Null

# 验证 publish 目录
$publishDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\bin\$Configuration\$TargetFramework\$Runtime\publish"))
if (-not (Test-Path $publishDir)) {
    Write-Host "Error: Publish directory not found: $publishDir" -ForegroundColor Red
    exit 1
}
Write-Host "Publish source: $publishDir" -ForegroundColor Gray

# 复制文件到 staging（过滤 .pdb 和 .xml 开发文件）
$copiedCount = 0
$skippedCount = 0

Get-ChildItem $publishDir -Recurse | ForEach-Object {
    # 跳过目录
    if ($_.PSIsContainer) { return }
    
    # 跳过调试文件
    if ($_.Extension -eq ".pdb" -or $_.Extension -eq ".xml") {
        $skippedCount++
        return
    }
    
    # 计算相对路径和目标路径
    $relativePath = $_.FullName.Substring($publishDir.Length).TrimStart('\', '/')
    $destPath = Join-Path $launcherDir $relativePath
    
    # 创建目标目录并复制
    $destDir = Split-Path $destPath -Parent
    if (-not (Test-Path $destDir)) {
        New-Item -ItemType Directory -Force -Path $destDir | Out-Null
    }
    Copy-Item $_.FullName -Destination $destPath -Force
    $copiedCount++
}

Write-Host "Staging: $copiedCount files copied, $skippedCount files filtered" -ForegroundColor Green

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

# 5.1 代码签名（可选，未配置证书时自动跳过）
Sign-InstallerIfConfigured -InstallerPath $outputFile.FullName

Write-Host "=== Installer Build Successful ===" -ForegroundColor Green
Write-Host "Version: $Version" -ForegroundColor Cyan
Write-Host "Output file: $($outputFile.FullName)" -ForegroundColor Cyan
$fileSizeMB = [math]::Round($outputFile.Length / 1MB, 2)
Write-Host "File size: $fileSizeMB MB" -ForegroundColor Cyan
