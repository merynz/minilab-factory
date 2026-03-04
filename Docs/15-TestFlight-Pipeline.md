# 15 - TestFlight Pipeline

## Official Strategy

Resmi yol: **Seçenek B - GitHub Actions macOS runner**.

Neden:
- Windows tabanli ekipte merkezi iOS release.
- PR/calisma gecmisi ve release audit izi net.
- Secrets GitHub Actions secrets ile yonetilebilir.

## Strategy Matrix (A/B/C)

### A) Dedicated Mac mini (lokal release host)

- Sertifika/provisioning:
  - Apple Distribution cert + provisioning profile Mac keychain'e kurulur.
- App Store Connect API key:
  - key id / issuer id / private key secure local vault'tan okunur (repo'ya girmez).
- TestFlight upload:
  - `bundle exec fastlane ios internal`

### B) GitHub Actions macOS runner (resmi)

- Sertifika/provisioning:
  - CI runtime'da import edilir (base64 p12 + profile secret).
- App Store Connect API key:
  - `APP_STORE_CONNECT_API_KEY_ID`
  - `APP_STORE_CONNECT_ISSUER_ID`
  - `APP_STORE_CONNECT_API_KEY_CONTENT`
- TestFlight upload:
  - macOS workflow icinde `build-ios.ps1` + fastlane lane.
  - workflow: `.github/workflows/mobile-release.yml` (`platform=ios` veya `both`).

### C) Unity Cloud Build

- Sertifika/provisioning:
  - Unity Cloud Build signing panelinden yönetilir.
- App Store Connect API key:
  - UCB / downstream CI secrets olarak saklanir.
- TestFlight upload:
  - UCB export + fastlane upload adimi.

## Internal vs External Testers

- Internal:
  - TestFlight App Review gerekmez.
  - Hedef: smoke + regression + quality gate.
- External:
  - TestFlight App Review gerekir.
  - Privacy/compliance verileri release oncesi tamamlanmis olmali.

## Standard Flow

1. Unity iOS export ve archive olustur.
2. IPA TestFlight internal'a yuklenir.
3. Internal QA quality gate raporu verir.
4. Gerekirse external teste promote edilir.
5. Go/No-Go ile production release karari alinir.

## Secret Management Rule

- Secrets sadece CI/local secure store'da tutulur.
- Repo icine `.p12`, `.mobileprovision`, `.env`, API key commit edilmez.
- Secret rotasyonu: ekip degisikligi + incident sonrasi zorunlu.
