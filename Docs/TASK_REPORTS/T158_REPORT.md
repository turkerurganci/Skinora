# T158 — Ethereum kurulumu: ayarlar, sırlar, alarmlar, runbook

**Faz:** F8 | **Durum:** ⏳ Devam ediyor — doğrulama ✗ FAIL → düzeltme turu bitti, yeniden doğrulama bekliyor | **Tarih:** 2026-10-19

---

## Yapılan İşler

- **(h) ⚠ PARA — kalıcı "şu bloktan itibaren izle" (T151 doğrulamasından):**
  - Ethereum servisi `POST /api/wallet/derive` cevabına `watchFromBlock` ekler — servisin o an okuduğu son `finalized` blok (ondalık metin). Henüz baş okunmadıysa 502 `FINALIZED_HEAD_UNAVAILABLE` (adres verilmez). (`sidecar-ethereum/src/api/walletHandlers.ts`)
  - Backend `PaymentAddress.MonitorFromBlock` (bigint NULL, CHECK ≥ 0; migration `20261018211249_T158_PaymentAddressMonitorFromBlock`). Ethereum derive cevabında alan yoksa/bozuksa adres ayrılmaz (`BlockchainNetworkPolicy.ReportsMonitorStartBlock`, `HttpBlockchainSidecarClient`).
  - Değer her izleme başlatmasında gönderilir: `EnsurePaymentMonitorJob` (dakikalık yeniden kurma), `PaymentMonitorStartRequestedEvent` / `PostCancelMonitorStartRequestedEvent` (yeni `WatchFromBlock` alanı, eski outbox satırları için varsayılan null), `PostCancelMonitorRecoveryHook`. Null'sa gövdeye hiç yazılmaz (TRON gövdesi değişmedi).
  - Servis (`DepositRegistry`, `PaymentScanner`): `watchFromBlock`'u olan depozit `watchFromBlock + 1`'den, **sıfır bakiye tabanıyla** okunur (baş gerideyse başa indirilir); mutabakat tabanı o bloktan başlar. Başlangıç bloğu olmayan (T158 öncesi) depozitlere T151'in geri okuma kuralı uygulanır. Bozuk değer 400.
