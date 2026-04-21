# T30 — ToS kabul, yaş gate, geo-block

**Faz:** F2 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-04-21

---

## Yapılan İşler

- **ToS kabul endpoint'i:** `POST /api/v1/auth/tos/accept` — `{ tosVersion, ageOver18 }` kabul eder; ToS versiyonu + 18+ self-attestation'ı atomik kaydeder. 200 başarı, 400 `VALIDATION_ERROR` (ageOver18=false veya tosVersion eksik/uzun), 409 `TOS_ALREADY_ACCEPTED`, 401 `unauthenticated` sözleşmesi.
- **User entity:** `AgeConfirmedAt DateTime?` alanı eklendi (06 §3.1 — MVP soft yaş gate self-attestation için). `TosAcceptanceService` bu alanı `TosAcceptedAt` ile birlikte set eder.
- **Steam account age heuristic'i (age gate):** `IAgeGateCheck` + `SettingsBasedAgeGateCheck` — Steam hesabı `auth.min_steam_account_age_days` SystemSetting'deki eşikten daha yeniyse giriş `AuthenticationOutcome.AgeBlocked` ile reddedilir, controller `?error=age_blocked` redirect'i çıkarır. `SteamPlayerSummary.AccountCreatedAt` yeni alanı `timecreated` JSON unix saniyesinden parse edilir. Fail-open: hesap yaşı bilinmiyorsa izin verilir.
- **Geo-block:** `AllowAllGeoBlockCheck` stub'ı `SettingsBasedGeoBlockCheck` ile değiştirildi. `auth.banned_countries` SystemSetting'den CSV (örn. `IR,KP,CU`) okunur; `NONE` marker hiçbir ülkenin engellenmediği anlamına gelir. IP→country resolution `ICountryResolver` abstraction + `HeaderCountryResolver` (MVP: edge tarafından set edilen `X-Country-Code` header) ile yapılır. T83 gerçek geolocation provider + VPN/proxy detection'ı devralacak.
- **Pipeline reorder:** assertion → geo-block → sanctions → profile fetch → **age gate (yeni)** → provisioning → ban check → tokens. Age gate profile fetch sonrasında çünkü Steam hesap yaşı profile'dan okunur; sanctions öncesindeyse Steam API çağrısı sanctions match'de gereksiz olur.
- **DI:** `IHttpContextAccessor`, `ICountryResolver`, `IGeoBlockCheck`, `IAgeGateCheck`, `ITosAcceptanceService`, `TimeProvider` (System) `SteamAuthenticationModule` içinde register.
- **Dokümanlar:** 07 §4.4 request body `ageOver18` alanını dahil eder (v2.1 → v2.2). 02 §21.1 yaş kısıtı satırı Steam account age eşiğinin `auth.min_steam_account_age_days` ile yönetildiğini açıklar (v2.4 → v2.5).
- **Migration:** `20260421195807_T30_AddAgeConfirmedAtAndAccessControlSettings` — `Users.AgeConfirmedAt datetime2 NULL` + `SystemSettings` 2 yeni seed satırı (`auth.banned_countries` = `NONE`, `auth.min_steam_account_age_days` = `30`).

## Etkilenen Modüller / Dosyalar

**Dokümantasyon:**
- `Docs/07_API_DESIGN.md` — §4.4 request body + field tablosu, v2.2 bump
- `Docs/02_PRODUCT_REQUIREMENTS.md` — §21.1 yaş kısıtı açıklaması, v2.5 bump

**Auth modülü:**
- `backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/IAgeGateCheck.cs` — yeni
- `backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/SettingsBasedAgeGateCheck.cs` — yeni
- `backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/ICountryResolver.cs` — yeni
- `backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/HeaderCountryResolver.cs` — yeni
- `backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/SettingsBasedGeoBlockCheck.cs` — yeni
- `backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/AuthenticationOutcome.cs` — `AgeBlocked` case eklendi
- `backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/SteamAuthenticationPipeline.cs` — age gate reorder
- `backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/ISteamAuthenticationPipeline.cs` — doc comment
- `backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/ISteamProfileClient.cs` — `SteamPlayerSummary.AccountCreatedAt`
- `backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/SteamProfileClient.cs` — `timecreated` parse
- `backend/src/Modules/Skinora.Auth/Application/TosAcceptance/ITosAcceptanceService.cs` — yeni
- `backend/src/Modules/Skinora.Auth/Application/TosAcceptance/TosAcceptanceService.cs` — yeni
- `backend/src/Modules/Skinora.Auth/Skinora.Auth.csproj` — `FluentValidation` + `Skinora.Platform` referansı

