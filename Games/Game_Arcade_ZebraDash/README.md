# Game_Arcade_ZebraDash

Sample standalone game app directory.

## Setup

1. Create Unity project clone for this game.
2. Add dependency to `MiniLab.Core` in `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.zebratank.minilab.core": "file:../../Packages/MiniLab.Core"
  }
}
```

3. Copy `store.yaml` and fill game-specific values.
4. Run Android build with `tools/build-android.ps1`.

