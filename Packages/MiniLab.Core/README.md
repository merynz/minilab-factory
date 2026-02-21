# MiniLab.Core

Shared package for all MiniLab game apps.

## Modules

- Boot
- Save
- Audio/Haptics
- Localization scaffold
- Policy kit (consent + ads gate + privacy options bridge)
- Telemetry wrapper
- Remote config defaults
- Debug menu
- Store ops helpers

## Integration

In each game Unity project `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.zebratank.minilab.core": "file:../../Packages/MiniLab.Core"
  }
}
```

