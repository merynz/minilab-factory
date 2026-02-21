# 14 - iOS Privacy Manifest and Required Reason APIs

## Policy Baseline

For submissions to App Store Connect from 12 February 2025 onward, applicable apps/SDKs must include valid privacy manifest declarations.

## Required Controls

- Include `PrivacyInfo.xcprivacy` in app target or embedded SDK targets.
- Ensure third-party SDK manifests are present and compatible.
- If app/SDK uses listed required-reason APIs, provide approved reasons as required by Apple.

## Project Checklist

1. Inventory APIs used directly and via SDKs.
2. Mark APIs that are in Apple required-reason list.
3. Add approved reason mappings to manifest declarations.
4. Validate in archive and App Store Connect upload.

## Sample Skeleton (`PrivacyInfo.xcprivacy`)

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>NSPrivacyTracking</key>
  <false/>
  <key>NSPrivacyTrackingDomains</key>
  <array/>
  <key>NSPrivacyAccessedAPITypes</key>
  <array>
    <!-- Add required-reason APIs and approved reason codes here -->
  </array>
</dict>
</plist>
```

## Release Rule

Missing or invalid manifest/reason declarations is a hard stop for iOS release.

