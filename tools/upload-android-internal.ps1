param(
    [string]$AabPath = "",
    [string]$PackageName = "",
    [string]$Track = "internal"
)

if (!(Get-Command bundle -ErrorAction SilentlyContinue)) {
    throw "Ruby bundler is required for fastlane. Install bundler first."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$env:MINILAB_ANDROID_AAB_PATH = $AabPath
$env:MINILAB_ANDROID_PACKAGE_NAME = $PackageName
$env:MINILAB_ANDROID_TRACK = $Track

Push-Location $repoRoot
bundle exec fastlane android internal
$code = $LASTEXITCODE
Pop-Location

if ($code -ne 0) {
    throw "Android internal upload failed."
}

Write-Host "Android internal upload completed."

