# WP1 — Escrow Tamamlama: Satıcı Payout + COMPLETED

**Faz:** F6 öncesi (PRE_F6_PLAN) | **Durum:** ✓ Tamamlandı (kabul kriterleri **PASS 8/8**; **F1 S2 sertleştirme bağımsız yeniden doğrulama PASS** — F1 fix + tüm zincir) | **Tarih:** 2026-06-14

---

## Yapılan İşler

Happy-path'in `ITEM_DELIVERED`'da kalan çıkmaz sokağı kapatıldı (03 §2.4, 02 §4.7, PRE_F6_PLAN WP1). Teslim → satıcı payout → `COMPLETED` zinciri uçtan uca bağlandı:

1. **Producer (yeni)** — `SellerPayoutQueueJob`: per-minute Hangfire job, `ITEM_DELIVERED` (ve `!IsOnHold`, `!HasActiveDispute`, henüz SELLER_PAYOUT satırı olmayan) transaction'ları tarar; gas-fee koruma net tutarını hesaplar (`ResolveSellerPayoutAsync` → `CalculateSellerPayout`); `PENDING SELLER_PAYOUT` `BlockchainTransaction` satırı oluşturur. `OutgoingTransferDispatchJob` (mevcut) bu satırı yayınlar — **değiştirilmedi**.
2. **Completion event (yeni)** — `OutgoingTransferConfirmationJob` SELLER_PAYOUT satırını `CONFIRMED` (20-blok finality) yapınca `PayoutCompletedEvent` outbox'a yayınlar (aynı SaveChanges). `IOutboxService` ctor'a eklendi. Refund satırları event üretmez.
3. **Completion consumer (yeni)** — `PayoutCompletedConsumer` (MediatR `INotificationHandler`), `Fire(Complete)` → `COMPLETED` (`CompletedAt` OnEntry'de stamp'lenir). Domain-idempotent (`Status==ITEM_DELIVERED` guard), hold-guard'lı, explicit DI kaydı.
4. **Gas estimate ayarı (yeni)** — `blockchain.payout_gas_fee_estimate_usdt` (default **0.50** USDT, 04 §7.3 örneğiyle birebir). `GasFeeSettings`'e + `SystemSettingSeed`'e (Id 59) + `SystemSettingsCatalog`'a eklendi. **Migration `WP1_AddPayoutGasFeeEstimateSetting`** — seed `HasData` model'in parçası olduğu için yeni satır `InsertData` migration'ı gerektirir (şema değişikliği YOK; CK constraint SELLER_PAYOUT'u zaten kapsıyor). Owner kararı.
5. **Payout breakdown DTO (07 §7.5)** — `TransactionDetailService` `SellerPayout` artık COMPLETED + satıcı görünümünde dolduruluyor (önceden `null`). Split, kayıttan tam türetiliyor: producer kullanılan gas estimate'i `BlockchainTransaction.GasFee`'ye snapshot'lar → `FinancialCalculator.ReconstructSellerPayoutSplit` saf aritmetikle `gasFeeFromSeller = price − net`, `gasFeeFromCommission = total − fromSeller` (drift yok). Admin DTO (`AdminTransactionQueryService.BuildPayoutDetail`) da aynı paylaşılan helper'la 0-yer-tutucudan gerçek split'e geçirildi.

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `Skinora.Shared/Events/PayoutCompletedEvent.cs`
- `Skinora.Transactions/Application/Transfers/SellerPayoutQueueJob.cs`
- `Skinora.Transactions/Application/Transfers/PayoutCompletedConsumer.cs`
- Test: `SellerPayoutQueueJobTests.cs`, `PayoutCompletedConsumerTests.cs`

**Değişen (production):**
- `Skinora.Transactions/Application/GasFee/{IGasFeeSettingsProvider,GasFeeSettingsProvider}.cs` — `PayoutGasFeeEstimateUsdt`
- `Skinora.Transactions/Application/Transfers/OutgoingTransferConfirmationJob.cs` — IOutboxService + event emit
- `Skinora.Transactions/Application/Transfers/OutgoingTransferJobsRegistrar.cs` — yeni job kaydı
- `Skinora.Transactions/Application/Lifecycle/TransactionDetailService.cs` — SellerPayout DTO
- `Skinora.Transactions/Domain/Calculations/FinancialCalculator.cs` — `ReconstructSellerPayoutSplit` + `SellerPayoutSplit`
- `Skinora.Platform/.../SystemSettingSeed.cs` (Id 59) + `SystemSettingsCatalog.cs` (catalog entry)
- `Skinora.API/Configuration/TransactionsModule.cs` — job + consumer DI
- `Skinora.API/Services/AdminTransactionQueryService.cs` — admin split (paylaşılan helper)

