# WP3 — Hot-wallet/ledger doğruluğu: SWEEP dispatcher

**Faz:** PRE_F6_PLAN (F6 öncesi MVP borç kapatma) | **Durum:** ✓ Tamamlandı (bağımsız doğrulama PASS) | **Tarih:** 2026-06-16

---

## Yapılan İşler

- **Yeni `SweepQueueJob`** (`Skinora.Transactions.Application.Transfers`, per-minute Hangfire producer, WP1 `SellerPayoutQueueJob` deseni birebir): `ITEM_DELIVERED && !IsOnHold && !HasActiveDispute && mevcut SWEEP satırı yok` taraması → her uygun işlem için PENDING `SWEEP` `BlockchainTransaction` satırı kuyruğa alır. Mevcut `OutgoingTransferDispatchJob` yayınlar, `OutgoingTransferConfirmationJob` CONFIRMED'a sürer, T76 reconciliation hot-wallet inflow olarak sayar.
- **Sweep tetiği iade penceresi sonrasına ERTELENDİ (owner kararı, AskUserQuestion 2026-06-15):** 05 §3.3 satır 316 tetik olarak `PaymentReceivedEvent` der, ama alıcı-iadesi (WP2) **depozit adresinden** çeker ve ana iade tetiği (teslim-timeout) ödeme onayından *sonra* ateşlenir → eager sweep depoziti boşaltıp yaygın "ödeme sonrası iptal" iadesini bozardı. Sweep `ITEM_DELIVERED` anına ertelenince depozit iade penceresi boyunca fonlu kalır (05 §3.3 satır 323: sweep öncesi iade depozitten). **WP2 iade yolu hiç değişmedi.** Hot wallet operasyonel havuz olduğu için (05 §3.3 satır 307) SELLER_PAYOUT (WP1) ile sweep arası sıralama gerekmez — ikisi de aynı `ITEM_DELIVERED` kapısında üretilir.
- **SWEEP satır şekli (T76 reconciliation sözleşmesine uyumlu):** `PaymentAddressId = deposit.Id` (depozit-bağlı kaynak; reconciliation deposit-outflow bu alana göre eşler), `FromAddress = deposit.Address`, `ToAddress = hot wallet` (reconciliation hot-inflow `ToAddress==hot && Type==SWEEP` ile eşler), `Amount = Transaction.TotalAmount` (tam emanet tutarı = fiyat + komisyon; fazla-ödeme ayrı EXCESS_REFUND ile boşaltılır), `Token = StablecoinType`, `ActualTokenAddress = null`, `GasFee = null` (gas'ı merkezi sweeper hesabı energy delegation ile karşılar, 05 §3.3 satır 332-335 / T74 — tutardan düşülmez).
- **Hot wallet adresi çözümü:** `reconciliation.hot_wallet_address` SystemSetting (T76, "NONE" sentinel = yapılandırılmamış). Tick başında bir kez okunur; NONE/boş ise tüm tarama atlanır + warn log (bogus ToAddress'li satır üretilmez — `ReconciliationService`/`HotWalletService` NONE deseniyle birebir).
- **Dispatch wiring:** `OutgoingTransferDispatchJob.OutboundTypes` += SWEEP → dispatcher mevcut non-SELLER_PAYOUT depozit-çözüm dalını yeniden kullanır (kardeş BUYER_PAYMENT satırından `depositIndex`/`depositAddress`; `FromAddress = deposit`). `HttpBlockchainTransferClient.BuildRequest` += SWEEP dalı → `POST api/transfer/sweep` + yeni `SweepBody` (`toHotWalletAddress = ToAddress`, `depositIndex`/`depositAddress` zorunlu → null ise atar). **Sidecar `/api/transfer/sweep` + `TransferService.sweep()` zaten gerçek/test edilmiş (T74 energy delegation) — sidecar değişikliği YOK.** Eski yanlış doc-comment ("SWEEP composite — implicit") düzeltildi (kod bugün SWEEP'i `_` default → `/refund`'a düşürüyordu; bu defekt kapatıldı).
- **Confirmation wiring:** `OutgoingTransferConfirmationJob.OutboundTypes` += SWEEP (planın atladığı **zorunlu düzeltme**) — yoksa broadcast SWEEP DETECTED'da sonsuza kalır, CONFIRMED-only reconciliation hot-wallet inflow'u hiç görmez. SWEEP durum-geçişi tetiklemez → `PayoutCompletedEvent` emit edilmez (yalnız SELLER_PAYOUT).
- **Idempotency — tam savunma (WP1 F1 deseni):** (1) `[DisableConcurrentExecution(50)]` (Hangfire distributed lock); (2) `AnyAsync(TransactionId, Type=SWEEP)` guard; (3) filtered unique index `UQ_BlockchainTransactions_Sweep_TransactionId` (`(TransactionId) WHERE [Type]='SWEEP'`) DB-backstop; (4) `catch(DbUpdateException)` → detach + re-query → varsa idempotent no-op / yoksa re-throw. Tek SaveChanges/aday.

## Etkilenen Modüller / Dosyalar

| Dosya | Değişiklik |
|---|---|
| `Application/Transfers/SweepQueueJob.cs` | **YENİ** — ITEM_DELIVERED producer (hot-wallet çözümü + tam savunma idempotency) |
| `Application/Transfers/OutgoingTransferDispatchJob.cs` | `OutboundTypes` += SWEEP; refund→outbound log mesajı genelleştirildi |
| `Application/Transfers/OutgoingTransferConfirmationJob.cs` | `OutboundTypes` += SWEEP (DETECTED→CONFIRMED; PayoutCompletedEvent emit etmez) |
| `Application/Transfers/HttpBlockchainTransferClient.cs` | SWEEP routing dalı + `SweepBody` record + doc düzeltme |
| `Application/Transfers/OutgoingTransferJobsRegistrar.cs` | `SweepQueueJob` recurring kaydı |
| `Infrastructure/Persistence/BlockchainTransactionConfiguration.cs` | `CK_..._Type_Sweep` + `UQ_..._Sweep_TransactionId` |
| `Skinora.Shared/Enums/BlockchainTransactionType.cs` | SWEEP enum doc-comment güncel (ertelenmiş ITEM_DELIVERED tetiği) |
| `Skinora.API/Configuration/TransactionsModule.cs` | `SweepQueueJob` DI kaydı |
| `Persistence/Migrations/20260615194323_WP3_AddSweepConstraintAndIndex.cs` (+Designer, +Snapshot) | **YENİ** — şema-only (CHECK + index; seed yok) |
| `tests/.../Unit/Transfers/SweepQueueJobTests.cs` | **YENİ** — 11 unit testi |
| `tests/.../Unit/Transfers/HttpBlockchainTransferClientTests.cs` | +2 SWEEP routing testi |
| `tests/.../Unit/Transfers/OutgoingTransferDispatchJobTests.cs` | +1 SWEEP dispatch testi |
| `tests/.../Unit/Transfers/OutgoingTransferConfirmationJobTests.cs` | +1 SWEEP confirm testi |
| `tests/.../Unit/Reconciliation/ReconciliationServiceTests.cs` (Skinora.API.Tests) | Hot-scope SWEEP fixture'ları yeni CK'ye uyumlu (sentetik STOPPED depozit) |

## Kabul Kriterleri Kontrolü

| # | Kriter (plan WP3 "İş" + owner kararı) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Producer, settled (ITEM_DELIVERED) işlem için PENDING SWEEP satırı üretir | ✓ | `SweepQueueJob.QueueSweepAsync`; test `ConfiguredHotWallet_DeliveredTx_QueuesPendingSweep_DepositSource_HotDestination` |
| 2 | Tetik iade penceresi sonrasına ertelendi; WP2 iade yolu değişmedi | ✓ | Gate `ITEM_DELIVERED && !IsOnHold && !HasActiveDispute`; iade dispatch/consumer dosyaları diff-dışı |
| 3 | SWEEP satır şekli reconciliation'ı tatmin eder (PaymentAddressId/ToAddress/Amount/GasFee null) | ✓ | Test AC1 alan assertion'ları; `ReconciliationService` deposit+hot sorguları |
| 4 | SWEEP dispatch'e eklendi → sidecar /sweep'e yönlenir | ✓ | `OutboundTypes`+SWEEP; `BuildRequest` SWEEP dalı; testler `Sweep_ResolvesDepositSource_AndBroadcastsToHotWallet` + `Sweep_Routes_To_SweepEndpoint...` |
| 5 | SWEEP confirmation'a eklendi → CONFIRMED, PayoutCompletedEvent yok | ✓ | Confirmation `OutboundTypes`+SWEEP; test `SweepConfirmed_FlipsToConfirmed_AndDoesNotPublishPayoutCompletedEvent` |
| 6 | Migration: CK_..._Type_Sweep + filtered unique index; şema-only, drift yok | ✓ | `20260615194323_WP3_...`; `has-pending-model-changes` → "No changes"; testler `SweepRow_WithNullPaymentAddressId_IsRejectedByCheckConstraint`, `SecondSweepRow_..._IsRejectedByUniqueIndex` |
| 7 | Idempotency tam savunma (4 katman) | ✓ | `[DisableConcurrentExecution(50)]` + AnyAsync + unique index + catch; testler `RunTwice_...`, `ConcurrentInsertRace_SwallowsDuplicate...`, `NonDuplicateDbUpdateException_IsRethrown...` |
| 8 | Hot-wallet unconfigured (NONE) → atla; held/disputed/non-delivered/missing-deposit → atla | ✓ | testler `HotWalletUnconfigured_QueuesNoSweep`, `HeldTransaction_IsSkipped`, `DisputedTransaction_IsSkipped`, `NonDeliveredTransaction_IsSkipped`, `MissingDepositAddress_IsSkipped` |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (Transactions) | ✓ **101/101** (`Category=Unit`) | `SweepQueueJobTests` 11 + transfer-client/dispatch/confirm SWEEP testleri dahil |
| Unit (Transfers alt-küme) | ✓ **75/75** | `--filter "FullyQualifiedName~Transfers"` |
| Unit (Reconciliation, Skinora.API.Tests) | ✓ **13/13** | SWEEP fixture'ları yeni CK'ye uyumlu (sentetik STOPPED depozit) |
| Unit (HotWallet, Skinora.API.Tests) | ✓ **20/20** | Regresyon yok |
| Integration (Transactions) | ⏳ CI-authoritative | Lokal Docker/SQL Server yok; CI Integration + Migration dry-run job'ları doğrular |
| Build | ✓ 0W/0E | `dotnet build Skinora.sln` Debug **ve** Release |
| Format | ✓ temiz | `dotnet format Skinora.sln --verify-no-changes` (exit 0) |
| Migration drift | ✓ yok | `dotnet ef migrations has-pending-model-changes` → "No changes have been made to the model since the last migration." |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** (bağımsız validator chat, 2026-06-16 — `feedback_validation_separate_chat`) |
| Bloke-edici bulgu | **0** |
| Yapım-içi adversarial self-check | ✓ PASS — 5-boyut/refute-default workflow (15 ajan), **9 ham → 0 onaylı bloke-edici/major** |
| Düzeltme gerekli mi | Validator-fix: 06 doc-conformance (owner onaylı, bu PR'a katıldı). CC-03 + FE enums.ts ertelendi. |

### Bağımsız Doğrulama (ayrı chat, 2026-06-16) — VERDICT ✓ PASS

Validator rapor görülmeden kendi verdict'ini oluşturdu (izolasyon §3.3). **Kapılar:** Adım -1 temiz · Adım 0 main son-3 success (`27567370074`/`27567370227`/`27509836014`) · Adım 0b repo memory mevcut · **Adım 8a task CI HEAD `bcfd3e1` run [`27574022583`](https://github.com/turkerurganci/Skinora/actions/runs/27574022583) tüm job success** (Lint/Build/**Unit**/**Integration**/Contract/**Migration-dry-run**/Docker; Guard skipped). **Lokal yeniden çalıştırma:** Transactions unit **101/101** + API reconciliation+hotwallet **29/29** + full Release build **0W/0E** + snapshot drift yok (CK + UQ index). **Sidecar kontrat teyidi:** `POST /api/transfer/sweep { blockchainTransactionId, depositIndex, depositAddress, toHotWalletAddress, amount, token }` ↔ backend `SweepBody` **birebir**; `sweep()` gerçek (T74), `request.amount`'ı **tam** sweepler (full-balance değil) → ledger `SWEEP.Amount = TotalAmount` = on-chain swept değer → reconciliation iki-taraflı kapanır.

**6-boyut/refute-default adversarial workflow (23 ajan: money-idempotency · reconciliation · trigger-wp2 · constraint-migration · sidecar-routing · state-events-di + completeness critic) → 0 onaylı bloke-edici bulgu.** 8 kabul kriteri ✓. Money-safety (3-katman idempotency, double-sweep yolu yok) + reconciliation matematiği (overpayment/multi/under hepsi net) + state-machine (SWEEP geçiş tetiklemez, PayoutCompletedEvent yok) + DI/registrar bağımsız doğrulandı.

**Validator net-yeni bulgular (hepsi non-blocking):**
- **06_DATA_MODEL.md doc-drift (S1)** — §2.5 tip tablosu + §3.8 CHECK-listesi/retry notu SWEEP'i belgelemiyordu (kod+migration+EnumTests 10 değer güncel). **Owner kararı (AskUserQuestion 2026-06-16): bu PR'a kat** → §2.5'e SWEEP satırı + §3.8'e `CK_..._Type_Sweep` bullet + retry/RetryCount/PaymentAddressId notlarına SWEEP eklendi.
- **CC-03 (S3, düşük olasılık)** — dispatcher SWEEP kaynağını kardeş BUYER_PAYMENT'tan çözüyor; o lookup null dönerse satır RetryCount/FAILED/alert olmadan sonsuz PENDING döner. Teslim edilmiş tx'te onaylı BUYER_PAYMENT hep var → canlı risk değil; terminal/alert yolu + test **follow-up'a ertelendi** (owner onaylı).
- **FE `enums.ts` SWEEP yok** → mevcut `FE-enums-ts-lag` / **WP13** (değişiklik yok, owner onaylı erteleme).

Yapım raporuyla tam uyumlu (rapor 05 §3.3 trigger-drift→WP17, FE enums.ts→WP13, teslim-sonrası dispute iadesi→WP5/WP12 known-limitation'larını zaten kaydetmişti; 06 doc-drift + CC-03 yalnız validator tarafından bulundu).

**Yapım-içi adversarial review (5-boyut: money-safety/ordering · idempotency/concurrency · constraint/migration · dispatch/confirm/client wiring · spec/reconciliation; refute-default + bağımsız verify):**
- **VERDICT PASS — 9 ham bulgu → 0 onaylanmış bloke-edici/major.** Bağımsız teyit edilenler:
  - **Money-safety uçtan uca:** `SweepQueueJob` WP1 `SellerPayoutQueueJob` desenine sadık — `ITEM_DELIVERED + !IsOnHold + !HasActiveDispute` kapısı + döngü-içi yeniden doğrulama + 4-katman idempotency (`[DisableConcurrentExecution(50)]` + AnyAsync + filtered unique index + catch detach/re-query/re-throw). Çift-sweep yolu yok.
  - **Reconciliation matematiği tam tutarlı (taşıyıcı invariant):** `ExpectedAmount = TotalAmount` (`PaymentAddressAllocator.cs:120`) = SWEEP `Amount` → depozit sıfıra iner (inflow BUYER_PAYMENT − outflow SWEEP[+EXCESS_REFUND fazla-ödemede]); deposit-outflow `PaymentAddressId`'e, hot-inflow `ToAddress==hot && Type==SWEEP`'e göre eşler; iki tarafta da CONFIRMED-only.
  - **Dispatch depozit-çözümü doğru** (kardeş BUYER_PAYMENT üzerinden; ITEM_DELIVERED'da daima mevcut, aynı 1:1 PaymentAddress) · **confirmation** SWEEP'i CONFIRMED'a sürer, PayoutCompletedEvent emit etmez · **client** SWEEP'i `/api/transfer/sweep`'e yönlendirir · **migration** config+snapshot ile birebir · **NONE handling** ReconciliationService ile tutarlı.
- **Non-blocking gözlemler:** (1) **minor doc-drift** — 05 §3.3:316 + PRE_F6_PLAN.md:73 hâlâ "PaymentReceivedEvent tetik" der; owner kararıyla kod ITEM_DELIVERED'a erteliyor (kod doğru, inline gerekçe + enum yorumu mitigasyon). PRE_F6_PLAN WP3 satırı bu PR'da güncellendi; 05 §3.3 → **WP17**. (2) **nit** — `frontend/src/types/enums.ts` SWEEP'i yansıtmıyor (WP3 salt-backend, FE tüketici yok, `BlockchainTransactionType` hiçbir DTO'da serialize edilmiyor) → mevcut `FE-enums-ts-lag` backlog / **WP13** enum-sync.

## Altyapı Değişiklikleri

- **Migration:** Var — `20260615194323_WP3_AddSweepConstraintAndIndex` (`CK_BlockchainTransactions_Type_Sweep` + `UQ_BlockchainTransactions_Sweep_TransactionId`, **şema-only, seed yok** → SystemSettings sayısı değişmez, SeedData testleri etkilenmez). SWEEP `CK_..._Type_Outbound`'a **eklenmedi** (o PaymentAddressId NULL şart koşar; SWEEP'in tam tersi gerekir). Pre-launch → temiz uygulanır.
- **Config/env değişikliği:** Yok (`reconciliation.hot_wallet_address` T76'dan seeded — production deploy NONE→gerçek adres ayarlamalı; deploy runbook WP14).
- **Docker değişikliği:** Yok.
- **Yeni dış bağımlılık:** Yok (`Hangfire.Core` zaten Transactions.csproj'da — WP1 F1; sidecar `/sweep` zaten mevcut).

## Commit & PR

- Branch: `task/WP3-sweep-dispatcher`
- Commit: `97d6dff` (implementation) + `cb0fe40`/`bcfd3e1` (docs) + validator-fix commit (06 doc-conformance + rapor/status/memory finalize)
- PR: [#171](https://github.com/turkerurganci/Skinora/pull/171)
- CI: ✓ **success** — HEAD `bcfd3e1` run [`27574022583`](https://github.com/turkerurganci/Skinora/actions/runs/27574022583) **tüm job success** (Lint/Build/**Unit**/**Integration**/Contract/**Migration dry-run**/Docker/CI Gate; Guard skipped). Integration + Migration dry-run = yeni `CK_..._Type_Sweep` + filtered unique index'in SQL Server'da temiz uygulandığını teyit eder. Validator-fix commit'i (yalnız `Docs/`) ek CI tetikler — izlenir.

## Known Limitations / Follow-up

- **Teslim sonrası buyer-favor dispute/admin-cancel iadesi (sweep'ten sonra):** Sweep `ITEM_DELIVERED + !HasActiveDispute` kapısında çalışır (WP1 payout kapısıyla aynı). Teslimden *sonra* açılan ve alıcı-lehine çözülen bir dispute, ya da geç admin-cancel, depozit zaten süpürülmüşse hot-wallet'tan iade gerektirir — bu **WP5 (dispute çözüm) / WP12 (refund override)** kapsamı; WP3'te dokunulmadı (WP1 payout'u da aynı varsayımı paylaşır). MVP'de bu nadir yol admin-elle ele alınır.
- **Sweep başarısızlık fallback'i (05 §3.3 satır 322):** Tüm denemeler başarısızsa admin'e alert + depozitten doğrudan gönderim — `OutgoingTransferDispatchJob` retry/FAILED + `TransferDispatchFailedEvent` mevcut; özel sweep-fallback orkestrasyonu MVP-sonrası.
- **05 §3.3 satır 316 doc reconciliation:** Tetik "PaymentReceivedEvent" diyor ama uygulama (owner kararı) ITEM_DELIVERED'a erteliyor; enum doc-comment güncellendi, spec doc-drift **WP17** (doc/spec mutabakat) için not edildi. Satır 317 "iade hot wallet'tan" da uygulanan depozit-kaynaklı iadeyle çelişir (WP17).
- **Overpayment kenar durumu:** SWEEP `Amount = TotalAmount` (beklenen); fazla-ödeme depozitte kalan kısmı ayrı EXCESS_REFUND ile boşaltılır (her ikisi de depozitten, toplamları bakiyeyi aşmaz).
- **CC-03 (validator follow-up, owner onaylı erteleme):** Dispatcher SWEEP kaynağını SWEEP satırının kendi `PaymentAddressId`'si yerine kardeş BUYER_PAYMENT satırından çözüyor (`OutgoingTransferDispatchJob.cs:119-152`). O lookup null dönerse satır `RetryCount`/`FAILED`/`TransferDispatchFailedEvent` olmadan sonsuza kadar PENDING döner (o işlem için hot-wallet kredilenmez, log spam). Teslim edilmiş bir tx'te onaylı BUYER_PAYMENT daima mevcut olduğundan düşük olasılık; ama terminal/alert yolu + regresyon testi yok. **Follow-up:** ya SWEEP'in kendi `PaymentAddressId`'sini kullan (DIM3-N1, indirection'ı kaldırır), ya da unresolvable-source dalını RetryCount/FAILED'a bağla + test.

## Notlar

- **Working tree (Adım -1):** temiz.
- **Adım 0 (main CI son 3 run):** hepsi success — `27567370074` / `27567370227` (WP2 #170), `27509836014` (WP1 #169).
- **Adım 2 (bağımlılık):** WP1 (#169) + WP2 (#170) main'e merge edildi (origin/main `8c0b996`). WP3 WP1 üstüne oturur, WP2'den bağımsız — ikisi de yeşil.
- **Dış varsayımlar (Adım 4):** sidecar `/api/transfer/sweep` + `sweep()` **gerçek** (stub değil — `TransferService.ts:190-251`, `transferHandlers.ts:161-200`, `routes.ts:97`; T74 energy delegation) → sidecar değişikliği yok ✓ · `reconciliation.hot_wallet_address` SystemSetting T76'dan seeded ✓ · `dotnet ef` 9.0.3 + EF `AddCheckConstraint`/`CreateIndex` SQL Server + SQLite EnsureCreated'da uygulanır (WP1/WP2 emsali) ✓ · yeni dış varsayım yok.
- **Anlama fazı:** 5-ajan paralel keşif workflow (money-flow / reconciliation-spec / consumer-wiring / check-constraint-migration / sidecar) + sentez + kendi bağımsız okumalarım → SWEEP-vs-refund çift-çekim defekti ve iki "plan-atladı" düzeltmesi (confirmation-job OutboundTypes + BuildRequest SWEEP dalı) keşfedildi.
- **Owner kararı (AskUserQuestion 2026-06-15):** "Sweep'i iade penceresi sonrasına ertele" (3 seçenek arasından; eager-sweep + hot-wallet iade-kaynaklama ve eager-sweep + admin-elle reddedildi).
