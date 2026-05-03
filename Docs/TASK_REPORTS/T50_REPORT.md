# T50 — Timeout freeze/resume

**Faz:** F3 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-05-03

---

## Yapılan İşler

T44 state machine + T47 scheduling pipeline + T48/T49 timeout side effect altyapısı sonrası, **bir transaction'ın geçici olarak timeout sayacının dondurulması ve daha sonra kaldığı yerden devam ettirilmesi** mekaniği bağlandı (02 §3.3, 05 §4.4–§4.5, 09 §13.3, 06 §3.5 + §8.1):

1. **Reason → state scope haritası** (`Skinora.Transactions/Application/Timeouts/TimeoutFreezeReasonScopes.cs`):
   - `MAINTENANCE` → 8 aktif state (CREATED, ACCEPTED, TRADE_OFFER_SENT_TO_SELLER, ITEM_ESCROWED, PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER, ITEM_DELIVERED, FLAGGED).
   - `STEAM_OUTAGE` → Steam-bağımlı 2 state (TRADE_OFFER_SENT_TO_SELLER, TRADE_OFFER_SENT_TO_BUYER).
   - `BLOCKCHAIN_DEGRADATION` → ITEM_ESCROWED (yalnız ödeme adımı).
   - `EMERGENCY_HOLD` → bulk API'lerde `ArgumentException`; tek-tx `FreezeAsync`/`ResumeAsync` üzerinden T59 orchestrator'ı tüketir.

2. **Freeze/resume servisi** (aynı klasör — `ITimeoutFreezeService` + `TimeoutFreezeService`):
   - `FreezeAsync(transaction, reason, ct)` — per-tx freeze. `TimeoutFrozenAt = now`, `TimeoutFreezeReason = reason`, `TimeoutRemainingSeconds = floor((activeDeadline − now), 0)` damgalar; ITEM_ESCROWED'a ait `PaymentTimeoutJobId` + `TimeoutWarningJobId` Hangfire job'larını inline `_scheduler.Delete()` ile iptal eder. **Idempotent:** zaten frozen tx'lerde damga korunur (orijinal stamp/reason/remainder dokunulmaz), yalnız job cancel pass yeniden yürütülür. Caller SaveChanges'ı sahiplenir.
   - `ResumeAsync(transaction, ct)` — per-tx resume. `remaining = TimeSpan.FromSeconds(TimeoutRemainingSeconds ?? 0)`, `newDeadline = now + remaining`; **otorite `TimeoutRemainingSeconds`** (06 §8.1 "Reschedule'ın kaynağı `TimeoutRemainingSeconds`'tır"). State→deadline matrix'inden (06 §3.5) doğru phase deadline alanını seçer ve `newDeadline`'a set eder; ITEM_ESCROWED için ek olarak `_scheduling.ReschedulePaymentTimeoutAsync(remaining, newDeadline)` çağırır (T47 mevcut API — warning job ratio + already-sent guard içinde). Sonra `TimeoutFrozenAt`/`TimeoutFreezeReason`/`TimeoutRemainingSeconds` üçlüsünü temizler (06 `CK_Transactions_FreezePassive`). **Idempotent:** frozen olmayan tx no-op.
   - `FreezeManyAsync(reason, ct)` — bulk freeze. `!IsDeleted && !IsOnHold && TimeoutFrozenAt == null && Status IN scope` filter ile aktif tx'leri yükler, her birine FreezeAsync uygular, **tek `SaveChangesAsync`** ile commit eder, `count` döndürür. EMERGENCY_HOLD reason için `ArgumentException` (scope helper'dan re-trigger).
   - `ResumeManyAsync(reason, ct)` — bulk resume. `TimeoutFrozenAt != null && TimeoutFreezeReason == reason` filter, her birine ResumeAsync, tek SaveChanges, count döndürür. EMERGENCY_HOLD için aynı throw.

3. **State→active deadline matrix entegrasyonu** (06 §3.5 normatif tablo, servis içinde private helper):
   - `GetActiveDeadline(t)`: CREATED→AcceptDeadline, ACCEPTED/TRADE_OFFER_SENT_TO_SELLER→TradeOfferToSellerDeadline, ITEM_ESCROWED→PaymentDeadline, PAYMENT_RECEIVED/TRADE_OFFER_SENT_TO_BUYER→TradeOfferToBuyerDeadline, ITEM_DELIVERED/FLAGGED→null (deadline yok).
   - `SetActiveDeadline(t, newDeadline)`: aynı eşleme tersi yönde, terminal/null durumlarda no-op.
   - `ComputeRemainingSeconds(deadline, now)`: deadline null ise 0; pozitif ise `Math.Floor((deadline − now).TotalSeconds)`; negatif ise 0 (clamp).

