# Audit Raporu — 05_TECHNICAL_ARCHITECTURE.md

**Tarih:** 2026-03-16
**Hedef:** 05 — Technical Architecture (v1.3)
**Baglam:** 01_PROJECT_VISION.md, 02_PRODUCT_REQUIREMENTS.md, 03_USER_FLOWS.md, 04_UI_SPECS.md, 10_MVP_SCOPE.md
**Odak:** Tam denetim

---

## Envanter Ozeti

| Kaynak | Toplam Oge | ✓ | ⚠ | ✗ |
|---|---|---|---|---|
| 01 | 12 | 12 | 0 | 0 |
| 02 | 52 | 44 | 6 | 2 |
| 03 | 38 | 33 | 4 | 1 |
| 04 | 14 | 12 | 2 | 0 |
| 10 | 20 | 19 | 1 | 0 |
| Hedef (ic) | 35 | 30 | 4 | 1 |
| **Toplam** | **171** | **150** | **17** | **4** |

---

## Envanter ve Eslestirme Detayi

### Envanter — 01_PROJECT_VISION

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 1 | 01-§1-01 | Skinora: CS2 item ticaretinde guvenli escrow | §1 | §1 | ✓ |
| 2 | 01-§1-02 | Marketplace degil, escrow aracisi | §1 | §1 | ✓ |
| 3 | 01-§3.3-01 | Aktorler: Satici, Alici, Platform, Admin | §3.3 | §3.1, §6.2 | ✓ |
| 4 | 01-§4.1-01 | Guvenlik: Alici-satici birbirine guvenmek zorunda degil | §4.1 | §6, §3.4 | ✓ |
| 5 | 01-§4.1-02 | Otomasyon: Odeme ve teslim dogrulama otomatik | §4.1 | §3.3, §4 | ✓ |
| 6 | 01-§4.1-03 | Izlenebilirlik: Her islem kaydedilir, audit trail | §4.1 | §5.4 | ✓ |
| 7 | 01-§5.1-01 | Gelir: %2 komisyon, alicidan alinir | §5.1 | Dokumanda dolayali referans | ✓ |
| 8 | 01-§6.1-01 | MVP: CS2 item, Tron TRC-20, USDT/USDC, web | §6.1 | §2.1, §3.3, §2.3 | ✓ |
| 9 | 01-§6.2-01 | Orta vadeli: Diger Steam oyunlari, mobil | §6.2 | Kapsam disi (MVP mimari) | ✓ |
| 10 | 01-§8-01 | Guvenlik onceliklidir | §8 | §6, §3.4, §3.5 | ✓ |
| 11 | 01-§8-02 | Otomasyon esastir | §8 | §4, §5 | ✓ |
| 12 | 01-§8-03 | Platform tarafsizdir, fiyata mudahale etmez | §8 | §3.1 (modulsacak yapilarla tutarli) | ✓ |

**Toplam: 12 oge (12 ✓, 0 ⚠, 0 ✗)**

---

