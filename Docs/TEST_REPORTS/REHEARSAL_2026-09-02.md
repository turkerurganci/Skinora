# Canlı P2P Prova Raporu — Gerçek Steam Hesapları + Nile Testnet (2026-09-02)

**Kapsam:** 7 senaryo koşuldu · 3 senaryo koşulamadı · her adım DB, sidecar logu veya zincirden ölçüldü
**Sonuç:** **ödeme bacağı uçtan uca ✓** · **teslimat bacağı ⛔** · **2 yeni bulgu** (1 🔴 · 1 🟡) · **7 tez ölçümle çürütüldü**
**Ortam:** lokal stack (`docker-compose.yml`, override YOK), nginx `:8080`, imajlar `main` `f6c1141`'den 2026-09-01 21:54/21:55'te kuruldu
**Hesaplar:** satıcı `76561199053273410` / `turkerurganci` · alıcı `76561198652999063` / `turkerurganci_2`
**Zincir:** Tron **Nile testnet**, USDT `TXYZopYRdj2D9XRtbG411XZZ3kM5VkAeBf`
**İşlem:** `C393DEF3-FFF6-4061-900E-B789084E0755`

---

## 1. Neden bu prova yapıldı

Post-MVP tablosunun (`IMPLEMENTATION_STATUS.md` §G adım 10) açık kalan **tek** satırı happy path'ti. Blokaj Steam tarafındaydı ve **beklemekle** çözülüyordu: alıcı hesabının mobil doğrulayıcısı 2026-08-24'te kuruldu, Steam 7 günden genç MA'ya 15 günlük escrow uyguluyor, alıcı kapıları bekletme = 0 arıyor. 7 gün 08-31'de doldu.

Turun yöntemi UI turunun (`UI_TOUR_2026-08-23.md`) yöntemiyle aynı: **kusur aramak değil, ürünü kullanmaya çalışmak.** Aşağıdaki iki bulgunun ikisi de aranarak bulunmadı.

---

## 2. Hazırlık — yapılan ve doğrulananlar

| # | Adım | Doğrulama | Sonuç |
|---|---|---|---|
| H1 | MA beklemesinin dolduğu ölçüldü | `IEconService/GetTradeHoldDurations` (token ile) | `their_escrow = 0` ✓ |
| H2 | MA bayrağı **ürünün kendi ucundan** tazelendi | `PUT /users/me/settings/steam/trade-url` | 200, `mobileAuthenticatorActive: true`, DB `0 → 1` ✓ |
| H3 | Docker Desktop + stack kaldırıldı | `docker compose -f docker-compose.yml up -d --wait` | 11/11 healthy ✓ |
| H4 | §G.5 iki restart tuzağı uygulandı | `restart skinora-reverse-proxy skinora-grafana` | `:8080/health` 200, Grafana 8/8 `health: ok` ✓ |
| H5 | İmajın tazeliği **davranışla** ölçüldü | `/transactions/params` | `minHours: 1` (0 olsa bayat olurdu) ✓ |
| H6 | #312'nin canlı olduğu ölçüldü | frontend bundle dizgi araması | *"Ödeme Alacağınız Cüzdan"* **YOK**, `walletMissing` **VAR** ✓ |
| H7 | Satıcı uygunluğu prova öncesi okundu | `GET /transactions/eligibility` | `eligible: true`, kota 0/3 ✓ |

Komut disiplini: her `docker compose` çağrısı `-f docker-compose.yml` ile (§G.3).

**H3'te bir soğuk başlangıç yarışı görüldü ve teşhis edildi:** SQL Server container'ı `healthy` derken `Skinora` veritabanı henüz açılabilir değildi (`Error 4060 — Login failed`); backend `SystemSetting bootstrap failed — host will stop` ile fail-fast etti, yeniden başladı ve geçti. Ayakta olan sürecin bootstrap'i **gerçekten** tamamladığı ayrıca doğrulandı: fatal `17:46:14`, süreç başlangıcı `17:46:17`, bootstrap complete `17:46:22`. "Healthy" tek başına kanıt sayılmadı.

