# T75 — Blockchain Sidecar gecikmeli ödeme izleme

**Faz:** F4 | **Durum:** ⏳ Devam ediyor (doğrulama bekleniyor) | **Tarih:** 2026-05-17

---

## Yapılan İşler

- **Sidecar `PostCancelMonitorRegistry`** — kademeli state machine + cadence (08 §3.4):
  - `POST_CANCEL_24H` (30 s) → `POST_CANCEL_7D` (5 dk) → `POST_CANCEL_30D` (1 sa) → `STOPPED`
  - Window'lar `cancelledAt + 24h/7d/30d` anchor edilir (sidecar restart sonrası aynı boundary'de devam eder)
  - Tek `setInterval(tickIntervalMs)` shared tick + per-entry `nextPollAt` eligibility — N monitor tek timer ile sürür
  - **A — Sidecar self-clocked** (proje sahibi 2026-05-17 onayı): state transition kararı sidecar'da, backend webhook ile mirror'lar
  - Phase 1 (expected contract filter) + Phase 2 (filtersiz) — T71 aktif yolla aynı idempotency, spam/wrong-token sınıflandırma
  - `seenTxHashes` per-address dedup + retryable webhook bubble-up (next tick yeniden dener)
- **2 yeni HTTP endpoint:** `POST /api/monitor/post-cancel-start` + `POST /api/monitor/post-cancel-stop` (X-Internal-Key auth, idempotent)
- **2 yeni webhook event:** `payment.late_detected` (LatePaymentDetected) + `monitor.post_cancel_state_changed` (PostCancelMonitorStateChanged)
- **Backend `IPostCancelMonitorStarter` + `PostCancelMonitorStarter`** — 3 cancel handler ortak entry:
  - PaymentAddress.MonitoringStatus = `POST_CANCEL_24H`, MonitoringExpiresAt = `cancelledAt + 24h`
  - Outbox `PostCancelMonitorStartRequestedEvent` publish
  - Idempotent: PaymentAddress yok (CREATED-cancel before allocation) veya zaten POST_CANCEL_* → no-op
- **`PostCancelMonitorStartDispatcher`** (MediatR `INotificationHandler`) — outbox event → sidecar `IBlockchainSidecarClient.StartPostCancelMonitoringAsync` HTTP çağrısı; transient hata throw eder (outbox retry tetiklenir), 400 terminal log
- **`PostCancelMonitorRecoveryHook`** (`IHostedService`) — host startup'ta DB'den `MonitoringStatus IN POST_CANCEL_*` adresleri sidecar'a re-register eder; sidecar restart sonrası state kaybı önlenir
- **Cancel handler stamping** (4 yer): T51 user-cancel (TransactionCancellationService Stage 6d), T49 timeout (TimeoutExecutor + DeadlineScannerJob) ve T59 admin-cancel (AdminTransactionService AD19 5d.1 + ApplyEmergencyHoldReleaseAsync CANCEL aksiyonu) — hepsi aynı `_postCancelMonitor.RequestStartAsync()` çağrısı
- **Backend webhook handler 2 yeni method:**
  - `HandleLatePaymentDetectedAsync` — incoming BUYER_PAYMENT row (Status=DETECTED) + `AmountValidationService.ValidateLatePaymentDetectedAsync` (LATE_PAYMENT_REFUND queue, gas fee düşülü, 2× gas min threshold → block + admin alert, T72 RefundDecisionService reuse)
  - `HandlePostCancelMonitorStateChangedAsync` — `PaymentAddress.MonitoringStatus` + `MonitoringExpiresAt` mirror; idempotent (aynı state ack → Idempotent)
