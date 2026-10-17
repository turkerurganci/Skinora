# T142 — Ethereum ön ölçümleri (Sepolia + mainnet salt okuma) [ÖLÇÜM GÖREVİ]

**Faz:** F8 | **Durum:** ⏳ Devam ediyor — yapım bitti, doğrulama bekliyor (F8 yönetici düzeni: doğrulama ayrı alt ajanda) | **Tarih:** 2026-10-17 (ölçümler 21:28–21:55 UTC)

---

## Yapılan İşler

- **Yalnız okuma yapıldı.** Anahtar yok, imza yok, işlem gönderilmedi, ücretli kayıt yok. Bütün sayılar zincirden (JSON-RPC), Binance'in açık ucundan ya da sağlayıcıların yayımlanmış belgelerinden geldi.
- **Ölçüm betikleri ve ham çıktıları** [`T142_probes/`](T142_probes/) klasöründe (Node 24, bağımlılık yok — yalnız `fetch`). Her betik tek başına yeniden koşulabilir: `node <betik>.mjs [argüman]`. 8–14 MB'lık ham `eth_feeHistory` JSON'ları depoya konmadı; aynı betik (`feehist.mjs`) onları yeniden üretir. Aşağıdaki her sayı bir betik çıktısına ya da bir URL'ye bağlıdır.
- **RPC sağlayıcıları ölçüldü ve seçildi** (§1): anahtarsız uçlarda `eth_getLogs` aralık / liste / geçmiş sınırları, hız sınırı davranışı, `finalized`/`safe` desteği; anahtarlı sağlayıcıların kota ve fiyatı belgelerinden; Skinora yükü için aylık kullanım hesabı.
- **Zincir gerçekleri okundu** (§2): `finalized` gecikmesi (20 örnek), gas dağılımı (30 gün tam, 365 gün örneklem, 2025-10-10 çöküşü), USDT ve USDC kontrat davranışı, Sepolia token'ları ve muslukları.
- **Gas birimleri gerçek makbuzlardan** (§3) ve satış başı maliyet bugünkü / p50 / p99 fiyatla.
- **Öneriler** (§4): ücret tavanı, "bekle" alarm eşiği, yakıt ve sıcak cüzdan ETH alarmları. Hepsi `(öneriyle kaydedildi — ⚠)`.
- **Belgelere işlendi:** 08 §3.7'deki "T142 ölçecek" yerleri dolduruldu (v3.17 → v3.18); 11 §F8'e "Yönetici kararları — T142" bloğu ve §7.6'ya yedi izlenebilirlik satırı (v1.1 → v1.2).
- **Bir T141 iddiası düzeltildi:** "kara listeye takılacak transfer `eth_call` simülasyonuyla yakalanır" yalnız gönderen için doğru. USDT kara listedeki **alıcıya** transferi engellemez (§2.3). Yeni kural: payout ve iade öncesi `isBlackListed(hedef)` okuması.

## Etkilenen Modüller / Dosyalar

Yalnız doküman ve ölçüm ekleri. Kod, migration, config, Docker değişikliği yok. Sidecar koduna ve T143'ün satırlarına dokunulmadı.

| Dosya | Sürüm | Değişiklik |
|---|---|---|
| `Docs/08_INTEGRATION_SPEC.md` | v3.17 → **v3.18** | §3.7 durum notu; §3.7.1 Token / USDT davranışı / RPC satırları; §3.7.4 Sorgu, Kabul, yeni Yoklama, Bakiye mutabakatı; §3.7.5 yakıt ve sıcak cüzdan alarmı, gas birimleri, artık; §3.7.6 yeni "Kara liste (alıcı tarafı)" satırı; §3.7.7 tavan, "bekle" eşiği, ölçüm; §3.7.8 dört satır; §8 risk matrisi; §9.1 iki satır; §9.3 musluklar |
| `Docs/11_IMPLEMENTATION_PLAN.md` | v1.1 → **v1.2** | §F8 "Yönetici kararları — T142" (sekiz ⚠ madde); "Ethereum akış ayrıntıları" maddesine kara liste düzeltmesi; §7.6'ya yedi satır |
| `Docs/TASK_REPORTS/T142_REPORT.md` | yeni | bu rapor |
| `Docs/TASK_REPORTS/T142_probes/` | yeni | 16 betik + 16 ham çıktı/log |
| `Docs/IMPLEMENTATION_STATUS.md` | — | T142 satırı ⏳ |
| `.claude/memory/MEMORY.md` | — | Current Status'a T142 satırı |

---

## Ölçümler

Zaman damgaları UTC'dir. "Betik" sütunu `T142_probes/` altındaki dosyadır; çıktısı aynı adlı `.out`/`.log` dosyasındadır.

### 1. RPC sağlayıcıları

#### 1.1 Anahtarsız uçlar — erişim ve `finalized` / `safe` (betik `probe.mjs`, 21:28:42–21:30:30Z)

