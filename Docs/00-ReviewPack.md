# 00 - Review Pack

- Repo: https://github.com/merynz/minilab-factory
- PR: `https://github.com/merynz/minilab-factory/pull/new/milestone-2-juice-polish`
- Commit: `9e6a8d5`
- Unity detected version: `6000.2.6f2 (C:\Program Files\Unity\Hub\Editor\6000.2.6f2\Editor\Unity.exe)`

## PASS / FAIL / SKIP

- Doctor: PASS (5 PASS / 0 FAIL / 3 SKIP)
- Unity compile: PASS
- Analyze audio (2 WAV): PASS
- Deploy script (`tools/deploy-zebradash-dev.cmd`): PASS
- OneClick dev build/install: PASS
- Android APK build (`BuildArtifacts/zebradash-dev.apk`): PASS
- Android APK install/update (`adb install -r`): PASS
- Android launch (`adb shell monkey`): PASS
- Android AAB build (`BuildArtifacts/Android/zebradash.aab`): PASS
- Android upload internal: SKIP (Ruby/Bundler/Fastlane + Play secrets missing)
- iOS build: SKIP (Windows host, Mac/signing required)
- iOS TestFlight upload: SKIP (Windows host, Mac/signing required)

## Milestone-2 Output

- BeatClock DSP otorite: `PlayScheduled` + pause/resume/restart deterministic.
- BeatMap event standardi: `Tap`, `Accent`, `Long`, `RestSection` (+ backward compat `Hold/Gap`).
- Spawner: `spawnTime = hitTime - travelTime` + time-based kinematics.
- Gameplay: 2 lane (`tap = lane switch`), deterministic collision penceresi, combo/judgement HUD.
- Flow scenes: `Boot`, `MainMenu`, `LevelSelect`, `Gameplay`, `Results`, `BeatmapWorkbench`.
- UI: prefabsiz, runtime code-driven `UnityEngine.UI`.
- One-click scripts: `OneClick-DevBuildInstall.cmd`, `OneClick-PushBranch.cmd`.

## Reproduce Komutlari

```powershell
pwsh tools/doctor.ps1
pwsh tools/check-unity-compile.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject"
pwsh tools/analyze-audio.ps1 -GamePath "Games/Game_Arcade_ZebraDash"
pwsh tools/dev-build-install.ps1
pwsh tools/build-android.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject" -OutputName "zebradash.aab"
```

## One-Click

- `OneClick-DevBuildInstall.cmd`: analyze + sync + APK build + install + app launch.
- `OneClick-PushBranch.cmd`: branch kontrol + add/commit/push (`milestone-2-polish`).

## Log Pathleri

- `BuildArtifacts/unity-compile.log`
- `BuildArtifacts/unity-android-apk-build.log`
- `BuildArtifacts/unity-android-build.log`
- `BuildArtifacts/zebradash-dev.apk`
- `BuildArtifacts/Android/zebradash.aab`

## Uretilen Level Dosyalari

- `Games/Game_Arcade_ZebraDash/Content/Levels/music_catalog.json`
- `Games/Game_Arcade_ZebraDash/Content/Levels/level01_electro.json`
- `Games/Game_Arcade_ZebraDash/Content/Levels/level02_robo.json`

## Blokajlar

- iOS archive/upload adimlari macOS + signing materyali olmadan calismaz.
- Android internal upload icin release host'ta Ruby/Bundler/Fastlane + Play service json gereklidir.
