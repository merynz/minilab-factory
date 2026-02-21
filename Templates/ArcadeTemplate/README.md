# Arcade Template

One-button or runner style skeleton.

## Core Loop

1. Tap to start.
2. Survive / collect score.
3. Fail and restart quickly.

## Minimum Scenes

- `Boot`
- `MainMenu`
- `Run`

## Required Integrations

- `MiniLabBootstrap` in `Boot` scene.
- `DebugMenuController` enabled for dev builds.
- Event hooks: `run_start`, `run_end`, `fail_reason`.

## 5-Minute Smoke Test

1. First launch reaches playable scene in under 15s.
2. 3 full runs can be completed without crash.
3. Airplane mode run still allows gameplay loop.
4. Consent flow does not hard-block gameplay.