### Envanter — 02_PRODUCT_REQUIREMENTS

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 1 | 02-§2.1-01 | Islem olusturma: Satici item secer, stablecoin belirler, fiyat ve timeout girer | §2.1 | §4.1 (CREATED state) | ✓ |
| 2 | 02-§2.1-02 | Alici kabulu: Alici detaylari gorur ve kabul eder | §2.1 | §4.1 (ACCEPTED state) | ✓ |
| 3 | 02-§2.1-03 | Item emaneti: Platform trade offer gonderir, satici kabul eder | §2.1 | §3.2, §4.1 (ITEM_ESCROWED) | ✓ |
| 4 | 02-§2.1-04 | Odeme: Benzersiz odeme adresi uretilir, alici tutar gonderir | §2.1 | §3.3 (Blockchain servisi) | ✓ |
| 5 | 02-§2.1-05 | Odeme dogrulama: Blockchain uzerinden otomatik | §2.1 | §3.3 | ✓ |
| 6 | 02-§2.1-06 | Item teslimi: Platform aliciya trade offer gonderir | §2.1 | §3.2, §4.1 | ✓ |
| 7 | 02-§2.1-07 | Teslim dogrulama: Steam uzerinden otomatik | §2.1 | §3.2 | ✓ |
| 8 | 02-§2.1-08 | Saticiya odeme: Komisyon kesilir, kalan saticinin cuzdanina gonderilir | §2.1 | §3.3 | ✓ |
| 9 | 02-§2.2-01 | Her islem tek item icerir | §2.2 | Dogrudan karsiligi yok ama mimariyle tutarli | ✓ |
| 10 | 02-§2.2-02 | Sadece item karsiligi kripto odeme | §2.2 | §3.3 | ✓ |
| 11 | 02-§2.2-03 | Islemi satici baslatir | §2.2 | §4.1 (CREATED satici tarafindan) | ✓ |
| 12 | 02-§2.2-04 | Islem detaylari degistirilemez | §2.2 | Dogrudan karsiligi yok ama state machine tutarli | ✓ |
| 13 | 02-§2.2-05 | Sadece tradeable itemlarla islem | §2.2 | §3.2 (Steam sidecar dogrulama) | ✓ |
| 14 | 02-§3.1-01 | Alici kabul timeout'u: Admin tarafindan ayarlanabilir | §3.1 | §4.4 | ✓ |
| 15 | 02-§3.1-02 | Satici trade offer timeout'u: Admin tarafindan ayarlanabilir | §3.1 | §4.4 | ✓ |
| 16 | 02-§3.1-03 | Odeme timeout'u: Admin min-max, satici secer | §3.1 | §4.4 | ✓ |
| 17 | 02-§3.1-04 | Teslim trade offer timeout'u: Admin tarafindan ayarlanabilir | §3.1 | §4.4 | ✓ |
| 18 | 02-§3.2-01 | Timeout dolunca islem iptal olur | §3.2 | §4.2 (CANCELLED_TIMEOUT) | ✓ |
| 19 | 02-§3.2-02 | Transfer edilen her sey iade edilir | §3.2 | §4.2 (iade tablosu) | ✓ |
| 20 | 02-§3.2-03 | Odeme timeout'unda adres izlemeye devam, gecikmelide iade | §3.2 | §3.3 (Gecikmelide odeme izleme) | ✓ |
| 21 | 02-§3.3-01 | Bakim sirasinda timeout dondurulur | §3.3 | §4.4 (Downtime notu) | ✓ |
| 22 | 02-§3.3-02 | Steam kesintisinde timeout dondurulur | §3.3 | §4.4 | ✓ |
| 23 | 02-§3.4-01 | Timeout uyarisi: Sure dolmadan ilgili tarafa uyari gonderilir | §3.4 | §5.3 (TimeoutWarningEvent) | ✓ |
| 24 | 02-§3.4-02 | Uyari esigi admin tarafindan oran olarak ayarlanir | §3.4 | — | ⚠ |
| 25 | 02-§4.1-01 | Odeme yontemi: Kripto stablecoin | §4.1 | §2.1, §3.3 | ✓ |
| 26 | 02-§4.1-02 | Desteklenen: USDT ve USDC | §4.1 | §3.3 (TRC-20 iletisim) | ✓ |
| 27 | 02-§4.1-03 | Blockchain agi: Tron (TRC-20) | §4.1 | §3.3 | ✓ |
| 28 | 02-§4.1-04 | Dis cuzdan modeli, platform cuzdani yok | §4.1 | §3.3 | ✓ |
| 29 | 02-§4.1-05 | Her islem icin benzersiz odeme adresi | §4.1 | §3.3 | ✓ |
| 30 | 02-§4.4-01 | Eksik tutar: Kabul etmez, iade eder | §4.4 | §3.3 (izleme sadece beklenen tokeni takip) | ⚠ |
| 31 | 02-§4.4-02 | Fazla tutar: Dogru tutari kabul eder, fazlayi iade eder | §4.4 | §3.3 | ⚠ |
| 32 | 02-§4.4-03 | Yanlis token: Kabul etmez, iade eder | §4.4 | §3.3 (Yanlis token notu var) | ✓ |
| 33 | 02-§4.4-04 | Gecikmelide odeme: Adres izlemeye devam, iade | §4.4 | §3.3 (Gecikmelide odeme izleme tablosu) | ✓ |
| 34 | 02-§4.5-01 | Saticiya odeme: Item teslimi dogrulandiktan sonra | §4.5 | §5.3 (ItemDeliveredEvent → odeme gonder) | ✓ |
| 35 | 02-§4.6-01 | Iade kapsami: Tam iade, komisyon dahil | §4.6 | §3.3 (iade gas fee notu) | ✓ |
| 36 | 02-§4.6-02 | Iade adresi: Alicinin islem kabul ederken belirledigi adres | §4.6 | §3.3 (02 §12.2 referansi var) | ✓ |
| 37 | 02-§4.7-01 | Gas fee: Alici odeme gas fee'si alici karsilar | §4.7 | §3.3 (implicit — dis cuzdan) | ✓ |
| 38 | 02-§4.7-02 | Gas fee: Saticiya gonderim, komisyondan dusulur | §4.7 | §3.3 (iade gas fee notu) | ✓ |
| 39 | 02-§4.7-03 | Gas fee koruma esigi: %10, admin degistirebilir | §4.7 | — | ✗ |
| 40 | 02-§7-01 | Odeme oncesi satici iptal edebilir | §7 | §4.2 (iptal tablosu) | ✓ |
| 41 | 02-§7-02 | Odeme oncesi alici iptal edebilir | §7 | §4.2 | ✓ |
| 42 | 02-§7-03 | Odeme sonrasi tek tarafli iptal yok | §7 | §4.2 (Not) | ✓ |
| 43 | 02-§7-04 | Iptal sonrasi cooldown, admin tarafindan belirlenir | §7 | — | ⚠ |
| 44 | 02-§7-05 | Iptal sebebi zorunlu | §7 | — | ⚠ |
| 45 | 02-§10.1-01 | Dispute: Otomatik cozum (blockchain, Steam) | §10.1 | §3.1 (Dispute modulu) | ✓ |
| 46 | 02-§10.2-01 | Dispute yalnizca alici acabilir | §10.2 | §3.1 (Dispute modulu) | ⚠ |
| 47 | 02-§11-01 | Giris: Steam OpenID | §11 | §6.1 | ✓ |
| 48 | 02-§11-02 | Steam Mobile Authenticator zorunlu | §11 | §6.2 (Mobile Authenticator kontrolu) | ✓ |
| 49 | 02-§12.1-01 | Satici cuzdan adresi: Profilde varsayilan + islem bazli | §12.1 | §6.4 | ✓ |
| 50 | 02-§12.3-01 | Adres degisikligi: Steam re-auth zorunlu | §12.3 | §6.4 | ✓ |
| 51 | 02-§14.4-01 | Kara para: Piyasa fiyat sapmasi flag | §14.4 | §3.1 (Fraud modulu) | ✓ |
| 52 | 02-§21-01 | 4 dil destegi: EN, ZH, ES, TR | §21 | §2.3 (i18n), §7.3 | ✓ |

