# 09 - Quality Gates and Device Targets

Ship/No-Ship rules are objective and shared across templates.

## Device Targets

- Minimum Android profile:
  - 3 GB RAM
  - Android 10+
  - mid/low CPU class
- Minimum iOS profile:
  - iPhone 8 or newer equivalent performance baseline

## Gate Thresholds

- Crash-free session rate:
  - Internal test: >= 99.0%
  - First 48h production: >= 98.5%
- ANR rate (Android): <= 0.47%
- First run time:
  - Cold start to first interaction <= 15s on target low-end Android.
- FPS:
  - Average >= 30 FPS on min target scenes.

## Offline Behavior

- First-run with no network still starts gameplay loop.
- Consent update failures must fail-safe without hard lock.
- Remote config fallback defaults must load from local values.

## 5-Minute Smoke Tests

- Arcade:
  - 3 complete runs.
  - Fail/restart loop stable.
- Puzzle:
  - Tutorial + win + lose path.
  - Level progression persists.
- Defense:
  - 3 waves + one upgrade + one rewarded path.

