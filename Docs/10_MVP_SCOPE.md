# Skinora — MVP Scope

**Versiyon: v1.3** | **Bağımlılıklar:** `01_PROJECT_VISION.md`, `02_PRODUCT_REQUIREMENTS.md` | **Son güncelleme:** 2026-03-22

---

## 1. MVP Amacı

Skinora'nın ilk sürümü (MVP), CS2 item ticaretinde platform dışında anlaşmış alıcı ve satıcı arasında güvenli, otomatik bir escrow hizmeti sunmayı amaçlar.

MVP'nin hedefi:
- Temel escrow akışının sorunsuz çalıştığını kanıtlamak
- İlk kullanıcı kitlesini kazanmak ve güven oluşturmak
- Komisyon geliri üretmeye başlamak
- Fraud/abuse risklerini kontrol altında tutmak

---

## 2. MVP'de Olan Özellikler

### 2.1 Temel Escrow Akışı

- Satıcı işlem başlatır (item seçimi, fiyat, stablecoin türü, timeout süresi)
- Alıcıya bildirim / davet linki gider
- Alıcı işlemi kabul eder
- Satıcı item'ı platforma emanet eder (Steam trade offer)
- Alıcı ödemeyi gönderir (blockchain)
- Platform otomatik doğrulama yapar (ödeme + item teslim)
- Item alıcıya teslim edilir (Steam trade offer)
- Satıcıya ödeme gönderilir (komisyon düşülerek)

### 2.2 Ödeme

- USDT ve USDC desteği (Tron TRC-20)
- Dış cüzdan modeli — her işlem için benzersiz ödeme adresi
- Otomatik blockchain doğrulama
- Ödeme edge case yönetimi (eksik tutar, fazla tutar, yanlış token, desteklenmeyen token, çoklu/parçalı ödeme, gecikmeli ödeme)
- Gas fee yönetimi (satıcı payout: komisyondan karşılanır + koruma eşiği, iade: tutarından düşülür)

### 2.3 Timeout Sistemi

- Her adım için ayrı timeout
- Admin tarafından ayarlanabilir süreler
- Ödeme timeout'u satıcı tarafından seçilebilir (admin aralığı dahilinde)
- Timeout sonucu state'e göre değişir:

| Timeout Adımı | Sonuç |
|---|---|
| Alıcı kabulü (adım 2) | İşlem iptal — henüz varlık transferi yok |
| Satıcı trade offer (adım 3) | İşlem iptal — henüz varlık transferi yok |
| Ödeme (adım 4) | İşlem iptal, item satıcıya iade. Adres izlemeye devam — gecikmeli ödeme gelirse alıcıya otomatik iade |
| Teslim trade offer (adım 6) | İşlem iptal, item satıcıya iade, ödeme alıcıya iade |

### 2.4 Kullanıcı Yönetimi

- Steam ile giriş
- Steam Mobile Authenticator zorunluluğu
- Profil ve cüzdan adresleri yönetimi (satıcı ödeme adresi + alıcı iade adresi)
- Cüzdan adresi değişikliğinde ek doğrulama
- Hesap silme/deaktif etme (aktif işlem varken silinemez; soft-delete uygulanır — PII temizlenir, işlem geçmişi ve audit logları anonim olarak kalıcı saklanır)
- Kullanıcı itibar skoru (işlem sayısı, başarı oranı, hesap yaşı)

### 2.5 Alıcı Belirleme

- Steam ID ile belirleme (aktif) — sadece belirtilen kişi kabul edebilir
- Kayıtlı alıcıya platform bildirimi, kayıtlı değilse satıcıya davet linki
- Açık link yöntemi (pasif, admin aktif edebilir)

### 2.6 İptal Yönetimi

