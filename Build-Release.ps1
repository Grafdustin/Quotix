# Build-Release.ps1
# 标准化发布流程：Git提交 → 编译 → 发布安装包
# 用法：
#   .\Build-Release.ps1 -CommitMessage "修复XXX功能"
#   .\Build-Release.ps1 -CommitMessage "新增XXX功能" -SkipGit
#   .\Build-Release.ps1 -CommitMessage "发布v1.0.1" -Version "1.0.1"

param(
    [Parameter(Mandatory=$true)]
    [string]$CommitMessage,
    
    [string]$Version,
    
    [switch]$SkipGit,
    
    [switch]$SkipBuild,
    
    [switch]$NoAutoIncrement,
    
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ProjectDir = Resolve-Path "$PSScriptRoot"
$InstallerDir = Join-Path $ProjectDir "Installer"

Write-Host "=== Quotix 发布流程 ===" -ForegroundColor Cyan
Write-Host "提交信息: $CommitMessage" -ForegroundColor Gray
if ($Version) {
    Write-Host "版本号: $Version" -ForegroundColor Gray
}
Write-Host ""

# ========== 步骤 1: Git 提交 ==========
if (-not $SkipGit) {
    Write-Host "步骤 1/4: Git 提交..." -ForegroundColor Yellow
    
    # 检查是否在 git 仓库中
    $isGitRepo = Test-Path (Join-Path $ProjectDir ".git")
    if (-not $isGitRepo) {
        Write-Host "警告: 当前目录不是 Git 仓库，跳过 Git 操作" -ForegroundColor Yellow
    } else {
        # 检查是否有未提交的更改
        $gitStatus = git -C $ProjectDir status --porcelain
        if ($gitStatus) {
            # 添加所有更改
            git -C $ProjectDir add -A
            
            # 提交
            git -C $ProjectDir commit -m "$CommitMessage"
            if ($LASTEXITCODE -ne 0) {
                Write-Host "错误: Git 提交失败" -ForegroundColor Red
                exit 1
            }
            Write-Host "Git 提交成功" -ForegroundColor Green
        } else {
            Write-Host "没有需要提交的更改" -ForegroundColor Gray
        }
    }
} else {
    Write-Host "步骤 1/4: 跳过 Git 提交" -ForegroundColor Gray
}

# ========== 步骤 2: 更新版本号 ==========
Write-Host ""
Write-Host "步骤 2/4: 检查版本号..." -ForegroundColor Yellow

$csprojPath = Join-Path $ProjectDir "QuotixDesktop.csproj"
[xml]$csproj = Get-Content $csprojPath
$currentVersion = $csproj.Project.PropertyGroup.Version

# 如果没有提供版本号且未禁用自动递增，则自动递增
if (-not $Version -and -not $NoAutoIncrement) {
    # 解析当前版本号 (假设格式为 x.y.z)
    if ($currentVersion -match '^(\d+)\.(\d+)\.(\d+)$') {
        $major = [int]$Matches[1]
        $minor = [int]$Matches[2]
        $patch = [int]$Matches[3]
        
        # 自动递增 patch 版本号
        $patch++
        $Version = "$major.$minor.$patch"
        
        Write-Host "自动递增版本号: $currentVersion → $Version" -ForegroundColor Green
    } else {
        Write-Host "警告: 无法解析当前版本号 '$currentVersion'，使用默认递增" -ForegroundColor Yellow
        $Version = "1.0.1"
    }
}

# 如果提供了版本号，验证格式
if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        Write-Host "错误: 版本号格式不正确，应为 x.y.z (如 1.0.1)" -ForegroundColor Red
        exit 1
    }
    
    # 检查是否与当前版本相同
    if ($Version -eq $currentVersion) {
        Write-Host "警告: 版本号 $Version 与当前版本相同" -ForegroundColor Yellow
        
        # 检查是否已存在相同版本的安装包
        $existingInstaller = Join-Path $InstallerDir "Out\Quotix_Setup_$Version.exe"
        if (Test-Path $existingInstaller) {
            Write-Host "警告: 已存在相同版本的安装包: $existingInstaller" -ForegroundColor Yellow
            $confirm = Read-Host "是否继续并覆盖？(y/N)"
            if ($confirm -ne "y" -and $confirm -ne "Y") {
                Write-Host "已取消" -ForegroundColor Gray
                exit 0
            }
        }
    }
    
    Write-Host "更新版本号: $currentVersion → $Version" -ForegroundColor Green
    
    # 更新 .csproj 文件
    $csprojContent = Get-Content $csprojPath -Encoding UTF8
    $csprojContent = $csprojContent -replace '(?<=<Version>)[^<]+', $Version
    $csprojContent = $csprojContent -replace '(?<=<AssemblyVersion>)[^<]+', "$Version.0"
    $csprojContent = $csprojContent -replace '(?<=<FileVersion>)[^<]+', "$Version.0"
    $csprojContent = $csprojContent -replace '(?<=<InformationalVersion>)[^<]+', $Version
    Set-Content -Path $csprojPath -Value $csprojContent -Encoding UTF8
    
    # 提交版本号更改
    if (-not $SkipGit) {
        git -C $ProjectDir add "$csprojPath"
        git -C $ProjectDir commit -m "更新版本号到 $Version"
        if ($LASTEXITCODE -ne 0) {
            Write-Host "警告: 版本号更改提交失败（可能没有任何更改）" -ForegroundColor Yellow
        }
    }
    
    Write-Host "版本号已更新为: $Version" -ForegroundColor Green
} else {
    Write-Host "保持当前版本号: $currentVersion" -ForegroundColor Gray
    $Version = $currentVersion
}

# ========== 步骤 3: 编译项目 ==========
if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "步骤 3/4: 编译项目..." -ForegroundColor Yellow
    
    $publishDir = Join-Path $ProjectDir "bin\$Configuration\net10.0-windows\win-x64\publish"
    dotnet publish "$ProjectDir\QuotixDesktop.csproj" -c $Configuration -r win-x64 --self-contained true
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "错误: 项目编译失败" -ForegroundColor Red
        exit 1
    }
    Write-Host "项目编译成功" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "步骤 3/4: 跳过编译" -ForegroundColor Gray
}

# ========== 步骤 4: 构建安装包 ==========
Write-Host ""
Write-Host "步骤 4/4: 构建安装包..." -ForegroundColor Yellow

$buildInstallerScript = Join-Path $InstallerDir "Build-Installer.ps1"
if (Test-Path $buildInstallerScript) {
    & "$buildInstallerScript" -Configuration $Configuration -SkipBuild
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "错误: 安装包构建失败" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "警告: 找不到 Build-Installer.ps1，跳过安装包构建" -ForegroundColor Yellow
}

# ========== 完成 ==========
Write-Host ""
Write-Host "=== 发布流程完成 ===" -ForegroundColor Green
Write-Host "提交信息: $CommitMessage" -ForegroundColor Cyan
if ($Version) {
    Write-Host "版本号: $Version" -ForegroundColor Cyan
}

# 显示生成的安装包
$outputDir = Join-Path $InstallerDir "Out"
if (Test-Path $outputDir) {
    $installer = Get-ChildItem (Join-Path $outputDir "Quotix_Setup_*.exe") | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($installer) {
        Write-Host "安装包: $($installer.FullName)" -ForegroundColor Cyan
        Write-Host "文件大小: $([math]::Round($installer.Length / 1MB, 2)) MB" -ForegroundColor Cyan
    }
}
