# T72 — Blockchain Sidecar — tutar doğrulama ve edge case'ler

**Faz:** F4 | **Durum:** ✓ Tamamlandı (bağımsız validator PASS) | **Tarih:** 2026-05-16

---

## Yapılan İşler

**Backend (`Skinora.Transactions` + `Skinora.Notifications` + `Skinora.Platform` + `Skinora.Shared`):**

- `IAmountValidationService` + `AmountValidationService` (02 §4.4, 08 §3.4) — confirmed buyer payment'ı `PaymentAddress.ExpectedAmount`'a göre sınıflandırır:
  - **Doğru tutar** (`received == expected`, ITEM_ESCROWED): `TransactionStateMachine.Fire(ConfirmPayment)` → PAYMENT_RECEIVED + `PaymentReceivedEvent` outbox publish (T44 K2 wiring closed).
  - **Eksik tutar** (`received < expected`): state machine değişmez (timeout devam, 02 §4.4 "alıcı doğru tutarı baştan gönderir"); `INCORRECT_AMOUNT_REFUND` PENDING row + `BuyerPaymentInsufficientEvent`.
  - **Fazla tutar** (`received > expected`): state machine fire + `EXCESS_REFUND` PENDING (delta için) + `BuyerPaymentExcessRefundedEvent(IsMultiPayment=false)`.
  - **Multi-payment** (Transaction state ≠ ITEM_ESCROWED, tipik PAYMENT_RECEIVED sonrası gelen ekstra): `EXCESS_REFUND` PENDING (tüm tutar için) + `BuyerPaymentExcessRefundedEvent(IsMultiPayment=true)`.
  - **Threshold koruması** (09 §14.4): tüm refund kararları `IRefundDecisionService` (T53) üzerinden geçer; `net = received − gasFee < gasFee × min_refund_threshold_ratio` ise refund row yazılmaz, sadece `RefundBlockedAdminAlertEvent` yayınlanır (Audit + outbox `RefundBlockedAlertService` üzerinden).
  - **State machine reddi** (emergency hold / cancelled): `AdvanceStateMachineAsync` `CanFire` kontrolü + `DomainException` catch ile log + `StateMachineRejected` outcome; refund row yazılmaz (T59 hold release sonrası manuel müdahale, K1 forward-devir).
  - **Source address parse**: refund hedefi `BlockchainTransaction.FromAddress` (T71 zaten DETECTED kaydında parse ediyor; T72 ek parsing yapmıyor — 02 §4.6 source-address pratiği zaten yerinde).
