param(
    [string]$UnityPath = "",
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [string]$BuildNumber = "",
    [string]$ExportDir = "",
    [string]$IpaName = "app-store.ipa",
    [int]$TimeoutMinutes = 20,
    [switch]$Archive,
    [switch]$UploadInternal,
    [switch]$SkipIfNoMac
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

    if (-not $IsMacOS) { return "" }

    $candidate = Get-ChildItem -Path "/Applications/Unity/Hub/Editor" -Recurse -Filter "Unity" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like "*/Contents/MacOS/Unity" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($candidate) { return $candidate.FullName }
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

function Get-UnityVersion([string]$UnityExePath) {
    $editorDir = Split-Path -Parent $UnityExePath
    $versionDir = Split-Path -Parent $editorDir
    return Split-Path -Leaf $versionDir
}

function Get-LogRootCause([string]$LogPath) {
    if (!(Test-Path $LogPath)) { return "" }
    $raw = Get-Content -Path $LogPath -Raw
    $patterns = @(
        @{ Regex = "(?im)Project has invalid dependencies.*"; Reason = "Project has invalid UPM dependencies (manifest local path may be wrong)." },
        @{ Regex = "(?im)Unable to add package.*com\.zebratank\.minilab\.core.*"; Reason = "UPM failed to resolve com.zebratank.minilab.core." },
        @{ Regex = "(?im)bundle_id_ios.*bulunamadi|bundle id.*invalid"; Reason = "iOS bundle id not found/invalid in store.yaml." },
        @{ Regex = "(?im)No scene found|No enabled scenes found"; Reason = "Build Settings scene list is empty; bootstrap scene guard is being used or failed." }
    )
    foreach ($pattern in $patterns) {
        if ([regex]::IsMatch($raw, $pattern.Regex)) {
            return $pattern.Reason
        }
    }
    return ""
}

function Show-UnityLogDiagnostics([string]$LogPath) {
    Write-Host "Unity log: $LogPath"
    if (!(Test-Path $LogPath)) {
        Write-Host "Unity log not found."
        return
    }

    $rootCause = Get-LogRootCause $LogPath
    if (-not [string]::IsNullOrWhiteSpace($rootCause)) {
        Write-Host "Root cause: $rootCause"
    }

    $firstException = Select-String -Path $LogPath -Pattern '(?im)\b[A-Za-z0-9_.]*Exception:.*' | Select-Object -First 1
    if ($firstException) {
        Write-Host "First exception: $($firstException.Line.Trim())"
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

function Invoke-WithTimeout([string]$FilePath, [string[]]$Args, [int]$TimeoutMins, [string]$LogPath) {
    $argsPreview = ($Args | ForEach-Object {
            if ($_ -match '\s') { '"' + $_ + '"' } else { $_ }
        }) -join ' '
    Write-Host "Command: `"$FilePath`" $argsPreview"

    $process = Start-Process -FilePath $FilePath -ArgumentList $Args -PassThru -NoNewWindow

    if ($FilePath -like "*Unity*") {
        Start-Sleep -Seconds 8
        $process.Refresh()
        if (-not $process.HasExited -and $process.MainWindowHandle -ne 0) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            if ($LogPath) { Show-UnityLogDiagnostics $LogPath }
            throw "FAIL: interactive launch happened (Unity GUI window detected)."
        }
    }

    $completed = $process.WaitForExit([int]([Math]::Max(1, $TimeoutMins) * 60 * 1000))
    if (-not $completed) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        if ($LogPath) { Show-UnityLogDiagnostics $LogPath }
        throw "Command timed out after $TimeoutMins minutes."
    }
    return $process.ExitCode
}

if (-not $IsMacOS) {
    $message = "iOS build is blocked by Mac/signing requirements."
    if ($SkipIfNoMac) {
        Write-Host "SKIP: $message"
        Write-Host "Alternatives: remote Mac, GitHub Actions macOS runner, Unity Cloud Build."
        exit 0
    }
    throw $message
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedUnityPath = Resolve-UnityPath $UnityPath
if ([string]::IsNullOrWhiteSpace($resolvedUnityPath)) {
    throw "Unity executable not found. Provide -UnityPath or MINILAB_UNITY_PATH."
}

$unityVersion = Get-UnityVersion $resolvedUnityPath
Write-Host "Unity detected: $unityVersion ($resolvedUnityPath)"

$iosModulePath = Join-Path (Split-Path -Parent $resolvedUnityPath) "Data/PlaybackEngines/iOSSupport"
if (!(Test-Path $iosModulePath)) {
    Write-Host "SKIP: iOS Build Support module is missing for Unity $unityVersion."
    Write-Host "Install from Unity Hub -> Installs -> Add modules -> iOS Build Support."
    exit 0
}

$resolvedProjectPath = Resolve-CanonicalProjectPath $ProjectPath $repoRoot
Write-Host "Resolved ProjectPath: $resolvedProjectPath"

if ([string]::IsNullOrWhiteSpace($ExportDir)) {
    $ExportDir = Join-Path $repoRoot "BuildArtifacts/iOS/XcodeProject"
}

$artifactDir = Join-Path $repoRoot "BuildArtifacts/iOS"
New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null
New-Item -ItemType Directory -Path $ExportDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $repoRoot "BuildArtifacts") -Force | Out-Null

$logFile = Join-Path $repoRoot "BuildArtifacts/unity-ios-build.log"
$env:MINILAB_IOS_EXPORT_DIR = $ExportDir
if ($BuildNumber -ne "") {
    $env:MINILAB_IOS_BUILD_NUMBER = $BuildNumber
}

$unityArgs = @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath", $resolvedProjectPath,
    "-buildTarget", "iOS",
    "-executeMethod", "MiniLab.Build.BuildPipelineEntry.BuildiOSXcodeProject",
    "-stackTraceLogType", "Full",
    "-logFile", $logFile
)

$exportCode = Invoke-WithTimeout $resolvedUnityPath $unityArgs $TimeoutMinutes $logFile
Show-UnityLogDiagnostics $logFile
if ($exportCode -ne 0) {
    throw "iOS build export failed (exit $exportCode). Log: $logFile"
}

Write-Host "iOS Xcode export completed: $ExportDir"

if ($Archive -or $UploadInternal) {
    if (!(Get-Command bundle -ErrorAction SilentlyContinue)) {
        throw "Ruby bundler is required for fastlane archive lane."
    }

    $xcodeProjPath = Join-Path $ExportDir "Unity-iPhone.xcodeproj"
    if (!(Test-Path $xcodeProjPath)) {
        throw "Xcode project not found after export: $xcodeProjPath"
    }

    $env:MINILAB_IOS_XCODEPROJ = $xcodeProjPath
    $env:MINILAB_IOS_OUTPUT_DIR = $artifactDir
    $env:MINILAB_IOS_IPA_NAME = $IpaName
    $archiveCode = Invoke-WithTimeout "bundle" @("exec", "fastlane", "ios", "build_archive") $TimeoutMinutes ""
    if ($archiveCode -ne 0) {
        throw "iOS archive/export failed."
    }
}

if ($UploadInternal) {
    $ipaPath = Join-Path $artifactDir $IpaName
    & (Join-Path $PSScriptRoot "upload-testflight-internal.ps1") -IpaPath $ipaPath -SkipIfNoMac -SkipIfSecretsMissing
}
