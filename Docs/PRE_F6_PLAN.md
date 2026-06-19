# F6 Öncesi MVP Borç Kapatma Planı (Pre-F6 Plan)

> **Karar (2026-06-14, owner):** F6'ya (Uçtan Uca Doğrulama) başlamadan önce **MVP kapsamındaki tüm ertelenmiş / not-bırakılmış / yarım iş kapatılacak. Artık erteleme yok.**
>
> **Kaynak:** [`DEFERRED_BACKLOG.md`](DEFERRED_BACKLOG.md) (~90 kalem) + 2026-06-14 kod-doğrulama taraması (3 inceleme ajanı) + plan-doğrulama workflow'u (4 boyut: completeness / MVP-sınıflandırma / kanıt-doğruluğu / bağımlılık). Her iş paketi backlog ID'leri + kod kanıtıyla bağlandı; doğrulama bulguları bu sürüme folde edildi.
>
> **İlke:** Yalnızca **MVP-DIŞI** (bkz. §3) veya **by-design / imkânsız** olanlar hariç tutuldu — bunlar erteleme değil, MVP tanımı gereği kapsam dışı.

---

## 0. Kritik bağlam — neden bu iş F6'dan ÖNCE

F6 = uçtan uca E2E test fazı. Tarama, **happy-path'in kendisinin bugün tamamlanamadığını** kanıtladı: escrow akışı `ITEM_DELIVERED`'da çıkmaz sokağa giriyor — satıcıya payout kuyruğa alınmıyor, `Complete`→`COMPLETED` geçişi hiçbir prod kodundan ateşlenmiyor (`.Fire(Complete)` grep = 0). Yani ertelenen işlerin çoğu "isteğe bağlı temizlik" değil, **ürünün gerçekte uçtan uca çalışması için eksik kalan motor bağlantıları (caller/consumer wire-up'ları)**. Bunlar kapanmadan E2E testleri zaten geçemez.

**Backlog düzeltmeleri ([`DEFERRED_BACKLOG.md`](DEFERRED_BACKLOG.md)'ye yansıtıldı):**
- **`emergency-hold-callers` kısmen stale** → tekil + per-user bulk hold/release/cancel hepsi bağlı (T59/T100/T103b-2). Kalan gerçek alt-kalemler: *post-payment refund tetik* → **WP2**, *dispute-queue surfacing* → **WP5**. RowVersion guard zaten mevcut.
- **`blockchain-monitor-consumers` kısmen stale** → `payment-confirmed` → `PAYMENT_RECEIVED` geçişi **tam bağlı** (finality webhook, `AmountValidationService`). Kalan: mempool `PaymentDetected` consumer'ı (by-design opsiyonel, finality-temelli) + DROPPED metrik (→ **WP16**).
- **`item-refund-consumers` çoğu bağlı** → 5 iade akışından yalnız **`BUYER_REFUND`** gerçekten kopuk. Late-payment / wrong-token / excess / incorrect-amount iadeleri `BlockchainWebhookHandler` içinde **inline intent** üretir (`:201`/`:354`); item-iade **T106a** `ItemRefundDispatchConsumer` ile bağlı.
- **`T55-DormantThreshold` tekil değil** → aynı fail-fast'le **21 zorunlu SystemSetting** prod-startup'ı bloklar (`SettingsBootstrapTests.cs:92`). Bu deploy runbook işi, kod değil.

---

## 1. Sıra ve faz tablosu

| Faz | WP | Başlık | Tamamladığı yetenek | F6 bağı | Efor |
|---|---|---|---|---|---|
| **P1 — Para akışı** | WP1 | Escrow tamamlama: payout + `COMPLETED` | Satıcı ödenir, işlem biter | **T107** | M |
| | WP2 | İade yürütme: `BUYER_REFUND` (+ canlı admin-cancel defekti) | Alıcı parası gerçekten iade olur | **T108** | M |
| | WP3 | Hot-wallet/ledger doğruluğu: SWEEP dispatcher *(migration)* | Mutabakat iki-taraflı doğru | **T110** | M |
| **P2 — Fraud/uyum** | WP4a | Fraud accept-gate + canlı fiyat (wiring) | Flag'li hesap engellenir, PRICE_DEVIATION çalışır | **T111** | M |
| | WP4b ✅ | Retro-scan + FLAGGED-approve timeout (by-design) + alloc + note-limit | Fraud kapsam tamlığı | T111 | M |
| | WP5 ✅ | Dispute çözüm (admin): `/admin/disputes` + resolve *(migration)* | ESCALATED çıkmaz sokak kapanır | **T113** | M–L |
| | WP6 | Steam dispute checker'ları (trade-hold + MA) + auto-resolve doğrula | DELIVERY/WRONG_ITEM otomatik çözülür | T111/T113 | M |
| **P3 — Operasyon** | WP7 | Outage/maintenance: bulk-freeze çağıran + toggle push | Platform dondurulabilir | **T114** | M |
| | WP8 | Admin bildirim/alert + audit tamamlama *(migration)* | Admin olayları görür/aksiyon alır | T113 | M |
| | WP9 | Realtime tamlık: Steam push + FE admin abonelik | Canlı durum/admin event'leri | — | M |
| | WP10 ⏳ | Tron dayanıklılık: 429 failover + ikincil key + per-event dedup *(migration)* | Para-katmanı dayanıklı (MVP, 08 §3.6/§3.7) | — | M |
| **P4 — Kullanıcı/FE** | WP11 ✅ | Auth UI wire-up + ToS reprompt + brute-force lock | Kullanıcı UI'dan gerçekten login olur | T107 (browser) | M |
| | WP12 | Kullanıcı kenar durumları (OPEN_LINK 409 *bağımsız*, refund override) | Eşzamanlılık/UX doğruluğu | T108/T110 | M |
| | WP13 | FE tamlık: yasal sayfalar + polish + enum sync | /privacy /terms /support + UX | T113 | M–L |
| **P5 — Config/altyapı** | WP14 | Settings runtime propagasyon + 21 ayar seed/runbook | Ayar değişimi yansır, prod açılır | T114 | M |
| | WP15 | Reputation aggregation tetik | İtibar skoru güncel | — | M |
| | WP16 | Monitoring/health probe + uptime heartbeat (MVP) | Outage recovery + gözlemlenebilirlik | T114 | M |
| **P6 — Borç temizliği** | WP17 | Doc/spec/i18n mutabakat | Doküman↔kod hizalı | gate | M |
| | WP18 | Test/CI sertleştirme (FE runner, prettier CI, npm audit) | Regresyon güvenliği | gate | M–L |

**Bağımlılıklar:** WP1 her şeyin temeli (ilk). **WP5 → WP1 + WP2** (satıcı-lehine release WP1'in `Complete`→payout yolunu, alıcı-lehine refund WP2'nin `BUYER_REFUND`'ünü kullanır). WP4a accept-gate ucuz/yüksek-değer (erken). WP12'deki **OPEN_LINK 409 fix bağımsız** — P1'de WP1 ile inebilir. WP18 sürekli/son.
**Migration taşıyan paketler:** **WP3** (SWEEP için type-bağımlı CHECK constraint), **WP8** (`Notification.FlagId` kolonu), **WP4a** (owner kararı: `price_deviation_threshold` seed default 1.0 → `UpdateData`; seed `HasData` model'in parçası olduğundan migration gerektirir), **WP5** (owner kararı: yapısal statü — `CK_Disputes_Resolved_ResolvedAt` + `CK_Transactions_Cancel` REFUNDED, iki CHECK recreate, seed yok) ve **WP10** (owner Q1=full-per-event: `BlockchainTransaction.EventIndex` kolonu + `(TxHash,EventIndex)` UNIQUE recreate + `CK_..._EventIndex`, şema-only) — gate-check yeni migration dosyası bekler.

---

## 2. İş paketleri — detay

### WP1 — Escrow tamamlama: satıcı payout + `COMPLETED`
> **Durum: ⏳ Devam ediyor (2026-06-14)** — PR [#169](https://github.com/turkerurganci/Skinora/pull/169), doğrulama bekliyor. Uygulama: `SellerPayoutQueueJob` (producer) + `PayoutCompletedEvent`/`PayoutCompletedConsumer` (completion) + `blockchain.payout_gas_fee_estimate_usdt` (0.50) + 07 §7.5 payout DTO. Rapor: [`TASK_REPORTS/WP1_REPORT.md`](TASK_REPORTS/WP1_REPORT.md).

**Backlog:** payout-completed-consumer · calculator-caller-wiring (payout kısmı) · energy-gas-token-config (gas-split) — *İkincil, COMPLETED'i bloklamaz:* StubPayoutVerifier · payout-retry-consumer
**Kanıt:** `SteamWebhookHandler.cs:497` `DeliverItem` sonrası durur; `TransactionStateMachine.cs:251-253` `Complete` çağıransız (`.Fire(Complete)` grep=0); `RefundDecisionService.ResolveSellerPayoutAsync` (`:48`) çağıransız. **On-chain finality ZATEN var:** `OutgoingTransferConfirmationJob.cs:31-39,89-99` `SELLER_PAYOUT` satırını DETECTED→CONFIRMED 20-blok eşiğinde işler.
**İş (T107'yi açan çekirdek):** Teslim sonrası `SELLER_PAYOUT` PENDING satırı kuyruğa al (mevcut `OutgoingTransferDispatchJob` yayınlar) → `CalculateSellerPayout`+gas-split aktifleşir → mevcut confirmation job `SELLER_PAYOUT` CONFIRMED olunca **`PayoutCompletedEvent` emit** → consumer `Complete`→`COMPLETED` ateşler. **Yeni `IPayoutVerifier` GEREKMEZ** — mevcut confirmation job on-chain doğrulamayı yapar.
**İkincil (ayrı/düşük öncelik, COMPLETED'i bloklamaz):** Gerçek `IPayoutVerifier` + `RETRY_SCHEDULED` retry job — bunlar `PayoutIssueService` (T60) **satıcı-bildirimli sorun akışı**, yalnız COMPLETED *sonrası* çalışır (`PayoutIssueService.cs:101-105`).
**Efor:** M · **Açar:** T107

### WP2 — İade yürütme: `BUYER_REFUND` (+ canlı admin-cancel defekti)
**Backlog:** item-refund-consumers (gerçek boşluk = `BUYER_REFUND`) · calculator-caller-wiring (refund kısmı) · emergency-hold-callers (post-payment refund tetik)
**Kanıt:** `PaymentRefundToBuyerRequestedEvent` `TimeoutSideEffectPublisher.cs:99` + `AdminTransactionService.cs:162,553`'te yayınlanıyor ama `IConsumer<PaymentRefundToBuyerRequestedEvent>` **yok** → `BUYER_REFUND` satırı hiç üretilmiyor. `OutgoingTransferDispatchJob.cs:44-52` `OutboundTypes`'da `BUYER_REFUND` zaten var → yalnız satır-üreten consumer eksik.
**ZATEN bağlı (`BUYER_REFUND` boşluğu DEĞİL):** late-payment + wrong-token + excess + incorrect-amount iadeleri `BlockchainWebhookHandler` inline `QueueRefundIntent` üretir (`:201` wrong-token, `:354` late-payment); item-iade T106a `ItemRefundDispatchConsumer`.
**İş:** `PaymentRefundToBuyerRequestedEvent` consumer'ı → `BUYER_REFUND` PENDING satır (`AmountValidationService` deseni; dispatch job zaten işler). Bu aynı zamanda **canlı bir defekti kapatır:** admin-cancel iadesi (`AdminTransactionService.cs:162`) bugün sessizce kopuk — yani WP2 yalnız WP5 enabler'ı değil, üretim defekti düzeltmesidir.
**Efor:** M · **Açar:** T108

### WP3 — Hot-wallet/ledger doğruluğu: SWEEP dispatcher
> **Durum: ✓ Tamamlandı — bağımsız validator PASS (2026-06-16)** — branch `task/WP3-sweep-dispatcher`, PR #171. Uygulama: `SweepQueueJob` (producer, **ITEM_DELIVERED kapısı** — owner kararıyla iade penceresi sonrasına ertelendi) + SWEEP'i dispatch **ve** confirmation `OutboundTypes`'a ekle + `HttpBlockchainTransferClient` SWEEP→`/api/transfer/sweep` dalı + `CK_..._Type_Sweep` + `UQ_..._Sweep_TransactionId` migration. Rapor: [`TASK_REPORTS/WP3_REPORT.md`](TASK_REPORTS/WP3_REPORT.md). Yapım-içi adversarial review PASS (9 ham → 0 bloke-edici); bağımsız validator 6-boyut/23-ajan PASS (0 bloke-edici). Validator-fix: 06_DATA_MODEL.md §2.5/§3.8 SWEEP doc-conformance bu PR'a katıldı (owner onaylı). CC-03 (unresolvable-source sonsuz PENDING) → follow-up.
**Backlog:** SWEEP-dispatcher
**Kanıt:** `BlockchainTransactionType.cs:15-22` yorumu: "sweep dispatcher (PaymentReceivedEvent consumer) T-future; 0 SWEEP satırı → mutabakat sadece-çıkış"; tek `PaymentReceivedEvent` consumer'ı SignalR push (`PaymentReceivedRealtimeConsumer.cs`).
**İş (uygulanan):** SWEEP (depozit→hot-wallet) satırı üreten producer; `OutgoingTransferDispatchJob.OutboundTypes`'a (**ve confirmation job'a — yoksa CONFIRMED'a hiç ulaşmaz, mutabakat görmez**) `SWEEP` ekle; kaynak-adres çözümü (`OutgoingTransferDispatchJob.cs`, `row.Type != SELLER_PAYOUT` dalı) SWEEP'i ele alır; `HttpBlockchainTransferClient.BuildRequest`'e SWEEP→`/api/transfer/sweep` dalı (kod SWEEP'i sessizce `/refund` default'una düşürüyordu — kapatıldı; sidecar `/sweep` zaten gerçek/T74).
**⚠ Tetik kararı (owner, AskUserQuestion 2026-06-15):** 05 §3.3:316 tetik=`PaymentReceivedEvent` der ama bu, depozit-kaynaklı alıcı-iadesini (WP2) **boş depozitle** bozardı (teslim-timeout iadesi ödemeden sonra). **Sweep `ITEM_DELIVERED` anına ERTELENDİ** (05 §3.3:323: sweep öncesi iade depozitten) — iade yolu değişmedi. 05 §3.3:316/317 doc reconciliation → **WP17**.
**⚠ Migration:** SWEEP `CK_..._Type_Outbound`'a eklenmez (o PaymentAddressId NULL şart koşar; SWEEP'in tersi gerekir) → **yeni `CK_BlockchainTransactions_Type_Sweep`** (PaymentAddressId NOT NULL, ActualTokenAddress NULL) + filtered unique index. Migration `20260615194323_WP3_AddSweepConstraintAndIndex` (şema-only, seed yok).
**Efor:** M · **Açar:** T110 (mutabakat assertion'ları)

### WP4a — Fraud accept-gate + canlı fiyat (wiring)
**Backlog:** fraud-acceptance-gate (accept-gate kısmı) · T81-PriceConsumerWireup
**Kanıt:** `IAccountFlagChecker` yalnız `TransactionEligibilityService.cs:56`'da (advisory), accept yolunda yok → flag'li hesap kabul edebiliyor. `IMarketPriceProvider`=`NullMarketPriceProvider` (`TransactionsModule.cs:76`). **Steam Market stack ZATEN var:** `ISteamMarketPriceClient` + `SteamMarketRateLimiter` (429/RegisterRetryAfter) + `SteamMarketPriceParser` + `ItemPriceCache` entity (T81 migration) + Fraud `PriceService` (cache-first, TTL).
**İş:** Accept yoluna `IAccountFlagChecker` gate ekle; **fiyat = SADECE WIRING:** Transactions `IMarketPriceProvider` → mevcut Fraud `PriceService`/`ISteamMarketPriceClient` köprüsü + `classId/instanceId`→`marketHashName` çözümü. **T81'i yeniden yazma** (harici API + cache + rate-limit hazır).
**Efor:** M · **Açar:** T111

### WP4b — Fraud kapsam tamlığı (retro-scan + FLAGGED yolları) — ✅ Çözüldü (validator bekliyor)
**Backlog:** T56-MultiAccountRetroScan · T54-FlaggedApproveNoTimeoutJob · flagged-allocation-detail (backend) · T54-FraudNoteNoMaxLength · fraud-acceptance-gate (sinyal üreticileri/background scan kısmı)
**Kanıt:** `MultiAccountDetector` yalnız `WalletAddressService.cs:144` (wallet-update); FLAGGED-approve (FLAGGED→CREATED) per-tx accept-timeout job kaydetmiyor (poll'a güveniyor); payment-address allocate yalnız CREATED'de (FLAGGED-approval atlanıyor); fraud-note max-length yok.
**İş:** Periyodik MultiAccount retro-scan Hangfire job; FLAGGED-approve per-tx timeout job; FLAGGED-approval'da payment-address allocate; fraud-note max-length validasyonu.
**Çözüm (owner kararları, AskUserQuestion):** (1) **retro-scan** = günlük const cron `MultiAccountRetroScanJob` (Skinora.API, `AutoUnsuspendJob` deseni), cüzdanlı aktif kullanıcıları `IMultiAccountDetector` ile tarar (kaba per-user dedup, mevcut gate). (2) **FLAGGED-approve timeout** = **sadece-doğrula, yeni job YOK** — 05 §4.4 + 06 §3.5:650 accept-deadline'ları bilinçli poller-driven yapar (yalnız ITEM_ESCROWED per-tx job alır); `DeadlineScannerJob` `ApproveAsync`'in setlediği `AcceptDeadline`'ı zaten enforce ediyor → regresyon testi + resolved-by-design. (3) **allocation** = `FraudFlagService.ApproveAsync` post-commit eager `AllocateAsync` (best-effort; `EnsurePaymentAddressJob` recovery'yi tamamlar). (4) **note max-length** = 2000 char (kolon genişliği) → 400 `VALIDATION_ERROR`. **Migration YOK** (model değişmedi).
**Efor:** M · **Açar:** T111

### WP5 — Dispute çözüm (admin) — ✅ Çözüldü (validator bekliyor)
> **Durum: ⏳ Devam ediyor (2026-06-17)** — yapım tamam, bağımsız validator bekliyor. **Owner kararları (AskUserQuestion):** çözüm modeli = **yapısal statü + migration** (yeni `DisputeStatus` `RESOLVED_FOR_SELLER`/`RESOLVED_FOR_BUYER` + yeni terminal `TransactionStatus.REFUNDED` + `AdminResolveRefund` trigger) · kapsam = **full-stack** · permission = **VIEW_DISPUTES + MANAGE_DISPUTES çifti** · minör kalemler = **ikisi de WP5'te** (per-type `disputableTypes` + `ACTIVE_DISPUTE_EXISTS` 07'den kaldırıldı). Uygulama: `AdminDisputeService` (AD27/28/29, API katmanı) + `DisputeResolvedEvent`/consumer + FE `/admin/disputes` ekranı. **Migration `WP5_AddDisputeResolution`** (iki CHECK recreate; seed yok). Rapor: [`TASK_REPORTS/WP5_REPORT.md`](TASK_REPORTS/WP5_REPORT.md).

**Backlog:** T58-AdminDisputeQueue · T58-canDisputeEnvelopeBit · T58-ActiveDisputeExistsUnreachable · emergency-hold-callers (dispute-queue surfacing)
**Kanıt:** `GET /admin/disputes` yok; `IDisputeService` yalnız buyer-facing; `Dispute.AdminId/AdminNote` (`Dispute.cs:16,23`) hiç atanmıyor; ESCALATED→CLOSED yolu yok. DELIVERY/WRONG_ITEM dispute yalnız `ITEM_DELIVERED`'da açılır (`DisputeService.cs:73-81`) → satıcı-lehine resolve `Complete`→`COMPLETED` (WP1), alıcı-lehine resolve `BUYER_REFUND` (WP2) gerektirir.
**İş:** `GET /admin/disputes` (ESCALATED liste) + `POST /admin/disputes/{id}/resolve` → `AdminResolveAsync` (CLOSED + AdminId/AdminNote/ResolvedAt + item-release **[WP1]** veya iade **[WP2]** çıktısı + audit + notify). Per-type `canDispute`.
**🔒 Scope-fence (10_MVP_SCOPE §3.6/§6):** YALNIZ minimal çıkmaz-sokak-açıcı (ESCALATED→CLOSED + release/refund + audit + notify). SLA/çok-adımlı state/atama/şablon-kural **post-MVP** — gold-plating yok.
**Efor:** M–L · **Açar:** T113 · **Bağımlı:** **WP1 + WP2**

### WP6 — Steam dispute checker'ları + auto-resolve doğrulama
> **Durum: ⏳ Devam ediyor (2026-06-17)** — yapım tamam, bağımsız validator bekliyor. **Owner kararları (AskUserQuestion):** sidecar = **doğrudan Steam Web API** (`GetTradeHoldDurations`, bot session yok) · Item 2/3 = **doğrula + regresyon + by-design**. Sidecar `GET /api/trade-hold/:steamId` + paylaşılan `ISteamTradeHoldProbe`/`HttpSteamTradeHoldClient` + `SidecarTradeHoldChecker` (U17) + `SidecarMobileAuthenticatorCheck` (A7, fail-closed); auto-resolve yolu doğrulandı + null-probe regresyonu; TradeOfferMonitor hot-add resolved-by-design (statik pool). MIGRATION YOK. Rapor: [`TASK_REPORTS/WP6_REPORT.md`](TASK_REPORTS/WP6_REPORT.md).

**Backlog:** steam-sidecar-stubs (trade-hold + MA checker) ✅ · TradeOfferMonitor-hotadd-T69 ✅ (resolved-by-design)
**Kanıt:** DELIVERY/WRONG_ITEM auto-checker'lar `ISteamInventoryReader` tüketir — bu **zaten gerçek** (`SidecarSteamInventoryReader`, T67; `SteamModule.cs:56` DI-swap). Gerçek stub'lar yalnız: `StubTradeHoldChecker` (`UsersModule.cs:124`, Available=true) + `StubMobileAuthenticatorCheck` (`SteamAuthenticationModule.cs:156`).
**İş:** Gerçek sidecar-destekli trade-hold + MA checker; mevcut `SidecarSteamInventoryReader` auto-resolve yolunu **doğrula/sertleştir** (sıfırdan kurma DEĞİL); `TradeOfferMonitor` hot-add re-attach (yalnız dinamik pool — statik pool'da sorun yok). Not: bu checker'lar WP1 teslim-confirmation ve WP4a creation-check yollarına da dokunur.
**Efor:** M · **Açar:** T111/T113 dispute kapsamı

### WP7 — Outage/maintenance
**Backlog:** T50-OutageFreezeCallers · maintenance-toggle · suspend-signalr-spec (force-restrict)
**Kanıt:** `FreezeManyAsync`/`ResumeManyAsync` (`TimeoutFreezeService.cs:106`) **0 prod çağıran** (yalnız test). `PublishMaintenanceStatusChangedAsync` (`SignalRNotificationRealtimePublisher.cs:63`) **0 çağıran**; `PUT /admin/settings` push tetiklemez.
**İş:** Admin `POST /admin/maintenance/freeze`+`/resume` → `FreezeManyAsync(reason)`; STEAM_OUTAGE/BLOCKCHAIN_DEGRADATION auto-detect; `platform.maintenance.*` update'inde cache-evict + `PublishMaintenanceStatusChangedAsync`.
**Efor:** M · **Açar:** T114

### WP8 — Admin bildirim/alert + audit tamamlama
> **Durum: ✓ Tamamlandı — bağımsız validator PASS (2026-06-17)** — PR [#177](https://github.com/turkerurganci/Skinora/pull/177). 4 admin NotificationType gerçek üretici event'lere bağlandı (owner: tüm adminler · `BOT_SESSION_FAILED` additive · audit-detail dar kapsam · 4 tip · TradeOfferDispatchFailed hariç). Migration `WP8_AddNotificationFlagId`. Validator: 6/6 AC, 0 bloke-edici; Release 0W/0E + Notifications unit 36/36 + EnumTests 204/204 + AuditLogCategoryMap 38/38 + drift yok; task CI `fdeddb8`/`dd245da` success. Rapor: [`TASK_REPORTS/WP8_REPORT.md`](TASK_REPORTS/WP8_REPORT.md).

**Backlog:** T68-K1 · T64-BotWebhookHandler · T38-AdminFlagAlert-FlagId · admin-alert-consumers · audit-detail-schema (central wiring)
**Kanıt:** bot lifecycle yalnız Warning log; `Notification` entity'de `FlagId` yok (`Notification.cs:12-25`; `ADMIN_FLAG_ALERT` `TransactionId`'yi flag-link olarak suistimal eder, `NotificationTargetMapper.cs:24-27`); `RefundBlockedAdminAlert`/`TransferDispatchFailed`/payout-issue alert consumer'ları yok.
**İş:** Bot lifecycle → admin notification + `BOT_SESSION_FAILED` audit + `bot.session_failed`/`removed_from_pool` handler; `Notification.FlagId` ekle (flag inbox link); admin-alert kanal consumer'ları; central AuditLog wiring + OldValue.
**⚠ Migration:** `Notification.FlagId` (nullable Guid) yeni kolon (indeksli, soft-delete tablo) + `NotificationTargetMapper` gevşetme → **EF migration gerekir.**
**Efor:** M

### WP9 — Realtime tamlık
**Backlog:** T61-SteamTransitionRealtimePush · FE-admin-signalr-subscription · T61-AdminHubJoinBypass · T69-K4 · signalr-toast-countdown · signalr-scaling (group-failure obs)
**Kanıt:** Steam pipeline geçişlerinde `TransactionStatusChanged` push yok; `RealtimeProvider.tsx:40-43` üç admin event'ini abone etmiyor; `TransactionsHub.JoinTransaction` admin bypass yok; `AdminBotStatusChanged` `Clients.All`.
**İş:** Steam geçiş push'ları (RealtimeConsumer); FE 3 admin event aboneliği; hub admin-join bypass; admin-only group scope; toast/countdown realtime; group-failure observability. *(Redis backplane ölçek-dışı → §3.)*
**Efor:** M

### WP10 — Tron dayanıklılık
> **Durum: ⏳ Devam ediyor (2026-06-18)** — yapım tamam, bağımsız validator bekliyor. **Owner kararları (AskUserQuestion):** event_index = **full per-event (sidecar + backend migration)**, index kaynağı = **gerçek on-chain log index** (`gettransactioninfobyid` `log[]`) · 429/failover kapsamı = **yalnız okuma yolu (TronGridClient)** · backoff = **poll-dostu kısa retry** (08 §3.5 doc reconciliation). **Dış varsayım bulgusu:** TronGrid trc20 list endpoint'i `event_index` döndürmüyor (canlı probe doğrulandı) → log dizisinden çözüldü. **MIGRATION TAŞIR** (`WP10_AddBlockchainTxEventIndex`). Rapor: [`TASK_REPORTS/WP10_REPORT.md`](TASK_REPORTS/WP10_REPORT.md).

**Backlog:** tron-resilience · energy-gas-token-config (HD cache, gas config)
**Kapsam notu:** **MVP** — `08_INTEGRATION_SPEC.md:597` "MVP | TronGrid (primary) + ikinci TronGrid API key (rate limit fallback)"; §3.6/§3.7 5xx 3×-retry + 429 exponential backoff. (Multi-sweeper/Redis backplane bu DEĞİL → §3.)
**İş:** TronGrid 429 + `TRON_API_KEY_SECONDARY` failover + retry (okuma yolu); event_index dedup **txid+event_index** (gerçek on-chain log index, 08 §3.4 / 06 §3.8 `(TxHash,EventIndex)` UNIQUE + migration); HD address cache; gas fee config'lenebilir (`TRANSFER_FEE_LIMIT_SUN`).
**Migration:** `WP10_AddBlockchainTxEventIndex` — yeni nullable `EventIndex` kolonu + `UQ_BlockchainTransactions_TxHash` → `UQ_BlockchainTransactions_TxHash_EventIndex` (recreate) + `CK_BlockchainTransactions_EventIndex` (`>= 0`); şema-only, seed yok.
**Efor:** M (owner Q1=full-per-event ile full-stack'e büyüdü)

### WP11 — Auth UI wire-up ✅
**Backlog:** T87-K1 · T30-TosVersionReprompt · misc-user-features (brute-force lock) · T40-PermClaimCache
**Kanıt:** Callback yalnız `?status` okur (`callback/page.tsx:48`), `POST /auth/refresh` çağırmaz → `localStorage["access_token"]` hiç yazılmaz → `isAuthenticated` daima false. ToS-accept/authenticator UI-only. Backend endpoint'leri hazır.
**İş:** Callback→`/auth/refresh`→token store; ToS-accept→`POST /auth/tos/accept`; authenticator→`POST /auth/check-authenticator`; 401→refresh interceptor; ToS-versiyon reprompt; login brute-force lock; per-user permission TTL cache.
**Efor:** M · **Açar:** T107 (full-stack/browser E2E ise)
> **Durum: ✅ Tamamlandı (PR #—, doğrulama bekliyor).** Owner kararları (AskUserQuestion): **MA recheck = /auth/me ile** (A7 trade-URL akışına ait, login'de değil — 03 §2.1/07 §4.8; standalone recheck `mobileAuthenticatorActive`'i yeniden okur) · **brute-force = mevcut rate-limit'i `temporarily_locked` redirect'ine bağla** (05 §6.3 klasik brute-force N/A; `GET /auth/steam` 429 yerine callback'e redirect; migration yok) · **permission TTL cache = T40 kararı korunur (cache YOK)** (dinamiklik > performans, by-design) · **ToS reprompt = tam** (CurrentUserDto += `tosAcceptedVersion`, `tos/accept` versiyon-upgrade'e izin verir, FE `TosRepromptGate`). **MIGRATION YOK** (DTO + logic). Uygulama: FE callback→refresh→token store + 401 single-flight refresh interceptor + `acceptTos` wire-up + `TosRepromptGate` + MA recheck via /auth/me; BE `CurrentUserDto.TosAcceptedVersion` + `TosAcceptanceService` versiyon-upgrade + `RateLimitAttribute.RedirectToSteamCallbackOnReject` + middleware redirect. Rapor: [`TASK_REPORTS/WP11_REPORT.md`](TASK_REPORTS/WP11_REPORT.md).

### WP12 — Kullanıcı kenar durumları
**Backlog:** T46-OpenLinkConcurrentAcceptRace · misc-user-features (per-tx refund override, trade-offer URL DTO, delete atomicity) · timeout-warning-setting
**Kanıt:** Eşzamanlı OPEN_LINK accept'te race-loser `DbUpdateConcurrencyException`→HTTP 500 (`TransactionAcceptanceService.cs:100-147`) — 409 ALREADY_ACCEPTED olmalı.
**İş:** OPEN_LINK accept race → 409 (**bağımsız fix, WP2/WP3'e bağlı değil — P1'de inebilir**); per-tx refund override (WP2'ye bağlı); trade-offer URL DTO; delete atomicity; `DefaultTimeoutWarningPercent`/`accept_timeout_minutes` config'lenebilir.
**Efor:** M
> **Durum: ✅ Tamamlandı (PR #184, doğrulama bekliyor).** Owner kararları (AskUserQuestion, hepsi öneri): #2 **saf snapshot** (accept profil `DefaultRefundAddress`/cooldown'a dokunmaz; Stage-5 gate yalnız okur) · #3 **backend DTO + cross-module port şimdi** (`steamTradeOfferUrl`; FE href WP13) · #4 **`BeginTransactionAsync` ile sarmala** · #5 **`timeout_warning_ratio` wire-up**. **Plandan sapma (WP4a emsali):** plan WP12'yi migration-taşımaz sayıyordu; #5 için mevcut `timeout_warning_ratio` seed'i Unconfigured→`Default("0.75")` çevrildi (yeni key DEĞİL — keşif ajanı mevcut anahtarı kaçırmıştı; `feedback_validate_placement`) → `UpdateData` migration `WP12_SeedTimeoutWarningRatio` (saf seed, şema yok). #1/#2/#4 migration gerektirmez. Rapor: [`TASK_REPORTS/WP12_REPORT.md`](TASK_REPORTS/WP12_REPORT.md).

### WP13 — FE tamlık
> **Durum: ✓ Tamamlandı — bağımsız validator PASS (2026-06-19)** — PR [#186](https://github.com/turkerurganci/Skinora/pull/186). **SALT FRONTEND, migration YOK.** Owner kararları (AskUserQuestion): yasal sayfalar = iskelet+i18n placeholder (gerçek metin WP17) · permission guard = minimal FE route guard · **next/image ERTELE → WP18** · kalan polish = TAM. **Validator (ayrı chat, rapor görülmeden): ✓ PASS — 9/9 AC, 0 bloke-edici** (3 non-blocking gözlem); enum sync backend `.cs` ile birebir (27/30/5/10); tsc0/eslint0/prettier(touched)/next build/i18n 1230×4 (legal 42×4) validator-çalıştırıldı; task CI iki run success (`27831840011`/`27831329203`). Teslim: enum sync + `/privacy`+`/terms`+`/support` iskelet + login→dashboard redirect + `AdminGuard` + admin-table-sort (transactions+flags) + url-state-sync (dashboard+wizard) + NEXT_LOCALE cookie + Tronscan-link/asset-id polish + verification countdown + email-cooldown + steamTradeOfferUrl href + ACCOUNT_FLAGGED i18n + formatAmount/logout cleanup. Lokal: tsc0/eslint0/prettier(touched)/next build 36 route/i18n 1230×4. **Ertelenenler:** next/image→WP18; closed-dispute notu + payout-issue UI = backend DTO gap; gerçek ToS/Privacy metni→WP17 (content-authoring). Rapor: [`TASK_REPORTS/WP13_REPORT.md`](TASK_REPORTS/WP13_REPORT.md).

**Backlog:** static-routes-pages · admin-table-sort · url-state-sync · profile-prefill-image · dispute-detail-polish · FE-permission-guard · FE-enums-ts-lag · T97-NEXT_LOCALE-cookie · T97-formatAmount-deprecated-alias · flagged-allocation-detail (tx-detail sub-DTO)
**Kanıt:** `/privacy` placeholder, `/terms` 404, `/support` undefined (**MVP yasal gereklilik**, 10_MVP_SCOPE §2.15); `types/enums.ts` backend'in gerisinde.
**İş:** Yasal sayfalar (/privacy /terms /support) + login→dashboard redirect; admin tablo tıkla-sırala (API hazır); url-state-sync; profil pre-fill + next/image; dispute-detail polish; client permission guard; enum sync; NEXT_LOCALE cookie; deprecated alias temizliği.
**Efor:** M–L

### WP14 — Settings propagasyon + 19 ayar ✅
**Backlog:** setting-sidecar-propagation ✅ · T55-DormantThreshold ✅ · ~~timeout-warning-setting~~ (zaten WP12 ✅)
**Kanıt:** `SystemSettingsService.UpdateAsync` (`:68`) yalnız DB+audit; sidecar env-only boot; cron `StartAsync`'te register; `SettingsBootstrapTests.cs:92` "19 mandatory rows" (plan "21" stale → WP4a+WP12 ikisini düşürdü).
**İş (yapıldı):** (1) **cron re-register** — `reconciliation.schedule_cron` + `hot_wallet.monitor_cron` admin değişiminde restart'sız re-register (`ISettingChangePropagator`→`CronSettingChangePropagator`→`ICronJobReconfigurer.Reconfigure`); geçersiz cron → 400 (`SystemSettingsValidator` + Cronos). (2) **sidecar cadence/sweep** → owner kararı **env parity + runbook** (runtime push/pull DEĞİL; post-MVP T74 K1/T96). (3) **19 zorunlu ayar** → owner kararı **deploy runbook** (seed-default DEĞİL; fail-fast korundu) → `Docs/DEPLOY_RUNBOOK.md` + `.env.example`.
**Migration:** YOK (cron re-register + runbook; seed-default seçilmedi).
**Efor:** M

### WP15 — Reputation aggregation tetik
> **Durum: ✓ Tamamlandı (2026-06-19, bağımsız validator PASS — 4/4 AC, 0 bloke-edici, 4 non-blocking; N1 PayoutCompletedConsumer self-heal yorum hassasiyeti → WP17).** PR [#188](https://github.com/turkerurganci/Skinora/pull/188), task CI HEAD `6af9ae0` run `27847024321` tüm job success. T43'ün caller'sız bıraktığı reputation altyapısı + onun bağımlı olduğu **TransactionHistory yazımı** (06 §3.6, bugüne dek hiç yazılmıyordu) bağlandı. **Owner kararları (AskUserQuestion):** History kapsamı = **tam audit trail** (tüm geçişler); mimari = **sync inline + paylaşılan recorder** (`TransactionCancellationService` emsali). Yeni `TransactionHistoryRecorder` (static helper) + `ITransactionReputationRefresher` (aggregator+cooldown sarması). 12 caller wiring (genesis USER + forward/terminal SYSTEM + admin ADMIN). Terminal recompute iki-fazlı (flush→recompute→save; `AsNoTracking` + timeout `PreviousStatus` görünürlüğü). **Kritik fix:** timeout sorumluluk-atfı History olmadan sessizce kopuktu. Migration YOK. Test-infra: 3 API endpoint `Reset()` FK fix (History `NO ACTION` FK + Steam SYSTEM-user koruma). Lokal: Tx **801/801** + Steam 106 + Fraud 91 + API **523/523** + Disp/Users/Plat 39/22/187; format temiz. Rapor: [`TASK_REPORTS/WP15_REPORT.md`](TASK_REPORTS/WP15_REPORT.md).

**Backlog:** reputation-aggregator-trigger ✅
**İş:** `IReputationAggregator.RecomputeAsync` + cooldown tetik; state-machine OnEntry/History caller'ları.
**Efor:** M

### WP16 — Monitoring/health probe + uptime heartbeat
**Backlog:** misc-monitoring-probes · uptime-cache-scaleout (heartbeat kısmı) · blockchain-monitor-consumers (DROPPED metrik)
**Kapsam ayrımı:** **uptime heartbeat = MVP** (`05_TECHNICAL_ARCHITECTURE.md:533-535` — `LastHeartbeat` job, restart sonrası recovery, timeout'lar outage-window kadar uzar; **T114 downtime E2E'nin motoru**). *Redis cache scale-out* kısmı **post-MVP** → §3.
**İş:** Steam/Telegram health probe; Redis webhook idempotency; **uptime heartbeat tablo/job (MVP)**; timeout reschedule; DROPPED metrik.
**Efor:** M

### WP17 — Doc/spec/i18n mutabakat
**Backlog:** T33-SuccessRate-FractionVsPercent · AD6-AD7-contract-recon · audit-doc-drift · backend-i18n-migration · T103-K4 · content-authoring (ToS/Privacy metni) · permissioncatalog-xmldoc-drift · datamodel-sanctioned-index-drift · admin-route-table-drift · T84-emergencyhold-status-doc-drift
**İş:** successRate fraction/percent kararı; AD6-AD7 kontrat recon; audit doc-drift; backend notification/fraud/dispute mesajları es/zh çeviri + AD10 `warningMessage`; ToS/Privacy gerçek metin; tüm xmldoc/datamodel/route drift'leri tek doc-pass'te.
**Efor:** M

### WP18 — Test/CI sertleştirme
**Backlog:** FE-test-runner · prettier-drift (CI) · i18n-lint-ci · filterbar-dateto-chore · test-infra-misc · sidecar-npm-audit · template-side-escape-audit · like-escape-helper
**İş:** FE Vitest runner; CI `format:check`; i18n lint CI; dateTo end-of-day repo-geneli; TestContainers Redis/SQL + eksik endpoint testleri; sidecar npm-audit (20 transitive); paylaşılan LIKE-escape helper + no-direct-INSERT arch rule.
**Efor:** M–L

---

## 3. Kapsam Dışı — erteleme DEĞİL (MVP tanımı gereği)

Aşağıdakiler bu plana **dahil değil** çünkü MVP-dışı (10_MVP_SCOPE / 02_PRD), by-design, veya imkânsız. Bunları "yapmamak" erteleme değildir:

| Kalem | Neden hariç |
|---|---|
| reviews, KYC, mobil app, diğer oyunlar, multi-item/barter, ek blockchain, fiat, premium, Discord guild | 10_MVP_SCOPE açık MVP-dışı |
| discord-interactions-userinstall | Post-MVP geliştirme (mevcut Bot DM + OAuth2 MVP'yi karşılar) |
| vpn-fraud (default-off) | Opsiyonel/post-MVP |
| sanctions-expansion (OFAC/EU/UN auto-sync) | MANUAL `SanctionedAddress` MVP'yi karşılar (02_PRD §21.1); feed-sync post-MVP |
| signalr-scaling: **yalnız Redis backplane (multi-instance)** · multi-sweeper | Tek-instance/tek-sweeper MVP; ölçek post-MVP (05_TECHNICAL_ARCHITECTURE §… `101,132`). *CountdownSync/group-failure obs → WP9.* |
| uptime-cache-scaleout: **yalnız Redis cache scale-out** | Post-MVP ölçek. *Heartbeat'in kendisi MVP → WP16.* |
| Sentry / harici APM | 10_MVP_SCOPE post-MVP (05 §9.4/§9.6) |
| USDC/USDT 1:1 · hardcoded 6-decimal | By-design doğru (TRC-20) |
| Wallet adres backfill | İmkânsız — tarihsel veri hiç tutulmadı |
| GitHub branch protection | Paid feature (403), owner kararı — kod borcu değil |
| dev-route-visibility (`/dev/components`) | By-design dev menü |

---

## 4. Büyüklük ve yaklaşım

- **19 iş paketi** (WP1–WP18, WP4 = WP4a+WP4b), her biri sizin metodolojinizde ayrı **task** (plan→uygula→**ayrı chat** validate). Kabaca **30–45 geliştirme-günü** (validate chat'leri hariç). Bu, ürünün gerçek MVP-tamamlanması — F6 bunun üzerine sağlam test yazabilsin diye.
- **Her task ayrı PR + CI yeşil + bağımsız validator** (mevcut disiplin; gate-check öncesi kalite).
- **Migration taşıyan paketler:** WP3 (SWEEP CHECK constraint) · WP8 (`Notification.FlagId`) · WP4a (seed `price_deviation_threshold`=1.0 `UpdateData`, owner kararı) · WP5 (dispute/REFUNDED CHECK recreate) · WP10 (`BlockchainTransaction.EventIndex` + `(TxHash,EventIndex)` UNIQUE recreate, owner Q1=full-per-event) · WP12 (seed `timeout_warning_ratio`=0.75 `UpdateData`, owner #5 kararı — plan başta migration-taşımaz sayıyordu, WP4a emsali) — gate-check yeni migration dosyası bekler.
- **Önerilen ilk hamle:** WP1 (escrow tamamlama) — ürünün tamamlanamadığı tek nokta; geri kalan her şey bunun üstüne oturur. OPEN_LINK 409 fix'i (WP12) ucuz/bağımsız, WP1 ile paralel inebilir.
- Bu plan ilerledikçe güncellenir; her WP biten için backlog satırı **✓ Çözüldü** işaretlenir.