- **(f) ⚠ PARA — sweep sonrası iade sıcak cüzdandan (T154 doğrulamasının devir notu):**
  - Backend `RefundSourceResolver` (yeni): yalnız Ethereum (`RefundsAfterSweepFromHotWallet`) ve yalnız `BUYER_REFUND`. Sweep `CONFIRMED` → sıcak cüzdan; `PENDING`/`DETECTED` → bekle (`SWEEP_IN_FLIGHT`, `Deferred` — deneme hakkı harcanmaz); `FAILED`/yok → depozit.
  - `PaymentRefundToBuyerConsumer` ücreti aynı kararla fiyatlar (yeni `IChargedGasFeeResolver.ResolveHotWalletRefundFeeAsync` — `fromAddress`'siz, yani sıcak cüzdan). `OutgoingTransferDispatchJob` gönderimi `FromHotWallet` ile ister, satırın `FromAddress`'ini sıcak cüzdana yazar (defter mutabakatı sıcak çıkış sayar); sıcak adres tanımsızsa satır bekler (`HOT_WALLET_NOT_CONFIGURED`).
  - `HttpBlockchainTransferClient`: `source: "HOT_WALLET"` gövdesi, depozit alanı yok.
  - Servis (`TransferService.hotWalletRefund`, `transferHandlers.refundHandler`): sıcak anahtar imzalar; yalnız USDT, `WRONG_TOKEN_REFUND` asla; tek gönderim tavanı; **sıcak cüzdanın günlük tavanı** (`TransferGuard` imzalanıp görünmeyen sıcak iadeleri de sayar); tavanın tamamı (`CAP_REFERENCE_GAS.HOT_REFUND` = 64.031, yakıt yok); sıcak cüzdanın kendi ETH'si. Kimlik karşılaştırmasına imzalayan rol eklendi.
- **(g) ⚠ PARA — günlükte olmayan hash (T152 doğrulamasından):**
  - Backend `OutgoingTransferConfirmationJob` her Ethereum durum sorgusuna satırın transferini ekler (`TransferStatusExpectation`: kontrat — yanlış token iadesinde gelen token; gönderici — `FromAddress`, payout'ta ağın sıcak cüzdanı; alıcı; tutar). Sorgu `?contract=&from=&to=&amount=` yalnız `VerifiesUnjournalledTransferStatus` ağında (TRON isteği değişmedi).
  - Servis (`TransferService.status`) günlükte olmayan, zincirdeki hash'te makbuzda tam bu `Transfer` olayını arar (servisin bildiği USDT/USDC kontratı, gönderici, alıcı, tam tutar, kaldırılmamış, üç konulu). Yoksa ya da beklenti gelmediyse `contractRet: "UNKNOWN"` + `skinora_ethereum_transfer_status_unverified_total{reason}`. Yarım/bozuk beklenti 400 `INVALID_STATUS_EXPECTATION`.
  - Backend `UNKNOWN` → yeni `TransferStatusOutcome.Unverified`: satır `DETECTED` kalır (asla `CONFIRMED`/`FAILED`), `ErrorMessage` `TRANSFER_UNVERIFIED`, onay rotasyonunda kalır.
- **(i) Bakiye isteği parçaları + tur alarmı:** `ReconciliationService.ReadBalancesAsync` Ethereum'da en çok 20 adreslik parça (`BalanceSnapshotChunkSize`); bir parça düşerse tur düşer (kısmi kontrol yok). Yeni `IReconciliationRunObserver` → `skinora_backend_reconciliation_last_run_failed{network}` + `_last_success_timestamp_seconds{network}`; kural `reconciliation-network-failed`.
- **(j) Webhook 401:** `WebhookDeliveryError.retryable` 401'i kapsar (aralık geçilmez, olay düşürülmez); `skinora_ethereum_webhook_rejected_total{status}`; kural `ethereum-webhook-signature-refused` (critical).
- **(k) Tarama alarmı:** `ethereum-monitor-scan-failing` — `increase(skinora_ethereum_monitor_scan_failures_total[15m]) > 20`, critical.
- **(a) Bekleyen satır sayısı alarmı:** `PrometheusOutboundQueueObserver` ağ başına `skinora_backend_outbound_pending_rows_alarm_threshold` yayımlar (ayar `OutboundQueueAlarm__PendingRowsThreshold`, compose `OUTBOUND_PENDING_ROWS_ALARM_THRESHOLD`, varsayılan 25); kural `outbound-transfer-backlog` (oran > 1, 15 dk).
- **Temel kriter alarmları:** `ethereum-rpc-latency-high` (RPC p95 > 3 sn, 15 dk); `ethereum-transfer-status-unverified` (critical). `finalized` ilerlemiyor (`ethereum-finalized-stalled`, T150) ve yakıt bakiyesi (`ethereum-wallet-eth-low`, T153) zaten vardı — runbook adımları yazıldı.
- **(b) Outbox sıra testi:** `OutboxTests.T158b_Fresh_Events_Are_Dispatched_Before_Due_Retries_In_The_Same_Poll` — bir turda yeni olay ve vadesi gelen yeniden deneme; sıra kaydedilir.
- **(c) Tohum / anahtar yalnız secret dosyasından:** `config/index.test.ts` — her olası env adına geçerli tohum ve anahtar konur, dosyalar yok → tohum yoksa servis açılmaz, anahtarlar null; ayrıca `config/index.ts` kaynağında `*_FILE` dışında `MNEMONIC|SEED|PRIVATE_KEY` içeren env okuması olmadığını tarayan kod düzeyi test.
- **(d)** `ethereum-finalized-stalled`'ın servis kapalıyken sustuğu DEPLOY_RUNBOOK §J.2'de (ve §D parite satırında); o durumu backend sağlık yoklaması görür.
- **(e) Fiyat kaynağı probu:** `scripts/probe-eth-price-sources.sh` (servisin iki URL'si ve kabul kuralları; 451/403/429/cevapsızlık adlandırılır; `--json`). Yerelden koşuldu (aşağıda).
- **Compose / env:** backend'e `OutboundQueueAlarm__PendingRowsThreshold`; `ETHEREUM_SIDECAR_BASE_URL` yorumları "servis tamam, doldurmak açılış kararıdır" olarak güncellendi (varsayılan boş kalır — K1).
- **sidecar-fake:** Ethereum derive `watchFromBlock` döndürür (backend artık şart koşuyor); `source: "HOT_WALLET"` iade gövdesini gerçek servis gibi kabul/ret eder.
- **Sözleşme örnekleri (iki taraf aynı dosyayı okur):** `sidecar-ethereum/contracts/wallet-derive/response.json`, `monitor/start.request.json`, `transfer-refund/hot-wallet.request.json`, `transfer-status/unknown.response.json`.
- **Belgeler:** DEPLOY_RUNBOOK yeni **§J** (J.1 kurulum sırası · J.2 alarm tablosu · J.3 uzun kesinti · J.4 yanlış EVM ağına gelen paranın elle kurtarılması · J.5 doğrulanamayan gönderim · J.6 sweep sonrası iade) + §0/§B/§D satırları; 05 v3.25 (§3.3a "Uygulandı (T158)"); 06 v6.32 (§3.7 `MonitorFromBlock`); 08 v3.36 (§3.0, §3.7.4, §3.7.6, §3.7.8); 11 v1.16 ("T158 kararları" + §7.6 satırları).

## Etkilenen Modüller / Dosyalar

- `sidecar-ethereum/src/`: `api/{walletHandlers,monitorHandlers,transferHandlers,routes}.ts`, `monitor/{DepositRegistry,PaymentScanner}.ts`, `transfer/{TransferService,TransferGuard,FeePolicy}.ts`, `webhook/WebhookClient.ts`, `metrics.ts`; testler (`PaymentScanner.outage.test.ts` yeni) ve `contracts/` (dört yeni örnek).
- `backend/src/`: Shared (`BlockchainNetworkPolicy`, iki olay, migration + snapshot), Transactions (`PaymentAddress` + konfigürasyon, `PaymentAddressAllocator`, `HttpBlockchainSidecarClient`, `IBlockchainSidecarClient`, `EnsurePaymentMonitorJob`, `PaymentMonitorStartDispatcher`, `PostCancelMonitor{Starter,StartDispatcher,RecoveryHook}`, `TransactionReadinessService`, `RefundSourceResolver` (yeni), `OutgoingTransfer{Dispatch,Confirmation}Job`, `HttpBlockchainTransferClient`, `IBlockchainTransferClient`, `PaymentRefundToBuyerConsumer`, `ChargedGasFeeResolver`), API (`ReconciliationService`, `IReconciliationRunObserver` + Prometheus uygulaması (yeni), `PrometheusOutboundQueueObserver`, `TransactionsModule`).
- `sidecar-fake/src/routes/blockchain.ts`, `infra/grafana/provisioning/alerting/rules.yml` (6 yeni kural), `docker-compose.yml`, `.env.example`, `scripts/probe-eth-price-sources.sh` (yeni).
- `Docs/`: DEPLOY_RUNBOOK, 05, 06, 08, 11, IMPLEMENTATION_STATUS, bu rapor; `.claude/memory/MEMORY.md`.

## Kabul Kriterleri Kontrolü

| Kriter | Durum | Kanıt |
|---|---|---|
| compose/env | ✓ | backend `OutboundQueueAlarm__PendingRowsThreshold`; `ETHEREUM_SIDECAR_BASE_URL` açılış notu (compose, `.env.example`, §B); Ethereum servisi env'leri T150–T154'ten tam (composeWiring testi yeşil) |
| Docker secret | ✓ | Değişiklik gerekmedi (T150); kurulum sırası §J.1 adım 1; (c) testi tohum/anahtarın env'den okunmadığını sabitler |
| Grafana alarmları: finalized ilerlemiyor, RPC gecikmesi, yakıt bakiyesi | ✓ | `ethereum-finalized-stalled` (T150) + runbook; `ethereum-rpc-latency-high` (yeni, `alertRuleWiring.test.ts`); `ethereum-wallet-eth-low` (T153) + runbook |
| DEPLOY_RUNBOOK Ethereum bölümü + yanlış EVM ağı elle kurtarma | ✓ | §J (J.4 kurtarma adımları; otomatik kurtarma yok, alıcı destek talebi açar) |
| (a) ağ başına bekleyen satır sayısı metriği + eşik alarmı | ✓ | `outbound-transfer-backlog`; `PrometheusOutboundQueueObserverTests.T158a_*` |
| (b) outbox sıra testi | ✓ | `OutboxTests.T158b_*`; `due.Concat(fresh)` bozması **yakalandı** |
| (c) tohum yalnız secret dosyasından | ✓ | `config/index.test.ts` T158 (c) üç test; env yedeği bozmaları (iki biçim) **yakalandı** |
| (d) `or vector(0)` notu | ✓ | DEPLOY_RUNBOOK §J.2 + §D; 08 §3.7.8 |
| (e) fiyat kaynağı erişim probu | ✓ (yerel) / ⚠ üretim sunucusu yok | `scripts/probe-eth-price-sources.sh`; ölçüm aşağıda; üretimden koşulması §J.1 adım 4 (açılış şartı) |
| (f) ⚠ PARA sweep sonrası iade sıcak cüzdandan | ✓ | dispatcher 10 test, consumer 5 test, servis 9 test, sözleşme örneği; bozmalar aşağıda |
| (g) ⚠ PARA günlükte olmayan hash → doğrula / `UNKNOWN` + alarm | ✓ | servis 10 test (+ handler 6), backend sözleşme 3 + onay işi 3 test; bozmalar aşağıda |
| (h) ⚠ PARA kalıcı başlangıç bloğu + mutabakat tabanı + 7.200 bloğu aşan kesinti testi | ✓ | `PaymentScanner.outage.test.ts` (6), route/handler testleri, backend sözleşme 7 + dispatcher 2 + entegrasyon 2 test |
| (i) bakiye parçaları + tur düşünce admin alarmı | ✓ | `ReconciliationServiceTests.T158_*` (3), `reconciliation-network-failed` |
| (j) webhook 401 | ✓ | yeniden denenir + ayrı alarm; `PaymentScanner.test.ts` 401 testi |
| (k) `monitor_scan_failures_total` alarmı | ✓ | `ethereum-monitor-scan-failing` |
| Geçici hatada FAILED olmama + T152 günlük/idempotency kuralları bozulmadı | ✓ | yeni bekleme yolları `Deferred` (deneme hakkı harcanmaz, FAILED değil); `Unverified` FAILED değil; sıcak iade aynı günlük/kimlik yolundan (replay, `IDEMPOTENCY_CONFLICT` testleri) |

## Test Sonuçları

| Süit | Komut | Sonuç |
|---|---|---|
| sidecar-ethereum | `npx vitest run` | **767/767** (önce 713) · `eslint` temiz · `tsc --noEmit` temiz · prettier (LF) temiz |
| sidecar-fake | `npx vitest run` | **120/120** |
| backend unit | `dotnet test Skinora.sln --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` | Transactions 919 · API 130 · Shared 568 · Platform 157 · diğerleri yeşil; Notifications 16 kırmızı = Docker gerektiren testler (yerelde Docker kapalı; CI'da koşar, T158'le ilgisiz) |
| backend contract | `--filter "FullyQualifiedName~.Contract"` | 34 + 24 + 5, hepsi yeşil (yeni `EthereumT158ContractTests` 16) |
| backend integration | CI | Yerelde koşulmadı (SQL Server testcontainer — Docker kapalı; yerel yığın `unless-stopped` ile Nile sidecar'ı da kaldırdığı için bilinçli açılmadı). Yeni 2 entegrasyon testi (`PaymentAddressAllocatorTests.T158h_*`, `EnsurePaymentMonitorJobTests.T158h_*`) ve migration CI'da |

**Bozma ölçümü (PARA kuralları + kriterler):**

- Servis (26 bozma, `mutate_sidecar.py`): **24 yakalandı**, 2 yeşil kaldı — `M12` bilinen kontrat kapısını kaldırmak (beklenen kontrattan bir log yoksa sonuç zaten `UNKNOWN`; fark yalnız backend bilinmeyen bir kontrat adlandırırsa çıkar — savunma katmanı, eşdeğere yakın) ve `M21` kimlik karşılaştırmasından imzalayan rolü çıkarmak (depozit iadesi ile sıcak iadenin `depositIndex`'i zaten farklı — eşdeğer). Yakalananlar: kalıcı blok yok sayılır / başa indirme yok / sıfır taban yok / handler bloğu düşürür / bozuk blok kabul / başsız derive 200 / günlüksüz hash = SUCCESS / tutar, gönderici, alıcı, kontrat karşılaştırılmaz / sayaç yok / beklenti iletilmez / yarım beklenti kabul / sıcak iade depozit payıyla / günlük tavan atlanır (iki yerden) / tek tavan atlanır / depozit alanı kabul (iki yerden) / 401 kalıcı (iki test) / tohum ve sıcak anahtar env'den.
- Backend (17 bozma, `mutate_backend.py` + elle B11): **17 yakalandı** — yoldaki sweep = sıcak cüzdan / her iade türü sweep'i izler / TRON da izler / dispatcher depozitten gönderir / tüketici depoziti fiyatlar / başlangıç bloğu olmayan Ethereum derive kabul / `UNKNOWN` → Failed / iş `Unverified` satırı onaylar / beklenti gönderilmez / payout beklentisinde sıcak cüzdan yok / bakiye parçalanmaz / düşen parça atlanır / düşen tur bildirilmez / satır eşiği yayımlanmaz / sorguya beklenti eklenmez / izleme gövdesi bloğu düşürür / olay bloğu düşürür. Ek: `OutboxDispatcher` `due.Concat(fresh)` yakalandı.

**(e) Fiyat kaynağı probu — yerel ölçüm** (üretim sunucusu yok; Türkiye ev bağlantısı, host `DESKTOP-1LTKGUN`):

```
$ bash scripts/probe-eth-price-sources.sh
Ethereum price source probe — 2026-10-18T21:48:28Z — host DESKTOP-1LTKGUN
  Binance   HTTP 200 (0.344506s): usable — 2365.07731938 USDT, 1s old.
  CoinGecko HTTP 200 (0.426380s): usable — 2363.14 USDT, 52s old.
  Agree within 2 %: yes
  => OK — both sources usable and within 2 %: a fresh Ethereum service can accept a price.
rc=0

$ PROBE_BINANCE_URL='https://httpbin.org/status/451' bash scripts/probe-eth-price-sources.sh
  Binance   HTTP 451 (0.681943s): HTTP 451 — the source refuses this host's region (Binance geo-block). Permanent: decide a different egress or source before opening Ethereum.
  CoinGecko HTTP 200 (0.397325s): usable — 2363.34 USDT, 40s old.
  => FAIL — only one source is usable: a FRESHLY STARTED service cannot accept a price (two sources required at start, T154 doğrulaması). Do not open Ethereum from this host until both answer.
rc=1

$ PROBE_BINANCE_URL='https://httpbin.org/status/451' PROBE_COINGECKO_URL='https://httpbin.org/status/429' bash scripts/probe-eth-price-sources.sh
  => FAIL — neither source is usable from this host: the Ethereum service will never price a send (ETH_PRICE_UNAVAILABLE).
rc=1
```

Betik URL'leri servisin koduyla aynı (`sidecar-ethereum/src/price/EthPriceService.ts` satır 44–46). Binance'in gerçek 451 cevabı Türkiye'den üretilemedi; başarısızlık biçimi httpbin ile prova edildi.

## Altyapı Değişiklikleri

- Migration `20261018211249_T158_PaymentAddressMonitorFromBlock` (kolon + CHECK). Paralel T157 de migration eklerse model snapshot'ı ikinci birleşen tarafta yeniden üretilmeli.
- Grafana: 6 yeni kural (critical: `ethereum-monitor-scan-failing`, `ethereum-webhook-signature-refused`, `ethereum-transfer-status-unverified`; warning: `ethereum-rpc-latency-high`, `outbound-transfer-backlog`, `reconciliation-network-failed`).
- Yeni env: `OUTBOUND_PENDING_ROWS_ALARM_THRESHOLD` (backend). Yeni metrikler: `skinora_ethereum_transfer_status_unverified_total`, `skinora_ethereum_webhook_rejected_total`, `skinora_backend_outbound_pending_rows_alarm_threshold`, `skinora_backend_reconciliation_last_run_failed`, `skinora_backend_reconciliation_last_success_timestamp_seconds`.
- Sözleşme: derive cevabı `watchFromBlock`; izleme gövdeleri `watchFromBlock`; iade `source: "HOT_WALLET"`; durum sorgusu beklenti parametreleri; `contractRet: "UNKNOWN"`. **Backend ve Ethereum servisi aynı deploy'da** güncellenmeli (§J.1 adım 9).

## Commit & PR

- Branch: `task/T158-ethereum-kurulumu` (origin/main `99b808cf` üzerinden)
- Kod commit'i: `8335c876`
- PR: #353
- CI: rapor commit'inden sonra izlenir (son run ve sonucu PR'da)

## Düzeltme turu (2026-10-19)

Bağımsız doğrulama ✗ FAIL verdi (HEAD `22c75eb6`). Bulgular aynı dalda, yönetici kararlarıyla düzeltildi (11 §F8 "T158 düzeltme turu — yönetici kararları").

**1. 🟠 ⚠ PARA (bloke edici) — iki şerit.** (h)'nin kuralıyla backend her yeniden başlatmada bütün depozitleri türetildikleri bloktan yeniden kuruyordu; tek şeritte eskiden yeniye okunan geçmiş taze ödemeyi bekletiyordu.

- `PaymentScanner`: her depozitin blokları ikiye ayrılır. **Canlı şerit** (`scannedThrough`) T158 öncesi kuralın başlatacağı yerden (yeniden kurulan depozitte açılış bloğunun 7.200 blok gerisi, taze depozitte kendi başlangıç bloğu; hiçbir zaman başlangıç bloğundan önce değil) her tikte okunur; geri okuması bütçeye sığan kısa boşluklarla başlar ve tik aralığının %90'ında durur (`LIVE_TICK_SHARE`) — tik bir sonrakini atlatmaz, baş her tikte okunur; her tikte en az bir parça yine okunur (yavaş baş okuması geri okumayı sonsuza dek durduramasın). **Tarih şeridi** (`ScanState.history`) başlangıç bloğundan canlı şeride kadar olan geçmişi tikte kalan blok bütçesiyle ve tikin yalnız ilk yarısında (`HISTORY_TICK_SHARE`) okur (canlı şerit boştaysa en az bir parça). İki şerit aynı `(txHash, logIndex)` kümesini ve akış kayıtlarını kullanır; sıfır taban başlangıç bloğunda kalır.
- `Reconciler`: depozit ancak boşluksuz okunduğu bloğa kadar karşılaştırılır (`contiguousThrough`) — tarih şeridi bitmeden karşılaştırmak yanlış uyuşmazlık verirdi.
- `RatePacer` / `pace()`: bütçe çağrı başına ayrılır (bir aralığın tamamı tek seferde değil) — bkz. madde 4. Yeni gösterge `skinora_ethereum_monitor_history_deposits`.
- **Ölçüm** (aynı hikâyeler: eski depozit yeniden kuruluyor, taze depozit açılıştan 5 dk 15 sn sonra, ödeme hemen; 15 sn tik, 500 blok/tik, 7.200 blok geri okuma; "tempolu" = 200 CU/s + 10 blokluk aralık, beklemeler saati ilerletir):

| Durum | Önce (HEAD `22c75eb6`) | T158 öncesi kural (başlangıç bloğu yok) | Sonra |
|---|---|---|---|
| 30 günlük iptal sonrası depozit | 6.180 sn (1,7 sa) | 15 sn | **15 sn** |
| aynı, tempolu | 12.660 sn (3,5 sa) | 30 sn | **15 sn** |
| 8 günlük HOLDING depozit | 1.425 sn (24 dk) | 15 sn | **15 sn** |
| aynı, tempolu | 3.135 sn (52 dk) | 30 sn | **15 sn** |
| 30 günlük, ödeme açılıştan 1 sa sonra, tempolu | 9.390 sn | 15 sn | **15 sn** |

  Bedel arka plana geçti: tarih şeridi 30 günlük depozitte tempolu ~7,4 sa (tempo yokken 1,8 sa), 8 günlükte ~1,9 sa; o sürede o depozitin eski geçmişindeki geç ödeme geç bildirilir (kaybolmaz), depozit mutabakata girmez.
- **Testler** (`PaymentScanner.lanes.test.ts`, 12): 30 günlük iptal sonrası / 8 günlük HOLDING × tempolu / tempo yok — taze ödeme ≤ 30 sn (T158 öncesi ölçüt) ve tek kez; açılıştan 2 sa önce türetilmiş PAYMENT depoziti (600 blok, bütçeden uzun boşluk) aynı hızda; yeniden kurma penceresinin hemen sonrası (geri okuma sürerken, kısa boşluk önce); **ayrım/kontrol:** taze ödeme bildirildiğinde 30 günlük geçmiş hâlâ okunuyor ve önündeki bloklar tek şeritte ≥ 1,5 sa ekleyecek kadar — geçmiş sonra bitiyor, geçmişin başındaki ve sonundaki eski transfer tek kez bildiriliyor, taze ödeme tekrar bildirilmiyor; tarih şeridi tikin yarısında duruyor, hiçbir tik atlanmıyor; tempolu geri okuma tiki bir sonrakinden önce bitiyor; mutabakat geçmişi okunmamış depoziti dışarıda bırakıp bitince sıfır tabandan karşılaştırıyor; süresi baştan tükenmiş tikte iki şerit de birer parça ilerliyor. Eski kodla (HEAD tarayıcı + mutabakat) yeni dosyalarda 22 testin 11'i kırmızı.
- **Bozma (14 / 14 yakalandı):** şerit ayrımı yok · kısa boşluk önceliği yok · tarih şeridi okunmuyor · mutabakat tarih şeridini yok sayıyor · tarih şeridi süresini aşıyor · canlı geri okuma süresini aşıyor · aralık tek seferde ayrılıyor · kayıt yeniden kurma bloğunu yok sayıyor · kayıt daha geç bloğu alıyor · tarayıcı erken bloğa uzanmıyor · taban erken bloğa taşınmıyor · tarih şeridi canlı şeride 1.000 blok kala bitiyor (ilk koşuda yeşil kalmıştı → geçmişin sonuna bir transfer eklenip yakalandı) · canlı şeridin / tarih şeridinin güvenceli ilk parçası yok (ilk koşuda yeşil → baş okumasının tiki tükettiği test eklendi).

**2. 🟡 ⚠ PARA test boşluğu.** Backend'in başlangıç bloğunu taşıyan üç yolu testlendi (blok + null): B21 `PostCancelMonitorRecoveryHookTests.T158h_The_Persisted_Start_Block_Reaches_The_Replayed_PostCancel_Start` (servis isteği), B23 `PostCancelMonitorStarterStartBlockTests.T158h_The_Persisted_Start_Block_Rides_The_PostCancel_Event`, B22 `TransactionReadinessStartBlockTests.T158h_The_Persisted_Start_Block_Rides_The_Payment_Monitor_Event` (olay). Üç yolda bloğu düşürmek → üç test kırmızı. `DepositRegistry.startActive` / `startPostCancel` var olan kayıtta gelen blok ilkse ya da daha erkense kaydeder, daha geç bloğu almaz; `PaymentScanner.extendToEarlierStarts` tarih şeridini o bloğa uzatır, mutabakat henüz okuma almadıysa sıfır tabanı taşır. Testler `PaymentScanner.outage.test.ts` (4 yeni: kayıt kuralı aktif / post-cancel; geri okumadan eski ödeme yeni blokla bulunur; kontrol: blok gelmezse görülmez); bozmaları madde 1'deki 14'ün içinde.

**3. 🟡 (j) webhook 401 sayacı.** `WebhookClient.test.ts` (3): gerçek istemci, 401 → `skinora_ethereum_webhook_rejected_total{status="401"}` +1 ve yeniden denenebilir; 409 kendi etiketinde, kalıcı; 200 sayılmaz. `webhookRejectedTotal.inc` silinince 2 test kırmızı.

**4. 🟡 (i) bakiye parçası.** Seçilen: backend'de parça **10** + bakiye ucu için **30 sn** zincir okuma bütçesi (`ResolveChainReadTimeout`; yeni `BlockchainSidecar.ChainRead[.ETHEREUM]` istemcisi — derive / izleme / soğuk aktarım 10 sn'de kalır; TRON'un bakiye isteği de 30 sn'ye geçti) + serviste çağrı başına bütçe ayırma. Gerekçe: öncelik kuyruğu paylaşılan sağlayıcı sınırını ya aşar ya da tarayıcının çoktan ayrılmış payını geri alamaz; çağrı başına ayırmayla bakiye isteğinin her çağrısı en çok bir tarayıcı (60 CU) + bir mutabakat (≤ 26 CU) çağrısı bekler → adres başına en kötü 330 CU ≈ 1,65 sn, 10 adres ≈ 16,5 sn < 30 sn (20 adres ≈ 33 sn > 30 sn). Testler: `PaymentScanner.pacing.test.ts` (sanal zaman: 500 bloklu aralıkla aynı anda başlayan 10 adreslik okuma 12,5 sn'de biter; eski tek ayırmada aralığın 30 sn'sinin arkasında kalırdı — bozma yakalandı); backend `EthereumBalanceSnapshotBudgetTests.T158i_An_Ethereum_Balance_Chunk_Fits_The_Chain_Read_Budget_With_The_Pacer_Shared` (parça 10, 16,5 < 30, 20 > 30, 16,5 > 10) ve `BlockchainServiceRegistryWiringTests.T158i_Ethereum_Balances_Run_Under_The_Chain_Read_Budget_And_Monitor_Calls_Keep_The_Short_One` (gerçek konteynerden: bakiye uzun bütçede başarılı, izleme çağrısı kısa bütçede zaman aşımı). Backend bozmaları 5 / 5 yakalandı (parça 20 · zincir okuma = 1× · bakiye 10 sn istemcide · kayıt kısa istemciyi iki kez veriyor · zincir okuma istemcisi 10 sn).

**5. 🟡 Küçükler.**
- ⚠ PARA sweep yoldayken fiyatlanan iade — **belgelendi** (yeniden fiyatlama yok). Akış doğrulandı: tüketici sweep'i beklemeden sıcak cüzdan ücretiyle fiyatlar; gönderim işi her denemede kaynağı yeniden sorar, sweep `FAILED` olursa aynı `Amount` ile depozitten gönderir. Gas farkı platformda; gönderimde yeniden fiyat, T152'nin "bir kimlik, bir imza" kuralı altında imzalanmış olabilecek satırın tutarını değiştirirdi; yalnız sweep arızasında olur. Yorum `PaymentRefundToBuyerConsumer.cs`'te; 08 §3.7.6, runbook §J.6, 11.
- `IChargedGasFeeResolver.cs` XML yorumları her üyenin kendisini anlatacak şekilde düzeltildi.
- S14: sıcak cüzdan iadesinde `WRONG_TOKEN_REFUND` + **USDT** reddi testi (`TransferService.test.ts`); iade türü denetimi kaldırılınca test kırmızı (eski test USDC kullandığı için token denetimi bunu maskeliyordu).
- F02: sahte serviste `HOT_WALLET` gövdesinde `depositIndex` (sayı ya da null) ve `depositAddress: null` reddi testli (`sidecar-fake/src/blockchain.test.ts`); `depositIndex` denetimi kaldırılınca test kırmızı.

**6. Belgeler.** Runbook §J.3 (iki şerit, bedel yalnız arka plan okumasında, göstergeler; iptal sonrası yeniden kurma boşluğu backlog'a bağlandı), §J.4 adım 5 (**gelen tutarın tamamı iade, kurtarma gas'ı platformda** — yönetici kararı, ⚠ PARA), §J.2 `reconciliation-network-failed` satırı (10'luk parça, 30 sn), §J.6 (fiyat farkı). 08 v3.37 (§3.0, §3.7.4, §3.7.6). 11 v1.17 ("T158 düzeltme turu — yönetici kararları" (a)–(f); (xi) kapandı, (xii) backlog'a bağlandı). `DEFERRED_BACKLOG` §4: 🟡 `PostCancelRearmOnlyOnBackendStart` (F9 öncesi kapanmalı) ve 🟡 `TronRefundAfterSweepFromDeposit` — **42 aktif / 156 çözülmüş** (dosyanın `awk`'iyle; 40/156 → +2).

**Ek bulgu (düzeltilmedi, kapsam dışı):** tüketicinin `sweepHasRun` denetimi (T149) `CONFIRMED` ve hash'li `PENDING`'i sayıyor, gönderim işinin yayından sonra koyduğu `DETECTED`'ı saymıyor — TRON'da sweep `DETECTED` iken kuyruğa giren iade 2,90'lık ağ ücreti satırını hâlâ taşıyor. Ethereum'da ücret satırı yok, etkilenmez; TRON'da sweep sonrası depozitten iade zaten başarısız (`TronRefundAfterSweepFromDeposit`), para etkisi küçük.

**Düzeltme turu testleri:** sidecar-ethereum `npx vitest run` **788/788** (önce 767) · `tsc --noEmit` temiz · `eslint src` temiz · prettier (değişen dosyalar, satır sonu yok sayılarak) temiz · sidecar-fake **120/120** · backend birim + sözleşme: Transactions 925 · API 132 · Shared 568 · Platform 157 · Auth 92 · Steam 59 · Users 49 · Realtime 44 · Disputes 25 · Fraud 19 yeşil; Notifications 16 kırmızı = Docker gerektiren testler (yerelde Docker kapalı, önceden de) · `dotnet format --verify-no-changes` temiz. Entegrasyon CI'da.

## Known Limitations / Açık sorular

> Düzeltme turu durumu: 1 → **kapandı** (yönetici kararı: tamamı iade, gas platformda). 2 → `DEFERRED_BACKLOG` 🟡 `PostCancelRearmOnlyOnBackendStart` (F9 öncesi). 3 → bedel artık yalnız arka plan geçmiş okumasında (iki şerit). 4 → `DEFERRED_BACKLOG` 🟡 `TronRefundAfterSweepFromDeposit`.

1. **(⚠ açık soru) Yanlış EVM ağı kurtarma ücreti:** gas'ı kim öder, platform hizmet ücreti alır mı — karar yok. Runbook karara kadar "gelen tutarın tamamı iade, gas platformda" diyor.
2. **(⚠ açık soru, mevcut boşluk) İptal sonrası izleme yalnız backend açılışında yeniden kurulur** (`PostCancelMonitorRecoveryHook`): Ethereum (ve TRON) servisi tek başına yeniden başlarsa iptal sonrası adresler backend yeniden başlayana kadar izlenmez. Runbook geçici yordamı yazıyor; kalıcı çözüm (dakikalık yeniden kurma) T158 kapsamı dışında bırakıldı.
3. **(⚠) (h) için kontrol noktası değil türetme bloğu seçildi:** sıfır taban kesin; bedeli uzun kesinti sonrası yeniden okuma maliyeti (30 günlük iptal sonrası depozit ≈ 2,6 M CU, ≈ 3,6 sa tempolu).
4. **TRON'da sweep sonrası alıcı iadesi** hâlâ depozitten denenir (boş depozit → servis reddi → `FAILED` + admin alarmı). Kapsam dışı; Ethereum'a özgü kural uygulandı.
5. **Anahtarlı RPC sınır probu** (Alchemy/Chainstack) koşulamadı — anahtar yok → T169.
6. **Fiyat kaynağı probu üretim sunucusundan koşulmadı** (sunucu yok) — açılış şartı olarak §J.1'de; F9.
7. `reconciliation-network-failed` göstergesi backend yeniden başlayınca sıfırlanır (bir sonraki tura kadar alarm göremez).
8. `ethereum-finalized-stalled` servis kapalıyken susar; ayrı "Ethereum servisi kapalı" kuralı bilinçli eklenmedi (backend sağlık yoklaması görür).

## Notlar

- **Working tree:** temiz (dal `origin/main` `99b808cf`'ten açıldı).
- **Main CI startup check:** main'in son tamamlanmış run'ları — CI `39153430208` ✓ (T152), Docker Publish `39153430245` ✓ (T152), Docker Publish `39164777570` ✓ (T153); T153'ün CI run'ı `39164777560` başlangıçta `in_progress` idi, sonra `success` ile tamamlandı.
- **Bağımlılıklar:** T151–T154 ✓ (IMPLEMENTATION_STATUS).
- **Dış varsayımlar:** (1) Binance `api/v3/avgPrice?symbol=ETHUSDT` ve CoinGecko `simple/price` anahtarsız erişilebilir — yerelden ölçüldü (yukarıda); üretim çıkışından doğrulanamadı. (2) `dotnet-ef` 9.0.3 kurulu — migration üretildi. (3) Alchemy/Chainstack anahtarlı sınırlar — doğrulanamadı (anahtar yok, T169). (4) Grafana kural dosyası geçerli YAML — `js-yaml` ile ayrıştırıldı (12 + 14 kural). Gerçek ağa hiçbir işlem gönderilmedi.
- **Paralel T157:** ortak dosyalarda gereksiz düzenleme yapılmadı; olası çakışma noktaları `AppDbContextModelSnapshot.cs` (migration) ve `OutgoingTransferConfirmationJob` (T157 satır kapatma aracı bu satırlara dokunabilir).
- Docker açılmadı.
