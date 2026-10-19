# T159 — Sepolia canlı provası + TRON regresyonu

**Faz:** F8 | **Durum:** ⏳ Devam ediyor — yapım bitti, doğrulama bekliyor | **Tarih:** 2026-10-19 (prova 01:50–03:25 UTC)

---

## Özet

| Senaryo | Ağ | Sonuç | İşlem |
|---|---|---|---|
| 1. Mutlu yol (kabul → depozit → ödeme → kesinlik → teslimat (sahte Steam) → mutabakat → sweep (yakıt dahil) → payout) | Sepolia | ✓ `COMPLETED` — **Sepolia'ya özgü elle müdahaleyle** (aşağıda B1) | `dfb7864d` |
| 2. İade (sweep öncesi, K9 iade adresine; admin iptali) | Sepolia | ✓ `CANCELLED_ADMIN`, 10,01 USDT iade adresine — **elle ETH takviyesiyle** (B1) | `9ad42f2a` |
| 3. Geç ödeme (alıcı iptal etti, sonra ödedi → otomatik iade) | Sepolia | ✓ `LATE_PAYMENT_REFUND` 10,20 iade adresine — **elle ETH takviyesiyle** (B1) | `8f4e4c9f` |
| 4. Yanlış token (Circle USDC → otomatik iade) | Sepolia | ✓ `WRONG_TOKEN_REFUND` 10,20 USDC iade adresine; yakıt yolu çalıştı — **iade adresine ve depozite önceden durum açıldı** (B1) | `e41c9294` |
| 5. TRON mutlu yolu (ödeme → sweep → payout) aynı stack'te | Nile | ✓ `COMPLETED`, müdahale yok | `2fe0fd64` |

**Asıl bulgu (B1, büyük, ⚠ PARA — düzeltilmedi, sahip kararı gerekiyor):** Sepolia'nın gas tarifesi bugün mainnet'ten farklı: **boş bir hesaba ETH göndermek 204.600 gas** (mainnet 21.000), **daha önce token almamış bir adrese USDT/USDC göndermek ~152.000–170.000 gas** (mainnet ~64.000). Ethereum servisi iki sabite dayanıyor: yakıt gönderimi tam **21.000 gas** (imzacı kuralı) ve token gönderimi **≤ 150.000 gas** + ücret tavanının referans gas'ı. Sonuç: Sepolia'da **her** yakıt gönderimi (depozitler her zaman yeni hesaptır) zincirde **geri döndü** (status 0, ücret yandı), ilgili sweep / iade `FUEL_REQUIRED` ile bekledi ve tasarım gereği ikinci yakıt gönderilmedi; boş adrese USDC iadesi `FEE_ABOVE_CAP` ile bekledi. Para kaybolmadı, servis fail-closed davrandı. Provayı bitirmek için depozitlere dışarıdan ETH ve iade adresine 0,000001 USDC gönderildi. Mainnet bugün etkilenmiyor (aynı ölçüm 21.000 / 64.007), ama aynı tarife ana ağa gelirse Ethereum'da her sweep ve sweep öncesi iade durur.

**Küçük bulgu (B2, düzeltildi):** teslimat süresi uyarısı oran dolmadan, ödemeden saniyeler sonra gidiyordu.

---

## Yapılan İşler

### Ortam

- **Yığın:** `docker-compose.yml` + `--profile ethereum`, proje adı `escrow` (mevcut veritabanı birimi), worktree'nin kodu (`main` `92154a6d`). Tek fark, commit'lenmeyen bir katman dosyası: Steam sidecar yerine `sidecar-fake`'in Steam yüzeyi (gerçek takas sahibin telefon onayını istediği için). Ödeme bacakları gerçek: TRON servisi Nile'a, Ethereum servisi Sepolia'ya bağlı. Komut: `docker compose -p escrow -f docker-compose.yml -f <katman> --env-file C:/projects/Escrow/.env --env-file C:/projects/Escrow/secrets/sepolia-rehearsal.env --profile ethereum up -d --build --wait`, ardından nginx + Grafana yeniden başlatma (runbook §G.5).
- **Şema:** yerel veritabanı **49 → 59 migration** (`dotnet ef database update`, §G.2 adım 3). T143'ün §G.0b sıfırlaması **yapılmadı**: sıfırlama sahibin kullanıcılarını ve `auth.min_steam_account_age_days = 1` prova ayarını silerdi (o ayara dokunulmayacaktı). Eski yolla açılmış iki TRON depoziti (`PaymentAddresses` indeks 0–1) `STOPPED` durumda kalıyor; yeni işlemler indeks 2'den yeni yolla açıldı (`m/44'/195'/0'/0/2`). §G.0b adım 2 uygulandı: eski indeks 0 depozitindeki **2 USDT + 12 TRX** eski yolla türetilen anahtarla prova alıcısına çekildi (adresin eşleştiği önceden doğrulandı).
- **Kullanıcılar:** e2e harness'inin `seedHappyPath` biçiminde üç fikstür kullanıcı (`T159 Seller` / `T159 Buyer` / `T159 Admin`, Id `15900000-…-000000000001..3`); satıcının TRON ve Ethereum ödeme adresi `ChangedAt = NULL` (24 sa bekleme yok). Oturum `e2e/src/jwt.ts` deseniyle `.env`'deki `JWT_SECRET`'tan üretilen JWT ile. Sahibin gerçek kullanıcılarına dokunulmadı. Fikstür kullanıcılar ve prova işlemleri veritabanında kaldı (iz için).
- **RPC (anahtarsız):** birincil PublicNode Sepolia, yedek Tenderly Sepolia. Alchemy/Chainstack anahtarı yok (§J.1 adım 2).

