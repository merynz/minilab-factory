param(
    [Parameter(Mandatory = $true)][string]$VersionFile,
    [ValidateSet("major", "minor", "patch")][string]$Part = "patch",
    [int]$BuildNumber = -1
)

if (!(Test-Path $VersionFile)) {
    throw "Version file not found: $VersionFile"
}

$raw = Get-Content $VersionFile -Raw
$ver = [Version]$raw.Trim()

switch ($Part) {
    "major" { $newVersion = "{0}.0.0" -f ($ver.Major + 1) }
    "minor" { $newVersion = "{0}.{1}.0" -f $ver.Major, ($ver.Minor + 1) }
    "patch" { $newVersion = "{0}.{1}.{2}" -f $ver.Major, $ver.Minor, ($ver.Build + 1) }
}

Set-Content -Path $VersionFile -Value $newVersion -NoNewline
Write-Host "Semver: $newVersion"

if ($BuildNumber -ge 0) {
    Write-Host "BuildNumber: $BuildNumber"
}