**Toplam: 52 oge (44 ✓, 6 ⚠, 2 ✗)**

---

### Envanter — 03_USER_FLOWS

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 1 | 03-§1.2-01 | 13 islem durumu (CREATED → FLAGGED) | §1.2 | §4.1 | ✓ |
| 2 | 03-§1.2-02 | Dispute ayri islem durumu degil, ayri bayrak | §1.2 | §3.1 (Dispute modulu) | ✓ |
| 3 | 03-§2.1-01 | Steam Mobile Authenticator kontrolu giris sirasinda | §2.1 | §6.2 | ✓ |
| 4 | 03-§2.1-02 | Kullanici Sozlesmesi (ToS) ilk giriste gosterilir | §2.1 | — | ✗ |
| 5 | 03-§2.2-01 | Esanli aktif islem limiti kontrolu | §2.2 | §3.1 (Transaction modulu) | ✓ |
| 6 | 03-§2.2-02 | Iptal cooldown kontrolu | §2.2 | §3.1 (Transaction modulu) | ✓ |
| 7 | 03-§2.2-03 | Yeni hesap islem limiti kontrolu | §2.2 | §3.1 (Transaction modulu) | ✓ |
| 8 | 03-§2.2-04 | Steam envanter okuma ve tradeable item listeleme | §2.2 | §3.2 (Steam sidecar) | ✓ |
| 9 | 03-§2.2-05 | Fiyat min/max kontrolu | §2.2 | §3.1 (Transaction modulu) | ✓ |
| 10 | 03-§2.2-06 | Piyasa fiyati sapma kontrolu (arka plan) → FLAGGED | §2.2 | §3.1 (Fraud modulu), §4.1 | ✓ |
| 11 | 03-§2.2-07 | Aliciya bildirim veya davet linki | §2.2 | §5.3 (TransactionCreatedEvent) | ✓ |
| 12 | 03-§2.3-01 | Trade offer gonderimi baskrisiz olursa otomatik yeniden deneme | §2.3 | §3.2 | ⚠ |
| 13 | 03-§2.3-02 | Satici trade offer'i reddederse → CANCELLED_SELLER | §2.3 | §4.2 | ✓ |
| 14 | 03-§2.3-03 | Emanet alinan item eslesme kontrolu | §2.3 | §3.2 (implicit) | ✓ |
| 15 | 03-§2.4-01 | Gas fee komisyonun %10'unu asiyorsa saticidan kesilir | §2.4 | — | ⚠ |
| 16 | 03-§2.4-02 | Odeme gonderimi basarisizsa otomatik yeniden deneme | §2.4 | §3.3 | ⚠ |
| 17 | 03-§2.4-03 | Tekrarlayan basarisizlikta admin'e bildirim | §2.4 | §5.3 (event sistemi) | ✓ |
| 18 | 03-§3.2-01 | Alici islem detaylarini gorur (item, fiyat, komisyon, toplam) | §3.2 | §4.1 (state machine akisi) | ✓ |
| 19 | 03-§3.5-01 | Alici trade offer reddederse → CANCELLED_BUYER + iade | §3.5 | §4.2 | ✓ |
| 20 | 03-§4.1-01 | Alici kabul timeout'u → CANCELLED_TIMEOUT | §4.1 | §4.2, §4.4 | ✓ |
| 21 | 03-§4.2-01 | Satici trade offer timeout'u → CANCELLED_TIMEOUT | §4.2 | §4.2, §4.4 | ✓ |
| 22 | 03-§4.3-01 | Odeme timeout'u → CANCELLED_TIMEOUT + item iade + adres izleme | §4.3 | §4.2, §3.3 | ✓ |
| 23 | 03-§4.4-01 | Teslim trade offer timeout'u → CANCELLED_TIMEOUT + cift iade | §4.4 | §4.2 | ✓ |
| 24 | 03-§4.5-01 | Timeout uyarisi: Admin orani dolunca bildirim | §4.5 | §5.3 (TimeoutWarningEvent) | ✓ |
| 25 | 03-§5.1-01 | Eksik tutar → kabul etmez, iade eder, timeout devam | §5.1 | §3.3 | ⚠ |
| 26 | 03-§5.2-01 | Fazla tutar → dogru tutari kabul, fazlayi iade | §5.2 | §3.3 | ✓ |
| 27 | 03-§5.3-01 | Yanlis token → kabul etmez, iade eder | §5.3 | §3.3 | ✓ |
| 28 | 03-§5.4-01 | Gecikmelide odeme → otomatik iade | §5.4 | §3.3 | ✓ |
| 29 | 03-§7.1-01 | Fiyat sapmasi flag'i → FLAGGED → admin onay/red | §7.1 | §4.2 (FLAGGED gecisleri) | ✓ |
| 30 | 03-§7.2-01 | Yuksek hacim flag'i → FLAGGED | §7.2 | §3.1 (Fraud modulu) | ✓ |
| 31 | 03-§7.3-01 | Anormal davranis flag'i → FLAGGED | §7.3 | §3.1 (Fraud modulu) | ✓ |
| 32 | 03-§7.4-01 | Coklu hesap tespiti (cuzdan adresi + IP/cihaz) | §7.4 | §3.1 (Fraud modulu) | ✓ |
| 33 | 03-§8.2-01 | Flag inceleme: Admin onay/red | §8.2 | §4.2 (FLAGGED → admin_approve/reject) | ✓ |
| 34 | 03-§8.4-01 | Admin parametre yonetimi (timeout, komisyon, limitler) | §8.4 | §3.1 (Admin modulu) | ✓ |
| 35 | 03-§11.1-01 | Planli bakim: Timeout dondurma, bildirim | §11.1 | §4.4 (Downtime notu) | ✓ |
| 36 | 03-§11.2-01 | Steam kesintisi: Timeout dondurma | §11.2 | §4.4 | ✓ |
| 37 | 03-§12-01 | Satici bildirimleri listesi (9 tetikleyici) | §12.1 | §5.3 (Domain eventler), §7 | ✓ |
| 38 | 03-§12-02 | Alici bildirimleri listesi (10 tetikleyici) | §12.2 | §5.3, §7 | ✓ |

