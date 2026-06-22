# T109 — E2E: Timeout Senaryoları

**Faz:** F6 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-06-22

---

## Yapılan İşler

T107'nin kurduğu E2E harness'ı (Playwright + `docker-compose.e2e.yml` + fake sidecar) üzerine **timeout senaryolarının API-düzeyi uçtan uca testleri** eklendi. Backend timeout yolu zaten tümüyle wire-li (`DeadlineScannerJob` self-rescheduling Hangfire job — 05 §4.4, `TimeoutSchedulerStartupHook`'la startup'ta primed; `TimeoutSideEffectPublisher` faz-bazlı iade/late-payment event'leri; `TransactionTimedOutNotificationConsumer` çift-taraf bildirim; T75 `PostCancelMonitorStarter` post-cancel monitor stamp). Bu task **test kapsamı + iki e2e-only kaldıraç** ekler — **sıfır production kaynak değişikliği**.

İki e2e kaldıracı:
1. **Deadline backdate (harness):** e2e stack'teki tüm timeout'lar 60 dk olduğundan gerçek-saat beklemesi imkânsız. Harness ilgili faz deadline kolonunu (`AcceptDeadline` / `TradeOfferToSellerDeadline` / `PaymentDeadline` / `TradeOfferToBuyerDeadline`) DB'de geçmişe çeker ve **production scanner'ın** timeout'u tetiklemesini bekler — timeout yolunun kendisi mock'lanmaz.
2. **Trade auto-accept suppression (fake sidecar):** Fake normalde her trade offer'ı `sent → accepted` self-drive eder. §4.2/§4.4 için yön-bazlı (`SELLER_TO_BOT` / `BOT_TO_BUYER`) auto-accept bastırma kontrol ucu (`/__e2e/trade/suppress-accept` + `/__e2e/trade/reset`) eklendi → işlem `TRADE_OFFER_SENT_TO_*` durumunda asılı kalır, scanner onu timeout'a uğratır.

Yeni `e2e/tests/timeout.spec.ts` 4 test (03 §4.1–§4.4):

1. **Kabul timeout'u (§4.1)** — CREATED, alıcı kabul etmez; `AcceptDeadline` backdate → scanner → `CANCELLED_TIMEOUT`. Item hiç emanet edilmedi (escrow count 0, iade yok); her iki taraf `TRANSACTION_CANCELLED`.
2. **Satıcı trade-offer timeout'u (§4.2)** — `SELLER_TO_BOT` suppress → `TRADE_OFFER_SENT_TO_SELLER`'da asılı; `TradeOfferToSellerDeadline` backdate → `CANCELLED_TIMEOUT`. Item platforma ulaşmadı (escrow count 0); çift-taraf bildirim.
3. **Ödeme timeout'u (§4.3)** — escrow leg self-drive → ITEM_ESCROWED (count 1), alıcı ödemez; `PaymentDeadline` backdate → `CANCELLED_TIMEOUT`. Item satıcıya iade (`RETURN_TO_SELLER` ACCEPTED + count 0) + **gecikmeli ödeme izleme başlar** (`PaymentAddress.MonitoringStatus → POST_CANCEL_24H`, 08 §3.4); çift-taraf bildirim.
4. **Teslim timeout'u (§4.4)** — `BOT_TO_BUYER` suppress; ITEM_ESCROWED → ödeme → `TRADE_OFFER_SENT_TO_BUYER`'da asılı; `TradeOfferToBuyerDeadline` backdate → `CANCELLED_TIMEOUT`. Item satıcıya iade (`RETURN_TO_SELLER` ACCEPTED + count 0) + ödeme alıcıya iade (`BUYER_REFUND` CONFIRMED, net = `TotalAmount − gas`, alıcı iade adresi); çift-taraf bildirim.

## Etkilenen Modüller / Dosyalar

- `e2e/tests/timeout.spec.ts` (YENİ) — 4 senaryo
- `e2e/src/db.ts` — `backdateDeadline` (allow-list kolon + bound int offset), `pollPostCancelMonitoring` (`MonitoringStatus`/`MonitoringExpiresAt` okuması), `DeadlineColumn`/`MonitoringRow` tipleri
- `e2e/src/api.ts` — `suppressTradeAccept`, `resetTradeControl`; ortak `fakePost` helper (mevcut `payViaFake` da ona refactor edildi)
- `e2e/package.json` — `test:timeout` script
- `sidecar-fake/src/tradeControl.ts` (YENİ) — in-process yön-bazlı suppression state (Set)
- `sidecar-fake/src/tradeControl.test.ts` (YENİ) — 5 vitest birim testi
- `sidecar-fake/src/routes/control.ts` — `/__e2e/trade/suppress-accept` + `/__e2e/trade/reset` kontrol ucu
- `sidecar-fake/src/routes/steam.ts` — `/api/trade-offers/send` self-drive'a suppression guard (suppress ise yalnız `trade_offer.sent`)
- `docker-compose.e2e.yml` — `Timeouts__DeadlineScannerIntervalSeconds=5` (scanner sweep'i hızlandır)
- `.github/workflows/ci.yml` — `e2e-smoke` (advisory) job'a "Run API timeout E2E (T109)" adımı

**Production kaynağı (backend/frontend/gerçek sidecar-steam/sidecar-blockchain) değişmedi.** Fake sidecar bir test double'ıdır; eklenen kontrol ucu yalnız e2e harness tarafından çağrılır.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Kabul timeout, trade offer timeout, ödeme timeout, teslim timeout | ✓ | 4 test: §4.1 CREATED · §4.2 TRADE_OFFER_SENT_TO_SELLER · §4.3 ITEM_ESCROWED · §4.4 TRADE_OFFER_SENT_TO_BUYER → hepsi `CANCELLED_TIMEOUT` |
| 2 | Her senaryoda doğru iade tetikleme + bildirim | ✓ | İade: §4.1/§4.2 iade yok (item platformda değil, count 0); §4.3 `RETURN_TO_SELLER` ACCEPTED + count 0; §4.4 `RETURN_TO_SELLER` ACCEPTED + `BUYER_REFUND` CONFIRMED. Bildirim: 4 senaryoda da `TRANSACTION_CANCELLED` her iki tarafa |
| 3 | Gecikmeli ödeme izleme başlatma (ödeme timeout sonrası) | ✓ | §4.3: `PaymentAddress.MonitoringStatus = POST_CANCEL_24H` + `MonitoringExpiresAt` set (08 §3.4, 24h window) |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| E2E (Playwright, full docker stack) | ✓ **4 passed (6.2m)** | `npx playwright test timeout` (`:5000`/`:5200`, db+redis+backend+fake healthy, migrated). §4.1 accept 26.5s · §4.2 seller-offer 1.2m · §4.3 payment 1.1m · §4.4 delivery 3.4m. exit 0 |
| sidecar-fake vitest | ✓ **12 passed** (3 dosya) | `npx vitest run` — yeni `tradeControl.test.ts` 5 test + mevcut hmac/ids 7 |
| tsc / eslint / prettier (e2e + fake) | ✓ | her iki paket: `tsc --noEmit` exit 0 · `eslint` 0 · `prettier --check` (içerik LF-clean, `--end-of-line auto`) |

**Firsthand DB teyidi (son test §4.4 final durumu, full stack üzerinden sorgulandı — re-seed nedeniyle yalnız son senaryonun satırları kalır):**
- `Transactions.Status = CANCELLED_TIMEOUT`
- `TradeOffers`: `TO_SELLER ACCEPTED` (escrow) + **`TO_BUYER SENT`** (teslim offer **kabul edilmedi** — `BOT_TO_BUYER` suppression `TRADE_OFFER_SENT_TO_BUYER`'da tuttu, timeout'u mümkün kıldı) + `RETURN_TO_SELLER ACCEPTED` (item satıcıya iade)
- `BlockchainTransactions`: `BUYER_REFUND / CONFIRMED / ToAddress=TJRyWwFs…EkEN` (alıcı iade adresi) → ödeme iadesi onaylandı
- `PaymentAddresses.MonitoringStatus = POST_CANCEL_24H` (post-cancel monitor stamp)
- `PlatformSteamBots.ActiveEscrowCount = 0` (iade sonrası escrow slotu serbest)
- Backend logları: §4.1/§4.2/§4.3/§4.4 dördü de `CANCELLED_TIMEOUT`; §4.3 `PostCancelMonitorStarter: stamped → POST_CANCEL_24H`; fake logları `SELLER_TO_BOT` + `BOT_TO_BUYER` "holding at SENT" suppression teyidi

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validator chat'i bekleniyor |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- Migration: Yok
- Config/env değişikliği: `docker-compose.e2e.yml` backend env'ine `Timeouts__DeadlineScannerIntervalSeconds=5` (yalnız e2e stack; `TimeoutSchedulingOptions` altyapı knob'u, iş parametresi değil)
- Docker değişikliği: Yok (yeni image yok; mevcut backend+fake stack yeniden build)

## Commit & PR

- Branch: `task/T109-e2e-timeout`
- Commit: _(commit sonrası doldurulacak)_
- PR: _(PR sonrası doldurulacak)_
- CI: izleniyor (Claude — evrensel kural)

## Known Limitations / Follow-up

- **§4.5 "Timeout yaklaşıyor uyarısı" kapsam dışı (owner kararı):** AC 4 timeout + iade + bildirim + gecikmeli ödeme izleme sayar; `TIMEOUT_WARNING` (eşik-bazlı, `WarningDispatcher`) AC'de yok ve mevcut unit/integration testlerine bırakıldı.
- **UI seviyesi kapsam dışı:** Timeout akışları API+DB seviyesinde test edildi; UI state-geçiş gösterimi bu task'a dahil değil (T109 test beklentisi "E2E (kısa timeout ile)").
- **§4.2 post-cancel monitor stamp:** TRADE_OFFER_SENT_TO_SELLER'da PaymentAddress allocate edilmiş olduğundan scanner orada da `POST_CANCEL_24H` stamp'ler (defansif); §4.2 testi bunu assert etmez (iade-yok fazı), yalnız status + bildirim + escrow-0 kontrol edilir.

## Notlar

- **Dış varsayımlar:** Yeni dış bağımlılık yok. Reused: T107 e2e deps (`@playwright/test`, `mssql`, `jsonwebtoken`) + sidecar-fake (express, vitest). Doğrulanan kod gerçekleri: `DeadlineScannerJob` 4 faz deadline'ını tarar (CREATED/TRADE_OFFER_SENT_TO_SELLER/ITEM_ESCROWED/TRADE_OFFER_SENT_TO_BUYER), `!IsOnHold && TimeoutFrozenAt==null` guard; `TimeoutSchedulerStartupHook` scanner zincirini startup'ta primed eder; `TimeoutSideEffectPublisher` Payment→item-iade+late-payment-monitor, Delivery→item-iade+buyer-refund, Accept/TradeOfferToSeller→yalnız bildirim; tüm fazlar `TRANSACTION_CANCELLED` (seller + registered buyer); escrow `ActiveEscrowCount` +1 yalnız SELLER_TO_BOT *accept*'te (`AcceptEscrowAsync`); dispatch direction sabitleri `SELLER_TO_BOT`/`BOT_TO_BUYER`/`BOT_TO_SELLER_REFUND` (`ITradeOfferDispatchClient`).
- **Working tree:** Adım -1 temiz (T108 merge ff sonrası).
- **Adım 0 main CI startup:** son 3 run success — `27970475901` / `27970475909` (T108 #200) / `27954728455` (T107 #198).
