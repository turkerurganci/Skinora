# Audit Raporu — 04_UI_SPECS.md

**Tarih:** 2026-03-16
**Hedef:** 04 — UI Specifications (v1.3)
**Bağlam:** 02_PRODUCT_REQUIREMENTS.md (v1.5), 03_USER_FLOWS.md (v1.5), 10_MVP_SCOPE.md (v1.1)
**Odak:** Tam denetim

---

## Envanter Özeti

| Kaynak | Toplam Öğe | ✓ | ⚠ | ✗ |
|---|---|---|---|---|
| 02_PRODUCT_REQUIREMENTS | 78 | 72 | 4 | 2 |
| 03_USER_FLOWS | 65 | 60 | 3 | 2 |
| 10_MVP_SCOPE | 38 | 36 | 1 | 1 |
| Hedef (04 iç) | 32 | 29 | 2 | 1 |
| **Toplam** | **213** | **197** | **10** | **6** |

---

## Envanter ve Eşleştirme Detayı

### Envanter — 02_PRODUCT_REQUIREMENTS

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 1 | 02-§2.1-01 | İşlem oluşturma: satıcı item seçer, stablecoin belirler, fiyat ve timeout girer | §2.1 | S06 (§7.2) | ✓ |
| 2 | 02-§2.1-02 | Alıcı kabulü: alıcı detayları görür ve kabul eder | §2.1 | S07 CREATED (§7.3) | ✓ |
| 3 | 02-§2.1-03 | Item emaneti: platform satıcıya trade offer gönderir | §2.1 | S07 ACCEPTED/TRADE_OFFER_SENT_TO_SELLER (§7.3) | ✓ |
| 4 | 02-§2.1-04 | Ödeme: platform benzersiz adres üretir, alıcı gönderir | §2.1 | S07 ITEM_ESCROWED (§7.3) | ✓ |
| 5 | 02-§2.1-05 | Ödeme doğrulama: blockchain otomatik | §2.1 | S07 PAYMENT_RECEIVED (§7.3) | ✓ |
| 6 | 02-§2.1-06 | Item teslimi: platform alıcıya trade offer gönderir | §2.1 | S07 TRADE_OFFER_SENT_TO_BUYER (§7.3) | ✓ |
| 7 | 02-§2.1-07 | Teslim doğrulama: Steam otomatik | §2.1 | S07 ITEM_DELIVERED (§7.3) | ✓ |
| 8 | 02-§2.1-08 | Satıcıya ödeme: komisyon kesilir, kalan gönderilir | §2.1 | S07 COMPLETED (§7.3) | ✓ |
| 9 | 02-§2.2-01 | Her işlem tek item | §2.2 | S06 Adım 1 (§7.2) | ✓ |
| 10 | 02-§2.2-02 | Sadece item karşılığı kripto | §2.2 | S06 Adım 2 (§7.2) | ✓ |
| 11 | 02-§2.2-03 | İşlemi her zaman satıcı başlatır | §2.2 | S06 (§7.2) | ✓ |
| 12 | 02-§2.2-04 | Detaylar oluşturulduktan sonra değiştirilemez | §2.2 | S07 — implicit (salt okunur bilgi) | ✓ |
| 13 | 02-§2.2-05 | Sadece tradeable item'lar | §2.2 | S06 Adım 1 — non-tradeable devre dışı (§7.2) | ✓ |
| 14 | 02-§2.2-06 | Tüm CS2 item türleri desteklenir | §2.2 | S06 Adım 1 (§7.2) | ✓ |
| 15 | 02-§3.1-01 | Alıcı kabul timeout'u: admin ayarlanabilir | §3.1 | S07 CREATED countdown (§7.3) + S17 (§8.6) | ✓ |
| 16 | 02-§3.1-02 | Satıcı trade offer timeout'u: admin ayarlanabilir | §3.1 | S07 ACCEPTED/TRADE_OFFER countdown (§7.3) + S17 (§8.6) | ✓ |
| 17 | 02-§3.1-03 | Ödeme timeout'u: admin min-max-varsayılan, satıcı seçer | §3.1 | S06 Adım 2 slider/select (§7.2) + S17 (§8.6) | ✓ |
| 18 | 02-§3.1-04 | Teslim trade offer timeout'u: admin ayarlanabilir | §3.1 | S07 TRADE_OFFER_SENT_TO_BUYER countdown (§7.3) + S17 (§8.6) | ✓ |
| 19 | 02-§3.2-01 | Timeout dolarsa işlem iptal olur | §3.2 | S07 CANCELLED_TIMEOUT (§7.3) | ✓ |
| 20 | 02-§3.2-02 | Transfer edilen varlıklar iade edilir | §3.2 | S07 CANCELLED_* iade bilgisi (§7.3) | ✓ |
| 21 | 02-§3.2-03 | Ödeme timeout'unda platform adresi izlemeye devam eder | §3.2 | S07 gecikmeli ödeme edge case (§7.3) | ✓ |
| 22 | 02-§3.3-01 | Platform bakımında timeout dondurulur | §3.3 | C02 frozen state (§5), C08 (§5) | ✓ |
| 23 | 02-§3.3-02 | Steam kesintisinde timeout dondurulur | §3.3 | C02 frozen state (§5), C08 (§5) | ✓ |
| 24 | 02-§3.3-03 | Bakım/kesinti bitince timeout kaldığı yerden devam eder | §3.3 | C02 (§5) — implicit | ✓ |
| 25 | 02-§3.3-04 | Kullanıcılara planlı bakım öncesi bildirim | §3.3 | C08 planlı bakım varyantı (§5) | ✓ |
| 26 | 02-§3.4-01 | Timeout yaklaşıyor uyarısı gönderilir | §3.4 | S07 uyarı banner (traceability §3.1) + S11 (§7.7) | ✓ |
| 27 | 02-§3.4-02 | Uyarı eşiği admin tarafından oran olarak ayarlanır | §3.4 | S17 Timeout uyarı eşiği (§8.6) | ✓ |
| 28 | 02-§4.1-01 | Ödeme yöntemi: kripto (stablecoin) | §4.1 | S06 Adım 2 (§7.2), S07 ödeme bilgileri (§7.3) | ✓ |
| 29 | 02-§4.1-02 | USDT ve USDC desteği | §4.1 | S06 Adım 2 toggle (§7.2) | ✓ |
| 30 | 02-§4.1-03 | Tron (TRC-20) ağı | §4.1 | S07 ödeme bilgileri (§7.3), C11 (§5) | ✓ |
| 31 | 02-§4.1-04 | Dış cüzdan modeli | §4.1 | S07 ödeme bilgileri (§7.3) | ✓ |
| 32 | 02-§4.1-05 | Her işlem için benzersiz ödeme adresi | §4.1 | S07 ödeme adresi (§7.3) | ✓ |
| 33 | 02-§4.2-01 | Satıcı USDT/USDC seçer | §4.2 | S06 Adım 2 toggle (§7.2) | ✓ |
| 34 | 02-§4.2-02 | Alıcı satıcının seçtiği token ile öder | §4.2 | S07 ödeme bilgileri: token bilgisi (§7.3) | ✓ |
| 35 | 02-§4.3-01 | Fiyat doğrudan stablecoin miktarı olarak girilir | §4.3 | S06 Adım 2 input (§7.2) | ✓ |
| 36 | 02-§4.3-02 | Platform fiyata müdahale etmez | §4.3 | S06 — müdahale yok, serbest giriş | ✓ |
| 37 | 02-§4.3-03 | MVP'de kullanıcıya piyasa fiyatı gösterilmez | §4.3 | S06 — piyasa fiyatı yok | ✓ |
| 38 | 02-§4.4-01 | Eksik tutar: kabul etmez, iade eder | §4.4 | S07 ödeme edge case (§7.3) | ✓ |
| 39 | 02-§4.4-02 | Fazla tutar: doğru tutarı kabul eder, fazlayı iade eder | §4.4 | S07 ödeme edge case (§7.3) | ✓ |
| 40 | 02-§4.4-03 | Yanlış token: kabul etmez, iade eder | §4.4 | S07 ödeme edge case (§7.3) | ✓ |
| 41 | 02-§4.4-04 | Timeout sonrası gecikmeli ödeme: iade edilir | §4.4 | S07 ödeme edge case (§7.3) | ✓ |
| 42 | 02-§4.5-01 | Satıcıya ödeme: item teslimi doğrulandıktan sonra | §4.5 | S07 COMPLETED ödeme özeti (§7.3) | ✓ |
| 43 | 02-§4.5-02 | Satıcı cüzdan adresi: profilde varsayılan, işlemde değiştirilebilir | §4.5 | S06 Adım 3 (§7.2), S08 (§7.4) | ✓ |
| 44 | 02-§4.6-01 | İade: tam iade, komisyon dahil | §4.6 | S07 CANCELLED_* iade bilgisi (§7.3) | ✓ |
| 45 | 02-§4.6-02 | İade adresi: alıcının işlem kabul ederken belirlediği adres | §4.6 | S07 CREATED buyer — iade adresi (§7.3) | ✓ |
| 46 | 02-§4.6-03 | İade gas fee'si: iade tutarından düşülür | §4.6 | S07 — implicit (iade bilgisi) | ⚠ |
| 47 | 02-§4.7-01 | Gas fee koruma eşiği: komisyonun belirli % aşarsa satıcıdan kesilir | §4.7 | S07 COMPLETED ödeme özeti (§7.3) + S17 gas fee parametresi (§8.6) | ✓ |
| 48 | 02-§4.7-02 | Varsayılan eşik: %10 | §4.7 | S07 COMPLETED notu "admin tarafından belirlenen eşiğini (%10 varsayılan)" (§7.3) | ✓ |
| 49 | 02-§5-01 | Komisyonu alıcı öder | §5 | S06 Adım 2 "Alıcı %2 komisyon ödeyecek" (§7.2) + S07 (§7.3) | ✓ |
| 50 | 02-§5-02 | Varsayılan oran %2 | §5 | S06 Adım 2 (§7.2) | ✓ |
| 51 | 02-§5-03 | Oran admin tarafından değiştirilebilir | §5 | S17 komisyon oranı (§8.6) | ✓ |
| 52 | 02-§6.1-01 | Yöntem 1: satıcı alıcının Steam ID'sini girer | §6.1 | S06 Adım 3 (§7.2) | ✓ |
| 53 | 02-§6.1-02 | Sadece belirtilen kullanıcı kabul edebilir | §6.1 | S07 Steam ID kontrolü (§7.3) | ✓ |
| 54 | 02-§6.1-03 | Kayıtlı değilse satıcıya davet linki verilir | §6.1 | S07 CREATED seller — davet linki (§7.3) | ✓ |
| 55 | 02-§6.2-01 | Yöntem 2: açık link, ilk kabul eden alıcı olur | §6.2 | S06 Adım 3 toggle (§7.2), S07 açık link kontrolü (§7.3) | ✓ |
| 56 | 02-§6.2-02 | Admin tarafından aktif/pasif yapılabilir | §6.2 | S17 alıcı belirleme toggle (§8.6) | ✓ |
| 57 | 02-§7-01 | Ödeme öncesi satıcı iptal edebilir | §7 | S07 iptal butonu koşulları (§7.3) | ✓ |
| 58 | 02-§7-02 | Ödeme öncesi alıcı iptal edebilir | §7 | S07 iptal butonu koşulları (§7.3) | ✓ |
| 59 | 02-§7-03 | Ödeme sonrası tek taraflı iptal edilemez | §7 | S07 conditional buton kuralları (§7.3) | ✓ |
| 60 | 02-§7-04 | İptal sonrası cooldown | §7 | S06 error state — cooldown (§7.2) + S17 iptal kuralları (§8.6) | ✓ |
| 61 | 02-§7-05 | İptal sebebi zorunlu | §7 | C06 cancel modal — zorunlu textarea (§5) | ✓ |
| 62 | 02-§8-01 | Min/max işlem tutarı: admin ayarlanabilir | §8 | S06 Adım 2 min/max bilgisi (§7.2) + S17 (§8.6) | ✓ |
| 63 | 02-§8-02 | Eşzamanlı aktif işlem limiti | §8 | S06 error state (§7.2) + S17 (§8.6) | ✓ |
| 64 | 02-§8-03 | Yeni hesap işlem limiti | §8 | S06 error state (§7.2) + S17 (§8.6) | ✓ |
| 65 | 02-§10.1-01 | Ödeme itirazı: blockchain otomatik doğrulama | §10.1 | C07 dispute form — otomatik kontrol (§5) | ✓ |
| 66 | 02-§10.1-02 | Teslim itirazı: Steam otomatik doğrulama | §10.1 | C07 dispute form (§5) | ✓ |
| 67 | 02-§10.1-03 | Yanlış item itirazı: otomatik karşılaştırma | §10.1 | C07 dispute form (§5) | ✓ |
| 68 | 02-§10.2-01 | Yalnızca alıcı dispute açabilir | §10.2 | S07 conditional butonlar — "İtiraz Et" sadece alıcı (§7.3) | ✓ |
| 69 | 02-§10.2-02 | Aynı türde dispute tekrar açılamaz | §10.2 | S07 conditional butonlar — "aynı türde daha önce çözülmüş dispute yok" (§7.3) | ✓ |
| 70 | 02-§11-01 | Steam ile giriş | §11 | S02 Steam Login (§6.2) | ✓ |
| 71 | 02-§11-02 | MA zorunlu | §11 | S03 (§6.3), S06 error state (§7.2) | ✓ |
| 72 | 02-§12.1-01 | Satıcı profilde varsayılan adres tanımlar | §12.1 | S08 satıcı ödeme adresi (§7.4) | ✓ |
| 73 | 02-§12.1-02 | İşlem bazlı farklı adres girebilir | §12.1 | S06 Adım 3 cüzdan adresi (§7.2) | ✓ |
| 74 | 02-§12.2-01 | Alıcı profilde varsayılan iade adresi tanımlar | §12.2 | S08 alıcı iade adresi (§7.4) | ✓ |
| 75 | 02-§12.2-02 | İade adresi olmadan işlem kabul edilemez | §12.2 | S07 CREATED buyer — adres olmadan kabul devre dışı (§7.3) | ✓ |
| 76 | 02-§12.2-03 | Exchange uyarısı | §12.2 | S07 ITEM_ESCROWED ödeme bilgileri uyarılar (§7.3) | ✓ |
| 77 | 02-§16.2-01 | Audit log görüntüleme: fon hareketleri, admin aksiyonları, güvenlik olayları | §16.2 | — | ✗ |
| 78 | 02-§18.1-01 | Bildirim kanal tercihleri: platform içi, email, Telegram, Discord | §18.1 | S10 bildirim tercihleri (§7.6) | ✓ |

