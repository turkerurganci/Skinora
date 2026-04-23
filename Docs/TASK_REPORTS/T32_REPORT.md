# T32 — Refresh token yönetimi

**Faz:** F2 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-04-23

---

## Yapılan İşler

- **Refresh endpoint (`POST /api/v1/auth/refresh`, 07 §4.10):** `AllowAnonymous` + refresh cookie'den plaintext al → SHA-256 hash → DB'de ara. `IRefreshTokenService.RotateAsync` beş sonuçtan birini döner: `Success(User, Access, Refresh)`, `Missing`, `Invalid`, `Expired`, `Reused(UserId)`. Happy path'te eski token `IsRevoked=true` + `RevokedAt=now` + `ReplacedByTokenId=new.Id` işaretlenir, yeni refresh token `IRefreshTokenGenerator.IssueAsync` ile basılır, 15 dakikalık access JWT üretilir, yeni refresh cookie (`HttpOnly + Secure + SameSite=Strict + Path=/api/v1/auth`) set edilir. Response `{ accessToken, expiresIn }` (07 §4.10 `data` sözleşmesi).
- **Logout endpoint (`POST /api/v1/auth/logout`, 07 §4.9):** `[Authorize(Authenticated)]`. Cookie varsa `IRefreshTokenService.RevokeAsync` çağrılır (hash → DB update `IsRevoked=true, RevokedAt=now`), cookie `Max-Age=0` ile temizlenir. Cookie yoksa idempotent 200.
- **Me endpoint (`GET /api/v1/auth/me`, 07 §4.5):** `[Authorize(Authenticated)]`. `AuthClaimTypes.UserId`/`Role` claim'lerinden değerler → `ICurrentUserService.GetAsync(userId, role, ct)` → `CurrentUserDto` döner. DTO alan haritası: `id, steamId, displayName, avatarUrl, mobileAuthenticatorActive (=User.MobileAuthenticatorVerified), tosAccepted (=TosAcceptedAt != null), role, language (=PreferredLanguage), hasSellerWallet (=DefaultPayoutAddress != null), hasRefundWallet (=DefaultRefundAddress != null), createdAt`. DB okuması `AsNoTracking()`.
- **Token rotation (05 §6.1 reuse detection):** Halihazırda `IsRevoked=true` VEYA `ReplacedByTokenId != null` olan bir token ile refresh denemesi → `Reused(UserId)` döner + kullanıcının tüm aktif refresh token'ları cache'den silinip DB'de `IsRevoked=true, RevokedAt=now` işaretlenir. OWASP refresh-token reuse detection: çalınan token klonu gelecekte aynı kullanıcının diğer aktif oturumlarını da kilitler. Tek noktada toplanmış — `RefreshTokenService.MassRevokeAsync`.
- **DB source of truth + Redis cache (05 §6.1):** `IRefreshTokenCache` (GetAsync/SetAsync/RemoveAsync, key = token SHA-256 hash, TTL = token'ın kalan ömrü). `RedisRefreshTokenCache` JSON payload (`{tokenId, userId, expiresAt}`), namespace `skinora:refresh:{hash}`, corrupt entry otomatik drop. `NullRefreshTokenCache` test/fallback (tüm GetAsync null → DB okur). Rotate ve Revoke her durumda `RemoveAsync` çağırır; yalnızca başarılı rotate sonrasında yeni token `SetAsync` ile cache'lenir. "Redis çökerse DB'den okunur" semantiği null cache + query path'in aynılığıyla doğrudan sağlanır.
- **Cleanup job (T32 kabul kriteri "Expired/revoked token cleanup (periyodik)"):** `RefreshTokenCleanupJob.ExecuteAsync` — `ExpiresAt < now - 7d` VEYA `(IsRevoked && RevokedAt < now - 7d)` olan kayıtları soft-delete (`IsDeleted=true, DeletedAt=now`). 7 günlük grace window son revocation'ı audit için görünür tutar, tablo büyümesini bağlar. Global soft-delete query filter silinen satırları sonraki okumalardan otomatik saklar.
- **Cleanup job registration:** `RefreshTokenCleanupJobRegistrar : IHostedService`. Startup'ta `IBackgroundJobScheduler.AddOrUpdateRecurring<RefreshTokenCleanupJob>("refresh-token-cleanup", job => job.Execute(), "0 3 * * *")` çağırır. Scheduler yoksa (migrations-only tooling) warning log + devam — uygulama başlangıcı bloklanmaz.
- **`IBackgroundJobScheduler.AddOrUpdateRecurring` (yeni):** Shared abstraction'a cron-based recurring job API eklendi. `HangfireBackgroundJobScheduler` impl = `RecurringJob.AddOrUpdate` delegasyonu. Mevcut `Schedule`/`Enqueue`/`Delete` ile aynı Expression<Action<T>> kontratı — T47 (Timeout scheduling) ve T63b (Retention job'ları) ileride aynı yüzeyi tüketecek.
- **DI scope düzeltmesi:** İlk impl `RefreshTokenCleanupJobRegistrar`'ı `IBackgroundJobScheduler` (Scoped) doğrudan ctor injection ile aldı; IHostedService (Singleton) tüketmek DI validator'ını tetikledi ve 18 test host-build sırasında patladı. `IServiceScopeFactory` + `CreateScope` pattern'ine geçildi (aynı çözüm F0 Gate Check'te `OutboxStartupHook` için uygulanmıştı). XML-doc neden/pattern'i not eder.

