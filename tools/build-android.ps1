param(
    [string]$UnityPath = "",
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [string]$BuildNumber = "",
    [string]$OutputName = "",
    [string]$ArtifactDir = "",
    [string]$PackageName = "",
    [int]$TimeoutMinutes = 20,
    [switch]$UploadInternal,
    [switch]$SkipIfUnityMissing,
    [switch]$AllowNonGameProject
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
        "C:\\Program Files\\Unity\\Hub\\Editor",
        "C:\\Program Files\\Unity"
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
        @{ Regex = "(?im)store\.yaml bulunamadi|store\.yaml not found"; Reason = "store.yaml missing in project/game path." },
        @{ Regex = "(?im)application_id_android.*bulunamadi|applicationId.*invalid"; Reason = "Android package name not found/invalid in store.yaml." },
        @{ Regex = "(?im)keystore.*bulunamadi|keystore.*not found"; Reason = "Custom keystore path is configured but file is missing." },
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
        throw "Android build timed out after $TimeoutMins minutes."
    }

    return $process.ExitCode
}

function Restore-UnitySettingsNoise([string]$RepoRoot, [string]$ResolvedProjectPath) {
    if ([string]::IsNullOrWhiteSpace($ResolvedProjectPath)) {
        return
    }

    $relativePath = [System.IO.Path]::GetRelativePath($RepoRoot, $ResolvedProjectPath)
    $settingsRoot = Join-Path $relativePath "ProjectSettings"
    $paths = @(
        (Join-Path $settingsRoot "GraphicsSettings.asset"),
        (Join-Path $settingsRoot "QualitySettings.asset"),
        (Join-Path $settingsRoot "PackageManagerSettings.asset"),
        (Join-Path $settingsRoot "URPProjectSettings.asset")
    )

    git restore --source=HEAD --worktree -- $paths 2>$null | Out-Null
    git clean -f -- $paths 2>$null | Out-Null
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

$unityVersion = Get-UnityVersion $resolvedUnityPath
Write-Host "Unity detected: $unityVersion ($resolvedUnityPath)"

$androidModulePath = Join-Path (Split-Path -Parent $resolvedUnityPath) "Data/PlaybackEngines/AndroidPlayer"
if (!(Test-Path $androidModulePath)) {
    Write-Host "SKIP: Android Build Support module is missing for Unity $unityVersion."
    Write-Host "Install from Unity Hub -> Installs -> Add modules -> Android Build Support."
    exit 0
}
Write-Host "Android Build Support module: detected"

$resolvedProjectPath = Resolve-CanonicalProjectPath $ProjectPath $repoRoot
Write-Host "Resolved ProjectPath: $resolvedProjectPath"

$syncScript = Join-Path $PSScriptRoot "sync-zebradash-content.ps1"
if (Test-Path $syncScript) {
    Write-Host "Running ZebraDash content sync before AAB build..."
    & $syncScript -ProjectPath $resolvedProjectPath
    if ($LASTEXITCODE -ne 0) {
        throw "Content sync failed with exit code $LASTEXITCODE"
    }
}

if (-not $AllowNonGameProject) {
    if ($resolvedProjectPath -notmatch '[\\/]Games[\\/]Game_[^\\/]+[\\/]UnityProject$') {
        throw "AAB build must run on Games/<Game>/UnityProject. Use -AllowNonGameProject to override."
    }
}

if ([string]::IsNullOrWhiteSpace($ArtifactDir)) {
    $ArtifactDir = Join-Path $repoRoot "BuildArtifacts/Android"
}
New-Item -ItemType Directory -Path $ArtifactDir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $repoRoot "BuildArtifacts") -Force | Out-Null

$gameName = Split-Path -Leaf (Split-Path -Parent $resolvedProjectPath)
if ([string]::IsNullOrWhiteSpace($OutputName)) {
    $OutputName = "$gameName.aab"
}

$logFile = Join-Path $repoRoot "BuildArtifacts/unity-android-build.log"
$outputAabPath = Join-Path $ArtifactDir $OutputName

$env:MINILAB_ANDROID_ARTIFACT_DIR = $ArtifactDir
$env:MINILAB_ANDROID_AAB_NAME = $OutputName
if ($BuildNumber -ne "") {
    $env:MINILAB_ANDROID_BUILD_NUMBER = $BuildNumber
}

$storeCandidatePaths = @(
    (Join-Path $resolvedProjectPath "store.yaml"),
    (Join-Path $resolvedProjectPath "..\\store.yaml")
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
    "-executeMethod", "MiniLab.Build.BuildPipelineEntry.BuildAndroidAab",
    "-stackTraceLogType", "Full",
    "-logFile", $logFile
)

$exitCode = Invoke-UnityWithTimeout $resolvedUnityPath $args $TimeoutMinutes $logFile
Show-UnityLogDiagnostics $logFile
Restore-UnitySettingsNoise -RepoRoot $repoRoot -ResolvedProjectPath $resolvedProjectPath
if ($exitCode -ne 0) {
    throw "Android build failed (exit $exitCode). Log: $logFile"
}

if (!(Test-Path $outputAabPath)) {
    throw "Android build succeeded but AAB not found: $outputAabPath"
}

Write-Host "Android build completed."
Write-Host "AAB: $outputAabPath"

if ($UploadInternal) {
    $gamePath = Split-Path -Parent $resolvedProjectPath
    if ([string]::IsNullOrWhiteSpace($PackageName)) {
        & (Join-Path $PSScriptRoot "upload-android-internal.ps1") `
            -AabPath $outputAabPath `
            -GamePath $gamePath `
            -StorePath $storePath `
            -SkipIfSecretsMissing
    } else {
        & (Join-Path $PSScriptRoot "upload-android-internal.ps1") `
            -AabPath $outputAabPath `
            -PackageName $PackageName `
            -SkipIfSecretsMissing
    }
}