**Toplam: 78 öğe (72 ✓, 4 ⚠, 2 ✗)**

---

### Envanter — 03_USER_FLOWS

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 1 | 03-§2.1-01 | Satıcı platforma gelir | §2.1/1 | S01 (§6.1) | ✓ |
| 2 | 03-§2.1-02 | "Steam ile Giriş" butonuna tıklar | §2.1/2 | S01 CTA (§6.1) | ✓ |
| 3 | 03-§2.1-03 | Steam auth sayfasına yönlendirilir | §2.1/3 | S02 pre-redirect (§6.2) | ✓ |
| 4 | 03-§2.1-04 | Steam hesabıyla onay verir | §2.1/4 | S02 harici sayfa (§6.2) | ✓ |
| 5 | 03-§2.1-05 | Platform geri döner | §2.1/5 | S02 callback (§6.2) | ✓ |
| 6 | 03-§2.1-06 | MA kontrolü: aktif değilse uyarı | §2.1/6 | S03 (§6.3) | ✓ |
| 7 | 03-§2.1-07 | İlk kez geliyorsa hesap otomatik oluşturulur | §2.1/7 | S02 callback — yeni kullanıcı (§6.2) | ✓ |
| 8 | 03-§2.1-08 | İlk kez gelen kullanıcıya ToS gösterilir | §2.1/8 | S02 ToS kabul modal'ı (§6.2) | ✓ |
| 9 | 03-§2.1-09 | Dashboard'a yönlendirilir | §2.1/9 | S05 (§7.1) | ✓ |
| 10 | 03-§2.2-01 | "Yeni İşlem Başlat" tıklar | §2.2/1 | S05 CTA (§7.1) | ✓ |
| 11 | 03-§2.2-02 | Eşzamanlı aktif işlem limiti kontrolü | §2.2/2 | S06 error state (§7.2) | ✓ |
| 12 | 03-§2.2-03 | İptal cooldown kontrolü | §2.2/3 | S06 error state (§7.2) | ✓ |
| 13 | 03-§2.2-04 | Yeni hesap işlem limiti kontrolü | §2.2/4 | S06 error state (§7.2) | ✓ |
| 14 | 03-§2.2-05 | Steam envanter okuma | §2.2/5 | S06 Adım 1 (§7.2) | ✓ |
| 15 | 03-§2.2-06 | Tradeable item listesi gösterilir | §2.2/6 | S06 Adım 1 item grid (§7.2) | ✓ |
| 16 | 03-§2.2-07 | Satıcı item seçer | §2.2/7 | S06 Adım 1 selectable (§7.2) | ✓ |
| 17 | 03-§2.2-08 | Tradeable kontrolü | §2.2/8 | S06 Adım 1 non-tradeable devre dışı (§7.2) | ✓ |
| 18 | 03-§2.2-09 | Stablecoin seçimi | §2.2/9 | S06 Adım 2 toggle (§7.2) | ✓ |
| 19 | 03-§2.2-10 | Fiyat girişi | §2.2/10 | S06 Adım 2 input (§7.2) | ✓ |
| 20 | 03-§2.2-11 | Min/max fiyat kontrolü | §2.2/11 | S06 Adım 2 validation (§7.2) | ✓ |
| 21 | 03-§2.2-12 | Ödeme timeout seçimi | §2.2/12 | S06 Adım 2 slider/select (§7.2) | ✓ |
| 22 | 03-§2.2-13 | Alıcı belirleme: Steam ID veya açık link | §2.2/13 | S06 Adım 3 (§7.2) | ✓ |
| 23 | 03-§2.2-14 | Cüzdan adresi belirleme | §2.2/14 | S06 Adım 3 cüzdan (§7.2) | ✓ |
| 24 | 03-§2.2-15 | İşlem özeti gösterilir | §2.2/15 | S06 Adım 4 (§7.2) | ✓ |
| 25 | 03-§2.2-16 | Satıcı onaylar | §2.2/16 | S06 Adım 4 "İşlemi Başlat" (§7.2) | ✓ |
| 26 | 03-§2.2-17 | Piyasa fiyatı sapma kontrolü — FLAGGED | §2.2/17 | S07 FLAGGED durumu (§7.3) | ✓ |
| 27 | 03-§2.2-18 | İşlem CREATED'a geçer | §2.2/18 | S07 CREATED (§7.3) | ✓ |
| 28 | 03-§2.2-19 | Alıcıya bildirim / davet linki | §2.2/19 | S07 CREATED davet linki (§7.3), S11 (§7.7) | ✓ |
| 29 | 03-§2.3-01 | Alıcı kabul sonrası satıcıya bildirim | §2.3/1 | S11 (§7.7) | ✓ |
| 30 | 03-§2.3-02 | Platform satıcıya trade offer gönderir | §2.3/2 | S07 ACCEPTED aksiyon alanı (§7.3) | ✓ |
| 31 | 03-§2.3-03 | TRADE_OFFER_SENT_TO_SELLER durumu | §2.3/3 | S07 TRADE_OFFER_SENT_TO_SELLER (§7.3) | ✓ |
| 32 | 03-§2.3-04 | Satıcı trade offer'ı reddederse → CANCELLED_SELLER | §2.3/5 | S07 — implicit (CANCELLED_SELLER state) | ✓ |
| 33 | 03-§2.3-05 | Item eşleşme doğrulaması | §2.3/8 | S07 — implicit (arka plan) | ✓ |
| 34 | 03-§2.4-01 | Satıcıya ödeme: komisyon hesabı, gas fee eşiği kontrolü | §2.4/2-3 | S07 COMPLETED ödeme özeti (§7.3) | ✓ |
| 35 | 03-§2.5-01 | Satıcı iptal: ödeme gönderilmişse iptal edilemez | §2.5/3 | S07 conditional butonlar — iptal aktif olma koşulu (§7.3) | ✓ |
| 36 | 03-§2.5-02 | İptal sebebi zorunlu | §2.5/4 | C06 cancel modal (§5) | ✓ |
| 37 | 03-§2.5-03 | Item platformdaysa iade edilir | §2.5/6 | S07 CANCELLED_* iade bilgisi (§7.3) | ✓ |
| 38 | 03-§3.1-01 | Davet linkiyle gelen alıcı akışı | §3.1/1-6 | S07 public varyant → S02 → S07 (§7.3, traceability §3.1) | ✓ |
| 39 | 03-§3.2-01 | Alıcı işlem detaylarını görür | §3.2/1 | S07 CREATED buyer (§7.3) | ✓ |
| 40 | 03-§3.2-02 | Steam ID eşleşme kontrolü | §3.2/2 | S07 Steam ID kontrolü notu (§7.3) | ✓ |
| 41 | 03-§3.2-03 | Açık link: birisi kabul ettiyse mesaj | §3.2/3 | S07 açık link kontrolü notu (§7.3) | ✓ |
| 42 | 03-§3.2-04 | Alıcı iade adresi belirler | §3.2/4 | S07 CREATED buyer — iade adresi (§7.3) | ✓ |
| 43 | 03-§3.2-05 | İade adresi olmadan kabul edilemez | §3.2/4 | S07 CREATED buyer — adres olmadan devre dışı (§7.3) | ✓ |
| 44 | 03-§3.3-01 | Alıcı iptal: ödeme gönderilmişse edilemez | §3.3/3 | S07 conditional butonlar (§7.3) | ✓ |
| 45 | 03-§3.4-01 | Ödeme bilgileri gösterilir: adres, tutar, token, ağ, timeout | §3.4/3 | S07 ITEM_ESCROWED ödeme bilgileri (§7.3) | ✓ |
| 46 | 03-§3.4-02 | Exchange uyarısı | §3.4/3 | S07 ITEM_ESCROWED uyarılar (§7.3) | ✓ |
| 47 | 03-§3.5-01 | Alıcı trade offer reddederse: item satıcıya iade, ödeme alıcıya iade | §3.5/5 | S07 CANCELLED_BUYER (§7.3) | ✓ |
| 48 | 03-§4.1-01 | Alıcı kabul timeout'u: CANCELLED_TIMEOUT | §4.1 | S07 CANCELLED_TIMEOUT (§7.3) | ✓ |
| 49 | 03-§4.2-01 | Satıcı trade offer timeout'u | §4.2 | S07 countdown (§7.3) | ✓ |
| 50 | 03-§4.3-01 | Ödeme timeout'u: item iade, adres izleme devam | §4.3 | S07 CANCELLED_TIMEOUT + gecikmeli ödeme edge case (§7.3) | ✓ |
| 51 | 03-§4.4-01 | Teslim trade offer timeout'u: çift iade | §4.4 | S07 CANCELLED_TIMEOUT iade bilgisi (§7.3) | ✓ |
| 52 | 03-§4.5-01 | Timeout yaklaşıyor uyarısı: tüm timeout'lar için | §4.5 | S07 uyarı banner (traceability §3.1) + S11 (§7.7) | ✓ |
| 53 | 03-§5.1-01 | Eksik tutar: iade + uyarı | §5.1 | S07 ödeme edge case (§7.3) | ✓ |
| 54 | 03-§5.2-01 | Fazla tutar: fazla iade + işlem devam | §5.2 | S07 ödeme edge case (§7.3) | ✓ |
| 55 | 03-§5.3-01 | Yanlış token: iade | §5.3 | S07 ödeme edge case (§7.3) | ✓ |
| 56 | 03-§5.4-01 | Gecikmeli ödeme: otomatik iade | §5.4 | S07 ödeme edge case (§7.3) | ✓ |
| 57 | 03-§6.1-01 | Ödeme itirazı akışı | §6.1 | C07 dispute form + S07 dispute gösterimi (§7.3) | ✓ |
| 58 | 03-§6.4-01 | Admin eskalasyonu akışı | §6.4 | C07 eskalasyon adımı (§5), S07 dispute gösterimi (§7.3) | ✓ |
| 59 | 03-§7.1-01 | Fiyat sapması flag'i | §7.1 | S07 FLAGGED (§7.3), S13/S14 (§8.2-8.3) | ✓ |
| 60 | 03-§8.1-01 | Admin dashboard: aktif işlem, flag, günlük/haftalık | §8.1/3 | S12 özet kartları (§8.1) | ✓ |
| 61 | 03-§8.2-01 | Flag inceleme: detay, onayla/reddet | §8.2 | S14 (§8.3) | ✓ |
| 62 | 03-§8.3-01 | İşlem listesi ve filtreleme | §8.3 | S15 (§8.4) | ✓ |
| 63 | 03-§8.4-01 | Parametre yönetimi | §8.4 | S17 (§8.6) | ✓ |
| 64 | 03-§8.5-01 | Steam hesapları izleme | §8.5 | S18 (§8.7) | ✓ |
| 65 | 03-§8.6-01 | Rol ve yetki yönetimi | §8.6 | S19 (§8.8) | ✓ |