## Etkilenen Modüller / Dosyalar

**Auth modülü (yeni Session alt paketi):**
- `backend/src/Modules/Skinora.Auth/Application/Session/IRefreshTokenService.cs` — interface + `RotateOutcome` union (Success/Missing/Invalid/Expired/Reused)
- `backend/src/Modules/Skinora.Auth/Application/Session/RefreshTokenService.cs` — rotation + reuse detection + mass-revoke + cache invalidation
- `backend/src/Modules/Skinora.Auth/Application/Session/IRefreshTokenCache.cs` — interface + `RefreshTokenCacheEntry`
- `backend/src/Modules/Skinora.Auth/Application/Session/RedisRefreshTokenCache.cs` — JSON payload, namespaced key, corrupt-entry drop
- `backend/src/Modules/Skinora.Auth/Application/Session/NullRefreshTokenCache.cs` — no-op fallback (Redis yok/test)
- `backend/src/Modules/Skinora.Auth/Application/Session/CurrentUserService.cs` — `ICurrentUserService` + `CurrentUserDto` + concrete
- `backend/src/Modules/Skinora.Auth/Application/Session/RefreshTokenCleanupJob.cs` — sync `Execute()` + async `ExecuteAsync()`, soft-delete + grace window
- `backend/src/Modules/Skinora.Auth/Application/Session/RefreshTokenCleanupJobRegistrar.cs` — IHostedService + IServiceScopeFactory

**Shared abstraction:**
- `backend/src/Skinora.Shared/BackgroundJobs/IBackgroundJobScheduler.cs` — `AddOrUpdateRecurring<T>` eklendi
- `backend/src/Skinora.API/BackgroundJobs/HangfireBackgroundJobScheduler.cs` — `RecurringJob.AddOrUpdate` delegasyonu

**API katmanı:**
- `backend/src/Skinora.API/Controllers/AuthController.cs` — 3 yeni endpoint + `IssueRotatedSession`/`RefreshFailure`/`ClearRefreshCookie`/`BuildRefreshCookieOptions` helper'ları + `RefreshResponse` DTO
- `backend/src/Skinora.API/Configuration/SteamAuthenticationModule.cs` — `IRefreshTokenCache` (Redis factory), `IRefreshTokenService`/`ICurrentUserService` scoped, `RefreshTokenCleanupJob` scoped, `RefreshTokenCleanupJobRegistrar` hosted service

