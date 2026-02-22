param(
    [switch]$IgnoreUntracked
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $args = @("status", "--porcelain")
    if ($IgnoreUntracked) {
        $args += "--untracked-files=no"
    }

    $status = git @args
    if ($LASTEXITCODE -ne 0) {
        throw "git status failed."
    }

    if (-not [string]::IsNullOrWhiteSpace(($status -join ""))) {
        Write-Host "FAIL: working tree is dirty."
        $status | ForEach-Object { Write-Host $_ }
        Write-Host "----- git diff --name-status -----"
        git diff --name-status | ForEach-Object { Write-Host $_ }
        Write-Host "----- git diff --cached --name-status -----"
        git diff --cached --name-status | ForEach-Object { Write-Host $_ }
        exit 1
    }

    Write-Host "PASS: working tree is clean."
    exit 0
}
finally {
    Pop-Location
}
