param(
    [string]$GamePath = "",
    [string]$OutputName = "",
    [string]$Track = "internal",
    [string]$BuildNumber = "",
    [int]$TimeoutMinutes = 20,
    [switch]$SkipCompileCheck,
    [switch]$SkipAudioAnalysis,
    [switch]$UploadMetadata
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

if ([string]::IsNullOrWhiteSpace($OutputName)) {
    $gameName = Split-Path -Leaf $resolvedGamePath
    $OutputName = "$gameName-review.aab"
}

$buildParams = @(
    "-ProjectPath", $projectPath,
    "-OutputName", $OutputName,
    "-TimeoutMinutes", "$TimeoutMinutes"
)

if (-not [string]::IsNullOrWhiteSpace($BuildNumber)) {
    $buildParams += @("-BuildNumber", $BuildNumber)
}

& (Join-Path $PSScriptRoot "build-android.ps1") @buildParams
if (-not $?) {
    throw "build-android failed."
}

$aabPath = Join-Path $repoRoot ("BuildArtifacts/Android/" + $OutputName)
if (!(Test-Path $aabPath)) {
    throw "AAB not found after build: $aabPath"
}

$uploadParams = @(
    "-AabPath", $aabPath,
    "-GamePath", $resolvedGamePath,
    "-StorePath", $storePath,
    "-Track", $Track
)

if ($UploadMetadata) {
    $uploadParams += "-UploadMetadata"
}

& (Join-Path $PSScriptRoot "upload-android-internal.ps1") @uploadParams
if (-not $?) {
    throw "upload-android-internal failed."
}

Write-Host "SUCCESS: Android internal release pipeline completed."
Write-Host "AAB: $aabPath"
