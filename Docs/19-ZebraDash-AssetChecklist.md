# ZebraDash Asset Checklist (Milestone-2)

Amaç: okunabilirlik + oynanabilirlik için zorunlu asset setini netleştirmek.

## 1) Teknik Standart
- Format: `PNG`, `RGBA`, şeffaf arkaplan
- Power-of-two önerisi: `256x256`, `512x512`, `1024x1024`
- Import:
  - `Sprite (2D and UI)`
  - UI/karakter/engel: `Wrap=Clamp`
  - Seamless background/tilesheet: `Wrap=Repeat`
  - `MipMap=Off`, `Filter=Bilinear`
- Pivot:
  - Player: `Center`
  - Obstacle: `Center`
  - UI button/panel: `Center`

## 2) Zorunlu UI Set (Space_Game_GUI_PNG)
- `Main_Menu_BG.png`
- `Main_Menu_Header.png`
- `Main_Menu_Start_BTN.png`
- `Main_Menu_Settings_BTN.png`
- `Main_Menu_Exit_BTN.png`
- `Level_Menu_Window.png`
- `Level_Menu_Table.png`
- `Level_Menu_Header.png`
- `Level_Menu_Play_BTN.png`
- `Pause_Window.png`
- `Pause_Table.png`
- `Pause_Header.png`
- `Pause_Menu_BTN.png`
- `Pause_Ok_BTN.png`
- `BTN_Play.png`, `BTN_Play_Active.png`
- `BTN_Pause.png`, `BTN_Pause_Active.png`
- `BTN_Menu.png`, `BTN_Menu_Active.png`
- `BTN_Replay.png`, `BTN_Replay_Active.png`
- `BTN_Settings.png`, `BTN_Settings_Active.png`
- `BTN_Ok.png`, `BTN_Close.png`

## 3) Player Set
- `Player_Custom.png` (idle/ana sprite)
- Opsiyonel:
  - `Player_Bank_Left.png`
  - `Player_Bank_Right.png`
  - `Player_Hit.png`

## 4) Obstacle Set (10 Archetype)
Her archetype için minimum 2 sprite: `idle` + `telegraph`.

- `obs_laneblock_idle.png`, `obs_laneblock_telegraph.png`
- `obs_accentcrusher_idle.png`, `obs_accentcrusher_telegraph.png`
- `obs_alternatorpair_idle.png`, `obs_alternatorpair_telegraph.png`
- `obs_crossgate_idle.png`, `obs_crossgate_telegraph.png`
- `obs_holdlanelock_idle.png`, `obs_holdlanelock_telegraph.png`
- `obs_holdreleasegate_idle.png`, `obs_holdreleasegate_telegraph.png`
- `obs_offbeatsnap_idle.png`, `obs_offbeatsnap_telegraph.png`
- `obs_fakedoutghost_idle.png`, `obs_fakedoutghost_telegraph.png`
- `obs_spinnersentinel_idle.png`, `obs_spinnersentinel_telegraph.png`
- `obs_risingwall_idle.png`, `obs_risingwall_telegraph.png`

## 5) Telegraph / FX Set
- `fx_hitline_tick.png`
- `fx_lane_band.png`
- `fx_hold_progress.png`
- `fx_release_tick.png`
- `fx_accent_ring.png`
- `fx_fakeout_ghost.png`
- `fx_pulse_soft.png`
- `fx_warning_stripe.png`

## 6) Environment Set (Space Maze)
- Corridor walls/ceiling/floor tiles (en az 24 varyant)
- Lane rail tiles (en az 6 varyant)
- Rib/column tiles (en az 8 varyant)
- Background seamless katman:
  - far stars
  - nebula
  - mid maze pattern
  - foreground dust

## 7) Readability Kriteri
- Obstacle silueti arkaplandan tek bakışta ayrılmalı
- Telegraph rengi obstacle ile çakışmamalı
- Hitline çevresi (safe zone) düşük gürültü kalmalı
- Aynı lane üstünde UI/FX/obstacle renkleri birbirini yutmamalı

## 8) Teslim Yapısı
Yeni gelen assetleri şu kaynak ağacına koy:
- `ZebraDashArtWork/Space_Game_GUI_PNG/...`
- `ZebraDashArtWork/Foozle_2DT0001_Science_Fiction_Labs_Tileset/...`
- `ZebraDashArtWork/Player/...` (varsa)
- `ZebraDashArtWork/CustomObstacles/...` (varsa)

