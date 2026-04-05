# Audit Raporu — 03_USER_FLOWS.md

**Tarih:** 2026-03-16
**Hedef:** 03 — User Flows (v1.4)
**Bagiam:** 01_PROJECT_VISION.md, 02_PRODUCT_REQUIREMENTS.md, 10_MVP_SCOPE.md
**Odak:** Tam denetim

---

## Envanter Ozeti

| Kaynak | Toplam Oge | ✓ | ⚠ | ✗ |
|---|---|---|---|---|
| 02 | 89 | 79 | 7 | 3 |
| 01 | 12 | 12 | 0 | 0 |
| 10 | 18 | 17 | 1 | 0 |
| Hedef (ic) | 20 | 17 | 2 | 1 |
| **Toplam** | **139** | **125** | **10** | **4** |

---

## Envanter ve Eslestirme Detayi

### Envanter — 02_PRODUCT_REQUIREMENTS

#### §2.1 Temel Akis

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 1 | 02-§2.1-01 | Islem olusturma: satici item secer, stablecoin turunu belirler, fiyat ve odeme timeout suresi girer | §2.1 | §2.2 | ✓ |
| 2 | 02-§2.1-02 | Alici kabulu: alici islem detaylarini gorur ve kabul eder, henuz odeme yapmaz | §2.1 | §3.2 | ✓ |
| 3 | 02-§2.1-03 | Item emaneti: platform saticiya trade offer gonderir, satici kabul eder, item platforma gecer | §2.1 | §2.3 | ✓ |
| 4 | 02-§2.1-04 | Odeme: platform benzersiz odeme adresi uretir, alici toplam tutari gonderir | §2.1 | §3.4 | ✓ |
| 5 | 02-§2.1-05 | Odeme dogrulama: blockchain uzerinden otomatik | §2.1 | §3.4 | ✓ |
| 6 | 02-§2.1-06 | Item teslimi: platform aliciya Steam trade offer gonderir, alici kabul eder | §2.1 | §3.5 | ✓ |
| 7 | 02-§2.1-07 | Teslim dogrulama: Steam uzerinden otomatik | §2.1 | §3.5 | ✓ |
| 8 | 02-§2.1-08 | Saticiya odeme: platform komisyonu keser, kalan tutari saticinin cuzdan adresine gonderir | §2.1 | §2.4 | ✓ |

#### §2.2 Islem Kurallari

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 9 | 02-§2.2-01 | Her islem tek bir item icerir | §2.2 | §2.2 (adim 7: satici item secer) | ✓ |
| 10 | 02-§2.2-02 | Sadece item karsiligi kripto odeme (barter yok) | §2.2 | §2.2 (stablecoin secimi, fiyat girisi) | ✓ |
| 11 | 02-§2.2-03 | Islemi her zaman satici baslatir | §2.2 | §2.2 | ✓ |
| 12 | 02-§2.2-04 | Islem detaylari olusturulduktan sonra degistirilemez | §2.2 | — | ✗ |
| 13 | 02-§2.2-05 | Sadece tradeable itemlarla islem yapilabilir | §2.2 | §2.2 (adim 8) | ✓ |
| 14 | 02-§2.2-06 | Tum CS2 item turleri desteklenir | §2.2 | — | ⚠ |

