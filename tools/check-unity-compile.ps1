param(
    [string]$UnityPath = "",
    [string]$ProjectPath = "",
    [string]$TempProjectPath = "",
    [int]$TimeoutMinutes = 20,
    [switch]$RequireUnity,
    [switch]$RequireAndroidModule
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-UnityPath([string]$ExplicitPath) {
    if ($env:MINILAB_UNITY_PATH -and (Test-Path $env:MINILAB_UNITY_PATH)) {
        return (Resolve-Path $env:MINILAB_UNITY_PATH).Path
    }

    if ($ExplicitPath -and (Test-Path $ExplicitPath)) {
        return (Resolve-Path $ExplicitPath).Path
    }

    $roots = @(
        "C:\Program Files\Unity\Hub\Editor",
        "C:\Program Files\Unity",
        "/Applications/Unity/Hub/Editor"
    )

    foreach ($root in $roots) {
        if (!(Test-Path $root)) { continue }

        if ($IsWindows) {
            $candidate = Get-ChildItem -Path $root -Recurse -Filter "Unity.exe" -ErrorAction SilentlyContinue |
                Sort-Object FullName -Descending |
                Select-Object -First 1
        } else {
            $candidate = Get-ChildItem -Path $root -Recurse -Filter "Unity" -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -like "*/Contents/MacOS/Unity" } |
                Sort-Object FullName -Descending |
                Select-Object -First 1
        }

        if ($candidate) { return $candidate.FullName }
    }

    return ""
}

function Resolve-CanonicalProjectPath([string]$InputPath, [string]$RepoRoot) {
    $candidate = $InputPath
    if (-not [System.IO.Path]::IsPathRooted($candidate)) {
        $candidate = Join-Path $RepoRoot $candidate
    }
    $candidate = [System.IO.Path]::GetFullPath($candidate)
    if (!(Test-Path $candidate)) {
        throw "ProjectPath not found: $InputPath"
    }

    $resolved = (Resolve-Path $candidate).Path
    if ((Split-Path -Leaf $resolved) -ine "UnityProject") {
        $unityProjectSubdir = Join-Path $resolved "UnityProject"
        if (Test-Path $unityProjectSubdir) {
            $resolved = (Resolve-Path $unityProjectSubdir).Path
        }
    }

    return $resolved
}

function Get-LogRootCause([string]$LogPath) {
    if (!(Test-Path $LogPath)) { return "" }
    $raw = Get-Content -Path $LogPath -Raw
    $patterns = @(
        @{ Regex = "(?im)Project has invalid dependencies.*"; Reason = "Project has invalid UPM dependencies (manifest local path may be wrong)." },
        @{ Regex = "(?im)Unable to add package.*com\.zebratank\.minilab\.core.*"; Reason = "UPM failed to resolve com.zebratank.minilab.core." },
        @{ Regex = "(?im)Cannot perform upm operation.*"; Reason = "UPM operation failed while opening project." },
        @{ Regex = "(?im)creating project folder:.*failed"; Reason = "Unity failed to create/open temp compile project path." }
    )
    foreach ($pattern in $patterns) {
        if ([regex]::IsMatch($raw, $pattern.Regex)) {
            return $pattern.Reason
        }
    }
    return ""
}

function Show-UnityLogDiagnostics([string]$LogPath) {
    if (!(Test-Path $LogPath)) {
        Write-Host "Log not found: $LogPath"
        return
    }

    $rootCause = Get-LogRootCause $LogPath
    if (-not [string]::IsNullOrWhiteSpace($rootCause)) {
        Write-Host "Root cause: $rootCause"
    }

    Write-Host "----- LOG TAIL (last 200 lines) -----"
    Get-Content -Path $LogPath -Tail 200 | ForEach-Object { Write-Host $_ }

    Write-Host "----- C# COMPILE ERRORS (error CS####) -----"
    $csErrors = Select-String -Path $LogPath -Pattern 'error CS\d+' -CaseSensitive:$false
    if ($csErrors) {
        $csErrors | ForEach-Object { Write-Host $_.Line }
    } else {
        Write-Host "(none)"
    }
}

