# T151 — Ethereum ödeme izleyicisi [RİSKLİ]

**Faz:** F8 | **Durum:** ⏳ Doğrulama bekliyor | **Tarih:** 2026-10-18

---

## Yapılan İşler

**Ethereum servisi (`sidecar-ethereum`)**
- **Tarayıcı** (`src/monitor/PaymentScanner.ts`): blok aralığı başına iki `eth_getLogs` — (1) kontrat filtresiz ERC-20 `Transfer`, `topic2` = izlenen depozitler (beklenen token, izin listesindeki yanlış token, spam tek sorguda); (2) USDT/USDC'den `topic1` = depozitler (çıkan transferler, yalnız mutabakat için). Sağlayıcı adressiz sorguyu reddederse (1) USDT/USDC'ye daraltılır (spam o aralıkta görünmez, sayaç + uyarı). Üst uç her zaman FinalityTracker'ın `finalized` başı (K5): transfer `payment.detected` + `payment.confirmed` olarak art arda gider, `confirmationCount` = `finalized` − blok. İleri giden blok imleci; aralığın bütün olayları teslim edilmeden (ya da 4xx kalıcı ret) ilerlemez; geride kalan sağlayıcının eski başı imleci geri almaz.
- **Başlangıç bloğu**: servis açıldıktan sonraki 5 dk'da kurulan (backend'in yeniden kurması), HOLDING ve post-cancel adres 7.200 blok (≈ 24 sa) geri okunur, tik başına ≤ 500 blok, tempolu; taze PAYMENT depozitinin ETH/USDT/USDC bakiyesi `finalized`'da okunur — sıfırsa geri okuma yok. Aktif → post-cancel devri okunmuş durumu taşır.
- **Kayıt** (`src/monitor/DepositRegistry.ts`): TRON sözleşmesinin aktif + post-cancel kayıtları; 24 sa / 7 g / 30 g durum geçişleri webhook'la; aynı adres iki kayıtta ise post-cancel geçerli.
- **Uçlar**: `/api/monitor/start|stop|post-cancel-start|post-cancel-stop` (TRON gövdeleri + `expectedContract` servis kontratı değilse 400 `EXPECTED_CONTRACT_MISMATCH`), `/api/wallet/balances` (`finalized`'da, tempolu, `finalized` > 25 dk ise 502 `FINALIZED_STALE`).
- **Webhook** (`src/webhook/`): TRON'la aynı olaylar/yollar/alanlar, `network: "ETHEREUM"`, kendi HMAC sırrı; `contracts/webhook/*.json` örnekleri iki tarafta testli.
- **Saatlik bakiye mutabakatı** (`src/reconcile/`): delta modeli — iki okuma arasında token bakiyesi gözlenen giriş − çıkış kadar değişmeli; taze depozit sıfırdan karşılaştırılır. Yakıt olarak kaydedilmemiş ETH artışı alarm (`FuelLedger`, T153'ün kayıt noktası). `RatePacer` (varsayılan 200 CU/s). `finalized` > 25 dk ya da okuma hatası → tur atlanır, sayılır; 2,5 aralık tur yoksa alarm.
- **USDT durumu** (`src/token/UsdtStatus.ts`): `basisPointsRate`, `maximumFee`, `paused`, `deprecated` açılışta + 10 dk'da bir `finalized`'da; `readUsdtSwitches` T152'ye paylaşılır.
- **Kod düzeyi kapı**: `/health` → `acceptsPayments` (izleyici ∧ webhook sırrı ∧ USDT `ok`) + `payments.reasons`.
- **Alarmlar** (`infra/grafana/.../rules.yml`): `ethereum-deposit-balance-mismatch` (critical), `ethereum-usdt-status` (critical), `ethereum-deposit-native-eth` (warning), `ethereum-reconciliation-stale` (warning).
- RPC istemcisi: `getLogs`'ta `address` isteğe bağlı (T150 dosyasında tek değişiklik).

**Backend**
- `SidecarHealthClient`: Ethereum `/health` gövdesinde `acceptsPayments: true` yoksa (ya da false / string / gövde JSON değil) sağlıksız; TRON'dan istenmez.
- `PlatformHealthMonitorState.LastProbeHealthy` + `ProbeBlockchainNetworkHealth.AcceptsNewTransactions`: Ethereum `supportedNetworks`'e yalnız SON yoklama sağlıklıysa girer (ilk yoklamadan önce kapalı). `IBlockchainNetworkHealth.AcceptsNewTransactions` varsayılan uygulamalı (`!IsDegraded`), `BlockchainNetworkAvailability` onu okur.

## Etkilenen Modüller / Dosyalar
- Yeni: `sidecar-ethereum/src/monitor/{erc20,tokenRules,DepositRegistry,PaymentScanner}.ts`, `src/reconcile/{RatePacer,BalanceReader,FuelLedger,Reconciler}.ts`, `src/token/UsdtStatus.ts`, `src/webhook/{WebhookPayloads,WebhookClient}.ts`, `src/api/{monitorHandlers,balancesHandler}.ts`, `src/health/paymentReadiness.ts`, `src/testing/{FakeEthereumChain,monitorHarness}.ts`, `contracts/webhook/*.json` (6), testler (`PaymentScanner`, `Reconciler`, `UsdtStatus`, `erc20`, `WebhookPayloads`, `paymentRoutes`).
- Değişen: `sidecar-ethereum/src/{index,metrics}.ts`, `src/api/{routes,notImplemented}.ts`, `src/health/HealthController.ts`, `src/config/index.ts`, `src/rpc/RpcClient.ts`, `src/alertRuleWiring.test.ts`, `src/config/{composeWiring,startupWiring}.test.ts`, `docker-compose.yml` (4 env satırı), `.env.example`, `infra/grafana/provisioning/alerting/rules.yml`.
- Backend: `Skinora.API/Monitoring/{SidecarHealthClient,PlatformHealthMonitorState}.cs`, `Skinora.Transactions/Application/Networks/BlockchainNetworkAvailability.cs`; testler `Unit/Monitoring/{SidecarHealthClientTests,PaymentCapabilityGateTests}.cs`, `Contract/EthereumWebhookContractTests.cs`.
- Belgeler: 05 v3.19 (§3.3a), 07 v4.12 (§1.3, §7.4), 08 v3.25 (§3.0, §3.7.1, §3.7.4), 11 (§F8 "T151 kararları", §7.6), DEPLOY_RUNBOOK §B.

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Tek `eth_getLogs` ile tüm aktif depozitler; ileri blok imleci; yalnız finalized (K5); tekillik (ağ, txHash, logIndex) | ✓ | `PaymentScanner.test.ts` "finalized blocks only" (3 test + geri giden baş), "re-reading never double-reports" (4), "query limits" (2); 1.500 depozit tek mantıksal sorguda, istekler ≤ 1.000 alıcı / ≤ 10 blok |
| 2 | Yanlış token, eksik/fazla ödeme, iptal sonrası 30 gün izleme | ✓ | yanlış token/spam/ERC-721 testi; tutar backend'e 6 ondalıkla aynen (eksik/fazla kararı backend `AmountValidationService`, değişmedi); post-cancel late + 24h→7D→30D→STOPPED testi |
| 3 | Bakiye mutabakatı (zincir bakiyesi ↔ kayıtlar) | ✓ | `Reconciler.test.ts` — dengeli / log'dan saklanmış ödeme (taze depozit ilk okumada) / iki okuma arası tek seferlik fark / geri okuması süren adres atlanır |
| 4 | İzin listesiyle sınıflama (USDT + USDC iade; diğerleri iade yok, admin'e görünür) | ✓ | `erc20.test.ts` `classifyToken` tablosu; tarayıcı testi `payment.wrong_token` (USDC) / `payment.spam_token` |
| 5 | Yerel ETH → mutabakat yakalar + admin alarmı, otomatik iade yok; yakıt ETH'si alarm üretmez | ✓ | `Reconciler.test.ts` "native ETH" (3 test: yakıt değil → alarm; kayıtlı yakıt → alarm yok; başka bloğun yakıtı → alarm); alarm kuralı `ethereum-deposit-native-eth` (`alertRuleWiring.test.ts`). Yakıtın kaydı T153 |
| 6 | Saatlik mutabakat hız sınırına göre yayılır (≥ ~46 sn) | ✓ | `Reconciler.test.ts` "300 deposits … at least ~46 s at 300 CU/s" (sahte saat: 45,9 sn+); varsayılan 200 CU/s, USDT+USDC+ETH ile 300 depozit 108 sn |
| 7 | `finalized` > 25 dk → mutabakat atlanır ya da alarm, sessiz kalmaz | ✓ | `Reconciler.test.ts` "never silent" (finalized_stale sayacı; okuma hatasında son başarı korunur); `ethereum-reconciliation-stale` kuralı; `/wallet/balances` 502 `FINALIZED_STALE` (`paymentRoutes.test.ts`) |
| 8 | USDT `deprecated`/`paused` denetimi | ✓ | `UsdtStatus.test.ts` (durumlar + finalized blokta okuma + başarısız ilk okuma kapalı); `acceptsPayments` nedenleri |
| 9 | Kod düzeyi kapı: `/health` yetenek bayrağı; yoksa/false backend sağlıksız sayar, `supportedNetworks`'e almaz | ✓ | sidecar `paymentRoutes.test.ts` (/health true/false + neden); backend `SidecarHealthClientTests` (10), `PaymentCapabilityGateTests` (4) |
| 10 | Test beklentisi: Unit + Integration (sahte zincir; reorg ve sayfa sınırı) | ✓ | `FakeEthereumChain` sorguyu yorumlar (adres/konu filtresi, aralık/liste/sonuç sınırı hataları, reorg, bakiye); reorg + sayfa sınırı testleri |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| sidecar-ethereum (vitest) | ✓ 242/242 | `npx vitest run` — 16 dosya |
| sidecar-ethereum tsc / eslint / prettier | ✓ | `npx tsc --noEmit`, `npx eslint src/`, prettier yazımı |
| Backend Unit | ✓ | `dotnet test Skinora.sln --filter Category=Unit` — API 71, Transactions 215, diğer modüller yeşil |
| Backend yeni testler | ✓ 14 + 7 | `SidecarHealthClientTests`, `PaymentCapabilityGateTests`, `EthereumWebhookContractTests`; `dotnet format --verify-no-changes` temiz |
| Backend Integration (yerel) | ? | Docker kapalı (Testcontainers); CI'da koşar |
| CI ilk push | ✓ | run `39127748940` success (lint, build, unit, integration, contract, migration, docker sidecar-ethereum, e2e advisory) |

**Bozma (mutation) ölçümü** — kaynakta tek değişiklik, ilgili testler koşulur (betikler yapım oturumunun scratchpad'inde):
- Sidecar 32 bozma / 32 yakalandı (ilk koşu 28/30: ERC-721 konu sayısı ve post-cancel önceliği yeşil kaldı → iki test eklendi; adressiz-sorgu geri düşüşünün alıcı filtresi yeşil kaldı → istek içeriği testi eklendi). Kapsam: finality eşiği (+30 blok, onay sayısı), yanlış token (izin listesi yok sayıldı / boş slot joker / büyük-küçük harf), yanlış zincir (zincir kontrolü kapalı), adres bölme (1.000 sınırı), çift bildirim (bellek tekilleştirmesi yok / anahtardan logIndex düştü), imleç (teslimden önce ilerledi / geri gitti), ERC-721 süzgeci, kapı (USDT / webhook sırrı / /health hep true), USDT anahtarları (paused / maximumFee), mutabakat (akışlar yok sayıldı / taze depozit sıfırdan değil / yakıt düşülmedi / alarm kaldırıldı / yaş kontrolü yok / tempo yok), bakiye ucu yaş kontrolü, kontrat eşleşmesi, geri okuma (yeniden kurma / taze yoklama / devir), 4xx sonsuz tekrar, adressiz sorgu geri düşüşü.
- Backend 5 bozma / 5 yakalandı (bozmasız taban 14/14 yeşil): yoklama bayrağı yok sayar (4 kırmızı), son yoklama yok sayılır (2), uygunluk yalnız kesintiyi okur (2), Ethereum'dan bayrak istenmez (7), string `"true"` kabul edilir (1).

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS (bağımsız temiz bağlam; HEAD `b2e4757`) |
| Bulgu sayısı | 3 grup (açılışta USDT durumu, `finalized` etiketi testi, test boşlukları) |
| Düzeltme gerekli mi | Evet — bu kapanışta yapıldı |

## Doğrulama sonucu

- **Verdict:** PASS. Doğrulayıcı: bağımsız temiz bağlam ajanı (F8 yönetici düzeni). HEAD `b2e4757`, CI run [`39129005912`](https://github.com/turkerurganci/Skinora/actions/runs/39129005912) ✓.
- **Bozma ölçümü:** servis 54 bozma / 43 yakalandı + backend 6/6. Yakalanmayan gerçek boşluklar F1, L1, G5, R8, S7, S8 bu kapanışta testle kapandı; R6, R10, S3, S10, P3 eşdeğer.
- **Sepolia ölçümü:** doğrulayıcı canlı Sepolia'da sağlayıcı davranışını (adressiz `eth_getLogs` reddi, `finalized` etiketi) ölçtü; tasarımla uyumlu.
- **Bulgular ve kapanışı:**
  1. *Açılışta USDT durumu okunmuyor* — `usdt.start()` ilk okumayı `finalized` baş okunmadan deniyor ve sessizce atlıyordu; sonraki deneme 10 dk sonra olduğundan her yeniden başlatmada ~10 dk `acceptsPayments=false` ve backend Ethereum kesintisi ilan ediyordu. Düzeltme: `UsdtStatusMonitor` durum `unknown` iken 15 sn aralıkla yeniden dener (`retryMs`). Başlatma sırası `src/wiring.ts`'e alındı (`index.ts` onu kullanıyor); test (`wiring.test.ts`) gerçek sırayla, açılıştan 16 sn sonra durum `ok` ve `eth_call` yapılmış olmalı.
  2. *`getBlockHeader('finalized')` etiketi testle sabit değildi* (`'latest'` bozması geçti) — `wiring.test.ts`: sahte zincirde `latest` ≠ `finalized`, gerçek `FinalityTracker` ile; bildirim yalnız `finalized` blok için gider.
  3. *Test boşlukları* — adres bölme sınırı tam 1.000 / 1.001 adreste; kapının (`readiness`) izleyiciye bağlanması; imleç gerideyken mutabakatın okuduğu blok (hem imleç hem geride kalan sağlayıcı başı); `removed: true` ve aralık dışı log süzgeçleri.
  - Kapanış bozma kanıtı: USDT yeniden denemesi kaldırıldı → 3 test kırıldı; `finalized` → `latest` → 2; kapı `true` → 1; mutabakat bloğu (baş / imleç) → 1 + 1; `removed` süzgeci → 1; aralık süzgeci → 1; bölme sınırı `limit + 1` → 1, parça boyutu `-1` / adım `+1` → 1 + 1. `>=` bozması eşdeğer (tam sınırdaki liste tek parça kalır); adres-listesi bölmesi yalnız izin listesi geri düşüşünde ve en çok 2 adresle kullanıldığından ulaşılamaz.
- **Para riski değerlendirmesi:** para kaybı yok; geç tespit var. Bulgu 1 yalnız yeni işlemlerin açılışını geciktiriyordu (fail-closed); `finalized` etiketi bulgusu yalnız test boşluğuydu, kod doğru.
- **Yönetici kararları (11 §F8 T151 kararları):** 24 sa+ kesinti için T151'de runbook, kalıcı çözüm T158 (⚠ PARA); backend 10 sn zaman aşımı T158. T158'e dört yeni kriter eklendi (kalıcı "şu bloktan izle" bilgisi, bakiye isteği parçalama + mutabakat alarmı, webhook 401 yeniden deneme/alarm, `monitor_scan_failures_total` alarmı).
- **Main bütünleştirmesi:** `origin/main` (T149, T150) dala merge commit'le alındı; çakışmalar yalnız belgelerde (MEMORY.md, 05, 07, 08, 11, DEPLOY_RUNBOOK, IMPLEMENTATION_STATUS), iki taraf korundu; başlık versiyonları iki tarafın büyüğü +1.

## Altyapı Değişiklikleri
- Migration: Yok
- Config/env: Ethereum servisine `ETHEREUM_MONITOR_TICK_MS`, `_RESTART_LOOKBACK_BLOCKS`, `_REARM_WINDOW_MS`, `_CATCH_UP_BLOCKS_PER_TICK`, `ETHEREUM_RECONCILIATION_INTERVAL_MS`, `_CU_PER_SECOND`, `_MAX_FINALIZED_AGE_SECONDS`, `ETHEREUM_USDT_STATUS_INTERVAL_MS` (hepsi varsayılanlı; dördü compose + `.env.example`'da). `WEBHOOK_SECRET` artık ödeme bildirimi için gerekli (yoksa `acceptsPayments: false`). Backend `EthereumSidecar:BaseUrl` hâlâ boş kalır (T152–T154 tamamlanana kadar, T150 kararı i).
- Docker: compose'a 4 env satırı; imaj değişmedi. Alarm kuralları: 4 yeni.

## Commit & PR
- Branch: `task/T151-ethereum-odeme-izleyicisi`
- Commit'ler: `286cc91` (sidecar), `98353cd` (backend kapısı), sonraki commit (adressiz sorgu geri düşüşü + bozma testleri + belgeler + rapor)
- PR: #347
- CI: ilk push ✓ `39127748940`; son push sonucu PR'da

## Known Limitations / Follow-up
- **Uzun kesinti boşluğu (yöneticiye soru):** servis 24 saatten (geri okuma) uzun kapalı kalırsa ya da backend bir HOLDING / post-cancel adresi o kadar geç yeniden kurarsa, aradaki eski transferler log yolunda bulunmaz; yeniden kurulan adreste ilk mutabakat okuması taban olduğu için mutabakat da görmez. Taze PAYMENT depozitinde mutabakat yakalar.
- **Backend günlük defter mutabakatı zaman aşımı (yöneticiye soru):** `EthereumSidecar:TimeoutSeconds` = 10; tempolu `wallet/balances` 200 CU/s'de ~27 adresten fazlasında aşılır → backend'in Ethereum turu `Unavailable` ile düşer (servisin saatlik mutabakatı etkilenmez).
- `HealthProbe:Enabled=false` olan ortamda Ethereum hiç açılmaz (fail-closed, bilinçli).
- Alchemy/Chainstack'in adressiz `eth_getLogs` davranışı anahtarsız ölçülemedi (T158/T169 anahtarlı prob); reddederlerse spam o sağlayıcıda görünmez, ödeme/yanlış token görünür.
- Sağlayıcı arasında geçişte bir sağlayıcının `finalized` blok log'larını henüz indekslememiş olması (sessiz eksik cevap) log yolunda yakalanmaz; mutabakat ikinci yoldur (08 §3.7.8).
- Yakıt kaydı (`FuelLedger.record`) T153'te bağlanacak; o zamana kadar her ETH girişi alarm verir (doğru: yakıt yok).

## Notlar
- **Working tree:** temiz (oturum başı `git status` boş).
- **Adım 0 — main CI:** son 3 tamamlanmış run success: `39124319255` (Docker Publish, T150), `39114726766` (CI, T147), `39114726772` (Docker Publish, T147); T150 CI `39124319215` başta sürüyordu, sonra success.
- **Bağımlılıklar:** T142 ✓, T150 ✓ (IMPLEMENTATION_STATUS).
- **Dış varsayımlar:**
  - ABI seçicileri / `Transfer` konusu: `ethers.id(...)` ile hesaplandı (`0xddf252ad…`, `balanceOf 0x70a08231`, `basisPointsRate 0xdd644f72`, `maximumFee 0x35390714`, `deprecated 0x0e136b19`, `paused 0x5c975abb`).
  - USDT'nin dört anahtarı zincirde okunabilir: T142 blok 26.214.908 okuması (08 §3.7.1).
  - Adressiz `eth_getLogs` (yalnız `topic2` listesi): **canlı prob 2026-10-18 12:45 UTC, Sepolia, bloklar 11.941.072–11.941.081** — PublicNode `-32701 Please specify an address` ile reddetti; dRPC, Tenderly, 1RPC aynı 87 log'u döndürdü (21 USDC, adresle filtreli sorgunun 21'iyle aynı). Bulgu tasarımı değiştirdi: geri düşüş eklendi.
  - Alchemy CU maliyetleri (getLogs 60, eth_call 26, getBalance 20, getBlockByNumber 20): T142_REPORT §1.4 tablosu.
  - Yeni npm/NuGet bağımlılığı yok (`package.json` değişmedi).
  - Backend Ethereum HTTP zaman aşımı 10 sn: `appsettings.json` `EthereumSidecar.TimeoutSeconds`.
- Yerel prettier uyarıları CRLF artifaktıdır; CI "1. Lint" (LF) yetkili.
