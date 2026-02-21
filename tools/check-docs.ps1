Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$docsRoot = Join-Path $repoRoot "Docs"

$requiredDocs = @(
    "Docs/00-ReviewPack.md",
    "Docs/01-ReleaseChecklist.md",
    "Docs/02-PlayConsoleSetup.md",
    "Docs/03-AdMob-UMP-Consent.md",
    "Docs/04-DataSafety-SDKInventory.md",
    "Docs/05-Metrics-EventSchema.md",
    "Docs/06-TwoWeekCadence.md",
    "Docs/07-CICD-BuildAndUpload.md",
    "Docs/08-RemoteConfig-And-DebugMenu.md",
    "Docs/09-QualityGates-And-DeviceTargets.md",
    "Docs/10-Android-TargetApi-And-BuildSettings.md",
    "Docs/11-AppStoreConnectSetup.md",
    "Docs/12-iOS-Signing-Provisioning.md",
    "Docs/13-iOS-PrivacyLabel-ATT.md",
    "Docs/14-iOS-PrivacyManifest-RequiredReasonAPIs.md",
    "Docs/15-TestFlight-Pipeline.md",
    "Docs/16-iOS-AdMob-UMP.md",
    "Docs/17-StoreOps-StoreYaml-Standard.md",
    "Docs/18-Acceptance-Criteria.md",
    "Docs/99-Policy-References.md",
    "Docs/INDEX.md"
)

$missing = New-Object System.Collections.Generic.List[string]
foreach ($path in $requiredDocs) {
    $fullPath = Join-Path $repoRoot $path
    if (!(Test-Path $fullPath)) {
        $missing.Add($path)
    }
}

if ($missing.Count -gt 0) {
    Write-Host "FAIL: missing required docs."
    $missing | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: docs checklist complete."
