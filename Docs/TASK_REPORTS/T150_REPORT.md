# T150 — Ethereum servis iskeleti

**Faz:** F8 | **Durum:** ✓ Tamamlandı — doğrulama ✓ PASS (PR #346) (başlık F8 Gate Check'te bayattan düzeltildi) | **Tarih:** 2026-10-18

---

## Yapılan İşler

- **Yeni servis `sidecar-ethereum/` (11 §F8 K7 — kendi konteyneri):** TRON servisinin araç zinciri (TypeScript, express, vitest, eslint, prettier; `Dockerfile` kilit dosyası + `npm ci`). Konteyner `skinora-ethereum-sidecar`, port 5300, compose profili `ethereum`.
  - **Açılış kuralları (fail-closed):** `config/index.ts` `loadConfig` iç anahtar yoksa (`INTERNAL_KEY_MISSING`) ya da 32 karakterden kısaysa (`INTERNAL_KEY_TOO_SHORT`), Ethereum tohumu yoksa (`HD_WALLET_NOT_CONFIGURED`), birincil RPC yoksa (`RPC_NOT_CONFIGURED`), ya da bir değer bozuksa (`INVALID_CONFIG`, `INVALID_SIGNING_KEY` — değer asla mesaja yazılmaz) servis açılmaz. `index.ts` hatayı kodla son log satırına yazıp sıfır olmayan kodla çıkar.
  - **Sırlar Docker secret:** tohum, sıcak cüzdan ve yakıt cüzdanı anahtarı `/run/secrets/ethereum_{hd_mnemonic,hot_wallet_private_key,fuel_wallet_private_key}` (yol env ile değiştirilebilir); `config/secretFile.ts` (dosya yok/boş = ayarlı değil; okunamayan dosya = hata). Env'de imza materyali yok.
  - **Anahtar ayrımı (`wallet/walletKeys.ts`):** yakıt anahtarı sıcak anahtarla aynı adresi kontrol ediyorsa ya da yapılandırılmış sıcak/soğuk adresi kontrol ediyorsa `WALLET_KEYS_NOT_DISTINCT`; sıcak anahtar `ETHEREUM_HOT_WALLET_ADDRESS`'i kontrol etmiyorsa `HOT_WALLET_ADDRESS_MISMATCH` — servis açılmaz. Anahtarlar henüz hiçbir şey imzalamıyor (T152/T153).
  - **Türetme (`wallet/HdWalletService.ts`):** `m/44'/60'/0'/0/{i}`, BIP-32 kök düğümünden; adres EIP-55; kök kurucu tek üretim fonksiyonu (`hdRoot.ts`, aşağıda); adres ve imza anahtarı tek türetme rutininden. Kök kuruluşta (açılışta) yapılır: bozuk ifade ya da kök olmayan düğüm servisi açtırmaz.
  - **RPC istemcisi (`rpc/RpcClient.ts`):** birincil + yedek; taşıma hatasında (bağlantı, zaman aşımı, HTTP 429/5xx, `-32005`, arşiv reddi) sağlayıcı 30 sn atlanır, sonraki cevap verir; istek hakkındaki JSON-RPC cevabı (`execution reverted`) yedeğe taşınmaz; her sağlayıcının `eth_chainId`'si bir kez denetlenir, yanlış zincirdeki sağlayıcı hiç kullanılmaz. `getBalance`, `call`, `getLogs` yalnız açık `bigint` blok numarası alır (çalışma anında da reddeder: `RPC_IMPLICIT_BLOCK`); etiket yalnız `getBlockHeader('finalized' | 'latest')`. `getLogs` aralığı cevap veren sağlayıcının kendi `maxBlockRange`'ine, her liste konumunu 1.000'lik parçalara böler; "çok büyük" cevabını (T142'de ölçülen mesajlar) aralığı yarıya bölerek karşılar, tek blokta da reddedilirse diğer sağlayıcıya geçer. URL'ler (API anahtarı içinde) log'a ve hata mesajına girmez; yalnız sunucu adı.
  - **`finalized` takibi (`finality/FinalityTracker.ts`):** baş 30 sn'de bir okunur; durumlar `starting` / `unreadable` (3 yoklama boyunca cevap yok) / `stalled` (blok > `ETHEREUM_FINALIZED_STALL_SECONDS`, 1.800) / `backup` (yalnız yedek cevap veriyor) / `ok`.
  - **Sağlık (`health/HealthController.ts`):** yalnız tracker'ın son okumasından cevaplar (sağlayıcıya ek çağrı yok): `unreadable`/`stalled`/`starting` → 503 `unhealthy`, `backup` → 200 `degraded`, `ok` → 200; gövde `network: "ETHEREUM"`, `chainId`, son `finalized` blok ve yaşı.
  - **Metrikler (`metrics.ts`):** `skinora_ethereum_rpc_request_duration_seconds{provider,method,outcome}`, `_rpc_failover_total`, `_finalized_block_number`, `_finalized_block_age_seconds` (scrape anında bloğun zaman damgasından — okuma durursa yaş büyür), `_finalized_stall_threshold_seconds`, `_finalized_read_failures_total`.
  - **Uçlar (`api/`):** `GET /health`, `GET /metrics` (anahtarsız, iç ağ); `/api/*` her istekte `X-Internal-Key` (sabit zamanlı karşılaştırma) + `network: "ETHEREUM"` zorunlu (`NETWORK_MISMATCH`). `POST /api/wallet/derive` çalışır; sözleşmenin diğer 11 ucu 501 `NOT_IMPLEMENTED` (yan etkisiz; görev adını söyler).
