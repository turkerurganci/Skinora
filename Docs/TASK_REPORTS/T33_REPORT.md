# T33 — User profil servisi

**Faz:** F2 | **Durum:** ⏳ Yapım tamamlandı — doğrulama bekliyor | **Tarih:** 2026-04-23

---

## Yapılan İşler

- **`GET /api/v1/users/me` (U1, 07 §5.1):** `[Authorize(Authenticated)]` + `[RateLimit("user-read")]`. JWT'den `AuthClaimTypes.UserId` → `IUserProfileService.GetOwnProfileAsync(userId, ct)` → `UserProfileDto`. DTO alanları 07 §5.1 kontratıyla birebir: `id, steamId, displayName, avatarUrl, accountAge, createdAt, reputationScore, completedTransactionCount, successfulTransactionRate, cancelRate, sellerWalletAddress, refundWalletAddress, mobileAuthenticatorActive`. Kullanıcı bulunamaz veya `IsDeactivated=true` ise 401 (token geçerli ama kullanıcı artık aktif değil — oturumu sonlandır).
- **`GET /api/v1/users/me/stats` (U2, 07 §5.2):** Dashboard (S05) için hafif DTO — `completedTransactionCount`, `successfulTransactionRate`, `reputationScore`. Projection EF Core seviyesinde (`.Select(...)`) → gereksiz alanlar DB'den çekilmez.
- **`GET /api/v1/users/{steamId}` (U5, 07 §5.5):** `[AllowAnonymous]` + `[RateLimit("public")]`. Public profil — cüzdan adresleri ve `cancelRate` döndürülmez. 404 `USER_NOT_FOUND` envelope'u (`ApiResponse<object>.Fail("USER_NOT_FOUND", ...)` + `NotFound(body)`) — 07 §5.5 error code sözleşmesine uyar.
- **`IUserProfileService` + `UserProfileService`:** `AppDbContext` + `TimeProvider` bağımlılığı. Üç metod, hepsi `AsNoTracking()` + `!IsDeactivated` filtre (06 §1.3 predicate + SYSTEM seed'in operational sorgulardan dışlanması). Soft-delete zaten global query filter'da → `IsDeleted=true` satırlar otomatik saklanır.
- **`AccountAgeFormatter`:** Pure helper — `CreatedAt` ve `now` alır, Türkçe relative string üretir (`"3 gün"` < 30, `"6 ay"` 30 gün–12 ay, `"2 yıl"` 12 ay+). API Türkçe verbatim emit eder; i18n T97 konusu.
- **DI modülü (`UsersModule`):** `AddUsersModule()` → `TimeProvider` (idempotent — zaten kayıtlı değilse) + `IUserProfileService` scoped. `Program.cs` `AddSteamAuthenticationModule`'den hemen sonra çağırır.
- **Rate-limit seçimi:** `user-read` (authenticated own-profile reads) + `public` (anonymous endpoints) bucket'ları. Auth-bucket kullanmadım; login-flavored değil.

## Known Limitations (dokümansal devirler)

- **`reputationScore` ve `cancelRate` `null` döner** — gerçek hesaplama **T43** (User itibar skoru hesaplama) ve muhtemelen T52 kapsamında. DTO shape 07 §5.1/§5.2/§5.5 ile 1:1 uyumlu, sadece değerler null. T29 (MA stub) / T30 (sanctions stub) ile aynı forward-devir deseni.
- **`mobileAuthenticatorActive` `User.MobileAuthenticatorVerified` flag'ini yansıtır** — Gerçek Steam MA kontrolü T64–T69 sidecar'ıyla gelecek; T31 stub davranışı bu flag'i bugün sınırlı anlamlı kılıyor.
- **`accountAge` Türkçe verbatim** — `"6 ay"`, `"1 yıl"` gibi. Multilanguage (T97 i18n) gelince backend bunu culture-aware yapmalı veya frontend `createdAt` üzerinden format etmeli; `createdAt` DTO'da zaten mevcut → frontend alternative path kapalı değil.
- **Yeni entity kolonu / migration yok.** Mevcut User entity 07 §5'teki tüm mapping'leri karşılıyor.

## Etkilenen Modüller / Dosyalar

**Skinora.Users (yeni Application/Profiles alt paketi):**
- `backend/src/Modules/Skinora.Users/Application/Profiles/IUserProfileService.cs`
- `backend/src/Modules/Skinora.Users/Application/Profiles/UserProfileService.cs`
- `backend/src/Modules/Skinora.Users/Application/Profiles/UserProfileDtos.cs` — `UserProfileDto`, `UserStatsDto`, `PublicUserProfileDto`
- `backend/src/Modules/Skinora.Users/Application/Profiles/AccountAgeFormatter.cs`

**API katmanı:**
- `backend/src/Skinora.API/Controllers/UsersController.cs` — 3 endpoint
- `backend/src/Skinora.API/Configuration/UsersModule.cs` — DI
- `backend/src/Skinora.API/Program.cs` — `AddUsersModule()` kaydı (1 satır)

**Testler:**
- `backend/tests/Skinora.API.Tests/Integration/UserProfileEndpointTests.cs` — 5 integration test (pattern: SQLite in-memory factory, AuthSessionEndpointTests mirror)

## Kabul Kriterleri Kontrolü

| # | Kriter (11 §T33) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `GET /users/me` → kendi profil (wallet adresleri, skor, istatistikler) | ✓ | `UsersController.GetMe` + `UserProfileService.GetOwnProfileAsync`. `UserProfileEndpointTests.GetMe_Authenticated_ReturnsOwnProfile` — 07 §5.1 DTO alanları tek tek assert (id, steamId, displayName, avatarUrl, accountAge="6 ay", successfulTransactionRate=0.96, reputationScore=null, cancelRate=null, sellerWalletAddress, refundWalletAddress, mobileAuthenticatorActive=true). `GetMe_Unauthenticated_Returns401` auth gate. |
| 2 | `GET /users/me/stats` → dashboard hızlı istatistikler | ✓ | `UsersController.GetMyStats` + `UserProfileService.GetOwnStatsAsync` (EF projection). `UserProfileEndpointTests.GetMyStats_Authenticated_ReturnsStatsDto` — 07 §5.2 3 alan assert. |
| 3 | `GET /users/:steamId` → public profil (sınırlı alanlar) | ✓ | `UsersController.GetPublic` + `UserProfileService.GetPublicProfileAsync`. `UserProfileEndpointTests.GetPublic_ExistingUser_ReturnsLimitedProfile` — wallet adresleri ve cancelRate response'ta **yok** (`!TryGetProperty`), sadece 07 §5.5 7 alan. `GetPublic_MissingUser_Returns404WithUserNotFoundCode` — 404 + `error.code="USER_NOT_FOUND"`. |

## Doğrulama Kontrol Listesi (11 §T33)

- [x] 07 §5.1–§5.5 response DTO'ları doğru mu? — 3 DTO (`UserProfileDto`, `UserStatsDto`, `PublicUserProfileDto`) 07 §5.1/§5.2/§5.5 alanlarıyla 1:1 (reputationScore/cancelRate null — T43/T52 forward devir, above).

## Test Sonuçları

**Birim + integration (lokal, Release):**

| Test projesi | Geçen | Başarısız | Süre |
|---|---|---|---|
| `Skinora.Shared.Tests` (unit) | 150 | 0 | 578 ms |
| `Skinora.Auth.Tests` (unit) | 54 | 0 | 816 ms |
| `Skinora.API.Tests` (unit filter) | 15 | 0 | 83 ms |
| `Skinora.API.Tests` (**full** — integration + unit) | 138 | 0 | 3 dk 13 sn |

UserProfileEndpointTests 5/5 PASS (unauth 401, auth own profile, auth stats, public profile, public 404).

**Format:**
```bash
dotnet format --verify-no-changes --no-restore Skinora.sln
```
→ 0 W / 0 E.

**Build:**
```bash
dotnet build -c Release --nologo
```
→ Build succeeded. 0 Warning(s). 0 Error(s). 00:00:11.09.

## Altyapı Değişiklikleri

- **Yeni test helper pattern'i yok** — UserProfileEndpointTests kendi `Factory` sınıfıyla SQLite in-memory kullanır (AuthSessionEndpointTests mirror'ı). `NullRefreshTokenCache` + `InMemoryRateLimiterStore` + `NoopBackgroundJobScheduler` + `InMemoryDistributedLockProvider` — hepsi mevcut Common/Shared altyapısından.
- **`CreateUserAsync` iki-aşamalı save:** `AppDbContext.UpdateAuditFields` Added state'te `CreatedAt`'i `UtcNow`'a çekiyor. Test factory önce Add+Save, sonra desired `CreatedAt` değerini yazıp tekrar Save — Modified state'te audit pipeline sadece `UpdatedAt`'i güncelliyor, `CreatedAt` saygı görüyor. `AccountAge` assertion'ları bu sayede deterministik.
- **Migration yok.** Yeni kolon yok.
- **Yeni NuGet bağımlılığı yok.**

