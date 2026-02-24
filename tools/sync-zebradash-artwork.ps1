param(
    [string]$GamePath = "Games/Game_Arcade_ZebraDash",
    [string]$ProjectPath = "",
    [string]$ArtworkPath = "C:\Users\monster\Desktop\ZebraDashArtWork",
    [switch]$EnableUiPlayerOverrides
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
    if ($RequireExists -and !(Test-Path $candidate)) {
        throw "Path not found: $InputPath"
    }

    if ($RequireExists) {
        return (Resolve-Path $candidate).Path
    }

    return $candidate
}

function Ensure-EmptyDirectory([string]$DirPath) {
    New-Item -ItemType Directory -Path $DirPath -Force | Out-Null
    Get-ChildItem -Path $DirPath -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in @(".png", ".jpg", ".jpeg") } |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

function Copy-FilesByPattern([string]$Label, [string]$SourceDir, [string]$Pattern, [string]$DestinationDir) {
    New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null
    if (!(Test-Path $SourceDir)) {
        Write-Host "SKIP: $Label source not found: $SourceDir"
        return 0
    }

    $files = Get-ChildItem -Path $SourceDir -Recurse -File -Filter $Pattern -ErrorAction SilentlyContinue
    $count = 0
    foreach ($file in $files) {
        Copy-Item -Path $file.FullName -Destination (Join-Path $DestinationDir $file.Name) -Force
        $count++
    }

    Write-Host "PASS: $Label copied $count file(s) to $DestinationDir"
    return $count
}

function Copy-FirstMatchByName([string]$Label, [string]$SourceDir, [string[]]$FileNames, [string]$DestinationDir) {
    New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null
    if (!(Test-Path $SourceDir)) {
        Write-Host "SKIP: $Label source not found: $SourceDir"
        return 0
    }

    $count = 0
    foreach ($name in $FileNames) {
        $match = Get-ChildItem -Path $SourceDir -Recurse -File -Filter $name -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -eq $match) {
            Write-Host "WARN: $Label missing asset: $name"
            continue
        }

        Copy-Item -Path $match.FullName -Destination (Join-Path $DestinationDir $match.Name) -Force
        $count++
    }

    Write-Host "PASS: $Label copied $count file(s) to $DestinationDir"
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
} else {
    $resolvedProjectPath = Resolve-CanonicalPath -InputPath $ProjectPath -RepoRoot $repoRoot
    if ((Split-Path -Leaf $resolvedProjectPath) -ine "UnityProject") {
        $candidate = Join-Path $resolvedProjectPath "UnityProject"
        if (Test-Path $candidate) {
            $resolvedProjectPath = (Resolve-Path $candidate).Path
        }
    }

    $resolvedGamePath = Split-Path -Parent $resolvedProjectPath
}

$resolvedArtworkPath = Resolve-CanonicalPath -InputPath $ArtworkPath -RepoRoot $repoRoot -RequireExists:$false
if (!(Test-Path $resolvedArtworkPath)) {
    Write-Host "SKIP: Artwork root not found: $resolvedArtworkPath"
    exit 0
}

$resourcesRoot = Join-Path $resolvedProjectPath "Assets/Resources/ZebraDashArtLocal"
$destBackgrounds = Join-Path $resourcesRoot "Backgrounds"
$destObstacles = Join-Path $resourcesRoot "Obstacles"
$destDecor = Join-Path $resourcesRoot "Decor"
$destVfx = Join-Path $resourcesRoot "VFX"
$destTileset = Join-Path $resourcesRoot "Tileset"
$destUi = Join-Path $resourcesRoot "UI"
$destPlayer = Join-Path $resourcesRoot "Player"

Ensure-EmptyDirectory -DirPath $destBackgrounds
Ensure-EmptyDirectory -DirPath $destObstacles
Ensure-EmptyDirectory -DirPath $destDecor
Ensure-EmptyDirectory -DirPath $destVfx
Ensure-EmptyDirectory -DirPath $destTileset
Ensure-EmptyDirectory -DirPath $destUi
Ensure-EmptyDirectory -DirPath $destPlayer

$backgroundRoot = Join-Path $resolvedArtworkPath "SBS - Seamless Space Backgrounds - Large 1024x1024"
$foozleRoot = Join-Path $resolvedArtworkPath "Foozle_2DT0001_Science_Fiction_Labs_Tileset"
$vfxRoot = Join-Path $resolvedArtworkPath "brackeys_vfx_bundle"
$levelTilesetRoot = Join-Path $foozleRoot "Tileset/Individual_PNGs/Level_Tileset"
$uiZipPath = Join-Path $resolvedArtworkPath "Space_Game_GUI_PNG.zip"
$uiExtractedPath = Join-Path $resolvedArtworkPath "Space_Game_GUI_PNG"