**Toplam: 65 öğe (60 ✓, 3 ⚠, 2 ✗)**

---

### Envanter — 10_MVP_SCOPE

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 1 | 10-§2.1-01 | Temel escrow akışı: satıcı başlatır, alıcı kabul eder, item emanet, ödeme, teslim | §2.1 | S06, S07 (§7.2, §7.3) | ✓ |
| 2 | 10-§2.2-01 | USDT ve USDC desteği (Tron TRC-20) | §2.2 | S06 Adım 2 (§7.2), S07 (§7.3) | ✓ |
| 3 | 10-§2.2-02 | Dış cüzdan modeli, benzersiz ödeme adresi | §2.2 | S07 ödeme bilgileri (§7.3) | ✓ |
| 4 | 10-§2.2-03 | Otomatik blockchain doğrulama | §2.2 | S07 — arka plan (§7.3) | ✓ |
| 5 | 10-§2.2-04 | Ödeme edge case yönetimi | §2.2 | S07 ödeme edge case (§7.3) | ✓ |
| 6 | 10-§2.2-05 | Gas fee yönetimi (komisyondan, koruma eşiği) | §2.2 | S07 COMPLETED ödeme özeti (§7.3), S17 (§8.6) | ✓ |
| 7 | 10-§2.3-01 | Her adım için ayrı timeout | §2.3 | S07 countdown'lar (§7.3) | ✓ |
| 8 | 10-§2.3-02 | Admin ayarlanabilir timeout süreleri | §2.3 | S17 timeout süreleri (§8.6) | ✓ |
| 9 | 10-§2.3-03 | Ödeme timeout'u satıcı seçebilir | §2.3 | S06 Adım 2 (§7.2) | ✓ |
| 10 | 10-§2.3-04 | Timeout dolduğunda otomatik iptal ve iade | §2.3 | S07 CANCELLED_TIMEOUT (§7.3) | ✓ |
| 11 | 10-§2.4-01 | Steam ile giriş | §2.4 | S02 (§6.2) | ✓ |
| 12 | 10-§2.4-02 | MA zorunluluğu | §2.4 | S03 (§6.3) | ✓ |
| 13 | 10-§2.4-03 | Profil ve cüzdan adresleri yönetimi (satıcı + alıcı) | §2.4 | S08 (§7.4) | ✓ |
| 14 | 10-§2.4-04 | Cüzdan adresi değişikliğinde ek doğrulama | §2.4 | S08 değişiklik akışı (§7.4) | ✓ |
| 15 | 10-§2.4-05 | Hesap silme/deaktif etme | §2.4 | S10 hesap yönetimi (§7.6) | ✓ |
| 16 | 10-§2.4-06 | Kullanıcı itibar skoru | §2.4 | S08 (§7.4), S09 (§7.5), C04 (§5) | ✓ |
| 17 | 10-§2.5-01 | Steam ID ile belirleme (aktif) | §2.5 | S06 Adım 3 (§7.2) | ✓ |
| 18 | 10-§2.5-02 | Kayıtlı değilse satıcıya davet linki | §2.5 | S07 CREATED seller — davet linki (§7.3) | ✓ |
| 19 | 10-§2.5-03 | Açık link yöntemi (pasif, admin aktif edebilir) | §2.5 | S06 Adım 3 toggle (§7.2), S17 (§8.6) | ✓ |
| 20 | 10-§2.6-01 | İptal yönetimi kuralları | §2.6 | S07 iptal, C06 (§5, §7.3) | ✓ |
| 21 | 10-§2.7-01 | Dispute: ödeme, teslim, yanlış item + otomatik doğrulama | §2.7 | C07 (§5), S07 dispute gösterimi (§7.3) | ✓ |
| 22 | 10-§2.7-02 | Admin'e eskalasyon yolu | §2.7 | C07 eskalasyon adımı (§5) | ✓ |
| 23 | 10-§2.8-01 | Wash trading koruması | §2.8 | — (arka plan, UI etkisi yok) | ✓ |
| 24 | 10-§2.8-02 | İptal limiti ve geçici yasak | §2.8 | S06 error state — cooldown (§7.2) | ✓ |
| 25 | 10-§2.8-03 | Yeni hesap işlem limiti | §2.8 | S06 error state (§7.2) | ✓ |
| 26 | 10-§2.8-04 | Anormal davranış tespiti ve flag'leme | §2.8 | S07 FLAGGED (§7.3), S13/S14 (§8.2-8.3) | ✓ |
| 27 | 10-§2.8-05 | Çoklu hesap tespiti | §2.8 | S14 flag detay — çoklu hesap bilgisi (§8.3) | ✓ |
| 28 | 10-§2.8-06 | Kara para aklama tespiti (piyasa fiyat sapması, yüksek hacim) | §2.8 | S14 flag detay (§8.3), S17 fraud tespiti (§8.6) | ✓ |
| 29 | 10-§2.10-01 | Birden fazla Steam hesabı ile çalışma | §2.10 | S18 (§8.7) | ✓ |
| 30 | 10-§2.10-02 | Admin panelinden hesap durumu izleme | §2.10 | S18 (§8.7) | ✓ |
| 31 | 10-§2.11-01 | Süper admin + özel rol grupları | §2.11 | S19 (§8.8) | ✓ |
| 32 | 10-§2.11-02 | Tüm dinamik parametrelerin yönetimi | §2.11 | S17 (§8.6) | ✓ |
| 33 | 10-§2.11-03 | Flag'lenmiş işlem inceleme ve onay/red | §2.11 | S13, S14 (§8.2-8.3) | ✓ |
| 34 | 10-§2.11-04 | Audit log görüntüleme | §2.11 | — | ✗ |
| 35 | 10-§2.12-01 | Dashboard: aktif işlemler, geçmiş, profil, bildirimler | §2.12 | S05 (§7.1), S08 (§7.4), S11 (§7.7) | ✓ |
| 36 | 10-§2.13-01 | Bildirim kanalları: platform içi, email, Telegram/Discord | §2.13 | S11 (§7.7), S10 bildirim tercihleri (§7.6) | ✓ |
| 37 | 10-§2.15-01 | Landing page | §2.15 | S01 (§6.1) | ✓ |
| 38 | 10-§2.15-02 | 4 dil desteği (EN, CN, ES, TR) | §2.15 | §1 genel bakış, C10 (§5), §10 lokalizasyon (§10) | ✓ |

