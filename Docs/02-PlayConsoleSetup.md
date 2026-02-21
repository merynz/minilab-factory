# 02 - Play Console Setup

Each game is a separate app in Play Console.

## Initial Setup (per game app)

1. Create app entry with unique package name from `store.yaml`.
2. Assign team roles:
   - Release manager
   - QA
   - Store ops
3. Link AdMob app (if ads enabled).
4. Configure Internal Testing track users/groups.

## Service Accounts

- Create Google Cloud service account for CI upload.
- Grant minimum required Play Console permissions:
  - Release to testing tracks
  - View app information
- Store JSON key securely in CI secret manager.

## Listing Inputs

- Source of truth: game-local `store.yaml`.
- TR/EN short/long text must both be present before first upload.
- Screenshot/video assets follow shot list reference in `store.yaml`.

## Release Track Policy

- `internal` for every RC build.
- `closed` optional for larger QA pool.
- staged rollout for production (5% -> 20% -> 100%).

