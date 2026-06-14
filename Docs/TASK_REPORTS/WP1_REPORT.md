# WP1 — Escrow Tamamlama: Satıcı Payout + COMPLETED

**Faz:** F6 öncesi (PRE_F6_PLAN) | **Durum:** ⏳ Devam ediyor (doğrulama bekliyor) | **Tarih:** 2026-06-14

---

## Yapılan İşler

Happy-path'in `ITEM_DELIVERED`'da kalan çıkmaz sokağı kapatıldı (03 §2.4, 02 §4.7, PRE_F6_PLAN WP1). Teslim → satıcı payout → `COMPLETED` zinciri uçtan uca bağlandı:

1. **Producer (yeni)** — `SellerPayoutQueueJob`: per-minute Hangfire job, `ITEM_DELIVERED` (ve `!IsOnHold`, `!HasActiveDispute`, henüz SELLER_PAYOUT satırı olmayan) transaction'ları tarar; gas-fee koruma net tutarını hesaplar (`ResolveSellerPayoutAsync` → `CalculateSellerPayout`); `PENDING SELLER_PAYOUT` `BlockchainTransaction` satırı oluşturur. `OutgoingTransferDispatchJob` (mevcut) bu satırı yayınlar — **değiştirilmedi**.
2. **Completion event (yeni)** — `OutgoingTransferConfirmationJob` SELLER_PAYOUT satırını `CONFIRMED` (20-blok finality) yapınca `PayoutCompletedEvent` outbox'a yayınlar (aynı SaveChanges). `IOutboxService` ctor'a eklendi. Refund satırları event üretmez.
3. **Completion consumer (yeni)** — `PayoutCompletedConsumer` (MediatR `INotificationHandler`), `Fire(Complete)` → `COMPLETED` (`CompletedAt` OnEntry'de stamp'lenir). Domain-idempotent (`Status==ITEM_DELIVERED` guard), hold-guard'lı, explicit DI kaydı.
4. **Gas estimate ayarı (yeni)** — `blockchain.payout_gas_fee_estimate_usdt` (default **0.50** USDT, 04 §7.3 örneğiyle birebir). `GasFeeSettings`'e + `SystemSettingSeed`'e (Id 59) + `SystemSettingsCatalog`'a eklendi. Migration yok (seed default; CK constraint SELLER_PAYOUT'u zaten kapsıyor). Owner kararı.
5. **Payout breakdown DTO (07 §7.5)** — `TransactionDetailService` `SellerPayout` artık COMPLETED + satıcı görünümünde dolduruluyor (önceden `null`). Split, kayıttan tam türetiliyor: producer kullanılan gas estimate'i `BlockchainTransaction.GasFee`'ye snapshot'lar → `FinancialCalculator.ReconstructSellerPayoutSplit` saf aritmetikle `gasFeeFromSeller = price − net`, `gasFeeFromCommission = total − fromSeller` (drift yok). Admin DTO (`AdminTransactionQueryService.BuildPayoutDetail`) da aynı paylaşılan helper'la 0-yer-tutucudan gerçek split'e geçirildi.

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `Skinora.Shared/Events/PayoutCompletedEvent.cs`
- `Skinora.Transactions/Application/Transfers/SellerPayoutQueueJob.cs`
- `Skinora.Transactions/Application/Transfers/PayoutCompletedConsumer.cs`
- Test: `SellerPayoutQueueJobTests.cs`, `PayoutCompletedConsumerTests.cs`

**Değişen (production):**
- `Skinora.Transactions/Application/GasFee/{IGasFeeSettingsProvider,GasFeeSettingsProvider}.cs` — `PayoutGasFeeEstimateUsdt`
- `Skinora.Transactions/Application/Transfers/OutgoingTransferConfirmationJob.cs` — IOutboxService + event emit
- `Skinora.Transactions/Application/Transfers/OutgoingTransferJobsRegistrar.cs` — yeni job kaydı
- `Skinora.Transactions/Application/Lifecycle/TransactionDetailService.cs` — SellerPayout DTO
- `Skinora.Transactions/Domain/Calculations/FinancialCalculator.cs` — `ReconstructSellerPayoutSplit` + `SellerPayoutSplit`
- `Skinora.Platform/.../SystemSettingSeed.cs` (Id 59) + `SystemSettingsCatalog.cs` (catalog entry)
- `Skinora.API/Configuration/TransactionsModule.cs` — job + consumer DI
- `Skinora.API/Services/AdminTransactionQueryService.cs` — admin split (paylaşılan helper)

**Değişen (test):** `RefundDecisionServiceTests`, `AmountValidationServiceTests`, `OutgoingTransferConfirmationJobTests`, `FinancialCalculatorTests`, `GasFeeSettingsProviderTests`, `TransactionDetailServiceTests`, `SeedDataTests` (58→59 + configured array).

## Kabul Kriterleri Kontrolü