- `ValidateWrongTokenIncomingAsync` — `WRONG_TOKEN_INCOMING` DETECTED kaydı için:
  - Refund-decision threshold: `WRONG_TOKEN_REFUND` PENDING (Token = expected, ActualTokenAddress = wrong contract, 06 §3.8 token semantiği) + `WrongTokenRefundRequestedEvent`.
  - Sub-threshold: yalnızca `RefundBlockedAdminAlertEvent` (T71 K1 dispatch handoff'u kapatıldı).
- `BlockchainWebhookHandler` (T71): `HandlePaymentConfirmedAsync` + `HandleWrongTokenIncomingAsync` artık T72 servisini inline çağırıyor — tek `SaveChangesAsync` içinde CONFIRMED flip + state-machine + refund-intent + outbox row atomik commit (mevcut webhook handler kontratı korunur).
- `GasFeeSettings` record + `GasFeeSettingsProvider`: yeni `RefundGasFeeEstimateUsdt` field; `blockchain.refund_gas_fee_estimate_usdt` SystemSetting (default 2.0 USDT, kategori `Monitoring`) okunur. Validator generic positive-number rule (> 0) zaten yakalıyor — ek validator branch gerekmedi. `DefaultRefundGasFeeEstimateUsdt = 2.0m` code fallback malformed/unconfigured row için.
- `SystemSettingSeed`: index 50 yeni entry; `SystemSettingsCatalog`: `blockchain.refund_gas_fee_estimate_usdt` → `blockchain_health` kategori + USDT birimi.
- `NotificationType` enum: 3 yeni değer (`INSUFFICIENT_PAYMENT`, `OVERPAYMENT_REFUNDED`, `WRONG_TOKEN_REFUND`).
- `NotificationTemplates.resx` (neutral) + `.tr.resx`: 3×2 = 6 yeni başlık/gövde (es/zh partial-coverage yapısına dokunulmadı — T97 i18n devri).
- 3 `IDomainEvent` (`BuyerPaymentInsufficientEvent`, `BuyerPaymentExcessRefundedEvent`, `WrongTokenRefundRequestedEvent`) + 3 `NotificationConsumerBase<TEvent>` türevi (MediatR otomatik discovery `OutboxModule` assembly tarama listesinden geçer).
- Migration `T72_AddRefundGasFeeEstimate` — tek seed INSERT (`SystemSettings` Id `0aa51010-...-00000032`); Down idempotent DELETE.

## Etkilenen Modüller / Dosyalar

**Yeni:**
- [`backend/src/Modules/Skinora.Transactions/Application/Webhooks/IAmountValidationService.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Webhooks/IAmountValidationService.cs)
- [`backend/src/Modules/Skinora.Transactions/Application/Webhooks/AmountValidationService.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Webhooks/AmountValidationService.cs)
- [`backend/src/Skinora.Shared/Events/BuyerPaymentInsufficientEvent.cs`](../../backend/src/Skinora.Shared/Events/BuyerPaymentInsufficientEvent.cs)
- [`backend/src/Skinora.Shared/Events/BuyerPaymentExcessRefundedEvent.cs`](../../backend/src/Skinora.Shared/Events/BuyerPaymentExcessRefundedEvent.cs)
- [`backend/src/Skinora.Shared/Events/WrongTokenRefundRequestedEvent.cs`](../../backend/src/Skinora.Shared/Events/WrongTokenRefundRequestedEvent.cs)
- [`backend/src/Modules/Skinora.Notifications/Application/EventHandlers/BuyerPaymentInsufficientNotificationConsumer.cs`](../../backend/src/Modules/Skinora.Notifications/Application/EventHandlers/BuyerPaymentInsufficientNotificationConsumer.cs)
- [`backend/src/Modules/Skinora.Notifications/Application/EventHandlers/BuyerPaymentExcessRefundedNotificationConsumer.cs`](../../backend/src/Modules/Skinora.Notifications/Application/EventHandlers/BuyerPaymentExcessRefundedNotificationConsumer.cs)
- [`backend/src/Modules/Skinora.Notifications/Application/EventHandlers/WrongTokenRefundRequestedNotificationConsumer.cs`](../../backend/src/Modules/Skinora.Notifications/Application/EventHandlers/WrongTokenRefundRequestedNotificationConsumer.cs)
- [`backend/src/Skinora.Shared/Persistence/Migrations/20260516192049_T72_AddRefundGasFeeEstimate.cs`](../../backend/src/Skinora.Shared/Persistence/Migrations/20260516192049_T72_AddRefundGasFeeEstimate.cs) (+ Designer)
- [`backend/tests/Skinora.Transactions.Tests/Unit/Webhooks/AmountValidationServiceTests.cs`](../../backend/tests/Skinora.Transactions.Tests/Unit/Webhooks/AmountValidationServiceTests.cs)

**Güncellenen:**
- [`backend/src/Modules/Skinora.Transactions/Application/Webhooks/BlockchainWebhookHandler.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Webhooks/BlockchainWebhookHandler.cs) (T72 wiring)
- [`backend/src/Modules/Skinora.Transactions/Application/GasFee/IGasFeeSettingsProvider.cs`](../../backend/src/Modules/Skinora.Transactions/Application/GasFee/IGasFeeSettingsProvider.cs)
- [`backend/src/Modules/Skinora.Transactions/Application/GasFee/GasFeeSettingsProvider.cs`](../../backend/src/Modules/Skinora.Transactions/Application/GasFee/GasFeeSettingsProvider.cs)
- [`backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsCatalog.cs`](../../backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsCatalog.cs)
- [`backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs`](../../backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs)
- [`backend/src/Skinora.API/Configuration/TransactionsModule.cs`](../../backend/src/Skinora.API/Configuration/TransactionsModule.cs) (DI: `IAmountValidationService`)
- [`backend/src/Skinora.Shared/Enums/NotificationType.cs`](../../backend/src/Skinora.Shared/Enums/NotificationType.cs) (22 → 25 değer)
- [`backend/src/Modules/Skinora.Notifications/Resources/NotificationTemplates.resx`](../../backend/src/Modules/Skinora.Notifications/Resources/NotificationTemplates.resx) (3 yeni başlık/gövde çifti)
- [`backend/src/Modules/Skinora.Notifications/Resources/NotificationTemplates.tr.resx`](../../backend/src/Modules/Skinora.Notifications/Resources/NotificationTemplates.tr.resx) (3 yeni başlık/gövde çifti)
- [`backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs`](../../backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs) (seed +1)
- [`backend/tests/Skinora.Shared.Tests/Unit/EnumTests.cs`](../../backend/tests/Skinora.Shared.Tests/Unit/EnumTests.cs) (22 → 25)
- [`backend/tests/Skinora.Platform.Tests/Integration/SeedDataTests.cs`](../../backend/tests/Skinora.Platform.Tests/Integration/SeedDataTests.cs) (49 → 50 / 28 → 29 configured)
- [`backend/tests/Skinora.Transactions.Tests/Unit/GasFee/RefundDecisionServiceTests.cs`](../../backend/tests/Skinora.Transactions.Tests/Unit/GasFee/RefundDecisionServiceTests.cs) (3-arg `GasFeeSettings` ctor)
- [`backend/tests/Skinora.API.Tests/Integration/BlockchainWebhookEndpointTests.cs`](../../backend/tests/Skinora.API.Tests/Integration/BlockchainWebhookEndpointTests.cs) (6 yeni T72 integration testi + factory helpers + Reset SYSTEM-preserve)
- [`backend/tests/Skinora.Transactions.Tests/Skinora.Transactions.Tests.csproj`](../../backend/tests/Skinora.Transactions.Tests/Skinora.Transactions.Tests.csproj) (test-only `Microsoft.Data.Sqlite` + `Microsoft.EntityFrameworkCore.Sqlite` 9.0.3)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Doğru tutar → PAYMENT_RECEIVED | ✓ | `AmountValidationService` exact branch + `TransactionStateMachine.Fire(ConfirmPayment)` + `PaymentReceivedEvent` publish. Test: `ConfirmedPayment_ExactAmount_FiresConfirmPayment_PublishesPaymentReceivedEvent` (unit), `PaymentConfirmed_ExactAmount_AdvancesStateAndPublishesPaymentReceivedEvent` (integration). |
| 2 | Eksik tutar → iade + bildirim | ✓ | `HandleUnderpaymentAsync` → `INCORRECT_AMOUNT_REFUND` PENDING + `BuyerPaymentInsufficientEvent` + `BuyerPaymentInsufficientNotificationConsumer` (INSUFFICIENT_PAYMENT template'i). State machine değişmez — timeout devam (02 §4.4). Test: `ConfirmedPayment_Underpayment_AboveThreshold_QueuesIncorrectAmountRefund` (unit), `PaymentConfirmed_Underpayment_QueuesIncorrectAmountRefundAndBuyerEvent` (integration). |
| 3 | Fazla tutar → doğru tutarı kabul, fazlayı iade + bildirim | ✓ | `HandleOverpaymentAsync` → `Fire(ConfirmPayment)` + `EXCESS_REFUND` PENDING (delta) + `BuyerPaymentExcessRefundedEvent(IsMultiPayment=false)` + `OVERPAYMENT_REFUNDED` notification. Test: `ConfirmedPayment_Overpayment_AdvancesStateAndQueuesExcessRefund` (unit), `PaymentConfirmed_Overpayment_AdvancesStateAndQueuesExcessRefund` (integration). |
| 4 | Yanlış token (desteklenen TRC-20) → iade + bildirim | ✓ | `ValidateWrongTokenIncomingAsync` → `WRONG_TOKEN_REFUND` PENDING (Token=expected per 06 §3.8 token semantiği, ActualTokenAddress=wrong contract) + `WrongTokenRefundRequestedEvent` + `WRONG_TOKEN_REFUND` notification. T71 K1 dispatch handoff'u kapatıldı. Test: `WrongTokenIncoming_AboveThreshold_QueuesWrongTokenRefund` (unit), `WrongTokenIncoming_AboveThreshold_QueuesWrongTokenRefundAndBuyerEvent` (integration). |
| 5 | Desteklenmeyen token → admin review | ✓ | T71 zaten `SPAM_TOKEN_INCOMING` terminal CONFIRMED yazıyor + admin alert log; T72 spam-token akışını değiştirmedi (refund kuralı 08 §3.4 spam politikası — TRX/Energy DoS koruması, otomatik iade yok). Mevcut `SpamTokenIncoming_PersistsRowAtTerminalConfirmed` regresyon testi geçer. Admin dashboard görünürlüğü T96 admin UI scope'unda. |
| 6 | Çoklu/parçalı ödeme → birleştirmez, ilk doğru kabul, sonraki iade | ✓ | `HandleMultiPaymentAsync` (Transaction.Status ≠ ITEM_ESCROWED branch'i) → tüm `received` tutarı `EXCESS_REFUND` PENDING + `BuyerPaymentExcessRefundedEvent(IsMultiPayment=true)`. "İlk doğru kabul" kısmı T44 state machine zaten garanti ediyor (PAYMENT_RECEIVED'a geçtikten sonra ikinci `ConfirmPayment` fire edilemez). Test: `ConfirmedPayment_MultiPayment_PostEscrowState_RefundsEntireAmount` (unit). |
| 7 | Minimum iade eşiği: tutar < 2× gas fee → iade yapılmaz, admin alert | ✓ | T53 `RefundDecisionService.ResolveBuyerRefundAsync`/`ResolveOverpaymentRefundAsync` `Block` outcome döndürdüğünde `IRefundBlockedAlertService.RaiseAsync` çağrılır — refund row yazılmaz, sadece `RefundBlockedAdminAlertEvent` + AuditLog `REFUND_BLOCKED`. Default `min_refund_threshold_ratio=2.0` + `blockchain.refund_gas_fee_estimate_usdt=2.0` → effective threshold = 4 USDT. Test: `ConfirmedPayment_Underpayment_BelowThreshold_RaisesAdminAlertAndSkipsRefund` + `WrongTokenIncoming_BelowThreshold_RaisesAdminAlertOnly` (unit) + 2 paralel integration testi. |
| 8 | İade kaynak adrese gönderilir (source address parse) | ✓ | T71 `BlockchainTransaction.FromAddress` zaten parse edilmiş (sidecar `Trc20Record.from` → backend persisted); T72'nin tüm refund row'ları `ToAddress = confirmedPayment.FromAddress` set ediyor. Test: tüm refund integration testleri `Assert.Equal(detected.data.fromAddress, refund.ToAddress)` kontrolü içerir; ayrıca unit testler aynı asserti yapar. |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| `AmountValidationServiceTests` (yeni unit) | ✓ 9/9 PASS | `dotnet test --filter "FullyQualifiedName~AmountValidationServiceTests"` — SQLite in-memory `AppDbContext` + capturing stubs (IGasFeeSettingsProvider, IRefundBlockedAlertService, IOutboxService) + FakeTimeProvider. Branches: exact / under-above / under-below / over-above / over-below / multi-payment / wrong-token-above / wrong-token-below / on-hold. |
| `BlockchainWebhookEndpointTests` (mevcut + T72 ekleri) | ✓ 15/15 PASS | `dotnet test --filter "FullyQualifiedName~BlockchainWebhookEndpointTests"` — 9 mevcut + 6 yeni T72 integration testi (correct-amount / under / under-below-threshold / over / wrong-above / wrong-below). |
| `EnumTests` (regresyon — NotificationType 22 → 25) | ✓ 189/189 PASS (Shared.Tests) | InlineData'lara 3 yeni değer eklendi. |
| `RefundDecisionServiceTests` (regresyon — 3-arg `GasFeeSettings`) | ✓ included in 353 Transactions unit | 4 InlineData ctor güncellemesi. |
| Backend solution Unit testleri (full sweep) | ✓ **833/833 PASS** | `dotnet test backend/Skinora.sln -c Release --filter "FullyQualifiedName!~Integration & FullyQualifiedName!~InitialMigration"` — Shared 189 + Users 16 + Auth 57 + Platform 102 + Fraud 14 + Transactions 353 + Notifications 49 + Steam 13 + Realtime 25 + API 15. |
| Build Release | ✓ 0W/0E | `dotnet build backend/Skinora.sln -c Release`. |
| `dotnet format --verify-no-changes` | ✓ PASS | Lokal otomatik formatlama tek run'da kapandı. |
| Lokal Testcontainers integration testleri | ⚠ env-skip | Lokalde Docker Desktop kapalı — F3 gate ortam sınırı, CI Linux runner'da Testcontainers `services:mssql` ile çalışır. |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS (bağımsız validator, 2026-05-16) |
| Kabul kriterleri | **8/8 ✓** (tümü tam) |
| Doğrulama kontrol listesi | 1/1 ✓ (02 §4.4 edge case kapsama; Timeout sonrası gecikmeli ödeme satırı T75 forward devir K2) |
| Bulgu sayısı | **0 S-bulgu** (S1/S2/S3 yok) |
| Düzeltme gerekli mi | Hayır |

**Kanıt özeti:**

- **Working tree hygiene (Adım -1):** temiz.
- **Main CI startup (Adım 0):** son 3 main run 3/3 SUCCESS — [`25967060890`](https://github.com/turkerurganci/Skinora/actions/runs/25967060890), [`25967060883`](https://github.com/turkerurganci/Skinora/actions/runs/25967060883), [`25962252835`](https://github.com/turkerurganci/Skinora/actions/runs/25962252835).
- **Repo memory drift (Adım 0b):** `.claude/memory/MEMORY.md`'de T72 satırları mevcut (T72 + T72 dış varsayım notu + Next).
- **Build:** `dotnet build backend/Skinora.sln -c Release` — 0 Warning / 0 Error.
- **Unit tests:** `dotnet test backend/Skinora.sln -c Release --filter "FullyQualifiedName!~Integration & FullyQualifiedName!~InitialMigration"` — **833/833 PASS** (Shared 189 + Users 16 + Auth 57 + Platform 102 + Fraud 14 + Transactions 353 + Notifications 49 + Steam 13 + Realtime 25 + API 15).
- **AmountValidationServiceTests:** 9/9 PASS (SQLite in-memory + capturing outbox + FakeTimeProvider; 9 branch).
- **Lint:** `dotnet format --verify-no-changes` Δ=0.
- **Task branch CI:** [`25971513588`](https://github.com/turkerurganci/Skinora/actions/runs/25971513588) — **10/10 job ✓** (Detect/Lint/Build/Unit/Integration/Contract/Migration/Docker/CI Gate). Lokal Testcontainers Docker Desktop yokluğunda skip; CI Linux runner 4. Integration ✓.
- **Güvenlik:** secret sızıntısı yok, auth etkisi yok (webhook HMAC pipeline değişmedi), input validation kontrol guard'ları yerinde (`InvalidOperationException` for wrong entity type), prod dependency eklenmedi (test-only `Microsoft.Data.Sqlite` + `Microsoft.EntityFrameworkCore.Sqlite` 9.0.3, csproj `IsPackable=false`).
- **Doküman uyumu:** 02 §4.4 tablosu 6/6 satır kapsama (4 implement + 1 unchanged spam + 1 forward T75); 08 §3.4 tutar tablosu 5/5 + minimum eşik; 06 §3.8 token semantiği + outbound CHECK constraint'leri (`PaymentAddressId NULL`, `WRONG_TOKEN_REFUND` `ActualTokenAddress NOT NULL`) eşleşiyor. SystemSettingsCatalog + SystemSettingSeed 50/50 satır.
- **Yapım raporu uyumu:** Validator bağımsız verdict'i (8/8 ✓) yapım raporunun verdict tablosu ile birebir; hiçbir uyuşmazlık tespit edilmedi.

## Altyapı Değişiklikleri

- **Migration:** **Var** — `20260516192049_T72_AddRefundGasFeeEstimate` (tek seed INSERT, SystemSettings Id `0aa51010-...-32`). Down idempotent DELETE. 9 → 10 migration zinciri.
- **Config/env değişikliği:**
  - **Backend** — yeni SystemSetting `blockchain.refund_gas_fee_estimate_usdt` (default `2.0`, kategori `Monitoring`). Admin tarafından güncellenebilir; T74 sonrası runtime Energy/Bandwidth bedeli ile değiştirilir.
- **Docker değişikliği:** **Yok.** Mevcut blockchain-sidecar env'leri (T71'de set'lenmiş) T72 backend mantığını etkilemiyor.

## Commit & PR

- Branch: `task/T72-blockchain-amount-validation`
- Yapım commit'i: `370faee` (T72: Blockchain Sidecar — tutar doğrulama ve edge case'ler)
- PR: [#113](https://github.com/turkerurganci/Skinora/pull/113) — squash merge `main`
- Task branch CI: [`25971513588`](https://github.com/turkerurganci/Skinora/actions/runs/25971513588) ✓ 10/10 (Lint/Build/Unit/Integration/Contract/Migration/Docker/CI Gate)

## Known Limitations / Follow-up

- **K1 — T59 emergency hold ↔ T72 deferral**: Bir transaction emergency hold altındayken (`Transaction.IsOnHold=true`) gelen `PaymentConfirmed` için `AmountValidationService.AdvanceStateMachineAsync` `EnforceNotOnHold` rejection'ı catch eder ve `StateMachineRejected` outcome döndürür — refund row yazılmaz, sadece `BlockchainTransaction` CONFIRMED kaydı tutulur. Admin hold'u kaldırdığında sınıflandırmayı **manuel** olarak yeniden tetiklemek gerekir; otomatik replay yok. T96 admin-trigger replay endpoint forward-devir.
- **K2 — Cancelled transaction + payment**: Transaction cancelled-* terminal state'lerden birinde iken gelen confirmed payment `HandleMultiPaymentAsync` branch'ine düşer (state ≠ ITEM_ESCROWED). T72 bunu `EXCESS_REFUND` tüm tutarı olarak işler; ancak gecikmeli ödeme post-cancel monitoring'i (`MonitoringStatus=POST_CANCEL_*`) **T75** scope. K2 = T72 sıcak-yol koruması, T75 cold-yol soğutucu (gecikmeli ödeme polling cadence + `LATE_PAYMENT_REFUND` tipi).
- **K3 — Wrong-token finality yok**: T71 sidecar `MonitorRegistry.pollPhase2` `wrong_token` ve `spam_token` event'lerini DETECTED olarak yayınlar; finality probe (`pendingFinality` map'i) yalnızca beklenen token için tutuluyor. T72 wrong-token refund'u DETECTED üzerinde tetikler — TronGrid `only_confirmed=true` filtresi confirmed-yalnız sonuç dönderse de reorg riski sıfır değil. MVP'de kabul; full-finality wrong-token coverage T-future events API entegrasyonu (T71 K3 ile aynı havuz).
- **K4 — Gas fee estimate vs. runtime**: `blockchain.refund_gas_fee_estimate_usdt = 2.0` MVP sabitleri Tron mainnet ortalama TRC-20 transfer Energy/Bandwidth bedelini kabaca yansıtır; T74 (energy delegation) sonrasında her refund attempt'i için runtime Energy alındıktan sonra dinamik fiyatlandırma `RefundGasFeeEstimateUsdt` yerine geçecek. Stale estimate altında threshold yanlış hesaplanabilir — admin alert tarafında "estimated" damgası rapor edilir (forward-devir).
- **K5 — Wrong-token gas fee value compatibility**: USDC refund threshold'unun USDT cinsinden tahmin gas fee ile karşılaştırılması 1:1 stablecoin denk varsayımına dayanır; %1+ depeg senaryosunda alt-eşik sınır hesabı kayar. MVP'de kabul (stablecoin'lerin tarihsel %0.5 sapma bandı). T-future per-token gas fee mapping.
- **K6 — Locale coverage**: 3 yeni NotificationType (`INSUFFICIENT_PAYMENT`, `OVERPAYMENT_REFUNDED`, `WRONG_TOKEN_REFUND`) için `tr` + neutral (`en`) template'leri yazıldı; `es` ve `zh` için entry yok (mevcut partial-coverage pattern'i — `ResxNotificationTemplateResolver_LocaleMissingForKey_FallsBackToEnglish`). Full locale parity T97 i18n devri.
- **K7 — Spam-token admin notification**: T72 spam-token akışına dokunmadı; T71 sadece terminal CONFIRMED kaydı + log yazıyor. Dashboard'da görünürlük admin admin UI scope'unda T96 forward-devir.
- **K8 — `PAYMENT_RECEIVED` notification consumer yok**: `PaymentReceivedEvent` T72'de ilk kez yayınlanıyor; mevcut consumer'lardan yalnızca `PaymentReceivedRealtimeConsumer` (T61 SignalR) tüketiyor. PAYMENT_RECEIVED in-app notification rendering'i `NotificationTemplates.resx`'te hazır ama consumer wiring T-future (alıcı paneli "ödeme alındı" bildirimi). Hot-path zaten realtime push veriyor, in-app inbox eksiği UX nice-to-have.
- **K9 — `BuyerPaymentInsufficient` rate-limit**: Aynı buyer aynı transaction'a tekrar tekrar eksik tutar gönderirse her seferinde notification + refund queue spawn olur. Dedup `ProcessedEventStore` consumer-side idempotency üzerinden olmalı ama eventid'ler unique (her tx farklı). Spam buyer'a karşı koruma `T54 fraud flag` veya `T56 multi-account` policy'sinin alanı, T72 scope dışı.

## Notlar

- **Working tree (Adım -1):** Temiz.
- **Main CI startup (Adım 0):** Son 3 main run 3/3 SUCCESS (25967060890, 25967060883, 25962252835). ✓
- **Dış Varsayımlar (Adım 4):**
  - **02 §4.4 ödeme edge case tablosu** — okundu, 6 senaryonun her birine kabul kriteri eşlemesi yapıldı.
  - **08 §3.4 tutar doğrulama tablosu** — strict equality (06 §8.3 "Payment validation tolerance yok"); `IsPaymentExact` ile decimal `==` kullanılır.
  - **T53 RefundDecisionService altyapısı** — `ResolveBuyerRefundAsync` (under + multi-payment) + `ResolveOverpaymentRefundAsync` (over) + threshold math (`min_refund_threshold_ratio` default 2.0) zaten mevcut. **Doğrulandı:** `backend/src/Modules/Skinora.Transactions/Application/GasFee/IRefundDecisionService.cs`.
  - **`BlockchainTransactionType` enum** — `BUYER_REFUND`, `EXCESS_REFUND`, `WRONG_TOKEN_REFUND`, `INCORRECT_AMOUNT_REFUND`, `LATE_PAYMENT_REFUND` 06 §3.8'de tanımlı. **Doğrulandı:** `backend/src/Skinora.Shared/Enums/BlockchainTransactionType.cs`.
  - **06 §3.8 CHECK constraint'ler** — outbound transfers `PaymentAddressId NULL`, `WRONG_TOKEN_REFUND` `ActualTokenAddress NOT NULL`. T72 helper `QueueRefundIntent` bu kurala uyar.
  - **T44 state machine `ConfirmPayment` trigger** — `ITEM_ESCROWED → PAYMENT_RECEIVED`, no guard. **Doğrulandı:** `TransactionStateMachine.cs:229`. T72 ilk caller (`AdvanceStateMachineAsync`).
- **Scope onayı (2026-05-16):** Proje sahibi onayı `AskUserQuestion` ile alındı: backend-only validation + refund intent PENDING row → T73 dispatch + yeni `blockchain.refund_gas_fee_estimate_usdt` SystemSetting + underpayment'ta state değişmez. İkinci onay: 3 yeni NotificationType değeri (mevcut PAYMENT_INCORRECT/PAYMENT_REFUNDED reuse seçeneği reddedildi).
- **Squash-merge bundled-PR guard:** T72 commit'leri yalnızca `T72:` prefix taşıyacak (commit-msg hook real-time enforce eder).
