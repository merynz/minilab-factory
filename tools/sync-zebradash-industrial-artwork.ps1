param(
    [string]$GamePath = "Games/Game_Arcade_ZebraDash",
    [string]$ProjectPath = "",
    [string]$SourceZip = "C:\Users\monster\Desktop\ZebraDashArtWork\[SOURCE] Industrial Tileset.zip"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-CanonicalPath([string]$InputPath, [string]$RepoRoot, [bool]$RequireExists = $true) {
    if ([string]::IsNullOrWhiteSpace($InputPath)) {
        throw "Path argument cannot be empty."
    }

    $candidate = $InputPath
    if (-not [System.IO.Path]::IsPathRooted($candidate)) {
        $candidate = Join-Path $RepoRoot $candidate
    }

    $candidate = [System.IO.Path]::GetFullPath($candidate)
    if ($RequireExists -and !(Test-Path -LiteralPath $candidate)) {
        throw "Path not found: $InputPath"
    }

    if ($RequireExists) {
        return (Resolve-Path -LiteralPath $candidate).Path
    }

    return $candidate
}

function Ensure-EmptyDirectory([string]$DirPath) {
    New-Item -ItemType Directory -Path $DirPath -Force | Out-Null
    Get-ChildItem -Path $DirPath -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in @(".png", ".jpg", ".jpeg") } |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

function Convert-FlatName([string]$RelativePath) {
    $base = $RelativePath -replace '[\\/:*?"<>|]', '_'
    $base = $base -replace '\s+', '_'
    return $base
}

function Copy-FlattenedPngs([string]$SourceRoot, [string]$DestinationDir, [scriptblock]$Filter) {
    New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null
    if (!(Test-Path $SourceRoot)) {
        return 0
    }

    $count = 0
    $rootPrefix = (Resolve-Path $SourceRoot).Path
    $files = Get-ChildItem -Path $SourceRoot -Recurse -File -Filter "*.png" -ErrorAction SilentlyContinue
    foreach ($file in $files) {
        if ($null -ne $Filter) {
            $allow = & $Filter $file
            if (-not $allow) {
                continue
            }
        }

        $relative = $file.FullName.Substring($rootPrefix.Length).TrimStart('\', '/')
        $flatName = Convert-FlatName $relative
        Copy-Item -Path $file.FullName -Destination (Join-Path $DestinationDir $flatName) -Force
        $count++
    }

    return $count
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedProjectPath = ""
$resolvedGamePath = ""

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $resolvedGamePath = Resolve-CanonicalPath -InputPath $GamePath -RepoRoot $repoRoot
    $resolvedProjectPath = Join-Path $resolvedGamePath "UnityProject"
    if (!(Test-Path $resolvedProjectPath)) {
        throw "UnityProject not found under game path: $resolvedGamePath"
    }
}
else {
    $resolvedProjectPath = Resolve-CanonicalPath -InputPath $ProjectPath -RepoRoot $repoRoot
    if ((Split-Path -Leaf $resolvedProjectPath) -ine "UnityProject") {
        $candidate = Join-Path $resolvedProjectPath "UnityProject"
        if (Test-Path $candidate) {
            $resolvedProjectPath = (Resolve-Path $candidate).Path
        }
    }

    $resolvedGamePath = Split-Path -Parent $resolvedProjectPath
}

$resolvedZip = Resolve-CanonicalPath -InputPath $SourceZip -RepoRoot $repoRoot

$resourceRoot = Join-Path $resolvedProjectPath "Assets/Resources/ZebraDashArtLocal/Industrial"
$destTilesets = Join-Path $resourceRoot "Tilesets"
$destBackgrounds = Join-Path $resourceRoot "Backgrounds"
$destHazards = Join-Path $resourceRoot "Hazards"

Ensure-EmptyDirectory -DirPath $destTilesets
Ensure-EmptyDirectory -DirPath $destBackgrounds
Ensure-EmptyDirectory -DirPath $destHazards

$extractRoot = Join-Path ([System.IO.Path]::GetTempPath()) "zebradash-industrial-pack"
if (Test-Path $extractRoot) {
    Remove-Item -Path $extractRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null

Expand-Archive -LiteralPath $resolvedZip -DestinationPath $extractRoot -Force

$plus16Root = Join-Path $extractRoot "PLUS/Industrial Tileset - Plus Pack 16p"
$hazard16Root = Join-Path $extractRoot "PLUS/Hazards 16p"
$free32Root = Join-Path $extractRoot "FREE/Industrial Tileset - Free Pack 32p"

$tilesetCount = Copy-FlattenedPngs -SourceRoot $plus16Root -DestinationDir $destTilesets -Filter {
    param($file)
    $name = $file.Name
    return ($name -match '(?i)tileset') -and ($name -notmatch '(?i)background|farbackground|foreground')
}

$backgroundCount = Copy-FlattenedPngs -SourceRoot $plus16Root -DestinationDir $destBackgrounds -Filter {
    param($file)
    $name = $file.Name
    return ($name -match '(?i)background|farbackground|foreground')
}

# Also copy free far background tiles as fallback options.
$backgroundCount += Copy-FlattenedPngs -SourceRoot $free32Root -DestinationDir $destBackgrounds -Filter {
    param($file)
    return $file.Name -match '(?i)background'
}

$hazardCount = Copy-FlattenedPngs -SourceRoot $hazard16Root -DestinationDir $destHazards -Filter {
    param($file)
    return $true
}

Write-Host "Industrial artwork sync summary:"
Write-Host "  Tilesets   : $tilesetCount"
Write-Host "  Backgrounds: $backgroundCount"
Write-Host "  Hazards    : $hazardCount"
Write-Host "  ResourceRoot: $resourceRoot"

exit 0