#### §3 Timeout Gereksinimleri

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 15 | 02-§3.1-01 | Alicinin islemi kabul etmesi icin timeout — admin ayarlanabilir | §3.1 | §4.1 | ✓ |
| 16 | 02-§3.1-02 | Saticinin trade offer'i kabul etmesi icin timeout — admin ayarlanabilir | §3.1 | §4.2 | ✓ |
| 17 | 02-§3.1-03 | Alicinin odemeyi gondermesi icin timeout — admin min-max, satici secer | §3.1 | §2.2 (adim 12) + §4.3 | ✓ |
| 18 | 02-§3.1-04 | Alicinin teslim trade offer'ini kabul etmesi icin timeout | §3.1 | §4.4 | ✓ |
| 19 | 02-§3.2-01 | Timeout doldiginda islem iptal olur | §3.2 | §4.1-4.4 | ✓ |
| 20 | 02-§3.2-02 | Transfer edilen her sey ilgili tarafa otomatik iade | §3.2 | §4.3, §4.4 | ✓ |
| 21 | 02-§3.2-03 | Odeme timeout'unda platform adresi izlemeye devam eder, gecikmeli odeme aliciya iade | §3.2 | §4.3 (adim 4) + §5.4 | ✓ |
| 22 | 02-§3.3-01 | Platform bakiminda timeout dondurmasi | §3.3 | §11.1 | ✓ |
| 23 | 02-§3.3-02 | Steam kesintisinde timeout dondurmasi | §3.3 | §11.2 | ✓ |
| 24 | 02-§3.3-03 | Bakim/kesinti bittiginde timeout kaldigindan devam eder | §3.3 | §11.1, §11.2 | ✓ |
| 25 | 02-§3.3-04 | Kullanicilara planli bakim oncesi bildirim gonderilir | §3.3 | §11.1 | ✓ |
| 26 | 02-§3.4-01 | Timeout suresi dolmadan ilgili tarafa uyari gonderilir | §3.4 | §4.5 | ✓ |
| 27 | 02-§3.4-02 | Uyari esigi admin tarafindan oran olarak ayarlanir | §3.4 | §4.5 | ⚠ |
| 28 | 02-§3.4-03 | Uyari tum bildirim kanallari uzerinden iletilir | §3.4 | §4.5 | ✓ |

#### §4 Odeme Gereksinimleri

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 29 | 02-§4.1-01 | Odeme yontemi: kripto stablecoin | §4.1 | §3.4 | ✓ |
| 30 | 02-§4.1-02 | Desteklenen stablecoinler: USDT ve USDC | §4.1 | §2.2 (adim 9), §3.4 | ✓ |
| 31 | 02-§4.1-03 | Blockchain agi: Tron TRC-20 | §4.1 | §3.4 (adim 3) | ✓ |
| 32 | 02-§4.1-04 | Odeme modeli: dis cuzdan, platform cuzdani yok | §4.1 | §3.4 | ✓ |
| 33 | 02-§4.1-05 | Her islem icin platform benzersiz odeme adresi uretir | §4.1 | §3.4 (adim 3) | ✓ |
| 34 | 02-§4.1-06 | Dogrulama: blockchain uzerinden otomatik | §4.1 | §3.4 (adim 7) | ✓ |
| 35 | 02-§4.2-01 | Satici islem baslatirken USDT/USDC secer | §4.2 | §2.2 (adim 9) | ✓ |
| 36 | 02-§4.2-02 | Alici saticinin sectigi token ile odeme yapar | §4.2 | §3.4 | ✓ |
| 37 | 02-§4.2-03 | Bir islemde yalnizca bir stablecoin kabul edilir | §4.2 | §2.2 (adim 9) | ✓ |
| 38 | 02-§4.3-01 | Satici fiyati dogrudan stablecoin miktari girer | §4.3 | §2.2 (adim 10) | ✓ |
| 39 | 02-§4.3-02 | Platform fiyata mudahale etmez | §4.3 | — | ⚠ |
| 40 | 02-§4.3-03 | MVP'de kullaniciya piyasa fiyati gosterilmez | §4.3 | — | ⚠ |
| 41 | 02-§4.3-04 | Arka planda piyasa fiyat verisi cekilir, fraud tespiti icin kullanilir | §4.3 | §7.1 (adim 2-3) | ✓ |
| 42 | 02-§4.4-01 | Eksik tutar: platform kabul etmez, iade edilir | §4.4 | §5.1 | ✓ |
| 43 | 02-§4.4-02 | Fazla tutar: dogru tutar kabul, fazla iade, islem devam | §4.4 | §5.2 | ✓ |
| 44 | 02-§4.4-03 | Yanlis token: platform kabul etmez, iade eder | §4.4 | §5.3 | ✓ |
| 45 | 02-§4.4-04 | Gecikmeli odeme: islem iptal, adres izlenir, aliciya iade | §4.4 | §5.4 | ✓ |
| 46 | 02-§4.5-01 | Saticiya odeme: item teslimi dogrulandiktan sonra | §4.5 | §2.4 | ✓ |
| 47 | 02-§4.5-02 | Platform komisyonu keser, kalani saticiya gonderir | §4.5 | §2.4 (adim 2-4) | ✓ |
| 48 | 02-§4.5-03 | Satici profilinde varsayilan adres tanimlar, islem baslatirken farkli adres girebilir | §4.5 | §2.2 (adim 14) | ✓ |
| 49 | 02-§4.6-01 | Iade kapsami: tam iade, komisyon dahil | §4.6 | §3.5 (adim 5), §4.4 | ✓ |
| 50 | 02-§4.6-02 | Aliciya iade tutari: fiyat + komisyon - gas fee | §4.6 | §3.5 (adim 5), §4.4 (adim 4) | ✓ |
| 51 | 02-§4.6-03 | Iade adresi: alicinin islem kabul ederken belirledigi adres | §4.6 | §3.2 (adim 4) | ✓ |
| 52 | 02-§4.6-04 | Gas fee iade tutarindan dusulur | §4.6 | §5.1, §5.2, §5.3, §5.4 | ✓ |
| 53 | 02-§4.6-05 | Platform maliyeti sifir | §4.6 | — | ⚠ |
| 54 | 02-§4.7-01 | Alicinin odeme gas feesi: alici karsilar | §4.7 | §3.4 | ✓ |
| 55 | 02-§4.7-02 | Saticiya gonderim gas feesi: komisyondan dusulur | §4.7 | §2.4 (adim 3) | ✓ |
| 56 | 02-§4.7-03 | Iade gas feeleri: iade tutarindan dusulur | §4.7 | §5.1-5.4 | ✓ |
| 57 | 02-§4.7-04 | Koruma esigi: gas fee komisyonun belirli % asarsa saticidan kesilir | §4.7 | §2.4 (adim 3) | ✓ |
| 58 | 02-§4.7-05 | Varsayilan esik: %10 | §4.7 | §2.4 (adim 3) | ✓ |
| 59 | 02-§4.7-06 | Esik esnekligi: admin degistirebilir | §4.7 | — | ⚠ |

