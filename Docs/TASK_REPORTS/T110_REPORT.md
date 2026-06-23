# T110 — E2E: Ödeme Edge Case'leri

**Faz:** F6 | **Durum:** ✓ Tamamlandı (bağımsız validator PASS) | **Tarih:** 2026-06-22 (validate 2026-06-23)

---

## Yapılan İşler

03 §5 ödeme edge-case'leri için uçtan uca (E2E) kapsam — T107/T108/T109 ile aynı seam (Playwright + `docker-compose.e2e.yml` + `sidecar-fake`). Backend tüm dalları (`AmountValidationService`) zaten içeriyordu; bu task test kapsamı + e2e-only fake lever'ları ekler ve §5.4'teki tek eksik production boşluğunu kapatır.

- **Backend (tek production değişikliği):** `LatePaymentRefundRequestedNotificationConsumer` — §5.4 step 5 alıcı bildirimi (`NotificationType.LATE_PAYMENT_REFUNDED`). `LatePaymentRefundRequestedEvent` `AmountValidationService` tarafından yayınlanıyordu ama Notifications modülünde hiçbir `INotificationHandler<>` tüketmiyordu; enum değeri + 4-dil `NotificationTemplates.*.resx` template'leri (`LATE_PAYMENT_REFUNDED_Title/_Body`) + `EmailCategoryMap` eşlemesi **zaten mevcuttu** — yalnızca consumer sınıfı unutulmuştu (3 kardeş consumer'la asimetri). Owner kararı (AskUserQuestion 2026-06-22): consumer'ı ekle. + unit test.
- **sidecar-fake (`src/routes/control.ts`, e2e-only artifact):** 3 yeni webhook lever'ı (`/__e2e/payment/wrong-token` → `payment.wrong_token`, `/__e2e/payment/spam-token` → `payment.spam_token`, `/__e2e/payment/late-detected` → `payment.late_detected`) + `/__e2e/payment/{detect,confirm,pay}`'e `eventIndex` override (§5.5 ikinci transfer için ayrık `(txHash, eventIndex)`). `amount` override zaten vardı. USDT/USDC TRC-20 contract mirror'ı (backend `KnownStablecoinContracts` + gerçek sidecar `STABLECOIN_CONTRACTS_*` ile birebir) + allowlist-dışı `UNSUPPORTED_CONTRACT` (spam).
- **e2e helper'ları (`src/api.ts`, `src/db.ts`):** `payWrongTokenViaFake` / `paySpamTokenViaFake` / `payLateViaFake` + `payViaFake`'e `{amount, eventIndex}` opt + `pollBlockchainTxConfirmed(txId, type)` (tip-bazlı refund/audit row poller, allow-list + bound param) + `pollNotificationRecipients(type, expected)` + `fakeBuyerWallet` sabiti (`fakeTronAddress(999_001)` = `TGDcTRVZVvKBUE7h5fRCVUjRGj6K52AFWg`; refund `ToAddress` = ödeme kaynağı, **08 §562** — "underpayment, overpayment farkı, wrong token → kaynak adres").
- **`e2e/tests/payment-edge-cases.spec.ts`:** 6 test.

## Etkilenen Modüller / Dosyalar