### Sepolia kurulumu (`funder` = `0x6D807d68dE57fa096D236a5Ba7B0e00C9AD57Eb9`)

Ağ kontrolü: her gönderimden önce `eth_chainId` = 11155111 (Nile tarafında genesis blok kimliği) denetlendi; ana ağa hiçbir işlem gönderilmedi (ana ağa yalnız salt okuma: `eth_getCode`, `eth_estimateGas`).

- **Tether kopyası:** Sourcify'daki doğrulanmış `TetherToken.sol` (mainnet `0xdAC17F958D2ee523a2206206994597C13D831ec7`, `solc 0.4.18`, optimizer kapalı, "match") aynı derleyiciyle derlendi. **Çalışan bayt kodu mainnet'teki ile meta veri karması dışında birebir aynı** (11.075 bayt; zincirdeki kopya = derlenen). Adres **`0x5b7beEc237F345aFF63617F4361b3D99f6e1504D`**, kurulum tx `0x0a7a8c3b…2407c` (blok 11.945.047, 18.006.782 gas). `name` Tether USD, `symbol` USDT, `decimals` 6, `basisPointsRate` 0, `paused` false, `deprecated` false, sahibi funder. Arz 1.000.000 USDT.
- **Circle USDC:** musluk captcha istediği için Sepolia Uniswap V3'te 0,0005 ETH → 26,41 USDC takası (`0x5f2d6b90…48bd8`); 20 USDC alıcıya.
- **Yeni anahtarlar** (`C:/projects/Escrow/secrets/`, gitignored, içerik yazdırılmadı): `ethereum_hd_mnemonic.txt` (24 kelime, yeni), `ethereum_hot_wallet_private_key.txt`, `ethereum_fuel_wallet_private_key.txt`, `sepolia-rehearsal-wallets.json` (soğuk, alıcı, alıcı iade, satıcı ödeme), `nile-rehearsal-wallets.json` (Nile alıcı), `sepolia-rehearsal.env` (Ethereum env'leri, iç anahtar + webhook sırrı rastgele 64 hex). Adresler: sıcak `0xeC18E0708D68f11ac73b4cf4F8280d369ca1404d` · yakıt `0x6436aDD40861191B8B5F5b98078B9069C2d53239` · soğuk `0x61B308886B57098E38977B069974B0451e952D7D` · alıcı `0xC7fB0F183465C1451A684638845010441C936209` · alıcı iade `0x0285Be44D792dE8EcbBaf571b9017Fd66C2F0454` · satıcı ödeme `0xdC6D84691cb11Aa137bd9B1965dB0f07813a4c19` · Nile alıcı `THBVFCfoTmriK7WsiRxCvTYapztzM7iDfd`. Depozit indeks 0 = `0x362E8DF501Af917703095f250b6D4cFFd921Ed8f` — bağımsız türetme ile servisin açılış log'u ve ürünün açtığı ilk depozit aynı (tohum → secret → konteyner yolu uçtan uca).
- **Fonlama:** alıcı 0,005 ETH + 1.000 USDT kopyası; sıcak 0,01 ETH; yakıt 0,01 ETH (tx'ler `0x4a883e41…`, `0xcd7efc36…`, `0x242487f5…`, `0xd9d2a1bb…`).

### Gas ölçümü ve bütçe

Sepolia taban ücreti prova boyunca **15–18 wei**, öncelik ücreti 1 wei; bütçe sorun olmadı. Servis kendi gönderimlerinde ~1 gwei üst ücret kullandı (taban 16 wei + bahşiş 999.999.984 wei — bahşiş tabanı), yani servisin işlemi başına ~43.000–60.000 gas × 1 gwei ≈ 0,00004–0,00006 ETH.

| | ETH |
|---|---|
| Başlangıç (funder) | 0,05 |
| Uniswap'e (USDC'ye çevrildi, harcama değil) | 0,0005 |
| Toplam gas (bizim + servisin, üç geri dönen yakıt dahil) | ≈ 0,00032 |
| Kontrolümüzdeki cüzdanlarda kalan | **≈ 0,04918** (funder 0,02410 · alıcı 0,00510 · sıcak 0,00995 · yakıt 0,00986 · dört depozitte artık 0,00018) |

Sahipten ek ETH **gerekmiyor**.

---

## Senaryolar — ölçümler ve hash'ler

### 5. TRON regresyonu (Nile) — `2fe0fd64`, fiyat 1,00 USDT

| Adım | Zaman (UTC) | Kanıt |
|---|---|---|
| Kabul: toplam **2,02** = 1,00 + 0,02 komisyon + **1,00 ağ ücreti satırı** (K8, Nile tahmini) | 02:07:12 | API |
| Hazırlık onayı, depozit `TC2uc9crSgXogvmRj3ccLDAaaihCjCFcWB` (indeks 2, `m/44'/195'/0'/0/2`), izleyici kuruldu | 02:08:19 / 02:08:21 | sidecar `Monitor started` |
| Ödeme 2,02 USDT | 02:09:33 | `dc7f5150c5ed3bda9eeded00ab185e273880e17380f310b7721adbecd5c7fa90`, blok 71.947.664 |
| `payment-detected` | 02:09:37 (**4 sn**) | webhook |
| 20 onay → `PAYMENT_RECEIVED` | 02:10:43 (**70 sn**) | |
| Sahte takas + alıcı onayı → `ITEM_DELIVERED`; prova kısayolu `PayoutEligibleAt = now` (runbook §G.4 10a; değer 2026-10-27 02:12'den) | 02:12:21 | |
| Mutabakat doğrulandı | 02:15:06 | `SettlementVerifiedAt` |
| Sweep 2,02 → sıcak cüzdan | 02:16:07 → 02:20:08 | `ab6eec1028f092c2fafa5929a25bd1b6ece7d6360a66ba8430233aafa2b6a1c9`, blok 71.947.836 |
| Payout **0,882** (1,00 − 0,12 gas; koruma eşiği kuralı, 02 §5) | 02:21:09 → 02:24:09 | `6d47318bd308b59b36f688d3cc1339d9d01115160dad35fbdb56a0f09ce4a721`, blok 71.947.933, gerçek ücret 345.000 SUN |
| `COMPLETED` | 02:24:3x | depozit `STOPPED` |

Sıcak cüzdan TRX 538,64 → 535,32 (sweep için depozite yakma takviyesi + hesap açma + payout bant genişliği). Not: prova alıcısının 2,00 USDT'si 2,02'ye yetmedi; eksik 0,10 USDT sıcak cüzdandan elle gönderildi (`5e475c4d…2861`) — Nile'da başka USDT kaynağımız yoktu.

### 1. Ethereum mutlu yolu — `dfb7864d`, fiyat 10,00 USDT

| Adım | Zaman (UTC) | Kanıt |
|---|---|---|
| Kabul: toplam **10,20** (ağ ücreti satırı 0,00 — Ethereum'da yok, K8) | 02:10:1x | API |
| Depozit `0x362e8df5…ed8f` (indeks 0), `MonitorFromBlock` 11.945.058 | 02:10:19 | DB |
| Depozitin ilk fonlaması (doğrulamada eksik bulundu) | — | `0xc30408a8…` (tam hash elde yok) |
| Ödeme 10,20 USDT kopyası | 02:10:48 | `0x46d53c3b3775a08f1c67d7ba60a017a878c8382c946f94e5d1c5358fd127ca7d`, blok 11.945.126 |
| Kesinlik → bildirim → `PAYMENT_RECEIVED` (tespit ve onay aynı anda, izleyici yalnız `finalized` okur) | 02:29:30 (**18 dk 42 sn**) | |
| Sahte takas + alıcı onayı → `ITEM_DELIVERED`; prova kısayolu `PayoutEligibleAt = now` (değer 2026-10-27 02:29'dan) | 02:29:53 / 02:30:02 | |
| Mutabakat doğrulandı | 02:30:10 | |
| Sweep kuyruğu (yığın düşüp kalktıktan sonra, aşağıda "Beklenmeyen") | 02:37:04 | |
| **Yakıt geri döndü** (B1): `0xc4f0b7229c19cb47c47ef7d786d9a14447e332426ce53a95624df519f66b9340` (21.000 gas, status 0) → `FUEL_REQUIRED` | 02:38:06 | servis: *"Fuel transaction reverted — the deposit has no gas; the send waits (investigate)"* |
| Elle 0,0001 ETH depozite (`0x86b2fd45…ff03`) → servis depozitin kendi ETH'siyle imzaladı | 02:41:37 / 02:43 | |
| Sweep 10,20 → sıcak | 02:43 → 03:01:13 (kesinlik **~18 dk**) | `0xc116a374c43773527237c089b7745ae4c5fd8b10215c75fdbe212811d89b18e0`, blok 11.945.287, 43.598 gas × 1 gwei |
| Payout **10,00** (satıcı fiyatın tamamını alır, 02 §5 Ethereum; satırda tahmini gas 0,14) | 03:02:14 → 03:21:11 | `0xeaa0114375a002c288d874e0e5ca95594cb6a16dbfae490cec31aabc161c299a`, blok 11.945.388, 54.497 gas × 1 gwei |
| `COMPLETED` | 03:21:11 | depozit `STOPPED`; sıcak cüzdanda 0,20 USDT komisyon kaldı |

### 2. Ethereum sweep öncesi iade — `9ad42f2a`

`PAYMENT_RECEIVED` (02:29:30, ödeme `0xa32e2553ac74feb29de7ad5e65b11b87f06d4d2431dfc88278da3608f525e680`, blok 11.945.127) → admin iptali 02:29:54 → `BUYER_REFUND` **10,01** (10,20 − 0,19 tahmini gas; 02 §4.6) → yakıt `0x3d4a82b0e7375aad57d6aeb9aec22ecc4ea77b4d8dcd0518923d7a5bd04cfa8a` **geri döndü** (B1) → elle 0,0001 ETH (`0x65825da3…58a6`) → iade `0x21ed310e73a363174da3b663a234d883e560aa4b5609d915fae054d76a9f868c` (blok 11.945.278, 54.497 gas) → `CONFIRMED` 02:55:10. Para **K9 iade adresine** gitti, ödeme kaynağına değil. Depozitte 0,19 USDT (kesilen gas) kaldı.

### 3. Ethereum geç ödeme — `8f4e4c9f`

Hazırlık onayından sonra alıcı iptal etti (02:10:36, `CANCELLED_BUYER`, iptal sonrası izleme `POST_CANCEL_24H` başladı) → ödeme 10,20 (`0x94e9f737098c3796b79fd3f8900da35eeaef41df3e6f28031156df46fe7938d2`, blok 11.945.130, 02:11:38) → 02:29:31'de bildirim + `LATE_PAYMENT_REFUND` 10,20 → yakıt `0x471d685a3dd712905297fd1723510a1e761aee2a9161b664016cbc99dce8a2d6` **geri döndü** (B1) → elle 0,0001 ETH (`0xf8e235b0…23e0`) → iade `0xa5097c5e4c484fdb5aeb8dcd8a8d6b07a85aec436dd732764de03f5c2460bb68` (blok 11.945.277) → `CONFIRMED` 02:55:10. İade adresine tam 10,20 gitti.

### 4. Ethereum yanlış token (Circle USDC) — `e41c9294`

Alıcı USDT yerine 10,20 Circle USDC gönderdi (`0xd1ba251990789dd5baa3ff252331d4692820dd19b40a9198eef738f7fd49dafb`, 02:11:24) → 02:29:30 `WRONG_TOKEN_INCOMING` (`ActualTokenAddress` = Circle USDC) + `WRONG_TOKEN_REFUND` 10,20 → **`FEE_ABOVE_CAP`**: iade adresi daha önce USDC almadığı için tahmin 169.926 gas (B1). İade adresine 0,000001 USDC (`0xc9015bc9…c3bc1`) ve depozite 1 wei ETH (`0x539b1410…5cc4`) gönderilerek ikisinin de zincirde durumu açıldı → **yakıt yolu bu kez çalıştı:** yakıt `0xc16a11a410b43c59c0bb0e5a4b2d15973b5d0978d3b8846838aca65fb1ef93f5` (59.875.000.957.999 wei ≈ 0,0000599 ETH, tam eksik kadar) → USDC iadesi `0x3b585a6f9a6f7d8d862bc4849d773836846214437e87bbb8d1b8f47b561ca826` (blok 11.945.277, 43.088 gas) → `CONFIRMED` 02:55:10. İşlem yanlış token yüzünden iptal edilmedi (alıcı doğru token'ı hâlâ gönderebilirdi); ödeme süresi dolunca 03:10'da `CANCELLED_TIMEOUT`.

### Bitiş bakiyeleri (Sepolia)

Alıcı iade adresi: **20,210001 USDT kopyası** (10,01 + 10,20 + ön açma 0,000001) + **10,200001 USDC**. Satıcı: **10,000001 USDT**. Sıcak: 0,200001 USDT (komisyon) + 0,009946 ETH. Yakıt: 0,009856 ETH. Ethereum servisinin Grafana kurallarının hiçbiri çalmadı; tek çalan `Steam Sidecar Down` (sahte Steam metrik ucu sunmuyor — beklenen).

---

## Bulgular

### B1 — 🔴 ⚠ PARA (büyük, düzeltilmedi): sabit gas varsayımları Sepolia'nın bugünkü tarifesinde Ethereum gönderimlerini durduruyor

**Ölçüm (2026-10-19 02:00–02:40 UTC, PublicNode, `reth/v2.7.0`):**

| İşlem | Sepolia | Mainnet |
|---|---|---|
| Boş hesaba ETH (`eth_estimateGas`, gerçek makbuz) | 207.391 tahmin / **204.600** kullanılan | **21.000** (`0x5208`) |
| Var olan hesaba ETH | 21.000 | 21.000 |
| USDT'yi daha önce token almamış adrese | ~153.000 (makbuzlar 152.393–152.417) | **64.007** |
| USDT'yi bakiyeli adrese | 55.272 | ~41.000–46.000 (T142) |
| Circle USDC'yi boş adrese | 169.926 (servis tahmini) | — |

Sepolia blokları `blockAccessListHash` alanı taşıyor — ağ, durum oluşturma maliyetini yükselten bir sonraki hard fork'un kurallarıyla çalışıyor (fork'un adı ve mainnet tarihi bu provada doğrulanmadı).

**Koddaki varsayımlar:** `sidecar-ethereum/src/transfer/FeePolicy.ts` `FUEL_SEND_GAS = 21_000n`; `SignerPolicy.ts` yakıt için `gasLimit` tam 21.000 kuralı ve token gönderimi için 150.000 üst sınırı; ücret tavanının referans gas'ları (sweep 46.201 / 67.201, iade 64.031 / 85.031 — 08 §3.7.5, §3.7.7).

**Sepolia'daki davranış (ölçüldü):** yakıt imzalandı, yayımlandı, bloğa girdi ve **geri döndü** (üç kez: `0x471d685a…`, `0x3d4a82b0…`, `0xc4f0b722…`; her biri 21.000 gas × 1 gwei yaktı, yakıtın değeri depozite ulaşmadı). Servis bunu görüp `FUEL_REQUIRED` ile bekletti (*"Fuel transaction reverted — … (investigate)"*), backend satırı `PENDING` kaldı, **ikinci yakıt gönderilmedi** (T153 kararı). Yani **para güvende, gönderim süresiz durur**; 3 sa sonra `outbound-transfer-stuck` çalardı. Boş adrese USDC iadesi `FEE_ABOVE_CAP` ile bekledi.

**Etkisi:** bugün mainnet etkilenmiyor. Aynı tarife mainnet'e gelirse: her Ethereum sweep'i ve sweep öncesi iadesi (depozitler her zaman yeni hesap) yakıtı yakıp durur; daha önce token almamış satıcıya payout ve iade adresine iade 150.000 sınırına ve tavana takılır. Sepolia'da bundan sonraki her prova da aynı engele takılır.

**Neden düzeltilmedi:** imzacı kuralı (yakıtta tam 21.000) ve tavan referansları ⚠ PARA / güvenlik kararlarıdır (T152/T153 kararları). Seçenekler sahibin kararına:
1. Yakıtın `gasLimit`'ini `eth_estimateGas` ile almak, imzacı kuralını "≤ yapılandırılmış üst sınır" yapmak (sınır yine sabit kalır, yalnız ağa göre ayarlanır).
2. Token gönderiminin 150.000 sınırını ve tavan referans gas'larını ağ başına ayara taşımak.
3. Hiçbiri: Sepolia provası her seferinde aşağıdaki elle adımla yapılır, mainnet takvimi izlenir ve fork mainnet'e gelmeden önce karar verilir (F9 öncesi).

**Provada uygulanan geçici çare (yalnız Sepolia):** (a) sıcak cüzdan, satıcı ödeme ve alıcı iade adreslerine önceden 0,000001 USDT kopyası (ve iade adresine 0,000001 USDC) gönderildi — token durumu açık olsun; (b) yakıtı geri dönen üç depozite elle 0,0001 ETH gönderildi (servis bunu depozitin kendi ETH'si sayıp imzaladı); (c) yanlış token depozitine yakıttan önce 1 wei gönderildi (hesap var oldu, yakıt 21.000 ile geçti). (b) ve (c) runbook'un "depozite yakıt olmayan ETH" durumudur; `ethereum-deposit-native-eth` alarmı saatlik mutabakat koşmadan yığın kapandığı için gözlenmedi.

### B2 — 🟡 Düzeltildi: teslimat süresi uyarısı oran dolmadan gidiyordu

`DeadlineScannerJob.DispatchDeliveryWarningsAsync` `timeout_warning_ratio`'yu okuyup **sorguda kullanmıyordu**: `PAYMENT_RECEIVED`'a giren her işlem bir sonraki taramada (≤ 30 sn) uyarı alıyordu. Provada TRON işleminde ölçüldü: ödemeden 10 sn sonra *"Timeout warning published … RemainingMinutes=59"* (60 dk'lık pencerede oran 0,75 iken 45. dakikada gitmeliydi). Düzeltme: uyarı pencerenin (`PaymentReceivedAt` → `DeliveryDeadline`) oranı dolunca gider; `PaymentReceivedAt` yoksa eski davranış sürer (uyarı hemen gider; hiç gitmemesinden iyidir). Test: `Delivery_Warning_Waits_For_The_Ratio_Of_The_Delivery_Window` (oran 0,5; 10 / 29 dk uyarı yok, 31 / 45 dk uyarı var). Bozma 2/2 yakalandı (oran denetimini atlamak → 2 kırmızı; pencere oranını değiştirmek → 2 kırmızı). Çalışan imaj bu düzeltmeyi içermiyordu (prova `main` koduyla koştu).

Aynı satırda bir yan gözlem: uyarı aynı işlem için iki eşzamanlı taramada dağıtıldı ve biri `DbUpdateConcurrencyException` ile düştü (02:10:53, satır sürümü korudu — çift bildirim yok). Zararsız; ayrı kayıt açılmadı.

### Beklenmeyen davranışlar / gözlemler

- **Docker motoru prova ortasında kapandı (~02:31–02:36 UTC)** — bu oturum kapatmadı; aynı makinede paralel çalışan başka bir oturumun iş sonu `docker desktop stop`'u olması muhtemel (doğrulanamadı). `docker desktop start` ile geri açıldı; yığın `unless-stopped` ile kalktı. Plansız ama yararlı bir yeniden başlatma provası oldu: Ethereum servisinin gönderim günlüğü volume'de kaldı, geri dönen yakıtları açılışta yeniden okudu, izleyiciler (aktif + iptal sonrası) `MonitorFromBlock`'tan yeniden kuruldu, ödeme kaybı olmadı. Sahte Steam'in envanteri bellekteydi ve kayboldu → elle yeniden yüklendi (mutlu yolun mutabakatı kapanmadan önce bitmişti).
- **Kesinlik gecikmesi:** Sepolia `finalized` başın 13–19 dk gerisinde; ödemeden bildirime 18 dk 42 sn, gönderimden onaya 14–19 dk. TRON: 4 sn tespit, 70 sn onay. Tasarımla uyumlu (Ethereum izleyicisi yalnız `finalized` okur).
- **USDT durumu açılışta `unknown`:** kopya, `finalized` bloğun ilerisinde kurulduğu için servis ~5 dk `USDT_UNKNOWN` dedi ve Ethereum `supportedNetworks`'te görünmedi; kesinlik kurulum bloğunu geçince `ok`. Beklenen (okuma `finalized` blokta).
- **Yük dengeli düğüm cevapları:** `eth_estimateGas: block not found` ve `eth_feeHistory: request beyond head` birer kez — servis geçici sayıp yeniden denedi. PublicNode adressiz `eth_getLogs`'u reddediyor → spam token taraması o aralıkta yok (T151'de bilinen).
- **Reddedilen ödeme iadelerinde gas kesilmedi:** geç ödeme ve yanlış token iadeleri **tam 10,20** gitti, gas platformdan (yakıt) çıktı; sweep öncesi `BUYER_REFUND`'dan 0,19 kesildi. 02 §4.4 bu iadeler için gas kesintisini açıkça yazmıyor — açık soru (aşağıda).
- **Bahşiş tabanı:** servis Sepolia'da taban 16 wei iken ~1 gwei bahşişle imzaladı; ücretler ve iadeden kesilen 0,19 bu bahşişten geliyor. Tasarım sınırı mı yoksa Sepolia'ya özgü mü bu provada ayrıştırılmadı.

---

## Değiştirilen ayarlar ve geri alma

| Ne | Değer | Geri alındı mı |
|---|---|---|
| SystemSetting | **hiçbiri değiştirilmedi** | — |
| `auth.min_steam_account_age_days` | **hâlâ 1** (önceden vardı, dokunulmadı; üretim 30) | — |
| `Transactions.PayoutEligibleAt` (yalnız `2fe0fd64` ve `dfb7864d`, runbook §G.4 10a prova kısayolu) | `now` | İkisi de `COMPLETED`; değer anlamını yitirdi |
| Ethereum servisinin env'i (yalnız `secrets/sepolia-rehearsal.env`) | `ETHEREUM_FUEL_WALLET_MIN_ETH` / `ETHEREUM_HOT_WALLET_MIN_ETH` = 0,005 (backend satırları 0,06 kaldı — prova süresince parite dışı), tavanlar 100 / 500 USDT, yakıt 0,001 / 0,005 ETH | Dosya ayrı; normal `.env`'e ve normal yığına etkisi yok |
| Yerel şema | 49 → 59 migration | Geri alınmaz (ileri yönlü) |
| Yığın | Prova sonunda `docker compose … down` (birimler korundu, `-v` yok) | Sonraki `docker compose -f docker-compose.yml up` gerçek Steam sidecar'ını kurar |

---

## Sahibe: gerçek Steam'li tam akış için kontrol listesi

Ön koşullar (prova gününden önce):

1. **İki hesap da takas edebilir olmalı** (runbook §G.5): limited değil (5 USD harcama, 5 ardışık okuma), 15 günlük bekleme dolmuş, MA 7 günden eski. Bugün `turkerurganci` ve `turkerurganci_2` bu koşulları sağlıyor olmalı; prova günü `GetTradeHoldDurations` + XML profil ile yeniden okuyun.
2. **Satıcının Ethereum ödeme adresi en az 24 sa önce** girilmeli (`PUT /users/me/wallet/...`, Ethereum) — B1 yüzünden **o adres daha önce Sepolia USDT kopyası almış olmalı** (biz 0,000001 gönderebiliriz). Alıcı profilde Ethereum iade adresi **girmesin** (kabul kapısı 24 sa bloke olur, §G.5); adres kabul ekranında girilir ve o da önceden 0,000001 USDT kopyası almış olmalı.
3. **Sepolia alıcı cüzdanı:** MetaMask'a Sepolia + USDT kopyası (`0x5b7beEc237F345aFF63617F4361b3D99f6e1504D`, 6 ondalık) eklenir; bu provanın alıcı cüzdanında 969,4 USDT kopyası + 0,0051 ETH var (anahtarı `secrets/sepolia-rehearsal-wallets.json`'da), ya da sahibin cüzdanına funder'dan gönderilir.
4. **B1 kararı verilmediyse:** yakıt depozitte geri döner. Provada alıcı ödemeyi yaptıktan sonra depozit adresine dışarıdan ~0,0001 ETH gönderilir (sweep ve iade için), aksi hâlde sweep/iade `FUEL_REQUIRED`'da bekler.

Akış:

5. Yığını gerçek Steam sidecar'ıyla kaldırın: `docker compose -f docker-compose.yml --env-file .env --env-file secrets/sepolia-rehearsal.env --profile ethereum up -d --build --wait` → `restart skinora-reverse-proxy skinora-grafana`. Ethereum `/health` → `acceptsPayments: true`; `/transactions/params` → `supportedNetworks` iki ağ.
6. Satıcı (`turkerurganci`) tarayıcıda Steam ile girer, ilan açar: ağ **Ethereum**, alıcı yöntemi STEAM_ID (`open_link_enabled = false`), ödeme süresi 1 sa.
7. Alıcı (`turkerurganci_2`) girer, kabul eder: ağ Ethereum, iade adresi (2. maddedeki), trade URL.
8. Satıcı hazırlık onayı → depozit adresi alıcıya açılır. Backend log'unda `Payment monitor armed`, Ethereum servisinde `Ethereum monitor started`.
9. Alıcı MetaMask'tan **tam tutarı** depozite gönderir. **~15–20 dk** bekleyin (kesinlik) → `PAYMENT_RECEIVED`.
10. Satıcı item'ı **Steam'den doğrudan alıcıya** gönderir (telefon onayı burada); alıcı takası kabul eder ve platformda "teslim aldım" der → `ITEM_DELIVERED`.
11. Mutabakat 8 gün; aynı gün görmek için yalnız provada `UPDATE Transactions SET PayoutEligibleAt = SYSUTCDATETIME() WHERE Id = '<id>'` → ≤ 5 dk doğrulama (alıcının envanteri **gerçekten** okunur, item alıcıda olmalı) → sweep (yakıt + 4. madde) → ~20 dk → payout → ~20 dk → `COMPLETED`.
12. Kontrol: satıcı adresine fiyatın tamamı, sıcak cüzdanda komisyon, depozit `STOPPED`; Grafana'da Ethereum kurallarının hiçbiri çalmıyor.
13. Bitince: item artık 7 gün takas kilitli (ardışık prova için ikinci item); `auth.min_steam_account_age_days`'i 30'a geri alıp DB'den teyit edin.

---

## Etkilenen Modüller / Dosyalar

| Dosya | Değişiklik |
|---|---|
| `backend/src/Modules/Skinora.Transactions/Application/Timeouts/DeadlineScannerJob.cs` | B2: teslimat uyarısı oranla kapılandı |
| `backend/tests/Skinora.Transactions.Tests/Integration/Timeouts/DeadlineScannerJobTests.cs` | B2 testi (4 nokta) |
| `Docs/TASK_REPORTS/T159_REPORT.md` | Bu rapor |
| `Docs/IMPLEMENTATION_STATUS.md` | T159 ⏳ |
| `.claude/memory/MEMORY.md` | T159 satırı |

Commit'lenmeyen prova araçları (scratchpad): derleme/doğrulama, Sepolia ve Nile gönderim yardımcıları (ağ kontrollü), compose katmanı, SQL ve izleme betikleri. Sırlar yalnız `C:/projects/Escrow/secrets/` altında.

## Kabul Kriterleri Kontrolü

| Kriter | Durum | Kanıt |
|---|---|---|
| Sepolia'da mutlu yol uçtan uca | ✓ (B1 elle müdahalesiyle) | `dfb7864d` `COMPLETED`; ödeme → sweep → payout hash'leri yukarıda |
| Sepolia'da iade uçtan uca | ✓ (B1 elle müdahalesiyle) | `9ad42f2a` `BUYER_REFUND` `0x21ed310e…` |
| Sepolia'da geç ödeme uçtan uca | ✓ (B1 elle müdahalesiyle) | `8f4e4c9f` `LATE_PAYMENT_REFUND` `0xa5097c5e…` |
| Aynı stack'te Nile'da TRON akışı | ✓ müdahalesiz | `2fe0fd64` `COMPLETED` |
| (ek) Yanlış token iadesi | ✓ | `e41c9294` `WRONG_TOKEN_REFUND` `0x3b585a6f…` |

Kriterlerin "uçtan uca" kısmı **ürünün kendi başına** Sepolia'da tamamlayamadığı bir yolu içeriyor (B1): ödeme ve izleme bacağı müdahalesiz, giden gönderimler yalnız Sepolia'ya özgü elle adımla tamamlandı. Doğrulama bunu kriterin karşılanıp karşılanmadığı açısından ayrıca değerlendirmeli.

## Test Sonuçları

- `dotnet test tests/Skinora.Transactions.Tests --filter FullyQualifiedName~DeadlineScannerJobTests` → **25/25** (yeni 4 dahil; yerel SQL Server testcontainer).
- Bozma (B2): 2 değişiklik / 2 yakalandı.
- CI: aşağıda.

## Altyapı Değişiklikleri

Yok (repo). Yerel ortam: şema 59 migration'a çıktı; `escrow_skinora-ethereum-tx-journal` birimi oluştu (prova gönderim günlüğü — **silmeyin**, üç depozitte iptal sonrası izleme kayıtlı).

## Commit & PR

- Dal: `task/T159-sepolia-provasi` (`origin/main` `92154a6d`'den)
- Kod commit'i: `7feb25d2` (B2)
- PR: #355
- CI: `39194736989` ✓ success (HEAD `9300db1a`; bu satırı ekleyen commit için yeni koşu PR sayfasında)

## Known Limitations / Açık sorular

1. **B1 kararı (⚠ PARA):** yakıt `gasLimit`'i ve token gas sınırı/tavan referansı ağ başına mı olsun, yoksa elle Sepolia adımıyla mı devam edilsin? Mainnet'e aynı tarifenin ne zaman geleceği bu provada doğrulanmadı — F9 öncesi bakılmalı.
2. **Reddedilen ödeme iadelerinde (geç ödeme, yanlış token) gas kesilmeli mi?** Bugün tam tutar gidiyor, gas platformda.
3. **Bahşiş tabanı ~1 gwei** Sepolia'da tabanın 60 milyon katı; mainnet'te bugünkü bahşişlerle karşılaştırması T142 verisiyle yapılmalı (alıcı iadesinden kesilen tutarı belirliyor).
4. Anahtarlı RPC (Alchemy/Chainstack) yok — prova anahtarsız PublicNode + Tenderly ile koştu (§J.1 adım 2, T169).
5. Gerçek Steam bacağı koşulmadı (kapsam); kontrol listesi yukarıda.

## Notlar

- **Working tree:** temiz (başlangıç). **Main CI başlangıç kontrolü:** `39186712523` success (T158), `39164777570` success, `39164777560` success; `39186712526` kuyrukta (sayılmadı).
- **Bağımlılık:** T155, T156, T158 ✓; T157 ⬚ (doğrulamada, PR #354) — T159 admin ekranlarını kullanmadı, yönetici görevi verdi.
- **Dış varsayımlar:** (1) Sepolia ETH bütçesi — `eth_gasPrice` 15 wei, ölçüldü, yeterli; (2) Tether kaynağı — Sourcify v2 "match", derlenen = mainnet (meta veri hariç); (3) Circle USDC — Uniswap V3 Sepolia'da likidite var (0,001 ETH → 52,8 USDC teklifi); (4) anahtarsız RPC — PublicNode `finalized` ve sınırlı `eth_getLogs` destekliyor (T142/T151); (5) **Sepolia gas tarifesi mainnet ile aynı — KIRIK** (B1).
- Kullanılan kimlikler: sahte Steam yüzeyi `sidecar-fake` (T137/T155) — `/__e2e/steam/inventory`, `/__e2e/steam/trade`.

## Doğrulama sonucu

**KOŞULLU PASS.** Bağımsız, temiz bağlamlı doğrulayıcı (Sonnet). HEAD `1f1d38ae`, CI `39196090612` ✓.

- 5 senaryonun tüm hash'leri zincirden bağımsız okundu ve raporla uyuştu (Sepolia chainId 11155111, Nile SUCCESS).
- Tether kopyası mainnet USDT ile aynı uzunlukta (11.075 bayt); fark yalnız sondaki swarm meta veri karmasında.
- `eth_estimateGas` bağımsız ölçümü: boş adrese 207.391, var olan hesaba 21.000 (rapordaki tarife bulgusu doğrulandı).
- Elle müdahaleler (depozite dışarıdan ETH, adreslere ön token, `PayoutEligibleAt` kısayolu) kabul edilebilir prova kısayolu: ürün mantığını atlamıyor, B1 tarife bulgusunun engelini aşıyor.
- Teslimat uyarısı düzeltmesi `7feb25d2` testli (25/25, bozma 6/5, kaçan `>=`→`>` eşdeğer).
- Sır taraması temiz.

**KOŞUL:** Sepolia giden bacağı (yakıt, sweep, iade) T159a (gas tarifesinden bağımsız tahmin) birleştikten sonra elle ETH/ön token OLMADAN yeniden koşulacak (T159b).

Notlar:
- TRON payout alıcı ile aynı Nile adresine gitti; iki ayrı taraf kanıtlanmadı.
- Senaryo 1'de depozitin ilk fonlaması `0xc30408a8…` raporda eksikti (yukarıya eklendi; tam hash elde yok).
- Kısaltılmış bazı hash'lerin tamamı elde olmadığından raporda kısa kaldı.
