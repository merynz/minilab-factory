param(
    [string]$OutputPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "Docs/00-Tree.txt"
}

$excludeRegex = @(
    '(^|/)\.git(/|$)',
    '(^|/)Library(/|$)',
    '(^|/)Temp(/|$)',
    '(^|/)Obj(/|$)',
    '(^|/)Build(/|$)',
    '(^|/)Builds(/|$)',
    '(^|/)Logs(/|$)',
    '(^|/)UserSettings(/|$)',
    '(^|/)\.tmp(/|$)',
    '(^|/)tmp(/|$)',
    '(^|/)\.utmp(/|$)',
    '(^|/)BuildArtifacts(/|$)'
)

$allPaths = Get-ChildItem -Path $repoRoot -Recurse -Force -File |
    ForEach-Object { [System.IO.Path]::GetRelativePath($repoRoot, $_.FullName) } |
    ForEach-Object { $_.Replace('\', '/') } |
    Where-Object {
        $keep = $true
        foreach ($pattern in $excludeRegex) {
            if ($_ -imatch $pattern) {
                $keep = $false
                break
            }
        }
        $keep
    } |
    Sort-Object

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("MiniLab Repo Tree")
$lines.Add("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss K')")
$lines.Add("")
$lines.Add("Files:")
$allPaths | ForEach-Object { $lines.Add(" - $_") }
$lines.Add("")
$lines.Add("Total files: $($allPaths.Count)")

$outputDir = Split-Path -Parent $OutputPath
if (!(Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

Set-Content -Path $OutputPath -Value $lines
Write-Host "PASS: tree written to $OutputPath"
