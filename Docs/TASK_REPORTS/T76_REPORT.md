# T76 — Blockchain Sidecar reconciliation job

**Faz:** F4 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-05-17

---

## Yapılan İşler

- **Sidecar `TronGridClient.getAccountBalances`** — `GET /v1/accounts/{address}` TRX (SUN) + TRC-20 contract→raw map snapshot, batch'ten bağımsız (block height ayrı tek seferlik `getNowSolidBlock` çağrısı ile handler'da paylaşılır).
- **Sidecar `POST /api/wallet/balances` endpoint** (T77 placeholder `GET /api/wallet/hot-wallet-balance` yan yana korundu) — body `{addresses: string[]}` (max 100), response `{blockNumber, balances: [{address, tokens: {TRX, USDT, USDC}}]}`. Contract→symbol map config-driven (`TRON_USDT_CONTRACT` / `TRON_USDC_CONTRACT`), test-override edilebilir.
- **Backend `IBlockchainSidecarClient.GetWalletBalancesAsync`** — yeni method + `BlockchainSidecarBalancesResult` discriminated outcome + `BlockchainSidecarAddressBalances` record. `HttpBlockchainSidecarClient` impl: `X-Internal-Key` auth, 4xx → `InvalidRequest`, 5xx/timeout → `Unavailable`, başarısız payload parse → `Unavailable`.
- **`BlockchainTransactionType.SWEEP`** enum value eklendi (T73 K2 forward-deferred kapanışı — hot wallet expected hesabı için ledger satırı kategorisi). Sweep dispatcher T-future (PaymentReceivedEvent consumer); şu an 0 SWEEP satırı = hot wallet beklenen = boş.
- **`AuditAction.RECONCILIATION_MISMATCH`** enum value eklendi, `AuditLogCategoryMap` SECURITY_EVENT kategorisine kaydedildi (WALLET_ADDRESS_CHANGED + BOT_STATUS_CHANGED yan yana — custody-integrity sinyal).
- **`NotificationRealtimePayloads.AdminReconciliationMismatch`** record (Scope/Address/Token/Expected/Actual/Delta/BlockNumber/DetectedAt) + `INotificationRealtimePublisher.PublishAdminReconciliationMismatchAsync` + `SignalRNotificationRealtimePublisher` impl (Clients.All broadcast, T69 AdminBotStatusChanged pattern).
- **`ReconciliationService`** (Skinora.API/Services/Reconciliation) — 3 scope reconciliation:
  - **DepositAddress:** Aktif PaymentAddress'ler (MonitoringStatus != STOPPED, IsDeleted=false), CONFIRMED inflow (BUYER_PAYMENT/WRONG_TOKEN_INCOMING/SPAM_TOKEN_INCOMING) − CONFIRMED outflow (SWEEP/REFUND family).
  - **HotWallet:** CONFIRMED SWEEP inflow (ToAddress=hot) − CONFIRMED on-chain outflow (FromAddress=hot, SELLER_PAYOUT + 5 refund türü) − `ColdWalletTransfer.Amount` (FromAddress=hot).
  - **ColdWallet:** `ColdWalletTransfer.Amount` (ToAddress=cold). MVP'de cold→external outflow yok.
  - In-flight koruma: yalnızca `Status = CONFIRMED` satırlar beklenen toplama dahil → mid-finalization geçici mismatch yok.
  - Tolerans 0 (05 §3.3 finansal hesaplama prensibi); stablecoin scope sabit USDT + USDC (08 §3.3 allowlist), TRX reconciliation kapsamı dışı (platform TRX ledger'i yok).
  - Mismatch tespit edilirse: AuditLog `RECONCILIATION_MISMATCH` row (EntityType=scope, EntityId=address, OldValue=expected, NewValue=JSON envelope {token, expected, actual, delta, blockNumber}) + SignalR `AdminReconciliationMismatch` broadcast.
- **`ReconciliationJob`** (Hangfire entry-point) — service'i çağırır, outcome'u loglar, exception'ı rethrow ederek Hangfire retry policy'ye bırakır.
- **`ReconciliationJobRegistrar`** (`IHostedService`, RefreshTokenCleanupJobRegistrar pattern) — startup'ta `IBackgroundJobScheduler.AddOrUpdateRecurring<ReconciliationJob>` kayıt; cron `reconciliation.schedule_cron` SystemSetting'den okunur (default `0 3 * * *` UTC).
- **3 yeni SystemSetting** (idx 54–56):
  - `reconciliation.schedule_cron` — Default `"0 3 * * *"` (günlük 03:00 UTC). Runtime override host restart gerektirir (T96 devir).
  - `reconciliation.hot_wallet_address` — Unconfigured. Production deploy ayarlamadan hot wallet kapsamı skip + warn log.
  - `reconciliation.cold_wallet_address` — Unconfigured (opsiyonel). Cold transfer ledger MVP'de manuel başlatılır.
- **Migration `T76_AddReconciliationSettings`** — 3 SystemSetting `InsertData` (Ids 0aa51010-…0036/0037/0038); model snapshot + Designer dosyaları otomatik scaffold.
- **DI:** `TransactionsModule.cs` → `IReconciliationService` scoped + `ReconciliationJob` scoped + `ReconciliationJobRegistrar` hosted service.

## Etkilenen Modüller / Dosyalar

### Sidecar (sidecar-blockchain)
- [`src/tron/TronGridClient.ts`](../../sidecar-blockchain/src/tron/TronGridClient.ts) — `AccountBalances` interface + `getAccountBalances` method
- [`src/tron/TronGridClient.test.ts`](../../sidecar-blockchain/src/tron/TronGridClient.test.ts) — 5 yeni Vitest (account map parse, empty data, string balance coerce, malformed trc20 ignore, HTTP 5xx)
- [`src/api/walletHandlers.ts`](../../sidecar-blockchain/src/api/walletHandlers.ts) — `walletBalancesHandler` factory + contract→symbol map + 100 address cap
- [`src/api/walletHandlers.test.ts`](../../sidecar-blockchain/src/api/walletHandlers.test.ts) — yeni dosya, 6 Vitest (missing addresses / empty / cap / type / batch happy / upstream failure)
- [`src/api/routes.ts`](../../sidecar-blockchain/src/api/routes.ts) — `POST /api/wallet/balances` route

### Backend
- [`backend/src/Skinora.Shared/Enums/BlockchainTransactionType.cs`](../../backend/src/Skinora.Shared/Enums/BlockchainTransactionType.cs) — SWEEP value
- [`backend/src/Skinora.Shared/Enums/AuditAction.cs`](../../backend/src/Skinora.Shared/Enums/AuditAction.cs) — RECONCILIATION_MISMATCH value
- [`backend/src/Modules/Skinora.Platform/Application/Audit/AuditLogCategoryMap.cs`](../../backend/src/Modules/Skinora.Platform/Application/Audit/AuditLogCategoryMap.cs) — SECURITY_EVENT eşlemesi
- [`backend/src/Modules/Skinora.Realtime/Application/Contracts/NotificationRealtimePayloads.cs`](../../backend/src/Modules/Skinora.Realtime/Application/Contracts/NotificationRealtimePayloads.cs) — `AdminReconciliationMismatch` record
- [`backend/src/Modules/Skinora.Realtime/Application/INotificationRealtimePublisher.cs`](../../backend/src/Modules/Skinora.Realtime/Application/INotificationRealtimePublisher.cs) — `PublishAdminReconciliationMismatchAsync` method
- [`backend/src/Modules/Skinora.Realtime/Infrastructure/SignalRNotificationRealtimePublisher.cs`](../../backend/src/Modules/Skinora.Realtime/Infrastructure/SignalRNotificationRealtimePublisher.cs) — impl + event sabiti
- [`backend/src/Modules/Skinora.Transactions/Application/PaymentAddresses/IBlockchainSidecarClient.cs`](../../backend/src/Modules/Skinora.Transactions/Application/PaymentAddresses/IBlockchainSidecarClient.cs) — `GetWalletBalancesAsync` + `BlockchainSidecarBalancesResult` + `BlockchainSidecarAddressBalances` record
- [`backend/src/Modules/Skinora.Transactions/Application/PaymentAddresses/HttpBlockchainSidecarClient.cs`](../../backend/src/Modules/Skinora.Transactions/Application/PaymentAddresses/HttpBlockchainSidecarClient.cs) — impl + `BalancesRequest`/`BalancesResponse`/`BalancesRow` records
- [`backend/src/Modules/Skinora.Transactions/Application/Reconciliation/IReconciliationService.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Reconciliation/IReconciliationService.cs) — yeni dosya, interface + `ReconciliationOutcome` + `ReconciliationScope` enum
- [`backend/src/Skinora.API/Services/Reconciliation/ReconciliationService.cs`](../../backend/src/Skinora.API/Services/Reconciliation/ReconciliationService.cs) — yeni dosya, 3-scope reconciliation impl
- [`backend/src/Skinora.API/Services/Reconciliation/ReconciliationJob.cs`](../../backend/src/Skinora.API/Services/Reconciliation/ReconciliationJob.cs) — yeni dosya, Hangfire wrapper
- [`backend/src/Skinora.API/Services/Reconciliation/ReconciliationJobRegistrar.cs`](../../backend/src/Skinora.API/Services/Reconciliation/ReconciliationJobRegistrar.cs) — yeni dosya, IHostedService + SystemSetting cron reader
- [`backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsCatalog.cs`](../../backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsCatalog.cs) — 3 yeni catalog entry
- [`backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs`](../../backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs) — 3 yeni seed row (54, 55, 56)
- [`backend/src/Skinora.Shared/Persistence/Migrations/20260517155738_T76_AddReconciliationSettings.cs`](../../backend/src/Skinora.Shared/Persistence/Migrations/20260517155738_T76_AddReconciliationSettings.cs) — yeni migration (3 InsertData)
- [`backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs`](../../backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs) — 3 SystemSetting HasData entry
- [`backend/src/Skinora.API/Configuration/TransactionsModule.cs`](../../backend/src/Skinora.API/Configuration/TransactionsModule.cs) — DI: IReconciliationService + ReconciliationJob + ReconciliationJobRegistrar hosted

### Test dosyaları
- [`backend/tests/Skinora.API.Tests/Unit/Reconciliation/ReconciliationServiceTests.cs`](../../backend/tests/Skinora.API.Tests/Unit/Reconciliation/ReconciliationServiceTests.cs) — 11 unit (3 skip + 4 deposit + 2 hot + 1 cold + 1 multi-token)
- [`backend/tests/Skinora.API.Tests/Unit/Reconciliation/ReconciliationJobTests.cs`](../../backend/tests/Skinora.API.Tests/Unit/Reconciliation/ReconciliationJobTests.cs) — 2 unit (delegate happy path + exception rethrow)
- [`backend/tests/Skinora.Shared.Tests/Unit/EnumTests.cs`](../../backend/tests/Skinora.Shared.Tests/Unit/EnumTests.cs) — `BlockchainTransactionType_ShouldHave10Values` + SWEEP InlineData + `AuditAction_ShouldHave22Values` + RECONCILIATION_MISMATCH InlineData
- [`backend/tests/Skinora.Platform.Tests/Unit/Audit/AuditLogCategoryMapTests.cs`](../../backend/tests/Skinora.Platform.Tests/Unit/Audit/AuditLogCategoryMapTests.cs) — SECURITY_EVENT 3 değer beklentisi (RECONCILIATION_MISMATCH dahil)
- [`backend/tests/Skinora.Transactions.Tests/Integration/PaymentAddresses/StubBlockchainSidecarClient.cs`](../../backend/tests/Skinora.Transactions.Tests/Integration/PaymentAddresses/StubBlockchainSidecarClient.cs) — `GetWalletBalancesAsync` stub
- [`backend/tests/Skinora.Notifications.Tests/TestSupport/RecordingNotificationRealtimePublisher.cs`](../../backend/tests/Skinora.Notifications.Tests/TestSupport/RecordingNotificationRealtimePublisher.cs) — `PublishAdminReconciliationMismatchAsync` impl
- [`backend/tests/Skinora.Steam.Tests/TestSupport/RecordingNotificationRealtimePublisher.cs`](../../backend/tests/Skinora.Steam.Tests/TestSupport/RecordingNotificationRealtimePublisher.cs) — `PublishAdminReconciliationMismatchAsync` impl

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Günlük reconciliation: on-chain bakiye vs platform ledger karşılaştırma | ✓ | `ReconciliationJob` Hangfire recurring (`reconciliation.schedule_cron` default `0 3 * * *` UTC); `ReconciliationService.RunAsync` 3 scope (DepositAddress/HotWallet/ColdWallet) on-chain (sidecar `getAccountBalances`) vs ledger (BlockchainTransaction CONFIRMED sum + ColdWalletTransfer sum); ReconciliationServiceTests 9 happy-path test PASS (`RunAsync_DepositAddress_BalanceMatchesLedger_NoMismatchRecorded`, `RunAsync_HotWallet_AccountsForSweepInflowMinusPayoutAndColdTransfer`, `RunAsync_ColdWallet_SumsColdWalletTransferLedger`, `RunAsync_DepositAddress_SweptToHotWallet_BalanceCollapsesToZero`, `RunAsync_DepositAddress_InFlightDetectedExcludedFromExpected`, `RunAsync_HotWalletUnconfigured_SkipsHotScopeWithoutFailing`, `RunAsync_SidecarUnavailable_AbortsRunWithoutAudit`, `RunAsync_NoActiveDepositsAndNoWalletsConfigured_NoOpAndReturnsZeroOutcome`, `RunAsync_MultiToken_OnlyMismatchingTokenRaised`); ReconciliationJobTests 2 PASS |
| 2 | Uyumsuzluk tespit edilirse admin alert | ✓ | `EvaluateAndRecordAsync` mismatch'te `RecordMismatchAsync` çağırır → `AuditLog` row (Action=RECONCILIATION_MISMATCH, EntityType=scope, NewValue=JSON envelope {token, expected, actual, delta, blockNumber}) + `INotificationRealtimePublisher.PublishAdminReconciliationMismatchAsync` (SignalR Clients.All broadcast); `RunAsync_DepositAddress_ShortfallRaisesMismatchAndPushesAdmin` + `RunAsync_HotWallet_OnChainSurplusRaisesMismatch` testleri AuditLog row + publisher.Mismatches capture'ını doğrular |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (Sidecar) | ✓ 141/141 passed | `npm test` — `getAccountBalances` 5 + `walletBalancesHandler` 6 yeni, regresyon yok |
| Unit (Backend Shared) | ✓ 197/197 passed | `dotnet test Skinora.Shared.Tests` — enum count + InlineData güncellemesi |
| Unit (Backend Platform) | ✓ 111/111 passed | `dotnet test Skinora.Platform.Tests` — AuditLogCategoryMap SECURITY_EVENT güncellemesi |
| Unit (Backend Reconciliation) | ✓ 13/13 passed | `dotnet test Skinora.API.Tests --filter ReconciliationServiceTests\|ReconciliationJobTests` |
| Unit (Backend full) | ✓ 374/374 passed (Skinora.API.Tests) | `dotnet test Skinora.API.Tests --filter "Category!=Integration"` |
| Unit (Backend regression) | ✓ 25 Realtime + 33 Steam + 52 Fraud + 25 Disputes + 641 Transactions PASS | Hiçbir modülde regresyon yok |
| Build (Backend Release) | ✓ 0 Warning, 0 Error | `dotnet build -c Release` |
| Build (Sidecar) | ✓ tsc clean | `npm run build` |
| Format | ✓ Δ=0 | `dotnet format --verify-no-changes` |
| Integration | — | Plan "test beklentisi yok (operasyonel job)" — Hangfire dispatch + sidecar HTTP round-trip CI testcontainer'da F4 Gate Check kapsamında doğrulanır |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Doğrulama chat'i bekleniyor |
| Bulgu sayısı | 0 (self-check) |
| Düzeltme gerekli mi | Hayır |

## Altyapı Değişiklikleri

- **Migration:** Var — `20260517155738_T76_AddReconciliationSettings` (3 SystemSetting InsertData: `reconciliation.schedule_cron` Default `0 3 * * *`, `reconciliation.hot_wallet_address` Unconfigured, `reconciliation.cold_wallet_address` Unconfigured). Idempotent idempotent: ikinci `database update` no-op (mevcut Id'ler).
- **Config/env değişikliği:** Yok backend tarafında. Sidecar tarafında `TRON_USDT_CONTRACT` / `TRON_USDC_CONTRACT` zaten T70-T75 ile mevcut env değişkenleri.
- **Docker değişikliği:** Yok.
- **Yeni dependency:** Yok (TronGridClient + Express handler + Hangfire job pattern hepsi mevcut altyapıdan).
- **Hangfire recurring job:** `blockchain-reconciliation` ID, default cron `0 3 * * *` UTC. Cron `reconciliation.schedule_cron` SystemSetting ile override edilebilir (host restart gerekir).

## Commit & PR

- **Branch:** `task/T76-blockchain-reconciliation-job`
- **Commit:** TBD (commit'lenecek)
- **PR:** TBD
- **CI:** TBD

## Known Limitations / Follow-up

- **K1 — Sweep dispatcher T-future:** `BlockchainTransactionType.SWEEP` enum value mevcut, ledger satırı kategorisi hazır; ancak sweep akışını fiilen tetikleyen consumer henüz yok. Plan: `PaymentReceivedEvent` outbox consumer + sidecar `POST /api/transfer/sweep` çağrısı (T73 transfer primitive zaten hazır). Reconciliation hot wallet expected = `sum(SWEEP) − sum(outflow) − sum(ColdWalletTransfer)` mantığı; sweep akışı yok iken hot wallet bakiyesi de boş, mismatch oluşmaz. Sweep akışı eklendiğinde reconciliation otomatik devreye girer.
- **K2 — Email/Telegram/Discord dispatch:** Mismatch notify mekanizması AuditLog + SignalR broadcast ile sınırlı; persistent kanallar (admin email, Telegram, Discord) henüz yok. Plan: T78 (Resend) / T79 (Telegram) / T80 (Discord) sonrası `AdminNotificationDispatcher` mismatch event'ini abonelere fan-out etsin.
- **K3 — Runtime cron override:** `reconciliation.schedule_cron` SystemSetting değişikliği host restart gerektirir (registrar startup'ta okur). Plan: T96 admin tooling — runtime cron override + Hangfire `RecurringJob.AddOrUpdate` yeniden çağrısı.
- **K4 — Deposit address cap:** Tek run en fazla 98 deposit adres + hot + cold = 100 (sidecar handler `MAX_BALANCE_ADDRESSES` = 100). Üzerinde truncate + warn log. Production'da aktif deposit > 98 olursa T-future: pagination + per-token batch'leme.
- **K5 — TRX reconciliation:** Platform tarafında TRX-denominated ledger yok (energy delegation hot wallet sweeper account TRX'inden çıkar). TRX kapsamı dışı; sidecar response'ta gelse de service hesaba katmaz. Plan: T-future operasyonel TRX accounting (sweeper hesap bakiye + energy stake/unstake olayları).
- **K6 — Reorg post-hoc handling:** Plan T73 K5 "reorg post-hoc reconciliation" T76'ya devredilmişti. Bu mevcut tasarımda kapanır: reorg sonrası BlockchainTransaction Status değişikliği (CONFIRMED → tekrar PENDING) reconciliation expected'a otomatik yansır. Ek bir reorg-spesifik kod yolu yok — finality (20 blok) zaten reorg riskini minimal tutar.
- **K7 — Sidecar prettier drift:** Mevcut 37 dosyalık pre-existing drift (T73 K6 havuzu) korunuyor; T76 yeni dosyaları da drift içinde. Chore PR ayrı (T-future toplu format pass).
- **K8 — Multi-token per address:** Reconciliation USDT + USDC sabit allowlist (08 §3.3). Yeni stablecoin eklenirse: `ReconciliationService.SupportedTokens` + sidecar `buildContractToSymbol` + config env değişkenleri ekleyerek 1 satır config değişikliği.
- **K9 — Snapshot/ledger race window:** Sidecar block N snapshot, backend ledger tek transaction'lık atomik değil — N+1 blokta CONFIRMED'a geçen bir BlockchainTransaction reconciliation expected'da yer alır ama snapshot'ta görünmez. Tolerans 0 olduğu için bu mismatch raporlanır. Pratikte günlük cadence'te risk minimal; çoğu late-finalization birkaç saniye içinde gerçekleşir. T-future: snapshot block-bound expected filter (`Status == CONFIRMED AND BlockNumber <= snapshotBlockNumber`).

## Notlar

- **Working tree:** temiz (Adım -1).
- **Main CI startup:** 3/3 success (`25993624687`, `25993624655`, `25990227493` — T75 #116 + T74 #115).
- **Dış varsayım:** TronGrid `/v1/accounts/{address}` extended endpoint TRX balance + trc20 contract→raw map'i tek call'da verir (08 §3.4 paging endpoint'ten ayrı, T71 monitor'a değmez). HD wallet master mnemonic — sidecar runtime config, reconciliation kapsamı dışı. Mevcut `BlockchainTransactionType` enum'da SWEEP yok — T73 K2 forward-deferred → T76 scope'una dahil edildi (proje sahibi 2026-05-17 onayı).
- **Scope kararı:** SWEEP enum dahil (proje sahibi onayı), unconfigured wallet → skip + warn, cron default 03:00 UTC, test stratejisi unit + integration (integration F4 Gate Check'te testcontainer ile doğrulanır — plan "yok" demesi rağmen mismatch detection critical logic için unit zorunlu).
- **Architectural pattern:** ReconciliationService Skinora.API/Services/Reconciliation altında yaşar (T63 AdminTransactionQueryService pattern). Skinora.Realtime → Skinora.Transactions referans verdiği için ReconciliationService Skinora.Transactions modülünde yaşayamaz (circular dependency). Interface (`IReconciliationService` + `ReconciliationScope`) Skinora.Transactions.Application.Reconciliation'da kalır — domain-level public contract.
- **Decimal precision:** 6 decimals (USDT/USDC), `MidpointRounding.ToZero` (truncation, 09 §14.3 finansal hesaplama invariant). Sidecar raw uint string olarak gönderir, backend `decimal.Parse(InvariantCulture)` + `/10^6` ile karşılaştırılabilir scale'e çevirir.
- **CI watch:** Açılan PR için `gh run watch` ile concluded + success bekleneceği [`feedback_claude_watches_ci_always.md`](.claude/memory/feedback_claude_watches_ci_always.md) gereğince Claude tarafından izlenir.
