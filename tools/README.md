# Tools

Automation helpers for bootstrap, build, upload, and versioning.

## Scripts

- `new-game.ps1`: create a game folder from template and copy `store.yaml`.
- `bootstrap-template-projects.ps1`: generate real Unity template projects under `Templates/*Template/UnityProject`.
- `doctor.ps1`: prerequisite diagnostics (Unity/modules/tooling/env) with PASS/FAIL/SKIP report.
- `setup-release-tooling.ps1`: install/verify release tooling (`gh`, Ruby, bundler, fastlane, `bundle install`).
- `configure-release-secrets.ps1`: set Play/App Store credentials as env vars (process or user scope).
- `analyze-audio.ps1`: deterministic WAV analysis, catalog generation, and beatmap JSON export.
- `dev-build-install.ps1`: one-command local chain (analyze -> APK build -> install -> launch).
- `check-secrets.ps1`: fail if forbidden secret files/patterns are found.
- `check-docs.ps1`: verify required docs set exists.
- `check-store-yaml.ps1`: validate release-blocker fields in `store.yaml`.
- `platform-ready.ps1`: check deploy + test environment readiness for Android/iOS.
- `check-template-purity.ps1`: fail if forbidden Sirius* folders exist under template/game Unity assets.
- `check-tree.ps1`: generate repo tree summary at `Docs/00-Tree.txt`.
- `check-unity-compile.ps1`: run Unity Android compile check (game project or temp project) with timeout + deterministic logs.
- `export-store-metadata.ps1`: export Play metadata + TestFlight changelog from `store.yaml`.
- `build-android.ps1`: Unity batch Android AAB build.
- `upload-android-internal.ps1`: upload AAB to Google Play Internal Testing (optional metadata upload).
- `build-ios.ps1`: Unity batch iOS export + archive command hook.
- `upload-testflight-internal.ps1`: upload IPA to TestFlight internal testers (optional changelog from `store.yaml`).
- `smoke-android.ps1`: automatic Android 5-minute monkey smoke with crash/ANR log scan.
- `release-android-internal.ps1`: one-command Android internal release chain (validate -> compile -> build -> upload).
- `release-ios-internal.ps1`: one-command iOS internal release chain (validate -> export/archive -> TestFlight upload).
- `deploy-platform.ps1`: one-command platform deploy orchestrator (Android release + smoke + iOS release/dispatch).
- `version-bump.ps1`: semver + build number utility.
