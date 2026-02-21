param(
    [Parameter(Mandatory = $true)][string]$Genre,
    [Parameter(Mandatory = $true)][string]$Codename
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$templatePath = Join-Path $repoRoot ("Templates/{0}Template" -f $Genre)
if (!(Test-Path $templatePath)) {
    throw "Template not found: $templatePath"
}

$gameName = "Game_{0}_{1}" -f $Genre, $Codename
$gamePath = Join-Path $repoRoot ("Games/{0}" -f $gameName)
if (Test-Path $gamePath) {
    throw "Game folder already exists: $gamePath"
}

New-Item -ItemType Directory -Path $gamePath -Force | Out-Null
Copy-Item (Join-Path $templatePath "README.md") (Join-Path $gamePath "README.md")

$storeTemplate = Join-Path $repoRoot "Games/store.template.yaml"
Copy-Item $storeTemplate (Join-Path $gamePath "store.yaml")

Write-Host "Created: $gamePath"
Write-Host "Next: create Unity project clone and add MiniLab.Core dependency."
