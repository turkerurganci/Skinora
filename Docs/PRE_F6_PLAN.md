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
| | WP4b | Retro-scan + FLAGGED-approve timeout + alloc + note-limit | Fraud kapsam tamlığı | T111 | M |
| | WP5 | Dispute çözüm (admin): `/admin/disputes` + resolve | ESCALATED çıkmaz sokak kapanır | **T113** | M–L |
| | WP6 | Steam dispute checker'ları (trade-hold + MA) + auto-resolve doğrula | DELIVERY/WRONG_ITEM otomatik çözülür | T111/T113 | M |
| **P3 — Operasyon** | WP7 | Outage/maintenance: bulk-freeze çağıran + toggle push | Platform dondurulabilir | **T114** | M |
| | WP8 | Admin bildirim/alert + audit tamamlama *(migration)* | Admin olayları görür/aksiyon alır | T113 | M |
| | WP9 | Realtime tamlık: Steam push + FE admin abonelik | Canlı durum/admin event'leri | — | M |
| | WP10 | Tron dayanıklılık: 429 failover + ikincil key + dedup | Para-katmanı dayanıklı (MVP, 08 §3.6/§3.7) | — | M |
| **P4 — Kullanıcı/FE** | WP11 | Auth UI wire-up + ToS reprompt + brute-force lock | Kullanıcı UI'dan gerçekten login olur | T107 (browser) | M |
| | WP12 | Kullanıcı kenar durumları (OPEN_LINK 409 *bağımsız*, refund override) | Eşzamanlılık/UX doğruluğu | T108/T110 | M |
| | WP13 | FE tamlık: yasal sayfalar + polish + enum sync | /privacy /terms /support + UX | T113 | M–L |
| **P5 — Config/altyapı** | WP14 | Settings runtime propagasyon + 21 ayar seed/runbook | Ayar değişimi yansır, prod açılır | T114 | M |
| | WP15 | Reputation aggregation tetik | İtibar skoru güncel | — | M |
| | WP16 | Monitoring/health probe + uptime heartbeat (MVP) | Outage recovery + gözlemlenebilirlik | T114 | M |
| **P6 — Borç temizliği** | WP17 | Doc/spec/i18n mutabakat | Doküman↔kod hizalı | gate | M |
| | WP18 | Test/CI sertleştirme (FE runner, prettier CI, npm audit) | Regresyon güvenliği | gate | M–L |