#### §5 Komisyon

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 60 | 02-§5-01 | Komisyonu odeyen: alici | §5 | §3.2 (adim 1) | ✓ |
| 61 | 02-§5-02 | Alicinin odedigi toplam: item fiyati + komisyon | §5 | §3.2 (adim 1), §3.4 (adim 3) | ✓ |
| 62 | 02-§5-03 | Varsayilan oran: %2 | §5 | — | ⚠ |
| 63 | 02-§5-04 | Oran esnekligi: admin degistirebilir | §5 | — | ⚠ |

#### §6 Alici Belirleme

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 64 | 02-§6.1-01 | Yontem 1: satici alicinin Steam ID girer, sadece o kisi kabul edebilir | §6.1 | §2.2 (adim 13), §3.2 (adim 2) | ✓ |
| 65 | 02-§6.1-02 | Alici kayitliysa platform bildirimi | §6.1 | §2.2 (adim 19) | ✓ |
| 66 | 02-§6.1-03 | Alici kayitli degilse saticiya davet linki | §6.1 | §2.2 (adim 19) | ✓ |
| 67 | 02-§6.2-01 | Yontem 2: acik link, ilk kabul eden alici olur, tek kullanimlik | §6.2 | §2.2 (adim 13), §3.2 (adim 3) | ✓ |
| 68 | 02-§6.2-02 | Admin tarafindan aktif/pasif yapilabilir | §6.2 | §8.4 | ✓ |

#### §7 Iptal

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 69 | 02-§7-01 | Odeme oncesi satici iptal edebilir, item iade | §7 | §2.5 | ✓ |
| 70 | 02-§7-02 | Odeme oncesi alici iptal edebilir, item varsa saticiya iade | §7 | §3.3 | ✓ |
| 71 | 02-§7-03 | Alici odemeyse hicbir taraf tek tarafli iptal edemez | §7 | §2.5 (adim 3), §3.3 (adim 3) | ✓ |
| 72 | 02-§7-04 | Iptal sonrasi cooldown | §7 | §2.2 (adim 3) | ✓ |
| 73 | 02-§7-05 | Iptal sebebi zorunlu | §7 | §2.5 (adim 4), §3.3 (adim 4) | ✓ |