**Testler:**
- `backend/tests/Skinora.Auth.Tests/Integration/RefreshTokenServiceTests.cs` — yeni (9 test: missing/unknown/happy/revoked-reused/rotated-reused/expired, revoke unknown/active/already-revoked). Pattern: `IntegrationTestBase` + `NullRefreshTokenCache` + gerçek `RefreshTokenGenerator`/`AccessTokenGenerator`.
- `backend/tests/Skinora.Auth.Tests/Integration/RefreshTokenCleanupJobTests.cs` — yeni (5 test: no stale / expired past grace / within grace kept / revoked past grace / revoked within grace kept)
- `backend/tests/Skinora.API.Tests/Integration/AuthSessionEndpointTests.cs` — yeni (9 test: `/auth/me` unauth+auth; `/auth/refresh` no-cookie+happy+replay-reuse+expired; `/auth/logout` unauth+auth-with-cookie+auth-without-cookie). SQLite in-memory fixture + `NullRefreshTokenCache` + NoopBackgroundJobScheduler.
- `backend/tests/Skinora.API.Tests/Integration/AuthSteamEndpointTests.cs` — `NoopBackgroundJobScheduler` sınıfına `AddOrUpdateRecurring` no-op eklendi (interface genişlediği için derleme şartı)
- `backend/tests/Skinora.API.Tests/Integration/AuthReVerifyEndpointTests.cs` — aynı Noop scheduler güncellemesi
- `backend/tests/Skinora.API.Tests/Integration/TosAcceptEndpointTests.cs` — aynı Noop scheduler güncellemesi
- `backend/tests/Skinora.API.Tests/Integration/OutboxTests.cs` — `SpyJobScheduler`'a `AddOrUpdateRecurring` (counter) eklendi

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | POST /auth/refresh → access token yenileme (refresh cookie'den) | ✓ | `AuthController.Refresh` + `RefreshTokenService.RotateAsync` Success branch. Integration `AuthSessionEndpointTests.Refresh_ValidCookie_RotatesAndReturnsAccessToken` — yeni accessToken + yeni refresh cookie + eski token `IsRevoked=true, ReplacedByTokenId=new.Id`, DB'de 2 satır. |
| 2 | POST /auth/logout → refresh token revoke, cookie temizleme | ✓ | `AuthController.Logout` + `RefreshTokenService.RevokeAsync`. Integration `Logout_Authenticated_RevokesTokenAndClearsCookie` — cookie `expires=` past tarih + DB'de token `IsRevoked=true, RevokedAt!=null`. `Logout_Authenticated_NoCookie_IsIdempotent` idempotency. |
| 3 | GET /auth/me → mevcut oturum bilgisi | ✓ | `AuthController.Me` + `CurrentUserService.GetAsync`. Integration `GetMe_Authenticated_ReturnsProfileDto` — 07 §4.5 DTO alanları tek tek assert (id, steamId, displayName, avatarUrl, mobileAuthenticatorActive, tosAccepted, role="user", language="tr", hasSellerWallet, hasRefundWallet). `GetMe_Unauthenticated_Returns401`. |
| 4 | Token rotation: kullanılan refresh token invalidate, yeni üretilir | ✓ | `RefreshTokenService.RotateAsync` Success branch — yeni token önce `IRefreshTokenGenerator.IssueAsync`, sonra eski token `IsRevoked=true + ReplacedByTokenId=new.Id + RevokedAt=now`, `SaveChangesAsync`. Integration `RotateAsync_HappyPath_RevokesOldAndIssuesNewPair` + `Refresh_ValidCookie_RotatesAndReturnsAccessToken`. |
| 5 | DB source of truth + Redis cache | ✓ | `IRefreshTokenCache` hash-keyed cache katmanı, `RedisRefreshTokenCache` prod. Rotate başarısında eski hash `RemoveAsync`, yeni hash `SetAsync(ttl=refresh kalan ömrü)`. Revoke'da her zaman `RemoveAsync`. Cache miss → `RefreshTokenService.RotateAsync` doğrudan DB'ye düşer → "Redis çökerse DB'den okunur" semantiği. |
| 6 | Expired/revoked token cleanup (periyodik) | ✓ | `RefreshTokenCleanupJob.ExecuteAsync` + `RefreshTokenCleanupJobRegistrar` (IHostedService) — günde bir, 03:00 UTC, cron `0 3 * * *`. 7 günlük grace. Integration `RefreshTokenCleanupJobTests` 5 senaryo: expired past grace silinir, within grace tutulur, revoked past grace silinir, revoked within grace tutulur, no-stale zero döner. |
| 7 | Token reuse detection (05 §6.1 bonus) | ✓ | `RotateAsync` halihazırda rotated veya revoked token → `Reused(UserId)` + `MassRevokeAsync` (kullanıcının tüm aktif token'ları). Unit `RotateAsync_AlreadyRevokedToken_MassRevokesAndReturnsReused` + `RotateAsync_AlreadyRotatedToken_MassRevokesAndReturnsReused`. Integration `Refresh_WithRotatedCookie_ReturnsInvalidAndMassRevokes`: 2. çağrı 401 INVALID + `active count=0`. |

## Doğrulama Kontrol Listesi (11 §T32)

- [x] 07 §4.9–§4.10 sözleşmeleri doğru — request/response body, auth profili, hata kodları (`REFRESH_TOKEN_MISSING/INVALID/EXPIRED`), cookie semantiği (HttpOnly+Secure+SameSite=Strict+Path=/api/v1/auth).
- [x] Token rotation çalışıyor — rotated `ReplacedByTokenId` işaretli, yeni access + refresh pair üretiliyor, testler 1. rotasyon happy, 2. rotasyon reuse detection ile ayırt ediliyor.
- [x] Kullanılmış refresh token ile tekrar istek → 401 — `Refresh_WithRotatedCookie_ReturnsInvalidAndMassRevokes` 401 `REFRESH_TOKEN_INVALID` + kullanıcının tüm active token'ları mass-revoke.

## Test Sonuçları

| Tür | Sonuç | Komut |
|---|---|---|
| Build (Debug) | ✓ 0W/0E | `dotnet build -c Debug` — 17.61s |
| Unit (Auth) | ✓ 54/54 | `dotnet test tests/Skinora.Auth.Tests --filter "!~Integration"` — 519 ms |
| Integration (Auth, MsSql gerekli) | ⏳ CI-only | T11.3 shared MsSql pattern; lokal Docker + MsSql container gerektirir, CI'da `INTEGRATION_TEST_SQL_SERVER` env var ile çalışır — `RefreshTokenServiceTests` 9 + `RefreshTokenCleanupJobTests` 5 |
| Integration (API, SQLite) | ✓ 127/127 | `dotnet test tests/Skinora.API.Tests --filter "!~InitialMigration"` — 3:14; yeni `AuthSessionEndpointTests` 9/9 + regression yok |

## Altyapı Değişiklikleri

| Alan | Değişiklik |
|---|---|
| Shared abstraction | `IBackgroundJobScheduler.AddOrUpdateRecurring<T>(string jobId, Expression<Action<T>>, string cron)` — yeni yüzey. T47/T63b için ileride tekrar tüketilecek |
| Redis key space | Yeni prefix `skinora:refresh:*` (TTL = token kalan ömrü, max refresh expiry kadar). Mevcut rate-limit `skinora:rl:*` ve re-auth `skinora:reauth:*` ile çakışma yok |
| Hangfire | Yeni recurring job `refresh-token-cleanup` günlük 03:00 UTC. Hangfire storage durable olduğundan job tanımı yeniden başlatmalarda korunur; `AddOrUpdateRecurring` idempotent |
| Migration | Yok — RefreshToken entity T18'de + gerekli alanlar (`ReplacedByTokenId`, `RevokedAt`, `IsRevoked`, soft-delete) tamamen mevcut |
| NuGet | Yok (StackExchange.Redis Auth modülünde zaten T31 ile var) |
| Config | Yok — JwtSettings mevcut alanlar (AccessTokenExpiryMinutes, RefreshTokenExpiryDays) yeterli |

## Güvenlik Self-Check

| Kontrol | Sonuç |
|---|---|
| Secret sızıntısı | ✓ Refresh token plaintext sadece client cookie + short-lived memory'de; DB'de SHA-256 hash (`Token` kolonu), Redis'te de hash key. Access JWT SigningCredentials mevcut, logger hash'li kayıt/yok |
| Auth etkisi | ✓ `/auth/logout` + `/auth/me` `[Authorize(Authenticated)]`; `/auth/refresh` `[AllowAnonymous]` ama cookie gerektiriyor (plaintext → hash → DB lookup, bilinmeyen token 401). Rotation ile eski cookie'nin paralel geçerliliği bloklanıyor |
| Input validation | ✓ Refresh cookie eksik/whitespace → erken 401 (`REFRESH_TOKEN_MISSING`, cookie zaten yoksa clearCookie=false — gereksiz Set-Cookie yok). Cookie Set path ile delete path aynı (`/api/v1/auth`) — yanlış scope'lu cookie kalmaz |
| CSRF | ✓ Refresh cookie `SameSite=Strict` — cross-site POST /auth/refresh tetiklenemiyor. `/auth/logout` bearer ile korunan olduğundan CSRF risk yok |
| OWASP refresh token reuse | ✓ Rotation chain + mass-revoke: çalınmış ve kullanılmış token gelecekte aynı kullanıcının tüm oturumlarını kapatır (05 §6.1) |
| Yeni dış bağımlılık | Yok |

## Known Limitations (T32 dışı kalan, sonraki task'a devir)

- **Redis cache integration testi yok:** `NullRefreshTokenCache` ile DB path test edildi, `RedisRefreshTokenCache` için unit test/entegrasyon eklemedim (mevcut `InMemoryReAuthTokenStore` pattern'i ile `FakeRedis` benzeri bir çözüm olurdu ama T32 MVP için cache "acceleration" — DB tek doğruluk kaynağı olduğundan regresyon riski düşük). Not edildi; CI'da Redis container aktif olduğunda ileride smoke test eklenebilir.
- **`RefreshTokenCleanupJob` CI smoke çalıştırması yok:** Recurring job production'da Hangfire aracılığıyla günde bir tetiklenir. CI'da Hangfire persisted storage + worker olmadığından lojik (`ExecuteAsync`) integration test ile kanıtlandı; Hangfire dispatch zinciri test edilmedi — T47 veya F2 Gate Check'te Hangfire smoke eklenebilir.
- **T41 Admin parametre yönetimi etkisi:** Cron expression ve grace period hard-coded (`"0 3 * * *"`, 7 gün). T41'de dinamik ayar SystemSetting katmanı geldiğinde `cleanup.refresh_token_grace_days` + `cleanup.refresh_token_cron` gibi setting'ler tanımlanabilir (out of scope for T32).

## Notlar

- **Working tree hygiene:** Session başında `git status --short` temiz ✓
- **Main CI startup:** Son 3 run success (`24827836299`, `24827836274`, `24827146834`) — T31 merge + settings.local chore sonrası stabil ✓
- **Dış varsayım doğrulaması (feedback_check_external_assumptions):**
  1. `IConnectionMultiplexer` rate-limiting ile shared singleton — RateLimitingServiceCollectionExtensions.cs:29 ✓
  2. Hangfire startup `AddHangfire` + `AddHangfireServer` Program.cs:81 ✓; `RecurringJob.AddOrUpdate` static API mevcut
  3. User.PreferredLanguage + MobileAuthenticatorVerified + DefaultPayoutAddress/DefaultRefundAddress hazır — User.cs:17-32 ✓
  4. RefreshToken entity + `ReplacedByTokenId`/`RevokedAt`/`IsRevoked`/`IsDeleted` hazır — T18 ✓; migration gereksiz
  5. Global soft-delete query filter tokenları `_db.Set<RefreshToken>()` query'lerinden otomatik hariç tutuyor (AppDbContext.cs:130-141 `ApplySoftDeleteFilter`) — cleanup job için `.IgnoreQueryFilters()` gerekmiyor, job zaten IsDeleted=false satırları görüyor ve soft-delete işaretliyor
- **DI scope düzeltmesi (aksiyonlu ders):** İlk impl'de `RefreshTokenCleanupJobRegistrar` scoped `IBackgroundJobScheduler`'ı doğrudan ctor ile aldı → `ASP.NET Core` DI validator'ı "singleton IHostedService cannot consume scoped" diyerek 18 test host-build'de patladı. Fix: `IServiceScopeFactory` + `CreateScope` (F0 `OutboxStartupHook`'dan birebir pattern). Test regression onayladı: 66/66 restore. Memory notu: scoped background scheduler'ı hosted service'ten çekiyorsan her zaman scope factory. XML-doc'ta not edildi.
- **Bundled commit riski:** Yok — branch `task/T32-refresh-token` main'den taze açıldı, sadece T32 değişiklikleri.
- **07 API_DESIGN etkisi:** Yok — §4.5, §4.9, §4.10 halihazırda spec'lenmişti; implementasyon birebir uyumlu.
- **06 DATA_MODEL etkisi:** Yok — RefreshToken entity + config T18'de tamamlandı.

## Commit & PR

- **Branch:** `task/T32-refresh-token`
- **Commit:** `8a22c15` (T32 implementation) + `b65862d` (dotnet format fix)
- **PR:** #55 — https://github.com/turkerurganci/Skinora/pull/55
- **CI runs:**
  - `24830392733` — FAIL (4 whitespace lint errors; `AddOrUpdateRecurring` no-op stubs `{ }` aynı satırdaydı)
  - `24830517533` — ✓ 10/10 (Lint/Build/Unit/Integration/Contract/Migration/Docker/CI Gate). Guard skipped (PR context).
- **BYPASS_LOG entry:** 1× `[ci-failure]` Layer 2 — `b65862d` push'unda (pre-push guard prior-run failed'ı bloklamak istedi, fix commit'i kendi blocker'ıydı; bypass reason "this commit IS the fix — dotnet format applied").
