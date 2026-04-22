# T31 — Steam re-verify ve authenticator kontrolü

**Faz:** F2 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekleniyor) | **Tarih:** 2026-04-23

---

## Yapılan İşler

- **Re-verify başlatma:** `POST /api/v1/auth/steam/re-verify` — authenticated. Body `{ purpose, returnUrl? }`. `IReAuthPipeline.Initiate` giriş kullanıcının `UserId`/`SteamId` (JWT claim'leri) ile `ReAuthState` üretir; `IReAuthStateProtector` (IDataProtector, 10 dk TTL) state'i şifreler ve `skinora_oid_rv` cookie'sine (HttpOnly + Secure + SameSite=Lax, 10 dk max-age) yazar. Response `{ steamAuthUrl }` — builder `SteamOpenId.ReVerifyReturnToUrl`'i `openid.return_to` olarak kullanır (login assertion'ının re-verify callback'e replay edilememesi için ayrık).
- **Re-verify callback:** `GET /api/v1/auth/steam/re-verify/callback` — public. Response header `Referrer-Policy: same-origin` (query-param reAuthToken sızıntısına karşı). Cookie'den state'i çözüp `ReAuthPipeline.HandleCallbackAsync` ile: (1) state yoksa/tampered/expired → `StateMissing` → `?error=re_verify_failed`; (2) OpenID assertion reddedilirse → `AuthFailed` → `?error=re_verify_failed`; (3) `claimed_id` SteamID state'teki SteamID ile eşleşmiyorsa → `SteamIdMismatch` → `?error=steam_id_mismatch`; (4) başarılı → 48 byte cryptographically random token üretir, SHA-256 hash'ini `IReAuthTokenStore`'a 5 dk TTL single-use olarak yazar, `{returnUrl}?reAuthToken=...` redirect'i kurar.
- **Authenticator check:** `POST /api/v1/auth/check-authenticator` — authenticated. Body `{ tradeOfferAccessToken }`. Eksik/whitespace → 400. `IMobileAuthenticatorCheck.CheckAsync(steamId, tradeOfferAccessToken)` çağrılır; default `StubMobileAuthenticatorCheck` `{ active=false, setupGuideUrl=https://help.steampowered.com/.../06B0-... }` döner. Real Steam sidecar impl (Steam Web API `IEconService/GetTradeHoldDurations/v1`) T64–T69 sidecar task bloğunda DI swap ile gelir — çağrı kontratı bu task'ta sabitlenmiştir.
- **ReAuthToken store:** `IReAuthTokenStore` iki impl — `RedisReAuthTokenStore` prod (mevcut `IConnectionMultiplexer` rate-limiting ile shared, key `skinora:reauth:token:{sha256(token)}`, single-use için `GETDEL` atomic), `InMemoryReAuthTokenStore` test (`ConcurrentDictionary` + `TryRemove` + TTL). Payload `{ UserId, SteamId }` JSON.
- **Token doğrulama (T34 kullanımı için):** `IReAuthTokenValidator.ValidateAsync(headerValue)` — `X-ReAuth-Token` header değerini store'a redeem eder; ilk redeem payload döner, ikinci redeem null (single-use kanıtlanmış).
- **OpenID validator genişletmesi:** `ISteamOpenIdValidator.ValidateAsync(params, expectedReturnTo, ct)` overload eklendi. Mevcut login çağrısı hiç değişmeden (default `_settings.ReturnToUrl`), re-verify pipeline `_settings.ReVerifyReturnToUrl` geçer. `SteamOpenIdUrlBuilder.Build(settings, returnToUrl)` overload'ı aynı amaçla eklendi — OpenID realm, identity, claimed_id, mode sabit; sadece `return_to` parametrize.
- **Güvenlik mitigasyonları (07 §4.7):** (a) Referrer-Policy: same-origin (cross-site Referer sızıntısı yok), (b) reAuthToken SHA-256 hash at-rest (plaintext yalnızca URL query ve client memory), (c) state cookie IDataProtector tamper-evident, (d) single-use atomic consume, (e) returnUrl `IReturnUrlValidator` ile open-redirect koruması (login flow ile aynı allow-list).
- **Konfigürasyon:** `SteamOpenIdSettings.ReVerifyReturnToUrl` required property eklendi; `appsettings.json` + `appsettings.Development.json` güncellendi. `SteamAuthenticationModule` DI: `AddDataProtection`, `IReAuthStateProtector` singleton, `IReAuthTokenStore` Redis singleton, `IReAuthPipeline`/`IReAuthTokenValidator` scoped, `IMobileAuthenticatorCheck` scoped (stub).

## Etkilenen Modüller / Dosyalar

**Auth modülü (yeni ReAuthentication + MobileAuthenticator klasörleri):**
- `backend/src/Modules/Skinora.Auth/Application/ReAuthentication/ReAuthState.cs` — yeni
- `backend/src/Modules/Skinora.Auth/Application/ReAuthentication/IReAuthStateProtector.cs` — yeni
- `backend/src/Modules/Skinora.Auth/Application/ReAuthentication/ReAuthStateProtector.cs` — yeni
- `backend/src/Modules/Skinora.Auth/Application/ReAuthentication/IReAuthTokenStore.cs` — yeni
- `backend/src/Modules/Skinora.Auth/Application/ReAuthentication/ReAuthTokenHasher.cs` — yeni
- `backend/src/Modules/Skinora.Auth/Application/ReAuthentication/RedisReAuthTokenStore.cs` — yeni
- `backend/src/Modules/Skinora.Auth/Application/ReAuthentication/InMemoryReAuthTokenStore.cs` — yeni
- `backend/src/Modules/Skinora.Auth/Application/ReAuthentication/IReAuthPipeline.cs` — yeni (ReAuthInitiation + ReAuthOutcome union types)
- `backend/src/Modules/Skinora.Auth/Application/ReAuthentication/ReAuthPipeline.cs` — yeni
- `backend/src/Modules/Skinora.Auth/Application/ReAuthentication/IReAuthTokenValidator.cs` — yeni (interface + concrete)
- `backend/src/Modules/Skinora.Auth/Application/MobileAuthenticator/IMobileAuthenticatorCheck.cs` — yeni
- `backend/src/Modules/Skinora.Auth/Application/MobileAuthenticator/StubMobileAuthenticatorCheck.cs` — yeni
- `backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/ISteamOpenIdValidator.cs` — `ValidateAsync(..., expectedReturnTo, ...)` overload
- `backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/SteamOpenIdValidator.cs` — overload impl; default method delegate
- `backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/SteamOpenIdUrlBuilder.cs` — `Build(settings, returnToUrl)` overload
- `backend/src/Modules/Skinora.Auth/Configuration/SteamOpenIdSettings.cs` — `ReVerifyReturnToUrl` required
- `backend/src/Modules/Skinora.Auth/Skinora.Auth.csproj` — `StackExchange.Redis 2.8.16` (rate limiting ile aynı versiyon)

**API katmanı:**
- `backend/src/Skinora.API/Controllers/AuthController.cs` — 3 yeni endpoint + request/response DTO'lar + `BuildReVerifyRedirect` helper
- `backend/src/Skinora.API/Configuration/SteamAuthenticationModule.cs` — `AddDataProtection`, ReAuth ve MobileAuthenticator servis kayıtları
- `backend/src/Skinora.API/appsettings.json` — `SteamOpenId.ReVerifyReturnToUrl`
- `backend/src/Skinora.API/appsettings.Development.json` — `SteamOpenId.ReVerifyReturnToUrl`

**Testler:**
- `backend/tests/Skinora.Auth.Tests/Unit/ReAuthPipelineTests.cs` — yeni (5 test: initiate happy path, initiate open-redirect reject, callback no-state, invalid assertion, steamId mismatch, valid happy path)
- `backend/tests/Skinora.Auth.Tests/Unit/InMemoryReAuthTokenStoreTests.cs` — yeni (4 test: TTL happy path + single-use, expired, unknown, null/whitespace)
- `backend/tests/Skinora.Auth.Tests/Unit/ReAuthTokenValidatorTests.cs` — yeni (3 test: single-use, null-input theory, unknown)
- `backend/tests/Skinora.Auth.Tests/Unit/SteamOpenIdUrlBuilderTests.cs` — `ReVerifyReturnToUrl` field'ı required için fixture güncellemesi
- `backend/tests/Skinora.API.Tests/Integration/AuthReVerifyEndpointTests.cs` — yeni (7 test: initiate unauth/auth, callback no-state, mismatch, valid happy path + single-use; check-authenticator unauth/auth stub)
- `backend/tests/Skinora.API.Tests/Integration/AuthSteamEndpointTests.cs` — `FakeSteamOpenIdValidator` yeni overload impl

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `POST /auth/steam/re-verify` → Steam re-auth başlatma (purpose + returnUrl) | ✓ | `AuthController.InitiateReVerify` + `ReAuthPipeline.Initiate`; testler `AuthReVerifyEndpointTests.InitiateReVerify_Authenticated_ReturnsSteamUrlAndSetsStateCookie` (200 + steamAuthUrl + state cookie Set) ve `InitiateReVerify_Unauthenticated_Returns401`. Re-verify openid.return_to `ReVerifyReturnToUrl`'e eşit, login callback'den ayrık. |
| 2 | `GET /auth/steam/re-verify/callback` → reAuthToken üretimi (kısa ömürlü) | ✓ | `AuthController.HandleReVerifyCallback` + `ReAuthPipeline.HandleCallbackAsync`. TTL = `ReAuthPipeline.ReAuthTokenTtl` = 5 dk (const). Single-use: `InMemoryReAuthTokenStore` `TryRemove`, `RedisReAuthTokenStore` `StringGetDeleteAsync`. Test `AuthReVerifyEndpointTests.ReVerifyCallback_Valid_IssuesSingleUseReAuthTokenAndRedirects`: ilk `ValidateAsync` payload döner, ikinci `null`. |
| 3 | `POST /auth/check-authenticator` → GetTradeHoldDurations ile MA kontrolü | ✓ (sözleşme) / ⏳ (gerçek sidecar) | `AuthController.CheckAuthenticator` + `IMobileAuthenticatorCheck` kontratı `CheckAsync(steamId, tradeOfferAccessToken)` → `{active, setupGuideUrl?}` sabitlendi — bu 08 §2.2 `GetTradeHoldDurations` parametreleriyle birebir örtüşür. Default `StubMobileAuthenticatorCheck` active=false + Steam setup guide URL döner (conservative fail-safe). Gerçek sidecar çağrısı T64–T69 Steam Sidecar task bloğunda DI swap ile devralır; rapor ve interface XML-doc bunu açıkça belirtir. Testler `CheckAuthenticator_Unauthenticated_Returns401` + `CheckAuthenticator_Authenticated_ReturnsStubResult`. |
| 4 | Referrer-Policy: same-origin (reAuthToken sızma koruması) | ✓ | `AuthController.HandleReVerifyCallback` response'a `Referrer-Policy: same-origin` header ekler. Test `AuthReVerifyEndpointTests` 3 callback senaryosunda (NoStateCookie, SteamIdMismatch, Valid) header assert edilir. |
| 5 | X-ReAuth-Token header doğrulaması (cüzdan değişikliğinde kullanılacak) | ✓ (kontrat) | `IReAuthTokenValidator.ValidateAsync(headerValue, ct)` interface + `ReAuthTokenValidator` concrete. T34 cüzdan değişikliği endpoint'i bu servisi çağırarak token → `{UserId, SteamId}` çözümü + tek kullanımlık invalidation alır. Integration test `ReVerifyCallback_Valid_...` gerçek token'ı validator üzerinden doğrulayıp single-use semantiğini kanıtlar (ilk PASS, ikinci null). |

## Test Sonuçları

| Tür | Sonuç | Komut |
|---|---|---|
| Unit (Auth) | ✓ 54/54 | `dotnet test tests/Skinora.Auth.Tests --filter "FullyQualifiedName!~Integration"` — 97 ms |
| Integration (Auth, MsSql gerekli) | ⏳ CI-only | T11.3 shared MsSql pattern; lokal Docker gerektirir — CI'da validate edilecek |
| Integration (API, SQLite) | ✓ 118/118 | `dotnet test tests/Skinora.API.Tests --filter "FullyQualifiedName!~InitialMigration"` — yeni re-verify 7/7, regression temiz |
| Integration (InitialMigration, Docker) | ⏳ CI-only | Lokal Docker kapalı; CI Linux runner'da çalışır |
| Shared unit+integration | ✓ 166/166 | standalone run |
| Build (Debug + Release) | ✓ 0W/0E | `dotnet build Skinora.sln -c Release` — 9.72s |

## Altyapı Değişiklikleri

| Alan | Değişiklik |
|---|---|
| NuGet | `Skinora.Auth.csproj` → `StackExchange.Redis 2.8.16` (Skinora.API ile aynı) eklendi. Transitive — yeni prod bağımlılığı değil, mevcut surface içinde |
| Data Protection | `AddDataProtection()` çağrısı `SteamAuthenticationModule`'da. Default key ring (production'da Redis/Azure/keyring persist stratejisi T26+ SystemSetting ile F3/F6'da netleşir — 05 §?. MVP geliştirme için in-memory ephemeral) |
| Redis key space | Yeni prefix `skinora:reauth:token:*` (TTL 5 dk) — rate limiting `skinora:rl:*` ile çakışma yok |
| Config | `SteamOpenId.ReVerifyReturnToUrl` required — deploy öncesi production config (`https://skinora.com/api/v1/auth/steam/re-verify/callback`) doldurulmalı |
| Migration | Yok (Redis-backed, DB değişmedi) |

## Güvenlik Self-Check

| Kontrol | Sonuç |
|---|---|
| Secret sızıntısı | ✓ reAuthToken SHA-256 hash'li at-rest; state cookie IDataProtector şifreli; logger sadece hash veya eventcode, plaintext token loglanmıyor |
| Auth/authorization etkisi | ✓ A5/A7 `[Authorize(Authenticated)]`; A6 public (state cookie + SteamID match zorunlu); tokenvalidator single-use enforce eder |
| Input validation | ✓ `tradeOfferAccessToken` whitespace check 400; `returnUrl` `IReturnUrlValidator.Sanitize` open-redirect koruması; state cookie tamper-evident |
| Yeni dış bağımlılık | ~ StackExchange.Redis 2.8.16 (zaten solution surface'inde mevcut) — ek risk yok |

## Known Limitations (T31 dışı kalan, sonraki task'a devir)

- **IMobileAuthenticatorCheck gerçek impl:** Stub `{active=false, setupGuideUrl=...}` döner. Gerçek sidecar çağrısı (HTTP → Steam sidecar → GetTradeHoldDurations) T64–T69 task bloğunda DI swap ile gelecek. T31 kontratı + çağrı yolunu sabitledi.
- **X-ReAuth-Token tüketici entegrasyonu:** `IReAuthTokenValidator` hazır; T34 (Cüzdan adresi yönetimi) endpoint'i bu servisi çağırarak wallet change güvenlik kapısını kuracak. T31 validator'ı ve single-use semantiğini integration test ile kanıtladı.
- **DataProtection key ring persistence:** Default ASP.NET Core in-memory key ring geliştirme/tek-instance prod için uygun; multi-instance prod deployment Redis/keyring persistence stratejisi altyapı task'ında (F3/F6 deploy retrospektifinde) netleşecek.

## Notlar

- **Working tree hygiene:** Session başında `git status --short` temiz (F1 → T29 → T30 sonrası). 0 bundled PR riski.
- **Main CI startup:** Son 3 run `success` (`24803554863`, `24803554873`, `24799689846`) — T30 post-squash chore'lardan sonraki state stabil. HARD STOP tetiklenmedi.
- **Dış varsayım doğrulaması (feedback_check_external_assumptions):**
  1. Steam OpenID endpoint re-verify için aynı — T29'da kanıtlandı ✓
  2. `IEconService/GetTradeHoldDurations/v1` endpoint erişilebilir — `curl ... -w "%{http_code}"` → 403 with dummy key (endpoint live, valid key gerekli) ✓
  3. Steam sidecar `GetTradeHoldDurations` endpoint'i: yok (stub 501 T67 için) — T31 MA-check için interface + stub ile bypass, gerçek çağrı T64–T69 bloğuna devir (feedback: "sidecar yok → stub impl kullan, interface sabitle" kararı kullanıcıyla netleştirildi) ✓
  4. Redis zaten `IConnectionMultiplexer` olarak rate limiting için wired — singleton re-use, yeni connection pool gerekmez ✓
  5. `Microsoft.AspNetCore.DataProtection` — `FrameworkReference Include="Microsoft.AspNetCore.App"` sayesinde NuGet eklenmeden erişilir ✓
- **Bundled commit riski:** Yok — branch `task/T31-steam-reverify-authenticator` main'den taze açıldı, sadece T31 ilgili değişiklikler var.
- **06 DATA_MODEL etkisi:** Yok — reAuthToken ve state cookie Redis/cookie backed; entity/tablo değişimi yok.
- **07 API_DESIGN etkisi:** 07 §4.6–§4.8 hali hazırda detayı dokümanlanmış; implementasyon sözleşmeyle birebir uyumlu (endpoint, method, body, response, redirect error code'ları, Referrer-Policy notu). Dokümana ek değişiklik gerekli değil.

## Commit & PR

- **Branch:** `task/T31-steam-reverify-authenticator`
- **Commit:** `e34a68b`
- **PR:** #52 — https://github.com/turkerurganci/Skinora/pull/52
