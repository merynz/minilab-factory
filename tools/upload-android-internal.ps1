param(
    [string]$AabPath = "",
    [string]$PackageName = "",
    [string]$GamePath = "",
    [string]$StorePath = "",
    [string]$Track = "internal",
    [switch]$UploadMetadata,
    [switch]$SkipMetadataIfMissing,
    [switch]$SkipIfSecretsMissing
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Native([string]$FilePath, [string[]]$Arguments) {
    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -Wait -PassThru -NoNewWindow
    return $process.ExitCode
}

function Resolve-FastlaneInvocation([string]$RepoRoot) {
    $bundle = Get-Command bundle -ErrorAction SilentlyContinue
    $fastlane = Get-Command fastlane -ErrorAction SilentlyContinue
    $gemfilePath = Join-Path $RepoRoot "Gemfile"
    $bundleFallbacks = @(
        "C:\Ruby33-x64\bin\bundle.bat",
        "C:\Ruby33-x64\bin\bundle"
    )
    $fastlaneFallbacks = @(
        "C:\Ruby33-x64\bin\fastlane.bat",
        "C:\Ruby33-x64\bin\fastlane"
    )
    if (-not $bundle) {
        foreach ($candidate in $bundleFallbacks) {
            if (Test-Path $candidate) {
                $bundle = [pscustomobject]@{ Source = (Resolve-Path $candidate).Path }
                break
            }
        }
    }
    if (-not $fastlane) {
        foreach ($candidate in $fastlaneFallbacks) {
            if (Test-Path $candidate) {
                $fastlane = [pscustomobject]@{ Source = (Resolve-Path $candidate).Path }
                break
            }
        }
    }

    if ($bundle -and (Test-Path $gemfilePath)) {
        return @{
            FilePath = $bundle.Source
            Prefix = @("exec", "fastlane")
            Mode = "bundle"
        }
    }

    if ($fastlane) {
        return @{
            FilePath = $fastlane.Source
            Prefix = @()
            Mode = "direct"
        }
    }

    if ($bundle -and !(Test-Path $gemfilePath)) {
        throw "Gemfile not found at repo root. Add Gemfile or install fastlane globally."
    }

    throw "Fastlane executable not found. Install Ruby + bundler and run bundle install."
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

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactDir = Join-Path $repoRoot "BuildArtifacts/Android"
$resolvedStorePath = Resolve-StorePath -ExplicitStorePath $StorePath -ExplicitGamePath $GamePath -RepoRoot $repoRoot

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
    $PackageName = Read-AndroidPackageNameFromStore -StoreYamlPath $resolvedStorePath
}

if ([string]::IsNullOrWhiteSpace($PackageName)) {
    throw "PackageName is required and could not be derived from store.yaml."
}

if (-not [string]::IsNullOrWhiteSpace($resolvedStorePath) -and (Test-Path $resolvedStorePath)) {
    & (Join-Path $PSScriptRoot "check-store-yaml.ps1") -StorePath $resolvedStorePath
    if (-not $?) {
        throw "store.yaml validation failed: $resolvedStorePath"
    }
}

$playJsonKeyPath = ""
if (-not [string]::IsNullOrWhiteSpace($env:MINILAB_PLAY_JSON)) {
    $playJsonKeyPath = $env:MINILAB_PLAY_JSON
} elseif (-not [string]::IsNullOrWhiteSpace($env:GOOGLE_PLAY_JSON_KEY_PATH)) {
    $playJsonKeyPath = $env:GOOGLE_PLAY_JSON_KEY_PATH
}

if ([string]::IsNullOrWhiteSpace($playJsonKeyPath) -or !(Test-Path $playJsonKeyPath)) {
    $message = "Play service account JSON secret is missing. Set MINILAB_PLAY_JSON (preferred) or GOOGLE_PLAY_JSON_KEY_PATH."
    if ($SkipIfSecretsMissing) {
        Write-Host "SKIP: $message"
        exit 0
    }

    throw $message
}

$env:MINILAB_ANDROID_AAB_PATH = $AabPath
$env:MINILAB_ANDROID_PACKAGE_NAME = $PackageName
$env:MINILAB_ANDROID_TRACK = $Track
$env:MINILAB_PLAY_JSON = $playJsonKeyPath
$env:GOOGLE_PLAY_JSON_KEY_PATH = $playJsonKeyPath
$env:SUPPLY_JSON_KEY = $playJsonKeyPath
$env:MINILAB_ANDROID_UPLOAD_METADATA = "0"
$env:MINILAB_ANDROID_METADATA_PATH = ""

if ($UploadMetadata) {
    if ([string]::IsNullOrWhiteSpace($resolvedStorePath) -or !(Test-Path $resolvedStorePath)) {
        $message = "store.yaml is required for metadata upload."
        if ($SkipMetadataIfMissing) {
            Write-Host "WARN: $message Metadata upload disabled."
        } else {
            throw $message
        }
    } else {
        $metadataOutputRoot = Join-Path $repoRoot "BuildArtifacts/store-metadata"
        try {
            & (Join-Path $PSScriptRoot "export-store-metadata.ps1") -StorePath $resolvedStorePath -Platform android -OutputRoot $metadataOutputRoot
            if (-not $?) {
                throw "export-store-metadata failed."
            }

            $androidMetadataPath = Join-Path $metadataOutputRoot "android"
            if (!(Test-Path $androidMetadataPath)) {
                throw "Android metadata path not found: $androidMetadataPath"
            }

            $env:MINILAB_ANDROID_UPLOAD_METADATA = "1"
            $env:MINILAB_ANDROID_METADATA_PATH = $androidMetadataPath
        } catch {
            if ($SkipMetadataIfMissing) {
                Write-Host "WARN: Metadata export failed and metadata upload will be skipped."
                Write-Host $_.Exception.Message
            } else {
                throw
            }
        }
    }
}

$fastlaneInvoker = $null
try {
    $fastlaneInvoker = Resolve-FastlaneInvocation -RepoRoot $repoRoot
} catch {
    if ($SkipIfSecretsMissing) {
        Write-Host "SKIP: $($_.Exception.Message)"
        exit 0
    }

    throw
}

Push-Location $repoRoot
$fastlaneArgs = @()
$fastlaneArgs += $fastlaneInvoker.Prefix
$fastlaneArgs += @("android", "internal")
$code = Invoke-Native $fastlaneInvoker.FilePath $fastlaneArgs
Pop-Location

if ($code -ne 0) {
    throw "Android internal upload failed."
}

Write-Host "Android internal upload completed."
