# T49 — Timeout execution

**Faz:** F3 | **Durum:** ✓ Tamamlandı (PASS, 1 minor advisory) | **Tarih:** 2026-05-02

---

## Yapılan İşler

T44'ün `Timeout` trigger'ı ve T47'nin scheduling pipeline'ı sonrası, bir `Timeout` tetiklenmesinin **observable side effect'leri** bağlandı (02 §3.2, 03 §4.1–§4.4):

1. **Phase enum + 4 yeni outbox event** (`Skinora.Shared/Enums/TimeoutPhase.cs`, `Skinora.Shared/Events/`):
   - `TimeoutPhase` — `Accept` / `TradeOfferToSeller` / `Payment` / `Delivery` (03 §4.1–§4.4 birebir).
   - `TransactionTimedOutEvent` — notification fan-out trigger (her 4 phase için).
   - `ItemRefundToSellerRequestedEvent` — Steam sidecar (T64–T68 forward) için tetikleyici, Payment + Delivery phase'lerinde.
   - `PaymentRefundToBuyerRequestedEvent` — Blockchain sidecar (T73 forward) için tetikleyici, Delivery phase'inde.
   - `LatePaymentMonitorRequestedEvent` — Blockchain sidecar (T75 forward) için tetikleyici, Payment phase'inde.

2. **Phase-aware side-effect publisher** (`Skinora.Transactions/Application/Timeouts/`):
   - `ITimeoutSideEffectPublisher` + `TimeoutSideEffectPublisher` — tek noktada phase → event mapping. Caller `previousStatus` (Timeout fire öncesi state) geçer; publisher uygun event(ler)i outbox change-tracker'a enlist eder.
   - Mapping: `CREATED → Accept` (sadece notification), `TRADE_OFFER_SENT_TO_SELLER → TradeOfferToSeller` (sadece notification), `ITEM_ESCROWED → Payment` (notification + item refund + late payment monitor), `TRADE_OFFER_SENT_TO_BUYER → Delivery` (notification + item refund + payment refund).
   - Defensive logging: ITEM_ESCROWED'de BuyerId/BuyerRefundAddress null ise late payment monitor skip + log warning (06 §3.5'e göre olmamalı, schema regression'ı bayraklamak için). TRADE_OFFER_SENT_TO_BUYER'da aynı koruma + log error.

3. **TimeoutExecutor + DeadlineScannerJob entegrasyonu** (aynı klasör):
   - Her ikisi `previousStatus` capture ediyor → state machine `Fire(Timeout)` çağırıyor → publisher `PublishAsync(transaction, previousStatus)` çağırıyor → tek `SaveChangesAsync` ile state flip + outbox satırları atomik commit. T48 `WarningDispatcher` deseni birebir.
   - TimeoutExecutor sadece `ITEM_ESCROWED` (Payment phase) için aktif (per-tx Hangfire job + scanner belt-and-suspenders). DeadlineScannerJob 4 phase'i de tarar.

