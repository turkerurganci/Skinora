# T138 — E2E spec'lerinin yeniden yazımı

**Faz:** F7 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-08-21

---

## Yapılan İşler

T117'nin P2P pivotundan beri sekiz advisory E2E leg'i custody dönemine göre yazılıydı. T137a bunu ölçmüştü: harness onarıldıktan sonra **32 testin 10'u geçiyor**, kalan 22'si emekli durumlarda (`ITEM_ESCROWED` / `TRADE_OFFER_SENT_TO_*`) takılıyordu. Bu görev spec'leri 02 §2.2'nin P2P zincirine yeniden yazdı.

**Zincir:** `CREATED → ACCEPTED →` (satıcı `confirm-ready`) `→ SELLER_CONFIRMED →` (ödeme) `→ PAYMENT_RECEIVED →` (satıcı→alıcı trade + alıcı `confirm-receipt`) `→ ITEM_DELIVERED →` (mutabakat turu) `→ COMPLETED`.

**Sıfır production kaynak değişikliği.** `git diff origin/main...HEAD --stat` yalnız `e2e/**` + `.github/workflows/ci.yml` (+ bu tur sonunda docs/memory).

### Kapsamı belirleyen üç ölçüm

Yeniden yazımın şekli üç sert kısıttan çıktı; üçü de kod okunarak doğrulandı, varsayılmadı:

1. **`AmountValidationService.cs:87` ödemeyi yalnız `SELLER_CONFIRMED`'da kabul ediyor.** Dahası, §5.5 çok-ödeme kolunu *"durum SELLER_CONFIRMED değil"* diye seçiyor — yani kurulumu bir adım eksik bırakan bir spec, test ettiğini sandığı kolun yerine sessizce çok-ödeme kolunu ölçerdi. Bu yüzden `confirm-ready` altı ödeme senaryosunun da kurulumuna girdi.
2. **`delivery.inventory_evidence_auto_release_enabled` seed default `false`** (`SystemSettingSeed.cs:170`). Envanter kanıtı yolu launch kapısıyla kapalı olduğu için e2e'de kendiliğinden ilerleyen tek teslimat yolu **alıcı onayı**dır — DEPLOY_RUNBOOK §G.4 kontrol 10'un canlı prova için söylediğinin aynısı.
3. **`payout_settlement_days` tabanı 7 gün** (`SystemSettingsValidator`, 02 §16.2) — ayardan kısaltılamaz. `COMPLETED` görmek isteyen her test runbook §G.4 kontrol 10a'nın kendi kısayolunu kullanıyor (`PayoutEligibleAt = SYSUTCDATETIME()`). Kısayol yalnız **saati** sahteler: `settlement-verification` alıcının envanterini gerçekten yeniden okuyor ve `SettlementVerifiedAt`'i item orada durduğu için damgalıyor; `seller-payout-queue` de o damgayı önkoşul olarak arıyor.

### Proje sahibi kararları (2026-08-21, üçü de öneri kabul edildi)

- **D1 — `happy-path.ui` için 9. leg eklendi.** Alternatif "yalnız lokal koşulur" idi. Gerekçe: T134/T135 FE durum yüzeyini (`data-status`, 04 §7.3 state×rol paneli) yeni yazdı ve onu doğrulayan tek sinyal bu spec'ti; ölçülmeyen bir sinyal T137'nin kalıcı dersinin tekrarı olurdu.
- **D2 — Happy path `COMPLETED`'a kadar sürülüyor**, runbook kısayoluyla. Alternatif `ITEM_DELIVERED`'da durmak idi. Gerekçe: `SettlementVerificationJob` + `SellerPayoutQueueJob`'ın e2e'deki **tek** kapsamı bu; kısayol verdict'i değil saati sahteliyor.
- **D3 — Yeni `delivery.spec.ts` + kendi leg'i.** Alternatif üç senaryoyu mevcut spec'lere dağıtmak idi. Gerekçe: sinyalin sahibi olsun (T137 dersi) ve 03 §4'ün dört fazı `timeout.spec.ts`'te bütün kalsın.

## Etkilenen Modüller / Dosyalar

