# T107 — E2E: Happy path (tam escrow akışı)

**Faz:** F6 | **Durum:** ⏳ PR-3/3 merge-hazır — **canAccept keşfi WP20 ile çözüldü (PR #199 → main `4c5b1a0`)**; PR-3 main üzerine rebase'lendi ve **UI smoke mainline registered-buyer akışına geri alındı** (deferred-buyer workaround kaldırıldı) → WP20 fix'i UI'dan uçtan uca **yerel re-verify** edildi (1 passed 4.5m; DB: `BuyerId=SET` + COMPLETED + 7 bildirim). PR-1/PR-2 ✓ merged+validator PASS; PR-3 deliverables (FE testid + UI smoke + CI e2e job) ✓. **Merge → T107 kapanır.** | **Tarih:** 2026-06-22

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
| 1 | Tam escrow akışı (giriş→…→COMPLETED) | ✓ (API düzeyi, PR-2 smoke) | Yerel docker stack'te 8 state'in 7 geçişi sürüldü → COMPLETED (kanıt aşağıda); UI düzeyi assert PR-3 |
| 2 | Tüm bildirimler doğru tetikleniyor | ✓ (PR-2 smoke) | 7 WP19 tipi gerçek üretildi (matris aşağıda); ITEM_DELIVERED bildirimi yok (WP19 bastırma) doğrulandı |
| 3 | Tüm state geçişleri UI'da doğru gösteriliyor | ✓ (PR-3 UI smoke) | Browser (chromium) detay sayfasında status badge `data-status` CREATED→…→COMPLETED izlendi; accept gerçek UI formuyla yapıldı (kanıt PR-3 bölümünde) |

PR-1 (fake) + PR-2 (API smoke) + PR-3 (UI smoke) **AC1+AC2+AC3'ü kanıtladı**.

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
- PR: [#196](https://github.com/turkerurganci/Skinora/pull/196)
- CI: ✓ PASS — run [27911099227](https://github.com/turkerurganci/Skinora/actions/runs/27911099227) (`af33445`). `1. Lint` + `CI Gate` success; Build/Unit/Integration/Contract/Migration/Docker/JS-test **skipped** (sidecar-fake + compose + docs `code` path filtresinde değil — mevcut joblar tetiklenmez). Beklenen davranış; mevcut prod hiçbir şey kırılmadı.

## Doğrulama (Bağımsız Validator — ayrı chat 2026-06-21, kendi verdict'i rapor görülmeden)

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** (kapsam: PR-1/3 = E2E altyapısı; T107 task-bütünü AC'leri PR-2/3'e ertelenir) |
| Bulgu sayısı | 0 bloke-edici |
| Düzeltme gerekli mi | Hayır |

**Kapılar:** Adım -1 working tree temiz · Adım 0 main son-3 run success (`27907999068` / `27907999071` / `27904430722`) · Adım 0b repo memory T107 satırı mevcut · Adım 8a task CI HEAD `1db3f2c` run [`27911199769`](https://github.com/turkerurganci/Skinora/actions/runs/27911199769) success (Lint + CI Gate success; Build/Unit/Integration/Contract/Migration/Docker/JS-test **skipped** — `sidecar-fake/` + `docker-compose.e2e.yml` + docs `code` path filtresinde değil, beklenen).

**Validator-firsthand kanıt (`sidecar-fake/`, Node 24 lokal + node:20-alpine Docker):**
- `npm run build` (tsc) exit 0 · `npm run lint` (eslint) exit 0 · `npm run format:check` (prettier) clean · `npm test` (vitest) **7/7** (hmac 3 + ids 4).
- `docker compose -f docker-compose.e2e.yml config` exit 0.
- **`docker build ./sidecar-fake`** (rapor iddia etmiyordu — validator ekledi) **exit 0** → Dockerfile + committed lockfile node:20-alpine'da `npm ci` (builder + `--omit=dev` runtime) + `tsc` temiz; WP18 npm-skew lockfile riski **bu PR'da yok**.
- **Zero prod source change:** `git diff --name-only origin/main...HEAD -- backend frontend sidecar-steam sidecar-blockchain infra nginx .github` = boş.

**Seam'ler bağımsız teyit (fake ↔ backend kaynağı, repo notuna güvenmeden):**
- HMAC: backend `WebhookSignatureMiddleware.ComputeSignature` = `HMACSHA256(secret, timestamp+nonce+body)` hex-lower, header `X-Signature`/`X-Timestamp`/`X-Nonce`, ±`ReplayWindowSeconds`, prefix-bazlı secret seçimi → fake `signWebhook` birebir.
- Inbound payload: `trade_offer.accepted` (`TradeOfferEventData`), `payment.detected` (`PaymentDetectedData`), `payment.confirmed` (`PaymentConfirmedData`) alan adları + envelope `{event,timestamp,data}` camelCase birebir; detect/confirm aynı `txHash`+`eventIndex=0` → dedup anahtarı tutar.
- Outbound: inventory `{items,totalCount,tradeableCount}` · trade-hold `{active,escrowEndDurationSeconds}` · trade-offers/send `{status,offerId,attempts}` (status="sent"→Sent) · transfer `{txHash}` · transfer/status `{txHash,blockNumber,contractRet,confirmations}` (25≥20+SUCCESS=finality) — backend HTTP client DTO'larıyla birebir; tüm path'ler eşleşir.

**Mini güvenlik (validator):** secret'lar sabit test fixture (açıkça işaretli, gerçek secret yok) · `/__e2e/*` kontrol yüzeyi kasıtlı auth'suz + yalnız fake image'da (prod'a deploy edilmez) · SQL lookup parametreli (`sql.UniqueIdentifier`, injection yok) · yeni dep yalnız fake'te (express/pino mevcut aile, mssql pure-JS).

**Non-blocking gözlem (N1 — CI coverage):** `sidecar-fake/` CI path filtresinde olmadığı için lint/format/unit/docker job'ları **hiç çalışmaz** (PR'ın "Lint SUCCESS"i fake'i lint etmedi). Owner "advisory E2E CI" kararıyla uyumlu; doğal yer **PR-3'ün e2e job'u** — orada `sidecar-fake` lint+unit (ve ideal olarak image build) CI'ya bağlanmalı ki 7 birim test + lint/format gelecekteki değişiklikleri kapısın. Rapor (Commit & PR §) bu skip davranışını şeffaf belgeliyor.

**Yapım raporu karşılaştırması:** Tam uyumlu — rapordaki seam tablosu, test sonuçları (7/7 + compose config), zero-prod-change ve AC ertelemesi (3 kriter ⏳ → PR-3) validator bağımsız bulgularıyla birebir; rapor docker build iddia etmemiş (over-claim yok), validator ek olarak doğruladı.

## PR-2 — E2E harness + happy-path smoke (2026-06-21, branch `task/T107-e2e-harness`)

**Teslim:** Playwright `e2e/` workspace + JWT-inject login + SQL seed + API-düzeyi happy-path smoke; ayrıca smoke'un ortaya çıkardığı **2 fake düzeltmesi** + **1 compose düzeltmesi**.

### Yapılan
- **`e2e/`** — Playwright workspace (`playwright.config.ts`, tsconfig, eslint/prettier). API-düzeyi smoke: SQL seed → JWT-inject (Bearer) → create → accept → fake ile escrow/delivery sürüş + ödeme → COMPLETED poll → bildirim assert.
  - `src/jwt.ts` — HS256 mint (`Jwt__Secret`, iss=skinora, aud=skinora-client, claim sub/steam_id/role).
  - `src/db.ts` — mssql seed: seller (MA+payout+backdated), buyer (SteamId), 1 ACTIVE bot, **`ItemPriceCaches`** satırı (= listeleme fiyatı → %0 sapma → FLAGGED değil, Steam Market erişiminden bağımsız); idempotent cleanup; `getNotificationTypes` assert.
  - `src/api.ts` — create/accept/get + `/__e2e/payment/pay` (fake) + ApiResponse unwrap + `pollStatus`.
  - `tests/happy-path.smoke.spec.ts` — tek serial smoke.
- **Fake düzeltmeleri (`sidecar-fake/src/routes/steam.ts`):**
  1. **`trade_offer.sent` → `trade_offer.accepted` sıralı** — backend `HandleSentAsync` TradeOffer satırını (offerId, bot=DisplayName) yaratır; `HandleAcceptedAsync` satırı offerId ile bulur. Yalnız `accepted` emit etmek ITEM_ESCROWED'a ilerletmiyordu.
  2. **`direction` passthrough** — webhook `direction` = dispatch isteğinin token'ı (`SELLER_TO_BOT`/`BOT_TO_BUYER`), "escrow"/"delivery" **değil** (backend `ParseDirection` `SidecarDirection*` sabitleri = SELLER_TO_BOT/BOT_TO_BUYER).
- **Compose düzeltmesi (`docker-compose.e2e.yml`):** frontend healthcheck `wget` → node `fetch` (node:20-slim'de wget/curl yok) **+ path `/health` → `/api/health`** (Next App Router route'u; düz `/health` 404 → container unhealthy → nginx bloklanırdı). Düzeltme sonrası full stack (frontend + nginx dahil) healthy ayağa kalkar; **validator F1 ile yakalandı ve bu PR'da kapatıldı** (aşağı).

### Çalıştırma mekanizması (doğrulandı)
Backend **auto-migrate etmez** (N3, PR-1'den miras — `compose up --wait` tek-satırı fresh DB'de takılır) → önce `compose up -d skinora-db`, db healthy olunca host'tan `dotnet ef database update --project src/Skinora.Shared --startup-project src/Skinora.API` (`Server=localhost,14333`) ile şema, **sonra** `compose up -d` (kalan servisler). **F1 düzeltmesi sonrası full stack (frontend + nginx dahil) healthy ayağa kalkar** ve smoke **committed default `:8080`** (nginx origin) üzerinden koşar — override gerekmez. Hangfire recurring job'ları (her ~1 dk) job-driven 3 geçişi sürer (tam akış ~5 dk).

### Smoke sonucu — ✅ GREEN (yerel, gerçek docker stack)
`npx playwright test` → **1 passed (4.8m)**. Geçiş zinciri (DB'den izlendi): CREATED → ACCEPTED → TRADE_OFFER_SENT_TO_SELLER → ITEM_ESCROWED → PAYMENT_RECEIVED → (TRADE_OFFER_SENT_TO_BUYER) → ITEM_DELIVERED → **COMPLETED**.

**WP19 bildirim matrisi (gerçek üretim, `Notifications` tablosu):**

| Tip | Adet | Alıcı |
|---|---|---|
| TRANSACTION_INVITE | 1 | alıcı |
| BUYER_ACCEPTED | 1 | satıcı |
| ITEM_ESCROWED | 1 | alıcı |
| PAYMENT_RECEIVED | 1 | satıcı |
| TRADE_OFFER_SENT_TO_BUYER | 1 | alıcı |
| SELLER_PAYMENT_SENT | 1 | satıcı |
| TRANSACTION_COMPLETED | **2** | satıcı + alıcı |

ITEM_DELIVERED için bildirim **yok** (WP19 bastırma) — doğrulandı. COMPLETED 2+1 (owner kararı) doğrulandı. Bu, T107 AC1 (tam akış) + AC2 (tüm bildirimler) + WP19'u uçtan uca kanıtlar.

**F2 düzeltmesi (validator):** smoke'un bildirim assert'i artık **regresyon guard'ı** — 7 tipin hepsi `toContain` + `TRANSACTION_COMPLETED` adedi `=2` + `ITEM_DELIVERED` `not.toContain` assert edilir (önceki sürüm yalnız `TRANSACTION_COMPLETED` bakıyordu = AC2 over-claim). `pollNotificationTypes` ile kısa poll eklendi (COMPLETED-flip / notification-commit race'ine karşı = N1).

### Iterasyon (smoke yeşile giderken bulunan + düzeltilen)
1. Tablo adı `ItemPriceCache` → **`ItemPriceCaches`** (EF çoğul). 2. `paymentTimeoutHours 24` → **1** (e2e max=60 dk). 3. Bot `Status` **nvarchar** ("ACTIVE", `0` değil) → bot seçilmiyordu. 4. Webhook `direction` "escrow" → **SELLER_TO_BOT** (yukarıda).

### Doğrulama (lokal)
- e2e: `tsc --noEmit` ✓, `eslint .` ✓ 0. Fake (düzeltme sonrası): build ✓, lint ✓ 0, unit **7/7**. Smoke **1/1 PASS**. Stack `compose up --build --wait` (db/redis/fake/backend healthy) + `dotnet ef database update` ✓.

### Known (PR-3)
- AC3 UI assert (FE `data-testid` + Playwright browser); CI e2e job (advisory) + `sidecar-fake`/`e2e` CI lint/build/test wiring (validator N1, N2). Frontend healthcheck + full-stack (frontend+nginx) ayağa kalkış **PR-2'de validator tarafından teyit edildi** (F1 fix). N3 (compose header'ın "schema harness uygular" yanılgısı + `up --wait` migration prereq'i) PR-1'den miras, non-blocking — PR-3 CI e2e job'unda host-migration adımı eklenmeli.

### Commit & PR (PR-2)
- Branch: `task/T107-e2e-harness` · Commit: `71e5d69`
- PR: [#197](https://github.com/turkerurganci/Skinora/pull/197)
- CI: ✓ PASS — run [27913553177](https://github.com/turkerurganci/Skinora/actions/runs/27913553177). `1. Lint` + `CI Gate` success; Build/Unit/Integration/Contract/Migration/Docker/JS-test **skipped** (`e2e/` + `sidecar-fake/` + compose + docs `code` path filtresinde değil — PR-1 deseni). E2E smoke CI'da çalışmaz (advisory job PR-3'te); bu PR'da smoke **yerel** kanıt.

### Doğrulama (Bağımsız Validator — PR-2, ayrı chat 2026-06-21, kendi verdict'i rapor görülmeden)

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | **FAIL → fix → ✓ PASS** (validator gerçek docker stack'i kurup smoke'u bizzat koştu) |
| Bulgu sayısı | 1 bloke-edici (F1) + 1 minor (F2), **ikisi de bu PR'da kapatıldı** |
| Düzeltme gerekli mi | Yapıldı (owner kararı: F1+F2'yi PR-2'de düzelt + re-validate) |

**Kapılar:** Adım -1 temiz · Adım 0 main son-3 success (`27912352893`/`27912352878`/`27907999068`) · Adım 0b memory mevcut · Adım 8a task CI run `27913553177`/`27913630332` success (ama **vacuous** — `e2e/`+`sidecar-fake/` path-filtre dışı; N2).

**Validator-firsthand reprodüksiyon (gerçek docker stack, Docker 29 + .NET 9 + Node 24):** `compose build` (5 image) exit0 → `compose up -d skinora-db` → host `dotnet ef database update` (`:14333`) → `compose up -d` → smoke. İlk turda DB'den canlı izlendi `…→ITEM_DELIVERED→COMPLETED`; `Notifications` tablosu **tam 7 tip** + COMPLETED×2 + ITEM_DELIVERED yok = rapor matrisi birebir. e2e tsc0/eslint0/prettier-clean; fake tsc0/eslint0/vitest **7/7**.

**Bulgular:**
- **F1 (S3, bloke-edici → kapatıldı):** committed `config.ts` default `baseUrl :8080` (nginx) ama frontend healthcheck'i yanlış path (`/health` 404; gerçek route `/api/health` 200) prob ediyordu → frontend **unhealthy** → nginx (`depends_on: frontend healthy`) **hiç başlamıyor** (state=`created`) → committed-default smoke **erişilemez** (`:8080` http=000); "GREEN" yalnız belgesiz `:5000` override ile alınmıştı. Validator nginx'i `--no-deps` zorla başlatınca `:8080` tam çalıştı (tek engel healthcheck path'i). **Düzeltme:** `docker-compose.e2e.yml` frontend healthcheck `/health` → `/api/health`. **Re-validation:** `compose up -d` sonrası frontend **healthy** + nginx **healthy**; smoke **committed default `:8080` (override yok) → 1 passed (5.1m)**.
- **F2 (S1, minor → kapatıldı):** committed smoke yalnız `toContain('TRANSACTION_COMPLETED')` (1/7) assert ediyordu; rapor AC2'yi "tüm bildirimler kanıtlandı" sunuyordu (davranış doğru ama regresyon guard'ı yok). **Düzeltme:** 7 tip `toContain` + COMPLETED `=2` + `ITEM_DELIVERED` `not.toContain` + `pollNotificationTypes` (N1 race). Re-validation run'ında **güçlendirilmiş assert'ler geçti**.

**Çürütülen / non-blocking (validator + 6-ajan refute-default workflow):** JWT claim/iss/aud backend `AccessTokenGenerator`+`AuthModule` ile birebir · fake fix'leri (sent→accepted sıralı, direction passthrough) backend `HandleSentAsync`/`ParseDirection`'a karşı doğru+gerekli · seed NOT-NULL kolonları kapsar · AC1 kısa-devre yok (ayrı exact-match poll'lar) · zero prod source change · güvenlik temiz (test-fixture secret, parametreli seed, `/__e2e/*` yalnız fake). **N2** task CI vacuous (path-filtre), advisory e2e job PR-3'te. **N3** compose header schema-by-harness yanılgısı PR-1'den miras (non-blocking, host-migration prereq).

**Yapım raporu karşılaştırması:** Seam analizi + WP19 matrisi + zero-prod-change birebir doğru. İki uyuşmazlık F1 (healthcheck düzeltmesi amacına ulaşmıyordu) + F2 (AC2 test kapsamı over-claim) bu PR'da kapatıldı; davranışsal iddialar (AC1+AC2) validator tarafından firsthand kanıtlandı.

## PR-3 — FE data-testid + UI smoke + CI e2e job (2026-06-21, branch `task/T107-e2e-ui`) — T107'yi kapatır

**Teslim:** AC3 (state geçişleri UI'da) için FE `data-testid` + browser-driven Playwright UI smoke; ayrıca CI e2e job (advisory) + `sidecar-fake`/`e2e` CI lint/build/test wiring (validator N1).

### Yapılan
- **FE `data-testid`:** `StatusBadge` (`data-status={status}` her zaman + opsiyonel `testId`), `DetailHeader` (`testId="tx-status-badge"`), `AcceptForm` (`accept-refund-input` + `accept-submit`). Yalnız test-hook ekleri; davranış değişmedi (FE lint ✓, format ✓, vitest 28/28 ✓).
- **UI smoke** (`e2e/tests/happy-path.ui.spec.ts` + `e2e/src/browser.ts`): JWT-inject (`localStorage["access_token"]` → `AuthInitializer` hidrasyon) → buyer detay sayfası → **gerçek UI AcceptForm** ile accept → status badge `data-status`'u CREATED→…→COMPLETED reload-poll ile izle. nginx origin'i (`:8080`, committed default) hedefler; relative `/api/v1`+`/hubs` proxy ile (prod ile aynı). chromium projesi + `test:ui` script.
- **CI** (`.github/workflows/ci.yml`): (a) `changes` filtresine `sidecar-fake`/`e2e`/`e2e-stack`; (b) **lint** job'a `sidecar-fake` (tsc+format+lint) + `e2e` (tsc+format+lint) — bloke-edici (N1); (c) **JS test** job'a `sidecar-fake` vitest; (d) yeni **`e2e-smoke`** job — **advisory** (`continue-on-error: true` + `ci-gate.needs`'te **değil**): backend+fake imajlarını build, db→migrate→up, API smoke (`:5000`). Owner kararı: advisory.

### UI smoke sonucu — ✅ GREEN (yerel, chromium + full nginx stack)
`npx playwright test happy-path.ui` → **1 passed (5.3m)**. Badge `data-status` izleme (DB ile teyitli): CREATED → ACCEPTED → TRADE_OFFER_SENT_TO_SELLER → PAYMENT_RECEIVED → ITEM_DELIVERED → **COMPLETED**. Accept **gerçek UI formuyla** yapıldı (mock değil). Full stack (db/redis/fake/backend/frontend/nginx) tümü healthy; `:8080/en` 200.

### T107 keşfi (registered STEAM_ID buyer `canAccept`) — ✅ ÇÖZÜLDÜ (WP20)
`TransactionDetailService.BuildAuthenticatedActions`: `canAccept = role=="buyer" && CREATED && BuyerId is null`. STEAM_ID **kayıtlı** alıcıda create `BuyerId`'yi **set ediyor** (`TransactionCreationService`) → detay `canAccept=false` → **UI accept formu disabled**, hâlbuki accept endpoint'i (party=SteamId eşleşme) izin veriyor. Yani kayıtlı bir hedef alıcı (TRANSACTION_INVITE alan) UI'dan kabul edemiyordu; yalnız prospective (BuyerId null) alıcı edebiliyordu. **Çözüm:** WP20 (`&& BuyerId is null` kaldırıldı + EMERGENCY_HOLD detay projeksiyonu), PR #199 → main `4c5b1a0`. Bu keşif sırasında UI smoke geçici olarak **deferred-buyer** (prospective) workaround'unu kullanıyordu; WP20 sonrası **mainline registered-buyer akışına geri alındı** (aşağı bkz.).

### PR-3 güncelleme (post-WP20) — UI smoke mainline registered-buyer'a alındı + re-verify
WP20 main'e merge edilip PR-3 rebase'lendikten sonra deferred-buyer workaround kaldırıldı: `seedHappyPath()` artık alıcıyı **kayıtlı STEAM_ID** kullanıcı olarak create öncesi seed eder (`includeBuyer` opsiyonu + `insertBuyer` deferred-çağrısı silindi; her iki smoke da mainline shape). Böylece UI smoke artık WP20'nin asıl senaryosunu — create-time `BuyerId` set olan kayıtlı alıcının gerçek UI formundan kabul etmesini — egzersiz ediyor.
- **Yerel re-verify (full stack, chromium + nginx `:8080`):** `npx playwright test happy-path.ui` → **1 passed (4.5m)**. DB teyidi: `Transactions.BuyerId=SET(registered)` + `TargetBuyerSteamId=76561198000000061` + `Status=COMPLETED`; `Notifications` = 7 tip (TRANSACTION_INVITE / BUYER_ACCEPTED / ITEM_ESCROWED / PAYMENT_RECEIVED / TRADE_OFFER_SENT_TO_BUYER / SELLER_PAYMENT_SENT / TRANSACTION_COMPLETED×2; ITEM_DELIVERED yok = WP19 bastırma).
- **Net değişiklik:** yalnız `e2e/src/db.ts` + `e2e/tests/happy-path.ui.spec.ts` (workaround scaffolding kaldırıldı, backend/FE dokunulmadı). e2e tsc/eslint ✓, prettier `--end-of-line=auto` temiz.

### Doğrulama (lokal)
- FE: lint ✓ · format (düzenlenen 3 dosya) ✓ · vitest **28/28** · frontend image build ✓ (UI smoke kullandı).
- e2e: tsc ✓ · eslint ✓ · prettier ✓. sidecar-fake: build/lint/format/**7-7** ✓ (değişmedi).
- CI YAML: js-yaml parse ✓; `e2e-smoke` advisory (`continue-on-error` + gate-dışı) doğrulandı. `docker compose -f docker-compose.e2e.yml config` ✓.
- UI smoke **1/1 PASS** (chromium, full stack).

### Known / Follow-up
- registered-buyer `canAccept` keşfi (yukarıda) — owner follow-up.

### Commit & PR (PR-3)
- Branch: `task/T107-e2e-ui` · Commit: `7cabfe4`
- PR: [#198](https://github.com/turkerurganci/Skinora/pull/198)
- CI: ✓ PASS — run [27916787128](https://github.com/turkerurganci/Skinora/actions/runs/27916787128). **Tüm bloke-edici joblar success** (Lint [+`sidecar-fake`/`e2e` adımları], Build, Unit, Integration, JS-test [+`sidecar-fake` vitest], Contract, Migration, 4× Docker, CI Gate). **`E2E smoke (advisory)` job da SUCCESS** — yani docker-compose stack CI'da gerçekten ayağa kalktı, migrate oldu ve API smoke COMPLETED'a ulaştı (yerel doğrulanamayan endişe CI'da kendiliğinden kapandı; yine de advisory kalır).

## Doğrulama (PR-3/3, bağımsız validator) — 2026-06-22

**Branch:** `task/T107-e2e-ui` · **HEAD:** `9d0d8ea` · **PR:** [#198](https://github.com/turkerurganci/Skinora/pull/198) · **Verdict: ✓ PASS (PR-3 teslimatları + T107 AC'leri)** — ama owner kararıyla **T107 HELD** (aşağıdaki keşif önce düzeltilecek; merge yapılmadı).

> Validator yapım raporunu görmeden bağımsız verdict üretti; her iddia firsthand doğrulandı. Yapım raporuyla **tam uyumlu, over-claim yok** (rapor keşfi kendisi de bildiriyor).

### Kapılar
- Adım -1 working tree temiz · Adım 0 main son-3 success (`27915545862`/`27915545858`/`27912352893`) · Adım 0b repo memory T107 mevcut.
- Adım 8a task CI HEAD `9d0d8ea` run [`27917038313`](https://github.com/turkerurganci/Skinora/actions/runs/27917038313) — **15 job hepsi success/skipped, vacuous değil.** `E2E smoke (advisory)` job docker-compose stack'i CI'da gerçekten kurup migrate edip API smoke'u COMPLETED'a sürdü + **PASS** (yapım "yerel doğrulanamadı" demişti → CI'da kapandı). Lint yeni `sidecar-fake`+`e2e` bloke gate'leriyle, JS-test FE 28 + `sidecar-fake` vitest ile, CI Gate success.

### Kabul kriterleri (T107 bütünü)
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Tam akış → COMPLETED | ✓ | API smoke (PR-2) + UI smoke registered STEAM_ID buyer (PR-3, post-WP20 mainline) |
| 2 | Tüm bildirimler | ✓ | PR-2/WP19 — 7 tip + COMPLETED×2, ITEM_DELIVERED yok |
| 3 | State geçişleri UI'da | ✓ | `happy-path.ui.spec.ts` gerçek chromium+nginx, badge `data-status` her geçiş |

### Firsthand
- **Kapsam:** prod değişikliği yalnız 3 FE test-hook (StatusBadge/AcceptForm/DetailHeader), **0 backend**. `data-testid` opsiyonel + `data-status` her zaman → davranış korunur.
- `extraHTTPHeaders` kaldırma regresyon değil — `api.ts` native fetch kendi `Content-Type`'ını set ediyor (Playwright request fixture kullanılmıyordu).
- `e2e-smoke` `continue-on-error:true` + `ci-gate.needs`'te DEĞİL → advisory gerçekten bloke etmez. Lockfile'lar tracked → `npm ci` sağlam. N1 kapandı.
- Yerel kalite: e2e tsc/eslint temiz · prettier `--end-of-line=auto` temiz (çıplak `format:check` 5-dosya = belgeli Windows CRLF false-pos; CI LF'de PASS).
- Güvenlik: yalnız test-fixture secret · auth yüzeyi değişmedi · PR-3'te yeni runtime dep yok.

### Bulgu — S1 sapma (pre-existing kod, PR-3 sokmadı) → **owner: önce düzelt**
**registered STEAM_ID buyer `canAccept`:** `TransactionDetailService.cs:468-470` `canAccept = role=="buyer" && CREATED && BuyerId is null`. STEAM_ID **kayıtlı** alıcıda create `BuyerId`'yi set ediyor (`TransactionCreationService.cs:182-186,216`) → `canAccept=false` → UI AcceptForm **disabled** (`StateActionPanel.tsx:264`; `cannotAcceptReason` üstelik gerçek gate'le uyumsuz MA/cooldown metni). **03 §3.2:195 ("Eşleşiyorsa → devam eder") ile çelişir.** Ek inversion: `TRANSACTION_INVITE` bildirimi `BuyerId null→no-op` (WP19) → bildirim alan kayıtlı alıcı UI'dan kabul **edemiyor**, kabul edebilen prospective alıcı bildirim **almıyor** = mainline UI happy-path kırık. UI smoke deferred-buyer (prospective) ile aşıyor (geçerli bir variant ama mainline değil).

**Owner kararı (AskUserQuestion 2026-06-22): "önce düzelt, sonra T107 kapat"** (WP19-style promote-before-close). → canAccept ayrı backend/FE fix-task'ı (ayrı yapım chat'i); sonra mevcut UI harness registered-buyer akışını da doğrular → T107 öyle kapanır. **✅ Çözüldü: WP20 (PR #199 → main `4c5b1a0`) canAccept'i düzeltti; PR-3 rebase'lendi + UI smoke mainline registered-buyer'a alındı + yerel re-verify (1 passed 4.5m, `BuyerId=SET`/COMPLETED) — "PR-3 güncelleme (post-WP20)" bölümüne bkz. PR #198 merge-hazır.**

## Notlar

- **Working tree (Adım -1):** task öncesi temiz (main'den branch açıldı).
- **Main CI startup (Adım 0):** son 3 main run success (`27907999068` WP19 CI / `27907999071` WP19 Docker / `27904430722` WP18 teyit).
- **WP19 post-merge teyit (bu branch'te kayıt — merge öncesi main'e push edilemezdi, pre-push hook):** PR #195 squash merge → main `5b9570c`; post-merge CI `27907999068` + Docker Publish `27907999071` **success**. WP19 KAPANDI.
- **Dış varsayımlar (Adım 4):** `@playwright/test` 1.61.0 mevcut (npm view ✓); Node ≥20 (sidecar engines); JWT HS256 self-mint uygulanabilir (`Jwt__Secret` paylaşımlı); nginx `/api`+`/hubs`+`/health` proxy ✓; 19 `SKINORA_SETTING_*` fail-fast (env'den hidrat) ✓. Hepsi koda/komuta karşı doğrulandı.