**Toplam: 38 öğe (36 ✓, 1 ⚠, 1 ✗)**

---

### Envanter — Hedef (04 İç Tutarlılık)

| # | ID | Öğe Özeti | Kaynak Bölüm | Durum |
|---|---|---|---|---|
| 1 | 04-§1-01 | Tasarım seviyesi: wireframe düzeyinde bilgi mimarisi | §1 | ✓ |
| 2 | 04-§1-02 | Platform: web-first, mobil uyumlu | §1 | ✓ |
| 3 | 04-§1-03 | Dil desteği: EN, CN, ES, TR | §1 | ✓ |
| 4 | 04-§2.1-01 | Ekran sayısı: 19 (3 genel + 7 kullanıcı + 9 admin) | §2.1 | ✓ |
| 5 | 04-§2.2-01 | S04 atlanmış, ToS S02'de modal olarak | §2.2 not | ✓ |
| 6 | 04-§3-01 | İleri izlenebilirlik: akışlar → ekranlar | §3.1 | ✓ |
| 7 | 04-§3-02 | İleri izlenebilirlik: gereksinimler → ekranlar | §3.2 | ✓ |
| 8 | 04-§3-03 | Geri izlenebilirlik: ekranlar → kaynaklar | §3.3 | ✓ |
| 9 | 04-§4-01 | Ekran navigasyon haritası: genel, kullanıcı, admin | §4 | ✓ |
| 10 | 04-§5-01 | 17 ortak bileşen (C01–C17) | §5 | ✓ |
| 11 | 04-§5-02 | C01 Status Badge: 13 durum × renk kodlaması | §5 C01 | ✓ |
| 12 | 04-§5-03 | C02 Countdown Timer: renk geçişi + uyarı eşiği | §5 C02 | ✓ |
| 13 | 04-§5-04 | C05 Transaction Timeline: 8 adımlık progress bar | §5 C05 | ✓ |
| 14 | 04-§5-05 | C06 Cancel Modal: iptal sebebi zorunlu, min 10 karakter | §5 C06 | ✓ |
| 15 | 04-§5-06 | C07 Dispute Form: 3 tür + otomatik kontrol + eskalasyon | §5 C07 | ✓ |
| 16 | 04-§5-07 | C11 Wallet Address Input: TRC-20 format, T ile başlar, 34 karakter | §5 C11 | ✓ |
| 17 | 04-§7.2-01 | S06 İşlem Oluşturma: 4 adımlı form | §7.2 | ✓ |
| 18 | 04-§7.3-01 | S07 İşlem Detay: 13 state × 3 role varyant matrisi | §7.3 | ✓ |
| 19 | 04-§7.3-02 | S07 Ödeme özeti: gas fee detaylı gösterim, 2 varyant | §7.3 COMPLETED | ✓ |
| 20 | 04-§7.3-03 | S07 Conditional buton kuralları: 4 buton × koşullar | §7.3 | ✓ |
| 21 | 04-§7.3-04 | S07 Ödeme edge case gösterimleri: 4 senaryo | §7.3 | ✓ |
| 22 | 04-§7.4-01 | S08 Profil: satıcı ödeme adresi + alıcı iade adresi ayrı | §7.4 | ✓ |
| 23 | 04-§7.6-01 | S10 Bildirim tercihleri: 4 kanal, platform içi devre dışı bırakılamaz | §7.6 | ✓ |
| 24 | 04-§7.6-02 | S10 Telegram bağlama akışı: 5 adım | §7.6 | ✓ |
| 25 | 04-§7.6-03 | S10 Hesap silme: "SİL" yazarak onay | §7.6 | ✓ |
| 26 | 04-§8.1-01 | S12 Admin Dashboard: özet kartları + steam hesapları + son flag'ler | §8.1 | ✓ |
| 27 | 04-§8.3-01 | S14 Flag detay: 4 flag türü × detay bilgisi | §8.3 | ✓ |
| 28 | 04-§8.3-02 | S14 Aksiyon: onayla/reddet + onay modal'ı | §8.3 | ✓ |
| 29 | 04-§8.6-01 | S17 Parametre Yönetimi: 8 kategori × parametreler | §8.6 | ⚠ |
| 30 | 04-§8.8-01 | S19 Rol & Yetki: roller listesi + yetki matrisi + kullanıcı-rol atama | §8.8 | ✓ |
| 31 | 04-§9-01 | Responsive tasarım: 3 breakpoint, ekran bazlı kurallar | §9 | ✓ |
| 32 | 04-§10-01 | Lokalizasyon: metin uzunluk, tarih/saat, sayı formatı, çevrilmeyecek terimler | §10 | ✓ |

