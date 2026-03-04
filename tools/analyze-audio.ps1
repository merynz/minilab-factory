param(
    [string]$GamePath = "",
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

    $limit = [Math]::Min($Flux.Length - 1, [int](12 / $HopSec))
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

function Get-OnsetCandidates([double[]]$Flux, [double]$HopSec, [double]$OffsetSec) {
    $mean = ($Flux | Measure-Object -Average).Average
    $variance = 0.0
    foreach ($v in $Flux) {
        $variance += ($v - $mean) * ($v - $mean)
    }
    $std = [Math]::Sqrt($variance / [Math]::Max(1, $Flux.Length - 1))
    $threshold = $mean + (0.70 * $std)

    $result = New-Object System.Collections.Generic.List[object]
    for ($i = 1; $i -lt ($Flux.Length - 1); $i++) {
        if ($Flux[$i] -lt $threshold) {
            continue
        }

        if ($Flux[$i] -lt $Flux[$i - 1] -or $Flux[$i] -lt $Flux[$i + 1]) {
            continue
        }

        $time = ($i * $HopSec)
        if ($time -lt $OffsetSec) {
            continue
        }

        $result.Add([pscustomobject]@{
                timeSec = [Math]::Round($time, 4)
                strength = [Math]::Round($Flux[$i], 6)
            })
    }

    return @($result | Sort-Object timeSec | ForEach-Object { $_ })
}

function Thin-Events([object[]]$Events, [double]$MinSpacingSec) {
    $thinned = New-Object System.Collections.Generic.List[object]
    $lastTime = -999.0
    foreach ($evt in ($Events | Sort-Object timeSec)) {
        if (($evt.timeSec - $lastTime) -ge $MinSpacingSec) {
            $thinned.Add($evt)
            $lastTime = $evt.timeSec
        }
        elseif ($evt.strength -gt $thinned[$thinned.Count - 1].strength) {
            $thinned[$thinned.Count - 1] = $evt
            $lastTime = $evt.timeSec
        }
    }

    return $thinned.ToArray()
}

function Build-RestSections([double]$DurationSec, [double]$OffsetSec, [int]$SeedValue) {
    $rng = [System.Random]::new($SeedValue)
    $rest = New-Object System.Collections.Generic.List[object]

    $cursor = [Math]::Max($OffsetSec + 6.0, 6.0)
    while ($cursor -lt ($DurationSec - 4.0)) {
        $length = 2.0 + ($rng.NextDouble() * 2.0) # 2-4 sec
        $start = $cursor
        $end = [Math]::Min($DurationSec - 0.5, $start + $length)
        if (($end - $start) -ge 1.5) {
            $rest.Add([pscustomobject]@{
                    startSec = [Math]::Round($start, 4)
                    endSec = [Math]::Round($end, 4)
                })
        }

        $gap = 8.0 + ($rng.NextDouble() * 4.0) # every 8-12 sec
        $cursor += $gap
    }

    return @($rest | Sort-Object startSec | ForEach-Object { $_ })
}

function Is-InRest([double]$TimeSec, [object[]]$RestSections) {
    foreach ($rest in $RestSections) {
        if ($TimeSec -ge $rest.startSec -and $TimeSec -le $rest.endSec) {
            return $true
        }
    }

    return $false
}

function Get-LongSegments([double[]]$Energy, [double]$HopSec, [double]$DurationSec, [object[]]$RestSections) {
    $sorted = $Energy.Clone()
    [Array]::Sort($sorted)
    $threshold = $sorted[[int]([Math]::Floor($sorted.Length * 0.75))]

    $segments = New-Object System.Collections.Generic.List[object]
    $startIdx = -1
    for ($i = 0; $i -lt $Energy.Length; $i++) {
        if ($Energy[$i] -ge $threshold) {
            if ($startIdx -lt 0) {
                $startIdx = $i
            }
            continue
        }

        if ($startIdx -ge 0) {
            $startSec = $startIdx * $HopSec
            $endSec = $i * $HopSec
            $len = $endSec - $startSec
            if ($len -ge 0.8 -and -not (Is-InRest $startSec $RestSections)) {
                $segments.Add([pscustomobject]@{
                        startSec = [Math]::Round($startSec, 4)
                        endSec = [Math]::Round([Math]::Min($DurationSec, $endSec), 4)
                    })
            }

            $startIdx = -1
        }
    }

    if ($startIdx -ge 0) {
        $startSec = $startIdx * $HopSec
        $endSec = $DurationSec
        if (($endSec - $startSec) -ge 0.8 -and -not (Is-InRest $startSec $RestSections)) {
            $segments.Add([pscustomobject]@{
                    startSec = [Math]::Round($startSec, 4)
                    endSec = [Math]::Round($endSec, 4)
                })
        }
    }

    return @($segments | Sort-Object startSec | ForEach-Object { $_ })
}

function Build-Sections([double]$OffsetSec, [double]$DurationSec, [object[]]$RestSections, [object[]]$Events) {
    $sections = New-Object System.Collections.Generic.List[object]

    $cursor = [Math]::Max(0.0, $OffsetSec)
    foreach ($rest in $RestSections) {
        if ($rest.startSec -gt $cursor) {
            $sections.Add([pscustomobject]@{
                    type = "Active"
                    startSec = [Math]::Round($cursor, 4)
                    endSec = [Math]::Round($rest.startSec, 4)
                    density = 0.0
                    intensity = 0.0
                })
        }

        $sections.Add([pscustomobject]@{
                type = "Rest"
                startSec = $rest.startSec
                endSec = $rest.endSec
                density = 0.0
                intensity = 0.1
            })
        $cursor = [Math]::Max($cursor, $rest.endSec)
    }

    if ($cursor -lt $DurationSec) {
        $sections.Add([pscustomobject]@{
                type = "Active"
                startSec = [Math]::Round($cursor, 4)
                endSec = [Math]::Round($DurationSec, 4)
                density = 0.0
                intensity = 0.0
            })
    }

    foreach ($section in $sections) {
        $dur = [Math]::Max(0.001, $section.endSec - $section.startSec)
        $inside = @($Events | Where-Object { $_.timeSec -ge $section.startSec -and $_.timeSec -lt $section.endSec })
        $density = [Math]::Min(1.0, $inside.Count / [Math]::Max(1.0, $dur * 1.4))
        $intensity = if ($inside.Count -gt 0) { ($inside | Measure-Object -Property intensity -Average).Average } else { 0.2 }
        $section.density = [Math]::Round($density, 3)
        $section.intensity = [Math]::Round([Math]::Min(1.0, [Math]::Max(0.0, $intensity)), 3)
    }

    return $sections.ToArray()
}

function Assign-Lanes([object[]]$Events, [int]$SeedValue) {
    $rng = [System.Random]::new($SeedValue)
    $lane = 0
    $streak = 0

    foreach ($evt in ($Events | Sort-Object timeSec)) {
        $next = if ($rng.NextDouble() -gt 0.5) { 1 } else { 0 }
        if ($streak -ge 3) {
            $next = 1 - $lane
        }

        if ($next -eq $lane) {
            $streak++
        }
        else {
            $lane = $next
            $streak = 1
        }

        $evt.lane = $lane
    }

    return $Events
}

function Build-BeatMap(
    [string]$TrackId,
    [double]$Bpm,
    [double]$OffsetSec,
    [double]$DurationSec,
    [double[]]$Flux,
    [double[]]$Energy,
    [double]$HopSec,
    [int]$SeedValue
) {
    $restSections = Build-RestSections -DurationSec $DurationSec -OffsetSec $OffsetSec -SeedValue $SeedValue
    $onsets = Get-OnsetCandidates -Flux $Flux -HopSec $HopSec -OffsetSec $OffsetSec
    $onsets = Thin-Events -Events $onsets -MinSpacingSec 0.12
    $onsets = @($onsets | Where-Object { -not (Is-InRest $_.timeSec $restSections) })

    $accentCount = [Math]::Max(1, [int]([Math]::Ceiling($onsets.Count * 0.10)))
    $accentTimes = New-Object System.Collections.Generic.HashSet[string]
    foreach ($a in ($onsets | Sort-Object strength -Descending | Select-Object -First $accentCount)) {
        [void]$accentTimes.Add($a.timeSec.ToString("F4"))
    }

    $tapAccentEvents = New-Object System.Collections.Generic.List[object]
    foreach ($evt in ($onsets | Sort-Object timeSec)) {
        $kind = if ($accentTimes.Contains($evt.timeSec.ToString("F4"))) { "Accent" } else { "Tap" }
        $tapAccentEvents.Add([pscustomobject]@{
                timeSec = [Math]::Round($evt.timeSec, 4)
                endTimeSec = [Math]::Round($evt.timeSec, 4)
                lane = 0
                kind = $kind
                intensity = if ($kind -eq "Accent") { 1.0 } else { 0.65 }
                prefabId = if ($kind -eq "Accent") { "accent_basic" } else { "tap_basic" }
                motion = "Slide"
            })
    }

    $longSegments = New-Object System.Collections.Generic.List[object]
    foreach ($segment in (Get-LongSegments -Energy $Energy -HopSec $HopSec -DurationSec $DurationSec -RestSections $restSections)) {
        $longSegments.Add($segment)
    }

    if ($longSegments.Count -eq 0) {
        $fallbackRng = [System.Random]::new($SeedValue + 111)
        $cursor = [Math]::Max($OffsetSec + 5.0, 5.0)
        while ($longSegments.Count -lt 3 -and $cursor -lt ($DurationSec - 2.5)) {
            $start = $cursor + ($fallbackRng.NextDouble() * 1.2)
            $end = [Math]::Min($DurationSec - 0.5, $start + 1.0 + ($fallbackRng.NextDouble() * 1.4))
            if (($end - $start) -ge 0.8 -and -not (Is-InRest $start $restSections)) {
                $longSegments.Add([pscustomobject]@{
                        startSec = [Math]::Round($start, 4)
                        endSec = [Math]::Round($end, 4)
                    })
            }

            $cursor += 14.0
        }
    }
    $longEvents = New-Object System.Collections.Generic.List[object]
    foreach ($segment in $longSegments) {
        $longEvents.Add([pscustomobject]@{
                timeSec = $segment.startSec
                endTimeSec = $segment.endSec
                lane = 0
                kind = "Long"
                intensity = 0.86
                prefabId = "long_basic"
                motion = "Drop"
            })
    }

    Assign-Lanes -Events $tapAccentEvents -SeedValue ($SeedValue + 17) | Out-Null
    Assign-Lanes -Events $longEvents -SeedValue ($SeedValue + 47) | Out-Null

    $events = @($tapAccentEvents + $longEvents | Sort-Object timeSec)
    $sections = Build-Sections -OffsetSec $OffsetSec -DurationSec $DurationSec -RestSections $restSections -Events $events

    $tapLegacy = @($events | Where-Object { $_.kind -eq "Tap" -or $_.kind -eq "Accent" } | ForEach-Object {
            [pscustomobject]@{
                timeSec = $_.timeSec
                beatIndex = [int][Math]::Round(($_.timeSec - $OffsetSec) / (60.0 / $Bpm))
                lane = [int]$_.lane
                prefabId = "tap_basic"
                intensityTag = if ($_.kind -eq "Accent") { "high" } else { "mid" }
            }
        })

    $longLegacy = @($events | Where-Object { $_.kind -eq "Long" } | ForEach-Object {
            [pscustomobject]@{
                startSec = $_.timeSec
                endSec = $_.endTimeSec
                lane = [int]$_.lane
                intensity = $_.intensity
            }
        })

    $pulses = @($events | Where-Object { $_.kind -eq "Accent" } | ForEach-Object {
            [pscustomobject]@{
                timeSec = $_.timeSec
                strength = $_.intensity
            }
        })

    $gaps = @($restSections | ForEach-Object {
            [pscustomobject]@{
                startSec = $_.startSec
                endSec = $_.endSec
            }
        })

    return [pscustomobject]@{
        schemaVersion = "2.0.0"
        trackId = $TrackId
        bpm = [Math]::Round($Bpm, 4)
        offsetSec = [Math]::Round($OffsetSec, 4)
        seed = $SeedValue
        sections = $sections
        restSections = $restSections
        events = $events
        tapEvents = $tapLegacy
        longEvents = $longLegacy
        visualPulses = $pulses
        breathGaps = $gaps
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($GamePath)) {
    $detectedGame = Get-ChildItem -Path (Join-Path $repoRoot "Games") -Directory -Filter "Game_*" -ErrorAction SilentlyContinue |
        Sort-Object Name |
        Select-Object -First 1
    if ($null -eq $detectedGame) {
        throw "GamePath not provided and no Games/Game_* directory found."
    }

    $GamePath = [System.IO.Path]::GetRelativePath($repoRoot, $detectedGame.FullName).Replace('\', '/')
    Write-Host "Auto-detected GamePath: $GamePath"
}

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
    [pscustomobject]@{ Name = "9. Electro Dance Mania.wav"; TrackId = "electro_dance_mania"; LevelFile = "level01_electro.json" },
    [pscustomobject]@{ Name = "4. RoboTrance.wav"; TrackId = "robo_trance"; LevelFile = "level02_robo.json" }
)

$missing = @()
foreach ($t in $requiredTracks) {
    $candidate = Join-Path $AudioRoot $t.Name
    if (!(Test-Path $candidate)) {
        $missing += $t.Name
    }
}

if ($CopyFromZip -or $missing.Count -gt 0) {
    if (Test-Path $SourceZip) {
        Import-RequiredTracks -ZipPath $SourceZip -TargetAudioRoot $AudioRoot
    } else {
        Write-Host "SKIP: required WAV files are missing and source zip was not found: $SourceZip"
        Write-Host "Expected local files under: $AudioRoot"
        exit 0
    }
}

$stillMissing = @()
foreach ($t in $requiredTracks) {
    $candidate = Join-Path $AudioRoot $t.Name
    if (!(Test-Path $candidate)) {
        $stillMissing += $t.Name
    }
}

if ($stillMissing.Count -gt 0) {
    Write-Host "SKIP: required WAV files are missing: $($stillMissing -join ', ')"
    Write-Host "Expected local files under: $AudioRoot"
    exit 0
}

$catalogTracks = New-Object System.Collections.Generic.List[object]
for ($i = 0; $i -lt $requiredTracks.Count; $i++) {
    $track = $requiredTracks[$i]
    $wavPath = Join-Path $AudioRoot $track.Name

    $wav = Get-WavData -Path $wavPath
    $energyFlux = Get-EnergyFlux -Samples $wav.Samples -SampleRate $wav.SampleRate
    $bpm = Estimate-Bpm -Flux $energyFlux.Flux -HopSec $energyFlux.HopSec
    $offset = Estimate-Offset -Flux $energyFlux.Flux -HopSec $energyFlux.HopSec

    $trackSeed = $Seed + (($i + 1) * 9973)
    $beatMap = Build-BeatMap `
        -TrackId $track.TrackId `
        -Bpm $bpm `
        -OffsetSec $offset `
        -DurationSec $wav.DurationSec `
        -Flux $energyFlux.Flux `
        -Energy $energyFlux.Energy `
        -HopSec $energyFlux.HopSec `
        -SeedValue $trackSeed

    $levelPath = Join-Path $levelsRoot $track.LevelFile
    $beatMap | ConvertTo-Json -Depth 16 | Set-Content -Path $levelPath

    $catalogTracks.Add([ordered]@{
            trackId = $track.TrackId
            filePath = "AudioLocal/$($track.Name)"
            audioPath = "AudioLocal/$($track.Name)"
            bpm = [Math]::Round($bpm, 4)
            offsetSec = [Math]::Round($offset, 4)
            durationSec = [Math]::Round($wav.DurationSec, 4)
            startTrimSec = 0.0
            levelPath = "Content/Levels/$($track.LevelFile)"
        })

    Write-Host "Generated: $levelPath"
}

$catalog = [pscustomobject]@{ tracks = $catalogTracks.ToArray() }
$catalogPath = Join-Path $levelsRoot "music_catalog.json"
$catalog | ConvertTo-Json -Depth 8 | Set-Content -Path $catalogPath
Write-Host "Generated: $catalogPath"
