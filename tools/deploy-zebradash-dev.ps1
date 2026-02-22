param(
    [string]$ProjectPath = "Games/Game_Arcade_ZebraDash/UnityProject",
    [string]$GamePath = "Games/Game_Arcade_ZebraDash",
    [string]$OutputName = "zebradash-dev.apk",
    [switch]$StartLogcat
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

function Invoke-Step([string]$Label, [scriptblock]$Action) {
    Write-Host "---- $Label ----"
    & $Action
    if (-not $?) {
        throw "$Label failed."
    }
}

function Get-RootCauseHint([string]$LogContent) {
    if ([string]::IsNullOrWhiteSpace($LogContent)) {
        return "No Unity log content available."
    }

    if ($LogContent -match 'package\.json cannot be found|Unable to add package') {
        return "Local package dependency path is invalid (check manifest file: com.zebratank.minilab.core)."
    }
    if ($LogContent -match 'error CS\d+') {
        return "C# compile error detected."
    }
    if ($LogContent -match '(?im)\b[A-Za-z0-9_.]*Exception:') {
        return "Unhandled exception found in Unity build log."
    }
    if ($LogContent -match 'keystore|jks|No key with alias') {
        return "Android signing/keystore configuration issue."
    }

    return "See log tail below for failure details."
}

function Show-LogDiagnostics([string]$RepoRoot) {
    $logFiles = @(
        (Join-Path $RepoRoot "BuildArtifacts/unity-compile.log"),
        (Join-Path $RepoRoot "BuildArtifacts/unity-android-apk-build.log"),
        (Join-Path $RepoRoot "BuildArtifacts/unity-android-build.log")
    )

    foreach ($log in $logFiles) {
        if (!(Test-Path $log)) {
            continue
        }

        Write-Host ""
        Write-Host "Log: $log"
        Write-Host "----- tail -200 -----"
        Get-Content -Path $log -Tail 200 | ForEach-Object { Write-Host $_ }
        Write-Host "----- root-cause hint -----"
        $content = Get-Content -Path $log -Raw
        Write-Host (Get-RootCauseHint -LogContent $content)

        $firstException = Select-String -Path $log -Pattern '(?im)\b[A-Za-z0-9_.]*Exception:.*' | Select-Object -First 1
        if ($firstException) {
            Write-Host "First exception: $($firstException.Line.Trim())"
        }

        $csErrors = Select-String -Path $log -Pattern 'error CS\d+' -CaseSensitive:$false
        if ($csErrors) {
            Write-Host "C# compile errors:"
            $csErrors | Select-Object -First 20 | ForEach-Object { Write-Host $_.Line.Trim() }
        }
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedProjectPath = Resolve-CanonicalPath -InputPath $ProjectPath -RepoRoot $repoRoot
$resolvedGamePath = Resolve-CanonicalPath -InputPath $GamePath -RepoRoot $repoRoot

Write-Host "RepoRoot: $repoRoot"
Write-Host "ProjectPath: $resolvedProjectPath"
Write-Host "GamePath: $resolvedGamePath"

try {
    Invoke-Step "Doctor" {
        & (Join-Path $PSScriptRoot "doctor.ps1")
    }

    Invoke-Step "Analyze Audio (SKIP allowed if WAV missing)" {
        & (Join-Path $PSScriptRoot "analyze-audio.ps1") -GamePath $resolvedGamePath
    }

    Invoke-Step "Sync ZebraDash Content" {
        & (Join-Path $PSScriptRoot "sync-zebradash-content.ps1") -ProjectPath $resolvedProjectPath
    }

    Invoke-Step "Unity Compile Check" {
        & (Join-Path $PSScriptRoot "check-unity-compile.ps1") -ProjectPath $resolvedProjectPath
    }

    Invoke-Step "Build Android APK" {
        & (Join-Path $PSScriptRoot "build-android-apk.ps1") -ProjectPath $resolvedProjectPath -OutputName $OutputName
    }

    $apkPath = Join-Path $repoRoot ("BuildArtifacts/" + $OutputName)
    Invoke-Step "Install APK (-r)" {
        & (Join-Path $PSScriptRoot "install-android.ps1") -ApkPath $apkPath
    }

    $storePath = Join-Path $resolvedGamePath "store.yaml"
    $packageName = Get-AndroidPackageName -StorePath $storePath
    if ([string]::IsNullOrWhiteSpace($packageName)) {
        throw "application_id_android missing in $storePath"
    }

    $adbPath = Resolve-AdbPath
    if ([string]::IsNullOrWhiteSpace($adbPath)) {
        throw "adb not found for launch/logcat."
    }

    Invoke-Step "Launch app" {
        & $adbPath shell monkey -p $packageName -c android.intent.category.LAUNCHER 1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "adb launch failed for package $packageName"
        }
    }

    if ($StartLogcat) {
        Write-Host "Starting live logcat (Ctrl+C to stop)..."
        & $adbPath logcat -s Unity ActivityManager AndroidRuntime
    }

    Write-Host "SUCCESS: deploy-zebradash-dev completed."
    exit 0
}
catch {
    Write-Host "FAIL: $($_.Exception.Message)"
    Show-LogDiagnostics -RepoRoot $repoRoot
    exit 1
}
