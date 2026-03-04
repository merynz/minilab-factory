param(
    [string]$GamePath = "",
    [ValidateSet("android", "ios", "both")][string]$Platform = "both"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$results = New-Object System.Collections.Generic.List[object]

function Add-Result([string]$Check, [string]$Status, [string]$Details) {
    $results.Add([pscustomobject]@{
            Check = $Check
            Status = $Status
            Details = $Details
        })
}

function Resolve-CanonicalPath([string]$InputPath, [string]$RepoRootPath) {
    $candidate = $InputPath
    if (-not [System.IO.Path]::IsPathRooted($candidate)) {
        $candidate = Join-Path $RepoRootPath $candidate
    }

    $candidate = [System.IO.Path]::GetFullPath($candidate)
    if (!(Test-Path $candidate)) {
        throw "Path not found: $InputPath"
    }

    return (Resolve-Path $candidate).Path
}

function Run-ChildPowerShell([string]$ScriptPath, [string[]]$Arguments) {
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $args = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $ScriptPath)
    if ($Arguments) {
        $args += $Arguments
    }

    $process = Start-Process -FilePath $pwsh -ArgumentList $args -Wait -PassThru -NoNewWindow
    return $process.ExitCode
}

function Resolve-UnityPath {
    if ($env:MINILAB_UNITY_PATH -and (Test-Path $env:MINILAB_UNITY_PATH)) {
        return (Resolve-Path $env:MINILAB_UNITY_PATH).Path
    }

    $roots = @(
        "C:\Program Files\Unity\Hub\Editor",
        "C:\Program Files\Unity"
    )

    foreach ($root in $roots) {
        if (!(Test-Path $root)) { continue }
        $candidate = Get-ChildItem -Path $root -Recurse -Filter "Unity.exe" -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }

    return ""
}

function Resolve-AdbPath {
    $adb = Get-Command adb -ErrorAction SilentlyContinue
    if ($adb) {
        return $adb.Source
    }

    if ($env:ANDROID_SDK_ROOT) {
        $sdkAdb = Join-Path $env:ANDROID_SDK_ROOT "platform-tools/adb.exe"
        if (Test-Path $sdkAdb) {
            return (Resolve-Path $sdkAdb).Path
        }
    }

    $unityPath = Resolve-UnityPath
    if (-not [string]::IsNullOrWhiteSpace($unityPath)) {
        $unityEditorDir = Split-Path -Parent $unityPath
        $unityAdb = Join-Path $unityEditorDir "Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
        if (Test-Path $unityAdb) {
            return (Resolve-Path $unityAdb).Path
        }
    }

    return ""
}

