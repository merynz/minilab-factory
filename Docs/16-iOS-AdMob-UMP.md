# 16 - iOS AdMob UMP

## Setup Notes

- iOS UMP message configuration is managed in AdMob Privacy and messaging.
- App must use correct AdMob App ID and linked iOS bundle ID.
- Consent message types should match target regions and policy needs.

## Runtime Flow

1. Update consent info at each app launch.
2. Present consent form if required.
3. If required by UMP, expose in-app privacy options entry point.
4. Initialize ad requests only after consent gate allows.

## QA Cases

- New install in consent-required region.
- Returning user with prior consent choice.
- User changes privacy options from settings screen.
- No-network launch fallback behavior.

## Operational Rule

- UMP message content/version changes must be logged in release notes.
- Any ad personalization change requires Data Safety and Privacy Label re-check.

