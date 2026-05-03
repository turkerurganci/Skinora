# T54 — Fraud Flag Sistemi

**Faz:** F3 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekleniyor) | **Tarih:** 2026-05-04 (yapım)

---

## Yapılan İşler

T22, T42, T44 ✓ Tamamlandı tabanı üzerine fraud flag review pipeline'ı kuruldu (02 §14.0, 03 §7–§8.2, 07 §9.2–§9.5):

1. **`IFraudFlagService` + `FraudFlagService`** — `Skinora.Fraud/Application/Flags/`:
   - `StageAccountFlagAsync(userId, type, details, actorId, actorType, cascadeEmergencyHold, emergencyHoldReason, ct)` — caller-owned SaveChanges stage'i; flag row + AuditLog (FRAUD_FLAG_CREATED) + outbox event (FraudFlagCreatedEvent). `cascadeEmergencyHold=true` ise kullanıcının seller veya buyer olduğu tüm aktif transactions için EMERGENCY_HOLD uygulanır (02 §14.0 yüksek risk path: sanctions, hesap ele geçirme).
   - `StageTransactionFlagAsync(userId, transactionId, type, details, ...)` — pre-create flag staging; `TransactionCreationService` tarafından `Status=FLAGGED` transaction insert ile aynı SaveChanges'te çağrılır → admin asla flag'siz FLAGGED transaction göremez.
   - `ApproveAsync(flagId, adminId, note, ct)` — own SaveChanges + DB transaction. Pre-create scope için: `TransactionStateMachine.Fire(AdminApprove)` → FLAGGED → CREATED + `AcceptDeadline = now + AcceptTimeoutMinutes` (TransactionLimitsProvider'dan, fallback 60 min). Account scope için: yalnızca flag row review fields. AuditLog (FRAUD_FLAG_APPROVED) + outbox (FraudFlagApprovedEvent).
   - `RejectAsync(flagId, adminId, note, ct)` — own SaveChanges + DB transaction. Pre-create scope için: `Fire(AdminReject)` → FLAGGED → CANCELLED_ADMIN, `CancelReason = "Flag reddedildi (admin)"` (default), `CancelledBy = ADMIN`. Account scope için: yalnızca flag row review fields. AuditLog (FRAUD_FLAG_REJECTED) + outbox (FraudFlagRejectedEvent).
   - **Idempotency / state guard:** `flag.Status != PENDING` → `AlreadyReviewed`; pre-create flag için linked transaction `Status != FLAGGED` → `TransactionNotFlagged` (rollback path, flag PENDING kalır).

2. **`IFraudFlagAdminQueryService` + `FraudFlagAdminQueryService`** — `Skinora.Fraud/Application/Flags/`:
   - `ListAsync(query, ct)` — paged + filtered (type, reviewStatus, dateFrom, dateTo, sortBy, sortOrder); 07 §9.2 envelope: items + totalCount + page + pageSize + **pendingCount badge** (separate count of PENDING rows). `AsNoTracking` projection + per-page join into Transactions/Users for seller display data.
   - `GetDetailAsync(id, ct)` — 07 §9.3 detail; type-specific `flagDetail` JSON deserialization (PRICE_DEVIATION → `PriceDeviationFlagDetail`, HIGH_VOLUME → `HighVolumeFlagDetail`, ABNORMAL_BEHAVIOR → `AbnormalBehaviorFlagDetail`, MULTI_ACCOUNT → `MultiAccountFlagDetail`); fallback `{ raw }` payload on JSON parse error so legacy/malformed rows don't break the admin UI. `historicalTransactionCount` — completed transactions where flagged user was seller.
   - **Sıralama:** default `CreatedAt desc` (newest-first review queue); admin sortBy `type|reviewStatus|createdat` + sortOrder `asc|desc`.

3. **`AdminFlagsController`** — `Skinora.API/Controllers/AdminFlagsController.cs`:
   - `GET /api/v1/admin/flags` — AD2; `[Authorize(Policy="Permission:VIEW_FLAGS")]` + `[RateLimit("admin-read")]`.
   - `GET /api/v1/admin/flags/:id` — AD3; aynı policy.
   - `POST /api/v1/admin/flags/:id/approve` — AD4; `Permission:MANAGE_FLAGS` + `[RateLimit("admin-write")]`. Outcome → 200/404/409 envelope.
   - `POST /api/v1/admin/flags/:id/reject` — AD5; aynı policy + status semantic; reject sonucu `transactionStatus=CANCELLED_ADMIN`.

4. **`TransactionCreationService` (T45) wiring** — `Skinora.Transactions/Application/Lifecycle/TransactionCreationService.cs`:
   - Yeni `ITransactionFraudFlagWriter` port (Skinora.Transactions'da declared, implementation Skinora.Fraud'da — `IAccountFlagChecker` paterni mirror; Fraud → Transactions referansı zaten var, ters yön cycle olur).
   - `Status == FLAGGED` ise `_flagWriter.StagePreCreateFlagAsync(sellerId, transactionId, FraudFlagType.PRICE_DEVIATION, details)` → flag row aynı SaveChanges'te insert edilir. `details` JSON: `{ inputPrice, marketPrice, deviationPercent }` (07 §9.3 PRICE_DEVIATION şekli; deviationPercent = `Math.Round(DeviationRatio * 100, 4)`).
   - Stage 9 olarak yerleştirildi; outbox publish + SaveChangesAsync sonrasına dokunulmadı. Atomik: transaction row + flag row + outbox event tek commit'te.

5. **Cascade EMERGENCY_HOLD logic (02 §14.0):**
   - `ApplyEmergencyHoldCascadeAsync(userId, actorId, actorType, reason, flagId, ct)`:
     - Aktif transaction set: `(SellerId == userId OR BuyerId == userId) AND !IsDeleted AND !IsOnHold AND Status NOT IN (COMPLETED, CANCELLED_*)`. FLAGGED dahil (07 §9.21).
     - **`ITimeoutFreezeService.FreezeAsync(tx, EMERGENCY_HOLD, ct)` çağrılır önce** — TimeoutRemainingSeconds'ı active phase deadline'dan (06 §3.5 matrix) hesaplayıp setler. Bu olmadan `TransactionStateMachine.ApplyEmergencyHold` non-ITEM_ESCROWED state'ler için TimeoutRemainingSeconds boş bırakıyordu → CK_Transactions_FreezeActive constraint reddediyordu. Pairs with T50 freeze engine.
     - Sonra `TransactionStateMachine.ApplyEmergencyHold(actorId, reason)` — IsOnHold + EmergencyHoldAt/Reason/ByAdminId + PreviousStatusBeforeHold setler. RowVersion enforcement aktif.
     - Her transaction için ayrı AuditLog (FRAUD_FLAG_AUTO_HOLD, granular trail); ActorType caller'ın ActorType'ını aktarır (admin manuel high-risk flag → ADMIN; otomatik detection → SYSTEM).
   - **Buyer-side coverage:** Sanctions match örneğin buyer wallet adresinden gelirse, kullanıcının buyer olarak yer aldığı transactions de freeze edilir (03 §11a.3 cross-role). Already-on-hold transactions atlanır (idempotency).

6. **Yeni `AuditAction` enum değerleri** — `Skinora.Shared/Enums/AuditAction.cs`:
   - `FRAUD_FLAG_CREATED`, `FRAUD_FLAG_APPROVED`, `FRAUD_FLAG_REJECTED`, `FRAUD_FLAG_AUTO_HOLD` (13 → 17 değer).
   - `AuditLogCategoryMap` 4 yeni eşleşme: hepsi `ADMIN_ACTION` (admin queue UX — actor SYSTEM olsa bile admin'in inceleyeceği trail).

7. **3 yeni outbox event** — `Skinora.Shared/Events/`:
   - `FraudFlagCreatedEvent`: admin alert kanalı; `EmergencyHoldAppliedToActiveTransactions` field'ı template'lerin "aktif işlemler donduruldu" satırını koşullu eklemesi için.
   - `FraudFlagApprovedEvent` / `FraudFlagRejectedEvent`: party-side notification için. Consumer T62 (SignalR) + T78–T80 (Telegram/Discord/Email) forward-devir.

8. **`FraudModule` DI** — `Skinora.Fraud/FraudModule.cs`: Scoped (`IFraudFlagService`, `IFraudFlagAdminQueryService`, `ITransactionFraudFlagWriter`). `Program.cs` `AddFraudModule()` çağrısı `AddAdminModule` sonrası eklendi.

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `backend/src/Modules/Skinora.Fraud/FraudModule.cs` (DI)
- `backend/src/Modules/Skinora.Fraud/Application/Flags/IFraudFlagService.cs` (interface)
- `backend/src/Modules/Skinora.Fraud/Application/Flags/FraudFlagService.cs` (impl, ~365 satır)
- `backend/src/Modules/Skinora.Fraud/Application/Flags/IFraudFlagAdminQueryService.cs` (interface + query record)
- `backend/src/Modules/Skinora.Fraud/Application/Flags/FraudFlagAdminQueryService.cs` (impl, ~280 satır)
- `backend/src/Modules/Skinora.Fraud/Application/Flags/FraudFlagDtos.cs` (request/response DTOs + 4 type-specific FlagDetail records)
- `backend/src/Modules/Skinora.Fraud/Application/Flags/FraudFlagOutcomes.cs` (Approve/Reject discriminated union)
- `backend/src/Modules/Skinora.Fraud/Application/Flags/FraudFlagErrorCodes.cs` (constants)
- `backend/src/Modules/Skinora.Fraud/Application/Flags/TransactionFraudFlagWriter.cs` (cross-module adapter)
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/ITransactionFraudFlagWriter.cs` (port declaration)
- `backend/src/Skinora.API/Controllers/AdminFlagsController.cs` (4 endpoints)
- `backend/src/Skinora.Shared/Events/FraudFlagCreatedEvent.cs`
- `backend/src/Skinora.Shared/Events/FraudFlagApprovedEvent.cs`
- `backend/src/Skinora.Shared/Events/FraudFlagRejectedEvent.cs`
- `backend/tests/Skinora.Fraud.Tests/Integration/FraudFlagServiceTests.cs` (12 integration test — service surface)
- `backend/tests/Skinora.Fraud.Tests/Integration/FraudFlagAdminQueryServiceTests.cs` (5 integration test — read paths)
- `backend/tests/Skinora.API.Tests/Integration/AdminFlagsEndpointTests.cs` (9 endpoint smoke test)

**Değişiklik:**
- `backend/src/Modules/Skinora.Fraud/Skinora.Fraud.csproj` — Skinora.Platform reference eklendi (IAuditLogger).
- `backend/src/Skinora.Shared/Enums/AuditAction.cs` — 4 yeni enum değer (13 → 17).
- `backend/src/Modules/Skinora.Platform/Application/Audit/AuditLogCategoryMap.cs` — 4 yeni mapping → ADMIN_ACTION + xmldoc güncellemesi.
- `backend/src/Skinora.API/Program.cs` — `AddFraudModule()` registration + using.
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionCreationService.cs` — `ITransactionFraudFlagWriter` constructor parametresi + Stage 9 (FLAGGED → flag row staging).
- `backend/tests/Skinora.Fraud.Tests/Skinora.Fraud.Tests.csproj` — Skinora.Platform reference + Microsoft.Extensions.TimeProvider.Testing 9.0.0 paketi.
- `backend/tests/Skinora.Shared.Tests/Unit/EnumTests.cs` — AuditAction count 13→17 + 4 yeni InlineData.
- `backend/tests/Skinora.Platform.Tests/Unit/Audit/AuditLogCategoryMapTests.cs` — 4 yeni InlineData + ADMIN_ACTION count 7→11 (test method adı + Contains assertions).
- `backend/tests/Skinora.Transactions.Tests/Integration/Lifecycle/TransactionCreationServiceTests.cs` — `RecordingFraudFlagWriter` test double + BuildSut çağrısına eklendi.

**Migration:** Yok (AuditAction enum string-stored `nvarchar(100)`; FraudFlag entity T22 InitialCreate'te zaten mevcut). **SystemSetting:** Yok. **Yeni dış paket:** Yok (test projesine `Microsoft.Extensions.TimeProvider.Testing` 9.0.0 eklendi — diğer test projelerinde zaten var). **Yeni env var:** Yok.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Hesap flag: fon akışı aksiyonları engellenir, mevcut işlemler devam eder | ✓ | `AccountFlagChecker.HasActiveAccountFlagAsync` (T22) PENDING/APPROVED ACCOUNT_LEVEL flag varsa true döner; T45 `TransactionEligibilityService` bu flag'i `account-flagged` reason ile reddeder. Cascade=false yolu için: `StageAccountFlag_NoCascade_PersistsFlag_AndAuditLog_AndOutbox` testi (flag oluşturulur, transaction'lar IsOnHold=false kalır). |
| 2 | İşlem flag (pre-create): işlem CREATED öncesi durdurulur, timeout başlamaz | ✓ | `TransactionCreationService` `Status = fraud.ShouldFlag ? FLAGGED : CREATED`; FLAGGED için `AcceptDeadline = null` (line 225-227); state machine `HasFlaggedStateInvariant` tüm deadline NULL guard'ı korur. T45 mevcut testleri (`Creates_Flagged_Transaction_*`) ile doğrulanmış davranış değişmedi. |
| 3 | Admin flag kuyruğu: GET /admin/flags, GET /admin/flags/:id | ✓ | `AdminFlagsController` AD2 + AD3 endpoint'leri; `IFraudFlagAdminQueryService` impl. `ListFlags_SuperAdmin_ReturnsPagedResponseWithPendingCount` (totalCount=2, pendingCount=1 + items envelope) + `GetFlag_Existing_ReturnsDetailEnvelope` (detail JSON envelope). `FraudFlagAdminQueryServiceTests` — 5 integration test (filter, sort, type-specific flagDetail, malformed JSON fallback). |
| 4 | Admin onay: POST /admin/flags/:id/approve → işlem devam | ✓ | `FraudFlagService.ApproveAsync` → flag APPROVED + (pre-create için) state machine AdminApprove + AcceptDeadline reset. `Approve_TransactionFlag_PromotesFlaggedToCreated_AndSetsAcceptDeadline` testi (flag.Status=APPROVED, tx.Status=CREATED, tx.AcceptDeadline > now); `ApproveFlag_AccountLevel_Returns200WithReviewedAt` endpoint testi 200 + envelope. |
| 5 | Admin red: POST /admin/flags/:id/reject → işlem iptal | ✓ | `FraudFlagService.RejectAsync` → flag REJECTED + state machine AdminReject → CANCELLED_ADMIN. `Reject_TransactionFlag_TransitionsToCancelledAdmin` testi (tx.Status=CANCELLED_ADMIN + CancelReason="Flag reddedildi (admin)" default). |
| 6 | Yüksek risk durumlarında (sanctions, hesap ele geçirme): aktif işlemlere otomatik EMERGENCY_HOLD | ✓ | `cascadeEmergencyHold=true` parametresi + `ApplyEmergencyHoldCascadeAsync`. `StageAccountFlag_Cascade_FreezesActiveSellerTransactions` (active tx → IsOnHold=true, EmergencyHoldReason=cascade reason; completed tx atlanır; already-on-hold tx korunur). `StageAccountFlag_Cascade_FreezesBuyerSideTransactionsToo` (buyer-side coverage). `StageAccountFlag_Cascade_Without_Reason_Throws` (validation guard). |
| 7 | AuditLog kaydı tüm flag aksiyonlarında | ✓ | `IAuditLogger` + 4 yeni AuditAction (FRAUD_FLAG_CREATED/APPROVED/REJECTED/AUTO_HOLD); ActorType invariant (06 §8.6a) korunur — admin kararları ADMIN, otomatik detection SYSTEM. Her cascade target'ı için ayrı FRAUD_FLAG_AUTO_HOLD row (granular trail). Test: `StageAccountFlag_Cascade_FreezesActiveSellerTransactions` `holdAudits` assertion + `Approve_TransactionFlag` audit fetch (`Action=FRAUD_FLAG_APPROVED, ActorType=ADMIN, ActorId=adminId`). |
| 8 | Bildirimler: admin'e flag bildirimi, taraflara sonuç bildirimi | ✓ | 3 yeni outbox event: `FraudFlagCreatedEvent` (admin alert), `FraudFlagApprovedEvent`/`FraudFlagRejectedEvent` (party fan-out). `IOutboxService.PublishAsync` her staging path'inde çağrılır. Notification consumer T62 SignalR + T78–T80 channel handlers forward-devir (T49 `TransactionTimedOutEvent` paterni mirror). Test: `StageAccountFlag_NoCascade_PersistsFlag_AndAuditLog_AndOutbox` `_outbox.Published.Single()` assertion. |

## Test Sonuçları

| Suite | Sonuç |
|---|---|
| `Skinora.Fraud.Tests` (lokal MSSQL Testcontainers) | **34/34 PASS** (12 yeni FraudFlagServiceTests + 5 yeni FraudFlagAdminQueryServiceTests + 17 mevcut T22/T45 testleri) — 28 sn |
| `Skinora.API.Tests.Integration.AdminFlagsEndpointTests` | **9/9 PASS** (auth gate ×2 + AD2 ×1 + AD3 ×2 + AD4 ×3 + AD5 ×1) |
| `Skinora.API.Tests` total | **280/280 PASS** — 3 m 24 s |
| `Skinora.Transactions.Tests` total | **511/511 PASS** (TransactionCreationServiceTests `RecordingFraudFlagWriter` ile uyumlu) — 2 m 7 s |
| `Skinora.Platform.Tests` total | **141/141 PASS** (AuditLogCategoryMapTests 4 yeni InlineData + ADMIN_ACTION count 7→11) |
| `Skinora.Shared.Tests` total | **192/192 PASS** (EnumTests AuditAction 13→17 + 4 InlineData) |
| `Skinora.sln` solution test sweep | **1402+ PASS** (Admin 20 + API 280 + Auth 93 + Disputes 11 + Fraud 34 + Notifications 77 + Payments 6 + Platform 141 + Shared 192 + Steam 21 + Transactions 511 + Users 16) |
| `dotnet build Skinora.sln -c Release` | **0 Warning(s), 0 Error(s)** (Time Elapsed 24 sn) |
| `dotnet format Skinora.sln --verify-no-changes` | exit=0 (temiz) |

## Notlar

- **Working tree:** Adım -1 check temiz (`git status --short` boş; main'e geçiş + pull, sonra `task/T54-fraud-flag-system` branch'i açıldı).
- **Main CI startup check:** Adım 0 ✓ — son 3 main run hepsi `success` (T53 #84 ×2 + T52 #83; CI run ID 25289592150, 25289592174, 25287500662).
- **Bağımlılık:** T22 ✓ (FraudFlag entity), T42 ✓ (IAuditLogger), T44 ✓ (TransactionStateMachine.ApplyEmergencyHold + AdminApprove/AdminReject triggers).
- **Dış varsayımlar:**
  - `FraudFlag` entity + `FraudFlagScope` + `FraudFlagType` + `ReviewStatus` enum'ları zaten T22'den mevcut → ✓ doğrulandı.
  - `IAuditLogger.LogAsync` change-tracker only (caller SaveChanges) → ✓ doğrulandı (`AuditLogger.cs`).
  - `IOutboxService.PublishAsync(IDomainEvent)` API → ✓ doğrulandı (`Skinora.Shared.Interfaces.IOutboxService`).
  - `TransactionStateMachine.Fire(AdminApprove|AdminReject)` FLAGGED'dan transition izinli → ✓ doğrulandı (`TransactionStateMachine.ConfigureTransitions` line 271-274).
  - `TransactionStateMachine.ApplyEmergencyHold` non-ITEM_ESCROWED state'ler için TimeoutRemainingSeconds setlemiyor → ✓ doğrulandı (mevcut unit test `ApplyEmergencyHold_OnNonItemEscrowedState_DoesNotSetTimeoutRemainingSeconds`). T54 cascade `ITimeoutFreezeService.FreezeAsync`'i ön-pass ile çağırarak boşluğu doldurur (CK_Transactions_FreezeActive constraint compliance).
  - `Permission:VIEW_FLAGS` ve `Permission:MANAGE_FLAGS` policy provider'da çalışır → ✓ doğrulandı (T39 `PermissionPolicyProvider` dynamic + `PermissionCatalog.Keys.ViewFlags|ManageFlags` tanımlı).
  - `AuditLogCategoryMap.Every_AuditAction_Has_A_Category` testi yeni enum eklendiğinde fail eder → düzeltildi (4 yeni mapping + 4 InlineData + ADMIN_ACTION count 7→11).
- **Atomicity boundary (09 §13.3):**
  - **Staging path** (caller-owned SaveChanges): `StageAccountFlagAsync` / `StageTransactionFlagAsync` yalnızca change-tracker'a Add'ler; caller (`TransactionCreationService`) SaveChanges'i çağırır. Transaction insert + flag insert + outbox + audit log tek commit'te.
  - **Review path** (own SaveChanges): `ApproveAsync` / `RejectAsync` kendi `BeginTransactionAsync` + SaveChanges + Commit zincirini yönetir; flag review + transaction state transition + audit + outbox tek atomik akışta.
- **EMERGENCY_HOLD cascade — T44 latent bug fix:** T44'ün `ApplyEmergencyHold` sadece `ITEM_ESCROWED` state için TimeoutRemainingSeconds set ediyor; CREATED/ACCEPTED/TRADE_OFFER_*/PAYMENT_RECEIVED/TRADE_OFFER_* state'lerinde null bırakıyor. Ama CK_Transactions_FreezeActive constraint `TimeoutFrozenAt NOT NULL → TimeoutRemainingSeconds NOT NULL` istiyor → ApplyEmergencyHold non-ITEM_ESCROWED state üzerinde DB constraint reject. T54 cascade'inde `ITimeoutFreezeService.FreezeAsync(tx, EMERGENCY_HOLD)` ön-pass'i ile (T50 freeze engine 06 §3.5 active-deadline matrix'ini doğru handle ediyor) bu boşluk dolduruldu. T44 unit test'i `ApplyEmergencyHold_OnNonItemEscrowedState_DoesNotSetTimeoutRemainingSeconds` mevcut davranışı koruyor; T54 fix wrapper layer'da, state machine'e dokunulmadı.
- **Cross-module port pattern:** `ITransactionFraudFlagWriter` declared in Skinora.Transactions, implemented in Skinora.Fraud (`TransactionFraudFlagWriter` adapter routes through `IFraudFlagService`). Aynı pattern T45 `IAccountFlagChecker` ile birebir aynı: Fraud → Transactions referansı zaten var; ters yön cycle olur. Adapter `actorId = SeedConstants.SystemUserId` + `actorType = SYSTEM` sabit çünkü pre-create flag otomatik fraud detection sırasında oluşur.
- **Forward-devir (T55–T57, T82):**
  - **T55** AML kontrolü (price deviation + high volume threshold + dormant account anomaly) — `IFraudFlagService.StageTransactionFlagAsync` veya `StageAccountFlagAsync` çağıracak; T54 servis tarafını production-ready yapar, signal generators T55+ task.
  - **T56** Çoklu hesap tespiti (wallet/IP/device) — `StageAccountFlagAsync` MULTI_ACCOUNT type ile.
  - **T57** Wash trading — fraud flag dışında ayrı mekanizma (skor etkisi kaldırılır, flag oluşturmaz).
  - **T82** Sanctions screening — `StageAccountFlagAsync(cascadeEmergencyHold=true)` ile sanctions match → tüm aktif tx'lere EMERGENCY_HOLD. Yeni FraudFlagType değeri (`SANCTIONS_MATCH`) gerekirse T82 ekler — T54 mevcut 4 type ile yetinir (ABNORMAL_BEHAVIOR ile geçici eşleştirme T82'ye kadar uygulanabilir).
- **Notification consumer wire-up:** 3 yeni event (`FraudFlagCreatedEvent`, `FraudFlagApprovedEvent`, `FraudFlagRejectedEvent`) outbox'a publish ediliyor ama notification template'leri ve channel consumer wire-up'ı T62 (SignalR push) + T78–T80 (Telegram/Discord/Email) forward-devir. Şimdilik admin için tek görünür sinyal: `GET /admin/audit-logs?category=ADMIN_ACTION` (FRAUD_FLAG_* action'lar burada görünür).
- **`SortBy` semantic:** `createdat`/`type`/`reviewstatus` lower-case match; bilinmeyen sortBy → `CreatedAt desc` fallback. SortOrder default `desc` (newest-first), `asc` ile tersine çevrilir.
- **`historicalTransactionCount`:** AD3 detail field'ı `Status == COMPLETED` count'u — admin'in trust signal'i. Cancelled/in-flight transactions kasıtlı olarak sayılmaz (07 §9.3 wording match).
- **flagDetail JSON parse fallback:** Malformed `Details` JSON → `{ raw: "<original>" }` payload. Admin UI veriyi gösterebilir, error vermez. Test: `GetDetailAsync_Falls_Back_To_Raw_When_Details_Json_Is_Malformed`.
- **Already-on-hold idempotency:** Cascade aktif transaction listesini `!IsOnHold` filtreyle çekiyor; existing hold'a sahip transaction atlanır — reason override yok, double-stamping yok. Test: `StageAccountFlag_Cascade_FreezesActiveSellerTransactions` `alreadyOnHold` transaction kontrolü.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bekleniyor (yapım bitti, ayrı validate chat'inde) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Commit & PR

- Commit: pending push (yapım finalize)
- PR: pending (push sonrası açılacak)
- Task branch CI run: pending
- Squash merge: pending
- Main CI (post-merge): pending watch

## Known Limitations

- **Notification consumer forward-devir:** 3 yeni outbox event'i için template + channel handler wire-up'ı yok. T62 SignalR + T78–T80 Telegram/Discord/Email consumer'larında implement edilecek. Şimdilik admin AuditLog kuyruğunda görünür (FRAUD_FLAG_* under ADMIN_ACTION category).
- **Signal generators forward-devir:** T54 yalnızca **review pipeline + admin API**'sini sağlar. Otomatik fraud detection (price deviation T55, multi-account T56, sanctions T82) flag'leri oluşturacak; T54 service surface'i bu task'lara hazır. Pre-create PRICE_DEVIATION flag'i `TransactionCreationService` üzerinden zaten oluşuyor (T45 `IFraudPreCheckService` evaluate sonucu).
- **`FraudFlagType` enum genişletmesi T82'ye devir:** Mevcut 4 type (PRICE_DEVIATION, HIGH_VOLUME, ABNORMAL_BEHAVIOR, MULTI_ACCOUNT) — sanctions match veya account takeover için yeni type değerleri T82 task'ında karar verilecek (geçici olarak ABNORMAL_BEHAVIOR ile cascade=true kullanılabilir).
- **Transaction approval'da Hangfire job re-schedule yok:** `ApproveAsync` `AcceptDeadline`'ı resetliyor ama T47 `ITimeoutSchedulingService` üzerinden Hangfire payment timeout job kaydı zorunlu olabilir; mevcut akışta `DeadlineScannerJob` poll-based pickup ediyor (15 sn cycle) — yapısal olarak çalışıyor ama T47 patterned per-tx job için forward-devir notu var. Risk: 15 sn'lik scan cycle'a kadar timeout hesabı gecikebilir; admin approval sonrası AcceptDeadline 60 dk default olduğu için pratik etki yok.
