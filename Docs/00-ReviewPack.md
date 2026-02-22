# 00 - Review Pack

- Repo: https://github.com/merynz/minilab-factory
- PR: `https://github.com/merynz/minilab-factory/pull/new/milestone-2-juice-polish`
- Commit: `c03d3d7`
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

## Tek Tap = Beat Update

- Pattern jenerasyonu tek kaynak olarak netlestirildi: `BeatMap -> GameplayPattern -> Spawner/Judge`.
- Judge eventleri artik `Tap + Accent` hazard eventlerinden uretiliyor (bos tap = Miss).
- `AccentPulse` kaynaklari beatmap yerine pattern'den okunuyor (gorsel vurgu ile gameplay ayni kaynaktan geliyor).
- Obstacle presentation katmani ayrildi (`Straight/Diagonal/Drop/Pop`) ve hit-time invariant korundu.
- `Long -> HoldSlide` eventleri deterministic travel + endTime ile spawn ediliyor.
- Hazard kontrol loop'unda spawn-sira kaynakli erken `break` kaldirildi; hit-time bazli kontrol korunuyor.

## Harmony + Parallax/Hitline Stabilizasyonu (c03d3d7)

- `GameplayPatternGenerator` phrase planner kilitlendi:
  - 2 bar horizon
  - LanePlan + HazardSlots birlikte deterministic beam-search
  - fallback sirası: offbeat↓, switch↓, hazard↓
  - downbeat/phrase anchor (slot 0 / slot 8) skor agirligi
- `BuildJudgeEvents` sadece `Tap/Accent` kaynakli hazard eventlerini judge kuyruğuna aliyor (tap gerektirmeyen hazardlar judge'e girmiyor).
- `GameplayPattern.gridDebugBars` HUD'a baglandi (`Grid bX ... mask/lanePlan/k/switch/E/target/preset`) ve tuning gorunur hale geldi.
- `ObstacleKinematics` post-hit hareketi ramp'e alindi; hitline invariant korundu (ilk frame drift azaltildi).
- `ParallaxSystem` beat/bar pulse carpani guclendirildi ama DSP delta integrasyonlu stabil kayma korunuyor.
- `RenderMaterialUtils` sprite gorunurlugu icin `Sprites/Default` onceligi + `_MainTex/_BaseMap` white texture set edildi.

## Milestone-2 Geometry Dash Akis Update

- Player mechanic kesinlestirildi: `tap = lane switch` (jump/airborne yok).
- InputJudge BPM-scale window aktif:
  - `perfectMs = clamp(0.10 * beatMs, 30, 46)`
  - `goodMs = clamp(0.22 * beatMs, 68, 98)`
- PLL-lite run-local offset stabilizasyonu eklendi (sadece Perfect/Good update, Miss ignore).
- Empty tap kurali eklendi:
  - Rest/Transition: `ignore`
  - Active/Drop: yakinda hazard varsa `miss`
- Pattern generator strain-temelli hale getirildi:
  - `strain = 0.32*density + 0.24*switchFreq + 0.18*holdLoad + 0.16*offbeatRatio + 0.10*accentDensity`
  - Section target strain: Rest `0.10-0.20`, Active `0.35-0.60`, Drop `0.65-0.85`
  - 12 preset ID aktif: `ALT_1212_1BAR`, `ALT_1212_2BAR_ACCENT_END`, `STREAK3_BREAK`, `STREAK2_SYNCOPATED`, `HOLD_SHORT_RELEASE`, `HOLD_LONG_SAFE`, `DOUBLE_SWAP_PAIR`, `CROSS_GATE_BRIDGE`, `BUILD_RAMP_4BAR`, `DROP_DENSE_ACCENTED`, `REST_RESET_2TO4S`, `FAKEOUT_BRIDGE_TO_DROP`
- Archetype dili aktif (10 adet): `LaneBlock`, `AccentCrusher`, `AlternatorPair`, `StreakBreaker`, `HoldLaneLock`, `HoldReleaseGate`, `CrossGate`, `OffbeatSnap`, `FakeoutGhost`, `RestPulse`.
- Hold telegraph eklendi (kalan sureyi gosteren bar + release pulse).
- Hit-time drift assert/log eklendi (`ObstacleSpawner` + `ObstacleKinematics`).
- Debug overlay genislestirildi (F3 toggle):
  - DSP-time, beatMs, sessionPhaseMs
  - signed last tap offset
  - next 3 hazard preview (time/lane/archetype)
  - section state + strain/target + preset
  - empty-tap decision

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
