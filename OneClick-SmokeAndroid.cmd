@echo off
setlocal
cd /d "%~dp0"

echo [MiniLab] One-click Android smoke test started...

pwsh -NoProfile -ExecutionPolicy Bypass -File "tools\smoke-android.ps1"
if errorlevel 1 (
  echo.
  echo [MiniLab] FAILED: Android smoke test failed.
  pause
  exit /b 1
)

echo.
echo [MiniLab] SUCCESS: Android smoke test completed.
pause
