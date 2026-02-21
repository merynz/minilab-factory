param(
    [string]$GamePath = "Games/Game_Arcade_ZebraDash",
    [string]$AudioRoot = "",
    [string]$SourceZip = "C:\Users\monster\Downloads\FREE EDM Music Pack.zip",
    [int]$Seed = 20260222,
    [switch]$CopyFromZip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-PathSafe([string]$InputPath, [string]$RepoRoot) {
    if ([System.IO.Path]::IsPathRooted($InputPath)) {
        return [System.IO.Path]::GetFullPath($InputPath)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $InputPath))
}

function Import-RequiredTracks([string]$ZipPath, [string]$TargetAudioRoot) {
    if (!(Test-Path $ZipPath)) {
        throw "Zip not found: $ZipPath"
    }

    $entries = @(
        "FREE EDM Music Pack/4. RoboTrance.wav",
        "FREE EDM Music Pack/9. Electro Dance Mania.wav"
    )

    $tempExtractRoot = Join-Path $env:TEMP "MiniLabEdmExtract"
    if (Test-Path $tempExtractRoot) {
        Remove-Item -Path $tempExtractRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $tempExtractRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $TargetAudioRoot -Force | Out-Null

    foreach ($entry in $entries) {
        tar -xf $ZipPath -C $tempExtractRoot $entry
        $extracted = Join-Path $tempExtractRoot $entry
        if (!(Test-Path $extracted)) {
            throw "Track not found in zip: $entry"
        }

        Copy-Item -Path $extracted -Destination (Join-Path $TargetAudioRoot ([System.IO.Path]::GetFileName($entry))) -Force
    }
}

function Get-WavData([string]$Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    $reader = New-Object System.IO.BinaryReader($stream)
    try {
        $riff = -join ($reader.ReadChars(4))
        if ($riff -ne "RIFF") { throw "Not RIFF WAV: $Path" }
        [void]$reader.ReadInt32()
        $wave = -join ($reader.ReadChars(4))
        if ($wave -ne "WAVE") { throw "Not WAVE format: $Path" }

        $channels = 0
        $sampleRate = 0
        $bitsPerSample = 0
        $audioFormat = 0
        $dataBytes = $null

        while ($stream.Position -lt $stream.Length) {
            $chunkIdChars = $reader.ReadChars(4)
            if ($chunkIdChars.Length -lt 4) { break }
            $chunkId = -join $chunkIdChars
            $chunkSize = $reader.ReadInt32()

            if ($chunkId -eq "fmt ") {
                $audioFormat = $reader.ReadInt16()
                $channels = $reader.ReadInt16()
                $sampleRate = $reader.ReadInt32()
                [void]$reader.ReadInt32()
                [void]$reader.ReadInt16()
                $bitsPerSample = $reader.ReadInt16()

                $remaining = $chunkSize - 16
                if ($remaining -gt 0) {
                    [void]$reader.ReadBytes($remaining)
                }
            }
            elseif ($chunkId -eq "data") {
                $dataBytes = $reader.ReadBytes($chunkSize)
            }
            else {
                [void]$reader.ReadBytes($chunkSize)
            }

            if (($chunkSize % 2) -eq 1 -and $stream.Position -lt $stream.Length) {
                [void]$reader.ReadByte()
            }
        }

        if ($audioFormat -ne 1) {
            throw "Only PCM WAV supported: $Path"
        }

        if ($bitsPerSample -ne 16) {
            throw "Only 16-bit WAV supported: $Path"
        }

        if ($channels -lt 1 -or $sampleRate -le 0 -or $null -eq $dataBytes) {
            throw "Invalid WAV payload: $Path"
        }

        $samples16 = New-Object 'System.Int16[]' ($dataBytes.Length / 2)
        [System.Buffer]::BlockCopy($dataBytes, 0, $samples16, 0, $dataBytes.Length)

        $sampleCount = [int]($samples16.Length / $channels)
        $mono = New-Object 'System.Double[]' $sampleCount

        $idx = 0
        for ($i = 0; $i -lt $sampleCount; $i++) {
            $sum = 0.0
            for ($c = 0; $c -lt $channels; $c++) {
                $sum += $samples16[$idx]
                $idx++
            }

            $mono[$i] = $sum / ($channels * 32768.0)
        }

        return [pscustomobject]@{
            SampleRate = $sampleRate
            Samples = $mono
            DurationSec = $sampleCount / [double]$sampleRate
        }
    }
    finally {
        $reader.Close()
        $stream.Close()
    }
}

function Get-EnergyFlux([double[]]$Samples, [int]$SampleRate, [int]$WindowSize = 1024, [int]$Hop = 512) {
    if ($Samples.Length -lt $WindowSize) {
        return [pscustomobject]@{
            Energy = @(0.0)
            Flux = @(0.0)
            HopSec = $Hop / [double]$SampleRate
            Hop = $Hop
            Window = $WindowSize
        }
    }

    $abs = New-Object 'System.Double[]' $Samples.Length
    for ($i = 0; $i -lt $Samples.Length; $i++) {
        $abs[$i] = [Math]::Abs($Samples[$i])
    }

    $prefix = New-Object 'System.Double[]' ($Samples.Length + 1)
    for ($i = 0; $i -lt $Samples.Length; $i++) {
        $prefix[$i + 1] = $prefix[$i] + $abs[$i]
    }

    $frameCount = [int]([Math]::Floor(($Samples.Length - $WindowSize) / $Hop)) + 1
    $energy = New-Object 'System.Double[]' $frameCount
    for ($f = 0; $f -lt $frameCount; $f++) {
        $start = $f * $Hop
        $sum = $prefix[$start + $WindowSize] - $prefix[$start]
        $energy[$f] = $sum / $WindowSize
    }

    $flux = New-Object 'System.Double[]' $frameCount
    $flux[0] = 0
    for ($f = 1; $f -lt $frameCount; $f++) {
        $delta = $energy[$f] - $energy[$f - 1]
        $flux[$f] = if ($delta -gt 0) { $delta } else { 0.0 }
    }

    return [pscustomobject]@{
        Energy = $energy
        Flux = $flux
        HopSec = $Hop / [double]$SampleRate
        Hop = $Hop
        Window = $WindowSize
    }
}

function Estimate-Bpm([double[]]$Flux, [double]$HopSec, [int]$MinBpm = 80, [int]$MaxBpm = 180) {
    $lagMin = [int]([Math]::Floor((60.0 / $MaxBpm) / $HopSec))
    $lagMax = [int]([Math]::Ceiling((60.0 / $MinBpm) / $HopSec))
    $lagMin = [Math]::Max(1, $lagMin)
    $lagMax = [Math]::Min($Flux.Length - 2, $lagMax)

    $bestLag = $lagMin
    $bestScore = [double]::NegativeInfinity

    for ($lag = $lagMin; $lag -le $lagMax; $lag++) {
        $score = 0.0
        for ($i = $lag; $i -lt $Flux.Length; $i++) {
            $score += $Flux[$i] * $Flux[$i - $lag]
        }

        if ($score -gt $bestScore) {
            $bestScore = $score
            $bestLag = $lag
        }
    }

    $bpm = 60.0 / ($bestLag * $HopSec)
    return [Math]::Round($bpm, 2)
}

function Estimate-Offset([double[]]$Flux, [double]$HopSec) {
    if ($Flux.Length -lt 5) {
        return 0.0
    }

    $limit = [Math]::Min($Flux.Length - 1, [int](25 / $HopSec))
    $slice = New-Object 'System.Double[]' ($limit + 1)
    for ($i = 0; $i -le $limit; $i++) {
        $slice[$i] = $Flux[$i]
    }

    $mean = ($slice | Measure-Object -Average).Average
    $variance = 0.0
    foreach ($v in $slice) {
        $variance += ($v - $mean) * ($v - $mean)
    }

    $std = [Math]::Sqrt($variance / [Math]::Max(1, $slice.Length - 1))
    $threshold = $mean + (1.4 * $std)

    for ($i = 1; $i -le $limit; $i++) {
        if ($Flux[$i] -ge $threshold) {
            return [Math]::Round($i * $HopSec, 4)
        }
    }

    $maxVal = -1.0
    $maxIdx = 0
    for ($i = 0; $i -le $limit; $i++) {
        if ($Flux[$i] -gt $maxVal) {
            $maxVal = $Flux[$i]
            $maxIdx = $i
        }
    }

    return [Math]::Round($maxIdx * $HopSec, 4)
}

function Build-BarEnergy([double[]]$Flux, [double]$HopSec, [double]$StartSec, [double]$BarSec, [int]$BarCount) {
    $barEnergy = New-Object 'System.Double[]' $BarCount
    for ($bar = 0; $bar -lt $BarCount; $bar++) {
        $barStart = $StartSec + ($bar * $BarSec)
        $barEnd = $barStart + $BarSec
        $startIdx = [int]([Math]::Floor($barStart / $HopSec))
        $endIdx = [int]([Math]::Ceiling($barEnd / $HopSec))
        $startIdx = [Math]::Max(0, $startIdx)
        $endIdx = [Math]::Min($Flux.Length - 1, $endIdx)

        $sum = 0.0
        $count = 0
        for ($i = $startIdx; $i -le $endIdx; $i++) {
            $sum += $Flux[$i]
            $count++
        }

        $barEnergy[$bar] = if ($count -gt 0) { $sum / $count } else { 0.0 }
    }

    return $barEnergy
}

function Build-SectionsAndEvents(
    [string]$TrackId,
    [double]$Bpm,
    [double]$OffsetSec,
    [double]$DurationSec,
    [double[]]$BarEnergy,
    [int]$Seed
) {
    $rand = [System.Random]::new($Seed)
    $spb = 60.0 / $Bpm
    $barSec = $spb * 4.0
    $barCount = $BarEnergy.Length

    $sorted = $BarEnergy.Clone()
    [Array]::Sort($sorted)
    $median = if ($sorted.Length -gt 0) { $sorted[[int]($sorted.Length / 2)] } else { 0.0 }
    $activeThreshold = $median * 0.90

    $isActive = New-Object 'System.Boolean[]' $barCount
    for ($bar = 0; $bar -lt $barCount; $bar++) {
        $isActive[$bar] = $BarEnergy[$bar] -ge $activeThreshold
    }

    for ($group = 0; $group -lt $barCount; $group += 4) {
        $groupEnd = [Math]::Min($barCount - 1, $group + 3)
        $activeInGroup = 0
        $minEnergy = [double]::PositiveInfinity
        $minIdx = $group
        for ($bar = $group; $bar -le $groupEnd; $bar++) {
            if ($isActive[$bar]) { $activeInGroup++ }
            if ($BarEnergy[$bar] -lt $minEnergy) {
                $minEnergy = $BarEnergy[$bar]
                $minIdx = $bar
            }
        }

        if ($activeInGroup -eq ($groupEnd - $group + 1)) {
            $isActive[$minIdx] = $false
        }

        if ($activeInGroup -eq 0) {
            $maxEnergy = [double]::NegativeInfinity
            $maxIdx = $group
            for ($bar = $group; $bar -le $groupEnd; $bar++) {
                if ($BarEnergy[$bar] -gt $maxEnergy) {
                    $maxEnergy = $BarEnergy[$bar]
                    $maxIdx = $bar
                }
            }

            $isActive[$maxIdx] = $true
        }
    }

    $sparseBars = New-Object System.Collections.Generic.HashSet[int]
    for ($group = 0; $group -lt $barCount; $group += 4) {
        $groupEnd = [Math]::Min($barCount - 1, $group + 3)
        $candidate = -1
        $minEnergy = [double]::PositiveInfinity
        for ($bar = $group; $bar -le $groupEnd; $bar++) {
            if ($isActive[$bar] -and $BarEnergy[$bar] -lt $minEnergy) {
                $minEnergy = $BarEnergy[$bar]
                $candidate = $bar
            }
        }

        if ($candidate -ge 0) {
            [void]$sparseBars.Add($candidate)
        }
    }

    $sections = New-Object System.Collections.Generic.List[object]
    $breathGaps = New-Object System.Collections.Generic.List[object]
    $tapEvents = New-Object System.Collections.Generic.List[object]
    $holdEvents = New-Object System.Collections.Generic.List[object]
    $pulses = New-Object System.Collections.Generic.List[object]

    $patterns = @(
        @{ name = "simple"; beats = @(0.0, 1.0, 2.0) },
        @{ name = "syncopated"; beats = @(0.0, 1.5, 2.5) },
        @{ name = "staircase"; beats = @(0.0, 1.0, 2.0) },
        @{ name = "fakeout"; beats = @(0.0, 2.75) }
    )

    $currentLane = 1
    $lastType = ""
    $sectionStart = 0.0

    for ($bar = 0; $bar -lt $barCount; $bar++) {
        $barStart = $OffsetSec + ($bar * $barSec)
        $barEnd = [Math]::Min($DurationSec, $barStart + $barSec)
        $type = if ($isActive[$bar]) { "Active" } else { "Rest" }

        if ($type -ne $lastType) {
            if ($lastType -ne "") {
                $sections.Add([pscustomobject]@{
                        type = $lastType
                        startSec = [Math]::Round($sectionStart, 4)
                        endSec = [Math]::Round($barStart, 4)
                    })
            }

            $lastType = $type
            $sectionStart = $barStart
        }

        if ($type -eq "Rest") {
            $breathGaps.Add([pscustomobject]@{
                    startSec = [Math]::Round($barStart, 4)
                    endSec = [Math]::Round($barEnd, 4)
                })
            continue
        }

        $patternIndex = $rand.Next(0, $patterns.Count)
        $pattern = $patterns[$patternIndex]

        if ($sparseBars.Contains($bar)) {
            $pattern = @{ name = "simple"; beats = @(0.0) }
        }

        foreach ($beatOffset in $pattern.beats) {
            $timeSec = $barStart + ($beatOffset * $spb)
            if ($timeSec -ge $barEnd - (0.05 * $spb)) {
                continue
            }

            if ($pattern.name -eq "staircase") {
                $currentLane = ($currentLane + 1) % 3
            }
            elseif ($pattern.name -eq "syncopated") {
                if ($rand.NextDouble() -gt 0.5) {
                    $currentLane = ($currentLane + 2) % 3
                }
            }
            elseif ($pattern.name -eq "fakeout" -and $beatOffset -gt 2.0) {
                $currentLane = ($currentLane + 1) % 3
            }

            $tapEvents.Add([pscustomobject]@{
                    timeSec = [Math]::Round($timeSec, 4)
                    beatIndex = [int][Math]::Round(($timeSec - $OffsetSec) / $spb)
                    lane = $currentLane
                    prefabId = "tap_basic"
                    intensityTag = if ($BarEnergy[$bar] -gt $median * 1.2) { "high" } else { "mid" }
                })

            $pulses.Add([pscustomobject]@{
                    timeSec = [Math]::Round($timeSec, 4)
                    strength = if ($pattern.name -eq "fakeout") { 0.65 } else { 1.0 }
                })
        }

        if ($pattern.name -eq "fakeout") {
            $pulses.Add([pscustomobject]@{
                    timeSec = [Math]::Round($barStart + (1.5 * $spb), 4)
                    strength = 0.55
                })
        }

        $holdChance = if ($sparseBars.Contains($bar)) { 0.08 } else { 0.34 }
        if ($rand.NextDouble() -lt $holdChance) {
            $holdStart = $barStart + (0.5 * $spb)
            $durationCandidate = 0.75 + ($rand.NextDouble() * 2.75)
            $holdEnd = [Math]::Min($DurationSec, $holdStart + $durationCandidate)
            $holdDuration = $holdEnd - $holdStart
            if ($holdDuration -ge 0.75) {
                $motions = @("Slide", "Drop", "Oscillate")
                $motion = $motions[$rand.Next(0, $motions.Length)]
                $laneFrom = $currentLane
                $laneTo = $laneFrom
                if ($motion -eq "Slide") {
                    $laneTo = ($laneFrom + 1 + $rand.Next(0, 2)) % 3
                }
                elseif ($motion -eq "Oscillate") {
                    $laneTo = ($laneFrom + 2) % 3
                }

                $holdEvents.Add([pscustomobject]@{
                        startSec = [Math]::Round($holdStart, 4)
                        endSec = [Math]::Round($holdEnd, 4)
                        motion = $motion
                        laneFrom = $laneFrom
                        laneTo = $laneTo
                        prefabId = "hold_basic"
                    })
            }
        }
    }

    if ($lastType -ne "") {
        $sections.Add([pscustomobject]@{
                type = $lastType
                startSec = [Math]::Round($sectionStart, 4)
                endSec = [Math]::Round($DurationSec, 4)
            })
    }

    $tapEventsSorted = $tapEvents | Sort-Object timeSec
    $holdEventsSorted = $holdEvents | Sort-Object startSec
    $pulsesSorted = $pulses | Sort-Object timeSec

    return [pscustomobject]@{
        schemaVersion = "1.0.0"
        trackId = $TrackId
        bpm = [Math]::Round($Bpm, 4)
        offsetSec = [Math]::Round($OffsetSec, 4)
        seed = $Seed
        sections = $sections.ToArray()
        tapEvents = @($tapEventsSorted)
        holdEvents = @($holdEventsSorted)
        visualPulses = @($pulsesSorted)
        breathGaps = $breathGaps.ToArray()
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedGamePath = Resolve-PathSafe -InputPath $GamePath -RepoRoot $repoRoot
if (!(Test-Path $resolvedGamePath)) {
    throw "Game path not found: $resolvedGamePath"
}

if ([string]::IsNullOrWhiteSpace($AudioRoot)) {
    $AudioRoot = Join-Path $resolvedGamePath "AudioLocal"
} else {
    $AudioRoot = Resolve-PathSafe -InputPath $AudioRoot -RepoRoot $repoRoot
}

$contentRoot = Join-Path $resolvedGamePath "Content"
$levelsRoot = Join-Path $contentRoot "Levels"
New-Item -ItemType Directory -Path $AudioRoot -Force | Out-Null
New-Item -ItemType Directory -Path $levelsRoot -Force | Out-Null

$requiredTracks = @(
    [pscustomobject]@{ Index = 9; Name = "9. Electro Dance Mania.wav"; TrackId = "electro_dance_mania"; LevelFile = "level01_electro.json" },
    [pscustomobject]@{ Index = 4; Name = "4. RoboTrance.wav"; TrackId = "robo_trance"; LevelFile = "level02_robo.json" }
)

$missing = @()
foreach ($t in $requiredTracks) {
    $candidate = Join-Path $AudioRoot $t.Name
    if (!(Test-Path $candidate)) {
        $missing += $t.Name
    }
}

if ($CopyFromZip -or $missing.Count -gt 0) {
    Import-RequiredTracks -ZipPath $SourceZip -TargetAudioRoot $AudioRoot
}

$catalogTracks = New-Object System.Collections.Generic.List[object]

for ($i = 0; $i -lt $requiredTracks.Count; $i++) {
    $track = $requiredTracks[$i]
    $wavPath = Join-Path $AudioRoot $track.Name
    if (!(Test-Path $wavPath)) {
        throw "Required track missing: $wavPath"
    }

    $wav = Get-WavData -Path $wavPath
    $energyFlux = Get-EnergyFlux -Samples $wav.Samples -SampleRate $wav.SampleRate
    $bpm = Estimate-Bpm -Flux $energyFlux.Flux -HopSec $energyFlux.HopSec
    $offset = Estimate-Offset -Flux $energyFlux.Flux -HopSec $energyFlux.HopSec

    $spb = 60.0 / $bpm
    $barSec = 4.0 * $spb
    $barCount = [int]([Math]::Max(8, [Math]::Floor(([Math]::Max(0.0, $wav.DurationSec - $offset)) / $barSec)))
    $barEnergy = Build-BarEnergy -Flux $energyFlux.Flux -HopSec $energyFlux.HopSec -StartSec $offset -BarSec $barSec -BarCount $barCount

    $mapSeed = $Seed + (($i + 1) * 7919)
    $beatMap = Build-SectionsAndEvents -TrackId $track.TrackId -Bpm $bpm -OffsetSec $offset -DurationSec $wav.DurationSec -BarEnergy $barEnergy -Seed $mapSeed

    $levelPath = Join-Path $levelsRoot $track.LevelFile
    $beatMap | ConvertTo-Json -Depth 12 | Set-Content -Path $levelPath

    $catalogTracks.Add([ordered]@{
            trackId = $track.TrackId
            filePath = "AudioLocal/$($track.Name)"
            bpm = [Math]::Round($bpm, 4)
            offsetSec = [Math]::Round($offset, 4)
            durationSec = [Math]::Round($wav.DurationSec, 4)
            startTrimSec = 0.0
            levelPath = "Content/Levels/$($track.LevelFile)"
        })

    Write-Host "Generated: $levelPath"
}

$catalog = [pscustomobject]@{ tracks = $catalogTracks.ToArray() }
$catalogPath = Join-Path $contentRoot "music_catalog.json"
$catalog | ConvertTo-Json -Depth 8 | Set-Content -Path $catalogPath
Write-Host "Generated: $catalogPath"