- **`AmountValidationService.ValidateLatePaymentDetectedAsync`** — T72 multi-payment branch pattern'inin LATE_PAYMENT_REFUND variant'ı: state machine ilerletmez, sadece refund intent + outbox event
- **`LatePaymentRefundRequestedEvent`** — yeni outbox event (BuyerId notification recipient + RefundTransactionId + MonitorState audit)
- **`PostCancelMonitorStartRequestedEvent`** — yeni outbox event (dispatcher consumer)
- **`KnownStablecoinContracts`** — `internal` → `public static` + `ResolveContractAddress(StablecoinType)` helper (Starter + RecoveryHook reuse)
- **MediatR 12.4.1** Skinora.Transactions.csproj'a eklendi (dispatcher için, Notifications/Realtime modüllerinde zaten kullanılıyor)
- **`WebhookSignatureMiddleware` path-scope:** `/api/v1/webhooks/blockchain` prefix-based — 2 yeni endpoint otomatik kapsanır (T68 K-future kapanışı, kod değişikliği yok)

## Etkilenen Modüller / Dosyalar

### Sidecar (sidecar-blockchain)
- [`src/monitor/PostCancelMonitor.ts`](../../sidecar-blockchain/src/monitor/PostCancelMonitor.ts) — stub'tan tam class'a (450+ satır)
- [`src/monitor/PostCancelMonitor.test.ts`](../../sidecar-blockchain/src/monitor/PostCancelMonitor.test.ts) — 26 Vitest test (state derivation 6 + transitions 4 + cadence 3 + emission 6 + stop/shutdown 3 + delivery 2 + defaults 2)
- [`src/api/monitorHandlers.ts`](../../sidecar-blockchain/src/api/monitorHandlers.ts) — `postCancelStartHandler` + `postCancelStopHandler` factory + validation
- [`src/api/routes.ts`](../../sidecar-blockchain/src/api/routes.ts) — 2 yeni route + RouterDeps genişletme
- [`src/webhook/WebhookPayloads.ts`](../../sidecar-blockchain/src/webhook/WebhookPayloads.ts) — 2 yeni event sabiti + `PostCancelMonitorStates` enum + 2 yeni data interface
- [`src/config/index.ts`](../../sidecar-blockchain/src/config/index.ts) — 7 yeni env değişkeni (tick interval + 3 cadence + 3 window) + 2 yeni webhook endpoint
- [`src/index.ts`](../../sidecar-blockchain/src/index.ts) — `PostCancelMonitorRegistry` DI + shutdown hook

