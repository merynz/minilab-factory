@echo off
setlocal
cd /d "%~dp0\.."

echo [MiniLab] ZebraDash deploy started...
pwsh -NoProfile -ExecutionPolicy Bypass -File "tools\deploy-zebradash-dev.ps1"
if errorlevel 1 (
  echo.
  echo [MiniLab] FAILED: deploy-zebradash-dev failed. Check BuildArtifacts logs.
  pause
  exit /b 1
)

echo.
echo [MiniLab] SUCCESS: ZebraDash deployed.
pause
