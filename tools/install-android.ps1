param(
    [string]$ApkPath = "BuildArtifacts/zebradash-dev.apk",
    [switch]$Launch,
    [string]$PackageName = "com.zebratank.zebradash"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-UnityPath {
    if ($env:MINILAB_UNITY_PATH -and (Test-Path $env:MINILAB_UNITY_PATH)) {
        return (Resolve-Path $env:MINILAB_UNITY_PATH).Path
    }

    $roots = @(
        "C:\Program Files\Unity\Hub\Editor",
        "C:\Program Files\Unity"
    )

    foreach ($root in $roots) {
        if (!(Test-Path $root)) { continue }
        $candidate = Get-ChildItem -Path $root -Recurse -Filter "Unity.exe" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }

    return ""
}

function Resolve-AdbPath {
    $adb = Get-Command adb -ErrorAction SilentlyContinue
    if ($adb) {
        return $adb.Source
    }

    if ($env:ANDROID_SDK_ROOT) {
        $sdkAdb = Join-Path $env:ANDROID_SDK_ROOT "platform-tools/adb.exe"
        if (Test-Path $sdkAdb) {
            return (Resolve-Path $sdkAdb).Path
        }
    }

    $unityPath = Resolve-UnityPath
    if (-not [string]::IsNullOrWhiteSpace($unityPath)) {
        $unityEditorDir = Split-Path -Parent $unityPath
        $unityAdb = Join-Path $unityEditorDir "Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
        if (Test-Path $unityAdb) {
            return (Resolve-Path $unityAdb).Path
        }
    }

    return ""
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not [System.IO.Path]::IsPathRooted($ApkPath)) {
    $ApkPath = Join-Path $repoRoot $ApkPath
}
$ApkPath = [System.IO.Path]::GetFullPath($ApkPath)

if (!(Test-Path $ApkPath)) {
    throw "APK not found: $ApkPath"
}

$adbPath = Resolve-AdbPath
if ([string]::IsNullOrWhiteSpace($adbPath)) {
    throw "adb not found. Install Android platform-tools, set PATH/ANDROID_SDK_ROOT, or install Unity Android Build Support."
}

Write-Host "Using adb: $adbPath"
Write-Host "Installing APK: $ApkPath"
& $adbPath install -r $ApkPath
if ($LASTEXITCODE -ne 0) {
    throw "adb install failed with exit code $LASTEXITCODE"
}

Write-Host "APK installed successfully."

if ($Launch) {
    Write-Host "Launching app package: $PackageName"
    & $adbPath shell monkey -p $PackageName -c android.intent.category.LAUNCHER 1
    if ($LASTEXITCODE -ne 0) {
        throw "adb launch failed with exit code $LASTEXITCODE"
    }
}