- Alıcı ödeme yapmadıysa satıcı iptal edebilir
- Alıcı ödeme yapmadan önce iptal edebilir (item varsa satıcıya iade)
- Alıcı ödediyse hiçbir taraf tek taraflı iptal edemez
- İptal sebebi zorunlu
- İptal sonrası cooldown (admin tarafından ayarlanabilir)
- Admin doğrudan iptal (aktif işlemler, sebep zorunlu, ayrı yetki)
- Admin emergency hold (işlem dondurma, timeout durur, devam ettirme veya iptal)

### 2.7 Dispute / Anlaşmazlık

- Ödeme, teslim ve yanlış item itirazlarında otomatik doğrulama
- Admin'e eskalasyon yolu (detayları MVP sonrası)

### 2.8 Fraud / Abuse Önlemleri

- Wash trading koruması (aynı çift, 1 ay kuralı)
- İptal limiti ve geçici işlem yasağı
- Yeni hesap işlem limiti
- Anormal davranış tespiti ve flag'leme
- Çoklu hesap tespiti (cüzdan adresi çapraz kontrol + IP/cihaz parmak izi)
- Kara para aklama tespiti (piyasa fiyat sapması, yüksek hacim) — flag'lenen işlemler admin onayı bekler
- Arka planda piyasa fiyat verisi çekimi (sadece fraud tespiti için)

### 2.9 Item Yönetimi

- Steam envanter okuma
- Item doğrulama (varlık ve tradeable kontrolü)
- Tüm CS2 item türleri desteği
- Sadece tradeable item'lar

### 2.10 Platform Steam Hesapları

- Birden fazla Steam hesabı ile çalışma
- Hesap kısıtlanırsa yeni işlemler diğer uygun bot hesaplarına yönlendirilir; aktif custody işlemleri orijinal bot bağlamında yönetilir (gerekirse emergency hold veya admin müdahalesine alınır)
- Admin panelinden hesap durumu izleme

### 2.11 Admin Paneli

- Süper admin + özel rol grupları
- Süper admin rol ve yetkileri belirler
- Tüm dinamik parametrelerin yönetimi (timeout, komisyon, limitler, eşikler)
- Flag'lenmiş işlem inceleme ve onay/red
- Emergency hold yönetimi (listeleme, devam ettirme, iptal)
- Platform Steam hesapları izleme
- Audit log görüntüleme (fon hareketleri, admin aksiyonları, güvenlik olayları)

### 2.12 Kullanıcı Dashboard

- Aktif işlemler ve durum takibi
- İşlem geçmişi
- Cüzdan/ödeme bilgileri
- Profil ve itibar skoru
- Bildirimler

### 2.13 Bildirimler

- Platform içi bildirim
- Email
- Telegram/Discord bot
- Tüm kritik adımlarda ilgili tarafa bildirim

### 2.14 Downtime Yönetimi

- Platform bakımında timeout dondurma
- Steam kesintisinde timeout dondurma
- Blockchain altyapı kesintisinde ödeme timeout dondurma
- Kullanıcılara önceden bildirim

### 2.15 Diğer

- Landing page
- Web platformu
- 4 dil desteği (İngilizce, Çince, İspanyolca, Türkçe)
- Kullanıcı sözleşmesi / Terms of Service
- Süresiz işlem geçmişi saklama

### 2.16 Erişim ve Uyumluluk

- Yasaklı bölge erişim engeli (OFAC/AB/BM yaptırım listesi, IP bazlı geo-block, admin tarafından güncellenebilir)
- Yaş kısıtı (minimum 18 yaş, Steam hesap yaşı + kullanıcı beyanı ile kontrol)

---

## 3. MVP'de Olmayan Özellikler

### 3.1 İşlem Genişletmeleri

| Özellik | Neden MVP dışı |
|---|---|
| Barter (item-item takas) | Akışı ciddi şekilde karmaşıklaştırır |
| Çoklu item işlemleri | Tek item ile başlamak basitlik sağlar |
| Trade lock'lu item desteği | Uzun bekleme süreleri akışı bozar |
| Diğer Steam oyunları (Dota 2, TF2, Rust) | Önce CS2'de kanıtlanmalı |

### 3.2 Ödeme Genişletmeleri