---

## 3. Koşulan senaryolar

Saatler UTC. Her satır DB'den, sidecar logundan veya zincirden okundu.

| # | Senaryo | Ne ölçüldü | Sonuç |
|---|---|---|---|
| S1 | **İlan açma** (satıcı, STEAM_ID yöntemi) | `17:51:57` `CREATED` · `Tec-9 \| Groundwater`, `assetId 18514036356` · 10.00 + %2 = **10.20 USDT** · `AcceptDeadline 18:51:57` | ✓ |
| S1a | **#312 canlı doğrulaması** | `SellerPayoutAddress` = `TWrbG7F…KmHK` = `User.DefaultPayoutAddress` → **EŞLEŞİYOR** | ✓ |
| S2 | **Alıcı kabulü** | `17:54:31` `ACCEPTED` · canlı MA probu geçti · `BuyerRefundAddress` işlem-bazlı snapshot alındı · `BuyerTradeUrl` yazıldı | ✓ |
| S2a | **§G.5 iade adresi tuzağı** | alıcı profil `DefaultRefundAddress` **NULL**, `RefundAddressChangedAt` **NULL** → Stage 5 cooldown kapısı geçildi | ✓ |
| S3 | **Satıcı hazırlık onayı** | `18:04:07` `SELLER_CONFIRMED` · deposit adresi tahsis edildi (`TF6nQDf6Pv7Ao8vfzSbCWm2Uh9H5iBhgyM`, HD index 0) · `PaymentDeadline 19:04:07` | ✓ |
| S3a | **Ödeme izleyicisi kurulumu (T139)** | backend `"Payment monitor armed with sidecar"` → sidecar `POST /api/monitor/start` → `"Monitor started"`, `started: true` | ✓ |
| S3b | **İzleyicinin kendi kendini toparlaması (T139)** | `EnsurePaymentMonitorJob` bir dk sonra tekrar çağırdı → sidecar `"Monitor already active — no-op restart"`, `started: false` | ✓ |
| S4 | **Ödeme tespiti** | zincire düşüş `18:09:24` → `DETECTED 18:10:27` (**63 sn**) · `BUYER_PAYMENT` satırı, `ConfirmationCount 0` | ✓ |
| S5 | **Blok onayı ve geçiş** | `20/20` onay, blok **70617761** → `18:11:28` `PAYMENT_RECEIVED` · `DeliveryDeadline 19:11:28` | ✓ |
| S6 | **Admin iptali** (AD19) | `18:56:35` `CANCELLED_ADMIN` · `paymentRefunded: true` · `CancelledBy = ADMIN` · **`FraudFlags = 0`** (satıcıya kusur yazılmadı) | ✓ |
| S7 | **İade transferi** | `BUYER_REFUND` `CONFIRMED`, **8.20 USDT**, 39 onay · zincirde `18:57:15` · alıcı bakiyesi `1000.00 → 998.00` | ✓ |

**S6 ve S7 bu tura kadar hiç koşulmamıştı.**

### Zincir kanıtları

| Ne | Hash |
|---|---|
| Alıcı ödemesi (10.20 USDT) | `fbfd958bc4a9a0f071fb850d2f87d8770f85a620725ab05235dae19e30b9358c` |
| Alıcıya iade (8.20 USDT) | `a89d6675e8bacc3593162a73ea60a4266a157a024cfa1d41e225e1c6070ecd0a` |

Muhasebe kapanıyor: `1000.00 − 10.20 + 8.20 = 998.00`. Fark **tam 2.00 USDT** ve nereye gittiği §5.2'de.

---

## 4. Koşulamayan senaryolar ve sebebi