#### §8-9 Islem Limitleri ve Item Yonetimi

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 74 | 02-§8-01 | Min/max islem tutari: admin belirlenir | §8 | §2.2 (adim 11) | ✓ |
| 75 | 02-§8-02 | Eszamanli aktif islem limiti | §8 | §2.2 (adim 2) | ✓ |
| 76 | 02-§8-03 | Yeni hesap islem limiti | §8 | §2.2 (adim 4) | ✓ |
| 77 | 02-§9-01 | Envanter okuma: platform satici envanterini okur | §9 | §2.2 (adim 5-6) | ✓ |
| 78 | 02-§9-02 | Item dogrulama: var ve tradeable | §9 | §2.2 (adim 8) | ✓ |
| 79 | 02-§9-03 | Transfer sirasi: once item, sonra odeme | §9 | §2.3 → §3.4 sirasi | ✓ |

#### §10-11 Dispute ve Giris

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 80 | 02-§10.1-01 | Odeme itirazi: blockchain otomatik dogrulama | §10.1 | §6.1 | ✓ |
| 81 | 02-§10.1-02 | Teslim itirazi: Steam otomatik dogrulama | §10.1 | §6.2 | ✓ |
| 82 | 02-§10.1-03 | Yanlis item itirazi: sistem otomatik karsilastirir | §10.1 | §6.3 | ✓ |
| 83 | 02-§10.2-01 | Dispute yalnizca alici acabilir | §10.2 | §6 (not) | ✓ |
| 84 | 02-§10.2-02 | Dispute timeout surelerini durdurmaz | §10.2 | §6 (not) | ✓ |
| 85 | 02-§10.2-03 | Rate limiting: ayni turde dispute tekrar acilamaz | §10.2 | — | ✗ |
| 86 | 02-§10.3-01 | Eskalasyon: otomatik cozum yetersizse admine eskalasyon | §10.3 | §6.4 | ✓ |
| 87 | 02-§11-01 | Giris yontemi: Steam ile giris | §11 | §2.1 | ✓ |
| 88 | 02-§11-02 | Steam Mobile Authenticator zorunlu | §11 | §2.1 (adim 6) | ✓ |

#### §12-23 Diger Gereksinimler

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 89 | 02-§12.2-01 | Alici iade adresi: islem kabul ederken belirler, zorunlu | §12.2 | §3.2 (adim 4) | ✓ |

**Toplam: 89 oge (79 ✓, 7 ⚠, 3 ✗)**

---

### Envanter — 01_PROJECT_VISION

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 1 | 01-§1-01 | Skinora marketplace degil, escrow servisi | §1 | §1 (genel yaklasim) | ✓ |
| 2 | 01-§3.3-01 | Aktor: Satici — islemi baslatir, itemi emanet eder, odemeyi alir | §3.3 | §1.1, §2 | ✓ |
| 3 | 01-§3.3-02 | Aktor: Alici — islemi kabul eder, odemeyi gonderir, itemi teslim alir | §3.3 | §1.1, §3 | ✓ |
| 4 | 01-§3.3-03 | Aktor: Platform — escrow aracisi, dogrulamalari yapar | §3.3 | §1.1 | ✓ |
| 5 | 01-§3.3-04 | Aktor: Admin — ayarlari yonetir, flaglenenmis islemleri inceler | §3.3 | §1.1, §8 | ✓ |
| 6 | 01-§4.1-01 | Timeout/hatada varliklar otomatik iade | §4.1 | §4.1-4.4, §3.5 | ✓ |
| 7 | 01-§4.1-02 | Otomasyon: odeme ve teslim dogrulama otomatik | §4.1 | §3.4, §3.5 | ✓ |
| 8 | 01-§4.1-03 | Izlenebilirlik: her islem kaydedilir | §4.1 | — (dogrudan akinsta yok ama akisin dogasi geregi her adim takip edilir) | ✓ |
| 9 | 01-§5.1-01 | Gelir: %2 komisyon alicidan | §5.1 | §3.2 (adim 1) | ✓ |
| 10 | 01-§6.1-01 | MVP: CS2, tek item, tek stablecoin, web platformu | §6.1 | §2.2 (tek item, stablecoin secimi) | ✓ |
| 11 | 01-§6.1-02 | Tron TRC-20 tabanli odeme | §6.1 | §3.4 (adim 3) | ✓ |
| 12 | 01-§8-01 | Adalet ilkesi: aksaklikta varliklar iade edilir | §8 | §4.1-4.4 | ✓ |

