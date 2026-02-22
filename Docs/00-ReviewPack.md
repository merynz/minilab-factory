# 00 - Review Pack

- Repo: https://github.com/merynz/minilab-factory
- PR: `https://github.com/merynz/minilab-factory/pull/2`
- Commit: `see latest PR head commit`
- Unity detected version: `6000.2.6f2 (C:\Program Files\Unity\Hub\Editor\6000.2.6f2\Editor\Unity.exe)`

## PASS / FAIL / SKIP

- Doctor: PASS (5 PASS / 0 FAIL / 3 SKIP)
- Unity compile (x2 back-to-back): PASS
- Clean tree after compile x2: PASS (no new unstaged write)
- Analyze audio (2 WAV): PASS
- Android APK build (`BuildArtifacts/zebradash-dev.apk`): PASS
- Android AAB build (`BuildArtifacts/Android/zebradash-review.aab`): PASS
- Android upload internal: SKIP (Ruby/Bundler/Fastlane + Play secrets missing)
- iOS build: SKIP (Windows host, Mac/signing required)
- iOS TestFlight upload: SKIP (Windows host, Mac/signing required)

## Root Cause (Fail varsa)

- Bu kosuda FAIL yok.
- `GraphicsSettings.asset` icin `git diff` icerik farki cikmadi; hash HEAD ile ayni.
- Unity compile sonrasi ayni dosyada yeni rewrite gozlenmedi.

## Reproduce Komutlari

```powershell
pwsh tools/doctor.ps1
pwsh tools/analyze-audio.ps1 -GamePath "Games/Game_Arcade_ZebraDash"
pwsh tools/check-unity-compile.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject"
pwsh tools/check-unity-compile.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject"
pwsh tools/build-android-apk.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject" -OutputName "zebradash-dev.apk"
pwsh tools/build-android.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject" -OutputName "zebradash-review.aab"
pwsh tools/upload-android-internal.ps1 -AabPath "BuildArtifacts/Android/zebradash-review.aab" -GamePath "Games/Game_Arcade_ZebraDash" -SkipIfSecretsMissing
```

## Log Pathleri

- `BuildArtifacts/unity-compile.log`
- `BuildArtifacts/unity-android-apk-build.log`
- `BuildArtifacts/unity-android-build.log`
- `BuildArtifacts/unity-ios-build.log`

## Uretilen Level Dosyalari

- `Games/Game_Arcade_ZebraDash/Content/Levels/music_catalog.json`
- `Games/Game_Arcade_ZebraDash/Content/Levels/level01_electro.json`
- `Games/Game_Arcade_ZebraDash/Content/Levels/level02_robo.json`

## Milestone-1 Kanitlari

- BeatClock DSP anchor + `PlayScheduled` tek otorite.
- Hit-time anchored spawn (`spawnTime = hitTime - travelTime`) aktif.
- Workbench overlay: `dspNow`, `dspStart`, `songTime`, `offsetMs`, `nextBeatDelta`, judge/combo.
- Tap->Sync + slider offset track bazli `PlayerPrefs` ile kalici.
- Playable loop: countdown -> play -> fail/restart -> complete/next/exit.

## Blokajlar

- iOS archive/upload adimlari macOS + signing materyali olmadan calismaz.
- Android internal upload icin release host'ta Ruby/Bundler/Fastlane + Play service json gereklidir.

## Sir Icin Karar Sorulari (max 2)

1. iOS resmi yolunu hangi modelde kilitleyelim: Dedicated Mac mini, GitHub Actions macOS runner, yoksa Unity Cloud Build?
2. Android internal upload zorunlu noktasi lokal release host mu yoksa sadece CI mi olacak?
