# 00 - Review Pack

Use this file as the current PR review summary.

## Header

- Repo: `<repo-url>`
- PR: `<pr-url>`
- Commit: `<commit-sha>`
- Unity version: `<version>`

## PASS / FAIL / SKIP

- Doctor: `<PASS|FAIL|SKIP>`
- Unity compile: `<PASS|FAIL|SKIP>`
- Analyze audio: `<PASS|FAIL|SKIP>`
- Android APK build: `<PASS|FAIL|SKIP>`
- Android AAB build: `<PASS|FAIL|SKIP>`
- Android upload internal: `<PASS|FAIL|SKIP>`
- iOS build: `<PASS|FAIL|SKIP>`
- iOS TestFlight upload: `<PASS|FAIL|SKIP>`

## Reproduce Commands

```powershell
pwsh tools/doctor.ps1
pwsh tools/check-unity-compile.ps1 -ProjectPath "Games/<GameName>/UnityProject"
pwsh tools/analyze-audio.ps1 -GamePath "Games/<GameName>"
pwsh tools/build-android-apk.ps1 -ProjectPath "Games/<GameName>/UnityProject" -OutputName "<codename>-dev.apk"
pwsh tools/build-android.ps1 -ProjectPath "Games/<GameName>/UnityProject" -OutputName "<codename>.aab"
```

## Artifacts

- `BuildArtifacts/unity-compile.log`
- `BuildArtifacts/unity-android-apk-build.log`
- `BuildArtifacts/unity-android-build.log`
- `BuildArtifacts/<codename>-dev.apk`
- `BuildArtifacts/Android/<codename>.aab`
