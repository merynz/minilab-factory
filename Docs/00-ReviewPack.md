# 00 - Review Pack Standardi

Her PR aciklamasi su formati kullanir:

## Header

- Repo: `<repo-link>`
- PR: `<pr-link>`
- Commit: `<sha>`
- Unity detected version: `<version + path>`

## PASS / FAIL / SKIP Tablosu

- Template purity (no SiriusGameMaker):
- Secrets scan:
- Docs checklist:
- Doctor:
- Unity compile:
- Android AAB build:
- Android upload internal:
- iOS build:
- iOS TestFlight upload:

## Root Cause (Fail varsa)

- 1-3 satir teknik kok neden.

## Reproduce Komutlari

```powershell
.\tools\doctor.ps1
.\tools\check-secrets.ps1
.\tools\check-docs.ps1
.\tools\check-template-purity.ps1
.\tools\check-unity-compile.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject"
.\tools\analyze-audio.ps1 -GamePath "Games/Game_Arcade_ZebraDash" -CopyFromZip
.\tools\build-android.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject" -OutputName "zebradash-review.aab"
.\tools\upload-android-internal.ps1 -AabPath "BuildArtifacts/Android/zebradash-review.aab" -GamePath "Games/Game_Arcade_ZebraDash" -SkipIfSecretsMissing
.\tools\build-ios.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject" -SkipIfNoMac
.\tools\upload-testflight-internal.ps1 -IpaPath "BuildArtifacts/iOS/app-store.ipa" -SkipIfNoMac -SkipIfSecretsMissing
```

## Log Pathleri

- `BuildArtifacts/unity-compile.log`
- `BuildArtifacts/unity-android-build.log`
- `BuildArtifacts/unity-ios-build.log`

## Uretilen Level Dosyalari

- `Games/Game_Arcade_ZebraDash/Content/music_catalog.json`
- `Games/Game_Arcade_ZebraDash/Content/Levels/level01_electro.json`
- `Games/Game_Arcade_ZebraDash/Content/Levels/level02_robo.json`

## Risk / Blokajlar

- iOS macOS + signing zorunlulugu.
- Play/TestFlight secrets yoksa upload adimlari SKIP.

## Sir Icin Karar Sorulari (max 2)

1. iOS resmi yolu: Dedicated Mac mini / GitHub Actions macOS runner / Unity Cloud Build?
2. Android upload adimi lokal release host'ta mi, CI'da mi zorunlu olacak?
