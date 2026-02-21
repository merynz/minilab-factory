# 04 - Data Safety and SDK Inventory

Objective: declare data collection/sharing correctly, including third-party SDK behavior.

## Inventory Model

- Core inventory: maintained once in `MiniLab.Core` baseline.
- Game delta inventory: stored per game in `store.yaml -> sdk_inventory.delta`.
- Release cannot proceed if delta inventory is missing for new SDK.

## Data Safety Workflow (Android)

1. Build SDK inventory list (core + delta).
2. Map each SDK to data types collected/shared.
3. Fill Play Console Data Safety form from this merged map.
4. Re-validate after every SDK version bump.

## Required Metadata per SDK

- SDK name + version
- Data types touched (diagnostics, device IDs, purchase info, etc.)
- Collection vs sharing status
- Encryption in transit yes/no
- Deletion request path if applicable

## Ownership

- Engineering owns technical inventory accuracy.
- Store ops owns console form updates.
- Release manager signs off before production rollout.

