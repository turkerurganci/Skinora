# T29 — Steam OpenID authentication (login + callback + token üretimi)

**Faz:** F2 | **Durum:** ⏳ S1 düzeltme uygulandı, re-doğrulama bekliyor | **Tarih:** 2026-04-21

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
| Doğrulama durumu | ✗ FAIL |
| Doğrulama tarihi | 2026-04-21 |
| Doğrulama branch/commit | `task/T29-steam-openid-auth` @ `b0101e7` |
| Bulgu sayısı | 1 (S1 Sapma) |
| Düzeltme gerekli mi | Evet — merge engellendi |

### HARD STOP Kapıları
- **Adım -1 Working tree:** ✓ temiz (`git status --short` boş).
- **Adım 0 Main CI startup (son 3 run):** ✓ 24710222019, 24710222028, 24690294086 tamamı `success`.
- **Adım 0b Repo memory drift:** ✓ `.claude/memory/MEMORY.md` T29 satırları mevcut (L11 "Sırada F2 … T29 Steam OpenID auth ilk task", L22 T29 özet, L23 "Next: T29 doğrulama → T30").
- **Task branch CI:** ✓ run 24737739392 (sha `b0101e7`) `conclusion=success`.

### Kabul Kriterleri (Validator Tekrar Değerlendirme)
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `GET /auth/steam` → Steam OpenID redirect | ✓ | `AuthController.cs:38-54`; `GetSteam_RedirectsToSteamOpenIdLogin` PASS |
| 2 | `GET /auth/steam/callback` → assertion doğrula + user upsert + JWT + refresh | ✓ (kısmi — #refresh storage S1) | 6 integration test PASS; ancak refresh DB'ye plain text yazılıyor (aşağıda S1) |
| 3a | Assertion backend'de doğrulanır (claimed_id güvenilmez) | ✓ | `SteamOpenIdValidator` Steam'e `check_authentication` POST; `SteamIdParser` prefix/format guard |
| 3b | Return URL kontrolü | ✓ | `ReturnUrlValidator` absolute/protocol-relative/backslash reddeder; `SteamOpenIdValidator` `openid.return_to` ile `settings.ReturnToUrl` ordinal eşleşmesi zorlar |
| 3c | Nonce replay koruması | ✓ (dolaylı) | Yerel nonce tracking yok; Steam `check_authentication` aynı assoc_handle+nonce için tekrar `is_valid:true` vermediği için OpenID 2.0 §11.4 RP sorumluluğu karşılanır |
| 3d | HTTPS zorunlu | ✓ | `Program.cs:131 UseHttpsRedirection` + tüm cookie'ler `Secure=true` |
| 4 | İlk giriş: ToS gösterilmeli (tosAccepted) | ~ Kısmi | Backend: `?status=new_user` işareti atar; ToS endpoint + modal T30. Rapor/plan açık olarak T30 scope, kullanıcı onaylı. |
| 5 | `returnUrl` sadece relative path | ✓ | `ReturnUrlValidatorTests` 11 case + `GetSteam_InvalidReturnUrl_IsNotReflectedInRedirect` cookie'de "dashboard" gözlendi |
| 6 | `GetPlayerSummaries` çağrısı | ✓ | `SteamProfileClient` `x-webapi-key` header, `response.players[0]` parse; `WebApiKey` yoksa graceful degrade (placeholder display name) |
| 7 | Geo-block kontrolü (IP bazlı) | ✓ (stub) | `IGeoBlockCheck` pipeline hook çağrılır; `AllowAllGeoBlockCheck` default (T30/T83 DI swap). Pipeline sözleşmesi `SteamAuthenticationPipelineTests` ile doğrulanır. |
| 8 | Sanctions eşleşmesi kontrolü | ✓ (stub) | `ISanctionsCheck` pipeline hook çağrılır; `NoMatchSanctionsCheck` default (T82 DI swap). |
| 9 | Hesap askıya alınmış mı kontrolü | ✓ | `IsDeactivated=true` → `AccountBanned` outcome → `?error=account_banned`, refresh cookie yok. `Callback_DeactivatedUser_*` test PASS. "Suspended read-only session" kısıtlı oturum semantiği (JWT ile kısıtlı claim) — T30/T32 scope'u. |

### Doğrulama Kontrol Listesi (11 plan'dan)
- [x] 08 §2.1 güvenlik kuralları uygulanmış mı? → assertion + return_to mismatch + claimed_id + HTTPS karşılanır; replay Steam side'a delege (kabul)
- [x] 03 §2.1 akış adımları karşılanmış mı? → redirect → callback → geo/sanctions/suspend hook + provisioning + token + audit
- [x] 07 §4.2–§4.3 endpoint sözleşmesi eşleşiyor mu? → 302 redirect, returnUrl sanitize, cookie contract (`HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth`) eşleşir

### Test Sonuçları (Validator Tekrar Çalıştırma)
| Tür | Sonuç | Komut | Süre |
|---|---|---|---|
| Unit — Skinora.Auth.Tests | ✓ 32/32 | `dotnet test tests/Skinora.Auth.Tests/Skinora.Auth.Tests.csproj` | 228 ms |
| Integration — AuthSteamEndpointTests | ✓ 6/6 | `dotnet test tests/Skinora.API.Tests --filter "FullyQualifiedName~AuthSteamEndpointTests"` | 2 s |
| Build (Release) | ✓ 0 Warning / 0 Error | `dotnet build -c Release` | 9.88 s |

### Güvenlik Kontrolü
- [x] Secret sızıntısı: **Temiz** — `WebApiKey` `x-webapi-key` header'dan (URL değil), `JwtSettings.Secret` `IOptions` üzerinden, log'larda yok.
- [x] Auth etkisi: Yeni auth akışı eklendi — rate limit `[RateLimit("auth")]` uygulanır, `[AllowAnonymous]` yalnızca `/auth/steam` + `/auth/steam/callback`.
- [x] Input validation: `returnUrl` sanitizer, `claimed_id` prefix + 17 haneli SteamID64 regex, `openid.return_to` strict eşleşme, UA/IP truncate (45/256).
- [x] Yeni bağımlılık: `Microsoft.EntityFrameworkCore` 9.0.3, `Microsoft.IdentityModel.Tokens` 8.0.1, `System.IdentityModel.Tokens.Jwt` 8.0.1 — solution'daki diğer projelerle versiyon hizalı.
- [✗] **DB secret at-rest:** Refresh token plain text saklanıyor — 06 §3.3 ihlali (aşağıda S1).

### Bulgular
| # | Seviye | Açıklama | Etkilenen dosya |
|---|---|---|---|
| 1 | **S1 Sapma** | `RefreshTokenGenerator.IssueAsync` refresh token'ı plain text olarak DB'ye yazıyor (`Token = plainText`, satır 35). **06 §3.3 L441** açıkça diyor: *"`Token` \| string(256) \| UNIQUE, NOT NULL \| Refresh token'ın SHA-256 hash'i — plain text saklanmaz, DB breach'e karşı koruma"*. Beklenen: plain text cookie'ye (client round-trip), `SHA256(plainText)` → DB `Token` kolonu. Ayrıca `GeneratedRefreshToken.PlainTextToken` ile entity aynı string'i taşıyor (`RefreshTokenGenerator.cs:45` `new GeneratedRefreshToken(entity, plainText, ...)`). **Güvenlik etkisi:** DB breach senaryosunda aktif refresh token'lar plain text olduğu için hemen oturum çalmaya kullanılabilir; SHA-256 hash saklanırsa attacker cookie'yi türetemez. **Testler yakalamadı** çünkü `AuthSteamEndpointTests` yalnız `Assert.Single(...)` ile satır sayısını doğruluyor, DB token içeriğini cookie ile karşılaştırmıyor. **Düzeltme:** (1) `IssueAsync` içinde `Token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainText)))` (veya Base64url) yap; (2) refresh path'inde (T32) incoming cookie `SHA256` ile hash'lenip `RefreshTokens.Token` kolonunda arat; (3) test: DB'deki `Token`'ın cookie ile eşit OLMADIĞINI ve `SHA256(cookie) == Token` olduğunu assert et. | `backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/RefreshTokenGenerator.cs:35, 45`; `backend/tests/Skinora.API.Tests/Integration/AuthSteamEndpointTests.cs:93` (test eksikliği) |

### Yapım Raporu Karşılaştırması
- Yapım raporu kabul kriterleri #2 ve #3'ü ✓ olarak işaretlemiş, ama #2 "JWT + refresh token üretimi" kısmında 06 §3.3 spec ihlali var.
- Yapım raporu "Güvenlik" başlığı altında bu kuralı denetlemedi; at-rest hashing standardı Shared/sözleşme literatüründe değil yalnız 06 §3.3 data model'inde belirtilmiş — yapım chat'i entity spec'ini ayrıntılı okumamış olabilir.
- Stub'lar (geo/sanctions) ve #4/#9 partial'ları kapsam ayrımı olarak kabul edilebilir (plan T30/T82/T83 bunları devralıyor, arayüz sabit) — bunlar bulgu değil.

### Verdict: ✗ FAIL

**Merge engellendi.** Yeni yapım chat'inde S1 bulgusu düzeltilecek, ardından yeni doğrulama chat'i açılacak.

### S1 Düzeltme (2026-04-21, fix chat)
- **Kod:** [`RefreshTokenGenerator.cs`](../../backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/RefreshTokenGenerator.cs) — `Token = plainText` yerine `Token = HashToken(plainText)`; `HashToken` = `Convert.ToHexString(SHA256.HashData(UTF8(plainText)))` (64 hex karakter, 06 §3.3 `string(256)` sınırı içinde). `GeneratedRefreshToken.PlainTextToken` değişmedi → cookie hâlâ plain text alır, DB yalnız hash saklar.
- **Test:** [`AuthSteamEndpointTests.cs`](../../backend/tests/Skinora.API.Tests/Integration/AuthSteamEndpointTests.cs) `Callback_ValidAssertion_NewUser_CreatesUserAndSetsRefreshCookie` — Set-Cookie'den refreshToken değerini parse eder, DB'deki `RefreshToken.Token` ile `Assert.NotEqual(cookie, stored)` ve `Assert.Equal(SHA256(cookie), stored)` asserten. Hash eşitliği + plain text leak yokluğu birlikte doğrulanır.
- **Sonuç:** Release build 0W/0E; `Skinora.Auth.Tests` 32/32 ✓ (184 ms); `AuthSteamEndpointTests` 6/6 ✓ (2 s); `Skinora.API.Tests` regresyon 105/105 ✓ (3:08, InitialMigrationTests hariç — lokal Docker gereksinimi). Validator'ın önerdiği 3 maddelik düzeltmenin (1) + (3)'ü bu fix'te; (2) T32 refresh path scope'unda kalır (plan §T32 incoming cookie → `SHA256` lookup).
- **Kapsam dışı:** T32 `/auth/refresh` endpoint implementasyonu (cookie → hash → DB lookup) — plan ayrımına sadık, bu PR'da yok.

### S1 Düzeltme — Test Infra Follow-up (2026-04-21, commit `e6e102a` sonrası CI kırılması)
- **Belirti:** CI run `24739083342` (sha `e6e102a`) integration test job'unda 6/96 test FAIL: tüm `AuthSteamEndpointTests` case'leri `System.InvalidOperationException : Cannot create a DbSet for 'RefreshToken' because this type is not included in the model for the context` ile patladı (0.001 s süresinde, constructor'daki `ResetFakes` satırında). Aynı test bir önceki run `24737739392` (sha `b0101e7`)'de PASS idi.
- **Kök neden (EF model cache flakiness):** `AppDbContext` modeli `IModelSource` tarafından **DbContext tipine göre process-statik** cache'lenir. `IntegrationTestBase` türevi test sınıfları (OutboxTests, EfCoreGlobalConfigTests, HangfireTests) `Program.cs`'yi çalıştırmadan doğrudan `AppDbContext` kurar; bu sınıflardan biri, `WebApplicationFactory<Program>` tabanlı bir test sınıfından (AuthSteamEndpointTests, AuthenticationTests) **önce** DbContext açarsa modül kayıtları henüz `Program.cs`'de yapılmamış olduğu için model eksik entity kümesiyle cache'lenir ve sonrasında gelen WebApplicationFactory testleri bu bozuk cache'i devralır. Sıralama xUnit collection paralel tarama kararına bağlı — CI'da shared MSSQL pre-warm olduğu için OutboxTests çok hızlı başlar; lokal'de MsSqlContainer spin-up süresi sayesinde WebApplicationFactory testleri önce tamamlanır. TRX timeline'ından doğrulandı: previous PASS run'da ilk test `MiddlewarePipelineTests` (WebApplicationFactory), FAIL run'da ilk test `OutboxTests` (IntegrationTestBase).
- **Düzeltme:** [`backend/tests/Skinora.API.Tests/TestAssemblyModuleInitializer.cs`](../../backend/tests/Skinora.API.Tests/TestAssemblyModuleInitializer.cs) yeni dosya — `[ModuleInitializer]` ile assembly yüklemesi sırasında (herhangi bir test sınıfı instantiate edilmeden önce) 10 modül `Register*Module()` çağrısı yapılır. Böylece `_moduleAssemblies` listesi **her zaman** tam, ilk `AppDbContext` hangi test tarafından yaratılırsa yaratılsın cache'lenen model eksik kalmaz. Üretim kodu (Skinora.API/Program.cs) etkilenmez; yalnızca test assembly'sinin yükleme sırası deterministikleştirilir.
- **Not:** Bu latent flakiness T29'un hatası değil — model cache semantiği entity modülleri eklendiği günden beri olası idi; xUnit discovery/collection sıralamasının şansına bağlı olarak T29'a kadar maskelenmişti. F1 Gate Check (PR #44) bu şansın bir diğer örneği.
- **Doğrulama:** Release build 0W/0E; `Skinora.API.Tests` tam koşu 105/105 ✓ (3:08); yeni `TestAssemblyModuleInitializer` assembly load'da sessiz çalışır (test/log çıktısı değişmez).
- **CI:** Run `24740092503` @ `3583c17` ✓ PASS (Integration test 3m31s + CI Gate).
- **BYPASS_LOG:** T11.2 Layer 2 (ci-failure) 1× entry — `f99d565` push (fix'i içeren commit) pre-push hook tarafından engellendi, `SKINORA_ALLOW_DIRECT_PUSH=1` + reason ile geçildi. Entry hook tarafından otomatik yazıldı, commit `3583c17`.

## Altyapı Değişiklikleri
- **Migration:** Yok — User/RefreshToken/UserLoginLog T18'de mevcut, şema değişmedi.
- **Config/env:** `SteamOpenId` bölümü eklendi (`Realm`, `ReturnToUrl`, `FrontendCallbackUrl`, `DefaultReturnPath`, `WebApiKey`). Production'da `SteamOpenId__WebApiKey` env var ile set edilecek; dev'de boş → profil bilgileri placeholder ile doldurulur.
- **Docker:** Yok.
- **Yeni NuGet:** `Skinora.Auth.csproj` → `Microsoft.EntityFrameworkCore` 9.0.3, `Microsoft.IdentityModel.Tokens` 8.0.1, `System.IdentityModel.Tokens.Jwt` 8.0.1.

## Commit & PR
- Branch: `task/T29-steam-openid-auth`
- Commit: `62ffead`
- PR: [#46](https://github.com/turkerurganci/Skinora/pull/46)
- CI: (run izleniyor)

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
