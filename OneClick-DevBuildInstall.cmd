@echo off
setlocal
cd /d "%~dp0"

echo [MiniLab] ZebraDash one-click deploy started...

pwsh -NoProfile -ExecutionPolicy Bypass -File "tools\deploy-zebradash-dev.ps1"
if errorlevel 1 (
  echo.
  echo [MiniLab] FAILED: Deploy pipeline failed. Check logs under BuildArtifacts.
  pause
  exit /b 1
)

echo.
echo [MiniLab] SUCCESS
pause
