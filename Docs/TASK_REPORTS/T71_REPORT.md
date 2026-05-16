# T71 — Blockchain Sidecar — ödeme izleme

**Faz:** F4 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-05-16

---

## Yapılan İşler

**Sidecar (`sidecar-blockchain`):**
- `TronGridClient` — TronGrid REST sarmalayıcı: `listTrc20` (faz 1 `contract_address` filtreli + faz 2 filtresiz), `getNowSolidBlock` (`walletsolidity/getnowblock`), `getTransactionInfoById` (`walletsolidity/gettransactioninfobyid`). `TRON-PRO-API-KEY` header, prom metrics, `TronGridHttpError` 4xx/5xx ayrımı.
- `PaymentMonitorRules` — pure rule modülü: `isTransferRecord` (yalnız `type === 'Transfer'`), `classifyToken` (expected / wrong_token / spam_token), `isFinalized` + `confirmationCount` (`currentSolidBlock - txBlock >= 20`), `formatTokenAmount` (raw uint → "100.500000" decimal string, scale 6), `isIncomingFor` (deposit address sahipliği).
- `MonitorRegistry` — adres başına izleme yaşam döngüsü: `start`/`stop`/`tick` idempotent API, tek paylaşımlı `setInterval` (`paymentPollingIntervalMs`), `polling` guard ile overlapping önlenmiş. Her tick: phase 1 → phase 2 → finality probe. Phase 1'in fingerprint'i ile phase 2 fingerprint'i bağımsız. `seenTxHashes` set'i sidecar lifetime dedup (txid). Phase 2 expected token hit'ini "late catch" olarak kabul eder (phase 1 fingerprint kayması savunması).
- Webhook payload tipleri (`WebhookPayloads.ts`): `PaymentDetectedData`, `PaymentConfirmedData`, `WrongTokenIncomingData`, `SpamTokenIncomingData` + `BlockchainWebhookEnvelope<T>` envelope.
- `WebhookClient.sendCallback` HMAC-SHA256 imzayı `timestamp + nonce + body` üzerinden hesaplar, `WebhookDeliveryError.retryable` 5xx/408/429 için true.
- `api/monitor/start` + `api/monitor/stop` HTTP endpoint'leri internal-key auth'lu, yapısal validasyon (`address`/`paymentAddressId`/`transactionId`/`expectedContract`/`expectedSymbol` zorunlu, symbol USDT/USDC).
- `metrics.ts` idempotent registration (`Symbol.for` guard) — vitest singleFork pool'unda çift kayıt çakışmasını engeller (T70'te de aynı bug pattern'i yaşandı, T71'de structurel çözüm).
- Stub `TransactionMonitor.ts` kaldırıldı (replaced by `MonitorRegistry`). `PostCancelMonitor.ts` stub T75 için duruyor.