| # | Senaryo | Neden koşulamadı |
|---|---|---|
| S8 | **Teslimat** (satıcı trade offer → alıcı kabulü → "teslim aldım") | Steam: *"turkerurganci_2 takas yapmak için uygun değil"* — bulgu §5.1 |
| S9 | **Mutabakat** (`settlement-verification`, item alıcıda mı) | S8 koşulmadan başlamıyor; ayrıca `payout_settlement_days = 8` ve tabanı 7 gün, tek oturumda gözlenemez |
| S10 | **Satıcı ödemesi** (`seller-payout-queue` → `COMPLETED`) | S9'a bağlı |

**Teslimat kanıt yolu ayrıca kapalıydı ve bu beklenen davranış:** alıcının Steam envanteri okunamıyor (o hesapta CS2 hiç çalıştırılmamış), `TransactionReadinessService` Stage 6 bunu **bilerek** non-blocking tutuyor, teslimat yalnız alıcı onayıyla ilerliyor (02 §9.2). Akışı durdurmadı.

---

## 5. Bulgular

### 5.1 🔴 `Prova-LimitedAccountNeverChecked`

**Belirti.** Satıcı trade offer sayfasını açtığında Steam *"turkerurganci_2 takas yapmak için uygun değil"* dedi. O ana kadar **hiçbir platform kapısı** itiraz etmemişti; para zincirde çoktan onaylanmıştı.

**Ölçüm.** Profil XML'i: alıcı `isLimitedAccount = 1`, satıcı `0`. Kaynak ağacında `isLimitedAccount` / `limited.?account` araması → **0 eşleşme** (`backend/src` + `sidecar-steam/src`). Platform bu kısıtı hiçbir yerde okumuyor.

**Takas edebilirlik üç bağımsız koşul, platform yalnız birini ölçüyor:**

| Koşul | Kural | Prova hesabı | Platform ölçüyor mu |
|---|---|---|---|
| Limited account | Steam **mağazasında** ≥ 5 USD harcama | `1` ⛔ | **hayır** |
| Hesap yaşı | **15 gün** | 9 günlük ⛔ (15'i 2026-09-08'de doluyor) | **hayır** |
| Escrow / MA | bekletme = 0 | `0` ✓ | evet — dolaylı olarak |

**Kök neden bir çıkarım hatası ve sidecar kendi yorumunda yazıyor** (`sidecar-steam/src/trade/TradeHoldService.ts:8-13`):

> *"Steam exposes no direct 'is Mobile Authenticator active?' endpoint; the platform **infers** it from the user's trade hold duration."*

Zincir: `escrow_end_duration_seconds == 0` → "MA aktif" → "takas edebilir". İlk iki adım doğru, **üçüncüsü yanlış** — o uç *"takas ne kadar bekletilir"* sorusuna cevap veriyor, *"bu hesap takas edebilir mi"* sorusuna değil, ve **limited hesap da `0` döndürüyor**.

**Neden ağır.** (a) Para emanette ve zincirde onaylı; (b) teslimat süresi dolsaydı tarama turu item'ı satıcıda bulup işlemi iptal eder ve **kusuru SATICIYA yazardı** (03 §4.4) — oysa engel tamamen alıcının hesap kısıtı; (c) limited hesap yeni Steam kullanıcıları arasında çok yaygın, nadir bir uç durum değil. **Simetriği de açık:** satıcı limited ise item'ı hiç gönderemez ve satıcı kapısı bundan da habersiz (üstelik o kapı bayat DB bayrağını okuyor).

**Ürün gereksinimi (proje sahibi isteği, 2026-09-02).** Kullanıcı bu duvara toslayınca ürün ona **sebebi ve ne yapması gerektiğini** söylemeli. Bugün hiçbir ekran söylemiyor; kullanıcı Steam'in jenerik sayfasını görüp platformda açıklama bulamıyor. Üç koşul **üç farklı eylem** gerektiriyor — *"5 USD'lik mağaza harcaması gerekiyor"* (cüzdana yükleme değil) · *"hesabınız 15 günlük olmalı, kalan: N gün"* · *"mobil doğrulayıcınız 7 günden genç"* — jenerik tek mesaj kullanıcıyı yanlış eyleme yönlendirir.

