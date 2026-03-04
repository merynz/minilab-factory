param(
    [switch]$InstallGithubCli = $true,
    [switch]$InstallRuby = $true,
    [switch]$InstallGems = $true,
    [switch]$SkipBundleInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Has-Command([string]$Name) {
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Invoke-Process([string]$FilePath, [string[]]$Arguments) {
    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -Wait -PassThru -NoNewWindow
    return $process.ExitCode
}

function Ensure-WingetInstall([string]$PackageId) {
    if (-not (Has-Command "winget")) {
        throw "winget not found. Install package manually: $PackageId"
    }

    $args = @(
        "install",
        "--id", $PackageId,
        "-e",
        "--accept-package-agreements",
        "--accept-source-agreements"
    )

    $code = Invoke-Process -FilePath "winget" -Arguments $args
    if ($code -ne 0) {
        throw "winget install failed for $PackageId (exit $code)."
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot

if ($IsWindows -and $InstallGithubCli -and -not (Has-Command "gh")) {
    Write-Host "Installing GitHub CLI (gh)..."
    Ensure-WingetInstall -PackageId "GitHub.cli"
}

if ($IsWindows -and $InstallRuby -and -not (Has-Command "ruby")) {
    Write-Host "Installing Ruby (with devkit)..."
    Ensure-WingetInstall -PackageId "RubyInstallerTeam.RubyWithDevKit.3.3"
}

if ($InstallGems) {
    if (-not (Has-Command "gem")) {
        throw "gem not found. Ruby install is required."
    }

    Write-Host "Installing bundler + fastlane gems..."
    $gemCode = Invoke-Process -FilePath "gem" -Arguments @("install", "bundler", "fastlane", "--no-document")
    if ($gemCode -ne 0) {
        throw "gem install bundler fastlane failed (exit $gemCode)."
    }
}

if (-not $SkipBundleInstall) {
    if (-not (Has-Command "bundle")) {
        throw "bundle not found after gem install."
    }

    Push-Location $repoRoot
    try {
        Write-Host "Running bundle install at repo root..."
        $bundleCode = Invoke-Process -FilePath "bundle" -Arguments @("install")
        if ($bundleCode -ne 0) {
            throw "bundle install failed (exit $bundleCode)."
        }
    } finally {
        Pop-Location
    }
}

$checks = @(
    @{ Name = "gh"; Required = $InstallGithubCli },
    @{ Name = "ruby"; Required = $InstallRuby },
    @{ Name = "bundle"; Required = $true },
    @{ Name = "fastlane"; Required = $true }
)

$failed = $false
foreach ($check in $checks) {
    if (-not $check.Required) { continue }

    if (Has-Command $check.Name) {
        $path = (Get-Command $check.Name -ErrorAction SilentlyContinue).Source
        Write-Host "PASS: $($check.Name) -> $path"
    } else {
        Write-Host "FAIL: $($check.Name) not found."
        $failed = $true
    }
}

if ($failed) {
    throw "setup-release-tooling completed with missing tools."
}

Write-Host "SUCCESS: release tooling setup completed."
