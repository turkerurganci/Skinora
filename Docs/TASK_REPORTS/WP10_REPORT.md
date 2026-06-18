# WP10 — Tron dayanıklılık (TronGrid resilience + per-event dedup + HD cache + gas config)

**Faz:** Pre-F6 (P3 — Operasyon) | **Durum:** ✓ Tamamlandı — bağımsız validator PASS (2026-06-18) | **Tarih:** 2026-06-18

---

## Yapılan İşler

WP10, blockchain para-katmanının dayanıklılığını dört eksende tamamlar (PRE_F6_PLAN WP10; backlog `tron-resilience` + `energy-gas-token-config`'in HD/gas kısmı).

1. **TronGrid 429 / key-suspension failover + retry (okuma yolu — `TronGridClient`):** Her istek `fetchResilient` üzerinden geçer. 429/403'te **anında** ikincil `TRON_API_KEY`'e geçer (ayrı rate-limit havuzu, 08 §3.6 — backoff yok); her iki key de throttled olduğunda kısa/sınırlı exponential backoff (`TRONGRID_RETRY_BACKOFF_BASE_MS`/`_CAP_MS`, `TRONGRID_MAX_RETRIES`), sonra `TronGridRateLimitError` fırlatır ve monitor bir sonraki tick'te (3 sn) yeniden poll eder. 5xx'te aynı key üzerinde sınırlı retry (sağlayıcı bozuk, key değil). Sticky key rotasyonu + `tronApiErrorsTotal{error_type=key_failover}` metriği.
2. **event_index dedup (txid-only → txid + gerçek on-chain log index):** `TronGridClient.resolveTransferEventIndices` `gettransactioninfobyid` `log[]` dizisini çekip ilgili `Transfer` log'unu (kontrat + alıcı + `value`) eşler, gerçek log array index'ini döndürür. `MonitorRegistry` + `PostCancelMonitor` dedup'ı `seenTxHashes`'ten `seenEvents` (`${txHash}:${eventIndex}`)'e geçti; her transfer kaydı kendi log entry'sine `value` ile eşlenir (per-event tutar otantik kalır), log yoksa index 0'a düşülür (status-quo, regresyonsuz). 5 webhook payload'una `eventIndex` eklendi (sidecar + backend). Backend `BlockchainTransaction.EventIndex` kolonu + `(TxHash, EventIndex)` UNIQUE; webhook handler dedup + confirmation lookup `(TxHash, EventIndex)`-keyed.
3. **HD address cache:** `HdWalletService.derive(index)` per-index `DeriveResult` memoize eder (BIP-32 deterministik). **Yalnız public adres cache'lenir** — `deriveSigner` private key'i her seferinde yeniden hesaplar, asla saklanmaz (05 §3.3 signing isolation).
4. **Gas fee config'lenebilir:** `TronTransferClient` `feeLimit` default'u hardcoded `100_000_000`'dan `config.transferFeeLimitSun` (`TRANSFER_FEE_LIMIT_SUN`, default 100 TRX)'e taşındı; per-request `feeLimitSun` override'ı korunur.

**Owner kararları (AskUserQuestion, 2026-06-18):** event_index = **full per-event (sidecar + backend migration)**, index kaynağı = **gerçek on-chain log index** · 429/failover kapsamı = **yalnız okuma yolu** · backoff = **poll-dostu kısa retry**.

**Dış varsayım bulgusu (task Adım 4):** TronGrid `/v1/accounts/{address}/transactions/trc20` endpoint'i `event_index` alanı **döndürmez** — canlı probe (`api.trongrid.io`, alanlar: `transaction_id, token_info, block_timestamp, from, to, type, value`) + resmi doküman ile doğrulandı. Backlog'un "event_index dedup (txid-only)" varsayımı kırıktı; gerçek index `gettransactioninfobyid` log dizisinden türetildi.

## Etkilenen Modüller / Dosyalar

**Sidecar (`sidecar-blockchain`):**
- `src/config/index.ts` — `tronGridMaxRetries`, `tronGridRetryBackoffBaseMs`/`CapMs`, `transferFeeLimitSun` knobs
- `src/tron/TronGridClient.ts` — `fetchResilient` (429/403 failover + bounded retry), `resolveTransferEventIndices` + `extractTransferLogEntries`, `TronGridRateLimitError`
- `src/monitor/MonitorRegistry.ts`, `src/monitor/PostCancelMonitor.ts` — per-event dedup (`seenEvents`), `resolveEventIndex`, eventIndex emission, per-event finality
- `src/webhook/WebhookPayloads.ts` — `eventIndex` 5 payload'a
- `src/wallet/HdWalletService.ts` — `addressCache`
- `src/tron/TronTransferClient.ts` — `feeLimit` config'ten
- Testler: `TronGridClient.test.ts`, `MonitorRegistry.test.ts`, `PostCancelMonitor.test.ts`, `HdWalletService.test.ts`, `TronTransferClient.test.ts`

**Backend:**
- `Skinora.Transactions/Domain/Entities/BlockchainTransaction.cs` — `int? EventIndex`
- `Skinora.Transactions/Infrastructure/Persistence/BlockchainTransactionConfiguration.cs` — `EventIndex` property + `UQ_BlockchainTransactions_TxHash_EventIndex` (eski TxHash-only yerine) + `CK_BlockchainTransactions_EventIndex`
- `Skinora.Transactions/Application/Webhooks/BlockchainWebhookPayloads.cs` — `EventIndex` 5 DTO'ya
- `Skinora.Transactions/Application/Webhooks/BlockchainWebhookHandler.cs` — `ExistsByTxHashAndEventIndexAsync`, confirmation lookup `(TxHash, EventIndex)`, `EventIndex` row yazımı
- `Skinora.Shared/Persistence/Migrations/20260618093930_WP10_AddBlockchainTxEventIndex.cs` (+ Designer + snapshot)
- Test: `Skinora.API.Tests/Integration/BlockchainWebhookEndpointTests.cs` (+2 per-event test + helper)

**Docs:** 06 §3.8 (EventIndex field + `(TxHash,EventIndex)` UNIQUE + CHECK), 08 §3.4 (event_index çözüm kaynağı) + §3.5 (retry/backoff reconciliation), PRE_F6_PLAN (WP10 ⏳ + migration-bearing), DEFERRED_BACKLOG (tron-resilience ✅ + energy-gas-token-config kısmen ✅).

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | TronGrid 429/403 → ikincil key failover | ✓ | `TronGridClient.test.ts` "fails over to the secondary key immediately on a 429" + "gives up with TronGridRateLimitError once both keys are throttled" |
| 2 | 5xx → sınırlı retry (key rotasyonu yok) + poll-dostu | ✓ | "retries a 5xx on the same key with bounded backoff" (aynı key 2 çağrı); backoff `paymentPollingIntervalMs` altında |
| 3 | event_index dedup = txid + gerçek on-chain log index | ✓ | `extractTransferLogEntries` 4 test (index 0 / non-zero / multi / contract-mismatch) + `MonitorRegistry` per-event 5 test (multi-transfer → 2 emit, fallback index 0) |
| 4 | Backend per-event tekillik + confirmation matching | ✓ | `BlockchainWebhookEndpointTests` "SameTxHashDifferentEventIndex_CreatesSeparateRows" (2 satır + idempotent) + "PaymentConfirmed_MatchesTheRowForItsEventIndex" (idx1 CONFIRMED, idx0 DETECTED) |
| 5 | HD address cache (private-key cache'lenmez) | ✓ | `HdWalletService.test.ts` "caches the derived address per index (returns the same instance)" |
| 6 | Gas fee config'lenebilir + override | ✓ | `TronTransferClient.test.ts` "defaults feeLimit to transferFeeLimitSun, override wins" (100 TRX default / 7 TRX override) |
| 7 | Migration + drift yok | ✓ | `WP10_AddBlockchainTxEventIndex`; `dotnet ef migrations has-pending-model-changes` → "No changes" |
| 8 | Common path (tek-transfer) regresyonsuz | ✓ | eventIndex=0; mevcut webhook/monitor testleri yeşil (sidecar 161/161, webhook 17/17) |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Sidecar (vitest) | ✓ 161/161 | `npx vitest run` — +25 yeni test (resilience 5, log-resolve 6, monitor per-event 5, HD cache 1, gas 1, + güncellemeler) |
| Sidecar tsc/lint/prettier | ✓ | `tsc --noEmit` 0; `eslint src/` 0; prettier (WP10 dosyaları) clean |
| Backend webhook (SQLite) | ✓ 17/17 | `BlockchainWebhookEndpointTests` (+2 per-event) |
| Backend Transactions unit | ✓ 101/101 | `Category=Unit` |
| Backend Shared | ✓ 385/385 | contract + enum parity + model |
| Backend recon/hotwallet | ✓ 29/29 | BlockchainTransaction reader regresyonu temiz |
| Backend build | ✓ 0W/0E | Debug + Release |
| Migration drift | ✓ | has-pending-model-changes "No changes" |
| Integration (geniş) | CI-authoritative | lokal hedef suite'ler yeşil; tam Integration CI'da |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** — bağımsız validator (ayrı chat, 2026-06-18, kendi verdict'i rapor görülmeden) |
| Bulgu sayısı | 0 bloke-edici (3 non-blocking gözlem) |
| Düzeltme gerekli mi | Hayır |

**Kapılar:** Adım -1 working tree temiz · Adım 0 main son-3 success (`27746724695`/`27746724678`/`27719423184`) · Adım 0b repo memory mevcut · Adım 8a task CI HEAD `a087dbf` run [`27752117542`](https://github.com/turkerurganci/Skinora/actions/runs/27752117542) **11/11 job success** (Lint/Build/Unit/Integration/Contract/Migration dry-run/Docker×2/Gate) + `00c26d1b` run `27751540086`.

**Validator lokal koşumu:** sidecar `tsc --noEmit` exit 0 + **vitest 161/161**; backend `dotnet build -c Release` **0W/0E** + Transactions `Category=Unit` **101/101**; snapshot diff = migration ile birebir (EventIndex kolon + `UQ_BlockchainTransactions_TxHash`→`_TxHash_EventIndex` + `CK_..._EventIndex`) → **drift yok**. Integration + Migration dry-run gerçek SQL Server'da CI-authoritative (lokal Docker yok).

**Bağımsız teyit (4 kalem):**
- **(1) Resilience okuma-yolu:** `fetchResilient` her okuma çağrısını sarar (`listTrc20`/`getNowSolidBlock`/`getAccountBalances`/`getTransactionInfoById`/`resolveTransferEventIndices`); 429/403'te `keysTriedThisThrottle < keys.length` iken anında failover (sleep yok, `keyIndex++`), her iki key throttled olunca `backoffAttempts <= maxRetries` sınırlı backoff → `TronGridRateLimitError` (alt-tip `TronGridHttpError` → mevcut `instanceof` korunur); 5xx aynı key bounded retry; döngü `backoffAttempts` ile sınırlı (sonsuz döngü yok). Transfer/broadcast yolu (`TronTransferClient.getTransactionStatus`) bilinçli **hariç** (owner Q2, çift-retry önleme, 08 §3.5 not).
- **(2) Per-event dedup:** `resolveEventIndex` log entry'sini `value` ile eşler + **iç `seenEvents` kontrolü** aynı-değerli çoklu transferi ayrı index'lere ayırır; `(txHash,eventIndex)` dedup MonitorRegistry + PostCancelMonitor'da simetrik; backend `ExistsByTxHashAndEventIndexAsync` + confirmation lookup `(TxHash,EventIndex)`-keyed (çok-event'te yanlış satır flip edilmez); per-row amount validation değişmedi. Composite UNIQUE `(TxHash,EventIndex) WHERE TxHash IS NOT NULL`: outbound NULL EventIndex + distinct TxHash → SQL Server NULL-equal farkı irrelevant (çakışma yok); inbound EventIndex hep ≥0.
- **(3) HD cache:** `derive` yalnız `DeriveResult` (public adres) memoize eder (aynı instance döner); `deriveSigner` private key'i her çağrıda yeniden hesaplar, cache'lemez (05 §3.3 signing isolation korunur).
- **(4) Gas config:** `feeLimit = request.options?.feeLimitSun ?? config.transferFeeLimitSun` (`TRANSFER_FEE_LIMIT_SUN` default 100 TRX); per-request override korunur; hardcoded magic number kaldırıldı.

**Mini güvenlik:** secret sızıntısı yok (API key `TRON-PRO-API-KEY` header'da, log'larda key yok; mnemonic redact'li); auth/endpoint değişmedi (webhook'lar signature-gated); EventIndex DB CHECK `>= 0` ile backstop'lu; **0 yeni bağımlılık** (built-in fetch + mevcut TronWeb/ethers; package.json/csproj değişmedi).

**Non-blocking gözlemler (bloke etmez):** N1 — çok-transfer + solidity log-lag'de index 0 fallback 2.+ event'i status-quo gibi davranır (owner-onaylı, para kaybı yok, doc'lu). N2 — `RateLimitedQueue` hâlâ ölü kod (pre-existing, WP10 kapsamı dışı, follow-up). N3 — `measure()` hata yolunda 'ok'-timer'ı bırakıp ~0ms 'error' gözlemi kaydeder (yalnız observability, sızıntı yok).

**Yapım raporu karşılaştırması:** Tam uyumlu — AC tablosu (8/8 ✓) bağımsız verdict'imle örtüşüyor; uyuşmazlık yok.

## Altyapı Değişiklikleri

- **Migration:** Var — `WP10_AddBlockchainTxEventIndex` (yeni nullable `EventIndex` kolonu + `UQ_BlockchainTransactions_TxHash` → `UQ_BlockchainTransactions_TxHash_EventIndex` recreate + `CK_BlockchainTransactions_EventIndex >= 0`; **şema-only, seed yok**, SystemSettings sayısı değişmez).
- **Config/env değişikliği:** Var — sidecar env (opsiyonel, default'lu): `TRONGRID_MAX_RETRIES`, `TRONGRID_RETRY_BACKOFF_BASE_MS`, `TRONGRID_RETRY_BACKOFF_CAP_MS`, `TRANSFER_FEE_LIMIT_SUN`. `TRON_API_KEY_SECONDARY` zaten config'te tanımlıydı (artık kullanılıyor).
- **Docker değişikliği:** Yok.
- **Yeni bağımlılık:** Yok (built-in fetch + mevcut TronWeb/ethers).

## Commit & PR

- Branch: `task/WP10-tron-resilience`
- Commit: `89a2a15` (impl) + `00c26d1b` (rapor PR-ref)
- PR: [#180](https://github.com/turkerurganci/Skinora/pull/180)
- CI: ✓ PASS — HEAD `00c26d1b` run [`27751540086`](https://github.com/turkerurganci/Skinora/actions/runs/27751540086) **tüm job success** (Lint/Build/Unit/Integration/Contract/Migration dry-run/Docker×2/Gate). (İlk run `89a2a15` concurrency ile cancelled — rapor-ref push supersede etti, failure değil.)

## Known Limitations / Follow-up

- **Çok-transfer + log lag:** Solidity node detection anında log'u yüzeye çıkarmamışsa index 0'a düşülür; bu nadir durumda çok-transferli tek-txid'in 2.+ event'i status-quo (txid-collapse) gibi davranır — **para kaybı/çift-kredi yok** (mevcut davranışla aynı). `only_confirmed=true` kayıtlar solidleştiği için pratikte log mevcuttur.
- **429 resilience yalnız okuma yolu (owner Q2):** Transfer/delegation broadcast 429'ları backend dispatch job'ı tarafından retry edilir (08 §3.5 `5s/15s/45s`); sidecar transfer yoluna eklenmedi (çift-retry önleme).
- **Sidecar→backend runtime ayar propagasyonu** (gas/key/cadence) hâlâ restart-bound → WP14 (`setting-sidecar-propagation`).
- **`RateLimitedQueue` ölü kod** (hiçbir yerde wire değil) — pre-existing; WP10 kapsamı değil (429 *handling* eklendi, proaktif rate-limit değil). ✅ **Çözüldü** (validator non-blocking N2, follow-up PR `task/WP10-followup-nonblocking`): dosya silindi (0 referans, test yok).
- **`measure()` metrik hata yolu** (validator non-blocking N3, pre-existing): hata yolunda başlatılan 'ok'-timer terk edilip ~0 ms 'error' gözlemi kaydediliyordu → error-süre histogramı 0'a çarpıtılıyordu. ✅ **Çözüldü** (aynı follow-up PR): timer `endpoint` ile başlatılır, `status` end-time'da çözülür → her iki yol gerçek süreyi kaydeder.
- **N1 (multi-transfer + log-lag)** — owner-onaylı limitasyon olarak **bilinçli bırakıldı** (para-yolu davranış değişikliği; `only_confirmed=true` solid → log pratikte mevcut, edge erişilemez).

## Notlar

- **Working tree (Adım -1):** temiz (session başında WP9 branch, sonra main'den WP10 branch açıldı).
- **Main CI startup (Adım 0):** son 3 run success (WP9 `27746724695`/`27746724678`, WP8 `27719423184`).
- **Dış varsayımlar (Adım 4):** TronGrid trc20 endpoint `event_index` **yok** (kırık varsayım → log-index'ten çözüldü, canlı probe kanıtlı); `TRON_API_KEY_SECONDARY` config'te mevcut (`config:65`); yeni paket yok. Owner Q1=full-per-event kararıyla scope sidecar-only'den full-stack+migration'a büyüdü.
- **Anlama fazı:** TronGridClient/MonitorRegistry/PostCancelMonitor/HdWalletService/TronTransferClient + backend webhook handler/AmountValidationService/BlockchainTransaction config dosya-satır haritalandı; money-path per-event uyumu (per-row amount validation, common path eventIndex=0 identik) doğrulandı.
