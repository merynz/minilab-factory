param(
    [string]$ApkPath = "BuildArtifacts/zebradash-dev.apk"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not [System.IO.Path]::IsPathRooted($ApkPath)) {
    $ApkPath = Join-Path $repoRoot $ApkPath
}
$ApkPath = [System.IO.Path]::GetFullPath($ApkPath)

if (!(Test-Path $ApkPath)) {
    throw "APK not found: $ApkPath"
}

$adb = Get-Command adb -ErrorAction SilentlyContinue
if (-not $adb) {
    $unityRoot = "C:\Program Files\Unity\Hub\Editor\6000.2.6f2\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
    if (Test-Path $unityRoot) {
        $adbPath = $unityRoot
    } else {
        throw "adb not found in PATH. Install Android platform-tools or set PATH."
    }
} else {
    $adbPath = $adb.Source
}

Write-Host "Installing APK: $ApkPath"
& $adbPath install -r $ApkPath
if ($LASTEXITCODE -ne 0) {
    throw "adb install failed with exit code $LASTEXITCODE"
}

Write-Host "APK installed successfully."
