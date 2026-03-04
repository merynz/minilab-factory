param(
    [string]$GamePath = "",
    [string]$OutputName = "minilab-smoke.apk",
    [int]$DurationSeconds = 300,
    [switch]$SkipCompileCheck,
    [switch]$SkipAudioAnalysis,
    [switch]$SkipBuild
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

function Read-AndroidPackageNameFromStore([string]$StoreYamlPath) {
    $raw = Get-Content -Path $StoreYamlPath -Raw
    $match = [regex]::Match($raw, '(?m)^\s*application_id_android\s*:\s*[''"]?([A-Za-z0-9._]+)[''"]?\s*$')
    if ($match.Success) {
        return $match.Groups[1].Value.Trim()
    }

    return ""
}

if ($DurationSeconds -lt 60) {
    throw "DurationSeconds must be >= 60."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($GamePath)) {
    $detectedGame = Get-ChildItem -Path (Join-Path $repoRoot "Games") -Directory -Filter "Game_*" -ErrorAction SilentlyContinue |
        Sort-Object Name |
        Select-Object -First 1
    if ($null -eq $detectedGame) {
        throw "GamePath not provided and no Games/Game_* directory found."
    }

    $GamePath = [System.IO.Path]::GetRelativePath($repoRoot, $detectedGame.FullName).Replace('\', '/')
    Write-Host "Auto-detected GamePath: $GamePath"
}

$resolvedGamePath = Resolve-CanonicalPath -InputPath $GamePath -RepoRoot $repoRoot
$projectPath = Join-Path $resolvedGamePath "UnityProject"
if (!(Test-Path $projectPath)) {
    throw "UnityProject not found: $projectPath"
}

$storePath = Join-Path $resolvedGamePath "store.yaml"
& (Join-Path $PSScriptRoot "check-store-yaml.ps1") -StorePath $storePath
if (-not $?) {
    throw "check-store-yaml failed."
}

$packageName = Read-AndroidPackageNameFromStore -StoreYamlPath $storePath
if ([string]::IsNullOrWhiteSpace($packageName)) {
    throw "application_id_android not found in store.yaml: $storePath"
}

if (-not $SkipCompileCheck) {
    & (Join-Path $PSScriptRoot "check-unity-compile.ps1") -ProjectPath $projectPath -RequireUnity -RequireAndroidModule
    if (-not $?) {
        throw "check-unity-compile failed."
    }
}

if (-not $SkipAudioAnalysis) {
    & (Join-Path $PSScriptRoot "analyze-audio.ps1") -GamePath $resolvedGamePath
    if (-not $?) {
        throw "analyze-audio failed."
    }
}

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build-android-apk.ps1") -ProjectPath $projectPath -OutputName $OutputName
    if (-not $?) {
        throw "build-android-apk failed."
    }
}

$apkPath = Join-Path $repoRoot ("BuildArtifacts/" + $OutputName)
if (!(Test-Path $apkPath)) {
    throw "APK not found: $apkPath"
}

& (Join-Path $PSScriptRoot "install-android.ps1") -ApkPath $apkPath -Launch -PackageName $packageName
if (-not $?) {
    throw "install-android failed."
}

$adbPath = Resolve-AdbPath
if ([string]::IsNullOrWhiteSpace($adbPath)) {
    throw "adb not found."
}

$qaDir = Join-Path $repoRoot "BuildArtifacts/QA"
New-Item -ItemType Directory -Path $qaDir -Force | Out-Null
$logcatPath = Join-Path $qaDir "smoke-android-logcat.txt"
$reportPath = Join-Path $qaDir "smoke-android-report.txt"

& $adbPath logcat -c
if ($LASTEXITCODE -ne 0) {
    throw "adb logcat -c failed."
}

$eventCount = [Math]::Max(200, [int]($DurationSeconds * 2))
Write-Host "Running monkey test: package=$packageName duration~${DurationSeconds}s events=$eventCount"
& $adbPath shell monkey -p $packageName --throttle 500 -v $eventCount | Out-Null

& $adbPath logcat -d | Set-Content -Path $logcatPath
if ($LASTEXITCODE -ne 0) {
    throw "adb logcat dump failed."
}

$logRaw = Get-Content -Path $logcatPath -Raw
$crashPatterns = @(
    "FATAL EXCEPTION",
    "ANR in",
    "AndroidRuntime: FATAL EXCEPTION",
    "has died"
)

$crashHits = New-Object System.Collections.Generic.List[string]
foreach ($pattern in $crashPatterns) {
    if ([regex]::IsMatch($logRaw, [regex]::Escape($pattern), [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
        $crashHits.Add($pattern)
    }
}

$status = "PASS"
if ($crashHits.Count -gt 0) {
    $status = "FAIL"
}

@(
    "MiniLab Android Smoke Report",
    "status=$status",
    "game_path=$resolvedGamePath",
    "package=$packageName",
    "apk=$apkPath",
    "duration_seconds=$DurationSeconds",
    "events=$eventCount",
    "logcat=$logcatPath",
    "crash_signals=$($crashHits -join ',')"
) | Set-Content -Path $reportPath

Write-Host "Smoke report: $reportPath"
Write-Host "Logcat: $logcatPath"

if ($status -ne "PASS") {
    throw "Android smoke test failed. Crash/ANR signals detected: $($crashHits -join ', ')"
}

Write-Host "PASS: Android smoke test completed."