**Backend (`Skinora.API` + `Skinora.Transactions`):**
- `WebhookSignatureMiddleware` path-scope `/api/v1/webhooks/steam` + `/api/v1/webhooks/blockchain` route tablosuna geçti (T68 K-future kapandı). Her route kendi shared secret'ı (`SteamSharedSecret` / `BlockchainSharedSecret`) ve nonce source'unu (`steam-sidecar` / `blockchain-sidecar`) kullanır.
- `WebhookSettings.BlockchainSharedSecret` (opsiyonel, prod'da boş ise 401).
- `BlockchainWebhookPayloads` + `IBlockchainWebhookHandler` + `BlockchainWebhookHandler` — 4 event handler (`PaymentDetected`, `PaymentConfirmed`, `WrongTokenIncoming`, `SpamTokenIncoming`). `BlockchainWebhookResult` enum (`Applied` / `Idempotent` / `Unknown` / `Invalid`).
- `BlockchainTransaction` persistence (06 §3.8): `BUYER_PAYMENT` Status=DETECTED → CONFIRMED akışı, `WRONG_TOKEN_INCOMING` DETECTED + `ActualTokenAddress`, `SPAM_TOKEN_INCOMING` doğrudan CONFIRMED + `ConfirmationCount=20` (CK_Status_Confirmed gereksinimini karşılar). Idempotency `TxHash` UNIQUE constraint üzerinden defense-in-depth.
- `BlockchainWebhooksController` — 4 POST endpoint (`payment-detected` / `payment-confirmed` / `wrong-token` / `spam-token`), `AllowAnonymous` (signature middleware auth eder), `X-Correlation-Id` propagation.
- DI: `TransactionsModule.AddTransactionsModule` `IBlockchainWebhookHandler` scoped kayıt.
- `appsettings.json` + `docker-compose.yml` — `Webhook__BlockchainSharedSecret` env var, blockchain-sidecar service env'ine `INTERNAL_KEY`, `WEBHOOK_SECRET`, `TRON_API_KEY`, `PAYMENT_POLLING_INTERVAL_MS`, `MIN_CONFIRMATIONS` eklendi.

## Etkilenen Modüller / Dosyalar

**Sidecar (yeni):**
- `sidecar-blockchain/src/tron/TronGridClient.ts` + `TronGridClient.test.ts`
- `sidecar-blockchain/src/monitor/PaymentMonitorRules.ts` + `PaymentMonitorRules.test.ts`
- `sidecar-blockchain/src/monitor/MonitorRegistry.ts` + `MonitorRegistry.test.ts`
- `sidecar-blockchain/src/api/monitorHandlers.ts`

**Sidecar (güncellendi):**
- `sidecar-blockchain/src/webhook/WebhookPayloads.ts` (concrete event types)
- `sidecar-blockchain/src/webhook/WebhookClient.ts` (typed envelope + retryable error)
- `sidecar-blockchain/src/api/routes.ts` (monitor endpoints wired)
- `sidecar-blockchain/src/config/index.ts` (polling config + allowlist + webhook endpoints)
- `sidecar-blockchain/src/index.ts` (MonitorRegistry initialize + shutdown)
- `sidecar-blockchain/src/metrics.ts` (idempotent registration guard)

**Sidecar (silindi):**
- `sidecar-blockchain/src/monitor/TransactionMonitor.ts` (replaced by MonitorRegistry)

**Backend (yeni):**
- `backend/src/Modules/Skinora.Transactions/Application/Webhooks/BlockchainWebhookPayloads.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Webhooks/IBlockchainWebhookHandler.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Webhooks/BlockchainWebhookHandler.cs`
- `backend/src/Skinora.API/Controllers/BlockchainWebhooksController.cs`
- `backend/tests/Skinora.API.Tests/Integration/BlockchainWebhookEndpointTests.cs`

**Backend (güncellendi):**
- `backend/src/Skinora.API/Middleware/WebhookSignatureMiddleware.cs` (route table)
- `backend/src/Skinora.API/Middleware/WebhookSettings.cs` (BlockchainSharedSecret)
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` (DI handler)
- `backend/src/Skinora.API/appsettings.json` (Webhook config)
- `docker-compose.yml` (env wiring)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 3sn polling aralığı ile deposit adresi izleme | ✓ | `config.paymentPollingIntervalMs=3_000` (env override `PAYMENT_POLLING_INTERVAL_MS`), `MonitorRegistry.ensureTimer` tek shared `setInterval` ile sürer. Test: `MonitorRegistry.test.ts` `tick()` invocation flow |
| 2 | Aşama 1: beklenen token sorgusu (contract_address, only_confirmed, fingerprint pagination) | ✓ | `TronGridClient.listTrc20({contractAddress, fingerprint, limit=20})`, `MonitorRegistry.pollPhase1` fingerprint state carry-over. Test: `TronGridClient.test.ts` URL build + `MonitorRegistry.test.ts` "emits PaymentDetected on phase 1 hit" |
| 3 | Aşama 2: yanlış token taraması (filtresiz, tüm TRC-20) | ✓ | `MonitorRegistry.pollPhase2` `contractAddress` omitted; ayrı `phase2Fingerprint`. Test: `MonitorRegistry.test.ts` "emits WrongTokenIncoming" + "emits SpamTokenIncoming" + "does not retry phase 2 records emitted as expected via phase 1" |
| 4 | Kayıt türü filtresi: yalnızca Transfer (Authorization/Approval/TRC-721 skip) | ✓ | `PaymentMonitorRules.isTransferRecord(t) === (t === 'Transfer')`. Test: `PaymentMonitorRules.test.ts` table-driven 5 case + `MonitorRegistry.test.ts` "skips Approval / non-Transfer records" |
| 5 | 20 blok minimum onay (currentSolidBlock - txBlock >= 20) | ✓ | `PaymentMonitorRules.isFinalized({currentSolidBlock, txBlock, minConfirmations})` + `MonitorRegistry.checkFinality` solid block + `getTransactionInfoById`. Test: 3-tick scenario in `MonitorRegistry.test.ts` "emits PaymentConfirmed once finality is reached (delta >= 20)" (delta 10 → no emit; delta 20 → emit) |
| 6 | İdempotent işleme: txid + event_index bileşik anahtar | ~ Kısmi | Sidecar dedup `seenTxHashes:Set<txid>` (event_index granularity K3 forward-devir — TronGrid v1 `trc20` endpoint event_index expose etmez). Backend defense-in-depth `BlockchainTransaction.TxHash` UNIQUE (06 §3.8). Test: `MonitorRegistry.test.ts` "the same txHash does not re-emit on subsequent ticks" + `BlockchainWebhookEndpointTests.PaymentDetected_DuplicateTxHash_ReturnsIdempotent` |
| 7 | Wrong-token: allowlist'te → iade, spam → ignore + log | ✓ | `classifyToken` allowlist USDT/USDC kontrol; `MonitorRegistry.emitWrongTokenIncoming` (refund dispatch T72/T73 devir K1/K2) vs `emitSpamTokenIncoming` (terminal CONFIRMED, refund yok). Test: `PaymentMonitorRules.test.ts` `classifyToken` 5 case + `MonitorRegistry.test.ts` `WrongTokenIncoming` + `SpamTokenIncoming` |
| 8 | Backend'e webhook callback: PaymentDetected, PaymentConfirmed | ✓ | `sendCallback` HMAC-SHA256 `timestamp+nonce+body`; 4 endpoint (detected/confirmed/wrong/spam). Backend `BlockchainWebhooksController` + `BlockchainWebhookHandler` persists `BlockchainTransaction`. Test: `BlockchainWebhookEndpointTests` 9/9 PASS (`PaymentDetected_HappyPath_PersistsBlockchainTransactionRow`, `PaymentConfirmed_FlipsExistingRowToConfirmed`, `WrongTokenIncoming_PersistsRowWithActualTokenAddress`, `SpamTokenIncoming_PersistsRowAtTerminalConfirmed`) |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Sidecar Vitest | ✓ 63/63 PASS | `npm test` C:/projects/Escrow/sidecar-blockchain — 24 PaymentMonitorRules + 13 MonitorRegistry + 11 TronGridClient + 15 HdWalletService |
| Backend BlockchainWebhookEndpointTests | ✓ 9/9 PASS | `dotnet test --filter "FullyQualifiedName~BlockchainWebhookEndpointTests"` Skinora.API.Tests (SQLite in-memory). MissingHeaders/InvalidSignature/SteamSecretRejection/HappyPath/DuplicateIdempotent/UnknownPaymentAddress/PaymentConfirmedFlip/WrongTokenPersist/SpamTokenConfirmed |
| Backend SteamWebhookEndpointTests (regresyon) | ✓ 6/6 PASS | `dotnet test --filter "FullyQualifiedName~SteamWebhookEndpointTests"` — middleware route table refactor sonrası Steam yolu kırılmadı |
| Backend solution build | ✓ 0W/0E | `dotnet build Skinora.sln -c Release` |
| Backend dotnet format | ✓ verify-no-changes PASS | `dotnet format Skinora.sln --verify-no-changes --severity error` |
| Sidecar tsc build | ✓ | `npm run build` 0 hata |
| Sidecar eslint | ✓ | `npm run lint` 0 warning |
| Sidecar prettier (T71 touched files) | ✓ | `prettier --write` 7 değişiklik aynı PR'a dahil. 14 mevcut sidecar dosyasında pre-existing drift (T70+ K-future) — CI'da `format:check` enforced değil (sadece `tsc --noEmit`) |
| API.Tests full suite | ⚠ 345/372 PASS lokalde | 27 fail Docker-bound (TestContainers) — lokalde Docker Desktop kapalı, CI Linux runner'da PASS. Bu T70 ve önceki task'larla aynı pattern. |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS (bağımsız validator chat'i, 2026-05-16) |
| Bulgu sayısı | 0 |
| Düzeltme gerekli mi | Hayır |

**Validator bağımsız değerlendirme:**
- HARD STOP kapıları (Adım -1/0/0b) tümü temiz.
- 8 kabul kriteri: 7 ✓ + 1 ~ Kısmi (K3 event_index — TronGrid v1 endpoint dış API limit, proje sahibi onaylı Yaklaşım A, mitigation backend UNIQUE TxHash defense, multi-event-per-tx edge case T-future events API devri).
- Doğrulama kontrol listesi (08 §3.4 + finality): tam karşılandı.
- Test kanıtları: sidecar Vitest 63/63 ✓, backend `BlockchainWebhookEndpointTests` 9/9 ✓, `SteamWebhookEndpointTests` regresyon 6/6 ✓, Realtime 25/25 ✓, backend Release build 0W/0E, dotnet format Δ=0.
- Task branch CI run [25966028065](https://github.com/turkerurganci/Skinora/actions/runs/25966028065) (HEAD `91e6bcc`): **10/10 job SUCCESS** (1.Lint, 2.Build, 3.Unit, 4.Integration, 5.Contract, 6.Migration dry-run, 7.Docker backend + sidecar-blockchain, CI Gate, Detect changed paths; 0.Guard skip — PR-only beklenen).
- Lokal Testcontainers integration fail (Docker Desktop yokluğu) — F3 Gate Check'te dokümante edilmiş env limit, CI Linux runner'da 4.Integration job ✓.
- Güvenlik: secret sızıntısı yok, yeni dış bağımlılık yok, sidecar internal-key + backend HMAC-SHA256 + replay window + nonce SETNX katmanları test'le doğrulandı (MissingHeaders/InvalidSignature/SteamSecretRejection→401).
- Doküman uyumu: 06 §2.5/2.6/3.8 enum + CHECK constraint + token field semantiği (WRONG → ExpectedToken), 05 §3.3 (3sn polling + 20 blok solid finality), 08 §3.4 (phase 1/2 + Transfer filter + wrong/spam tablosu) birebir.
- Yapım raporu uyumu: tam — bağımsız verdict rapordaki kriter sonuçlarıyla örtüşüyor; K3'ün `~ Kısmi` işaretlemesi sapma değil, dış API kısıt + proje sahibi onaylı scope kararı.

## Altyapı Değişiklikleri

- Migration: **Yok** — `BlockchainTransaction` ve `PaymentAddress` entity'leri T25/T26'da kuruldu, T71 schema'ya dokunmuyor (yalnız mevcut tablolara INSERT/UPDATE).
- Config/env değişikliği:
  - **Backend:** `Webhook__BlockchainSharedSecret` env var (eğer set edilmezse blockchain webhook endpoint'leri 401 döner — Steam ile aynı pattern, T68'in K-future genişletmesi).
  - **Blockchain sidecar:** `INTERNAL_KEY`, `WEBHOOK_SECRET`, `TRON_API_KEY`, `PAYMENT_POLLING_INTERVAL_MS` (default 3000), `MIN_CONFIRMATIONS` (default 20) — `docker-compose.yml` ve sidecar `config/index.ts` üzerinden okunur.
- Docker değişikliği: `skinora-blockchain-sidecar` service environment block'una T71 env'leri eklendi. Backend service'e `Webhook__BlockchainSharedSecret` eklendi.

## Commit & PR

- Branch: `task/T71-blockchain-payment-monitoring`
- Commit: `e97a26c` (yapım) + `91e6bcc` (rapor PR/commit referansı) + validator finalize commit (bu commit)
- PR: #112 — https://github.com/turkerurganci/Skinora/pull/112
- CI: ✓ 10/10 PASS (run [25966028065](https://github.com/turkerurganci/Skinora/actions/runs/25966028065))

## Known Limitations / Follow-up

- **K1:** `WrongTokenIncoming` event'inde refund dispatch (allowlist'teki yanlış token için otomatik iade) **T73 TRC-20 transfer scope**'una devredildi. T71 sadece `BlockchainTransaction` (Type=`WRONG_TOKEN_INCOMING`, Status=DETECTED) kaydı yazar; T73 handler implement edildikten sonra `WRONG_TOKEN_REFUND` kaydı oluşturulur. Backend log'unda `"refund dispatch deferred to T72/T73"` mesajı bu bağı belgeler.
- **K2:** `PaymentConfirmed` üzerine Transaction state machine `PAYMENT_RECEIVED` geçişi **T72 amount validation** scope'una devredildi (tutar doğrulama T72 kabul kriteri 1). T71'de yalnızca `BlockchainTransaction.Status=CONFIRMED` flip eder; underpayment/overpayment ayrımı T72'de yapılır.
- **K3:** Idempotency anahtarı plan'da `txid + event_index` olarak belirtilmiş, ancak TronGrid v1 `/v1/accounts/{addr}/transactions/trc20` endpoint'i `event_index` expose etmez (canlı kontrol: 2026-05-16, response shape `{transaction_id, token_info, block_timestamp, from, to, type, value, meta.fingerprint}`). MVP'de tek Transfer event per tx dominant senaryo, dedup key olarak `transaction_id` yeterli; `BlockchainTransaction.TxHash` UNIQUE constraint defense-in-depth. Multi-event-per-tx edge case (1 tx içinde 2+ TRC-20 transfer aynı adrese) gözlenirse TronGrid events API (`/v1/accounts/{addr}/transactions/events`) entegrasyonu **T-future** olarak değerlendirilir.
- **K4:** Post-cancel monitoring (`PostCancelMonitor.ts` stub) **T75** scope'unda kalmaya devam. T71 ACTIVE phase polling kapsamı.
- **K5:** Reconciliation job (hot wallet bakiye ↔ deposit adresleri ↔ platform ledger) **T76** scope.
- **K6:** TronGrid 429/key failover (08 §3.6 fallback strategy) — T71'de basit `TronGridHttpError` raise eder, retry T-future (08 §3.5 retry tablosu). Mevcut implementation tek key kullanır, secondary key (`TRON_API_KEY_SECONDARY`) config'de tanımlı ama failover logic yok.
- **K7:** WebhookDeliveryError 4xx non-retryable durumunda payload sessizce drop edilir (sidecar log'lar, retry yok). 4xx genelde sidecar payload bug'ı anlamına geldiği için intentional — but operasyonel monitoring T-future'da `transfersTotal{status='DROPPED'}` metric'i eklenebilir.
- **K8:** `sidecar-blockchain` prettier drift 14 pre-existing dosyada (T70+ K-future). CI `format:check` enforced değil; ayrı chore PR'da temizlenir.
- **K9:** Backend handler `PaymentConfirmed` için prior `DETECTED` row yoksa "Unknown" döner — bu konservatif tercih (partial row yazmak istemiyoruz). Sidecar contract'ı zaten önce DETECTED sonra CONFIRMED akışını garanti ediyor (`MonitorRegistry.pendingFinality` map'i ile).

## Notlar

- **Working tree:** Temiz (Adım -1 kontrolü ✓).
- **Main CI startup:** Son 3 main run 3/3 SUCCESS (25962252835, 25962252826, 25959602313) ✓.
- **Dış varsayımlar (Adım 4):**
  - TronGrid `/v1/accounts/.../transactions/trc20` endpoint çalışıyor, `contract_address` + `only_confirmed` + `limit` + `fingerprint` parametreleri response'da gözlemlendi. **Kanıt:** 2026-05-16 canlı curl `https://api.trongrid.io/v1/accounts/TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t/transactions/trc20?only_confirmed=true&limit=1` → `data[].{transaction_id, token_info.address, block_timestamp, from, to, type, value}` + `meta.fingerprint`.
  - `walletsolidity/getnowblock` (POST) solid block döndürüyor. **Kanıt:** curl → `block_header.raw_data.number=82_757_932`.
  - `walletsolidity/gettransactioninfobyid` solid node lookup. (Test'te mock'la doğrulandı; canlı curl T70 sırasında).
  - tronweb 5.3.5 `{ TronWeb, Trx, Event }` named exports. **Kanıt:** `require('tronweb')` → `[providers, BigNumber, TransactionBuilder, Trx, Contract, Plugin, Event, version, utils]`.
  - Vitest test runner sidecar'da T70'te wired. ✓
  - Backend `WebhookSignatureMiddleware` path scope'u T71'de blockchain'e genişletilebilir (T68 K-future). ✓
  - **Plan'daki "event_index" varsayımı kısmi kırık** — TronGrid v1 endpoint'i `event_index` expose etmiyor (kanıt yukarıda). Scope onayında proje sahibi Yaklaşım A (txid-only) seçti, K3 forward-deferred. Bu task'ta event_index granularity için gerekli alternatif (events API entegrasyonu) yapılmadı, dokümantasyonda K3 olarak işaretlendi.
- **Squash-merge bundled-PR guard:** T71 commit'leri yalnızca `T71` prefix'i taşıyacak. Pre-existing `sidecar-blockchain` prettier drift'i (14 dosya) ayrı chore PR'a alınacak — bu PR'a katılmıyor.