| Uç | Sonuç (ham) |
|---|---|
| `ethereum-rpc.publicnode.com` | `chainId 0x1`, `finalized 26214876`, `safe 26214908` |
| `eth.drpc.org` | `0x1`, `finalized 26214876` |
| `mainnet.gateway.tenderly.co` | `0x1`, `finalized 26214876` |
| `1rpc.io/eth` | `0x1`, `finalized 26214749` (diğerlerinden 127 blok geride) |
| `eth-mainnet.public.blastapi.io` | `0x1`, `finalized 26214876` |
| `rpc.flashbots.net` | `0x1`, `finalized 26214876` |
| `rpc.ankr.com/eth` | `Unauthorized: You must authenticate your request with an API key` |
| `cloudflare-eth.com` | `chainId` var, `finalized`: `Cannot fulfill request` |
| `eth.llamarpc.com` | HTTP 525 (Cloudflare HTML) |
| `eth.merkle.io` | HTTP 429 `Rate limit exceeded` |
| `ethereum-sepolia-rpc.publicnode.com` | `0xaa36a7`, `finalized 11936610` |
| `sepolia.gateway.tenderly.co` | `0xaa36a7`, `finalized 11936610` |
| `sepolia.drpc.org` | HTTP 400 `chain is not available on free plan, please upgrade to paid plan` |
| `eth-sepolia.public.blastapi.io` | HTTP 403 `Blast API is no longer available. Please update your integration to use Alchemy's API instead` |
| `rpc.sepolia.org`, `rpc2.sepolia.org` | 15 sn'de cevap yok |

#### 1.2 `eth_getLogs` sınırları — sorgu şekli Skinora'nınki (betik `getlogs.mjs`, `bounds.mjs`, `sepolia.mjs`)

Sorgu: `address = [USDT, USDC]`, `topics = [Transfer, null, <alıcı listesi>]`, aralık `finalized`'da biter. Listenin ilk 20'si son 50 bloktaki gerçek alıcılar, kalanı rastgele adres.

Mainnet (`getlogs_mainnet.out`, 21:31:06–21:33:49Z, `finalized 26214908`):

| Uç | 10 blok | 100 | 1.000 | 5.000 | 10.000 | Liste 5.000 |
|---|---|---|---|---|---|---|
| PublicNode | ok | ok | ok (1 kez 403) | ok (38.320 log, 1,55 sn) | `403 Archive requests require a personal token` | `query exceeds the maximum of 1000 addresses or topics per search position` |
| dRPC (anahtarsız) | ok | ok | `ranges over 10000 blocks are not supported on free plan` | aynı | aynı | 10/100 blokta ok |
| Tenderly | ok | ok | ok (8.848 log) | liste ≥100'de `invalid params` | aynı | 100+ blokta `-32002 not supported` |
| 1RPC | ok | `eth_getLogs is limited to 0 - 50 blocks range` | aynı | aynı | aynı | 10 blokta ok |
| Blast (Alchemy) | ok | `You can make eth_getLogs requests with up to a 10 block range` | aynı | aynı | aynı | 10 blokta ok |

Sınır arama (`bounds.out`, 21:34:40–21:35:34Z):
- dRPC anahtarsız: 101 blok `ok`, 200 blok hata → sınır 101–199 arası (mesaj "10000" dese de).
- PublicNode geçmiş derinliği: başın 8.300 blok gerisi `ok`, 9.000 `403`, 9.500 `ok`, 12.000 `403` — yük dengeleyici arkasındaki düğümler farklı; güvenilir derinlik ~8 bin blok (~27 saat). 8.000 blokluk aralık `ok`.
- PublicNode durum (state) derinliği: `eth_call` başın 64 blok gerisinde `ok`, **128 blok gerisinde `403 Archive requests require a personal token`**.
- Tenderly: 2.000 / 3.000 / 4.000'lik liste 10 blokta `ok`; filtresiz USDT 1.000 blokta `Query returned more than 20000 results`.

Sepolia (`getlogs_sepolia.out`, 21:43:44–21:45:39Z; `sepolia.out`, 21:46:47–21:47:13Z): PublicNode 5.000'lik listeyi ve 10.000 blok aralığını kabul etti. **Tenderly 5.000'lik listede hata vermeden eksik döndü:** aynı 20 gerçek alıcı, aynı 1.000 blok; liste 20 / 1.000 / 2.000 / 3.000 → `logs=1569`, liste 5.000 → `logs=622`. İki tekrarda da aynı (`sepolia.out` satır `rep 0` ve `rep 1`).

Geth kaynağı (araştırma alt ajanı, `raw.githubusercontent.com/ethereum/go-ethereum/master`, 21:41Z): `LogQueryLimit: 1000` — "Maximum number of alternative addresses or topics allowed per search position in eth_getLogs filter criteria".

#### 1.3 Hız sınırı (betik `burst.mjs`, 21:42:44–21:43:31Z)

