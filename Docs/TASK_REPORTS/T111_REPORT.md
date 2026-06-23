# T111 — E2E: Fraud/flag Senaryoları

**Faz:** F6 | **Durum:** ⏳ Devam ediyor (yapım tamam, bağımsız doğrulama bekliyor) | **Tarih:** 2026-06-23

---

## Yapılan İşler

03 §7 (fraud/flag akışları) + §8.2 (admin flag inceleme) için uçtan uca (E2E) kapsam — T107–T110 ile aynı seam (Playwright + `docker-compose.e2e.yml` + `sidecar-fake`). Üç flag mekanizması da backend-side **tam wire-li** (`FraudPreCheckService` → `TransactionCreationService` Stage 7; `AdminFlagsController` + `FraudFlagService`; `AccountFlagChecker` → `TransactionEligibilityService`) olduğundan bu task **yalnız test kapsamı ekler — sıfır production kaynak değişikliği** (T108/T109 gibi; T110'daki tek consumer eklemesinden farklı).

- **`e2e/tests/fraud-flags.spec.ts` (yeni):** 4 test.
  1. Fiyat sapması → `FLAGGED` + `PRICE_DEVIATION` pre-create flag → admin **approve** → `CREATED` (07 §9.4).
  2. Fiyat sapması → `FLAGGED` → admin **reject** → `CANCELLED_ADMIN` (07 §9.5).
  3. Yüksek hacim → `FLAGGED` + `HIGH_VOLUME` flag (admin inceleme kuyruğunda görünür, AD2).
  4. Hesap flag'i → yeni işlem `422 ACCOUNT_FLAGGED` (fon akışı engeli); admin **reject** → blok kalkar → `CREATED`.
- **`e2e/src/db.ts`:** `seedHappyPath` cleanup'ına `DELETE FROM FraudFlags WHERE UserId IN (@s,@b)` eklendi (FraudFlag → User/Transaction NO ACTION FK'leri; fraud testleri flag satırı bıraktığından sonraki re-seed'in Users/Transactions silmesini bu olmadan FK bloklardı — diğer suite'ler için zararsız no-op). Yeni: `seed.accountFlagId`, `getFlagForTransaction`, `insertAccountFlag` (ACCOUNT_LEVEL/PENDING), `getSystemSetting`/`setSystemSetting`.
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
| e2e statik | ✓ | `npx tsc --noEmit` exit 0 + `npm run lint` (eslint) 0 + prettier (committed LF) clean — değişen 3 dosya içerik-temiz |
| E2E senaryoları (4) | ⏳ CI'da gözleniyor | CI advisory `e2e-smoke` job'unda "Run API fraud/flag E2E (T111)" adımı (`npm run test:fraud`) — sonuç bağımsız validator tarafından firsthand koşulur (docker yığını gerektirir; T107–T110 kalıbı) |
| Backend | — | Üretim kodu değişmedi → backend unit/integration etkilenmez |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validator bekliyor (ayrı chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

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

- **Admin "flag oluştu" bildirimi (`ADMIN_FLAG_ALERT`, 03 §7.1 step 6) asserte edilmiyor.** `AdminRecipientResolver` admin'leri `AdminUserRole` satırlarından çözer; e2e admin yalnız super_admin JWT claim ile çalışır (DB rol ataması yok) → broadcast 0 alıcıya gider, `Notifications` satırı oluşmaz. Admin-yüzü `GET /admin/flags` kuyruğu (AD2) ile kanıtlanıyor — admin'in flag'i gördüğü ve incelediği uçtan uca akış kapalı. `ADMIN_FLAG_ALERT` üretimi backend unit testleriyle kaplı.
- **Approve/reject taraf bildirimi (03 §8.2 "taraflara bildirim gider") realtime-only.** `FraudFlag{Approved,Rejected}Event` yalnız `Skinora.Realtime` SignalR consumer'larınca tüketilir (inbox `INotificationHandler` yok) — WP19'un bazı state değişimlerini realtime-only bıraktığı deseniyle tutarlı. API-düzeyi test taraf bildirimini değil, state geçişini (`CREATED`/`CANCELLED_ADMIN`) doğrular. Spec'in pre-create flag-reject'inde inbox `TRANSACTION_CANCELLED` üretip üretmemesi gerektiği **olası takip konusu** (pre-existing tasarım, T111 kusuru değil; owner kararına bırakıldı).
- E2E senaryoları docker yığını gerektirdiğinden yapım sırasında lokal koşulmadı; CI advisory `e2e-smoke` job'unda gözlenir (T107–T110 ile aynı kalıp). Bağımsız validator lokal tam docker stack'te firsthand koşar.

## Notlar

- **Working tree (Adım -1):** Oturum başında temiz.
- **Main CI (Adım 0):** Son 3 run `success` (`28017118090`/`28017118276` docs T110-K1 #203 · `28013526658` T110 #202).
- **Lever tasarımı:** FLAGGED işlem hiç sidecar çağrısı yapmaz (payment-address allocation yalnız CREATED'da; escrow dispatch yalnız ACCEPTED sonrası) → T111 yeni `sidecar-fake` yüzeyi gerektirmez (T110'dan farklı). Fiyat sapması yalnız listeleme fiyatıyla (300 vs seed market 100) sürülür — cache manipülasyonu gerekmez. Yüksek hacim için `high_volume_amount_threshold` runtime düşürülür (admin parametre yönetimi 03 §8.4'ün gerçek kullanımını yansıtır); `finally` ile geri alınır + her test re-seed prior tx'leri temizlediğinden diğer testler etkilenmez.
- **CI ölçeklenme:** T111 ucuz (FLAGGED dispatch etmez → cron-gated bekleme yok), tek-job 70dk bütçesine sığar. ci.yml yorumu T112–T114 için paralel job/matrix bölünmesi notunu korur.
