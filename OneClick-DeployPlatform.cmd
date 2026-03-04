@echo off
setlocal
cd /d "%~dp0"

echo [MiniLab] One-click full platform deploy started...

pwsh -NoProfile -ExecutionPolicy Bypass -File "tools\deploy-platform.ps1" -Platform both -UploadMetadata $true -RunAndroidSmokeTest $true -DispatchIosFromWindows $true
if errorlevel 1 (
  echo.
  echo [MiniLab] FAILED: full platform deploy failed.
  pause
  exit /b 1
)

echo.
echo [MiniLab] SUCCESS: full platform deploy completed.
pause
