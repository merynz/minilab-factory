# 15 - TestFlight Pipeline

## Internal Testers

- Build upload does not require TestFlight App Review.
- Use for rapid smoke and regression rounds.
- Minimum: release notes + known issues note per build.

## External Testers

- Build must pass TestFlight App Review before external distribution.
- Keep compliance metadata and export docs ready before submission.
- Reuse internal-tested build when possible to reduce delays.

## Standard Flow

1. CI uploads IPA to TestFlight internal.
2. Internal QA validates quality gates.
3. Promote to external group only after pass criteria.
4. Collect crash + retention signals.
5. Decide production readiness.

## Automation Inputs

- `MINILAB_IOS_IPA_PATH`
- `MINILAB_IOS_BUNDLE_ID`
- App Store Connect API credentials in CI secrets.

