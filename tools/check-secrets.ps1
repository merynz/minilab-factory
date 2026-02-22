param(
    [string]$RootPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RootPath)) {
    $RootPath = Split-Path -Parent $PSScriptRoot
}

$forbiddenFileRegex = @(
    '\.keystore$',
    '\.jks$',
    '\.p12$',
    '\.mobileprovision$',
    '(^|[\\/])\.env($|\.|[\\/])',
    '(^|[\\/])GoogleService-Info\.plist$',
    '(^|[\\/])google-services\.json$',
    '\.pem$',
    '\.pfx$',
    '\.key$',
    '\.token$'
)

$secretContentRegex = @(
    '-----BEGIN (?:RSA |EC |OPENSSH |)PRIVATE KEY-----',
    'ghp_[A-Za-z0-9]{20,}',
    'github_pat_[A-Za-z0-9_]{20,}',
    'AIza[0-9A-Za-z\-_]{20,}',
    'AKIA[0-9A-Z]{16}',
    'ASIA[0-9A-Z]{16}',
    'xox[baprs]-[A-Za-z0-9-]{10,}',
    'ca-app-pub-[0-9]{16}~[0-9]{10}',
    'ca-app-pub-[0-9]{16}/[0-9]{10}'
)

$files = Get-ChildItem -Path $RootPath -Recurse -Force -File |
    Where-Object { $_.FullName -notmatch '[\\/]\.git[\\/]' }

$violations = New-Object System.Collections.Generic.List[string]

foreach ($file in $files) {
    $relativePath = [System.IO.Path]::GetRelativePath($RootPath, $file.FullName)
    foreach ($pattern in $forbiddenFileRegex) {
        if ($relativePath -imatch $pattern) {
            $violations.Add("Forbidden file path: $relativePath")
            break
        }
    }
}

foreach ($file in $files) {
    if ($file.Length -gt 2MB) {
        continue
    }

    $relativePath = [System.IO.Path]::GetRelativePath($RootPath, $file.FullName)
    $content = Get-Content -Path $file.FullName -Raw -ErrorAction SilentlyContinue
    if ($null -eq $content) {
        continue
    }

    foreach ($pattern in $secretContentRegex) {
        if ($content -match $pattern) {
            $violations.Add("Secret-like content match in $relativePath (pattern: $pattern)")
            break
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host "FAIL: secret check failed."
    $violations | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: no forbidden secret files or secret-like patterns detected."
