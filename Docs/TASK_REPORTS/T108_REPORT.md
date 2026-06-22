# T108 — E2E: İptal Senaryoları

**Faz:** F6 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-06-22

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
| Doğrulama durumu | Bağımsız validator bekliyor (ayrı chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- Migration: Yok
- Config/env değişikliği: Yok (mevcut `docker-compose.e2e.yml` + e2e env defaults kullanılır)
- Docker değişikliği: Yok (yeni image yok; mevcut backend+fake stack)

## Commit & PR

- Branch: `task/T108-e2e-cancellation`
- Commit: _(doldurulacak)_
- PR: _(doldurulacak)_
- CI: _(doldurulacak)_

## Known Limitations / Follow-up

- **Senaryo 4 zamanlama:** Admin'in ödeme-sonrası iptali, delivery-dispatch job'ı (`* * * * *` per-minute) ITEM_DELIVERED'a ilerletmeden önce yapılmalıdır. Test, PAYMENT_RECEIVED'ı erken yakalayıp (≤~2s) hemen iptal eder; `pollUntilRefundableCancel` ayrıca TRADE_OFFER_SENT_TO_BUYER'ı da kabul eder (o da admin-cancel-ile-refundable). Artık ITEM_DELIVERED yarışı pratikte ihmal edilebilir (~%1; advisory job).
- **UI seviyesi kapsam dışı (owner kararı):** İptal akışları API+DB seviyesinde test edildi; UI cancel formu/akışı bu task'a dahil değil (T108 test beklentisi "E2E", UI zorunlu değil).

## Notlar

- **Dış varsayımlar:** Yeni dış bağımlılık yok. Reused: T107 e2e deps (`@playwright/test`, `mssql`, `jsonwebtoken`). Doğrulanan kod gerçekleri: user-cancel (`POST /transactions/:id/cancel`) + admin-cancel (`POST /admin/transactions/:id/cancel`, `CANCEL_TRANSACTIONS` izni super_admin JWT ile karşılanır — `PermissionAuthorizationHandler` super_admin'i kısa devre yapar) endpoint'leri; `TransactionCancelledNotificationConsumer` (SELLER→buyer, BUYER→seller, ADMIN→both); `ItemRefundDispatchConsumer` (T106a, `BOT_TO_SELLER_REFUND`); `PaymentRefundToBuyerConsumer` (WP2, `BUYER_REFUND`); fake sidecar `/api/trade-offers/send` her direction için self-drive eder + `/api/transfer/refund` `{txHash}` döner; AuditLog.ActorId FK → admin User seed zorunlu.
- **Working tree:** Adım -1 temiz.
- **Adım 0 main CI startup:** son 3 run success — `27954728455` / `27954728208` / `27946431943`.
