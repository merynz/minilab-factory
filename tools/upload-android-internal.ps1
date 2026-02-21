param(
    [string]$AabPath = "",
    [string]$PackageName = "",
    [string]$GamePath = "",
    [string]$StorePath = "",
    [string]$Track = "internal",
    [switch]$SkipIfSecretsMissing
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Native([string]$FilePath, [string[]]$Arguments) {
    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -Wait -PassThru -NoNewWindow
    return $process.ExitCode
}

function Resolve-StorePath([string]$ExplicitStorePath, [string]$ExplicitGamePath, [string]$RepoRoot) {
    if ($ExplicitStorePath) {
        if ([System.IO.Path]::IsPathRooted($ExplicitStorePath)) {
            return [System.IO.Path]::GetFullPath($ExplicitStorePath)
        }

        return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $ExplicitStorePath))
    }

    if ($ExplicitGamePath) {
        $gameRoot = $ExplicitGamePath
        if (-not [System.IO.Path]::IsPathRooted($gameRoot)) {
            $gameRoot = Join-Path $RepoRoot $gameRoot
        }

        return [System.IO.Path]::GetFullPath((Join-Path $gameRoot "store.yaml"))
    }

    if ($env:MINILAB_STORE_PATH) {
        return [System.IO.Path]::GetFullPath($env:MINILAB_STORE_PATH)
    }

    return ""
}

function Read-AndroidPackageNameFromStore([string]$StoreYamlPath) {
    if ([string]::IsNullOrWhiteSpace($StoreYamlPath) -or !(Test-Path $StoreYamlPath)) {
        return ""
    }

    $raw = Get-Content -Path $StoreYamlPath -Raw
    $match = [regex]::Match($raw, '(?m)^\s*application_id_android\s*:\s*[''"]?([A-Za-z0-9._]+)[''"]?\s*$')
    if ($match.Success) {
        return $match.Groups[1].Value.Trim()
    }

    return ""
}

if (!(Get-Command bundle -ErrorAction SilentlyContinue)) {
    $message = "Ruby bundler is required for fastlane."
    if ($SkipIfSecretsMissing) {
        Write-Host "SKIP: $message"
        exit 0
    }

    throw "$message Install bundler first."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactDir = Join-Path $repoRoot "BuildArtifacts/Android"

if ([string]::IsNullOrWhiteSpace($AabPath)) {
    $latestAab = Get-ChildItem -Path $artifactDir -Filter *.aab -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($latestAab) {
        $AabPath = $latestAab.FullName
    }
}

if ([string]::IsNullOrWhiteSpace($AabPath) -or !(Test-Path $AabPath)) {
    throw "AAB not found. Provide -AabPath or create build artifact first."
}

if ([string]::IsNullOrWhiteSpace($PackageName)) {
    $resolvedStorePath = Resolve-StorePath -ExplicitStorePath $StorePath -ExplicitGamePath $GamePath -RepoRoot $repoRoot
    $PackageName = Read-AndroidPackageNameFromStore -StoreYamlPath $resolvedStorePath
}

if ([string]::IsNullOrWhiteSpace($PackageName)) {
    throw "PackageName is required and could not be derived from store.yaml."
}

if ([string]::IsNullOrWhiteSpace($env:GOOGLE_PLAY_JSON_KEY_PATH) -or !(Test-Path $env:GOOGLE_PLAY_JSON_KEY_PATH)) {
    $message = "GOOGLE_PLAY_JSON_KEY_PATH secret is missing."
    if ($SkipIfSecretsMissing) {
        Write-Host "SKIP: $message"
        exit 0
    }

    throw $message
}

$env:MINILAB_ANDROID_AAB_PATH = $AabPath
$env:MINILAB_ANDROID_PACKAGE_NAME = $PackageName
$env:MINILAB_ANDROID_TRACK = $Track

Push-Location $repoRoot
$code = Invoke-Native "bundle" @("exec", "fastlane", "android", "internal")
Pop-Location

if ($code -ne 0) {
    throw "Android internal upload failed."
}

Write-Host "Android internal upload completed."
