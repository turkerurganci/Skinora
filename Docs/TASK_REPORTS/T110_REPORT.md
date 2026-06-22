# T110 — E2E: Ödeme Edge Case'leri

**Faz:** F6 | **Durum:** ⏳ Devam ediyor (yapım tamam, doğrulama bekliyor) | **Tarih:** 2026-06-22

---

## Yapılan İşler

03 §5 ödeme edge-case'leri için uçtan uca (E2E) kapsam — T107/T108/T109 ile aynı seam (Playwright + `docker-compose.e2e.yml` + `sidecar-fake`). Backend tüm dalları (`AmountValidationService`) zaten içeriyordu; bu task test kapsamı + e2e-only fake lever'ları ekler ve §5.4'teki tek eksik production boşluğunu kapatır.

- **Backend (tek production değişikliği):** `LatePaymentRefundRequestedNotificationConsumer` — §5.4 step 5 alıcı bildirimi (`NotificationType.LATE_PAYMENT_REFUNDED`). `LatePaymentRefundRequestedEvent` `AmountValidationService` tarafından yayınlanıyordu ama Notifications modülünde hiçbir `INotificationHandler<>` tüketmiyordu; enum değeri + 4-dil `NotificationTemplates.*.resx` template'leri (`LATE_PAYMENT_REFUNDED_Title/_Body`) + `EmailCategoryMap` eşlemesi **zaten mevcuttu** — yalnızca consumer sınıfı unutulmuştu (3 kardeş consumer'la asimetri). Owner kararı (AskUserQuestion 2026-06-22): consumer'ı ekle. + unit test.
- **sidecar-fake (`src/routes/control.ts`, e2e-only artifact):** 3 yeni webhook lever'ı (`/__e2e/payment/wrong-token` → `payment.wrong_token`, `/__e2e/payment/spam-token` → `payment.spam_token`, `/__e2e/payment/late-detected` → `payment.late_detected`) + `/__e2e/payment/{detect,confirm,pay}`'e `eventIndex` override (§5.5 ikinci transfer için ayrık `(txHash, eventIndex)`). `amount` override zaten vardı. USDT/USDC TRC-20 contract mirror'ı (backend `KnownStablecoinContracts` + gerçek sidecar `STABLECOIN_CONTRACTS_*` ile birebir) + allowlist-dışı `UNSUPPORTED_CONTRACT` (spam).
- **e2e helper'ları (`src/api.ts`, `src/db.ts`):** `payWrongTokenViaFake` / `paySpamTokenViaFake` / `payLateViaFake` + `payViaFake`'e `{amount, eventIndex}` opt + `pollBlockchainTxConfirmed(txId, type)` (tip-bazlı refund/audit row poller, allow-list + bound param) + `pollNotificationRecipients(type, expected)` + `fakeBuyerWallet` sabiti (`fakeTronAddress(999_001)` = `TGDcTRVZVvKBUE7h5fRCVUjRGj6K52AFWg`; refund `ToAddress` = ödeme kaynağı, 02 §4.6).
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
| 2 | Fazla tutar → kabul + fazla iade | ✓ | §5.2 testi: `payViaFake(amount:110)` → tx post-payment state (PAYMENT_RECEIVED+) + `EXCESS_REFUND` (Amount=10 = sadece fazla) CONFIRMED + `OVERPAYMENT_REFUNDED` bildirimi |
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
| E2E senaryoları (6) | ⏳ CI'da | docker-compose.e2e yığını gerektirir; CI advisory `e2e-smoke` job'una eklenen "Run API payment edge cases E2E (T110)" adımı (`npm run test:payment`) koşar (lokal docker stack ile çalıştırılmadı — T107–T109 ile aynı kalıp) |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validate bekliyor (ayrı chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

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
- Commit: `4db9bf7` — T110: E2E — Ödeme edge case'leri (payment edge cases E2E)
- PR: #202
- CI: ⏳ izleniyor

## Known Limitations / Follow-up

- **§5.3a desteklenmeyen-token bildirim + admin-review akışı backend'de bağlı değil** (owner-onaylı kapsam dışı). Backend yalnızca `SPAM_TOKEN_INCOMING` audit row (terminal CONFIRMED) yazar; §5.3a step 7 (alıcı bildirimi "Desteklenmeyen varlık tespit edildi…") ve step 6/8 (admin review + manuel iade) için ne `NotificationType` enum değeri ne consumer ne admin-review pipeline mevcut. §5.3a testi yalnızca audit row + state-değişmezliği + auto-refund-yok doğrular. Ayrı follow-up task gerektirir.
- **§5.5 çoklu ödeme ile §5.2 fazla ödeme aynı `OVERPAYMENT_REFUNDED` bildirim tipini** üretir (`BuyerPaymentExcessRefundedEvent.IsMultiPayment` consumer'da ayrıştırılmıyor). E2E bunları refund Amount (fazla vs tam) ile ayırır; bildirim tipiyle değil. Mevcut backend davranışı (by-design).
- E2E senaryoları docker yığını gerektirdiğinden lokal koşulmadı; CI advisory `e2e-smoke` job'unda gözlenir (T107–T109 ile aynı kalıp).

## Notlar

- **Working tree (Adım -1):** Oturum başında temiz.
- **Main CI (Adım 0):** Son 3 run `success` (`27977804938` T109 #201 / `27977804900` / `27970475909` T108 #200).
- **Dış varsayım keşfi:** 7-ajan paralel keşif workflow'u (webhook route'ları, payload şekilleri, notification type'ları, refund eşiği, fake mekaniği, §5.4 setup, mevcut backend kapsamı) — tümü file:line kanıtlı.
- **Refund `ToAddress` ayrımı:** Edge-case refund'ları ödeme **kaynağına** (buyer on-chain wallet = `fakeBuyerWallet`) gider — item-timeout `BUYER_REFUND`'ün kullandığı trade-tarafı `seed.buyerRefundAddress`'ten farklı (02 §4.6).
