# T80 — Discord entegrasyonu (Bot API + OAuth2 + DM channel cache)

**Faz:** F4 | **Durum:** ⏳ Yapım bitti, doğrulama bekliyor | **Tarih:** 2026-05-18

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
| **CI run** | ⏳ push sonrası | — |

## Altyapı Değişiklikleri

- **Migration:** Yok. Discord DM cache Redis-only, OAuth state Redis-only, UserNotificationPreference T23'te zaten mevcut.
- **Config:** `Discord` section yeni (14 alan); `Discord:Provider=logging` default → CI/dev hiçbir Discord trafiği yapmaz; production override gerekli.
- **DI:** `IDiscordRateLimiter` singleton, `IDiscordOAuthClient` + `IDiscordBotClient` scoped (HttpClient typed); `IDiscordDmChannelCache` singleton (Redis). `ITelegramBotClient`/`IResendEmailClient` paterniyle simetrik.
- **Middleware:** Discord webhook **YOK** (OAuth2 callback HTTPS + state CSRF zaten korur; Discord interaction webhook'larına MVP'de ihtiyaç yok).
- **Dış bağımlılık:** `Skinora.Notifications.csproj`'a `StackExchange.Redis 2.8.16` eklendi (DM channel cache). `Skinora.Users.csproj`'da zaten vardı (T35 stores). Hiçbir Discord NuGet (Discord.Net) eklenmedi — raw HttpClient + System.Text.Json (T78/T79 precedent).

## Commit & PR

- **Commits:**
  - `TBD` — `T80: Discord entegrasyonu (Bot API + OAuth2 + DM channel cache)` (yapım)
  - `TBD` — `T80: rapor + status + memory yansıt`
- **PR:** **TBD** — `task/T80-discord-bot-integration` → `main`
- **CI:** ⏳ push sonrası
- **Branch izolasyon check:** ✓ temiz — yalnız `T80`

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
