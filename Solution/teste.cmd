@echo off
setlocal
cd /d "%~dp0"

set "JORNADA_MODE_ARG=-RuntimeMode HML"
if "%~1"=="" goto run
if /I "%~1"=="--hml" (
  set "JORNADA_MODE_ARG=-RuntimeMode HML"
  shift
  goto validate
)
if /I "%~1"=="--dev" (
  set "JORNADA_MODE_ARG=-RuntimeMode DEV"
  shift
  goto validate
)
if /I "%~1"=="--prod" (
  set "JORNADA_MODE_ARG=-RuntimeMode PROD"
  shift
  goto validate
)
echo Uso: teste.cmd [--hml^|--dev^|--prod]
exit /b 2

:validate
if not "%~1"=="" (
  echo Argumento inesperado: %~1
  echo Uso: teste.cmd [--hml^|--dev^|--prod]
  exit /b 2
)

:run
where pwsh >nul 2>nul
if %errorlevel%==0 (
  pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0teste.ps1" %JORNADA_MODE_ARG%
) else (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0teste.ps1" %JORNADA_MODE_ARG%
)
exit /b %errorlevel%