**Bağımlılıklar:** WP1 her şeyin temeli (ilk). **WP5 → WP1 + WP2** (satıcı-lehine release WP1'in `Complete`→payout yolunu, alıcı-lehine refund WP2'nin `BUYER_REFUND`'ünü kullanır). WP4a accept-gate ucuz/yüksek-değer (erken). WP12'deki **OPEN_LINK 409 fix bağımsız** — P1'de WP1 ile inebilir. WP18 sürekli/son.
**Migration taşıyan paketler:** **WP3** (SWEEP için type-bağımlı CHECK constraint) ve **WP8** (`Notification.FlagId` kolonu) — gate-check yeni migration dosyası bekler.

---

## 2. İş paketleri — detay

### WP1 — Escrow tamamlama: satıcı payout + `COMPLETED`
> **Durum: ⏳ Devam ediyor (2026-06-14)** — `task/WP1-escrow-completion-payout`, doğrulama bekliyor. Uygulama: `SellerPayoutQueueJob` (producer) + `PayoutCompletedEvent`/`PayoutCompletedConsumer` (completion) + `blockchain.payout_gas_fee_estimate_usdt` (0.50) + 07 §7.5 payout DTO. Rapor: [`TASK_REPORTS/WP1_REPORT.md`](TASK_REPORTS/WP1_REPORT.md).

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
**Backlog:** SWEEP-dispatcher
**Kanıt:** `BlockchainTransactionType.cs:15-22` yorumu: "sweep dispatcher (PaymentReceivedEvent consumer) T-future; 0 SWEEP satırı → mutabakat sadece-çıkış"; tek `PaymentReceivedEvent` consumer'ı SignalR push (`PaymentReceivedRealtimeConsumer.cs`).
**İş:** `PaymentReceivedEvent` consumer'ı SWEEP (depozit→hot-wallet) satırı ekler; `OutgoingTransferDispatchJob.OutboundTypes`'a `SWEEP` ekle; kaynak-adres çözümü (`OutgoingTransferDispatchJob.cs:114-147`, `row.Type != SELLER_PAYOUT` dalı) SWEEP'i doğru ele alır.
**⚠ Migration:** SWEEP satırları `CK_BlockchainTransactions_Type_Outbound`'u (`BlockchainTransactionConfiguration.cs:41-43`) ihlal eder (constraint SWEEP'i kapsamıyor; depozit→hot-wallet `PaymentAddressId` ister) → **type-bağımlı CHECK constraint migration'ı gerekir.**
**Efor:** M · **Açar:** T110 (mutabakat assertion'ları)

### WP4a — Fraud accept-gate + canlı fiyat (wiring)
**Backlog:** fraud-acceptance-gate (accept-gate kısmı) · T81-PriceConsumerWireup
**Kanıt:** `IAccountFlagChecker` yalnız `TransactionEligibilityService.cs:56`'da (advisory), accept yolunda yok → flag'li hesap kabul edebiliyor. `IMarketPriceProvider`=`NullMarketPriceProvider` (`TransactionsModule.cs:76`). **Steam Market stack ZATEN var:** `ISteamMarketPriceClient` + `SteamMarketRateLimiter` (429/RegisterRetryAfter) + `SteamMarketPriceParser` + `ItemPriceCache` entity (T81 migration) + Fraud `PriceService` (cache-first, TTL).
**İş:** Accept yoluna `IAccountFlagChecker` gate ekle; **fiyat = SADECE WIRING:** Transactions `IMarketPriceProvider` → mevcut Fraud `PriceService`/`ISteamMarketPriceClient` köprüsü + `classId/instanceId`→`marketHashName` çözümü. **T81'i yeniden yazma** (harici API + cache + rate-limit hazır).
**Efor:** M · **Açar:** T111

### WP4b — Fraud kapsam tamlığı (retro-scan + FLAGGED yolları)
**Backlog:** T56-MultiAccountRetroScan · T54-FlaggedApproveNoTimeoutJob · flagged-allocation-detail (backend) · T54-FraudNoteNoMaxLength · fraud-acceptance-gate (sinyal üreticileri/background scan kısmı)
**Kanıt:** `MultiAccountDetector` yalnız `WalletAddressService.cs:144` (wallet-update); FLAGGED-approve (FLAGGED→CREATED) per-tx accept-timeout job kaydetmiyor (poll'a güveniyor); payment-address allocate yalnız CREATED'de (FLAGGED-approval atlanıyor); fraud-note max-length yok.
**İş:** Periyodik MultiAccount retro-scan Hangfire job; FLAGGED-approve per-tx timeout job; FLAGGED-approval'da payment-address allocate; fraud-note max-length validasyonu.
**Efor:** M · **Açar:** T111

### WP5 — Dispute çözüm (admin)
**Backlog:** T58-AdminDisputeQueue · T58-canDisputeEnvelopeBit · T58-ActiveDisputeExistsUnreachable · emergency-hold-callers (dispute-queue surfacing)
**Kanıt:** `GET /admin/disputes` yok; `IDisputeService` yalnız buyer-facing; `Dispute.AdminId/AdminNote` (`Dispute.cs:16,23`) hiç atanmıyor; ESCALATED→CLOSED yolu yok. DELIVERY/WRONG_ITEM dispute yalnız `ITEM_DELIVERED`'da açılır (`DisputeService.cs:73-81`) → satıcı-lehine resolve `Complete`→`COMPLETED` (WP1), alıcı-lehine resolve `BUYER_REFUND` (WP2) gerektirir.
**İş:** `GET /admin/disputes` (ESCALATED liste) + `POST /admin/disputes/{id}/resolve` → `AdminResolveAsync` (CLOSED + AdminId/AdminNote/ResolvedAt + item-release **[WP1]** veya iade **[WP2]** çıktısı + audit + notify). Per-type `canDispute`.
**🔒 Scope-fence (10_MVP_SCOPE §3.6/§6):** YALNIZ minimal çıkmaz-sokak-açıcı (ESCALATED→CLOSED + release/refund + audit + notify). SLA/çok-adımlı state/atama/şablon-kural **post-MVP** — gold-plating yok.
**Efor:** M–L · **Açar:** T113 · **Bağımlı:** **WP1 + WP2**

### WP6 — Steam dispute checker'ları + auto-resolve doğrulama
**Backlog:** steam-sidecar-stubs (trade-hold + MA checker) · TradeOfferMonitor-hotadd-T69
**Kanıt:** DELIVERY/WRONG_ITEM auto-checker'lar `ISteamInventoryReader` tüketir — bu **zaten gerçek** (`SidecarSteamInventoryReader`, T67; `SteamModule.cs:56` DI-swap). Gerçek stub'lar yalnız: `StubTradeHoldChecker` (`UsersModule.cs:124`, Available=true) + `StubMobileAuthenticatorCheck` (`SteamAuthenticationModule.cs:156`).
**İş:** Gerçek sidecar-destekli trade-hold + MA checker; mevcut `SidecarSteamInventoryReader` auto-resolve yolunu **doğrula/sertleştir** (sıfırdan kurma DEĞİL); `TradeOfferMonitor` hot-add re-attach (yalnız dinamik pool — statik pool'da sorun yok). Not: bu checker'lar WP1 teslim-confirmation ve WP4a creation-check yollarına da dokunur.
**Efor:** M · **Açar:** T111/T113 dispute kapsamı

### WP7 — Outage/maintenance
**Backlog:** T50-OutageFreezeCallers · maintenance-toggle · suspend-signalr-spec (force-restrict)
**Kanıt:** `FreezeManyAsync`/`ResumeManyAsync` (`TimeoutFreezeService.cs:106`) **0 prod çağıran** (yalnız test). `PublishMaintenanceStatusChangedAsync` (`SignalRNotificationRealtimePublisher.cs:63`) **0 çağıran**; `PUT /admin/settings` push tetiklemez.
**İş:** Admin `POST /admin/maintenance/freeze`+`/resume` → `FreezeManyAsync(reason)`; STEAM_OUTAGE/BLOCKCHAIN_DEGRADATION auto-detect; `platform.maintenance.*` update'inde cache-evict + `PublishMaintenanceStatusChangedAsync`.
**Efor:** M · **Açar:** T114

### WP8 — Admin bildirim/alert + audit tamamlama
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
**Backlog:** tron-resilience · energy-gas-token-config (HD cache, gas config)
**Kapsam notu:** **MVP** — `08_INTEGRATION_SPEC.md:597` "MVP | TronGrid (primary) + ikinci TronGrid API key (rate limit fallback)"; §3.6/§3.7 5xx 3×-retry + 429 exponential backoff. (Multi-sweeper/Redis backplane bu DEĞİL → §3.)
**İş:** TronGrid 429 + `TRON_API_KEY_SECONDARY` failover + retry; event_index dedup (txid+index); HD address cache; gas fee config'lenebilir.
**Efor:** M

### WP11 — Auth UI wire-up
**Backlog:** T87-K1 · T30-TosVersionReprompt · misc-user-features (brute-force lock) · T40-PermClaimCache
**Kanıt:** Callback yalnız `?status` okur (`callback/page.tsx:48`), `POST /auth/refresh` çağırmaz → `localStorage["access_token"]` hiç yazılmaz → `isAuthenticated` daima false. ToS-accept/authenticator UI-only. Backend endpoint'leri hazır.
**İş:** Callback→`/auth/refresh`→token store; ToS-accept→`POST /auth/tos/accept`; authenticator→`POST /auth/check-authenticator`; 401→refresh interceptor; ToS-versiyon reprompt; login brute-force lock; per-user permission TTL cache.
**Efor:** M · **Açar:** T107 (full-stack/browser E2E ise)

### WP12 — Kullanıcı kenar durumları
**Backlog:** T46-OpenLinkConcurrentAcceptRace · misc-user-features (per-tx refund override, trade-offer URL DTO, delete atomicity) · timeout-warning-setting
**Kanıt:** Eşzamanlı OPEN_LINK accept'te race-loser `DbUpdateConcurrencyException`→HTTP 500 (`TransactionAcceptanceService.cs:100-147`) — 409 ALREADY_ACCEPTED olmalı.
**İş:** OPEN_LINK accept race → 409 (**bağımsız fix, WP2/WP3'e bağlı değil — P1'de inebilir**); per-tx refund override (WP2'ye bağlı); trade-offer URL DTO; delete atomicity; `DefaultTimeoutWarningPercent`/`accept_timeout_minutes` config'lenebilir.
**Efor:** M

### WP13 — FE tamlık
**Backlog:** static-routes-pages · admin-table-sort · url-state-sync · profile-prefill-image · dispute-detail-polish · FE-permission-guard · FE-enums-ts-lag · T97-NEXT_LOCALE-cookie · T97-formatAmount-deprecated-alias · flagged-allocation-detail (tx-detail sub-DTO)
**Kanıt:** `/privacy` placeholder, `/terms` 404, `/support` undefined (**MVP yasal gereklilik**, 10_MVP_SCOPE §2.15); `types/enums.ts` backend'in gerisinde.
**İş:** Yasal sayfalar (/privacy /terms /support) + login→dashboard redirect; admin tablo tıkla-sırala (API hazır); url-state-sync; profil pre-fill + next/image; dispute-detail polish; client permission guard; enum sync; NEXT_LOCALE cookie; deprecated alias temizliği.
**Efor:** M–L

### WP14 — Settings propagasyon + 21 ayar
**Backlog:** setting-sidecar-propagation · T55-DormantThreshold (21 zorunlu) · timeout-warning-setting
**Kanıt:** `SystemSettingsService.UpdateAsync` (`:68`) yalnız DB+audit; sidecar env-only boot; cron `StartAsync`'te register; `SettingsBootstrapTests.cs:92` "21 mandatory rows".
**İş:** cron key update'inde job re-register; sidecar `blockchain.*`/cadence runtime push/pull; 21 zorunlu ayar için seed-default veya **deploy runbook** (env var listesi belgelenir).
**Efor:** M

### WP15 — Reputation aggregation tetik
**Backlog:** reputation-aggregator-trigger
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
- **Migration taşıyan paketler:** WP3 (SWEEP CHECK constraint) · WP8 (`Notification.FlagId`) — gate-check yeni migration dosyası bekler.
- **Önerilen ilk hamle:** WP1 (escrow tamamlama) — ürünün tamamlanamadığı tek nokta; geri kalan her şey bunun üstüne oturur. OPEN_LINK 409 fix'i (WP12) ucuz/bağımsız, WP1 ile paralel inebilir.
- Bu plan ilerledikçe güncellenir; her WP biten için backlog satırı **✓ Çözüldü** işaretlenir.