function Invoke-UnityWithTimeout([string]$ExePath, [string[]]$Arguments, [string]$LogPath, [int]$TimeoutMins) {
    $logDir = Split-Path -Parent $LogPath
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null

    $argsPreview = ($Arguments | ForEach-Object {
            if ($_ -match '\s') { '"' + $_ + '"' } else { $_ }
        }) -join ' '
    Write-Host "Unity command: `"$ExePath`" $argsPreview"

    $process = Start-Process -FilePath $ExePath -ArgumentList $Arguments -PassThru -NoNewWindow

    Start-Sleep -Seconds 8
    $process.Refresh()
    if (-not $process.HasExited -and $process.MainWindowHandle -ne 0) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Show-UnityLogDiagnostics $LogPath
        throw "FAIL: interactive launch happened (Unity GUI window detected)."
    }

    $waitMs = [int]([Math]::Max(1, $TimeoutMins) * 60 * 1000)
    $completed = $process.WaitForExit($waitMs)
    if (-not $completed) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Show-UnityLogDiagnostics $LogPath
        throw "FAIL: Unity compile check timed out after $TimeoutMins minutes."
    }

    if ($process.ExitCode -ne 0) {
        Show-UnityLogDiagnostics $LogPath
        throw "FAIL: Unity compile check failed with exit code $($process.ExitCode)."
    }
}

function Validate-CoreDependencyPaths([string]$RepoRoot) {
    $manifestFiles = Get-ChildItem -Path (Join-Path $RepoRoot "Templates"), (Join-Path $RepoRoot "Games") -Recurse -File -Filter manifest.json -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like "*\UnityProject\Packages\manifest.json" }

    foreach ($manifest in $manifestFiles) {
        $manifestObj = Get-Content -Path $manifest.FullName -Raw | ConvertFrom-Json
        $deps = $manifestObj.dependencies
        if (-not $deps.PSObject.Properties.Name.Contains("com.zebratank.minilab.core")) {
            throw "Missing dependency in $($manifest.FullName): com.zebratank.minilab.core"
        }

        $depValue = [string]$deps."com.zebratank.minilab.core"
        if ($depValue -notlike "file:*") {
            throw "Invalid dependency format in $($manifest.FullName): $depValue (expected file:...)"
        }

        $relativePath = $depValue.Substring(5)
        $manifestDir = Split-Path -Parent $manifest.FullName
        $resolvedPath = [System.IO.Path]::GetFullPath((Join-Path $manifestDir $relativePath))
        if (!(Test-Path $resolvedPath)) {
            throw "Invalid local package path in $($manifest.FullName): $depValue (resolved: $resolvedPath)"
        }
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Validate-CoreDependencyPaths -RepoRoot $repoRoot

$resolvedUnityPath = Resolve-UnityPath $UnityPath
if ([string]::IsNullOrWhiteSpace($resolvedUnityPath)) {
    $msg = "Unity not found. Provide -UnityPath or MINILAB_UNITY_PATH."
    if ($RequireUnity) { throw $msg }
    Write-Host "SKIP: $msg"
    exit 0
}

$unityRoot = Split-Path -Parent $resolvedUnityPath
$androidModulePath = Join-Path $unityRoot "Data/PlaybackEngines/AndroidPlayer"
if (!(Test-Path $androidModulePath)) {
    $msg = "Unity Android module missing: $androidModulePath"
    if ($RequireAndroidModule) { throw $msg }
    Write-Host "SKIP: $msg"
    exit 0
}

New-Item -ItemType Directory -Path (Join-Path $repoRoot "BuildArtifacts") -Force | Out-Null
$compileLog = Join-Path $repoRoot "BuildArtifacts/unity-compile.log"

if (-not [string]::IsNullOrWhiteSpace($ProjectPath)) {
    $resolvedProjectPath = Resolve-CanonicalProjectPath -InputPath $ProjectPath -RepoRoot $repoRoot
    Write-Host "Compile target project: $resolvedProjectPath"

    Invoke-UnityWithTimeout $resolvedUnityPath @(
        "-batchmode",
        "-nographics",
        "-quit",
        "-projectPath", $resolvedProjectPath,
        "-buildTarget", "Android",
        "-stackTraceLogType", "Full",
        "-logFile", $compileLog
    ) $compileLog $TimeoutMinutes
} else {
    if ([string]::IsNullOrWhiteSpace($TempProjectPath)) {
        if ($IsWindows) {
            $TempProjectPath = Join-Path $repoRoot "tmp/UnityCompileCheck"
        } else {
            $TempProjectPath = Join-Path $repoRoot ".tmp/UnityCompileCheck"
        }
    }

    $createLog = Join-Path $repoRoot "BuildArtifacts/unity-compile-create.log"
    $projectVersionFile = Join-Path $TempProjectPath "ProjectSettings/ProjectVersion.txt"
    if (!(Test-Path $projectVersionFile)) {
        Invoke-UnityWithTimeout $resolvedUnityPath @(
            "-batchmode",
            "-nographics",
            "-quit",
            "-createProject", $TempProjectPath,
            "-stackTraceLogType", "Full",
            "-logFile", $createLog
        ) $createLog $TimeoutMinutes
    }

    $manifestPath = Join-Path $TempProjectPath "Packages/manifest.json"
    if (!(Test-Path $manifestPath)) {
        throw "Manifest not found: $manifestPath"
    }

    $manifestObj = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
    $deps = [ordered]@{}
    $manifestObj.dependencies.PSObject.Properties | ForEach-Object { $deps[$_.Name] = $_.Value }
    $corePackagePath = (Resolve-Path (Join-Path $repoRoot "Packages/MiniLab.Core")).Path.Replace('\', '/')
    $deps["com.zebratank.minilab.core"] = "file:$corePackagePath"

    $manifestOut = [ordered]@{
        dependencies = $deps
    }
    if ($manifestObj.PSObject.Properties.Name -contains "scopedRegistries") {
        $manifestOut["scopedRegistries"] = $manifestObj.scopedRegistries
    }
    $manifestOut | ConvertTo-Json -Depth 20 | Set-Content -Path $manifestPath

    $editorDir = Join-Path $TempProjectPath "Assets/Editor"
    New-Item -ItemType Directory -Path $editorDir -Force | Out-Null

    $compileEntryPath = Join-Path $editorDir "CompileCheckEntry.cs"
@"
using UnityEngine;

namespace MiniLab.CompileCheck
{
    public static class EntryPoint
    {
        public static void Run()
        {
            Debug.Log("MiniLab compile check passed.");
        }
    }
}
"@ | Set-Content -Path $compileEntryPath

    Invoke-UnityWithTimeout $resolvedUnityPath @(
        "-batchmode",
        "-nographics",
        "-quit",
        "-projectPath", $TempProjectPath,
        "-buildTarget", "Android",
        "-executeMethod", "MiniLab.CompileCheck.EntryPoint.Run",
        "-stackTraceLogType", "Full",
        "-logFile", $compileLog
    ) $compileLog $TimeoutMinutes
}

Write-Host "PASS: Unity Android compile check passed."
Write-Host "Log: $compileLog"
