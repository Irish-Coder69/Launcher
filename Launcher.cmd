@echo off
setlocal enabledelayedexpansion
where pwsh.exe >nul 2>&1
if !ERRORLEVEL! == 0 (
    start "" pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0launcher.ps1" -ConfigPath "%~dp0launcher.config.json"
) else (
    start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0launcher.ps1" -ConfigPath "%~dp0launcher.config.json"
)
