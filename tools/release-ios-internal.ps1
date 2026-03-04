param(
    [string]$GamePath = "",
    [string]$BuildNumber = "",
    [string]$IpaName = "app-store.ipa",
    [int]$TimeoutMinutes = 20
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

$iosParams = @(
    "-ProjectPath", $projectPath,
    "-IpaName", $IpaName,
    "-TimeoutMinutes", "$TimeoutMinutes",
    "-Archive",
    "-UploadInternal"
)

if (-not [string]::IsNullOrWhiteSpace($BuildNumber)) {
    $iosParams += @("-BuildNumber", $BuildNumber)
}

& (Join-Path $PSScriptRoot "build-ios.ps1") @iosParams
if (-not $?) {
    throw "build-ios failed."
}

$ipaPath = Join-Path $repoRoot ("BuildArtifacts/iOS/" + $IpaName)
Write-Host "SUCCESS: iOS internal release pipeline completed."
Write-Host "IPA: $ipaPath"
