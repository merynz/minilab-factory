@echo off
setlocal
cd /d "%~dp0"

echo [MiniLab] One-click Android internal release started...

pwsh -NoProfile -ExecutionPolicy Bypass -File "tools\release-android-internal.ps1" -UploadMetadata
if errorlevel 1 (
  echo.
  echo [MiniLab] FAILED: Android internal release pipeline failed.
  pause
  exit /b 1
)

echo.
echo [MiniLab] SUCCESS: Android internal release uploaded.
pause
