# T77 — Blockchain Sidecar hot wallet yönetimi

**Faz:** F4 | **Durum:** ⏳ Yapım bitti (doğrulama bekliyor) | **Tarih:** 2026-05-17

---

## Yapılan İşler

- **Sidecar `TransferService.coldWalletTransfer`** — `payout` mirror (hot wallet'tan `HOT_WALLET_PRIVATE_KEY` ile signing, TRC-20 contract çözümlemesi, scale-6 raw unit dönüşümü) + ayrı log etiketi `Broadcasting COLD_WALLET_TRANSFER (hot -> cold)` ve metric flow tag'i (`type=cold-wallet-transfer`). Admin yetkisi sidecar'da değil backend'de doğrulanır; sidecar sadece imzalayıcıdır.
- **Sidecar `POST /api/transfer/cold-wallet` endpoint** — `coldWalletTransferHandler` factory + route + lifecycle: body `{coldTransferId, toColdAddress, amount, token}` (USDT|USDC), response `{txHash}`. T77 placeholder GET endpoint (`/wallet/hot-wallet-balance` 501) kaldırıldı — bu fonksiyon backend monitor job tarafından `POST /api/wallet/balances` (T76) üzerinden karşılanıyor.
- **Backend `IBlockchainSidecarClient.SendHotToColdTransferAsync`** — yeni method + `HotToColdTransferRequest` record + `BlockchainSidecarTransferResult` discriminated outcome. `HttpBlockchainSidecarClient` impl: `X-Internal-Key` auth, 4xx → `InvalidRequest`, 5xx/timeout → `Unavailable`, başarısız payload parse → `Unavailable`.
- **`AuditAction.COLD_WALLET_TRANSFER_INITIATED`** (FUND_MOVEMENT) + **`AuditAction.HOT_WALLET_THRESHOLD_BREACHED`** (SECURITY_EVENT) — `AuditLogCategoryMap` güncellendi (FUND_MOVEMENT 5→6 satır, SECURITY_EVENT 3→4 satır).
- **`NotificationRealtimePayloads.AdminHotWalletThresholdBreached`** record (Token/Direction/Threshold/Actual/BlockNumber/DetectedAt) + `INotificationRealtimePublisher.PublishAdminHotWalletThresholdBreachedAsync` + `SignalRNotificationRealtimePublisher` impl (`Clients.All` broadcast, `AdminReconciliationMismatch` pattern mirror).
- **`HotWalletService`** (Skinora.API/Services/HotWallet — cross-module, ReconciliationService ile aynı placement gerekçesi: Transactions modülü Payments/Realtime'ı ref etmez):
  - `InitiateColdTransferAsync(amount, token, adminId)` 3 stage:
    1. Amount validation — pozitif, scale-6 (09 §14.3 finansal invariant)
    2. SystemSetting okuma — `reconciliation.hot_wallet_address` / `reconciliation.cold_wallet_address` (T76 NONE-sentinel pattern)
    3. Sidecar broadcast → success'te ColdWalletTransfer ledger row + AuditLog `COLD_WALLET_TRANSFER_INITIATED` row tek `SaveChanges` ile yazılır (reconciliation T76 cold scope eşleştirme açısından atomik görünür)
  - Sidecar `Unavailable`/`InvalidRequest`/`NotConfigured` → ledger ve audit YAZILMAZ (idempotent retry için kritik)
  - Outcome ayrımcı tip: `Success`/`InvalidAmount`/`HotWalletNotConfigured`/`ColdWalletNotConfigured`/`SidecarUnavailable`
- **`HotWalletMonitorService`** (Skinora.API/Services/HotWallet) — periyodik bakiye monitor:
  - SystemSetting okuma — `reconciliation.hot_wallet_address` + `hot_wallet_limit` + `hot_wallet.trx_balance_minimum`
  - Sidecar `GetWalletBalancesAsync` (T76 endpoint) ile tek-adres snapshot
  - **USDT > limit / USDC > limit → Upper breach** (per stablecoin, aynı `hot_wallet_limit` decimal applied per token)
  - **TRX < minimum → Lower breach** (gas alert)
  - Breach'te AuditLog `HOT_WALLET_THRESHOLD_BREACHED` row (EntityType=`HotWallet`, EntityId=token, NewValue=JSON {token, direction, threshold, actual, blockNumber}) + SignalR `AdminHotWalletThresholdBreached` broadcast — `ReconciliationService.RecordMismatchAsync` pattern mirror
- **`HotWalletMonitorJob`** (Hangfire entry-point) + **`HotWalletMonitorJobRegistrar`** (`IHostedService`) — `ReconciliationJobRegistrar` pattern; cron `hot_wallet.monitor_cron` SystemSetting'den okunur (default `*/15 * * * *`).
- **2 yeni SystemSetting** (idx 57–58):
  - `hot_wallet.monitor_cron` — Default `"*/15 * * * *"`. Runtime override host restart gerektirir (T96 devir).
  - `hot_wallet.trx_balance_minimum` — Default `"100"` (TRX). 100 TRX ≈ 50 TRC-20 transfer gas worst-case headroom (MVP ölçeği).
- **`AdminWalletsController`** — `POST /api/v1/admin/wallets/hot-to-cold-transfer` endpoint:
  - Auth: `Permission:MANAGE_SETTINGS` (mevcut yetki kullanıldı — 11 catalog satırı korundu; K-future `MANAGE_WALLETS` ayrımı 07 §9.11 doc change gerektirir).
  - RateLimit bucket `admin-write`.
  - Body `{amount, token}` → outcome → HTTP envelope (`200`/`400 INVALID_AMOUNT|INVALID_TOKEN`/`401`/`422 HOT_WALLET_NOT_CONFIGURED|COLD_WALLET_NOT_CONFIGURED`/`502 SIDECAR_UNAVAILABLE`).
  - Response: `{coldTransferId, txHash, amount, token, fromAddress, toAddress}`.
- **Migration `T77_AddHotWalletMonitorSettings`** — 2 SystemSetting `InsertData` (Ids 0aa51010-…0039/003a); model snapshot + Designer otomatik scaffold.
- **DI:** `TransactionsModule.cs` → `IHotWalletService`/`IHotWalletMonitorService` scoped + `HotWalletMonitorJob` scoped + `HotWalletMonitorJobRegistrar` hosted service.

## Etkilenen Modüller / Dosyalar

### Sidecar (sidecar-blockchain)
- [`src/transfer/TransferService.ts`](../../sidecar-blockchain/src/transfer/TransferService.ts) — `coldWalletTransfer` method + `ColdWalletTransferRequest` interface
- [`src/transfer/TransferService.test.ts`](../../sidecar-blockchain/src/transfer/TransferService.test.ts) — 3 yeni Vitest (broadcast happy + missing creds + scale violation)
- [`src/api/transferHandlers.ts`](../../sidecar-blockchain/src/api/transferHandlers.ts) — `coldWalletTransferHandler` factory + `handleTransferError` flow tag genişletmesi
- [`src/api/routes.ts`](../../sidecar-blockchain/src/api/routes.ts) — `POST /api/transfer/cold-wallet` route, T77 placeholder GET endpoint kaldırıldı

### Backend
- [`backend/src/Skinora.Shared/Enums/AuditAction.cs`](../../backend/src/Skinora.Shared/Enums/AuditAction.cs) — COLD_WALLET_TRANSFER_INITIATED + HOT_WALLET_THRESHOLD_BREACHED enum value
- [`backend/src/Modules/Skinora.Platform/Application/Audit/AuditLogCategoryMap.cs`](../../backend/src/Modules/Skinora.Platform/Application/Audit/AuditLogCategoryMap.cs) — FUND_MOVEMENT + SECURITY_EVENT eşlemeleri
- [`backend/src/Modules/Skinora.Realtime/Application/Contracts/NotificationRealtimePayloads.cs`](../../backend/src/Modules/Skinora.Realtime/Application/Contracts/NotificationRealtimePayloads.cs) — `AdminHotWalletThresholdBreached` record
- [`backend/src/Modules/Skinora.Realtime/Application/INotificationRealtimePublisher.cs`](../../backend/src/Modules/Skinora.Realtime/Application/INotificationRealtimePublisher.cs) — `PublishAdminHotWalletThresholdBreachedAsync`
- [`backend/src/Modules/Skinora.Realtime/Infrastructure/SignalRNotificationRealtimePublisher.cs`](../../backend/src/Modules/Skinora.Realtime/Infrastructure/SignalRNotificationRealtimePublisher.cs) — impl + event sabiti
- [`backend/src/Modules/Skinora.Transactions/Application/PaymentAddresses/IBlockchainSidecarClient.cs`](../../backend/src/Modules/Skinora.Transactions/Application/PaymentAddresses/IBlockchainSidecarClient.cs) — `SendHotToColdTransferAsync` + `HotToColdTransferRequest` + `BlockchainSidecarTransferResult`
- [`backend/src/Modules/Skinora.Transactions/Application/PaymentAddresses/HttpBlockchainSidecarClient.cs`](../../backend/src/Modules/Skinora.Transactions/Application/PaymentAddresses/HttpBlockchainSidecarClient.cs) — impl + `ColdTransferRequestBody`/`ColdTransferResponse` records
- [`backend/src/Modules/Skinora.Transactions/Application/Wallets/IHotWalletService.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Wallets/IHotWalletService.cs) — yeni dosya, interface + `HotWalletColdTransferOutcome` discriminated outcome
- [`backend/src/Skinora.API/Services/HotWallet/HotWalletService.cs`](../../backend/src/Skinora.API/Services/HotWallet/HotWalletService.cs) — yeni dosya, admin orchestrator impl
- [`backend/src/Skinora.API/Services/HotWallet/HotWalletMonitorService.cs`](../../backend/src/Skinora.API/Services/HotWallet/HotWalletMonitorService.cs) — yeni dosya, `IHotWalletMonitorService` interface + impl + `HotWalletMonitorOutcome` record
- [`backend/src/Skinora.API/Services/HotWallet/HotWalletMonitorJob.cs`](../../backend/src/Skinora.API/Services/HotWallet/HotWalletMonitorJob.cs) — yeni dosya, Hangfire wrapper
- [`backend/src/Skinora.API/Services/HotWallet/HotWalletMonitorJobRegistrar.cs`](../../backend/src/Skinora.API/Services/HotWallet/HotWalletMonitorJobRegistrar.cs) — yeni dosya, IHostedService + SystemSetting cron reader
- [`backend/src/Skinora.API/Controllers/AdminWalletsController.cs`](../../backend/src/Skinora.API/Controllers/AdminWalletsController.cs) — yeni dosya, `AD20 POST /admin/wallets/hot-to-cold-transfer` endpoint + request/response records
- [`backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsCatalog.cs`](../../backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsCatalog.cs) — 2 yeni catalog entry
- [`backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs`](../../backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs) — 2 yeni seed row (57, 58)
- [`backend/src/Skinora.Shared/Persistence/Migrations/20260517180331_T77_AddHotWalletMonitorSettings.cs`](../../backend/src/Skinora.Shared/Persistence/Migrations/20260517180331_T77_AddHotWalletMonitorSettings.cs) — yeni migration (2 InsertData)
- [`backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs`](../../backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs) — 2 SystemSetting HasData entry
- [`backend/src/Skinora.API/Configuration/TransactionsModule.cs`](../../backend/src/Skinora.API/Configuration/TransactionsModule.cs) — DI: IHotWalletService + IHotWalletMonitorService + HotWalletMonitorJob + HotWalletMonitorJobRegistrar hosted

### Test dosyaları
- [`backend/tests/Skinora.API.Tests/Unit/HotWallet/HotWalletServiceTests.cs`](../../backend/tests/Skinora.API.Tests/Unit/HotWallet/HotWalletServiceTests.cs) — 9 unit (4 validation + 1 hot/1 cold not configured + 1 sidecar unavailable + 2 success path)
- [`backend/tests/Skinora.API.Tests/Unit/HotWallet/HotWalletMonitorServiceTests.cs`](../../backend/tests/Skinora.API.Tests/Unit/HotWallet/HotWalletMonitorServiceTests.cs) — 8 unit (skip + sidecar fail + no breach + USDT upper + multi-token upper + TRX lower + no limit configured + NONE sentinel)
- [`backend/tests/Skinora.Shared.Tests/Unit/EnumTests.cs`](../../backend/tests/Skinora.Shared.Tests/Unit/EnumTests.cs) — `AuditAction_ShouldHave24Values` + 2 InlineData
- [`backend/tests/Skinora.Platform.Tests/Unit/Audit/AuditLogCategoryMapTests.cs`](../../backend/tests/Skinora.Platform.Tests/Unit/Audit/AuditLogCategoryMapTests.cs) — FUND_MOVEMENT 5→6, SECURITY_EVENT 3→4, 2 yeni InlineData
- [`backend/tests/Skinora.Platform.Tests/Integration/SeedDataTests.cs`](../../backend/tests/Skinora.Platform.Tests/Integration/SeedDataTests.cs) — Seed count 56→58, configured 35→37 (+2 yeni key listede)
- [`backend/tests/Skinora.Transactions.Tests/Integration/PaymentAddresses/StubBlockchainSidecarClient.cs`](../../backend/tests/Skinora.Transactions.Tests/Integration/PaymentAddresses/StubBlockchainSidecarClient.cs) — `SendHotToColdTransferAsync` stub
- [`backend/tests/Skinora.API.Tests/Unit/Reconciliation/ReconciliationServiceTests.cs`](../../backend/tests/Skinora.API.Tests/Unit/Reconciliation/ReconciliationServiceTests.cs) — Stub publisher + sidecar `SendHotToColdTransferAsync`/`HotToColdTransfer` no-op impl
- [`backend/tests/Skinora.Notifications.Tests/TestSupport/RecordingNotificationRealtimePublisher.cs`](../../backend/tests/Skinora.Notifications.Tests/TestSupport/RecordingNotificationRealtimePublisher.cs) — `PublishAdminHotWalletThresholdBreachedAsync` impl
- [`backend/tests/Skinora.Steam.Tests/TestSupport/RecordingNotificationRealtimePublisher.cs`](../../backend/tests/Skinora.Steam.Tests/TestSupport/RecordingNotificationRealtimePublisher.cs) — `PublishAdminHotWalletThresholdBreachedAsync` impl

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Hot wallet bakiye monitoring (TRX + USDT + USDC) | ✓ | `HotWalletMonitorJob` Hangfire recurring (`hot_wallet.monitor_cron` default `*/15 * * * *`); `HotWalletMonitorService.RunAsync` sidecar `GetWalletBalancesAsync` ile USDT/USDC/TRX bakiyelerini okur, eşik karşılaştırmaları yapar; `HotWalletMonitorServiceTests.RunAsync_BalancesAllWithinThresholds_NoBreach` (200 USDT + 750 USDC + 200 TRX vs 1000 USDT/USDC limit + 100 TRX min) PASS, snapshot endpoint çağrısı + block number tracking doğrulandı. |
| 2 | Limit aşımında admin alert (USDT/USDC > hot_wallet_limit) | ✓ | `RunAsync_UsdtAboveLimit_EmitsUpperBreach` (1500 USDT vs 1000 limit → `Upper` breach + AuditLog `HOT_WALLET_THRESHOLD_BREACHED` + SignalR `AdminHotWalletThresholdBreached`) PASS; `RunAsync_BothStablecoinsExceedLimit_EmitsTwoUpperBreaches` PASS — token başına ayrı breach. Cold wallet transferi 05 §3.3 gereği admin tarafından `POST /admin/wallets/hot-to-cold-transfer` ile manuel başlatılır. |
| 3 | Manuel cold wallet transferi sonrası ColdWalletTransfer ledger kaydı (tx hash + tutar + tarih) | ✓ | `AdminWalletsController.InitiateColdTransfer` → `HotWalletService.InitiateColdTransferAsync` → sidecar `POST /api/transfer/cold-wallet` success → ColdWalletTransfer row (Amount + Token + FromAddress=hot + ToAddress=cold + TxHash + InitiatedByAdminId + CreatedAt) + AuditLog `COLD_WALLET_TRANSFER_INITIATED` tek `SaveChanges`'da yazılır; `HotWalletServiceTests.InitiateColdTransferAsync_SidecarSuccess_WritesLedgerAndAudit` ledger field-by-field doğrular (250.5 USDC + correct addresses + tx hash + admin id), `_SidecarSuccessTwice_TwoLedgerRowsTwoAudits` çoklu transferleri doğrular. Sidecar `Unavailable`/`InvalidRequest` durumunda hiçbir row yazılmaz (`_SidecarUnavailable_NoLedgerOrAuditWritten` PASS — idempotent retry için kritik). |
| 4 | Hot wallet TRX bakiyesi eşik altında → admin alert | ✓ | `RunAsync_TrxBelowMinimum_EmitsLowerBreach` (50 TRX vs 100 minimum → `Lower` direction breach + AuditLog row (EntityId=TRX) + SignalR push) PASS. SystemSetting `hot_wallet.trx_balance_minimum` default 100 TRX (MVP ölçek ≈ 50 TRC-20 gas headroom); admin runtime'da SystemSettings UI'dan değiştirebilir. |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (Sidecar) | ✓ 144/144 passed | `npx vitest run` — 17 TransferService (+3 cold-wallet test), 24 PaymentMonitorRules, 15 HdWallet, 10 TronDelegation, vb. regresyon yok |
| Lint (Sidecar) | ✓ 0 issues | `npm run lint` |
| Unit (Backend Shared) | ✓ 209/209 passed | `dotnet test Skinora.Shared.Tests` — `AuditAction_ShouldHave24Values` + InlineData (önce 197 → +12 satır cover) |
| Unit (Backend Platform) | ✓ 115/115 passed | `dotnet test Skinora.Platform.Tests` — AuditLogCategoryMap FUND_MOVEMENT 6 satır + SECURITY_EVENT 4 satır beklentisi |
| Unit (Backend API HotWallet) | ✓ 20/20 passed | `dotnet test --filter HotWallet` — HotWalletServiceTests 9 + HotWalletMonitorServiceTests 8 + (regresyon stub publisher 3 paylaşımlı) |
| Unit (Backend API Reconciliation) | ✓ 13/13 passed | `dotnet test --filter Reconciliation` — T76 regresyon, stub publisher genişlemesi etkisiz |
| Unit (Backend API full) | ✓ 44/44 passed | `dotnet test --filter "FullyQualifiedName!~Integration"` |
| Build (Backend Release) | ✓ 0 Warning, 0 Error | `dotnet build Skinora.sln -c Release -p:TreatWarningsAsErrors=true` |
| Build (Sidecar) | ✓ tsc clean | `npm run build` |
| Format | ✓ Δ=0 | `dotnet format --verify-no-changes` |
| Integration | — | Lokal SQL Server yok; SeedDataTests (count 58 + configured 37) + diğer integration testleri CI services:mssql üzerinde çalışır (T11.3 pattern). |

## Altyapı Değişiklikleri

- **Migration:** 1 yeni — `T77_AddHotWalletMonitorSettings` (2 SystemSetting `InsertData`, 0 tablo/şema değişikliği).
- **Config/env:** Yok — `hot_wallet.monitor_cron` + `hot_wallet.trx_balance_minimum` admin tarafından SystemSettings UI'dan yönetilir; sidecar `HOT_WALLET_PRIVATE_KEY` zaten T73'te kullanılıyordu.
- **Docker:** Yok.
- **Yeni dış bağımlılık:** Yok.

## Mini güvenlik kontrolü

- **Secret sızıntısı:** Yok — hot wallet private key sidecar Docker secret olarak T73'te yapılandırıldı; backend orchestrator key görmez.
- **Auth/authorization:** Yeni endpoint `POST /admin/wallets/hot-to-cold-transfer` `Permission:MANAGE_SETTINGS` policy + `admin-write` rate limit bucket altında — anonim erişim 401, yetkisiz admin 403, super-admin bypass T06 PermissionAuthorizationHandler ile.
- **Input validation:** Amount scale-6 pozitif kontrol (`HotWalletService.InitiateColdTransferAsync`), Token enum parse, request body NULL guard. Sidecar boundary'sinde de aynı kontroller (defense in depth).
- **Yeni dış bağımlılık:** Yok — TronWeb / steam-totp gibi yeni paket eklenmedi.

## Commit & PR

- Branch: `task/T77-hot-wallet-management`
- Commit: pending
- PR: pending
- CI: pending (Claude izleyecek — INSTRUCTIONS §3.2 evrensel kural)

## Known Limitations / Follow-up

- **K1 — Permission ayrımı (T-future):** Endpoint `MANAGE_SETTINGS` permission'unu reuse ediyor. Dedicated `MANAGE_WALLETS` permission'u (07 §9.11 catalog + 04 §8.8 yetki matrix değişikliği gerektirir) operasyonel ayrım için T-future doc PR'ı olarak değerlendirilebilir. MVP için super-admin bypass + MANAGE_SETTINGS kapsamı yeterli güvenlik tier.
- **K2 — Runtime cron override (T96 devir):** `hot_wallet.monitor_cron` SystemSetting startup'ta okunur — admin UI'dan değiştirildiğinde host restart gerekir. T96 admin tooling bunu runtime'a alacak (ReconciliationJobRegistrar ile aynı kısıt — T76 K3).
- **K3 — Single threshold per stablecoin (M-future):** `hot_wallet_limit` decimal'i USDT ve USDC bakiyelerine ayrı ayrı uygulanır (aynı eşik). İki ayrı eşik istenirse `hot_wallet.usdt_limit` / `hot_wallet.usdc_limit` ayrı satırlar olarak eklenir; 06 §3.17 catalog satır eklemesi gerektirir.
- **K4 — TRX bakiye precision (gözlem):** TronGrid TRX bakiyesini SUN cinsinden raw int olarak döner — sidecar `getAccountBalances` zaten TRX'i `tokens.TRX` map'inde raw uint olarak yayınlar. Monitor scale-6 (1 TRX = 10^6 SUN) ile bölerek karşılaştırır; eşik decimal "100" gibi düz TRX cinsinden tanımlı.
- **K5 — Endpoint-layer integration test (T-future):** AdminWalletsController için JWT/Auth/RateLimit/Envelope tam zincirli integration test scope dışında — invariantlar HotWalletService unit testleri (9 senaryo) + AdminSettingsEndpointTests'in mevcut Permission policy gating coverage'ı ile dolaylı doğrulanmış. Tam tile-level test isteğe bağlı follow-up PR olarak değerlendirilebilir.
- **K6 — Email/Telegram/Discord alert (T78–T80 devir):** Threshold breach şu an AuditLog + SignalR (admin browser online ise) ile sınırlı. T78 Email sender / T79 Telegram bot / T80 Discord bot wired olduğunda `AdminHotWalletThresholdBreachedNotificationConsumer` (Notifications modülü) eklenip dış kanallara push edilebilir — bu T77 scope'u dışı, plan'da T78/T79/T80 satırlarında.
- **K7 — Sweep dispatcher (T-future):** T76 K1'le aynı: SWEEP `BlockchainTransactionType` enum'u T76'da eklendi ama otomatik sweep dispatcher henüz yok (PaymentReceivedEvent consumer T-future). Hot wallet henüz SWEEP row üzerinden büyümüyor — bu yüzden upper threshold breach gerçek üretimde çok yavaş tetiklenir. Sweep wired olduktan sonra cadence daha kritik hale gelecek.

## Notlar

- **Working tree check (task.md Adım -1):** Temiz. Git status: 0 dosya.
- **Main CI startup check (task.md Adım 0):** Son 3 main CI run'ı tümü `success` (T76 squash merge run [25997787137](https://github.com/turkerurganci/Skinora/actions/runs/25997787137), [25997787138](https://github.com/turkerurganci/Skinora/actions/runs/25997787138), T75 squash run [25993624687](https://github.com/turkerurganci/Skinora/actions/runs/25993624687)). HARD STOP yok.
- **Dış varsayım kontrolü (task.md Adım 4):**
  - Sidecar `getWalletBalances` endpoint (T76) çalışıyor ✓ (T76 squash `f87687b` merged)
  - T73 transfer endpoint pattern (payout) hot wallet → external address'i destekliyor ✓ (mevcut `TransferService.payout` reuse)
  - Backend `IBlockchainSidecarClient` interface mevcut ✓ (T70+T75+T76 ile büyüdü)
  - `ColdWalletTransfer` entity + table mevcut ✓ (T25 InitialCreate)
  - Hangfire recurring + `IBackgroundJobScheduler` abstraction mevcut ✓ (T63b retention + T76 reconciliation)
  - SignalR `INotificationRealtimePublisher` admin broadcast mevcut ✓ (T76 reconciliation)
  - `hot_wallet_limit` SystemSetting (idx 28, T26 base seed) mevcut ✓
  - `reconciliation.hot_wallet_address` + `reconciliation.cold_wallet_address` (T76) mevcut ✓
  - Tüm dış varsayımlar doğrulandı; BLOCKED durumu yok.
- **Scope kararı (2026-05-17):** Proje sahibi onayı ile A seçildi (backend orchestrate eder + sidecar TRC-20 transfer + atomic ledger insert); monitor cadence `*/15 * * * *`; TRX default eşiği 100. `MANAGE_WALLETS` ayrımı yerine `MANAGE_SETTINGS` reuse (K1 K-future).
- **Mimari yerleşim:** HotWalletService + HotWalletMonitorService Skinora.API/Services/HotWallet altında (ReconciliationService pattern — Skinora.Transactions modülü Payments/Realtime'ı ref etmez, cross-module impl Skinora.API'de yer alır). Interface (`IHotWalletService`) Skinora.Transactions altında kalır.