**Değişen (test):** `RefundDecisionServiceTests`, `AmountValidationServiceTests`, `OutgoingTransferConfirmationJobTests`, `FinancialCalculatorTests`, `GasFeeSettingsProviderTests`, `TransactionDetailServiceTests`, `SeedDataTests` (58→59 + configured array).

## Kabul Kriterleri Kontrolü

| # | Kriter (PRE_F6_PLAN WP1 + spec) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Teslim sonrası `PENDING SELLER_PAYOUT` satırı net tutarla oluşturulur | ✓ | `SellerPayoutQueueJob` + `GasAboveThreshold_QueuesPendingPayout...` (99.70), `GasBelowThreshold_PaysFullPrice` (100.00) |
| 2 | Gas-split aktif (`CalculateSellerPayout` wired, 02 §4.7 / 04 §7.3) | ✓ | producer `ResolveSellerPayoutAsync` çağırır; `FinancialCalculatorTests` + split testleri |
| 3 | SELLER_PAYOUT CONFIRMED → `PayoutCompletedEvent` emit | ✓ | `SellerPayoutConfirmed_PublishesPayoutCompletedEvent`; refund/failed emit etmez |
| 4 | Consumer `Complete`→`COMPLETED`, `CompletedAt` stamp | ✓ | `DeliveredTransaction_FiresComplete_AndStampsCompletedAt` |
| 5 | Para-güvenliği: held / disputed payout almaz (03 §2.4) | ✓ | `HeldTransaction_IsSkipped`, `DisputedTransaction_IsSkipped`, consumer `HeldTransaction_IsNotCompleted` |
| 6 | İdempotent (çift-pay yok, replay no-op) | ✓ | Sıralı replay/re-tick ✓ (`ExistingPayoutRow_IsNotDuplicated`, `RunTwice_QueuesExactlyOneSellerPayoutRow`, `AlreadyCompleted_IsNoOp`); **eşzamanlı çalıştırma artık 3-katman korumalı** (F1 sertleştirme aşağıda) — DB-seviyesi backstop test `SecondSellerPayoutRow_..._IsRejectedByUniqueIndex` + catch-yol testleri (swallow/re-throw) |
| 7 | Ödeme başarısızsa COMPLETED'a geçmez (03 §2.4 adım 4) | ✓ | event yalnız CONFIRMED'da; FAILED'da emit yok (`SellerPayoutFailed_DoesNotPublish...`) |
| 8 | COMPLETED satıcı görünümü payout breakdown (07 §7.5) | ✓ | `Completed_SellerView_Surfaces_PayoutBreakdown`; buyer view `null` |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (Transactions) | ✓ 80/80 | `dotnet test Skinora.Transactions.Tests --filter Category=Unit` — yeni: producer 7→11 (F1 sertleştirme +4: DB-backstop, run-twice, catch swallow, catch re-throw), consumer 5, confirmation emit 3, calculator split 3 |
| Unit (Platform catalog) | ✓ 7/7 | `SystemSettingsCatalogTests` — catalog↔seed kapsama korunuyor |
| Build | ✓ | `dotnet build Skinora.sln` — 0 warning, 0 error |
| Integration | ⏳ CI | SeedData (59 satır), GasFeeSettingsProvider (payout estimate 3 test), TransactionDetailService (payout breakdown 2 test) — Docker lokal yok, CI'da koşar |

## Doğrulama

**Bağımsız validator (2026-06-14, ayrı chat — rapor görülmeden, 13-ajan adversarial refute-default workflow + lokal build/test + CI kanıtı).**

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Kabul kriterleri PASS (7/8 ✓, AC6 ~Kısmi); 1 onaylanmış S2 para-güvenliği bulgusu → **merge öncesi sertleştirme** (owner kararı 2026-06-14: "önce sertleştir") |
| Bulgu sayısı | 1 (S2) + 2 küçük gözlem (S3 edge, WP9 deferral teyidi) |
| Düzeltme gerekli mi | Evet — F1 (producer çift-INSERT yarışı); ayrı yapım chat'inde, sonra yeniden doğrulama |

