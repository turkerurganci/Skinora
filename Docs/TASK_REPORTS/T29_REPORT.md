# T29 — Steam OpenID authentication (login + callback + token üretimi)

**Faz:** F2 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-04-21

---

## Yapılan İşler
- `Skinora.Auth` modülüne `Application/SteamAuthentication/` uygulama katmanı eklendi: OpenID 2.0 assertion doğrulayıcı, Steam Web API profil istemcisi, JWT access token üretici, refresh token üretici + persistence, kullanıcı upsert (new/existing), return URL sanitizer, login audit servisi, geo-block + sanctions pipeline hook'ları (stub — T30/T82/T83 gerçek impl'i dolduracak), sonunda tüm akışı orkestre eden `SteamAuthenticationPipeline`.
- `Skinora.API`'ye `AuthController` (GET `/api/v1/auth/steam`, GET `/api/v1/auth/steam/callback`) + `SteamAuthenticationModule` DI kaydı + `Program.cs` wire eklendi.
- `SteamOpenIdSettings` (Realm, ReturnToUrl, FrontendCallbackUrl, DefaultReturnPath, WebApiKey) `appsettings.json` + `appsettings.Development.json`'a konumlandı.
- `Skinora.Auth.csproj`'a `Microsoft.EntityFrameworkCore`, `Microsoft.IdentityModel.Tokens`, `System.IdentityModel.Tokens.Jwt` paketleri eklendi.
- Test: 5 unit test dosyası (SteamIdParser, SteamOpenIdUrlBuilder, ReturnUrlValidator, AccessTokenGenerator, SteamAuthenticationPipeline — toplam 32 case) + 1 integration test dosyası (AuthSteamEndpointTests, 6 case).

