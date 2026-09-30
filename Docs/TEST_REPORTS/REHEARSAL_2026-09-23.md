# Canlı P2P Prova Raporu — Teslimat, Mutabakat ve Payout Bacakları (2026-09-23)

**Kapsam:** happy path **uçtan uca** koşuldu · 2026-09-02 provasının koşamadığı üç bacak (teslimat · mutabakat · satıcı ödemesi) ilk kez ölçüldü
**Sonuç:** işlem **`COMPLETED`** · muhasebe zincirde birebir kapanıyor · **3 bulgu** (1 🟡 · 2 ⚪) + 1 altyapı gözlemi
**Ortam:** lokal stack (`docker-compose.yml`, override YOK), nginx `:8080`, imajlar `main` `f0363a4`'ten 2026-09-22/23'te kuruldu, dal `prova/canli-2026-09-20`
**Hesaplar:** satıcı `76561199053273410` / `turkerurganci` (aynı zamanda Super Admin) · alıcı `76561198652999063` / `turkerurganci_2`
**Zincir:** Tron **Nile testnet**, USDT `TXYZopYRdj2D9XRtbG411XZZ3kM5VkAeBf`
**İşlem:** `918b79d3-7bc0-4e8f-8773-6b067cf8c4fd` · **Steam takası:** `9382179489`

---

## 1. Neden bu prova