- **HD birleştirme (`sidecar-blockchain`):** `src/wallet/hdRoot.ts` (yeni) — `masterNodeFromPhrase` (tek kök kurucu; ayrıştırma hatası `INVALID_MASTER_MNEMONIC`, kök olmayan düğüm `HD_ROOT_NOT_MASTER`), `deriveChild`, `assertMasterNode`, `assertValidIndex`, hata sınıfları. TRON `HdWalletService.getRoot()` artık `masterNodeFromPhrase`'i çağırıyor (T143'te iki ayrı kurucu vardı). Dosya `sidecar-ethereum/src/wallet/hdRoot.ts` ile **bayt bayt aynı**; `hdRootParity.test.ts` farkı yakalar.
- **TRON servisi çağrı yeri testleri (T145 doğrulaması F3):** `src/address/evmCasingCallSites.test.ts` — `TransferGuard` sweep hedefi, izin verilen kontrat kümesi, `MonitorRegistry` anahtarı (`start`/`cadenceOf`/`stop`), `FeeEstimationService` gönderen = sıcak cüzdan kararı EVM yazımıyla (EIP-55 / küçük / büyük hex); ters yönde TRON harf farkı ayrı adres sayılıyor.
- **Ortak kuyruklar (backend) — `OutboxNoBackoffOrDeadLetter` kapandı:**
  - **Outbox:** `OutboxMessage.NextAttemptAt` (migration `20261018094011_T150_OutboxRetryBackoff`, yalnız nullable kolon). `OutboxDispatcher` önce en eski PENDING'i, sonra zamanı gelmiş FAILED'ı (en uzun bekleyen önce) okur ve bu sırayla işler; başarısızlıkta `NextAttemptAt = şimdi + 5 sn · 2^(n−1)` (en çok 15 dk); alarm `RetryCount == MaxRetryCount`'ta bir kez; kayıt bırakılmaz. `OutboxOptions`: `RetryBackoffBaseSeconds` 5, `RetryBackoffMaxSeconds` 900, `RetrySlotsPerBatch` 10; `TimeProvider` enjekte.
  - **Gönderim / onay / adres işleri:** `Networks/NetworkWorkQueue.cs` (yeni) — `ReachableNetworks` (yapılandırılmış ∧ sağlık yoklamasının kesinti ilan etmediği), `Interleave` (round-robin), `NetworkRunBreaker`. `OutgoingTransferDispatchJob`, `OutgoingTransferConfirmationJob`, `EnsurePaymentAddressJob` ağ başına ayrı parti alır ve sırayla işler; gönderim ve onay işi bir ağın ilk "erişilemez" cevabında (`TRANSPORT_FAILURE` / `Unavailable`) o ağın turunu bırakır. `IBlockchainNetworkHealth` üç işe enjekte; `HttpBlockchainTransferClient.TransportFailureCode` sabiti.