## Etkilenen Modüller / Dosyalar
- **Yeni:**
  - `backend/src/Modules/Skinora.Auth/Configuration/SteamOpenIdSettings.cs`
  - `backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/` (13 dosya — interface + implementation + DTO'lar)
  - `backend/src/Skinora.API/Configuration/SteamAuthenticationModule.cs`
  - `backend/src/Skinora.API/Controllers/AuthController.cs`
  - `backend/tests/Skinora.Auth.Tests/Unit/` (5 dosya)
  - `backend/tests/Skinora.API.Tests/Integration/AuthSteamEndpointTests.cs`
  - `Docs/TASK_REPORTS/T29_REPORT.md` (bu dosya)
- **Değişen:**
  - `backend/src/Modules/Skinora.Auth/Skinora.Auth.csproj` — paket referansları
  - `backend/src/Skinora.API/Program.cs` — `AddSteamAuthenticationModule` çağrısı
  - `backend/src/Skinora.API/appsettings.json` + `appsettings.Development.json` — `SteamOpenId` bölümü

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `GET /auth/steam` → Steam OpenID sayfasına redirect | ✓ | `AuthSteamEndpointTests.GetSteam_RedirectsToSteamOpenIdLogin` |
| 2 | `GET /auth/steam/callback` → assertion doğrulama, kullanıcı oluşturma/güncelleme, JWT + refresh token üretimi | ✓ | `Callback_ValidAssertion_NewUser_CreatesUserAndSetsRefreshCookie` + `Callback_ValidAssertion_ExistingUser_UpdatesDisplayNameAndReturnsSuccess` |
| 3 | Güvenlik: assertion backend'de doğrulanır (`claimed_id` güvenilmez), return URL kontrolü, replay koruması, HTTPS zorunlu | ✓ | `SteamOpenIdValidator.ValidateAsync` Steam'e `check_authentication` POST eder + `openid.return_to` mismatch reject + `SteamIdParser` claimed_id prefix/format kontrolü. HTTPS `app.UseHttpsRedirection` ile pipeline'da zorlu (T05) |
| 4 | İlk kez giriş: ToS gösterilmeli (`tosAccepted` kontrolü) | ~ | `status=new_user` query param frontend'e döner; ToS modal UI + `/auth/tos/accept` endpoint T30 scope'unda (plan §T30). Backend tarafı: `User.TosAcceptedVersion/At` alanları T18'de mevcut, T30 dolduracak. |
| 5 | `returnUrl` sadece relative path kabul eder | ✓ | `ReturnUrlValidatorTests` 11 case + `GetSteam_InvalidReturnUrl_IsNotReflectedInRedirect` |
| 6 | `GetPlayerSummaries` çağrısı ile profil bilgileri çekilir | ✓ | `SteamProfileClient` — `x-webapi-key` header + `response.players[0]` parse; `WebApiKey` yoksa graceful degrade + placeholder display name |
| 7 | Geo-block kontrolü (IP bazlı) | ✓ (stub) | `IGeoBlockCheck` pipeline hook'u çağrılır; `AllowAllGeoBlockCheck` stub'ı default (T30/T83 gerçek impl). `SteamAuthenticationPipelineTests.ExecuteAsync_GeoBlocked_*` hook sözleşmesini doğrular |
| 8 | Sanctions eşleşmesi kontrolü | ✓ (stub) | `ISanctionsCheck` hook'u çağrılır; `NoMatchSanctionsCheck` stub default (T82 gerçek impl). `SteamAuthenticationPipelineTests.ExecuteAsync_SanctionsMatch_*` |
| 9 | Hesap askıya alınmış mı kontrolü (kısıtlı oturum) | ✓ (partial) | `User.IsDeactivated = true` → `AuthenticationOutcome.AccountBanned` → redirect `?error=account_banned`, refresh cookie issue'lanmaz. Suspended read-only session (kısıtlı oturum) — JWT hâlâ issue ediliyor mu + hangi claim'lerle: detay T30 (ToS flow) + T32 (me/refresh) ile netleşecek. |

Notlar: #4 ve #9 için "~/partial": Frontend UI + `/auth/tos/accept` endpoint T30'a, `/auth/me`/refresh T32'ye devrediliyor (kapsam netleştirmesi kullanıcı ile onaylandı). T29'da pipeline hook'ları ve data çıktıları (status=new_user, IsDeactivated kontrolü) mevcut.

### Doğrulama kontrol listesi (11 plan'dan)
- [x] 08 §2.1 güvenlik kuralları uygulanmış mı? → assertion `check_authentication`, return_to mismatch reject, claimed_id prefix, HTTPS (`UseHttpsRedirection`)
- [x] 03 §2.1 akış adımları karşılanmış mı? → redirect → Steam → callback → pipeline (geo/suspend/sanctions hook + provisioning + token + audit)
- [x] 07 §4.2–§4.3 endpoint sözleşmesi eşleşiyor mu? → 302 redirect, returnUrl sanitize, `/auth/callback?status=...|error=...`, refresh cookie `HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth`

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Unit (Skinora.Auth.Tests) | ✓ 32/32 passed | `dotnet test tests/Skinora.Auth.Tests/Skinora.Auth.Tests.csproj` — 120 ms |
| Integration (AuthSteamEndpointTests) | ✓ 6/6 passed | `dotnet test tests/Skinora.API.Tests --filter "FullyQualifiedName~AuthSteamEndpointTests"` — 1 sn |
| Regresyon (API.Tests tüm) | ✓ 105/105 passed | `dotnet test tests/Skinora.API.Tests --filter "FullyQualifiedName!~InitialMigrationTests"` — 3:12 dk (Migration testi lokal Docker gereksinimi nedeniyle hariç; CI Linux runner'da infra hazır olduğundan geçer) |
| Combined Auth + Hangfire | ✓ 22/22 passed | Parallel fixture race condition (JobStorage.Current global) giderildi — factory Hangfire kayıtlarını scrublayıp dashboard'u devre dışı bırakır, `NoopBackgroundJobScheduler` stub'ı `IBackgroundJobScheduler` için enjekte edilir |

Build: `dotnet build Skinora.sln` → 0 Warning, 0 Error.

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Beklemede (ayrı chat açılacak) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri
- **Migration:** Yok — User/RefreshToken/UserLoginLog T18'de mevcut, şema değişmedi.
- **Config/env:** `SteamOpenId` bölümü eklendi (`Realm`, `ReturnToUrl`, `FrontendCallbackUrl`, `DefaultReturnPath`, `WebApiKey`). Production'da `SteamOpenId__WebApiKey` env var ile set edilecek; dev'de boş → profil bilgileri placeholder ile doldurulur.
- **Docker:** Yok.
- **Yeni NuGet:** `Skinora.Auth.csproj` → `Microsoft.EntityFrameworkCore` 9.0.3, `Microsoft.IdentityModel.Tokens` 8.0.1, `System.IdentityModel.Tokens.Jwt` 8.0.1.

## Commit & PR
- Branch: `task/T29-steam-openid-auth`
- Commit: (beklemede — bu raporla birlikte push'lanacak)
- PR: (push sonrası `gh pr create`)
- CI: (run'lar izlenecek)

## Known Limitations / Follow-up
- `IGeoBlockCheck` + `ISanctionsCheck` şu an no-op — T30/T82/T83 gerçek implementasyonu DI'da swap edecek (arayüz sabitlendi).
- `GetPlayerSummaries` rate limiting / retry yok — T81 Steam Market fiyat API ile birlikte genel Steam Web API istemci toleransı eklenebilir.
- Brute-force progressive delay / temporary lock (07 §4.2 "brute force koruması") T07 rate limit'in ötesinde değil; login lock counter T32 refresh token ailesiyle veya ayrı bir enhancement task'ı olarak düşünülmeli.
- `/auth/tos/accept`, `/auth/refresh`, `/auth/me`, `/auth/logout` → T30 + T32 scope'u. ToS modal UI → T87 auth ekranları.
- Re-verify + authenticator → T31.

## Notlar
- **Working tree check (task.md Adım -1):** temiz (git status --short → boş).
- **Main CI startup check (task.md Adım 0):** son 3 run ✓ success (24710222019, 24710222028, 24690294086).
- **Dış Varsayımlar (task.md Adım 4):**
  - Steam OpenID 2.0 endpoint (`https://steamcommunity.com/openid/login`) — stabil, 3. parti paket gerekmiyor (manual HTTP POST + URL parse). Kanıt: 08 §2.1 + Steam Partner docs `partner.steamgames.com/doc/features/auth`.
  - `JwtSecurityTokenHandler` için `Microsoft.IdentityModel.Tokens` + `System.IdentityModel.Tokens.Jwt` (8.0.1) — nuget.org'da mevcut, API projesinde zaten kullanılıyor (`AuthenticationTests`).
  - Steam Web API key (opsiyonel) — dev'de placeholder, prod env var'dan. `GetPlayerSummaries` key yoksa warning + placeholder display name akışı implement edildi (graceful degrade).
  - 03 §2.1 + 05 §3.1: GetPlayerSummaries backend'den direkt çağrılır (sidecar değil) — sidecar trade/inventory için (08 §2.3-§2.4).
- **Scope kararı:** Geo-block (T30+T83) ve sanctions (T82) gerçek implementasyonu T29'a çekilmedi — pipeline hook'u ile stub default kullanıldı. Gerekçe: plan ayrımını koru, T29 scope'unu şişirme, T30/T82/T83 rework olmadan DI swap ile gerçek impl devralır. Kullanıcı onaylı.
- **Test altyapı öğrenimi:** `AuthSteamEndpointTests.Factory` `HangfireBypassFactory`'den DEĞİL, plain `WebApplicationFactory<Program>`'dan türetildi. Sebep: xUnit parallel fixture execution + Hangfire'in `JobStorage.Current` process-wide static'i birlikte race condition oluşturuyor — benim factory disposed olduktan sonra HangfireTests'in BackgroundJobClient'ı stale/disposed InMemoryStorage ile sonlanabiliyor. Çözüm: factory Hangfire kayıtlarını tamamen scrublar (dashboard `Hangfire:DashboardEnabled=false` ile devre dışı) ve `IBackgroundJobScheduler` için no-op stub enjekte edilir. Böylece `JobStorage.Current` globaline dokunulmuyor, sibling Hangfire tests temiz kalıyor.