60 ardışık `eth_blockNumber` (~12–14 istek/sn) ve 30 eşzamanlı istek: PublicNode (mainnet + Sepolia), dRPC, Tenderly (mainnet + Sepolia) **hiç reddetmedi** (`ok: 60`, `ok: 30`). Ağır karışık yükte Tenderly `-32005 rate limit exceeded` döndü (21:32:15Z, `getLogs` matrisi ve `eth_call` aynı anda; `usdt.mjs`'in ilk koşusu). dRPC bir kez `Request timeout on the free plan` döndü (`fee_d30.log`, 211 çağrıdan 1'i).

#### 1.4 Anahtarlı sağlayıcılar — belgelerden (araştırma alt ajanı, 2026-10-17 21:28–21:43Z)

| Sağlayıcı | Ücretsiz plan | Birim maliyeti | `eth_getLogs` | Kaynak |
|---|---|---|---|---|
| Alchemy | "Free 30M CU per month", "300 CU/s"; tüm mainnet + testnet'ler. PAYG "$0.525/1M CU" | getLogs 60, eth_call 26, getBlockByNumber 20, getBalance 20, sendRawTransaction 40, receipt 20, feeHistory 10, estimateGas 20 CU | Free: **10 blok**; PAYG sınırsız aralık, cevap 150 MB | alchemy.com/pricing; alchemy.com/docs/reference/compute-unit-costs; alchemy.com/docs/reference/eth-getlogs |
| Chainstack | Developer: "Free 3M requests … 25 requests / second … No credit card required"; Sepolia var. Growth $49/ay | "1 RU per API request, regardless of method" (arşiv 2 RU) | Developer **100 blok**, Growth 10.000 | chainstack.com/pricing; docs.chainstack.com/docs/limits.md |
| Infura | Core: "3 million credits /day … 500 credits /second" | getLogs 255, estimateGas 300, diğerleri 80 | 10.000 sonuç, 10 sn, 5.000 parametre | infura.io/pricing; docs.infura.io/get-started/pricing/credit-cost/ |
| QuickNode | "Free trial … 1 month … 10M API credits 15 requests per second"; Build $49/ay 80M | tüm metotlar 20 kredi | deneme **5 blok**, ücretli 10.000 | quicknode.com/pricing; quicknode.com/docs/ethereum/eth_getLogs |
| dRPC | "Free 210M CU per month Only Public nodes … 100 requests per second" | her metot 20 CU | sayı yok ("may return an error or timeout") | drpc.org/pricing; drpc.org/docs/pricing/compute-units |
| Ankr | Freemium "200M API credits monthly" | EVM her metot 200 kredi (~1 M istek/ay) | Public 1.000 / Freemium 3.000 / Premium 10.000 blok | ankr.com/docs/rpc-service/service-plans/ |
| Cloudflare | Kamu ucu kullanımdan kalkıyor ("deprecating legacy hostnames … `cloudflare-eth.com`") | — | 800 blok | developers.cloudflare.com/web3/ethereum-gateway/ |

`finalized` / `safe` etiketi Alchemy, Chainstack, Infura, QuickNode ve dRPC belgelerinde yazılı.

#### 1.5 Aylık kullanım ve maliyet (betik `cost.mjs`)