2026-09-02 provası ödeme bacağını geçmiş, teslimatta **alıcı hesabının Steam kısıtına** takılmıştı (limited account + 15 günlük bekleme). O günden bu yana kısıt kalktı (15 gün 09-08'de doldu, hesap artık limited değil) ve platform kısıtı okuyan kod da merge edildi (#319). Geriye üç ölçülmemiş bacak kalmıştı: **teslimat · mutabakat · satıcı ödemesi**.

Ayrıca aradan geçen sürede para yollarına dokunan üç PR merge edildi — #323 (hibrit enerji), #324 (adres sabitleme + gönderim tavanları), #325 (ayrı kilit hesabı). Bu prova onların **canlıdaki ilk** koşusudur.

---

## 2. Hazırlık ve ortam

| # | Adım | Ölçüm |
|---|---|---|
| H1 | Docker motoru çalışmıyordu | 2026-09-17 21:14'te çökmüş; C: diskinde **2,5 GB** boş kalmıştı. Temp'te 7 günden eski 254 öğe (**18,6 GB**, yönetici yetkisiyle) + Docker derleme önbelleği ve kullanılmayan imajlar temizlendi |
| H2 | `.env` **12 anahtar eksikti** | #319 (limited-probe ×5), #324 (`COLD_WALLET_ADDRESS`, `MAX_SINGLE_TRANSFER_USDT`, `MAX_DAILY_OUTFLOW_USDT`), #325 (`STAKE_ACCOUNT_ADDRESS`, `STAKE_ACCOUNT_PERMISSION_ID`), `REVERSE_PROXY_*`. **Tavanlar fail-closed:** boş bırakılsaydı sidecar hiçbir transferi imzalamazdı. `docker compose config --quiet` uyarısız |
| H3 | Şema 41/49'du | `dotnet ef database update` → **49/49**, `SystemSettings` **64** satır |
| H4 | İmajlar yeniden kuruldu | frontend imajı `f0363a4` içeriğiyle; §G.5'in iki restart tuzağı (reverse proxy + Grafana) uygulandı |
| H5 | İmaj tazeliği **davranışla** ölçüldü | `/transactions/params` → `minHours: 1` (0 olsaydı bayat imaj) |
| H6 | Kilit hesabı kablosu | sidecar açılış satırı: `delegationOwner=TVqvXQ…EyQ6, permissionId=2, refundEstimateReadsStakeFrom=TVqvXQ…EyQ6` — #325 üretim ortamında **ilk kez** doğrulandı |
| H7 | Steam kapıları | iki hesapta da `isLimitedAccount=0` (5 ardışık okuma) · `GetPlayerBans` → economy/VAC/community **none** · alıcı bekletmesi `their_escrow=0` (token ile) |
| H8 | Zaman kapıları | satıcı ödeme adresi 2026-08-29'dan kayıtlı (24 saat dolmuş) · alıcı profil iade adresi **NULL** (§G.5 tuzağı temiz) |

---

## 3. Koşulan senaryolar

Saatler UTC. Her satır DB'den, sidecar logundan, backend logundan ya da zincirden okundu.

| # | Senaryo | Ölçüm | Sonuç |
|---|---|---|---|
| S0 | **Gerçek Steam girişi** (§G.4 kontrol 7 — bugüne kadar hiç koşulmamıştı) | iki hesap da OpenID ile girdi; `SteamAccountCreatedAt` dolduruldu (satıcı 2020-04-30, alıcı 2026-08-24) | ✓ |
| S1 | **İlan açma** (STEAM_ID yöntemi) | `20:59:09` `CREATED` · `Tec-9 \| Groundwater`, `assetId 18514036356` · 10.00 + %2 = **10.20 USDT** · ödeme adresi profilden (`TWrbG7…KmHK`, #312) | ✓ |
| S2 | **Alıcı kabulü** | `21:00:00` `ACCEPTED` · yeni **limited-account probu** alıcı için canlı koştu (6,4 sn, HTTP 200) · iade adresi işlem-bazlı snapshot | ✓ |
| S3 | **Satıcı hazırlık onayı** | `SELLER_CONFIRMED` · depozit `TJ4egh8fbT29DU1x6UTSSRnnjm3K7YXK6H` açıldı · ürün doğru uyarıyı gösterdi: *"alıcının envanteri gizli, teslimatı otomatik doğrulayamayacağız"* | ✓ |
| S3a | **Ödeme izleyicisi (T139)** | backend `Payment monitor armed with sidecar` · sidecar `Monitor started` · metrik `skinora_blockchain_active_monitors 1` · bir dakika sonra `Monitor already active — no-op restart` | ✓ |
| S4 | **Ödeme tespiti** | `21:05:06` `DETECTED`, tam **10.20 USDT**, hash `96a32f96…b2e062` · sidecar `payment.detected` webhook'u gönderdi | ✓ |
| S5 | **Blok onayı** | `21:06:06` **20/20** onay, blok **71195307** → `PAYMENT_RECEIVED`, `DeliveryDeadline 22:06:06` — tespitten onaya **60 saniye** | ✓ |
| S6 | **Teslimat — gerçek Steam takası** *(ilk kez)* | satıcı ekranı trade offer bağlantısını verdi (alıcının kendi trade URL'i) · teklif `9382179489` gönderildi, mobil onaydan geçti, alıcı kabul etti: **Trade Accepted 23 Eyl 00:21** · item 7 gün takas korumalı (CS2 kuralı, beklenen) | ✓ |
| S7 | **Alıcı onayı** | `21:23:17` `ITEM_DELIVERED` · `PayoutEligibleAt` 8 gün sonrasına damgalandı (`payout_settlement_days=8`) | ✓ |
| S8 | **Prova kısayolu** | `PayoutEligibleAt = SYSUTCDATETIME()` (§G.4 kontrol 10a, yalnız provada) | ✓ |
| S9 | **Mutabakat turu** *(ilk kez)* | `21:25:07` **eskalasyon**: `SETTLEMENT_NO_DELIVERY_REFERENCE` — baseline boş, `DeliveredBuyerAssetId` boş, `DeliveryEvidenceCaptures` **0** (alıcı envanteri `SELLER_CONFIRMED` anında gizliydi) | ✓ (beklenen dal) |
| S10 | **AD32 — admin mutabakatı kapatır** *(ilk kez)* | `POST /admin/transactions/:id/clear-settlement` → `SettlementVerifiedAt 21:26:45` · audit satırı **`SETTLEMENT_CLEARED_ADMIN`** (runbook §I.5 adım 3 birebir çalıştı) | ✓ |
| S11 | **Depozit süpürme** *(ilk kez)* | hesap açma 1 SUN → enerji planı **`{kind: burn, energyRequired: 29650, shortfall: 29650, reason: insufficient-stake}`** → `BURN_TOP_UP` **3,26 TRX** → `SWEEP` 10.20 USDT, hash `ab11d76e…b01e`, 36 onay | ✓ |
| S12 | **Satıcı ödemesi** *(ilk kez)* | süpürme `CONFIRMED` olduktan **sonra** kuyruğa girdi (#324 kuralı) · `SELLER_PAYOUT` **9.52 USDT**, hash `853a5fbb…1f11`, 39 onay → `21:36:11` **`COMPLETED`** | ✓ |
| S13 | **İzleyicinin kapanması** | `PaymentAddresses.MonitoringStatus` → **`STOPPED`** | ✓ |

### Muhasebe

| Cüzdan | Öncesi | Sonrası | Fark |
|---|---|---|---|
| Alıcı/satıcı cüzdanı `TWrbG7…KmHK` | 998.00 USDT | **997.32** | −0.68 (10.20 gitti, 9.52 geldi) |
| Sıcak cüzdan `TP6e9Y…YSFD` | 0.00 USDT | **0.68** | +0.68 = komisyon 0.20 + gas kesintisi 0.48 |

Sıcak cüzdanın TRX'i 546,1 → **538,6** (hesap açma + yakma takviyesi + işlem ücretleri ≈ 7,5 TRX).

**Satıcıya giden tutarın hesabı (02 §4.7):** eşik = komisyon × koruma oranı = 0.20 × 0.10 = **0.02**; gas 0.50 > eşik olduğu için fark satıcıdan kesiliyor: 10.00 − (0.50 − 0.02) = **9.52 USDT**. Formül doğru çalıştı.

---

## 4. Bulgular

### 4.1 🟡 `PayoutGasEstimateAlwaysFallsBack`

**Belirti.** Payout kuyruğa girerken backend log'u: `Gas fee estimate returned HTTP 400 … falling back to static setting` → `charging static fallback 0.50 USDT`.

**Ölçüm (fark ölçümü, canlı sidecar'a doğrudan).** Aynı uca iki istek:

```
{"fromAddress":null,"toAddress":"TWrbG7…","amount":"10","token":"USDT"}  → 400 INVALID_ESTIMATE_REQUEST
{"toAddress":"TWrbG7…","amount":"10","token":"USDT"}                      → doğrulamayı geçti
```

**Kök neden.** `ChargedGasFeeResolver.ResolvePayoutFeeAsync` isteği `FromAddress: null` ile kuruyor (`ChargedGasFeeResolver.cs:41`); gövde JSON'a `fromAddress: null` olarak yazılıyor. Sidecar yalnız **`undefined`**'ı hoş görüyor (`feeHandlers.ts:41`: `body.fromAddress !== undefined && !isNonEmptyString(...)`), `null` gelince 400 dönüyor.

**Neden bugüne kadar görünmedi.** İade yolu aynı çözücüyü `fromDepositAddress` **dolu** çağırıyor (`ChargedGasFeeResolver.cs:30`) — orada alan hiç null olmuyor. #315'in getirdiği çalışma anı tahmini bu yüzden yalnız iade tarafında çalışıyor; **payout her zaman sabit ayara düşüyor** ve kullanıcı bunu göremiyor (uyarı yalnız log'da).

**Etkisi.** Satıcıdan kesilen gas gerçek zincir maliyeti değil, `blockchain.payout_gas_fee_estimate_usdt` sabiti. Fazla ya da eksik kesinti kalıcı; tavan `blockchain.max_charged_gas_fee_usdt` (10 USDT) sınırlıyor.

**İkinci katman (ayrı ve bağımsız):** istek düzeltilse bile bu ortamda tahmin **`TRX_PRICE_UNAVAILABLE`** dönüyor — Binance ve CoinGecko'ya çıkılamıyor. Yani düzeltmenin doğrulaması fiyat kaynağı erişilebilir bir ortamda yapılmalı.

### 4.2 ⚪ `PrivateInventoryForcesManualPayout`

Alıcının envanteri `SELLER_CONFIRMED` anında gizliyse teslimat referansı **hiç doğmuyor** ve mutabakat turu `SETTLEMENT_NO_DELIVERY_REFERENCE` ile eskale ediyor (kod bunu bilerek yapıyor, `SettlementVerificationService.cs:311`). Sonuç: **ödeme ancak bir adminin AD32 kararıyla** çıkıyor. Bu provada tam olarak bu oldu.

Ürün, hazırlık onayında iki tarafa da *"teslimatı otomatik doğrulayamayacağız"* diyor — ama **sonucunu** söylemiyor: satıcının parası otomatik yolla hiç çıkmayacak, elle onay bekleyecek. Satıcı bunu ilanı açarken bilmiyor.

**Yapılacak (öneri):** (a) satıcıya hazırlık onayında bu sonucu söyle, (b) alıcıya envanterini açması için çağrı göster, (c) §I.5 kuyruğunun hacmini ölç.

### 4.3 ⚪ `AdminCanClearOwnSettlement`

AD32 (`clear-settlement`) yalnız `MANAGE_DISPUTES` yetkisine bakıyor; **işlemin tarafı olup olmadığına bakmıyor.** Bu provada mutabakatı kapatan admin, işlemin **satıcısıydı** (aynı hesap Super Admin) ve kendi ödemesini serbest bıraktı. Küçük ekipte admin aynı zamanda satıcı olabilir; görevler ayrılığı kontrolü yok.

**Yapılacak (öneri):** AD32 ve kardeş admin uçlarında "taraf olan admin karar veremez" kuralı tartışılmalı (02 §16 kapsamı).

### 4.4 Altyapı gözlemi — `.dockerignore` yok

`frontend/` derleme bağlamı **600 MB** (node_modules 568 MB + .next 43 MB) ve her derlemede aktarılıyor (ölçüm: 239 sn). Repoda hiç `.dockerignore` yok. İşlevsel bir kusur değil, derleme süresi maliyeti.

---

## 5. Çürütülen / doğrulanan tezler

| # | Tez | Ölçüm | Sonuç |
|---|---|---|---|
| Ç1 | *"Oturum enjeksiyonu (JWT) provayı koşmaya yeter"* | uygunluk kapısı `STEAM_UNAVAILABLE` verdi; `SteamAccountCreatedAt` yalnız **gerçek girişte** doluyor (`UserProvisioningService.cs:39`) | ✗ — gerçek Steam girişi şart |
| Ç2 | *"Kilit hesabı tanımlıysa süpürme enerjiyi devreder"* | plan `burn`, sebep `insufficient-stake` (Nile kilidi 18 TRX, gereken ≈ 3.100 TRX) | ✓ kod doğru, ortam yetersiz |
| Ç3 | *"Ödeme ve süpürme bağımsız çıkar"* | payout kuyruğu süpürme `CONFIRMED` olana kadar beklediği ölçüldü | ✓ #324 çalışıyor |
| Ç4 | *"Gas tahmini artık zincirden geliyor"* (#315) | payout yolunda **hiç çalışmıyor**, bkz. §4.1 | ✗ |

---

## 6. Bir sonraki prova için kontrol listesi

Önceki raporun listesi geçerli (§8, `REHEARSAL_2026-09-02.md`) — üzerine bu turda ölçülenler:

1. **Gerçek Steam girişi yapılmadan hiçbir kapı geçilmez.** JWT enjeksiyonu yalnız okuma/ölçüm içindir.
2. **`.env` şablonla karşılaştırılmalı** (`comm -23` ile anahtar farkı). Fail-closed tavanlar boşsa hiçbir transfer imzalanmaz.
3. **İmaj tazeliği davranışla ölçülmeli** (`/transactions/params` → `minHours ≠ 0`).
4. **Alıcının envanteri açık olmalı** — kapalıysa mutabakat otomatik yürümez, prova admin adımına düşer (§4.2).
5. **Aynı item 7 gün takas kilitli kalır**; ardışık prova için ikinci bir item gerekir.
6. Disk: Docker'ın sanal diski 140 GB; prova öncesi **en az 20 GB** boş yer bırakın.

---

## 7. Geri alınacak prova ayarı

⚠️ `auth.min_steam_account_age_days` = **1** (üretim değeri **30**). Alıcı hesabı bu raporun tarihinde 29 günlük; 2026-09-23'te 30 günü doluyor ve o andan sonra geri alma alıcıyı kapıda bırakmaz. **Geri alındığında DB'den teyit edilmelidir** — ölçülmeyen bir geri alma, yapılmamış bir geri almadır.
