# T108 — E2E: İptal Senaryoları

**Faz:** F6 | **Durum:** ✓ Tamamlandı (bağımsız validator PASS) | **Tarih:** 2026-06-22

---

## Yapılan İşler

T107'nin kurduğu E2E harness'ı (Playwright + `docker-compose.e2e.yml` + fake sidecar) üzerine **iptal senaryolarının API-düzeyi uçtan uca testleri** eklendi. Backend iptal yolu zaten tümüyle wire-li olduğundan (T51 user-cancel + T59 admin-cancel + T106a item-return + WP2 payment-refund + `TransactionCancelledNotificationConsumer`), bu task **yalnız test kapsamı** ekler — **sıfır production kaynak değişikliği**.

Yeni `e2e/tests/cancellation.spec.ts` 4 test:

1. **Satıcı iptali (ödeme öncesi)** — ITEM_ESCROWED'da `POST /transactions/:id/cancel` → `CANCELLED_SELLER`; item satıcıya iade (`RETURN_TO_SELLER` offer ACCEPTED + bot `ActiveEscrowCount`→0); yalnız **alıcıya** `TRANSACTION_CANCELLED` (03 §2.5).
2. **Alıcı iptali (ödeme öncesi)** — ITEM_ESCROWED'da alıcı cancel → `CANCELLED_BUYER`; item iadesi; yalnız **satıcıya** bildirim (03 §3.3).
3. **Admin iptali (ödeme öncesi)** — `POST /admin/transactions/:id/cancel` (super_admin JWT) → `CANCELLED_ADMIN`; item iadesi; **her iki tarafa** bildirim (03 §8.7).
4. **Ödeme sonrası** — PAYMENT_RECEIVED'da: satıcı + alıcı cancel **reddedilir** (`PAYMENT_ALREADY_SENT` → HTTP 422, 03 §2.5/§3.3); admin cancel **başarılı** + alıcıya **ödeme iadesi** (`BUYER_REFUND` blockchain transfer CONFIRMED, net = `TotalAmount − gas fee`, 02 §4.6) + her iki tarafa bildirim.

## Etkilenen Modüller / Dosyalar

- `e2e/tests/cancellation.spec.ts` (YENİ) — 4 senaryo
- `e2e/src/api.ts` — `cancelTransaction`, `adminCancelTransaction`, `pollUntilRefundableCancel` helper'ları
- `e2e/src/db.ts` — `ensureAdmin` (idempotent admin User seed; AuditLog FK), `pollCancelledNoticeRecipients`, `pollRefundOfferAccepted`, `getBotEscrowCount`, `pollBuyerRefundConfirmed`; `seed`'e `adminId`/`adminSteamId`
- `e2e/package.json` — `test:cancel` script
- `.github/workflows/ci.yml` — `e2e-smoke` (advisory) job'a "Run API cancellation E2E (T108)" adımı

**Production kaynağı (backend/frontend/sidecar-*) değişmedi.**

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Satıcı iptali (ödeme öncesi) | ✓ | Test 1: `CANCELLED_SELLER` + item iade + alıcı bildirimi |
| 2 | Alıcı iptali (ödeme öncesi) | ✓ | Test 2: `CANCELLED_BUYER` + item iade + satıcı bildirimi |
| 3 | Admin iptali | ✓ | Test 3 (ödeme öncesi) + Test 4 (ödeme sonrası): `CANCELLED_ADMIN` + iade + iki taraf bildirimi |
| 4 | Her senaryoda doğru iade + bildirim | ✓ | İade: `RETURN_TO_SELLER` ACCEPTED + escrow count 0 (1-3) / `BUYER_REFUND` CONFIRMED (4); bildirim: `TRANSACTION_CANCELLED` doğru alıcı(lar)a; negatif: ödeme sonrası user-cancel 422 |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| E2E (Playwright, full docker stack) | ✓ **4 passed (5.6m)** | `npx playwright test cancellation` (`:5000`/`:5200`, db+redis+backend+fake healthy). 1: seller 46.6s · 2: buyer 1.0m · 3: admin 1.0m · 4: post-payment 2.7m. exit 0 |
| tsc / eslint / prettier (e2e) | ✓ | `tsc --noEmit` exit 0 · `eslint .` 0 · `prettier --check --end-of-line=auto` clean |
| CI YAML | ✓ | `js-yaml` parse OK; `e2e-smoke` adımı doğru yerde |

