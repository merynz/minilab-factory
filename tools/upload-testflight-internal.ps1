param(
    [string]$IpaPath = "",
    [string]$BundleId = "",
    [switch]$SkipIfSecretsMissing,
    [switch]$SkipIfNoMac
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Native([string]$FilePath, [string[]]$Arguments) {
    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -Wait -PassThru -NoNewWindow
    return $process.ExitCode
}

if (-not $IsMacOS) {
    $message = "TestFlight upload is blocked by Mac/signing requirements."
    if ($SkipIfNoMac) {
        Write-Host "SKIP: $message"
        exit 0
    }

    throw $message
}

if (!(Get-Command bundle -ErrorAction SilentlyContinue)) {
    throw "Ruby bundler is required for fastlane. Install bundler first."
}

$repoRoot = Split-Path -Parent $PSScriptRoot

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

Push-Location $repoRoot
$code = Invoke-Native "bundle" @("exec", "fastlane", "ios", "internal")
Pop-Location

if ($code -ne 0) {
    throw "TestFlight internal upload failed."
}

Write-Host "TestFlight internal upload completed."
