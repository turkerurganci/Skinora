# T112 — E2E: Acil Dondurma (Emergency Hold) Senaryoları

**Faz:** F6 | **Durum:** ⏳ Devam ediyor (doğrulama bekliyor) | **Tarih:** 2026-06-23

---

## Yapılan İşler

03 §8.8 (Admin Emergency Hold) için uçtan uca (E2E) kapsam — T107–T111 ile aynı seam (Playwright + `docker-compose.e2e.yml` + `sidecar-fake`). Apply-hold / release-hold (RESUME / CANCEL) ve ITEM_DELIVERED cancel guard backend-side **tam wire-li** (`AdminTransactionsController` AD19b/AD19c → `AdminTransactionService.ApplyEmergencyHoldAsync` / `ReleaseEmergencyHoldAsync`; `TimeoutFreezeService` freeze/resume; `SellerPayoutQueueJob` + `PayoutCompletedConsumer` `!IsOnHold` gate; `DeadlineScannerJob` `!IsOnHold && TimeoutFrozenAt IS NULL` filtresi) olduğundan bu task **yalnız test kapsamı ekler — sıfır production kaynak değişikliği** (T108/T109/T111 gibi).

- **`e2e/tests/emergency-hold.spec.ts` (yeni):** 3 test.
  1. **Apply hold → timeout durur → resume → devam.** CREATED işleme hold uygulanır (`status=EMERGENCY_HOLD` projeksiyonu, `previousStatus=CREATED`, IsOnHold + freeze trio DB'de); accept deadline geçmişe çekilir → DeadlineScannerJob held satırı atlar (18 sn / ~3–4 sweep boyunca `EMERGENCY_HOLD`, altta yatan `Status` hâlâ `CREATED` — CANCELLED_TIMEOUT'a düşmedi); **RESUME** → status `CREATED`'a döner, IsOnHold/freeze temizlenir; alıcı kabul eder → akış `ITEM_ESCROWED`'a ilerler (escrow +1).
  2. **Apply hold (ITEM_ESCROWED) → cancel → CANCELLED_ADMIN.** ITEM_ESCROWED'a sürülür (escrow=1), hold uygulanır, **CANCEL** → `CANCELLED_ADMIN` + `itemReturned=true` + `paymentRefunded=false` (AD19 refund fan-out); RETURN_TO_SELLER teklifi kabul edilir (escrow=0); her iki taraf `TRANSACTION_CANCELLED` bildirimi alır.
  3. **Apply hold at ITEM_DELIVERED → sadece resume.** Tam happy-path ile ITEM_DELIVERED'a sürülür, hold uygulanır (`previousStatus=ITEM_DELIVERED`); payout pipeline `!IsOnHold` ile kapılı olduğundan 12 sn boyunca ITEM_DELIVERED'da park eder; **CANCEL** denenir → `422 CANNOT_CANCEL_DELIVERED_HOLD` (hold ayakta kalır); **RESUME** → status `ITEM_DELIVERED`'a döner; payout pipeline (queue → dispatch → confirm → complete, her biri dakikalık) işlemi `COMPLETED`'a sürer.
