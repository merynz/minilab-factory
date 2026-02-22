param(
    [string]$GamePath = "Games/Game_Arcade_ZebraDash",
    [string]$ProjectPath = "Games/Game_Arcade_ZebraDash/UnityProject",
    [string]$OutputName = "zebradash-dev.apk"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-CanonicalPath([string]$InputPath, [string]$RepoRoot) {
    $candidate = $InputPath
    if (-not [System.IO.Path]::IsPathRooted($candidate)) {
        $candidate = Join-Path $RepoRoot $candidate
    }

    $candidate = [System.IO.Path]::GetFullPath($candidate)
    if (!(Test-Path $candidate)) {
        throw "Path not found: $InputPath"
    }

    return (Resolve-Path $candidate).Path
}

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

function Restore-UnitySettingsNoise([string]$RepoRoot, [string]$ResolvedProjectPath) {
    $relativePath = [System.IO.Path]::GetRelativePath($RepoRoot, $ResolvedProjectPath)
    $settingsRoot = Join-Path $relativePath "ProjectSettings"
    $restoreFiles = @(
        (Join-Path $settingsRoot "GraphicsSettings.asset"),
        (Join-Path $settingsRoot "QualitySettings.asset"),
        (Join-Path $settingsRoot "PackageManagerSettings.asset"),
        (Join-Path $settingsRoot "URPProjectSettings.asset")
    )

    git restore --source=HEAD --worktree -- $restoreFiles 2>$null | Out-Null
    git clean -f -- $restoreFiles 2>$null | Out-Null
}

function Get-AndroidPackageName([string]$StorePath) {
    if (!(Test-Path $StorePath)) {
        return ""
    }

    $raw = Get-Content -Path $StorePath -Raw
    $match = [regex]::Match($raw, '(?m)^\s*application_id_android\s*:\s*[''"]?([A-Za-z0-9._]+)[''"]?\s*$')
    if ($match.Success) {
        return $match.Groups[1].Value.Trim()
    }

    return ""
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedProjectPath = Resolve-CanonicalPath -InputPath $ProjectPath -RepoRoot $repoRoot
$resolvedGamePath = Resolve-CanonicalPath -InputPath $GamePath -RepoRoot $repoRoot

Write-Host "GamePath: $resolvedGamePath"
Write-Host "ProjectPath: $resolvedProjectPath"

Restore-UnitySettingsNoise -RepoRoot $repoRoot -ResolvedProjectPath $resolvedProjectPath

& (Join-Path $PSScriptRoot "check-clean-tree.ps1")
if ($LASTEXITCODE -ne 0) {
    Write-Host "WARN: Working tree is dirty. Continuing dev build/install."
}

& (Join-Path $PSScriptRoot "analyze-audio.ps1") -GamePath $resolvedGamePath
if (-not $?) {
    throw "analyze-audio failed"
}

& (Join-Path $PSScriptRoot "sync-zebradash-content.ps1") -ProjectPath $resolvedProjectPath
if (-not $?) {
    throw "sync-zebradash-content failed"
}

& (Join-Path $PSScriptRoot "build-android-apk.ps1") -ProjectPath $resolvedProjectPath -OutputName $OutputName
if (-not $?) {
    throw "build-android-apk failed"
}

$apkPath = Join-Path $repoRoot ("BuildArtifacts/" + $OutputName)
& (Join-Path $PSScriptRoot "install-android.ps1") -ApkPath $apkPath
if (-not $?) {
    throw "install-android failed"
}

$storePath = Join-Path $resolvedGamePath "store.yaml"
$packageName = Get-AndroidPackageName -StorePath $storePath
if ([string]::IsNullOrWhiteSpace($packageName)) {
    throw "application_id_android missing in $storePath"
}

$adbPath = Resolve-AdbPath
if ([string]::IsNullOrWhiteSpace($adbPath)) {
    throw "adb not found for app launch"
}

Write-Host "Launching app package: $packageName"
& $adbPath shell monkey -p $packageName -c android.intent.category.LAUNCHER 1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "adb launch failed"
}

Restore-UnitySettingsNoise -RepoRoot $repoRoot -ResolvedProjectPath $resolvedProjectPath
Write-Host "SUCCESS: analyze + sync + build + install + launch completed."
