@echo off
setlocal
cd /d "%~dp0"

for /f "delims=" %%i in ('git branch --show-current') do set BRANCH=%%i
if "%BRANCH%"=="" (
  echo [MiniLab] FAILED: git branch could not be detected.
  pause
  exit /b 1
)

echo [MiniLab] Current branch: %BRANCH%
if /I not "%BRANCH%"=="milestone-2-polish" (
  echo [MiniLab] Switching to milestone-2-polish...
  git checkout -B milestone-2-polish
  if errorlevel 1 (
    echo [MiniLab] FAILED: could not switch/create milestone-2-polish branch.
    pause
    exit /b 1
  )
)

echo [MiniLab] Clean tree check (info):
pwsh -NoProfile -ExecutionPolicy Bypass -File "tools\check-clean-tree.ps1" >nul 2>&1
if errorlevel 1 (
  echo [MiniLab] Working tree has changes. Continuing to commit.
) else (
  echo [MiniLab] Working tree already clean.
)

git add -A
if errorlevel 1 (
  echo [MiniLab] FAILED: git add failed.
  pause
  exit /b 1
)

git diff --cached --quiet
if not errorlevel 1 (
  echo [MiniLab] Nothing to commit.
  echo [MiniLab] Attempting push anyway...
  git push -u origin milestone-2-polish
  if errorlevel 1 (
    echo [MiniLab] FAILED: push failed. Check Git credentials/token.
    pause
    exit /b 1
  )
  echo [MiniLab] SUCCESS: push completed.
  pause
  exit /b 0
)

git commit -m "ZebraDash milestone-2 polish"
if errorlevel 1 (
  echo [MiniLab] FAILED: git commit failed.
  pause
  exit /b 1
)

git push -u origin milestone-2-polish
if errorlevel 1 (
  echo [MiniLab] FAILED: push failed. GitHub auth may be missing or expired.
  echo [MiniLab] Check credential manager / PAT login and retry.
  pause
  exit /b 1
)

echo [MiniLab] SUCCESS: commit and push completed.
pause
