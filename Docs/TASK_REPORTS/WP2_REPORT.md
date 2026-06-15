# WP2 — İade yürütme: `BUYER_REFUND` (+ canlı admin-cancel defekti)

**Faz:** PRE_F6_PLAN (F6 öncesi MVP borç kapatma) | **Durum:** ✓ Tamamlandı (bağımsız validator PASS 2026-06-15) | **Tarih:** 2026-06-15

---

## Yapılan İşler

- **Yeni `PaymentRefundToBuyerConsumer`** (`INotificationHandler<PaymentRefundToBuyerRequestedEvent>`, `Skinora.Transactions.Application.Transfers`): üç terminal-iptal yolundan (delivery timeout, admin-cancel AD19, emergency-hold-release-cancel AD19c) yayınlanan event'i tüketir → PENDING `BUYER_REFUND` `BlockchainTransaction` satırı kuyruğa alır. **Bu, hiç tüketilmeyen event nedeniyle `BUYER_REFUND` satırının hiç üretilmemesi olan canlı defekti kapatır.**
- **İade tutarı (02 §4.6 / §4.7):** `IRefundDecisionService.ResolveBuyerRefundAsync(transaction.TotalAmount, refundGasFee)` (mevcuttu, hiç çağrılmıyordu — "calculator-caller-wiring refund kısmı"). `Amount = decision.NetRefund = TotalAmount − gasFee` (net; alıcı gas'ı karşılar, platform maliyeti sıfır). `GasFee = refundGasFee` snapshot (07 §7.5 reconstruction: originalAmount = Amount + GasFee = TotalAmount). Gas kaynağı = `blockchain.refund_gas_fee_estimate_usdt` (2.0, T72'den **zaten seeded**).
- **Block sonucu (owner kararı: webhook paritesi):** net negatif veya dust-eşiğin altında ise `IRefundBlockedAlertService.RaiseAsync` → audit `REFUND_BLOCKED` + `RefundBlockedAdminAlertEvent`, satır üretilmez. `AmountValidationService`'teki 5 iade yoluyla birebir tutarlı.
- **Idempotency — tam savunma (owner kararı, WP1 F1 deseni):** (1) `AnyAsync(TransactionId, Type=BUYER_REFUND)` guard; (2) filtered unique index `UQ_BlockchainTransactions_BuyerRefund_TransactionId` (`(TransactionId) WHERE [Type]='BUYER_REFUND'`) DB-backstop; (3) `catch(DbUpdateException)` → detach + re-query → varsa idempotent no-op / yoksa re-throw. Tx başına en fazla 1 BUYER_REFUND meşru (üç publish yolu da terminal; tx bir kez iptal edilir). Diğer iade tipleri kısıtlanmaz.
- **DI:** `TransactionsModule.cs`'te explicit MediatR kaydı (concrete + interface factory) — `Skinora.Transactions` OutboxModule MediatR scan listesinde değil, eksik kayıt event'i sessizce düşürürdü (kapatılan defektin ta kendisi).
- **07 §7.5 iade kırılımı DTO (owner kararı: şimdi wire et, WP1 ile simetrik):** `TransactionDetailService.BuildRefundAsync` — alıcı görünümünde (`role=="buyer"`) BUYER_REFUND satırından `RefundDto` türetir (originalAmount/gasFee/netRefundAmount/refundAddress/txHash/refundedAt). Admin tarafı (`AdminTransactionQueryService.BuildRefundDetail`) zaten jenerik *_REFUND satırından türetiyordu → **değişiklik gerekmedi**, yeni satırı otomatik gösterir.
- **State flip YOK:** tx event publish anında zaten terminal `CANCELLED_*` (REFUNDED status yok; `OutgoingTransferConfirmationJob` yalnız SELLER_PAYOUT için event emit eder). Mevcut `OutgoingTransferDispatchJob` (BUYER_REFUND zaten `OutboundTypes`'da) yayınlar, confirmation job on-chain finalize eder.

## Etkilenen Modüller / Dosyalar

| Dosya | Değişiklik |
|---|---|
| `Application/Transfers/PaymentRefundToBuyerConsumer.cs` | **YENİ** — consumer (net hesap + gas snapshot + Block-alert + 3-katman idempotency) |
| `Infrastructure/Persistence/BlockchainTransactionConfiguration.cs` | Filtered unique index `UQ_BlockchainTransactions_BuyerRefund_TransactionId` |
| `Persistence/Migrations/20260615160558_WP2_AddBuyerRefundUniqueIndex.cs` (+Designer, +Snapshot) | **YENİ** — şema-only index migration (seed yok) |
| `Skinora.API/Configuration/TransactionsModule.cs` | Consumer'ın explicit MediatR kaydı |
| `Application/Lifecycle/TransactionDetailService.cs` | `BuildRefundAsync` → `Refund` DTO (alıcı görünümü, 07 §7.5) |
| `tests/.../Unit/Transfers/PaymentRefundToBuyerConsumerTests.cs` | **YENİ** — 7 unit testi |
| `tests/.../Integration/Lifecycle/TransactionDetailServiceTests.cs` | +2 refund-breakdown testi + cancel-CK helper |

## Kabul Kriterleri Kontrolü

| # | Kriter (plan WP2 "İş" + owner kararları) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `PaymentRefundToBuyerRequestedEvent` consumer'ı PENDING `BUYER_REFUND` satırı üretir | ✓ | `PaymentRefundToBuyerConsumer.Handle`; test `ValidRefund_QueuesPendingBuyerRefund...` (Amount 100, GasFee 2, ToAddress=event adresi, Token USDT, PaymentAddressId/ActualTokenAddress null, Status PENDING) |
| 2 | İade tutarı = TotalAmount − gasFee (net, 02 §4.6); gas snapshot | ✓ | `decision.NetRefund` + `GasFee = gasFee`; `RefundDecisionService` `net = totalPaid − gasFee` |
| 3 | Idempotent — tx başına en fazla 1 BUYER_REFUND (tam savunma) | ✓ | AnyAsync + filtered unique index + catch re-query; testler `Redelivery_QueuesExactlyOneRow`, `ConcurrentInsertRace_SwallowsDuplicate...`, `NonDuplicateDbUpdateException_IsRethrown...` |
| 4 | Block (negatif/dust) → admin-alert, satır yok | ✓ | testler `BelowThresholdRefund_RaisesAdminAlert...` (reason BelowMinimumThreshold), `NegativeRefund_RaisesAdminAlert...` (NegativeAmount) |
| 5 | Consumer explicit MediatR kaydı (scan-dışı assembly) | ✓ | `TransactionsModule.cs` concrete + interface factory; build ✓ |
| 6 | Canlı admin-cancel iade defekti kapandı | ✓ | Event 3 yerden yayınlanıyordu (`AdminTransactionService.cs:162,553`, `TimeoutSideEffectPublisher.cs:99`) → artık tüketiliyor; consumer testi event→satır kanıtlar |
| 7 | 07 §7.5 iade kırılımı (alıcı görünümü + admin) | ✓ | `BuildRefundAsync`; testler `Cancelled_BuyerView_Surfaces_RefundBreakdown` (originalAmount 102.00), `Cancelled_SellerView_Omits_RefundBreakdown`; admin tarafı jenerik (değişiklik yok) |
| 8 | Dispatch + finality mevcut pipeline'ı kullanır; status flip yok | ✓ | `OutgoingTransferDispatchJob.OutboundTypes` BUYER_REFUND içeriyor; tx zaten terminal CANCELLED_* (REFUNDED status yok) |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (Transactions) | ✓ **446/446** (+7 yeni) | `dotnet test --filter "...!~.Integration&...!~.Contract"` — `PaymentRefundToBuyerConsumerTests` 7/7 |
| Integration (Transactions) | ✓ CI-confirmed | +2 refund-breakdown testi (`TransactionDetailServiceTests`) — lokal Docker/SQL Server yok; CI `4. Integration test` job success (HEAD `b43fe03` run `27561695471`) |
| Build | ✓ 0W/0E | `dotnet build Skinora.sln` Debug **ve** Release |
| Format | ✓ temiz | `dotnet format --verify-no-changes --severity error` — çıktı yok |
| Migration drift | ✓ yok | `dotnet ef migrations has-pending-model-changes` → "No changes have been made to the model since the last migration." |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** — bağımsız validator (ayrı chat, 2026-06-15) |
| Verdict | ✓ PASS — 8/8 kabul kriteri ✓, 0 bloke-edici bulgu |
| Bulgu sayısı | 0 (6-boyut adversarial workflow, refute-default: hiçbir boyut bulgu üretmedi) |
| Düzeltme gerekli mi | Hayır |

**Validator kanıtları (bağımsız üretildi, rapor görülmeden):**
- **Kapılar:** Adım -1 working tree temiz · Adım 0 main son-3 run success (`27509836014`/`27509836010`/`27500668387`) · Adım 0b repo memory WP2 satırı mevcut · Adım 8a task branch CI HEAD `b43fe03` run [`27561695471`](https://github.com/turkerurganci/Skinora/actions/runs/27561695471) **tüm job success** (Lint/Build/Unit/**Integration**/Contract/**Migration dry-run**/Docker/Gate). (Önceki `d277860` run'ı cancelled — docs commit'i tarafından supersede edildi.)
- **Lokal yeniden çalıştırma:** Transactions `--filter "...!~.Integration&...!~.Contract"` **446/446** + `Category=Unit` 87/87 (7 consumer testi dahil) · `dotnet build Skinora.sln -c Release` **0W/0E** · `dotnet format --verify-no-changes --severity error` temiz · model snapshot BUYER_REFUND filtered unique index içeriyor (`AppDbContextModelSnapshot.cs:2108`), drift yok.
- **Spec uyumu (primary kaynaktan bağımsız teyit):** İade tutarı net = `TotalAmount − gas` = 02 §4.6 line 126 "Fiyat + komisyon − gas fee" + §4.7 (alıcı gas'ı karşılar, platform maliyeti sıfır); `TotalAmount = Price + CommissionAmount` (06 §3.5:560 = `PaymentAddress.ExpectedAmount` = alıcının gerçekten ödediği) → fazla-ödeme ayrı EXCESS_REFUND ile döner, çift-iade yok; refund DTO `BuildRefundAsync` 07 §7.5 birebir (originalAmount=Amount+GasFee, netRefundAmount=Amount, yalnız alıcı görünümü).
- **Money-safety:** 3 publish yolu yalnız `PaymentWasReceived` (PAYMENT_RECEIVED / TRADE_OFFER_SENT_TO_BUYER) + non-empty TRC-20 refund adresinde fire eder → ödenmemiş işleme iade yok. Idempotency 3-katman (AnyAsync + filtered unique index gerçekten enforce — `ConcurrentInsertRace` SQLite testi kanıtlar + catch detach/re-query/re-throw). Constraint: BUYER_REFUND satırı `CK_..._Type_Outbound`'u (PaymentAddressId/ActualTokenAddress null) + 4 status CHECK'i sağlar. Dispatch: `OutgoingTransferDispatchJob.OutboundTypes` BUYER_REFUND içerir → satır PENDING'de takılı kalmaz.
- **6-boyut adversarial workflow (6 ajan refute + adversarial verify, refute-default):** money / idempotency / activation / constraints / dto / wiring — **6/6 PASS, 0 onaylanmış bloke-edici bulgu, 0 ham bulgu**.
- **Güvenlik mini-kontrol:** Secret sızıntısı yok · yeni dış bağımlılık yok (csproj değişmedi) · refund yolu server-side, adres upstream TRC-20-doğrulanmış · auth etkisi yok.
- **Yapım raporu karşılaştırması:** Tam uyumlu — 8/8 AC ve test numaraları (446/446) bağımsız teyit edildi, uyuşmazlık yok.

## Altyapı Değişiklikleri

- **Migration:** Var — `20260615160558_WP2_AddBuyerRefundUniqueIndex` (filtered unique index, **şema-only, seed yok**). `CK_..._Type_Outbound` BUYER_REFUND'ı zaten kapsıyor; yeni index `IX_BlockchainTransactions_TransactionId` ile yan yana (named overload). Pre-launch → temiz uygulanır.
- **Config/env değişikliği:** Yok (`refund_gas_fee_estimate_usdt`=2.0 T72'den seeded).
- **Docker değişikliği:** Yok.
- **Yeni dış bağımlılık:** Yok (consumer mevcut MediatR/EF kullanır; Hangfire gerekmez — bu bir job değil event consumer'ı).

## Commit & PR

- Branch: `task/WP2-buyer-refund`
- Commit: `d277860` (+ docs `b43fe03`)
- PR: [#170](https://github.com/turkerurganci/Skinora/pull/170)
- CI: ✓ HEAD `b43fe03` run [`27561695471`](https://github.com/turkerurganci/Skinora/actions/runs/27561695471) tüm job success

## Known Limitations / Follow-up

- **Block redelivery re-alert:** Block durumunda satır üretilmediği için `AnyAsync` guard tekrar-teslimde alerti tekrar yayınlayabilir (yalnız handler RaiseAsync+SaveChanges'ten önce throw ederse). Düşük olasılık, düşük zarar (yinelenen admin alarmı; para hareketi yok) — at-least-once semantiğiyle tutarlı.
- **Alıcıya "İadeniz gönderildi" push/realtime:** WP9'a ertelendi (WP1'in COMPLETED push ertelemesiyle simetrik). Event payload + on-chain finality hazır.
- **Inline iade yolları (wrong-token/late-payment/excess/incorrect-amount):** Gross-amount `QueueRefundIntent` kullanır (T72-dönemi, ayrı senaryo); WP2 kapsamı dışı, dokunulmadı.

## Notlar

- **Working tree:** Adım -1 temiz.
- **Adım 0 (main CI son 3 run):** hepsi success — `27509836010` (WP1 #169), `27500668387` (#168), `27498092432` (T103b-2).
- **Dış varsayımlar:** `dotnet ef` 9.0.3 mevcut (migration üretimi); `refund_gas_fee_estimate_usdt` T72'den seeded (08 §3.4, default 2.0). Yeni dış varsayım yok.
- **Anlama fazı:** 1 Explore probe + 6-ajan paralel keşif workflow + completeness critic → BUYER_REFUND pipeline tam haritalandı; iki ajan arasındaki "net vs gross" çelişkisi primary kaynaktan (02 §4.6 + 07 §7.5 DTO) **net** lehine çözüldü.
- **Owner kararları (AskUserQuestion 2026-06-15):** idempotency = tam savunma (F1) · refund DTO = şimdi wire et · Block = webhook paritesi.
