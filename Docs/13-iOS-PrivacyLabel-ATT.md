# 13 - iOS Privacy Label and ATT

## App Privacy Details (Privacy Nutrition Label)

App Store Connect requires App Privacy Details declarations, including third-party SDK/partner data practices.

## Workflow

1. Merge core SDK inventory + game delta.
2. Map collected data types and usage purposes.
3. Update App Privacy Details in App Store Connect.
4. Re-check after SDK or feature changes.

Note: privacy detail updates may be possible in App Store Connect without shipping a new binary, depending on change scope.

## ATT (App Tracking Transparency)

If the app performs tracking as defined by Apple policy:

- show ATT prompt via iOS ATT API before tracking starts
- do not treat silence as consent
- gate tracking-dependent SDK features until authorization result

If tracking is disabled by product decision, ATT prompt should not be shown.

## Operational Rule

- ATT decision path must be testable in internal QA builds.
- Privacy label updates are a release-blocker item.

