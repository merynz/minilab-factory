# 12 - iOS Signing and Provisioning

## Certificates and Profiles

- Use Apple Distribution certificate for release builds.
- Create app-specific provisioning profile per bundle ID.
- Keep profiles synchronized with CI keychain.

## Xcode Export Baseline

- Automatic signing in local dev is acceptable.
- CI should use deterministic export options:
  - method: `app-store`
  - signing style: automatic or manual (team standard)
  - team ID fixed in pipeline variables

## Secret Management

- Store p12/password and App Store Connect API key in CI secrets.
- Never commit signing material in repo.
- Rotate certificates on staff/offboarding changes.

## Failure Playbook

- Invalid profile:
  - regenerate profile and re-run export.
- Expired cert:
  - create new distribution cert and update CI keychain.
- Missing entitlements:
  - align capabilities in both Xcode project and Apple portal.