**Toplam: 12 oge (12 ✓, 0 ⚠, 0 ✗)**

---

### Envanter — 10_MVP_SCOPE

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 1 | 10-§2.1-01 | Temel escrow akisi 8 adim | §2.1 | §2-§3 | ✓ |
| 2 | 10-§2.2-01 | USDT ve USDC destegi Tron TRC-20 | §2.2 | §2.2, §3.4 | ✓ |
| 3 | 10-§2.2-02 | Odeme edge case yonetimi | §2.2 | §5 | ✓ |
| 4 | 10-§2.2-03 | Gas fee yonetimi | §2.2 | §2.4 (adim 3) | ✓ |
| 5 | 10-§2.3-01 | Her adim icin ayri timeout | §2.3 | §4 | ✓ |
| 6 | 10-§2.4-01 | Steam ile giris + Mobile Authenticator zorunlu | §2.4 | §2.1 | ✓ |
| 7 | 10-§2.4-02 | Profil ve cuzdan adresleri yonetimi (satici + alici iade) | §2.4 | §9, §3.2 (adim 4) | ✓ |
| 8 | 10-§2.4-03 | Hesap silme/deaktif etme | §2.4 | §10 | ✓ |
| 9 | 10-§2.4-04 | Kullanici itibar skoru | §2.4 | §9.3 | ✓ |
| 10 | 10-§2.6-01 | Alici odeme yapmadiysa satici iptal edebilir | §2.6 | §2.5 | ✓ |
| 11 | 10-§2.6-02 | Iptal sebebi zorunlu | §2.6 | §2.5 (adim 4), §3.3 (adim 4) | ✓ |
| 12 | 10-§2.7-01 | Dispute: otomatik dogrulama + eskalasyon | §2.7 | §6 | ✓ |
| 13 | 10-§2.8-01 | Wash trading korumasi | §2.8 | §7.3 (not) | ✓ |
| 14 | 10-§2.8-02 | Coklu hesap tespiti (cuzdan + IP) | §2.8 | §7.4 | ✓ |
| 15 | 10-§2.11-01 | Admin: super admin + ozel rol gruplari | §2.11 | §8.6 | ✓ |
| 16 | 10-§2.14-01 | Downtime yonetimi: timeout dondurma | §2.14 | §11 | ✓ |
| 17 | 10-§2.15-01 | 4 dil destegi (Ingilizce, Cince, Ispanyolca, Turkce) | §2.15 | — | ⚠ |
| 18 | 10-§2.15-02 | Kullanici sozlesmesi / ToS | §2.15 | §2.1 (adim 8) | ✓ |

**Toplam: 18 oge (17 ✓, 1 ⚠, 0 ✗)**

---

### Envanter — Hedef (03_USER_FLOWS ic tutarlilik)

| # | ID | Oge Ozeti | Kaynak Bolum | Durum |
|---|---|---|---|---|
| 1 | 03-§1.2-01 | 13 islem durumu tanimli | §1.2 | ✓ |
| 2 | 03-§1.2-02 | Dispute ayri islem durumu degil, ayri bayrak | §1.2 | ✓ |
| 3 | 03-§2.1-01 | Ilk giris: Steam ile giris + Mobile Authenticator kontrolu | §2.1 | ✓ |
| 4 | 03-§2.2-01 | Islem baslatma: 20 adimli normal akis | §2.2 | ✓ |
| 5 | 03-§2.3-01 | Item emaneti: trade offer + dogrulama | §2.3 | ✓ |
| 6 | 03-§2.4-01 | Saticiya odeme: komisyon + gas fee + gonderim | §2.4 | ✓ |
| 7 | 03-§2.5-01 | Satici iptal akisi | §2.5 | ✓ |
| 8 | 03-§3.1-01 | Alici ilk giris: "satici akisi ile ayni" referansi | §3.1 | ⚠ |
| 9 | 03-§3.2-01 | Islemi kabul etme akisi | §3.2 | ✓ |
| 10 | 03-§3.3-01 | Alici iptal akisi | §3.3 | ✓ |
| 11 | 03-§3.4-01 | Odeme gonderme akisi | §3.4 | ✓ |
| 12 | 03-§3.5-01 | Item teslim alma akisi | §3.5 | ✓ |
| 13 | 03-§4-01 | 4 ayri timeout akisi + uyari mekanizmasi | §4 | ✓ |
| 14 | 03-§5-01 | 4 odeme edge case akisi | §5 | ✓ |
| 15 | 03-§6-01 | 4 dispute akisi (odeme, teslim, yanlis item, eskalasyon) | §6 | ✓ |
| 16 | 03-§7-01 | 4 fraud/flag akisi | §7 | ✓ |
| 17 | 03-§8-01 | 6 admin akisi | §8 | ✓ |
| 18 | 03-§9-01 | Profil ve cuzdan yonetimi akislari | §9 | ✓ |
| 19 | 03-§10-01 | Hesap yonetimi akislari (deaktif + silme) | §10 | ✓ |
| 20 | 03-§12-01 | Bildirim ozeti: satici, alici, admin bildirimleri tablolari — §2-§11 ile tutarlilik | §12 | ⚠ |