| Dosya | Değişiklik |
|---|---|
| `e2e/src/api.ts` | `confirmReady` + `confirmReceipt` eklendi; `pollUntilRefundableCancel` **kaldırıldı**; `freezeMaintenance` doc'u P2P kapsamlarına hizalandı |
| `e2e/src/db.ts` | İkinci listelenebilir item + fiyat cache satırı; `setPayoutEligibleNow`, `getSettlementState`, `pollSettlementVerified`, `pollDisputeForTransaction`, `getLatestDeliveryCapture`; `setDeadlineFromNow` **kaldırıldı** |
| `e2e/tests/happy-path.smoke.spec.ts` | Yeniden yazıldı — tam P2P zinciri + P2P bildirim kümesi |
| `e2e/tests/happy-path.ui.spec.ts` | Yeniden yazıldı — **iki** tarayıcı context'i (satıcı + alıcı) |
| `e2e/tests/cancellation.spec.ts` | Yeniden yazıldı — iptal-öncesi durum `SELLER_CONFIRMED`; `itemReturned` assertion'ları düştü |
| `e2e/tests/timeout.spec.ts` | Yeniden yazıldı — 03 §4.1–§4.4 dört faz, §4.4 artık doğrulama turu |
| `e2e/tests/payment-edge-cases.spec.ts` | Yeniden yazıldı — kurulum `SELLER_CONFIRMED`'a taşındı |
| `e2e/tests/emergency-hold.spec.ts` | Yeniden yazıldı — senaryo 3'te mutabakat penceresi kaynaklı **yalancı geçiş** onarıldı |
| `e2e/tests/downtime.spec.ts` | Yeniden yazıldı — STEAM_OUTAGE `ACCEPTED`'a, BLOCKCHAIN_DEGRADATION `SELLER_CONFIRMED`'a; G1 reset hatası kapatıldı |
| `e2e/tests/fraud-flags.spec.ts` | Noktasal — high-volume ikinci asset'e taşındı |
| `e2e/tests/admin-flows.spec.ts` | Noktasal — `steamAccounts` assertion'ı silindi |
| `e2e/tests/delivery.spec.ts` | **YENİ** — 02 §9.2 teslimat doğrulaması, 2 senaryo |
| `e2e/playwright.config.ts` | Test timeout 12 → 20 dk (mutabakat penceresi gerekçeli) |
| `e2e/package.json` | `test:delivery` script'i |
| `.github/workflows/ci.yml` | E2E matrisi 8 → 10 leg; `extraServices` / `baseUrl` / `timeoutMinutes` anahtarları; koşullu chromium kurulumu |

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 7 spec yeniden yazıldı (21 test): happy-path.smoke (1) · happy-path.ui (1) · cancellation (4) · timeout (3/4) · payment-edge-cases (6) · emergency-hold (3) · downtime (3) | ✓ | Yedi dosyanın hepsi P2P zincirine yeniden yazıldı; ayrıntı §Senaryo haritası. `timeout`'un 4. testi de yeniden yazıldı (kriter 3/4 diyordu — accept-timeout testi de dokunulmadan geçmedi, `beforeEach` reset'i eklendi ama senaryosu değişmedi) |
| 2 | 2 spec noktasal düzeltmeyle kapandı: admin-flows AC1 `steamAccounts` · fraud-flags high-volume | ✓ | `admin-flows.spec.ts` — üç satırlık assertion silindi, yerine alanın neden geri gelmeyeceğini yazan not; `fraud-flags.spec.ts` — `createBody` ikinci parametre aldı, tx2 `seed.secondItemAssetId`'yi listeliyor |
| 3 | `happy-path.ui` için CI'da leg yok → ya 9. leg eklenir ya da lokal-only olduğu açıkça yazılır | ✓ | **Leg eklendi** (D1). `ci.yml` matrisinde `happy-path UI`; `extraServices` frontend + nginx'i ayağa kaldırıyor, `baseUrl` nginx origin'i, chromium **yalnız** o leg'de indiriliyor |
| 4 | Yeni specler: alıcı-onay hızlı yolu · delivery timeout → satıcı kusurlu iptal · satıcı-başka-yere-gönderdi → auto-escalation | ✓ | `delivery.spec.ts` test 1 (hızlı yol + idempotency) · `timeout.spec.ts` test 4 (satıcı kusurlu iptal — 03 §4.4'ün kendi fazı, bkz. §Senaryo haritası) · `delivery.spec.ts` test 2 (auto-escalation) |
| 5 | Envanter seed sorumluluğu: her spec (a) ortak seed yetmiyorsa kendi `setFakeInventory`'sini yazar, (b) teslimat kanıtını `simulateFakeTrade` ile üretir; alıcının SIFIR baseline'ı korunur | ✓ | Ortak seed iki item'ı da satıcıya yazıyor (ikincisi T128 kapısı için); alıcı hiçbir yerde seed edilmiyor → PUBLIC + BOŞ, baseline 0. `simulateFakeTrade` üç yerde: happy-path.smoke, happy-path.ui, emergency-hold #3 (satıcı→alıcı) ve delivery #2 (satıcı→üçüncü taraf) |
| 6 | Bilinen vaka: `downtime.spec.ts`'in iki testi `resetFakeSteamState()`'i `seedHappyPath()`'ten **sonra** çağırıp ortak seed'i siliyor (T137 G1) | ✓ | Gövde içi iki çağrı silindi, `test.beforeEach` hook'una taşındı; hook'a bunun neden orada olması gerektiğini yazan not kondu |
| 7 | `DEFERRED_BACKLOG` `T136-E2EDeadItemReturnedAssertions` (satır T138'i sahibi olarak adlandırıyor) | ✓ | Altı maddenin altısı kapandı + satırın kendisinin kaçırdığı yedinci ölü referans; ayrıntı §Kapatılan backlog satırı. Satır ✅ işaretlendi, backlog 58 → **57 aktif / 64 çözülmüş** |

## Senaryo haritası — üç yeni senaryo nereye düştü

Kabul kriteri üç "yeni spec" sayıyor; ikisi yeni dosyaya, biri mevcut suite'in kendi fazına düştü. Gerekçe:

| Senaryo | Yer | Neden orada |
|---|---|---|
| Alıcı-onay hızlı yolu | `delivery.spec.ts` #1 | 02 §9.2'nin kanıt motoru; hiçbir faz timeout'una bağlı değil |
| Delivery timeout → satıcı kusurlu iptal | `timeout.spec.ts` #4 | 03 §4'ün **dördüncü fazı**. Dört fazı bir arada tutmak dokümanın kendi yapısı; ayrıca üç doğrulama kolundan **iptal eden tek kol** budur |
| Satıcı başka yere gönderdi → auto-escalation | `delivery.spec.ts` #2 | İptal **etmeyen** kol. Timeout suite'ine koymak "timeout iptal eder" iddiasını yanlışlardı |

## Ölçüm sırasında düzeltilen iki gerçek hata

Bunlar P2P yeniden adlandırması değil; yazarken kodu okuyunca çıktı:

- **`emergency-hold` #3 yalancı geçiş üretiyordu.** "Hold hattı tutar" iddiası, custody döneminde `ITEM_DELIVERED → COMPLETED`'ın per-minute bir payout hattı olmasına dayanıyordu. P2P araya 02 §4.5.1 mutabakat penceresini koydu: hold çalışsa da çalışmasa da satır sekiz gün `ITEM_DELIVERED`'da beklerdi ve test **yanlış sebeple** geçerdi. Düzeltme: önce hold uygulanıyor, **sonra** `setPayoutEligibleNow` — bu sıra hem saati bahane olmaktan çıkarıyor hem de "uygun ama henüz hold'lu değil" penceresini (mutabakat cron'unun içine düşebileceği bir yarış) tamamen kapatıyor.
- **`fraud-flags` high-volume testi ilgisiz bir iş kuralında düşüyordu.** İkinci `create`'i T128'in `(SellerId, ItemAssetId)` tekillik kapısı `ITEM_ALREADY_LISTED` ile reddediyordu, yani test "rolling window'u ölçüyorum" derken aslında hiç fraud motoruna varmıyordu. İkinci asset + kendi fiyat cache satırı (aynı fiyattan, `PRICE_DEVIATION` — daha yüksek öncelikli kural — temiz kalsın diye).

## Kapatılan backlog satırı — `T136-E2EDeadItemReturnedAssertions`

`DEFERRED_BACKLOG` §Öne Çıkanlar'daki bu 🟡 satır **T138'i açıkça sahibi olarak adlandırıyordu** ("T136 branch'inde uygulanmadı, kabul kriterine taşınmalı"). Altı maddesinin altısı da kapatıldı ve tek tek doğrulandı:

| Madde | Durum |
|---|---|
| `cancellation.spec.ts` üç `itemReturned` hard-fail'i | ✓ spec yeniden yazıldı |
| Aynı dosyanın iki custody başlığı ("item returned") | ✓ başlıklar "nothing refunded" |
| `emergency-hold.spec.ts` `cancelBody.itemReturned` hard-fail'i | ✓ silindi |
| `emergency-hold.spec.ts` **boşuna geçen** `expect(resumeBody.itemReturned ?? null).toBeNull()` | ✓ yerine `paymentRefunded` iddiası — RESUME'de gerçekten `null` döner, yani iddia artık bir şey ölçüyor |
| `admin-flows.spec.ts` `steamAccounts` bloğu | ✓ silindi + notu yazıldı |
| `e2e/src/api.ts` docstring'i | ✓ (bu turda `itemReturned` referansı kalmadı) |

**Satırın kendi envanteri eksikti.** Kapatırken yedinci bir ölü referans çıktı: `e2e/src/db.ts`'in T137a notu, `pollRefundOfferAccepted`'in neden silindiğini anlatırken *"P2P'de bir cancel'ın gözlenecek dönüş bacağı yok — cancel yanıtının `itemReturned` alanı hikâyenin tamamı"* diyordu. O alan da yok. Yani ölü-referans avının kendisi bir ölü referans bırakmıştı; not düzeltildi. Ayrıca `docker-compose.e2e.yml` başlığı hâlâ "users, bot" seed'ini anlatıyordu (T137a bot satırını kaldırmıştı) — o da düzeltildi.

Doğrulama: `grep -rn "itemReturned" e2e/ docker-compose.e2e.yml` → geriye yalnız **silmeyi açıklayan** üç yorum kalıyor.

## Kaldırılan iki harness kaldıracı

İkisi de T137a'nın desenini izliyor: yerine ne konacağını (ya da konmayacağını) yazan bir not bırakıldı.

- **`pollUntilRefundableCancel`** — `PAYMENT_RECEIVED` *veya* `TRADE_OFFER_SENT_TO_BUYER` kabul ediyordu, çünkü per-minute delivery-dispatch job'ı testi izlerken satırı birinciden ikinciye kaydırabiliyordu. P2P'de `PAYMENT_RECEIVED`'ı kendiliğinden ilerleten hiçbir şey yok, yani hedge ettiği yarış yok; yerine düz `pollStatus(..., 'PAYMENT_RECEIVED')`.
- **`setDeadlineFromNow`** — tek çağıranı, `SellerConfirmDeadline`'ı `null` bırakan bir fake webhook yoluyla girilen `TRADE_OFFER_SENT_TO_SELLER`'a canlı pencere uydurmak zorundaydı. P2P'de her dondurulabilir faza kendi deadline'ını damgalayan bir **production ucundan** giriliyor (accept → `SellerConfirmDeadline`, confirm-ready → `PaymentDeadline`, ConfirmPayment → `DeliveryDeadline`), dolayısıyla uydurma deadline gerektiren bir test, akışın üretemediği bir durumu test ediyor demektir.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| tsc | ✓ 0 hata | `npx tsc --noEmit` (e2e) |
| eslint | ✓ 0/0 | `npx eslint .` (e2e) |
| prettier | ✓ temiz | `npx prettier --end-of-line auto --check "src/**/*.ts" "tests/**/*.ts" "*.ts" "package.json"` — lokal CRLF artefaktı ayıklandı, CI "1. Lint" LF üzerinde yetkili |
| ci.yml YAML | ✓ ayrıştırılıyor | `js-yaml` ile yüklendi; matris 10 leg, `timeout-minutes: ${{ matrix.timeoutMinutes \|\| 30 }}` |
| E2E (10 leg) | _PR CI'sinde koşuyor_ | Sonuç aşağıya işlenecek |

## Altyapı Değişiklikleri

- Migration: Yok
- Config/env değişikliği: Yok (`docker-compose.e2e.yml` değişmedi — UI leg'i zaten tanımlı `skinora-frontend` + `skinora-reverse-proxy` servislerini ayağa kaldırıyor)
- Docker değişikliği: Yok
- CI: E2E matrisi 8 → 10 leg; üç leg'in `timeout-minutes` bütçesi 40–45 dk'ya çıktı; chromium indirmesi koşullu

## Commit & PR

- Branch: `task/T138-e2e-p2p-rewrite`
- Commit: `580d6ae` — E2E spec'lerinin P2P'ye yeniden yazımı
- PR: [#255](https://github.com/turkerurganci/Skinora/pull/255)
- CI: _izleniyor_

## Known Limitations / Follow-up

- **Bloke etmeyen gözlem (backend doküman driftı, bu turun kapsamı dışında):** `AdminTransactionDtos.cs:63-65`'teki `ReleaseEmergencyHoldResponse` XML doc'u hâlâ `itemReturned` alanından söz ediyor; alan v3.0'da record'dan düşmüş. Yalnız yorum satırı, davranış etkisi yok. E2E görevinde production kaynağına dokunmamak için düzeltilmedi. **Bilerek backlog satırı açılmadı** ve karar doğrulama turuna bırakıldı (backlog durum notunda yazılı): satır açmaya değer mi, yoksa T135 N2 gibi "iş üretmediği için" mi geçilmeli. Bir sonraki backend turunun yolu üstünde olduğu için ikinci seçenek savunulabilir.
- **`happy-path.ui` leg'i pahalı:** frontend imajı build ediliyor ve chromium indiriliyor (~+8 dk). Bütçesi 45 dk'ya çekildi; ilk birkaç koşumda gerçek süresi izlenmeli.
- Mutabakat turunun cron'u (`SettlementVerificationJob.Cron`, beş dakika) bir `const` — konfigüre edilemiyor. `COMPLETED`'a varan üç testin her biri en kötü ihtimalle beş dakika bekliyor; leg süresi bütçelerinin asıl sebebi budur.
- 02 §9.2'nin **envanter kanıtı** yolu (launch kapısı açıkken `SELLER_ASSET_GONE ∧ INVENTORY_DELTA` → `Delivered`) e2e'de hâlâ kapsanmıyor: kapı seed'de `false` ve onu açmak bir platform ayarını değiştirmek demek. `InventoryEvidencePendingReview` kolu da aynı sebeple kapsanmıyor.

## Notlar

**Working tree (task.md Adım -1):** Temiz.

**Adım 0 (main CI startup):** Son 3 tamamlanmış run `success` — `32497982838`, `32497983077`, `32478492189`.

**Bağımlılıklar:** T135 ✓ · T137 ✓ · T137a ✓ (hepsi `IMPLEMENTATION_STATUS.md`'de ✓ Tamamlandı).

**Dış varsayımlar:**
- `payout_settlement_days` 7 günlük taban — `SystemSettingsValidator.MinimumSettlementDays` okundu, ayardan kısaltılamadığı doğrulandı → runbook §G.4 10a kısayolu kullanıldı.
- `delivery.inventory_evidence_auto_release_enabled` seed default'u — `SystemSettingSeed.cs:170`'te `"false"` okundu.
- `TimeoutFreezeReasonScopes` P2P kapsamları — dosya okundu: `STEAM_OUTAGE = {ACCEPTED, PAYMENT_RECEIVED}`, `BLOCKCHAIN_DEGRADATION = {SELLER_CONFIRMED}`.
- GitHub Actions ifadelerinde tireli matris anahtarı — `matrix.timeout-minutes` çıkarma olarak ayrıştığı için anahtarlar tiresiz seçildi (`timeoutMinutes` vb.); aynı dosya zaten `needs.changes.outputs['e2e-stack']` indeks formunu bu sebeple kullanıyor.
- Playwright tarayıcı kurulumu — `npm ci` chromium indirmiyor; sekiz API leg'i `page` fixture'ını hiç kullanmadığı için tarayıcısız çalışıyordu, UI leg'ine koşullu `npx playwright install --with-deps chromium` adımı eklendi.
- **Lokal Docker çalışmıyor** (daemon kapalı), dolayısıyla stack lokalde ayağa kaldırılmadı; E2E kanıtı PR CI'sinin 10 advisory leg'inden gelir — T137/T137a'nın izlediği yolun aynısı.
