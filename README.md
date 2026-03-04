# MiniLab Factory v0.1

Monorepo scaffold for Zebra Tank Game Studio.

Goal: ship a new mobile mini game every 2 weeks with:
- shared `MiniLab.Core` package
- genre templates
- policy/compliance kit
- repeatable release ops for Android + iOS

## Repository Layout

- `Packages/MiniLab.Core/`: shared Unity UPM package.
- `Templates/`: reusable genre starter structures.
- `Games/`: standalone game app directories (one app per game).
- `Docs/`: single-source operational and compliance documentation.
- `tools/`: helper scripts for game bootstrap and build pipelines.

## Quick Start

1. `pwsh tools/doctor.ps1`
2. `pwsh tools/new-game.ps1 -Template arcade -Codename mygame`
3. `pwsh tools/check-unity-compile.ps1 -ProjectPath "Games/Game_Arcade_mygame/UnityProject"`
4. `pwsh tools/analyze-audio.ps1 -GamePath "Games/Game_Arcade_mygame"`
5. `pwsh tools/build-android-apk.ps1 -ProjectPath "Games/Game_Arcade_mygame/UnityProject" -OutputName "mygame-dev.apk"`
6. `pwsh tools/install-android.ps1 -ApkPath "BuildArtifacts/mygame-dev.apk"`
7. `OneClick-DevBuildInstall.cmd` (double-click, full dev pipeline)

## Internal Release

1. `pwsh tools/check-store-yaml.ps1 -GamePath "Games/Game_Arcade_mygame"`
2. Android internal upload:
   - `pwsh tools/release-android-internal.ps1 -GamePath "Games/Game_Arcade_mygame" -UploadMetadata`
   - or double-click `OneClick-ReleaseAndroidInternal.cmd`
3. iOS TestFlight internal upload (macOS runner/host required):
   - `pwsh tools/release-ios-internal.ps1 -GamePath "Games/Game_Arcade_mygame"`
   - or double-click `OneClick-ReleaseIOSInternal.cmd`
4. GitHub Actions manual release:
   - run `.github/workflows/mobile-release.yml` with `game_path` input.

## One-Click Platform Mode

1. (Once) Tooling bootstrap:
   - `pwsh tools/setup-release-tooling.ps1`
   - `gh auth login` (once, for Windows iOS workflow dispatch)
2. (Once) Secret env setup:
   - `pwsh tools/configure-release-secrets.ps1 -PlayJsonPath "<path-to-play-service-account.json>" -AscApiKeyId "<id>" -AscIssuerId "<issuer>" -AscApiKeyFile "<AuthKey_xxx.p8>"`
   - (optional) `-FastlaneSession "<session>"`
3. Readiness check:
   - `pwsh tools/platform-ready.ps1 -GamePath "Games/Game_Arcade_mygame" -Platform both`
4. Full one-click deploy:
   - `OneClick-DeployPlatform.cmd`
   - this runs readiness check + Android smoke + Android internal upload + iOS release (or dispatches iOS workflow from Windows).
5. Standalone Android smoke:
   - `OneClick-SmokeAndroid.cmd`
   - or `pwsh tools/smoke-android.ps1 -GamePath "Games/Game_Arcade_mygame"`

## How To Run / Structure

- `Packages/MiniLab.Core`: shared runtime/editor modules used by all game apps.
- `Templates/*Template`: reusable genre skeletons.
- `Games/Game_<Genre>_<Codename>`: standalone app directories, each with its own `store.yaml`.
- `Games/Game_<Genre>_<Codename>/AudioLocal`: local-only WAV workspace (gitignored).
- `Docs/*`: release/compliance/ops single source of truth.

## Review Workflow

- Every milestone uses a dedicated branch and PR title format:
  - `MiniLab Factory v0.1 - <milestone adı>`
- Every PR starts with a `REVIEW PACK` block (see `.github/pull_request_template.md`).
- Run local checks before pushing:
  - `.\tools\doctor.ps1`
  - `.\tools\check-secrets.ps1`
  - `.\tools\check-docs.ps1`
  - `.\tools\check-template-purity.ps1`
  - `.\tools\check-store-yaml.ps1 -GamePath "Games/<GameName>"`
  - `.\tools\check-tree.ps1`
  - `.\tools\check-unity-compile.ps1 -ProjectPath "Games/<GameName>/UnityProject"`
  - `.\tools\check-clean-tree.ps1`
