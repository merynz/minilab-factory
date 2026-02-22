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
2. `pwsh tools/bootstrap-template-projects.ps1`
3. `pwsh tools/new-game.ps1 -Template arcade -Codename ZebraDash`
4. `pwsh tools/analyze-audio.ps1 -GamePath "Games/Game_Arcade_ZebraDash" -CopyFromZip`
5. `pwsh tools/check-unity-compile.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject"`
6. `pwsh tools/build-android-apk.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject" -OutputName "zebradash-dev.apk"`
7. `pwsh tools/build-android.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject" -OutputName "zebradash.aab"`

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
  - `.\tools\check-tree.ps1`
  - `.\tools\check-unity-compile.ps1 -ProjectPath "Games/Game_Arcade_ZebraDash/UnityProject"`
  - `.\tools\check-clean-tree.ps1`
