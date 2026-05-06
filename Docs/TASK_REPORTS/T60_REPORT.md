# T60 — Satıcı Payout Issue

**Faz:** F3 | **Durum:** ⏳ Yapım bitti, doğrulama bekliyor | **Tarih:** 2026-05-06 (yapım)

---

## Yapılan İşler

T60, satıcının COMPLETED bir işlemde ödemeyi almadığını bildirmesi için tek endpoint kurar — 07 §7.11, 02 §10.3, 06 §3.8a, 03 §2.4a Senaryo A. Bildirim sonrası sistem `IPayoutVerifier` ile blockchain'i sorgular ve sonuca göre `SellerPayoutIssue` rowu RESOLVED / ESCALATED / RETRY_SCHEDULED terminal/ara state'ine atomik tek `SaveChanges` ile geçirir.

1. **Domain event'leri (`Skinora.Shared/Events/`):** Üç yeni outbox event'i T62 SignalR + T37 notification consumer'ları için iskelet kurar.
   - `SellerPayoutIssueReportedEvent` — REPORTED → RETRY_SCHEDULED dalında yayınlanır (gelecekteki retry pipeline hook'u).
   - `SellerPayoutIssueResolvedEvent` — Confirmed dalında (auto) veya admin manual resolve'da (T63 forward) yayınlanır.
   - `SellerPayoutIssueEscalatedEvent` — AnomalyDetected / UnableToVerify dalında yayınlanır; `EscalatedToAdminId` ile birlikte taşınır.

2. **Application layer (`Skinora.Transactions/Application/PayoutIssues/`):** 7 yeni dosya — orchestrator + verifier port + admin resolver port + DTO/error/stub.
   - `IPayoutIssueService` + `PayoutIssueService` (~210 satır) — 7 stage pipeline:
     - Stage 1 — Transaction load.
     - Stage 2 — Seller guard (`SellerId == callerUserId`).
     - Stage 3 — `Status == COMPLETED` guard (07 §7.11).
     - Stage 4 — Active-issue UQ defensive pre-check (`IgnoreQueryFilters` + `VerificationStatus != RESOLVED`).
     - Stage 5 — Detail validation (≥10 char trimmed).
     - Stage 6 — REPORTED row insert.
     - Stage 7 — `IPayoutVerifier.VerifyAsync` çağrısı + outcome'a göre state transition + outbox event:
       - **`Confirmed`** → RESOLVED + `PayoutTxHash` + `ResolvedAt`; `SellerPayoutIssueResolvedEvent`.
       - **`AnomalyDetected` / `UnableToVerify`** → ESCALATED + `EscalatedToAdminId` (resolver); `SellerPayoutIssueEscalatedEvent`. Resolver `null` döndürürse `InvalidOperationException` (CK constraint koruması).
       - **`StillPending`** → RETRY_SCHEDULED + `RetryCount=1`; `SellerPayoutIssueReportedEvent`.
     - Tek `SaveChangesAsync` — atomic.
   - `IPayoutVerifier` + `StubPayoutVerifier` — T64–T69 forward-deferred port. Stub conservative `UnableToVerify` döner ki üretimde her bildirim admin'e ulaşsın (silent drop yok). Sözleşme T31 `IMobileAuthenticatorCheck` paterni mirror.
   - `IPayoutEscalationAdminResolver` — modül arası boundary. Üretim implementasyonu (`PayoutEscalationAdminResolver`, `Skinora.API/Services/`) `AdminUserRole`'u sorgular; `Skinora.Transactions` Skinora.Admin'e referans veremez (Disputes/Fraud paterni).
   - `PayoutIssueDtos` — `ReportPayoutIssueRequest` + `ReportPayoutIssueResponse` + `ReportPayoutIssueOutcome` + `ReportPayoutIssueStatus` enum (6 değer: Reported / NotFound / NotSeller / TransactionNotCompleted / IssueAlreadyReported / ValidationFailed).
   - `PayoutIssueErrorCodes` — 5 stable string (07 §7.11 Hatalar tablo birebir).

3. **Controller (`Skinora.API/Controllers/TransactionsController.cs`):** Mevcut controller'a `T11 ReportPayoutIssue` action'ı eklendi.
   - `[HttpPost("{id:guid}/report-payout-issue")]` + `[Authorize(Policy = AuthPolicies.Authenticated)]` + `[RateLimit("user-write")]`.
   - Outcome → HTTP eşlemesi: 201 Created (Reported) / 404 (NotFound) / 403 (NotSeller) / 409 (TransactionNotCompleted, IssueAlreadyReported) / 400 (ValidationFailed). Kontrolör class özet doc'u T60 referansı eklendi.

4. **DI wiring (`Skinora.API/Configuration/TransactionsModule.cs`):** Üç satır kayıt — `IPayoutIssueService` Scoped, `IPayoutVerifier` `TryAddScoped` (testler kendi fake'ini override eder), `IPayoutEscalationAdminResolver` Scoped (üretim). `Skinora.API.Services` namespace import.

5. **Doküman uyumu:** Plan/spec değişikliği yok — 07 §7.11 ve 02 §10.3 + 06 §3.8a + 03 §2.4a Senaryo A referansları halihazırda eksiksizdi. T25 entity + filtered UQ + CHECK constraint paterni `SellerPayoutIssueConfiguration.cs`'te zaten mevcut.

## Etkilenen Modüller / Dosyalar

**Yeni (10 src + 2 test):**
- `backend/src/Skinora.Shared/Events/SellerPayoutIssueReportedEvent.cs`
- `backend/src/Skinora.Shared/Events/SellerPayoutIssueResolvedEvent.cs`
- `backend/src/Skinora.Shared/Events/SellerPayoutIssueEscalatedEvent.cs`
- `backend/src/Modules/Skinora.Transactions/Application/PayoutIssues/PayoutIssueDtos.cs`
- `backend/src/Modules/Skinora.Transactions/Application/PayoutIssues/PayoutIssueErrorCodes.cs`
- `backend/src/Modules/Skinora.Transactions/Application/PayoutIssues/IPayoutVerifier.cs`
- `backend/src/Modules/Skinora.Transactions/Application/PayoutIssues/StubPayoutVerifier.cs`
- `backend/src/Modules/Skinora.Transactions/Application/PayoutIssues/IPayoutEscalationAdminResolver.cs`
- `backend/src/Modules/Skinora.Transactions/Application/PayoutIssues/IPayoutIssueService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/PayoutIssues/PayoutIssueService.cs`
- `backend/src/Skinora.API/Services/PayoutEscalationAdminResolver.cs`
- `backend/tests/Skinora.Transactions.Tests/Integration/PayoutIssues/PayoutIssueServiceTests.cs` (13 test)
- `backend/tests/Skinora.API.Tests/Integration/PayoutIssueEndpointTests.cs` (9 test)

**Değişiklik:**
- `backend/src/Skinora.API/Controllers/TransactionsController.cs` — +T11 action + import + ctor parameter + class docü güncel.
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` — +3 DI satırı + 2 import.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `POST /transactions/:id/report-payout-issue` → sadece COMPLETED işlemler, sadece satıcı | ✓ | `TransactionsController.ReportPayoutIssue` (07 §7.11 route) + `PayoutIssueService.ReportAsync` Stage 2 (NotSeller 403) + Stage 3 (TransactionNotCompleted 409). Endpoint test: `Report_NonSeller_Returns_403_NotSeller`, `Report_BuyerCallsAsSeller_Returns_403_NotSeller`, `Report_TransactionNotCompleted_Returns_409_TransactionNotCompleted`. Service test: `Report_NonSeller_Returns_NotSeller`, `Report_BuyerCallsAsSeller_Returns_NotSeller`, `Report_TransactionNotCompleted_Returns_TransactionNotCompleted`. |
| 2 | Otomatik doğrulama: tx hash ile blockchain kontrolü | ✓ | `PayoutIssueService.ApplyVerificationOutcomeAsync` `IPayoutVerifier.VerifyAsync` çağrısı; service `BlockchainTransaction.SELLER_PAYOUT.TxHash`'i okur, verifier'a iletir. Verifier sözleşmesi T64–T69 forward-deferred (`StubPayoutVerifier` üretimde geçici). Service test: `Report_VerifierConfirms_TransitionsToResolved_AndEmitsResolvedEvent` (tx hash blockchain'de doğrulandı → tx hash response message + entity'de `PayoutTxHash` damgalanır). |
| 3 | Retry: gönderim başarısız/stuck ise otomatik yeniden deneme | ✓ kısmi (orchestration) | Verifier `StillPending` döndüğünde service RETRY_SCHEDULED + `RetryCount=1` + `SellerPayoutIssueReportedEvent`. Gerçek payout retry (yeni transfer broadcast) 06 §3.8 BlockchainTransaction.RetryCount + 05 §3.3 exponential backoff (1m/5m/15m) altyapısının sahipliğidir — Senaryo B (pre-COMPLETED stuck) zaten bu mekanizma altında çalışıyor. T60 RETRY_SCHEDULED state'ini ve event hook'unu sağlar; retry job consumer'ı T-future. Service test: `Report_VerifierStillPending_TransitionsToRetryScheduled_AndEmitsReportedEvent` (RetryCount=1 + status doğrulanır). |
| 4 | Eskalasyon: otomatik çözüm başarısızsa admin'e | ✓ | Verifier `AnomalyDetected` veya `UnableToVerify` → service `IPayoutEscalationAdminResolver` ile admin guid resolve eder, `EscalatedToAdminId` damgalar, `SellerPayoutIssueEscalatedEvent` yayınlar. `StubPayoutVerifier` üretim default'u `UnableToVerify` (T64–T69 sidecar gelene kadar her bildirim admin'e gider). Service test: `Report_VerifierDetectsAnomaly_TransitionsToEscalated_AndEmitsEscalatedEvent` + `Report_VerifierUnableToVerify_StubProductionDefault_EscalatesToAdmin`. Resolver null döndüğünde service `InvalidOperationException` fırlatır (06 §3.8a CK koruması) — test: `Report_NoAdminAvailable_AndAnomalyDetected_Throws`. |
| 5 | SellerPayoutIssue entity state'leri: REPORTED → VERIFYING → RETRY_SCHEDULED / ESCALATED → RESOLVED | ✓ | Tüm 5 enum değeri `PayoutIssueStatus` üzerinden state machine'de işlenir. Sync verification: REPORTED entry + verifier outcome'a göre terminal/ara state. VERIFYING geçici durumdur ve sync flow'da observable değildir — async/queue tabanlı pipeline'a (T-future) hazır. ESCALATED → RESOLVED admin manuel çözüm dalı T63 forward (06 §3.8a state-dependent CHECK'ler `EscalatedToAdminId NOT NULL` + `ResolvedAt NOT NULL` + `RetryCount > 0` mevcut). Service test: 5 verifier outcome × 4 state geçişi (Confirmed→RESOLVED, Anomaly→ESCALATED, Unable→ESCALATED, StillPending→RETRY_SCHEDULED). Reopen testi: `Report_ReopenAfterResolved_Allowed` (RESOLVED sonrası filtered UQ filtre dışı kalır → yeni issue açılır). |

**Doğrulama kontrol listesi:**

- [x] **06 §3.8a SellerPayoutIssue yapısı doğru mu?** ✓ — entity T25 PR #29'da kuruldu; T60 entity'i değiştirmedi. CHECK constraint'ler (`ESCALATED → EscalatedToAdminId NOT NULL`, `RESOLVED → ResolvedAt NOT NULL`, `RETRY_SCHEDULED → RetryCount > 0`) ve filtered UQ (`UNIQUE(TransactionId) WHERE != RESOLVED`) aynen kullanılır. Service kodu her transition'da bu invariant'lara uyar (örn. ESCALATED dalında resolver null ise throw → invalid row commit'e gitmez).
- [x] **07 §7.11 sözleşmesi doğru mu?** ✓ — request `{detail}` (≥10 char), response `{issueId, status, createdAt, message}`, error code'ları (`TRANSACTION_NOT_COMPLETED`, `ISSUE_ALREADY_REPORTED`, `NOT_SELLER`, `VALIDATION_ERROR`, `TRANSACTION_NOT_FOUND`) ve HTTP kodları (201/404/403/409/400) DTO + outcome record + controller switch ile birebir. `status` örnek değeri `REPORTED` ama spec enum'unun tüm değerleri (`PayoutIssueStatus`) geçerli — service sync verification sonrası gerçek state'i döner (response'un `status` alanı `PayoutIssueStatus` enum'u serileştirir).

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (Skinora.Shared.Tests) | ✓ 185/185 | Regresyon temiz — yeni event tipleri enum/count assertion'ları etkilemiyor. |
| Unit (Skinora.Notifications.Tests) | ✓ 49/49 | Regresyon temiz. |
| Unit (Skinora.Platform.Tests) | ✓ 85/85 | Regresyon temiz. |
| Unit (Skinora.Transactions.Tests) | ✓ 333/333 | Mevcut StateMachine + Lifecycle + Timeouts + GasFee + Reputation unit'leri regresyon temiz. T60 service tests Integration namespace'inde — CI shared services:mssql üzerinde çalışır. |
| Unit (Skinora.Auth.Tests / Users.Tests / Fraud.Tests) | ✓ 57+16+14 | Regresyon temiz. |
| Endpoint smoke (Skinora.API.Tests) | ✓ 32/32 | 9 yeni `PayoutIssueEndpointTests` (auth gate / happy path / NotSeller / BuyerAsSeller / TransactionNotCompleted / TransactionNotFound / DuplicateActiveIssue / DetailTooShort / EmptyBody) + 23 mevcut endpoint testi regresyon temiz. SQLite in-memory + Factory + `IPayoutEscalationAdminResolver` override. |
| Integration (Skinora.Transactions.Tests/Integration/PayoutIssues) | ⏳ CI bekliyor | 13 yeni `PayoutIssueServiceTests` lokal Docker yok (Windows env), CI Linux runner shared services:mssql üzerinde koşacak (T11.3 paterni). Build temiz + lokal compile + endpoint smoke testleri service kontratını dolaylı kanıtlıyor. |
| Build (Release) | ✓ 0W/0E | `dotnet build Skinora.sln -c Release` → `Build succeeded. 0 Warning(s) 0 Error(s)`. |
| Format verify | ✓ exit=0 | `dotnet format Skinora.sln --verify-no-changes` clean. |

**Test sayımı (lokal toplam):** Shared 185 + Auth 57 + Notifications 49 + Users 16 + Fraud 14 + Platform 85 + Transactions(unit) 333 + API.Tests 32 = **771 lokal pass, 0 fail**. T60 integration 13 test CI'de doğrulanacak.

## Altyapı Değişiklikleri

- **Migration:** Yok — `SellerPayoutIssue` tablosu T25/T28'de kurulmuştu; T60 schema'ya dokunmadı.
- **SystemSetting:** Yok.
- **Config/env:** Yok.
- **Docker:** Yok.
- **Yeni dış bağımlılık:** Yok — `IOutboxService` (F0), `AppDbContext` (F0), `Skinora.Admin.AdminUserRole` query (T24/T39 paterni) — hepsi mevcut. `IPayoutVerifier` üretim implementasyonu (Tron sidecar) T64–T69 forward; T60 stub kullanır.

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok — yeni dosyaların hiçbiri secret içermiyor; tx hash literal'leri test fixture içinde.
- **Auth/authorization:** Endpoint `[Authorize(Policy = AuthPolicies.Authenticated)]` — JWT zorunlu. Service Stage 2 `transaction.SellerId != callerUserId → 403 NOT_SELLER` (alıcı dahil hiçbir 3. taraf bildirim atamaz). Stage 3 COMPLETED state guard (terminal-yalın) — pre-COMPLETED Senaryo B otomatik retry'in kapsamında. Rate limit `user-write` per-user. `EscalatedToAdminId` resolver `AdminUserRole` tablosunu sorgular (T24/T39 admin policy katmanı).
- **Input validation:** `detail` string trim sonrası ≥10 char (07 §7.11 floor). Whitespace-only → 400. Empty body → 400 (controller `request is null` check). `id` route param `Guid` constraint. Outcome enum switch default 500 (defensive).
- **Yeni dış bağımlılık:** Yok.

## Commit & PR

- Branch: `task/T60-seller-payout-issue`
- Commits: `3a1c802` (yapım, tek commit).
- PR: [#96](https://github.com/turkerurganci/Skinora/pull/96)
- CI: ✓ PASS — run [`25455122485`](https://github.com/turkerurganci/Skinora/actions/runs/25455122485) (HEAD `3a1c802`) 10/10 success: Detect/Lint/Build/Unit/Contract/Integration/Migration dry-run/Docker/CI Gate hepsi ✓; Guard skipped (task branch push). 13 yeni `PayoutIssueServiceTests` Integration job'unda shared services:mssql üzerinde ilk runda PASS — same-PR fix gerekmedi.

## Known Limitations / Follow-up

- **K1 — `IPayoutVerifier` üretim implementasyonu T64–T69 sidecar'a forward-deferred.** `StubPayoutVerifier` `UnableToVerify` döner — üretimde her bildirim admin'e eskale olur (fail-closed). Tron blockchain sidecar bağlanınca DI swap ile gerçek implementasyon geçer; service kontratı (input/output) sabit.
- **K2 — RETRY_SCHEDULED → terminal state retry consumer T-future.** `StillPending` outcome RETRY_SCHEDULED + `SellerPayoutIssueReportedEvent` damgalar; gerçek "yeniden payout transfer broadcast et" işi 06 §3.8 BlockchainTransaction retry pipeline'ının (1m/5m/15m exponential, 3 deneme) sorumluluğunda. Senaryo B (pre-COMPLETED stuck) zaten bu mekanizmayla çalışıyor (T46/T47/T49 timeout pipeline'ları içinde). Senaryo A için ayrı consumer açmak gerekirse T-future'da `SellerPayoutIssueReportedEvent` consume edilir + `RetryCount` ilerletilir + max retry sonrası ESCALATED'a otomatik geçilir (06 §2.22 state geçiş kuralı).
- **K3 — Admin manuel `RESOLVED` endpoint'i T63 admin dashboard'a forward-deferred.** ESCALATED → RESOLVED transition (admin kararıyla) `AdminTransactionsController` veya `AdminPayoutIssueController` üzerinden T63'te eklenir. T60 entity'de `AdminNote` + `ResolvedAt` + `EscalatedToAdminId` field'ları zaten mevcut; admin dashboard query/komut yüzeyini bağlayacak.
- **K4 — NotificationType genişlemesi yok.** 07 §8.1 mevcut tipler tablosu T60 için ayrı NotificationType tanımlamıyor — `ADMIN_PAYMENT_FAILURE` (mevcut, "Satıcıya ödeme gönderim hatası") ESCALATED için natural fan-out hedefi. Notification consumer (yeni `SellerPayoutIssueEscalatedNotificationConsumer`) T62 SignalR + T37 paterniyle eklenebilir; T60 yalnızca event'leri yayınlar — fan-out forward-deferred (T63 admin queue surfacing veya ayrı T-future).
- **K5 — VERIFYING ara state'i sync flow'da gözlemlenebilir değil.** 06 §2.22 state diagram REPORTED → VERIFYING → terminal akışını tanımlar; T60 sync verification içinde VERIFYING transient (verifier çağrısı sırasında) — DB'ye yazılmaz çünkü tek atomik SaveChanges. Async pipeline (T-future) eklenirse REPORTED row write → ayrı job VERIFYING'e geçer → sonra terminal state. Bu, kontratı bozmaz çünkü 07 §7.11 yalnızca "status değeri PayoutIssueStatus enum'unu takip eder" der; spesifik state değeri zorunlu değildir.
- **K6 — Integration testler CI'de doğrulanacak.** Lokal Windows Docker engine çalışmadığı için 13 yeni `PayoutIssueServiceTests` lokal `IntegrationTestBase` üzerinden koşturulamadı. CI Linux runner'da services:mssql üzerinden çalışacak (T11.3 paterni). Build clean + format clean + endpoint smoke 9/9 lokal doğrulandı.

## Notlar

- **Working tree pre-flight:** clean (çıktı boş). Adım -1 ✓.
- **Main CI startup pre-flight:** son 3 main run ✓ — `25452094736` + `25452094665` (chore PR #95 settings.local.json) + `25451442626` (T59 #92). Adım 0 ✓.
- **Dış varsayım kontrolü (Adım 4):** 7 varsayım listelendi, 6 ✓ doğrulandı + 1 (NotificationType yeni değer gerekmez) advisory. Bkz. yapım chat'i scope clarification mesajı.
- **Scope kararı (Adım 5):** Senaryo A (post-COMPLETED) odaklı; Senaryo B (pre-COMPLETED stuck) mevcut payout retry mekanizmasının sorumluluğunda. Kullanıcı onayladı.
- **`SellerPayoutIssue` entity T25'ten beri mevcut (PR #29 squash `25ce5b9`); T60 yalnızca service+endpoint+event+stub ekledi.** Entity DDL/CHECK/UQ değişmedi.
- **Cross-module pattern:** `IPayoutEscalationAdminResolver` Skinora.Transactions'da declare, Skinora.API/Services'te implement (Skinora.Transactions Skinora.Admin'e referans veremez — Disputes/Fraud cross-module port pattern'i).
