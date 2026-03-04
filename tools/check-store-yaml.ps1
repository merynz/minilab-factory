param(
    [string]$GamePath = "",
    [string]$StorePath = "",
    [switch]$AllowTemplate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-StorePath([string]$RepoRoot, [string]$InputGamePath, [string]$InputStorePath) {
    if (-not [string]::IsNullOrWhiteSpace($InputStorePath)) {
        if ([System.IO.Path]::IsPathRooted($InputStorePath)) {
            return [System.IO.Path]::GetFullPath($InputStorePath)
        }

        return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $InputStorePath))
    }

    if (-not [string]::IsNullOrWhiteSpace($InputGamePath)) {
        $gameRoot = $InputGamePath
        if (-not [System.IO.Path]::IsPathRooted($gameRoot)) {
            $gameRoot = Join-Path $RepoRoot $gameRoot
        }

        return [System.IO.Path]::GetFullPath((Join-Path $gameRoot "store.yaml"))
    }

    if (-not [string]::IsNullOrWhiteSpace($env:MINILAB_STORE_PATH)) {
        return [System.IO.Path]::GetFullPath($env:MINILAB_STORE_PATH)
    }

    $firstGame = Get-ChildItem -Path (Join-Path $RepoRoot "Games") -Directory -Filter "Game_*" -ErrorAction SilentlyContinue |
        Sort-Object Name |
        Select-Object -First 1

    if ($firstGame) {
        return [System.IO.Path]::GetFullPath((Join-Path $firstGame.FullName "store.yaml"))
    }

    return ""
}

function Has-Key([string]$Raw, [string]$Key) {
    return [regex]::IsMatch($Raw, "(?m)^\s*$([regex]::Escape($Key))\s*:\s*.+$")
}

function Has-Section([string]$Raw, [string]$Key) {
    return [regex]::IsMatch($Raw, "(?m)^\s*$([regex]::Escape($Key))\s*:\s*$")
}

function Extract-Scalar([string]$Raw, [string]$Key) {
    $match = [regex]::Match($Raw, "(?m)^\s*$([regex]::Escape($Key))\s*:\s*[""']?(.+?)[""']?\s*$")
    if ($match.Success) {
        return $match.Groups[1].Value.Trim()
    }

    return ""
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedStorePath = Resolve-StorePath -RepoRoot $repoRoot -InputGamePath $GamePath -InputStorePath $StorePath
if ([string]::IsNullOrWhiteSpace($resolvedStorePath)) {
    throw "store.yaml path could not be resolved. Provide -GamePath or -StorePath."
}

if (!(Test-Path $resolvedStorePath)) {
    throw "store.yaml not found: $resolvedStorePath"
}

$fileName = Split-Path -Leaf $resolvedStorePath
if (-not $AllowTemplate -and $fileName -ieq "store.template.yaml") {
    throw "store.template.yaml is not releasable. Use Games/<Game>/store.yaml."
}

$raw = Get-Content -Path $resolvedStorePath -Raw
$missing = New-Object System.Collections.Generic.List[string]

$requiredScalarKeys = @(
    "bundle_id_ios",
    "application_id_android",
    "privacy_policy_url",
    "support_email"
)

foreach ($key in $requiredScalarKeys) {
    if (-not (Has-Key -Raw $raw -Key $key)) {
        $missing.Add($key)
    }
}

$requiredSections = @(
    "locales",
    "category",
    "tags",
    "sdk_inventory",
    "release_notes_template",
    "shots_ref"
)

foreach ($key in $requiredSections) {
    if (-not (Has-Section -Raw $raw -Key $key)) {
        $missing.Add($key)
    }
}

$requiredNestedPatterns = @(
    @{ Label = "locales.tr-TR"; Pattern = "(?m)^\s{2}tr-TR\s*:\s*$" },
    @{ Label = "locales.en-US"; Pattern = "(?m)^\s{2}en-US\s*:\s*$" },
    @{ Label = "locales.tr-TR.app_name"; Pattern = "(?m)^\s{4}app_name\s*:\s*.+$" },
    @{ Label = "locales.tr-TR.short_description"; Pattern = "(?m)^\s{4}short_description\s*:\s*.+$" },
    @{ Label = "locales.tr-TR.long_description"; Pattern = "(?m)^\s{4}long_description\s*:\s*.+$" },
    @{ Label = "locales.en-US.app_name"; Pattern = "(?m)^\s{4}app_name\s*:\s*.+$" },
    @{ Label = "locales.en-US.short_description"; Pattern = "(?m)^\s{4}short_description\s*:\s*.+$" },
    @{ Label = "locales.en-US.long_description"; Pattern = "(?m)^\s{4}long_description\s*:\s*.+$" },
    @{ Label = "category.android"; Pattern = "(?m)^\s{2}android\s*:\s*.+$" },
    @{ Label = "category.ios"; Pattern = "(?m)^\s{2}ios\s*:\s*.+$" },
    @{ Label = "sdk_inventory.core"; Pattern = "(?m)^\s{2}core\s*:\s*$" },
    @{ Label = "sdk_inventory.delta"; Pattern = "(?m)^\s{2}delta\s*:\s*$" },
    @{ Label = "release_notes_template.tr-TR"; Pattern = "(?m)^\s{2}tr-TR\s*:\s*$" },
    @{ Label = "release_notes_template.en-US"; Pattern = "(?m)^\s{2}en-US\s*:\s*$" },
    @{ Label = "shots_ref.screenshot_shot_list"; Pattern = "(?m)^\s{2}screenshot_shot_list\s*:\s*.+$" },
    @{ Label = "shots_ref.video_shot_list"; Pattern = "(?m)^\s{2}video_shot_list\s*:\s*.+$" }
)

foreach ($rule in $requiredNestedPatterns) {
    if (-not [regex]::IsMatch($raw, $rule.Pattern)) {
        $missing.Add($rule.Label)
    }
}

if ($missing.Count -gt 0) {
    Write-Host "FAIL: store.yaml missing required fields."
    $missing | Sort-Object -Unique | ForEach-Object { Write-Host " - $_" }
    exit 1
}

$shotRefs = @(
    $(Extract-Scalar -Raw $raw -Key "screenshot_shot_list"),
    $(Extract-Scalar -Raw $raw -Key "video_shot_list")
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

$missingShotRefs = New-Object System.Collections.Generic.List[string]
foreach ($shotRef in $shotRefs) {
    $candidate = $shotRef
    if (-not [System.IO.Path]::IsPathRooted($candidate)) {
        $candidate = Join-Path $repoRoot $shotRef
    }

    if (-not (Test-Path $candidate)) {
        $missingShotRefs.Add($shotRef)
    }
}

if ($missingShotRefs.Count -gt 0) {
    Write-Host "FAIL: shots_ref paths not found."
    $missingShotRefs | Sort-Object -Unique | ForEach-Object { Write-Host " - $_" }
    exit 1
}

$androidAppId = Extract-Scalar -Raw $raw -Key "application_id_android"
$iosBundleId = Extract-Scalar -Raw $raw -Key "bundle_id_ios"

if (-not [regex]::IsMatch($androidAppId, "^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)+$")) {
    throw "Invalid application_id_android format: $androidAppId"
}

if (-not [regex]::IsMatch($iosBundleId, "^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)+$")) {
    throw "Invalid bundle_id_ios format: $iosBundleId"
}

Write-Host "PASS: store.yaml validation complete."
Write-Host "Path: $resolvedStorePath"
Write-Host "Android: $androidAppId"
Write-Host "iOS: $iosBundleId"
