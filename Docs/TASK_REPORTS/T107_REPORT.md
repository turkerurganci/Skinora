# T107 — E2E: Happy path (tam escrow akışı)

**Faz:** F6 | **Durum:** ⏳ Devam ediyor (PR-1/3 — altyapı) | **Tarih:** 2026-06-21

---

## Bağlam

- **Bağımlılık:** F5 ✓, pre-F6 WP1–WP19 ✓ (WP19 PR #195 → main `5b9570c`, 2026-06-21 14:51 UTC; post-merge CI + Docker Publish ✓ — run `27907999068` / `27907999071`).
- **Yaklaşım (owner kararı, önceki chat AskUserQuestion):** **B = tam Playwright + `docker-compose.e2e.yml` + fake sidecar.** Dış-olay simülasyon sınırı = backend webhook/client seam (gerçek Steam human-trade + on-chain USDT finality CI'da unattended çalışamaz).
- **Teslim yapısı (owner kararı 2026-06-21):** **3 PR'a böl.** **CI gating:** E2E job **advisory / non-blocking** başlar.
  - **PR-1 (bu PR):** `sidecar-fake/` servisi + `docker-compose.e2e.yml`.
  - **PR-2:** Playwright workspace (`e2e/`) + JWT-inject login + SQL seed + smoke spec.
  - **PR-3:** FE `data-testid` + tam happy-path spec (8 state + tüm bildirimler) + CI e2e job (advisory).

## Doğrulanan happy-path zinciri (03 §1.2/§2–§3)

`CREATED → ACCEPTED → TRADE_OFFER_SENT_TO_SELLER → ITEM_ESCROWED → PAYMENT_RECEIVED → TRADE_OFFER_SENT_TO_BUYER → ITEM_DELIVERED → COMPLETED` (8 state, 7 geçiş).

Backend tam wire-li (placeholder yok — keşif workflow doğruladı): HTTP accept · `TradeOfferDispatchJob` (escrow+delivery dispatch) · `SteamWebhookHandler` (escrow+delivery accept) · `AmountValidationService` (payment confirm) · `SellerPayoutQueueJob`→`OutgoingTransferDispatchJob`→`OutgoingTransferConfirmationJob`→`PayoutCompletedConsumer` (payout→COMPLETED). Bildirimler WP19 ile üretiliyor.

## Doğrulanan seam'ler (mevcut koda karşı — fake bunlara uyacak)

**Inbound webhook (fake → backend), HMAC-SHA256:** imzalı dizi = `timestamp + nonce + rawBody`; header `X-Signature`(hex-lower)/`X-Timestamp`(ISO `O`)/`X-Nonce`; ayrı secret'lar `Webhook__SteamSharedSecret` / `Webhook__BlockchainSharedSecret`; zarf `{event,timestamp,data}` camelCase; ±300s replay penceresi.
- Steam: `POST /api/v1/webhooks/steam/trade-events` — `trade_offer.accepted` data: `transactionId`, `direction`("escrow"|"delivery"), `partnerSteamId`, `botSteamId`, `botAccountName`, `offerId`, `receivedAssetId`(escrow→`EscrowBotAssetId`), `deliveredAssetId`(delivery→`DeliveredBuyerAssetId`).
- Blockchain: `POST /api/v1/webhooks/blockchain/payment-detected` + `/payment-confirmed`. `payment-detected` data: `paymentAddressId`, `transactionId`, `txHash`, `eventIndex`, `fromAddress`, `toAddress`, `contractAddress`(handler'da saklanmıyor — kozmetik), `tokenSymbol`(StablecoinType'a parse olmalı), `amount`(`ExpectedAmount`'a **birebir** eşit olmalı), `blockTimestampMs`, `detectedAt`. `payment-confirmed`: aynı `(txHash, eventIndex)` + `blockNumber>0` + `confirmationCount`.

**Outbound (backend → fake), `X-Internal-Key`:**
- Steam (`SteamSidecar__BaseUrl` :5100): `GET /api/inventory/{steamId}`, `DELETE /api/inventory/{steamId}/cache`, `GET /api/trade-hold/{steamId}`, `POST /api/trade-offers/send` (`{status:"sent",offerId,attempts}`).
- Blockchain (`BlockchainSidecar__BaseUrl` :5200): `POST /api/wallet/derive`, `POST /api/wallet/balances`, `POST /api/monitor/post-cancel-{start,stop}`, `POST /api/transfer/{payout,refund,sweep,cold-wallet}` (`{txHash}`), `GET /api/transfer/status/{txHash}` (`confirmations≥20` + `SUCCESS` = finality).
- **"Path gap" riski (recon) çürütüldü:** outbound `/api/*` ile inbound `/api/v1/webhooks/*` karıştırılmış; gerçek uyumsuzluk yok.

**JWT-inject (PR-2):** HS256, secret `Jwt__Secret`, issuer `skinora`, audience `skinora-client`, claim `sub`(GUID)/`steam_id`/`role`; FE `localStorage["access_token"]`.

## Yapılan İşler (PR-1)

- **`sidecar-fake/`** — Node/TS (CommonJS, Express, pino), mevcut sidecar konvansiyonlarını birebir yansıtır (tsconfig/eslint/prettier/vitest/Dockerfile). Tek process iki port dinler (5100 steam, 5200 blockchain); compose network alias'ları ile her iki sidecar hostname'i bu container'a çözülür.
  - Outbound yüzeyin tamamı (inventory/trade-hold/trade-offers send + blockchain wallet/transfer/monitor/status).
  - `POST /api/trade-offers/send` → `sent` döner, `FAKE_TRADE_ACCEPT_DELAY_MS` (default 2000ms) sonra imzalı `trade_offer.accepted` webhook'u self-emit eder (dispatch job state geçişini commit etsin diye gecikme).
  - `GET /api/transfer/status/*` → `confirmations=25` + `SUCCESS` (payout ilk poll'de onaylanır).
  - **`/__e2e/payment/{pay,detect,confirm}`** kontrol endpoint'leri (auth'suz) — SQL'den `PaymentAddress`(id + `ExpectedAmount` + `ExpectedToken`) çözüp imzalı payment webhook'u POST eder. `/pay` = detect→confirm (exact amount) = `PAYMENT_RECEIVED`.
  - HMAC imzalayıcı (`timestamp+nonce+body`), webhook client, deterministik id/adres/txHash üreticileri, mssql lookup.
  - **Birim test 7/7** (HMAC imzanın elle hesapla birebir eşleşmesi + boş secret throw; deterministik üreticiler).
- **`docker-compose.e2e.yml`** — self-contained (db, redis, fake-sidecar [alias: steam+blockchain], backend, frontend, nginx). 19 `SKINORA_SETTING_*` + sabit test secret'ları (JWT/webhook/internal) + sidecar BaseUrl'leri fake'e. Playwright nginx origin'ini (`:8080`) hedefler; FE relative `/api/v1`+`/hubs` nginx proxy ile çalışır (CORS/rebuild yok). `docker compose config` ✓ geçti.

## Etkilenen Modüller / Dosyalar

**Yeni (PR-1):**
- `sidecar-fake/` — `package.json`, `package-lock.json`, `tsconfig.json`, `.eslintrc.json`, `.prettierrc.json`, `vitest.config.ts`, `.dockerignore`, `Dockerfile`, `README.md`
- `sidecar-fake/src/` — `config.ts`, `logger.ts`, `hmac.ts`, `webhookClient.ts`, `ids.ts`, `db.ts`, `middleware.ts`, `app.ts`, `index.ts`, `types.d.ts`, `routes/{health,steam,blockchain,control}.ts`, `{hmac,ids}.test.ts`
- `docker-compose.e2e.yml`

Mevcut prod kaynak **değişmedi** (yeni dizin + yeni compose dosyası).

## Kabul Kriterleri (T107 — task bütünü; PR bazında kapanış)

| # | Kriter | Durum | Not |
|---|---|---|---|
| 1 | Tam escrow akışı (giriş→…→COMPLETED) | ⏳ | Backend wire ✓; uçtan uca sürüş **PR-3** spec'inde assert edilir (fake bu PR'da hazırlanır) |
| 2 | Tüm bildirimler doğru tetikleniyor | ⏳ | Producer'lar WP19 ✓; E2E assert **PR-3** |
| 3 | Tüm state geçişleri UI'da doğru gösteriliyor | ⏳ | FE `data-testid` + assert **PR-3** |

PR-1 bu üç kriteri **mümkün kılar** (altyapı); kapanış PR-3'te kanıtlanır.

## Test Sonuçları (PR-1)

| Tür | Sonuç | Detay |
|---|---|---|
| Build (fake) | ✓ | `npm run build` (tsc) exit 0 |
| Lint (fake) | ✓ 0 | `npm run lint` (eslint) exit 0 |
| Format (fake) | ✓ | `npm run format:check` (prettier) clean |
| Unit (fake) | ✓ 7/7 | `npm test` (vitest) — `hmac.test.ts` 3 + `ids.test.ts` 4 |
| Compose | ✓ | `docker compose -f docker-compose.e2e.yml config` exit 0 |

## Altyapı Değişiklikleri

- Migration: **Yok**.
- Yeni servis: `sidecar-fake` (yalnız E2E; prod compose'a dahil **değil**).
- Yeni compose dosyası: `docker-compose.e2e.yml` (standalone).
- Yeni paket (fake): `express`, `mssql`, `pino` (+ dev: ts/eslint/prettier/vitest). Prod backend/sidecar bağımlılıkları değişmedi.

## Mini Güvenlik Kontrolü

- Secret sızıntısı: `docker-compose.e2e.yml` içindeki secret'lar **sabit test değerleri** (prod değil, açıkça işaretli). Gerçek secret yok.
- Auth: fake `X-Internal-Key`'i set ise doğrular; `/__e2e/*` kontrol yüzeyi auth'suz (yalnız E2E ağı). Servis prod'a deploy edilmez.
- Input validation: fake test double; kullanıcı girdisi işlemez.
- Yeni dış bağımlılık: `mssql` (tedious, pure-JS) yalnız fake'te.

## Known Limitations / Follow-up (PR-2/3)

- **Hangfire cadence:** 3 geçiş recurring job tick'ine bağlı (default ~1 dk → tam akış birkaç dk). PR-2'de cadence hızlandırma (config override varsa) veya Playwright timeout ayarı netleştirilecek.
- **Schema + seed:** backend fresh DB'ye migration uyguluyor mu PR-2'de doğrulanacak; happy-path seed (seller MA+payout, buyer SteamId, 1 ACTIVE bot) PR-2 harness'ında.
- **Bot-identity:** fake `trade_offer.accepted`'te `FAKE_BOT_STEAM_ID` gönderir; backend accept handler'ı bot SteamId'yi katı doğruluyorsa seed bot SteamId ile eşitlenecek (PR-2 smoke doğrular).
- **trade_offer.sent:** fake yalnız `accepted` emit eder; backend `sent` webhook'u da bekliyorsa eklenecek (send response `status:"sent"` zaten dispatch job'ı ilerletir).

## Commit & PR

- Branch: `task/T107-e2e-fake-sidecar`
- PR: (push sonrası doldurulacak)
- CI: (watch — sonuç eklenecek)

## Notlar

- **Working tree (Adım -1):** task öncesi temiz (main'den branch açıldı).
- **Main CI startup (Adım 0):** son 3 main run success (`27907999068` WP19 CI / `27907999071` WP19 Docker / `27904430722` WP18 teyit).
- **WP19 post-merge teyit (bu branch'te kayıt — merge öncesi main'e push edilemezdi, pre-push hook):** PR #195 squash merge → main `5b9570c`; post-merge CI `27907999068` + Docker Publish `27907999071` **success**. WP19 KAPANDI.
- **Dış varsayımlar (Adım 4):** `@playwright/test` 1.61.0 mevcut (npm view ✓); Node ≥20 (sidecar engines); JWT HS256 self-mint uygulanabilir (`Jwt__Secret` paylaşımlı); nginx `/api`+`/hubs`+`/health` proxy ✓; 19 `SKINORA_SETTING_*` fail-fast (env'den hidrat) ✓. Hepsi koda/komuta karşı doğrulandı.
