@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0teste.ps1"
exit /b %ERRORLEVEL%