**Toplam: 38 oge (33 ✓, 4 ⚠, 1 ✗)**

---

### Envanter — 04_UI_SPECS

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 1 | 04-§2.2-01 | 20 ekranlik envanter (3 genel, 7 kullanici, 10 admin) | §2.2 | Kapsam disi (mimari ekran tanimlamaz) | ✓ |
| 2 | 04-§5-01 | C01 Status Badge: 13 durum ile renk kodu | §5 | §4.1 (13 durum) | ✓ |
| 3 | 04-§5-02 | C02 Countdown Timer: Timeout geri sayim | §5 | §4.4 (Timeout yonetimi) | ✓ |
| 4 | 04-§5-03 | C05 Transaction Timeline: 8 adimlik ilerleme | §5 | §4.1 (state machine) | ✓ |
| 5 | 04-§7.2-01 | S06: Islem olusturma form adimlari (item, detay, alici, ozet) | §7.2 | §3.1 (Transaction modulu) | ✓ |
| 6 | 04-§7.3-01 | S07: 13 durum x 3 rol varyant matrisi | §7.3 | §4.1, §4.2 | ✓ |
| 7 | 04-§7.3-02 | S07: Odeme edge case gosterimleri (eksik/fazla/yanlis/gecikmeli) | §7.3 | §3.3 | ✓ |
| 8 | 04-§7.3-03 | S07: Dispute gosterimi ve form | §7.3 | §3.1 (Dispute modulu) | ✓ |
| 9 | 04-§7.6-01 | S10: Bildirim tercihleri (platform, email, Telegram, Discord) | §7.6 | §7.4 | ✓ |
| 10 | 04-§8.1-01 | S12: Admin Dashboard ozet kartlari | §8.1 | §3.1 (Admin modulu) | ✓ |
| 11 | 04-§8.6-01 | S17: Parametre yonetimi (timeout, komisyon, limitler, fraud) | §8.6 | §3.1 (Admin modulu), §4.4 | ✓ |
| 12 | 04-§8.6-02 | S17: Timeout uyari esigi parametresi (%, C02 icin de kullanilir) | §8.6 | §4.4 | ⚠ |
| 13 | 04-§8.10-01 | S21: Audit Log ekrani — fon hareketleri, admin aksiyonlari, guvenlik olaylari | §8.10 | §5.4 (AuditLog) | ✓ |
| 14 | 04-§10-01 | Lokalizasyon: 4 dil destegi, tarih/saat/sayi format farklari | §10 | §2.3 (i18n), §7.3 | ⚠ |

**Toplam: 14 oge (12 ✓, 2 ⚠, 0 ✗)**

---

