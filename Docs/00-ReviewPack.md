# 00 - Review Pack

- Repo: https://github.com/merynz/minilab-factory
- PR: https://github.com/merynz/minilab-factory/pull/1
- Commit: `see latest PR head commit`
- Unity detected version: `6000.2.6f2 (C:\Program Files\Unity\Hub\Editor\6000.2.6f2\Editor\Unity.exe)`

## PASS / FAIL / SKIP

- Template purity (no SiriusGameMaker): PASS
- Secrets scan: PASS
- Docs checklist: PASS
- Doctor: PASS (5 PASS / 0 FAIL / 3 SKIP)
- Unity compile: PASS
- Android AAB build: PASS
- Android upload internal: SKIP (Ruby/Bundler/Fastlane + Play secrets missing)
- iOS build: SKIP (Windows host, Mac/signing required)
- iOS TestFlight upload: SKIP (Windows host, Mac/signing required)

## Root Cause (Fail varsa)

- Bu kosuda FAIL yok.

## Reproduce Komutlari

```powershell
pwsh tools/doctor.ps1
pwsh tools/check-secrets.ps1
pwsh tools/check-docs.ps1
pwsh tools/check-template-purity.ps1
pwsh tools/check-unity-compile.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject"
pwsh tools/analyze-audio.ps1 -GamePath "Games/Game_Arcade_ZebraDash"
pwsh tools/build-android.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject" -OutputName "zebradash.aab"
pwsh tools/upload-android-internal.ps1 -AabPath "BuildArtifacts/Android/zebradash.aab" -GamePath "Games/Game_Arcade_ZebraDash" -SkipIfSecretsMissing
pwsh tools/build-ios.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject" -SkipIfNoMac
pwsh tools/upload-testflight-internal.ps1 -IpaPath "BuildArtifacts/iOS/app-store.ipa" -SkipIfNoMac -SkipIfSecretsMissing
```

## Log Pathleri

- `BuildArtifacts/unity-compile.log`
- `BuildArtifacts/unity-android-build.log`
- `BuildArtifacts/unity-ios-build.log`

## Uretilen Level Dosyalari

- `Games/Game_Arcade_ZebraDash/Content/Levels/music_catalog.json`
- `Games/Game_Arcade_ZebraDash/Content/Levels/level01_electro.json`
- `Games/Game_Arcade_ZebraDash/Content/Levels/level02_robo.json`

## Blokajlar

- iOS archive/upload adimlari macOS + signing materyali olmadan calismaz.
- Android internal upload icin release host'ta Ruby/Bundler/Fastlane + Play service json gereklidir.

## Sir Icin Karar Sorulari (max 2)

1. iOS resmi yolunu hangi modelde kilitleyelim: Dedicated Mac mini, GitHub Actions macOS runner, yoksa Unity Cloud Build?
2. Android internal upload zorunlu noktasi lokal release host mu yoksa sadece CI mi olacak?
