param(
    [string]$GamePath = "",
    [ValidateSet("android", "ios", "both")][string]$Platform = "both",
    [string]$BuildNumber = "",
    [string]$AndroidTrack = "internal",
    [bool]$UploadMetadata = $true,
    [bool]$RunAndroidSmokeTest = $true,
    [bool]$DispatchIosFromWindows = $true,
    [string]$GithubRepo = "",
    [switch]$SkipReadinessCheck
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

function Get-RepoSlugFromGitRemote([string]$RepoRoot) {
    if (-not [string]::IsNullOrWhiteSpace($GithubRepo)) {
        return $GithubRepo
    }

    Push-Location $RepoRoot
    $remote = (& git remote get-url origin 2>$null)
    Pop-Location
    if ([string]::IsNullOrWhiteSpace($remote)) {
        return ""
    }

    $patterns = @(
        'github\.com[:/](?<slug>[^/\s]+/[^/\s]+?)(?:\.git)?$',
        'https://github\.com/(?<slug>[^/\s]+/[^/\s]+?)(?:\.git)?$',
        'ssh://git@github\.com/(?<slug>[^/\s]+/[^/\s]+?)(?:\.git)?$'
    )

    foreach ($pattern in $patterns) {
        $match = [regex]::Match($remote, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($match.Success) {
            return $match.Groups["slug"].Value
        }
    }

    return ""
}

function Dispatch-IosWorkflow([string]$RepoRoot, [string]$GamePathArg, [string]$BuildNumberArg, [string]$RepoSlug, [string]$TrackArg, [bool]$UploadMetadataArg) {
    $ghPath = Resolve-GhPath
    if ([string]::IsNullOrWhiteSpace($ghPath)) {
        throw "GitHub CLI (gh) not found. Install gh or run iOS release on macOS runner."
    }

    if ([string]::IsNullOrWhiteSpace($RepoSlug)) {
        throw "Could not resolve GitHub repo slug. Set -GithubRepo owner/repo."
    }

    $uploadFlag = if ($UploadMetadataArg) { "true" } else { "false" }

    $args = @(
        "workflow", "run", "mobile-release.yml",
        "-R", $RepoSlug,
        "-f", "game_path=$GamePathArg",
        "-f", "platform=ios",
        "-f", "android_track=$TrackArg",
        "-f", "upload_metadata=$uploadFlag"
    )

    if (-not [string]::IsNullOrWhiteSpace($BuildNumberArg)) {
        $args += @("-f", "build_number=$BuildNumberArg")
    } else {
        $args += @("-f", "build_number=")
    }

    Write-Host "Dispatching iOS release workflow: repo=$RepoSlug game=$GamePathArg"
    $code = (Start-Process -FilePath $ghPath -ArgumentList $args -Wait -PassThru -NoNewWindow).ExitCode
    if ($code -ne 0) {
        throw "Failed to dispatch iOS workflow with gh (exit $code)."
    }
}

function Resolve-GhPath {
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($gh) {
        return $gh.Source
    }

    $candidates = @(
        "C:\Program Files\GitHub CLI\gh.exe",
        "C:\Program Files (x86)\GitHub CLI\gh.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    return ""
}

function Run-ChildPowerShell([string]$ScriptPath, [string[]]$Arguments) {
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $args = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $ScriptPath)
    if ($Arguments) {
        $args += $Arguments
    }

    $process = Start-Process -FilePath $pwsh -ArgumentList $args -Wait -PassThru -NoNewWindow
    return $process.ExitCode
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
$storePath = Join-Path $resolvedGamePath "store.yaml"
& (Join-Path $PSScriptRoot "check-store-yaml.ps1") -StorePath $storePath

if (-not $SkipReadinessCheck) {
    $readinessArgs = @("-Platform", $Platform, "-GamePath", $resolvedGamePath)
    $readyCode = Run-ChildPowerShell -ScriptPath (Join-Path $PSScriptRoot "platform-ready.ps1") -Arguments $readinessArgs
    if ($readyCode -ne 0) {
        throw "Platform readiness check failed. Resolve FAIL items and retry."
    }
}

if ($Platform -eq "android" -or $Platform -eq "both") {
    if ($RunAndroidSmokeTest) {
        & (Join-Path $PSScriptRoot "smoke-android.ps1") -GamePath $resolvedGamePath -SkipCompileCheck -SkipAudioAnalysis
    }

    $androidArgs = @(
        "-GamePath", $resolvedGamePath,
        "-Track", $AndroidTrack
    )
    if (-not [string]::IsNullOrWhiteSpace($BuildNumber)) {
        $androidArgs += @("-BuildNumber", $BuildNumber)
    }
    if ($UploadMetadata) {
        $androidArgs += "-UploadMetadata"
    }

    & (Join-Path $PSScriptRoot "release-android-internal.ps1") @androidArgs
}

if ($Platform -eq "ios" -or $Platform -eq "both") {
    if ($IsMacOS) {
        $iosArgs = @("-GamePath", $resolvedGamePath)
        if (-not [string]::IsNullOrWhiteSpace($BuildNumber)) {
            $iosArgs += @("-BuildNumber", $BuildNumber)
        }

        & (Join-Path $PSScriptRoot "release-ios-internal.ps1") @iosArgs
    } else {
        if (-not $DispatchIosFromWindows) {
            throw "iOS local release requires macOS. Set -DispatchIosFromWindows true to trigger GitHub workflow."
        }

        $repoSlug = Get-RepoSlugFromGitRemote -RepoRoot $repoRoot
        $gamePathRelative = [System.IO.Path]::GetRelativePath($repoRoot, $resolvedGamePath).Replace('\', '/')
        Dispatch-IosWorkflow -RepoRoot $repoRoot -GamePathArg $gamePathRelative -BuildNumberArg $BuildNumber -RepoSlug $repoSlug -TrackArg $AndroidTrack -UploadMetadataArg $UploadMetadata
    }
}

Write-Host "SUCCESS: deploy-platform completed."