### Envanter — 10_MVP_SCOPE

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 1 | 10-§2.1-01 | Temel escrow akisi: 8 adim | §2.1 | §4.1, §4.2 | ✓ |
| 2 | 10-§2.2-01 | USDT ve USDC destegi (Tron TRC-20) | §2.2 | §3.3 | ✓ |
| 3 | 10-§2.2-02 | Dis cuzdan modeli, benzersiz odeme adresi | §2.2 | §3.3 | ✓ |
| 4 | 10-§2.2-03 | Otomatik blockchain dogrulama | §2.2 | §3.3 | ✓ |
| 5 | 10-§2.2-04 | Odeme edge case yonetimi | §2.2 | §3.3 | ✓ |
| 6 | 10-§2.2-05 | Gas fee yonetimi (komisyondan, koruma esigi) | §2.2 | §3.3 | ⚠ |
| 7 | 10-§2.3-01 | Her adim icin ayri timeout | §2.3 | §4.4 | ✓ |
| 8 | 10-§2.3-02 | Admin tarafindan ayarlanabilir sureler | §2.3 | §4.4 | ✓ |
| 9 | 10-§2.4-01 | Steam ile giris | §2.4 | §6.1 | ✓ |
| 10 | 10-§2.4-02 | Steam Mobile Authenticator zorunlulugu | §2.4 | §6.2 | ✓ |
| 11 | 10-§2.4-03 | Hesap silme/deaktif etme | §2.4 | §6.5 | ✓ |
| 12 | 10-§2.4-04 | Kullanici itibar skoru | §2.4 | §3.1 (User modulu) | ✓ |
| 13 | 10-§2.8-01 | Fraud onlemleri: Wash trading, iptal limiti, yeni hesap limiti, flag'leme | §2.8 | §3.1 (Fraud modulu) | ✓ |
| 14 | 10-§2.9-01 | Steam envanter okuma, item dogrulama | §2.9 | §3.2 | ✓ |
| 15 | 10-§2.10-01 | Birden fazla Steam hesabi, failover | §2.10 | §3.2 (Bot yonetimi) | ✓ |
| 16 | 10-§2.11-01 | Admin paneli: Roller, parametreler, flag yonetimi, audit log | §2.11 | §3.1 (Admin modulu) | ✓ |
| 17 | 10-§2.13-01 | Bildirimler: Platform ici, email, Telegram/Discord | §2.13 | §7 | ✓ |
| 18 | 10-§2.14-01 | Downtime yonetimi: Timeout dondurma | §2.14 | §4.4 | ✓ |
| 19 | 10-§2.15-01 | 4 dil destegi | §2.15 | §2.3, §7.3 | ✓ |
| 20 | 10-§4-01 | Kisitlamalar: Sadece CS2, tek item, Tron TRC-20, web, %2 komisyon | §4 | §2.1, §3.3, §2.3 | ✓ |

**Toplam: 20 oge (19 ✓, 1 ⚠, 0 ✗)**

---

### Envanter — 05_TECHNICAL_ARCHITECTURE (Hedef — Ic Envanter)

