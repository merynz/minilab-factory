# Puzzle Template

Grid and level-based mini game skeleton (match-like systems).

## Core Loop

1. Start level.
2. Solve board objective with limited moves/time.
3. Win/lose, reward, next level.

## Minimum Scenes

- `Boot`
- `MainMenu`
- `Level`

## Required Integrations

- `MiniLabBootstrap` in `Boot` scene.
- Event hooks: `tutorial_step`, `level_start`, `level_end`, `fail_reason`.
- Remote config keys for move limits and level timers.

## 5-Minute Smoke Test

1. Tutorial completes once without dead-end.
2. Win and lose states both reachable.
3. Next level load works after 3 consecutive levels.
4. Offline mode preserves local progress.