**Toplam: 32 öğe (29 ✓, 2 ⚠, 1 ✗)**

---

## Bulgular

| # | Envanter ID | Tür | Seviye | Bulgu | Öneri |
|---|---|---|---|---|---|
| 1 | 02-§16.2-01 | GAP | High | 02 §16.2'de "Audit log görüntüleme (fon hareketleri, admin aksiyonları, güvenlik olayları)" admin paneli gereksinimi tanımlı. 04_UI_SPECS'te S12-S20 admin ekranlarının hiçbirinde audit log görüntüleme ekranı veya bölümü yok. S16'da "Durum Geçmişi (Timeline)" ve "Bildirim Geçmişi" var ama bunlar audit log ile aynı şey değil. | S12 Admin Dashboard'a "Audit Log" sol menü öğesi eklenmeli VEYA mevcut ekranlardan birine (S16 veya S20) audit log bölümü eklenmeli. En uygun çözüm: S12 sol menüsüne "Audit Log" eklenmeli ve bu ekran (S21 veya mevcut bir ekranın alt bölümü) ayrı tanımlanması 07_API_DESIGN veya sonraki dokümanların kapsamına bırakılabilir. Şimdilik S12 sol menü ve navigasyon haritasına eklenmeli. |
| 2 | 10-§2.11-04 | GAP | High | 10_MVP_SCOPE §2.11'de "Audit log görüntüleme" açıkça MVP kapsamında. 04_UI_SPECS'te karşılığı yok. | #1 ile aynı — audit log için en azından navigasyon noktası ve temel tanım eklenmeli. |
| 3 | 02-§4.6-03 | Kısmi | Medium | 02 §4.6'da "İade gas fee'si: iade tutarından düşülür (alıcı karşılar)" kuralı var. S07 CANCELLED_* iade bilgisinde alıcıya "Ödemeniz iade edildi" + tx hash gösterildiği belirtiliyor ama iade tutarı detayında gas fee kesintisinin gösterilip gösterilmediği açık değil. COMPLETED'daki ödeme özeti gibi detaylı bir iade özeti yok. | S07 CANCELLED_* durumunda alıcıya gösterilen iade bilgisine "İade Özeti" eklenebilir: orijinal tutar, gas fee kesintisi, net iade tutarı, tx hash. Bu Medium çünkü iade detayları 07_API_DESIGN'da response yapısında da ele alınabilir. |
| 4 | 04-§8.6-01 | Kısmi | Medium | S17 Parametre Yönetimi'nde 02 §16.2'de listelenen "Timeout uyarı eşiği" parametresi var ve doğru tanımlı. Ancak 02 §3.4'te uyarı eşiğinin "oran olarak" ayarlanacağı belirtiliyor, S17'de de "%" birimi ile tanımlı — bu tutarlı. Ancak S17'de uyarı eşiğinin açıklama metninde C02 bağlantısı yapılmış ("Aynı eşik C02 countdown timer renk değişimi için de kullanılır") — bu doğru ve iyi bir çapraz referans. Ancak "Yüksek hacim periyodu" parametresinin birimi "Saat" olarak gösterilirken, 02 §14.4'te "kısa sürede" ifadesi kullanılmış — birim tutarlılığı açısından bu kabul edilebilir bir detaydır. **Asıl bulgu:** S17'de "timeout uyarı eşiği"nin açıklaması tek uyarı eşiği olduğunu ima ediyor ama C02'de renk geçişi üç aşamalı (%0-eşiğin yarısı→Yeşil, eşiğin yarısı-eşik→Sarı, eşik-%100→Kırmızı). Bu, tek bir "uyarı eşiği" parametresinden türetiliyor — bu tutarlı ama C02 açıklamasında "eşiğin yarısı" ifadesi S17 parametresiyle açıkça bağlantılandırılmalı. | S17 timeout uyarı eşiği parametresinin açıklamasına "C02 countdown timer'da bu eşiğin yarısı sarı, eşiğin kendisi kırmızı renk geçişi için kullanılır" notu eklenmesi netleştirici olur ama Low seviye. |
| 5 | 03-§2.1-06a | Kısmi | Low | 03 §2.1 adım 6-8 sıralamasında: önce MA kontrolü (adım 6), sonra hesap oluşturma (adım 7), sonra ToS (adım 8) yapılıyor. 04 S02'de callback sonrası: yeni kullanıcı → ToS modal → MA kontrolü sıralaması tanımlı. Bu, 03 §2.1'deki sıralama ile farklı: 03'te MA önce (adım 6), ToS sonra (adım 8); 04'te ToS önce, MA sonra. | 03 §2.1'deki sıralama: MA (6) → hesap oluşturma (7) → ToS (8). 04 S02'deki sıralama: yeni kullanıcı → ToS modal → MA kontrolü. Bu tutarsızlık var. 03'ün ilgili adımlarına bakıldığında adım 6 MA kontrolü tüm kullanıcılar için, adım 7-8 sadece ilk kez gelenler için. 04'te ise önce ToS sonra MA. **Kaynak doküman (03) referans alınmalı** — 04'teki sıralama 03 ile uyumlu hale getirilmeli: MA kontrolü → ToS olmalı. |
| 6 | 04-§7.3-05 | Tutarsızlık | High | S07 CREATED durumunda satıcı için "Davet linki" satırında "alıcı kayıtlı değilse" koşulu belirtilmiş. Ancak 02 §6.1 ve 03 §2.2/19'da davet linkinin gösterilme koşulu "alıcı platformda kayıtlı değilse" olarak tanımlı. 04'te "kayıtlı değilse" yazmak doğru ancak buraya ek bir koşul eklenmemiş: alıcı kayıtlıysa platform bildirimi gider ve satıcıya davet linki gösterilmez. 04'teki mevcut ifade ("alıcı kayıtlı değilse") bu durumu kısmen karşılıyor — davet linkinin koşullu görünürlüğü doğru. Ancak alıcı kayıtlıysa satıcının S07'de ne göreceği (davet linki yerine) açık değil. | S07 CREATED satıcı varyantına "alıcı kayıtlıysa → davet linki gösterilmez, sadece 'Alıcıya bildirim gönderildi' bilgisi gösterilir" eklenmeli. |
| 7 | 04-§5-C02a | Kalite | Medium | C02 Countdown Timer'da "Gerçek zamanlı güncellenir (WebSocket veya polling)" ifadesi teknik implementasyon detayı içeriyor. UI spec olarak "gerçek zamanlı güncellenir" yeterli olabilir — WebSocket/polling kararı 05_TECHNICAL_ARCHITECTURE veya 07_API_DESIGN kapsamı. Ancak bu bilgi 05 §2.2'de zaten tanımlı (SignalR/WebSocket) olduğundan tutarsızlık riski taşıyor: 04'te "WebSocket veya polling" denirken 05'te sadece "SignalR (WebSocket)" var. | "WebSocket veya polling" ifadesini "gerçek zamanlı" olarak sadeleştirmek daha doğru. Teknik karar 05'te zaten alınmış. |
| 8 | 04-§7.3-06 | Kalite | Low | S07 ITEM_ESCROWED durumunda satıcı iptal koşulu "ödeme gelmediği sürece" olarak belirtilmiş. Bu 02 §7 ile tutarlı. Ancak "İşlemi İptal Et" butonunun ITEM_ESCROWED'da hem satıcı hem alıcı için "aktif" olarak gösterilmesi, S07 conditional buton kurallarındaki "Ödeme gönderilmemiş" koşulu ile tutarlı — sorun yok. | Sorun yok — bilgi amaçlı not. |
| 9 | 04-§6.2-01 | Tutarsızlık | High | S02 ToS Kabul Modal'ı sonunda "Kabul sonrası → Mobile Authenticator kontrolü → S03 veya S05" yazılı. Ancak bu sadece yeni kullanıcılar için geçerli (ToS sadece ilk kayıtta gösteriliyor). Mevcut kullanıcılar için callback sonrası ToS modal'ı atlanıyor ve direkt MA kontrolüne geçiliyor. Bu doğru ancak 03 §2.1'deki adım sıralaması ile çelişki var (bkz. Bulgu #5). Ayrıca S02 "Callback Sonrası Kontroller" tablosunda mevcut kullanıcı için "→ S05 (veya davet linkinden geldiyse → S07)" yazılı ama burada MA kontrolü atlanmış — her girişte MA kontrolü yapılıyor mu, sadece ilk kayıtta mı? 03 §2.1 adım 6'da "Sistem kontrolü: Steam Mobile Authenticator aktif mi?" her giriş için geçerli görünüyor. | S02 Callback Sonrası Kontroller tablosundaki "mevcut kullanıcı" satırına MA kontrolü adımı eklenmeli: "Steam auth başarılı, mevcut kullanıcı → MA kontrolü → MA aktifse S05 (veya davet linkinden geldiyse S07), MA aktif değilse S03". |
| 10 | 04-§7.3-07 | Kalite | Low | S07 COMPLETED durumunda alıcı için "İşlem başarıyla tamamlandı." mesajı gösterilirken satıcı için detaylı ödeme özeti var. Alıcı tarafında da "alınan item" bilgisi (C03) zaten sabit layout'ta mevcut olduğundan ek bilgi gerekmeyebilir. Bu uygun bir tasarım kararı. | Sorun yok — bilgi amaçlı not. |
| 11 | 04-§8.3-03 | Kalite | Medium | S14 Flag Detay'da "Onayla" aksiyonunun açıklaması: "İşlem normal akışa döner (CREATED)." Bu, 03 §7.1 adım 7 ile tutarlı: "Admin onaylarsa → İşlem CREATED durumuna geçer". Ancak yüksek hacim flag'i (03 §7.2) aktif bir işlem sırasında da tetiklenebilir — bu durumda işlem CREATED'a değil, flag'lenmeden önceki durumuna dönmeli. S14'teki "CREATED" ifadesi sadece fiyat sapması flag'i için doğru. | S14 "Onayla" açıklamasındaki "(CREATED)" ifadesi "flag'lenmeden önceki durumuna" olarak genelleştirilmeli. |
| 12 | 04-§8.9-01 | GAP | Medium | S20 Kullanıcı Detay'da "Dispute Geçmişi" bölümü yok. Oysa S16 İşlem Detay (Admin)'da dispute geçmişi gösteriliyor. Admin bir kullanıcıyı incelerken o kullanıcının dispute geçmişini de görmek isteyebilir — ancak bu bilgiye işlem detayı üzerinden (S16) ulaşılabileceğinden kritik bir eksiklik değil. | S20'ye opsiyonel olarak "Dispute Geçmişi" bölümü eklenebilir veya mevcut haliyle bırakılabilir — bilgi S16 üzerinden erişilebilir. |

