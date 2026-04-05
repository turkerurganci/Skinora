# Audit Raporu — 10_MVP_SCOPE.md

**Tarih:** 2026-03-22
**Hedef:** 10_MVP_SCOPE.md (v1.1)
**Bağlam:** 01_PROJECT_VISION.md (v1.1), 02_PRODUCT_REQUIREMENTS.md (v2.4)
**Odak:** Tam denetim

---

## Envanter Özeti

| Kaynak | Toplam Öğe | ✓ | ⚠ | ✗ |
|---|---|---|---|---|
| 01 | 34 | 31 | 2 | 1 |
| 02 | 124 | 103 | 12 | 9 |
| Hedef (iç) | 28 | 26 | 2 | 0 |
| **Toplam** | **186** | **160** | **16** | **10** |

---

## Envanter ve Eşleştirme Detayı

### Envanter — 01_PROJECT_VISION

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 1 | 01-§1-01 | CS2 item escrow servisi | §1 | §1 | ✓ |
| 2 | 01-§1-02 | Marketplace değil — fiyat, listeleme, eşleştirme yok | §1 | §1 | ✓ |
| 3 | 01-§3.3-01 | Satıcı aktörü | §3.3 | §2.1 | ✓ |
| 4 | 01-§3.3-02 | Alıcı aktörü | §3.3 | §2.1 | ✓ |
| 5 | 01-§3.3-03 | Platform aktörü | §3.3 | §2.1 | ✓ |
| 6 | 01-§3.3-04 | Admin aktörü | §3.3 | §2.11 | ✓ |
| 7 | 01-§5.1-01 | %2 komisyon, alıcıdan | §5.1 | §4 | ✓ |
| 8 | 01-§5.1-02 | Ek gelir kanalları MVP sonrası | §5.1 | §3.4 | ✓ |
| 9 | 01-§5.2-01 | CS2 skin pazarı büyüklüğü | §5.2 | — | ✓ (kapsam dışı — iş bağlamı, scope'da yeri yok) |
| 10 | 01-§5.2-02 | Diğer Steam oyunlarına genişleme potansiyeli | §5.2 | §6 | ✓ |
| 11 | 01-§6.1-01 | MVP: CS2, Tron TRC-20, tek item, USDT/USDC, web | §6.1 | §4 | ✓ |
| 12 | 01-§6.2-01 | Orta vadeli: Diğer Steam oyunları | §6.2 | §3.1, §6 | ✓ |
| 13 | 01-§6.2-02 | Orta vadeli: Mobil uygulama | §6.2 | §3.3, §6 | ✓ |
| 14 | 01-§6.2-03 | Orta vadeli: Kullanıcı yorum/değerlendirme | §6.2 | §3.3, §6 | ✓ |
| 15 | 01-§6.2-04 | Orta vadeli: Ek gelir kanalları | §6.2 | §3.4, §6 | ✓ |
| 16 | 01-§6.2-05 | Orta vadeli: Fiyat referansı gösterimi | §6.2 | §3.3, §6 | ✓ |
| 17 | 01-§6.3-01 | Uzun vadeli: Çoklu item ve barter | §6.3 | §3.1, §6 | ✓ |
| 18 | 01-§6.3-02 | Uzun vadeli: Ek blockchain ağları | §6.3 | §3.2, §6 | ✓ |
| 19 | 01-§6.3-03 | Uzun vadeli: Yüksek tutarlı KYC | §6.3 | §3.5, §6 | ✓ |
| 20 | 01-§6.3-04 | Uzun vadeli: Trade lock desteği | §6.3 | §3.1, §6 | ✓ |
| 21 | 01-§7-01 | Büyüme metrikleri: haftalık/aylık işlem, yeni kullanıcı, geri dönüş | §7 | §5 | ✓ |
| 22 | 01-§7-02 | Güvenilirlik metrikleri: başarılı tamamlanma, doğrulama hata oranı, dispute oranı | §7 | §5 | ✓ |
| 23 | 01-§7-03 | Gelir metrikleri: aylık komisyon geliri | §7 | §5 | ✓ |
| 24 | 01-§7-04 | Güven metrikleri: tekrar kullanım oranı | §7 | §5 | ✓ |
| 25 | 01-§7-05 | Hedef rakamlar MVP sonrası belirlenecek | §7 | §5 | ✓ |
| 26 | 01-§8-01 | Güvenlik öncelikli ilke | §8 | — | ✓ (ilkeler scope belgesi kapsamı dışı) |
| 27 | 01-§8-02 | Otomasyon esastır ilkesi | §8 | — | ✓ (ilkeler scope belgesi kapsamı dışı) |
| 28 | 01-§8-03 | Adalet sağlanır ilkesi | §8 | — | ✓ (ilkeler scope belgesi kapsamı dışı) |
| 29 | 01-§8-04 | Şeffaflık ilkesi | §8 | — | ✓ (ilkeler scope belgesi kapsamı dışı) |
| 30 | 01-§8-05 | Platform tarafsızdır ilkesi | §8 | — | ✓ (ilkeler scope belgesi kapsamı dışı) |
| 31 | 01-§6.1-01a | MVP'de fiyat referansı gösterilmez (§4.3 ile uyumlu) | §6.1 | §3.3 "Kullanıcıya piyasa fiyatı gösterimi" | ⚠ |
| 32 | 01-§5.3-03 | Kripto ödeme ile global erişim, chargeback riski yok | §5.3 | §4 | ✓ |
| 33 | 01-§6.1-01b | MVP'de platform cüzdanı yok (dış cüzdan modeli) | §6.1 (implicit) | §3.2 | ✓ |
| 34 | 01-§5.1-01a | Fiat ödeme desteği yok — chargeback riski | §5.3 | §3.2 | ✓ |

**Toplam: 34 öğe (31 ✓, 2 ⚠, 1 ✗)**

> ⚠ #31: 01 §6.1 "fiyat referansı" derken 10 §3.3 "piyasa fiyatı gösterimi" diyor — aynı anlama geliyor ama terimler farklı. Minor.

---

### Envanter — 02_PRODUCT_REQUIREMENTS

#### §2 İşlem Akışı

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 1 | 02-§2.1-01 | Adım 1: İşlem oluşturma (item seçimi, stablecoin, fiyat, timeout) | §2.1 | §2.1 | ✓ |
| 2 | 02-§2.1-02 | Adım 2: Alıcı kabulü | §2.1 | §2.1 | ✓ |
| 3 | 02-§2.1-03 | Adım 3: Item emaneti (Steam trade offer) | §2.1 | §2.1 | ✓ |
| 4 | 02-§2.1-04 | Adım 4: Ödeme (blockchain) | §2.1 | §2.1 | ✓ |
| 5 | 02-§2.1-05 | Adım 5: Ödeme doğrulama (otomatik) | §2.1 | §2.1 | ✓ |
| 6 | 02-§2.1-06 | Adım 6: Item teslimi (Steam trade offer) | §2.1 | §2.1 | ✓ |
| 7 | 02-§2.1-07 | Adım 7: Teslim doğrulama (otomatik) | §2.1 | §2.1 | ✓ |
| 8 | 02-§2.1-08 | Adım 8: Satıcıya ödeme (komisyon düşülerek) | §2.1 | §2.1 | ✓ |
| 9 | 02-§2.2-01 | Her işlem tek bir item | §2.2 | §4 "Tek item per işlem" | ✓ |
| 10 | 02-§2.2-02 | Sadece kripto ödeme, barter yok | §2.2 | §3.1, §4 | ✓ |
| 11 | 02-§2.2-03 | İşlemi satıcı başlatır | §2.2 | §2.1 | ✓ |
| 12 | 02-§2.2-04 | İşlem detayları değiştirilemez | §2.2 | — | ✓ (detay seviyesi — scope'da yeri yok) |
| 13 | 02-§2.2-05 | Sadece tradeable item'lar | §2.2 | §2.9, §4 | ✓ |
| 14 | 02-§2.2-06 | Tüm CS2 item türleri desteklenir | §2.2 | §2.9 | ✓ |

#### §3 Timeout

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 15 | 02-§3.1-01 | Alıcı kabulü timeout — admin ayarlanabilir | §3.1 | §2.3 | ✓ |
| 16 | 02-§3.1-02 | Satıcı trade offer timeout — admin ayarlanabilir | §3.1 | §2.3 | ✓ |
| 17 | 02-§3.1-03 | Ödeme timeout — admin min-max, satıcı seçer | §3.1 | §2.3 | ✓ |
| 18 | 02-§3.1-04 | Teslim trade offer timeout — admin ayarlanabilir | §3.1 | §2.3 | ✓ |
| 19 | 02-§3.2-01 | Timeout dolarsa işlem iptal | §3.2 | §2.3 | ✓ |
| 20 | 02-§3.2-02 | Varlıklar otomatik iade | §3.2 | §2.3 | ✓ |
| 21 | 02-§3.2-03 | Ödeme timeout'unda adres izlemeye devam, gecikmeli ödeme iadesi | §3.2 | — | ⚠ |
| 22 | 02-§3.3-01 | Platform bakımında timeout dondurma | §3.3 | §2.14 | ✓ |
| 23 | 02-§3.3-02 | Steam kesintisinde timeout dondurma | §3.3 | §2.14 | ✓ |
| 24 | 02-§3.3-03 | Blockchain altyapı sağlıksızsa ödeme timeout dondurma | §3.3 | — | ✗ |
| 25 | 02-§3.3-04 | Bakım/kesinti bitince timeout devam | §3.3 | — | ✓ (§2.14 implied) |
| 26 | 02-§3.3-05 | Planlı bakım öncesi bildirim | §3.3 | §2.14 | ✓ |
| 27 | 02-§3.4-01 | Timeout yaklaşıyor uyarısı | §3.4 | — | ⚠ |

#### §4 Ödeme

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 28 | 02-§4.1-01 | Kripto (stablecoin) | §4.1 | §2.2 | ✓ |
| 29 | 02-§4.1-02 | USDT ve USDC | §4.1 | §2.2, §4 | ✓ |
| 30 | 02-§4.1-03 | Tron (TRC-20) | §4.1 | §2.2, §4 | ✓ |
| 31 | 02-§4.1-04 | Dış cüzdan modeli | §4.1 | §2.2, §4 | ✓ |
| 32 | 02-§4.1-05 | Her işlem benzersiz ödeme adresi | §4.1 | §2.2 | ✓ |
| 33 | 02-§4.1-06 | Blockchain otomatik doğrulama | §4.1 | §2.2 | ✓ |
| 34 | 02-§4.2-01 | Satıcı USDT veya USDC seçer | §4.2 | §2.1 | ✓ |
| 35 | 02-§4.3-01 | Satıcı stablecoin miktarı olarak girer | §4.3 | — | ✓ (detay) |
| 36 | 02-§4.3-02 | Platform fiyata müdahale etmez | §4.3 | — | ✓ (detay) |
| 37 | 02-§4.3-03 | MVP'de kullanıcıya piyasa fiyatı gösterilmez | §4.3 | §3.3 | ✓ |
| 38 | 02-§4.3-04 | Arka planda piyasa fiyat verisi fraud tespiti için | §4.3 | §2.8 | ✓ |
| 39 | 02-§4.4-01 | Eksik tutar — kabul etmez, iade | §4.4 | §2.2 "eksik tutar" | ✓ |
| 40 | 02-§4.4-02 | Fazla tutar — doğru tutarı kabul, fazla iade | §4.4 | §2.2 "fazla tutar" | ✓ |
| 41 | 02-§4.4-03 | Yanlış token (TRC-20) — iade | §4.4 | §2.2 "yanlış token" | ✓ |
| 42 | 02-§4.4-04 | Desteklenmeyen token — manuel inceleme | §4.4 | — | ✗ |
| 43 | 02-§4.4-05 | Timeout sonrası gecikmeli ödeme — iade | §4.4 | §2.2 "gecikmeli ödeme" | ✓ |
| 44 | 02-§4.4-06 | Çoklu/parçalı ödeme — tek seferde gönderim | §4.4 | — | ✗ |
| 45 | 02-§4.5-01 | Satıcıya ödeme: item teslimi doğrulandıktan sonra | §4.5 | §2.1 | ✓ |
| 46 | 02-§4.5-02 | Komisyon kesilir, kalan gönderilir | §4.5 | §2.1 | ✓ |
| 47 | 02-§4.6-01 | Tam iade, komisyon dahil | §4.6 | — | ⚠ |
| 48 | 02-§4.7-01 | Gas fee yönetimi | §4.7 | §2.2 | ⚠ |

#### §5 Komisyon

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 49 | 02-§5-01 | Komisyonu alıcı öder | §5 | §4 implied | ✓ |
| 50 | 02-§5-02 | Varsayılan %2 | §5 | §4 | ✓ |
| 51 | 02-§5-03 | Admin tarafından değiştirilebilir | §5 | §2.11 | ✓ |
| 52 | 02-§5-04 | MVP'de sadece komisyon geliri | §5 | §4 | ✓ |

#### §6 Alıcı Belirleme

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 53 | 02-§6.1-01 | Steam ID ile belirleme (aktif) | §6.1 | §2.5 | ✓ |
| 54 | 02-§6.1-02 | Sadece belirtilen kullanıcı kabul edebilir | §6.1 | §2.5 | ✓ |
| 55 | 02-§6.1-03 | Kayıtlı alıcıya platform bildirimi | §6.1 | §2.5 | ✓ |
| 56 | 02-§6.1-04 | Kayıtlı olmayan alıcıya satıcı davet linki | §6.1 | §2.5 | ✓ |
| 57 | 02-§6.2-01 | Açık link yöntemi (pasif) | §6.2 | §2.5 | ✓ |
| 58 | 02-§6.2-02 | İlk kabul eden alıcı olur, tek kullanımlık | §6.2 | — | ✓ (detay) |
| 59 | 02-§6.2-03 | Admin tarafından aktif/pasif yapılabilir | §6.2 | §2.5 | ✓ |

#### §7 İptal

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 60 | 02-§7-01 | Ödeme öncesi — satıcı iptal edebilir | §7 | §2.6 | ✓ |
| 61 | 02-§7-02 | Ödeme öncesi — alıcı iptal edebilir (item varsa satıcıya iade) | §7 | §2.6 | ✓ |
| 62 | 02-§7-03 | Alıcı ödediyse tek taraflı iptal yok | §7 | §2.6 | ✓ |
| 63 | 02-§7-04 | Teslim offer timeout → iptal + iade | §7 | §2.3 | ✓ |
| 64 | 02-§7-05 | İptal sonrası cooldown (admin ayarlı) | §7 | §2.6 | ✓ |
| 65 | 02-§7-06 | İptal sebebi zorunlu | §7 | §2.6 | ✓ |
| 66 | 02-§7-07 | Admin doğrudan iptal (CREATED–PAYMENT_CONFIRMED arası + FLAGGED) | §7 | — | ✗ |
| 67 | 02-§7-08 | Admin doğrudan iptal — ITEM_DELIVERED sonrası kısıtlama | §7 | — | ✗ |
| 68 | 02-§7-09 | Admin emergency hold (timeout durur, akış bekler) | §7 | — | ✗ |
| 69 | 02-§7-10 | Admin emergency hold — ITEM_DELIVERED kısıtı | §7 | — | ✗ |

#### §8 İşlem Limitleri

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 70 | 02-§8-01 | Min/max işlem tutarı (admin ayarlı) | §8 | — | ⚠ |
| 71 | 02-§8-02 | Eşzamanlı aktif işlem limiti | §8 | — | ⚠ |
| 72 | 02-§8-03 | Yeni hesap işlem limiti | §8 | §2.8 | ✓ |

#### §9 Item Yönetimi

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 73 | 02-§9-01 | Steam envanter okuma | §9 | §2.9 | ✓ |
| 74 | 02-§9-02 | Item doğrulama (var + tradeable) | §9 | §2.9 | ✓ |
| 75 | 02-§9-03 | Tüm CS2 item türleri | §9 | §2.9 | ✓ |
| 76 | 02-§9-04 | Sadece tradeable item'lar | §9 | §2.9 | ✓ |

#### §10 Dispute

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 77 | 02-§10.1-01 | Ödeme itirazı — blockchain otomatik doğrulama | §10.1 | §2.7 | ✓ |
| 78 | 02-§10.1-02 | Teslim itirazı — Steam otomatik doğrulama | §10.1 | §2.7 | ✓ |
| 79 | 02-§10.1-03 | Yanlış item itirazı — otomatik karşılaştırma | §10.1 | §2.7 | ✓ |
| 80 | 02-§10.2-01 | Sadece alıcı dispute açabilir | §10.2 | — | ⚠ |
| 81 | 02-§10.3-01 | Satıcı payout sorunu — blockchain doğrulama + retry | §10.3 | — | ⚠ |
| 82 | 02-§10.4-01 | Eskalasyon yolu var, detaylar ileriye bırakıldı | §10.4 | §2.7, §3.6 | ✓ |

#### §11 Kullanıcı Kimlik ve Giriş

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 83 | 02-§11-01 | Steam ile giriş | §11 | §2.4 | ✓ |
| 84 | 02-§11-02 | KYC yok (MVP) | §11 | §3.5, §4 | ✓ |
| 85 | 02-§11-03 | Steam Mobile Authenticator zorunlu | §11 | §2.4, §3.6 | ✓ |

#### §12 Cüzdan Adresi Güvenliği

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 86 | 02-§12.1-01 | Satıcı profil varsayılan adresi | §12.1 | §2.4 | ✓ |
| 87 | 02-§12.1-02 | İşlem bazlı farklı adres | §12.1 | — | ✓ (detay) |
| 88 | 02-§12.1-04 | Adres değişikliğinde Steam onayı | §12.1 | §2.4 | ✓ |
| 89 | 02-§12.2-01 | Alıcı iade adresi profilde | §12.2 | §2.4 | ✓ |
| 90 | 02-§12.3-01 | TRC-20 format doğrulama | §12.3 | — | ✓ (detay) |
| 91 | 02-§12.3-04 | Sanctions screening (cüzdan adresi bazlı) | §12.3 | — | ⚠ |

#### §13 İtibar Skoru

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 92 | 02-§13-01 | İtibar sistemi aktif | §13 | §2.4 | ✓ |
| 93 | 02-§13-02 | Kriterler: işlem sayısı, başarı oranı, hesap yaşı | §13 | §2.4 | ✓ |
| 94 | 02-§13-05 | Kullanıcı yorumu MVP'de yok | §13 | §3.3, §4 | ✓ |

#### §14 Fraud / Abuse

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 95 | 02-§14.1-01 | Wash trading — 1 ay kuralı | §14.1 | §2.8 | ✓ |
| 96 | 02-§14.2-01 | İptal limiti ve geçici işlem yasağı | §14.2 | §2.8 | ✓ |
| 97 | 02-§14.3-01 | Yeni hesap işlem limiti | §14.3 | §2.8 | ✓ |
| 98 | 02-§14.3-03 | Anormal davranış tespiti ve flag'leme | §14.3 | §2.8 | ✓ |
| 99 | 02-§14.3-04 | Çoklu hesap tespiti — cüzdan adresi çapraz kontrol | §14.3 | §2.8 | ✓ |
| 100 | 02-§14.3-06 | Çoklu hesap tespiti — IP/cihaz parmak izi | §14.3 | §2.8 | ✓ |
| 101 | 02-§14.4-01 | Kara para aklama tespiti — piyasa fiyat sapması, yüksek hacim | §14.4 | §2.8 | ✓ |
| 102 | 02-§14.4-03 | Flag'lenen işlemler admin onayı bekler | §14.4 | §2.8 | ✓ |

#### §15 Platform Steam Hesapları

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 103 | 02-§15-01 | Birden fazla Steam hesabı | §15 | §2.10 | ✓ |
| 104 | 02-§15-02 | Hesap kısıtlanırsa diğer hesaplar | §15 | §2.10 | ✓ |
| 105 | 02-§15-03 | Admin panelinden izleme | §15 | §2.10, §2.11 | ✓ |

#### §16 Admin Paneli

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 106 | 02-§16.1-01 | Admin paneli var | §16.1 | §2.11 | ✓ |
| 107 | 02-§16.1-02 | Süper admin + özel rol grupları | §16.1 | §2.11 | ✓ |
| 108 | 02-§16.1-03 | Süper admin yetkileri belirler | §16.1 | §2.11 | ✓ |
| 109 | 02-§16.2-01 | Tüm dinamik parametrelerin yönetimi | §16.2 | §2.11 | ✓ |
| 110 | 02-§16.2-13 | Flag'lenmiş işlem inceleme ve onay/red | §16.2 | §2.11 | ✓ |
| 111 | 02-§16.2-15 | Emergency hold yönetimi | §16.2 | — | ✗ |
| 112 | 02-§16.2-16 | Audit log görüntüleme | §16.2 | §2.11 | ✓ |

#### §17 Kullanıcı Dashboard

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 113 | 02-§17-01 | Aktif işlemler ve durum takibi | §17 | §2.12 | ✓ |
| 114 | 02-§17-02 | İşlem geçmişi (süresiz) | §17 | §2.12 | ✓ |
| 115 | 02-§17-03 | Cüzdan/ödeme bilgileri | §17 | §2.12 | ✓ |
| 116 | 02-§17-04 | Profil ve itibar skoru | §17 | §2.12 | ✓ |
| 117 | 02-§17-05 | Platform içi bildirimler | §17 | §2.12 | ✓ |

#### §18 Bildirimler

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 118 | 02-§18.1-01 | Platform içi bildirim | §18.1 | §2.13 | ✓ |
| 119 | 02-§18.1-02 | Email | §18.1 | §2.13 | ✓ |
| 120 | 02-§18.1-03 | Telegram/Discord bot | §18.1 | §2.13 | ✓ |

#### §19 Hesap Yönetimi

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 121 | 02-§19-01 | Hesap silme/deaktif etme | §19 | §2.4 | ✓ |

#### §20 Platform Sorumluluğu

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 122 | 02-§20-01 | Platformun garanti ettiği ve etmediği sorumluluklar | §20 | — | ⚠ |

#### §21 Erişim ve Uyumluluk

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 123 | 02-§21-01 | Web platformu | §21 | §2.15, §4 | ✓ |
| 124 | 02-§21-02 | Mobil MVP sonrası | §21 | §3.3 | ✓ |
| 125 | 02-§21-03 | Landing page | §21 | §2.15 | ✓ |
| 126 | 02-§21-04 | Global hedef pazar | §21 | — | ✓ (implicit) |
| 127 | 02-§21-05 | 4 dil desteği | §21 | §2.15, §4 | ✓ |
| 128 | 02-§21-06 | İşlem geçmişi süresiz saklama | §21 | §2.15 | ✓ |
| 129 | 02-§21.1-01 | Yasaklı bölge geo-block (OFAC/AB/BM) | §21.1 | — | ✗ |
| 130 | 02-§21.1-02 | Geo-block mekanizması (IP bazlı) | §21.1 | — | ✗ |
| 131 | 02-§21.1-03 | Yaş kısıtı (18+, Steam yaşı + beyan) | §21.1 | — | ✗ |
| 132 | 02-§21.1-04 | VPN/proxy tespiti (destekleyici sinyal) | §21.1 | — | ⚠ |

#### §22–23

| # | ID | Öğe Özeti | Kaynak Bölüm | Hedef Bölüm | Durum |
|---|---|---|---|---|---|
| 133 | 02-§22-01 | Kullanıcı sözleşmesi / ToS olacak | §22 | §2.15 | ✓ |
| 134 | 02-§23-01 | Platform bakımında timeout dondurma + bildirim | §23 | §2.14 | ✓ |
| 135 | 02-§23-02 | Steam kesintisinde timeout dondurma | §23 | §2.14 | ✓ |

**Toplam: 124 öğe (103 ✓, 12 ⚠, 9 ✗)**

---

### Envanter — 10_MVP_SCOPE (İç Envanter)

| # | ID | Öğe Özeti | Kaynak Bölüm | İç Tutarlılık | Durum |
|---|---|---|---|---|---|
| 1 | 10-§1-01 | MVP amacı: güvenli, otomatik escrow | §1 | 02 §1 ile uyumlu | ✓ |
| 2 | 10-§1-02 | Hedef: temel akışı kanıtlama | §1 | — | ✓ |
| 3 | 10-§1-03 | Hedef: ilk kullanıcı kitlesi | §1 | — | ✓ |
| 4 | 10-§1-04 | Hedef: komisyon geliri | §1 | §4 ile uyumlu | ✓ |
| 5 | 10-§1-05 | Hedef: fraud/abuse kontrolü | §1 | §2.8 ile uyumlu | ✓ |
| 6 | 10-§2.2-01 | Gas fee yönetimi — "komisyondan karşılanır, koruma eşiği ile" | §2.2 | 02 §4.7 ile kısmi çelişki | ⚠ |
| 7 | 10-§2.6-01 | Alıcı ödeme yapmadıysa satıcı iptal edebilir | §2.6 | 02 §7-01 ile uyumlu | ✓ |
| 8 | 10-§2.6-02 | Alıcı ödeme yapmadan önce iptal edebilir | §2.6 | 02 §7-02 ile uyumlu | ✓ |
| 9 | 10-§2.6-03 | Alıcı ödediyse tek taraflı iptal yok | §2.6 | 02 §7-03 ile uyumlu | ✓ |
| 10 | 10-§2.6-04 | İptal sebebi zorunlu | §2.6 | 02 §7-06 ile uyumlu | ✓ |
| 11 | 10-§2.6-05 | İptal sonrası cooldown | §2.6 | 02 §7-05 ile uyumlu | ✓ |
| 12 | 10-§2.7-01 | Dispute: otomatik doğrulama | §2.7 | 02 §10.1 ile uyumlu | ✓ |
| 13 | 10-§2.7-02 | Admin eskalasyon yolu (detayları sonra) | §2.7 | §3.6 ile uyumlu | ✓ |
| 14 | 10-§3.1-01 | MVP dışı: Barter — akışı karmaşıklaştırır | §3.1 | 02 §2.2 ile uyumlu | ✓ |
| 15 | 10-§3.1-02 | MVP dışı: Çoklu item | §3.1 | 02 §2.2 ile uyumlu | ✓ |
| 16 | 10-§3.1-03 | MVP dışı: Trade lock desteği | §3.1 | 02 §2.2 ile uyumlu | ✓ |
| 17 | 10-§3.1-04 | MVP dışı: Diğer Steam oyunları | §3.1 | 01 §6.2 ile uyumlu | ✓ |
| 18 | 10-§3.2-01 | MVP dışı: Platform cüzdanı | §3.2 | 02 §4.1 ile uyumlu | ✓ |
| 19 | 10-§3.2-02 | MVP dışı: Ek blockchain ağları | §3.2 | 01 §6.3 ile uyumlu | ✓ |
| 20 | 10-§3.2-03 | MVP dışı: Fiat ödeme | §3.2 | 01 §5.3 ile uyumlu | ✓ |
| 21 | 10-§3.5-01 | MVP dışı: KYC | §3.5 | 02 §11 ile uyumlu | ✓ |
| 22 | 10-§4-01 | Kısıtlama: Sadece CS2 | §4 | 02 §9 ile uyumlu | ✓ |
| 23 | 10-§4-02 | Kısıtlama: Tek item, sadece tradeable | §4 | 02 §2.2 ile uyumlu | ✓ |
| 24 | 10-§4-03 | Kısıtlama: Sadece USDT/USDC, Tron | §4 | 02 §4.1 ile uyumlu | ✓ |
| 25 | 10-§4-04 | Kısıtlama: Sadece web | §4 | 02 §21 ile uyumlu | ✓ |
| 26 | 10-§4-05 | Kısıtlama: %2 komisyon | §4 | 02 §5 ile uyumlu | ✓ |
| 27 | 10-§4-06 | Kısıtlama: KYC yok | §4 | 02 §11 ile uyumlu | ✓ |
| 28 | 10-§4-07 | Kısıtlama: Sadece otomatik itibar skoru, yorum yok | §4 | 02 §13 ile uyumlu | ⚠ |

**Toplam: 28 öğe (26 ✓, 2 ⚠, 0 ✗)**

---

## Bulgular

| # | Envanter ID | Tür | Seviye | Bulgu | Öneri |
|---|---|---|---|---|---|
| 1 | 02-§7-07, 02-§7-08 | GAP | **High** | **Admin doğrudan iptal eksik.** 02 §7'de admin'in aktif işlemleri (CREATED–PAYMENT_CONFIRMED + FLAGGED) doğrudan iptal edebileceği, ayrı yetki gerektirdiği, ITEM_DELIVERED sonrası kısıtlama olduğu detaylı tanımlı. 10 §2.6 sadece kullanıcı taraflı iptal kurallarını listeliyor, admin doğrudan iptal hiç bahsedilmiyor. Bu MVP'de olan önemli bir admin yeteneği | §2.6'ya "Admin doğrudan iptal (aktif işlemler, sebep zorunlu, ayrı yetki)" maddesi eklenmeli |
| 2 | 02-§7-09, 02-§7-10 | GAP | **High** | **Admin emergency hold eksik.** 02 §7'de admin'in herhangi bir aktif işlemi geçici olarak dondurabilmesi (sanctions, hesap ele geçirme) detaylı tanımlı. 10'da hiç bahsedilmiyor. Sanctions screening (02 §21.1) ve fraud flag'leme (02 §14.0) ile doğrudan ilişkili kritik güvenlik özelliği | §2.6'ya "Admin emergency hold (işlem dondurma, timeout durur)" veya ayrı bir satır olarak eklenmeli. §2.11'de emergency hold yönetimi de listelenmiş olmalı |
| 3 | 02-§21.1-01, 02-§21.1-02 | GAP | **High** | **Geo-block (yasaklı bölge) eksik.** 02 §21.1'de OFAC/AB/BM yaptırım listesindeki ülkelerden erişim engeli, IP bazlı geo-block mekanizması, admin tarafından güncellenebilir ülke listesi detaylı tanımlı. 10'da bu özellik ne §2'de (olan) ne §3'te (olmayan) bahsedilmiyor. Uyumluluk açısından kritik | §2.15'e veya yeni bir "Uyumluluk" alt bölümüne "Yasaklı bölge erişim engeli (OFAC/AB/BM, IP bazlı geo-block)" eklenmeli |
| 4 | 02-§21.1-03 | GAP | **High** | **Yaş kısıtı (18+) eksik.** 02 §21.1'de minimum 18 yaş, Steam hesap yaşı ve kullanıcı beyanı ile kontrol tanımlı. 10'da bahsedilmiyor. Yasal gereklilik | Aynı bölüme "Yaş kısıtı (18+, Steam hesap yaşı + kullanıcı beyanı)" eklenmeli |
| 5 | 02-§3.3-03 | GAP | **Medium** | **Blockchain altyapı timeout dondurma eksik.** 02 §3.3'te blockchain altyapısı sağlıksız olduğunda ödeme adımındaki timeout dondurma tanımlı. 10 §2.14 sadece "Platform bakımı" ve "Steam kesintisi" için timeout dondurma listeliyor | §2.14'e "Blockchain altyapı kesintisinde ödeme timeout dondurma" eklenmeli |
| 6 | 10-§2.2-01 | Tutarsızlık | **Medium** | **Gas fee açıklaması eksik/yanıltıcı.** 10 §2.2 "Gas fee yönetimi (komisyondan karşılanır, koruma eşiği ile)" diyor. 02 §4.7'ye göre sadece satıcıya gönderim gas fee'si komisyondan karşılanır; alıcı ödeme gas fee'si alıcı tarafından, iade gas fee'si iade tutarından düşülür. Parantez içi açıklama tüm gas fee'lerin komisyondan karşılandığı izlenimini veriyor | Parantez içi açıklamayı düzeltmek: "Gas fee yönetimi (satıcı payout komisyondan, iade tutarından düşüm, koruma eşiği)" veya sadece "(detaylar 02 §4.7)" şeklinde referans |
| 7 | 02-§16.2-15 | GAP | **Medium** | **Admin paneli emergency hold yönetimi eksik.** 02 §16.2'de emergency hold'daki işlemleri listeleme, hold kaldırma veya iptal etme admin paneli yeteneği tanımlı. 10 §2.11'de bu yetenek listelenmiyor. Bulgu #2 ile bağlantılı | §2.11'e "Emergency hold yönetimi (listeleme, devam ettirme, iptal)" eklenmeli |
| 8 | 02-§4.4-04 | GAP | **Low** | **Desteklenmeyen token edge case'i eksik.** 02 §4.4'te desteklenmeyen token/kontrat gönderildiğinde otomatik iadenin garanti edilemeyeceği ve manuel incelemeye düşeceği tanımlı. 10 §2.2'nin edge case listesinde sadece "(eksik tutar, fazla tutar, yanlış token, gecikmeli ödeme)" var | §2.2 edge case listesine "desteklenmeyen token" eklenmeli |
| 9 | 02-§4.4-06 | GAP | **Low** | **Çoklu/parçalı ödeme kuralı eksik.** 02 §4.4'te parçalı ödemelerin birleştirilmediği, tek seferde doğru tutar gönderilmesi gerektiği tanımlı. 10 §2.2 edge case listesinde bahsedilmiyor | §2.2 edge case listesine "çoklu/parçalı ödeme" eklenmeli (veya mevcut listeyi "(detaylar 02 §4.4)" referansıyla genişletmek) |

**Seviye tanımları:**
- **Critical:** Güvenlik açığı, veri kaybı riski, temel işlevsellik eksikliği — düzeltilmeden ilerlenmemeli
- **High:** Bu dokümanda düzeltilmeli
- **Medium:** Bu dokümanda düzeltilmeli ama işlevselliği değil ifadeyi etkiliyor
- **Low:** Farkındalık yeterli

---

## Aksiyon Planı

**High:**
- [ ] §2.6'ya admin doğrudan iptal ve emergency hold maddeleri eklenmeli (Bulgu #1, #2)
- [ ] §2.11'e emergency hold yönetimi eklenmeli (Bulgu #7)
- [ ] §2.15'e veya yeni alt bölüme erişim/uyumluluk gereksinimleri eklenmeli: geo-block, yaş kısıtı (Bulgu #3, #4)

**Medium:**
- [ ] §2.14'e blockchain altyapı timeout dondurma eklenmeli (Bulgu #5)
- [ ] §2.2 gas fee açıklaması düzeltilmeli (Bulgu #6)

**Low:**
- §2.2 edge case listesi genişletilebilir: desteklenmeyen token, çoklu/parçalı ödeme (Bulgu #8, #9)

---

*Audit tamamlandı — 10_MVP_SCOPE.md v1.1, 2026-03-22*