- **Compose / yapılandırma:** `docker-compose.yml` `skinora-ethereum-sidecar` (profil `ethereum`, env, `secrets:`, healthcheck) + üst düzey `secrets:`; backend `ETHEREUM_SIDECAR_BASE_URL` varsayılanı boş kaldı. `.env.example` Ethereum bölümü (RPC, ağ, `finalized` eşikleri, sırların dosya olduğu notu). `infra/prometheus/prometheus.yml` hedef; `infra/grafana/provisioning/alerting/rules.yml` `ethereum-finalized-stalled` (yaş ÷ servisin yayımladığı eşik > 1, critical). `secrets/README.md` Ethereum secret dosyaları.
- **CI:** `ci.yml` — `sidecar-ethereum` yol filtresi (dizin + `sidecar-blockchain/src/wallet/hdRoot.ts` + `docker-compose.yml` + `infra/**`), lint işinde typecheck + format + eslint + `npm audit --omit=dev --audit-level=high` (bloke eden), 3b JS test adımı, docker build matrisi; `docker-publish.yml` matrisi.
- **Dokümanlar:** 02 v4.10 ve 07 v4.11 ("servisi olmayan Ethereum" notları — `BaseUrl` T151–T154 tamamlanana kadar boş), 05 v3.17 (§3.3a "Uygulandı (T150)", §3.5, §5.1, §8.1), 06 v6.26 (§3.18 `NextAttemptAt` + yeniden deneme semantiği), 08 v3.23 (§3, §3.0, §3.7 — "T150 ✓" satırları), 11 §F8 "T150 kararları" + §7.6 iki satır, DEPLOY_RUNBOOK §B/§D/§G.1, `DEFERRED_BACKLOG` (39 aktif / 156 çözülmüş).

## Etkilenen Modüller / Dosyalar

