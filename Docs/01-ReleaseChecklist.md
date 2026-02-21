# 01 - Release Checklist

Use this checklist for every game app release.

## A) Scope Lock (Day 7 latest)

- Feature freeze confirmed.
- SDK delta list finalized (`store.yaml -> sdk_inventory.delta`).
- Remote config defaults frozen for release candidate.

## B) Compliance Lock (Day 8 latest)

- UMP launch flow validated on Android + iOS.
- Privacy options entry point reachable in settings/help screen.
- Data Safety and App Privacy deltas reviewed against SDK inventory.
- ATT logic reviewed: prompt only if tracking is enabled.
- iOS privacy manifest + required reason APIs validated.

## C) Build + Artifact

- Android:
  - AAB produced.
  - `mapping.txt` produced.
  - Release notes generated.
- iOS:
  - Xcode archive created.
  - Export logs saved.
  - IPA produced for TestFlight.

## D) Store Ops

- `store.yaml` completed for TR/EN.
- Screenshot and video shot list delivered.
- Support email + privacy policy URL verified.
- Category/tags checked for each store.

## E) Quality Gate Pass

- 5-minute smoke test passed for selected template.
- Crash-free target met in internal testing.
- First-run time target met.
- Offline behavior checked.

## F) Rollout

- Android uploaded to Internal Testing then Production staged rollout.
- iOS uploaded to TestFlight internal.
- External testers started only after smoke pass + release notes ready.