| Özellik | Neden MVP dışı |
|---|---|
| Platform cüzdanı (bakiye yükleme) | Yasal sorumluluk ve karmaşıklık |
| Ek blockchain ağları | Tron ile başlamak yeterli |
| Fiat ödeme desteği | Chargeback riski ve yasal yükümlülükler |

### 3.3 Kullanıcı Deneyimi Genişletmeleri

| Özellik | Neden MVP dışı |
|---|---|
| Mobil uygulama | Web ile başlamak yeterli |
| Kullanıcı yorum/değerlendirme sistemi | İtibar skoru başlangıç için yeterli |
| Kullanıcıya piyasa fiyatı gösterimi | Fiyat serbesttir, fraud tespiti arka planda yapılır |

### 3.4 İş Modeli Genişletmeleri

| Özellik | Neden MVP dışı |
|---|---|
| Premium üyelik | Önce temel gelir modeli kanıtlanmalı |
| Ek gelir kanalları | MVP'de sadece komisyon |

### 3.5 Güvenlik Genişletmeleri

| Özellik | Neden MVP dışı |
|---|---|
| KYC | Kullanıcı kazanımını yavaşlatır, ileride yüksek tutarlı işlemler için düşünülebilir |

### 3.6 Detayları Sonraya Bırakılan Konular

| Konu | Durum |
|---|---|
| Admin eskalasyon süreci detayları | Eskalasyon yolu var ama süreç detayları belirlenmedi |
| Kullanıcı sözleşmesi içeriği | Olacağına karar verildi, içerik yazılmadı |
| Bildirim mesaj içerikleri | Tetikleyiciler belirlendi, mesaj metinleri yazılmadı |
| Platform Steam hesapları yönetim detayları | Genel yaklaşım belirlendi, operasyonel detaylar belirlenmedi |
| Steam Mobile Authenticator kontrol detayları | Zorunlu olacak, kontrol mekanizması detaylandırılmadı |

---

## 4. MVP Sınırları ve Kısıtlamalar

| Kısıtlama | Detay |
|---|---|
| Oyun | Sadece CS2 |
| Item | Tek item per işlem, sadece tradeable |
| Ödeme | Sadece USDT/USDC, sadece Tron (TRC-20), sadece dış cüzdan |
| Platform | Sadece web |
| Dil | İngilizce, Çince, İspanyolca, Türkçe |
| Gelir | Sadece komisyon (%2 varsayılan) |
| KYC | Yok |
| Kullanıcı değerlendirmesi | Sadece otomatik itibar skoru, yorum yok |

---

## 5. MVP Başarı Kriterleri

| Alan | Metrik |
|---|---|
| Büyüme | Haftalık/aylık tamamlanan işlem sayısı artıyor mu? Yeni kullanıcı kazanımı devam ediyor mu? Geri dönüş oranı nedir? |
| Güvenilirlik | İşlemlerin yüzde kaçı başarıyla tamamlanıyor? Otomatik doğrulama hata oranı nedir? Dispute/eskalasyon oranı düşük mü? |
| Gelir | Aylık komisyon geliri artıyor mu veya stabil mi? |
| Güven | Kullanıcılar platforma geri dönüyor mu? (tekrar kullanım oranı) |

Hedef rakamlar MVP lansmanı sonrası belirlenecektir.

---

## 6. MVP Sonrası Yol Haritası (Öncelik Sırası Belirlenmedi)

- Diğer Steam oyunları desteği (Dota 2, TF2, Rust)
- Mobil uygulama
- Çoklu item işlemleri
- Barter desteği
- Kullanıcı yorum/değerlendirme sistemi
- Kullanıcıya piyasa fiyatı gösterimi
- Ek blockchain ağları
- Trade lock'lu item desteği
- Platform cüzdanı
- Yüksek tutarlı işlemler için KYC
- Premium üyelik ve ek gelir kanalları
- Admin eskalasyon sürecinin detaylandırılması

---

*Skinora — MVP Scope v1.3*
