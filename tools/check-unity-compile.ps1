param(
    [string]$UnityPath = "",
    [string]$TempProjectPath = "",
    [int]$TimeoutMinutes = 20,
    [switch]$RequireUnity,
    [switch]$RequireAndroidModule
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-UnityPath([string]$ExplicitPath) {
    if ($ExplicitPath -and (Test-Path $ExplicitPath)) {
        return (Resolve-Path $ExplicitPath).Path
    }

    $roots = @(
        "C:\\Program Files\\Unity\\Hub\\Editor",
        "C:\\Program Files\\Unity",
        "/Applications/Unity/Hub/Editor"
    )

    foreach ($root in $roots) {
        if (!(Test-Path $root)) {
            continue
        }

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

        if ($candidate) {
            return $candidate.FullName
        }
    }

    return ""
}

function Show-UnityLogDiagnostics([string]$LogPath) {
    if (!(Test-Path $LogPath)) {
        Write-Host "Log not found: $LogPath"
        return
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

    $process = Start-Process -FilePath $ExePath -ArgumentList $Arguments -PassThru -NoNewWindow
    $waitMs = [int]([Math]::Max(1, $TimeoutMins) * 60 * 1000)
    $completed = $process.WaitForExit($waitMs)

    if (-not $completed) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        return @{
            ExitCode = 124
            TimedOut = $true
            LogPath = $LogPath
        }
    }

    return @{
        ExitCode = $process.ExitCode
        TimedOut = $false
        LogPath = $LogPath
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($TempProjectPath)) {
    if ($IsWindows) {
        # Unity createProject rejects leading-dot folder names on Windows.
        $TempProjectPath = Join-Path $repoRoot "tmp/UnityCompileCheck"
    } else {
        $TempProjectPath = Join-Path $repoRoot ".tmp/UnityCompileCheck"
    }
}

$resolvedUnityPath = Resolve-UnityPath $UnityPath
if ([string]::IsNullOrWhiteSpace($resolvedUnityPath)) {
    $msg = "Unity not found. Provide -UnityPath."
    if ($RequireUnity) {
        throw $msg
    }

    Write-Host "SKIP: $msg"
    exit 0
}

$unityRoot = Split-Path -Parent $resolvedUnityPath
$androidModulePath = Join-Path $unityRoot "Data/PlaybackEngines/AndroidPlayer"
if (!(Test-Path $androidModulePath)) {
    $msg = "Unity Android module missing: $androidModulePath"
    if ($RequireAndroidModule) {
        throw $msg
    }

    Write-Host "SKIP: $msg"
    exit 0
}

$compileLog = Join-Path $repoRoot "BuildArtifacts/unity-compile.log"
$createLog = Join-Path $repoRoot "BuildArtifacts/unity-compile-create.log"

$projectVersionFile = Join-Path $TempProjectPath "ProjectSettings/ProjectVersion.txt"
if (!(Test-Path $projectVersionFile)) {
    $createResult = Invoke-UnityWithTimeout $resolvedUnityPath @(
        "-batchmode",
        "-nographics",
        "-quit",
        "-createProject", $TempProjectPath,
        "-logFile", $createLog
    ) $createLog $TimeoutMinutes

    if ($createResult.TimedOut -or $createResult.ExitCode -ne 0) {
        Show-UnityLogDiagnostics $createLog
        throw "FAIL: Unity project creation failed (timeout or non-zero exit)."
    }
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

$compileResult = Invoke-UnityWithTimeout $resolvedUnityPath @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath", $TempProjectPath,
    "-buildTarget", "Android",
    "-executeMethod", "MiniLab.CompileCheck.EntryPoint.Run",
    "-logFile", $compileLog
) $compileLog $TimeoutMinutes

if ($compileResult.TimedOut -or $compileResult.ExitCode -ne 0) {
    Show-UnityLogDiagnostics $compileLog
    throw "FAIL: Unity Android compile check failed (timeout or non-zero exit)."
}

Write-Host "PASS: Unity Android compile check passed."
Write-Host "Log: $compileLog"
