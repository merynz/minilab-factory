@echo off
setlocal
cd /d "%~dp0"

echo [MiniLab] One-click dev build/install started...

pwsh -NoProfile -ExecutionPolicy Bypass -File "tools\dev-build-install.ps1"
if errorlevel 1 (
  echo.
  echo [MiniLab] FAILED: Deploy pipeline failed. Check logs under BuildArtifacts.
  pause
  exit /b 1
)

echo.
echo [MiniLab] SUCCESS
pause