- `backend/src/Modules/Skinora.Notifications/Application/EventHandlers/LatePaymentRefundRequestedNotificationConsumer.cs` (yeni)
- `backend/tests/Skinora.Notifications.Tests/Unit/LatePaymentRefundRequestedNotificationConsumerTests.cs` (yeni)
- `sidecar-fake/src/routes/control.ts` (değişti — 3 lever + eventIndex)
- `e2e/src/api.ts` (değişti — 3 helper + payViaFake opt)
- `e2e/src/db.ts` (değişti — pollBlockchainTxConfirmed + pollNotificationRecipients + fakeBuyerWallet)
- `e2e/tests/payment-edge-cases.spec.ts` (yeni — 6 test)
- `e2e/package.json` (değişti — `test:payment` script)
- `.github/workflows/ci.yml` (değişti — advisory `e2e-smoke` job'a "Run API payment edge cases E2E (T110)" adımı; T108/T109 deseniyle birebir)

## Kabul Kriterleri Kontrolü

| # | Kriter (03 §5) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Eksik tutar → iade | ✓ | §5.1 testi: `payViaFake(amount:10)` → tx `ITEM_ESCROWED` kalır (no-advance) + `INCORRECT_AMOUNT_REFUND` (Amount=10, ToAddress=fakeBuyerWallet) CONFIRMED + `INSUFFICIENT_PAYMENT` bildirimi (buyer) |
| 2 | Fazla tutar → kabul + fazla iade | ✓ | §5.2 testi: `getExpectedAmount(txId)` (≈102) DB'den okunur, `payViaFake(amount: expected+20)` → tx post-payment state (PAYMENT_RECEIVED+) + `EXCESS_REFUND` (Amount≈20 = sadece fazla, `received − ExpectedAmount`, fee hardcode değil) CONFIRMED + `OVERPAYMENT_REFUNDED` bildirimi |
| 3 | Yanlış token → iade | ✓ | §5.3 testi: `payWrongTokenViaFake(USDC, 10)` → tx `ITEM_ESCROWED` kalır + `WRONG_TOKEN_REFUND` (Amount=10) CONFIRMED + `WRONG_TOKEN_REFUND` bildirimi |
| 4 | Gecikmeli ödeme → iade | ✓ | §5.4 testi: payment-timeout → `CANCELLED_TIMEOUT`+`POST_CANCEL_24H` → `payLateViaFake(10)` → `LATE_PAYMENT_REFUND` (Amount=10) CONFIRMED + tx terminal kalır + `LATE_PAYMENT_REFUNDED` bildirimi (yeni consumer) |
| 5 | Çoklu ödeme → ilk kabul, sonraki iade | ✓ | §5.5 testi: ilk tam ödeme → tx ilerler (post-payment) → 2. ödeme (`eventIndex:1`, 50) → `EXCESS_REFUND` (Amount=50 = tam) CONFIRMED + `OVERPAYMENT_REFUNDED` bildirimi |
| Ek | §5.3a desteklenmeyen token | ✓ (kısmi — bkz. Known limitations) | §5.3a testi: `paySpamTokenViaFake` → `SPAM_TOKEN_INCOMING` audit row CONFIRMED + tx `ITEM_ESCROWED` kalır + escrow 1 + auto-refund yok |

**Doğrulama kontrol listesi:** `[x] 03 §5 tüm edge case'ler çalışıyor mu?` — 6 testle (§5.1–§5.5 + §5.3a) kaplandı.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend Unit (Notifications) | ✓ 168/168 | `dotnet test Skinora.Notifications.Tests` (yeni consumer testi 2 dahil) |
| Backend build (solution) | ✓ | `dotnet build Skinora.sln` exit 0 |
| sidecar-fake | ✓ tsc + lint + format + vitest 12/12 | `npm run build/lint/format:check/test` |
| e2e statik | ✓ tsc --noEmit + lint + format | `npx tsc --noEmit` + `npm run lint` + prettier |
| E2E senaryoları (6) | ✓ 6/6 CI'da geçti | CI advisory `e2e-smoke` job'unda "Run API payment edge cases E2E (T110)" adımı (`npm run test:payment`) — run `27986154333` (HEAD `d97e27e`) step **conclusion=success**, 6/6 test migrated docker-compose stack'inde geçti (5 passed (16.0m) + §5.2 fix sonrası 6/6). happy-path/T108/T109 step'leri de success (regresyon yok) |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **Bağımsız validator PASS** (ayrı chat, 2026-06-23, rapor görülmeden) |
| Bulgu sayısı | 4 bloke-edici-olmayan (K1 cross-doc refund-adresi çelişkisi · K2 atıf 02§4.6→08§562 [düzeltildi] · K3 AC#2 stale satır [düzeltildi] · K4 §5.3a bildirim/admin-review ertelendi) |
| Düzeltme gerekli mi | Hayır (bloke-edici yok). K2/K3 validator-fix ile kapatıldı; K1 owner-onaylı doc follow-up; K4 owner-onaylı kapsam dışı |

**Validator kanıt özeti (rapor görülmeden bağımsız):**
- **Kapılar:** Adım -1 temiz · Adım 0 main son-3 success (`27977804938`/`27977804900`/`27970475909`) · Adım 0b memory mevcut · Adım 8a task CI HEAD `7496099` run `27988125483` success + e2e **step-level** kanıt run `27986154333`'te "Run API payment edge cases E2E (T110)" adımı `conclusion=success` (continue-on-error maskelemiyor); HEAD↔test-edilen-commit farkı yalnız docs.
- **Firsthand E2E:** lokal tam docker stack (db/redis/fake-sidecar/backend healthy) `npm run test:payment` → **6/6 passed (15.2m)** — §5.1/§5.2/§5.3/§5.3a/§5.4/§5.5 hepsi geçti.
- **Seam:** 5 AC backend `AmountValidationService` dallarına birebir (Underpayment→INCORRECT_AMOUNT_REFUND no-advance / Overpayment→state-advance + EXCESS_REFUND=received−ExpectedAmount / WrongToken→WRONG_TOKEN_REFUND no-advance / Late→LATE_PAYMENT_REFUND + event `:261`→**yeni consumer**→LATE_PAYMENT_REFUNDED / Status≠ITEM_ESCROWED→multi-payment tam iade); idempotency `(TxHash,EventIndex)` UQ. Testler fake değil **gerçek backend** ürettiği DB satırlarını (CONFIRMED blockchain tx + notification + status) assert eder.
- **Tek prod değişiklik** (`LatePaymentRefundRequestedNotificationConsumer`): MediatR auto-register (`OutboxModule` scan), unit test **2/2** firsthand, resx 4 dil, event publish'li → §5.4 e2e end-to-end fire'ı kanıtlar (pre-T110 bu bildirim hiç üretilmiyordu).
- **Statik/güvenlik:** e2e tsc0/eslint0 + T110 dosyaları prettier-clean · sidecar tsc0/lint0/vitest 12/12 · 0 yeni bağımlılık · secret'lar e2e-only fixture · `/__e2e/*` yalnız sidecar-fake (compose-internal) · BYPASS_LOG advisory job timeout (35→70), tüm blocking gate yeşildi.
- **10-ajan adversarial workflow:** 4/4 cross CLEAN, 5/6 seam CONFIRMED_OK; 1 ajanın §5.3 "S2"si validator tarafından 08 §562 ile çürütüldü → K1 (pre-existing cross-doc, T110 kusuru değil).

## Altyapı Değişiklikleri

- Migration: **Yok**
- Enum/şema değişikliği: **Yok** (`LATE_PAYMENT_REFUNDED` zaten mevcuttu → `EnumTests` / `AuditLogCategoryMap` parity etkilenmez)
- Config/env değişikliği: **Yok**
- Docker değişikliği: **Yok**
- Yeni dış bağımlılık: **Yok**

## Dış Varsayımlar (task.md Adım 4)

- **Backend edge-case wiring tam mı?** — Evet. `AmountValidationService` 5 dalı da implement ediyor; §5.1/§5.2/§5.3/§5.5 backend unit+integration testleriyle kaplı (`AmountValidationServiceTests`, `BlockchainWebhookEndpointTests`). §5.4 backend yolu tam ama unit/integration testi yoktu (E2E-only). Keşif workflow'u (7 ajan, file:line) doğruladı.
- **Webhook route'ları + payload şekilleri** — `BlockchainWebhooksController` + `BlockchainWebhookPayloads.cs`'e karşı birebir doğrulandı (`wrong-token`, `late-payment-detected`, `spam-token` route'ları; envelope `{event,timestamp,data}` + HMAC `X-Signature/X-Timestamp/X-Nonce`).
- **Refund eşiği** — `RefundDecisionService`: net = received − gas (2.0) ≥ gas × `MinRefundThresholdRatio` (2.0) = 4.0. Test tutarları (10/110→10/10/10/50) net ≥ 8 ile iadeyi PROCEED ettirir (blok değil).
- **Fake auto-confirm** — fake blockchain sidecar outgoing transfer'leri otomatik CONFIRMED yapar (`/api/transfer/refund` + `/api/transfer/status`), ekstra kontrol çağrısı gerekmez (mevcut BUYER_REFUND yoluyla aynı).

## Commit & PR

- Branch: `task/T110-e2e-payment-edge-cases`
- Commit: `4db9bf7` (kod) → `d97e27e` (final, §5.2 fix dahil)
- PR: #202
- CI: ✓ PASS — run `27986154333` (HEAD `d97e27e`): CI Gate (blocking) success + advisory `e2e-smoke` job success (T110 step 6/6 success)

## Known Limitations / Follow-up

- **§5.3a desteklenmeyen-token bildirim + admin-review akışı backend'de bağlı değil** (owner-onaylı kapsam dışı). Backend yalnızca `SPAM_TOKEN_INCOMING` audit row (terminal CONFIRMED) yazar; §5.3a step 7 (alıcı bildirimi "Desteklenmeyen varlık tespit edildi…") ve step 6/8 (admin review + manuel iade) için ne `NotificationType` enum değeri ne consumer ne admin-review pipeline mevcut. §5.3a testi yalnızca audit row + state-değişmezliği + auto-refund-yok doğrular. Ayrı follow-up task gerektirir.
- **§5.5 çoklu ödeme ile §5.2 fazla ödeme aynı `OVERPAYMENT_REFUNDED` bildirim tipini** üretir (`BuyerPaymentExcessRefundedEvent.IsMultiPayment` consumer'da ayrıştırılmıyor). E2E bunları refund Amount (fazla vs tam) ile ayırır; bildirim tipiyle değil. Mevcut backend davranışı (by-design).
- E2E senaryoları docker yığını gerektirdiğinden yapım sırasında lokal koşulmadı; CI advisory `e2e-smoke` job'unda gözlendi (T107–T109 ile aynı kalıp). **Validator lokal tam docker stack'te firsthand koştu → 6/6 (15.2m).**
- **(K1, validator — pre-existing cross-doc) Refund hedef adresi doküman çelişkisi:** Edge-case iadeleri (underpayment/overpayment/wrong-token/late) ödeme **kaynak** adresine gider — **08 §562** açıkça bunu mandate eder ("…her zaman gönderim yapan kaynak adrese gönderilir … standart blockchain iade pratiğidir"). Ancak **02 §4.4 (s.108) / 02 §4.6 (s.127) / 06 §736** alıcının **belirlediği iade adresini** söyler. Üretim kodu (`AmountValidationService.QueueRefundIntent`, T72/T73) ve T110 testi 08 §562'yi takip eder. T110 yalnızca spec-yetkili davranışı test eder (T110 kusuru değil). **Follow-up:** 02/06'yı 08 §562 ile uyumla; üretim kod yorumu (`QueueRefundIntent` "02 §4.6") da 08 §562'ye güncellensin (pre-existing, T110 diff'i dışında).

## Notlar

- **Working tree (Adım -1):** Oturum başında temiz.
- **Main CI (Adım 0):** Son 3 run `success` (`27977804938` T109 #201 / `27977804900` / `27970475909` T108 #200).
- **Dış varsayım keşfi:** 7-ajan paralel keşif workflow'u (webhook route'ları, payload şekilleri, notification type'ları, refund eşiği, fake mekaniği, §5.4 setup, mevcut backend kapsamı) — tümü file:line kanıtlı.
- **Refund `ToAddress` ayrımı:** Edge-case refund'ları ödeme **kaynağına** (buyer on-chain wallet = `fakeBuyerWallet`) gider — item-timeout `BUYER_REFUND`'ün kullandığı trade-tarafı `seed.buyerRefundAddress`'ten farklı. Operatif kaynak **08 §562** (underpayment/overpayment/wrong-token → kaynak adres). **Doküman çelişkisi (validator K1, pre-existing):** 02 §4.4 (s.108) / 02 §4.6 (s.127) / 06 §736 alıcının **belirlediği iade adresini** söyler — 08 §562 (+ üretim kodu `QueueRefundIntent` ve T110 testi) ise kaynak adresi. İmplementasyon 08 §562'yi takip eder; 02/06 ile 08 uyumlanmalı (ayrı doc follow-up).
- **§5.2 expected-amount düzeltmesi (2. CI run bulgusu):** Timeout fix'li run (`27983878586`) e2e-smoke'u tamamladı → **5/6 geçti, §5.2 FAIL** (`EXCESS_REFUND` Amount beklenen 10, gelen 8). Kök neden backend hatası **değil**: buyer'ın ödeyeceği `PaymentAddress.ExpectedAmount` listing price (100) değil **price + buyer komisyonu (≈102)** (02 §4.6; T109 "100=102−2gas" notuyla tutarlı) → excess = 110 − 102 = 8 (refund row gross excess saklar, gas broadcast'ta düşer). Diğer 4 refund testi `received`'ı (kontrolümde) kullandığından geçti; yalnız §5.2 expected'a bağlıydı. **Fix:** §5.2 artık `getExpectedAmount(txId)`'yi DB'den okuyup `excess = sent − expected` assert ediyor (fee'yi hardcode etmeden; 20 birim overpay marjı eşiği rahat aşar). Yeni `getExpectedAmount` helper'ı [`e2e/src/db.ts`].
- **CI e2e-smoke timeout (ilk run gözlemi):** İlk CI run'ında (`27981428925`, HEAD `1e5f544`) tüm **blocking job'lar success** (Lint/Build/Unit/Integration/Contract/Migration/Docker/CI Gate) ama advisory `e2e-smoke` job'u T110 step'i sırada iken `timeout-minutes: 35`'e takıldı → step `cancelled` → run conclusion `cancelled` (continue-on-error failure'ı maskeler ama timeout-cancellation'ı maskelemez). happy-path + T108 + T109 + setup tek başına ~35 dk dolduruyordu. **Fix:** `timeout-minutes` 35→70 (T108/T109 ile aynı tek-job deseni; transfer/escrow cron'ları kod-sabiti `* * * * *`, env-config değil → hızlandırılamıyor). **Scaling notu (follow-up):** bu tek ardışık advisory job T111–T114 ile büyümeye devam edecek — paralel job'lara (veya matrix'e) bölünmeli. ci.yml'ye not düşüldü.