**Firsthand DB teyidi (test 4 son durum, full stack üzerinden sorgulandı):**
- `Transactions.Status = CANCELLED_ADMIN`, `IsOnHold = false`
- `BlockchainTransactions`: `BUYER_REFUND` / `Status=CONFIRMED` / `Amount=100` / `ToAddress=TJRyWwFs…EkEN` (alıcının iade adresi) → ödeme iadesi onaylandı
- `TradeOffers`: `TO_SELLER ACCEPTED` (escrow) + `RETURN_TO_SELLER ACCEPTED` (item iadesi)
- `Notifications`: `TRANSACTION_CANCELLED` × 2 (admin iptali her iki tarafa)
- `PlatformSteamBots.ActiveEscrowCount = 0` (iade sonrası escrow slotu serbest)

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** — bağımsız validator (ayrı chat, 2026-06-22, rapor görülmeden kendi verdict'i) |
| Bulgu sayısı | 0 bloke-edici (5 non-blocking precision note) |
| Düzeltme gerekli mi | Hayır |

### Bağımsız Doğrulama Sonucu — T108 (E2E: İptal Senaryoları)

**Verdict: ✓ PASS — AC 1–4 karşılandı, 0 bloke-edici bulgu.**

**Kapılar:**
- Adım -1 (working tree): temiz.
- Adım 0 (main CI startup): son 3 run success — `27954728455` / `27954728208` / `27946431943`.
- Adım 0b (repo memory drift): `.claude/memory/MEMORY.md` T108 satırı mevcut.
- Adım 8a (task branch CI): HEAD `9c044f3` run [`27965460095`](https://github.com/turkerurganci/Skinora/actions/runs/27965460095) success. **Kritik: `e2e-smoke` job'unun "Run API cancellation E2E (T108)" ADIMI conclusion=success** — `continue-on-error` yalnız job seviyesinde (ci.yml:621, gate-dışı/owner T107 kararı) ve **adım conclusion'ını maskelemiyor** → `npm run test:cancel` exit 0 = 4 Playwright iptal testi **gerçek migrated docker-compose stack'inde koştu ve geçti** (önceki `13049c9` run cancelled/superseded). Vacuous değil.

**Kabul kriterleri (validator):**

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Satıcı iptali (ödeme öncesi) | ✓ | Test 1 → `CANCELLED_SELLER` + `RETURN_TO_SELLER` ACCEPTED + escrow 0 + yalnız alıcı `TRANSACTION_CANCELLED` (03 §2.5) |
| 2 | Alıcı iptali (ödeme öncesi) | ✓ | Test 2 → `CANCELLED_BUYER` + item iade + yalnız satıcı bildirim (03 §3.3) |
| 3 | Admin iptali | ✓ | Test 3 (ödeme öncesi) + Test 4 (ödeme sonrası) → `CANCELLED_ADMIN` + iade + iki taraf bildirim (03 §8.7) |
| 4 | Her senaryoda doğru iade + bildirim | ✓ | Item iade: `RETURN_TO_SELLER` ACCEPTED + escrow 0 (1–3); ödeme iade: `BUYER_REFUND` CONFIRMED → alıcı iade adresi, net=`TotalAmount−gas` (4); negatif: ödeme-sonrası user-cancel **422** (`PAYMENT_ALREADY_SENT`) |

**Bağımsız backend-seam teyidi (validator-firsthand + 7-ajan adversarial workflow, refute-default — yapım raporu/test dosyası körlemesine kabul edilmedi, BACKEND production kaynağı okundu):**
- **A — user-cancel:** `POST /transactions/:id/cancel` (`TransactionsController.cs:287`), body `{reason}`, party=token-userId; response DTO `CancelTransactionResponse(Status, CancelledAt, ItemReturned, PaymentRefunded)` → JSON `status`/`itemReturned`/`paymentRefunded` testle birebir; SELLER→`CANCELLED_SELLER`, BUYER→`CANCELLED_BUYER`.
- **B — admin-cancel:** `POST /admin/transactions/:id/cancel`, `CANCEL_TRANSACTIONS` izni; `super_admin` rol claim'i `PermissionAuthorizationHandler` kısa-devresiyle (izin claim'i olmadan) karşılar; success path 200 + `CANCELLED_ADMIN`; ödeme-sonrası (PAYMENT_RECEIVED/TRADE_OFFER_SENT_TO_BUYER, ITEM_DELIVERED hariç) izinli.
- **C — 422 PAYMENT_ALREADY_SENT:** controller **yalnızca** `PaymentAlreadySent`→`UnprocessableEntity`(422) eşler; diğer iptal hataları 404/403/409/400 → 422 assertion'ı ayırt edici/anlamlı. Ödeme-sonrası `IsPostPaymentState` guard (`TransactionCancellationService.cs:142`) `ResolveTrigger`'dan önce çalışır → hem satıcı hem alıcı 422 alır.
- **D — bildirim fan-out:** `TransactionCancelledNotificationConsumer` SELLER→buyer / BUYER→seller / ADMIN→both; `Type=TRANSACTION_CANCELLED`, alıcı=`UserId`. İnisiyatör hiçbir zaman bildirilmiyor → testin `.not.toContain(initiator)` ayırt edici.
- **E — item iade bacağı:** kalıcı `TradeOffers.Direction='RETURN_TO_SELLER'` satırı webhook (`SteamWebhookHandler`) tarafından yazılır, accept'te `ActiveEscrowCount` **relative −1** (clamp 0) düşer; per-test re-seed (0→1 escrow→0 iade) → `toBe(0)` slot-release'i kanıtlar.
- **F — ödeme iadesi:** `PaymentRefundToBuyerConsumer` `BUYER_REFUND` BlockchainTransaction (net=`TotalAmount−gas`, 02 §4.6 "### 4.6 İade Politikası"), `ToAddress`=alıcı iade adresi; price=100/gas=2 → net≈98 dust-threshold üstü → satır gerçekten kuyruğa girer (block edilseydi poll null → `.not.toBeNull()` FAIL = vacuous değil).
- **G — AuditLog.ActorId FK:** Users(Id)'ye NO ACTION FK → `ensureAdmin()` seed zorunluluğu doğrulandı.

**Vacuousness denetimi:** Tüm poll helper'ları firsthand denetlendi — timeout'ta `false`/`-1`/`null`/boş-dizi döner (pozitif `.toContain`/`.toBeTruthy`/`.not.toBeNull` leg'i FAIL eder) veya throw eder (`pollStatus`/`pollUntilRefundableCancel`). `retries:0`, `workers:1`, `fullyParallel:false` → retry/paralel maskeleme yok. **Vacuous assertion yok.**

**Güvenlik:** `Jwt__Secret`/DB parolası açıkça test-fixture (`e2e-jwt-secret-do-not-use-in-prod-…`), yalnız e2e stack'e geçerli → prod auth-bypass yok; `super_admin` kısa-devresi gerçek backend özelliği (e2e secret ile tetikleniyor). **Sıfır production kaynak değişikliği** (`git diff main...HEAD -- backend frontend sidecar-* infra nginx` boş). Yeni dış bağımlılık yok.

**Non-blocking notlar (bloke etmez):**
- **N1** — CI step'i gerçek sinyal ama merge-gate'inde non-blocking (advisory job, `ci-gate.needs` dışı). Doğrulama sağlamlığı step'in gerçekten koşup success vermesine dayanır (verdi).
- **N2** — `getBotEscrowCount().toBe(0)` mutlak-0; backend relative −1 uygular → tek-item re-seed shape'ine bağlı (bu harness'te güvenli; eksik decrement'i yine de yakalar).
- **N3** — iade bacağında iki ayrı direction enum'u (dispatch `BotToSellerRefund` vs kalıcı `RETURN_TO_SELLER`); test kalıcı satıra assert eder = doğru seam.
- **N4** — Test-4 `BUYER_REFUND` erişilebilirliği net>dust-threshold'a bağlı (100−2=98 güvenli); block edilse bile assertion vacuous değil.
- **N5** — Senaryo-4 zamanlama yarışı (delivery-dispatch `* * * * *` ITEM_DELIVERED'a ilerletmeden admin-cancel yapılmalı); `pollUntilRefundableCancel` PAYMENT_RECEIVED'ı erken yakalar + TRADE_OFFER_SENT_TO_BUYER'ı da kabul eder → pratik yarış ~%1 (advisory). Yapım raporu da bildiriyor (over-claim yok).

**Yapım raporu karşılaştırması:** Tam uyumlu — 0 uyuşmazlık. Rapor kod gerçeklerini doğru aktarıyor, zamanlama yarışı + UI-kapsam-dışı sınırlamalarını dürüstçe açıklıyor, over-claim yok.

## Altyapı Değişiklikleri

- Migration: Yok
- Config/env değişikliği: Yok (mevcut `docker-compose.e2e.yml` + e2e env defaults kullanılır)
- Docker değişikliği: Yok (yeni image yok; mevcut backend+fake stack)

## Commit & PR

- Branch: `task/T108-e2e-cancellation`
- Commit: `13049c9` — T108: E2E — İptal senaryoları (cancellation E2E)
- PR: [#200](https://github.com/turkerurganci/Skinora/pull/200)
- CI: izleniyor (Claude — evrensel kural)

## Known Limitations / Follow-up

- **Senaryo 4 zamanlama:** Admin'in ödeme-sonrası iptali, delivery-dispatch job'ı (`* * * * *` per-minute) ITEM_DELIVERED'a ilerletmeden önce yapılmalıdır. Test, PAYMENT_RECEIVED'ı erken yakalayıp (≤~2s) hemen iptal eder; `pollUntilRefundableCancel` ayrıca TRADE_OFFER_SENT_TO_BUYER'ı da kabul eder (o da admin-cancel-ile-refundable). Artık ITEM_DELIVERED yarışı pratikte ihmal edilebilir (~%1; advisory job).
- **UI seviyesi kapsam dışı (owner kararı):** İptal akışları API+DB seviyesinde test edildi; UI cancel formu/akışı bu task'a dahil değil (T108 test beklentisi "E2E", UI zorunlu değil).

## Notlar

- **Dış varsayımlar:** Yeni dış bağımlılık yok. Reused: T107 e2e deps (`@playwright/test`, `mssql`, `jsonwebtoken`). Doğrulanan kod gerçekleri: user-cancel (`POST /transactions/:id/cancel`) + admin-cancel (`POST /admin/transactions/:id/cancel`, `CANCEL_TRANSACTIONS` izni super_admin JWT ile karşılanır — `PermissionAuthorizationHandler` super_admin'i kısa devre yapar) endpoint'leri; `TransactionCancelledNotificationConsumer` (SELLER→buyer, BUYER→seller, ADMIN→both); `ItemRefundDispatchConsumer` (T106a, `BOT_TO_SELLER_REFUND`); `PaymentRefundToBuyerConsumer` (WP2, `BUYER_REFUND`); fake sidecar `/api/trade-offers/send` her direction için self-drive eder + `/api/transfer/refund` `{txHash}` döner; AuditLog.ActorId FK → admin User seed zorunlu.
- **Working tree:** Adım -1 temiz.
- **Adım 0 main CI startup:** son 3 run success — `27954728455` / `27954728208` / `27946431943`.
