param(
    [string]$GamePath = "Games/Game_Arcade_ZebraDash",
    [string]$ProjectPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-CanonicalPath([string]$InputPath, [string]$RepoRoot) {
    if ([string]::IsNullOrWhiteSpace($InputPath)) {
        throw "Path argument cannot be empty."
    }

    $candidate = $InputPath
    if (-not [System.IO.Path]::IsPathRooted($candidate)) {
        $candidate = Join-Path $RepoRoot $candidate
    }

    $candidate = [System.IO.Path]::GetFullPath($candidate)
    if (!(Test-Path $candidate)) {
        throw "Path not found: $InputPath"
    }

    return (Resolve-Path $candidate).Path
}

function Sync-DirectoryContent(
    [string]$Label,
    [string]$SourceDir,
    [string]$DestinationDir,
    [string[]]$Extensions
) {
    New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null

    foreach ($ext in $Extensions) {
        Get-ChildItem -Path $DestinationDir -File -Filter "*$ext" -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue
    }

    if (!(Test-Path $SourceDir)) {
        Write-Host "SKIP: $Label source not found: $SourceDir"
        return [pscustomobject]@{
            Label = $Label
            Status = "SKIP"
            Copied = 0
            Source = $SourceDir
            Destination = $DestinationDir
        }
    }

    $sourceFiles = Get-ChildItem -Path $SourceDir -File |
        Where-Object {
            $fileExt = $_.Extension.ToLowerInvariant()
            $Extensions -contains $fileExt
        } |
        Sort-Object Name

    if ($sourceFiles.Count -eq 0) {
        Write-Host "SKIP: $Label source has no matching files ($($Extensions -join ', ')): $SourceDir"
        return [pscustomobject]@{
            Label = $Label
            Status = "SKIP"
            Copied = 0
            Source = $SourceDir
            Destination = $DestinationDir
        }
    }

    foreach ($file in $sourceFiles) {
        Copy-Item -Path $file.FullName -Destination (Join-Path $DestinationDir $file.Name) -Force
    }

    Write-Host "PASS: $Label synced $($sourceFiles.Count) file(s) -> $DestinationDir"
    return [pscustomobject]@{
        Label = $Label
        Status = "PASS"
        Copied = $sourceFiles.Count
        Source = $SourceDir
        Destination = $DestinationDir
    }
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

$levelsSource = Join-Path $resolvedGamePath "Content/Levels"
$audioSource = Join-Path $resolvedGamePath "AudioLocal"

$streamingRoot = Join-Path $resolvedProjectPath "Assets/StreamingAssets/ZebraDash"
$levelsDestination = Join-Path $streamingRoot "Levels"
$audioDestination = Join-Path $streamingRoot "Audio"

Write-Host "GamePath: $resolvedGamePath"
Write-Host "ProjectPath: $resolvedProjectPath"

$levelResult = Sync-DirectoryContent `
    -Label "Levels JSON" `
    -SourceDir $levelsSource `
    -DestinationDir $levelsDestination `
    -Extensions @(".json")

$audioResult = Sync-DirectoryContent `
    -Label "Audio WAV" `
    -SourceDir $audioSource `
    -DestinationDir $audioDestination `
    -Extensions @(".wav")

Write-Host "Content sync summary:"
@($levelResult, $audioResult) | Format-Table -AutoSize

exit 0