function Get-RepoSlugFromGitRemote([string]$RepoRootPath) {
    Push-Location $RepoRootPath
    $remote = (& git remote get-url origin 2>$null)
    Pop-Location
    if ([string]::IsNullOrWhiteSpace($remote)) {
        return ""
    }

    $patterns = @(
        'github\.com[:/](?<slug>[^/\s]+/[^/\s]+?)(?:\.git)?$',
        'https://github\.com/(?<slug>[^/\s]+/[^/\s]+?)(?:\.git)?$',
        'ssh://git@github\.com/(?<slug>[^/\s]+/[^/\s]+?)(?:\.git)?$'
    )

    foreach ($pattern in $patterns) {
        $match = [regex]::Match($remote, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($match.Success) {
            return $match.Groups["slug"].Value
        }
    }

    return ""
}

function Resolve-GhPath {
    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if ($gh) {
        return $gh.Source
    }

    $candidates = @(
        "C:\Program Files\GitHub CLI\gh.exe",
        "C:\Program Files (x86)\GitHub CLI\gh.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    return ""
}

try {
    $hasGame = $true
    $resolvedGamePath = ""
    if ([string]::IsNullOrWhiteSpace($GamePath)) {
        $detectedGame = Get-ChildItem -Path (Join-Path $repoRoot "Games") -Directory -Filter "Game_*" -ErrorAction SilentlyContinue |
            Sort-Object Name |
            Select-Object -First 1
        if ($null -eq $detectedGame) {
            $hasGame = $false
            Add-Result -Check "GamePath" -Status "SKIP" -Details "No Games/Game_* directory found yet."
        } else {
            $GamePath = [System.IO.Path]::GetRelativePath($repoRoot, $detectedGame.FullName).Replace('\', '/')
        }
    }

    if ($hasGame -and -not [string]::IsNullOrWhiteSpace($GamePath)) {
        $resolvedGamePath = Resolve-CanonicalPath -InputPath $GamePath -RepoRootPath $repoRoot
        Add-Result -Check "GamePath" -Status "PASS" -Details $resolvedGamePath

        $storePath = Join-Path $resolvedGamePath "store.yaml"
        try {
            & (Join-Path $PSScriptRoot "check-store-yaml.ps1") -StorePath $storePath | Out-Null
            Add-Result -Check "store.yaml schema" -Status "PASS" -Details $storePath
        } catch {
            Add-Result -Check "store.yaml schema" -Status "FAIL" -Details $_.Exception.Message
            $hasGame = $false
        }

        $projectPath = Join-Path $resolvedGamePath "UnityProject"
        if (!(Test-Path $projectPath)) {
            Add-Result -Check "UnityProject" -Status "FAIL" -Details "UnityProject not found."
            $hasGame = $false
        } else {
            Add-Result -Check "UnityProject" -Status "PASS" -Details $projectPath
        }
    } else {
        Add-Result -Check "store.yaml schema" -Status "SKIP" -Details "No game yet."
        Add-Result -Check "UnityProject" -Status "SKIP" -Details "No game yet."
    }

    if ($Platform -eq "android" -or $Platform -eq "both") {
        $doctorCode = Run-ChildPowerShell -ScriptPath (Join-Path $PSScriptRoot "doctor.ps1") -Arguments @("-RequireUploadTooling")
        if ($doctorCode -eq 0) {
            Add-Result -Check "Android doctor/upload tooling" -Status "PASS" -Details "doctor -RequireUploadTooling"
        } else {
            Add-Result -Check "Android doctor/upload tooling" -Status "FAIL" -Details "doctor -RequireUploadTooling returned non-zero."
        }

        $playJson = if ($env:MINILAB_PLAY_JSON) { $env:MINILAB_PLAY_JSON } else { $env:GOOGLE_PLAY_JSON_KEY_PATH }
        if ([string]::IsNullOrWhiteSpace($playJson) -or !(Test-Path $playJson)) {
            Add-Result -Check "Play credentials" -Status "FAIL" -Details "MINILAB_PLAY_JSON / GOOGLE_PLAY_JSON_KEY_PATH missing."
        } else {
            Add-Result -Check "Play credentials" -Status "PASS" -Details $playJson
        }

        $adbPath = Resolve-AdbPath
        if (-not [string]::IsNullOrWhiteSpace($adbPath)) {
            $devices = & $adbPath devices
            $deviceCount = @($devices | Where-Object { $_ -match "device$" -and $_ -notmatch "^List of devices" }).Count
            if ($deviceCount -gt 0) {
                Add-Result -Check "Android test device" -Status "PASS" -Details "Connected devices: $deviceCount"
            } else {
                Add-Result -Check "Android test device" -Status "SKIP" -Details "No connected device/emulator."
            }
        } else {
            Add-Result -Check "Android test device" -Status "SKIP" -Details "adb not found."
        }
    }

    if ($Platform -eq "ios" -or $Platform -eq "both") {
        if ($IsMacOS) {
            Add-Result -Check "iOS host" -Status "PASS" -Details "macOS host detected."
        } else {
            if (Test-Path (Join-Path $repoRoot ".github/workflows/mobile-release.yml")) {
                Add-Result -Check "iOS host" -Status "PASS" -Details "Windows host with GitHub Actions iOS workflow fallback."
            } else {
                Add-Result -Check "iOS host" -Status "FAIL" -Details "macOS host missing and workflow fallback not found."
            }

            $ghPath = Resolve-GhPath
            if (-not [string]::IsNullOrWhiteSpace($ghPath)) {
                Add-Result -Check "GitHub CLI" -Status "PASS" -Details $ghPath
            } else {
                Add-Result -Check "GitHub CLI" -Status "FAIL" -Details "gh not found (required for Windows iOS workflow dispatch)."
            }

            $repoSlug = Get-RepoSlugFromGitRemote -RepoRootPath $repoRoot
            if ([string]::IsNullOrWhiteSpace($repoSlug)) {
                Add-Result -Check "Git remote slug" -Status "FAIL" -Details "Could not resolve owner/repo from git remote origin."
            } else {
                Add-Result -Check "Git remote slug" -Status "PASS" -Details $repoSlug
            }
        }

        $hasAscApiKey = -not [string]::IsNullOrWhiteSpace($env:APP_STORE_CONNECT_API_KEY_ID) -and
            -not [string]::IsNullOrWhiteSpace($env:APP_STORE_CONNECT_ISSUER_ID) -and
            -not [string]::IsNullOrWhiteSpace($env:APP_STORE_CONNECT_API_KEY_CONTENT)
        $hasFastlaneSession = -not [string]::IsNullOrWhiteSpace($env:FASTLANE_SESSION)

        if ($hasAscApiKey -or $hasFastlaneSession) {
            Add-Result -Check "App Store Connect credentials" -Status "PASS" -Details "ASC env detected."
        } else {
            Add-Result -Check "App Store Connect credentials" -Status "SKIP" -Details "ASC env not set in local shell."
        }
    }

    if (Test-Path (Join-Path $repoRoot ".github/workflows/mobile-release.yml")) {
        Add-Result -Check "CI release workflow" -Status "PASS" -Details ".github/workflows/mobile-release.yml"
    } else {
        Add-Result -Check "CI release workflow" -Status "FAIL" -Details "mobile-release.yml missing."
    }

    if (Test-Path (Join-Path $repoRoot ".github/workflows/mobile-smoke-test.yml")) {
        Add-Result -Check "CI smoke workflow" -Status "PASS" -Details ".github/workflows/mobile-smoke-test.yml"
    } else {
        Add-Result -Check "CI smoke workflow" -Status "FAIL" -Details "mobile-smoke-test.yml missing."
    }

    if (Test-Path (Join-Path $repoRoot "tools/smoke-android.ps1")) {
        Add-Result -Check "Android smoke script" -Status "PASS" -Details "tools/smoke-android.ps1"
    } else {
        Add-Result -Check "Android smoke script" -Status "FAIL" -Details "tools/smoke-android.ps1 missing."
    }
} catch {
    Add-Result -Check "Unhandled" -Status "FAIL" -Details $_.Exception.Message
}

Write-Host "MiniLab Platform Readiness"
Write-Host "-------------------------"
$results | Format-Table -AutoSize

$failCount = @($results | Where-Object { $_.Status -eq "FAIL" }).Count
$passCount = @($results | Where-Object { $_.Status -eq "PASS" }).Count
$skipCount = @($results | Where-Object { $_.Status -eq "SKIP" }).Count

Write-Host ""
Write-Host "Summary: PASS=$passCount FAIL=$failCount SKIP=$skipCount"

if ($failCount -gt 0) {
    exit 1
}

exit 0
