# T73 — Blockchain Sidecar — TRC-20 transfer (payout, refund, sweep)

**Faz:** F4 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-05-17

---

## Yapılan İşler

**Sidecar (`sidecar-blockchain`):**

- `HdWalletService.deriveSigner(index)` — mevcut `derive()` ile aynı BIP-44 türevi, ek olarak imzalama için `privateKey` döndüren `DeriveSignerResult`. Caller (`TransferService`/`RefundService`) tek seferlik kullanır, JS string'inin GC'ye düşmesini bekler — 05 §3.3 "Signing isolation" pratiği.
- `TronTransferClient` (yeni) — TronWeb 5.x üzerinden `triggersmartcontract` + `sendRawTransaction` ile TRC-20 transfer yayını + `gettransactioninfobyid` + `getnowblock` üzerinden status lookup. Test edilebilirlik için `TronWebFactory` injection point (üretim path'i `new TronWeb({fullHost, headers: {TRON-PRO-API-KEY}, privateKey})` ile yeni instance üretir; private key her çağrı için yeni binding'e bağlanır).
- `TransferService.payout(toAddress, amount, token, ...)` — hot wallet'tan satıcıya TRC-20 transfer (signer = `HOT_WALLET_PRIVATE_KEY` env). Hot wallet credentials eksikse `HOT_WALLET_NOT_CONFIGURED` non-retryable hata.
- `TransferService.sweep(depositIndex, depositAddress, toHotWallet, ...)` — deposit adresinden hot wallet'a sweep. Signer HD wallet türevi; caller-supplied `depositAddress` ile derive sonucu karşılaştırılır (`DEPOSIT_ADDRESS_MISMATCH` defense-in-depth).
- `RefundService.refund(depositIndex, depositAddress, toBuyerAddress, ...)` — deposit adresinden buyer source adresine net iade (gas fee düşülmüş tutar). T53 `RefundDecisionService` gas fee'yi backend tarafında hesaplar; sidecar yalnızca yayınlar.
- `TransferService.toRawUnits(amount, decimalsPower)` — bigint arithmetic ile `"100.5"` → `"100500000"` (6 ondalık); float yok, 09 §14.3 invariant'ı koruyor.
- 4 yeni HTTP endpoint (`api/transferHandlers.ts` + `routes.ts`):
  - `POST /api/transfer/payout` `{blockchainTransactionId, toAddress, amount, token}` → `200 {txHash}` / `400 INVALID_TRANSFER_REQUEST` / `502 TRANSFER_BROADCAST_*`
  - `POST /api/transfer/refund` `{blockchainTransactionId, depositIndex, depositAddress, toBuyerAddress, amount, token}` → aynı surface + `DEPOSIT_ADDRESS_MISMATCH`
  - `POST /api/transfer/sweep` `{blockchainTransactionId, depositIndex, depositAddress, toHotWalletAddress, amount, token}`
  - `GET /api/transfer/status/:txHash` → `200 {txHash, blockNumber?, contractRet?, confirmations}` (Solidity node 20-blok finality kontrolü backend tarafında)
- Vitest unit: `TronTransferClient.test.ts` (7) + `TransferService.test.ts` (9 — payout/sweep/refund + `toRawUnits` 4 dal). Toplam **79/79 PASS** (T70 15 + T71/T72 47 + T73 16 yeni + diğer 1).
- Yeni env: `HOT_WALLET_PRIVATE_KEY` (Docker secret production; mevcut `HOT_WALLET_ADDRESS` zaten vardı).

**Backend (`Skinora.Transactions` + `Skinora.Platform` + `Skinora.Shared`):**