4. **DI kaydı** (`Skinora.API/Configuration/TransactionsModule.cs`):
   - `services.AddScoped<ITimeoutFreezeService, TimeoutFreezeService>();` — Scoped (per-request UoW içinde).

5. **CHECK constraint uyumu** (06 §3.5 + DB CK_Transactions_FreezeActive/FreezePassive/FreezeHold_Forward/FreezeHold_Reverse):
   - Freeze → `TimeoutFrozenAt`, `TimeoutFreezeReason`, `TimeoutRemainingSeconds` üçlüsü atomik set (`CK_Transactions_FreezeActive`).
   - Resume → üçlü atomik clear (`CK_Transactions_FreezePassive`).
   - Bulk filter `!IsOnHold` ile EMERGENCY_HOLD freeze'i clobber etmez (T59 path korunur).
   - EMERGENCY_HOLD bulk API guard'ı (`CK_Transactions_FreezeHold_Forward/Reverse` invariantına dokunmama).

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `backend/src/Modules/Skinora.Transactions/Application/Timeouts/TimeoutFreezeReasonScopes.cs` — public static helper (3 reason → status array, EMERGENCY_HOLD throws).
- `backend/src/Modules/Skinora.Transactions/Application/Timeouts/ITimeoutFreezeService.cs` — 4 method kontrat (FreezeAsync, ResumeAsync, FreezeManyAsync, ResumeManyAsync).
- `backend/src/Modules/Skinora.Transactions/Application/Timeouts/TimeoutFreezeService.cs` — impl, ~165 satır (özet helper'larla).
- `backend/tests/Skinora.Transactions.Tests/Unit/Timeouts/TimeoutFreezeReasonScopesTests.cs` — 5 unit test (3 reason mapping + EMERGENCY_HOLD throws + terminal-states-excluded).
- `backend/tests/Skinora.Transactions.Tests/Integration/Timeouts/TimeoutFreezeServiceTests.cs` — 16 integration test (FreezeAsync ITEM_ESCROWED + non-payment + already-frozen + zero-remainder, ResumeAsync ITEM_ESCROWED + non-payment + no-op, FreezeMany 4 reason senaryosu + skip held/frozen + EMERGENCY_HOLD throws + zero-match, ResumeMany matching reason + EMERGENCY_HOLD throws + Freeze→Resume cycle).

**Değişiklik:**
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` — `ITimeoutFreezeService` Scoped DI kaydı (1 satır).

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Platform bakımı: tüm aktif işlemlerin timeout'ları dondurulur | ✓ | `FreezeManyAsync(MAINTENANCE)` 8 aktif state'i kapsar (`TimeoutFreezeReasonScopes.For(MAINTENANCE)`), terminal state'ler hariç. `FreezeManyAsync_MAINTENANCE_Freezes_All_Eight_Active_States` testi 8 state'in hepsini freeze + COMPLETED hariç bırakır (assert.Equal(8, affected) + assert null TimeoutFrozenAt for terminal). |
| 2 | Steam kesintisi: Steam bağımlı adımlardaki timeout'lar dondurulur | ✓ | `FreezeManyAsync(STEAM_OUTAGE)` yalnız TRADE_OFFER_SENT_TO_SELLER + TRADE_OFFER_SENT_TO_BUYER targetler. `FreezeManyAsync_STEAM_OUTAGE_Targets_Only_Trade_Offer_States` testi 2 Steam state'i freeze + payment/created hariç bırakır. |
| 3 | Blockchain degradasyonu: ödeme adımındaki timeout'lar dondurulur | ✓ | `FreezeManyAsync(BLOCKCHAIN_DEGRADATION)` yalnız ITEM_ESCROWED targetler. `FreezeManyAsync_BLOCKCHAIN_DEGRADATION_Targets_Only_ITEM_ESCROWED` testi tek payment row'u freeze + Steam state'i hariç bırakır + Hangfire job iptalini doğrular (`p-old`/`w-old` DeletedJobIds). |
| 4 | Emergency hold: tek işlem dondurma | ✓ | `FreezeAsync(transaction, EMERGENCY_HOLD, ct)` per-tx kontrat (T59 forward-devirli orchestrator T44 state machine `ApplyEmergencyHold` ile birlikte tüketir). `FreezeManyAsync(EMERGENCY_HOLD)` ArgumentException atar — `FreezeManyAsync_EMERGENCY_HOLD_Throws` + `ResumeManyAsync_EMERGENCY_HOLD_Throws` testleri doğrular. Bulk filter `!IsOnHold` ile mevcut emergency hold'ları korur — `FreezeManyAsync_Skips_Already_Held_And_Already_Frozen` testi doğrular. |
| 5 | TimeoutFrozenAt, TimeoutFreezeReason, TimeoutRemainingSeconds set edilir | ✓ | FreezeAsync üç alanı atomik damgalar; `TimeoutRemainingSeconds` 06 §3.5 state→deadline matrix'inden hesaplanır (her aktif state'in active deadline field'ı vardır; ITEM_DELIVERED/FLAGGED için 0). DB CK_Transactions_FreezeActive constraint'i bu invariantı zorlar. `FreezeAsync_ITEM_ESCROWED_Stamps_Fields_And_Cancels_Hangfire_Jobs` (45 dk → 2700 sn) + `FreezeAsync_NonPayment_State_Captures_RemainingSeconds_From_Active_Deadline` (TRADE_OFFER_SENT_TO_SELLER, 120 dk → 7200 sn) + `FreezeAsync_ITEM_ESCROWED_PastDeadline_Captures_Zero_Seconds` (negatif clamp → 0) testleri doğrular. |
| 6 | Resume: frozen süre hesaplanır, deadline uzatılır, job yeniden schedule | ✓ | ResumeAsync `newDeadline = now + remaining`; SetActiveDeadline ilgili phase field'ını günceller. ITEM_ESCROWED için `ITimeoutSchedulingService.ReschedulePaymentTimeoutAsync` (T47) ile yeni Hangfire job (payment + warning) schedule eder. Sonra freeze trio temizlenir. `ResumeAsync_ITEM_ESCROWED_Reissues_Job_And_Clears_Freeze_Fields` (15 dk freeze sonrası 20 dk remaining → newDeadline=now+20dk + ITimeoutExecutor.ExecutePaymentTimeoutAsync schedule + IWarningDispatcher.DispatchWarningAsync schedule with ratio 0.5 = 10 dk delay) + `ResumeAsync_NonPayment_Bumps_Deadlines_By_Elapsed_Freeze` (7 dk freeze sonrası TradeOfferToSellerDeadline = freezeStart+67 dk) + `FreezeMany_Then_ResumeMany_Restores_Original_Remaining_Time` (full cycle: 50 dk remaining freeze → 17 dk wait → resume → newDeadline=resumeNow+50 dk) testleri doğrular. |

**Doğrulama kontrol listesi:**

- [x] **02 §3.3 tüm freeze senaryoları var mı?** ✓ — 4 senaryo `TimeoutFreezeReason` enum'unda + 3 platform-level reason `TimeoutFreezeReasonScopes` map'inde + emergency hold per-tx API'de (T59 wrap).
- [x] **05 §4.5 emergency hold mekanizması doğru mu?** ✓ — T44 state machine `ApplyEmergencyHold`/`ReleaseEmergencyHold` (IsOnHold + EmergencyHold* + freeze trio) zaten mevcut; T50 `FreezeAsync`/`ResumeAsync` per-tx API EMERGENCY_HOLD reason ile çağrılınca: idempotent stamp + Hangfire iptal + (resume'da) deadline restore + reschedule. Karşılıklı invariant (`IsOnHold ↔ TimeoutFreezeReason=EMERGENCY_HOLD`) DB CK_Transactions_FreezeHold_Forward/Reverse ile + state machine guard ile çift korumada. T59 emergency-hold endpoint orchestrator'ı T44+T50 kompozisyonu ile yapım sırası: `ApplyEmergencyHold` → `FreezeAsync(EMERGENCY_HOLD)` → audit + outbox event → SaveChanges. Release: `ResumeAsync` → `ReleaseEmergencyHold` → audit + outbox event → SaveChanges (T59 forward-devirli).

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (TimeoutFreezeReasonScopes) | ✓ 5/5 | `TimeoutFreezeReasonScopesTests` — MAINTENANCE 8 state, STEAM_OUTAGE 2 state, BLOCKCHAIN_DEGRADATION ITEM_ESCROWED, EMERGENCY_HOLD throws, terminal states excluded. |
| Integration (TimeoutFreezeService) | ✓ 16/16 | `TimeoutFreezeServiceTests` — FreezeAsync 4 (payment + non-payment + already-frozen idempotent + past-deadline zero), ResumeAsync 3 (ITEM_ESCROWED reschedule + non-payment bump + not-frozen no-op), FreezeMany 6 (3 reason senaryosu + skip held/frozen + EMERGENCY_HOLD throws + no-match-zero), ResumeMany 3 (matching reason + EMERGENCY_HOLD throws + full Freeze→Resume cycle). |
| Regresyon — Skinora.Transactions.Tests | ✓ 424/424 | T49: 403 → T50: 424 (+21 = 5 unit + 16 integration). Diğer test sınıfları regresyon temiz. |
| Regresyon — tüm test asembly'leri | ✓ 1264/1264 | Skinora.Users 16 + Fraud 17 + Payments 6 + Disputes 11 + Steam 21 + Shared 182 + Platform 136 + Transactions 424 + API 265 + Auth 93 + Notifications 73 + Admin 20 = **1264**. T49: 1243 → T50: 1264 (+21 net). |
| Build (Release) | ✓ 0W/0E | `dotnet build C:\projects\Escrow\backend\Skinora.sln -c Release` — `Build succeeded. 0 Warning(s) 0 Error(s)`. |
| Format verify | ✓ exit=0 | `dotnet format --verify-no-changes` clean. |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS (bağımsız validator chat, 2026-05-03) |
| Bulgu sayısı | 0 S-bulgu, 0 minor advisory |
| Verdict | ✓ PASS |
| Pre-flight: working tree | temiz |
| Pre-flight: main CI son 3 run | ✓ ✓ ✓ — `25259885322`, `25259885329` (chore #80), `25259571348` (T49 #79) |
| Pre-flight: repo memory drift (T50) | ✓ var (`.claude/memory/MEMORY.md` 9 satır match) |
| Task branch CI | ✓ run [`25274389994`](https://github.com/turkerurganci/Skinora/actions/runs/25274389994) — 9/9 (Lint/Build/Unit/Contract/Integration/Migration dry-run/Docker/CI Gate hepsi success; Guard skipped) |
| Build (Release) | ✓ 0W/0E |
| Format verify | ✓ exit=0 |
| Lokal test re-run (Transactions) | ✓ 424/424 (T50 odak 21/21: 5 unit + 16 integration) |
| Kabul kriterleri | 6/6 ✓ — 02 §3.3 + 05 §4.4–§4.5 + 06 §3.5 + 06 §8.1 + 09 §13.3 referans uyumu tam |
| Doğrulama kontrol listesi | 2/2 ✓ |
| Mini güvenlik | secret yok, auth/authorization etkisi yok (service-only), input validation defense-in-depth, yeni dış bağımlılık yok |
| Doc uyumu | TimeoutFreezeReason enum (4 değer) ↔ scope helper map ↔ DB CK constraint trio ↔ ITimeoutSchedulingService.ReschedulePaymentTimeoutAsync API tam eşleşme |
| Yapım raporu uyumu | Tam — 1264 toplam test sayımı CI'de doğrulandı; 21/21 lokal odak run, 424/424 lokal Transactions assembly run rapor ile birebir uyumlu |

## Altyapı Değişiklikleri

- **Migration:** Yok — `TimeoutFrozenAt`/`TimeoutFreezeReason`/`TimeoutRemainingSeconds`/`PaymentTimeoutJobId`/`TimeoutWarningJobId` kolonları T19/T44'ten beri var; CK_Transactions_FreezeActive/FreezePassive/FreezeHold_Forward/FreezeHold_Reverse constraint'leri T30 migration'ında kuruldu.
- **SystemSetting:** Yok — operasyonel altyapı, business parametre değil. (Maintenance window storage entity'si T63a P2 endpoint'i kapsamında devirli; T50 yalnız freeze/resume mekaniği.)
- **Config/env değişikliği:** Yok.
- **Docker değişikliği:** Yok.
- **Yeni dış bağımlılık:** Yok — `IBackgroundJobScheduler` (T13) + `ITimeoutSchedulingService` (T47) + `AppDbContext` + `TimeProvider` mevcut altyapı.

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok — yeni dosyaların hiçbiri secret içermiyor.
- **Auth/authorization etkisi:** Yok — yeni endpoint yok; T50 saf service katmanı. T59 emergency-hold + admin maintenance trigger endpoint'leri auth/permission kontrollerini kendi orchestrator'larında uygulayacak (`Permission:EMERGENCY_HOLD` / `Permission:MANAGE_SETTINGS` benzeri policy attribute'leri).
- **Input validation:** `TimeoutFreezeReasonScopes.For()` EMERGENCY_HOLD için `ArgumentException` (defense-in-depth — bulk API yanlış reason ile çağrılırsa fail-fast). `FreezeAsync`/`ResumeAsync` `ArgumentNullException.ThrowIfNull(transaction)`. Time clamping: `remaining < 0 → 0`, `Math.Floor` (06 §3.5 saniye granülarite).
- **DB constraint çift güvence:** CK_Transactions_FreezeActive (frozen ⇒ üçlü NOT NULL) + CK_Transactions_FreezePassive (not frozen ⇒ üçlü NULL) + CK_Transactions_FreezeHold_Forward/Reverse (EMERGENCY_HOLD ↔ IsOnHold) constraint'leri runtime mantık kırılırsa bile yarım/tutarsız freeze kaydını engeller.

## Commit & PR

- Branch: `task/T50-timeout-freeze-resume`
- Commit: `3143676` — `T50: Timeout freeze/resume (02 §3.3, 05 §4.4-§4.5, 09 §13.3)`
- PR: [#81](https://github.com/turkerurganci/Skinora/pull/81)
- CI: ✓ PASS — run [`25274239015`](https://github.com/turkerurganci/Skinora/actions/runs/25274239015) (HEAD `3143676`) 9/9 + Guard (direct push) skipped. Detect/Lint/Build/Unit/Contract/Integration/Migration dry-run/Docker build/CI Gate hepsi success.

## Known Limitations / Follow-up

- **Caller wiring forward-devir:**
  - `FreezeAsync`/`ResumeAsync` per-tx API tüketicisi **T59** (Emergency hold endpoint AD19b/AD19c). T59 orchestrator: `TransactionStateMachine.ApplyEmergencyHold(adminId, reason)` → `TimeoutFreezeService.FreezeAsync(tx, EMERGENCY_HOLD)` (idempotent stamp = no-op çünkü state machine zaten damgalamış; sadece Hangfire job cancel devreye girer) → audit + RT1 `EmergencyHoldApplied` outbox event → SaveChanges. Release: `TimeoutFreezeService.ResumeAsync(tx)` → `TransactionStateMachine.ReleaseEmergencyHold` → audit + RT1 `EmergencyHoldReleased` event → SaveChanges. Sırası kritik: ResumeAsync ReleaseEmergencyHold'dan önce çağrılmalı çünkü state machine ReleaseEmergencyHold freeze trio'yu temizliyor (T44 mevcut davranış); ResumeAsync ona ihtiyaç duyuyor (TimeoutFrozenAt'ten elapsed hesaplamak için). Aynı mantık FreezeAsync ↔ ApplyEmergencyHold sırasında da: ApplyEmergencyHold önce damgalar, sonra FreezeAsync idempotent çağrı yalnız Hangfire iptalini yürütür.
  - `FreezeManyAsync`/`ResumeManyAsync` bulk API tüketicileri:
    - **MAINTENANCE:** Admin maintenance start/stop endpoint'i — şimdilik 11 plan'de explicit task yok, T63a kapsamında veya sonradan açılacak admin görevinde devralınır. Önerilen orchestration: admin endpoint → `MaintenanceWindow` entity yarat (start) → `FreezeManyAsync(MAINTENANCE)` → audit `MAINTENANCE_MODE_CHANGED` + RT2 `MaintenanceStatusChanged` outbox event → SaveChanges. Bitiş: `ResumeManyAsync(MAINTENANCE)` → `MaintenanceWindow.EndedAt = now` + audit + event.
    - **STEAM_OUTAGE:** **T64–T68** Steam sidecar health check (otomatik tetikleme) + admin manuel trigger. 02 §3.3: "Steam bot health check başarısız olduğunda otomatik algılanır; admin manuel olarak da tetikleyebilir."
    - **BLOCKCHAIN_DEGRADATION:** **T73–T75** Blockchain sidecar health check (otomatik) + admin manuel. 02 §3.3: "blockchain health check başarısız olduğunda otomatik algılanır; admin manuel olarak da tetikleyebilir."
- **MaintenanceWindow entity / P2 endpoint:** **T63a** kapsamında (07 §10.2 GET /platform/maintenance). T50 yalnız freeze/resume engine'i — banner/status storage'ı ve public endpoint o tarafa devirli. T50'nin verdiği API contract (FreezeManyAsync/ResumeManyAsync return count + reason filter) T63a için yeterli; window entity tasarımı tamamen T63a sahipliğinde.
- **Audit log:** 05 §4.4 "Maintenance mode giriş/çıkış ve otomatik timeout uzatma işlemleri AuditLog'a kaydedilir" → caller (admin endpoint orchestrator) `IAuditLogger.LogAsync(MAINTENANCE_MODE_CHANGED, ...)` çağıracak. T50 service-only katman olduğu için audit yazmıyor (T59 tarzında — emergency hold audit T59'da). Yeni `AuditAction` value (`MAINTENANCE_MODE_CHANGED`) + `AuditLogCategoryMap` entry caller task'ında eklenecek.
- **Outbox events:** RT1 `EmergencyHoldApplied`/`EmergencyHoldReleased` (T59) + RT2 `MaintenanceStatusChanged` (admin maintenance task) caller orchestrator'larında publish edilir. T50 freeze/resume mekaniği event publish etmiyor — caller phase aware fan-out kararını veriyor.
- **T44 state machine ile çakışma:** T44 `ApplyEmergencyHold` ITEM_ESCROWED için `TimeoutRemainingSeconds`'i floor((PaymentDeadline−now).TotalSeconds, 0) ile damgalıyor; non-ITEM_ESCROWED'da NULL bırakıyor. Bu davranış DB CK_Transactions_FreezeActive constraint'i tarafından non-ITEM_ESCROWED state'de SaveChanges'te reddedilir. **Bu pre-existing bir uyumsuzluk** (T44 unit testleri DB save yapmadığı için yakalanmamış). T50 yapımı sırasında **T44'e dokunulmadı** (out-of-scope refactor riski). T59 orchestrator'ı T44 state machine + T50 freeze service'i kombine ederken: state machine'den sonra T50 FreezeAsync çağrısı idempotent olduğundan ITEM_ESCROWED'daki damgayı korur ama non-ITEM_ESCROWED için T50 doğru değerle (active deadline'dan hesaplanan) damgalar — doğru davranış. Yani T59 wiring doğal olarak T44'ün non-ITEM_ESCROWED gap'ini kapatır. Eğer ileride T44 standalone olarak SaveChanges yapan bir caller eklenirse (şu an yok) o zaman T44 refactor'u gerekir; bunu **T59 yapım chat'i için flagged-issue** olarak buraya yazıyorum.

## Notlar

- **Working tree pre-flight:** temiz (`git status --short` boş çıktı).
- **Main CI startup pre-flight:** son 5 main run ✓ — `25259885322`+`25259885329` (chore #80 post-merge fix), `25259571342`+`25259571348` (T49 #79), `25256755520` (T48 #78).
- **Dış varsayım:** yok — internal task. `Stateless 5.20.1` (T44), `Hangfire IBackgroundJobScheduler` (T13/T47), `EF Core 9 SaveChangesAsync`, `TimeProvider` (T44/T47), CK_Transactions_Freeze* DB constraint'leri (T30 migration) — hepsi mevcut altyapı.
- **Dependency check:** T47 ✓ (PR #77 squash `e00f97a`) — T50 plan dependency'si tek; tüm araç hazır.
- **Mimari karar — neden state machine'e ek API yok, ayrı service var?** State machine domain primitive'leri yönetir (status, milestone timestamps, IsOnHold flag, freeze trio). Hangfire job iptali + bulk DB query + Reschedule çağrısı **infrastructure** kapsamına girer (09 §9.2 "side effect üretmez" kuralı state machine için). `ITimeoutFreezeService` Application katmanında yer alıyor — T44 + T47 patternine uyumlu (T44 = state machine Domain, T47 = scheduling service Application). T59 orchestrator iki katmanı kompoze eder.
- **Mimari karar — neden Resume otoritesi `TimeoutRemainingSeconds`?** İki eşdeğer matematik var: (A) `newDeadline = oldDeadline + elapsed` (extend), (B) `newDeadline = now + remainingSeconds` (rewrite). `freezeStart`'tan beri `remainingSeconds = oldDeadline − freezeStart`, `elapsed = now − freezeStart` ⇒ ikisi aynı sonucu verir. **Spec açıkça (B)'yi seçiyor** (06 §8.1 "Reschedule'ın kaynağı `TimeoutRemainingSeconds`'tır. Deadline field'ları bu değerden türetilir, tersi değil.") — single source of truth ile race-condition'a daha dirençli (oldDeadline arada değiştirilirse yanlış extend yapmaz). T50 doc-aligned (B)'yi uyguluyor.
- **Mimari karar — neden FreezeAsync inline Hangfire delete, T47 CancelTimeoutJobsAsync değil?** T47 `CancelTimeoutJobsAsync(transactionId, ct)` kendi LoadAsync'ini yapar (DB roundtrip). Bulk freeze 200 tx'de 200 ekstra query oluşturur — admin-tetikli operasyon olsa bile gereksiz IO. Inline `_scheduler.Delete(jobId)` + `transaction.PaymentTimeoutJobId = null` 5 satır, mantık değişmedi (aynı `IBackgroundJobScheduler` API), DB roundtrip = 0. Trade-off: T47 method'unun tek tüketicisi şimdilik mevcut consumer (TimeoutSchedulingService.ReschedulePaymentTimeoutAsync internal); T50 ayrı path. Eğer T47 method'u ileride Transaction overload alırsa migrate edilir.
- **Mimari karar — neden ITEM_DELIVERED ve FLAGGED scope'ta MAINTENANCE için var ama active deadline yok?** 06 §3.5 matrix'i bu iki state için "tümü NULL" diyor. Ancak 02 §3.3 "tüm aktif işlemler" diyor. ITEM_DELIVERED auto-complete bekleme penceresinde (henüz spec'de keskin tanım yok), FLAGGED admin review bekleme penceresinde — her ikisi de "platform aktif olarak ilerletmesi gereken" durumlar. Maintenance sırasında dondurulmaları kullanıcı algısı için tutarlı (banner aktifken hiçbir tx ilerlememeli). TimeoutRemainingSeconds = 0 set ediliyor (matematik anlamlı: "kalan süre yok ama frozen kayıt"); resume'da SetActiveDeadline no-op (ilgili field yok) ve service çıkar. Bu pratik bir yorum — dokümandaki "tümü NULL"un freeze sırasında da geçerli olduğu anlamına gelir, ki freeze field'ları (TimeoutFrozenAt vb.) zaten matrix'in dışında.
- **Idempotency tasarımı — neden FreezeAsync zaten frozen ise stamp atlar ama job cancel yürütür?** Çift freeze çağrıları (örn. STEAM_OUTAGE freeze sırasında PLATFORM_MAINTENANCE de tetiklenirse) ilk reason'u korur — kullanıcı hangi nedenden donduğunu bilir, resume sırasında o reason ile filtrelenir. Job cancel idempotent (Hangfire `Delete` zaten yok ise no-op döner) ama ikinci çağrıda PaymentTimeoutJobId zaten null (ilk freeze'de temizlenmiş) olacağı için pratikte tek seferlik. ResumeAsync simetrik: TimeoutFrozenAt null ise tamamen no-op (already-resumed double-call güvenli).
- **Test coverage notu — buyer FK:** Test fixture `EmergencyHoldByAdminId = _seller.Id` kullanıyor (User FK constraint sağlamak için seed'lenmiş seller user). Test conceptual olarak admin için ayrı user seed etmek isterdi ama T50 audit/admin layer'a girmediği için seller user yeterli — FK constraint testi geçer, semantik bir senaryoyu test etmez (zaten bu test "hold'lu tx'ler bulk freeze tarafından clobber edilmiyor mu" senaryosunu test ediyor; admin kimliği önemli değil).
