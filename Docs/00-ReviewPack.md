# 00 - Review Pack (Milestone 0 Stabilize + Bootstrap)

## Repo Yapisi Ozeti

- `Templates/*Template/UnityProject`: gercek Unity template skeleton (Assets/Packages/ProjectSettings + Bootstrap scene + URP 2D setup).
- `Games/Game_Arcade_ZebraDash/UnityProject`: build test edilen gercek oyun projesi.
- `Packages/MiniLab.Core`: ortak runtime + editor build entry (`BuildPipelineEntry`).
- `tools/`: check/build/upload/new-game/bootstrap otomasyonlari.
- `.github/workflows/`: PR checks (secrets, docs, purity, compile) + iOS dry-run.

## Milestone 0 Sonucu

- PASS:
  - Secrets scan (`tools/check-secrets.ps1`)
  - Docs checklist (`tools/check-docs.ps1`)
  - Template purity (`tools/check-template-purity.ps1`)
  - Unity compile check (`tools/check-unity-compile.ps1`)
  - Android AAB build (`tools/build-android.ps1`)
- SKIP:
  - Android internal upload: localde `bundle`/Play secrets yok.
  - iOS build + TestFlight upload: Windows ortaminda macOS/signing blokaji.

## Kalanlar / Eksikler

- CI secrets (Play service account, App Store Connect key) baglanmadi.
- iOS archive+upload gercek calisma icin macOS runner veya remote Mac secimi bekliyor.

## Riskler

- iOS tarafi macOS/signing olmadan binary adimina gecemez.
- Android upload tarafi `bundle exec fastlane` tooling + secret seti olmadan SKIP kalir.
- SDK envanteri degisirse Data Safety/Privacy Label tekrar guncellenmeli.

## Sir Icin Karar Sorulari

1. iOS icin resmi yol hangisi olsun: remote Mac, GitHub Actions macOS runner, yoksa Unity Cloud Build?
2. Android upload icin standart nerede calissin: lokal release makinesi mi, CI runner mi?
