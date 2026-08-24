# Skinora — MVP Scope

**Versiyon: v2.0** | **Bağımlılıklar:** `01_PROJECT_VISION.md`, `02_PRODUCT_REQUIREMENTS.md` | **Son güncelleme:** 2026-08-08

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
- Alıcı işlemi kabul eder (iade adresi + Steam trade URL verir)
- Satıcı göndermeye hazır olduğunu onaylar — platform item'ın hâlâ tradeable olduğunu doğrular
- Alıcı ödemeyi gönderir (blockchain), ödeme emanete alınır
- **Satıcı item'ı doğrudan alıcıya gönderir** (platform trade'in tarafı değildir)
- Platform teslimatı doğrular (alıcı onayı veya envanter kanıtı)
- İşlem 8 günlük mutabakat süresine girer (Steam'in trade geri alma penceresi kapanana kadar para platformda tutulur)
- Süre sonunda item'ın hâlâ alıcıda olduğu doğrulanır → satıcıya ödeme gönderilir (komisyon düşülerek). Trade geri alınmışsa ödeme yapılmaz, para alıcıya iade edilir

> **v3.0:** Item custody kaldırıldı — escrow edilen para, item değil (02 §2.1). Sıra tersine döndü: önce ödeme, sonra teslimat.

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
- Item doğrulama (varlık ve tradeable kontrolü) — oluşturmada, hazırlık onayında ve ödeme onayında
- Tüm CS2 item türleri desteği
- Sadece tradeable item'lar (trade-protected item'lar dâhil değil)
- **Item custody yok** — item hiçbir aşamada platformda bulunmaz
- Teslimat doğrulaması: alıcı onayı veya envanter kanıtı (02 §9.2)
- Her iki tarafta Steam Mobile Authenticator zorunluluğu

### 2.10 Platform Steam Hesapları

**Kapsam dışı (v3.0).** Platform Steam hesabı işletmez; bot havuzu, failover ve hesap durumu izleme kaldırılmıştır (02 §15). Steam ile tek etkileşim salt okunur envanter ve trade-hold sorgularıdır.

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
| Mobile Authenticator | **Her iki tarafta zorunlu** (v3.0). MA'sı olmayan kullanıcılar platformu kullanamaz — trade 15 gün Steam escrow'una düşeceği için. Kullanıcı tabanını daraltan bilinçli bir kısıttır (02 §9.1) |
| Gizlilik | Taraflar birbirinin Steam profilini ve trade URL'ini görür — P2P modelinin kaçınılmaz sonucu (02 §21) |
| Teslimat doğrulama hassasiyeti | Item sınıfı (`classid`/`instanceid`) düzeyinde. Aynı sınıftan iki item arasındaki aşınma/desen farkı otomatik tespit edilmez; `WRONG_ITEM` dispute'una tabidir (02 §9.2). Float doğrulaması post-MVP |
| Eşzamanlı teslimat kapasitesi | Steam Community envanter ucunun rate limiti, aynı anda doğrulanabilen teslimat sayısına pratik bir tavan koyar (08 §2.6). Ölçek arttığında çoklu-IP/proxy havuzu gerekecektir — MVP kapsamı dışında |

### 4.1 Kabul Edilen Riskler

| Risk | Neden kabul ediliyor |
|---|---|
| **Satıcı ödemesinin 8 gün gecikmesi** — satıcı item'ı gönderdikten sonra parasını 8 gün bekler | Steam'in 7 günlük trade geri alma penceresi kapanmadan ödeme yapmak, satıcının item'ı gönderip parayı aldıktan sonra trade'i geri almasına açık kapı bırakır. Bekleme, bu dolandırıcılığa karşı tek etkili korumadır (02 §4.5.1). Sektördeki diğer platformlar da benzer gecikme uygular. İtibarlı satıcılar için süreyi kısaltmak post-MVP'ye bırakıldı |
| **Satıcı non-delivery** — ödeme emanete girdikten sonra satıcı item'ı göndermeyebilir | Para emanette güvendedir ve iade edilir; kayıp yalnız zamandır. Teslimat süresi kısa tutulur, gecikme satıcının itibarına yazılır, tekrarı yaptırıma tabidir (02 §14.2). Alternatifi (satıcının önce göndermesi) alıcı ödemediğinde item'ı tamamen kaybettirirdi |
| **Alıcı envanteri gizliyse otomatik kanıt üretilemez** | Alıcı onayı yolu bağımsız çalışır ve tipik akışta zaten birincil yoldur; kullanıcı hazırlık onayı adımında uyarılır (02 §9.2) |

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

*Skinora — MVP Scope v2.0*
