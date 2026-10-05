@echo off
setlocal
cd /d "%~dp0"
set "MODEARG="
if /I "%~1"=="--dev" (
  set "MODEARG=-Dev"
  shift
) else if /I "%~1"=="--prod" (
  set "MODEARG=-Prod"
  shift
)
where pwsh >nul 2>nul
if %errorlevel%==0 (
  pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0teste.ps1" %MODEARG% %*
) else (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0teste.ps1" %MODEARG% %*
)
exit /b %errorlevel%
