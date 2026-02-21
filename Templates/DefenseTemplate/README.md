# Defense Template

Tower defense / idle defense / upgrade loop skeleton.

## Core Loop

1. Place or upgrade unit.
2. Survive enemy wave.
3. Earn currency and progress.

## Minimum Scenes

- `Boot`
- `MainMenu`
- `DefenseBattle`

## Required Integrations

- `MiniLabBootstrap` in `Boot` scene.
- Event hooks: `run_start`, `run_end`, `session_end`, `rewarded_offer`, `rewarded_accept`.
- Remote config for spawn rate and upgrade costs.

## 5-Minute Smoke Test

1. First wave starts in under 20s.
2. Upgrade loop works across 3 waves.
3. Rewarded flow can be entered and exited safely.
4. Session end writes save and re-open restores state.