- `BlockchainTransaction.NextAttemptAt` (`DateTime?` nullable) — dispatcher retry takvimi için ekstra alan. NULL = anında uygun. Migration `T73_AddNextAttemptAt` (single column add + composite filtered index `IX_BlockchainTransactions_DispatchScan` üzerinde `Status, NextAttemptAt, CreatedAt` filter `Status='PENDING'`).
- `IBlockchainTransferClient` + `HttpBlockchainTransferClient` — sidecar HTTP port. `TransferBroadcastRequest` tip discriminator: `SELLER_PAYOUT` → `/api/transfer/payout`; refund family → `/api/transfer/refund` (DepositIndex + DepositAddress zorunlu, eksikse `InvalidOperationException`); incoming type'lar disallowed. Status outcome enum: `Success | InvalidRequest | TransientFailure`. `X-Internal-Key` header service-to-service auth (05 §3.4 — `BlockchainSidecarOptions` mevcut T70 binding'ini paylaşır).
- `ITransferRetryPolicy` + `SystemSettingsTransferRetryPolicy` — `blockchain.transfer_retry_intervals_minutes` SystemSetting CSV ("1,5,15" default) okuyup `IReadOnlyList<TimeSpan>` döndürür. Malformed/empty/negative entries → `DefaultIntervals` fallback. `GetRetryDelayAsync(retryCount)` exhausted olunca `null` döner; `GetMaxAttemptsAsync()` = `intervals.Count + 1`.
- `OutgoingTransferDispatchJob` (Hangfire recurring, cron `* * * * *`, batch 20) — `Status=PENDING AND OutboundTypes AND (NextAttemptAt IS NULL OR NextAttemptAt <= now)` row'larını picker. Her row için:
  - SELLER_PAYOUT → DepositIndex/Address = null
  - Refund family → kardeş `BUYER_PAYMENT`/`WRONG_TOKEN_INCOMING` row üzerinden `PaymentAddressId` çözümleyip `PaymentAddress.HdWalletIndex + Address` set eder; row.FromAddress'a deposit address'i basar
  - Success → `Status=DETECTED`, `TxHash` set, `NextAttemptAt=null`
  - TransientFailure → `RetryCount++`, `NextAttemptAt = now + retryPolicy[RetryCount-1]`; policy exhausted ise FAILED + outbox publish
  - InvalidRequest → terminal FAILED + outbox publish
