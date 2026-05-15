# T68 — Steam Sidecar Webhook Callback ve Backend Entegrasyonu

**Faz:** F4 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-05-15

---

## Yapılan İşler

### Replay-protection altyapısı (Shared)

- `backend/src/Skinora.Shared/Persistence/Webhooks/ProcessedNonce.cs` — yeni entity. `IAppendOnly` (06 §4.2): inbound webhook'tan kabul edilen `(Source, Nonce)` çiftleri için tek-yön marker. Alanlar: `Id` (Guid PK), `Source` (max 50, sidecar discriminator — ileride blockchain/notification sidecar'lar aynı tabloyu paylaşır), `Nonce` (max 100, UUID v4), `ProcessedAt`, `ExpiresAt`.
- `backend/src/Skinora.Shared/Persistence/Webhooks/Configurations/ProcessedNonceConfiguration.cs` — EF Core mapping. `(Source, Nonce)` UNIQUE index (`UX_ProcessedNonces_Source_Nonce`) DB seviyesinde replay tespitinin nihai garantisidir; cleanup taraması için `IX_ProcessedNonces_ExpiresAt`.
- `backend/src/Skinora.Shared/Persistence/AppDbContext.cs` — `DbSet<ProcessedNonce> ProcessedNonces` ve `using Skinora.Shared.Persistence.Webhooks` eklendi.
- `backend/src/Skinora.Shared/Persistence/Migrations/20260515202559_T68_AddProcessedNonces.cs` — yeni tablo + 2 index. Migration zinciri 9 → 10.

### Webhook signature middleware (API)

