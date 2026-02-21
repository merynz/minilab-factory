# 00 - Review Pack

## Repo Yapisi Ozeti

- `Packages/MiniLab.Core`: tum oyunlarda ortak Unity UPM package (boot, policy kit, telemetry, remote config, debug, store ops yardimcilari).
- `Templates/ArcadeTemplate`, `Templates/PuzzleTemplate`, `Templates/DefenseTemplate`: tur bazli starter dokular + smoke test checklist.
- `Games/`: her oyunun ayri app dizini ve tek kaynak `store.yaml`.
- `Docs/`: release, compliance, CI/CD, quality gate ve store ops standartlari.
- `tools/` + `fastlane/`: build/upload ve release otomasyon girisleri.

## Deliverable Durumu

- Tamamlandi:
  - Monorepo iskeleti ve `MiniLab.Core` paket v0.1 scaffold.
  - 3 adet template iskeleti.
  - Multi-app store metadata standardi (`store.yaml` + template).
  - Android + iOS compliance dokumantasyonu (UMP, Data Safety, Privacy Label, ATT, Privacy Manifest, TestFlight).
  - CI/CD komut akislari ve fastlane lane iskeleti.
  - Quality gates, 2 haftalik cadence ve acceptance mapping dokumanlari.
- Eksik / sonraki sprint:
  - Unity tarafinda gercek SDK baglantilari (AdMob/UMP/ATT) tamamlanmasi.
  - CI ortamlarinda credential kurulumu ve pipeline dry-run.
  - Her template icin oynanabilir scene/prefab baseline’i.

## Riskler (Play / App Store / Privacy)

- Google Play Data Safety beyanlari SDK guncellemelerinde eski kalabilir; her SDK bump’ta envanter yeniden dogrulanmali.
- iOS Privacy Manifest + Required Reason API beyanlari 3rd-party SDK degisimlerinde kirilabilir; upload oncesi kontrol zorunlu.
- ATT akisi urun kararina gore netlesmezse iOS review red riski olusur.
- UMP consent/Privacy Options entry point eksik kalirsa reklam ve policy uyumsuzlugu riski var.

## Sir Karar Noktalari (1-2)

1. Reklam stratejisi: ilk fazda tracking kapali mi acik mi? (ATT prompt ve attribution akisini dogrudan etkiler.)
2. CI araci standardi: fastlane + Unity batch tek standart mi, yoksa Unity Cloud Build ile hibrit mi ilerleyecegiz?