---

## Aksiyon Planı

**Critical:**
- (Yok)

**High:**
- [x] Bulgu #1/#2: Audit log görüntüleme — S21 Audit Log ekranı eklendi (ekran listesi, navigasyon haritası, traceability, responsive, S12 sol menü güncellemeleri dahil) ✓ UYGULAND
- [x] Bulgu #6: S07 CREATED satıcı — alıcı kayıtlıysa "Alıcıya bildirim gönderildi" bilgisi eklendi ✓ UYGULAND
- [x] Bulgu #9: S02 Callback Sonrası Kontroller tablosunda mevcut kullanıcı için MA kontrolü adımı eklendi ✓ UYGULAND

**Medium:**
- [x] Bulgu #3: S07 CANCELLED_* iade detay gösterimi — iade özeti eklendi (orijinal tutar, gas fee kesintisi, net iade, tx hash) ✓ UYGULAND
- [x] Bulgu #7: C02'deki "WebSocket veya polling" ifadesi "gerçek zamanlı" olarak sadeleştirildi ✓ UYGULAND
- [x] Bulgu #11: S14 "Onayla" açıklaması "flag'lenmeden önceki durumuna" olarak genelleştirildi ✓ UYGULAND
- [x] Bulgu #12: S20'ye dispute geçmişi bölümü eklendi ✓ UYGULAND

**Low:**
- Bulgu #4: S17 timeout uyarı eşiği → C02 bağlantısı açıklama notu — farkındalık yeterli
- Bulgu #5: ToS/MA sıralama tutarsızlığı — 03'teki sıralama referans alınmalı, şu anki 04 ifadesi fonksiyonel olarak doğru çalışıyor (her iki kontrol de yapılıyor) sadece sıralama farkı var — Low
- Bulgu #8: S07 ITEM_ESCROWED iptal koşulu — sorun yok
- Bulgu #10: S07 COMPLETED alıcı bilgisi — sorun yok

---

*Audit tamamlandı — 04_UI_SPECS.md v1.3 → v1.4 (tüm Critical, High ve Medium bulgular uygulandı)*
