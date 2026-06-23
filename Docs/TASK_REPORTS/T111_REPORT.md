# T111 — E2E: Fraud/flag Senaryoları

**Faz:** F6 | **Durum:** ✓ Tamamlandı (bağımsız validator PASS) | **Tarih:** 2026-06-23

---

## Yapılan İşler

03 §7 (fraud/flag akışları) + §8.2 (admin flag inceleme) için uçtan uca (E2E) kapsam — T107–T110 ile aynı seam (Playwright + `docker-compose.e2e.yml` + `sidecar-fake`). Üç flag mekanizması da backend-side **tam wire-li** (`FraudPreCheckService` → `TransactionCreationService` Stage 7; `AdminFlagsController` + `FraudFlagService`; `AccountFlagChecker` → `TransactionEligibilityService`) olduğundan bu task **yalnız test kapsamı ekler — sıfır production kaynak değişikliği** (T108/T109 gibi; T110'daki tek consumer eklemesinden farklı).

- **`e2e/tests/fraud-flags.spec.ts` (yeni):** 4 test.
  1. Fiyat sapması → `FLAGGED` + `PRICE_DEVIATION` pre-create flag → admin **approve** → `CREATED` (07 §9.4).
  2. Fiyat sapması → `FLAGGED` → admin **reject** → `CANCELLED_ADMIN` (07 §9.5).
  3. Yüksek hacim → `FLAGGED` + `HIGH_VOLUME` flag (admin inceleme kuyruğunda görünür, AD2).
  4. Hesap flag'i → yeni işlem `422 ACCOUNT_FLAGGED` (fon akışı engeli); admin **reject** → blok kalkar → `CREATED`.
- **`e2e/src/db.ts`:** `seedHappyPath` cleanup'ına `DELETE FROM FraudFlags WHERE UserId IN (@s,@b)` + `DELETE FROM AuditLogs WHERE UserId IN (@s,@b) OR ActorId IN (@s,@b)` eklendi. **Neden (CI bulgusu — ilk run düzeltmesi):** FraudFlag (UserId/TransactionId/ReviewedByAdminId) ve AuditLog (UserId/ActorId — 06 §4.2 NO ACTION) User'a FK tutar; fraud akışı `FRAUD_FLAG_CREATED/APPROVED/REJECTED` AuditLog satırlarını `UserId=seller` ile yazar. Bu satırlar temizlenmediğinden bir sonraki test'in `seedHappyPath`'i seller'ı silemiyor → (cleanup batch `.catch` ile yutuluyor) → seller re-insert'i `PK_Users` ihlali. T108–T110 seller-UserId audit yazmadığından etkilenmemişti. **Raw SQL EF append-only guard'ını bypass eder** (immutability AppDbContext seviyesinde, DB trigger değil) → AuditLogs DELETE çalışır. Diğer suite'ler için zararsız (audit'e assert etmezler). Yeni: `seed.accountFlagId`, `getFlagForTransaction`, `insertAccountFlag` (ACCOUNT_LEVEL/PENDING), `getSystemSetting`/`setSystemSetting`.
- **`e2e/src/api.ts`:** `listFlags` (AD2 — `GET /admin/flags`), `approveFlag` (AD4), `rejectFlag` (AD5).
- **`e2e/package.json`:** `test:fraud` script.
- **`.github/workflows/ci.yml`:** advisory `e2e-smoke` job'a "Run API fraud/flag E2E (T111)" adımı (T110 deseniyle birebir) + job yorumu 5 suite'e güncellendi (T111 cron-gated bekleme eklemez — FLAGGED işlem dispatch etmez).

## Etkilenen Modüller / Dosyalar

- `e2e/tests/fraud-flags.spec.ts` (yeni — 4 test)
- `e2e/src/db.ts` (değişti — seed cleanup + 5 helper + accountFlagId)
- `e2e/src/api.ts` (değişti — 3 admin-flag helper)
- `e2e/package.json` (değişti — `test:fraud`)
- `.github/workflows/ci.yml` (değişti — T111 e2e adımı + yorum)

## Kabul Kriterleri Kontrolü

| # | Kriter (11 §T111) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Fiyat sapması → flag → admin onay/red | ✓ | Test 1+2: `createTransaction(price=300)` (market=100 seed cache → `\|300−100\|/100 = %200 > %100 price_deviation_threshold`) → `201` body `status=FLAGGED`, `flagReason=PRICE_DEVIATION`; `FraudFlags` satırı `TRANSACTION_PRE_CREATE`/`PRICE_DEVIATION`/`PENDING`; AD2 kuyruğunda görünür. **approve** → `200` `{reviewStatus:APPROVED, transactionStatus:CREATED}` + tx `CREATED` + flag `APPROVED`. **reject** → `{REJECTED, CANCELLED_ADMIN}` + tx `CANCELLED_ADMIN` + flag `REJECTED`. |
| 2 | Yüksek hacim → flag | ✓ | Test 3: `high_volume_amount_threshold` 5000→50 (SystemSetting, cache'siz okunur) → tx1 (boş pencere) `CREATED`, tx2 (pencere ~102 > 50) `FLAGGED`+`flagReason=HIGH_VOLUME`; `FraudFlags` `HIGH_VOLUME`/`PENDING`; AD2 `type=HIGH_VOLUME` kuyruğunda; eşik `finally`'de geri alınır. |
| 3 | Hesap flag'i → fon akışı engeli | ✓ | Test 4: seller'a `ACCOUNT_LEVEL`/`PENDING` flag insert → `createTransaction` `422` `error.code=ACCOUNT_FLAGGED` (`AccountFlagChecker` → eligibility); admin **reject** → flag `REJECTED` → `AccountFlagChecker` false → aynı seller `createTransaction` → `CREATED` (blok kalktı, 03 §7.3 step 5). |

**Doğrulama kontrol listesi:** `[x] Flag akışı uçtan uca çalışıyor mu?` — pre-create fraud engine (PRICE_DEVIATION + HIGH_VOLUME) + AD2/AD4/AD5 admin review + ACCOUNT_LEVEL eligibility block, 4 testle uçtan uca kaplandı.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| e2e statik | ✓ | `npx tsc --noEmit` exit 0 + `npm run lint` (eslint) 0 + prettier (committed LF) clean — değişen dosyalar içerik-temiz |
| E2E senaryoları (4) | ✓ **4/4 firsthand (lokal docker stack)** | Yapımcı lokal tam docker stack'i (db/redis/fake/backend healthy, migrate'li) kurup `npm run test:fraud` koştu → **4/4 passed** (taze DB 4.3s + dirty DB re-run 2.5s; re-seed robustluğu kanıtlı). İlk CI run (`28019011271`) T111 adımı: test 1 geçti, test 2–4 `seedHappyPath`'te `PK_Users` ihlaliyle düştü → AuditLogs cleanup fix → firsthand 4/4. CI advisory `e2e-smoke` "Run API fraud/flag E2E (T111)" adımında da gözlenecek. |
| Regresyon (paylaşılan seed değişimi) | ✓ | AuditLogs cleanup paylaşılan `seedHappyPath`'i etkilediğinden happy-path smoke **taze DB'de firsthand** koşuldu → `CREATED→COMPLETED` **1 passed (4.9m)** (CI sırası: happy-path önce, sonra fraud 4/4) → mainline seed/akış regresyon yok. |
| Backend | — | Üretim kodu değişmedi → backend unit/integration etkilenmez |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** — bağımsız validator (ayrı chat, 2026-06-23, rapor görülmeden kendi verdict'i) |
| Bulgu sayısı | 0 bloke-edici (1 non-blocking doc-note K1) |
| Düzeltme gerekli mi | Hayır |

**Bağımsız validator (ayrı chat, 2026-06-23 — rapor görülmeden):** Kapılar Adım -1 temiz · Adım 0 main son-3 `success` (`28017118090`/`28017118276`/`28013526658`) · Adım 0b repo memory mevcut · branch origin ile senkron (0/0). **Adım 8a:** task CI HEAD `3b129e8` run `28022439502` — **tüm blocking job'lar success** (Lint/Build/Unit/Integration/Contract/Migration/Docker×4/CI Gate) + advisory `e2e-smoke` job ran (skipped değil) ve **"Run API fraud/flag E2E (T111)" adımı `conclusion=success`** (job `continue-on-error` step-level başarıyı maskelemiyor → 4 test gerçek migrate'li docker-compose stack'inde geçti, önceki 4 suite adımı da success → T108–T110 regresyon yok).

**Validator-firsthand lokal tam docker stack** (build → db healthy → migrate → backend+fake healthy → `npm run test:fraud`): **4/4 passed (5.6s)**. Statik: e2e `tsc --noEmit` 0 + `eslint` 0 + prettier (committed/LF) clean (lokal CRLF uyarıları `core.autocrlf=true` checkout artifaktı; CI blocking "1. Lint" job'u `e2e` `format:check`'i LF'te koşar → success).

**Seam (rapor iddiasından bağımsız teyit — kod kaynağına karşı, 5 boyut + adversarial):** 5/5 CONFIRMED. (1) `TransactionCreationService` Stage 7 `status = fraud.ShouldFlag ? FLAGGED : CREATED` + Stage 9 `StagePreCreateFlagAsync` (scope `TRANSACTION_PRE_CREATE`/status `PENDING`) + Stage 11 DTO `FlagReason = fraud.FlagType?.ToString()`; enum'lar `JsonStringEnumConverter` + camelCase ile `status`/`flagReason` olur. (2) `FraudDetectionCalculator.IsHighVolume` **OR semantiği** (her eşik bağımsız `>`; null/0 o kolu kapatır) → yalnız `amount_threshold` 50'ye düşürmek `~102>50` ile tetikler; `CalculatePriceDeviation` kesir döner (`\|300−100\|/100=2.0 > 1.0`); `IsDormantAnomaly` (completed==0 ∧ age≥min ∧ amount>value) e2e'de `value=1000 > 100` olduğundan tetiklenmez → tx1 `CREATED` korunur, öncelik PRICE_DEVIATION→HIGH_VOLUME→dormant. (3) `FraudFlagService.ApproveAsync` (FLAGGED→CREATED state-machine `AdminApprove`, AcceptDeadline init, `{APPROVED,CREATED}`) / `RejectAsync` (tx flag FLAGGED→CANCELLED_ADMIN `{REJECTED,CANCELLED_ADMIN}`; **ACCOUNT_LEVEL flag → transaction bloğu atlanır, NPE yok, yalnız REJECTED**). (4) `GET /api/v1/admin/flags` `[Authorize(Permission:VIEW_FLAGS)]` + `scope`/`type`/`reviewStatus` server-side filtre + `items[].transactionId/type`; super_admin JWT claim `PermissionAuthorizationHandler` bypass (DB rol yok). (5) `AccountFlagChecker` aktif = `Scope==ACCOUNT_LEVEL ∧ Status!=REJECTED ∧ !IsDeleted` → PENDING bloklar/REJECTED kaldırır; eligibility → `TransactionsController` 422 `error.code='ACCOUNT_FLAGGED'`. **Vacuousness LOW** (`retries:0`/`workers:1`; optional-chain assert'ler undefined'da FAIL eder; `find/some` `.toBeTruthy()`; `unwrap` raw-body fallback → bozuk envelope FAIL; 422 + code çifti asserte edilir). **3 refutation denemesi (priority/interference · env-var-vs-DB config · account-flag reject NPE) hepsi REJECTED.**

**Güvenlik:** Sıfır production kaynak değişikliği (`git diff main...HEAD` yalnız `e2e/` + `ci.yml` + docs + memory) · test-fixture secret (JWT mint, T107–T110 ile aynı seam) · yeni dış bağımlılık yok (`package.json` yalnız `test:fraud` script) · yeni `sidecar-fake` yüzeyi yok · DB helper'ları parametreli (`insertAccountFlag`/`get/setSystemSetting` bound param + `FraudSettingKey` union). Temiz.

**Yapım raporuyla karşılaştırma:** Tam uyumlu — 3 AC, zero-prod-change, lever tasarımı, OR-semantik high-volume, CI fix iterasyonu (AuditLogs cleanup → `PK_Users` re-seed) hepsi birebir. Tek ek validator gözlemi → K1.

## Altyapı Değişiklikleri

- Migration: **Yok**
- Enum/şema değişikliği: **Yok**
- Config/env değişikliği: **Yok** (e2e SystemSetting override test içinde runtime; `finally` ile geri alınır)
- Docker değişikliği: **Yok**
- Yeni dış bağımlılık: **Yok**

## Dış Varsayımlar (task.md Adım 4)

- **Backend fraud wiring tam mı?** — Evet, 3 kural da implement edilmiş ve hot-path'e bağlı: `FraudPreCheckService.EvaluateAsync` (price_deviation → high_volume → dormant öncelik sırası) `TransactionCreationService` Stage 7'de çağrılır → `FLAGGED` + `StagePreCreateFlagAsync` flag satırı (06 §3.12 invariant); `AccountFlagChecker.HasActiveAccountFlagAsync` (Status != REJECTED) `TransactionEligibilityService` Stage 2'de → `ACCOUNT_FLAGGED`; `AdminFlagsController` + `FraudFlagService.Approve/RejectAsync` (FLAGGED→CREATED / FLAGGED→CANCELLED_ADMIN) mevcut. file:line ile doğrulandı.
- **Market price kaynağı** — Production `PriceServiceMarketPriceProvider` → `IPriceService` → `ItemPriceCache` (TTL ≤24h fresh → `MedianPrice ?? LowestPrice`). e2e seed `ItemPriceCaches` satırını listeleme fiyatına (100, taze) yazar → kontrollü market fiyatı; HTTP/Steam erişimi gerekmez.
- **e2e SystemSetting değerleri** — `docker-compose.e2e.yml` bootstrap (`SKINORA_SETTING_*`): `price_deviation_threshold=1.0` (migration default), `high_volume_amount_threshold=5000`/`count=10`/`period=24h`, `dormant_value_threshold=1000`, `max_concurrent=5`, `min/max amount=1/10000`. Doğrulandı → 300 listeleme (≤10000) sapar; 100 listeleme dormant'a (1000) takılmaz; 2 aktif tx concurrent'e (5) sığar.
- **Enum string kolonlar** — `FraudFlags.Scope/Type/Status` nvarchar (CHECK constraint'leri `'ACCOUNT_LEVEL'`/`'PENDING'` literal karşılaştırır); `RowVersion` SQL Server rowversion (insert'te omit). Seed'in `PlatformSteamBots.Status='ACTIVE'` deseniyle tutarlı.
- **Admin yetkisi** — `VIEW_FLAGS`/`MANAGE_FLAGS` policy'leri super_admin JWT claim ile `PermissionAuthorizationHandler` bypass üzerinden karşılanır (DB rol ataması gerekmez; T108 admin-cancel ile aynı). `ensureAdmin()` ReviewedByAdminId FK için admin User satırını garanti eder.

## Commit & PR

- Branch: `task/T111-e2e-fraud-flags`
- Commit: `a31e777` (kod)
- PR: [#204](https://github.com/turkerurganci/Skinora/pull/204)
- CI: ⏳ izleniyor (CI Gate blocking + advisory `e2e-smoke` T111 adımı)

## Known Limitations / Follow-up

- **K1 (validator non-blocking — doc reconciliation, T111 kusuru değil):** Test 4 hesap flag'ini `POST /admin/flags/:id/reject` ile kaldırır. **03 §8.2** (`03_USER_FLOWS.md:517`) hesap flag'lerinin bu kuyrukta görünmeyip "ayrı bir hesap flag yönetim yüzeyinden" yönetildiğini söyler; ancak **07 §9.2** (`07_API_DESIGN.md:1705`) + **04 §8.2 / T100a** `GET /admin/flags`'in `scope=ACCOUNT_LEVEL`'i kabul edip tüm kategorileri döndürdüğünü tanımlar ve hesap-flag kolonlarını AD2 yüzeyine ekler. Üretim kodu 07/04'ü izler (ayrı hesap-flag yönetim endpoint'i **yok**; her iki scope da `/admin/flags/*` paylaşır). Yani **03 §8.2 ↔ 07 §9.2/04 §8.2 çelişkisi pre-existing** (T100/T100a'da tek-yüzey lehine fiilen çözülmüş; 03 §8.2 ifadesi stale) — **T111 bu mevcut gerçekliği tüketir**, AC3'ü zayıflatmaz (AC3 yalnız "fon akışı engeli" ister; 422 + reject-ile-kaldırma birebir kanıtlı). **Takip:** 03 §8.2 metni 04 §8.2 / 07 §9.2 ile hizalanmalı (owner kararı; T110-K1 cross-doc deseniyle aynı tür).
- **Admin "flag oluştu" bildirimi (`ADMIN_FLAG_ALERT`, 03 §7.1 step 6) asserte edilmiyor.** `AdminRecipientResolver` admin'leri `AdminUserRole` satırlarından çözer; e2e admin yalnız super_admin JWT claim ile çalışır (DB rol ataması yok) → broadcast 0 alıcıya gider, `Notifications` satırı oluşmaz. Admin-yüzü `GET /admin/flags` kuyruğu (AD2) ile kanıtlanıyor — admin'in flag'i gördüğü ve incelediği uçtan uca akış kapalı. `ADMIN_FLAG_ALERT` üretimi backend unit testleriyle kaplı.
- **Approve/reject taraf bildirimi (03 §8.2 "taraflara bildirim gider") realtime-only.** `FraudFlag{Approved,Rejected}Event` yalnız `Skinora.Realtime` SignalR consumer'larınca tüketilir (inbox `INotificationHandler` yok) — WP19'un bazı state değişimlerini realtime-only bıraktığı deseniyle tutarlı. API-düzeyi test taraf bildirimini değil, state geçişini (`CREATED`/`CANCELLED_ADMIN`) doğrular. Spec'in pre-create flag-reject'inde inbox `TRANSACTION_CANCELLED` üretip üretmemesi gerektiği **olası takip konusu** (pre-existing tasarım, T111 kusuru değil; owner kararına bırakıldı).
- E2E senaryoları docker yığını gerektirdiğinden yapım sırasında lokal koşulmadı; CI advisory `e2e-smoke` job'unda gözlenir (T107–T110 ile aynı kalıp). Bağımsız validator lokal tam docker stack'te firsthand koşar.

## Notlar

- **Working tree (Adım -1):** Oturum başında temiz.
- **Main CI (Adım 0):** Son 3 run `success` (`28017118090`/`28017118276` docs T110-K1 #203 · `28013526658` T110 #202).
- **Lever tasarımı:** FLAGGED işlem hiç sidecar çağrısı yapmaz (payment-address allocation yalnız CREATED'da; escrow dispatch yalnız ACCEPTED sonrası) → T111 yeni `sidecar-fake` yüzeyi gerektirmez (T110'dan farklı). Fiyat sapması yalnız listeleme fiyatıyla (300 vs seed market 100) sürülür — cache manipülasyonu gerekmez. Yüksek hacim için `high_volume_amount_threshold` runtime düşürülür (admin parametre yönetimi 03 §8.4'ün gerçek kullanımını yansıtır); `finally` ile geri alınır + her test re-seed prior tx'leri temizlediğinden diğer testler etkilenmez.
- **CI ölçeklenme:** T111 ucuz (FLAGGED dispatch etmez → cron-gated bekleme yok), tek-job 70dk bütçesine sığar. ci.yml yorumu T112–T114 için paralel job/matrix bölünmesi notunu korur.
- **CI fix iterasyonu (ilk run → düzeltme → firsthand doğrulama):** İlk push (`8b642b1`, FraudFlags cleanup ile) CI run `28019011271`'de tüm **blocking job'lar success** (CI Gate ✓) ama advisory e2e-smoke T111 adımı **failure** — test 1 geçti, test 2–4 `seedHappyPath`'te `PK_Users` ihlaliyle düştü (yukarıdaki AuditLog FK kök nedeni). **Düzeltme:** `seedHappyPath`'e AuditLogs cleanup eklendi. **Firsthand doğrulama (lokal tam docker stack):** fraud 4/4 (taze + dirty re-run) + happy-path taze DB 4.9m → COMPLETED (regresyon yok). **Lokal gözlem (CI'yı etkilemez):** fraud suite'i happy-path'ten ÖNCE dirty DB'de koşturulunca outbox dispatcher, re-seed'in sildiği transaction'a ait orphan notification event'inde (`FK_Notifications_Transactions_TransactionId`) takılıp poison-message ile durdu → happy-path'in payout→COMPLETED flip'i gecikti. Bu, e2e harness'ın re-seed-while-events-pending karakteristiği (pre-existing, T108–T110'da da var); CI'da happy-path ÖNCE taze DB'de koşar → etkilenmez. T111 testleri senkron state'e assert eder (async outbox'a değil) → poison testleri düşürmez.
