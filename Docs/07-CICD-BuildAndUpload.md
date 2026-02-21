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
tools/build-android.ps1 -UnityPath "C:\Unity\Editor\Unity.exe" -ProjectPath "C:\Games\Game_Arcade_ZebraDash"
```

### Upload Internal Testing (single command)

```powershell
tools/upload-android-internal.ps1 -AabPath "BuildArtifacts/Android/game.aab" -PackageName "com.zebratank.zebradash"
```

## iOS Pipeline

### Build export (single command)

```powershell
tools/build-ios.ps1 -UnityPath "/Applications/Unity/Hub/Editor/2022.3.0f1/Unity.app/Contents/MacOS/Unity" -ProjectPath "/Users/runner/Games/Game_Arcade_ZebraDash"
```

### Upload TestFlight Internal (single command)

```powershell
tools/upload-testflight-internal.ps1 -IpaPath "BuildArtifacts/iOS/game.ipa" -BundleId "com.zebratank.zebradash"
```

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