**Toplam: 20 oge (17 ✓, 2 ⚠, 1 ✗)**

---

## Bulgular

| # | Envanter ID | Tur | Seviye | Bulgu | Oneri |
|---|---|---|---|---|---|
| 1 | 02-§2.2-04 | GAP | High | "Islem detaylari olusturulduktan sonra degistirilemez" kurali 03'te hic belirtilmiyor. Satici islem baslatma akisinda (§2.2) bu kisitlama acikcasi yazilmamis. | §2.2 sonuna veya islem ozeti adimina "Islem olusturulduktan sonra detaylar degistirilemez" notu eklenmeli |
| 2 | 02-§10.2-03 | GAP | High | Dispute rate limiting kurali (ayni turde dispute tekrar acilamaz) 03'teki dispute akislarinda (§6) hic belirtilmiyor | §6 girisindeki notlara "Bir islem icin ayni turde dispute tekrar acilamaz" eklenmeli |
| 3 | 02-§3.4-02 | Kismi | Medium | Timeout uyari esiginin "admin tarafindan oran olarak ayarlanir" detayi 03 §4.5'te yok. §4.5 sadece "admin tarafindan belirlenen orani" ifadesini kullaniyor, "oran olarak" kismini belirtiyor ama "§16.2" referansi yok | Mevcut ifade yeterli duzeyde — 02 ile tutarli. Ancak "oran olarak" ifadesi acikca belirtilmeli |
| 4 | 02-§4.3-02 | Kismi | Low | "Platform fiyata mudahale etmez" ifadesi 03'te dogrudan belirtilmiyor ama akista da mudahale eden bir adim yok. Akis bu kuralla uyumlu. | Farkindalilik yeterli — bu kural 02'nin sorumlulugunda, 03'te akis zaten uyumlu |
| 5 | 02-§4.3-03 | Kismi | Low | "MVP'de kullaniciya piyasa fiyati gosterilmez" — 03'te fiyat gosterme adimi yok, akis uyumlu ama kural belirtilmiyor | Farkindalilik yeterli — bu UI detayi 04'un sorumlulugunda |
| 6 | 02-§4.6-05 | Kismi | Low | "Platform maliyeti sifir" ilkesi 03'te dogrudan belirtilmiyor ama iade akislarinda gas fee her zaman kullanicidan karsilanarak bu ilke korunuyor | Farkindalilik yeterli — akis bu ilkeyle uyumlu |
| 7 | 02-§4.7-06 | Kismi | Low | Gas fee koruma esiginin admin tarafindan degistirilebilirligi 03 §2.4'te belirtilmiyor ama §8.4'te admin parametre listesinde "gas fee koruma esigi" var | Farkindalilik yeterli — admin parametreleri §8.4'te kapsanmis |
| 8 | 02-§5-03 | Kismi | Low | Varsayilan komisyon orani (%2) 03'te hicbir yerde belirtilmiyor. §3.2'de sadece "komisyon (orn: 2 USDT)" ornegi var | Farkindalilik yeterli — oranin kendisi 02'nin sorumlulugu, 03 akis dokumani |
| 9 | 02-§5-04 | Kismi | Low | Komisyon oraninin admin tarafindan degistirilebilirligi 03'te belirtilmiyor ama §8.4'te "komisyon orani" admin parametresi listede var | Farkindalilik yeterli — §8.4'te kapsanmis |
| 10 | 02-§2.2-06 | Kismi | Low | "Tum CS2 item turleri desteklenir" ifadesi 03'te dogrudan belirtilmiyor ama kapsami sinirlamayan bir akis tanimlaniyor | Farkindalilik yeterli — akis CS2 item turlerini sinirlamiyor |
| 11 | 10-§2.15-01 | Kismi | Low | 4 dil destegi 03'te belirtilmiyor. Akis dokumani dil desteginden bagmsiz ama bilgi bütünlüğü icin belirtilebilir | Farkindalilik yeterli — dil destegi 04 ve 09 dokumanlarin sorumlulugu |
| 12 | 03-§3.1-01 | Ic Tutarlilik | Medium | §3.1 "Satici akisi ile ayni (bkz. 2.1)" diyor ve ardindan 6 adimlik bir akis tanimliyor. Bu 6 adimlik akis §2.1'den farkli — §2.1'de Mobile Authenticator kontrolu, ToS adimi gibi adimlar var ama §3.1'de bunlar belirtilmiyor. Okuyucu "ayni" referansi nedeniyle §2.1'in TAMAMI gecerli mi, yoksa sadece listelenen 6 adim mi gecerli belirsiz | §3.1'deki "Satici akisi ile ayni" ifadesi korunarak, alici akisindaki ek adimlarin ayrica belirtilmesi yerine, "Satici akisi ile ayni (bkz. 2.1). Tek fark: alici genellikle davet linki uzerinden gelir, islemi kabul ettikten sonra islem detay sayfasina yonlendirilir" seklinde netlestirilmeli |
| 13 | 03-§12-01 | Ic Tutarlilik | High | Bildirim ozet tablosunda (§12) eksiklikler var: (a) §12.1 satici bildirimlerinde "Item'iniz iade edildi" bildirimi eksik — §4.3 ve §4.4'te item satıcıya iade ediliyor, (b) §12.3 admin bildirimlerinde "Coklu hesap tespiti" bildirimi eksik — §7.4'te admin'e bildirim gidiyor, (c) §12.1'de "Saticiya odeme basarisiz" bildirimi eksik — §2.4 adim 4'te admin'e bildirim gidiyor ama satici tablosunda belirtilmiyor | §12'deki bildirim tablolari §2-§11'deki tum bildirim tetikleyicileriyle eslestirilmeli. Eksik bildirimleri ekle |
| 14 | 03-§2.2-01 | Kalite | Medium | §2.2 adim 17'de piyasa fiyatindan sapma esiginin admin tarafindan belirlendigi belirtiliyor ama "admin'in belirledigi esik" ifadesi hangi parametreye isaret ettigini aciklamiyor. §8.4'te "fraud sapma esigi" olarak listelenmis — cakisma yok ama cross-reference faydali olurdu | Bulgu notu olarak kalmasi yeterli — referans eklenmesi opsiyonel |