if (!(Test-Path $uiExtractedPath) -and (Test-Path $uiZipPath)) {
    try {
        Expand-Archive -Path $uiZipPath -DestinationPath $uiExtractedPath -Force
        Write-Host "PASS: UI zip extracted -> $uiExtractedPath"
    } catch {
        Write-Host "WARN: UI zip could not be extracted: $($_.Exception.Message)"
    }
}

$backgroundCount = 0
$backgroundCount += Copy-FilesByPattern -Label "Background Starfield" -SourceDir $backgroundRoot -Pattern "Starfield_*.png" -DestinationDir $destBackgrounds
$backgroundCount += Copy-FilesByPattern -Label "Background Blue Nebula" -SourceDir $backgroundRoot -Pattern "Blue_Nebula_*.png" -DestinationDir $destBackgrounds
$backgroundCount += Copy-FilesByPattern -Label "Background Green Nebula" -SourceDir $backgroundRoot -Pattern "Green_Nebula_*.png" -DestinationDir $destBackgrounds
$backgroundCount += Copy-FilesByPattern -Label "Background Purple Nebula" -SourceDir $backgroundRoot -Pattern "Purple_Nebula_*.png" -DestinationDir $destBackgrounds

$obstacleNames = @(
    "laser_idle.png",
    "laser_activate.png",
    "laser_deactivate.png",
    "laser_spikes_idle.png",
    "laser_spikes_activate.png",
    "laser_spikes_deactivate.png",
    "laser_turret.png",
    "electric_turret.png",
    "saw_idle.png",
    "saw_activate.png",
    "saw_deactivate.png",
    "wall_blades.png",
    "barrier_idle.png",
    "barrier_deactivate.png",
    "platform_moving.png"
)
$obstacleCount = Copy-FirstMatchByName -Label "Obstacle Sprites" -SourceDir $foozleRoot -FileNames $obstacleNames -DestinationDir $destObstacles

$decorNames = @(
    "decor_1.png",
    "decor_2.png",
    "decor_3.png",
    "decor_4.png",
    "decor_5.png",
    "decor_6.png",
    "decor_7.png",
    "decor_8.png",
    "control_panel_idle.png",
    "control_panel_deactivate.png",
    "barrier_idle.png",
    "platform_moving.png"
)
$decorCount = Copy-FirstMatchByName -Label "Decor Sprites" -SourceDir $foozleRoot -FileNames $decorNames -DestinationDir $destDecor

$vfxNames = @(
    "light_01_a.png",
    "light_02_a.png",
    "light_03_a.png",
    "flare_01_a.png",
    "trace_01_a.png",
    "trace_02_a.png",
    "trace_03_a.png",
    "symbol_01_a.png",
    "symbol_02_a.png",
    "window_01_a.png",
    "window_02_a.png",
    "circle_01_a.png",
    "circle_02_a.png",
    "star_01_a.png",
    "star_02_a.png",
    "spark_01_a.png",
    "spark_02_a.png",
    "smoke_01_a.png",
    "smoke_07_a.png",
    "twirl_01_a.png",
    "slash_01_a.png",
    "slash_02_a.png"
)
$vfxCount = Copy-FirstMatchByName -Label "VFX Sprites" -SourceDir $vfxRoot -FileNames $vfxNames -DestinationDir $destVfx
$tilesetCount = Copy-FilesByPattern -Label "Level Tileset" -SourceDir $levelTilesetRoot -Pattern "tile*.png" -DestinationDir $destTileset