### 5.2 🟡 `Prova-GasFeeChargedIsFixedGuess`

**Belirti.** İadede alıcıdan **2.00 USDT** kesildi (10.20 → 8.20, **%19,6**).

**Ölçüm.** Kesinti kuralı doğru (02 §195-197: iade gas'i alıcıya yüklenir) ama tutar sabit bir ayardan geliyor: `blockchain.refund_gas_fee_estimate_usdt = 2.0` (payout ikizi `0.50`). Aynı ağdaki gerçek transferlerin ücreti `gettransactioninfobyid` ile ölçüldü:

| İşlem | hash | fee | energy_usage_total | origin_energy_usage |
|---|---|---|---|---|
| Alıcının ödemesi | `fbfd958b…` | **0** | 29.650 | **29.650** |
| **Platformun iadesi** | `a89d6675…` | **0** | 14.650 | **14.650** |

> **⚠️ DÜZELTME — 2026-09-04.** Bu bölümün ilk hâli iki şeyi yanlış yazmıştı ve ikisi de sonraki turda ölçülerek düzeltildi.
>
> **(1) Yanlış özne ölçülmüştü.** Yalnız `fbfd958b…` (alıcının **gelen** ödemesi) ölçülüp platformun kesintisine gerekçe yapılmıştı; oysa gerekçeyi taşıması gereken işlem `a89d6675…`, yani platformun imzaladığı **iade**. İkisi de sıfır çıktığı için sonuç tesadüfen doğruydu — `feedback_verify_probe_subject` ailesi.
>
> **(2) Sıfırın sebebi yanlış açıklanmıştı.** *"Delege enerji yakılacak TRX bırakmadı"* denmişti. Delegasyon hiçbir şey teslim edemez: hot cüzdanın stake'i **sıfır** (`getaccountresource` → `EnergyLimit: 0`, 2026-09-04 ölçümü), dolayısıyla delege edilecek enerji yok. Gerçek sebep `origin_energy_usage` kolonunda duruyor: bu Nile test USDT'si (`TXYZop…`) `consume_user_resource_percent = 0` ile deploy edilmiş, yani **enerjiyi kontratın sahibi ödüyor**. Mainnet Tether bunu 100 yapar; orada gönderen öder.
>
> Bir sonucu görüp sebebini varsaymak, sonuç doğru olsa bile yanlış bir kural üretiyor — bu kural üzerine kurulan tahmin, bu ölçümden çıkan üçüncü kusuru doğurdu (aşağıda).

Mainnet'te enerji satın alınsaydı ~0,6–0,9 USD tutardı — yani sabit değer gerçeğin **2–3 katı**, bu işlemde ise tamamen gereksizdi.

**Tasarım tercihi değil, yarım kalmış iş.** Kodun kendi seed yorumu söylüyor (`SystemSettingSeed.cs:123`, payout ikizi `:161`): *"T72 MVP iade gas fee tahmini … **T74 energy delegation tamamlandıktan sonra runtime Energy/Bandwidth bedeli ile değiştirilir**"*. T74 çıktı, değiştirme yapılmadı.

**Sabit sayı ilkesel olarak da yanlış.** `sidecar-blockchain/src/config/index.ts:94-96` zincirden ölçerek yazıyor: aynı TRC-20 transferi, alıcının o token'dan bakiyesi **varsa ~64.285**, **yoksa ~130.285** enerji yakıyor — **iki kat fark**. Enerji/TRX oranı da ağ genelinde değişken (*"cannot stay a constant here"*).

**Fark platformda kalıyor ve muhasebesi yok.** Kesilen 2.00 USDT yakılmadı — deposit adresinde duruyor (zincirden ölçüldü) ve sweep turu onu sıcak cüzdana taşıyacak. Yani kullanıcı sessizce fazla ödüyor, fazlası platforma geçiyor.

**Proje sahibi kararı (2026-09-02): kesin tutar kesilmeli.** Kısıt: gerçekleşen ücret ancak gönderim **sonrası** bilinir ve o noktada kesilecek bakiye kalmaz; fark için ikinci transfer çoğu vakada farkın kendisinden pahalıdır. **Yapılabilir olan gönderim öncesi hesaplanmış maliyet:** `triggerconstantcontract` ile bu transferin enerjisi + hot wallet'ın delege/stake enerjisi + güncel enerji fiyatı → yakılacak TRX → USDT.

> **⚠️ DÜZELTME — 2026-09-04.** Bu paragrafın son cümlesi *"bugün bu yapılsaydı sonuç 0 çıkardı"* diyordu. **Yanlış, ölçüldü.** Aynı transfer için canlı `triggerconstantcontract` **29.650** enerji raporluyor; hot cüzdanın enerjisi **0** olduğu için hesabın tamamı açığa yazılıyor: 29.650 × 100 sun = 2,965 TRX × 0,3244 = **0,97 USDT**. Yani ilk hâliyle tahmin, hiç kimsenin ödemediği bir maliyet için kullanıcıdan 0,97 dolar keserdi — eski sabitin yarısı, ama sıfır değil.
>
> **Gerekçenin kendisi de eksikti:** "hot wallet'ın delege/stake enerjisi" ifadesi delegasyonun tavanını görmezden geliyordu. Deposit adresine hot cüzdanın **havuzu** değil, sabit bir delegasyon (`sweepEnergyDelegationSun`, 200 TRX) aktarılıyor; mainnet'te bu ~1.914 enerji eder, bir transfer ise ~64.285 ister.
>
> **Üç düzeltme 2026-09-04'te yapıldı** (`FeeEstimationService`): kontratın `consume_user_resource_percent` ayarı okunuyor (sahip ödüyorsa kullanıcıdan kesilmiyor) · iade yolunda enerji kredisi delegasyonun taşıyabileceğiyle sınırlı · bandwidth ya-hep-ya-hiç yakılıyor. Bugün aynı iade **0.00** keser, çünkü kontrat sahibi ödüyor — bu kez sebebi ölçülerek.

**Dikkat — payout sabitinin ikinci tüketicisi var:** 02 §4.7 gas-fee koruma split'i (04 §7.3 örneği `0.50` → satıcıdan `0.30`). Değer değişince o eşik de yeniden ölçülmeli.

**`EnergyPerTrxAssumptionUnverified` ile karıştırılmasın:** o satır platformun **ödediği** delegasyonu boyutlandırıyor, bu satır kullanıcıdan **kesilen** tutarı.

---

## 6. Ölçümle çürütülen tezler

Bu turun en verimli kısmı. Yedi iddia ölçülünce yanlış çıktı; hepsi eyleme dönüşmeden yakalandı.

| # | Tez | Ölçüm | Sonuç |
|---|---|---|---|
| Ç1 | *"Boş `{"response":{}}` = MA yok"* | MA'sı **kesin aktif** olan birinci hesap **aynı boşu** verdi | ✗ — boş cevap "token vermedin" demek; `trade_offer_access_token` zorunlu |
| Ç2 | *"Sanctions taraması gereksiz, profil kaydında zaten var"* | Yaptırım listesi adres kaydedildikten **sonra** büyüyebilir; U3 yalnız o günkü listeye bakabilir | ✗ — create anı bugünkü listeye bakan tek nokta; tarama **korundu** |
| Ç3 | *"`gas_fee_protection_ratio` %10 aşıldı, kusur olabilir"* | O ayar başka yerde (satıcı gas payı) kullanılıyor; iadeyi bastıran kural `min_refund_threshold_ratio = 2.0`, net `8.20 > 4.00` | ✗ — doğru davranış |
| Ç4 | *"Alıcının `SteamTradeUrl`'i NULL"* (tracker) | DB'den okundu | ✗ — **kayıtlıydı**; bayrak `0`'da kalmıştı çünkü URL hold > 0 iken kaydedilmişti |
| Ç5 | *"`.env`'de 8 dış-değer anahtarı boş"* (hafıza) | `.env` sayıldı | ✗ — **4 boş ve hiçbiri bloke etmiyor** (ikisi bilerek same-origin, `TRON_USDC_CONTRACT` bilerek, `TRON_API_KEY_SECONDARY` failover) |
| Ç6 | *"`hot_wallet_address` kurulmadı"* (hafıza) | SystemSettings | ✗ — **kurulu** (`TP6e9Yqa…YSFD`) |
| Ç7 | *"Limit kalktı"* (nöbetçi, **iki kez**) | Peş peşe 5 örnekleme | ✗ — ikisi de yanlış alarm; §7'ye bak |

---

## 7. Ölçüm aracı uyarısı — profil XML'i tek okumada güvenilmez

`steamcommunity.com/profiles/<steamid64>?xml=1` → `<isLimitedAccount>` **iki kez yanlış "limit kalktı" alarmı verdi.** İlk nöbetçi tek okumaya karar bağlıyordu; düzeltip **iki ardışık teyit** istedim, o da yanlış alarm verdi. Her iki turda da peş peşe **5 örnekleme** gerçeği gösterdi: değer `1`'de sabit.

Uç arada bir bayat `0` döndürüyor (muhtemelen CDN düğümü). **Kural: en az 5 ardışık aynı okuma olmadan karar verme; nihai testi üründen yap** (trade offer sayfası açılıyor mu). Bu, [[feedback_verify_probe_subject]]'in kardeşi — sorun probe'un öznesi değil, **kararlılığı**.

Runbook §G.5'e yazıldı.

---

## 8. Bir sonraki prova için kontrol listesi

Sırayla, prova gününden **önce**:

1. **Her iki hesabın da limited olmadığını doğrula** — 5 ardışık okuma:
   ```bash
   for i in 1 2 3 4 5; do curl -s "https://steamcommunity.com/profiles/<id>?xml=1" \
     | grep -o '<isLimitedAccount>[01]</isLimitedAccount>'; sleep 3; done
   ```
2. **15 günlük eşiğin dolduğunu doğrula.** Prova hesabı `2026-08-24`'te açıldı → **2026-09-08**.
3. **MA bekletmesinin 0 olduğunu doğrula** — `GetTradeHoldDurations`, token **ile**.
4. **Ekonomi/VAC yasağı yok** — `GetPlayerBans` → `EconomyBan: none`.
5. **Satıcının ödeme adresi ≥ 24 saat önce kaydedilmiş olmalı** (`wallet.payout_address_cooldown_hours`). Prova günü girilirse prova bir gün kayar. **Adres bir daha değiştirilmemeli.**
6. **Alıcı profil iade adresine DOKUNMAMALI** — sezginin tersi, §G.5.
7. **İmajın tazeliğini davranışla ölç** — `/transactions/params` → `minHours` **0 değil**.
8. **İki restart tuzağı** — `restart skinora-reverse-proxy skinora-grafana`.

**Prova sırasındaki kısıtlar (bu turda ölçüldü):** `open_link_enabled = false` → **STEAM_ID** yöntemi zorunlu · satıcı yeni hesap kotası **3 işlem** (pencere 2026-09-06'ya kadar, **iptaller de sayılıyor** — bu turda 1 hak kullanıldı) · ödeme süresi tek geçerli değer **1 saat**.

**Süre dolmadan admin iptali, satıcıyı hak etmediği kusurdan korur.** Timeout'a bırakılırsa kusur satıcıya yazılır (03 §4.4).

---

## 9. Geri alınacak prova ayarı

⚠️ `auth.min_steam_account_age_days` = **1** (üretim değeri **30**). İkinci hesabın Steam hesabı bu yazının tarihinde 9 günlük olduğu için prova bitmeden geri alınamaz. **Ölçülmeyen bir geri alma, yapılmamış bir geri almadır** — geri alındığında DB'den teyit edilmeli.
