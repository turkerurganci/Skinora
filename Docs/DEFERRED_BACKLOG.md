# Ertelenmiş İşler Backlog'u (Deferred Work Backlog)

> **Amaç:** Proje boyunca bilinçli olarak ertelenen / sonraya bırakılan tüm somut işlerin tek izlenebilir listesi. Tamamlanan işler için tek doğru kaynak [`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md); bu dosya yalnızca **ertelenen** kalemleri toplar.
>
> **Oluşturulma:** 2026-06-13 · iki-turlu çok-ajanlı kaynak taraması (status doc + 115 task report + repo/auto memory + backend/frontend kod + sidecar + discovery docs + gate-check/audit/GPT-review raporları). Her kalem kod veya rapor kanıtıyla doğrulandı.
>
> **Durum (2026-08-09, F7/P2P sonrası):** **34 aktif satır** (28 + §9'da 6 yeni) · 59 satır ✅ çözüldü · P2P geçişiyle **3 satır konusuz kaldı** (bot katmanı — §9 sonundaki nota bakınız). Yeni kalemlerin hiçbiri MVP'yi bloklamıyor.
>
> **Önceki durum (2026-07-26):** **28 aktif satır** · 59 satır ✅ çözüldü (Öne Çıkanlar tablosu gövde satırlarını tekrarladığı için satır sayısı kalem sayısından fazladır) · **F6 Gate Check'i bloklayan: 0** (F6 ✓ PASS 2026-06-24 → MVP kapandı; kalan kalemler post-MVP). Bu dosya bir kalem ele alındıkça güncellenmelidir (satırı **✓ Çözüldü** işaretle veya kaldır).
>
> **Sıralama/sahiplik:** F6 öncesi MVP-içi kalemler [`PRE_F6_PLAN.md`](PRE_F6_PLAN.md)'de 19 iş paketine (WP1–WP18) bağlandı. Aşağıdaki bazı satırlar 2026-06-14 kod taramasıyla **kısmen stale** bulundu ve düzeltildi (emergency-hold, blockchain-monitor, item-refund, steam-sidecar, T55).
>
> **Hijyen taraması (2026-07-26, F6 sonrası):** WP1–WP20 + F6 merge'lerinden sonra kalan 🟡 satırlar üretim wiring'i (DI kaydı + çağıran + migration + test) düzeyinde tek tek doğrulandı. Sonuç: **5 satır tam çözülmüş** (SWEEP-dispatcher → WP3 · item-refund-consumers → WP2 · T38-AdminFlagAlert-FlagId → WP8 · T61-SteamTransitionRealtimePush → WP9 · FE-admin-signalr-subscription → WP9) + Öne Çıkanlar tablosunda gövdeye göre stale kalan 2 satır (T33-SuccessRate → WP17 · T107 → F6) ✅ işaretlendi; **T50-OutageFreezeCallers yalnız manuel yarısı** kapandığı için daraltıldı; **T81-PriceConsumerWireup** "kod ✓ / prod config bağımlı" olarak yeniden yazıldı. `StubPayoutVerifier` doğrulandı — hâlâ açık.

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
| ✅ | SWEEP-dispatcher | **ÇÖZÜLDÜ → WP3** — `SweepQueueJob` PENDING `SWEEP` satırı üretir (`OutgoingTransferJobsRegistrar` recurring kaydeder, registrar `IHostedService` olarak `TransactionsModule`'de) + dispatcher `OutboundTypes`'ında SWEEP var | — |
| 🟡 | T81-PriceConsumerWireup | **Kod tarafı çözüldü → WP4a, prod config'e bağlı:** port `PriceServiceMarketPriceProvider`'a bağlandı, `MarketPriceAtCreation` set ediliyor, PRICE_DEVIATION kuralı canlı. **Açık kalan:** `SteamMarket:Provider` varsayılanı `logging` (`NoPrice()` → fail-open) + seed `price_deviation_threshold=1.0` (%100) → prod'da ikisi ayarlanmadıkça kural sessiz kalır (`DEPLOY_RUNBOOK §C`) | PRICE_DEVIATION'ın prod'da etkin olması (deploy config) |
| 🟡 | StubPayoutVerifier | Üretim payout doğrulayıcı yok (fail-closed, manuel admin) | Otomatik on-chain payout doğrulama |
| 🟡 | P2P-SettlementTiering | **F7/yeni** — herkes 8 gün bekliyor; itibarlı satıcılar için sürenin kısaltılması satıcı deneyimini belirgin iyileştirir (§9) | — |
| 🟡 | P2P-HotWalletPolicyReview | **F7/yeni** — para artık işlem başına 8 gün platformda; sıcak cüzdan eşiği ve soğuk cüzdan aktarma sıklığı yeni profile göre hesaplanmalı (§9) | — |
| 🟡 | P2P-DeliveryPollingJob | **F7/yeni** — sürekli teslimat taraması yok; pasif alıcıda satıcı süre sonuna kadar bekliyor (§9) | — |
| ✅ | steam-sidecar-stubs | **ÇÖZÜLDÜ → WP6** — sidecar `GET /api/trade-hold/:steamId` (`GetTradeHoldDurations`) + `SidecarTradeHoldChecker` (U17) + `SidecarMobileAuthenticatorCheck` (A7); envanter reader zaten gerçekti | — |
| ✅ | item-refund-consumers | **ÇÖZÜLDÜ → WP2** — `PaymentRefundToBuyerConsumer` `BUYER_REFUND` satırı üretir; DI'da kayıtlı + iki katmanlı idempotency + `UQ_BlockchainTransactions_BuyerRefund_TransactionId` | — |
| 🟡 | T50-OutageFreezeCallers | **Yarısı çözüldü → WP7** (admin manuel freeze/resume: `AdminMaintenanceService` `FreezeManyAsync`/`ResumeManyAsync` çağırıyor). **Açık kalan: otomatik tespit** — 02 §3.3 `STEAM_OUTAGE`/`BLOCKCHAIN_DEGRADATION` auto-detect'inde bulk-freeze tetiklenmiyor; WP16 `PlatformHealthProbeJob` **alert-only** (admin alert + audit) | Otomatik outage dayanıklılığı (manuel yol açık) |
| ✅ | T56-MultiAccountRetroScan | **ÇÖZÜLDÜ → WP4b** — günlük `MultiAccountRetroScanJob` cüzdanlı aktif kullanıcıları retroaktif tarar (`IMultiAccountDetector` yeniden çağrılır) | — |
| ✅ | T61-SteamTransitionRealtimePush | **ÇÖZÜLDÜ → WP9** — `SteamWebhookHandler` geçişlerde `TransactionStatusChangedEvent`'i outbox'a yayınlar → `TransactionStatusChangedRealtimeConsumer` SignalR push eder | — |
| ✅ | T38-AdminFlagAlert-FlagId | **ÇÖZÜLDÜ → WP8** — `Notification.FlagId` kolonu + filtered index (migration `WP8_AddNotificationFlagId`); `FraudFlagCreatedAdminNotificationConsumer` alanı dolduruyor | — |
| ✅ | T30-TosVersionReprompt | **ÇÖZÜLDÜ → WP11** — CurrentUserDto += `tosAcceptedVersion`, `tos/accept` versiyon-upgrade'e izin verir (409 yalnız aynı versiyonda), FE `TosRepromptGate` versiyon uyuşmazlığında re-prompt | — |
| ✅ | T87-K1 | **ÇÖZÜLDÜ → WP11** — callback `/auth/refresh`→token store + `acceptTos` wire-up + 401 refresh interceptor; MA recheck /auth/me ile (A7 trade-URL akışına ait) | — |
| ✅ | FE-admin-signalr-subscription | **ÇÖZÜLDÜ → WP9** — `RealtimeProvider.tsx` üç admin event'ine de abone (`onAdminBotStatusChanged` / `onAdminReconciliationMismatch` / `onAdminHotWalletThresholdBreached`) | — |
| ✅ | TradeOfferMonitor-hotadd-T69 | **ÇÖZÜLDÜ → WP6 (resolved-by-design)** — statik pool (`BotManager` dinamik-add yok); idempotent `attachToSession` hook'u T69 dinamik pool için hazır + test edilmiş | — (statik pool'da sorun yok) |
| ✅ | T33-SuccessRate-FractionVsPercent | **ÇÖZÜLDÜ → WP17 (no-op)** — kod (`HasPrecision(5,4)`) + 06 §3.1 + 07 örnekleri zaten fraction (0..1) üzerinde hizalı; detay §7 satırı | — |
| ✅ | T107 | **ÇÖZÜLDÜ → F6** — E2E happy path (harness + smoke + UI), bağımsız validator PASS (PR #198); F6 Gate Check ✓ PASS | — |

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
| ✅ | T107 | **ÇÖZÜLDÜ** — E2E happy path (smoke+UI); bağımsız validator PASS (PR #198) | task | IMPLEMENTATION_STATUS F6 |
| ✅ | T108 | **ÇÖZÜLDÜ** — E2E iptal senaryoları (satıcı/alıcı/admin); bağımsız validator PASS (PR #200) | task | F6 |
| ✅ | T109 | **ÇÖZÜLDÜ** — E2E timeout senaryoları (4 faz); bağımsız validator PASS (PR #201) | task | F6 |
| ✅ | T110 | **ÇÖZÜLDÜ** — E2E ödeme edge case'leri (§5.1–§5.5 + §5.3a) firsthand 6/6; bağımsız validator PASS 2026-06-23 (PR #202). Bulgu K1 (refund-adresi doc çelişkisi) → §6 `T110-RefundAddressDocConflict` (post-MVP) | task | F6 |
| ✅ | T111 | **ÇÖZÜLDÜ** — E2E fraud/flag senaryoları (PRICE_DEVIATION dahil); bağımsız validator PASS (PR #204). Bulgu K1 (admin-flags cross-doc çelişkisi) → §6 `T111-AdminFlagsSurfaceDocConflict` (post-MVP, F6 gate forward) | task | F6 |
| ✅ | T112 | **ÇÖZÜLDÜ** — E2E emergency hold (hold/resume/cancel + ITEM_DELIVERED guard); bağımsız validator PASS (PR #205) | task | F6 |
| ✅ | T113 | **ÇÖZÜLDÜ** — E2E admin akışları (6 akış + AD17); bağımsız validator PASS (PR #206). Bulgu B1 (`UQ_AdminRoles_Name` re-create 500) → §4 `T113-AdminRoleNameReuse500` (F6 gate forward) | task | F6 |
| ✅ | T114 | **ÇÖZÜLDÜ** — E2E downtime/bakım (3 senaryo); bağımsız validator PASS (PR #207) | task | F6 |
| ✅ | T87-K1 | **ÇÖZÜLDÜ → WP11** — callback refresh→token store + ToS-accept wire-up + 401 refresh interceptor; check-authenticator owner-kararıyla /auth/me recheck'ine bağlandı (A7 trade-URL/U17 akışına ait, login'de değil) | k-note | T87 K1-K3 / T85 K1 |

## 4. T-future — Backend orkestrasyon (caller/consumer wire-up)

### Orta öncelik

| Önc. | ID | Açıklama | Tip | Kaynak |
|---|---|---|---|---|
| 🟡 | AdminUserActivity-RefundedTerminal 🆕 | **`AdminUserActivityProvider._terminalStates`'te `REFUNDED` eksik.** XML doc'u `AdminTransactionQueryService._terminalStates` / `AdminDashboardService._terminalStates`'i "birebir yansıttığını" ve AD1 sayacıyla eşleştiğini iddia ediyor; o iki liste `REFUNDED`'ı içeriyor, bu içermiyor. Sonuç: REFUNDED bir işlem AD16 kullanıcı-aktivite panelinde **hâlâ aktif** sayılıyor ve AD19d hold-by-user yüklemi ile AD1 sayacından sapıyor. WP5 `REFUNDED`'ı eklerken atlanmış (T117 doğrulamasında tespit edildi, `git diff` boş → T117 kaynaklı değil). `_cancelledStates`'in dört CANCELLED_* ile sınırlı olması **bilinçli** (04 §8.9.2), o değişmemeli | backend-gap | T117 doğrulaması 2026-08-09 |
| ✅ | T58-AdminDisputeQueue 🆕 | **ÇÖZÜLDÜ → WP5** — `AdminDisputeService` (AD27/28/29) + `RESOLVED_FOR_*`/`REFUNDED` + audit/notify; FE `/admin/disputes` | backend-gap | `T58_REPORT.md:178` |
| ✅ | SWEEP-dispatcher | **ÇÖZÜLDÜ → WP3** — `SweepQueueJob` (recurring, `OutgoingTransferJobsRegistrar` → `IHostedService` `TransactionsModule.cs`) settle olmuş işlemler için PENDING `SWEEP` `BlockchainTransaction` satırı üretir (`UQ_BlockchainTransactions_Sweep_TransactionId` ile tekil); `OutgoingTransferDispatchJob.OutboundTypes` SWEEP'i kapsıyor; `SweepQueueJobTests` | backend-gap | T73/T76/T77 K |
| 🟡 | T81-PriceConsumerWireup | **Kod çözüldü → WP4a:** `IMarketPriceProvider`=`PriceServiceMarketPriceProvider` (→ `IPriceService`→`PriceService`→`ISteamMarketPriceClient`), `MarketPriceAtCreation` set ediliyor (`TransactionCreationService`), PRICE_DEVIATION `FraudPreCheckService` Rule 1'de canlı. **Açık kalan (deploy config):** `Program.cs` `SteamMarket:Provider` varsayılanı `logging` → `LoggingSteamMarketPriceClient.NoPrice()` → fail-open; ayrıca seed `price_deviation_threshold=1.0` (%100) pratikte ateşlemez. Prod'da `SteamMarket__Provider=steam-market` + daraltılmış eşik gerekli (`DEPLOY_RUNBOOK §C`) | backend-gap | T81 K1, `Program.cs:154-167` |
| 🟡 | StubPayoutVerifier | `IPayoutVerifier`=stub (her zaman `UnableToVerify`→manuel admin) | backend-gap | T60 K1, `StubPayoutVerifier` |
| ✅ | steam-sidecar-stubs | **ÇÖZÜLDÜ → WP6** — sidecar `GET /api/trade-hold/:steamId` (`GetTradeHoldDurations`, 08 §2.2) + paylaşılan `ISteamTradeHoldProbe`/`HttpSteamTradeHoldClient` + `SidecarTradeHoldChecker` (U17) + `SidecarMobileAuthenticatorCheck` (A7); fail-closed; envanter reader zaten gerçekti (`SidecarSteamInventoryReader`) | backend-gap | T35/T31/T58 K |
| ✅ | item-refund-consumers | **ÇÖZÜLDÜ → WP2** — `PaymentRefundToBuyerConsumer` `PaymentRefundToBuyerRequestedEvent`'i tüketip `BUYER_REFUND` `BlockchainTransaction` satırı üretir (DI: `TransactionsModule.cs`; iki katmanlı idempotency + `UQ_BlockchainTransactions_BuyerRefund_TransactionId`; `PaymentRefundToBuyerConsumerTests`). Diğer 4 iade yolu zaten inline/T106a bağlıydı | backend-gap | T49/T51/T71 K |
| 🟡 | T50-OutageFreezeCallers | **Yarısı çözüldü → WP7:** admin manuel bakım toggle'ı `AdminMaintenanceService` üzerinden `FreezeManyAsync`/`ResumeManyAsync` çağırıyor (02 §3.3 "admin manual" yolu kapandı). **Açık kalan:** `STEAM_OUTAGE`/`BLOCKCHAIN_DEGRADATION` **otomatik tespitinde** bulk-freeze tetikleyen çağıran yok — WP16 `PlatformHealthProbeJob` bilinçli olarak **alert-only** (`ADMIN_PLATFORM_OUTAGE` + `PLATFORM_OUTAGE_DETECTED` audit, edge-detected), freeze etmiyor | backend-gap | `T50_REPORT.md:124-125`, `PRE_F6_PLAN.md` WP16-D3 |
| ✅ | T56-MultiAccountRetroScan 🆕 | **ÇÖZÜLDÜ → WP4b** — günlük retro-scan Hangfire job (`MultiAccountRetroScanJob`, `AutoUnsuspendJob` deseni) | backend-gap | `T56_REPORT.md:150` |
| ⚪ | T113-AdminRoleNameReuse500 🆕 | **F6 Gate Check forward (2026-06-24).** `UQ_AdminRoles_Name` **filtresiz** unique index + `AdminRoleService.DeleteAsync` **soft-delete** (`IsDeleted=1`, query filter `!IsDeleted`) → silinen rolün adı kalıcı rezerve kalır; aynı adı yeniden insert/rename **500 INTERNAL_ERROR** verir (temiz **409 CONFLICT** yerine; `Cannot insert duplicate key … UQ_AdminRoles_Name`). Kök tasarım T24'ten (by-design Name-kirlenmesi engeli — `T24_REPORT.md:39,84`), ama 03 §8.6 "sil→aynı adla yeni rol" akışı için latent backend kusuru. Düzeltme: filtered unique index (`WHERE IsDeleted=0`) **veya** servis-katmanı pre-check → 409. T113 E2E per-run benzersiz ad ile robust (CI fresh DB etkilenmez). | backend-gap | `T113_REPORT.md:75` |
| ✅ | T61-SteamTransitionRealtimePush | **ÇÖZÜLDÜ → WP9** — `SteamWebhookHandler.PublishStatusChangedAsync` status flip'iyle aynı unit-of-work'te outbox'a `TransactionStatusChangedEvent` yazar (escrow + delivery geçişleri); `TransactionStatusChangedRealtimeConsumer` bunu SignalR'a push eder | backend-gap | `T61_REPORT.md:146` (K2) |
| ✅ | T38-AdminFlagAlert-FlagId | **ÇÖZÜLDÜ → WP8** — `Notification.FlagId` (`Guid?`) kolonu + `IX_Notifications_FlagId` filtered index + migration `20260617194020_WP8_AddNotificationFlagId`; `FraudFlagCreatedAdminNotificationConsumer` `FraudFlagId`'yi geçiriyor → admin flag-link hedefi çalışıyor | backend-gap | `T38_REPORT.md:20`, `NotificationTargetMapper.cs:25` |
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
| ✅ | FE-admin-signalr-subscription | **ÇÖZÜLDÜ → WP9** — `RealtimeProvider.tsx` üç admin event'ine de abone (`onAdminBotStatusChanged` / `onAdminReconciliationMismatch` / `onAdminHotWalletThresholdBreached`) ve ilgili query'leri invalidate ediyor | T96 K2 |
| ⚪ | FE-timeline-cancel-step-position 🆕 | **Lokal inceleme bulgusu (2026-07-26).** `TransactionTimeline` iptal/FLAGGED/REFUNDED durumunda kırmızı X'i **her zaman 1. adımda** gösteriyor (`indexForStatus` → `-1`, `effectiveIndex = max(0,-1) = 0`), yani işlemin hangi adımda iptal edildiği kayboluyor; 04 §C05 "aktif adımda kırmızı X" diyor. Frontend-only çözülemez: kullanıcı detay DTO'sunda (`TransactionDetailResponse`) iptal anındaki durum yok (`cancelInfo` yalnız `cancelledBy`/`reason`/`cancelledAt`/`itemReturned`/`paymentRefunded`). Gerekli: AD7'ye iptal-anı durumu (veya kullanıcıya açık `statusHistory`) eklenmesi + timeline'ın bunu tüketmesi. Terminal-render kusurunun diğer yarısı (COMPLETED/REFUNDED) `fix/timeline-terminal-step` PR'ında kapatıldı. | T96 / 04 §C05 |
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
| ⚪ | T110-RefundAddressDocConflict 🆕 | **MVP sonrası (owner kararı 2026-06-23 — yakın zamanda yapılmayacak).** Edge-case iade hedef-adresi doküman çelişkisi: impl + **08 §562** reddedilen-ödeme iadelerini (insufficient/excess/wrong-token/late) **ödeme kaynak adresine** (`FromAddress`) gönderir; ama 02 §4.4 (s.108) · 02 §4.6 (s.127) · 03 §4.3/§5.3/§5.4/§5.5 (s.287/340/360/368) · 06 §3.8 (s.736) "alıcının belirlediği iade adresine" der. `BUYER_REFUND` (kabul edilmiş ödeme iadesi) doğru şekilde belirlenen adrese gider (02 §4.6 s.127 doğru, korunur). Yapılacak: iki-hedef ayrımını dokümanlarda netleştir + `AmountValidationService.QueueRefundIntent` yorumu 02§4.6→08§562. T110 testi spec-yetkili (08 §562) davranışı **doğru** test eder → bu doc-borcu, kod değil. | T110 validate K1 |
| ⚪ | T111-AdminFlagsSurfaceDocConflict 🆕 | **MVP sonrası (F6 Gate Check forward 2026-06-24 — kardeş T110-K1 deseni).** Admin flag yönetim yüzeyi cross-doc çelişkisi: **03 §8.2** (`03_USER_FLOWS.md:517`) hesap flag'lerinin **ayrı bir hesap flag yönetim yüzeyinden** yönetildiğini söyler; ama **07 §9.2 + 04 §8.2 + T100a** tek `/admin/flags` + `scope=ACCOUNT_LEVEL` yüzeyini tanımlar ve üretim kodu (T100a) tek-yüzey lehine fiilen çözmüştür (03 §8.2 stale). Yapılacak: 03 §8.2 metnini 04 §8.2 / 07 §9.2 ile hizala. T111 E2E mevcut gerçekliği (`/admin/flags/:id/reject`) doğru test eder, AC3'ü zayıflatmaz → bu doc-borcu, kod değil. | T111 validate K1 |
| ~ | content-authoring | **WP17 (taslak):** ToS/Privacy/Support `legal.*` taslak metin 4 dil yazıldı (owner "taslak yaz" kararı) — **otoriter metin hukuk review gerektirir** (jurisdiction/governing-law/entity belirsiz) | SPEC |
| ⚪ | suspend-signalr-spec | Suspension'da otomatik EMERGENCY_HOLD/live force-restrict yok (request-time enforce); `/auth/suspended` vs `/account-suspended` | T105a K2 |
| ✅ | like-escape-helper | **ÇÖZÜLDÜ → WP18 (PR-3):** kanonik bracket-wrapping escaper `AdminTransactionQueryService` private'ından `Skinora.Shared.Persistence.SqlLikeEscaper`'a çıkarıldı + 3 escape'siz `EF.Functions.Like` call-site'ı (AdminUserService/AuditLogQueryService/AdminSanctionsService) düzeltildi (LIKE-wildcard injection kapandı). `NoRawSqlConventionTests` source-scan arch testi (`ExecuteSqlRaw`/`FromSqlRaw`/* yasak, backend/src-only, NetArchTest yok). | T63 K6 / T106 K8 / T42 K1 |
| ~ | i18n-untranslatable-localized 🆕 | **WP18 (advisory, PR-1):** `check-i18n.mjs` 15 anahtarın 04 §10.4 "untranslatable" terimini yerelleştirdiğini buldu — yalnız **"Gas fee"** (es `Tarifa de gas` / zh `Gas 费` / tr `…gas` — 12 anahtar) + **"Mobile Authenticator"** (zh `手机令牌` — 3 anahtar). Sert marka token'ları (USDT/USDC/TRC-20/Tron/Steam/Steam ID/CS2/Trade offer) temiz. Owner kararı: çeviriler **değiştirilmedi** (zh'de Steam'in resmî terimi olabilir, İngilizce'ye zorlamak UX'i bozar), kural **advisory** kaldı; spec-vs-çeviri uzlaşısı (çeviri düzelt **veya** 04 §10.4 listesini daralt) follow-up'a bırakıldı. | `check-i18n.mjs` |
| ✅ | permissioncatalog-xmldoc-drift | **ÇÖZÜLDÜ → WP17 (no-op):** xmldoc zaten "14 catalog entries", `All` 14 içerir (T82 sonrası güncel); aksiyon gerekmedi | `PermissionCatalog.cs:56` |
| ✅ | datamodel-sanctioned-index-drift | **ÇÖZÜLDÜ → WP17:** 06 §3.25 obsolete `IX_SanctionedAddresses_Address` satırı kaldırıldı; filtered UQ `WHERE IsActive=1` hot-path'i karşılar | `06_DATA_MODEL.md` |
| ✅ | admin-route-table-drift | **ÇÖZÜLDÜ → WP17:** 04 §1 S12 `/admin`→`/admin/dashboard`, S21 `/admin/audit-log`→`/admin/audit-logs` | `GATE_CHECK_F5.md` |
| ✅ | T84-emergencyhold-status-doc-drift | **ÇÖZÜLDÜ → WP17:** 04 §5'e overlay-rozet notu eklendi (`EMERGENCY_HOLD` = IsOnHold overlay, enum değeri değil; `FLAGGED` ise kanonik statü — review F4) | `MEMORY_ARCHIVE.md` T84 K6 |
| ✅ | T58-ActiveDisputeExistsUnreachable 🆕 | **ÇÖZÜLDÜ → WP5** — 07 §7.8 Hatalar'dan kaldırıldı (03 §6 farklı-tip eşzamanlı dispute'a izin verdiği için tasarım-gereği erişilemez) | `T58_REPORT.md:177` |

## 7. Test / CI borcu

| ID | Açıklama | Kaynak |
|---|---|---|
| ✅ FE-test-runner | **ÇÖZÜLDÜ → WP18 (PR-2):** `vitest 4.1.9` + `@vitejs/plugin-react` + `jsdom` + `@testing-library/react`/`jest-dom`; `vitest.config.ts` (jsdom + `@/` alias) + setup + `test` script. Seed B = **25 test/7 dosya** (6 pure-util + 1 `StatusBadge` render). Yeni CI `frontend-test` job (FE + 2 sidecar vitest, per-area gated, ci-gate.needs'te blocking). Geniş component/hook kapsamı F6'ya ertelendi. | T92/T98/T99-T106 K |
| ✅ prettier-drift | **ÇÖZÜLDÜ → WP18 (PR-1):** FE + 2 sidecar `format:check` **blocking** CI gate'i mevcut lint job'a eklendi (backend `dotnet format` paritesi). Gerçek LF drift 25 dosya (FE 13 / steam 7 / BC 5) `prettier --write` ile normalize edildi; görünen 144 CRLF working-tree artefaktıydı. 3 `.prettierrc` alan-bazlı bilinçli farkla korundu (FE singleQuote:false vs sidecar:true). | T84 K8 / T64 K1 |
| ✅ filterbar-dateto-chore | **ÇÖZÜLDÜ → WP18 (PR-3):** paylaşılan `toEndOfDay` helper (`lib/utils/date.ts` + vitest) admin transactions + flags'e uygulandı, audit-logs inline refactor edildi. Client-side; query-useMemo'ya anchor (dep/write-back dokunulmadı). | T106 K2 / T100 K6 |
| ✅ test-infra-misc | **ÇÖZÜLDÜ → WP18 (PR-3):** `AdminWalletsEndpointTests` (HTTP-boundary auth/mapping/envelope, IHotWalletService stub) + suspend permission-isolation testi (VIEW_FLAGS→403, ayrı token helper). TestContainers/Redis/migration-verify zaten kapsanmış (no-op). | T07/T82/T77/T105a K |
| ✅ i18n-lint-ci | **ÇÖZÜLDÜ → WP18 (PR-1):** `frontend/scripts/check-i18n.mjs` + `npm run i18n:check` CI lint job'da. Key-parity **blocking** (1291×4 anahtar, identical key-set); untranslatable kuralı (`UNTRANSLATABLE_TERMS` `untranslatable.ts`'ten tek-kaynak parse) **advisory** (owner kararı — bkz. `i18n-untranslatable-localized`). | T97 K1 |
| ~ sidecar-npm-audit | **WP18 (PR-1 advisory + PR-2 BC blocking):** her iki sidecar `npm audit` CI adımı. **Steam = kalıcı advisory@high** (`continue-on-error`) — 4 critical (`request@2.88.2`/`protobufjs`) **upstream-fix yok**, `--force` yıkıcı `steam-user` downgrade (owner accept-risk). **Blockchain = blocking@critical** (PR-2): 2 non-breaking override fixable high'ları kapattı — `ethers 6.17`→`ws 8.21.0` (GHSA-58qx) + `form-data 4.0.6` (GHSA-hmw2 CRLF; yapım-içi review bulgusu, high 4→3, tronweb 5.3.5 korundu). **Residual: axios + lodash high** (tronweb 5.3.5 altında) **non-breaking fix yok** → npm yalnız breaking `tronweb@6.0.2` sunar → owner accept-risk (pratik risk düşük); BC gate prod-critical'a scope'landı (0 bugün). hermes-parser yalnız frontend-lock'ta. | T64 K5 / T70 / T84 K9 |
| ⚪ | tronweb-6-major-bump 🆕 | **WP18 PR-2 follow-up:** BC residual lodash high'ları + diğer tronweb-transitive high'ları yalnız `tronweb 5.3.5→6.x` major bump temizler (breaking API). Ayrı scoped task: API recon + sidecar smoke test gerekir. | `WP18-2_REPORT.md` |
| ⚪ | npm-version-ci-parity 🆕 | **WP18 PR-2 dersi:** CI Node 20 → **npm 10.x**; lokal Node 24 → **npm 11.6.2**. npm 11 ile üretilen lockfile, npm 10 `npm ci` tarafından reddedilir (PR-2 ilk run lint fail). Kalıcı çözüm: CI Node 20→24 bump (npm 11 paritesi) **veya** `engines`/`.nvmrc` ile lokal npm pin. Şimdilik lockfile'lar `npm@10` ile üretiliyor. | `WP18-2_REPORT.md` |
| ✅ template-side-escape-audit | **ÇÖZÜLDÜ → WP18 (PR-3):** `BoldHeaderMessageComposer` raw-truncate-then-escape ile kanal limitinde (Discord 2000 / Telegram 4096) envelope kurar → escape-pair bölünmez, bold korunur, over-length artık preference auto-disable etmez. Composer unit + escaper negatif-parity + 2 handler over-length fact. (Escape-after-substitute zaten doğruydu; canlı injection yoktu.) | `T79_REPORT.md:131` K5 / `T80_REPORT.md:225` K5 |

## 8. Operasyonel config

| Önc. | ID | Açıklama | Kaynak |
|---|---|---|---|
| ✅ | T55-DormantThresholdMandatoryUnconfigured 🆕 | **ÇÖZÜLDÜ → WP14:** 19 zorunlu ayar (gerçek sayı; "21" stale) `Docs/DEPLOY_RUNBOOK.md §A` + `.env.example`'da belgelendi. Fail-fast (06 §8.9) bilinçli olarak korundu — owner kararı seed-default DEĞİL. | `T55_REPORT.md:37` |

---

## 9. F7 — P2P geçişiyle ertelenen işler

P2P pivotu sırasında bilinçli olarak kapsam dışında bırakılan kalemler (T115, 02 §2.1).

| Önc. | ID | Açıklama | Tip | Kaynak |
|---|---|---|---|---|
| 🟡 | P2P-SettlementTiering | **İtibarlı satıcılar için mutabakat süresini kısaltma.** MVP'de herkes 8 gün bekliyor (02 §4.5.1). Geçmişi temiz satıcılar için sürenin kısaltılması (ör. 20+ başarılı işlem → 24 saat) satıcı deneyimini belirgin biçimde iyileştirir. Riski geçmişe göre fiyatlar. **Ön koşul:** itibar verisinin bu karar için yeterince olgun olması | task | 02 §4.5.1, 10 §4.1 |
| 🟡 | P2P-HotWalletPolicyReview | **Sıcak/soğuk cüzdan politikasının 8 günlük mutabakat süresine göre gözden geçirilmesi.** Para artık işlem başına 8 gün platformda duruyor; aynı anda tutulan toplam tutar custodial modele göre çok daha yüksek. `hot_wallet_limit` eşiği, soğuk cüzdana aktarma sıklığı ve `SweepQueueJob` zamanlaması bu yeni profile göre yeniden hesaplanmalı | task | 02 §4.5.1 |
| 🟡 | P2P-DeliveryPollingJob | **Sürekli teslimat taraması.** MVP'de doğrulama üç noktada çalışıyor: alıcı onayı, dispute açılışı, teslimat süresi sonu (02 §9.2). Dakikalık arka plan taraması eklenirse pasif alıcıda teslimat 2-3 dakikada kapanır; şu an satıcı süre sonuna kadar bekleyebiliyor. `DeliveryVerificationService` bu iş için hazır tasarlandı (saf, yan etkisiz) — job ince bir sarmalayıcı olacak. **Maliyet:** Steam envanter okuma bütçesi ve eşzamanlı teslimat tavanı (08 §2.6) | task | 02 §9.2, 11 T125 |
| ⚪ | P2P-FloatVerification | **Aynı sınıf içindeki kalite farkının doğrulanması.** Eşleştirme `(classid, instanceid)` düzeyinde; float/desen farkı otomatik tespit edilmiyor. Satıcı aynı skinin daha kötü kopyasını gönderirse `WRONG_ITEM` dispute'una ve admin incelemesine kalıyor (02 §9.2). Çözüm CS2 Game Coordinator istemcisi gerektirir — yeni Steam hesabı + yeni servis; pivotun Steam hesap bağımlılığından kurtulma amacına ters. Post-MVP'de **fraud sinyali** olarak eklenebilir, durum geçiş kapısı olarak değil | task | 02 §9.2, 10 §4 |
| ⚪ | P2P-SellerDebtLedger | **Satıcı borç defteri.** Satıcı kusurlu iptallerde/teslim etmemede gas ücretini alıcı yiyor (02 §4.6 — owner kararı: mevcut formül korundu). Adil olan, kusurlu tarafa yazmak; ancak satıcının platformda parası olmadığı için kesinti yapılamıyor. Borç defteri (sonraki payout'tan kesme) çözerdi ama yeni bir muhasebe katmanı gerektirir ve satıcı bir daha hiç dönmeyebilir | task | 02 §4.6, 10 §4.1 |
| ⚪ | P2P-BotCodeArchive | **Bot custody kodunun arşiv işaretçisi.** T132/T133'te silinecek: bot havuzu, dispatch job, bot recovery, `TradeOffer`/`PlatformSteamBot`/`BotRecoveryItem` entity'leri, sidecar bot/trade modülleri. Kod git geçmişinde kalacak; silme commit'i merge edildiğinde **sha buraya yazılmalıdır**. **Backend tarafı T117'de silindi → `82bff4d`** (`TradeOffer`/`PlatformSteamBot`/`BotRecoveryItem` entity'leri, bot seçimi, dispatch, recovery, `SteamWebhooksController`; Steam modülü 35→11 dosya). Sidecar tarafı T133'te silinecek, sha'sı o zaman eklenecek. Geri dönüş senaryosu yok (eski model Steam kuralı nedeniyle çalışmıyor) ama cooldown'ı olmayan bir oyuna genişlerken referans olabilir | doc-drift | 11 T132/T133 |

> **Bot katmanına ait eski kalemler geçersizleşti.** Yukarıdaki §1 (T69 — bot health/failover/recovery) ve §2 (T103b — Steam hesapları backend) bölümlerindeki **açık** kalemler P2P geçişiyle konusuz kalmıştır: `T69-K4`, `T68-K1`, `T64-BotWebhookHandler`. Platform Steam hesabı işletmediği için bot durumu yayını, bot oturum hatası bildirimi ve bot webhook handler'ı diye bir şey kalmamıştır (02 §15, 05 §3.2). Bu satırlar tarihsel izlenebilirlik için yerinde bırakıldı; **yeni iş üretmezler**.

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