$uiCount = 0
$playerCount = 0
if ($EnableUiPlayerOverrides.IsPresent) {
    $uiMap = @(
        @{ Src = "PNG/Main_Menu/BG.png"; Dst = "Main_Menu_BG.png" },
        @{ Src = "PNG/Main_Menu/Header.png"; Dst = "Main_Menu_Header.png" },
        @{ Src = "PNG/Main_Menu/Start_BTN.png"; Dst = "Main_Menu_Start_BTN.png" },
        @{ Src = "PNG/Main_Menu/Settings_BTN.png"; Dst = "Main_Menu_Settings_BTN.png" },
        @{ Src = "PNG/Main_Menu/Exit_BTN.png"; Dst = "Main_Menu_Exit_BTN.png" },
        @{ Src = "PNG/Level_Menu/Window.png"; Dst = "Level_Menu_Window.png" },
        @{ Src = "PNG/Level_Menu/Table.png"; Dst = "Level_Menu_Table.png" },
        @{ Src = "PNG/Level_Menu/Header.png"; Dst = "Level_Menu_Header.png" },
        @{ Src = "PNG/Level_Menu/Play_BTN.png"; Dst = "Level_Menu_Play_BTN.png" },
        @{ Src = "PNG/Pause/Window.png"; Dst = "Pause_Window.png" },
        @{ Src = "PNG/Pause/Table.png"; Dst = "Pause_Table.png" },
        @{ Src = "PNG/Pause/Header.png"; Dst = "Pause_Header.png" },
        @{ Src = "PNG/Pause/Menu_BTN.png"; Dst = "Pause_Menu_BTN.png" },
        @{ Src = "PNG/Pause/Ok_BTN.png"; Dst = "Pause_Ok_BTN.png" },
        @{ Src = "PNG/Buttons/BTNs/Play_BTN.png"; Dst = "BTN_Play.png" },
        @{ Src = "PNG/Buttons/BTNs/Pause_BTN.png"; Dst = "BTN_Pause.png" },
        @{ Src = "PNG/Buttons/BTNs/Menu_BTN.png"; Dst = "BTN_Menu.png" },
        @{ Src = "PNG/Buttons/BTNs/Replay_BTN.png"; Dst = "BTN_Replay.png" },
        @{ Src = "PNG/Buttons/BTNs/Settings_BTN.png"; Dst = "BTN_Settings.png" },
        @{ Src = "PNG/Buttons/BTNs/Close_BTN.png"; Dst = "BTN_Close.png" },
        @{ Src = "PNG/Buttons/BTNs/Ok_BTN.png"; Dst = "BTN_Ok.png" },
        @{ Src = "PNG/Buttons/BTNs_Active/Play_BTN.png"; Dst = "BTN_Play_Active.png" },
        @{ Src = "PNG/Buttons/BTNs_Active/Pause_BTN.png"; Dst = "BTN_Pause_Active.png" },
        @{ Src = "PNG/Buttons/BTNs_Active/Menu_BTN.png"; Dst = "BTN_Menu_Active.png" },
        @{ Src = "PNG/Buttons/BTNs_Active/Replay_BTN.png"; Dst = "BTN_Replay_Active.png" },
        @{ Src = "PNG/Buttons/BTNs_Active/Settings_BTN.png"; Dst = "BTN_Settings_Active.png" }
    )

    if (Test-Path $uiExtractedPath) {
        foreach ($item in $uiMap) {
            $srcFile = Join-Path $uiExtractedPath $item.Src
            if (!(Test-Path $srcFile)) {
                Write-Host "WARN: UI asset missing: $($item.Src)"
                continue
            }

            Copy-Item -Path $srcFile -Destination (Join-Path $destUi $item.Dst) -Force
            $uiCount++
        }
        Write-Host "PASS: UI Sprites copied $uiCount file(s) to $destUi"
    } else {
        Write-Host "SKIP: UI root not found: $uiExtractedPath"
    }

    $preferredPlayerPaths = @(
        (Join-Path $resolvedArtworkPath "ChatGPT Image 24 Şub 2026 03_16_23.png"),
        (Join-Path $uiExtractedPath "PNG/Ship_Parts/Ship_Main_Icon.png"),
        (Join-Path $uiExtractedPath "PNG/Ship_Parts/Ship_1_1_BTN.png"),
        (Join-Path $uiExtractedPath "PNG/Ship_Parts/Ship_2_1_BTN.png"),
        (Join-Path $uiExtractedPath "PNG/Ship_Parts/Ship_3_1_BTN.png")
    )

    $selectedPlayer = $null
    foreach ($candidatePath in $preferredPlayerPaths) {
        if (Test-Path $candidatePath) {
            $selectedPlayer = Get-Item $candidatePath
            break
        }
    }

    if ($null -eq $selectedPlayer) {
        $playerCandidates = @()
        $playerCandidates += Get-ChildItem -Path $resolvedArtworkPath -File -Filter "*.png" -ErrorAction SilentlyContinue
        $playerCandidates += Get-ChildItem -Path $uiExtractedPath -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '(?i)ship|main_icon|avatar|player' }
        $selectedPlayer = $playerCandidates |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
    }

    if ($null -ne $selectedPlayer) {
        Copy-Item -Path $selectedPlayer.FullName -Destination (Join-Path $destPlayer "Player_Custom.png") -Force
        $playerCount = 1
        Write-Host "PASS: Player sprite selected -> $($selectedPlayer.FullName)"
    } else {
        Write-Host "SKIP: Player sprite candidate not found."
    }
} else {
    Write-Host "SKIP: UI/Player override sync disabled (use -EnableUiPlayerOverrides to enable)."
}

Write-Host "Artwork sync summary:"
Write-Host "  Backgrounds: $backgroundCount"
Write-Host "  Obstacles  : $obstacleCount"
Write-Host "  Decor      : $decorCount"
Write-Host "  VFX        : $vfxCount"
Write-Host "  Tileset    : $tilesetCount"
Write-Host "  UI         : $uiCount"
Write-Host "  Player     : $playerCount"

exit 0