---

## Aksiyon Plani

**Critical:**
- (Yok)

**High:**
- [x] Bulgu #1: §2.2'ye "islem detaylari degistirilemez" kuralini ekle → **UYGULANDI** (v1.5)
- [x] Bulgu #2: §6 girisindeki notlara dispute rate limiting kuralini ekle → **UYGULANDI** (v1.5)
- [x] Bulgu #13: §12 bildirim tablolarindaki eksiklikleri tamamla → **UYGULANDI** (v1.5): satici "item iade edildi", admin "coklu hesap tespiti" ve "odeme gonderim hatasi" bildirimleri eklendi

**Medium:**
- [x] Bulgu #12: §3.1'deki alici giris akisini netlestir → **UYGULANDI** (v1.5): Mobile Authenticator ve ToS referanslari acikca eklendi
- [ ] Bulgu #3: §4.5'te timeout uyari esigi ifadesi zaten "admin tarafindan belirlenen orani" olarak yer aliyor — 02 ile tutarli, ek duzeltme gerekmedi
- [ ] Bulgu #14: Farkindalilik notu — opsiyonel cross-reference, duzeltme gerekmedi

**Low:**
- Bulgu #4, #5, #6, #7, #8, #9, #10, #11: Farkindalilik yeterli — akislar kaynak dokuman kurallariyla uyumlu

---

*Skinora — 03 User Flows Audit Report*