- **Yeni servis:** `sidecar-ethereum/` — `package.json`, `package-lock.json`, `Dockerfile`, `tsconfig.json`, `vitest.config.ts`, `.eslintrc.json`, `.prettierrc`; `src/index.ts`, `src/network.ts`, `src/logger.ts`, `src/metrics.ts`, `src/errors/SidecarError.ts`, `src/address/{canonicalAddress,evmAddress}.ts`, `src/config/{index,secretFile}.ts`, `src/wallet/{hdRoot,HdWalletService,walletKeys}.ts`, `src/rpc/RpcClient.ts`, `src/finality/FinalityTracker.ts`, `src/health/HealthController.ts`, `src/api/{routes,middleware,walletHandlers,notImplemented}.ts`, `src/types/express.d.ts`; testler `src/**/*.test.ts` (10 dosya).
- **TRON servisi:** `sidecar-blockchain/src/wallet/hdRoot.ts` (yeni), `src/wallet/HdWalletService.ts`, `src/wallet/HdWalletService.test.ts`, `src/address/evmCasingCallSites.test.ts` (yeni).
- **Backend:** `Skinora.Shared/Persistence/Outbox/OutboxMessage.cs`, `.../Configurations/OutboxMessageConfiguration.cs`, `Persistence/Migrations/20261018094011_T150_OutboxRetryBackoff{,.Designer}.cs`, `AppDbContextModelSnapshot.cs`; `Skinora.API/Outbox/OutboxDispatcher.cs`, `OutboxOptions.cs`, `appsettings.json`; `Skinora.Transactions/Application/Networks/NetworkWorkQueue.cs` (yeni), `Transfers/OutgoingTransferDispatchJob.cs`, `Transfers/OutgoingTransferConfirmationJob.cs`, `Transfers/HttpBlockchainTransferClient.cs`, `PaymentAddresses/EnsurePaymentAddressJob.cs`.
- **Backend testleri:** `Skinora.API.Tests/Integration/OutboxTests.cs`, `Integration/NetworkDimension/NetworkQueueIsolationTests.cs` (yeni, SQL Server); `Skinora.Transactions.Tests/Unit/Transfers/OutgoingTransfer{Dispatch,Confirmation}JobTests.cs`, `Integration/PaymentAddresses/EnsurePaymentAddressJobTests.cs`, `Helpers/SettableNetworkHealth.cs` (yeni).
- **Altyapı:** `docker-compose.yml`, `.env.example`, `infra/prometheus/prometheus.yml`, `infra/grafana/provisioning/alerting/rules.yml`, `.github/workflows/ci.yml`, `.github/workflows/docker-publish.yml`, `secrets/README.md`.
- **Doküman:** `Docs/02_PRODUCT_REQUIREMENTS.md`, `07_API_DESIGN.md`, `05_TECHNICAL_ARCHITECTURE.md`, `06_DATA_MODEL.md`, `08_INTEGRATION_SPEC.md`, `11_IMPLEMENTATION_PLAN.md`, `DEPLOY_RUNBOOK.md`, `DEFERRED_BACKLOG.md`, `IMPLEMENTATION_STATUS.md`, `.claude/memory/MEMORY.md`.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Ağ bağlantısı birincil + yedek RPC; açık blok numaralarıyla okuma | ✓ | `RpcClient.test.ts` (23): beş taşıma arızasında yedeğe geçiş, `execution reverted`'da geçmeme, 30 sn bekletme ve geri dönüş, iki sağlayıcı da düşerse `AllProvidersFailedError` (URL/anahtar mesajda yok), yanlış zincirdeki sağlayıcının hiç kullanılmaması; durum okumalarının açık blok numarasıyla gitmesi ve `'latest'`/sayı verilirse reddi; `getLogs` sağlayıcı başına aralık (10 → 100), 2.345 adresin 1.000/1.000/345 bölünmesi, "çok büyük" cevabında yarıya bölme, ölçülen beş sınır mesajının cevap sayılmaması. **Canlı:** 2026-10-18 09:30 UTC Sepolia — ölü birincil (127.0.0.1:9) → PublicNode yedeği; 200 `finalized` blokta 615 USDC transferi, 1.621 adreslik alıcı listesiyle (iki parça) aynı 615 log; `getBalance` `finalized` blokta. Anahtarlı uçlar: ⚠ aşağıda |
| 2 | Adres türetme K7'nin ayrı tohumundan, T143'ün yolundan; imza anahtarları yalnız bu serviste; yakıt anahtarı sıcak cüzdanınkinden ayrı (⚠) | ✓ | Tohum yalnız Ethereum servisinin Docker secret'ı (`composeWiring.test.ts` 20 test: üç secret yalnız bu serviste, `/run/secrets/<ad>` yolu = kodun varsayılanı, başka hiçbir konteynerde yok, servis bloğunda env olarak imza materyali yok, TRON tohumu/anahtarı/TronGrid yok). `HdWalletService.test.ts` (20): T143'ün yedi Ethereum vektörü `derive` ve `deriveSigner` üzerinden (anahtar adresi kontrol ediyor). `walletKeys.test.ts` (7) + `startupWiring.test.ts` (aynı anahtar iki cüzdana → süreç `WALLET_KEYS_NOT_DISTINCT` ile çıkıyor). Docker imajı yerelde kuruldu ve bağlanmış secret dosyasıyla çalıştı (`depositIndex0` = vektör, `/health` 200) |
| 3 | Sağlık, metrikler, iç anahtar zorunlu (anahtarsız açılmaz) | ✓ | `startupWiring.test.ts`: `INTERNAL_KEY` boş → `INTERNAL_KEY_MISSING`, kısa → `INTERNAL_KEY_TOO_SHORT`, tohum yok, birincil RPC yok → süreç açılmadan çıkıyor. `routes.test.ts` (29): anahtarsız/yanlış/önek anahtar 401, ağ alanı yok/TRON/küçük harf 400, `/health` durumları (200 healthy / 200 degraded / 503 ×3), `/metrics`. `FinalityTracker.test.ts` (8): durumlar ve metrikler (yaş scrape anında büyüyor). `alertRuleWiring.test.ts` (3) kuralın ifadesini dışa aktarılan metrik adlarına bağlıyor |
| 4 | Kök düğüm tek üretim fonksiyonundan (`masterNodeFromPhrase` + `getRoot` birleşir); Ethereum vektörleri bu yoldan; `HD_ROOT_NOT_MASTER` servis üzerinden testli | ✓ | TRON `getRoot` → `masterNodeFromPhrase`; Ethereum servisi aynı fonksiyonun bayt bayt kopyasını kullanıyor (`hdRootParity.test.ts`: iki dosya eşit; yalnız `ethers` ve `../errors/SidecarError.js` içe aktarılıyor). Servis üzerinden: TRON `HdWalletService` `derive`/`deriveSigner` ve Ethereum `EthereumHdWallet` kurulumu, ethers'ın yolsuz kök döndürdüğü durumda `HD_ROOT_NOT_MASTER` veriyor; bozuk ifade `INVALID_MASTER_MNEMONIC`. Bozma: kök kontrolünü iki yerden de silmek ve `'m'` yolunu kaldırmak testleri kırıyor (aşağıda) |
| 5 | Sidecar adres karşılaştırma çağrı yerleri (TransferGuard sweep hedefi, MonitorRegistry anahtarı, izin verilen kontrat kümesi, FeeEstimationService) EVM yazımıyla testli | ✓ | `evmCasingCallSites.test.ts` (8). Bozma: yedi çağrı yeri bozması (`sameAddress` → `===`, büyük/küçük harf duyarsız TRON, `canonicalAddress` düşürme ×4, ücret servisinde `!==`) + 2 HD bozması: **10/10 yakalandı** |
| 6 | Not: `finalized` uzun durursa durum okuması arşiv isteyebilir (T151) | ✓ (not) | Arşiv reddi (`403 Archive requests …`) taşıma arızası sayılıp diğer sağlayıcıya geçiliyor (`RpcClient.test.ts`); `finalized` 30 dk'yı aşınca servis sağlıksız. Mutabakatın davranışı T151 |
| 7 | Yapılandırılmış ama erişilemeyen ağ ortak kuyrukları tıkamaz: outbox ve gönderim/onay işleri için backoff ya da ölü-mektup; testli | ✓ | Outbox geri çekilmesi + yeni kayıtlar önce (`OutboxTests` 7 yeni test, SQLite); **SQL Server:** `NetworkQueueIsolationTests` (migrated şema, T150 migration'ı dahil): Ethereum servisi düşükken 30 Ethereum izleme başlatma olayının arkasındaki TRON olayı 4. yoklamada işleniyor, alarm her satır için bir kez, servis dönünce 30 olayın hepsi teslim ediliyor; gönderim işi parti dolusu Ethereum satırının arkasındaki TRON satırını aynı turda gönderiyor ve Ethereum'u bir kez soruyor; onay işi aynı. Birim: gönderim (4 yeni), onay (3 yeni), adres işi (2 yeni). Bozma: **10/10 yakalandı** (aşağıda) |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| sidecar-ethereum (vitest) | ✓ 139/139 | `cd sidecar-ethereum && npm test` — 10 dosya; `tsc --noEmit`, `eslint src/`, `prettier --check` temiz; `npm audit --omit=dev` 0 |
| sidecar-blockchain (vitest) | ✓ 549/549 | `cd sidecar-blockchain && npm test` — 30 dosya (yeni: `evmCasingCallSites.test.ts` 8, HD testine +5); `tsc --noEmit` temiz |
| Backend birim | ✓ 1920/1920 | `dotnet test Skinora.sln --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` |
| Backend entegrasyon (SQL Server, Testcontainers) | ✓ 1692/1692 | Transactions 713, API 621 (yeni `NetworkQueueIsolationTests` 3), Fraud 82, Platform 74, Notifications 60, Disputes 58, Auth 40, Admin 22, Shared 16, Payments 6 — `dotnet test Skinora.sln --filter "FullyQualifiedName~.Integration"` |
| Backend kontrat | ✓ 26/26 | `dotnet test Skinora.sln --filter "FullyQualifiedName~.Contract"` |
| Format | ✓ | `dotnet format Skinora.sln --verify-no-changes --severity error` exit 0 |
| Compose | ✓ | `docker compose -f docker-compose.yml --env-file .env.example config --quiet` (profil kapalı) ve `--profile ethereum config --services` (servis listede); secret dosyaları yokken profil kapalı yapılandırma geçerli |
| Docker imajı | ✓ | `docker build sidecar-ethereum` (node:20-alpine, `npm ci`), imaj bağlanmış secret ve PublicNode Sepolia ile çalıştırıldı: `/health` 200, `depositIndex0` = T143 vektörü |

**Bozma (mutasyon) ölçümleri:**

- sidecar-blockchain, 10 bozma / 10 yakalandı: sweep hedefi `===` · büyük/küçük harf duyarsız · izin kümesi anahtarı ham · izin kümesi kurulumu ham · izleyici `start`/`stop`/`cadenceOf` anahtarı ham · ücret servisi `!==` · `hdRoot` kök kontrolü iki yerden silindi · kök `'m'` yolu olmadan.
- backend, 10 bozma / 10 yakalandı: outbox geri çekilmeyi yok saymak · outbox tek karışık en-eski-ilk parti (T150 öncesi) · alarm eşikten sonra her denemede · gönderim tur içi kesici yok · gönderim kesintideki ağı atlamıyor · gönderim tek ortak parti · onay kesici yok · onay kesintideki ağı atlamıyor · adres işi kesintideki ağı atlamıyor · round-robin yerine art arda ekleme.
- Betikler: yerel karalama dizininde (`mutate.mjs`, `mutate_cs.py`); her bozma uygulandı, ilgili test dosyası koşuldu, geri alındı.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS (F8 yönetici düzeni — bağımsız temiz bağlam ajanı) |
| Bulgu sayısı | 4 küçük (bloke etmeyen) |
| Düzeltme gerekli mi | Hayır — bulgu 2 belgelere yayıldı, 1/3/4 sonraki görev kriterine alındı |

## Doğrulama sonucu

- **Verdict:** PASS. Doğrulayıcı: bağımsız temiz bağlam ajanı (F8 yönetici düzeni).
- **Kanıt:** HEAD `2f0ba0b`, CI run `39120152616` başarılı. Bozma ölçümü 38 bozma / 35 yakalandı; kaçanlar: E4 (tohumun env yedeği), B4b (outbox sırası), E18 (eşdeğer).
- **Testler:** sidecar-ethereum 139/139, sidecar-blockchain 549/549, backend birim geçti, hedefli entegrasyon 46/46 + 36/36.
- **Dört küçük bulgu:** (1) onay işi kesicisi satıra özgü 4xx'te de açılıyor -> T152 kriteri. (2) Kesinti istisnası (ağ kesintideyse giden satırlar `PENDING` bekler) 05 §3.3, 08 §3.3 ve 06 `blockchain.transfer_retry_intervals_minutes` metninde yoktu -> bu commit'te üç belgeye yayıldı. (3) Outbox sırası ve tohum env yedeği bozmaları yakalanmıyor, bekleyen satır metriği yok -> T158 kriteri (a)-(c). (4) `ethereum-finalized-stalled` alarmı servis kapalıyken `or vector(0)` yüzünden susuyor -> T158 kriteri (d).
- **İki açık soru kararı:** (1) T151'e kod düzeyi kapı eklendi (`/health` yetenek bayrağı; yoksa ağ `supportedNetworks`'e giremez). (2) Kesintide `PENDING` bekleme para açısından güvenli: yeni gönderim yolu açmıyor, deneme hakkı artmıyor, çift gönderim riski önceden de vardı ve büyümüyor; görünürlük T158 metriğiyle sağlanacak.
- **Doğrulanamayan iddialar:** canlı Sepolia denemesi, secret'lı yerel imaj çalıştırma, yerel tam entegrasyon sayıları (CI destekliyor), `dotnet format`.

