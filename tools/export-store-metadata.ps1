param(
    [string]$GamePath = "",
    [string]$StorePath = "",
    [ValidateSet("android", "ios", "all")][string]$Platform = "all",
    [string]$OutputRoot = ""
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

    return ""
}

function Unquote([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    $trimmed = $Value.Trim()
    if (($trimmed.StartsWith('"') -and $trimmed.EndsWith('"')) -or ($trimmed.StartsWith("'") -and $trimmed.EndsWith("'"))) {
        $trimmed = $trimmed.Substring(1, $trimmed.Length - 2)
    }

    return $trimmed
}

function Parse-StoreData([string]$StoreRaw) {
    $locales = @{}
    $releaseNotes = @{}
    $currentRoot = ""
    $currentLocale = ""
    $currentReleaseLocale = ""

    foreach ($line in ($StoreRaw -split "`r?`n")) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $trimmed = $line.Trim()
        if ($trimmed.StartsWith("#")) {
            continue
        }

        if ($line -match '^[A-Za-z0-9_]+\s*:\s*.*$' -and $line -notmatch '^\s') {
            $currentRoot = ($line -replace ':.*$', '').Trim()
            $currentLocale = ""
            $currentReleaseLocale = ""
            continue
        }

        if ($currentRoot -eq "locales") {
            if ($line -match '^\s{2}([A-Za-z]{2}-[A-Za-z]{2})\s*:\s*$') {
                $currentLocale = $matches[1]
                if (-not $locales.ContainsKey($currentLocale)) {
                    $locales[$currentLocale] = @{
                        app_name = ""
                        short_description = ""
                        long_description = ""
                        ad_text = ""
                    }
                }
                continue
            }

            if (-not [string]::IsNullOrWhiteSpace($currentLocale) -and $line -match '^\s{4}([A-Za-z0-9_]+)\s*:\s*(.+)$') {
                $key = $matches[1]
                $value = Unquote $matches[2]
                if ($locales[$currentLocale].ContainsKey($key)) {
                    $locales[$currentLocale][$key] = $value
                }
                continue
            }
        }

        if ($currentRoot -eq "release_notes_template") {
            if ($line -match '^\s{2}([A-Za-z]{2}-[A-Za-z]{2})\s*:\s*$') {
                $currentReleaseLocale = $matches[1]
                if (-not $releaseNotes.ContainsKey($currentReleaseLocale)) {
                    $releaseNotes[$currentReleaseLocale] = New-Object System.Collections.Generic.List[string]
                }
                continue
            }

            if (-not [string]::IsNullOrWhiteSpace($currentReleaseLocale) -and $line -match '^\s{4}-\s*(.+)$') {
                $note = Unquote $matches[1]
                if (-not [string]::IsNullOrWhiteSpace($note)) {
                    $releaseNotes[$currentReleaseLocale].Add($note)
                }
                continue
            }
        }
    }

    return @{
        Locales = $locales
        ReleaseNotes = $releaseNotes
    }
}

function Ensure-Dir([string]$Path) {
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedStorePath = Resolve-StorePath -RepoRoot $repoRoot -InputGamePath $GamePath -InputStorePath $StorePath
if ([string]::IsNullOrWhiteSpace($resolvedStorePath)) {
    throw "store.yaml path could not be resolved. Provide -GamePath or -StorePath."
}

if (!(Test-Path $resolvedStorePath)) {
    throw "store.yaml not found: $resolvedStorePath"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "BuildArtifacts/store-metadata"
}

$storeRaw = Get-Content -Path $resolvedStorePath -Raw
$parsed = Parse-StoreData -StoreRaw $storeRaw
$locales = $parsed.Locales
$releaseNotes = $parsed.ReleaseNotes

if ($locales.Count -eq 0) {
    throw "No locales found in store.yaml: $resolvedStorePath"
}

if ($Platform -eq "android" -or $Platform -eq "all") {
    $androidRoot = Join-Path $OutputRoot "android"
    Ensure-Dir $androidRoot

    foreach ($localeKey in $locales.Keys) {
        $localeData = $locales[$localeKey]
        $localeDir = Join-Path $androidRoot $localeKey
        Ensure-Dir $localeDir

        Set-Content -Path (Join-Path $localeDir "title.txt") -Value $localeData.app_name -NoNewline
        Set-Content -Path (Join-Path $localeDir "short_description.txt") -Value $localeData.short_description -NoNewline
        Set-Content -Path (Join-Path $localeDir "full_description.txt") -Value $localeData.long_description -NoNewline
    }

    $changelogDir = Join-Path $androidRoot "changelogs"
    Ensure-Dir $changelogDir
    $defaultNotes = if ($releaseNotes.ContainsKey("en-US")) {
        ($releaseNotes["en-US"] -join [Environment]::NewLine)
    } elseif ($releaseNotes.ContainsKey("tr-TR")) {
        ($releaseNotes["tr-TR"] -join [Environment]::NewLine)
    } else {
        ""
    }

    if (-not [string]::IsNullOrWhiteSpace($defaultNotes)) {
        Set-Content -Path (Join-Path $changelogDir "default.txt") -Value $defaultNotes -NoNewline
    }

    Write-Host "Android metadata exported: $androidRoot"
}

if ($Platform -eq "ios" -or $Platform -eq "all") {
    $iosRoot = Join-Path $OutputRoot "ios"
    Ensure-Dir $iosRoot

    $iosNotes = if ($releaseNotes.ContainsKey("en-US")) {
        ($releaseNotes["en-US"] -join [Environment]::NewLine)
    } elseif ($releaseNotes.ContainsKey("tr-TR")) {
        ($releaseNotes["tr-TR"] -join [Environment]::NewLine)
    } else {
        ""
    }

    $iosChangelogPath = Join-Path $iosRoot "testflight_changelog.txt"
    Set-Content -Path $iosChangelogPath -Value $iosNotes -NoNewline
    Write-Host "iOS changelog exported: $iosChangelogPath"
}

Write-Host "PASS: store metadata export complete."
Write-Host "store.yaml: $resolvedStorePath"
