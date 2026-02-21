param(
    [Parameter(Mandatory = $true)][string]$UnityPath,
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [string]$BuildNumber = "",
    [switch]$UploadInternal
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactDir = Join-Path $repoRoot "BuildArtifacts/Android"
New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null

$logFile = Join-Path $artifactDir "unity-android-build.log"
$buildMethod = "MiniLab.Build.BuildPipelineEntry.BuildAndroidAab"

$args = @(
    "-batchmode",
    "-quit",
    "-projectPath", $ProjectPath,
    "-buildTarget", "Android",
    "-executeMethod", $buildMethod,
    "-logFile", $logFile
)

if ($BuildNumber -ne "") {
    $args += @("-customBuildNumber", $BuildNumber)
}

& $UnityPath @args
if ($LASTEXITCODE -ne 0) {
    throw "Android build failed. Log: $logFile"
}

Write-Host "Android build completed. Log: $logFile"

if ($UploadInternal) {
    & (Join-Path $PSScriptRoot "upload-android-internal.ps1")
}