| # | ID | Oge Ozeti | Kaynak Bolum | Durum |
|---|---|---|---|---|
| 1 | 05-§1.1-01 | Mimari yaklasim: Moduler Monolith | §1.1 | ✓ |
| 2 | 05-§2.1-01 | Backend: .NET 8 (C#) + ASP.NET Core | §2.1 | ✓ |
| 3 | 05-§2.1-02 | Frontend: Next.js (React + TypeScript) | §2.1 | ✓ |
| 4 | 05-§2.1-03 | Veritabani: SQL Server 2022 | §2.1 | ✓ |
| 5 | 05-§2.1-04 | Cache/Session: Redis 7 | §2.1 | ✓ |
| 6 | 05-§2.1-05 | ORM: Entity Framework Core | §2.1 | ✓ |
| 7 | 05-§2.1-06 | Background Jobs: Hangfire (SQL Server storage) | §2.1 | ✓ |
| 8 | 05-§2.1-07 | Real-time: SignalR | §2.1 | ✓ |
| 9 | 05-§2.2-01 | Temel kutuphaneler tablosu (8 kutuphane) | §2.2 | ✓ |
| 10 | 05-§2.2-02 | API versioning: URL prefix /api/v1/ | §2.2 | ✓ |
| 11 | 05-§2.5-01 | Redis rolleri: Session, cache, rate limiting | §2.5 | ✓ |
| 12 | 05-§2.5-02 | Redis cokerse: Sistem durmaz, degrade olur | §2.5 | ✓ |
| 13 | 05-§2.5-03 | Redis resilience: Standalone → Sentinel → Cluster | §2.5 | ✓ |
| 14 | 05-§3-01 | 3 runtime: .NET Backend, Steam Sidecar, Blockchain Service | §3 | ✓ |
| 15 | 05-§3.1-01 | 9 modul: Transaction, Payment, Steam, User, Auth, Notification, Admin, Dispute, Fraud | §3.1 | ✓ |
| 16 | 05-§3.2-01 | Steam Sidecar: Node.js, HTTP ile .NET haberlesme | §3.2 | ✓ |
| 17 | 05-§3.2-02 | Bot yonetim stratejisi: Capacity-based, health check, failover | §3.2 | ✓ |
| 18 | 05-§3.3-01 | Blockchain Servisi: TronWeb, HD Wallet, monitoring | §3.3 | ✓ |
| 19 | 05-§3.3-02 | Minimum onay sayisi: 20 blok (~60 saniye) | §3.3 | ✓ |
| 20 | 05-§3.3-03 | Polling araligi: 3 saniye | §3.3 | ✓ |
| 21 | 05-§3.3-04 | Gecikmelide odeme izleme: Kademeli polling (24h→7d→30d) | §3.3 | ✓ |
| 22 | 05-§3.3-05 | Minimum iade esigi: iade < 2x gas fee → iade yapilmaz, admin alert | §3.3 | ✓ |
| 23 | 05-§3.4-01 | Servisler arasi guvenlik: Docker internal network + API key + HMAC | §3.4 | ✓ |
| 24 | 05-§3.5-01 | Secrets management: Dev (.env), Prod (Docker Secrets / Key Vault) | §3.5 | ✓ |
| 25 | 05-§4.1-01 | 13 durum tanimlari tablosu | §4.1 | ✓ |
| 26 | 05-§4.2-01 | Durum gecisleri diyagrami ve iptal/red tablosu | §4.2 | ✓ |
| 27 | 05-§5.1-01 | Outbox Pattern: State gecisi + event yazma ayni DB transaction | §5.1 | ✓ |
| 28 | 05-§5.1-02 | Consumer Idempotency: EventId + ProcessedEvents tablosu | §5.1 | ✓ |
| 29 | 05-§5.3-01 | 9 domain event tanimlari | §5.3 | ⚠ |
| 30 | 05-§6.1-01 | Steam OpenID + JWT + Refresh Token | §6.1 | ✓ |
| 31 | 05-§6.3-01 | 10 guvenlik katmani (Rate limiting → Brute force) | §6.3 | ✓ |
| 32 | 05-§7.1-01 | Bildirim mimarisi: Outbox → Consumer → Kanal Dispatching | §7.1 | ✓ |
| 33 | 05-§8.1-01 | Docker Compose: 11 container | §8.1 | ⚠ |
| 34 | 05-§9.1-01 | Loglama: Serilog → Loki, Pino → Loki, Correlation ID | §9.1 | ✓ |
| 35 | 05-§10-01 | Testing: Unit, Integration, E2E, Contract | §10 | ⚠ |

**Toplam: 35 oge (30 ✓, 4 ⚠, 1 ✗)**

---

## Bulgular

| # | Envanter ID | Tur | Seviye | Bulgu | Oneri |
|---|---|---|---|---|---|
| 1 | 02-§4.7-03 | GAP | High | Gas fee koruma esigi mekanizmasi (komisyonun %10'unu asarsa saticidan kesilir) 02 §4.7'de net olarak tanimli ve 03 §2.4'te akis adimi olarak islenilmis, ancak 05'te hicbir bolumlde karsiligi yok. §3.3 Blockchain Servisi bolumlunde sadece iade gas fee ve minimum iade esiginden bahsediliyor; saticiya odeme sirasindaki gas fee koruma esigi mekanizmasi eksik. | §3.3'un "Odeme gonderimi" bolumune veya ayri bir "Saticiya Odeme" alt bolumlune gas fee koruma esigi mekanizmasi eklenmeli: "Gas fee komisyonun admin tarafindan belirlenen esigini (%10 varsayilan) asarsa, asan kisim saticinin alacagindan dusulur (02 §4.7)" |
| 2 | 03-§2.1-02 | GAP | Medium | Kullanici Sozlesmesi (Terms of Service) kabul mekanizmasi 03 §2.1'de (adim 8) ve 04 §6.2 (S02 ToS modal)'de net olarak tanimli, ancak 05'te hicbir yerde karsiligi yok. Bu UI ve akim katmaninda tanimli bir ozellik olmasina ragmen, teknik mimaride en azindan Auth modulu veya User modulu icerisinde ToS kabul kaydinin nasil saklanacagi belirtilmeli. | §3.1 Auth modulu sorumluluguna "ToS kabul kaydı" veya §6.1 Authentication akisina "ilk giris sirasinda ToS kabulu kontrol edilir" notu eklenmeli. Ancak bu 06_DATA_MODEL.md (User entity, AcceptedTermsAt field) ve sonraki dokumanlarin kapsamina da girdigi icin seviye Medium. |
| 3 | 02-§3.4-02 | Kismi | Medium | Timeout uyari esiginin admin tarafindan oran olarak ayarlanabilir oldugu 02 §3.4 ve 04 §8.6 (S17 Parametre Yonetimi)'de acikca tanimli. 05 §5.3'te TimeoutWarningEvent var, ancak uyari esiginin admin tarafindan konfigüre edilebilir bir parametre oldugu ve bu parametrenin nasil kullanilacagi (Hangfire job ile mi, state machine side effect ile mi) belirtilmemis. | §4.4 Timeout Yonetimi bolumune "Timeout uyari esigi (admin tarafindan oran olarak ayarlanir — 02 §3.4, 04 §8.6 S17). Sure dolmadan belirli bir oran gerildiginde TimeoutWarningEvent uretilir" bilgisi eklenmeli. |
| 4 | 02-§4.4-01 | Kismi | Medium | Eksik tutar senaryosu 02 §4.4'te ve 03 §5.1'de detayli tanimli: "Platform kabul etmez, gelen tutar iade edilir, alici dogru tutari bastan gonderir, timeout devam eder." 05 §3.3'te blockchain servisinde izleme, dogrulama ve yanlis token iade mekanizmasi tanimli, ancak eksik tutar senaryosunun nasil islenecegi (kabul etmeme, iade, timeout devamı) acikca belirtilmemis. Sadece beklenen tokeni takip ettigi ve yanlis token icin iade denedigi yazili. | §3.3 Blockchain Servisi bolumune eksik/fazla tutar senaryolarinin teknik davranisi eklenebilir veya bu detay 08_INTEGRATION_SPEC.md kapsamina birakilabilir. Seviye Medium cunku temel mekanizma (blockchain monitoring + iade) mevcut, sadece edge case davranisi eksik. |
| 5 | 02-§4.4-02 | Kismi | Medium | Fazla tutar senaryosu benzer sekilde 05'te acikca tanimli degil. 02 §4.4 ve 03 §5.2'de "dogru tutari kabul eder, fazlayi iade eder" tanimli. | §3.3'e veya 08'e eklenmeli. 02-§4.4-01 ile ayni oneri. |
| 6 | 02-§7-04 | Kismi | Low | Iptal sonrasi cooldown mekanizmasi 02 §7'de tanimli ve 03 §2.2'de akim olarak islenilmis. 05'te Transaction modulu sorumlulugu altinda "is kurallari" olarak kapsanabilir ancak cooldown mekanizmasi (sure tabanli engelleme) teknik mimaride acikca yer almamis. Cooldown in-memory mi, DB'de mi, Hangfire job ile mi? | Low — bu detay implementation katmaninda (09, 11) kararlanacak bir konu. Mimari dokumanda zorunlu degil. |
| 7 | 02-§7-05 | Kismi | Low | Iptal sebebi zorunlu olmasi 02 §7 ve 03 §2.5'te tanimli. Teknik mimaride bu bir validation kurali olarak FluentValidation ile uygulanabilir, ancak mimari dokumanda bu detay beklenmez. | Low — API ve implementation katmaninda ele alinacak. |
| 8 | 02-§10.2-01 | Kismi | Low | Dispute kurallarinin detayi (sadece alici acabilir, timeout durdurmaz, rate limiting) 02 §10.2'de tanimli. 05 §3.1'de Dispute modulu "itiraz yonetimi, otomatik dogrulama, admin eskalasyonu" olarak ozetlenmis ancak bu is kurallarinin hepsi listelenmemis. | Low — Modul sorumluluk tanimi yeterli, is kurallari 02 referansiyla implementation'da uygulanir. |
| 9 | 03-§2.3-01 | Kismi | Medium | Trade offer gonderimi basarisiz oldugunda otomatik retry mekanizmasi 03 §2.3'te ve §3.5'te tanimli. 05 §3.2 Steam Sidecar bolumunde "Mid-trade failure: Bot cokerse restart'ta pending trade offer'lar kontrol edilir" deniyor, ancak aktif bir trade offer gonderim denemesinin basarisiz oldugundaki retry stratejisi (exponential backoff? max retry? timeout icerisinde mi?) acikca tanimli degil. | §3.2 Steam Sidecar bolumune trade offer gonderim retry stratejisi eklenebilir veya bu detay 08_INTEGRATION_SPEC.md kapsamina birakilabilir. Seviye Medium — temel mekanizma var ama retry detayi eksik. |
| 10 | 03-§2.4-02 | Kismi | Medium | Saticiya odeme gonderimi basarisiz oldugundaki retry mekanizmasi 03 §2.4'te tanimli: "Sistem otomatik yeniden dener, tekrarlayan basarisizlikta admin'e bildirim". 05 §3.3'te blockchain servisi odeme gonderimi tanimli ancak retry stratejisi (kac deneme, backoff, tum denemeler basarisizsa ne olur) belirtilmemis. | §3.3'e veya 08'e odeme gonderim retry stratejisi eklenmeli. 7.5'teki bildirim retry stratejisi (exponential backoff) referans alinabilir. |
| 11 | 10-§2.2-05 | Kismi | High | Gas fee yonetimi 10 §2.2'de "komisyondan karsilanir, koruma esigi ile" diye ozetlenmis. Bu #1 bulguyla ayni konu — 05'te gas fee koruma esigi mekanizmasi eksik. | #1 bulgusu ile birlikte cozulur. |
| 12 | 04-§8.6-02 | Kismi | Low | S17'deki "Timeout uyari esigi" parametresinin C02 countdown timer renk degisimi icin de kullanildigi bilgisi 04'te detayli, 05'te sadece TimeoutWarningEvent olarak referans var, renk degisimi UI katmani — mimaride beklenmez. | Low — UI detayi, mimari kapsaminda degil. |
| 13 | 04-§10-01 | Kismi | Low | Lokalizasyon detaylari (tarih/saat/sayi format farklari) 04 §10'da detayli. 05 §2.3'te "i18n: next-intl veya next-i18next — 4 dil" ve §7.3'te "lokalizasyon: .NET resource dosyalari (.resx)" olarak genel mekanizma tanimli. Format detaylari frontend implementation konusu. | Low — Implementation detayi. |
| 14 | 05-§5.3-01 | Ic Tutarsizlik | Medium | 9 domain event tanimli, ancak 03 §12'deki bildirim ozetinde yer alan bazi tetikleyiciler icin event eksik: (a) Satıcıya odeme gonderildi bildirimi icin ayri bir event yok — ItemDeliveredEvent altinda mi cozuluyor? (b) Item iade bildirimi icin ayri event yok — TransactionCancelledEvent altinda mi? Mevcut event listesi temel akislari kapsıyor ancak her bildirim tetikleyicisinin hangi event'e baglandigi tam netlestirilmemis. | §5.3'teki event tablosuna bir not eklenebilir: "Bildirim consumer'i bu event'leri aldiginda, olayin turune ve state'ine gore uygun bildirimleri gonderir. Ornegin TransactionCancelledEvent alındiginda iptal sebebi, iade durumu ve ilgili taraflara gore farkli bildirim mesajlari uretilir." |
| 15 | 05-§8.1-01 | Ic Tutarsizlik | Low | Docker Compose container listesinde 11 container var. Servis mimarisi diyagraminda (§3) ise .NET Backend, Next.js Frontend, Steam Sidecar, Blockchain Service, SQL Server, Redis gosteriliyor (Nginx, Loki, Prometheus, Grafana, Uptime Kuma diyagramda yok). Bu bilinçli bir sadeleştirme — diyagram is servisleri gösteriyor, monitoring stack'i gosternmiyor. Tutarsizlik degil, tasarim karari. | Low — Istenirse §3 diyagramina monitoring stack'i de eklenebilir, ancak diyagramın okunabilirligini bozabilir. Mevcut hali kabul edilebilir. |
| 16 | 05-§10-01 | Ic — Yeterlilik | Medium | Testing stratejisi 4 satirlik bir tablo ile ozetlenmis. Hangi modullerin/katmanlarin once test edilecegi, test coverage hedefi, CI pipeline'indaki test adimlari, test data stratejisi gibi detaylar yok. Bir gelistiricinin "test stratejisi nedir?" sorusuna bu tablo yeterli cevap vermez. | §10'a en azindan su bilgiler eklenebilir: (a) Oncelikli test alanlari (state machine, odeme dogrulama, iade hesaplama), (b) CI'da test calistirma kurali, (c) Test data yaklasimi. Ancak bu detaylar 09_CODING_GUIDELINES.md veya 11_IMPLEMENTATION_PLAN.md kapsaminda da ele alinabilir, dolayisiyla seviye Medium. |
| 17 | 05-§3.3-06 | Ic — Guvenlik | High | §3.3 Blockchain Servisi guvenlik katmanlarinda "Hot wallet limiti: Operasyonel miktarda fon tutulur, fazlasi cold wallet'a" deniyor. Ancak hot-to-cold transfer mekanizmasi tanimli degil: Otomatik mi, manuel mi? Esik nedir? Admin mi tetikler? Bu, fon guvenligi acisindan onemli bir eksiklik. | §3.3 guvenlik katmanlarina su bilgi eklenmeli: "Hot wallet limiti admin tarafindan belirlenir. Limit asildiginda [otomatik cold wallet transferi/admin alert] tetiklenir." En azindan mekanizmanin adi ve tetikleyicisi tanimlanmali. |

---

## Aksiyon Plani

**Critical:**
- (Yok)

**High:**
- [x] Bulgu #1 / #11: §3.3'e gas fee koruma esigi mekanizmasi eklenmeli (02 §4.7 ile tutarli)
- [x] Bulgu #17: §3.3 guvenlik katmanlarina hot wallet limit mekanizmasi eklenmeli

**Medium:**
- [x] Bulgu #2: §3.1 Auth veya User modul sorumluluguna ToS kabul kaydi notu → Bu dokumanda cozulebilir
- [x] Bulgu #3: §4.4'e timeout uyari esigi bilgisi eklenmeli → Bu dokumanda cozulebilir
- [x] Bulgu #4 / #5: §3.3'e eksik/fazla tutar senaryosu eklenmeli veya 08'e birakilmali → Bu dokumanda cozulebilir (kisa not olarak)
- [x] Bulgu #9: §3.2'ye trade offer retry stratejisi → Bu dokumanda cozulebilir (kisa not olarak)
- [x] Bulgu #10: §3.3'e odeme gonderim retry stratejisi → Bu dokumanda cozulebilir (kisa not olarak)
- [x] Bulgu #14: §5.3 event tablosuna aciklayici not eklenmeli → Bu dokumanda cozulebilir
- [x] Bulgu #16: §10 Testing stratejisine ek bilgi → Bu dokumanda cozulebilir

**Low:**
- Bulgu #6: Iptal cooldown mekanizmasi — implementation/coding guidelines kapsaminda
- Bulgu #7: Iptal sebebi zorunlulugu — API/validation kapsaminda
- Bulgu #8: Dispute kural detaylari — implementation kapsaminda
- Bulgu #12: Timeout uyari esigi UI detayi — UI kapsaminda, mimari dokumanda beklenmez
- Bulgu #13: Lokalizasyon format detaylari — frontend implementation kapsaminda
- Bulgu #15: Docker Compose vs diyagram farki — bilinçli sadeleştirme, kabul edilebilir

---

*Audit tamamlandi — 05_TECHNICAL_ARCHITECTURE.md v1.3*
