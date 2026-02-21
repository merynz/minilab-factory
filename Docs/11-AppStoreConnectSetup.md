# 11 - App Store Connect Setup

Each game is published as a separate iOS app record.

## Per-App Setup

1. Create app with unique bundle ID from `store.yaml`.
2. Set primary locale and add TR/EN localizations.
3. Configure app category and age rating.
4. Set support URL and privacy policy URL.

## Access and Roles

- Admin: account owner and release lead.
- App Manager: store ops + producer.
- Developer: CI/API integration owner.

## API Access for Automation

- Create App Store Connect API key for CI upload.
- Store key ID, issuer ID, and private key in secure CI secrets.
- Restrict access to required apps only.

## Listing Source of Truth

- `store.yaml` drives title/description/release notes.
- Changes must be reviewed in PR before store update.

