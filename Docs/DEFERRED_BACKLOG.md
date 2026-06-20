# Ertelenmiş İşler Backlog'u (Deferred Work Backlog)

> **Amaç:** Proje boyunca bilinçli olarak ertelenen / sonraya bırakılan tüm somut işlerin tek izlenebilir listesi. Tamamlanan işler için tek doğru kaynak [`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md); bu dosya yalnızca **ertelenen** kalemleri toplar.
>
> **Oluşturulma:** 2026-06-13 · iki-turlu çok-ajanlı kaynak taraması (status doc + 115 task report + repo/auto memory + backend/frontend kod + sidecar + discovery docs + gate-check/audit/GPT-review raporları). Her kalem kod veya rapor kanıtıyla doğrulandı.
>
> **Durum:** ~90 aktif ertelenmiş kalem · **F5 Gate Check'i bloklayan: 0**. Bu dosya bir kalem ele alındıkça güncellenmelidir (satırı **✓ Çözüldü** işaretle veya kaldır).
>
> **Sıralama/sahiplik:** F6 öncesi MVP-içi kalemler [`PRE_F6_PLAN.md`](PRE_F6_PLAN.md)'de 19 iş paketine (WP1–WP18) bağlandı. Aşağıdaki bazı satırlar 2026-06-14 kod taramasıyla **kısmen stale** bulundu ve düzeltildi (emergency-hold, blockchain-monitor, item-refund, steam-sidecar, T55).

## Lejant

- **Öncelik:** 🔴 high · 🟡 medium · ⚪ low
- **Tip:** `task` (planlı görev) · `backend-gap` (kod boşluğu) · `k-note` (task K-notu) · `code-todo` · `doc-drift` (doküman ↔ kod uyumsuzluğu) · `test-gap` · `bypass`
- **Bloklar mı:** kalemin engellediği şey; "—" = hiçbir şeyi bloklamıyor.

---

## 🔝 Öne Çıkanlar (dikkat gerektiren)

| Önc. | ID | Özet | Bloklar mı |
|---|---|---|---|
| ✅ | T58-AdminDisputeQueue | **ÇÖZÜLDÜ → WP5** — `GET /admin/disputes` (AD27) + resolve (AD29) + ESCALATED→RESOLVED_FOR_* | — |
| ✅ | T55-DormantThreshold | **ÇÖZÜLDÜ → WP14:** `dormant_account_value_threshold` dahil **19 zorunlu SystemSetting** (plan "21" stale — WP4a `price_deviation_threshold` + WP12 `timeout_warning_ratio` seed-default ile düştü) `Docs/DEPLOY_RUNBOOK.md §A`'da `SKINORA_SETTING_*` env listesi olarak belgelendi + `.env.example`'a eklendi. Owner kararı: seed-default DEĞİL → fail-fast bilinçli güvenlik korundu (06 §8.9 iş-kritik değerler). | — |
| ✅ | T69-DispatchCaller | **ÇÖZÜLDÜ → T106a** (Escrow Trade-Offer Dispatch Engine): `SelectAsync` çağrılıyor + `EscrowBotId` persist + `ActiveEscrowCount` ITEM_ESCROWED'da artar + escrow/delivery/refund dispatch | — |
| ✅ | T69-BotRecoveryStateMachine | **ÇÖZÜLDÜ → T103b-2** — `BotRecoveryItem` recovery domaini + AD10 canlı `RestrictionReason`/`FailoverStatus`/`RecoveryTransactionCount` | — |
| ✅ | T103b-2/-3 | **ÇÖZÜLDÜ → T103b-2 (birleşik)** — recovery queue domain + MANAGE_STEAM_RECOVERY enforcement + emanet item listesi + otomatik EMERGENCY_HOLD | — |
| 🟡 | SWEEP-dispatcher | SWEEP satırı üreten consumer yok → hot-wallet mutabakatı tek-taraflı | Hot-wallet mutabakat doğruluğu |
| 🟡 | T81-PriceConsumerWireup | `NullMarketPriceProvider` → PRICE_DEVIATION fraud kuralı inert | PRICE_DEVIATION kuralı |
| 🟡 | StubPayoutVerifier | Üretim payout doğrulayıcı yok (fail-closed, manuel admin) | Otomatik on-chain payout doğrulama |
| ✅ | steam-sidecar-stubs | **ÇÖZÜLDÜ → WP6** — sidecar `GET /api/trade-hold/:steamId` (`GetTradeHoldDurations`) + `SidecarTradeHoldChecker` (U17) + `SidecarMobileAuthenticatorCheck` (A7); envanter reader zaten gerçekti | — |
| 🟡 | item-refund-consumers | Yalnız `BUYER_REFUND` kopuk (diğer 4 iade inline/T106a bağlı) → WP2 | Alıcı iadesi (delivery-timeout + admin-cancel) |
| 🟡 | T50-OutageFreezeCallers | Outage/degradation bulk-freeze motoru var, çağıran yok | Outage dayanıklılığı |
| ✅ | T56-MultiAccountRetroScan | **ÇÖZÜLDÜ → WP4b** — günlük `MultiAccountRetroScanJob` cüzdanlı aktif kullanıcıları retroaktif tarar (`IMultiAccountDetector` yeniden çağrılır) | — |
| 🟡 | T61-SteamTransitionRealtimePush | Steam pipeline geçişlerinde SignalR push yok | — (T96 refetch maskeliyor) |
| 🟡 | T38-AdminFlagAlert-FlagId | `Notification` entity'de `FlagId` yok → admin flag-link bozuk | Admin flag inbox linki |
| ✅ | T30-TosVersionReprompt | **ÇÖZÜLDÜ → WP11** — CurrentUserDto += `tosAcceptedVersion`, `tos/accept` versiyon-upgrade'e izin verir (409 yalnız aynı versiyonda), FE `TosRepromptGate` versiyon uyuşmazlığında re-prompt | — |
| ✅ | T87-K1 | **ÇÖZÜLDÜ → WP11** — callback `/auth/refresh`→token store + `acceptTos` wire-up + 401 refresh interceptor; MA recheck /auth/me ile (A7 trade-URL akışına ait) | — |
| 🟡 | FE-admin-signalr-subscription | `RealtimeProvider.tsx:40-43` üç admin event'ini abone etmiyor | Canlı admin event'leri |
| ✅ | TradeOfferMonitor-hotadd-T69 | **ÇÖZÜLDÜ → WP6 (resolved-by-design)** — statik pool (`BotManager` dinamik-add yok); idempotent `attachToSession` hook'u T69 dinamik pool için hazır + test edilmiş | — (statik pool'da sorun yok) |
| 🟡 | T33-SuccessRate-FractionVsPercent | `successfulTransactionRate` fraction (06) vs percent (07) | FE entegrasyonu öncesi karar |
| 🟡 | T107 | E2E happy-path testi (başlamadı) | — |

---

## 1. T69 — Bot health / failover / recovery pipeline

| Önc. | ID | Açıklama | Tip | Hedef | Kaynak |
|---|---|---|---|---|---|
| ✅ | T69-DispatchCaller | **ÇÖZÜLDÜ → T106a** (Escrow Trade-Offer Dispatch Engine): `SelectAsync` çağrılıyor + `EscrowBotId` persist + `ActiveEscrowCount` ITEM_ESCROWED'da artar + sidecar `selectBot(botAccountName)` hint'i onurlandırır (round-robin yalnız fallback) | done | — | T106a |
| ✅ | T69-BotRecoveryStateMachine | **ÇÖZÜLDÜ → T103b-2:** `BotRecoveryItem` domaini + AD10 canlı alanlar (`AdminSteamBotQueryService` türetir) | done | — | T103b-2 |
| ⚪ | T69-K4 | `AdminBotStatusChanged` `Clients.All` yayını; admin-only group scope daraltma | k-note | T-future | T69 K4 |
| ✅ | FE-RecoveryQueue-T69 | **ÇÖZÜLDÜ → T103b-2:** `RecoveryQueuePanel` canlı AD25 satırları + Manual Recovery/Resolve/Not aksiyonları (AD26) | done | — | T103b-2 |
| ✅ | FE-SteamAccountCard-EscrowList-T69 | **ÇÖZÜLDÜ → T103b-2:** emanet item'lar per-bot recovery kuyruğunda listelenir | done | — | T103b-2 |
| ✅ | FE-admin-ts-RecoveryFields-T69 | **ÇÖZÜLDÜ → T103b-2:** AD10 alanları canlı; failover banner `RESTRICTED_NEW_TXN_DIVERTED` ile görünür | done | — | T103b-2 |
| ⚪ | T68-K1 | Bot lifecycle event → admin notification + `BOT_SESSION_FAILED` AuditAction; şu an yalnız Warning log | backend-gap | admin notification track | T68 K1 |
| ⚪ | T64-BotWebhookHandler | `bot.session_failed`/`removed_from_pool` backend handler (T68 log-only ötesi) | backend-gap | T68/T69 | T64 |
| ✅ | TradeOfferMonitor-hotadd-T69 🆕 | **ÇÖZÜLDÜ → WP6 (resolved-by-design)** — statik pool, dinamik-add yolu yok; idempotent `attachToSession` hook'u T69 için hazır + test edilmiş; doc-comment WP6 doğrulamasıyla güncellendi | k-note | T69 | `sidecar-steam/src/trade/TradeOfferMonitor.ts` |

## 2. T103b — Steam hesapları backend tamamlama (S18)

| Önc. | ID | Açıklama | Tip | Hedef | Kaynak |
|---|---|---|---|---|---|
| ✅ | T103b | **ÇÖZÜLDÜ → T103b-2 (birleşik impl, 2026-06-13):** emanet item listesi + Recovery Queue satır verisi + `MANAGE_STEAM_RECOVERY` enforcement + otomatik EMERGENCY_HOLD | done | — | T103b-2_REPORT |
| ✅ | T103-K4 | **ÇÖZÜLDÜ → WP17:** ölü TR-sabit `warningMessage` alanı AD10'dan kaldırıldı (FE banner zaten `status`'ten client-derive) | backend-gap | — | T103 K4 |

## 3. F6 — Uçtan uca testler (T107–T114)

| Önc. | ID | Açıklama | Tip | Kaynak |
|---|---|---|---|---|
| 🟡 | T107 | E2E — Happy path (tam escrow akışı) | task | IMPLEMENTATION_STATUS F6 |
| ⚪ | T108 | E2E — İptal senaryoları | task | F6 |
| ⚪ | T109 | E2E — Timeout senaryoları | task | F6 |
| ⚪ | T110 | E2E — Ödeme edge case'ler | task | F6 |
| ⚪ | T111 | E2E — Fraud/flag senaryoları (PRICE_DEVIATION dahil) | task | F6 |
| ⚪ | T112 | E2E — Emergency hold | task | F6 |
| ⚪ | T113 | E2E — Admin akışları | task | F6 |
| ⚪ | T114 | E2E — Downtime ve bakım senaryoları | task | F6 |
| ✅ | T87-K1 | **ÇÖZÜLDÜ → WP11** — callback refresh→token store + ToS-accept wire-up + 401 refresh interceptor; check-authenticator owner-kararıyla /auth/me recheck'ine bağlandı (A7 trade-URL/U17 akışına ait, login'de değil) | k-note | T87 K1-K3 / T85 K1 |

## 4. T-future — Backend orkestrasyon (caller/consumer wire-up)

### Orta öncelik

| Önc. | ID | Açıklama | Tip | Kaynak |
|---|---|---|---|---|
| ✅ | T58-AdminDisputeQueue 🆕 | **ÇÖZÜLDÜ → WP5** — `AdminDisputeService` (AD27/28/29) + `RESOLVED_FOR_*`/`REFUNDED` + audit/notify; FE `/admin/disputes` | backend-gap | `T58_REPORT.md:178` |
| 🟡 | SWEEP-dispatcher | `PaymentReceivedEvent` consumer'ı SWEEP ledger satırı üretmiyor; `OutgoingTransferDispatchJob` SWEEP picker yok | backend-gap | T73/T76/T77 K |
| 🟡 | T81-PriceConsumerWireup | `IMarketPriceProvider`=`NullMarketPriceProvider`; `MarketPriceAtCreation` set + PRICE_DEVIATION FraudFlag yok | backend-gap | T81 K1, `NullMarketPriceProvider` |
| 🟡 | StubPayoutVerifier | `IPayoutVerifier`=stub (her zaman `UnableToVerify`→manuel admin) | backend-gap | T60 K1, `StubPayoutVerifier` |
| ✅ | steam-sidecar-stubs | **ÇÖZÜLDÜ → WP6** — sidecar `GET /api/trade-hold/:steamId` (`GetTradeHoldDurations`, 08 §2.2) + paylaşılan `ISteamTradeHoldProbe`/`HttpSteamTradeHoldClient` + `SidecarTradeHoldChecker` (U17) + `SidecarMobileAuthenticatorCheck` (A7); fail-closed; envanter reader zaten gerçekti (`SidecarSteamInventoryReader`) | backend-gap | T35/T31/T58 K |
| 🟡 | item-refund-consumers | **Yalnız `BUYER_REFUND` kopuk** (delivery-timeout + admin-cancel `PaymentRefundToBuyerRequestedEvent` yayınlar, satır-üreten consumer yok). Wrong-token/late-payment/excess/incorrect inline `QueueRefundIntent` üretir; item-iade T106a bağlı → **WP2** | backend-gap | T49/T51/T71 K |
| 🟡 | T50-OutageFreezeCallers 🆕 | `STEAM_OUTAGE`/`BLOCKCHAIN_DEGRADATION` `FreezeManyAsync`/`ResumeManyAsync` çağıransız (02 §3.3 auto-detect + admin manual) | backend-gap | `T50_REPORT.md:124-125` |
| ✅ | T56-MultiAccountRetroScan 🆕 | **ÇÖZÜLDÜ → WP4b** — günlük retro-scan Hangfire job (`MultiAccountRetroScanJob`, `AutoUnsuspendJob` deseni) | backend-gap | `T56_REPORT.md:150` |
| 🟡 | T61-SteamTransitionRealtimePush 🆕 | Steam pipeline geçişleri için `TransactionStatusChanged` push yok (her biri T67 event'iyle RealtimeConsumer ister) | backend-gap | `T61_REPORT.md:146` (K2) |
| 🟡 | T38-AdminFlagAlert-FlagId 🆕 | `Notification` entity'de `FlagId` kolonu yok (yalnız `TransactionId`); admin flag-link reinterpret/extend gerek | backend-gap | `T38_REPORT.md:20`, `NotificationTargetMapper.cs:25` |
| ✅ | T30-TosVersionReprompt 🆕 | **ÇÖZÜLDÜ → WP11** — `tosAcceptedVersion` /auth/me'de sunulur + `tos/accept` versiyon-upgrade (409 yalnız aynı versiyon) + FE `TosRepromptGate` | backend-gap | `T30_REPORT.md:155` |

### Düşük öncelik

| ID | Açıklama | Kaynak |
|---|---|---|
| payout-completed-consumer | `PayoutCompletedEvent`→COMPLETED geçişi yok (T73 yalnız finality flush) | T73 K4 |
| payout-retry-consumer | `RETRY_SCHEDULED` set ediliyor ama broadcast retry consumer'ı yok | T60 K2/K3 |
| calculator-caller-wiring | `CalculateRefund`/`SellerPayout`/`IRefundDecisionService` tüketilmiyor; gas/refund ratios deferred; AD7 gas split=0 | T52/T53/T63 K |
| reputation-aggregator-trigger | **✅ WP15** — COMPLETED/CANCELLED_TIMEOUT/Steam-cancel recompute+cooldown bağlandı; ön-koşul **TransactionHistory yazımı** (06 §3.6, hiç yazılmıyordu) tüm geçişlere eklendi (timeout sorumluluk-atfı buna bağımlıydı, sessizce kopuktu). | T43/T44/T68 K |
| blockchain-monitor-consumers | `payment-confirmed`→PAYMENT_RECEIVED **bağlı** (finality webhook); kalan: mempool `PaymentDetected` consumer (by-design opsiyonel) + DROPPED metrik → WP16 | T61/T71/T72 K |
| flagged-allocation-detail | **payment-address ✅ ÇÖZÜLDÜ → WP4b** (`FraudFlagService.ApproveAsync` post-commit eager `AllocateAsync`, best-effort); **tx-detail payment/payout/refund/dispute alt-DTO'ları null kısmı → WP13** | T70/T46 K |
| emergency-hold-callers | **Bulk/per-user hold/release/cancel bağlı** (T59/T100/T103b-2, stale); RowVersion guard mevcut; post-payment refund tetik **✅ WP2**, dispute-queue surfacing **✅ WP5** (AD27 kuyruğu). Kalan kalem yok. | T50/T51/T58 K |
| fraud-acceptance-gate | **✅ ÇÖZÜLDÜ** — accept-gate → WP4a; background scan (retro-scan) + note max-length → WP4b. (Sinyal üreticileri PRICE_DEVIATION T45/multi-account T56 zaten mevcut) | T54/T56 K |
| energy-gas-token-config | **Kısmen ✅ → WP10:** gas fee config'lenebilir (`TRANSFER_FEE_LIMIT_SUN` — hardcoded 100 TRX kaldırıldı) + HD address cache (per-index `derive` memoization, private-key cache'lenmez) çözüldü. **By-design/kapsam-dışı:** USDC/USDT 1:1 + hardcoded 6-decimal (by-design TRC-20, §3); tek-sweeper/T74 multi-sweeper (post-MVP ölçek, §3); 20-blok finality **zaten var** (`minConfirmations=20`, stale not). | T72-T76 K |
| setting-sidecar-propagation | **WP14 ✅ ÇÖZÜLDÜ:** (1) **cron** (`reconciliation.schedule_cron`, `hot_wallet.monitor_cron`) admin değişiminde Hangfire job **restart'sız re-register** olur (`ISettingChangePropagator`→`CronSettingChangePropagator`→registrar `ICronJobReconfigurer.Reconfigure`; geçersiz cron → 400 validator). (2) **sidecar cadence/sweep** (`monitoring_post_cancel_*`, `blockchain.sweep_*`) owner kararı = **env parity + runbook** (runtime push/pull post-MVP, T74 K1/T96): `Docs/DEPLOY_RUNBOOK.md §D` + `.env.example` — sidecar env otoriter, değişim sidecar restart gerektirir, backend SystemSetting kopyası admin-görünür. Backend gas/retry zaten her run'da taze okunuyor (propagasyon gerektirmiyor). | T74/T75/T76/T77 K |
| admin-alert-consumers | `RefundBlockedAdminAlert`/`TransferDispatchFailed`/stranded-delegation/STOPPED/spam-token/payout-issue alert kanal consumer'ları yok | T53/T60/T72-T77 K |
| misc-monitoring-probes | Steam/Telegram health probe, Redis webhook idempotency, in-app dispatch, timeout reschedule, ItemEscrowed publish, DROPPED metric | T64/T68/T78/T79 K |
| signalr-scaling | SignalR in-memory (multi-instance Redis backplane tek-satır DI); CountdownSync/handler/group-failure obs. | T61/T62/T96 K |
| maintenance-toggle | `PublishMaintenanceStatusChangedAsync` wired ama hiç tetiklenmiyor; admin maintenance-toggle endpoint + setting yok | T62/T84 K |
| uptime-cache-scaleout | `platformUptimePercent` config sabiti (heartbeat tablo/job yok); `IMemoryCache` (Redis scale-out yok); SET invalidation hook yok | T63a/T86 K |
| tron-resilience | **✅ ÇÖZÜLDÜ → WP10** — TronGrid 429/403 → ikincil `TRON_API_KEY` anında failover + sınırlı poll-dostu backoff (okuma yolu, `TronGridClient`); **event_index dedup** txid+gerçek-on-chain-log-index'e yükseltildi (06 §3.8 `(TxHash,EventIndex)` UNIQUE + migration; sidecar `gettransactioninfobyid` `log[]`'tan çözer — trc20 list endpoint event_index vermiyor). Büyüme backup node (Ankr/GetBlock) MVP-dışı (§3). | T71/T72 K |
| sanctions-expansion | `SanctionedAddress` MANUAL-only (OFAC/EU/UN feed auto-sync yok); Network TRC-20 sabit; AD22 reason UI | T82/T34 K |
| vpn-fraud | `VpnDetection:Enabled` default off; fraud-modül entegrasyonu + datacenter ASN / commercial VPN listeleri | T83 K |
| misc-user-features | **WP12 ✅ ÇÖZÜLDÜ:** per-tx refund override (accept artık profil `DefaultRefundAddress`/cooldown'ı mutate etmiyor — snapshot-only, 02 §12.2 + 04 §7.3 "profil adresi etkilenmez"); trade-offer URL DTO (`steamTradeOfferUrl` cross-module port, 04 §7.3); delete atomicity (`AccountLifecycleService.DeleteAsync` 3 adım `BeginTransactionAsync` ile atomik); OPEN_LINK race (→ T46 satırı). brute-force lock ✅ WP11. **Kalan:** multi-account user UI | T90/T29/T36/T46 K |
| timeout-warning-setting | **WP12 ✅ ÇÖZÜLDÜ:** kopyalı `DefaultTimeoutWarningPercent`=75 const'ları kaldırıldı → read-path (Detail+List) mevcut `timeout_warning_ratio` SystemSetting'ini okur (oran×100; seeded default 0.75 → mandatory değil); `accept_timeout_minutes` env-mandatory **by-design doğrulandı** (06 §8.9 fail-fast, "—" default) | T83a/T45 K |
| T61-AdminHubJoinBypass 🆕 | `TransactionsHub.JoinTransaction` admin bypass yok (yalnız seller/buyer) | `T61_REPORT.md:147` (K3) |
| T54-FlaggedApproveNoTimeoutJob 🆕 | **✅ ÇÖZÜLDÜ-by-design → WP4b** — 05 §4.4 + 06 §3.5:650: accept-deadline'lar **bilinçli olarak poller-driven** (yalnız ITEM_ESCROWED per-tx job alır); `DeadlineScannerJob` `ApproveAsync`'in setlediği `AcceptDeadline`'ı zaten enforce ediyor (regresyon testi eklendi). Yeni per-tx job spec'i ihlal ederdi → kurulmadı | `T54_REPORT.md:234` |
| T54-FraudNoteNoMaxLength 🆕 | **✅ ÇÖZÜLDÜ → WP4b** — `ApproveAsync`/`RejectAsync` 2000 char (kolon genişliği) validasyonu → 400 `VALIDATION_ERROR`; rapordaki "1000" stale | `T54_REPORT.md:194,219` |
| T58-canDisputeEnvelopeBit 🆕 | **✅ ÇÖZÜLDÜ → WP5** — `availableActions.disputableTypes: DisputeType[]` eklendi (per-type), `canDispute` korunur (07 §7.5) | `T58_REPORT.md:203` |
| T46-OpenLinkConcurrentAcceptRace 🆕 | **WP12 ✅ ÇÖZÜLDÜ:** `AcceptAsync` SaveChanges `catch(DbUpdateConcurrencyException)` → no-tracking status re-query → ACCEPTED ise 409 ALREADY_ACCEPTED, başka state ise re-throw (maskeleme yok). RowVersion optimistic concurrency zaten mevcuttu | `T46_REPORT.md:145` |
| T40-PermClaimCache 🆕 | **✅ ÇÖZÜLDÜ-by-design → WP11** — owner kararı: T40'ın "dinamiklik > performans" kararı korunur, cache **EKLENMEZ** (her login/refresh DB lookup; perf darboğazı kanıtı yok, ≤2dk staleness istenmedi) | `T40_REPORT.md:113` |
| discord-interactions-userinstall 🆕 | Discord slash commands/interactions (Ed25519 webhook) + user-install; şu an yalnız Bot DM + OAuth2 | `MEMORY_ARCHIVE.md:178` (T80 K6-K8) |

## 5. T-future — Frontend polish / enhancements

| Önc. | ID | Açıklama | Kaynak |
|---|---|---|---|
| 🟡 | FE-admin-signalr-subscription | `RealtimeProvider.tsx:40-43` üç admin event'ini (`AdminBotStatusChanged`/`AdminReconciliationMismatch`/`AdminHotWalletThresholdBreached`) abone etmiyor | T96 K2 |
| ⚪ | admin-table-sort | Admin tablolarında tıkla-sırala başlık yok (`sortBy`/`sortOrder` API var, UI göndermiyor) | T101 K10 / T106 K1 |
| ⚪ | url-state-sync | Tx-list tab/page `?tab=&page=` senkron değil; wizard hard-refresh resetler; dashboard deep-link consume | T88/T89/T99 K |
| ⚪ | signalr-toast-countdown | C09 toast realtime'da boş; verification countdown; email cooldown Retry-After; LanguageSelector drift | T96/T94/T97 K |
| ⚪ | profile-prefill-image | Seller payout adresi profil pre-fill; `next/image` migration + Steam-CDN whitelist (`<img>`+ESLint disable) | T89/T93 K |
| ⚪ | dispute-detail-polish | Seller payout-issue UI, autoCheck refresh, closed-dispute audit, asset-id, drawer auto-close, Tronscan URL sabit | T92/T90/T98/T101 K |
| ⚪ | FE-permission-guard | Admin sayfalarında client-side permission/route guard yok (backend enforce authoritative) — tekrarlayan | T85/T88/T99/T103/T104/T105/T106 K |
| ⚪ | static-routes-pages | `/privacy` (placeholder), `/terms` (404), `/support` (undefined), login→dashboard redirect | T85/T86/T87 K |
| ⚪ | dev-route-visibility | `/dev/components` public-but-unindexed (dev-menü kararı); responsive audit (T98'de büyük ölçüde çözüldü) | T84 K |
| ⚪ | T97-NEXT_LOCALE-cookie 🆕 | `LanguageSelector` localStorage+path; next-intl `NEXT_LOCALE` cookie migration | `MEMORY_ARCHIVE.md:219` (T97 K5) |
| ⚪ | T97-formatAmount-deprecated-alias 🆕 | `format.ts:133-139` deprecated `formatAmount` alias kaldırılacak (call-site migration tamam) | `MEMORY_ARCHIVE.md:219` (T97 K3) |
| ⚪ | FE-enums-ts-lag 🆕 | `types/enums.ts` backend'in gerisinde: `NotificationType` (-7), `AuditAction` (-14), `FraudFlagType` (-`SANCTIONS_MATCH`). Runtime kırılmaz (icon `?? "transactionUpdate"` fallback / admin ekranları `admin.ts` union'larını kullanır / audit `action` serbest string); F0 enums'tan miras, kozmetik. | `GATE_CHECK_F5.md` / EnumTests 27/26/5 |

## 6. Doküman / spec borcu

| Önc. | ID | Açıklama | Kaynak |
|---|---|---|---|
| ✅ | T33-SuccessRate-FractionVsPercent | **ÇÖZÜLDÜ → WP17 (no-op):** kod (`UserConfiguration.HasPrecision(5,4)`) + 06 §3.1 + 07 §5.x örnekleri zaten **fraction (0..1)** üzerinde hizalı (M1 2026-05-01 kapandı); aksiyon gerekmedi | `T33_REPORT.md:142` |
| ✅ | AD6-AD7-contract-recon | **ÇÖZÜLDÜ → WP17:** 3 alan koda eklendi — AD7 party `reputationScore` (yeni `AdminTransactionPartyDetailDto`), AD6 list `cancelledAt`, AD7 notification `content` (`Notification.Body`) + FE + 07 §9.6/9.7 doc + 2 test | T101 K3/K4/K6 |
| ✅ | backend-i18n-migration | **ÇÖZÜLDÜ → WP17 (hibrit):** notification resx tr/es/zh→56 (parity) · dispute auto-check buyer-locale lokalizasyon (`DisputeAutoCheckMessages`) · settings 59 + permission 2 label FE-key-mapping · steam `warningMessage` kaldırıldı. **Kalan:** notification `{Outcome}` per-recipient (auto-escalated iki-taraf + DisputeResolved) → notification-mimari follow-up | T49/T92/T95/T102/T106 K |
| ~ | audit-doc-drift | **Kısmen → WP17:** 07 §9.19 `SELLER_PAYOUT_SENT`→`WALLET_ESCROW_RELEASE` ✅ · 06 §3.25 stale index ✅ · PermissionCatalog count ✅ (zaten 14). **Kalan (WP17-dışı):** RefreshToken purge (by-design soft-delete) · ACTIVE_DISPUTE_EXISTS (WP5'te 07'den kaldırıldı, erişilemez) | T42/T63b/T82 K |
| ⚪ | audit-detail-schema | AuditLog `detail` pass-through NewValue; central AuditLog wiring; RestartRecovery audit; OldValue yok | T42/T39/T47/T106 K |
| ⚪ | mvp-scope-postmvp | Bilinçli MVP-dışı: reviews, KYC, mobil, diğer oyunlar, multi-item/barter, ek blockchain, fiat, premium, Discord guild, Sentry vb. | 10_MVP_SCOPE / 02_PRD |
| ~ | content-authoring | **WP17 (taslak):** ToS/Privacy/Support `legal.*` taslak metin 4 dil yazıldı (owner "taslak yaz" kararı) — **otoriter metin hukuk review gerektirir** (jurisdiction/governing-law/entity belirsiz) | SPEC |
| ⚪ | suspend-signalr-spec | Suspension'da otomatik EMERGENCY_HOLD/live force-restrict yok (request-time enforce); `/auth/suspended` vs `/account-suspended` | T105a K2 |
| ⚪ | like-escape-helper | `AdminUserService.ListAsync` + audit/sanctions search raw `EF.Functions.Like` (parametrize, injection değil); paylaşılan escape helper + no-direct-INSERT arch rule | T63 K6 / T106 K8 / T42 K1 |
| ✅ | permissioncatalog-xmldoc-drift | **ÇÖZÜLDÜ → WP17 (no-op):** xmldoc zaten "14 catalog entries", `All` 14 içerir (T82 sonrası güncel); aksiyon gerekmedi | `PermissionCatalog.cs:56` |
| ✅ | datamodel-sanctioned-index-drift | **ÇÖZÜLDÜ → WP17:** 06 §3.25 obsolete `IX_SanctionedAddresses_Address` satırı kaldırıldı; filtered UQ `WHERE IsActive=1` hot-path'i karşılar | `06_DATA_MODEL.md` |
| ✅ | admin-route-table-drift | **ÇÖZÜLDÜ → WP17:** 04 §1 S12 `/admin`→`/admin/dashboard`, S21 `/admin/audit-log`→`/admin/audit-logs` | `GATE_CHECK_F5.md` |
| ✅ | T84-emergencyhold-status-doc-drift | **ÇÖZÜLDÜ → WP17:** 04 §5'e overlay-rozet notu eklendi (`EMERGENCY_HOLD` = IsOnHold overlay, enum değeri değil; `FLAGGED` ise kanonik statü — review F4) | `MEMORY_ARCHIVE.md` T84 K6 |
| ✅ | T58-ActiveDisputeExistsUnreachable 🆕 | **ÇÖZÜLDÜ → WP5** — 07 §7.8 Hatalar'dan kaldırıldı (03 §6 farklı-tip eşzamanlı dispute'a izin verdiği için tasarım-gereği erişilemez) | `T58_REPORT.md:177` |

## 7. Test / CI borcu

| ID | Açıklama | Kaynak |
|---|---|---|
| FE-test-runner | Frontend unit-test runner / Vitest yok (F5 plan-onaylı; yalnız validator smoke) | T92/T98/T99-T106 K |
| prettier-drift | Repo-geneli ~149 + sidecar 10-36 dosya prettier drift; CI `format:check` yok (bloke etmez) | T84 K8 / T64 K1 |
| filterbar-dateto-chore | `dateTo` end-of-day off-by-one yalnız audit log'da düzeltildi; S13/S15 FilterBar'da repo-geneli kalıyor | T106 K2 / T100 K6 |
| test-infra-misc | TestContainers Redis/SQL, `AdminWalletsController` endpoint testi, suspend permission isolation, migration verify | T07/T82/T77/T105a K |
| i18n-lint-ci | `UNTRANSLATABLE_TERMS`/`isUntranslatable()` var ama CI lint scripti yok | T97 K1 |
| sidecar-npm-audit | 20 transitive npm-audit açığı; `ethers@6.16.0` dolaylı dep; ESLint hermes-parser kırılganlığı | T64 K5 / T70 / T84 K9 |
| template-side-escape-audit 🆕 | Notification template'lerinin escaper'la kötü etkileşen rezerve karakter denetimi (defense-in-depth) + Discord 2000-char | `T79_REPORT.md:131` K5 / `T80_REPORT.md:225` K5 |

## 8. Operasyonel config

| Önc. | ID | Açıklama | Kaynak |
|---|---|---|---|
| ✅ | T55-DormantThresholdMandatoryUnconfigured 🆕 | **ÇÖZÜLDÜ → WP14:** 19 zorunlu ayar (gerçek sayı; "21" stale) `Docs/DEPLOY_RUNBOOK.md §A` + `.env.example`'da belgelendi. Fail-fast (06 §8.9) bilinçli olarak korundu — owner kararı seed-default DEĞİL. | `T55_REPORT.md:37` |

---

## Ek — Zaten çözülmüş / non-actionable (yalnız izlenebilirlik)

Aşağıdakiler tarama sırasında "erteleme" olarak görünebilir ama **aktif borç değil**:

| ID | Durum |
|---|---|
| AD16 user-detail boşlukları (stats/wallet history/reputation/flag-dispute-counterparty) | ✓ T105 + T105b'de çözüldü |
| SignalR push'lar (tx-detail/notifications/dispute/profile/header) | ✓ T96'da çözüldü |
| Hesap suspend + flag-content projeksiyon (K2/K9/K10) | ✓ T105a + T100a'da çözüldü |
| F0/F1/F2 bootstrap forward-defer'leri (rate-limit/auth/Loki/migration/stub'lar) | ✓ Hedef successor task'larında çözüldü |
| T105b öncesi wallet adres backfill | ✗ Non-actionable — tarihsel veri hiç tutulmadı, imkânsız |
| GitHub branch protection | ✗ Paid feature (403); owner kararı bekliyor — implementasyon borcu değil |
| Çeşitli by-design MVP stub'ları (loose tx-hash, rate-limiter, price-cache retention) | ✗ By-design |
| 08-Telegram webhook 07'de eksik | ✓ Aslında `07_API_DESIGN.md:767` §5.11b'de **var** (tarama yanlış alarmı; doğrulandı) |
| T37 kanal-handler PII log maskeleme | ✓ `TargetExternalIdMasker.Mask()` ile T78/T79/T80'de yerine getirilmiş |

---

> **Doğrulama:** İki-turlu çıkarımın tüm net-yeni adayları refute-default doğrulayıcıdan geçirildi (kod/rapor kanıtıyla). Doğrulayıcı verdict'i: "kalan üst-düzey erteleme tespit edilmedi; iki geçiş ertelenen iş havuzunu yüksek güvenle kapsıyor." Kritik kod iddiaları (escrow→bot bağı yok, `NullMarketPriceProvider`/`StubPayoutVerifier`, sidecar round-robin) bağımsız doğrulandı.
