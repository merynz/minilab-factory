# 06 - Two Week Cadence

Target: one new standalone game app every 2 weeks.

## Cadence Plan (10 work days)

1. Day 1:
   - Select genre template.
   - Bootstrap game folder and `store.yaml`.
   - Define KPI targets for first 48h.
2. Day 2-3:
   - Core gameplay loop implementation.
   - Integrate `MiniLab.Core` boot/policy/telemetry hooks.
3. Day 4:
   - Add monetization entry points and remote config keys.
   - Draft store listing text.
4. Day 5:
   - Internal playable milestone.
   - First smoke test pass.
5. Day 6-7:
   - Balance + performance pass (low-end Android focus).
   - Compliance pass (Data Safety, Privacy Label, ATT, manifest).
6. Day 8:
   - Release candidate build.
   - CI/CD artifact validation.
7. Day 9:
   - Internal testing feedback fixes.
   - Release notes finalize.
8. Day 10:
   - Upload and rollout prep.
   - Post-launch dashboard ready.

## Non-Negotiables

- No code copy from previous game projects. Shared logic goes to `MiniLab.Core`.
- Every new SDK is logged in `store.yaml` delta before merge.
- Every release candidate must pass `Docs/09-QualityGates-And-DeviceTargets.md`.

