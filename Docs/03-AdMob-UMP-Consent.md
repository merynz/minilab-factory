# 03 - AdMob UMP Consent (Android)

Policy kit standard for consent and ad gating.

## Launch Sequence (mandatory)

1. Call UMP consent info update on every app launch.
2. If consent form is required, load and present form.
3. Resolve consent state.
4. Only then initialize/load ads when allowed.
5. If UMP requires privacy options, keep a persistent in-app entry point.

## Gating Rules

- `AdsGate` stays closed until consent state is known.
- If consent denied, non-personalized or no-ad fallback behavior is used per config.
- Gameplay must continue even when consent network calls fail.

## QA Matrix

- EEA/UK simulated region with consent required.
- Non-EEA region with consent not required.
- User changes privacy options from settings screen.
- First run offline, then online resume.

## Implementation Notes

- Core exposes stubs in `MiniLab.Core.Policy`.
- Game app integrates concrete UMP SDK calls.
- Keep consent logs for QA only; avoid personal data in telemetry.

