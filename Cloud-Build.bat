@echo off
setlocal EnableExtensions
chcp 65001 >nul

cd /d "%~dp0"

echo.
echo === Quotix Cloud Build ===
echo.
echo 这个脚本会调用 Cloud-Build.ps1，并在确认后推送版本 tag 触发 GitHub Actions。
echo.

set "PS_EXE=pwsh"
where pwsh >nul 2>nul
if errorlevel 1 set "PS_EXE=powershell"

set "VERSION="
set "CHANGELOG="
set "REDEPLOY="

set /p VERSION=输入版本号，直接回车使用 csproj 当前版本: 
set /p CHANGELOG=输入更新日志，直接回车则打开记事本填写: 
set /p REDEPLOY=是否重新触发当前版本构建？输入 y 表示是，直接回车表示否: 

echo.
if /i "%REDEPLOY%"=="y" (
    echo 即将重新触发当前版本构建...
    "%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Cloud-Build.ps1" -Redeploy
) else if not "%VERSION%"=="" (
    if not "%CHANGELOG%"=="" (
        echo 即将发布版本 %VERSION%...
        "%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Cloud-Build.ps1" -Version "%VERSION%" -CommitMessage "%CHANGELOG%"
    ) else (
        echo 即将发布版本 %VERSION%，稍后会打开记事本填写更新日志...
        "%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Cloud-Build.ps1" -Version "%VERSION%"
    )
) else (
    if not "%CHANGELOG%"=="" (
        echo 即将使用当前版本发布...
        "%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Cloud-Build.ps1" -CommitMessage "%CHANGELOG%"
    ) else (
        echo 即将使用当前版本发布，稍后会打开记事本填写更新日志...
        "%PS_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Cloud-Build.ps1"
    )
)

set "EXIT_CODE=%ERRORLEVEL%"
echo.
if "%EXIT_CODE%"=="0" (
    echo 完成。
) else (
    echo 失败，退出码：%EXIT_CODE%
)
echo.
pause
exit /b %EXIT_CODE%
