# 18 - Acceptance Criteria Mapping

## AC-1: New game bootstrap <= 60 minutes

- `tools/new-game.ps1` creates game folder + `store.yaml`.
- Game team then links `MiniLab.Core` via UPM file dependency.
- Android build command is standardized in `Docs/07-CICD-BuildAndUpload.md`.

## AC-2: Compliance reuse (Core + delta)

- Core policy stubs in `Packages/MiniLab.Core/Runtime/Policy`.
- Per-game delta captured only in `store.yaml -> sdk_inventory.delta`.
- Data Safety + Privacy Label docs use merged inventory approach.

## AC-3: Android template defaults API 35+

- Baseline documented in `Docs/10-Android-TargetApi-And-BuildSettings.md`.

## AC-4: iOS submission requirements documented

- Privacy label + ATT: `Docs/13-iOS-PrivacyLabel-ATT.md`
- Privacy manifest + required reason APIs: `Docs/14-iOS-PrivacyManifest-RequiredReasonAPIs.md`
- TestFlight flow: `Docs/15-TestFlight-Pipeline.md`

## AC-5: Single-source store metadata

- Required schema defined in `Docs/17-StoreOps-StoreYaml-Standard.md`
- Template file: `Games/store.template.yaml`

