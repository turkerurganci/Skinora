# T59 — Emergency Hold

**Faz:** F3 | **Durum:** ⏳ Devam ediyor (yapım bitti) | **Tarih:** 2026-05-05

---

## Yapılan İşler

T59, admin tarafından tetiklenen üç işlem-yaşam-döngüsü endpoint'ini bağlar — 07 §9.20–§9.22, 02 §7, 03 §8.8. T44 state machine + T50 freeze service + T42 audit logger + T37 notification dispatcher kompozisyonu ile orchestrator katmanı kuruldu.

1. **Enum + event genişlemeleri (`Skinora.Shared`):**
   - `AuditAction` (T54: 17 → T59: 20) — `TRANSACTION_CANCELLED_ADMIN`, `EMERGENCY_HOLD_APPLIED`, `EMERGENCY_HOLD_RELEASED`. 06 §2.19 tablosu üç satır eklenerek senkronize edildi.
   - `ItemRefundTrigger` (4 → 5) — `AdminCancel` (T49 `TimeoutPayment/Delivery` + T51 `SellerCancel/BuyerCancel` ile aynı consumer'ı tüketir).
   - `NotificationType` (20 → 22) — `EMERGENCY_HOLD_APPLIED`, `EMERGENCY_HOLD_RELEASED`. EN + TR resx girdileri (`NotificationTemplates.resx`, `.tr.resx`) — Title/Body × 2 = 4 yeni anahtar; ES/ZH locale forward-deferred (T97 paterni).
   - `EmergencyHoldReleaseAction` (RESUME / CANCEL) — yeni enum, 07 §9.22 request `action` alanını tipler.
   - `EmergencyHoldAppliedEvent` + `EmergencyHoldReleasedEvent` — outbox/notification fan-out için iki yeni domain event'i. RT1 SignalR (07 §11.1) consumer T61 forward.

2. **`AdminTransactionDtos` + `AdminTransactionErrorCodes` — `Skinora.Transactions/Application/Admin/`:**
   - 3 request + 3 response + 3 outcome + 3 status enum (Cancel / ApplyHold / ReleaseHold). 07 §9.20–§9.22 envelope'larıyla 1:1.
   - 7 stable error code: `TRANSACTION_NOT_FOUND`, `VALIDATION_ERROR`, `INVALID_STATE_TRANSITION`, `CANNOT_CANCEL_AT_DELIVERY_STAGE` (AD19 422), `CANNOT_CANCEL_DELIVERED_HOLD` (AD19c CANCEL 422), `ALREADY_ON_HOLD`, `NOT_ON_HOLD`.

3. **`IAdminTransactionService` + `AdminTransactionService` — `Skinora.Transactions/Application/Admin/`:**
   - 3 method orchestrator (~480 satır). Her metot 5-7 stage pipeline + tek `SaveChangesAsync` ile atomik commit (09 §13.3).
   - **`CancelAsync` (AD19):** load → reason ≥10 char trim → state guard (ITEM_DELIVERED 422 / IsOnHold 409 / terminal 409) → `Fire(AdminCancel, ctx)` → `CancelTimeoutJobsAsync` → ItemRefund (ITEM_ESCROWED+) + PaymentRefund (PAYMENT_RECEIVED+) + `TransactionCancelledEvent`(ADMIN) → `IAuditLogger.LogAsync(TRANSACTION_CANCELLED_ADMIN)` → SaveChanges.
   - **`ApplyEmergencyHoldAsync` (AD19b):** load → reason ≥10 char → state guard (terminal 409 / AlreadyOnHold 409) → `state machine.ApplyEmergencyHold(adminId, reason)` (T44 — IsOnHold + EmergencyHold* + freeze trio + PreviousStatusBeforeHold tek atomik damgalama) → `IFreezeService.FreezeAsync(EMERGENCY_HOLD)` (idempotent — yalnız Hangfire job iptali) → `EmergencyHoldAppliedEvent` outbox → audit `EMERGENCY_HOLD_APPLIED` → SaveChanges. Response `status: "EMERGENCY_HOLD"` (overlay projection — gerçek `Status` alanı değişmez, 06 §3.5).
   - **`ReleaseEmergencyHoldAsync` (AD19c):**
     - **RESUME branch:** `IFreezeService.ResumeAsync` (newDeadline=now+TimeoutRemainingSeconds + ITEM_ESCROWED Hangfire reschedule + freeze trio temizle) → `state machine.ReleaseEmergencyHold` (IsOnHold=false) → `EmergencyHoldReleasedEvent`(RESUME) → audit `EMERGENCY_HOLD_RELEASED` → SaveChanges.
     - **CANCEL branch:** `state machine.ReleaseEmergencyHold` → `TimeoutRemainingSeconds = null` (CK_Transactions_FreezePassive zorunluluğu) → `Fire(AdminCancel, "Hold sonrası iptal: …")` → `CancelTimeoutJobsAsync` → ItemRefund + PaymentRefund (PreviousStatusBeforeHold'a göre) + `TransactionCancelledEvent`(ADMIN) → 2 audit row (`EMERGENCY_HOLD_RELEASED` + `TRANSACTION_CANCELLED_ADMIN`) → SaveChanges.
     - **ITEM_DELIVERED CANCEL guard:** `previousStatus == ITEM_DELIVERED` ise 422 `CANNOT_CANCEL_DELIVERED_HOLD` (07 §9.22 tablo + 03 §8.8 kısıt). RESUME yine izinli.
   - Order kritik (T50 raporu Known Limitations'tan): RESUME'da `ResumeAsync` `ReleaseEmergencyHold`'dan önce çağrılır (idempotent stamp guard — ResumeAsync `TimeoutFrozenAt` null değilken çalışıp freeze trio'yu temizler; Release sonra IsOnHold flag'ini düşürür).

4. **`AdminTransactionsController` — `Skinora.API/Controllers/`:**
   - 3 endpoint (`/admin/transactions/{id:guid}/cancel`, `/emergency-hold`, `/release-hold`). `[Authorize(Policy = Permission:CANCEL_TRANSACTIONS)]` (AD19) ve `[Authorize(Policy = Permission:EMERGENCY_HOLD)]` (AD19b/c) — iki yetki bağımsız (02 §7 not). `[RateLimit("admin-write")]` üç endpoint için.
   - Outcome → HTTP eşleme: 200 / 400 (validation) / 404 (not found) / 409 (terminal/ALREADY_ON_HOLD/NOT_ON_HOLD/INVALID_STATE_TRANSITION) / 422 (`CANNOT_CANCEL_AT_DELIVERY_STAGE`, `CANNOT_CANCEL_DELIVERED_HOLD`).
   - Admin user id `claim sub`'tan; ip `HttpContext.Connection.RemoteIpAddress` — audit'e geçer.

5. **`AuditLogCategoryMap` (`Skinora.Platform/Application/Audit`):** 3 yeni AuditAction → `ADMIN_ACTION` kategorisi (`/admin/audit-logs?category=ADMIN_ACTION` queue'sunda görünürler — 07 §9.19).

6. **Notification fan-out — `Skinora.Notifications/Application/EventHandlers/`:**
   - `EmergencyHoldAppliedNotificationConsumer` (yeni) — seller + buyer (varsa) `EMERGENCY_HOLD_APPLIED` request, `Reason` parametresiyle.
   - `EmergencyHoldReleasedNotificationConsumer` (yeni) — RESUME → seller + buyer; CANCEL → skip (kanal kapatılır, `TransactionCancelledNotificationConsumer` ADMIN dalı tüketir).
   - `TransactionCancelledNotificationConsumer` (T51, ADMIN dalı eklendi) — `CancelledByType.ADMIN` için her iki tarafa "İşlem yönetici tarafından iptal edildi" gönderir; pre-accept (BuyerId null) durumunda yalnız seller. Var olan TR resx şablonu (`TRANSACTION_CANCELLED_*`) reuse edilir.

7. **DI wiring — `Skinora.API/Configuration/TransactionsModule.cs`:** `IAdminTransactionService` Scoped 1 satır. `IAuditLogger` zaten `Skinora.Platform` modülünde kayıtlı, mevcut DI'dan resolve olur.

8. **Doküman uyumu:** 06 §2.19 AuditAction tablosu 3 satır eklenerek koddaki enum ile senkron tutuldu. Plan/spec değişikliği yok — 07 §9.20–§9.22 ve 02 §7 + 03 §8.8 referansları halihazırda eksiksizdi.

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `backend/src/Skinora.Shared/Enums/EmergencyHoldReleaseAction.cs`
- `backend/src/Skinora.Shared/Events/EmergencyHoldAppliedEvent.cs`
- `backend/src/Skinora.Shared/Events/EmergencyHoldReleasedEvent.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Admin/AdminTransactionDtos.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Admin/AdminTransactionErrorCodes.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Admin/IAdminTransactionService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Admin/AdminTransactionService.cs`
- `backend/src/Skinora.API/Controllers/AdminTransactionsController.cs`
- `backend/src/Modules/Skinora.Notifications/Application/EventHandlers/EmergencyHoldAppliedNotificationConsumer.cs`
- `backend/src/Modules/Skinora.Notifications/Application/EventHandlers/EmergencyHoldReleasedNotificationConsumer.cs`
- `backend/tests/Skinora.Transactions.Tests/Integration/Admin/AdminTransactionServiceTests.cs`
- `backend/tests/Skinora.Notifications.Tests/Unit/EmergencyHoldAppliedNotificationConsumerTests.cs`
- `backend/tests/Skinora.Notifications.Tests/Unit/EmergencyHoldReleasedNotificationConsumerTests.cs`

**Değişiklik:**
- `backend/src/Skinora.Shared/Enums/AuditAction.cs` — +3 değer.
- `backend/src/Skinora.Shared/Enums/ItemRefundTrigger.cs` — +AdminCancel.
- `backend/src/Skinora.Shared/Enums/NotificationType.cs` — +2 değer.
- `backend/src/Modules/Skinora.Platform/Application/Audit/AuditLogCategoryMap.cs` — +3 girdi (AdminAction).
- `backend/src/Modules/Skinora.Notifications/Application/EventHandlers/TransactionCancelledNotificationConsumer.cs` — ADMIN dalı + xmldoc.
- `backend/src/Modules/Skinora.Notifications/Resources/NotificationTemplates.resx` + `.tr.resx` — +4 anahtar.
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` — DI 1 satır.
- `backend/tests/Skinora.Shared.Tests/Unit/EnumTests.cs` — count assertion'ları (NotificationType 20→22, AuditAction 17→20, ItemRefundTrigger 4→5, namespace count 26→27) + EmergencyHoldReleaseAction 2 yeni test.
- `backend/tests/Skinora.Platform.Tests/Unit/Audit/AuditLogCategoryMapTests.cs` — +3 InlineData + ADMIN_ACTION 11→14 sayım.
- `backend/tests/Skinora.Notifications.Tests/Unit/TransactionCancelledNotificationConsumerTests.cs` — +2 ADMIN dalı testi.
- `Docs/06_DATA_MODEL.md` — §2.19 AuditAction +3 satır.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `POST /admin/transactions/:id/cancel` → admin doğrudan iptal | ✓ | `AdminTransactionsController.Cancel` + `AdminTransactionService.CancelAsync`. Integration test: `CancelAsync_From_Created_Cancels_Without_Refund_Events`, `CancelAsync_From_ItemEscrowed_Emits_ItemRefund_With_AdminCancel_Trigger`, `CancelAsync_From_PaymentReceived_Emits_Both_Refund_Events`. |
| 2 | `POST /admin/transactions/:id/emergency-hold` → hold uygulama | ✓ | `AdminTransactionsController.EmergencyHold` + `AdminTransactionService.ApplyEmergencyHoldAsync`. Integration: `ApplyEmergencyHoldAsync_Stamps_Hold_Fields_And_Cancels_Hangfire_Jobs` (IsOnHold=true + freeze trio + Hangfire delete + audit + outbox event). |
| 3 | `POST /admin/transactions/:id/release-hold` → hold kaldırma (RESUME veya CANCEL) | ✓ | `AdminTransactionsController.ReleaseHold` + `AdminTransactionService.ReleaseEmergencyHoldAsync`. Integration: RESUME (`ReleaseEmergencyHoldAsync_Resume_From_PaymentReceived_Restores_Status`), CANCEL (`ReleaseEmergencyHoldAsync_Cancel_From_PaymentReceived_Hold_Transitions_To_AdminCancel_With_Both_Refunds`). |
| 4 | `CANCEL_TRANSACTIONS` ve `EMERGENCY_HOLD` ayrı yetkiler | ✓ | Controller `[Authorize(Policy = "Permission:CANCEL_TRANSACTIONS")]` (AD19) vs `[Authorize(Policy = "Permission:EMERGENCY_HOLD")]` (AD19b/c) — iki ayrı policy. Permission catalog'da bu iki anahtar T39'dan beri ayrı kayıtlı (`PermissionCatalog.Keys.CancelTransactions`, `Keys.EmergencyHold`). |
| 5 | `ITEM_DELIVERED` hold'unda CANCEL yasak, yalnızca RESUME | ✓ | `AdminTransactionService.ReleaseEmergencyHoldAsync` Stage 4 — `previousStatus == ITEM_DELIVERED && action == CANCEL → 422 CANNOT_CANCEL_DELIVERED_HOLD`. Integration: `ReleaseEmergencyHoldAsync_Cancel_From_ITEM_DELIVERED_Hold_Returns_422_CannotCancelDeliveredHold` + `ReleaseEmergencyHoldAsync_Resume_From_ITEM_DELIVERED_Hold_Is_Allowed` (counterpart). |
| 6 | Timeout durur, akış bekler | ✓ | T44 `ApplyEmergencyHold` `IsOnHold=true` + `TimeoutFrozenAt`+`TimeoutFreezeReason=EMERGENCY_HOLD` damgalar; T50 `FreezeAsync` Hangfire job'ları siler. State machine `EnforceNotOnHold` her sonraki `Fire` çağrısını OnHoldErrorCode ile reddeder (05 §4.5). RESUME'da T50 `ResumeAsync` `newDeadline = now + TimeoutRemainingSeconds` ile freeze süresini ileri taşır (06 §8.1 otoritesi). Integration test'in `Resume` varyantı freeze trio'nun temizlendiğini ve Status'un `PreviousStatusBeforeHold`'a döndüğünü doğrular. |
| 7 | Tüm aksiyonlar AuditLog'a yazılır | ✓ | `IAuditLogger.LogAsync` her endpoint'te (CancelAsync → `TRANSACTION_CANCELLED_ADMIN`, ApplyEmergencyHold → `EMERGENCY_HOLD_APPLIED`, ReleaseEmergencyHold RESUME → `EMERGENCY_HOLD_RELEASED`, CANCEL → 2 row: `EMERGENCY_HOLD_RELEASED` + `TRANSACTION_CANCELLED_ADMIN`). `AuditLogCategoryMap` 3 yeni AuditAction → `ADMIN_ACTION` kategorisi. Integration: `CancelAsync_Writes_TRANSACTION_CANCELLED_ADMIN_AuditRow`, `ApplyEmergencyHoldAsync_Stamps_…` (assert audit row), `ReleaseEmergencyHoldAsync_Cancel_…` (assert iki audit row var). |
| 8 | Bildirimler: taraflara hold/release bildirimi | ✓ | `EmergencyHoldAppliedEvent` + `EmergencyHoldReleasedEvent` outbox publish, `EmergencyHoldApplied/ReleasedNotificationConsumer` her iki tarafa fan-out (buyer null ise yalnız seller). RESX şablonları EN + TR. CANCEL dalı `TransactionCancelledEvent`(ADMIN) ile aynı pipeline'a düşer — `TransactionCancelledNotificationConsumer` ADMIN dalı her iki tarafa "İşlem yönetici tarafından iptal edildi" gönderir. Unit testler: `EmergencyHoldAppliedNotificationConsumerTests` (4) + `EmergencyHoldReleasedNotificationConsumerTests` (4) + `TransactionCancelledNotificationConsumerTests.Handle_Admin_Cancel_Notifies_Both_Parties`. |

**Doğrulama kontrol listesi:**

- [x] **02 §7 emergency hold kuralları eksiksiz mi?** ✓ — admin direct cancel + emergency hold + ITEM_DELIVERED kısıtı + ayrı yetki (`EMERGENCY_HOLD`) + audit kaydı zorunluluğu hepsi implement edildi. 02 §7 tablo satırları (Admin emergency hold, Admin emergency hold — ITEM_DELIVERED kısıtı) AdminTransactionService guard'larıyla 1:1 eşleşiyor.
- [x] **07 §9.20–§9.22 sözleşmeleri doğru mu?** ✓ — request/response gövdeleri DTO + outcome record'larıyla birebir; tüm hata kodları (`CANNOT_CANCEL_AT_DELIVERY_STAGE`, `INVALID_STATE_TRANSITION`, `ALREADY_ON_HOLD`, `NOT_ON_HOLD`, `CANNOT_CANCEL_DELIVERED_HOLD`, `VALIDATION_ERROR`, `TRANSACTION_NOT_FOUND`) ve HTTP kodları (200/400/404/409/422) AdminTransactionsController'da yansıtıldı. AD19b response `status: "EMERGENCY_HOLD"` overlay projeksiyon olarak literal string ile döner (06 §3.5 + 07 §9.20 not).

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (Skinora.Shared.Tests) | ✓ 180/180 | Enum count + Theory casses (NotificationType 22, AuditAction 20, ItemRefundTrigger 5, EmergencyHoldReleaseAction 2, namespace 27). |
| Unit (Skinora.Notifications.Tests) | ✓ 49/49 | TransactionCancelledNotificationConsumer ADMIN dalı (2 yeni) + EmergencyHoldAppliedNotificationConsumer (3) + EmergencyHoldReleasedNotificationConsumer (4) + mevcut consumer regresyonu temiz. |
| Unit (Skinora.Platform.Tests) | ✓ 85/85 | AuditLogCategoryMap +3 InlineData + ADMIN_ACTION 11→14 sayım assertion'ı pass. |
| Unit (Skinora.Transactions.Tests) | ✓ 333/333 | Mevcut StateMachine + Lifecycle + Timeouts + GasFee + Reputation unit'leri regresyon temiz. |
| Unit (Skinora.Users.Tests / Auth.Tests / Fraud.Tests) | ✓ 16/16 + 57/57 + 14/14 | Regresyon temiz. |
| Integration (CI shared services:mssql) | ✓ PASS | İlk CI run `25399832698` 4× `CK_Transactions_FreezeActive` fail → S2 same-PR fix (`bcab472` — FreezeAsync pre-pass T54 paterni); re-CI run [`25400359096`](https://github.com/turkerurganci/Skinora/actions/runs/25400359096) (HEAD `92dc105`) 10/10 success (Lint/Build/Unit/Contract/Integration/Migration dry-run/Docker/Gate). Lokal Docker yok (Windows env), CI Linux runner shared services:mssql üzerinde 17 yeni `AdminTransactionServiceTests` + tüm regresyon ✓. |
| Build (Release) | ✓ 0W/0E | `dotnet build Skinora.sln -c Release` → `Build succeeded. 0 Warning(s) 0 Error(s)`. |
| Format verify | ✓ exit=0 | `dotnet format Skinora.sln --verify-no-changes` clean. |

**Test sayımı (lokal unit toplam):** Shared 180 + Auth 57 + Notifications 49 + Users 16 + Fraud 14 + Platform 85 + Transactions(unit) 333 = **734 unit pass**.

## Altyapı Değişiklikleri

- **Migration:** Yok — `IsOnHold`, `EmergencyHold*`, `PreviousStatusBeforeHold`, `TimeoutFreezeReason`, `TimeoutFrozenAt`, `TimeoutRemainingSeconds` kolonları T19/T44'ten beri var; CK_Transactions_Freeze* + CK_Transactions_Hold constraint'leri T30 migration'ında kuruldu.
- **SystemSetting:** Yok — admin yetkileri `PermissionCatalog`'da statik (T39); business parametre yok.
- **Config/env:** Yok.
- **Docker:** Yok.
- **Yeni dış bağımlılık:** Yok — IAuditLogger (T42), IOutboxService (F0), TimeoutFreezeService (T50), TransactionStateMachine (T44), TimeoutSchedulingService (T47) hepsi mevcut.

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok — yeni dosyaların hiçbiri secret içermiyor.
- **Auth/authorization:** 3 endpoint zorunlu authentication + ayrı permission policy'leri (`CANCEL_TRANSACTIONS` / `EMERGENCY_HOLD`). Anonim erişim kapalı (`Authorize(Policy=...)`). Admin id JWT `sub` claim'inden, IP `HttpContext.Connection.RemoteIpAddress`'ten — audit logger `ActorType.ADMIN` invariantını zorlar (boş/SYSTEM Guid yasak).
- **Input validation:** Reason ≥10 char trimmed (Cancel + ApplyHold), note ≥1 char trimmed (ReleaseHold). Trim öncesi/sonrası persist edilen değer aynı (T51 paterni). Action enum `JsonStringEnumConverter` (Program.cs T45) ile string olarak gelir → geçersiz değer 400 döner. RowVersion check state machine içinde aktif (`EnforceRowVersion`). State guard'lar 4 katmanlı: orchestrator (terminal/ITEM_DELIVERED/IsOnHold) → state machine `EnforceNotOnHold` + `EnforceRowVersion` + `CanFire` + DB CK_Transactions_Freeze*/Hold.
- **Yeni dış bağımlılık:** Yok.

## Commit & PR

- Branch: `task/T59-emergency-hold`
- Commits: `10c58a0` (yapım) + `bcab472` (S2 same-PR fix — CK_Transactions_FreezeActive root cause) + `92dc105` (BYPASS_LOG follow-up).
- PR: [#92](https://github.com/turkerurganci/Skinora/pull/92)
- CI: ✓ PASS — son tamamlanmış run [`25400359096`](https://github.com/turkerurganci/Skinora/actions/runs/25400359096) (HEAD `92dc105`) 10/10 success (Detect/Lint/Build/Unit/Contract/Integration/Migration dry-run/Docker/CI Gate hepsi ✓; Guard skipped). Önceki ardışık runlar: `25400333793` cancelled (bcab472 üzerine 92dc105 superseded), `25399832698` failure (root cause, S2 fix `bcab472` ile çözüldü).
- BYPASS_LOG entry: 1× `[ci-failure]` Layer 2 — `bcab472` push'u son CI failure'i fix'lediği için Layer 2 hook bypass'i zorunluydu (T28 + T29 + T44 + T58 paterni; same-PR S2 fix). `Docs/BYPASS_LOG.md` hook tarafından otomatik güncellendi, `92dc105` follow-up commit'i ile commit'lendi.

## Same-PR S2 Fix (post-initial-CI)

İlk CI run [`25399832698`](https://github.com/turkerurganci/Skinora/actions/runs/25399832698) — `4. Integration test` job'unda 4 `AdminTransactionServiceTests` test fail (`CK_Transactions_FreezeActive` constraint violation). Lokal Docker engine olmadığı için integration testleri yapım sırasında lokal koşturulamamıştı; CI ilk runda root cause'u yakaladı.

**Root cause:** `T44 ApplyEmergencyHold` non-ITEM_ESCROWED state için `TimeoutRemainingSeconds`'i NULL bırakıyor (T50 raporu Known Limitations bölümünde T59 yapım chat'ine flag edilmiş bilinen davranış). Yapımda orchestrator state machine'i FreezeAsync'ten ÖNCE çağırıyordu — state machine `TimeoutFrozenAt`'i set'liyor, FreezeAsync idempotent guard'ı (`if (TimeoutFrozenAt is null)`) erken return ediyor → `TimeoutRemainingSeconds` NULL kalıyor → `CK_Transactions_FreezeActive` (`(TimeoutFrozenAt IS NULL) OR (TimeoutFreezeReason IS NOT NULL AND TimeoutRemainingSeconds IS NOT NULL)`) ihlali → `SaveChangesAsync` reject. ITEM_ESCROWED test geçti çünkü state machine bu state için `TimeoutRemainingSeconds`'i `PaymentDeadline`'dan hesaplıyor.

**Fix:** Sırayı tersine çevirdim — T54 cascade-hold paterni: önce `_freeze.FreezeAsync` (06 §3.5 active-deadline matrix → her aktif state için `TimeoutRemainingSeconds` doğru hesaplanır), sonra `state machine.ApplyEmergencyHold` (`IsOnHold` + `EmergencyHold*` set'ler; freeze trio'yu aynı değerlerle yeniden yazar). State machine'e dokunulmadı — wrapper layer fix.

**Commit:** `bcab472` (`AdminTransactionService.cs` sadece — Stage 4/5 reorder + xmldoc).

**Re-CI:** Run [`25400359096`](https://github.com/turkerurganci/Skinora/actions/runs/25400359096) (HEAD `92dc105`) 10/10 success — tüm 17 `AdminTransactionServiceTests` pass + tam regresyon temiz.

## Known Limitations / Follow-up

- **SignalR RT1 events forward-deferred T61:** `EmergencyHoldAppliedEvent` + `EmergencyHoldReleasedEvent` outbox satırları SignalR `EmergencyHoldApplied`/`EmergencyHoldReleased` push consumer'ı T61 hub task'ında eklendiğinde aynı outbox üzerinden tüketilir. T59 yalnız notification consumer'larını wire'lar.
- **CANCEL release dual-event pattern:** AD19c CANCEL dalı hem `EmergencyHoldReleasedEvent`(CANCEL) (consumer skip) hem `TransactionCancelledEvent`(ADMIN) yayar. Notification fan-out'u tek noktada (T51 consumer ADMIN dalı) tutmak için `EmergencyHoldReleasedNotificationConsumer` CANCEL action'ı için early-return; SignalR T61'de iki ayrı RT1 event olarak kullanılabilir.
- **Sanctions auto-trigger forward-deferred T82:** 07 §9.21 not — sanctions screening eşleşmesi tespit edildiğinde sistemin AD19b endpoint'ini otomatik çağırması T82 (sanctions screening) sahipliğindedir. T59 endpoint kontratı T82'nin tüketmesi için hazır (admin actor id yerine SYSTEM user id geçilirse T59 audit row `ActorType=ADMIN` invariantı esnetilmeli — T82 yapım sırasında karar verilecek; alternatif: `IAdminTransactionService.ApplyEmergencyHoldAsync` içinde `ActorType` parametresi opsiyonel).
- **i18n locale coverage:** Yeni `EMERGENCY_HOLD_APPLIED_*` + `EMERGENCY_HOLD_RELEASED_*` resx şablonları sadece EN + TR. ES + ZH locale T97 i18n full coverage'da T49/T51/T58 paralelinde eklenir (T59 lokal `tr.resx` zaten partial — pattern korundu).
- **Permission ADMIN actor capture:** `AdminTransactionsController` admin user id'yi JWT claim'den okur. `PermissionAuthorizationHandler` super-admin bypass'ı (T40) bu policy'de de geçerli — super-admin bypass'la giren admin için `actorId` yine JWT sub claim'inden gelir.
- **Integration testler CI'de doğrulanacak:** Lokal Windows Docker engine çalışmadığı için `AdminTransactionServiceTests` (17 test) lokal `IntegrationTestBase` üzerinden koşturulamadı. CI Linux runner'da services:mssql üzerinden çalışacak (T11.3 paterni). Build clean + format clean lokal doğrulandı.

## Notlar

- **Working tree pre-flight:** `M .claude/settings.local.json` (otomatik permission allow eklemesi — task öncesi inceleme sırasında oluştu); selektif staging ile commit dışında tutuldu.
- **Main CI startup pre-flight:** son 3 main run ✓ — `25393246103` + `25393246079` (T58 #90) + `25388029904` (chore T57 status #89). PR #91 (chore T50 row drift) merge sonrası `25398165947`+`25398165830` ✓.
- **T50 status row drift fix (chore PR #91, squash `fc9682c`):** T59 yapımı öncesi T50 row stale (`⏳ Devam ediyor / ⏳`) tespit edildi — PR #81 squash sonrası `cb71f74` "validator ✓ PASS" commit'i sadece "Son güncelleme" header'ını yenilemiş, tabloyu atlamıştı. Ayrı chore PR ile düzeltildi (bundled-PR yasağı). T50 functionally complete (PR #81 + validator) — sadece tablo görünümü drift'i.
- **Dış varsayım kontrolü:**
  - `IAuditLogger` API: `LogAsync(AuditLogEntry, CancellationToken)` — `Skinora.Platform.Application.Audit.AuditLogger` (T42) — caller SaveChanges sahiplenir. ✓ (kod okuması, T53 RefundBlockedAlertService kullanımı).
  - `ITimeoutFreezeService.FreezeAsync` per-tx idempotent stamp + Hangfire job iptali — T50 raporu line 18 + servis kodu satır 33. ✓
  - `ITimeoutFreezeService.ResumeAsync` per-tx newDeadline + (ITEM_ESCROWED için) reschedule + freeze trio temizler — T50 servis kodu satır 73. ✓
  - `TransactionStateMachine.ApplyEmergencyHold` IsOnHold + EmergencyHold* + freeze trio + PreviousStatusBeforeHold tek atomik damgalama — T44 servis kodu satır 72. ✓
  - `TransactionStateMachine.ReleaseEmergencyHold` IsOnHold=false + TimeoutFreezeReason/TimeoutFrozenAt clear; **TimeoutRemainingSeconds preserved for T47 reschedule** — T44 servis kodu satır 100 + comment. CANCEL release branch için orchestrator manuel olarak `TimeoutRemainingSeconds = null` set eder (CK_Transactions_FreezePassive zorunluluğu). ✓
  - `Permission:CANCEL_TRANSACTIONS` + `Permission:EMERGENCY_HOLD` policy registration — T40 RBAC + `PermissionPolicyProvider` (dinamik) — `PermissionCatalog.Keys.CancelTransactions` / `Keys.EmergencyHold` mevcut. ✓
- **Mimari kararlar:**
  1. **Tek service tek tip kompozisyon:** `AdminTransactionService` 3 metot tek sınıfta, T58 `DisputeService` ile aynı pattern (3 method orchestrator). Ayrı sınıflar (CancelService / HoldService / ReleaseService) over-engineering — DI surface büyütüyor, paylaşılan helper'lar (ItemWasOnPlatform, IsTerminalState) duplikasyon.
  2. **CancelledByType.ADMIN reuse:** Admin direct cancel dedicated event (`AdminCancelledEvent` vb.) yerine T51 mevcut `TransactionCancelledEvent` + `CancelledByType.ADMIN` enum genişlemesi. Avantaj: tek consumer infra (TransactionCancelledNotificationConsumer ADMIN dalı eklendi), tek template (`TRANSACTION_CANCELLED_*` resx). Trade-off: T51 raporu "Admin-initiated cancellation (T59)... emit their own dedicated events because the counter-party reason text differs significantly" ifadesi vardı — gerçek implementasyonda reason field zaten event'in içinde, ayrı template gerekmedi; consumer ADMIN dalında prefix metin "İşlem yönetici tarafından iptal edildi" eklendi, body resx aynı kalıyor.
  3. **CANCEL release branch dual-event pattern:** `EmergencyHoldReleasedEvent`(CANCEL) + `TransactionCancelledEvent`(ADMIN) iki event yayılıyor. Released event SignalR T61 forward-aware, ama notification fan-out yalnız Cancel event üzerinden gidiyor (Released consumer CANCEL action'ı skip). Alternatifler: (a) Tek event (Cancel-Only) — SignalR RT1 EmergencyHoldReleased eksik kalır, (b) Notification consumer her iki event'ten fan-out — duplicate notification. Seçilen path future-proof + duplicate-free.
  4. **Idempotent ApplyEmergencyHold + FreezeAsync:** State machine ApplyEmergencyHold trio'yu zaten damgalar; T50 FreezeAsync idempotent guard (`if (TimeoutFrozenAt is null)` kondisyonu) sayesinde ikinci stamp yapmaz, sadece Hangfire job iptalini yürütür. Bu order kritik: state machine önce, freeze service sonra — yarım state'den kaçınma için.
  5. **CK_Transactions_FreezePassive + CANCEL release order:** ReleaseEmergencyHold sadece 2 freeze field temizler (TimeoutFrozenAt, TimeoutFreezeReason) — T47 reschedule consumer için TimeoutRemainingSeconds saklanır. CANCEL release path'inde T47 path tetiklenmiyor (tx CANCELLED_ADMIN'e gidiyor) → orchestrator `TimeoutRemainingSeconds = null` manuel clear eder. Aksi halde DB save SaveChangesAsync sırasında CK_Transactions_FreezePassive ihlali atar.
  6. **ItemWasOnPlatform / PaymentWasReceived state matrix:** `ITEM_ESCROWED, PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER` üçlüsü item escrow'da; `PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER` ikilisi payment received. ITEM_DELIVERED hariç (cancel reachable değil). Bu helper'lar AdminTransactionService static private — T51 `itemWasOnPlatform` lokal değişken paterni genişletilmiş hali.
- **T50 reference test pattern reuse:** Integration test fixture `CapturingJobScheduler` + `CapturingOutboxService` + `TimeoutTestSupport.cs` mevcut altyapıdan kullanıldı (Skinora.Transactions.Tests project'inde zaten internal). `AdminTransactionServiceTests` 17 test (10 Cancel + 4 Hold + 6 Release) — full lifecycle kapsama. CI'de `AdminTransactionServiceTests` ve `EmergencyHoldApplied/ReleasedNotificationConsumerTests` doğrulanacak.