- `OutgoingTransferConfirmationJob` (Hangfire recurring, cron `* * * * *`, batch 30) — `Status=DETECTED AND TxHash NOT NULL AND OutboundTypes` row'ları için sidecar status endpoint'ini çağırır. `Confirmed` (≥20 blok + SUCCESS) → CONFIRMED + BlockNumber + ConfirmedAt; `Failed` (≥20 blok + REVERT) → FAILED + ErrorMessage; `Pending` → no-op; `Unavailable` → no-op (next tick yeniden).
- `OutgoingTransferJobsRegistrar` (IHostedService) — startup'ta her iki recurring job'ı `IBackgroundJobScheduler.AddOrUpdateRecurring` ile kaydeder. `IServiceScopeFactory` pattern (T11.3 mirror — singleton hosted service scoped DI'ı capture etmez).
- `TransferDispatchFailedEvent` (`IDomainEvent`) — terminal FAILED'da `OutboxService.PublishAsync` ile yayınlanır. Admin alert yolu: T63 admin dashboard `BlockchainTransaction Status=FAILED` filtresinden zaten görülebilir; ayrıca outbox event downstream T96 admin notification consumer'ına bağlanabilir (forward devir).
- SystemSetting yeni: `blockchain.transfer_retry_intervals_minutes` (string, default `"1,5,15"`, kategori `Monitoring`). `SystemSettingsCatalog` `blockchain_health` API kategorisi + birim `dakika`. Seed index 51.
- `BlockchainTransactionConfiguration`'a `NextAttemptAt` property + `IX_BlockchainTransactions_DispatchScan` composite filtered index.
- DI wiring (`Skinora.API/Configuration/TransactionsModule.cs`):
  - `HttpClient<HttpBlockchainTransferClient>` named "BlockchainTransfer" (mevcut `BlockchainSidecarOptions` BaseUrl/InternalKey/Timeout × 3 → broadcast için daha gevşek timeout)
  - `IBlockchainTransferClient` → `HttpBlockchainTransferClient` (Scoped)
  - `ITransferRetryPolicy` → `SystemSettingsTransferRetryPolicy` (Scoped)
  - `OutgoingTransferDispatchJob` + `OutgoingTransferConfirmationJob` (Scoped) + `OutgoingTransferJobsRegistrar` (HostedService)
- xUnit unit testleri:
  - `OutgoingTransferDispatchJobTests` (7 test) — exact branch coverage: success, hot-wallet payout deposit-less, transient retry + NextAttemptAt schedule, exhausted retry → FAILED + outbox event, invalid request → terminal FAILED, not-yet-eligible skip, confirmed row not re-dispatched
  - `OutgoingTransferConfirmationJobTests` (5 test) — confirmed flip, failed flip with contractRet, pending no-op, unavailable no-op, inbound DETECTED row not polled
  - `SystemSettingsTransferRetryPolicyTests` (8 test) — seed default 1/5/15, unconfigured fallback, CSV parse, malformed/whitespace/negative/zero fallback, exhaustion semantics, negative retryCount throws
  - `HttpBlockchainTransferClientTests` (12 test) — payout/refund routing + body shape, refund missing depositIndex throws, 400/502/HTTP exception outcomes, empty 200 body → transient, GetStatus confirmed/failed/pending/unavailable, X-Internal-Key header set
- `Skinora.Platform.Tests/Integration/SeedDataTests.cs` — 50 → 51 row count, configured 29 → 30 (yeni `blockchain.transfer_retry_intervals_minutes` ekledi).

## Etkilenen Modüller / Dosyalar

**Yeni (sidecar):**

- [`sidecar-blockchain/src/tron/TronTransferClient.ts`](../../sidecar-blockchain/src/tron/TronTransferClient.ts)
- [`sidecar-blockchain/src/tron/TronTransferClient.test.ts`](../../sidecar-blockchain/src/tron/TronTransferClient.test.ts)
- [`sidecar-blockchain/src/api/transferHandlers.ts`](../../sidecar-blockchain/src/api/transferHandlers.ts)
- [`sidecar-blockchain/src/transfer/TransferService.test.ts`](../../sidecar-blockchain/src/transfer/TransferService.test.ts)

**Güncellenen (sidecar):**

- [`sidecar-blockchain/src/transfer/TransferService.ts`](../../sidecar-blockchain/src/transfer/TransferService.ts) (stub → tam implementasyon)
- [`sidecar-blockchain/src/transfer/RefundService.ts`](../../sidecar-blockchain/src/transfer/RefundService.ts) (stub → tam implementasyon)
- [`sidecar-blockchain/src/wallet/HdWalletService.ts`](../../sidecar-blockchain/src/wallet/HdWalletService.ts) (`deriveSigner` + `DeriveSignerResult`)
- [`sidecar-blockchain/src/wallet/WalletManager.ts`](../../sidecar-blockchain/src/wallet/WalletManager.ts) (`deriveSigner` proxy)
- [`sidecar-blockchain/src/config/index.ts`](../../sidecar-blockchain/src/config/index.ts) (`hotWalletPrivateKey`)
- [`sidecar-blockchain/src/api/routes.ts`](../../sidecar-blockchain/src/api/routes.ts) (4 transfer endpoint)
- [`sidecar-blockchain/src/index.ts`](../../sidecar-blockchain/src/index.ts) (DI: TronTransferClient + TransferService + RefundService)

**Yeni (backend):**

- [`backend/src/Skinora.Shared/Persistence/Migrations/20260516213003_T73_AddNextAttemptAt.cs`](../../backend/src/Skinora.Shared/Persistence/Migrations/20260516213003_T73_AddNextAttemptAt.cs) (+ Designer)
- [`backend/src/Skinora.Shared/Events/TransferDispatchFailedEvent.cs`](../../backend/src/Skinora.Shared/Events/TransferDispatchFailedEvent.cs)
- [`backend/src/Modules/Skinora.Transactions/Application/Transfers/IBlockchainTransferClient.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Transfers/IBlockchainTransferClient.cs)
- [`backend/src/Modules/Skinora.Transactions/Application/Transfers/HttpBlockchainTransferClient.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Transfers/HttpBlockchainTransferClient.cs)
- [`backend/src/Modules/Skinora.Transactions/Application/Transfers/ITransferRetryPolicy.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Transfers/ITransferRetryPolicy.cs)
- [`backend/src/Modules/Skinora.Transactions/Application/Transfers/SystemSettingsTransferRetryPolicy.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Transfers/SystemSettingsTransferRetryPolicy.cs)
- [`backend/src/Modules/Skinora.Transactions/Application/Transfers/OutgoingTransferDispatchJob.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Transfers/OutgoingTransferDispatchJob.cs)
- [`backend/src/Modules/Skinora.Transactions/Application/Transfers/OutgoingTransferConfirmationJob.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Transfers/OutgoingTransferConfirmationJob.cs)
- [`backend/src/Modules/Skinora.Transactions/Application/Transfers/OutgoingTransferJobsRegistrar.cs`](../../backend/src/Modules/Skinora.Transactions/Application/Transfers/OutgoingTransferJobsRegistrar.cs)
- [`backend/tests/Skinora.Transactions.Tests/Unit/Transfers/OutgoingTransferDispatchJobTests.cs`](../../backend/tests/Skinora.Transactions.Tests/Unit/Transfers/OutgoingTransferDispatchJobTests.cs)
- [`backend/tests/Skinora.Transactions.Tests/Unit/Transfers/OutgoingTransferConfirmationJobTests.cs`](../../backend/tests/Skinora.Transactions.Tests/Unit/Transfers/OutgoingTransferConfirmationJobTests.cs)
- [`backend/tests/Skinora.Transactions.Tests/Unit/Transfers/SystemSettingsTransferRetryPolicyTests.cs`](../../backend/tests/Skinora.Transactions.Tests/Unit/Transfers/SystemSettingsTransferRetryPolicyTests.cs)
- [`backend/tests/Skinora.Transactions.Tests/Unit/Transfers/HttpBlockchainTransferClientTests.cs`](../../backend/tests/Skinora.Transactions.Tests/Unit/Transfers/HttpBlockchainTransferClientTests.cs)

**Güncellenen (backend):**

- [`backend/src/Modules/Skinora.Transactions/Domain/Entities/BlockchainTransaction.cs`](../../backend/src/Modules/Skinora.Transactions/Domain/Entities/BlockchainTransaction.cs) (`NextAttemptAt`)
- [`backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/BlockchainTransactionConfiguration.cs`](../../backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/BlockchainTransactionConfiguration.cs) (property + composite index)
- [`backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs`](../../backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs) (column + index + seed)
- [`backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsCatalog.cs`](../../backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsCatalog.cs) (`blockchain.transfer_retry_intervals_minutes`)
- [`backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs`](../../backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs) (50 → 51 row, index 51 yeni entry)
- [`backend/src/Skinora.API/Configuration/TransactionsModule.cs`](../../backend/src/Skinora.API/Configuration/TransactionsModule.cs) (Transfers DI block)
- [`backend/tests/Skinora.Platform.Tests/Integration/SeedDataTests.cs`](../../backend/tests/Skinora.Platform.Tests/Integration/SeedDataTests.cs) (50 → 51 count; configured 29 → 30 with new key)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Satıcıya payout: TRC-20 transfer, retry 3 deneme (1dk, 5dk, 15dk), başarısızlıkta admin alert | ✓ | `TransferService.payout()` USDT contract + hot wallet signer → TronGrid `triggerSmartContract` + `sendRawTransaction`. Backend `OutgoingTransferDispatchJob` PENDING SELLER_PAYOUT picker → success → DETECTED + TxHash; transient → `NextAttemptAt = now + retryPolicy[count-1]`; policy exhausted → FAILED + `TransferDispatchFailedEvent` outbox publish. Tests: `SuccessfulBroadcast_FlipsRowToDetected_AndStampsTxHash`, `SellerPayout_DoesNotRequireDepositAddress_BroadcastsFromHotWallet`, `TransientFailure_IncrementsRetryAndSchedulesNextAttempt`, `TransientFailure_AfterExhaustedRetries_FlipsToFailedAndPublishesEvent`. Default cadence `1,5,15` SystemSetting seed default + `SeedDefault_Parses_To_1_5_15_Intervals` regression guard. |
| 2 | Alıcıya refund: TRC-20 transfer, retry 3 deneme | ✓ | `RefundService.refund()` deposit signer (HD wallet `deriveSigner(index)`) → buyer source address. Backend dispatcher refund family (BUYER_REFUND / EXCESS_REFUND / WRONG_TOKEN_REFUND / INCORRECT_AMOUNT_REFUND / LATE_PAYMENT_REFUND) routing → `/api/transfer/refund`; deposit index/address resolution kardeş `BUYER_PAYMENT`/`WRONG_TOKEN_INCOMING` row üzerinden `PaymentAddress.HdWalletIndex + Address`. Retry policy SELLER_PAYOUT ile aynı. Tests: `HttpBlockchainTransferClientTests.Refund_Routes_To_RefundEndpoint_WithDepositPayload` + dispatch suite refund branches. |
| 3 | Sweep: deposit → hot wallet, sweep sonrası delegation geri alımı | ~ | `TransferService.sweep()` deposit signer → hot wallet broadcast endpoint hazır + handler + DI; backend dispatcher SWEEP-tipi BlockchainTransaction row'u görmediği sürece otomatik tetikleyici yok (PaymentReceivedEvent consumer T75/T76 forward devir). Delegation `delegateresource`/`undelegateresource` çağrıları **T74 scope** — sidecar sweep path delegation port'larını henüz çağırmaz, fallback olarak "deposit'te TRX var" varsayımıyla çalışır (08 §3.3 fallback "delegation başarısızsa minimum TRX transfer"). T74 delegation eklenince path tamamlanır. Kanıt: `TransferService.sweep()` sweep request gönderir + `DEPOSIT_ADDRESS_MISMATCH` guard; test `derives signer and broadcasts deposit -> hot wallet`. |
| 4 | Sweep hata yönetimi: retry + fallback (deposit'ten doğrudan gönderim) | ~ | Retry: dispatcher SELLER_PAYOUT/refund ile aynı policy (`1,5,15` exhaust → FAILED + alert). Fallback: 05 §3.3 "Sweep başarısız olursa retry (exponential backoff, 3 deneme). Tüm denemeler başarısızsa admin'e alert — payout veya refund deposit adresinden doğrudan gönderilir (fallback)" — payout/refund zaten deposit-direct mantığını destekler (refund flow), yani admin alert sonrası ops manuel olarak refund row yazabilir veya sweep skip ederek payout deposit-direct olarak çalıştırılabilir. T74 delegation + T-future SweepDispatcher tetikleyici. |
| 5 | Transaction broadcasting: broadcasttransaction endpoint | ✓ | `TronTransferClient.sendTransfer` `tronWeb.trx.sendRawTransaction(signed)` (`/wallet/broadcasttransaction` TronWeb wrapper) çağrısı. Test: `builds, signs and broadcasts a TRC-20 transfer and returns the txid` + `throws TRANSFER_BROADCAST_REJECTED when broadcast returns result=false`. |
| 6 | Onay takibi: gettransactioninfobyid ile doğrulama | ✓ | `TronTransferClient.getTransactionStatus(txHash)` `/walletsolidity/gettransactioninfobyid` + `/walletsolidity/getnowblock` paralel çağrı → confirmations = solidBlock - txBlock. `HttpBlockchainTransferClient.GetStatusAsync` 20-blok eşiği + `SUCCESS` contractRet kontrolü → `TransferStatusOutcome.Confirmed` vs `Failed`. `OutgoingTransferConfirmationJob` DETECTED row'ları her dakika polling. Tests: `getTransactionStatus returns confirmation count`, `GetStatus_Confirmed_When_Confirmations_GE_20_AndSuccess`, `GetStatus_Failed_When_Confirmations_GE_20_AndContractReverted`, `OutgoingTransferConfirmationJobTests.ConfirmedStatus_FlipsRowToConfirmedAndStampsBlock`. |

## Doğrulama Kontrol Listesi

| # | Kontrol | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 08 §3.1 TronGrid API çağrıları doğru mu? | ✓ | `triggersmartcontract` (TronWeb wrapper), `broadcasttransaction` (`sendRawTransaction`), `gettransactioninfobyid` (POST solidity), `getnowblock` (POST solidity) — 4'ü de 08 §3.1 endpoint tablosunda mevcut. Headers: `TRON-PRO-API-KEY` (constructor `{fullHost, headers, privateKey}` form'unda set). |
| 2 | Retry stratejisi doğru mu? | ✓ | Default `1,5,15` dakika (08 §3.3 + 05 §3.3). 3 deneme sonra admin alert (`TransferDispatchFailedEvent` outbox publish). SystemSetting admin tarafından runtime ayarlanabilir (`blockchain.transfer_retry_intervals_minutes`). Malformed value → documented default fallback. Tests: 8 retry-policy testi (seed default + unconfigured + CSV parse + malformed + exhaustion). |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Sidecar Vitest | ✓ **79/79 PASS** | `npm test` — TronTransfer 7 + Transfer 9 + HdWallet 15 + Monitor 13 + PaymentMonitorRules 24 + TronGrid 11 (T73 16 yeni). |
| Backend Unit (full) | ✓ **870/870 PASS** | `dotnet test backend/Skinora.sln -c Release --filter "FullyQualifiedName!~Integration & FullyQualifiedName!~InitialMigration"` — Shared 189 + Users 16 + Auth 57 + Platform 102 + Fraud 14 + Transactions **386** + Notifications 49 + Steam 13 + Realtime 25 + API 15 + Disputes 4 = 870. T73 yeni 32: 7 dispatch + 5 confirmation + 8 retry policy + 12 HTTP client. |
| Backend Build Release | ✓ 0W/0E | `dotnet build backend/Skinora.sln -c Release`. |
| Backend `dotnet format --verify-no-changes` | ✓ PASS (Δ=0) | Lokal otomatik formatlama tek run'da kapandı. |
| Sidecar `npm run format:check` | ⚠ pre-existing drift (23 dosya) | T73 yeni dosyaları (TronTransferClient.test.ts + TransferService.test.ts) formatlandı; pre-existing 23 dosya T71 K8 ile aynı havuzda, ayrı chore PR. |
| Lokal Testcontainers integration | ⚠ env-skip | Lokalde Docker Desktop kapalı — F4 envelope, CI Linux runner'da Testcontainers `services:mssql` ile çalışır. |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS bağımsız validator (2026-05-17) |
| Kabul kriterleri | 4 ✓ (1 payout, 2 refund, 5 broadcasttransaction, 6 gettransactioninfobyid finality) + 2 ~ Kısmi (3 sweep delegation geri alımı + 4 sweep hata fallback — T74 energy delegation forward devir, plan'da T74 explicit `delegateresource`/`undelegateresource` tanımlı; sidecar sweep primitive + endpoint ready) |
| Doğrulama kontrol listesi | 2/2 ✓ (08 §3.1 TronGrid API çağrıları doğru — triggersmartcontract + broadcasttransaction + gettransactioninfobyid + getnowblock + retry stratejisi 1,5,15 dk doğru) |
| Bulgu sayısı | 0 S-bulgu (S1/S2/S3 yok); 2 minor advisory: M1 rapor migration filename drift (`20260516213003` → gerçek `20260516221505`, kozmetik); M2 dispatcher OutboundTypes listesinde SWEEP enum yok (K2 forward-devir ile uyumlu — sidecar `/api/transfer/sweep` primitive olarak hazır, backend orkestrasyon T-future) |
| Validator izolasyonu | Yapım raporu read'i Faz 3 Adım 13'te yapıldı; kabul kriterleri ve verdict bağımsız oluşturuldu — yapım raporuyla 1:1 uyumlu (4 ✓ + 2 ~ aynı sınıflama). |
| Adım 0 main CI startup | ✓ 3/3 success (`25972872805` T72 + `25972872809` T72 + `25967060890` T71) |
| Adım 0b memory drift | ✓ T73 satırları MEMORY.md "Current Status" bloğunda mevcut (post-merge `T73 yansıt` satırı eklenecek) |
| Adım 8a task branch CI | ✓ Son run [`25974588707`](https://github.com/turkerurganci/Skinora/actions/runs/25974588707) 11/11 SUCCESS (Detect changed + Guard + Lint + Build + Unit + Integration + Contract + Migration + Docker×2 + CI Gate) |
| Lokal test çalıştırma | Sidecar Vitest 79/79 PASS (1.25s); Backend `dotnet test --filter Transfers` 33/33 PASS |
| Mini güvenlik | Secret leak temiz (env-only HOT_WALLET_PRIVATE_KEY + HD_WALLET_MNEMONIC, private key local scope); auth temiz (`X-Internal-Key` middleware tüm yeni endpoint'lerde); input validation tam (handler tip + token enum + `DEPOSIT_ADDRESS_MISMATCH` defense-in-depth); yeni dep yok (TronWeb T70'te zaten kullanımda, ethers transitive) |
| Doküman uyumu | TronGrid endpoint isimleri 08 §3.1 birebir; 20-blok finality 05 §3.3 + 08 §3.4 ile uyumlu; retry cadence 08 §3.5 (1dk/5dk/15dk) birebir; token decimals 6 (USDT/USDC) 08 §3.3 ile uyumlu; HMAC/InternalKey auth 05 §3.4 |

## Altyapı Değişiklikleri

- **Migration:** **Var** — `20260516213003_T73_AddNextAttemptAt` — `BlockchainTransactions.NextAttemptAt` (DateTime? nullable) + `IX_BlockchainTransactions_DispatchScan` (composite: Status, NextAttemptAt, CreatedAt; filter `[Status]='PENDING'`). Down idempotent (drop index → drop column). 10 → 11 migration zinciri.
- **Config/env değişikliği:**
  - **Sidecar** — yeni env `HOT_WALLET_PRIVATE_KEY` (Docker secret production, env var dev). Mevcut `HOT_WALLET_ADDRESS` ile birlikte SELLER_PAYOUT için signer + sanity check. Eksikse `HOT_WALLET_NOT_CONFIGURED` non-retryable hata (sidecar dev modda başlar, prod hot wallet credentials zorunlu).
  - **Backend** — yeni SystemSetting `blockchain.transfer_retry_intervals_minutes` (default `"1,5,15"`, kategori `Monitoring`). Admin tarafından `PATCH /admin/settings` ile değiştirilebilir. Malformed → code-side default fallback.
- **Docker değişikliği:** **Yok** — yeni env var docker-compose.yml'a opsiyonel eklenebilir; T73 implementasyonu mevcut blockchain sidecar service'ini yeniden başlatma gerektirmez (sidecar startup'ta env'i okur).

## Commit & PR

- Branch: `task/T73-trc20-transfer`
- Yapım commit'leri: `4dbc981` (ana implementasyon) + `8aa8576` (sidecar typecheck fix) + `8a4dfdc` (BYPASS_LOG) + `454cfc3` (CI fix — migration seed + ModuleInitializer test order) + `ae2ec55` (BYPASS_LOG #2 + IMPLEMENTATION_STATUS CI ✓ rapor)
- PR: [#114](https://github.com/turkerurganci/Skinora/pull/114)
- CI: ✓ PASS — son task branch run [`25974588707`](https://github.com/turkerurganci/Skinora/actions/runs/25974588707) 11/11 SUCCESS
- BYPASS_LOG: 2× `[ci-failure]` entry (Layer 2 lokal hook bypass — CI'da geçen sonraki fix push'ları)

## Known Limitations / Follow-up

- **K1 — Energy delegation T74 devir.** `TransferService.sweep()` + `RefundService.refund()` deposit signer ile broadcast yapar ama TronGrid `delegateresource`/`undelegateresource` çağrıları henüz yok. T74 energy delegation tamamlanmadığı sürece deposit adresinde TRX/Energy bulunmuyorsa transfer `OUT_OF_ENERGY` ile fail eder. Workaround: deposit adresine deploy-time minimum TRX prefund (08 §3.3 fallback). T74 scope'unda delegation port'ları implement edilecek.
- **K2 — Sweep otomatik tetikleyici T-future.** Backend `PaymentReceivedEvent` consumer'ı henüz sweep row yazmıyor; T76 reconciliation veya T-future consumer'a bağlanacak. T73 sadece sweep primitive'ini sağlar.
- **K3 — Admin alert downstream consumer.** `TransferDispatchFailedEvent` outbox'a yayınlanır ama in-app/email notification consumer henüz yok (RefundBlockedAdminAlertEvent ile aynı pattern). T63 admin dashboard `Status=FAILED` filtresi ile manual visible; otomatik admin notif (push/email) T96 admin tooling forward devir.
- **K4 — Confirmation hot-path consumer.** `Status=CONFIRMED` flip sonrası `PaymentReceivedEvent`/`PayoutCompletedEvent` benzeri downstream event yayınlanmıyor (T73 yalnızca finality kaydını flush eder). Satıcıya "payout completed" notification ve transaction `COMPLETED` state geçişi T-future task.
- **K5 — On-chain reorg pencerelerinde finality.** 20 blok eşiği Tron mainnet SR setine güvenir; ekstrem reorg senaryosunda confirmation job CONFIRMED → FAILED'a flip'in mantığı yok (sadece DETECTED → CONFIRMED ve DETECTED → FAILED tek yönlü). T76 reconciliation post-hoc düzelir.
- **K6 — Sidecar pre-existing format drift (23 dosya).** T64-T72 boyunca biriken prettier drift; T73 yeni dosyaları (`TronTransferClient.test.ts` + `TransferService.test.ts`) formatlandı, geri kalan 23 dosya T71 K8 ile aynı chore PR havuzunda.
- **K7 — `RefundService` ve `TransferService.sweep` deposit signer derivation maliyeti.** Her transfer için `HdWalletService.deriveSigner(index)` çağrılıyor (BIP-32/44 türevi ~5ms in-process); MVP trafiğinde performans sorunu değil ama yüksek hacim altında HD wallet cache eklenmeli (T-future optimizasyon).
- **K8 — Hot wallet TRX bakiye uyarısı T77.** `HOT_WALLET_NOT_CONFIGURED` non-retryable hata sadece env eksikse fırlatılır. Hot wallet TRX/USDT bakiyesi runtime'da düşük olduğunda admin alert T77 scope.
- **K9 — Sidecar token decimals 6 sabit.** USDT/USDC TRC-20 6 ondalık (08 §3.3) — hardcoded `tokenDecimals: 6` config'de. Farklı ondalıklı stablecoin desteği T-future.

## Notlar

- **Working tree (Adım -1):** Temiz.
- **Main CI startup (Adım 0):** Son 3 main run 3/3 SUCCESS — `25972872809`, `25972872805`, `25967060890`. ✓
- **Repo memory drift (Adım 0b):** [`.claude/memory/MEMORY.md`](../../.claude/memory/MEMORY.md) "Current Status" bloğu T72 ile güncel; T73 yapım sonrası "T73 yansıt" satırı eklenecek (post-push).
- **Dış Varsayımlar (Adım 4):**
  - **TronWeb 5.3.5 API** — `new TronWeb({fullHost, headers: {TRON-PRO-API-KEY}, privateKey})` object-form constructor + `transactionBuilder.triggerSmartContract` + `trx.sign` + `trx.sendRawTransaction` mevcut. Canlı `require('tronweb')` ile doğrulandı: `new TronWeb({fullHost:'...', headers:{...}})` → `transactionBuilder` + `trx` namespace'leri var, object-form OK.
  - **TronGrid endpoint'leri** (08 §3.1) — `/walletsolidity/gettransactioninfobyid`, `/walletsolidity/getnowblock` T71'de zaten kullanımda + `triggersmartcontract` + `broadcasttransaction` TronWeb 5.x ile dolaylı kullanım. Headers `TRON-PRO-API-KEY` set ediliyor (api key boşsa header eklenmez — dev mode public RPS limit'inde çalışır).
  - **TRC-20 transfer ABI** — `transfer(address,uint256)` standard ERC-20 ABI; USDT/USDC TRC-20 contract'larında 06 §3.3 tablosunda dokümante edilmiş 6 ondalık + transfer fonksiyonu.
  - **HD wallet child private key** — `ethers 6.16.0` (transitive dep) `HDNodeWallet.derivePath().privateKey` mevcut + `TronWeb.address.fromPrivateKey(hex)` mevcut (T70'te zaten kullanımda).
  - **`BlockchainSidecarOptions` mevcut yapı** — T70'te tanımlanmış (BaseUrl/InternalKey/TimeoutSeconds); T73 yeniden tanımlamadan reuse ediyor, HttpClient timeout × 3 (broadcast daha uzun).
- **Scope onayı (2026-05-17):** Proje sahibi onayı `AskUserQuestion` ile alındı — Alt A: full sidecar 4 endpoint + backend dispatch job + confirmation job + retry policy + admin alert. Hot wallet key: ayrı `HOT_WALLET_PRIVATE_KEY` env (HD master seed compromise protection).
- **Squash-merge bundled-PR guard:** T73 commit'leri yalnızca `T73:` prefix taşıyacak (commit-msg hook real-time enforce eder).