- Her testte hold sonrası `EMERGENCY_HOLD_APPLIED` ve resume sonrası `EMERGENCY_HOLD_RELEASED` bildirim fan-out'u (seller + kayıtlı buyer) asserte edilir.
- **`e2e/src/api.ts`:** `applyEmergencyHold` (AD19b), `releaseEmergencyHold` (AD19c, action RESUME/CANCEL), `assertStatusStable` (held projeksiyonun bir pencere boyunca değişmediğini doğrular — frozen kanıtının API ayağı).
- **`e2e/src/db.ts`:** `getTransactionHoldState` + `HoldStateRow` (IsOnHold, TimeoutFreezeReason, TimeoutFrozenAt, TimeoutRemainingSeconds, PreviousStatusBeforeHold, Status — DB'de string enum). Seed/cleanup'a dokunulmadı (T111'in FraudFlags/AuditLogs temizliği yeterli; emergency-hold yeni FK satırı eklemez).
- **`e2e/package.json`:** `test:hold` script.
- **`.github/workflows/ci.yml`:** advisory `e2e-smoke` job'a "Run API emergency-hold E2E (T112)" adımı (T111 deseniyle birebir) + job yorumu 6 suite'e güncellendi + `timeout-minutes` 70→80 (Test 3'ün dakikalık payout pipeline beklemesi için marj).

## Etkilenen Modüller / Dosyalar

- `e2e/tests/emergency-hold.spec.ts` (yeni — 3 test)
- `e2e/src/api.ts` (değişti — `applyEmergencyHold` + `releaseEmergencyHold` + `assertStatusStable`)
- `e2e/src/db.ts` (değişti — `getTransactionHoldState` + `HoldStateRow`)
- `e2e/package.json` (değişti — `test:hold`)
- `.github/workflows/ci.yml` (değişti — T112 e2e adımı + yorum + timeout 80)

## Kabul Kriterleri Kontrolü

| # | Kriter (11 §T112) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Hold uygulama → timeout durur → resume → devam | ✓ | Test 1: `applyEmergencyHold` → `200` body `status=EMERGENCY_HOLD`/`previousStatus=CREATED`; DB `IsOnHold=1`, `TimeoutFreezeReason=EMERGENCY_HOLD`, `TimeoutFrozenAt` set, `TimeoutRemainingSeconds>0`. `backdateDeadline(AcceptDeadline)` + 18 sn → API `EMERGENCY_HOLD` sabit, DB `Status` hâlâ `CREATED` (scanner held satırı atladı, CANCELLED_TIMEOUT yok). `releaseEmergencyHold(RESUME)` → `200` `{status:CREATED, action:RESUME, itemReturned:null, paymentRefunded:null}`; DB `IsOnHold=0`/freeze null. Alıcı `accept` → `ACCEPTED` → poll `ITEM_ESCROWED` + escrow=1 (akış devam etti). `EMERGENCY_HOLD_APPLIED` + `EMERGENCY_HOLD_RELEASED` her iki tarafa. |
| 2 | Hold uygulama → cancel (ITEM_DELIVERED hariç) | ✓ | Test 2: ITEM_ESCROWED'da hold (`previousStatus=ITEM_ESCROWED`, IsOnHold=1) → `releaseEmergencyHold(CANCEL)` → `200` `{status:CANCELLED_ADMIN, action:CANCEL, itemReturned:true, paymentRefunded:false}`; DB `Status=CANCELLED_ADMIN`/`IsOnHold=0`; RETURN_TO_SELLER teklifi ACCEPTED + escrow=0; her iki taraf `TRANSACTION_CANCELLED`. |
| 3 | ITEM_DELIVERED'da hold → sadece resume | ✓ | Test 3: ITEM_DELIVERED'da hold (`previousStatus=ITEM_DELIVERED`); 12 sn park (`EMERGENCY_HOLD` sabit, DB `Status=ITEM_DELIVERED` — payout pipeline `!IsOnHold` ile kapılı). `releaseEmergencyHold(CANCEL)` → `422` `error.code=CANNOT_CANCEL_DELIVERED_HOLD`, DB `IsOnHold` hâlâ 1 (hold ayakta). `releaseEmergencyHold(RESUME)` → `200` `{status:ITEM_DELIVERED, action:RESUME}`, `IsOnHold=0`; poll `COMPLETED` (payout pipeline devam etti). |

**Doğrulama kontrol listesi:** `[x] Hold/resume/cancel akışları doğru mu?` — apply-hold (freeze + bildirim), RESUME (timeout devam + akış ilerler), CANCEL (non-delivered → CANCELLED_ADMIN + refund fan-out), ITEM_DELIVERED cancel guard (422) ve resume-only, 3 testle uçtan uca kaplandı.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| E2E (lokal tam docker stack) | ✓ 3/3 passed (7.9m) | `E2E_BASE_URL=http://localhost:5000 npm run test:hold`: Test 1 1.6m, Test 2 1.5m, Test 3 4.7m. Stack: db + redis + fake-sidecar + backend (migrations `dotnet ef database update` ile uygulandı, backend healthy). |
| Statik (e2e harness) | ✓ | `tsc --noEmit` temiz; `eslint .` 0/0; `prettier --check` (LF-normalize) değişen 3 dosyada temiz (lokal CRLF uyarısı `core.autocrlf` artifaktı — CI "1. Lint" LF yetkili). |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validator bekliyor (ayrı chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- Migration: Yok (yeni şema yok; mevcut emergency-hold kolonları T44/T50/T59'dan).
- Config/env değişikliği: Yok (yeni env yok; `Timeouts__DeadlineScannerIntervalSeconds=5` T109'dan mevcut).
- Docker değişikliği: Yok (yeni servis/image yok).
- CI: advisory `e2e-smoke` job'a T112 adımı; `timeout-minutes` 70→80.

## Commit & PR

- Branch: `task/T112-e2e-emergency-hold`
- Commit: (bu commit) — T112: E2E — Emergency hold senaryoları (test coverage)
- PR: #205
- CI: ⏳ İzleniyor

## Known Limitations / Follow-up

- **ITEM_DELIVERED park stratejisi:** payout pipeline (`SellerPayoutQueueJob` + `PayoutCompletedConsumer`) `!IsOnHold` ile kapılı + dakikalık 3-iş zinciri (≥2–3 dk) olduğundan, ITEM_DELIVERED'a ulaştıktan birkaç saniye sonra uygulanan hold yarışsız park eder; ayrı bir fake "payout suppress" kolu eklemeye gerek kalmadı.
- **Hold sırasında detay status projeksiyonu:** `GET /transactions/:id` held işlemde `status=EMERGENCY_HOLD` döndürür (overlay); frozen kanıtı bu yüzden DB `Status` kolonunu (CANCELLED_TIMEOUT'a düşmediğini) okur — API-yalnız assertion held-then-cancelled durumunu maskeleyebilirdi.
- **Bulk hold (AD19d — `hold-by-user/:userId`):** 03 §8.8 kapsamı tekil işlem hold/resume/cancel; bulk hold (hesap flag "Hold" aksiyonu) bu E2E'de kapsanmadı (AD19d ayrı endpoint; tekil yüzeyle aynı freeze sequence'i kullanır, T59 unit/integration testleriyle kaplı).
- **Bildirim:** `EMERGENCY_HOLD_APPLIED`/`EMERGENCY_HOLD_RELEASED` inbox satırları asserte edilir; SignalR realtime push (overlay) asserte edilmez (realtime-only, inbox + state geçişiyle kanıtlı — T111 deseni).

## Notlar

- **Adım -1 (Working tree):** temiz (session başında `task/T111-e2e-fraud-flags` branch'inde, `git status --short` boş).
- **Adım 0 (Main CI startup):** son 3 main run `success` — `28027679980` + `28027679952` (T111 #204) + `28017118090` (docs #203).
- **Adım 0b (Memory):** repo + auto memory mevcut, T111 kapanışı yansımış.
- **Dış varsayımlar (kod-okumayla doğrulandı, kırık yok):**
  - AD19b/AD19c endpoint'leri + DTO'lar 07 §9.21–§9.22 / 03 §8.8 ile birebir (`AdminTransactionsController.cs:137/172`, `AdminTransactionDtos.cs`).
  - `super_admin` JWT claim'i `EMERGENCY_HOLD` policy'sini bypass eder (`PermissionAuthorizationHandler.cs:17`).
  - `MinReasonLength=10`, `MinNoteLength=1` (`AdminTransactionService.cs:44/47`).
  - String enum serileştirme (request + response) global (`Program.cs:279/392` `JsonStringEnumConverter`).
  - e2e scanner aralığı 5 sn (`docker-compose.e2e.yml` `Timeouts__DeadlineScannerIntervalSeconds=5`).
  - ITEM_ESCROWED → `itemWasOnPlatform=true`/`paymentWasReceived=false` (`AdminTransactionService.cs:763/771`).
  - ITEM_DELIVERED→COMPLETED payout pipeline `!IsOnHold` ile kapılı (`SellerPayoutQueueJob` + `PayoutCompletedConsumer`).
- **Lokal stack notu (audit trail):** Bu makinede backend ilk boot'ta `Skinora` DB'si yokken migration yarışıyla 139 (SIGSEGV) ile düştü — backend self-migrate **etmez**; CI gibi `dotnet ef database update` (Skinora.Shared + Skinora.API startup-project) ön-adımı uygulanıp backend restart edilince healthy oldu. Ortamsal (Docker Desktop timing), kod kusuru değil; backend image main ile byte-aynı (e2e-only değişiklik, build CACHED).
