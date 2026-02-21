param(
    [Parameter(Mandatory = $true)][string]$UnityPath,
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [string]$BuildNumber = "",
    [switch]$UploadInternal
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactDir = Join-Path $repoRoot "BuildArtifacts/iOS"
New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null

$logFile = Join-Path $artifactDir "unity-ios-build.log"
$buildMethod = "MiniLab.Build.BuildPipelineEntry.BuildiOSXcodeProject"

$args = @(
    "-batchmode",
    "-quit",
    "-projectPath", $ProjectPath,
    "-buildTarget", "iOS",
    "-executeMethod", $buildMethod,
    "-logFile", $logFile
)

if ($BuildNumber -ne "") {
    $args += @("-customBuildNumber", $BuildNumber)
}

& $UnityPath @args
if ($LASTEXITCODE -ne 0) {
    throw "iOS build export failed. Log: $logFile"
}

Write-Host "iOS Xcode export completed. Log: $logFile"

if ($UploadInternal) {
    & (Join-Path $PSScriptRoot "upload-testflight-internal.ps1")
}

