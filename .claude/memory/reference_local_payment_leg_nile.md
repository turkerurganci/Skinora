---
name: reference_local_payment_leg_nile
description: Lokal ödeme bacağı Nile testnet kurulumu — üretilen cüzdanlar, seçilen USDT kontratı ve kalan dış-değer borcu
type: reference
---

**2026-08-28'de lokal ödeme bacağının yapılandırılabilir yarısı kuruldu** (`DEPLOY_RUNBOOK §G` adım 5 + 9). Sırlar `.env`'de (gitignored); burada yalnız **kamuya açık** değerler var.

- **HD master mnemonic** — 24 kelime, `skinora-blockchain-sidecar` container'ının kendi `ethers`'ıyla üretildi (256-bit entropy). Deposit adresleri `m/44'/195'/0'/0/{index}` yolundan türüyor. **index 0 = `TF6nQDf6Pv7Ao8vfzSbCWm2Uh9H5iBhgyM`** — bu adres kurulum doğrulaması için kullanılır: `POST /api/wallet/derive {"index":0}` aynı adresi döndürmelidir (`.env` → container → `HdWalletService` yolunun uçtan uca kanıtı).
- **Hot wallet** — HD master'dan bağımsız ayrı anahtar (05 §3.3 gereği), adres **`TP6e9Yqa1wFFDbJzKaSgTwBq2LHax9YSFD`**. Aynı adres `reconciliation.hot_wallet_address` SystemSetting'ine **admin API üzerinden** yazıldı (`PUT /api/v1/admin/settings/:key`, süper admin JWT ile; audit satırı oluştu) — SQL'le değil, çünkü ayar env'den set edilemez ve gerçek doğrulama yolundan geçmesi istendi.
- **`TRON_USDT_CONTRACT` = `TXYZopYRdj2D9XRtbG411XZZ3kM5VkAeBf`** — Nile'da zincire sorularak seçildi: `name()` = *Tether USD*, `symbol()` = *USDT*, `decimals()` = **6** (sidecar `tokenDecimals: 6` bekliyor), totalSupply ≈ 17.6M. Rakip aday `TXLAQ63Xg1NAzckPwKHvzw7CSEmLMEqcdj` de aynı üç değeri veriyor ama totalSupply'ı ~1e27 (serbest mint test token'ı). **Kesin karar faucet'ten gelen token'la verilir** — runbook §G.5 bunu şart koşuyor; bakiye geldiğinde hangi kontratta durduğu `POST /api/wallet/balances` ile teyit edilmeli.
- **`TRON_USDC_CONTRACT` bilerek BOŞ bırakıldı.** Kod boşu güvenle işliyor (`walletHandlers.ts:96` ve `PaymentMonitorRules.ts:46` boş girişi atlıyor), allowlist yalnız USDT taşıyor. Doğrulanamamış bir adres yazmak, yanlış kontratı allowlist'e almaktan daha kötü olurdu.
- **Bağlantı kanıtı** (`/health` değil): `POST /api/wallet/balances` **`blockNumber: 70476111`** ile döndü ve `tokens` yalnız `TRX` + `USDT` taşıdı — sidecar Nile'ı gerçekten okuyor ve allowlist beklendiği gibi. **TronGrid API key olmadan çalıştı**; anahtar hız limiti içindir, açılış şartı değil.

**Kalan dış-değer borcu (yalnız insan yapabilir) — ~~bu satırın tamamı 2026-08-29/09-01'de KAPANDI~~, tarihsel kayıt olarak duruyor:** TronGrid API key (`TRON_API_KEY`; 3 sn'lik izleme turu anahtarsız 429 yer) · hot wallet'a faucet TRX (sweep 200 TRX energy delege eder) · alıcı rolündeki cüzdana Nile test USDT. Ayrıca happy path'in Steam yarısı için ikinci hesabın (`76561198652999063`) MA + trade URL'i gerekiyor — alıcı kapıları fail-closed. **Bugünkü gerçek borç: YOK.** `.env`'de boş kalan 4 anahtarın hiçbiri bloke etmiyor (`NEXT_PUBLIC_API_URL` + `NEXT_PUBLIC_SIGNALR_URL` bilerek boş — same-origin varsayılanı; `TRON_USDC_CONTRACT` bilerek boş; `TRON_API_KEY_SECONDARY` yalnız failover).