4. **Notifications consumer** (`Skinora.Notifications/Application/EventHandlers/`):
   - `TransactionTimedOutNotificationConsumer : NotificationConsumerBase<TransactionTimedOutEvent>` — `notifications.transaction-timed-out` consumer name.
   - Her event için **iki ayrı** `NotificationRequest` (seller + buyer if registered) — phase × role'a göre Türkçe `Reason` parametresi (03 §4.1–§4.4 metinleri verbatim). Mevcut `TRANSACTION_CANCELLED` template'i (`{ItemName} işlemi iptal edildi: {Reason}.`) tekrar kullanılıyor, yeni enum/resx satırı eklenmedi. i18n full coverage T97 forward-deferred (Türkçe literal'ler taşınacak).

5. **Sidecar consumer'lar** Steam (T64–T68) ve Blockchain (T73 / T75) sidecar task'larında devralınacak — T49'da publisher tarafı production-ready, tüketici tarafı F4'te kayıt edilecek. T45 `TransactionCreatedEvent` + T46 `BuyerAcceptedEvent` events'leri için de aynı pattern (consumer T62/T78–T80 forward-deferred).

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `backend/src/Skinora.Shared/Enums/TimeoutPhase.cs`
- `backend/src/Skinora.Shared/Events/TransactionTimedOutEvent.cs`
- `backend/src/Skinora.Shared/Events/ItemRefundToSellerRequestedEvent.cs`
- `backend/src/Skinora.Shared/Events/PaymentRefundToBuyerRequestedEvent.cs`
- `backend/src/Skinora.Shared/Events/LatePaymentMonitorRequestedEvent.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Timeouts/ITimeoutSideEffectPublisher.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Timeouts/TimeoutSideEffectPublisher.cs`
- `backend/src/Modules/Skinora.Notifications/Application/EventHandlers/TransactionTimedOutNotificationConsumer.cs`
- `backend/tests/Skinora.Transactions.Tests/Integration/Timeouts/TimeoutSideEffectPublisherTests.cs` — 7 test.
- `backend/tests/Skinora.Transactions.Tests/Integration/Timeouts/TimeoutExecutorSideEffectsTests.cs` — 2 test.
- `backend/tests/Skinora.Transactions.Tests/Integration/Timeouts/DeadlineScannerJobSideEffectsTests.cs` — 3 test.
- `backend/tests/Skinora.Notifications.Tests/Unit/TransactionTimedOutNotificationConsumerTests.cs` — 7 test.

**Değişiklik:**
- `backend/src/Modules/Skinora.Transactions/Application/Timeouts/TimeoutExecutor.cs` — `ITimeoutSideEffectPublisher` inject + Fire sonrası `PublishAsync` çağrısı.
- `backend/src/Modules/Skinora.Transactions/Application/Timeouts/DeadlineScannerJob.cs` — aynı.
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` — `ITimeoutSideEffectPublisher` Scoped DI kaydı.
- `backend/tests/Skinora.Transactions.Tests/Integration/Timeouts/TimeoutTestSupport.cs` — `CapturingOutboxService` + `NoOpTimeoutSideEffectPublisher` test double'ları + `NoOpSideEffects()` helper.
- `backend/tests/Skinora.Transactions.Tests/Integration/Timeouts/TimeoutExecutorTests.cs` — `TimeoutExecutor` constructor çağrılarına `NoOpSideEffects()` argümanı.
- `backend/tests/Skinora.Transactions.Tests/Integration/Timeouts/DeadlineScannerJobTests.cs` — aynı.
- `backend/tests/Skinora.Shared.Tests/Unit/EnumTests.cs` — `Skinora.Shared.Enums` enum count assertion 24 → 25 (`TimeoutPhase` eklendi).

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Kabul timeout → CANCELLED_TIMEOUT (iade gerekmez) | ✓ | `DeadlineScannerJobSideEffectsTests.Accept_Timeout_Publishes_Only_Notification_Event`: state CANCELLED_TIMEOUT'a geçer, sadece `TransactionTimedOutEvent{Phase=Accept}` publish — refund event yok. |
| 2 | Trade offer timeout → CANCELLED_TIMEOUT (iade gerekmez — item henüz platformda değil) | ✓ | `DeadlineScannerJobSideEffectsTests.TradeOfferToSeller_Timeout_Publishes_Only_Notification_Event`: state flip + `Phase=TradeOfferToSeller` event, refund yok. |
| 3 | Ödeme timeout → CANCELLED_TIMEOUT (item satıcıya iade) | ✓ | `TimeoutExecutorSideEffectsTests.Payment_Timeout_Publishes_Notification_ItemRefund_And_LatePaymentMonitor`: 3 event publish — `TransactionTimedOutEvent{Phase=Payment}` + `ItemRefundToSellerRequestedEvent{Trigger=Payment}` + `LatePaymentMonitorRequestedEvent`. Item refund event'i Steam sidecar (T64–T68) tarafından gerçek trade offer'a çevrilecek. |
| 4 | Teslim timeout → CANCELLED_TIMEOUT (item satıcıya iade, ödeme alıcıya iade) | ✓ | `DeadlineScannerJobSideEffectsTests.Delivery_Timeout_Publishes_Notification_ItemRefund_And_PaymentRefund`: `TransactionTimedOutEvent{Phase=Delivery}` + `ItemRefundToSellerRequestedEvent{Trigger=Delivery}` + `PaymentRefundToBuyerRequestedEvent{BuyerId, BuyerRefundAddress}`. Ödeme iadesi T73 (Blockchain Sidecar transfer) ile gerçeklenecek. |
| 5 | Her senaryoda doğru iade tetikleme | ~ Kısmi | Event tetikleme cihetinden ✓ — `TimeoutSideEffectPublisherTests` 7 testi her phase için doğru event setini ve payload alanlarını doğrular. **Gerçek refund execution** (Steam trade offer + TRC-20 transfer) sidecar task'larına forward-deferred: T64–T68 (Steam Sidecar), T73 (Blockchain Sidecar transfer), T75 (post-cancel monitor). T49 publisher tarafını production-ready hale getiriyor; consumer kayıtları F4'te. T45 `TransactionCreatedEvent` (consumer T62/T78–T80) ile aynı paralel pattern. |
| 6 | Gecikmeli ödeme izleme başlatma (ödeme timeout sonrası) | ~ Kısmi | `LatePaymentMonitorRequestedEvent` Payment phase'inde publish ediliyor (`TimeoutExecutorSideEffectsTests`). Gerçek izleme T75 (Blockchain Sidecar — `PostCancelMonitor` stub mevcut, gerçek implementasyon T75'te). T49 event'i ile sidecar'ın "izlemeye başla" sinyalini alacağı kanal hazır. |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (TimeoutSideEffectPublisher) | ✓ 7/7 | `TimeoutSideEffectPublisherTests` — 4 phase × event setleri + 2 missing buyer guard + 1 unsupported phase exception. |
| Unit (TransactionTimedOutNotificationConsumer) | ✓ 7/7 | `TransactionTimedOutNotificationConsumerTests` — accept fan-out, missing buyer skip, 4 phase × role reason text [Theory], idempotency replay. |
| Integration (TimeoutExecutor) | ✓ 2/2 | `TimeoutExecutorSideEffectsTests` — Payment timeout 3-event publish + state-already-advanced no-op. |
| Integration (DeadlineScannerJob) | ✓ 3/3 | `DeadlineScannerJobSideEffectsTests` — Accept / TradeOfferToSeller / Delivery phase'leri için doğru event setleri. |
| Regresyon — tüm test asembly'leri | ✓ 1243/1243 | Skinora.Users 16 + Fraud 17 + Payments 6 + Disputes 11 + Steam 21 + Shared 182 + Platform 136 + Transactions 403 + API 265 + Auth 93 + Notifications 73 + Admin 20 = **1243** (yapım raporunda toplam 1237 yazılmıştı — breakdown 1243'e topluyor; validator düzeltti, fonksiyonel etki yok). T48: 1224 → T49: 1243 (+19 net = 12 yeni Transactions + 7 yeni Notifications). Skinora.Transactions.Tests 391/391 → 403/403 (+12), Skinora.Notifications.Tests 66/66 → 73/73 (+7). |
| Build (Release) | ✓ 0W/0E | `dotnet build -c Release` — `Build succeeded. 0 Warning(s) 0 Error(s)`. |
| Format verify | ✓ exit=0 | `dotnet format --verify-no-changes` clean. |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS (bağımsız validator chat, 2026-05-02) |
| Bulgu sayısı | 1 minor advisory (S0/S1/S2/S3 yok) |
| Düzeltme gerekli mi | Hayır — minor rapor sayım drift'i validator inline düzeltti (1237 → 1243) |

### Bağımsız Validator Bulguları

| # | Seviye | Açıklama | Etkilenen dosya |
|---|---|---|---|
| M1 | minor | "Test Sonuçları" satırı toplam **1237** yazılmıştı; per-assembly breakdown (16+17+6+11+21+182+136+403+265+93+73+20) **1243**'e topluyor. Lokal `dotnet test` 1243/1243 PASS. Fonksiyonel etki yok — rapor metin drift'i, validator inline düzeltti (bkz. **Test Sonuçları** tablosu). | `Docs/TASK_REPORTS/T49_REPORT.md` |

### Validator Kanıt Özeti

- **Pre-flight:** working tree temiz, main CI son 3 ardışık ✓ (T48 #78 + T47 #77), MEMORY.md T49 satırı mevcut (drift yok).
- **Lokal:** `dotnet build -c Release` 0W/0E (24 proje), `dotnet format --verify-no-changes` exit=0, `dotnet test -c Release --no-build` 1243/1243 PASS (12 test assembly).
- **Task branch CI:** run [25258360678](https://github.com/turkerurganci/Skinora/actions/runs/25258360678) HEAD `393749c` 9/9 + Guard skipped ✓ (Lint/Build/Unit/Integration/Contract/Migration dry-run/Docker build/CI Gate). Önceki run [25258220783](https://github.com/turkerurganci/Skinora/actions/runs/25258220783) HEAD `93cbfae` da ✓.
- **Doc compliance:** Notification reason text 03 §4.1–§4.4'ten verbatim alınmış (4 phase × 2 role = 8 dize); refund mapping `MapPhase(previousStatus)` 02 §3.2 ile birebir; atomicity (state flip + outbox enqueue + tek SaveChanges) 09 §13.3 / §9.3 ile uyumlu.
- **Güvenlik:** secret leak yok; yeni endpoint yok; user input yok; PII (BuyerRefundAddress) outbox internal kanal, sidecar handler'larında dış kullanım T64–T68 / T73 / T75 forward.

## Altyapı Değişiklikleri

- **Migration:** Yok — yeni veritabanı kolonu/index yok.
- **Config/env değişikliği:** Yok.
- **Docker değişikliği:** Yok.
- **Yeni dış bağımlılık:** Yok (MediatR + Hangfire + Outbox infra T10/T37/T47'den var).
- **Yeni enum (`TimeoutPhase`):** `Skinora.Shared.Enums` içinde, 4 değer (`Accept`/`TradeOfferToSeller`/`Payment`/`Delivery`). EnumTests count assertion güncellendi.

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok — yeni dosyaların hiçbiri secret içermiyor.
- **Auth/authorization etkisi:** Yok — yeni endpoint yok; arka plan iş hattı.
- **Input validation:** İşlem yok — event payload'u tamamen sunucu kontrolünde (Transaction snapshot + clock); kullanıcı girdisi yok.
- **PII fan-out:** `BuyerRefundAddress` (Tron wallet) `LatePaymentMonitorRequestedEvent` ve `PaymentRefundToBuyerRequestedEvent` payload'larına giriyor — sidecar event'leri internal kanaldan (outbox + MediatR) tüketilir, dış servise gönderim sidecar handler'larında yapılır (T73/T75 kapsamı). Notification body'sinde wallet yok; PII yüzeyi büyütülmedi. T63b `NotificationAccountAnonymizer` (kullanıcı silimde) zaten kapsanmış.

## Commit & PR

- Branch: `task/T49-timeout-execution`
- Commit: `ef5d54f` — `T49: Timeout execution (02 §3.2, 03 §4.1-§4.5, 09 §13.3)`
- PR: [#79](https://github.com/turkerurganci/Skinora/pull/79)
- CI: ✓ PASS — run [`25258220783`](https://github.com/turkerurganci/Skinora/actions/runs/25258220783) (HEAD `93cbfae`) 9/9 + Guard (direct push) skipped. Detect/Lint/Build/Unit/Contract/Integration/Migration dry-run/Docker build/CI Gate hepsi success. Önceki run [`25258207765`](https://github.com/turkerurganci/Skinora/actions/runs/25258207765) (HEAD `ef5d54f`) cancelled (ardışık push concurrency).

## Known Limitations / Follow-up

- **Sidecar consumer'lar forward-deferred:**
  - `ItemRefundToSellerRequestedEvent` consumer'ı **T64–T68** (Steam Sidecar — bot session, trade offer gönderme/durum izleme/webhook). Event payload'ı yeterli (`TransactionId`, `SellerId`, `Trigger=Payment|Delivery`); consumer Steam sidecar'a HTTP çağrısı yapar.
  - `PaymentRefundToBuyerRequestedEvent` consumer'ı **T73** (Blockchain Sidecar — TRC-20 transfer). Refund tutarı (price + commission − gas fee, 02 §4.6) consumer tarafında hesaplanır — DB'den lookup yeterli (event payload'ında refund address + buyer id var).
  - `LatePaymentMonitorRequestedEvent` consumer'ı **T75** (Blockchain Sidecar — gecikmeli ödeme izleme). Sidecar'ın `PostCancelMonitor` stub'ı (`sidecar-blockchain/src/monitor/PostCancelMonitor.ts`) zaten yer tutuyor; T75 gerçek izleme + auto-refund mantığını ekler.
- **i18n:** Notification reason metinleri (03 §4.1–§4.4) Türkçe verbatim consumer içinde hard-code. `TRANSACTION_CANCELLED_Body` mevcut template'i `{Reason}` parametresini render ediyor, ama 4 dil resx'inde phase × role variants'ı yok. Full locale coverage **T97 (i18n)** kapsamında: 8 ayrı resx anahtarı (`TIMEOUT_REASON_ACCEPT_SELLER`, `_BUYER`, `TIMEOUT_REASON_TRADE_OFFER_SELLER`, `_BUYER`, `_PAYMENT_*`, `_DELIVERY_*`) × 4 dil = 32 satır eklenir, consumer phase × role'u resx anahtarına çevirir, dispatcher `IStringLocalizer` üzerinden render eder. Bu işin sigortası: T97 plan kabul kriterleri zaten "tüm bildirim metinleri 4 dilde resx'te" diyor.
- **MediatR scan asembly:** `OutboxModule.GetMediatRScanAssemblies` halen yalnızca `Skinora.API` + `Skinora.Notifications` modül asembly'lerini scan ediyor. Steam/Payments sidecar consumer'ları F4'te eklendiğinde scan listesine `Skinora.Steam` + `Skinora.Payments` modül asembly'leri eklenecek (T48 desenine paralel: `Skinora.Notifications.NotificationsModule.Assembly` aynı PR'da eklenmişti).

## Notlar

- **Working tree pre-flight:** temiz (`git status --short` boş çıktı).
- **Main CI startup pre-flight:** son 3 main run ✓ — [`25256755520`](https://github.com/turkerurganci/Skinora/actions/runs/25256755520) (T48 #78), [`25256755518`](https://github.com/turkerurganci/Skinora/actions/runs/25256755518) (T48 #78), [`25255400779`](https://github.com/turkerurganci/Skinora/actions/runs/25255400779) (T47 #77).
- **Dış varsayım:** yok — internal task, no paid features, no external SDK; tüm altyapı (MediatR, Hangfire, Outbox, NotificationConsumerBase, IOutboxService) önceki task'lardan mevcut.
- **Dependency check:** T47 ✓ (PR #77 squash `e00f97a`) + T44 ✓ (PR #74 squash) + T37 ✓ (PR #61).
- **Mimari karar — neden 4 ayrı event yerine tek event + phase parametresi?** Tek event tutarlı görünüyor ama farklı consumer'ları (Notifications / Steam / Blockchain) tek payload'a bağlamak zorlama bağlantı yaratıyor: Steam consumer phase = Payment | Delivery için tetiklenmek istiyor ama kendi guard'ını yazmak zorunda kalıyor. Ayrı event'ler subscriber'ı net çizer (her consumer ilgili event'i bekler, başka tipte tetiklenmez). 09 §13.1 "domain event = bir şey oldu" semantiği ile uyumlu.
- **Mimari karar — neden `previousStatus` parametresi?** State machine `Fire(Timeout)` çağrısından sonra `transaction.Status` zaten `CANCELLED_TIMEOUT`. Phase'i (hangi deadline doldu) yeniden türetmek için `previousStatus` capture şart. Capture noktası fire'dan hemen önce — concurrent fire riski yok (RowVersion concurrency token tek seferlik commit).
- **Atomicity (05 §5.1, 09 §9.3):** Hem TimeoutExecutor hem DeadlineScannerJob `_sideEffects.PublishAsync(transaction, previousStatus)` çağırır → publisher `IOutboxService.PublishAsync` ile change-tracker'a outbox satırlarını ekler → caller tek `SaveChangesAsync` ile state flip + outbox row'lar atomik commit. T48 deseninin paraleli.
- **Pre-existing test count drift:** Skinora.Shared.Tests T48 raporunda 172 yazıyordu, gerçek run 182. T49'da değişmedi (sadece 1 enum count assertion update). Validator dikkate alabilir; fonksiyonel etki yok.
