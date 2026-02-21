Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

$assetRoots = @()
$assetRoots += Get-ChildItem -Path (Join-Path $repoRoot "Templates") -Directory -ErrorAction SilentlyContinue |
    ForEach-Object { Join-Path $_.FullName "UnityProject/Assets" }
$assetRoots += Get-ChildItem -Path (Join-Path $repoRoot "Games") -Directory -ErrorAction SilentlyContinue |
    ForEach-Object { Join-Path $_.FullName "UnityProject/Assets" }

$forbiddenPattern = '(?i)(^|[\\/])Sirius(GameMaker|GM|[^\\/]*)($|[\\/])'
$violations = New-Object System.Collections.Generic.List[string]

foreach ($root in $assetRoots) {
    if (!(Test-Path $root)) {
        continue
    }

    $rootRelative = [System.IO.Path]::GetRelativePath($repoRoot, $root)
    if ($rootRelative -match $forbiddenPattern) {
        $violations.Add("Forbidden root path: $rootRelative")
    }

    Get-ChildItem -Path $root -Recurse -Force -Directory | ForEach-Object {
        $relative = [System.IO.Path]::GetRelativePath($repoRoot, $_.FullName)
        if ($relative -match $forbiddenPattern) {
            $violations.Add("Forbidden folder: $relative")
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host "FAIL: template purity check failed."
    $violations | Sort-Object -Unique | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: template purity check passed (no Sirius* folders)."
