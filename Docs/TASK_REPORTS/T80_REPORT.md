# T80 — Discord entegrasyonu (Bot API + OAuth2 + DM channel cache)

**Faz:** F4 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-05-18

---

## Yapılan İşler

- **`Skinora.Shared/Discord/` transport katmanı** (yeni klasör, T79 Telegram + T78 Email paterni mirror) — `DiscordSettings` (consolidated: OAuth + bot transport + DM cache + rate limit), `IDiscordOAuthClient`/`DiscordOAuthClient` (real HttpClient: `POST /oauth2/token` x-www-form-urlencoded + `GET /users/@me` Bearer), `IDiscordBotClient`/`DiscordBotClient` (raw HttpClient: `POST /users/@me/channels` createDM + `POST /channels/{id}/messages` sendMessage + `allowed_mentions: { "parse": [] }` zorunlu + `Authorization: Bot {token}` header), `DiscordBotExceptions` (Transient/Permanent/Unauthorized/Forbidden + `DiscordForbiddenReason` taxonomy + `DiscordOAuthExchangeException`/`DiscordOAuthFailureReason`), `DiscordBotModels` (CreateDM/SendMessage request+result records + `DiscordProfile` Shared'a taşındı), `DiscordMarkdownEscaper` (7 reserved char: `*` `_` `~` `` ` `` `>` `|` `\`), `IDiscordRateLimiter`/`DiscordRateLimiter` (header-driven per-bucket `SemaphoreSlim` map + `X-RateLimit-Bucket` → stable-to-Discord-bucket mapping + global sliding window + `Retry-After`/`X-RateLimit-Reset-After` honor + global-flag pause + injected `Func<DateTimeOffset>` clock for test), `IDiscordDmChannelCache`/`InMemoryDiscordDmChannelCache` (test/dev fallback).
- **`DiscordOAuthClient` error mapping** (08 §6.4 OAuth2 hata tablosu):
  - 200 + access_token + 200 /users/@me → `DiscordProfile`
  - 4xx + body içeriği "invalid_grant" / 4xx body yok → `DiscordOAuthExchangeException(InvalidGrant, 400)`
  - 5xx / transport / timeout / parseable-OK-but-no-access_token → `DiscordOAuthExchangeException(TokenExchangeFailed)`
  - /users/@me non-OK → `DiscordOAuthExchangeException(UsersMeFailed)`
- **`DiscordBotClient` error mapping** (08 §6.4 Bot API hata tablosu):
  - 2xx → `DiscordDmChannel` / `DiscordSendMessageResult`
  - 401 → `DiscordUnauthorizedException` (bot token revoke → admin alert, preference KORUNUR)
  - 403 + createDM → `DiscordForbiddenException(MutualGuildRequired)`
  - 403 + sendMessage (code 50007) → `DiscordForbiddenException(DmClosed)`
  - 403 + sendMessage (diğer code) → `DiscordForbiddenException(Unknown)`
  - 404 → `DiscordPermanentException(404)`
  - 429 → `DiscordTransientException` + `IDiscordRateLimiter.RegisterRetryAfter(bucket, retry_after, isGlobal)` + `retry_after` float precision korunur
  - 5xx / transport → `DiscordTransientException`
  - Diğer 4xx → `DiscordPermanentException`
- **`DiscordRateLimiter`** — `X-RateLimit-Bucket` Discord-issued bucket id'yi `RegisterBucket(stableKey, discordBucket)` ile call-site stable key'e (`createDm`, `sendMessage:{channelId}`) map'ler; sonraki çağrılar canonical bucket gate'i üzerinden geçer. `X-RateLimit-Reset-After` `RegisterReset` ile sonraki call'ı pause eder. 429 + `global: true` → global gate `_globalRetryUntilUtc` pause; tüm bucket'lar bekler. Per-bucket gate `SemaphoreSlim(1,1)` + `NextSendAtUtc` timestamp.
- **`DiscordNotificationChannelHandler` real impl** (T37 stub'ın gerçek Bot API swap'i, `Skinora.Notifications/Infrastructure/Channels/`):
  - DM channel cache lookup → cache hit ise direkt sendMessage, miss ise createDM + cache.SetAsync
  - 404 + cache hit → cache invalidate + 1 retry (fresh createDM + sendMessage), ikinci 404 permanent path
  - 403 (Mutual/DmClosed/Unknown) → preference auto-disable + `PermanentChannelDeliveryException`
  - 401 → preference KORUNUR (admin sorunu) + `PermanentChannelDeliveryException`
  - 404 cache-miss → preference auto-disable + cache forget + `PermanentChannelDeliveryException`
  - 429/5xx → `TransientChannelDeliveryException` (limiter zaten retry-after register'lı)
  - Markdown envelope: `**title**\n\nbody` (Discord bold marker iki yıldız) + `DiscordMarkdownEscaper.Escape` her iki kanal
- **`LoggingDiscordNotificationChannelHandler` stub** (yeni dosya) — T37 default stub `DiscordNotificationChannelHandler` adından ayrıştırıldı. Provider switch: `Discord:Provider=discord` → real, default `logging` → stub (CI/dev fail-closed).
- **`RedisDiscordDmChannelCache`** (yeni, `Skinora.Notifications/Infrastructure/Channels/`) — key `{prefix}:discord:dm_channel:{discordUserId}`, TTL `DmChannelCacheTtlHours=24` default. Stale entries 404 ile auto-invalidate. Modül-side because Notifications zaten dispatcher orchestration'ı yapıyor, Shared'a StackExchange.Redis dep eklemekten kaçındık (T35 Redis stores pattern simetrisi).
- **`StubDiscordOAuthClient` extension** (Users-side, T80) — `invalid-*` prefix → `DiscordOAuthExchangeException(InvalidGrant, 400)`; `transport-fail-*` → `TokenExchangeFailed`. Integration test'lerin yeni callback dallarını gerçek client'a ihtiyaç duymadan exercise edebilmesi için.
- **`DiscordCallbackStatus.InvalidGrant`** (yeni enum value, 07 §5.13'te 08 §6.4 OAuth2 hata tablosuyla hizalama). `DiscordConnectionService.HandleCallbackAsync` try/catch ile `DiscordOAuthExchangeException` yakalar ve `InvalidGrant → InvalidGrant`, diğerleri `ExchangeFailed`'a map'ler.
- **`UsersController.DiscordCallback`** — yeni `InvalidGrant` redirect: `?reason=expired` (08 §6.4 invalid_grant satırı).
- **DI wiring (composition root):**
  - `Program.cs`: `DiscordSettings` bind + `IDiscordRateLimiter` singleton + provider switch ile koşullu `AddHttpClient<IDiscordOAuthClient, DiscordOAuthClient>` + `AddHttpClient<IDiscordBotClient, DiscordBotClient>` (provider=discord dışında hiçbir HttpClient DI'ya girmez → fail-closed).
  - `UsersModule.cs`: `DiscordSettings` bind kaldırıldı (Program.cs'e taşındı); provider=logging'te `IDiscordOAuthClient` → `StubDiscordOAuthClient` register; `using Skinora.Shared.Discord;` import güncellemesi.
  - `NotificationsModule.cs`: `IDiscordDmChannelCache` → `RedisDiscordDmChannelCache` singleton; provider switch ile `DiscordNotificationChannelHandler` (real) veya `LoggingDiscordNotificationChannelHandler` (stub) register.
  - `appsettings.json`: yeni `Discord` section (14 alan) — Provider="logging" default, ClientId/ClientSecret/BotToken `REPLACE_IN_ENV`, AuthorizeUrl/BaseUrl/RedirectUri/Scope defaults, StateTtlSeconds=600, SuccessRedirectUrl/FailureRedirectUrl, TimeoutSeconds=10, GlobalRatePerSecond=45, DmChannelCacheTtlHours=24, MaxRetries=3.
- **`Skinora.Users.Application.Settings.DiscordSettings` + `IDiscordOAuthClient` SİLİNDİ** — duplicate consolidated; tüm consumer'lar `Skinora.Shared.Discord` namespace'ini import eder. `DiscordConnectionService` + `StubDiscordOAuthClient` + `UsersController` + `UsersModule` import güncellemesi.
- **`Docs/INTEGRATION_RUNBOOKS/DISCORD_SETUP.md` (yeni)** — operasyon runbook: Developer Portal application + bot oluşturma, MVP guild install, secret rotation (3 senaryo: bot token / client secret / redirect URI), hata senaryoları (08 §6.4 mapping), izleme/limit tablosu, sandbox/staging/prod matris, yaygın hatalar.

## Etkilenen Modüller / Dosyalar

### Skinora.Shared (yeni `Discord/` klasörü)

- [`backend/src/Skinora.Shared/Discord/IDiscordOAuthClient.cs`](../../backend/src/Skinora.Shared/Discord/IDiscordOAuthClient.cs) (yeni — Users-side'dan taşındı)
- [`backend/src/Skinora.Shared/Discord/DiscordOAuthClient.cs`](../../backend/src/Skinora.Shared/Discord/DiscordOAuthClient.cs) (yeni — real HttpClient impl)
- [`backend/src/Skinora.Shared/Discord/IDiscordBotClient.cs`](../../backend/src/Skinora.Shared/Discord/IDiscordBotClient.cs) (yeni)
- [`backend/src/Skinora.Shared/Discord/DiscordBotClient.cs`](../../backend/src/Skinora.Shared/Discord/DiscordBotClient.cs) (yeni — createDM + sendMessage + 403 reason classify)
- [`backend/src/Skinora.Shared/Discord/DiscordBotExceptions.cs`](../../backend/src/Skinora.Shared/Discord/DiscordBotExceptions.cs) (yeni — Transient/Permanent/Unauthorized/Forbidden + DiscordForbiddenReason + DiscordOAuthExchangeException + DiscordOAuthFailureReason)
- [`backend/src/Skinora.Shared/Discord/DiscordBotModels.cs`](../../backend/src/Skinora.Shared/Discord/DiscordBotModels.cs) (yeni — DTO records + DiscordProfile)
- [`backend/src/Skinora.Shared/Discord/DiscordSettings.cs`](../../backend/src/Skinora.Shared/Discord/DiscordSettings.cs) (yeni — consolidated, eski Users-side silindi)
- [`backend/src/Skinora.Shared/Discord/DiscordMarkdownEscaper.cs`](../../backend/src/Skinora.Shared/Discord/DiscordMarkdownEscaper.cs) (yeni — 7 char escape)
- [`backend/src/Skinora.Shared/Discord/IDiscordRateLimiter.cs`](../../backend/src/Skinora.Shared/Discord/IDiscordRateLimiter.cs) (yeni — interface)
- [`backend/src/Skinora.Shared/Discord/DiscordRateLimiter.cs`](../../backend/src/Skinora.Shared/Discord/DiscordRateLimiter.cs) (yeni — header-driven bucket + global)
- [`backend/src/Skinora.Shared/Discord/IDiscordDmChannelCache.cs`](../../backend/src/Skinora.Shared/Discord/IDiscordDmChannelCache.cs) (yeni — interface)
- [`backend/src/Skinora.Shared/Discord/InMemoryDiscordDmChannelCache.cs`](../../backend/src/Skinora.Shared/Discord/InMemoryDiscordDmChannelCache.cs) (yeni — test fallback)

### Skinora.Notifications

- [`backend/src/Modules/Skinora.Notifications/Infrastructure/Channels/DiscordNotificationChannelHandler.cs`](../../backend/src/Modules/Skinora.Notifications/Infrastructure/Channels/DiscordNotificationChannelHandler.cs) — T37 stub'tan gerçek Bot API impl'e dönüşüm
- [`backend/src/Modules/Skinora.Notifications/Infrastructure/Channels/LoggingDiscordNotificationChannelHandler.cs`](../../backend/src/Modules/Skinora.Notifications/Infrastructure/Channels/LoggingDiscordNotificationChannelHandler.cs) (yeni — T37 stub ayrılmış dosyada)
- [`backend/src/Modules/Skinora.Notifications/Infrastructure/Channels/RedisDiscordDmChannelCache.cs`](../../backend/src/Modules/Skinora.Notifications/Infrastructure/Channels/RedisDiscordDmChannelCache.cs) (yeni)
- [`backend/src/Modules/Skinora.Notifications/NotificationsModule.cs`](../../backend/src/Modules/Skinora.Notifications/NotificationsModule.cs) — provider switch + DM cache register
- [`backend/src/Modules/Skinora.Notifications/Skinora.Notifications.csproj`](../../backend/src/Modules/Skinora.Notifications/Skinora.Notifications.csproj) — `StackExchange.Redis 2.8.16` eklendi

### Skinora.Users

- [`backend/src/Modules/Skinora.Users/Application/Settings/DiscordConnectionService.cs`](../../backend/src/Modules/Skinora.Users/Application/Settings/DiscordConnectionService.cs) — `using Skinora.Shared.Discord;` + try/catch `DiscordOAuthExchangeException` → `InvalidGrant`/`ExchangeFailed`
- [`backend/src/Modules/Skinora.Users/Application/Settings/IDiscordConnectionService.cs`](../../backend/src/Modules/Skinora.Users/Application/Settings/IDiscordConnectionService.cs) — `DiscordCallbackStatus.InvalidGrant` eklendi
- [`backend/src/Modules/Skinora.Users/Application/Settings/StubDiscordOAuthClient.cs`](../../backend/src/Modules/Skinora.Users/Application/Settings/StubDiscordOAuthClient.cs) — `invalid-*` + `transport-fail-*` prefix branch'leri (integration test için)
- `backend/src/Modules/Skinora.Users/Application/Settings/DiscordSettings.cs` (D — silindi, Shared'a taşındı)
- `backend/src/Modules/Skinora.Users/Application/Settings/IDiscordOAuthClient.cs` (D — silindi, Shared'a taşındı)

### Skinora.API

- [`backend/src/Skinora.API/Controllers/UsersController.cs`](../../backend/src/Skinora.API/Controllers/UsersController.cs) — `using Skinora.Shared.Discord;` + `InvalidGrant → ?reason=expired` redirect
- [`backend/src/Skinora.API/Configuration/UsersModule.cs`](../../backend/src/Skinora.API/Configuration/UsersModule.cs) — `DiscordSettings` bind kaldırıldı + stub conditional register
- [`backend/src/Skinora.API/Program.cs`](../../backend/src/Skinora.API/Program.cs) — Discord binding + rate limiter singleton + provider-conditional `IDiscordOAuthClient` + `IDiscordBotClient` typed HttpClient
- [`backend/src/Skinora.API/appsettings.json`](../../backend/src/Skinora.API/appsettings.json) — `Discord` section (14 alan)

### Testler

- [`backend/tests/Skinora.Shared.Tests/Unit/Discord/DiscordMarkdownEscaperTests.cs`](../../backend/tests/Skinora.Shared.Tests/Unit/Discord/DiscordMarkdownEscaperTests.cs) (yeni) — 7 reserved char + null/empty + transaction msg + block-quote + double-escape (~13 test)
- [`backend/tests/Skinora.Shared.Tests/Unit/Discord/DiscordOAuthClientTests.cs`](../../backend/tests/Skinora.Shared.Tests/Unit/Discord/DiscordOAuthClientTests.cs) (yeni) — token OK + global_name fallback + invalid_grant + 4xx-no-body invalid_grant + 5xx + transport + /users/@me fail + no access_token + missing-credentials (10 test)
- [`backend/tests/Skinora.Shared.Tests/Unit/Discord/DiscordBotClientTests.cs`](../../backend/tests/Skinora.Shared.Tests/Unit/Discord/DiscordBotClientTests.cs) (yeni) — createDM OK + sendMessage OK + 403 mutual_guild + 403 dm_closed (50007) + 403 unknown code + 401 + 404 + 429 retry_after + 429 global + 5xx + transport + 400 + reset-header propagate + missing-bot-token (14 test)
- [`backend/tests/Skinora.Shared.Tests/Unit/Discord/DiscordRateLimiterTests.cs`](../../backend/tests/Skinora.Shared.Tests/Unit/Discord/DiscordRateLimiterTests.cs) (yeni) — first call + retry-after per-bucket + retry-after global + zero/negative no-op + bucket map + reset header + global budget exhaust (7 test)
- [`backend/tests/Skinora.Notifications.Tests/Unit/Channels/DiscordNotificationChannelHandlerTests.cs`](../../backend/tests/Skinora.Notifications.Tests/Unit/Channels/DiscordNotificationChannelHandlerTests.cs) (yeni — `IntegrationTestBase`) — no-cache create + cache hit + 404 stale-cache retry + 403 mutual_guild + 403 dm_closed + 401 keep-preference + 429 + 5xx + 404 no-cache (9 test)
- [`backend/tests/Skinora.API.Tests/Integration/AccountSettingsEndpointTests.cs`](../../backend/tests/Skinora.API.Tests/Integration/AccountSettingsEndpointTests.cs) — `DiscordCallback_InvalidGrant_RedirectsExpired` + `DiscordCallback_TransportFailure_RedirectsExchangeFailed` (2 yeni test, mevcut 25 → 27)
- [`backend/tests/Skinora.Notifications.Tests/Integration/NotificationDeliveryJobTests.cs`](../../backend/tests/Skinora.Notifications.Tests/Integration/NotificationDeliveryJobTests.cs) — handler factory `LoggingDiscordNotificationChannelHandler`'a güncellendi (constructor değişti)
- [`backend/tests/Skinora.Notifications.Tests/Integration/DeferredNotificationDeliveryJobTests.cs`](../../backend/tests/Skinora.Notifications.Tests/Integration/DeferredNotificationDeliveryJobTests.cs) — handler factory güncellendi

### Dokümantasyon

- [`Docs/INTEGRATION_RUNBOOKS/DISCORD_SETUP.md`](../INTEGRATION_RUNBOOKS/DISCORD_SETUP.md) (yeni) — operasyon runbook

## Kabul Kriterleri Kontrolü (08 §6.1–§6.5)

| Kriter | Durum | Kanıt |
|--------|-------|-------|
| Discord Bot: Developer Portal, OAuth2 scope: identify | ✓ | `DISCORD_SETUP.md §1` + `DiscordSettings.Scope = "identify"` default + appsettings |
| MVP Guild Install: Skinora sunucusu, bot invite | ✓ | `DISCORD_SETUP.md §2` + 08 §6.1 mutual-guild önkoşul tablosu |
| OAuth2 bağlantı: identify scope, callback, discord_user_id kayıt | ✓ | `DiscordOAuthClient.ExchangeAsync` 2-step (token + /users/@me) + `DiscordConnectionService.HandleCallbackAsync` `preferences.UpsertPreferenceAsync(... ExternalId=DiscordUserId ...)` + AccountSettings test `DiscordCallback_ValidState_BindsAccountAndRedirects` |
| State parametresi: server-side session correlation (CSRF koruması) | ✓ | `DiscordConnectionService.BuildAuthorizeUrlAsync` 32-byte RandomNumberGenerator + `IDiscordOAuthStateStore.IssueAsync` + `ConsumeAsync` GETDEL atomic single-use; T35'ten devraldı, T80 düzeltmedi |
| DM kanal: POST /users/@me/channels → POST /channels/{id}/messages | ✓ | `DiscordBotClient.CreateDmAsync` + `SendMessageAsync` + `DiscordBotClientTests` 14 test |
| Mention koruması: allowed_mentions: { "parse": [] } | ✓ | `DiscordBotClient.SendMessagePayload.AllowedMentions = new AllowedMentionsPayload()` (Parse = empty array) + test `Assert.Contains("\"allowed_mentions\":{\"parse\":[]}", ...)` |
| Rate limit: header-driven (X-RateLimit-*), kuyruk + throttle | ✓ | `DiscordRateLimiter` `RegisterBucket`/`RegisterReset`/`RegisterRetryAfter` + per-bucket SemaphoreSlim + global sliding window + bot client `UpdateRateLimitFromHeaders` her response'ta |
| Hata yönetimi: 401 → admin alert, 403 → DM kapalı/mutual guild yok, 404 → kanal devre dışı, 5xx → 3 deneme | ✓ | `DiscordBotClient.PostAsync` exception mapping + handler 401=keep-pref / 403=disable / 404=disable+invalidate / 5xx=transient (immediate-tier retry 3x via T78 paterni + deferred-tier T78 30dk/1sa/4sa) + 10 unit + 9 integration test |
| DM channel ID cache: Redis | ✓ | `RedisDiscordDmChannelCache` + DI register (`AddSingleton<IDiscordDmChannelCache>` NotificationsModule) + InMemoryDiscordDmChannelCache test pattern + `DmChannelCacheTtlHours=24` config + 404 stale-cache auto-invalidate retry |

## Doğrulama Kontrol Listesi (11 §T80)

- [x] **08 §6.1–§6.5 tüm entegrasyon detayları uygulanmış mı?** — 6.1 connection ✓ (Developer Portal + guild + OAuth state CSRF) / 6.2 API ✓ (4 endpoint + allowed_mentions) / 6.3 limits ✓ (header-driven + DM cache) / 6.4 errors ✓ (OAuth + Bot mapping) / 6.5 dependency risk ✓ (runbook §6 secret rotation)

## Test Sonuçları

| Suite | Sonuç (lokal) | Not |
|-------|---------------|-----|
| `dotnet build -c Release` | 0W/0E | — |
| `dotnet format --verify-no-changes` | Δ=0 | — |
| Skinora.Shared.Tests (tam suite) | **328/328 PASS** | Unit/Discord 44/44 + diğer 284 (T79 Telegram 41 dahil) |
| Skinora.Shared.Tests Unit/Discord filter | **45/45 PASS** | Escaper 13 + OAuth 10 + Bot 14 + RateLimiter 7 (sayım `~Discord` filter, 1 ekstra `Unknown` test) |
| Skinora.Notifications.Tests (tam suite) | **137/137 PASS** | Unit/Channels/Discord 9 + diğer 128 |
| Skinora.Notifications.Tests Unit/Channels Discord filter | **10/10 PASS** | (1 ekstra `Unknown`-fallback fakulte test) |
| Skinora.API.Tests AccountSettings filter | **27/27 PASS** | T35 23 + T79 +2 + T80 +2 (InvalidGrant + TransportFailure) |
| **CI run** | ✓ **10/10 SUCCESS** | run [`26035069276`](https://github.com/turkerurganci/Skinora/actions/runs/26035069276) (Detect+Guard skipped+Lint+Build+Unit+Integration+Contract+Migration+Docker backend+CI Gate) |

## Doğrulama Sonucu — Bağımsız Validator

**Tarih:** 2026-05-18
**Branch:** `task/T80-discord-bot-integration`
**Commit:** `2c8bfa3` (validator inceleme noktası)

### Verdict: ✓ PASS

### HARD STOP Kapıları (Adım -1 / 0 / 0b)

- **Adım -1 — Working tree:** ✓ Temiz (`git status --short` boş).
- **Adım 0 — Main CI startup:** ✓ 3/3 success — T79 main (run [`26027258676`](https://github.com/turkerurganci/Skinora/actions/runs/26027258676) + [`26027258705`](https://github.com/turkerurganci/Skinora/actions/runs/26027258705)) + T78 main (run [`26021262104`](https://github.com/turkerurganci/Skinora/actions/runs/26021262104)).
- **Adım 0b — Repo memory drift:** ✓ T80 satırları MEMORY.md'de mevcut (yapım chat'i memory'i güncellemiş — Bitiş Kapısı 8. madde'ye uydu).

### Kabul Kriterleri (08 §6.1–§6.5) — Bağımsız Kanıt

| # | Kriter | Sonuç | Bağımsız Kanıt |
|---|--------|-------|----------------|
| 1 | Bot Developer Portal + OAuth2 scope `identify` | ✓ | `DiscordSettings.Scope = "identify"` default ([`DiscordSettings.cs:92`](../../backend/src/Skinora.Shared/Discord/DiscordSettings.cs#L92)) + appsettings + DISCORD_SETUP §1 |
| 2 | MVP Guild Install: Skinora sunucusu, bot invite | ✓ | DISCORD_SETUP §2 — `scope: bot`, permissions 0, "Skinora Community" |
| 3 | OAuth2 bağlantı: identify scope, callback, discord_user_id kayıt | ✓ | [`DiscordOAuthClient.cs:71-83`](../../backend/src/Skinora.Shared/Discord/DiscordOAuthClient.cs#L71-L83) 2-step exchange + FetchProfile + [`DiscordConnectionService.cs:102-108`](../../backend/src/Modules/Skinora.Users/Application/Settings/DiscordConnectionService.cs#L102-L108) `UpsertPreferenceAsync(ExternalId=profile.DiscordUserId)` |
| 4 | State parametresi: CSRF koruması (server-side correlation) | ✓ | [`DiscordConnectionService.cs:132-138`](../../backend/src/Modules/Skinora.Users/Application/Settings/DiscordConnectionService.cs#L132-L138) 32-byte `RandomNumberGenerator.Fill` + `IDiscordOAuthStateStore.IssueAsync(ttl)` + callback `ConsumeAsync` (T35 Redis GETDEL infra) + min 60s floor |
| 5 | DM kanal: `POST /users/@me/channels` → `POST /channels/{id}/messages` | ✓ | [`DiscordBotClient.cs:105-141`](../../backend/src/Skinora.Shared/Discord/DiscordBotClient.cs#L105-L141) iki endpoint + `Authorization: Bot {token}` |
| 6 | Mention koruması: `allowed_mentions: { "parse": [] }` | ✓ | [`DiscordBotClient.cs:128`](../../backend/src/Skinora.Shared/Discord/DiscordBotClient.cs#L128) payload `AllowedMentions = new AllowedMentionsPayload()` (`Parse = Array.Empty<string>()`) hard-coded |
| 7 | Rate limit: header-driven (X-RateLimit-*), kuyruk + throttle | ✓ | [`DiscordRateLimiter.cs`](../../backend/src/Skinora.Shared/Discord/DiscordRateLimiter.cs) per-bucket `SemaphoreSlim(1,1)` + `NextSendAtUtc` + global sliding window + `RegisterBucket`/`RegisterReset`/`RegisterRetryAfter` + [`DiscordBotClient.cs:306-331`](../../backend/src/Skinora.Shared/Discord/DiscordBotClient.cs#L306-L331) her response header parse |
| 8 | Hata: 401 admin alert / 403 DM-guild / 404 disable / 5xx 3 retry | ✓ | [`DiscordBotClient.PostAsync`](../../backend/src/Skinora.Shared/Discord/DiscordBotClient.cs#L143-L282) 4-way map + [`DiscordNotificationChannelHandler.cs:96-157`](../../backend/src/Modules/Skinora.Notifications/Infrastructure/Channels/DiscordNotificationChannelHandler.cs#L96-L157) 401 keep-pref / 403 disable / 404-cache retry once / 404 no-cache disable + 5xx T78 deferred-tier 3 retry |
| 9 | DM channel ID cache: Redis | ✓ | [`RedisDiscordDmChannelCache.cs`](../../backend/src/Modules/Skinora.Notifications/Infrastructure/Channels/RedisDiscordDmChannelCache.cs) `{prefix}:discord:dm_channel:{userId}` + `DmChannelCacheTtlHours=24` + 404 auto-invalidate + handler `GetAsync`/`SetAsync`/`ForgetAsync` |

### Doğrulama Kontrol Listesi (11 §T80)

- [x] **08 §6.1–§6.5 tüm entegrasyon detayları uygulanmış mı?** — §6.1 connection (Developer Portal + guild + OAuth state CSRF) ✓ / §6.2 4 endpoint + allowed_mentions ✓ / §6.3 header-driven + Redis DM cache ✓ / §6.4 OAuth hata 6/6 + Bot hata 5/5 (50007 reason ayrımı dahil) ✓ / §6.5 secret rotation runbook ✓.

### Bağımsız Test Sonuçları (validator re-run)

| Tür | Sonuç | Komut | Kanıt |
|-----|-------|-------|-------|
| Build | ✓ 0W/0E | `dotnet build Skinora.sln -c Release` | "Build succeeded. 0 Warning(s) 0 Error(s)" 31.50s |
| Format | ✓ Δ=0 | `dotnet format --verify-no-changes` | exit 0, çıktı yok |
| Shared.Tests Discord filter | ✓ 45/45 | `dotnet test --filter ~Discord` | DiscordMarkdownEscaper + DiscordOAuthClient + DiscordBotClient + DiscordRateLimiter |
| Notifications.Tests Discord filter | ✓ 10/10 | `dotnet test --filter ~Discord` | DiscordNotificationChannelHandlerTests |
| API.Tests AccountSettings filter | ✓ 27/27 | `dotnet test --filter ~AccountSettings` | T80 yeni: `DiscordCallback_InvalidGrant_RedirectsExpired` + `DiscordCallback_TransportFailure_RedirectsExchangeFailed` |
| Task branch CI | ✓ 10/10 | `gh run list --branch task/T80-*` | run [`26035069276`](https://github.com/turkerurganci/Skinora/actions/runs/26035069276) tüm 10 check SUCCESS |

### Güvenlik Kontrolü

- [x] **Secret sızıntısı:** Temiz — `ClientId`/`ClientSecret`/`BotToken` default `REPLACE_IN_ENV` trip-wire (`DiscordOAuthClient` + `DiscordBotClient` ctor `InvalidOperationException` fail-closed); access_token kalıcı saklanmaz; bot token `Authorization: Bot` header'a inject; webhook signature gerekli değil (OAuth2 callback HTTPS + state CSRF).
- [x] **Auth etkisi:** Temiz — OAuth scope minimum `identify`; Bearer vs Bot ayrımı; state Redis GETDEL single-use atomic; `User.IsDeactivated` callback guard; `already_linked` check (06 §3.4) `ExternalIdInUseByAnotherUserAsync`.
- [x] **Input validation:** Temiz — `discordUserId` `IsNullOrWhiteSpace` guard; createDM response `channel.Id` empty kontrolü; `DiscordMarkdownEscaper` 7 reserved char escape; mention koruması; URL `Uri.EscapeDataString`.
- [x] **Yeni dış bağımlılık:** `StackExchange.Redis 2.8.16` Notifications'a eklendi (Users'ta zaten vardı, yeni paket değil transitive). Discord NuGet **eklenmedi** — raw HttpClient + System.Text.Json (T78/T79 precedent).

### Doküman Uyumu

- ✓ **Enum:** `DiscordCallbackStatus.InvalidGrant` 08 §6.4 invalid_grant satırı 1:1; `DiscordForbiddenReason` (Mutual/DmClosed/Unknown) 08 §6.4 403 ayrıştırması 1:1.
- ✓ **Field/header:** `discord_user_id` UNP.ExternalId; `recipient_id` Discord docs; `allowed_mentions.parse` Discord message schema; `X-RateLimit-Bucket`/`-Reset-After`/`Retry-After` rate-limit docs; error code `50007` (Cannot send messages to this user) Discord error code reference.
- ✓ **İş kuralları:** Spec §6.1 minimum scope `identify` ✓; §6.2 `application/x-www-form-urlencoded` zorunlu ✓; §6.3 header-driven hard-code yok ✓; §6.4 mutual guild OR user-install — MVP mutual path + user-install K7 forward-deferred ✓.

### Bulgular

**S-bulgu yok.**

### Minor Advisory

- **A1 — `DiscordRateLimiter.WaitAsync` semaphore release semantiği** ([`DiscordRateLimiter.cs:57-80`](../../backend/src/Skinora.Shared/Discord/DiscordRateLimiter.cs#L57-L80)): `released` değişkeni hiçbir zaman `true` set edilmiyor → finally bloğunda her zaman release yapılıyor. Per-bucket semaphore sadece wait-for-reset state'ini serileştirir, HTTP call sırasında concurrent request'lere izin verir. Header-driven `NextSendAtUtc` ile pace doğru, ama strict FIFO kuyruk değil. `released = false` ölü kod kalıntısı. Spec "kuyruk + throttle" diyor; throttle ✓, strict kuyruk semantiği bir tık zayıf. **T-future** cleanup: dead var kaldır veya release HTTP call sonrasına taşı.
- **A2 — 2000 char inline defense-in-depth yok:** Spec §6.2 `Maksimum uzunluk: 2000 karakter`. Template'ten kısa içerik beklensin diye explicit length check eklenmemiş; >2000 karakter Discord 400 → `DiscordPermanentException` → preference auto-disable (bir kerelik uzun template kullanıcı tercihini siler — heavy side-effect). **T-future** template-side audit (K5 advisory'nin parçası).

### Yapım Raporu Karşılaştırması

- **Uyum:** Tam uyumlu — yapım raporu 9/9 kabul kriterini ✓ ile listeliyor, bağımsız doğrulama 9/9 ✓ ile aynı sonuçta. Test sayıları (Shared 45/45, Notifications 10/10, API 27/27), CI 10/10, Build 0W/0E, format Δ=0 hepsi bağımsız re-run ile eşleşti. K1–K9 forward-deferred limitations yapım raporunda dokümante. A1+A2 advisory'ler validator-side yeni gözlemler — FAIL eşiğine çıkmıyor, T-future cleanup adayları.

## Altyapı Değişiklikleri

- **Migration:** Yok. Discord DM cache Redis-only, OAuth state Redis-only, UserNotificationPreference T23'te zaten mevcut.
- **Config:** `Discord` section yeni (14 alan); `Discord:Provider=logging` default → CI/dev hiçbir Discord trafiği yapmaz; production override gerekli.
- **DI:** `IDiscordRateLimiter` singleton, `IDiscordOAuthClient` + `IDiscordBotClient` scoped (HttpClient typed); `IDiscordDmChannelCache` singleton (Redis). `ITelegramBotClient`/`IResendEmailClient` paterniyle simetrik.
- **Middleware:** Discord webhook **YOK** (OAuth2 callback HTTPS + state CSRF zaten korur; Discord interaction webhook'larına MVP'de ihtiyaç yok).
- **Dış bağımlılık:** `Skinora.Notifications.csproj`'a `StackExchange.Redis 2.8.16` eklendi (DM channel cache). `Skinora.Users.csproj`'da zaten vardı (T35 stores). Hiçbir Discord NuGet (Discord.Net) eklenmedi — raw HttpClient + System.Text.Json (T78/T79 precedent).

## Commit & PR

- **Commits:**
  - `d00eef2` — `T80: Discord entegrasyonu (Bot API + OAuth2 + DM channel cache)` (yapım, 35 dosya / +3481 / -61)
  - `e92fb69` — `T80: rapor + status + memory yansıt` (3 dosya / +184 / -2)
- **PR:** [#121](https://github.com/turkerurganci/Skinora/pull/121) — `task/T80-discord-bot-integration` → `main`
- **CI:** ✓ **HEAD `0f3453b` run [`26033729167`](https://github.com/turkerurganci/Skinora/actions/runs/26033729167) 10/10 SUCCESS** (Detect + Lint + Build + Unit + Integration + Contract + Migration + Docker backend + CI Gate, Guard skipped — PR). Önceki run `26033518126` (HEAD `e92fb69`) yeni push concurrency ile cancel oldu — task.md "son tamamlanmış run" kuralı geçerli.
- **Branch izolasyon check:** ✓ temiz — `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+'` → yalnız `T80`

## Known Limitations

- **K1 — `DiscordSettings.MaxRetries` config knob şu an template** — backend retry pipeline T78 `NotificationDeliveryJob` immediate-tier ile zaten 3 retry yapıyor; bu config knob future header-based bot client retry için reserved (örn. transient 5xx idempotent retry'i bot client içinde yapacaksak). Şu an handler'lar üzerinden retry kuyruğu zaten 3 deneme + deferred 3 deneme = 6 toplam (T78 paterni mirror).
- **K2 — `getMe`/`getBotInfo` health probe API kapsanmadı** — Plan §6.2'de `oauth2/token` + `users/@me` + `users/@me/channels` + `channels/{id}/messages` listeli; runbook'ta operasyonel doğrulama curl örneği yok. Bot health probe T94+ monitoring task'ı.
- **K3 — DM channel cache invalidation sadece 404'te** — Discord channel'ı silinmediyse ama bot kicked olduysa cache stale kalır (403 alır, preference disable olur, cache de o sırada invalidate edilir). 24h TTL eventually kapatır.
- **K4 — Header-driven rate limiter manual smoke yok** — Unit testler header parse + bucket map + retry-after + reset-after kapsıyor; production smoke (Discord'un gerçek X-RateLimit-Bucket id'leri canlı çağrılarda nasıl rotate eder) Skinora Discord sunucusu kurulduktan sonra DISCORD_SETUP.md §4 ile doğrulanır.
- **K5 — Markdown injection saldırı yüzeyi** — `DiscordMarkdownEscaper` allow-list-free, channel handler title+body toplu escape ediyor. Template renderer'dan gelen string'ler escape'siz tutulamaz — T79'daki K5 ile aynı template-side audit T-future.
- **K6 — Discord webhook (interactions) yok** — MVP'de bot komut yanıtlamaz, sadece DM gönderir. Bot içinde `/discord/...` slash command desteği T-future.
- **K7 — User-install support yok** — 08 §6.1 MVP guild-install only. User-install (Discord app kullanıcının hesabına kurulur, mutual guild gerekmez) T-future.
- **K8 — Sidecar webhook signature middleware ile paralel değil** — Discord OAuth callback HTTPS + state CSRF korumalı, Telegram/sidecar webhook'ları gibi HMAC/secret-token middleware gerektirmez. Eğer Discord interactions eklenirse (T-future) o pipeline ayrı middleware ile gelir (Ed25519 signature).
- **K9 — `Discord:Scope` env-driven** — Şu an default `"identify"`. Plan §6.1 minimum scope zorunlu kılıyor. T-future "email", "guilds" gibi ek scope eklenirse env override yeterli, kod değişikliği gerekmez (DiscordSettings.Scope string).

## Notlar

- **Working tree check (Adım -1):** Branch açıldığında temiz (`git status --short` → boş).
- **Main CI startup check (Adım 0):** ✓ 3/3 success → T79 (670cedb run 26027258676/26027258705) + T78 (b8d2f26 run 26021262104). Task'a başlamadan kontrol edildi.
- **Dış varsayım doğrulama (Adım 4):**
  - Discord API public + ücretsiz: `discord.com/developers/docs` resmi doc, plan tier gerekmiyor ✓
  - OAuth2 token endpoint `application/x-www-form-urlencoded` zorunlu: Discord docs `oauth2` ✓
  - Bot token "Bot {token}" Authorization scheme (Bearer değil): Discord docs reference ✓
  - createDM `POST /users/@me/channels` + `recipient_id` body: Discord docs user resource ✓
  - allowed_mentions `{ "parse": [] }` mention spam'ı engeller: Discord docs message resource ✓
  - Rate limit header isimleri (`X-RateLimit-Bucket`/`Remaining`/`Reset-After` + 429 body `retry_after` float): Discord docs rate-limits ✓
  - 403 code 50007 = "Cannot send messages to this user" (DM closed): Discord error codes reference ✓
  - HTTP client: T78/T79 raw HttpClient precedent — Discord.Net NuGet'in yerine + plan onayı (user) ✓
- **Bağımlılık kontrolü (Adım 2):** T35 (✓ Tamamlandı F2 Gate Check'te validated), T37 (✓ Tamamlandı, b383983+7767fc7). İki bağımlılık da kapalı.
- **Scope onayı (Adım 5):** İki soru proje sahibine sunuldu → "T79 paterni birebir (Recommended)" + "Markdown escape şimdi ekle" seçimleri.
- **Architectural karar — RedisDmChannelCache yerleşimi:** Notifications/Infrastructure/Channels altında (Shared'da değil) çünkü (a) channel handler'ın direkt tüketicisi, (b) StackExchange.Redis dep'i Shared'a koymak T78/T79 paternine ters düşer (Telegram store'ları Users'ta, Email idempotency DB tablosunda), (c) Notifications zaten dispatcher orchestration owner. İnterface + InMemory impl Shared'da kalır (dependency-free test/dev fallback).
- **Architectural karar — IDiscordOAuthClient yerleşimi:** Shared'a taşındı (Users-side'dan); T79 paterni `ITelegramBotClient` Shared'da olduğu için. `StubDiscordOAuthClient` (Users-side) Shared interface'i implement eder; provider switch ile real (Shared) vs stub (Users) seçilir.
- **Drift kapama — `DiscordCallbackStatus.InvalidGrant`:** T35 enum'u `ExchangeFailed`'da tüm OAuth hata branch'lerini topluyordu. T80 08 §6.4 OAuth2 hata tablosundaki "invalid_grant → ?reason=expired" satırını ayrı status + redirect ile ayrıştırdı. T35 retro silinmedi, sadece daha-spesifik dal eklendi.