## Altyapı Değişiklikleri

- **Migration:** Var — `20261018094011_T150_OutboxRetryBackoff`: `OutboxMessages.NextAttemptAt datetime2 NULL` (Down kolonu düşürür). Veri dönüşümü yok; eski FAILED satırlar NULL → hemen denenir (öncekiyle aynı).
- **Config/env:** Var — `Outbox:RetryBackoffBaseSeconds` / `RetryBackoffMaxSeconds` / `RetrySlotsPerBatch` (appsettings); Ethereum servisi env'leri (`ETHEREUM_NETWORK`, `ETHEREUM_RPC_*`, `ETHEREUM_FINALIZED_*`, `ETHEREUM_SIDECAR_PORT`) ve üç Docker secret dosyası. Backend `ETHEREUM_SIDECAR_BASE_URL` boş kalır.
- **Docker:** Var — yeni imaj `sidecar-ethereum` (compose servisi `skinora-ethereum-sidecar`, profil `ethereum`), üst düzey `secrets:`; Prometheus hedefi; Grafana alarm kuralı; `docker-publish` matrisi 5 imaj.

## Commit & PR

- Branch: `task/T150-ethereum-servis-iskeleti`
- Commit'ler: `81324c4` (servis + HD + EVM testleri) · `210271e` (origin/main birleştirme — T147) · `db33296` (kuyruk yalıtımı, CI, compose, dokümanlar) · rapor commit'i (bu dosya)
- PR: #346 (https://github.com/turkerurganci/Skinora/pull/346)
- CI: ✓ PASS — run [`39119206523`](https://github.com/turkerurganci/Skinora/actions/runs/39119206523) (`db33296`, kod + dokümanlar): bütün işler success — lint (`Sidecar-ethereum typecheck + format + lint`, `npm audit` prod highs), build, birim, entegrasyon, kontrat, migration dry-run, 5 Docker imajı (`sidecar-ethereum` dahil), JS test (`Sidecar-ethereum vitest` dahil), 10 E2E advisory. Rapor commit'inin run'ı PR'da.

## Known Limitations / Follow-up

- **Anahtarlı sağlayıcı probu koşulamadı** (11 §F8 T142 "T150 anahtarla aynı probu koşar"): Alchemy/Chainstack hesabı ve anahtarı yok. Sınırlar T142'nin belgeden aldığı değerlerle ayar (Alchemy 10 blok, Chainstack 100 blok, 1.000 adres); istemci "çok büyük" cevabında aralığı kendisi daraltıyor. Prob T158/T169'a devredildi (⚠).
- **İskelet uçları 501:** izleme T151, gönderim T152, ücret tahmini T154. Backend `ETHEREUM_SIDECAR_BASE_URL` o zamana kadar boş kalmalı — doldurulursa backend Ethereum'u `supportedNetworks`'e açar ama ödemeyi izleyen olmaz (DEPLOY_RUNBOOK §B'de yazılı). Kod düzeyinde bir engel yok (açık soru 1).
- **501 ve backend:** gönderim işi 501'i geçici hata sayar → satır normal geri çekilmeyle denenir, hak bitince `FAILED` + alarm. Yanlış yapılandırmada (iskelete yönlendirilmiş backend) Ethereum gönderimleri 21 dk sonra `FAILED` olur; para hareket etmez.
- **Adres işinde tur içi kesici yok:** ayırıcının `SidecarUnavailable`'ı tek işlemlik 400'ü de kapsadığı için; ağ başına parti + kesinti atlama yeterli sayıldı (⚠).
- **Davranış değişikliği (⚠):** sağlık yoklaması bir ağı kesintide ilan ettiğinde (TRON dahil) o ağın gönderim satırları denenmez, `PENDING` bekler; eskiden kesinti boyunca denenip 1/5/15 dk sonra `FAILED` + alarm oluyordu. Kesinti alarmı zaten yoklamadan gidiyor.
- **Bir ağın kendi bekleyen satırları arasında rotasyon** (uzun `finalized` bekleyişi) T152 ölçütünde.
- **`finalized` durma eşiğinin iki kopyası:** servisin env'i (`ETHEREUM_FINALIZED_STALL_SECONDS`, otoriter) ve backend ayarı `blockchain.ethereum.finality_stall_alarm_minutes` (görünürlük; okuyan backend alarmı T158) — DEPLOY_RUNBOOK §D paritesi.
- **İmaj root çalışıyor** (TRON servisi gibi): compose secret'ları dosyanın sahibi/izniyle bağladığı için kullanıcı değiştirmek Linux sunucuda izin tuzağı açardı. Sertleştirme T158'de değerlendirilebilir.
- `hdRoot.ts` iki kopya (paylaşılan paket yerine): iki imaj ayrı dizinden kurulur, biri ötekinin kodunu taşımamalı (K7); eşlik testi tek fonksiyon olmasını sağlıyor.

## Notlar

- **Working tree:** temiz (görev başında `git status --short` boş; ayrı worktree, dal `origin/main`'den kesildi).
- **Main CI başlangıç kontrolü:** `39107652632` success · `39107652660` success · `39095082208` success (T148 ve T146 merge'leri).
- **Bağımlılıklar:** T142 ✓, T143 ✓, T148 ✓ (IMPLEMENTATION_STATUS). T147 çalışma sırasında main'e girdi (`7cf71c2`) ve dala normal birleştirmeyle alındı; çakışma çıkmadı.
- **Dış varsayımlar (task.md Adım 4):**

| Varsayım | Kanıt |
|---|---|
| `ethers` 6.17.0 var ve TRON servisinin ağacındaki sürüm | `sidecar-blockchain/package-lock.json` `node_modules/ethers` = 6.17.0 (override); `npm install` başarılı |
| express 4.x'in prod bağımlılıkları temiz | `express@4.22.1` → `qs` orta düzey uyarı; `npm view express dist-tags` latest-4 = 4.22.3 (`qs ~6.16.0`); 4.22.3 ile `npm audit --omit=dev` → 0 |
| Compose secret dosyası yokken profil kapalı yığın açılır | Karalama compose dosyasıyla ölçüldü: `docker compose config` hem profil kapalı hem açık çıkış 0; gerçek `docker-compose.yml` ile de aynı |
| Anahtarsız Sepolia ucu istemciyi canlı sınamaya yeter | PublicNode Sepolia: `eth_chainId` 0xaa36a7, `finalized` 11.940.099 (2026-10-18 09:30 UTC), 200 blok `eth_getLogs` + 1.621 adres |
| Alchemy / Chainstack anahtarları | **Kırık — anahtar yok.** Sınırlar belgeden (T142), prob T158/T169'a (⚠). Görev kapsamını bloklamıyor (iskelet sınırları ayar olarak taşıyor) |
| node:20 imajında `npm ci` kilit dosyasını kabul eder | `docker build sidecar-ethereum` başarılı |
| SQL Server entegrasyon testleri yerelde | Docker Desktop açık, Testcontainers SQL Server 2022 |

- **Kütüphane seçimi:** `ethers` 6.17.0 — TRON servisi ve T143 vektörleri zaten bu sürümle; 08 §3.7.1 `ethers` diyor; ikinci bir EVM kütüphanesi (viem) eklemek bağımlılık yüzeyini ikiye katlardı. RPC okumaları `ethers` sağlayıcısıyla değil kendi `fetch` istemcisiyle (gerekçe 11 §F8 "T150 kararları" (ii)). Sürümler `package.json`'da birebir sabit.
- **Yönetici düzeyi öneriler — `(öneriyle kaydedildi — ⚠)`** (11 §F8 "T150 kararları", F8 Gate Check'ten önce tek listede):
  1. Yerleşim: `sidecar-ethereum/`, `skinora-ethereum-sidecar`, port 5300, compose profili `ethereum`; backend `BaseUrl` T151–T154'e kadar boş.
  2. Kütüphane: `ethers` 6.17.0 + kendi JSON-RPC istemcisi; viem yok.
  3. Sırlar Docker secret (üç dosya); anahtar dosyaları T152/T153'e kadar boş olabilir; yakıt ≠ sıcak, sıcak = `ETHEREUM_HOT_WALLET_ADDRESS` açılışta denetlenir; imaj root.
  4. İç anahtar ≥ 32 karakter, her `/api` isteğinde zorunlu; `network` alanı zorunlu (TRON'daki istisna yok).
  5. İskelette 501 `NOT_IMPLEMENTED` (400 değil).
  6. `hdRoot.ts` bayt bayt kopya + eşlik testi (paylaşılan paket yerine).
  7. Sağlık: tracker'dan; 503 / degraded kuralları; `ethereum-finalized-stalled` alarmı T150'de, eşik servisin gauge'undan.
  8. Kuyruk yalıtımı: outbox geri çekilmesi (ölü-mektup yok, alarm bir kez); kesintideki ağı atlama (TRON dahil — davranış değişikliği); ağ başına parti + round-robin; gönderim/onay tur içi kesici, adres işinde yok.
  9. Anahtarlı sağlayıcı probunun T158/T169'a devri.
- **Açık sorular:**
  1. Backend'in `ETHEREUM_SIDECAR_BASE_URL`'i iskelete yönlendirilirse Ethereum yeni işlemlere açılıyor (izleme yokken). Şimdilik yalnız runbook uyarısı var; kod düzeyinde bir kapı (ör. servisin `/health`'inde "ödeme kabul eder" bayrağı) T151'e mi eklensin?
  2. Kesintide ilan edilen ağın gönderimlerini bekletmek (TRON dahil) doğru mu, yoksa TRON için eski "dene → 21 dk sonra FAILED" davranışı mı korunmalı?