| # | Kriter (PRE_F6_PLAN WP1 + spec) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Teslim sonrası `PENDING SELLER_PAYOUT` satırı net tutarla oluşturulur | ✓ | `SellerPayoutQueueJob` + `GasAboveThreshold_QueuesPendingPayout...` (99.70), `GasBelowThreshold_PaysFullPrice` (100.00) |
| 2 | Gas-split aktif (`CalculateSellerPayout` wired, 02 §4.7 / 04 §7.3) | ✓ | producer `ResolveSellerPayoutAsync` çağırır; `FinancialCalculatorTests` + split testleri |
| 3 | SELLER_PAYOUT CONFIRMED → `PayoutCompletedEvent` emit | ✓ | `SellerPayoutConfirmed_PublishesPayoutCompletedEvent`; refund/failed emit etmez |
| 4 | Consumer `Complete`→`COMPLETED`, `CompletedAt` stamp | ✓ | `DeliveredTransaction_FiresComplete_AndStampsCompletedAt` |
| 5 | Para-güvenliği: held / disputed payout almaz (03 §2.4) | ✓ | `HeldTransaction_IsSkipped`, `DisputedTransaction_IsSkipped`, consumer `HeldTransaction_IsNotCompleted` |
| 6 | İdempotent (çift-pay yok, replay no-op) | ✓ | `ExistingPayoutRow_IsNotDuplicated`, `AlreadyCompleted_IsNoOp` |
| 7 | Ödeme başarısızsa COMPLETED'a geçmez (03 §2.4 adım 4) | ✓ | event yalnız CONFIRMED'da; FAILED'da emit yok (`SellerPayoutFailed_DoesNotPublish...`) |
| 8 | COMPLETED satıcı görünümü payout breakdown (07 §7.5) | ✓ | `Completed_SellerView_Surfaces_PayoutBreakdown`; buyer view `null` |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (Transactions) | ✓ 76/76 | `dotnet test Skinora.Transactions.Tests --filter Category=Unit` — yeni: producer 7, consumer 5, confirmation emit 3, calculator split 3 |
| Unit (Platform catalog) | ✓ 7/7 | `SystemSettingsCatalogTests` — catalog↔seed kapsama korunuyor |
| Build | ✓ | `dotnet build Skinora.sln` — 0 warning, 0 error |
| Integration | ⏳ CI | SeedData (59 satır), GasFeeSettingsProvider (payout estimate 3 test), TransactionDetailService (payout breakdown 2 test) — Docker lokal yok, CI'da koşar |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validator bekliyor (ayrı chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri
- **Migration:** Yok. `SELLER_PAYOUT` zaten `CK_BlockchainTransactions_Type_Outbound` kapsamında; yeni ayar seed-default (mandatory değil → 21-mandatory startup gate etkilenmez).
- **Config/env:** Yeni SystemSetting `blockchain.payout_gas_fee_estimate_usdt` (default 0.50, admin-tunable). Yeni recurring job `seller-payout-queue` (cron `* * * * *`).
- **Docker:** Yok.

## Mini Güvenlik Kontrolü
- **Secret sızıntısı:** Yok (yeni secret/connection string yok).
- **Auth/authorization:** Yeni endpoint yok (background job + consumer). DTO `SellerPayout` yalnız satıcı görünümünde döner (07 §7.5).
- **Input validation:** Negative-payout guard (≤0 → satır oluşturmaz, error log); gas estimate read-side `>0` fallback; `ToAddress` boşsa skip.
- **Yeni dış bağımlılık:** Yok (0 yeni NuGet).
- **Para-güvenliği:** held/disputed gate; idempotent (çift-pay yok); SaveChanges atomik; COMPLETED yalnız on-chain finality (20-blok) sonrası.

## Tasarım Kararları (owner-onaylı)
- **Gas estimate kaynağı:** Yeni `blockchain.payout_gas_fee_estimate_usdt` (0.50) — refund estimate'ten (2.0) ayrı, çünkü 02 §4.7 split'i satıcı-gönderim gas'ına göre ölçer (refund estimate ~1.8 over-deduct ederdi). Owner kararı (AskUserQuestion).
- **Producer mekanizması:** Polling job (`TradeOfferDispatchJob`/`OutgoingTransferDispatchJob` deseni) — kaçan webhook'a dayanıklı, idempotent, modül-doğru. Owner kararı.
- **Completion mekanizması:** Event-driven (plan WP1'de yazıldığı gibi) — confirmation job emit → consumer fire.

## Known Limitations / Follow-up
- **Held-at-confirm edge:** Payout broadcast ile CONFIRMED arasında (~1dk) tx EMERGENCY_HOLD'a alınırsa, consumer Complete'i fire edemez (state machine held'de tüm trigger'ları reddeder) → tx ITEM_DELIVERED+held kalır, payout zincirde gitmiştir. Consumer error-log + return (sonsuz retry yok). Hold-release sonrası yeniden-tamamlama **WP5/WP7** (hold-release akışı) devir; nadir admin-aksiyonu edge'i.
- **gasFee tahmini:** MVP estimate (0.50 USDT); gerçek runtime Energy/Bandwidth ölçümü **T74** devir (`refund_gas_fee_estimate_usdt` ile aynı desen).
- **COMPLETED bildirim/realtime push:** WP1 yalnız state geçişini yapar; satıcı "Ödemeniz gönderildi" bildirimi + realtime TransactionStatusChanged push **WP9** (realtime/notification tamlığı) devir. PayoutCompletedEvent payload (txHash + net amount) bu tüketiciler için hazır.

## Commit & PR
- Branch: `task/WP1-escrow-completion-payout`
- Commit: `972a793` — WP1: Escrow completion — seller payout + COMPLETED
- PR: [#169](https://github.com/turkerurganci/Skinora/pull/169)
- CI: ⏳ izleniyor

## Notlar
- **Working tree:** Oturum başında temiz.
- **Main CI startup:** Son 3 run `success` (`27500668384`, `27500668387`, `27498092438`).
- **Dış varsayımlar:** (1) Yeni decimal SystemSetting key, validator generic positive-number kuralıyla otomatik kapsanır (`SystemSettingsValidator.cs:242-248` — kanıt: kod okundu) ✓. (2) `BlockchainTransaction.GasFee` USDT breakdown'da kullanımı mevcut (`AdminTransactionQueryService.BuildPayoutDetail` zaten `payout.GasFee` okur) — snapshot repurpose tutarlı ✓. (3) Docker lokal yok → integration testler CI'da (proje deseni) ✓.
