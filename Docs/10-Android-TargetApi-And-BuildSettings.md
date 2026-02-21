# 10 - Android Target API and Build Settings

## Policy Baseline

As of 31 August 2025, new apps and updates are expected to target Android 15 / API level 35+.

Template default for all new games:

- `targetSdkVersion = 35` or higher
- `compileSdkVersion = 35` or higher

## Unity Build Defaults

- Scripting backend: IL2CPP
- Architectures: ARM64 (required), ARMv7 optional only if needed
- Min SDK: project-specific, recommended Android 10+
- Gradle template enabled for deterministic CI builds

## Manifest/Permission Hygiene

- Keep only gameplay-required permissions.
- Remove unused ad/network/location permissions if SDK not present.
- Document every new permission in SDK inventory delta.

## Release Verification

- Confirm target API in built manifest/AAB.
- Verify Play Console pre-launch report for API compatibility.
- Track policy updates quarterly and update this file when needed.

