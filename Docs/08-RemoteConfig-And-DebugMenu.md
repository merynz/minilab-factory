# 08 - Remote Config and Debug Menu

Remote config must control core balancing and monetization knobs without app update.

## Required Remote Config Keys

- `interstitial_cooldown_seconds` (A/B: 60/90/120)
- `rewarded_daily_cap`
- `tutorial_skip_enabled`
- `difficulty_curve_variant`
- `debug_menu_enabled`

## A/B Variant Rules

- Keep control group at previous stable default.
- Variant rollout starts at <= 10% users.
- Stop experiment if crash-free drops or tutorial completion degrades.

## Debug Menu Minimum Features

- Force locale switch (`tr-TR`, `en-US`).
- Force consent states for QA.
- Show current remote config values.
- Trigger test events (`tutorial_step`, `run_end`, `ad_impression`).
- Open privacy options entry point.

## Safety Rules

- Debug menu only in dev/internal builds.
- No personal data in debug output.
- Include current config hash in bug reports.

