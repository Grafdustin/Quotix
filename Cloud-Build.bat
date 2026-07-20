@echo off
setlocal EnableExtensions

cd /d "%~dp0"

set "PS_EXE=pwsh"
where pwsh >nul 2>nul
if errorlevel 1 set "PS_EXE=powershell"

start "Quotix Cloud Build" "%PS_EXE%" -NoExit -NoProfile -ExecutionPolicy Bypass -File "%~dp0Cloud-Build.ps1" %*
exit /b
