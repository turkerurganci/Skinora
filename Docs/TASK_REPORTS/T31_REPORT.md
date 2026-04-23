# T31 — Steam re-verify ve authenticator kontrolü

**Faz:** F2 | **Durum:** ✓ Tamamlandı | **Doğrulama:** ✓ PASS (bağımsız validator, 2026-04-23) | **Tarih:** 2026-04-23

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

---

## Doğrulama (bağımsız validator, 2026-04-23)

### Verdict: ✓ PASS

### Hard-stop kapıları
- **Adım -1 Working tree:** `git status --short` → boş ✓
- **Adım 0 Main CI startup:** Son 3 run `success` — `24803554863`, `24803554873`, `24799689846` ✓
- **Adım 0b Memory drift:** `.claude/memory/MEMORY.md` T31 satırları mevcut (`.claude/memory/MEMORY.md:11`, `:25`) ✓
- **Adım 8a Task branch CI:** Son run `24806704665` ✓ success (10 job: Detect paths / Lint / Build / Unit / Integration / Contract / Migration dry-run / Docker build / CI Gate başarı + Guard skipped=PR context)

### Kabul kriterleri (bağımsız değerlendirme)

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | POST /auth/steam/re-verify → re-auth başlatma | ✓ | `AuthController:143-165` `[Authorize]`, 10 dk cookie (`skinora_oid_rv`), `openid.return_to=ReVerifyReturnToUrl` (login'den ayrık). Integration `InitiateReVerify_Unauthenticated_Returns401` + `..._Authenticated_ReturnsSteamUrlAndSetsStateCookie` PASS. |
| 2 | GET /auth/steam/re-verify/callback → kısa ömürlü reAuthToken | ✓ | `AuthController:168-201` + `ReAuthPipeline.HandleCallbackAsync:55-93`. TTL 5 dk (`ReAuthPipeline.ReAuthTokenTtl`), single-use: Redis `StringGetDeleteAsync` (GETDEL atomic), InMem `TryRemove`. Token 48B CSRNG + SHA-256 hash at-rest (`ReAuthTokenHasher`). Integration `ReVerifyCallback_Valid_IssuesSingleUseReAuthTokenAndRedirects`: 2. redeem `null` doğrulandı. |
| 3 | POST /auth/check-authenticator → GetTradeHoldDurations ile MA kontrolü | ~ Kısmi (kontrat sabit, gerçek sidecar çağrısı T64–T69'a devir) | `AuthController:204-220` endpoint + `IMobileAuthenticatorCheck(steamId64, tradeOfferAccessToken)` kontratı 08 §2.2 parametre listesiyle tam eşleşiyor. Production DI: `StubMobileAuthenticatorCheck` `{active=false, setupGuideUrl}` döner — conservative fail-safe (DI swap unutulsa bile wallet-change gate kapalı kalır). 08 §2.2 mimarisi "Steam sidecar üzerinden" diyor; sidecar endpoint'i T67 bloğunda. T31 raporu devri şeffaf işaretliyor (§"Known Limitations"). Kabul kriteri lafzı gerçek çağrı olmadan tamamen karşılanmadı → minor bulgu (F1 aşağıda), ancak kontrat kilit karar ve güvenlik açığı yok. Integration `CheckAuthenticator_Unauthenticated_Returns401` + `..._Authenticated_ReturnsStubResult` PASS. |
| 4 | Referrer-Policy: same-origin | ✓ | `AuthController:174` — callback cevabının ilk satırında header yazılıyor, tüm kod yollarında (success+error). 3 callback integration test'inde assert edildi. |
| 5 | X-ReAuth-Token header doğrulaması | ✓ (altyapı) | `IReAuthTokenValidator.ValidateAsync(headerValue, ct)` + `ReAuthTokenValidator:19-35` single-use redeem. T34 tüketicisi bu validator'ı çağıracak. Unit `ReAuthTokenValidatorTests` 3/3 (valid + single-use, null/whitespace, unknown) PASS. Integration `ReVerifyCallback_Valid_...` gerçek token'ı validator üzerinden 1. call payload / 2. call null doğruladı. |

### Doğrulama kontrol listesi (11 §T31)
- [x] 07 §4.6–§4.8 endpoint sözleşmeleri doğru — request/response body, auth profili, redirect error code (`re_verify_failed`, `steam_id_mismatch`), Referrer-Policy header tam uyum.
- [x] 08 §2.2 GetTradeHoldDurations çağrısı doğru — parametre listesi (steamid_target eşdeğeri `steamId64`, `trade_offer_access_token`) uyumlu; gerçek HTTP çağrısı T64–T69'a devredildi, kontrat burada kilitlendi.

### Test sonuçları (lokal re-doğrulama)

| Tür | Sonuç | Komut |
|---|---|---|
| Build (Release) | ✓ 0W/0E | `dotnet build Skinora.sln -c Release` — 10.60s |
| Unit (Auth) | ✓ 54/54 | `dotnet test tests/Skinora.Auth.Tests --no-build -c Release --filter "~Unit"` — 451ms |
| Integration (API, SQLite) | ✓ 118/118 | `dotnet test tests/Skinora.API.Tests --no-build -c Release --filter "!~InitialMigration"` — 3:14 (re-verify 7/7 dahil) |
| CI run task branch | ✓ | `gh run 24806704665` — 10/10 job başarı (Guard skipped=PR context) |

### Güvenlik kontrolü

| Alan | Sonuç | Not |
|---|---|---|
| Secret sızıntısı | ✓ Temiz | Token SHA-256 hash at-rest (`ReAuthTokenHasher.Hash`); state cookie `IDataProtector` şifreli + time-bound; logger yalnızca hash / eventcode / reason loglar — plaintext token asla. `appsettings.json` placeholder `REPLACE_IN_ENV`. |
| Auth etkisi | ✓ Temiz | A5/A7 `[Authorize(Authenticated)]`; A6 public ama state cookie + SteamID match zorunlu; token validator `GETDEL`/`TryRemove` atomic single-use. |
| Input validation | ✓ Temiz | `tradeOfferAccessToken` whitespace → 400; `returnUrl` `IReturnUrlValidator.Sanitize` ile open-redirect koruması (unit test `Initiate_RejectsOpenRedirectReturnUrl` → `/profile` fallback doğrulandı); state cookie tamper-evident (IDataProtector `ToTimeLimitedDataProtector`). |
| Yeni bağımlılık | ~ Minor | `StackExchange.Redis 2.8.16` Skinora.Auth.csproj'a eklendi (Skinora.API ile aynı versiyon). Solution surface'inde zaten mevcut — ek transitive risk yok. |

### Bulgular

| # | Seviye | Açıklama | Etkilenen dosya |
|---|---|---|---|
| F1 | Minor (sözleşme devri) | Kabul kriteri #3 "GetTradeHoldDurations ile MA kontrolü" — gerçek Steam Web API çağrısı yerine `StubMobileAuthenticatorCheck` `{active=false}` döner. Kontrat ve parametre listesi sabit, DI swap hazır; gerçek impl Steam Sidecar bloğunda (T64–T69) gelecek. 08 §2.2 mimarisi zaten "sidecar üzerinden" diyor. Conservative fail-safe sayesinde güvenlik riski yok; rapor §"Known Limitations" bunu şeffaf işaretliyor. Validator kararı: PASS'i engelleyecek sapma değil — T64–T69 kapanışında kapatılır. | `backend/src/Modules/Skinora.Auth/Application/MobileAuthenticator/StubMobileAuthenticatorCheck.cs` |

### Yapım raporu karşılaştırması

- **Uyum:** Tam uyumlu. Yapım raporundaki §"Kabul Kriterleri Kontrolü" tablosu validator verdict'iyle satır satır eşleşiyor (kriter 3 için aynı "sözleşme sabit / sidecar devir" ifadesi; diğerleri ✓).
- **Uyuşmazlık:** Yok.

### Dış varsayım doğrulaması (feedback_check_external_assumptions — validator tarafı)

- Steam OpenID endpoint re-verify için aynı: T29'da teyit edildi, burada değişmedi ✓
- `StringGetDeleteAsync` StackExchange.Redis 2.7+'da mevcut — pinned 2.8.16 ✓
- `ITimeLimitedDataProtector` ASP.NET Core 6+ API, .NET 9 hedefinde stable ✓
- Redis `IConnectionMultiplexer` singleton rate-limiting ile paylaşılıyor — yeni bağlantı havuzu yaratılmadı ✓