## Notlar

- **Working tree (Adım -1):** Temiz (kullanıcı smoke-test comment'ini session içinde geri aldı).
- **Startup CI check (Adım 0):** 3/3 `success` — run ID'ler `24833617580`, `24833617592`, `24827836299`.
- **Dış varsayımlar (Adım 4):** Aşağıdaki 4 varsayımın hepsi repo state ile doğrulandı — yeni dış bağımlılık yok, plan tier / API quota etkisi yok:
  - Auth policy `Authenticated` + `AllowAnonymous` mevcut (`AuthPolicies.cs`) ✓
  - User entity'de `SteamDisplayName`, `SteamAvatarUrl`, `DefaultPayoutAddress`, `DefaultRefundAddress`, `MobileAuthenticatorVerified`, `CompletedTransactionCount`, `SuccessfulTransactionRate`, `IsDeactivated` alanları mevcut (`User.cs`, T18) ✓
  - `ApiResponseWrapperFilter` success response'ları otomatik `{ success, data, traceId }` envelope'a sarar (`ApiResponseWrapperFilter.cs`) ✓
  - Rate-limit bucket'ları `user-read` + `public` kayıtlı (`RateLimitOptions.cs` → `DiagnosticsController.cs`'te kullanımları mevcut) ✓
- **Bundled-PR check (Bitiş Kapısı):** `git log main..HEAD` sadece `T33` commit'leri döner (aşağıdaki Commit & PR bölümü).
- **Post-merge CI watch:** Merge sonrası main CI izlenecek (validate.md Adım 18 + post-merge-ci-reminder hook gereği).

## Commit & PR

- **Branch:** `task/T33-user-profile`
- **Commit (kod):** `1ba4604`
- **Commit (rapor+status+memory):** `8f52e26`
- **PR:** [#56](https://github.com/turkerurganci/Skinora/pull/56)
- **CI run:** [`24836165946`](https://github.com/turkerurganci/Skinora/actions/runs/24836165946) — 10/10 job ✓ (Lint + Build + Unit + Integration + Contract + Migration dry-run + Docker build + CI Gate).

## Sırada

T33 doğrulama ✓ PASS → T34 (Cüzdan adresi yönetimi — `PUT /users/me/wallet/seller` + `PUT /users/me/wallet/refund`, 07 §5.3–§5.4; bağımlılık: T31 ✓, T33 ✓).