İlgili: [[reference_local_stack_runbook_g]]


## Prova durumu — 2026-08-29 (nerede kaldık)

**Tron tarafı BİTTİ, Steam tarafı 31 Ağustos'a kadar bloke.**

Hazır olanlar (ölçüldü):

- Hot wallet `TP6e9Yqa1wFFDbJzKaSgTwBq2LHax9YSFD` → **798,9 TRX**. Faucet 1000 TRX verdi; 200'ü alıcı cüzdanına gönderildi, 1,1 TRX ücret (1 TRX yeni hesap açma + 0,1 bant genişliği — `08 §3.3` ile uyumlu).
- Alıcı cüzdanı (TronLink, Nile) `TWrbG7F38xPMty4jRhgnBxrfAPtS84KmHK` → **200 TRX + 1000 USDT**.
- USDT'nin durduğu kontrat **`TXYZopYRdj2D9XRtbG411XZZ3kM5VkAeBf`** — yani `.env`'e yazılan kontrat **faucet tarafından doğrulandı** (rakip aday `TXLAQ63…`'te bakiye **0**). Runbook §G.5'in "faucet'in verdiğini kullan" kuralı karşılandı.
- `TRON_API_KEY` dolu. **Geçerliliği kanıtlanamadı:** Nile uydurma bir anahtara da 200 dönüyor, yani "çalıştı" cevabı anahtarı doğrulamıyor. Gerçek kanıt trongrid.io panelindeki istek sayacı ya da provada 429 görmemek.
- `auth.min_steam_account_age_days` **30 → 1** yapıldı (admin API, audit kaydıyla). İkinci hesabın Steam hesabı 4 günlüktü ve giriş kapıda duruyordu. **Prova sonrası 30'a geri alınmalı.**

Bloke eden tek şey (Steam) — **2026-09-01'de KALKTI, aşağıdaki son bölüme bak**:

- `turkerurganci_2` (`76561198652999063`) Steam hesabı **2026-08-24**'te açıldı ve MA **aynı gün** etkinleştirildi. Steam 7 günden genç MA'ya 15 günlük bekletme uyguluyor, alıcı kapıları da bekletme = 0 şartını arıyor. **7 gün 2026-08-31'de doluyor**; o gün trade URL kaydı hiçbir kısayol olmadan geçer.
- **Denenip geri alınan kısayol:** `MobileAuthenticatorVerified = 1`'i veritabanına elle yazmak **kapıyı açmıyor**. Alıcı kabul ve hazırlık adımları bayrağı hiç okumuyor, canlı probe kullanıyor (`TransactionAcceptanceService.cs:209` yorumunda yazılı). Bayrak yalnız satıcı kapısını besliyor. Tekrar denenmesin.
- Alıcının CS2 envanteri yok ve **gerek de yok** — teslimat referansı bilerek non-blocking. CS2 indirmeye gerek yok, ayrıntı `DEPLOY_RUNBOOK §G.5`'te.

**Değerlendirilip elenen alternatif — rolleri takas etmek (2026-08-29).** *"Eski hesabı alıcı, yenisini satıcı yapalım"* önerisi ölçüldü ve **daha uzun** çıktı. Eski hesap alıcı olarak gerçekten geçerdi (canlı probe ölçüldü: `turkerurganci` bekletme **0 saniye**). Ama engel çözülmüyor, **yer değiştiriyor**: `turkerurganci_2` satıcı olunca satacak bir şeyi yok — o hesapta **CS2 envanteri hiç yok**. Item koymanın üç yolu da 2 günden uzun: ücretsiz haftalık düşüş (rastgele/yavaş), eski hesaptan göndermek (**o takas 15 gün beklemede kalır**, çünkü alan tarafın MA'sı genç), market (yeni hesapta kısıt). Üstelik P2P'de item'ı **satıcı** gönderdiği için bekletme tam da gönderen tarafa geçer ve teslimat bacağı yine gözlenemez. **Sonuç: takas 2 gün yerine ~2 hafta demek. Tekrar önerilmesin.**

31 Ağustos'ta koşulacak: trade URL kaydı → satıcı ilan açar (`turkerurganci`, envanterinde Tec-9 | Groundwater var) → alıcı kabul → `confirm-ready` → deposit adresi → TronLink'ten USDT → `payment-detected` → 20 blok → `PAYMENT_RECEIVED`.

## Prova provası — 2026-08-29 (satıcı bacağı koşuldu, iki gizli engel çıktı)

Stack `main` `2e4c59c`'ye taşındı ve `DEPLOY_RUNBOOK §G.4` kontrolleri 1–9 koşuldu. **Prova provasının bulduğu iki engel, ikisi de prova gününü tek başına öldürebilirdi:**

1. **Satıcının ödeme adresi 24 saatlik ZAMAN kapısıdır.** `POST /transactions` önce `SELLER_WALLET_ADDRESS_MISSING` (422) döndü; `PUT /users/me/wallet/seller` ile adres yazılınca kapı `PAYOUT_ADDRESS_COOLDOWN_ACTIVE`'e döndü (`wallet.payout_address_cooldown_hours` = **24**). Adres **2026-08-29 13:21**'de yazıldı → cooldown **2026-08-30 13:21**'de doluyor, yani 31 Ağustos provası temiz. Prova günü girilseydi prova bir gün kayardı. **Seçilen adres `TWrbG7F38xPMty4jRhgnBxrfAPtS84KmHK`** (alıcı TronLink cüzdanının aynısı; proje sahibi kararı — para aynı cüzdana döner, payout bacağı yine gözlemlenebilir). **Bir daha değiştirilmemeli**: her değişiklik cooldown'ı sıfırdan başlatır.
2. **Alıcı profil iade adresine DOKUNMAMALI — sezginin tersi.** Kabul adımı iade adresini **işlem bazlı anlık görüntü** olarak alır (WP12/T90 K4); profil `DefaultRefundAddress` yazılmaz, profil cooldown'ı başlamaz. Ama `TransactionAcceptanceService` Stage 5 kapısı **profil** `RefundAddressChangedAt`'ine bakar → alıcı "hazır olayım" diye profilinden adres girerse **kabul 24 saat bloke olur**. Alıcının bugün profil adresi **yok** → kapı temiz; öyle kalmalı.

Ayrıca ölçülenler: 63 ayar / 0 null · gerçek CS2 envanteri okundu (`Tec-9 | Groundwater`, `assetId 18514036356`) · backend `StablecoinContracts__Usdt` ile sidecar `TRON_USDT_CONTRACT` **aynı** Nile adresinde (#306 uçtan uca canlı) · `/transactions/params` → `minHours: 1` (0 değil) · frontend bundle'ında `nile.tronscan.org`.

**Üçüncü bulgu, prova dışı ama aynı aileden:** #307'nin Grafana düzeltmesi **yeniden kurmayla devreye girmiyor**. `up -d --build --wait` Grafana'yı yeniden başlatmaz (servis tanımı değişmez, yalnız mount edilen volume'ün içeriği değişir); yedi kural hâlâ `health: error` idi. `restart skinora-grafana` sonrası yedisi de **ilk kez** `health: ok`. Ayrıntı `DEPLOY_RUNBOOK §G.5`'te. Kalıcı ders yine [[feedback_verify_probe_subject]]: merge edilmiş bir düzeltme, devrede olduğu **ölçülene kadar** devrede değildir.

## Prova günü — 2026-09-01 (MA beklemesi bitti, prova blokajı kalktı)

**Steam engeli beklenerek çözüldü, tam da 2026-08-29'da öngörüldüğü tarihte.** MA 08-24'te kuruldu, 7 gün 08-31'de doldu; 09-01'de canlı Steam probu (`IEconService/GetTradeHoldDurations`, `steamid_target` + `trade_offer_access_token`) `their_escrow.escrow_end_duration_seconds = 0` döndürdü.

**Ölçüm yönteminde bir tuzak var ve kontrol probu olmasa yanlış rapor edilecekti:** `trade_offer_access_token` **olmadan** yapılan çağrı `{"response":{}}` döndürüyor — yani boş cevap "MA yok" değil, "token vermedin" demek. Arkadaş olmayan hedef için token zorunlu (`TradeHoldService.ts:15-18`). Kontrol probu (MA'sı kesin aktif olan birinci hesap) **aynı boş cevabı** verdi; ayırt edici ölçüm buydu.

**Bayrak ürünün kendi ucundan tazelendi, DB'ye elle yazılmadı.** `PUT /users/me/settings/steam/trade-url` aynı URL ile tekrar çağrıldı → 200 + `mobileAuthenticatorActive: true`, DB `MobileAuthenticatorVerified 0 → 1`, `/auth/me` teyit etti. Servis "URL değişmedi" diye erken dönmüyor; her çağrıda canlı probe yapıp bayrağı yeniden hesaplıyor. **Bayrak neden `0`'da kalmıştı:** trade URL zaten kayıtlıydı (tracker "NULL" diyordu, yanlıştı) ama hold > 0 iken kaydedilmişti.

**Not — bayrağın bayat kalması alıcı bacağını zaten bloke etmiyordu.** Kabul ve hazırlık adımları canlı probe kullanıyor; bayrak yalnız **satıcı** uygunluk kapısını besliyor (`TransactionEligibilityService`). Yani 08-29'da kaydedilen "kısayol işe yaramaz" dersi doğruydu ama sebebi daha da güçlüydü: kısayolun düzeltmeye çalıştığı şey alıcı bacağında hiç okunmuyordu.

**Prova için ölçülen dört kısıt:** `open_link_enabled = false` → **STEAM_ID** yöntemi zorunlu (alıcının 17 haneli SteamID64'ü girilir) · satıcı yeni hesap kotası **3 işlem**, pencere 2026-09-06'ya kadar ve **iptaller de sayılıyor** · ödeme süresi tek geçerli değer **1 saat** (ayar dakika 15–60, istek saat cinsinden ×60) · alıcının profil iade adresi NULL kalmalı, adres kabul ekranında elle girilir.

**Ortam durumu (09-01 ölçüldü):** 11/11 container healthy · imajlar 2026-08-29 13:12 (#306+#307 içeriyor) · `:8080/health` 200 · nginx upstream temiz · Grafana **8/8 kural `health: ok`** (yeniden başlatma sonrası) · hot wallet **798,9 TRX** · alıcı cüzdanı **200 TRX + 1000 USDT** · sidecar `tron-node healthy, solid block 70591182` · satıcı `GET /transactions/eligibility` → `eligible: true` (ödeme adresi cooldown'ı 08-30'da doldu).

**Turun asıl ürünü prova değil, provayı doğrularken çıkan iki kusur** — ikisi de `Docs/DEFERRED_BACKLOG.md` §11'de: 🔴 `Prova-SellerPayoutAddressBypassesCooldown` (ödeme adresini koruyan iki kontrol de `POST /transactions` üzerinden atlanabiliyor — kapı profili okuyor, payout gövdeden gelen adrese gidiyor) ve 🟡 `Prova-InlineSellerWalletUnreachable` (04 §7.2'nin satır içi cüzdan girişi hiç çalışmıyor). **Kalıcı ders:** `wallet.payout_address_cooldown_hours` yapılandırılmış, ölçülmüş ve prova planlaması onun etrafında kurulmuştu — ama koruduğu değer o kapıdan hiç geçmiyor. [[feedback_verify_probe_subject]]'in kardeşi: bir kontrolün **var olması**, koruduğu şeyi koruduğu anlamına gelmiyor.

**⚠️ Hâlâ geri alınmadı:** `auth.min_steam_account_age_days` = **1** (üretim değeri 30). İkinci hesabın Steam hesabı 8 günlük olduğu için prova bitmeden geri alınamaz.

## Canlı prova — 2026-09-02 (ödeme bacağı ✓, teslimat Steam hesap kısıtına takıldı)

**Tam rapor:** `Docs/TEST_REPORTS/REHEARSAL_2026-09-02.md` (senaryolar, ölçümler, zincir hash'leri, çürütülen tezler, sonraki prova kontrol listesi). Aşağısı özet.

**Koşan ve ölçülen zincir:** ilan açma → alıcı kabulü (canlı MA probu geçti) → hazırlık onayı (deposit adresi açıldı, **ödeme izleyicisi gerçekten kuruldu** — sidecar `"Monitor started"`, T133b'nin sessizce durduğu nokta) → alıcı 10.20 USDT gönderdi → **63 sn'de tespit** → 20/20 onay, blok **70617761** → `PAYMENT_RECEIVED`. #312 de canlıda doğrulandı: `SellerPayoutAddress` ilk kez profilden yazıldı, ikisi birebir eşleşti.

**Durduğu yer ve sebebi:** satıcı trade offer'ı açtığında Steam *"turkerurganci_2 takas yapmak için uygun değil"* dedi. İki kapı birden kapalıydı ve **platform ikisini de hiç okumuyor**:
1. **Limited account** — 5 USD'lik **mağaza harcaması** yapmamış hesap takas edemez. Cüzdana yükleme **saymıyor**, harcamak gerekiyor. Market alışverişi de saymıyor (üstelik limited hesap Market'i zaten kullanamıyor).
2. **15 günlük bekleme** — hesap yaşı / Steam Guard süresi. Prova hesabı 2026-08-24'te açıldı, 15 günü **2026-09-08**'de doluyor. 5 USD harcansa bile bu tarihe kadar takas açılmıyor.

**Kök neden bir çıkarım hatası ve sidecar kendi yorumunda yazıyor** (`TradeHoldService.ts:8-13`): MA doğrudan ölçülmüyor, `GetTradeHoldDurations`'tan **çıkarılıyor**. Zincir: `escrow == 0` → "MA aktif" → "takas edebilir". İlk iki adım doğru, **üçüncüsü yanlış** — o uç *"bekletme ne kadar"* sorusuna cevap veriyor, *"bu hesap takas edebilir mi"* sorusuna değil, ve limited hesap da `0` döndürüyor. Backlog 🔴 `Prova-LimitedAccountNeverChecked`.

**Neden bu, ailenin en pahalı örneği:** kapı **vardı**, **geçti**, ve koruduğu sanılan şeyi korumuyordu — ama bu kez bedel teoride kalmadı. Alıcının parası zincirde onaylandı, teslimat imkânsız çıktı, ve süre dolsaydı sistem kusuru **satıcıya** yazacaktı (03 §4.4) oysa engel tamamen alıcının hesap kısıtıydı.

**Nasıl kapatıldı:** timeout'tan 15 dk önce admin iptali → `CANCELLED_ADMIN`, `paymentRefunded: true`, iade **8.20** USDT (10.20 − 2.00 gas, 02 §195-197), `FraudFlags = 0`. **Böylece hiç koşulmamış bir bacak daha ölçüldü: admin iptali + iade kuyruğu.** Yan ölçüm: `gas_fee_protection_ratio` (%10) şüphesi **yersiz çıktı** — iadeyi bastıran kural `min_refund_threshold_ratio = 2.0` (net < gas×2 ise durdurulur); 8.20 > 4.00, doğru davranış.

**ÖLÇÜM ARACI UYARISI — iki kez yanlış alarm verdi.** `steamcommunity.com/profiles/<id>?xml=1` → `<isLimitedAccount>` **tek okumada güvenilmez**: arada bir bayat `0` döndürüyor ve hemen ardından yine `1` diyor. İki nöbetçi de "limit kalktı" dedi, peş peşe 5 örnekleme ikisini de yalanladı. **Kural: en az 5 ardışık aynı okuma; nihai test üründen (trade offer sayfası açılıyor mu).** [[feedback_verify_probe_subject]]'in kardeşi — bu kez sorun probe'un öznesi değil, **kararlılığı**.

**Proje sahibi isteği (2026-09-02):** ürün bu duvara toslayan kullanıcıya **sebebi ve ne yapması gerektiğini** söylemeli. Bugün hiçbir ekran söylemiyor; kullanıcı Steam'in jenerik sayfasını görüp platformda açıklama bulamıyor. Üç koşul üç farklı eylem gerektiriyor (5 USD harca · N gün bekle · MA kur), jenerik tek mesaj yanlış yönlendirir.

**Sonraki prova için:** hesabın 15 günü 2026-09-08'de doluyor; `auth.min_steam_account_age_days` hâlâ **1** (üretim 30) ve prova bitince geri alınmalı. Ölçülemeyen bacaklar: teslimat · mutabakat · payout.
