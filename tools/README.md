# Tools

Automation helpers for bootstrap, build, upload, and versioning.

## Scripts

- `new-game.ps1`: create a game folder from template and copy `store.yaml`.
- `bootstrap-template-projects.ps1`: generate real Unity template projects under `Templates/*Template/UnityProject`.
- `doctor.ps1`: prerequisite diagnostics (Unity/modules/tooling/env) with PASS/FAIL/SKIP report.
- `analyze-audio.ps1`: deterministic WAV analysis, catalog generation, and beatmap JSON export.
- `check-secrets.ps1`: fail if forbidden secret files/patterns are found.
- `check-docs.ps1`: verify required docs set exists.
- `check-template-purity.ps1`: fail if forbidden Sirius* folders exist under template/game Unity assets.
- `check-tree.ps1`: generate repo tree summary at `Docs/00-Tree.txt`.
- `check-unity-compile.ps1`: run Unity Android compile check (game project or temp project) with timeout + deterministic logs.
- `build-android.ps1`: Unity batch Android AAB build.
- `upload-android-internal.ps1`: upload AAB to Google Play Internal Testing.
- `build-ios.ps1`: Unity batch iOS export + archive command hook.
- `upload-testflight-internal.ps1`: upload IPA to TestFlight internal testers.
- `version-bump.ps1`: semver + build number utility.