- `backend/src/Skinora.API/Middleware/WebhookSettings.cs` — `Webhook` config bölümü: `SteamSharedSecret`, `ReplayWindowSeconds` (default 300), `NonceRetentionSeconds` (default 3600). Sidecar `WEBHOOK_SECRET` env var ile aynı secret'i okur (09 §17.5).
- `backend/src/Skinora.API/Middleware/WebhookSignatureMiddleware.cs` — sadece `/api/v1/webhooks/steam` prefix'i için aktif. Akış:
  1. Header eksikliği (`X-Signature` / `X-Timestamp` / `X-Nonce`) → 401 `WEBHOOK_HEADERS_MISSING`.
  2. Secret yapılandırılmamış (`SteamSharedSecret` boş) → 401 `WEBHOOK_UNAUTHORIZED` (production'da fail-safe).
  3. Parse edilemeyen timestamp → 401 `WEBHOOK_TIMESTAMP_INVALID`.
  4. ±`ReplayWindowSeconds` dışında timestamp → 401 `WEBHOOK_TIMESTAMP_OUT_OF_WINDOW`.
  5. Request body buffering (`EnableBuffering`) — controller yine okuyabilir; body `Position = 0`'a sarılır.
  6. HMAC-SHA256 hesap: `HMAC(timestamp + nonce + body, secret)` (sidecar `WebhookClient.ts:21` ile birebir). `CryptographicOperations.FixedTimeEquals` ile sabit-zaman karşılaştırma; mismatch → 401 `WEBHOOK_SIGNATURE_INVALID`.
  7. Nonce DB insert — `(Source, Nonce)` UNIQUE constraint replay'i atomik yakalar (insert-first / catch unique violation pattern; SQL Server 2601/2627 + SQLite "UNIQUE constraint failed" mesajı). Replay → 401 `WEBHOOK_NONCE_REPLAY`.
  8. Pipeline `_next` çağrılır.
- `backend/src/Skinora.API/Program.cs` — `WebhookSettings` config binding + middleware pipeline adım 5a (CorrelationId/Logging/Exception sonrası, controllers/auth öncesi).

### Steam webhook handler (Steam modülü)

- `backend/src/Modules/Skinora.Steam/Application/Webhooks/SteamWebhookPayloads.cs` — Sidecar event isim sabitleri + DTO'lar:
  - `SteamWebhookEvents` — 9 event sabiti (`bot.session_failed`, `bot.removed_from_pool`, `trade_offer.{sent,failed,accepted,declined,expired,countered,invalid_items}`).
  - `SteamWebhookEnvelope<T>` — generic dış zarf (`event`/`timestamp`/`data`).
  - `BotEventData`, `TradeOfferEventData` — sidecar `WebhookPayloads.ts` discriminated union'larını backend tarafında temsil eden flat DTO'lar (her event türünden alan seti birleştirilmiş, deserializer optional handle eder).
- `backend/src/Modules/Skinora.Steam/Application/Webhooks/ISteamWebhookHandler.cs` — interface + `TradeWebhookResult` enum (`Applied` / `Idempotent` / `Unknown`).
- `backend/src/Modules/Skinora.Steam/Application/Webhooks/SteamWebhookHandler.cs` — orkestrasyon:
  - **`HandleBotEventAsync`** — Warning seviyesinde structured log (`accountName`/`reason`/`status`/`correlationId`); audit log ve admin notification entegrasyonu **T96 forward devir (K1)**.
  - **`HandleTradeEventAsync`** — event'e göre 7 alt-handler:
    - `trade_offer.sent` → `TradeOffer` kaydı oluşturur (TransactionId + SteamTradeOfferId + Direction + PlatformSteamBotId + `Status=SENT` + `RetryCount=attempts-1` + `SentAt`). UNIQUE constraint replay'i yakalar. Bilinmeyen transactionId veya botAccountName → `Unknown` ack.
    - `trade_offer.failed` → `Status=FAILED` row (SteamTradeOfferId NULL, `ErrorMessage`=reason); bot çözülemezse log + ack (boş `PlatformSteamBotId` ile insert edilmez).
    - `trade_offer.accepted` → SteamTradeOfferId üzerinden lookup → state machine `EscrowItem` (escrow direction) veya `DeliverItem` (delivery direction).
    - `trade_offer.declined` → state machine `SellerDecline` / `BuyerDecline` + cancel reason.
    - `trade_offer.expired` → state machine `Timeout` + cancel reason.
    - `trade_offer.countered` + `invalid_items` → 08 §2.4: cancellation eşdeğeri (`SellerDecline`/`BuyerDecline` + tag'li reason).
  - Tüm status-change handler'ları paylaşılan `ApplyStatusChangeAsync` pipeline'ı:
    - `CanFire(trigger) == false` → idempotent ack (state machine zaten geçmiş olabilir).
    - `Fire()` throws → `Idempotent` döner, TradeOffer status yine de persist edilir (audit trail).
    - Aynı status replay → idempotent (TradeOffer zaten `newStatus`).
  - **Side-effect orchestration scope dışı (K2-K4 forward devir):** timeout cancel/reschedule (T47 servisi), outbox event publish (`TradeOfferAcceptedEvent` vb.), reputation/cooldown recompute. T68 yalnızca TradeOffer kaydı + state machine fire eder.

### Controller (API)

- `backend/src/Skinora.API/Controllers/SteamWebhooksController.cs` — `[ApiController]` + `[Route("api/v1/webhooks/steam")]` + `[AllowAnonymous]` (middleware signature kontrol ediyor):
  - `POST /api/v1/webhooks/steam/bot-events` (`SteamWebhookEnvelope<BotEventData>`)
  - `POST /api/v1/webhooks/steam/trade-events` (`SteamWebhookEnvelope<TradeOfferEventData>`)
  - Her ikisi `correlationId = X-Correlation-Id ?? TraceIdentifier` resolve eder. Tüm path'ler 200 + `ApiResponse<{ acknowledged: true, result?: <Applied|Idempotent|Unknown> }>` döner; payload deserialize hatası → 400.

### Retention (Hangfire recurring)

- `backend/src/Skinora.API/Retention/ProcessedNonceCleanupJob.cs` — her 15 dakikada bir `ExpiresAt < UtcNow` satırlarını 5000'lik batch'lerle `ExecuteDeleteAsync` ile siler.
- `backend/src/Skinora.API/Retention/RetentionJobsRegistrar.cs` — `ProcessedNonceCron = "*/15 * * * *"` recurring kayıt eklendi.
- `backend/src/Skinora.API/Program.cs` — `AddScoped<ProcessedNonceCleanupJob>()`.

### DI wiring (Steam modülü)

- `backend/src/Skinora.API/Configuration/SteamModule.cs` — `services.AddScoped<ISteamWebhookHandler, SteamWebhookHandler>()` eklendi.

### Config / Env

- `backend/src/Skinora.API/appsettings.json` — yeni `Webhook` bölümü (SteamSharedSecret=REPLACE_IN_ENV, ReplayWindowSeconds=300, NonceRetentionSeconds=3600).
- `docker-compose.yml` — `skinora-backend` env: `Webhook__SteamSharedSecret=${WEBHOOK_SECRET}`; `skinora-steam-sidecar` env: `WEBHOOK_SECRET=${WEBHOOK_SECRET}` (aynı secret iki yöne).
- `.env.example` — yeni `WEBHOOK_SECRET=` placeholder + T68 başlığı.

### Testler

- `backend/tests/Skinora.Steam.Tests/Integration/SteamWebhookHandlerTests.cs` — 10 integration test (SQL Server testcontainer, `IntegrationTestBase`):
  1. `TradeOfferSent_PersistsTradeOfferRow` — insert + alan doğrulama.
  2. `TradeOfferSent_DuplicateOfferId_IsIdempotent` — UQ_TradeOffers_SteamTradeOfferId; iki çağrı sonrası 1 row.
  3. `TradeOfferSent_UnknownTransaction_AcksWithoutInsert` — `Unknown` + DB temiz.
  4. `TradeOfferAccepted_OnEscrowDirection_FiresEscrowItemTrigger` — TRADE_OFFER_SENT_TO_SELLER → ITEM_ESCROWED + `ItemEscrowedAt` stamp.
  5. `TradeOfferDeclined_OnEscrowDirection_FiresSellerDeclineTrigger` — CANCELLED_SELLER + CancelReason/CancelledBy.
  6. `TradeOfferExpired_FiresTimeoutTriggerWithCancelReason` — CANCELLED_TIMEOUT.
  7. `TradeOfferCountered_IsTreatedAsCancellation` — 08 §2.4: CANCELLED_SELLER + TradeOffer.Status=DECLINED.
  8. `StatusChange_UnknownOfferId_AckedAsUnknown` — sidecar'ın race ettiği durum.
  9. `StatusChange_SameStatusReplay_IsIdempotent` — aynı status ikinci kez.
  10. `BotEvent_LogsAndAcks_WithoutDbWrite` — log-only path (DB unchanged).
- `backend/tests/Skinora.API.Tests/Integration/SteamWebhookEndpointTests.cs` — 6 e2e integration test (SQLite + `WebApplicationFactory`):
  1. `TradeEvents_MissingHeaders_Returns401`
  2. `TradeEvents_InvalidSignature_Returns401`
  3. `TradeEvents_StaleTimestamp_Returns401` — `-15 dk` timestamp
  4. `TradeEvents_NonceReplay_SecondRequestReturns401` — UNIQUE-backed
  5. `TradeEvents_HappyPath_DrivesStateMachine` — full pipeline `trade_offer.accepted` → ITEM_ESCROWED, TradeOffer ACCEPTED
  6. `BotEvents_HappyPath_Returns200`
- `backend/tests/Skinora.API.Tests/Integration/Retention/ProcessedNonceCleanupJobTests.cs` — 2 integration test (SQL Server testcontainer):
  1. `Expired_Rows_Are_Purged_Fresh_Rows_Preserved` — 3 satır → 2 purge, 1 kalır.
  2. `No_Expired_Rows_Returns_Zero` — idempotent no-op.

## Etkilenen Modüller / Dosyalar

**Oluşturulan**
- `backend/src/Skinora.Shared/Persistence/Webhooks/ProcessedNonce.cs`
- `backend/src/Skinora.Shared/Persistence/Webhooks/Configurations/ProcessedNonceConfiguration.cs`
- `backend/src/Skinora.Shared/Persistence/Migrations/20260515202559_T68_AddProcessedNonces.cs` (+ `.Designer.cs`)
- `backend/src/Skinora.API/Middleware/WebhookSettings.cs`
- `backend/src/Skinora.API/Middleware/WebhookSignatureMiddleware.cs`
- `backend/src/Skinora.API/Controllers/SteamWebhooksController.cs`
- `backend/src/Skinora.API/Retention/ProcessedNonceCleanupJob.cs`
- `backend/src/Modules/Skinora.Steam/Application/Webhooks/SteamWebhookPayloads.cs`
- `backend/src/Modules/Skinora.Steam/Application/Webhooks/ISteamWebhookHandler.cs`
- `backend/src/Modules/Skinora.Steam/Application/Webhooks/SteamWebhookHandler.cs`
- `backend/tests/Skinora.Steam.Tests/Integration/SteamWebhookHandlerTests.cs`
- `backend/tests/Skinora.API.Tests/Integration/SteamWebhookEndpointTests.cs`
- `backend/tests/Skinora.API.Tests/Integration/Retention/ProcessedNonceCleanupJobTests.cs`

**Güncellenen**
- `backend/src/Skinora.Shared/Persistence/AppDbContext.cs` (DbSet<ProcessedNonce>)
- `backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs` (auto-generated)
- `backend/src/Skinora.API/Program.cs` (`WebhookSettings` binding + middleware adım 5a + cleanup job DI)
- `backend/src/Skinora.API/Configuration/SteamModule.cs` (`ISteamWebhookHandler` DI)
- `backend/src/Skinora.API/Retention/RetentionJobsRegistrar.cs` (`ProcessedNonceCron`)
- `backend/src/Skinora.API/appsettings.json` (`Webhook` config)
- `docker-compose.yml` (backend + sidecar `WEBHOOK_SECRET` env)
- `.env.example` (`WEBHOOK_SECRET` placeholder)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Sidecar → Backend webhook: HMAC-SHA256 imzalama, timestamp, nonce, signature header | ✓ | Sidecar tarafı T64'te `WebhookClient.ts` (`crypto.createHmac('sha256', secret).update(`${timestamp}${nonce}${body}`)`). Backend tarafı `WebhookSignatureMiddleware.ComputeSignature` aynı kompozisyon. `X-Signature`/`X-Timestamp`/`X-Nonce`/`X-Correlation-Id` header pipeline'da okunur. |
| 2 | Backend webhook handler: WebhookSignatureMiddleware ile doğrulama | ✓ | `Program.cs` pipeline adım 5a → `app.UseMiddleware<WebhookSignatureMiddleware>()`. Path-scoped (`/api/v1/webhooks/steam` prefix). Test: `SteamWebhookEndpointTests.TradeEvents_HappyPath_DrivesStateMachine` (geçer signature pass) + `TradeEvents_InvalidSignature_Returns401` (reddedilir). |
| 3 | Replay koruması: timestamp ±5dk, nonce tekrar kontrolü (ProcessedNonce) | ✓ | Timestamp: `WebhookSettings.ReplayWindowSeconds=300`; `Math.Abs((UtcNow - sentAt).TotalSeconds) > 300` → 401. Test: `TradeEvents_StaleTimestamp_Returns401`. Nonce: insert-first into `ProcessedNonces` ile `(Source, Nonce)` UNIQUE; replay → `DbUpdateException` (2601/2627 veya SQLite UNIQUE message) → 401. Test: `TradeEvents_NonceReplay_SecondRequestReturns401`. |
| 4 | Trade offer durum güncellemelerini backend'de işleme → state machine tetikleme | ✓ | `SteamWebhookHandler.ApplyStatusChangeAsync` `TradeOffer.SteamTradeOfferId` lookup → `TransactionStateMachine.Fire(trigger)`. 7 event x 2 yön: accepted→EscrowItem/DeliverItem, declined→SellerDecline/BuyerDecline, expired→Timeout, countered+invalid_items→aynı decline path'i. Test: `SteamWebhookHandlerTests` 4-7 + `TradeEvents_HappyPath_DrivesStateMachine` (TRADE_OFFER_SENT_TO_SELLER → ITEM_ESCROWED). |
| 5 | Idempotent işleme | ✓ | (a) Sent: UQ_TradeOffers_SteamTradeOfferId ile DB seviyesinde + handler `existing` lookup ile `Idempotent`. Test: `TradeOfferSent_DuplicateOfferId_IsIdempotent`. (b) Status change: aynı `newStatus` early-return; `CanFire == false` → `Idempotent`. Test: `StatusChange_SameStatusReplay_IsIdempotent`. (c) Nonce: aynı `(Source, Nonce)` ile gelen 2. istek → 401. |

## Doğrulama Kontrol Listesi (Plan §11.3 + §17.5)

| # | Madde | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 05 §3.4 güvenlik kuralları eksiksiz mi? | ✓ | 4-katman tablo karşılığı: Payload signing (HMAC-SHA256 ✓), Doğrulama (`WebhookSignatureMiddleware` ✓), Replay koruması (timestamp ±5dk + nonce ✓), Network (Docker internal — compose'da `skinora-network` ortak, sidecar `BACKEND_URL` internal hostname). |
| 2 | Replay koruması çalışıyor mu? | ✓ | Timestamp skew + nonce DB UNIQUE'ı paralel testlerle teyit; 6 e2e + 2 cleanup job test. |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend Release build | ✓ 0W/0E | `dotnet build backend/Skinora.sln -c Release` (24 proje) |
| Backend Debug build | ✓ 0W/0E | `dotnet build backend/Skinora.sln -c Debug` |
| Format check | ✓ | `dotnet format --verify-no-changes --severity warn` clean |
| Webhook endpoint integration (SQLite, lokal) | ✓ 6/6 PASS | `dotnet test --filter "FullyQualifiedName~SteamWebhookEndpointTests"` |
| API.Tests full suite (lokal Docker yok) | ✓ 336/340 PASS | 4 fail RestartRecoveryServiceTests **Docker testcontainer** sebebiyle pre-existing (CI'de geçiyor). 0 yeni regresyon. |
| Webhook handler integration (Steam.Tests, SQL Server testcontainer) | — CI'ye bırakıldı | Lokal Docker yok; T11.3 design ile CI runner'da koşacak. |
| ProcessedNonceCleanupJobTests (SQL Server testcontainer) | — CI'ye bırakıldı | Aynı sebep. |

## Altyapı Değişiklikleri

- **Migration:** ✓ `20260515202559_T68_AddProcessedNonces` — yeni `ProcessedNonces` tablosu (5 kolon, 2 index).
- **Yeni paket:** Yok (sadece `System.Security.Cryptography` BCL + mevcut EF Core).
- **Config/env:** `Webhook` bölümü `appsettings.json` + `WEBHOOK_SECRET` env (`docker-compose.yml` ve `.env.example`).
- **Docker:** `skinora-backend` ve `skinora-steam-sidecar` env'lerine `WEBHOOK_SECRET` paylaşımı eklendi.

## Commit & PR

- Branch: `task/T68-steam-webhook`
- Commit: `5a34de0` (kod) + `f67e435` (rapor/status metadata)
- PR: [#109](https://github.com/turkerurganci/Skinora/pull/109)
- CI: run [`25942576204`](https://github.com/turkerurganci/Skinora/actions/runs/25942576204) ✓ 10/10 job success (Lint + Detect + Guard skipped direct push olmadığı için + Build + Unit + Integration + Migration dry-run + Contract + Docker backend + CI Gate). Önceki run `25942543043` ikinci push ile concurrency policy gereği cancel oldu — son tamamlanmış run otoritedir (T11.2 concurrency notu).

## Known Limitations / Follow-up

- **K1 — Bot lifecycle event'lerinden admin notification → T96 devir:** `bot.session_failed` ve `bot.removed_from_pool` Warning seviyesinde structured log yazıyor; mevcut platformda admin notification kanalı (push/email/dashboard banner) henüz yok. T96 admin notification system'i geldiğinde `SteamWebhookHandler.HandleBotEventAsync` içine notification publisher injecte edilir.
- **K2 — Timeout cancel/reschedule → T68'in scope dışında, future task:** state machine `EscrowItem`/`DeliverItem`/`*Decline`/`Timeout` trigger'ları geçirildiğinde `TimeoutSchedulingService` mevcut state transition pattern'i için Hangfire job cancel/yeni job schedule yapar; webhook handler bu side-effect orchestration'ını henüz çağırmıyor. Bunu T47/T51 patterns'iyle aynı pipeline'a entegre etmek pure state-flip'in ötesine geçiyor. Forward devir adayı: bir sonraki F4 review.
- **K3 — Outbox event publishing → forward devir:** `trade_offer.accepted` mevcut `ItemEscrowedEvent` outbox kaydı yazmıyor; T48 publisher (mempool event consumer) zaten forward-deferred T61'e bağlı, T68'de döngüye girmiyor. Future PR.
- **K4 — Reputation/cooldown recompute → cancellation pipeline aynı şekilde:** `TransactionCancellationService` (T51) içindeki reputation refresh + cooldown stamp logic webhook tetiklemeli iptal durumlarında uygulanmıyor. Şu an webhook handler basit state-flip yapıyor; "sidecar-driven cancellation" için cancellation service çağrısı veya benzer pipeline gerekli. Future PR.
- **K5 — Bot lifecycle event'inde audit log entry yazılmadı:** `AuditAction` enum'da `BOT_SESSION_FAILED` veya benzeri bir kayıt türü yok; AuditLog yazımı scope dışı tutuldu. T96 ile birlikte AuditAction genişletilebilir.
- **K6 — `Webhook__SteamSharedSecret` `IsConfigured = false` fail-safe:** `SettingsBootstrapService` SystemSettings tablosunu yönetiyor; webhook secret env-driven (config binding), bootstrap fail-fast kapsamında değil. Boş secret durumunda middleware 401 döner (production safety). SystemSettings'e taşıma kararı F4 sonu reflektif gözden geçirme adayı.

## Notlar

- **Working tree (Adım -1):** 27 adet `*.csproj.lscache` (Visual Studio IntelliSense cache, `.gitignore`'da değil) untracked tespit edildi → ayrı `chore/gitignore-lscache` branch + PR #108 ile `*.csproj.lscache` `.gitignore`'a eklendi, squash-merge sonrası T68 branch'i temiz main'den açıldı.
- **Main CI startup check (Adım 0):** 3/3 son main run `success` — `25903519112` + `25903519092` (T67 #107 merge), `25881647522` (T66 #106 merge). HARD STOP yok.
- **Dış varsayımlar (Adım 4):**
  - `System.Security.Cryptography.HMACSHA256` + `CryptographicOperations.FixedTimeEquals` .NET 9 BCL'de ✓
  - `Stateless 5.20.1` (T44'te eklendi) — `TransactionStateMachine.CanFire`/`Fire` mevcut ✓
  - `EFCore.SqlServer 9.0.3` migration üretimi + SQLite test fallback için provider conditional ✓
  - Hangfire recurring job mevcut altyapı (T32/T63b kullandı) ✓
- **Mini güvenlik kontrolü:**
  - Secret sızıntısı: yok — `SteamSharedSecret` config'den okunur, log'a yazılmaz. `SecretMaskingEnricher` (T08) `secret` adlı property'leri maskler.
  - Auth: webhook controller `[AllowAnonymous]` ama path-scoped middleware HMAC + replay korumasıyla `Authenticated` denginde sıkı.
  - Input validation: middleware seviyesinde tüm 6 fail-path; controller body deserialization edemezse 400.
  - Yeni dış bağımlılık: yok.
- **Doğrulama önerisi:** Bağımsız validate chat açıldığında handler ve middleware testleri SQL Server testcontainer ile birlikte CI'de çalışır; yapım raporu görülmeden cross-check yapılabilir.