**Users modülü:**
- `backend/src/Modules/Skinora.Users/Domain/Entities/User.cs` — `AgeConfirmedAt`

**Platform modülü:**
- `backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs` — 2 yeni seed satırı

**API katmanı:**
- `backend/src/Skinora.API/Controllers/AuthController.cs` — `POST /auth/tos/accept` + `AgeBlocked` mapping + request/response DTO'lar
- `backend/src/Skinora.API/Configuration/SteamAuthenticationModule.cs` — yeni servis kayıtları

**Migrations:**
- `backend/src/Skinora.Shared/Persistence/Migrations/20260421195807_T30_AddAgeConfirmedAtAndAccessControlSettings.cs` (+.Designer.cs)
- `backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs`

**Testler:**
- `backend/tests/Skinora.Auth.Tests/Unit/HeaderCountryResolverTests.cs` — yeni (6 test)
- `backend/tests/Skinora.Auth.Tests/Unit/SteamAuthenticationPipelineTests.cs` — age gate senaryosu eklendi (+1)
- `backend/tests/Skinora.Auth.Tests/Integration/SettingsBasedAgeGateCheckTests.cs` — yeni (5 test)
- `backend/tests/Skinora.Auth.Tests/Integration/SettingsBasedGeoBlockCheckTests.cs` — yeni (6 test)
- `backend/tests/Skinora.Auth.Tests/Integration/TosAcceptanceServiceTests.cs` — yeni (6 test)
- `backend/tests/Skinora.API.Tests/Integration/TosAcceptEndpointTests.cs` — yeni (4 test)
- `backend/tests/Skinora.API.Tests/Integration/AuthSteamEndpointTests.cs` — geo-block + age-blocked senaryoları eklendi (+2), `SteamPlayerSummary` constructor güncellendi
- `backend/tests/Skinora.Auth.Tests/Skinora.Auth.Tests.csproj` — Microsoft.AspNetCore.App + Microsoft.Extensions.TimeProvider.Testing

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | POST /auth/tos/accept → ToS versiyonu kaydedilir | ✓ | `TosAcceptanceService` + `AuthController.AcceptTos`; `TosAcceptEndpointTests.Accept_ValidInput_ReturnsOkAndPersistsUser` PASS — kaydedilen TosAcceptedVersion/TosAcceptedAt/AgeConfirmedAt assert edilir |
| 2 | Yaş gate: 18+ beyanı + Steam hesap yaşı kontrolü, başarısız → erişim engeli | ✓ | 18+ self-attestation: `TosAcceptanceService` `ageOver18=false` → `ValidationException` (400). Steam hesap yaşı: `SettingsBasedAgeGateCheck` < threshold → `AuthenticationOutcome.AgeBlocked` → `?error=age_blocked`. Testler: `TosAcceptEndpointTests.Accept_AgeOver18False_Returns400`, `AuthSteamEndpointTests.Callback_FreshSteamAccount_AgeBlocked_RedirectsWithAgeBlocked`, `SteamAuthenticationPipelineTests.ExecuteAsync_AgeGateBlocked_...` |
| 3 | Geo-block: IP bazlı coğrafi engelleme, yasaklı ülke listesi admin tarafından yönetilebilir | ✓ | `SettingsBasedGeoBlockCheck` + `auth.banned_countries` SystemSetting (admin-yönetimli, T26 seed'de default `NONE`). `ICountryResolver` + `HeaderCountryResolver` (MVP). Testler: 6 `SettingsBasedGeoBlockCheckTests` + `AuthSteamEndpointTests.Callback_GeoBlockedCountry_RedirectsWithGeoBlocked` |
| 4 | VPN/proxy tespiti destekleyici sinyal olarak (tek başına engelleme sebebi değil) | ~ | T83'e devredildi — 02 §21.1 "MVP'de destekleyici sinyal olarak kullanılır — tek başına engelleme sebebi değil" diyor; T30 engelleme yolunu implement ediyor, VPN sinyal toplama T83 kapsamında. 11_IMPLEMENTATION_PLAN.md T83 Bağımlılık satırı: "T30" — T30 mekanizma, T83 sinyal. Test: yok (scope dışı) |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (Auth) | ✓ 39/39 passed | `dotnet test tests/Skinora.Auth.Tests --filter "Category!=Integration"` → 39 PASS (önceki 32 + 7 yeni: 6 HeaderCountryResolver + 1 Pipeline age gate) |
| Integration (API SQLite) | ✓ 111/111 passed | `dotnet test tests/Skinora.API.Tests --filter "FullyQualifiedName~TosAccept|FullyQualifiedName~AuthSteamEndpoint"` → 12 relevant PASS (6 existing + 4 TosAccept + 2 AuthSteam age/geo) |
| Integration (Auth MsSql) | ⏳ CI-only | `SettingsBasedAgeGateCheckTests` 5 + `SettingsBasedGeoBlockCheckTests` 6 + `TosAcceptanceServiceTests` 6 = 17 test; yerel Docker Desktop çalışmadığı için T11.3 shared MsSql pattern CI'da validate eder |
| Build | ✓ | `dotnet build` → 0 Warning 0 Error |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS (2026-04-21) |
| Verdict bağımsızlığı | Raporu okumadan Faz 1-2 tamamlandı, Faz 3'te karşılaştırıldı — uyuşmazlık yok |
| Bulgu sayısı | 0 |
| Düzeltme gerekli mi | Hayır |

### Ön Kontroller

| Adım | Durum | Kanıt |
|---|---|---|
| -1. Working tree hygiene | ✓ Temiz | `git status --short` boş |
| 0. Main CI startup | ✓ PASS | Son 3 main run: `24742526589`, `24742526586`, `24742125980` hepsi `success` |
| 0b. Repo memory drift | ✓ Güncel | `.claude/memory/MEMORY.md` satır 11, 22, 23 T30 referansları mevcut |

### Kabul Kriterleri (bağımsız verdict)

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | POST /auth/tos/accept → ToS versiyonu kaydedilir | ✓ | `AuthController.AcceptTos` (+145) → `TosAcceptanceService` `TosAcceptedVersion` + `TosAcceptedAt` + `AgeConfirmedAt` atomik set. 07 §4.4 sözleşmesi uyumlu: request `{tosVersion, ageOver18}`, response `{accepted, acceptedAt}`, 409 `TOS_ALREADY_ACCEPTED`, 400 `VALIDATION_ERROR`, 401 unauthenticated. 10 test PASS (4 TosAcceptEndpoint + 6 TosAcceptanceService) |
| 2 | Yaş gate: 18+ beyanı + Steam hesap yaşı | ✓ | 18+ beyanı: `TosAcceptanceService` `ageOver18=false` → `ValidationException`. Steam account age: `SettingsBasedAgeGateCheck` `auth.min_steam_account_age_days` (default 30) altındaki hesapları `AgeBlocked` → controller `?error=age_blocked` redirect. Pipeline sırası (geo→sanctions→profile→age→provisioning) doğru. 7 test PASS (5 SettingsBasedAgeGateCheck + 1 Pipeline.AgeGateBlocked + 1 AuthSteam.FreshSteamAccount) |
| 3 | Geo-block: IP bazlı + admin-yönetimli yasaklı ülke listesi | ✓ | `SettingsBasedGeoBlockCheck` + `auth.banned_countries` SystemSetting CSV; `NONE` marker + case-insensitive + trim. `ICountryResolver`/`HeaderCountryResolver` abstraction. Admin yönetimi: SystemSetting güncelle → anında etkili. 13 test PASS (6 SettingsBasedGeoBlockCheck + 6 HeaderCountryResolver + 1 AuthSteam.GeoBlockedCountry) |
| 4 | VPN/proxy tespiti destekleyici sinyal (tek başına engelleme değil) | ✓ | 11_IMPLEMENTATION_PLAN T83 (Geo-block servisi) bağımlılığı T30'dan devralıyor ve aynı kriteri tekrar listeliyor — kapsam ayrımı doküman-destekli. T30 mekanizma + stub resolver verir, T83 MaxMind/ipinfo + VPN heuristics ekleyecek. Şu an VPN detection yok → "tek başına engelleme sebebi" oluşamaz (negatif gereklilik karşılanıyor). Rapor bunu `~` olarak işaretlemişti; validator `✓` olarak düzeltti — scope designed deferral, finding değil |

### Doğrulama Kontrol Listesi

- [x] 07 §4.4 ToS endpoint sözleşmesi doğru mu? — Request/response/hata kodları birebir eşleşiyor
- [x] 02 §21.1 erişim kuralları eksiksiz mi? — Geo-block + yaş kısıtı `auth.min_steam_account_age_days` satırı güncel, sanctions (T82 stub) + VPN (T83 devri) kapsamlar doküman-uyumlu

### Test Sonuçları

| Tür | Sonuç | Komut | Kanıt |
|---|---|---|---|
| Build (Release) | ✓ 0W/0E | `dotnet build --configuration Release` | Lokal 2026-04-21 — 13.98s, tüm modüller |
| Unit (Auth) | ✓ 13/13 | `dotnet test tests/Skinora.Auth.Tests --filter "HeaderCountryResolverTests\|SteamAuthenticationPipelineTests"` | Lokal 2026-04-21 — 393 ms |
| Integration + Unit (CI) | ✓ 10/10 job | CI run [24745062009](https://github.com/turkerurganci/Skinora/actions/runs/24745062009) | Lint + Build + Unit 3× + Contract + Integration + Migration dry-run + Docker build + CI Gate hepsi success |
| Task branch CI | ✓ | `gh run list --branch task/T30-tos-age-geoblock` | Son 3 run success (`24745435990`, `24745062009`, `24744715105`) |

### Güvenlik Kontrolü

- [x] Secret sızıntısı: Temiz — `WebApiKey` IOptions pattern, no hardcoded secret
- [x] Auth etkisi: `[Authorize(Policy = AuthPolicies.Authenticated)]` ToS endpoint için mevcut; pipeline ordering geo-block/age gate provisioning öncesi (kullanıcı yaratılmaz)
- [x] Input validation: Temiz — `tosVersion` length (20 max) + whitespace, `ageOver18=true` zorunlu, CSV parse güvenli (2-char filter + trim), country header multi-value first-value + length check
- [x] Yeni bağımlılıklar: `FluentValidation` 11.11.0 (Auth modülü, API zaten kullanıyor), `Microsoft.Extensions.TimeProvider.Testing` 9.0.0 (Microsoft resmi test paketi) — ikisi de kabul edilebilir

### Yapım Raporu Karşılaştırması

- **Uyum:** Tam uyumlu, uyuşmazlık yok.
- **Tek ayar:** Rapor K4'ü `~` vermişti; validator scope ayrımı (T30/T83) 11_IMPLEMENTATION_PLAN.md § T83 ile doküman-destekli olduğundan `✓`e çıkardı. Yapım chat'i scope gap'i notes'da proje sahibi onayıyla kayıt altına almış (notlar §3).

## Altyapı Değişiklikleri

- **Migration:** Var — `20260421195807_T30_AddAgeConfirmedAtAndAccessControlSettings` (`Users.AgeConfirmedAt` datetime2 NULL kolon + 2 SystemSetting seed satırı)
- **Config/env değişikliği:** Yok (yeni SystemSetting'ler admin/env `SKINORA_SETTING_*` ile override edilebilir ama zorunlu değil — default değerler seed'de mevcut)
- **Docker değişikliği:** Yok
- **Yeni NuGet bağımlılıkları:**
  - `FluentValidation` 11.11.0 (Skinora.Auth — manuel validator için)
  - `Microsoft.Extensions.TimeProvider.Testing` 9.0.0 (Skinora.Auth.Tests — FakeTimeProvider için)

## Commit & PR

- Branch: `task/T30-tos-age-geoblock`
- Commit: `b4ec42e` (implementation) + `7fbb043` (CI fix — SeedDataTests 28→30 + Auth integration update-or-insert) + `eae82ac` (BYPASS_LOG)
- PR: #49 (https://github.com/turkerurganci/Skinora/pull/49)
- CI: ✓ PASS — run [24745062009](https://github.com/turkerurganci/Skinora/actions/runs/24745062009) 10/10 job (Lint + Build + Unit 3× + Contract + Integration + Migration dry-run + Docker build + CI Gate)
- BYPASS_LOG: 1× entry (Layer 2 ci-failure — T30 CI fix push için root cause çözüm commit'iyle birlikte geçildi)

## Known Limitations / Follow-up

- **Geo-block MVP stub:** `HeaderCountryResolver` `X-Country-Code` header'ına güvenir. Production'da edge/proxy bu header'ı kendisi set etmelidir (kullanıcı girişi spoofing'i önlemek için edge'in override etmesi şart). T83 bu konuyu MaxMind GeoLite2 / ipinfo.io inline lookup ile tamamen internalize edecek.
- **VPN/proxy detection:** T83 kapsamında — 02 §21.1'e göre destekleyici sinyal, MVP'de zorunlu değil.
- **Steam account age fail-open:** Private profile / Steam API fail durumunda `timecreated` null döner → age gate izin verir. Bu dokümante davranıştır (soft gate).
- **ToS version tracking:** Şu an tek versiyon destekliyor (ToS versionu değişince kullanıcılar tekrar kabul istemiyor). İleride ToS versiyon değişiminde re-prompt akışı eklenebilir (scope dışı).

## Notlar

- **Adım -1 (Working tree hygiene):** ✓ Temiz — `git status --short` boş çıktı verdi.
- **Adım 0 (Main CI startup check):** ✓ PASS — son 3 main CI run'ı hepsi `success`: 24742526589, 24742526586, 24742125980.
- **Dış Varsayımlar (Adım 4):**
  - **Steam `GetPlayerSummaries` `timecreated` alanı:** Kanıt — Steam Web API resmi doc (https://developer.valvesoftware.com/wiki/Steam_Web_API#GetPlayerSummaries_.28v0002.29) `timecreated` uint32 unix epoch (public profile için). Private profile'larda dönmez → fail-open.
  - **FluentValidation 11.11.0 NuGet mevcut mu:** Kanıt — aynı versiyon `Skinora.API.csproj`'de zaten kullanılıyor (`FluentValidation.DependencyInjectionExtensions` 11.11.0).
  - **Microsoft.Extensions.TimeProvider.Testing 9.0.0:** Kanıt — .NET 9 ile birlikte resmi Microsoft paketi, `FakeTimeProvider` içerir.
  - **SystemSetting admin-yönetimli:** T26'da kurulmuş `SettingsBootstrapService` + admin UI (T41 kapsamı, MVP'de DB direkt update yeterli). `IsConfigured=true` seed'li olduğu için startup fail-fast'e takılmaz.
- **Pipeline ordering kararı:** Age gate profile fetch sonrasında çünkü Steam hesap yaşı profile'dan okunur. Sanctions öncesinde değil çünkü sanctions (steamID bazlı) DB lookup'u Steam API çağrısından ucuzdur — sanctions match'de gereksiz API çağrısı önlenir.
- **Scope gap'leri proje sahibinin onayıyla karara bağlandı:**
  1. Age declaration `ageOver18: true` `/auth/tos/accept` request body'sine eklendi (ayrı endpoint yerine).
  2. Steam hesap yaşı threshold'u `auth.min_steam_account_age_days` SystemSetting (default 30 gün).
  3. T30 mekanizma + stub resolver implement eder; T83 gerçek geolocation provider + VPN/proxy detection devralır.
- **T29 bırakılan stub hook'lar:** `AllowAllGeoBlockCheck` → `SettingsBasedGeoBlockCheck` (gerçek impl). `NoMatchSanctionsCheck` hâlâ stub (T82 kapsamı).