Yük modeli (ağ başına): `finalized` başı her P sn'de bir (`eth_getBlockByNumber`); baş epoch başına bir kez ilerlediği için günde 384 sn'de bir = **225** `eth_getLogs` (32 blok; aralığı 32'den küçük sağlayıcıda ⌈32/sınır⌉ çağrı); ~300 aktif depozit için **saatlik** bakiye mutabakatı (`eth_call` + `eth_getBalance`, bu sıklık bir varsayımdır — T151 seçer); günde 40 giden işlem × 10 çağrı; bekleyen gönderim için dakikada bir `eth_feeHistory` (üst sınır). Sepolia test ortamı: 30 sn, 20 depozit, günde 10 işlem.

| Senaryo | Çağrı/ay | Alchemy Free | Chainstack Dev. | Infura Core (günlük) | QuickNode deneme | Ankr Freemium |
|---|---|---|---|---|---|---|
| Mainnet, 10 sn | 0,75 M | 17,4 M CU (%58) | 0,75 M (%25) | 2,06 M/gün (%69) | 15,9 M (%159) | 150,6 M (%75) |
| Mainnet, 30 sn | 0,58 M | 14,0 M CU (%47) | 0,58 M (%19) | 1,60 M/gün (%53) | 12,4 M (%124) | 116,1 M (%58) |
| Sepolia test | 0,17 M | 4,5 M CU (%15) | 0,17 M (%6) | 0,49 M/gün (%16) | 4,2 M (%42) | 33,6 M (%17) |

Mainnet (30 sn) + Sepolia: Alchemy **18,5 M / 30 M CU**, Chainstack **0,75 M / 3 M istek** → **aylık 0 $**. Alchemy Free aşılırsa PAYG: 18,5 M × 0,525 $ = **~9,7 $/ay**.

### 2. Zincir gerçekleri

#### 2.1 `finalized` gecikmesi (betik `lag.mjs`, 20 örnek, 21:35:49–21:54:58Z, PublicNode)

- **Mainnet:** baş − `finalized` = **63–93 blok**, zaman damgası farkı **768–1.128 sn (12,8–18,8 dk)**. Örnek ham satır: `{"t":"2026-10-17T21:35:49.728Z","net":"mainnet","head":26215000,"fin":26214908,"lagBlocks":92,"lagSec":1116}`.
- **Sepolia:** **58–87 blok**, **780–1.140 sn**.
- `finalized` 32 bloklık adımlarla ilerliyor (26214876 → 26214908 → 26214940 → 26214972 → 26215003), adımlar arası ~6,4 dk.
- Ölçülemeyen: geçmişteki kesinleşmeme olayları (RPC `finalized`'ın yalnız bugünkü değerini verir).

#### 2.2 Gas fiyatı (betik `feehist.mjs` + `analyze2.mjs`; ham sonuç `analyze2.out`, ilk geçiş `analyze.out`)

"Etkin fiyat" = baseFee + son 20 bloğun medyan tip'i (tek bloğun medyan tip'i MEV bloklarında 3.671 gwei'ye kadar sıçrıyor; bizim işlemimiz böyle teklif vermez). Satış başı gas 125.482 (§3), fiyat ETH 2.364,48 $.

| Pencere | Kaynak, toplama zamanı | Etkin gwei p50 / p90 / p99 / p99,9 / en yüksek | Satış başı $ p50 / p90 / p99 |
|---|---|---|---|
| Son 30 gün, 215.040 blok (25.998.929–26.214.992), tam | dRPC, 21:36:47Z (211 çağrıdan 1'i zaman aşımı → 1.024 blok eksik) | 0,18 / 1,45 / 5,82 / 19,67 / 38,73 | 0,054 / 0,43 / 1,73 |
| Son 365 gün, günde bir 1.024 blokluk pencere (373.760 blok, ~%14) | Tenderly, 21:38:39Z | 0,12 / 0,68 / 3,34 / 7,39 / 168,2 | 0,035 / 0,20 / 0,99 |
| 2025-10-10 çöküşü, 6.144 blok (23.546.872–23.553.015; merkez 23.549.943 = 2025-10-10T21:14:59Z) | Tenderly, 21:39:48Z | 2,47 / 51,5 / 359,7 / 502,6 / 552,2 | (bugünkü fiyatla) 0,73 / 15,3 / 106,7 |

- **Bugün** (blok 26.214.992): baseFee 0,1669 + tip 0,0500 = 0,2169 gwei → satış başı **0,064 $**.
- Çöküş saatinde ETH (Binance 1 saatlik mum, 2025-10-10 21:00Z kapanış) **3.949,99 $** → en pahalı blokta 125.482 × 552,2 gwei × 3.950 $ = **~274 $** (araştırmanın "~254 $"ının ölçülmüş karşılığı).
- Yakıt artığı dahil (yakıt = 46.201 × (2 × baseFee + tip)): 30 gün p50 0,069 $, p99 2,22 $.
- Eşik üstü süre ve bekleme bölümleri (`analyze2.out`):

| Eşik | 30 gün: blok payı · ≥10 dk bölüm sayısı · süre p50 / p90 / en uzun | 365 gün örneklem | Çöküş |
|---|---|---|---|
| 2 gwei | %6,38 · 51 · 0,46 / 2,03 / 4,48 sa | %2,53 · en uzun 2,66 sa | en uzun 6,96 sa |
| **3 gwei** | **%3,14 · 29 · 0,43 / 1,68 / 2,06 sa** | **%1,20 · en uzun 1,55 sa** | **en uzun 6,45 sa** |
| 5 gwei | %1,30 · 14 · 0,48 / 1,16 / 1,70 sa | %0,40 · en uzun 0,96 sa | en uzun 5,67 sa |

365 günlük örneklemin pencereleri 3,4 saatlik olduğu için oradaki "en uzun" pencereyle kesilmiştir.

- **Sepolia gas** (`sepolia.out`, 21:47:17Z, son 1.024 blok): baseFee p50 0,000005, en yüksek 0,000181 gwei — pratikte sıfır.

#### 2.3 USDT ve USDC kontratları (betik `usdt.mjs` 21:32:32Z, `blacklist.mjs` 21:49:24Z; blok 26.214.908 / 26.215.003)

| Okuma | Sonuç (ham) |
|---|---|
| USDT `eth_getCode` | 11.075 bayt |
| USDT `decimals()` | `0x…06` → **6** |
| USDT `basisPointsRate()` / `maximumFee()` | `0x…00` → **0** / **0** |
| USDT `paused()` / `deprecated()` / `upgradedAddress()` | 0 / 0 / `0x0` |
| USDT `owner()` | `0xc6cde7c39eb2f0f0095f41570af89efc2c1ea828` |
| USDT `transfer(fresh,1)` simülasyonu, `from` = `0x28c6c062…1d60` (bakiye 993.381.341,22 USDT) | dönüş verisi **`"0x"` (0 bayt)** |
| USDC aynı simülasyon | `0x…0001` (**32 bayt, `true`**) |
| USDT `estimateGas transfer(fresh,1)` / USDC | 64.007 / 63.054 |
| USDC `decimals()` / proxy uygulama yuvası | **6** / `0x43506849d7c04f9138d1a2050bbf3a0c054402dd` |
| USDT `Params` olayları, yayından (blok 4.634.748) bugüne | **0** (ücret anahtarı hiç açılmadı) |
| USDT `Pause` / `Unpause` / `Deprecate` olayları | **0 / 0 / 0** |
| USDT `AddedBlackList` olayları (aynı tam aralık — sorgunun işlediğinin sağlaması) | **3.173** (ilk 4.638.805, son 26.208.759) |
| Kara listedeki `0xa36d8304…4cad` (blok 26.184.056'da eklendi, bakiye 2.834.714,96 USDT) **adresinden** transfer | `execution reverted` |
| Bakiyeli bir adresten kara listedeki `0xa36d8304…4cad` **adresine** transfer | **`"0x"` — başarılı** |
| USDC: kara listedeki `0x8c3e09ec…3412` adresine transfer | `execution reverted: Blacklistable: account is blacklisted` |

Sonuç: USDT `transfer`'i `bool` döndürmez; ücret anahtarı kapalı ve hiç açılmamış; kara liste **yalnız göndereni** denetler.

#### 2.4 Sepolia token'ları ve muslukları (betik `sepolia.mjs`, 21:47:14–21:47:17Z; araştırma alt ajanı 21:37–21:43Z)

| Token | Kaynak | Zincir okuması |
|---|---|---|
| Circle USDC `0x1c7D4B196Cb0C7B01d743Fbc6116a902379C7238` | developers.circle.com/stablecoins/usdc-contract-addresses: "Ethereum Sepolia \| `0x1c7D…7238`" | `USDC`, 6 ondalık, `transfer` → `0x…01` |
| Aave "USDT" `0xaA8E23Fb1079EA71e0a56F48a2aA51851D8433D0` | github.com/bgd-labs/aave-address-book `AaveV3Sepolia.sol` | `USDT`, 6 ondalık, `transfer` → **`0x…01` (`bool` döndürüyor)** |
| Aave "USDC" `0x94a9D9AC8a22534E3FaCa9F4e7F2E2cf85d5E4C8` | aynı | `USDC`, 6 ondalık, `bool` döndürüyor |
| Aquanow "USDT" `0xb60B42C095d08776a6ecc9a246180FE37AbA02A5` | docs.aquanow.io | **18 ondalık** — USDT'ye benzemiyor |

- **Tether'in resmî Sepolia token'ı yok:** tether.to/en/supported-protocols yalnız mainnet adresini veriyor; Tether WDK belgesi (docs.wdk.tether.io/resources/concepts) "The USD₮ tokens … are not real … are not Tether Tokens" diyerek üçüncü taraf musluklara (Pimlico, Candide, Aave) yönlendiriyor.
- **Musluklar:** QuickNode `faucet.quicknode.com/ethereum/sepolia` — "once every 12 hours", "requires no Quicknode account", "no minimum mainnet balance". pk910 PoW `sepolia-faucet.pk910.de` — `minClaim 0.05 ETH`, `maxClaim 2.5 ETH`, captcha + tarayıcıda iş. Alchemy — "0.1 ETH every 24 hours", "at least 0.001 ETH on Ethereum Mainnet". Chainlink — "Drips 0.5 ETH", cüzdan bağlantısı. Chainstack — 0,5 ETH/24 sa, hesap + mainnet'te 0,002 ETH. Google Cloud Web3 musluğu JS ile açıldığı için okunamadı.

### 3. Gas birimleri (betik `receipts.mjs` 21:36:29Z, `estimate.mjs` 21:40:09Z; dRPC)

Bloklar 26.214.869–26.214.908 (40 blok), 430 sade USDT transferi (`to = USDT`, `transfer` seçicisi, tek log, `status = 1`). Sınıf: alıcının bir önceki bloktaki bakiyesi (0 mı?) × göndericinin işlem bloğundaki bakiyesi (0 mı?).

| Sınıf | Skinora karşılığı | n | gasUsed p50 (min–en yüksek) | `estimateGas` p50 (blok − 1'de, aynı from/to/tutar) | Örnek |
|---|---|---|---|---|---|
| Alıcı bakiyeli, gönderen boşalıyor | **sweep** (depozit → sıcak cüzdan) | 278 | **41.273** (41.249–46.085) | **46.201** | `0xf35eda03…c602` @26214908 |
| Alıcı bakiyeli, gönderende kalıyor | soğuk aktarım | 90 | 46.097 | 46.479 | `0x53f09ec9…1e34` @26214908 |
| Alıcı yeni, gönderende kalıyor | **payout / iade** (borsa depozit adresine) | 54 | **63.209** | 64.031 | `0xd39d6ecd…2ffa` @26214908 |
| Alıcı yeni, gönderen boşalıyor | sweep öncesi depozitten iade | 8 | 58.373 | 63.471 | `0x7b97988c…68ff` @26214908 |
| Yerel ETH, kod taşımayan adrese | **yakıt** | 58 | **21.000** | — | `0x9a84e3f7…a5d7` @26214908 |

`estimateGas` sweep'te gasUsed'dan ~4.900 fazla: boşalan bakiyenin gas iadesi işlem sonunda düşülür, gönderimin limiti iadeden önceki tutarı karşılamalıdır. Yakıt limiti tahminle hesaplanmalı.

**Satış başı** = yakıt 21.000 + sweep 41.273 + payout 63.209 = **125.482 gas**. ETH = **2.364,48 USDT** (`curl https://api.binance.com/api/v3/ticker/price?symbol=ETHUSDT` → `{"symbol":"ETHUSDT","price":"2364.48000000"}`, 2026-10-17T21:39:13Z). 1 gwei ≈ 0,297 $ / satış.

| Fiyat | gwei | Satış başı |
|---|---|---|
| Bugün | 0,217 | **0,064 $** |
| 30 gün p50 / p99 | 0,18 / 5,82 | 0,054 $ / 1,73 $ |
| 365 gün p50 / p99 | 0,12 / 3,34 | 0,035 $ / 0,99 $ |

### 4. Öneriler (hepsi `(öneriyle kaydedildi — ⚠)`)

1. **Ücret tavanı: işlem başına 0,50 $.** Sweep işlemi (yakıt + sweep, 67.201 gas limit) ve payout (64.031) ayrı ayrı 0,50 $; satış başı en çok 1,00 $. Canlı ETH kuruyla gwei'ye çevrilir: bugün 0,50 ÷ (67.201 × 2.364,48 × 10⁻⁹) = **3,15 gwei** (payout 3,30). Gönderim `baseFee + tip` tavanın altındaysa yapılır, `maxFeePerGas` tavanı aşmaz; kur yoksa bekler. **Gerekçe:** medyan satış 0,035–0,054 $; tavan ~10 kat pay bırakır; ~3 gwei'nin üstü 365 günde zamanın %1,2'si, son 30 günde %3,1'i. Daha düşük tavan (2 gwei ≈ 0,32 $) bekleme payını iki katına çıkarır (%2,5 / %6,4), daha yüksek tavan (5 gwei ≈ 0,79 $) beklemeyi %0,4 / %1,3'e indirir ama satış başı maliyet tavanı 1,6 $'a çıkar.
2. **"Bekle" eşiği: 3 saat.** Tavan yüzünden 3 saat kesintisiz bekleyen sweep ya da payout admin alarmı verir. **Gerekçe:** son 30 günün 29 bekleme bölümünün medyanı 26 dk, p90'ı 1,68 sa, en uzunu 2,06 sa; 2025-10-10 çöküşünde 6,45 sa. 3 saat olağan günlerde susar, çöküş gibi günlerde çalar.
3. **Yakıt cüzdanı alarmı: 0,06 ETH.** 7 günlük en yoğun yük (günde 20 sweep) tavanda: 7 × 20 × 0,000211 ETH = 0,0296 ETH; artı tavana takılmayan bir iadenin çöküş günü p99 fiyatıyla yakıtı: (21.000 + 64.031) × 360 gwei = 0,0306 ETH. Toplam ~0,06 ETH ≈ 142 $.
4. **Sıcak cüzdan ETH alarmı: 0,06 ETH.** Payout, sweep sonrası iade ve soğuk aktarım gas'ını sıcak cüzdan kendi ETH'siyle öder; T141 belgeleri bunu yazmıyordu. Aynı hesap (7 gün × 20 payout tavanda + bir çöküş günü iadesi). Elle doldurulur (runbook, T158).
5. **Alıcı tarafı kara liste kontrolü.** Payout ve iade öncesi `isBlackListed(hedef)` okunur; `true` ise gönderim yok, admin alarmı (§2.3 ölçümü).
6. **RPC: birincil Alchemy (Free), yedek Chainstack (Developer).** İkisi de Sepolia'yı kapsar, kart istemez, Skinora yükünde kotanın %62 / %25'i (§1.5). Elenenler: Infura Core (tek hesapta iki ağ %70–85), QuickNode (ücretsiz plan 1 aylık deneme, kota yetmiyor), Ankr Freemium (%75–92), anahtarsız uçlar (PublicNode durum derinliği ~127 blok, geçmiş ~8 bin blok, ara sıra yanlış 403; Tenderly sessiz eksik cevap + hız sınırı; 1RPC 50 blok; dRPC anahtarsız ~100 blok ve Sepolia yok).
7. **`eth_getLogs`:** adres listesi tek sorguda ≤ 1.000 (aşan parçalanır); blok aralığı sağlayıcı başına ayar (Alchemy Free 10, Chainstack 100); sonuç sınırı hatasında aralık yarıya bölünür; `finalized` başı **30 sn**'de bir yoklanır.
8. **`finalized` ilerlemiyor alarmı: 30 dk** (olağan en yüksek 18,8 dk ölçüldü).
9. **Sepolia:** T159 Tether kontratının doğrulanmış kaynağından kendi kopyasını kurar (Aave "USDT"si `bool` döndürdüğü için USDT'yi sınamaz); yanlış token provası Circle Sepolia USDC ile; test ETH QuickNode + pk910 musluklarından.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Birincil + yedek RPC sağlayıcısı seçildi; `eth_getLogs` blok aralığı ve adres listesi sınırları, ücretsiz/ücretli plan maliyeti ölçüldü | ✓ (anahtarlı sağlayıcılarda belgeden) | §1.1–§1.5; `probe.out`, `getlogs_*.out`, `bounds.out`, `burst.out`, `sepolia.out`, `cost.out`; 08 §3.7.1, §3.7.4; 11 §F8 T142 bloğu. Anahtarlı uçlar hesap açılmadan ölçülemezdi (görev kuralı: ücretli kayıt yok) — Known Limitations |
| 2 | `finalized` gecikmesi, gas fiyatı dağılımı, USDT kontratının davranışı (transfer bool döndürmüyor, blacklist, ücret anahtarı) zincirden okundu | ✓ | §2.1 (`lag.out`), §2.2 (`analyze2.out`, `fee_d30.log`, `fee_d365.log`), §2.3 (`usdt.out`, `blacklist.out`); 08 §3.7.1, §3.7.4, §3.7.7 |
| 3 | Ücret tavanı ve "bekle" eşiği için ölçülmüş öneri | ✓ | §4 madde 1–2 (`analyze2.out` bölüm sayımları); 08 §3.7.7; 11 §F8 T142 bloğu |
| — | (yönetici genişletmesi) Gas birimleri gerçek makbuzlardan, satış başı maliyet bugün / p50 / p99 | ✓ | §3 (`receipts.out`, `estimate.out`) |
| — | (yönetici genişletmesi) Yakıt cüzdanı alarmı önerisi | ✓ | §4 madde 3; 08 §3.7.5 |
| — | (yönetici genişletmesi) Sepolia token'ları, Tether test token'ı yokluğu ve T159 için yol, musluklar | ✓ | §2.4; 08 §3.7.1, §9.1, §9.3 |
| — | (yönetici genişletmesi) "T142 ölçecek" yerleri dolduruldu, sürümler artırıldı, ⚠ kararlar listelendi | ✓ | `grep -rn "T142" Docs/08_INTEGRATION_SPEC.md` — bekleyen "T142'de ölçülür/seçilir" ifadesi kalmadı; 08 v3.18, 11 v1.2; aşağıdaki ⚠ listesi |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit / Integration | Yok | Görevin test beklentisi "Yok (ölçüm raporu)"; kod değişikliği yok |
| Ölçüm tekrarlanabilirliği | ✓ | 16 betik `T142_probes/` altında; Tenderly sessiz eksik cevabı iki tekrarla, kara liste davranışı iki token ve iki yönle, gas birimleri 430 makbuz + 44 `estimateGas` ile |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor (ayrı doğrulama alt ajanı) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- Migration: Yok
- Config/env değişikliği: Yok (sağlayıcı anahtarları T150/T158'in işi)
- Docker değişikliği: Yok

## Commit & PR

- Branch: `task/T142-ethereum-olcumleri`
- Commit: `2175e43` — T142: Ethereum ön ölçümleri — mainnet + Sepolia salt okuma (bu PR numarası satırı sonraki commit'te)
- PR: #339 — https://github.com/turkerurganci/Skinora/pull/339
- CI: dal HEAD'inin run'ı PR açıklamasına ve doğrulama yorumuna yazılır (yalnız `Docs/` değiştiği için kod işleri paths-filter'la atlanır, CI Gate koşar)

## Known Limitations / Follow-up

- **Anahtarlı sağlayıcılar ölçülmedi, belgeden alındı** (Alchemy, Chainstack, Infura, QuickNode, dRPC anahtarlı, Ankr). T150 anahtarlar gelince `getlogs.mjs` / `bounds.mjs` / `burst.mjs`'i aynı sorgu şekliyle o uçlara koşmalı; özellikle Alchemy Free'nin 10 blokluk sınırı, Chainstack'in 100 bloğu ve iki sağlayıcının 1.000'lik liste davranışı (hata mı, sessiz eksik mi).
- **Geçmiş kesinleşmeme olayları ölçülmedi:** RPC yalnız bugünkü `finalized`'ı verir. 20 örnek olağan gecikmeyi gösterir, uç durumu göstermez.
- **365 günlük gas penceresi örneklemdir** (%14). Bekleme bölümlerinin uzunluğu orada 3,4 saatlik pencereyle kesilir; bölüm uzunlukları için 30 günlük tam veri ve çöküş penceresi esas alındı.
- **Tutarlar bugünkü ETH fiyatıyla** (2.364,48 $). Tavan dolar cinsinden olduğu için gwei karşılığı fiyatla değişir; bekleme payları ~3 gwei eşiğinde ölçüldü.
- **Mutabakat sıklığı varsayım** (saatlik). Aylık kullanımın en büyük kalemi budur; T151 başka sıklık seçerse §1.5 tablosu `cost.mjs` ile yeniden hesaplanır.
- **Sepolia test token'ının kurulumu** anahtar ve test ETH ister; T159'a bırakıldı.

## Notlar

### Görev öncesi kontroller

- **Working tree:** temiz (`git status --short` boş, 2026-10-17 21:27Z).
- **Main CI (Adım 0):** son üç tamamlanmış run `success` — `39070845359` (Docker Publish, T141 #337), `39063106414` (CI, #334), `39063106482` (Docker Publish, #334). O an koşan T141 merge CI'ı `39070845422` sonradan `success` (21:50Z kontrol).
- **Bağımlılık:** T141 ✓ (IMPLEMENTATION_STATUS satır 330).
- **Dal:** `task/T142-ethereum-olcumleri`, `origin/main` `6922b03`'ten.
- **Paralel iş:** T143 başka bir yapımcıda; sidecar koduna ve T143 satırlarına dokunulmadı.

### Dış varsayımlar (task.md Adım 4)

| Varsayım | Kanıt |
|---|---|
| Anahtarsız uçlarla mainnet + Sepolia salt okuma yapılabilir | `probe.out`: PublicNode, Tenderly, dRPC (mainnet) `finalized` döndürdü |
| Eski blok ücret geçmişi anahtarsız okunabilir | `feehist.mjs probe`: PublicNode 10.000 blok geride `403`; dRPC ve Tenderly 2,7 M blok geride `ok` → 30/365 gün bu ikisinden |
| Geçmiş bakiye (`eth_call` eski blokta) okunabilir | dRPC ve Tenderly başın 300.000 blok gerisinde `ok`; PublicNode 128'de `403` → makbuz sınıflaması dRPC'den |
| Binance ETH/USDT ucu açık | `api.binance.com/api/v3/ticker/price?symbol=ETHUSDT` 200 döndü; uç belgesi developers.binance.com/docs/binance-spot-api-docs/rest-api/market-data-endpoints |
| Anahtarlı sağlayıcı sınırları hesap açmadan ölçülebilir | **Kırık** — ölçülemez; belgeden alındı, T150'ye devredildi (Known Limitations). Görev kuralı ücretli kaydı yasaklıyor; ücretsiz kayıt da hesap açmak demek |

### Yeni yönetici düzeyi öneriler — `(öneriyle kaydedildi — ⚠)` listesi

F8 Gate Check'ten önce proje sahibine tek listede gösterilecek (11 §F8 "Yönetici kararları — T142"):

1. RPC ikilisi Alchemy (birincil) + Chainstack (yedek), mainnet ve Sepolia — 08 §3.7.1, §8, §9.1
2. `eth_getLogs` adres listesi ≤ 1.000, aralık sağlayıcı başına, `finalized` yoklaması 30 sn — 08 §3.7.4
3. Ethereum ücret tavanı işlem başına 0,50 $ (canlı kurla gwei; kur yoksa bekleme) — 08 §3.7.7
4. "Bekle" eşiği 3 saat — 08 §3.7.7
5. Yakıt cüzdanı ve sıcak cüzdan ETH alarmı 0,06 ETH; sıcak cüzdan ETH'si elle doldurulur — 08 §3.7.5
6. Payout / iade öncesi alıcı tarafı `isBlackListed` kontrolü — 08 §3.7.6, §3.7.8
7. `finalized` ilerlemiyor alarmı 30 dk — 08 §3.7.8
8. Sepolia: kendi Tether kopyası (T159) + Circle Sepolia USDC; QuickNode + pk910 muslukları — 08 §3.7.1, §9.1, §9.3

### Bulgu: T141'in kara liste cümlesi

T141 (11 §F8 "Ethereum akış ayrıntıları", 08 §3.7.8) "kara listeye takılacak transfer yayından önce `eth_call` simülasyonuyla yakalanır" diyordu. Zincir ölçümü bunun yalnız gönderen için doğru olduğunu gösterdi: USDT'nin `transfer`'i yalnız `msg.sender`'ı denetler, kara listedeki alıcıya giden transfer başarılı olur ve para donar. İki yer düzeltildi; öneri 6 boşluğu kapatır. Kod henüz yok (T152), dolayısıyla bu bir belge düzeltmesidir.