**Kapılar:** Adım -1 working tree temiz · Adım 0 main son-3 CI `success` (`27500668384`/`27500668387`/`27498092438`) · Adım 0b repo memory WP1 satırı mevcut. **Task branch CI HEAD `8c5f91a` run [27504561183](https://github.com/turkerurganci/Skinora/actions/runs/27504561183) — tüm job'lar success (Lint/Build/Unit/Integration/Contract/Migration dry-run/Docker/Gate).** Lokal: Release build 0W/0E; Transactions unit 435/435; Platform unit 106/106 (Shared.Tests'teki 16 "fail" = Docker-endpoint, WP1-dışı/ortamsal). Migration seed-only + snapshot tutarlı (CI migration dry-run yeşil → "no pending model changes").

**Uçtan uca zincir bağımsız doğrulandı:** ITEM_DELIVERED → `SellerPayoutQueueJob` (PENDING SELLER_PAYOUT) → `OutgoingTransferDispatchJob` (broadcast, hot-wallet'tan; `SellerPayout_DoesNotRequireDepositAddress` test) → `OutgoingTransferConfirmationJob` (CONFIRMED + `PayoutCompletedEvent` outbox, **atomik**) → `OutboxDispatcher` (`Type.GetType` çözümü, allow-list yok → `IPublisher.Publish` DI handler'a) → `PayoutCompletedConsumer` (`Fire(Complete)`) → COMPLETED (OnEntry `CompletedAt`). Üretimde tek `.Fire(Complete)` çağıranı = consumer (çift-fire yok). Finansal hesap 02 §4.7 / 07 §7.5 kanonik örnek (0.20/0.30/99.70) birebir. Gas ayarı/seed/catalog/migration doğru ve refund estimate'ten (2.0) ayrı.

### Validator Bulguları

| # | Seviye | Açıklama | Dosya | Durum |
|---|---|---|---|---|
| F1 | S2 (para) | **Producer çift-INSERT yarışı.** `(TransactionId, Type)` üzerinde DB unique constraint yok, recurring job `[DisableConcurrentExecution]` değil, `BlockchainTransaction`'da RowVersion yok → iki örtüşen tick `AnyAsync` kontrolünü ikisi de geçip iki PENDING SELLER_PAYOUT yazabilir → dispatcher ikisini de yayınlar → satıcıya çift ödeme. | `SellerPayoutQueueJob.cs`, `BlockchainTransactionConfiguration.cs` | ✅ **Düzeltildi — 3-katman defense-in-depth** (owner "tam savunma" kararı, aşağıdaki "F1 Sertleştirme" bölümü) |
| G1 | S3 (edge) | Soft-delete query-filter asimetrisi: `OutgoingTransferConfirmationJob` `.IgnoreQueryFilters()` kullanır (soft-deleted tx'in payout'unu confirm+emit eder), `PayoutCompletedConsumer` kullanmaz → broadcast↔confirm arası soft-delete edilen tx satıcıya ödenir ama COMPLETED'a geçemez (stranded; para kaybı yok). | `OutgoingTransferConfirmationJob.cs:67` ↔ `PayoutCompletedConsumer.cs:47` | Açık (düşük öncelik) |
| G2 | Gözlem | Satıcı "Ödemeniz gönderildi" push bildirimi WP9'a ertelenmiş; veri COMPLETED-view DTO pull'undan geliyor. WP1 AC'sinin push bildirimi kapsam-dışı bıraktığı teyit edilmeli. | `PayoutCompletedEvent.cs` | Teyit bekliyor |

## F1 Sertleştirme (S2 — producer çift-INSERT yarışı)

**Owner kararı (AskUserQuestion 2026-06-14): "Tam savunma"** — `[DisableConcurrentExecution]` + DB-seviyesi filtered unique index + producer catch (defense-in-depth). Üç katman birlikte çift-pay'i kapatır:

1. **Hangfire kilidi (uygulama seviyesi).** `SellerPayoutQueueJob.Execute()` artık `[DisableConcurrentExecution(50)]`. Örtüşen tick'ler Hangfire distributed lock'u ile serialize edilir (tek + çok-instance). Lock timeout (50sn) cron aralığından (60sn) kısa tutuldu → kilidi alamayan bekleyen tick bir sonraki tick fire etmeden vazgeçer, waiter yığılmaz. `Skinora.Transactions.csproj`'a `Hangfire.Core 1.8.18` ref eklendi (kardeş `Skinora.Notifications` `[AutomaticRetry]` için zaten Hangfire.Core referans ediyor → modül-içi job-filter emsali mevcut).
2. **DB-seviyesi backstop (ironclad).** Yeni filtered unique index `UQ_BlockchainTransactions_SellerPayout_TransactionId` = `(TransactionId) WHERE Type = 'SELLER_PAYOUT'`. Kilidi atlayan ikinci INSERT veritabanı tarafından reddedilir → çift-pay **imkansız**. Refund / diğer outbound tipler bilinçli olarak kısıtlanmaz (bir işlemin meşru biçimde birden çok refund satırı olabilir). SELLER_PAYOUT satırını üreten **tek production yazıcı** producer'dır ve retry'lar aynı satırı yeniden kullanır → unique index hiçbir meşru ikinci satırı bloklamaz. Named-index overload ile mevcut non-unique `IX_BlockchainTransactions_TransactionId` ile yan yana yaşar. (`UQ_BotRecoveryItems_TransactionId` T103b-2 emsaliyle aynı backstop deseni.)
3. **Producer catch (zarif degrade).** `QueuePayoutAsync` SaveChanges artık `catch (DbUpdateException)`: reddedilen satırı **detach** eder (paylaşılan batch DbContext'i sonraki adaylar için temiz bırakır), SELLER_PAYOUT satırının artık var olduğunu re-query ile doğrular → varsa idempotent **no-op** olarak yutar + warning log, yoksa **re-throw** (ilgisiz bir DB hatasını maskelemez). Re-query, mevcut `alreadyQueued` ön-kontrolüyle birebir aynı non-navigating predicate (`BlockchainTransaction` `ISoftDeletable` değil → query filter yok → yanlış re-throw imkansız).

**Migration:** `20260614173940_WP1_AddSellerPayoutUniqueIndex` — yalnız `CreateIndex` (Up) / `DropIndex` (Down); **şema-only, seed yok**. Pre-launch → mevcut SELLER_PAYOUT verisi yok → temiz uygulanır (backfill/duplicate riski yok). `has-pending-model-changes` → "No changes" (snapshot drift yok).

**Test (+4 → SellerPayoutQueueJobTests 7→11):**
- `SecondSellerPayoutRow_ForSameTransaction_IsRejectedByUniqueIndex` — DB-seviyesi backstop: ikinci SELLER_PAYOUT INSERT'ü filtered unique index `DbUpdateException` ile reddeder (SQLite EnsureCreated partial index'i gerçekten oluşturur ve uygular → backstop kanıtlı).
- `RunTwice_QueuesExactlyOneSellerPayoutRow` — uçtan uca sıralı idempotency.
- `ConcurrentInsertRace_SwallowsDuplicate_AndDoesNotDoublePay` — **catch swallow yolu**: rakip tick `AnyAsync` ile `SaveChanges` arasındaki pencerede satırı commit eder (mid-SaveChanges test seam) → gerçek index reddeder → catch detach + re-query + idempotent no-op + warning; tam olarak 1 satır, exception sızmaz.
- `NonDuplicateDbUpdateException_IsRethrown_NotMasked` — **catch re-throw yolu**: SELLER_PAYOUT satırı oluşmamış ilgisiz `DbUpdateException` → `if (!nowQueued) throw` ile yüzeye çıkar, maskelenmiz.

**Bağımsız ön-doğrulama (yapım-içi adversarial review workflow, 5-boyut/21-ajan, refute-default):** 16 ham bulgu → **15 çürütüldü, 1 onaylı S2** (catch-yolu test kapsamı yoktu — `RunTwice` `AnyAsync` guard'ı sayesinde yeşildi, catch'i hiç çalıştırmıyordu; index testi job'ı baypas ediyordu → false confidence). Onaylı bulgu **bu PR'da kapatıldı** (yukarıdaki 2 catch-yolu testi swallow + re-throw branch'lerini deterministik olarak çalıştırır). Çürütülen önemliler: named-index overload'ın mevcut index'i ezdiği (EF ikisini de üretir — snapshot+migration teyit), filtre `[Type]='SELLER_PAYOUT'` SQLite↔SQL Server taşınabilirliği (her iki provider'da çalışır — test yeşil), Hangfire.Core version conflict (Notifications zaten 1.8.18), index'in refund satırlarını kısıtladığı (yalnız SELLER_PAYOUT filtresi), catch'in ilgisiz hatayı yuttuğu (re-throw branch + test).

**Lokal kapılar:** Transactions unit **80/80** (`Category=Unit`, +4) · Debug + Release build **0W/0E** · `dotnet format --verify-no-changes --severity error` temiz · `has-pending-model-changes` → drift yok.

## Bağımsız Yeniden Doğrulama (F1 + tüm zincir) — VERDICT: ✓ PASS

**İkinci bağımsız validator (2026-06-14, ayrı chat, izolasyon §3.3 — yapım raporu görülmeden kendi verdict'i oluşturuldu, sonra rapor ile karşılaştırıldı). Owner "önce sertleştir" sonrası F1 sertleştirmenin + WP1 zincirinin yeniden doğrulaması.**

**Kapılar:** Adım -1 working tree temiz · Adım 0 main son-3 CI `success` (`27500668387`/`27500668384`/`27498092438`) · Adım 0b repo memory WP1 satırı mevcut · Adım 8a task branch CI HEAD `8f476d6` run [27507929137](https://github.com/turkerurganci/Skinora/actions/runs/27507929137) **tüm job success** + F1 commit `21bc790` run [27507547831](https://github.com/turkerurganci/Skinora/actions/runs/27507547831) **tüm job success** (Lint/Build/Unit/**Integration**/Contract/**Migration dry-run**/Docker/Gate; `0. Guard` PR'da doğru biçimde `skipped`). Lokal kanıt (bu doğrulama anında üretildi): Release build **0 error**, Transactions unit **80/80**.

**F1 fix bağımsız onayı — 3 katmanın her biri kanıtla doğrulandı:**
- **Katman 1 (`[DisableConcurrentExecution(50)]`):** Attribute, registrar'ın Hangfire'a verdiği tam metoda (`OutgoingTransferJobsRegistrar.cs:49` `job => job.Execute()`) uygulanmış. Hangfire SqlServer storage yapılandırılmış (`HangfireModule.cs:53` `UseSqlServerStorage` + `AddHangfireServer`) → distributed lock **çok-instance'da da** çalışır. 50sn timeout < 60sn cron → waiter yığılmaz. Tek başına yeterli değil (yalnız pencereyi daraltır) → Katman 2/3 gerekçeli.
- **Katman 2 (filtered unique index):** `Type` global `EnumToStringConverter` ile **nvarchar** saklanır (`AppDbContext.cs:66`; snapshot `nvarchar`; mevcut CK constraint'ler string literal kullanır) → `[Type]='SELLER_PAYOUT'` filtresi eşleşir. Backend genelinde SELLER_PAYOUT satırını **INSERT eden tek yol producer**'dır — `PayoutIssueService` yalnız **okur** (`:149-155`), dispatch/confirmation job'ları aynı satırı **UPDATE** eder, `RefundDecisionService` yalnız hesaplar. Index hiçbir meşru ikinci satırı bloklamaz. Refund/diğer outbound tipler kısıtlanmaz (filtre SELLER_PAYOUT-only). Snapshot'ta index mevcut (`:2108`) → drift yok; migration en yeni timestamp (gas-seed `…160541` sonrası); CI Integration + Migration dry-run yeşil → SQL Server'da temiz uygulanır.
- **Katman 3 (`catch(DbUpdateException)`):** Re-query yalnızca SELLER_PAYOUT satırı **gerçekten varsa** yutar; yoksa `if(!nowQueued) throw` ile ilgisiz hatayı maskelemeden yüzeye çıkarır. `BlockchainTransaction` `ISoftDeletable` **değil** (sade class, RowVersion da yok) → re-query query-filter ile kandırılamaz; detach yalnız `payoutRow`'u temizler, batch DbContext sonraki adaylar için temiz kalır.

**Test sadakati:** 4 yeni test doğru şeyi kanıtlıyor — `SecondSellerPayoutRow_…RejectedByUniqueIndex` (SQLite EnsureCreated partial index'i gerçekten oluşturur+uygular), `ConcurrentInsertRace_SwallowsDuplicate` (paylaşılan `:memory:` connection üzerinde rakip commit → gerçek index reddeder → catch swallow-yolu deterministik çalışır), `NonDuplicateDbUpdateException_IsRethrown` (re-throw yolu), `RunTwice` (`AnyAsync` idempotency). Yapım chat'inin kendi "false confidence" bulgusu kapatılmış. SQL Server tarafı CI Integration/Migration ile kapsanır (SQLite↔SQL Server ayrımı bilinçli).

**Zincir regresyonu:** producer → `OutgoingTransferDispatchJob` (F1'de **değişmedi**) → `OutgoingTransferConfirmationJob` (CONFIRMED + `PayoutCompletedEvent` **atomik**) → `PayoutCompletedConsumer` (üretimde **tek** `.Fire(Complete)` çağıranı, `PayoutCompletedConsumer.cs:92`, domain-idempotent + hold-guard) → COMPLETED. Finansal hesap F1'de dokunulmadı; testler kanonik 99.70/100 değerlerini kanıtlar. F1 yalnız producer eşzamanlılığı + index + test'e dokundu → refund/diğer akışlar etkilenmez.

**Kendi adversarial pass'im (refute-default, subagent altyapısı org-policy ile bloklandığı için inline yürütüldü):** Tüm aday endişeler çürütüldü (Type-as-int, meşru-ikinci-satır, catch-masking, detach-corruption, multi-instance-lock, test-false-confidence, drift/dependency-conflict, zincir/math regresyonu) → **0 bloke-edici/onaylı bulgu**.

| Kabul Kriteri | İlk val. | Bu yeniden val. |
|---|---|---|
| AC1–AC5, AC7, AC8 | ✓ | ✓ (F1 dokunmadı; testler yeşil) |
| **AC6 (idempotency / çift-pay yok)** | ~Kısmi | **✓** (3-katman; eşzamanlı INSERT yarışı DB-seviyesinde imkansız) |

**Açık (bloke etmeyen, documented):** G1 (soft-delete query-filter asimetrisi) — bağımsız teyit: `BlockchainTransaction` soft-deletable değil → ilgili `IgnoreQueryFilters` no-op; risk yalnız **Transaction** soft-delete edilirse broadcast↔confirm penceresinde stranded state kaydı (satıcı doğru biçimde **bir kez** ödenmiştir, **çift-pay/para kaybı yok**), F1 tarafından oluşturulmadı, **S3 edge**. G2 — COMPLETED bildirim/realtime push WP9 kapsamında (PRE_F6_PLAN ile uyumlu, payload hazır). İkisi de KRİTİK değil → §8.4 documented risk, merge'i bloklamaz.

**Rapor karşılaştırması:** Tam uyumlu — bağımsız verdict yapım raporundaki AC 8/8 + 3-katman + G1/G2 değerlendirmesiyle birebir örtüşüyor; uyuşmazlık yok.

## Altyapı Değişiklikleri
- **Migration (F1 sertleştirme):** **Var** — `20260614173940_WP1_AddSellerPayoutUniqueIndex` (yalnız `CreateIndex`/`DropIndex`; **şema-only**, filtered unique index `(TransactionId) WHERE Type='SELLER_PAYOUT'`).
- **Migration (gas estimate seed):** **Var** — `20260614160541_WP1_AddPayoutGasFeeEstimateSetting` (yalnızca `InsertData`/`DeleteData` yeni seed satırı için; **şema değişikliği YOK**). Seed `HasData(SystemSettingSeed.All)` model snapshot'ının parçası olduğundan yeni satır migration gerektirir (ilk "migration yok" varsayımı bu seed-data noktasında yanlıştı; CI migration dry-run + `Model_HasNoPendingChanges` yakaladı, eklendi). `SELLER_PAYOUT` CHECK constraint'i `CK_BlockchainTransactions_Type_Outbound` zaten kapsar; yeni ayar seed-default (mandatory değil → 21-mandatory startup gate etkilenmez).
- **Config/env:** Yeni SystemSetting `blockchain.payout_gas_fee_estimate_usdt` (default 0.50, admin-tunable). Yeni recurring job `seller-payout-queue` (cron `* * * * *`).
- **Docker:** Yok.

## Mini Güvenlik Kontrolü
- **Secret sızıntısı:** Yok (yeni secret/connection string yok).
- **Auth/authorization:** Yeni endpoint yok (background job + consumer). DTO `SellerPayout` yalnız satıcı görünümünde döner (07 §7.5).
- **Input validation:** Negative-payout guard (≤0 → satır oluşturmaz, error log); gas estimate read-side `>0` fallback; `ToAddress` boşsa skip.
- **Yeni dış bağımlılık:** `Hangfire.Core 1.8.18` `Skinora.Transactions.csproj`'a eklendi (`[DisableConcurrentExecution]` job-filter için). Çözümde zaten kullanımda (Skinora.Notifications doğrudan, Skinora.API transitif → yeni paket/sürüm değil, transitive collision yok). Başka yeni NuGet yok.
- **Para-güvenliği:** held/disputed gate; idempotent (çift-pay yok); SaveChanges atomik; COMPLETED yalnız on-chain finality (20-blok) sonrası.

## Tasarım Kararları (owner-onaylı)
- **Gas estimate kaynağı:** Yeni `blockchain.payout_gas_fee_estimate_usdt` (0.50) — refund estimate'ten (2.0) ayrı, çünkü 02 §4.7 split'i satıcı-gönderim gas'ına göre ölçer (refund estimate ~1.8 over-deduct ederdi). Owner kararı (AskUserQuestion).
- **Producer mekanizması:** Polling job (`TradeOfferDispatchJob`/`OutgoingTransferDispatchJob` deseni) — kaçan webhook'a dayanıklı, idempotent, modül-doğru. Owner kararı.
- **Completion mekanizması:** Event-driven (plan WP1'de yazıldığı gibi) — confirmation job emit → consumer fire.

## Known Limitations / Follow-up
- **Held-at-confirm edge:** Payout broadcast ile CONFIRMED arasında (~1dk) tx EMERGENCY_HOLD'a alınırsa, consumer Complete'i fire edemez (state machine held'de tüm trigger'ları reddeder) → tx ITEM_DELIVERED+held kalır, payout zincirde gitmiştir. Consumer error-log + return (sonsuz retry yok). Hold-release sonrası yeniden-tamamlama **WP5/WP7** (hold-release akışı) devir; nadir admin-aksiyonu edge'i.
- **gasFee tahmini:** MVP estimate (0.50 USDT); gerçek runtime Energy/Bandwidth ölçümü **T74** devir (`refund_gas_fee_estimate_usdt` ile aynı desen).
- **COMPLETED bildirim/realtime push:** WP1 yalnız state geçişini yapar; satıcı "Ödemeniz gönderildi" bildirimi + realtime TransactionStatusChanged push **WP9** (realtime/notification tamlığı) devir. PayoutCompletedEvent payload (txHash + net amount) bu tüketiciler için hazır.

## Commit & PR
- Branch: `task/WP1-escrow-completion-payout`
- Commit: `8c5f91a` — WP1 zinciri + seed migration + Layer-2 bypass log
- Commit (F1 sertleştirme): `21bc790` — `[DisableConcurrentExecution]` + filtered unique index + producer catch + migration + 4 test + rapor/status/memory
- PR: [#169](https://github.com/turkerurganci/Skinora/pull/169)
- CI: F1 sertleştirme HEAD `21bc790` run [27507547831](https://github.com/turkerurganci/Skinora/actions/runs/27507547831) **tüm job'lar success** (Lint/Build/Unit/Integration/Contract/Migration dry-run/Docker/Gate) — yeni filtered unique index SQL Server'da temiz uygulandı, `Model_HasNoPendingChanges` yeşil (drift yok), integration testler geçti

## Notlar
- **Working tree:** Oturum başında temiz.
- **Main CI startup:** Son 3 run `success` (`27500668384`, `27500668387`, `27498092438`).
- **Dış varsayımlar:** (1) Yeni decimal SystemSetting key, validator generic positive-number kuralıyla otomatik kapsanır (`SystemSettingsValidator.cs:242-248` — kanıt: kod okundu) ✓. (2) `BlockchainTransaction.GasFee` USDT breakdown'da kullanımı mevcut (`AdminTransactionQueryService.BuildPayoutDetail` zaten `payout.GasFee` okur) — snapshot repurpose tutarlı ✓. (3) Docker lokal yok → integration testler CI'da (proje deseni) ✓.
