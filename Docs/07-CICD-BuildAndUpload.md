# 07 - CI/CD Build and Upload

Goal: "tek tus" calisir pipeline. Unity GUI acmadan, deterministik log ve timeout ile.

## 0) Doctor (On Kosul)

```powershell
.\tools\doctor.ps1
```

Kontrol edilenler:
- Unity Editor (beklenen `6000.2.6f2`)
- Unity Android Build Support + SDK/NDK/OpenJDK
- `adb` ve `java`
- Ruby/Bundler/Fastlane (upload icin)
- Play/TestFlight env hazirligi

`doctor` cikti formati: `PASS / FAIL / SKIP`.

## 1) Android Pipeline (Windows baseline)

### Beatmap uretimi (iki demo track)

```powershell
.\tools\analyze-audio.ps1 -GamePath "Games/Game_Arcade_ZebraDash" -CopyFromZip
```

Uretilen dosyalar:
- `Games/Game_Arcade_ZebraDash/Content/Levels/music_catalog.json`
- `Games/Game_Arcade_ZebraDash/Content/Levels/level01_electro.json`
- `Games/Game_Arcade_ZebraDash/Content/Levels/level02_robo.json`

Not:
- Local audio klasoru: `Games/Game_Arcade_ZebraDash/AudioLocal/` (gitignored)
- Audio yoksa workbench metronom click ile devam eder.

### Compile check (hang-proof)

```powershell
.\tools\check-unity-compile.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject"
```

### AAB build

```powershell
.\tools\build-android.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject" -OutputName "zebradash-review.aab"
```

Deterministik output/log:
- AAB: `BuildArtifacts/Android/zebradash-review.aab`
- Unity log: `BuildArtifacts/unity-android-build.log`

### Internal upload (opsiyonel)

```powershell
.\tools\upload-android-internal.ps1 -AabPath "BuildArtifacts/Android/zebradash-review.aab" -GamePath "Games/Game_Arcade_ZebraDash" -SkipIfSecretsMissing
```

Secret standardi:
- Tercih edilen: `MINILAB_PLAY_JSON`
- Geriye donuk destek: `GOOGLE_PLAY_JSON_KEY_PATH`

Eksik tooling/secret varsa upload script `SKIP` doner (exit 0), nedeni acik yazar.

## 2) iOS Pipeline (Resmi Yol + Alternatifler)

### Resmi yol (secilen): Seçenek B - GitHub Actions macOS runner

Gerekce:
- Windows agirlikli ekipte merkezi release otomasyonu
- PR/branch tabanli izlenebilirlik
- fastlane + App Store Connect API key ile sirket ici standardizasyon

Windows host'ta iOS scripts:
- `tools/build-ios.ps1 -SkipIfNoMac` -> `SKIP`
- `tools/upload-testflight-internal.ps1 -SkipIfNoMac` -> `SKIP`

Bu beklenen davranistir; gercek build/upload macOS runner'da kosar.

### Seçenek A - Dedicated Mac mini

- Mac'te Unity + Xcode + fastlane kurulumu
- Signing materyalleri local keychain'de
- `tools/build-ios.ps1` + `tools/upload-testflight-internal.ps1` lokal calisir

### Seçenek C - Unity Cloud Build

- Unity Cloud Build ile iOS archive
- Sonrasi TestFlight upload fastlane/ASC API ile
- Signing/provisioning ve secrets platforma tasinir

### iOS secrets (B/A/C ortak)

- `APP_STORE_CONNECT_API_KEY_ID`
- `APP_STORE_CONNECT_ISSUER_ID`
- `APP_STORE_CONNECT_API_KEY_CONTENT`
- (opsiyonel fallback) `FASTLANE_SESSION`

## 3) PR Review checks

PR workflow:
- `tools/check-secrets.ps1`
- `tools/check-docs.ps1`
- `tools/check-template-purity.ps1`
- `tools/check-tree.ps1`
- `tools/check-unity-compile.ps1`
- iOS dry-run (macOS yoksa bilincli SKIP)

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
