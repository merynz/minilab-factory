param(
    [string]$IpaPath = "",
    [string]$BundleId = ""
)

if (!(Get-Command bundle -ErrorAction SilentlyContinue)) {
    throw "Ruby bundler is required for fastlane. Install bundler first."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$env:MINILAB_IOS_IPA_PATH = $IpaPath
$env:MINILAB_IOS_BUNDLE_ID = $BundleId

Push-Location $repoRoot
bundle exec fastlane ios internal
$code = $LASTEXITCODE
Pop-Location

if ($code -ne 0) {
    throw "TestFlight internal upload failed."
}

Write-Host "TestFlight internal upload completed."

