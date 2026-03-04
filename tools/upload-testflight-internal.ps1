param(
    [string]$IpaPath = "",
    [string]$BundleId = "",
    [string]$StorePath = "",
    [switch]$SkipIfSecretsMissing,
    [switch]$SkipIfNoMac
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

if (-not $IsMacOS) {
    $message = "TestFlight upload is blocked by Mac/signing requirements."
    if ($SkipIfNoMac) {
        Write-Host "SKIP: $message"
        exit 0
    }

    throw $message
}

$repoRoot = Split-Path -Parent $PSScriptRoot
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

if ([string]::IsNullOrWhiteSpace($IpaPath)) {
    $latestIpa = Get-ChildItem -Path (Join-Path $repoRoot "BuildArtifacts/iOS") -Filter *.ipa -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($latestIpa) {
        $IpaPath = $latestIpa.FullName
    }
}

if ([string]::IsNullOrWhiteSpace($IpaPath) -or !(Test-Path $IpaPath)) {
    throw "IPA not found. Provide -IpaPath or run iOS archive first."
}

$hasApiKeyTriple = -not [string]::IsNullOrWhiteSpace($env:APP_STORE_CONNECT_API_KEY_ID) -and
    -not [string]::IsNullOrWhiteSpace($env:APP_STORE_CONNECT_ISSUER_ID) -and
    -not [string]::IsNullOrWhiteSpace($env:APP_STORE_CONNECT_API_KEY_CONTENT)

if (-not $hasApiKeyTriple -and [string]::IsNullOrWhiteSpace($env:FASTLANE_SESSION)) {
    $message = "App Store Connect credentials are missing (API key triple or FASTLANE_SESSION)."
    if ($SkipIfSecretsMissing) {
        Write-Host "SKIP: $message"
        exit 0
    }

    throw $message
}

$env:MINILAB_IOS_IPA_PATH = $IpaPath
$env:MINILAB_IOS_BUNDLE_ID = $BundleId

if (-not [string]::IsNullOrWhiteSpace($StorePath)) {
    $storeCandidate = $StorePath
    if (-not [System.IO.Path]::IsPathRooted($storeCandidate)) {
        $storeCandidate = Join-Path $repoRoot $storeCandidate
    }

    if (Test-Path $storeCandidate) {
        $metadataRoot = Join-Path $repoRoot "BuildArtifacts/store-metadata"
        & (Join-Path $PSScriptRoot "export-store-metadata.ps1") -StorePath $storeCandidate -Platform ios -OutputRoot $metadataRoot
        if ($?) {
            $iosChangelogPath = Join-Path $metadataRoot "ios/testflight_changelog.txt"
            if (Test-Path $iosChangelogPath) {
                $content = Get-Content -Path $iosChangelogPath -Raw
                if (-not [string]::IsNullOrWhiteSpace($content)) {
                    $env:MINILAB_IOS_CHANGELOG = $content.Trim()
                }
            }
        }
    }
}

Push-Location $repoRoot
$fastlaneArgs = @()
$fastlaneArgs += $fastlaneInvoker.Prefix
$fastlaneArgs += @("ios", "internal")
$code = Invoke-Native $fastlaneInvoker.FilePath $fastlaneArgs
Pop-Location

if ($code -ne 0) {
    throw "TestFlight internal upload failed."
}

Write-Host "TestFlight internal upload completed."
