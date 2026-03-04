@echo off
setlocal
cd /d "%~dp0"

echo [MiniLab] One-click iOS internal release started...

pwsh -NoProfile -ExecutionPolicy Bypass -File "tools\release-ios-internal.ps1"
if errorlevel 1 (
  echo.
  echo [MiniLab] FAILED: iOS internal release pipeline failed.
  pause
  exit /b 1
)

echo.
echo [MiniLab] SUCCESS: iOS internal release uploaded.
pause