### Backend
- [`backend/src/Modules/Skinora.Transactions/Application/PostCancel/IPostCancelMonitorStarter.cs`](../../backend/src/Modules/Skinora.Transactions/Application/PostCancel/IPostCancelMonitorStarter.cs)
- [`backend/src/Modules/Skinora.Transactions/Application/PostCancel/PostCancelMonitorStarter.cs`](../../backend/src/Modules/Skinora.Transactions/Application/PostCancel/PostCancelMonitorStarter.cs)
- [`backend/src/Modules/Skinora.Transactions/Application/PostCancel/PostCancelMonitorStartDispatcher.cs`](../../backend/src/Modules/Skinora.Transactions/Application/PostCancel/PostCancelMonitorStartDispatcher.cs)
- [`backend/src/Modules/Skinora.Transactions/Application/PostCancel/PostCancelMonitorRecoveryHook.cs`](../../backend/src/Modules/Skinora.Transactions/Application/PostCancel/PostCancelMonitorRecoveryHook.cs)
- [`backend/src/Skinora.Shared/Events/PostCancelMonitorStartRequestedEvent.cs`](../../backend/src/Skinora.Shared/Events/PostCancelMonitorStartRequestedEvent.cs)
- [`backend/src/Skinora.Shared/Events/LatePaymentRefundRequestedEvent.cs`](../../backend/src/Skinora.Shared/Events/LatePaymentRefundRequestedEvent.cs)
- [`backend/src/Modules/Skinora.Transactions/Application/Webhooks/BlockchainWebhookPayloads.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Webhooks/BlockchainWebhookPayloads.cs) — 2 event sabiti + 2 data class
- [`backend/src/Modules/Skinora.Transactions/Application/Webhooks/IBlockchainWebhookHandler.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Webhooks/IBlockchainWebhookHandler.cs) — 2 method
- [`backend/src/Modules/Skinora.Transactions/Application/Webhooks/BlockchainWebhookHandler.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Webhooks/BlockchainWebhookHandler.cs) — 2 method impl + TryParseMonitoringStatus + TryParseUtc helpers
- [`backend/src/Modules/Skinora.Transactions/Application/Webhooks/IAmountValidationService.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Webhooks/IAmountValidationService.cs) — `ValidateLatePaymentDetectedAsync` + 2 yeni outcome
- [`backend/src/Modules/Skinora.Transactions/Application/Webhooks/AmountValidationService.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Webhooks/AmountValidationService.cs) — `ValidateLatePaymentDetectedAsync` impl + `KnownStablecoinContracts.ResolveContractAddress` helper
- [`backend/src/Modules/Skinora.Transactions/Application/PaymentAddresses/IBlockchainSidecarClient.cs`](../../backend/src/Modules/Skinora.Transactions/Application/PaymentAddresses/IBlockchainSidecarClient.cs) — `StartPostCancelMonitoringAsync` + `StopPostCancelMonitoringAsync` + `PostCancelMonitorStartRequest` record
- [`backend/src/Modules/Skinora.Transactions/Application/PaymentAddresses/HttpBlockchainSidecarClient.cs`](../../backend/src/Modules/Skinora.Transactions/Application/PaymentAddresses/HttpBlockchainSidecarClient.cs) — 2 method impl + ortak `SendCommandAsync` helper
- [`backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionCancellationService.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionCancellationService.cs) — DI + Stage 6d stamp
- [`backend/src/Modules/Skinora.Transactions/Application/Admin/AdminTransactionService.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Admin/AdminTransactionService.cs) — DI + AD19 Stage 5d.1 stamp + ApplyEmergencyHoldReleaseAsync stamp
- [`backend/src/Modules/Skinora.Transactions/Application/Timeouts/TimeoutExecutor.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Timeouts/TimeoutExecutor.cs) — DI + post-sideEffects stamp
- [`backend/src/Modules/Skinora.Transactions/Application/Timeouts/DeadlineScannerJob.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Timeouts/DeadlineScannerJob.cs) — DI + per-transaction stamp (idempotent on missing PA)
- [`backend/src/Skinora.API/Controllers/BlockchainWebhooksController.cs`](../../backend/src/Skinora.API/Controllers/BlockchainWebhooksController.cs) — 2 yeni route
- [`backend/src/Skinora.API/Configuration/TransactionsModule.cs`](../../backend/src/Skinora.API/Configuration/TransactionsModule.cs) — Starter scoped + Dispatcher scoped + INotificationHandler binding + RecoveryHook hosted
- [`backend/src/Modules/Skinora.Transactions/Skinora.Transactions.csproj`](../../backend/src/Modules/Skinora.Transactions/Skinora.Transactions.csproj) — MediatR 12.4.1 PackageReference

### Test dosyaları
- [`backend/tests/Skinora.Transactions.Tests/Helpers/NoOpPostCancelMonitorStarter.cs`](../../backend/tests/Skinora.Transactions.Tests/Helpers/NoOpPostCancelMonitorStarter.cs) — test stub
- [`backend/tests/Skinora.Transactions.Tests/Integration/Timeouts/TimeoutTestSupport.cs`](../../backend/tests/Skinora.Transactions.Tests/Integration/Timeouts/TimeoutTestSupport.cs) — `NoOpPostCancelMonitor()` helper
- [`backend/tests/Skinora.Transactions.Tests/Integration/PostCancel/PostCancelMonitorStarterTests.cs`](../../backend/tests/Skinora.Transactions.Tests/Integration/PostCancel/PostCancelMonitorStarterTests.cs) — 7 integration test (no-op + stamp + Theory[4] idempotency + USDC)
- Test stub güncellemeleri: `StubBlockchainSidecarClient` (2 method); `TimeoutExecutorTests`/`TimeoutExecutorSideEffectsTests`/`DeadlineScannerJobTests`/`DeadlineScannerJobSideEffectsTests`/`AdminTransactionServiceTests`/`TransactionCancellationServiceTests` constructor güncellemeleri

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | İptal sonrası kademeli polling: 30 s → 5 dk → 1 sa → durdur (MonitoringStatus POST_CANCEL_24H → 7D → 30D → STOPPED) | ✓ | [`PostCancelMonitor.ts:80-90`](../../sidecar-blockchain/src/monitor/PostCancelMonitor.ts) DEFAULT cadence/window + [`PostCancelMonitor.ts:240-310`](../../sidecar-blockchain/src/monitor/PostCancelMonitor.ts) state machine; Vitest 4/4 transition test PASS (`advances POST_CANCEL_24H → POST_CANCEL_7D`, `advances POST_CANCEL_7D → POST_CANCEL_30D`, `advances POST_CANCEL_30D → STOPPED and removes the entry`, `cascades multiple transitions in a single tick`) |
| 2 | Gecikmeli ödeme tespit edilirse → alıcının iade adresine otomatik iade | ✓ | Sidecar `emitLatePaymentDetected` → backend `HandleLatePaymentDetectedAsync` → `AmountValidationService.ValidateLatePaymentDetectedAsync` → `LATE_PAYMENT_REFUND` queue (T73 dispatch pipeline reuse); Vitest `emits LatePaymentDetected for the expected token (phase 1)` PASS |
| 3 | Gas fee düşülür | ✓ | `AmountValidationService.ValidateLatePaymentDetectedAsync` `IRefundDecisionService.ResolveBuyerRefundAsync(received, gasFee)` çağrısı (T72 reuse) — net amount queue edilir, sub-threshold (< 2× gas) `IRefundBlockedAlertService.RaiseAsync` ile admin alert |

## Doğrulama Kontrol Listesi

- [x] **06 §2.16 MonitoringStatus değerleri doğru mu?** Sidecar `PostCancelMonitorStates` (string) + backend `MonitoringStatus` enum (06 §2.16) birebir — `POST_CANCEL_24H`, `POST_CANCEL_7D`, `POST_CANCEL_30D`, `STOPPED`. Wire-format string parse `TryParseMonitoringStatus` ile yapılır.
- [x] **02 §4.4 gecikmeli ödeme kuralları eksiksiz mi?**
  - "İşlem zaten iptal, platform adresi izlemeye devam eder" → `PostCancelMonitorRegistry.start` 30-gün boyunca polling ✓
  - "Gelen ödeme alıcıya otomatik iade" → `LATE_PAYMENT_REFUND` queue + T73 dispatch ✓
  - "Çoklu/parçalı ödeme — işlem tamamlandıktan sonra gelen ek transferler gecikmeli ödeme kuralıyla iade" → AmountValidationService.HandleMultiPaymentAsync (T72 — aktif aşama EXCESS_REFUND) + ValidateLatePaymentDetectedAsync (T75 — post-cancel LATE_PAYMENT_REFUND), tip ayrımı audit için
  - "Minimum iade eşiği (2× gas)" → ortak `IRefundDecisionService.ResolveBuyerRefundAsync` çağrısı ✓

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Sidecar Vitest | ✓ 130/130 PASS | `npx vitest run` — 9 dosya; T75 yeni `PostCancelMonitor.test.ts` 26 test (initial state 6 + transitions 4 + cadence 3 + emission 6 + stop/shutdown 3 + delivery 2 + defaults 2); regresyon yok (MonitorRegistry 13, TransferService 14, EnergyDelegationService 10, TronDelegationClient 10, TronTransferClient 7, TronGridClient 11, PaymentMonitorRules 24, HdWalletService 15) |
| Backend Release build | ✓ 0W/0E | `dotnet build -c Release` |
| Backend Unit/Integration (lokal SQLite + InMemory) | ✓ 910/910 PASS | Auth 57 + Steam 13 + Fraud 14 + Platform 102 + Transactions 386 + API 15 + Realtime 25 + Shared 205 + Notifications 93 |
| T75 yeni Starter tests | ⏳ lokalde 1/7 (SQL Server testcontainer Docker Desktop yokken skip), CI Linux runner'da PASS bekleniyor | `dotnet test --filter PostCancelMonitorStarterTests` |
| Backend dotnet format | ✓ Δ=0 (auto-fix sonrası) | `dotnet format` |
| Sidecar prettier (T75 dosyaları) | ✓ Δ=0 (auto-fix sonrası) | 7 T75 dosyası fix edildi |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Doğrulama chat'ine geçilecek |
| Bulgu sayısı | TBD |
| Düzeltme gerekli mi | TBD |

## Altyapı Değişiklikleri

- **Migration:** Yok — `PaymentAddress.MonitoringStatus` + `MonitoringExpiresAt` field'ları zaten F1'de eklenmişti (InitialCreate).
- **Config/env değişikliği:** Sidecar 7 yeni env (`POST_CANCEL_TICK_INTERVAL_MS`, `POST_CANCEL_CADENCE_24H_MS`/`7D_MS`/`30D_MS`, `POST_CANCEL_WINDOW_24H_MS`/`7D_MS`/`30D_MS`) — hepsi default'lu, mevcut deploy'lar etkilenmez
- **Docker değişikliği:** Yok
- **Yeni paket:** Backend `Skinora.Transactions.csproj`'a `MediatR 12.4.1` — projede zaten kullanılan paket sürümü
- **Sidecar IHostedService:** `PostCancelMonitorRecoveryHook` startup'ta DB → sidecar HTTP replay

## Commit & PR

- Branch: `task/T75-blockchain-post-cancel-monitor`
- Commit (HEAD): `c9abdb8` — `T75: BYPASS_LOG entry — integration test fixture fix push` (3253504 ana T75 implementation + ad68d9d test fixture fix + c9abdb8 BYPASS_LOG entry)
- PR: [#116](https://github.com/turkerurganci/Skinora/pull/116)
- CI: ✓ PASS — run [`25992156986`](https://github.com/turkerurganci/Skinora/actions/runs/25992156986) 10/10 jobs success (Lint + Build + Unit + Integration + Contract + Migration + Docker backend + Docker sidecar-blockchain + CI Gate + Detect)
- BYPASS_LOG: 1× `[ci-failure]` entry (BuyerIdentificationMethod test fixture fix push, önceki run 25991927813 — sadece integration test fail)

## Known Limitations / Follow-up

- **K1 (T96 devir):** Sidecar restart sonrası recovery hook bir kerelik startup'ta çalışır. Sürekli reconciliation (drift catch-up) için periodic job T96 admin yetenekleri kapsamında. MVP'de kabul edilir — restart yakın sürede backend ile birlikte yapılır.
- **K2 (T78 / T96 devir):** 30-gün dolduğunda STOPPED state'inde admin notification + audit log emit eden ayrı outbox event henüz yok. Şu an webhook handler PaymentAddress.MonitoringStatus = STOPPED set eder + log; admin notification T96 NotificationService consumer ile bağlanacak.
- **K3 (T-future):** Late payment'lar için 20-blok finality bekleme **yok** — sidecar incoming TRC-20 transfer'i hemen LATE_PAYMENT_REFUND akışına sokar. Bu MVP edge case için uygundur; production'da double-spend riskine karşı backend tarafında confirmation kontrolü eklenebilir.
- **K4 (T96 devir):** Sidecar runtime cadence override (SystemSetting → POST_CANCEL_CADENCE_*) henüz env-bound; admin tuning T96 admin SystemSettings handler eklediğinde sidecar webhook ile yenilenebilir veya restart-bound kalabilir.
- **K5 (T73 K6 havuzu):** Pre-existing sidecar prettier drift T73/T74 K6 ile aynı kapsamda — T75 ile yeni dosyaların prettier'ı temiz, ancak 36 pre-existing dosya drift devam ediyor (ayrı chore PR).
- **K6 (T-future):** Multi-event-per-tx (T71 K3 ile aynı) post-cancel akışında da varsayım — TronGrid v1 event_index expose etmez. Backend `BlockchainTransaction.TxHash` UNIQUE defense-in-depth yeterli.

## Notlar

### Working Tree Hygiene Check (task.md Adım -1)
Working tree temiz (`git status --short` boş çıktı). Branch tabanı `main` (T74 son commit `73d7ffc`).

### Main CI Startup Check (task.md Adım 0)
Son 3 main CI run 3/3 success:
- `25990227493` (T74 #115) — success
- `25990227492` (T74 #115) — success
- `25987227889` (T73 #114) — success

### Dış Varsayımlar (task.md Adım 4)
| Varsayım | Kanıt |
|---|---|
| `MonitoringStatus` enum 5 değer (POST_CANCEL_24H/7D/30D + STOPPED + ACTIVE) | [MonitoringStatus.cs:5-9](../../backend/src/Skinora.Shared/Enums/MonitoringStatus.cs#L5-L9) doğrulandı |
| `BlockchainTransactionType.LATE_PAYMENT_REFUND` mevcut | [BlockchainTransactionType.cs:12](../../backend/src/Skinora.Shared/Enums/BlockchainTransactionType.cs#L12) doğrulandı |
| `PaymentAddress.MonitoringStatus` + `MonitoringExpiresAt` field'ları | [PaymentAddress.cs:17-18](../../backend/src/Modules/Skinora.Transactions/Domain/Entities/PaymentAddress.cs#L17-L18) doğrulandı |
| Sidecar `RefundService` `LATE_PAYMENT_REFUND` tipini destekler | [RefundService.ts:51-53](../../sidecar-blockchain/src/transfer/RefundService.ts#L51-L53) doc kapsamı doğrulandı |
| `WebhookSignatureMiddleware` `/api/v1/webhooks/blockchain` prefix-based | [WebhookSignatureMiddleware.cs:37+180-190](../../backend/src/Skinora.API/Middleware/WebhookSignatureMiddleware.cs#L37) `StartsWithSegments` ile yeni endpoint'leri otomatik kapsar |
| MediatR 12.4.1 Skinora projesinde mevcut | Notifications/Realtime.csproj'da kullanılıyor, Transactions.csproj'a eklendi |

### Scope Kararları (proje sahibi onayı 2026-05-17)
1. **State transition:** A — Sidecar self-clocked + backend recovery hook (precision yüksek, az hareketli parça)
2. **Cancel-time stamping:** Evet — tüm cancel handler'lara ekle (T49 + T51 + T59 + DeadlineScanner), aksi takdirde fonksiyonel olarak yarım kalır
3. **BlockchainTransaction.Type:** BUYER_PAYMENT (incoming) + LATE_PAYMENT_REFUND (outgoing) — mevcut enum, migration yok

### Yapım Sırasında Tespit Edilenler
- `TransactionCancellationService` constructor `IPostCancelMonitorStarter` parametresi sonradan eklenince 6 test dosyasında constructor çağrıları kırıldı — `TimeoutTestFixtures.NoOpPostCancelMonitor()` helper'ı ile düzeltildi.
- `StubBlockchainSidecarClient` integration test stub'ı 2 yeni interface metoduna güncellendi (PostCancelStart/Stop queue + calls list).
- AmountValidationService'in `KnownStablecoinContracts` internal class'ı public yapıldı (Starter + RecoveryHook reuse için ResolveContractAddress helper).
- `PostCancelMonitorStartDispatcher` Skinora.Transactions modülünde olduğu için MediatR PackageReference modüle eklendi.

### BYPASS_LOG
Henüz CI fail yok. Push sonrası CI takip edilecek.
