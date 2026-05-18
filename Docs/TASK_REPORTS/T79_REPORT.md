# T79 — Telegram entegrasyonu (Bot API + spec-gap fix)

**Faz:** F4 | **Durum:** ✓ Tamamlandı (PASS) | **Tarih:** 2026-05-18

---

## Yapılan İşler

- **`Skinora.Shared/Telegram/` transport katmanı** (yeni klasör, T78 Email pattern'i mirror) — `ITelegramBotClient`/`TelegramBotClient` (HttpClient impl), `TelegramSettings` (Provider/BotToken/BotUsername/BotUrl/BaseUrl/Timeout/WebhookSecretToken/CodeTtlSeconds/MaxFailedAttempts/IdempotencyTtlHours/PerChatRatePerSecond/GlobalRatePerSecond), `TelegramBotModels` (SendMessage/SetWebhook DTO), `TelegramBotExceptions` (Transient/Permanent/Forbidden + `TelegramForbiddenReason` taxonomy), `MarkdownV2Escaper` (18 reserved char escape), `TelegramRateLimiter`/`ITelegramRateLimiter` (per-chat 1msg/s SemaphoreSlim gate + global 30msg/s sliding window + retry-after honor + injected `Func<DateTimeOffset>` clock for test). Telegram.Bot NuGet yerine raw HttpClient — T78 ResendEmailClient precedent + plan onaylı (single endpoint için minimal yüzey + manual JSON + testability).
- **`TelegramBotClient` error classification** (08 §5.4):
  - 200 + `ok=true` → `TelegramSendMessageResult`
  - 429 → `TelegramTransientException` populated with `retry_after`
  - 5xx + transport (timeout/HttpRequestException) → `TelegramTransientException`
  - 403 → `TelegramForbiddenException` + `ClassifyForbidden(description)` 4-way: `BotBlockedByUser` / `UserDeactivated` / `CannotMessageBots` / `CannotInitiateConversation` / `Unknown` fallback
  - 400 + diğer 4xx → `TelegramPermanentException`
- **`TelegramNotificationChannelHandler` real impl** (T37 stub'ın gerçek Bot API swap'i) — `Skinora.Notifications/Infrastructure/Channels/`. Yeni constructor: `ITelegramBotClient`, `ITelegramRateLimiter`, `AppDbContext`, `ILogger`. `SendAsync` akışı: rate limiter wait → `MarkdownV2Escaper.Escape(title) + body` → `*title*\n\nbody` envelope → `sendMessage`. Exception mapping: `TelegramForbiddenException` → preference auto-disable (`UserNotificationPreference.IsEnabled=false`) + `PermanentChannelDeliveryException`; `TelegramPermanentException` (400 chat not found dahil) → preference disable + `PermanentChannelDeliveryException`; `TelegramTransientException` → 429 ise rate limiter'a `RegisterRetryAfter` + `TransientChannelDeliveryException`.
- **`LoggingTelegramNotificationChannelHandler` stub** (yeni dosya) — T37 default stub mevcut `TelegramNotificationChannelHandler` adından ayrıştırıldı. Provider switch: `Telegram:Provider=telegram` → real, default `logging` → stub (CI/dev fail-closed).
- **`TelegramWebhookSignatureMiddleware`** (yeni, `Skinora.API/Middleware/`) — path-scoped `/api/v1/webhooks/telegram`; `X-Telegram-Bot-Api-Secret-Token` constant-time compare (`CryptographicOperations.FixedTimeEquals`); `EnableBuffering` + body peek → `update_id` JSON property extract → `ProcessedNonces(Source="telegram", Nonce=update_id, TTL=24h)` INSERT; unique violation duplicate → 200 + `Idempotent` early return. Body parse failure → next pipeline (controller 200 noop, Telegram retry storm yok). Mevcut Steam/Blockchain (`WebhookSignatureMiddleware`, HMAC) ve Resend Svix (`ResendWebhookSignatureMiddleware`) ile paralel ayrı concern.
- **`WebhooksController.Telegram` refactor** — inline secret-token check + `TelegramSettings` IOptions kaldırıldı (middleware downstream'de). Action artık sadece body parse + handler dispatch. Mevcut endpoint URL (`POST /api/v1/webhooks/telegram`) ve regex (`SKN-[A-Za-z0-9]+`) korundu (backwards-compatible).
- **Spec-gap fix — T35 ↔ 08 §5.1 drift kapatma:**
  - **Code entropy:** `GenerateCode()` `SKN-` + 6-digit (~20 bit) → `SKN-` + `Convert.ToHexString(RNG-128-bit).ToLowerInvariant()` (regex `^SKN-[0-9a-f]{32}$`). Plan: "UUIDv4 (122 bit) veya 128+ bit CSPRNG"; 128-bit RNG hex %100 uyumlu.
  - **TTL default:** `CodeTtlSeconds` 300 → 600 (10 dk plan).
  - **Brute-force counter:** `ITelegramVerificationStore` API genişledi (`RegisterFailedAttemptAsync(telegramUserId, ttl)` + `GetFailedAttemptsAsync(telegramUserId)`). Redis impl: `INCR` + first-increment `EXPIRE` (`tg_verify_fail:{telegramUserId}` key, 600s TTL). InMemory impl: ConcurrentDictionary + expiry. Service flow: ProcessWebhookAsync başlangıcında `GetFailedAttemptsAsync` → `>= MaxFailedAttempts` ise `BruteForceLocked` early return; `ConsumeAsync` null dönerse `RegisterFailedAttemptAsync` invocate. Webhook → 200 (silent ignore), Telegram retry storm yok.
- **Settings konsolidasyonu:** mevcut `Skinora.Users.Application.Settings.TelegramSettings` (BotUrl/WebhookSecretToken/CodeTtlSeconds) silindi; `Skinora.Shared.Telegram.TelegramSettings` (transport + connection + rate limit consolidated) referans noktası. `TelegramConnectionService`, `WebhooksController`, `NotificationsModule` yeni namespace'i consume eder.
- **DI wiring (composition root):**
  - `Program.cs`: `Telegram` section binding + `ITelegramRateLimiter` singleton + provider switch ile koşullu `AddHttpClient<ITelegramBotClient, TelegramBotClient>` (provider=telegram dışında HTTP client hiç DI'ya girmez → fail-closed); `TelegramWebhookSignatureMiddleware` pipeline'a eklendi (Resend middleware'inden sonra).
  - `NotificationsModule.AddNotificationsModule`: provider switch'e göre `TelegramNotificationChannelHandler` (real) veya `LoggingTelegramNotificationChannelHandler` (stub) register.
  - `UsersModule`: `Configure<TelegramSettings>` kaldırıldı (Program.cs'e taşındı); `ITelegramConnectionService` consume aynı.
  - `appsettings.json`: yeni `Telegram` section — Provider="logging" default, BotToken/WebhookSecretToken `REPLACE_IN_ENV`, BotUsername="SkinoraBot", BotUrl="https://t.me/SkinoraBot", BaseUrl="https://api.telegram.org", TimeoutSeconds=10, CodeTtlSeconds=600, MaxFailedAttempts=5, IdempotencyTtlHours=24, PerChatRatePerSecond=1, GlobalRatePerSecond=30.
- **`Docs/INTEGRATION_RUNBOOKS/TELEGRAM_SETUP.md` (yeni)** — operasyon runbook: BotFather bot oluşturma + `/setjoingroups Disable`, webhook secret üretimi (`openssl rand -base64 48`), production env override, setWebhook curl + `getWebhookInfo` doğrulama, bağlantı + smoke message akışı, secret rotation prosedürü (3 senaryo), izleme/limit tablosu, sandbox/staging/prod provider matris, yaygın hatalar.

## Etkilenen Modüller / Dosyalar

### Skinora.Shared (yeni `Telegram/` klasörü)

- [`backend/src/Skinora.Shared/Telegram/ITelegramBotClient.cs`](../../backend/src/Skinora.Shared/Telegram/ITelegramBotClient.cs) (yeni) — low-level transport kontratı
- [`backend/src/Skinora.Shared/Telegram/TelegramBotClient.cs`](../../backend/src/Skinora.Shared/Telegram/TelegramBotClient.cs) (yeni) — HttpClient impl + error classification
- [`backend/src/Skinora.Shared/Telegram/TelegramBotExceptions.cs`](../../backend/src/Skinora.Shared/Telegram/TelegramBotExceptions.cs) (yeni) — base + Transient/Permanent/Forbidden + Reason enum
- [`backend/src/Skinora.Shared/Telegram/TelegramBotModels.cs`](../../backend/src/Skinora.Shared/Telegram/TelegramBotModels.cs) (yeni) — request/result records
- [`backend/src/Skinora.Shared/Telegram/TelegramSettings.cs`](../../backend/src/Skinora.Shared/Telegram/TelegramSettings.cs) (yeni — consolidated, eski Users-side silindi) — config sınıfı
- [`backend/src/Skinora.Shared/Telegram/MarkdownV2Escaper.cs`](../../backend/src/Skinora.Shared/Telegram/MarkdownV2Escaper.cs) (yeni) — 18 reserved char escape
- [`backend/src/Skinora.Shared/Telegram/ITelegramRateLimiter.cs`](../../backend/src/Skinora.Shared/Telegram/ITelegramRateLimiter.cs) (yeni) — interface
- [`backend/src/Skinora.Shared/Telegram/TelegramRateLimiter.cs`](../../backend/src/Skinora.Shared/Telegram/TelegramRateLimiter.cs) (yeni) — per-chat semaphore + global sliding window + retry-after

### Skinora.Notifications

- [`backend/src/Modules/Skinora.Notifications/Infrastructure/Channels/TelegramNotificationChannelHandler.cs`](../../backend/src/Modules/Skinora.Notifications/Infrastructure/Channels/TelegramNotificationChannelHandler.cs) — T37 stub'tan gerçek Bot API impl'e dönüşüm
- [`backend/src/Modules/Skinora.Notifications/Infrastructure/Channels/LoggingTelegramNotificationChannelHandler.cs`](../../backend/src/Modules/Skinora.Notifications/Infrastructure/Channels/LoggingTelegramNotificationChannelHandler.cs) (yeni) — T37 stub ayrılmış dosyada
- [`backend/src/Modules/Skinora.Notifications/NotificationsModule.cs`](../../backend/src/Modules/Skinora.Notifications/NotificationsModule.cs) — provider switch (TelegramNotificationChannelHandler vs LoggingTelegramNotificationChannelHandler)

### Skinora.Users

- [`backend/src/Modules/Skinora.Users/Application/Settings/TelegramConnectionService.cs`](../../backend/src/Modules/Skinora.Users/Application/Settings/TelegramConnectionService.cs) — 128-bit entropy `GenerateCode` + brute-force gate + namespace import güncellendi
- [`backend/src/Modules/Skinora.Users/Application/Settings/ITelegramConnectionService.cs`](../../backend/src/Modules/Skinora.Users/Application/Settings/ITelegramConnectionService.cs) — `TelegramWebhookStatus.BruteForceLocked` enum eklendi
- [`backend/src/Modules/Skinora.Users/Application/Settings/ITelegramVerificationStore.cs`](../../backend/src/Modules/Skinora.Users/Application/Settings/ITelegramVerificationStore.cs) — `RegisterFailedAttemptAsync` + `GetFailedAttemptsAsync` eklendi
- [`backend/src/Modules/Skinora.Users/Application/Settings/InMemoryTelegramVerificationStore.cs`](../../backend/src/Modules/Skinora.Users/Application/Settings/InMemoryTelegramVerificationStore.cs) — brute-force counter (ConcurrentDictionary + expiry)
- [`backend/src/Modules/Skinora.Users/Application/Settings/RedisTelegramVerificationStore.cs`](../../backend/src/Modules/Skinora.Users/Application/Settings/RedisTelegramVerificationStore.cs) — INCR + EXPIRE atomic-ish counter
- `backend/src/Modules/Skinora.Users/Application/Settings/TelegramSettings.cs` (D — silindi, Shared'a taşındı)

### Skinora.API

- [`backend/src/Skinora.API/Middleware/TelegramWebhookSignatureMiddleware.cs`](../../backend/src/Skinora.API/Middleware/TelegramWebhookSignatureMiddleware.cs) (yeni) — secret-token + update_id idempotency
- [`backend/src/Skinora.API/Controllers/WebhooksController.cs`](../../backend/src/Skinora.API/Controllers/WebhooksController.cs) — inline secret check kaldırıldı (middleware downstream)
- [`backend/src/Skinora.API/Configuration/UsersModule.cs`](../../backend/src/Skinora.API/Configuration/UsersModule.cs) — TelegramSettings bind kaldırıldı
- [`backend/src/Skinora.API/Program.cs`](../../backend/src/Skinora.API/Program.cs) — Telegram binding + rate limiter singleton + conditional HttpClient + middleware kayıt
- [`backend/src/Skinora.API/appsettings.json`](../../backend/src/Skinora.API/appsettings.json) — `Telegram` section

### Testler

- [`backend/tests/Skinora.Shared.Tests/Unit/Telegram/MarkdownV2EscaperTests.cs`](../../backend/tests/Skinora.Shared.Tests/Unit/Telegram/MarkdownV2EscaperTests.cs) (yeni) — 18 reserved char + null/empty + complex msg + double-escape (24 test)
- [`backend/tests/Skinora.Shared.Tests/Unit/Telegram/TelegramBotClientTests.cs`](../../backend/tests/Skinora.Shared.Tests/Unit/Telegram/TelegramBotClientTests.cs) (yeni) — sendMessage 200 + 429 retry_after + 500 + 403 reason 4-way + 400 + transport error + setWebhook + missing bot token (10 test)
- [`backend/tests/Skinora.Shared.Tests/Unit/Telegram/TelegramRateLimiterTests.cs`](../../backend/tests/Skinora.Shared.Tests/Unit/Telegram/TelegramRateLimiterTests.cs) (yeni) — FakeClock first/repeat/global-budget/retry-after (7 test)
- [`backend/tests/Skinora.Notifications.Tests/Unit/Channels/TelegramNotificationChannelHandlerTests.cs`](../../backend/tests/Skinora.Notifications.Tests/Unit/Channels/TelegramNotificationChannelHandlerTests.cs) (yeni — `IntegrationTestBase`) — OK + Forbidden disable + Permanent400 disable + Transient429 register + Transient5xx no-disable (5 test)
- [`backend/tests/Skinora.API.Tests/Integration/AccountSettingsEndpointTests.cs`](../../backend/tests/Skinora.API.Tests/Integration/AccountSettingsEndpointTests.cs) — entropy regex assert + DuplicateUpdateId idempotency + FailedAttemptsAtLimit lock + fixture `MaxFailedAttempts=2` (3 yeni test)
- [`backend/tests/Skinora.Notifications.Tests/Integration/NotificationDeliveryJobTests.cs`](../../backend/tests/Skinora.Notifications.Tests/Integration/NotificationDeliveryJobTests.cs) — handler factory `LoggingTelegramNotificationChannelHandler`'a güncellendi
- [`backend/tests/Skinora.Notifications.Tests/Integration/DeferredNotificationDeliveryJobTests.cs`](../../backend/tests/Skinora.Notifications.Tests/Integration/DeferredNotificationDeliveryJobTests.cs) — handler factory güncellendi

### Dokümantasyon

- [`Docs/INTEGRATION_RUNBOOKS/TELEGRAM_SETUP.md`](../INTEGRATION_RUNBOOKS/TELEGRAM_SETUP.md) (yeni) — operasyon runbook

## Kabul Kriterleri Kontrolü (08 §5.1–§5.5)

| Kriter | Durum | Kanıt |
|--------|-------|-------|
| Telegram Bot: BotFather ile oluşturma, token alma | ✓ | `TELEGRAM_SETUP.md §1` |
| Deep Link bağlantı: benzersiz kod (10dk TTL, single-use, 122+ bit entropy), `/start` ile eşleşme, chat_id kayıt | ✓ | `TelegramConnectionService.GenerateCode` 128-bit + `TelegramSettings.CodeTtlSeconds=600` + `RedisTelegramVerificationStore.ConsumeAsync` GETDEL single-use + AccountSettings test `^SKN-[0-9a-f]{32}$` |
| Webhook: `POST /webhooks/telegram`, `secret_token` doğrulaması | ✓ | `TelegramWebhookSignatureMiddleware` + AccountSettings test `TelegramWebhook_MissingSecret_Returns401` |
| Webhook idempotency: update_id ile duplicate filtreleme (Redis, 24sa TTL) | ✓ | `TelegramWebhookSignatureMiddleware` ProcessedNonces INSERT + AccountSettings test `TelegramWebhook_DuplicateUpdateId_AcknowledgesIdempotent`. **Not:** Storage SQL Server `ProcessedNonces` tablosu (T68 paterni); Redis değil (sidecar webhook pattern paralelliği — `Source="telegram"`). 24sa TTL `IdempotencyTtlHours=24` config. |
| sendMessage: MarkdownV2 format, escape helper | ✓ | `TelegramBotClient.SendMessageAsync` `parse_mode=MarkdownV2` + `MarkdownV2Escaper.Escape` 18 char + 24 test |
| Rate limit: chat başına 1 msg/s, farklı chat'ler 30 msg/s, sıralı kuyruk | ✓ | `TelegramRateLimiter` per-chat semaphore + global sliding window + 7 unit test |
| Hata yönetimi: 429 → retry_after bekle, 403 neden ayrıştırma (4 documented), 400 → bağlantı kopmuş, 5xx → 3 deneme | ✓ | `TelegramBotClient.PostAsync` mapping + `ClassifyForbidden` + handler retry-after register + 10 unit test. 5xx 3-deneme: `TransientChannelDeliveryException` → `NotificationDeliveryJob` immediate-tier 3 retry (T78 paterni mevcut). |
| setWebhook: url, secret_token, max_connections=40, allowed_updates=["message"] | ✓ | `ITelegramBotClient.SetWebhookAsync` + `TelegramSetWebhookRequest` defaults + `TELEGRAM_SETUP.md §4` |

## Test Sonuçları

| Suite | Sonuç (lokal) | Not |
|-------|---------------|-----|
| `dotnet build -c Release` | 0W/0E | — |
| `dotnet format --verify-no-changes --severity error` | Δ=0 | — |
| Skinora.Shared.Tests Unit/Telegram | **41/41 PASS** | MarkdownV2 24 + BotClient 10 + RateLimiter 7 |
| Skinora.API.Tests AccountSettingsEndpointTests | **25/25 PASS** | T35 mevcut 22 + T79 yeni 3 (entropy assert + DuplicateUpdateId + FailedAttemptsAtLimit) |
| Skinora.Notifications.Tests Unit/Channels | (lokal SQL Server bağlantısı yok — CI'da çalışacak) | 5 test |
| **CI run** | ⏳ in_progress | run [26024944336](https://github.com/turkerurganci/Skinora/actions/runs/26024944336) |

## Altyapı Değişiklikleri

- **Migration:** Yok. `ProcessedNonces` tablosu zaten T68 paterniyle mevcut (`Source` string column genişletilebilir; "telegram" yeni değer, schema değişikliği yok).
- **Config:** `Telegram` section yeni (12 alan); `Telegram:Provider=logging` default → CI/dev hiçbir ağ trafiği yapmaz; production override gerekli.
- **DI:** `ITelegramRateLimiter` singleton, `ITelegramBotClient` scoped (HttpClient typed); `IEmailSender` paterniyle simetrik.
- **Middleware:** pipeline sırası 5a (Steam HMAC) → 5b (Resend Svix) → **5c (Telegram secret_token + update_id idempotency, yeni)** → 6 CORS.
- **Dış bağımlılık:** yeni NuGet eklenmedi (raw HttpClient + System.Text.Json — mevcut bağımlılıklar yeterli).

## Commit & PR

- **Commits:**
  - `d305df8` — `T79: Telegram entegrasyonu (Bot API + spec-gap fix)` (yapım)
  - `d56f6ef` — `T79: rapor + status + memory yansıt`
  - `2504310` — `T79: fix Notifications.Tests FK violation` (CI fix push, 1× `[ci-failure]` BYPASS_LOG)
- **PR:** [#120](https://github.com/turkerurganci/Skinora/pull/120) — `task/T79-telegram-bot-integration` → `main`
- **CI:** ✓ **HEAD `2504310` run [`26025772241`](https://github.com/turkerurganci/Skinora/actions/runs/26025772241) 10/10 SUCCESS** (Detect+Lint+Build+Unit+Integration+Contract+Migration+Docker+CI Gate, Guard skipped); önceki run `d305df8` [`26024944336`](https://github.com/turkerurganci/Skinora/actions/runs/26024944336) cancelled (paralel push tetikledi) + `d56f6ef` [`26025237043`](https://github.com/turkerurganci/Skinora/actions/runs/26025237043) FAIL (Notifications.Tests TelegramNotificationChannelHandler 5 test FK violation — `UserNotificationPreference.UserId` için User satırı seed edilmemişti; lokal'de SQL Server bağlantı problemi nedeniyle test çalışmamış, root cause CI'da görüldü).
- **CI fix push (`2504310`):** `SeedPreferenceAsync` artık önce User satırını seed eder (random SteamId + PreferredLanguage="en") + SaveChanges, sonra preference o User.Id ile yazılır. Pattern T33 yapım notunda dokümante edilmiş ("AppDbContext.UpdateAuditFields Added state'te CreatedAt'i UtcNow'a çekiyor" — burada problem audit field değil FK, ama seed-then-link pattern aynı).
- **Branch izolasyon check:** ✓ temiz — `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+'` → yalnız `T79`.

## Known Limitations

- **K1 — Notifications.Tests Unit/Channels SQL Server bağımlılığı:** Yeni `TelegramNotificationChannelHandlerTests` `IntegrationTestBase` extend ediyor (SQL Server gerektirir). Lokal'de SQL Server bağlantısı yapılandırılmamışsa fail eder; CI'da `services:mssql` ile çalışacak. T11.3 paterni — Notifications.Tests dizininde mevcut SQLite-friendly altyapı yok, çakışan filtered index/CHECK constraint nedeniyle SQLite eklenemiyor. **Devir:** T-future, gerekirse Notifications.Tests'e SQLite-friendly fixture.
- **K2 — `getMe` / `getWebhookInfo` API kapsanmadı:** Plan §5.2'de `sendMessage` + `setWebhook` listeli; runbook'taki getWebhookInfo örnek curl operasyonel doğrulama için. Bot'un sağlık probu olarak `getMe` çağrılması T-future (T94 monitoring task'ı veya equivalent).
- **K3 — Webhook idempotency storage SQL Server (Redis değil):** Plan §5.2'de "Redis, TTL 24sa" yazıyor ama mevcut sidecar webhook paterni `ProcessedNonces` SQL Server tablosunda (T68). Tutarlılık için aynı tabloyu kullandık (`Source="telegram"`). Redis migration T-future opsiyonel — performans şu an yeterli (T63b retention job periyodik temizliyor).
- **K4 — Test fixture brute-force MaxFailedAttempts=2:** Production default 5, ama API.Tests fixture'da rate limit per-IP `auth` bucket budget (10/dk) korumak için 2'ye düşürüldü. Production behavior `appsettings.json` default değeriyle çalışır.
- **K5 — MarkdownV2 Markdown injection saldırı yüzeyi:** Allow-list-free escape (18 reserved + backslash). User-generated string'in (item adı, kullanıcı adı, transaction notu) escape'i çağrı sahasında yapılmalı; T79 channel handler title + body'i toplu escape ediyor — template renderer'dan gelen string'ler escape'siz tutulamaz. Template-side escape policy review T-future (template güvenliği audit).
- **K6 — Telegram Bot.SetWebhook deployment-time job yok:** Runbook curl ile manuel `setWebhook` öneriyor. Otomatik init-job (`dotnet run --project tools/Skinora.TelegramSetup`) yazılmadı — production deploy pipeline'a entegrasyon T-future.
- **K7 — Notifications.Tests fixture'da TelegramSettings yok:** `DeferredNotificationDeliveryJobTests` ve `NotificationDeliveryJobTests` doğrudan `LoggingTelegramNotificationChannelHandler` örnekliyor (stub). Real handler integration test'i ayrı `TelegramNotificationChannelHandlerTests` dosyasında. Provider-switch DI suite'i tetiklenmiyor — provider switch yalnız `Program.cs` composition root'da çalıştığı için module-level test'leri için ek yapı gerekmedi.

## Notlar

- **Working tree check (Adım -1):** Branch açıldığında temiz (`git status --short` → boş).
- **Main CI startup check (Adım 0):** ✓ 3/3 success → T78 (b8d2f26 run 26021262104/26021262112) + T77 (66af642 run 26000220452). Task'a başlamadan kontrol edildi.
- **Dış varsayım doğrulama (Adım 4):**
  - Telegram Bot API public + ücretsiz: `core.telegram.org/bots/api` resmi doc, plan tier gerekmiyor ✓
  - MarkdownV2 escape karakter listesi (18 char): Telegram docs "MarkdownV2 style" — plan ile birebir aynı ✓
  - Rate limit'ler (1/30 msg/s): Telegram FAQ `my-bot-is-hitting-limits` ile doğrulandı ✓
  - `X-Telegram-Bot-Api-Secret-Token` header convention: Telegram `setWebhook` docs ✓
  - HTTP client: T78 raw HttpClient precedent (`ResendEmailClient`) — Telegram.Bot NuGet'in yerine + plan onayı (user) ✓
- **Bağımlılık kontrolü (Adım 2):** T37 (✓ Tamamlandı, b383983+7767fc7), T35 (✓ Tamamlandı F2 Gate Check'te validated). İki bağımlılık da kapalı.
- **Scope onayı (Adım 5):** İki soru proje sahibine sunuldu → "Spec-gap fix T79'da düzelt (Recommended)" + "Raw HttpClient (Recommended)" seçimleri.

---

## Doğrulama Sonucu — Bağımsız Validator

**Tarih:** 2026-05-18
**Branch:** `task/T79-telegram-bot-integration`
**Commit:** `9878486` (validate öncesi HEAD)

### Verdict: ✓ PASS

### Adım -1 / 0 / 0b Hard-stop kontrolleri
- Working tree temiz (`git status --short` boş) ✓
- Main CI son 3 run conclusion=success (T78 `26021262104` + `26021262112` + T77 `26000220452`) ✓
- Repo memory'de T79 satırı mevcut (`.claude/memory/MEMORY.md` L181 vd.) ✓

### Kabul Kriterleri (8/8 ✓)

| # | Kriter | Sonuç | Bağımsız kanıt |
|---|---|---|---|
| 1 | BotFather setup + token alma | ✓ | `TELEGRAM_SETUP.md §1` + `appsettings.json` `Telegram__BotToken=REPLACE_IN_ENV` fail-closed trip-wire |
| 2 | Deep link (10dk TTL, single-use, 122+ bit entropy, /start eşleşme, chat_id kayıt) | ✓ | `TelegramConnectionService.GenerateCode` 16-byte RNG hex (128 bit) + `CodeTtlSeconds=600` + `RedisTelegramVerificationStore.ConsumeAsync` GETDEL single-use + `WebhooksController.StartCommandRegex` `^/start\s+(SKN-[A-Za-z0-9]+)\s*$` + `_preferences.UpsertPreferenceAsync` chat_id=`payload.TelegramUserId.ToString` + brute-force 5 fail counter (`MaxFailedAttempts=5`) |
| 3 | POST /webhooks/telegram + secret_token doğrulaması | ✓ | `WebhooksController.Telegram` `[HttpPost("telegram")]` + `TelegramWebhookSignatureMiddleware` `CryptographicOperations.FixedTimeEquals` constant-time compare; secret unset → 401 fail-closed |
| 4 | Webhook idempotency (update_id, 24sa TTL) | ✓ (advisory) | `TelegramWebhookSignatureMiddleware` `ProcessedNonces(Source="telegram", Nonce=update_id, ExpiresAt=Now+IdempotencyTtlHours)` + SQL Server UNIQUE → 200 Idempotent. **Storage divergence:** spec §5.2 "Redis"; impl ProcessedNonces SQL Server (T68 paterni — sidecar webhook tek dedup tablosunda buluşur, T63b retention job temizliyor). Semantik (dedup + 24h TTL) eşdeğer; K3 doc'lu architectural choice |
| 5 | sendMessage MarkdownV2 + escape helper | ✓ | `TelegramBotClient.SendMessageAsync` `parse_mode=MarkdownV2` + `MarkdownV2Escaper.Escape` 18 reserved char + backslash; `TelegramNotificationChannelHandler.FormatMessage` title+body iki kanal escape |
| 6 | Rate limit (1 msg/s per chat, 30 msg/s global, sıralı kuyruk) | ✓ | `TelegramRateLimiter` `ChatGate` SemaphoreSlim(1,1) + per-chat `MinInterval = 1/PerChatRatePerSecond` + global `LinkedList<DateTimeOffset>` sliding window 1s + `Func<DateTimeOffset>` injected clock |
| 7 | Hata yönetimi (429 retry_after, 403 4-way reason, 400 disconnect, 5xx 3 retry) | ✓ | `TelegramBotClient.PostAsync` 5xx/429 → `TelegramTransientException(retry_after)`, 403 → `ClassifyForbidden` 4-way (`BotBlockedByUser`/`UserDeactivated`/`CannotMessageBots`/`CannotInitiateConversation`/Unknown), 400+4xx → `TelegramPermanentException`; channel handler 403→preference auto-disable + 429→`RegisterRetryAfter`; 3 retry immediate-tier `NotificationDeliveryJob` (T78 paterni) |
| 8 | setWebhook (url + secret_token + max_connections=40 + allowed_updates=["message"]) | ✓ | `TelegramSetWebhookRequest` defaults `MaxConnections=40`, `AllowedUpdates=["message"]`, `DropPendingUpdates` opsiyonel; `ITelegramBotClient.SetWebhookAsync` `setWebhook` HTTP POST; runbook §4 curl örneği parametre listesi tam |

### Doğrulama Kontrol Listesi (2/2 ✓)
- [x] 08 §5.1–§5.5 tüm entegrasyon detayları uygulanmış mı? — 5.1 connection ✓ / 5.2 API ✓ / 5.3 limits ✓ / 5.4 errors ✓ / 5.5 dependency risk runbook §6 secret rotation ✓; K3 idempotency storage advisory (semantik eşdeğer)
- [x] 403 neden ayrıştırma doğru mu? — `ClassifyForbidden` 4 case + Unknown fallback, plan §5.4 tablosuyla 1:1 + `TelegramBotClientTests` `[Theory]` UserDeactivated/CannotMessageBots/CannotInitiateConversation/BotBlockedByUser

### Test Sonuçları (bağımsız re-run)

| Tür | Sonuç | Komut | Çıktı |
|---|---|---|---|
| Build Release | 0W/0E | `dotnet build Skinora.sln -c Release` | `Build succeeded. 0 Warning(s) 0 Error(s)` (7.83s) |
| Format | Δ=0 | `dotnet format Skinora.sln --verify-no-changes` | exit=0, no output |
| Unit (Shared/Telegram) | 41/41 ✓ | `dotnet test Skinora.Shared.Tests --filter ~Telegram` | `Passed! - Failed: 0, Passed: 41, Skipped: 0, Total: 41, Duration: 79 ms` |
| Integration (AccountSettings) | 25/25 ✓ | `dotnet test Skinora.API.Tests --filter ~AccountSettings` | `Passed! - Failed: 0, Passed: 25, Skipped: 0, Total: 25, Duration: 10 s` |
| Unit (Notifications/Telegram) | 6/6 ✓ | `dotnet test Skinora.Notifications.Tests --filter ~Telegram` | `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6, Duration: 17 s` |
| Task branch CI HEAD `9878486` | n/a (rapor finalize) | — | T79 rapor finalize commit'i; merge öncesi yeni run tetiklenecek |
| Task branch CI HEAD `2504310` | 10/10 ✓ | `gh run view 26025772241` | Detect/Lint/Build/Unit/Integration/Contract/Migration/Docker/CI Gate ✓, Guard skipped (PR) |

### Güvenlik Kontrolü
- [x] Secret sızıntısı: temiz — `BotToken` + `WebhookSecretToken` env var via `REPLACE_IN_ENV` trip-wire, Provider=`logging` default fail-closed; HttpClient BaseAddress'a embed olan token Information log seviyesinde dışarı çıkmıyor (log mesajları yalnız `{Method}` argüman alıyor)
- [x] Auth etkisi: temiz — webhook controller `[AllowAnonymous]` ama middleware secret_token + idempotency önce çalışıyor; constant-time compare (`FixedTimeEquals`) timing attack'a kapalı; secret unset → 401 fail-closed
- [x] Input validation: temiz — `update_id` JSON parse defensive (`TryGetProperty` + `TryGetInt64`), body stream `EnableBuffering` + rewind; malformed body → 200 noop (Telegram retry storm yok); `StartCommandRegex` constrained `^/start\s+(SKN-[A-Za-z0-9]+)\s*$`
- [x] Yeni dış bağımlılık: **yok** — `git diff main...HEAD -- "*.csproj"` 0Δ; raw HttpClient + System.Text.Json + Microsoft.Data.SqlClient (mevcut bağımlılıklar)

### Bulgular
| # | Seviye | Açıklama | Etkilenen dosya |
|---|---|---|---|
| A1 | advisory | Webhook idempotency storage spec §5.2'de Redis; impl ProcessedNonces SQL Server. T68 sidecar webhook paterniyle tutarlı, semantik (dedup + 24h TTL) eşdeğer, T63b retention job temizliyor. K3 Known Limitations'da doc'lu, FAIL değil. | `TelegramWebhookSignatureMiddleware.cs` |

**S-bulgu yok** (S1/S2/S3 hiçbiri).

### Yapım Raporu Karşılaştırması
- **Uyum:** Tam uyumlu. Yapım raporundaki 8/8 ✓ ve K1–K7 listesi bağımsız doğrulama bulgularıyla bire bir eşleşiyor; ek S-bulgu yok.
- **Test count:** Notifications.Tests filter `~Telegram` bağımsız run'da 6/6 PASS (rapor "5 test" yazıyor — 5 `TelegramNotificationChannelHandlerTests` + 1 başka Telegram-isimli test; semantik fark yok).
- **CI sonucu:** Rapor `26025772241` 10/10 ✓ kanıtladı; bağımsız `gh pr view 120` rollup aynı sonucu gösteriyor.

### Merge öncesi durum
- PR #120 `MERGEABLE`, base=`main`, head=`9878486` (rapor finalize)
- Branch isolation: ✓ (`git log main..HEAD --format='%s' | grep -oE '^T[0-9]+'` → yalnız T79)
- Task branch CI HEAD `2504310` ardından `9878486` finalize commit'i için run merge sonrası post-merge CI watch ile takip edilecek

---

## Validate Bitiş Kapısı (12/12 ✓)

- [x] Branch push edildi (`task/T79-telegram-bot-integration`)
- [x] PR açıldı (#120)
- [x] PR numarası raporda yazılı
- [x] Rapor + status push edildi (`d56f6ef` + `2504310` + bu finalize commit)
- [x] CI run tamamlandı (run `26025772241` concluded)
- [x] CI sonucu `success` (10/10 ✓)
- [x] Branch isolation check temiz (`T79` tek başına)
- [x] Repo memory'de T79 satırı eklendi
- [x] Bağımsız validator hard-stop kontrolleri PASS (-1/0/0b)
- [x] 8/8 kabul kriteri ✓ + 2/2 doğrulama listesi ✓ + 0 S-bulgu
- [x] Lokal Build 0W/0E + Format Δ=0 + 72/72 hedef test PASS
- [x] Verdict: ✓ PASS
