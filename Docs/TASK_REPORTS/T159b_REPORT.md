# T159b — Sepolia giden bacağı yeniden provası (müdahalesiz)

**Faz:** F8 | **Durum:** ✓ Tamamlandı — doğrulama PASS | **Tarih:** 2026-10-21 (prova 00:49–01:53 UTC)

---

## Özet

| # | Senaryo | Ağ | Sonuç | İşlem |
|---|---|---|---|---|
| 1 | Mutlu yol: ödeme → kesinlik → teslimat (sahte Steam) → mutabakat → **sweep (yakıt otomatik)** → payout **taze** satıcı adresine | Sepolia | ✓ `COMPLETED`, **elle müdahale yok** | `f7de324d` |
| 2 | Sweep öncesi iade (admin iptali, `BUYER_REFUND`) → **taze** iade adresi | Sepolia | ✓ `CANCELLED_ADMIN`, 9,70 iade adresine, **elle müdahale yok** (kesinti 0,50 yedek değer — bulgu B1) | `4d21e5dd` |
| 3 | Yanlış token (Circle USDC) → `WRONG_TOKEN_REFUND` → **taze** iade adresi | Sepolia | ✓ 5,00 USDC → **4,07 USDC** iade (gas 0,93 düşüldü), **ön token / 1 wei yok** | `c0930069` |
| 4a | Reddedilen ödeme — **eksik ödeme** (`INCORRECT_AMOUNT_REFUND`) | Sepolia | ✓ 5,00 → **4,08** (taban − gas 0,92), zincirde ölçüldü | `0fd198c9` |
| 4b | Reddedilen ödeme — **fazla ödeme** (`EXCESS_REFUND`), ardından admin iptali (`BUYER_REFUND`, aynı depozitten ikinci çıkış) | Sepolia | ✓ fazla 5,00 → **4,08**; iptal iadesi 9,70; ikinci yakıt 21.000 gas | `85b886b2` |
| 4c | Reddedilen ödeme — **geç ödeme** (`LATE_PAYMENT_REFUND`) | Sepolia | ✓ 10,20 → **9,28** (gas 0,92 düşüldü) | `af2e8dd4` |
| 5 | TRON mutlu yolu (T159 senaryo 5'in tekrarı) | Nile | ✓ `COMPLETED`, payout 0,882 | `da4f02a3` |
| 6 | TRON reddedilen ödeme — **eksik ödeme**, T159a kesintisi | Nile | ✓ 2,67 → **2,30** (gas 0,37 düşüldü), zincirde ölçüldü | `e7305d8d` |

**Kabul kriteri karşılandı:** Sepolia'da yeni depozitle sweep (yakıt dahil), sweep öncesi iade ve yanlış token iadesi **elle ETH / ön token olmadan** tamamlandı. T159'da üçü de geri dönen yakıt (`FUEL_REQUIRED`) ya da `FEE_ABOVE_CAP` ile durmuştu. Bu provada **7 yakıtın 7'si zincirde status 1** (yeni hesaba 6 kez 204.600 gas — tahmin 207.391; var olan hesaba 1 kez 21.000), **8 giden token gönderiminin 8'i status 1** (1 sweep, 1 payout, 6 iade), hiçbir `FUEL_REQUIRED`, `FEE_ABOVE_CAP`, `GAS_ESTIMATE_ABNORMAL` ya da geri dönen işlem yok.

**T159a kesintisi zincirde ölçüldü (iki ağ):** reddedilen ödeme ailesinin dört türü (eksik, fazla, yanlış token, geç) Sepolia'da `Amount = taban − GasFee` ile yazıldı ve **zincirde tam o net tutar** iade adresine ulaştı; TRON'da eksik ödeme iadesi aynı kuralla (2,67 − 0,37 = 2,30) gitti.

**Ayar geri alma:** `blockchain.ethereum.fee_cap_usd_per_transfer` 0,50 → **1,00** (00:51:24) → **0,50** (01:53:09, DB ve admin API'den teyit). Ayrıca geçici `max_concurrent_transactions` 5 → 8 → **5** ve `min_refund_threshold_ratio` 2,0 → 1,0 → **2,0** (aşağıda "Değiştirilen ayarlar").

**Bulgular:** B1 🟡 ⚠ PARA (küçük) — iptal iadesinde gas tahmini tek atış; düğümün geçici hatasında 0,50 sabit yedeğe düşüyor (2/2 admin iptalinde). B2 🟡 — yığın açılışındaki mutabakat turu servisler hazır olmadan koşup iki ağda alarm bırakıyor. B3 ℹ — yanlış token iade satırının `Token` alanı `USDT` (gönderilen USDC, doğru). B4 ℹ — kesinti ve yakıt payı depozitlerde kalıyor (T159a ⚠ kararı 2, bilinen). B5 ℹ — Nile'de üçüncü tarafın 0,000001 USDT tozu doğru işlendi. Kod değişikliği yapılmadı.

---

## Doğrulama sonucu

**✓ PASS** — bağımsız doğrulama, HEAD `7a5602e5`, Opus high (izole alt ajan). Üç kabul kriteri ve T159a (ix) kesintisi zincirde kanıtlandı; TRON regresyonu uçtan uca geçti. Rapordaki **27 hash'in 27'si zincirden bağımsız okundu** (Sepolia 21, Nile 6), sapma yok. 12 Sepolia adresinin tüm geçmişi tarandı, dışarıdan ETH / ön token girişi yok. Bulgular 5 adet S4 (öneri / bilgi):

- **S4-1 (B1, ⚠ PARA):** kodda teyit edildi; `DEFERRED_BACKLOG.md` §4'e 🟡 olarak yazıldı (hedef F9 canlı öncesi).
- **S4-2 (düzeltildi):** Sepolia'da 6 depozit var (indeks 4–9); 7 olan yakıt sayısı. "Yedi depozit" ifadeleri düzeltildi.
- **S4-3 (düzeltildi):** TRON zamanları blok zamanıyla düzeltildi (ödeme `67f6b470` 00:54:54; `6e7c4eb5` 01:17:30). "7 sn tespit" iddiası yerine ölçülen **~62 sn** (bloktan tespite) yazıldı.
- **S4-4:** çoklu ödeme türü (T159a (ix)) Ethereum'da koşulmadı; **T160'a not**; backlog ⚪ satırı.
- **S4-5:** yanlış token satırında `Token = USDT` tasarım gereği (06 §3.8, "Token = expected"); yalnız gösterim.

B2 (açılış mutabakat turu): log görülmedi, doğrulanamadı; backlog 🟡. Doğrulanamayanlar (kabul kriteri değil): yerel DB ayar geri alma ve satır değerleri.

## Ortam

- **Kod:** dal `task/T159b-sepolia-giden-bacak`, `origin/main` `eb66ffe6`'dan (T159a birleşmiş). Yığın bu koddan `--build` ile kuruldu: `escrow-skinora-backend`, `escrow-skinora-ethereum-sidecar`, `escrow-skinora-frontend` imajları 2026-10-21 00:49–00:50 UTC'de yeniden derlendi. TRON servisi ve `sidecar-fake` imajları önbellekten geldi (T159'dan beri `sidecar-blockchain/` ve `sidecar-fake/` değişmedi — `git log 92154a6d..HEAD -- sidecar-blockchain sidecar-fake` boş).
- **Yığın komutu:** `docker compose -p escrow -f docker-compose.yml -f <katman> --env-file C:/projects/Escrow/.env --env-file C:/projects/Escrow/secrets/sepolia-rehearsal.env --profile ethereum up -d --build --wait`, ardından `docker restart skinora-reverse-proxy skinora-grafana` (runbook §G.5). Katman (commit'lenmedi, T159'unkinin aynısı): Steam sidecar yerine `sidecar-fake`'in Steam yüzeyi; ödeme bacakları gerçek (TRON → Nile, Ethereum → Sepolia). Prova sonunda `docker compose … down` (birimler korundu, `-v` yok); Docker Desktop açık bırakıldı.
- **Şema:** 59 migration uygulanmış, repo 59 — değişiklik yok.
- **Ethereum servisi açılışı:** chainId 11155111, `acceptsPayments: true`, `usdt: ok` (Tether kopyası `0x5b7beEc237F345aFF63617F4361b3D99f6e1504D`, fee switch kapalı), yakıt cüzdanı `0x6436aDD40861191B8B5F5b98078B9069C2d53239`, tabanlar 0,005 ETH (prova env'i). `/transactions/params` → `supportedNetworks: [TRON, ETHEREUM]`.
- **Kullanıcılar:** T159'un fikstür kullanıcıları (`T159 Seller/Buyer/Admin`, Id `15900000-…-0001..3`); oturum `.env`'deki `JWT_SECRET`'tan üretilen JWT ile (e2e `jwt.ts` deseni). Satıcının Ethereum ödeme adresi SQL ile taze adrese çevrildi (`ChangedAt = NULL` korunarak — fikstür, 24 sa bekleme yok).
- **Taze adresler** (rastgele yeni anahtarlar, `C:/projects/Escrow/secrets/sepolia-t159b-wallets.json`, gitignored, içerik yazdırılmadı). Prova öncesi (00:51 UTC) hepsi **0 ETH / 0 USDT kopyası / 0 USDC**:

| Rol | Adres |
|---|---|
| Satıcı ödeme (senaryo 1) | `0x365b72FF56516c9287A95EF49a2a8b2A5ee66596` |
| İade adresi — senaryo 2 | `0xaFB95F31882a825B49F8bcaCd39c829DC448625A` |
| İade adresi — senaryo 3 | `0xd9B2dEbD55cD4eD15dDdb22D316D96d50F833DC3` |
| İade adresi — senaryo 4a | `0x595344116f94BD77866DCDaA5EaeA2e72a2d8B54` |
| İade adresi — senaryo 4b | `0x9561D761CAb4EFdfBEC51D5f373dd141DE7E50dA` |
| İade adresi — senaryo 4c | `0xBD012Ca5B200503EfE0443751f25FaB9CEa47772` |

  Senaryo 1'in iade adresi (iade olmayan akış) eski `0x0285Be44D792dE8EcbBaf571b9017Fd66C2F0454`. Altı depozit (HD indeks 4–9) yeni türetildi; ödemeden önce altısı da 0 ETH / 0 token.
- **Sıcak cüzdan** `0xeC18E0708D68f11ac73b4cf4F8280d369ca1404d` taze değil (T159'dan 0,200001 USDT); sweep hedefi bakiyeli olduğu için sweep 43.598 gas yolundan geçti (hazırlık raporu R1 notu — taze sıcak cüzdan env + anahtar değişikliği isterdi, yapılmadı).
- **RPC:** anahtarsız PublicNode Sepolia (birincil), Tenderly (yedek).
- **Sepolia ücretleri:** taban ~16 wei; servis ~1 gwei üst ücretle imzaladı (`maxFeePerGas` 1.000.000.015–16 wei, etkin 1 gwei — Sepolia'nın bahşiş medyanı, T159a madde 6). Alıcı ödemeleri ethers varsayılanıyla 200.024 wei ile gitti.

## Elle müdahale denetimi

| Ne | Yapıldı mı |
|---|---|
| Depozite ETH / token | **Hayır** — altı depozitin tüm ETH'si servisin yakıtından (aşağıdaki yakıt hash'leri), tek token girişi alıcının ödemesi |
| Satıcı / iade adresine ön token | **Hayır** — altı taze adres ilk token'ını ürünün gönderiminden aldı |
| Alıcının fonlanması | Sepolia: **gerekmedi** (alıcıda 969,4 USDT kopyası + 9,8 USDC vardı; yanlış token senaryosu 5 USDC ile açıldı). Nile: tek USDT kaynağı sıcak cüzdan olduğu için sıcak → alıcı 1,06 USDT (`8f8037e5…995a`) ve 1,79 USDT (`3860c895…f1c5`) — ödeme amaçlı, T159'daki emsal |
| Prova kısayolu | `PayoutEligibleAt = now` yalnız `f7de324d` ve `da4f02a3` (runbook §G.4 10a; 8 günlük mutabakat bekleyişi) |
| Kod / env değişikliği | Yok |

---

## Senaryolar — ölçümler ve hash'ler

Bağlantı tabanları: Sepolia `https://sepolia.etherscan.io/tx/<hash>`, Nile `https://nile.tronscan.org/#/transaction/<hash>`. Tüm Sepolia hash'lerinin makbuzu prova sonunda PublicNode'dan yeniden okundu (`eth_getTransactionReceipt`); hepsi **status 1**.

**Kesinti tahmini (Ethereum, depozit yolu — T159a §5):** `feeWei = tokenGasLimit × maxFeePerGas + fuelGasLimit × beklenen fiyat`, ETH fiyatı 2.444,861904 USDT (Binance). Örnek (4a): `168.387 × 1.000.000.015 + 207.391 × ~1e9 = 375.778.002.525.805 wei` → **0,92 USDT**. Gerçek zincir maliyeti (yakıt 204.600 + token 152.417 gas × 1 gwei) = 0,000357017 ETH = **0,873 USDT**; fark %10 token gas payı ve yakıt tahmininin (207.391) kullanılandan (204.600) büyük olması.

### 1. Ethereum mutlu yolu — `f7de324d`, fiyat 10,00 USDT

| Adım | Zaman (UTC) | Kanıt |
|---|---|---|
| Kabul + hazırlık; depozit `0x99B89640723F46743895C648236da35568Af8399` (indeks 4), `MonitorFromBlock` 11.958.884 | 00:52 | DB `PaymentAddresses` |
| Ödeme 10,20 USDT kopyası | 00:52:53 | `0xe269ad30fa9e60eaf7d9e017b4edf0c74a75239f8faa023d7d979ab61e517ec9`, blok 11.958.978, 152.417 gas (depozit ilk token alımı) |
| Kesinlik → `PAYMENT_RECEIVED` | 01:12:23 (**19 dk 30 sn**) | `BUYER_PAYMENT` `CONFIRMED` |
| Sahte takas + alıcı onayı → `ITEM_DELIVERED`; `PayoutEligibleAt = now` (değer 2026-10-29'dan) | 01:12:57 | |
| Mutabakat doğrulandı → sweep kuyruğu | 01:15:09 / 01:15:10 | `SettlementVerifiedAt` |
| **Yakıt** (yakıt cüzdanı → depozit, `purpose: SWEEP`) | 01:16:11 | `0x479cb8abbe80a64892fd7517850cc3298f64ca80b422db30b622c6b58bd53dd0`, blok 11.959.095, **status 1**, gas limiti 207.391 / kullanılan **204.600**, değer 60.074.000.901.110 wei, tavan payı 317.152.846.226.256 wei |
| Sweep 10,20 → sıcak cüzdan | 01:18 → 01:32:12 | `0x684839972d6315a31d470efc8392e7cc065aad5951ecaed053061fe6fb2bc6ae`, blok 11.959.105, 43.598 gas (limit 60.074) × 1 gwei |
| Payout **10,00** → taze satıcı adresi (satırda tahmini gas 0,38; Ethereum'da satıcıdan kesilmez, 02 §5) | 01:33:13 → 01:51:00 | `0x133c26428a48b5a50820c60050358a8905741bd910f9faaedf139bfff4db2476`, blok 11.959.180, **152.417 gas** (ilk token alımı, limit 168.966), sıcak cüzdandan |
| `COMPLETED` | 01:51:00 | satıcı adresi 10,0 USDT; sıcak cüzdan 0,200001 → 0,400001 USDT (komisyon); depozitte artık 0,000016476 ETH (yakıtın kullanılmayan gas payı), 0 USDT |

### 2. Ethereum sweep öncesi iade — `4d21e5dd`

Depozit `0x4D3ba2b63533fdC13e3D85CFD58A941394DDEbb2` (indeks 5). Ödeme 10,20 `0x58a3a3db51131557e9454c4ddcc0287503bcb2e2d659fb8ab4dfddd78044218c` (blok 11.958.979) → `PAYMENT_RECEIVED` 01:12:23 → admin iptali 01:12:58 (`CANCELLED_ADMIN`) → `BUYER_REFUND` **9,70**, `GasFee` **0,50** (tahmin `eth_estimateGas: block not found` ile düştü, sabit yedek — **B1**) → yakıt `0x3558a2f1139299ae0e16dfa27aadcb9e9718b393368e99ab63251590af72c862` (blok 11.959.091, status 1, 204.600 gas, değer 168.966.002.534.490 wei) → iade `0xc4fcd7638a8c9d42850bea25b0428fe92023d315ad56511dbeaa6d53c67dfb53` (blok 11.959.101, **152.417 gas** — taze adres) → `CONFIRMED` 01:32:13. İade adresinde **9,70 USDT**; depozitte 0,50 USDT kaldı.

Not: ilk denemeler `RPC_UNAVAILABLE` (`eth_feeHistory: request beyond head block`) ile geçici olarak düştü, satır `PENDING` kaldı, bir sonraki turda gitti — beklenen geçici hata yolu.

### 3. Ethereum yanlış token (Circle USDC) — `c0930069`, fiyat 5,00 (beklenen 5,10 USDT)

Depozit `0x99A331aA097e1Cc708D0281FAa7b9c6765F4d150` (indeks 6). Alıcı USDT yerine **5 USDC** gönderdi `0x0f34d40671b98f6d83ca1daefac655cad3adccd56c861f1980f5d1bac42e093f` (blok 11.958.980) → 01:12:24 `WRONG_TOKEN_INCOMING` (`ActualTokenAddress` = Circle USDC `0x1c7d…7238`) + `WRONG_TOKEN_REFUND` **4,07**, `GasFee` **0,93** (tahmin: USDC token gas 169.926 + yakıt 207.391) → **ön USDC yok, 1 wei yok:** yakıt `0x126f44a0f4a61c525de3d768d02423fd9e569519fc79980d817b2fd4c1ce0494` (blok 11.959.080, status 1, 204.600 gas, değer 169.567.002.543.505 wei; servis uyarısı *"Fuel costs more than it delivers (the deposit holds no ETH; its account is created by the fuel) — sent, within the cap"* — T159a §4 uyarlaması) → USDC iadesi `0x2369eab9608c21132b0703769db85e374ec7c089bdf238fb1635de6198af9f64` (blok 11.959.090, **151.779 gas**, limit 169.567) → `CONFIRMED` 01:32:13. İade adresinde **4,07 USDC** (gelen token cinsinden kesinti, T159a ⚠ 1). Depozitte 0,93 USDC kaldı. Ödeme süresi dolunca 01:52 `CANCELLED_TIMEOUT`.

T159'da aynı akış `FEE_ABOVE_CAP` ile durmuş, iade adresine 0,000001 USDC ve depozite 1 wei elle gönderilmişti.

### 4a. Ethereum eksik ödeme — `0fd198c9`, beklenen 10,20

Depozit `0x4085867343Ff966df5d5C065Ad090785408740A6` (indeks 7). Ödeme **5,00** `0xb1d12f9f3cddf26b155209a40d478229d70c398cad7688313211ef21bce83b19` (blok 11.958.981) → 01:12:24 `INCORRECT_AMOUNT_REFUND` **4,08**, `GasFee` **0,92** → yakıt `0xbd881fd5ac30d4766130154aa71a7c51b38bd022b11e8bce45e0420cda0a1fa0` (blok 11.959.090, status 1, 204.600 gas) → iade `0x7fff970e4a4aae7a7f9d3da3e840dae603d499de536b198d68d8ef557ac85f8e` (blok 11.959.100, 152.417 gas) → `CONFIRMED` 01:32:13. İade adresinde **4,08 USDT** = 5,00 − 0,92. Depozitte 0,92 kaldı. 01:52 `CANCELLED_TIMEOUT`.

Backend log: *"Underpayment refund queued — expected=10.20 received=5.00 net=4.08 gasFee=0.92"*.

### 4b. Ethereum fazla ödeme + iptal — `85b886b2`, beklenen 10,20

Depozit `0xb463a50995E0C8cF82E37054a18A0d1EB0828f90` (indeks 8). Ödeme **15,20** `0x1c0a74fb68088c20b38727dc57b5f5ba2aec988d51f5c8b76abf563b711fdabe` (blok 11.958.982) → 01:12:25 `PAYMENT_RECEIVED` + `EXCESS_REFUND` **4,08** (fazla 5,00 − gas 0,92) → yakıt `0xd81f9ca533db14f431047f49c4966bec2c1eb07a700a40714e8b11d5d6e4c2e0` (blok 11.959.091, status 1, 204.600 gas) → iade `0x223bb665f45781565a3f014f56f793d6ffc8a2e25c6049a988c34215c28cd13e` (blok 11.959.100, 152.417 gas) → `CONFIRMED` 01:32:13.

Ardından admin iptali 01:32:50 → `BUYER_REFUND` **9,70** (`GasFee` 0,50 — tahmin yine `block not found`, **B1**) → **ikinci yakıt, var olan hesaba:** `0x84e9e6d687e40201870dc792552559e1483f9542d66100110c4411c4b6405ba0` (blok 11.959.175, status 1, **21.000 gas**, değer 44.276.998.438.726 wei) → iade `0x1304a594f4f55dda865c9d975a23129544ac8e76dba07d5f1b58c8c71e2aae0e` (blok 11.959.185, **54.497 gas** — adres artık USDT tutuyor) → `CONFIRMED` 01:51:00. İade adresinde 4,08 + 9,70 = **13,78 USDT**; depozitte 1,42 (0,92 + 0,50) kaldı. Bu, yakıt tahmininin hedefin hesap durumunu doğru izlediğini gösterir (yeni hesap 207.391 → var olan hesap 21.000).

### 4c. Ethereum geç ödeme — `af2e8dd4`

Hazırlık onayından sonra alıcı iptal etti (00:52, `CANCELLED_BUYER`, depozit `0x4E4741A143D58074D78Bac0A7b313b2164AC6369` (indeks 9) `POST_CANCEL_24H`) → ödeme 10,20 `0x3245b802cdeb2cb3bb4116f0f1d939410b06ed1401300478bab39f715238ae88` (blok 11.958.983) → 01:12:25 `payment.late_detected` + `LATE_PAYMENT_REFUND` **9,28**, `GasFee` **0,92** → yakıt `0x265efe4c3c757101cfe228da7cd7b74c8d5a35d499fcbee61a6d4a9704f000a1` (blok 11.959.091, status 1, 204.600 gas) → iade `0x4febf99706aa8ad0316391ebba95c3e3064958c1add7bb542d5818f1655a63cd` (blok 11.959.101, 152.393 gas) → `CONFIRMED` 01:32:13. İade adresinde **9,28 USDT** = 10,20 − 0,92 (T159'da aynı akış brüt 10,20 gitmişti).

### 5. TRON regresyonu (Nile) — `da4f02a3`, fiyat 1,00

| Adım | Zaman (UTC) | Kanıt |
|---|---|---|
| Kabul: toplam **2,02** (1,00 + 0,02 komisyon + 1,00 ağ ücreti satırı, K8); depozit `TFJ4rA2Qj61CuYxW6iLenjLTG2YBewpvLj` (indeks 3) | 00:52 | API |
| Ödeme 2,02 USDT | 00:54:54 (blok zamanı) | `67f6b47024d0292885a7c7a78761e3b4302b921b878a036f2cd96278c86b1069`, blok 72.003.601 |
| Tespit / 20 onay → `PAYMENT_RECEIVED` | 00:55:56 / 00:56:56 (bloktan **~62 sn** sonra tespit) | |
| Sahte takas + alıcı onayı → `ITEM_DELIVERED`; `PayoutEligibleAt = now` | 00:57:05 / 00:57:10 | |
| Mutabakat doğrulandı | 01:00:06 | |
| Sweep 2,02 → sıcak cüzdan (hesap açma 1 SUN + yakma takviyesi; tahmin 0,988 USDT) | 01:01:06 → 01:05:08 | `1d2e1aaa9791c19da602f35dd778ea13e7153a12e1270532f4d3bea6478cbed1`, blok 72.003.749 |
| Payout **0,882** (1,00 − 0,12 gas; 02 §5) | 01:06:09 → 01:09:09 | `e8b9582b8f437cbc2cd1c10ae0e3b38837db37d978bacef1d724c3ce7bdadbe8`, blok 72.003.846, ücret 345.000 SUN |
| `COMPLETED` | 01:09:17 | T159 ile aynı biçim ve sayılar (0,882 / 0,12) |

### 6. TRON eksik ödeme — `e7305d8d`, fiyat 2,00 (beklenen 3,04)

Depozit `THmvon1aTzDhURM6LUdQftR8HqkQBkPXoo`. Ödeme **2,67** `6e7c4eb5988f85cd5850007afc5178c4525076646d7bbd1c36012d76384b4d54` (blok 72.004.051, 01:17:30 blok zamanı) → 01:19:32 `INCORRECT_AMOUNT_REFUND` **2,30**, `GasFee` **0,37** (servis tahmini: enerji açığı 0 — Nile USDT kontratı enerjiyi sahibinden karşılıyor; hesap açma yakması 1.100.000 SUN × TRX 0,3313) → iade `d1ac39047d69df64e87bcef746340d4b04c60ff8972334004e0695f136d4f9c1` (blok 72.004.108, SUCCESS, 14.650 enerji, ücret 0) → `CONFIRMED` 01:23:12. Alıcı adresi `THBVFCfoTmriK7WsiRxCvTYapztzM7iDfd` 0,004001 → **2,304001** USDT (+2,30). Depozitte 0,370001 USDT + 1,6115 TRX (kullanılmayan yakma takviyesi) kaldı.

Backend log: *"Underpayment refund queued — expected=3.04 received=2.67 net=2.30 gasFee=0.37"*.

Aynı depozite ödemeden 6 sn sonra üçüncü bir adresten (`TEMSSnDm7cPhBFmHetXg7t6iD8KybtHT6g`) **0,000001 USDT** geldi (`80da01947c1f39d68ac479b12f505ecbc9bf82f1e66e6acaed151d75cd1823da`) — Nile'deki toz botu (B5). Ürün bunu ikinci ödeme saydı ve iadeyi `NegativeAmount` ile durdurdu (admin uyarısı); doğru davranış.

**Ayar notu:** bu senaryodan önce `min_refund_threshold_ratio` (TRON) 2,0 → 1,0 yapıldı (Nile'de USDT az, eşiğe takılma ihtimaline karşı) ve iade kararından sonra 2,0'a geri alındı. Ölçülen gas (0,37) ile net 2,30 ≥ 2 × 0,37 = 0,74 olduğundan **karar 2,0'da da aynı olurdu**; ayar sonuca etki etmedi.

---

## Ölçülen gas / ücretler

### Sepolia gas (T159 ↔ T159b)

| İşlem | T159 (sabit gas) | T159b (tahmin) |
|---|---|---|
| Yakıt — boş depozite | 21.000 → **geri döndü** (status 0, 3 kez) | tahmin **207.391**, kullanılan **204.600**, status 1 (**6/6**) |
| Yakıt — var olan hesaba | — | 21.000 / 21.000, status 1 (1/1) |
| USDT ilk alım (taze adrese) | `FEE_ABOVE_CAP` / elle ön token | **152.393–152.417** (limit 168.940–168.966), 6 gönderim |
| USDC ilk alım (taze adrese) | `FEE_ABOVE_CAP` (169.926 tahmin) | **151.779** (limit 169.567) |
| Sweep → bakiyeli sıcak cüzdan | 43.598 | 43.598 (limit 60.074) |
| İade → bakiyeli adres | 54.497 | 54.497 (limit 60.826) |

### Harcamalar (Sepolia, ETH)

| Cüzdan | Önce (00:51) | Sonra (01:53) | Fark | Açıklama |
|---|---|---|---|---|
| Yakıt `0x6436…3239` | 0,009856125 | 0,007657769 | **−0,002198356** | 7 yakıt: değer 0,000949756 + ücret 0,0012486 (6 × 204.600 + 21.000 gas × 1 gwei). Günlük yakıt tavanı 0,005 ETH'nin %44'ü |
| Sıcak `0xeC18…404d` | 0,009945503 | 0,009793086 | −0,000152417 | payout (152.417 gas × 1 gwei) |
| Alıcı `0xC7fB…6209` | 0,005099999990 | 0,005099817198 | −0,000000183 | 6 ödeme × ~152.400 gas × 200.024 wei |
| Altı depozit | 0 | toplam ~0,000090 | + | yakıtın kullanılmayan gas payı (token gas limiti %10 pay + tahmin) — depozitte kalır |

Yakıt ve sıcak cüzdan 0,005 alarm tabanının üzerinde kaldı. `ethereum-*` Grafana kurallarının hiçbiri çalmadı (aşağıda).

### Kesinti — beklenen ↔ zincir

| Senaryo | Taban | `GasFee` | Beklenen net | Satır `Amount` | Zincirde iade adresine |
|---|---|---|---|---|---|
| 2 `BUYER_REFUND` | 10,20 | 0,50 (yedek, B1) | 9,70 | 9,70 | 9,70 USDT |
| 3 `WRONG_TOKEN_REFUND` | 5,00 USDC | 0,93 | 4,07 | 4,07 | 4,07 USDC |
| 4a `INCORRECT_AMOUNT_REFUND` | 5,00 | 0,92 | 4,08 | 4,08 | 4,08 USDT |
| 4b `EXCESS_REFUND` | 5,00 (15,20 − 10,20) | 0,92 | 4,08 | 4,08 | 4,08 USDT |
| 4b `BUYER_REFUND` | 10,20 | 0,50 (yedek, B1) | 9,70 | 9,70 | 9,70 USDT |
| 4c `LATE_PAYMENT_REFUND` | 10,20 | 0,92 | 9,28 | 9,28 | 9,28 USDT |
| 6 TRON `INCORRECT_AMOUNT_REFUND` | 2,67 | 0,37 | 2,30 | 2,30 | 2,30 USDT |

Her satırda `Amount + GasFee = taban`. Ücret tavanı 1,00 $ altında kaldı (en büyük akış 0,93 $); 0,50 $'da bu akışlar Sepolia'nın 1 gwei bahşişiyle `FEE_ABOVE_CAP`'te beklerdi (hazırlık R2 — beklenen, prova için 1,00).

---

## Bulgular

### B1 — 🟡 ⚠ PARA (küçük, düzeltilmedi): iptal iadesinde gas tahmini tek atış; düğümün geçici hatasında sabit yedeğe düşüyor

**Gözlem:** iki admin iptalinin ikisinde de (`4d21e5dd` 01:13:06, `85b886b2` 01:32:50) backend'in `estimate-fee` çağrısı Ethereum servisinden `502 FEE_ESTIMATE_SIMULATION_FAILED` (`eth_estimateGas: block not found: 0xb67b27` / `0xb67b85` — PublicNode'un yük dengeli düğümlerinden biri geride) aldı; servis cevabı `retryable: true` işaretli. `ChargedGasFeeResolver` yeniden denemeden **sabit yedeğe** düştü: *"Gas fee runtime estimate unavailable for ETHEREUM refund … — charging static fallback 0.50 USDT"* (`blockchain.ethereum.refund_gas_fee_estimate_usdt`). Satır `GasFee = 0,50` ile yazıldı ve bu tutar kesildi. Aynı dakikalarda webhook yolundaki beş tahmin başarılıydı; gönderim tarafında aynı düğüm hatası `GAS_ESTIMATE_UNAVAILABLE` ile **beklemeye** alınıp bir sonraki turda doğru tahminle imzalandı (T159a (c) — doğru davranış).

**Etkisi:** kesinti bir kez ve kalıcı yazılıyor. Sepolia'da gerçek maliyet ~0,87 $ iken alıcıdan 0,50 kesildi (fark platformda). Mainnet'te gerçek iade maliyeti ~0,05 $ (T142) iken geçici bir düğüm hatası alıcıdan **0,50** kesilmesine yol açar (10 katı). Para kaybı yok; kesinti yanlış tarafı yükleyebilir.

**Neden düzeltilmedi:** kapsam prova; kod değişikliği yapılmaz. Öneri (sahip / yönetici kararına): tahmini `retryable` hatada kısa yeniden deneme ile almak ya da iade satırını tahmin gelene kadar ertelemek (gönderim tarafı zaten bekliyor). Reddedilen ödeme ailesi de aynı çözücüyü kullandığı için aynı risk orada da var (bu provada gözlenmedi).

### B2 — 🟡 Düzeltilmedi: yığın açılışındaki mutabakat turu servisler hazır olmadan koşuyor

Backend açılışta `ReconciliationJob`'u hemen çalıştırıyor (00:50:34); iki ödeme servisi henüz ayağa kalkmadığı için TRON ve Ethereum turları *"Reconciliation of {Network} aborted: its service returned Unavailable"* ile düştü ve Grafana'da **"Ledger Reconciliation Round Failed On A Network"** (network=TRON, network=ETHEREUM, warning) prova boyunca çaldı — bir sonraki tur günlük cron (`0 3 * * *`) olduğu için kendiliğinden susmaz. Ethereum servisinin kendi saatlik turu 01:52:39'da temiz koştu (`deposits: 5, mismatches: 0, nativeEth: 0`). Para etkisi yok; her yığın açılışında yanlış alarm.

### B3 — ℹ Yanlış token iade satırında `Token = USDT`

`WRONG_TOKEN_REFUND` satırı `Token = USDT`, `ActualTokenAddress = 0x1c7d…7238` (USDC) taşıyor; zincirde doğru token (USDC) gitti. Admin ekranında tutar "4,07 USDT" gibi okunabilir. Görüntü / anlam; kayıt için.

### B4 — ℹ Kesinti ve yakıt payı depozitlerde kalıyor (bilinen — T159a ⚠ kararı 2)

Prova sonunda depozitlerde: 0,50 + 0,92 + 1,42 + 0,92 = **3,76 USDT kopyası**, **0,93 USDC**, ~0,00009 ETH (yakıtın kullanılmayan gas payı); Nile'de 0,37 USDT + iki depozitte 1,6115 TRX (kullanılmayan yakma takviyesi). Otomatik toplama yok (`DEFERRED_BACKLOG.md`, F8 gate'te sahibe sorulacak). Ethereum servisinin saatlik mutabakatı bu artık ETH'yi "depozite yabancı ETH" saymadı (`nativeEth: 0`).

### B5 — ℹ Nile toz botu

Senaryo 6'da depozite ödemeden 6 sn sonra yabancı adresten 0,000001 USDT geldi; ürün ikinci ödeme olarak işledi, iade `NegativeAmount` ile durdu (admin uyarısı). Doğru davranış. Prova adresinin zincirde görünmesiyle (alıcının işlemi) tetiklendiği düşünülüyor; doğrulanmadı.

### Beklenen / bilinen davranışlar

- **Kesinlik gecikmesi:** Sepolia ödemeden `PAYMENT_RECEIVED`'a 19 dk 30 sn; gönderimden onaya 14–18 dk. TRON tespit bloktan ~62 sn sonra (blok zamanıyla ölçüldü; ilk yazılan "7 sn" iddiası yanlıştı), `PAYMENT_RECEIVED` ~2 dk sonra.
- **Geçici düğüm hataları:** `RPC_UNAVAILABLE` (`eth_feeHistory: request beyond head block`) 2 kez, `GAS_ESTIMATE_UNAVAILABLE` (`block not found`) 2 kez — hepsi beklemeye alındı, bir sonraki turda gitti; satırlar `FAILED` olmadı, deneme hakkı harcanmadı. PublicNode adressiz `eth_getLogs`'u reddediyor (T151'de bilinen; spam token taraması o aralıkta yok).
- **Yakıt önce, token sonra:** her gönderimde `FUEL_PENDING` → yakıt bloğa girince imza (7/7).
- **Açık kalan işlem:** `e7305d8d` (TRON eksik ödeme) yığın indiğinde `SELLER_CONFIRMED`; ödeme süresi 02:17'de doluyordu — bir sonraki açılışta zaman aşımına düşer. Depozitinde yalnız kesinti tozu var.

---

## Grafana

Prova sonunda (01:53) 34 kural örneğinden **çalan 3**: `Steam Sidecar Down` (sahte Steam metrik ucu sunmuyor — T159'daki gibi beklenen) ve `Ledger Reconciliation Round Failed On A Network` × 2 (B2). `Ethereum Gas Estimate Abnormal`, `Ethereum Send Waiting Too Long`, `Ethereum Platform Wallet ETH Low`, `Native ETH On An Ethereum Deposit`, `Outbound Transfers Stuck/Piling Up`, `Ethereum Finality Stalled` dahil Ethereum kurallarının hepsi **Normal**.

---

## Değiştirilen ayarlar ve geri alma

| Ayar | Önce | Prova | Sonra (teyit) |
|---|---|---|---|
| `blockchain.ethereum.fee_cap_usd_per_transfer` | 0.50 (2026-01-01) | **1.00** (00:51:24, admin API `PUT /admin/settings`) | **0.50** (01:53:09) — DB `SystemSettings` satırı `0.50`, admin API `GET /admin/settings` `0.50` |
| `max_concurrent_transactions` | 5 | 8 (00:51:56; 8 eşzamanlı işlem için — satıcı başına 5 sınırı) | **5** (01:53:09, DB teyidi) |
| `min_refund_threshold_ratio` (TRON) | 2.0 | 1.0 (01:17:11 – 01:19:47; senaryo 6 — karara etkisi yok, yukarıda) | **2.0** (01:19:47, DB teyidi) |
| `auth.min_steam_account_age_days` | 1 | dokunulmadı | 1 (F9 T166'da geri alınacak) |
| Satıcı fikstürünün Ethereum ödeme adresi (`UserWalletAddresses`) | `0xdc6d…4c19` | taze `0x365b…6596` (`ChangedAt = NULL`) | yeni adreste kaldı (fikstür; iz için) |
| `Transactions.PayoutEligibleAt` (`f7de324d`, `da4f02a3`) | +8 gün | `now` (§G.4 10a) | ikisi de `COMPLETED`; anlamını yitirdi |
| Yığın | — | `--build` ile kuruldu | `docker compose … down` (birimler korundu, `-v` yok); Docker Desktop açık |

Doğrulama sorgusu (01:53): `SELECT [Key],[Value],UpdatedAt FROM SystemSettings WHERE [Key] IN ('blockchain.ethereum.fee_cap_usd_per_transfer','max_concurrent_transactions','min_refund_threshold_ratio','auth.min_steam_account_age_days')` → `0.50` (01:53:09.541) · `5` (01:53:09.738) · `2.0` (01:19:47.965) · `1` (2026-08-28).

---

## Bitiş bakiyeleri

Sepolia: satıcı (taze) **10,0 USDT**; iade adresleri — senaryo 2: 9,70 USDT · 3: 4,07 USDC · 4a: 4,08 USDT · 4b: 13,78 USDT · 4c: 9,28 USDT; altısında da ETH 0 (adreslere hiç ETH gönderilmedi). Sıcak 0,400001 USDT + 0,009793 ETH; yakıt 0,007658 ETH; alıcı 918,6 USDT kopyası + 4,8 USDC + 0,005100 ETH; funder değişmedi (0,0241 ETH).
Nile: alıcı 2,304001 USDT / 11,655 TRX; sıcak 0,006 USDT / 528,67 TRX.

---

## Etkilenen Modüller / Dosyalar

| Dosya | Değişiklik |
|---|---|
| `Docs/TASK_REPORTS/T159b_REPORT.md` | Bu rapor |
| `Docs/IMPLEMENTATION_STATUS.md` | T159b satırı (doğrulama bekliyor), "Son güncelleme" |
| `Docs/STATUS_CHANGELOG.md` | Önceki "Son güncelleme" satırı taşındı |
| `.claude/memory/MEMORY.md` | T159b satırı |

Kod değişikliği yok. Commit'lenmeyen prova araçları (scratchpad): compose katmanı, Sepolia / Nile gönderim yardımcıları (ağ kontrollü: `eth_chainId` = 11155111, Nile genesis kimliği), JWT'li API sürücüsü, SQL ve izleme betikleri, kanıt toplayıcı (makbuz okuma). Sırlar yalnız `C:/projects/Escrow/secrets/` altında; yeni dosya `sepolia-t159b-wallets.json` (taze adreslerin anahtarları).

## Kabul Kriterleri Kontrolü

| Kriter | Durum | Kanıt |
|---|---|---|
| Sepolia'da yeni depozitle sweep (yakıt dahil) elle ETH / ön token olmadan | ✓ | `f7de324d`: yakıt `0x479cb8ab…3dd0` status 1 (204.600 gas) → sweep `0x68483997…c6ae` → payout taze adrese `0x133c2642…2476` → `COMPLETED` |
| Sweep öncesi iade elle ETH / ön token olmadan | ✓ | `4d21e5dd`: yakıt `0x3558a2f1…c862` → `BUYER_REFUND` `0xc4fcd763…fb53` taze adrese |
| Yanlış token iadesi elle ETH / ön token olmadan | ✓ | `c0930069`: yakıt `0x126f44a0…0494` → USDC iadesi `0x2369eab9…9f64` taze adrese (151.779 gas) |
| (görev) Reddedilen ödeme iadesinde gas kesintisi zincirde | ✓ | 4a / 4b / 4c + TRON 6: `Amount = taban − GasFee`, zincirde net tutar (tablo yukarıda) |
| (görev) TRON regresyonu | ✓ | `da4f02a3` `COMPLETED` (T159 ile aynı sayılar) + `e7305d8d` kesintili iade |
| (görev) `fee_cap_usd_per_transfer` 1,00 → 0,50 geri alındı | ✓ | DB + admin API teyidi 01:53:09 |

## Test Sonuçları

- Canlı prova: 8 senaryo / 8 ✓ (yukarıda). Sepolia'da 21 hash'in (7 yakıt, 8 giden token, 5 USDT + 1 USDC alıcı ödemesi) makbuzu yeniden okundu, hepsi status 1. Nile'de 6 hash `getTransactionInfo` → SUCCESS.
- Kod değişikliği olmadığı için birim / entegrasyon testi koşulmadı. CI: aşağıda.

## Altyapı Değişiklikleri

Yok (repo). Yerel ortam: `escrow_skinora-ethereum-tx-journal` biriminde yeni gönderim kayıtları (silmeyin — `af2e8dd4`, `4d21e5dd`, `85b886b2` depozitlerinde iptal sonrası izleme kayıtlı).

## Commit & PR

- Dal: `task/T159b-sepolia-giden-bacak` (`origin/main` `eb66ffe6`'dan)
- Commit: `f5cc3a92` — rapor + durum + hafıza (kod yok); PR numarası + BYPASS_LOG satırı ayrı commit'te
- PR: #360
- CI: PR #360 sayfasındaki son koşu (bu satırı ekleyen commit için)

## Known Limitations / Açık sorular

1. **B1 (⚠ PARA, küçük):** tahmin hatasında kesinti sabit yedekle yazılıyor — yeniden deneme mi, erteleme mi? Sahip / yönetici kararı.
2. **B2:** açılış mutabakatı servis hazır olmadan koşuyor — küçük düzeltme görevi açılabilir.
3. Sıcak cüzdan taze değildi; sweep hedefinin ilk token alım yolu (sıcak cüzdan ilk kez USDT alırken ~152.000 gas) bu provada sınanmadı. Payout tarafında ilk token alımı sınandı (152.417, sıcak cüzdandan).
4. Bahşiş Sepolia'da ~1 gwei (ağın kendi medyanı, T159a madde 6) — kesintiler ve tavan hesabı bu fiyatla; mainnet'te ~0,01 gwei.
5. Gerçek Steam bacağı koşulmadı (kapsam dışı; T159 raporundaki kontrol listesi geçerli).

## Notlar

- **Working tree:** temiz (başlangıç). **Main CI başlangıç kontrolü:** `39522975374` success (T159a), `39495423338` success (T157), `39495423270` success (T157 Docker Publish); `39522975375` sürüyordu (sayılmadı).
- **Bağımlılık:** T159a ✓ (PR #356, main `eb66ffe6`).
- **Dış varsayımlar:** (1) Sepolia bakiyeleri yeterli — 00:51'de ölçüldü (yakıt 0,00986, sıcak 0,00995, alıcı 0,0051 ETH; harcanan 0,0022 / 0,00015 / 0,0000002); (2) Sepolia ücreti — `eth_gasPrice` 200.024 wei, servis bahşişi 1 gwei (ağ medyanı), tavan 1,00 $ akış başı 0,93 $'ı karşıladı (ölçüldü); (3) Ethereum servisi `acceptsPayments: true`, `usdt: ok` (açılış log'u); (4) Nile USDT kaynağı yalnız sıcak cüzdan — 2,68 USDT toplam, iki senaryoya yetti (ölçüldü); (5) PublicNode `finalized` ve sınırlı `eth_getLogs` (T142/T151, bu provada da aynı).
- Prova süresi: yığın kurulumu ~4 dk, Ethereum akışları ~60 dk (iki kesinlik turu), TRON ~14 dk (paralel).
