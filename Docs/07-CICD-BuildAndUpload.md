# 07 - CI/CD Build and Upload

Goal: tek tusla calisan pipeline. Unity GUI acmadan, deterministik log ve timeout ile.

## 0) Doctor (On Kosul)

```powershell
.\tools\doctor.ps1
```

Tooling bootstrap (opsiyonel ama onerilen):

```powershell
.\tools\setup-release-tooling.ps1
```

Secrets env setup:

```powershell
.\tools\configure-release-secrets.ps1 -PlayJsonPath "<play-json-path>" -AscApiKeyId "<id>" -AscIssuerId "<issuer>" -AscApiKeyFile "<AuthKey.p8>"
```

## 1) Android Pipeline (Windows baseline)

`<GamePath>` formati: `Games/Game_<Genre>_<Codename>`

### Release metadata gate

```powershell
.\tools\check-store-yaml.ps1 -GamePath "<GamePath>"
```

### Beatmap uretimi

```powershell
.\tools\analyze-audio.ps1 -GamePath "<GamePath>"
```

### Compile check (hang-proof)

```powershell
.\tools\check-unity-compile.ps1 -ProjectPath "<GamePath>/UnityProject"
```

### APK build (hizli cihaz testi)

```powershell
.\tools\build-android-apk.ps1 -ProjectPath "<GamePath>/UnityProject" -OutputName "<codename>-dev.apk"
```

Deterministik output/log:
- APK: `BuildArtifacts/<codename>-dev.apk`
- Unity log: `BuildArtifacts/unity-android-apk-build.log`

Opsiyonel cihaz kurulumu:

```powershell
.\tools\install-android.ps1 -ApkPath "BuildArtifacts/<codename>-dev.apk"
```

### AAB build

```powershell
.\tools\build-android.ps1 -ProjectPath "<GamePath>/UnityProject" -OutputName "<codename>-review.aab"
```

Deterministik output/log:
- AAB: `BuildArtifacts/Android/<codename>-review.aab`
- Unity log: `BuildArtifacts/unity-android-build.log`

### Internal upload (opsiyonel)

```powershell
.\tools\upload-android-internal.ps1 -AabPath "BuildArtifacts/Android/<codename>-review.aab" -GamePath "<GamePath>" -SkipIfSecretsMissing
```

Play text metadata upload (opsiyonel):

```powershell
.\tools\upload-android-internal.ps1 -AabPath "BuildArtifacts/Android/<codename>-review.aab" -GamePath "<GamePath>" -UploadMetadata
```

### One-command internal release

```powershell
.\tools\release-android-internal.ps1 -GamePath "<GamePath>" -UploadMetadata
```

Bu zincir:
- `check-store-yaml`
- `check-unity-compile`
- `analyze-audio`
- `build-android`
- `upload-android-internal`

Secret standardi:
- Tercih edilen: `MINILAB_PLAY_JSON`
- Geriye donuk destek: `GOOGLE_PLAY_JSON_KEY_PATH`

## 2) iOS Pipeline (Resmi Yol + Alternatifler)

### Resmi yol: Seçenek B - GitHub Actions macOS runner

Windows host'ta iOS scripts:
- `tools/build-ios.ps1 -SkipIfNoMac` -> `SKIP`
- `tools/upload-testflight-internal.ps1 -SkipIfNoMac` -> `SKIP`

### iOS secrets

- `APP_STORE_CONNECT_API_KEY_ID`
- `APP_STORE_CONNECT_ISSUER_ID`
- `APP_STORE_CONNECT_API_KEY_CONTENT`
- (opsiyonel fallback) `FASTLANE_SESSION`

### One-command internal release (macOS)

```powershell
.\tools\release-ios-internal.ps1 -GamePath "<GamePath>"
```

Bu zincir:
- `check-store-yaml`
- `build-ios` (Unity export + archive)
- `upload-testflight-internal`

### GitHub Actions workflow

Manual workflow:
- `.github/workflows/mobile-release.yml`
- Inputlar:
  - `game_path`
  - `platform` (`android|ios|both`)
  - `build_number` (opsiyonel)
  - `android_track`
  - `upload_metadata`

Runner gereksinimi:
- Android: `self-hosted`, `windows`, `minilab-unity`
- iOS: `self-hosted`, `macOS`, `minilab-unity`

### Full one-command orchestrator

```powershell
.\tools\deploy-platform.ps1 -GamePath "<GamePath>" -Platform both -UploadMetadata $true -RunAndroidSmokeTest $true -DispatchIosFromWindows $true
```

Windows host davranisi:
- Android internal release lokal calisir.
- iOS release lokal calisamazsa `gh` ile `mobile-release.yml` workflow dispatch eder.
- Deploy oncesi `platform-ready.ps1` otomatik calisir (aksi halde deploy durur).

Windows iOS dispatch gereksinimi:
- GitHub CLI (`gh`) kurulu olmali.
- `gh auth login` tamamlanmis olmali.

### Readiness check

```powershell
.\tools\platform-ready.ps1 -GamePath "<GamePath>" -Platform both
```

Bu kontrol:
- `store.yaml` schema
- upload/tooling readiness
- Android test device varligi
- CI release/smoke workflow varligi

### Android smoke workflow

- `.github/workflows/mobile-smoke-test.yml`
- manual input:
  - `game_path`
  - `duration_seconds`

## 3) PR Review checks

PR workflow:
- `tools/check-secrets.ps1`
- `tools/check-docs.ps1`
- `tools/check-template-purity.ps1`
- `tools/check-store-yaml.ps1` (Game_* varsa)
- `tools/check-tree.ps1`
- `tools/check-unity-compile.ps1`
- `tools/check-clean-tree.ps1`

## 4) Versioning standardi

- Semver:
  - `major`: buyuk kirilim
  - `minor`: yeni ozellik/icerik
  - `patch`: fix/tuning
- Build number:
  - platform bazli artan integer
  - onerilen format: `YYWWNN`

## 5) Artifact standardi

- Android:
  - `.aab`
  - `mapping.txt` (varsa)
  - `BuildArtifacts/unity-android-build.log`
  - release notes
- iOS:
  - Xcode archive/export log
  - `.ipa`
  - `BuildArtifacts/unity-ios-build.log`
  - release notes
