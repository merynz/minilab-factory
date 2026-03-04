# 17 - Store Ops `store.yaml` Standard

Each game folder must include exactly one metadata source file:

- `Games/<GameName>/store.yaml`

## Required Fields

- `bundle_id_ios`
- `application_id_android`
- `locales` (TR/EN minimum)
- `category`
- `tags`
- `privacy_policy_url`
- `support_email`
- `sdk_inventory.core`
- `sdk_inventory.delta`
- `release_notes_template`
- `shots_ref`

## Rules

- No store text hardcoded in CI scripts.
- Console listing updates must trace back to `store.yaml`.
- SDK delta must be empty (`none`) or explicit.
- Missing required field is release-blocker.

## Validation Command (example)

```powershell
yq e '.bundle_id_ios and .application_id_android and .locales."tr-TR" and .locales."en-US"' Games/<GameName>/store.yaml
```
