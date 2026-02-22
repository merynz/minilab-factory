param(
    [string]$UnityPath = "",
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [string]$BuildNumber = "",
    [string]$OutputName = "zebradash-dev.apk",
    [string]$ArtifactDir = "",
    [int]$TimeoutMinutes = 20,
    [switch]$SkipIfUnityMissing
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-UnityPath([string]$ExplicitPath) {
    if ($env:MINILAB_UNITY_PATH -and (Test-Path $env:MINILAB_UNITY_PATH)) {
        return (Resolve-Path $env:MINILAB_UNITY_PATH).Path
    }

    if ($ExplicitPath -and (Test-Path $ExplicitPath)) {
        return (Resolve-Path $ExplicitPath).Path
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

function Resolve-CanonicalProjectPath([string]$InputPath, [string]$RepoRoot) {
    $candidate = $InputPath
    if (-not [System.IO.Path]::IsPathRooted($candidate)) {
        $candidate = Join-Path $RepoRoot $candidate
    }
    $candidate = [System.IO.Path]::GetFullPath($candidate)
    if (!(Test-Path $candidate)) {
        throw "ProjectPath not found: $InputPath"
    }

    $resolved = (Resolve-Path $candidate).Path
    if ((Split-Path -Leaf $resolved) -ine "UnityProject") {
        $unityProjectSubdir = Join-Path $resolved "UnityProject"
        if (Test-Path $unityProjectSubdir) {
            $resolved = (Resolve-Path $unityProjectSubdir).Path
        }
    }

    return $resolved
}

function Show-UnityLogDiagnostics([string]$LogPath) {
    Write-Host "Unity log: $LogPath"
    if (!(Test-Path $LogPath)) {
        Write-Host "Unity log not found."
        return
    }

    Write-Host "----- LOG TAIL (last 200 lines) -----"
    Get-Content -Path $LogPath -Tail 200 | ForEach-Object { Write-Host $_ }

    Write-Host "----- C# COMPILE ERRORS (error CS####) -----"
    $csErrors = Select-String -Path $LogPath -Pattern 'error CS\d+' -CaseSensitive:$false
    if ($csErrors) {
        $csErrors | ForEach-Object { Write-Host $_.Line }
    } else {
        Write-Host "(none)"
    }
}

function Invoke-UnityWithTimeout([string]$ExePath, [string[]]$ArgumentList, [int]$TimeoutMins, [string]$LogPath) {
    $argsPreview = ($ArgumentList | ForEach-Object {
            if ($_ -match '\s') { '"' + $_ + '"' } else { $_ }
        }) -join ' '
    Write-Host "Unity command: `"$ExePath`" $argsPreview"

    $process = Start-Process -FilePath $ExePath -ArgumentList $ArgumentList -PassThru -NoNewWindow

    Start-Sleep -Seconds 8
    $process.Refresh()
    if (-not $process.HasExited -and $process.MainWindowHandle -ne 0) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Show-UnityLogDiagnostics $LogPath
        throw "FAIL: interactive launch happened (Unity GUI window detected)."
    }

    $waitMs = [int]([Math]::Max(1, $TimeoutMins) * 60 * 1000)
    $completed = $process.WaitForExit($waitMs)
    if (-not $completed) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Show-UnityLogDiagnostics $LogPath
        throw "Android APK build timed out after $TimeoutMins minutes."
    }

    return $process.ExitCode
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedUnityPath = Resolve-UnityPath $UnityPath
if ([string]::IsNullOrWhiteSpace($resolvedUnityPath)) {
    $msg = "Unity executable not found. Provide -UnityPath or MINILAB_UNITY_PATH."
    if ($SkipIfUnityMissing) {
        Write-Host "SKIP: $msg"
        exit 0
    }
    throw $msg
}

$resolvedProjectPath = Resolve-CanonicalProjectPath $ProjectPath $repoRoot
Write-Host "Resolved ProjectPath: $resolvedProjectPath"

if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
    $ArtifactDir = Join-Path $repoRoot "BuildArtifacts"
}
New-Item -ItemType Directory -Path $ArtifactDir -Force | Out-Null

$logFile = Join-Path $repoRoot "BuildArtifacts/unity-android-apk-build.log"
$outputApkPath = Join-Path $ArtifactDir $OutputName

$env:MINILAB_ANDROID_ARTIFACT_DIR = $ArtifactDir
$env:MINILAB_ANDROID_APK_NAME = $OutputName
if ($BuildNumber -ne "") {
    $env:MINILAB_ANDROID_BUILD_NUMBER = $BuildNumber
}

$storeCandidatePaths = @(
    (Join-Path $resolvedProjectPath "store.yaml"),
    (Join-Path $resolvedProjectPath "..\store.yaml")
)
$storePath = $storeCandidatePaths |
    ForEach-Object { [System.IO.Path]::GetFullPath($_) } |
    Where-Object { Test-Path $_ } |
    Select-Object -First 1
if ($storePath) {
    $env:MINILAB_STORE_PATH = $storePath
}

$args = @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath", $resolvedProjectPath,
    "-buildTarget", "Android",
    "-executeMethod", "MiniLab.Build.BuildPipelineEntry.BuildAndroidApk",
    "-stackTraceLogType", "Full",
    "-logFile", $logFile
)

$exitCode = Invoke-UnityWithTimeout $resolvedUnityPath $args $TimeoutMinutes $logFile
Show-UnityLogDiagnostics $logFile
if ($exitCode -ne 0) {
    throw "Android APK build failed (exit $exitCode). Log: $logFile"
}

if (!(Test-Path $outputApkPath)) {
    throw "Android APK build succeeded but APK not found: $outputApkPath"
}

Write-Host "Android APK build completed."
Write-Host "APK: $outputApkPath"
