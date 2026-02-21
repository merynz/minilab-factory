# 07 - CI/CD Build and Upload

Goal: one-command build and one-command upload flows.

## Tooling Choice

- Build orchestration: Unity batch mode + PowerShell scripts in `tools/`.
- Upload orchestration: fastlane lanes in `fastlane/Fastfile`.
- CI runners:
  - Android: Windows or Linux runner with Unity + Java + Android SDK.
  - iOS: macOS runner with Unity + Xcode + fastlane.

## Android Pipeline

### Build (single command)

```powershell
tools/build-android.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject" -OutputName "zebradash-review.aab"
```

Output path is deterministic by default:

- `BuildArtifacts/Android/<GameName>.aab`
- Unity log: `BuildArtifacts/unity-android-build.log`

### Upload Internal Testing (single command)

```powershell
tools/upload-android-internal.ps1 -AabPath "BuildArtifacts/Android/app-release.aab" -PackageName "com.zebratank.zebradash" -SkipIfSecretsMissing
```

Required secret:

- `GOOGLE_PLAY_JSON_KEY_PATH`
- If `bundle` or secret is missing and `-SkipIfSecretsMissing` is set, script returns `SKIP` (not `FAIL`).

## iOS Pipeline

### Build export (single command)

```powershell
tools/build-ios.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject" -SkipIfNoMac
```

### Build archive + Upload TestFlight Internal (single command)

```powershell
tools/build-ios.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject" -Archive -UploadInternal -SkipIfNoMac
```

If host is not macOS, script reports:

- `SKIP: blocked by Mac/signing requirements`
- Alternatives: remote Mac, GitHub Actions macOS runner, Unity Cloud Build.

Required iOS credentials (one of):

- App Store Connect API key triple:
  - `APP_STORE_CONNECT_API_KEY_ID`
  - `APP_STORE_CONNECT_ISSUER_ID`
  - `APP_STORE_CONNECT_API_KEY_CONTENT`
- or `FASTLANE_SESSION`

## PR Review Checks

GitHub Actions on PR runs:

- `tools/check-secrets.ps1`
- `tools/check-docs.ps1`
- `tools/check-template-purity.ps1`
- `tools/check-tree.ps1`
- `tools/check-unity-compile.ps1` (Android compile check, skip allowed if Unity/module unavailable)
- iOS dry-run workflow with explicit SKIP message when no macOS runner

## Versioning Standard

- Semver:
  - `major`: broad feature break
  - `minor`: content/system additions
  - `patch`: fixes/tuning
- Build number:
  - monotonically increasing integer per platform
  - format recommendation: `YYWWNN` (year, week, sequence)

## Artifact Standard

- Android:
  - `.aab`
  - `mapping.txt`
  - Unity build log
  - release notes
- iOS:
  - Xcode archive log
  - exported `.ipa`
  - export options/logs
  - release notes
