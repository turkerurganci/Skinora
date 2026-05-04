# Skinora — Product Requirements

**Versiyon: v2.6** | **Bağımlılıklar:** `01_PROJECT_VISION.md`, `PRODUCT_DISCOVERY_STATUS.md` | **Son güncelleme:** 2026-05-04

---

## 1. Genel Bakış

Bu doküman, Skinora escrow platformunun ürün gereksinimlerini tanımlar. Tüm gereksinimler product discovery sürecinde alınan kararlara dayanmaktadır.

---

## 2. İşlem Akışı Gereksinimleri

### 2.1 Temel Akış

Platformdaki her işlem aşağıdaki 8 adımdan oluşur:

| Adım | Açıklama | Doğrulama |
|---|---|---|
| 1. İşlem oluşturma | Satıcı item'ı seçer, stablecoin türünü belirler, fiyat ve ödeme timeout süresini girer | Platform envanter okuyarak item'ın var ve tradeable olduğunu doğrular |
| 2. Alıcı kabulü | Alıcı işlem detaylarını görür ve kabul eder. Henüz ödeme yapmaz | — |
| 3. Item emaneti | Platform satıcıya Steam trade offer gönderir, satıcı kabul eder, item platforma geçer | Platform Steam üzerinden item transferini doğrular |
| 4. Ödeme | Platform benzersiz ödeme adresi üretir, alıcı bu adrese toplam tutarı (fiyat + komisyon) gönderir | Platform blockchain üzerinden otomatik doğrular |
| 5. Ödeme doğrulama | Blockchain üzerinden otomatik | Otomatik |
| 6. Item teslimi | Platform alıcıya Steam trade offer gönderir, alıcı kabul eder | Platform Steam üzerinden teslimi doğrular |
| 7. Teslim doğrulama | Steam üzerinden otomatik | Otomatik |
| 8. Satıcıya ödeme | Platform komisyonu keser, kalan tutarı satıcının cüzdan adresine gönderir | Blockchain üzerinden doğrulanır |

### 2.2 İşlem Kuralları

- Her işlem tek bir item içerir
- Sadece item karşılığı kripto ödeme yapılır (barter yok)
- İşlemi her zaman satıcı başlatır
- İşlem detayları (item, fiyat, stablecoin türü) oluşturulduktan sonra değiştirilemez — değiştirmek isteyen satıcı iptal edip yeniden başlatır
- Sadece tradeable item'larla işlem yapılabilir (trade lock'lu item'lar desteklenmez)
- Tüm CS2 item türleri desteklenir

---

## 3. Timeout Gereksinimleri

### 3.1 Timeout Yapısı

Her işlem adımı için ayrı timeout süresi bulunur:

| Adım | Timeout Kuralı |
|---|---|
| Alıcının işlemi kabul etmesi (adım 2) | Admin tarafından ayarlanabilir |
| Satıcının trade offer'ı kabul etmesi (adım 3) | Admin tarafından ayarlanabilir |
| Alıcının ödemeyi göndermesi (adım 4) | Admin min-max ve varsayılan belirler, satıcı bu aralıkta seçer |
| Alıcının teslim trade offer'ını kabul etmesi (adım 6) | Admin tarafından ayarlanabilir |

### 3.2 Timeout Sonucu

- Herhangi bir adımda timeout dolarsa işlem iptal olur
- O ana kadar transfer edilen her şey (item ve/veya para) ilgili tarafa otomatik iade edilir (platformun desteklediği ve teknik olarak işleyebildiği varlıklar kapsamında — istisnalar §4.4)
- Ödeme adımında timeout dolduğunda platform adresi izlemeye devam eder — gecikmeli ödeme gelirse alıcıya otomatik iade edilir

### 3.3 Timeout Dondurma

- Platform bakımı sırasında aktif işlemlerin timeout süreleri dondurulur
- Steam kesintileri sırasında da aynı yaklaşım uygulanır. Tespit: Steam bot health check başarısız olduğunda otomatik algılanır; admin manuel olarak da tetikleyebilir
- Blockchain doğrulama altyapısı sağlıksız olduğunda (node/indexer erişim kaybı) ödeme adımındaki aktif işlemlerin timeout süreleri dondurulur. Tespit: blockchain health check başarısız olduğunda otomatik algılanır; admin manuel olarak da tetikleyebilir. Altyapı normale dönünce gecikmeli ödeme tespiti otomatik yapılır
- Bakım/kesinti bittiğinde timeout kaldığı yerden devam eder
- Kullanıcılara planlı bakım öncesi bildirim gönderilir

### 3.4 Timeout Uyarısı

- Timeout süresi dolmadan önce ilgili tarafa (alıcı veya satıcı) "timeout yaklaşıyor" uyarısı gönderilir
- Uyarı eşiği (süre dolmadan ne zaman gönderileceği) admin tarafından oran olarak ayarlanır (§16.2)
- Uyarı tüm bildirim kanalları üzerinden iletilir (§18)

---

## 4. Ödeme Gereksinimleri

### 4.1 Ödeme Altyapısı

| Gereksinim | Detay |
|---|---|
| Ödeme yöntemi | Kripto (stablecoin) |
| Desteklenen stablecoin'ler | USDT ve USDC |
| Blockchain ağı | Tron (TRC-20) |
| Ödeme modeli | Dış cüzdan — platformda kullanıcı bakiyesi tutulmaz, kullanıcılar kendi cüzdanlarından gönderim yapar. Escrow, iade ve payout işlemleri için platform kontrolündeki operasyonel adres altyapısı kullanılır (detaylar 05 §3.2) |
| Adres üretimi | Her işlem için platform benzersiz bir ödeme adresi üretir |
| Doğrulama | Blockchain üzerinden otomatik — ödeme, blockchain üzerinde nihai (final) kabul edildikten sonra onaylanır (teknik doğrulama kriterleri: 05 §3.2) |

### 4.2 Stablecoin Seçimi

- Satıcı işlem başlatırken USDT veya USDC'den birini seçer
- Alıcı satıcının seçtiği token ile ödeme yapar
- Bir işlemde yalnızca bir stablecoin kabul edilir

### 4.3 Fiyatlandırma

- Satıcı fiyatı doğrudan stablecoin miktarı olarak girer (örn: 100 USDT)
- Platform fiyata müdahale etmez — iki taraf anlaştıysa fiyat serbesttir
- MVP'de kullanıcıya piyasa fiyatı gösterilmez
- Arka planda piyasa fiyat verisi çekilir ancak sadece fraud tespiti için kullanılır

### 4.4 Ödeme Edge Case'leri

| Senaryo | Davranış |
|---|---|
| Eksik tutar | Platform kabul etmez, gelen tutar iade edilir, alıcı doğru tutarı baştan gönderir |
| Fazla tutar | Platform doğru tutarı kabul eder, fazlayı alıcıya iade eder, işlem devam eder |
| Yanlış token (desteklenen TRC-20) | Platform kabul etmez, alıcının iade adresine otomatik iade edilir. *Veri modeli notu: Yanlış token ile gelen transfer `WRONG_TOKEN_INCOMING` tipiyle blockchain audit kaydı oluşturulur; `ActualTokenAddress` field'ında yanlış token'ın contract adresi saklanır (06 §3.8).* |
| Desteklenmeyen token/kontrat | Platform bu varlığı işleyemez — otomatik iade garanti edilemez, manuel incelemeye (admin review) düşer |
| Timeout sonrası gecikmeli ödeme | İşlem zaten iptal, platform adresi izlemeye devam eder, gelen ödeme alıcıya otomatik iade edilir |
| Çoklu/parçalı ödeme | Platform parçalı ödemeleri birleştirmez — tek seferde doğru tutarın gönderilmesi gerekir. İlk doğru transfer kabul edilir, sonraki transferler fazla tutar kuralıyla iade edilir. İşlem tamamlandıktan sonra gelen ek transferler gecikmeli ödeme kuralıyla iade edilir |

### 4.5 Satıcıya Ödeme

| Gereksinim | Detay |
|---|---|
| Zamanlama | Item teslimi doğrulandıktan sonra |
| Akış | Platform komisyonu keser, kalan tutarı satıcının cüzdan adresine gönderir |
| Cüzdan adresi | Satıcı profilinde varsayılan adres tanımlar; işlem başlatırken isterse farklı adres girebilir, girmezse profildeki kullanılır |

### 4.6 İade Politikası

| Gereksinim | Detay |
|---|---|
| İade kapsamı | Tam iade — komisyon dahil (alıcı hizmet almadığı için komisyon da iade edilir) |
| Alıcıya iade tutarı | Fiyat + komisyon - gas fee |
| İade adresi | Alıcının işlem kabul ederken belirlediği iade adresine gönderilir (detaylar §12.2) |
| Gas fee | İade işleminin gas fee'si iade tutarından düşülür (alıcı karşılar) |
| Platform maliyeti | Sıfır — platform hiçbir iade senaryosunda kendi cebinden ödeme yapmaz |

### 4.7 Gas Fee Yönetimi

| Gereksinim | Detay |
|---|---|
| Alıcının ödeme gas fee'si | Alıcı karşılar (kendi cüzdanından gönderiyor) |
| Satıcıya gönderim gas fee'si | Platform karşılar (komisyondan düşülür) |
| İade gas fee'leri | İade tutarından düşülür (alıcı karşılar — alıcı alır: fiyat + komisyon - gas fee) |
| Koruma eşiği | Satıcıya gönderim gas fee'si komisyonun belirli bir yüzdesini aşarsa, aşan kısım satıcının alacağından düşülür |
| Varsayılan eşik | %10 |
| Eşik esnekliği | Admin tarafından değiştirilebilir |

---

## 5. Komisyon Gereksinimleri

| Gereksinim | Detay |
|---|---|
| Komisyonu ödeyen | Alıcı |
| Alıcının ödediği toplam | Item fiyatı + komisyon |
| Varsayılan oran | %2 |
| Oran esnekliği | Admin tarafından değiştirilebilir |
| Gelir modeli | MVP'de sadece komisyon |

> **Veri modeli notu:** Finansal hesaplamalar: `MidpointRounding.ToZero` (truncation), scale 6 ondalık basamak. Payment validation tolerance yok — gelen tutar beklenen tutarla tam eşleşmeli (06 §8.3, 09 §14.3).

---

## 6. Alıcı Belirleme Gereksinimleri

### 6.1 Yöntem 1 — Steam ID ile Belirleme (MVP'de aktif)

- Satıcı işlem başlatırken alıcının Steam ID'sini girer
- Sadece belirtilen kullanıcı işlemi kabul edebilir
- Alıcı platformda kayıtlıysa: platform bildirimi gider
- Alıcı platformda kayıtlı değilse: satıcıya davet linki verilir, satıcı kendisi alıcıya iletir

### 6.2 Yöntem 2 — Açık Link (MVP'de pasif)

- Satıcı açık bir işlem linki oluşturur
- İlk kabul eden kişi alıcı olur, link tek kullanımlıktır
- Bu yöntem admin tarafından aktif veya pasif yapılabilir

---

## 7. İptal Gereksinimleri

| Durum | Kural |
|---|---|
| Ödeme öncesi — Satıcı | Satıcı iptal edebilir, item iade edilir |
| Ödeme öncesi — Alıcı | Alıcı iptal edebilir, item varsa satıcıya iade edilir |
| Alıcı ödemeyi gönderdiyse | Hiçbir taraf tek taraflı iptal edemez |
| Alıcı teslim trade offer'ını kabul etmezse (timeout) | Item satıcıya iade, para alıcıya iade, işlem iptal |
| İptal sonrası cooldown | Var — süre admin tarafından dinamik belirlenir |
| İptal sebebi | Zorunlu — iptal eden taraf sebep belirtmek zorunda |
| Admin doğrudan iptal | Admin, CREATED'dan PAYMENT_CONFIRMED'a kadar olan aktif işlemleri (+ FLAGGED) doğrudan iptal edebilir. Sebep zorunludur. İade kuralları standart iptal iade kurallarıyla aynıdır. İşlem CANCELLED_ADMIN durumuna geçer. Ayrı bir yetki (`CANCEL_TRANSACTIONS`) gerektirir |
| Admin doğrudan iptal — ITEM_DELIVERED sonrası | ITEM_DELIVERED aşamasında item alıcıya teslim edilmiş olduğundan standart iptal/iade uygulanamaz. Bu aşamadan sonra admin yalnızca exceptional resolution (manuel inceleme ve müdahale) başlatabilir |
| Admin emergency hold | Admin, herhangi bir aktif işlemi geçici olarak dondurabilir (sanctions eşleşmesi, hesap ele geçirme şüphesi gibi yüksek risk durumlarında). Hold süresince timeout durur, akış bekler. Admin hold'u kaldırarak işlemi devam ettirebilir veya iptal edebilir. Sebep ve audit kaydı zorunludur. Ayrı bir yetki (`EMERGENCY_HOLD`) gerektirir |
| Admin emergency hold — ITEM_DELIVERED kısıtı | ITEM_DELIVERED state'indeki bir işlem hold'a alınabilir ancak hold'dan CANCEL ile çıkılamaz — yalnızca RESUME izinlidir. Item zaten alıcıya teslim edilmiş olduğundan standart iptal/iade uygulanamaz; exceptional durumlar admin tarafından manuel süreçle çözülür |

---

## 8. İşlem Limitleri

| Gereksinim | Detay |
|---|---|
| Min/max işlem tutarı | Admin tarafından dinamik olarak belirlenebilir |
| Eşzamanlı aktif işlem limiti | Var — admin tarafından değiştirilebilir |
| Yeni hesap işlem limiti | Var — detaylar §14.3'te |

---

## 9. Item Yönetimi Gereksinimleri

| Gereksinim | Detay |
|---|---|
| Envanter okuma | Platform satıcının Steam envanterini okur, satıcı listeden item seçer |
| Item doğrulama | Platform item'ın var olduğunu ve tradeable olduğunu baştan doğrular |
| Transfer sırası | Önce item platforma gelir (adım 3), sonra alıcı ödeme yapar (adım 4) |
| Desteklenen türler | Tüm CS2 item türleri |
| Trade lock | Desteklenmez — sadece tradeable item'lar |

> **Veri modeli notu:** Steam trade sonrası asset ID değişir. Platform, item'ın yaşam döngüsü boyunca üç ayrı asset ID'si takip eder: orijinal (satıcı), escrow (bot), teslim (alıcı). Detay: 06 §8.4.

---

## 10. Dispute (Anlaşmazlık) Gereksinimleri

### 10.1 Otomatik Çözüm

| İtiraz Türü | Çözüm |
|---|---|
| Ödeme itirazı ("ödedim ama sistem görmüyor") | Blockchain üzerinden otomatik doğrulama |
| Teslim itirazı ("item teslim edilmedi") | Steam üzerinden otomatik doğrulama |
| Yanlış item itirazı | Sistem emanet alınan item ile işlemdeki item'ı otomatik karşılaştırır |

### 10.2 Dispute Kuralları

| Gereksinim | Detay |
|---|---|
| Dispute açma yetkisi | Yalnızca alıcı dispute açabilir. Satıcıya yapılan ödemeler platform tarafından otomatik gerçekleştirildiği için satıcı tarafında dispute mekanizması gerekmez |
| Timeout etkisi | Dispute açılması timeout sürelerini durdurmaz. Dispute açık bir işlem timeout nedeniyle iptal olabilir — bu durumda dispute otomatik kapanır ve standart iade kuralları uygulanır |
| Rate limiting | Bir işlem için aynı türde dispute tekrar açılamaz |

### 10.3 Satıcı Payout Sorunu

| Gereksinim | Detay |
|---|---|
| Kapsam | Satıcı, tamamlanmış bir işlemde ödemeyi almadığını bildirebilir |
| Otomatik doğrulama | Sistem tx hash ile blockchain üzerinden gönderim durumunu doğrular |
| Retry | Gönderim başarısız veya stuck ise otomatik yeniden deneme uygulanır (teknik detaylar: 05 §3.3) |
| Eskalasyon | Otomatik çözüm başarısız olursa admin'e eskale edilir |

### 10.4 Eskalasyon

- Otomatik çözüm kullanıcıyı tatmin etmezse admin'e eskalasyon yolu var
- Eskalasyon sürecinin detayları ileriye bırakıldı

---

## 11. Kullanıcı Kimlik ve Giriş Gereksinimleri

| Gereksinim | Detay |
|---|---|
| Giriş yöntemi | Steam ile giriş (zorunlu) |
| KYC | MVP'de yok |
| Steam Mobile Authenticator | Zorunlu — aktif olmayan kullanıcılar işlem başlatamaz |

---

## 12. Cüzdan Adresi Güvenliği Gereksinimleri

### 12.1 Satıcı Cüzdan Adresi (Ödeme Alma)

| Gereksinim | Detay |
|---|---|
| Varsayılan adres | Satıcı profilinde tanımlar |
| İşlem bazlı adres | Satıcı işlem başlatırken farklı adres girebilir, girmezse profildeki kullanılır |
| Adres zorunluluğu | Cüzdan adresi olmadan işlem başlatılamaz (profil veya işlem bazlı en az biri tanımlı olmalı) |
| Adres değişikliği | Ek doğrulama istenir (Steam üzerinden tekrar onay) |
| Aktif işlem varken değişiklik | Profildeki adres değiştirilse bile aktif işlemler eski adresle tamamlanır |
| Yanlış adres koruması | Adres girişinde kullanıcıya onay adımı gösterilir |

### 12.2 Alıcı İade Adresi

| Gereksinim | Detay |
|---|---|
| Varsayılan adres | Alıcı profilinde tanımlar |
| İşlem bazlı adres | Alıcı işlemi kabul ederken farklı adres girebilir, girmezse profildeki kullanılır |
| Adres zorunluluğu | İade adresi olmadan işlem kabul edilemez (profil veya işlem bazlı en az biri tanımlı olmalı) |
| Adres değişikliği | Ek doğrulama istenir (Steam üzerinden tekrar onay) |
| Aktif işlem varken değişiklik | Profildeki adres değiştirilse bile aktif işlemler eski adresle tamamlanır |
| Yanlış adres koruması | Adres girişinde kullanıcıya onay adımı gösterilir |
| Exchange uyarısı | Ödeme ekranında "Exchange'den gönderim yapmayın, iade adresinize ulaşamayabilir" uyarısı gösterilir |

### 12.3 Ortak Kurallar

| Gereksinim | Detay |
|---|---|
| Adres formatı | Geçerli Tron (TRC-20) adresi olmalı |
| Merkezi doğrulama pipeline | Cüzdan adresi hangi noktadan girilirse girilsin (profil, işlem başlatma, işlem kabul, adres değiştirme) aynı doğrulama pipeline'ından geçer: (1) TRC-20 format geçerliliği, (2) sanctions screening (§21.1). Geçersiz veya yaptırımlı adres hiçbir noktada kaydedilmez |
| Adres değişikliği doğrulaması | Tüm adres değişiklikleri Steam üzerinden ek onay gerektirir |
| Adres değişikliği cooldown | Değişiklik sonrası admin tarafından ayarlanabilir süre boyunca fon akışı aksiyonları engellenir. **Satıcı payout-address cooldown:** yeni işlem başlatma engellenir; mevcut CREATED davetler eski snapshot adresle devam edebilir. **Alıcı refund-address cooldown:** yeni işlem başlatma ve işlem kabul etme engellenir |
| Snapshot prensibi | İşlem başlatıldığında/kabul edildiğinde adres sabitlenir, sonraki profil değişiklikleri aktif işlemi etkilemez |

---

## 13. Kullanıcı İtibar Skoru Gereksinimleri

| Gereksinim | Detay |
|---|---|
| İtibar sistemi | Aktif |
| Kriterler | Tamamlanan işlem sayısı, başarılı işlem oranı, platformdaki hesap yaşı |
| Skor ölçeği | 0-5 ondalık (1 ondalık basamak), ör: `4.8` |
| Skor formülü | `reputationScore = ROUND(SuccessfulTransactionRate × 5, 1)`. `SuccessfulTransactionRate` formülü ve sorumluluk prensibi 06 §3.1'de tanımlıdır. |
| Yetersiz veri eşikleri | (a) Hesap yaşı < `reputation.min_account_age_days` (default 30 gün) **VEYA** (b) Tamamlanmış işlem sayısı < `reputation.min_completed_transactions` (default 3) → skor `null` döner ("Yeni kullanıcı" UI durumu). Eşikler admin tarafından SystemSetting üzerinden ayarlanabilir. |
| Wash trading koruması | Aktif — detaylar §14.1'de. Aynı alıcı-satıcı çifti arasında 1 ay içindeki ardışık işlemler `SuccessfulTransactionRate` paydasına dahil edilmez. |
| İptal etkisi | İptal oranı itibar skorunu olumsuz etkiler. Sorumluluk prensibi 06 §3.1'de: `CANCELLED_SELLER` satıcının paydasına, `CANCELLED_BUYER` alıcının paydasına eklenir; `CANCELLED_TIMEOUT` adıma göre sorumlu tarafa atanır; `CANCELLED_ADMIN` paydaya dahil edilmez (platform kararı). |
| Kullanıcı yorumu | MVP'de yok — ileride eklenecek |

---

## 14. Fraud / Abuse Önleme Gereksinimleri

### 14.0 Flag Kategorileri

Platform iki seviyede flag mekanizması kullanır:

| Kategori | Kapsam | Tetikleme | Etki |
|---|---|---|---|
| **Hesap flag'i** | Kullanıcı hesabı | Çoklu hesap tespiti, anormal davranış, IP/cihaz parmak izi (§14.3), sanctions eşleşmesi (§21.1) | Tüm fon akışı aksiyonları engellenir: yeni işlem başlatma, işlem kabul etme, açık link kabulü. Mevcut aktif işlemler normal akışta devam eder. **İstisna:** Sanctions eşleşmesi, hesap ele geçirme şüphesi gibi yüksek risk durumlarında kullanıcının aktif işlemlerine otomatik EMERGENCY_HOLD uygulanır (§7). Admin kararları: flag kaldır, hesabı askıya al (tüm fon akışı engellenir, mevcut oturum kısıtlı oturuma döner, aktif işlemlerin otomatik adımları devam eder ancak kullanıcı aksiyonu gerektiren adımlar timeout'a düşer) veya aktif işlemlere hold uygula |
| **İşlem flag'i (pre-create)** | Tekil işlem | AML sapması, yüksek hacim (§14.4) | İşlem CREATED öncesi durdurulur, timeout başlamaz. Admin onaylarsa işlem devam eder, reddederse iptal olur |

### 14.1 Wash Trading

- Aynı alıcı-satıcı çifti arasında ardışık işlemler arasında en az 1 ay olmalı
- Bu süreden kısa aralıkla yapılan işlemler skora etki etmez
- İşlem engellenmez, sadece skor etkisi kaldırılır

### 14.2 Sahte İşlem Başlatma

- Belirli sürede belirli sayıda iptal yapan kullanıcıya geçici işlem başlatma yasağı
- İptal limiti ve yasak süresi admin tarafından dinamik belirlenir
- İptal oranı itibar skorunu etkiler
- İptal sebebi belirtmek zorunludur

### 14.3 Hesap Güvenliği

- Yeni hesaptan ilk işlemlerde sınırlı işlem limiti (admin tarafından dinamik belirlenir)
- Cüzdan adresi değişikliğinde ek doğrulama (Steam onayı)
- Anormal davranış tespiti ve flag'leme (örn: hiç işlem yapmayan hesabın aniden yüksek hacimli işlem yapması)
- Çoklu hesap tespiti: Aynı cüzdan adresi (satıcı ödeme adresi, alıcı iade adresi) birden fazla hesapta kullanılıyorsa flag'lenir
- Çoklu hesap tespiti: Aynı gönderim adresi (ödeme kaynak adresi) birden fazla hesapta görünüyorsa destekleyici sinyal olarak değerlendirilir — tek başına flag sebebi değildir. Bilinen exchange/custodial adresleri bu kontrolden hariç tutulur
- Çoklu hesap tespiti: Aynı IP/cihaz parmak izinden birden fazla hesapla işlem yapılıyorsa flag'lenir (destekleyici sinyal)

### 14.4 Kara Para Aklama Önlemi

- Platform arka planda item piyasa fiyatını çeker
- Piyasa fiyatından sapma eşiği admin tarafından belirlenir
- Eşiği aşan işlemler otomatik flag'lenir ve admin onayı bekler (işlem durdurulur). Flag'leme işlem oluşturma anında (CREATED öncesi) tetiklenir; bu aşamada timeout henüz başlamamıştır. Admin onaylarsa işlem CREATED'a geçer ve normal timeout'lar işlemeye başlar, reddederse işlem iptal olur (state machine detayları: 05 §4.2)
- Kısa sürede yüksek hacim tespiti — eşikler admin tarafından belirlenir (toplam tutar veya işlem sayısı; hangisi önce aşılırsa flag tetiklenir, periyot saati admin tarafından ayarlanır)
- Dormant hesap anomali tespiti (§14.3): minimum hesap yaşı (varsayılan 30 gün) eşiğinin üzerinde, hiç tamamlanmış işlemi olmayan hesabın admin tarafından belirlenen tek işlem tutar eşiğinin üzerinde işlem denemesi otomatik flag'lenir (`ABNORMAL_BEHAVIOR`). Yeni hesap koruması (T39 yeni hesap limitleri) ayrı bir kontrol katmanıdır; minimum yaş eşiği iki kuralın çakışmasını engeller
- AML kuralları işlem oluşturma anında öncelik sırasıyla değerlendirilir: PRICE_DEVIATION → HIGH_VOLUME → ABNORMAL_BEHAVIOR. İlk eşleşen kural flag tipini belirler — tek işlem için tek FraudFlag yazılır

---

## 15. Platform Steam Hesapları Gereksinimleri

| Gereksinim | Detay |
|---|---|
| Hesap yapısı | Birden fazla Steam hesabı ile çalışılır, risk dağıtılır |
| Hesap kısıtlanırsa | Yeni işlemler aktif diğer hesaplara yönlendirilir. Kısıtlanan hesapta emanette olan item'larla ilgili aktif işlemler için recovery/manual intervention akışı uygulanır |
| İzleme | Platform Steam hesaplarının durumu admin panelinden izlenebilir |

---

## 16. Admin Paneli Gereksinimleri

### 16.1 Genel

| Gereksinim | Detay |
|---|---|
| Admin paneli | Var |
| Roller | Süper admin + özel rol grupları |
| Yetki yönetimi | Süper admin rol ve yetkileri belirler |

### 16.2 Admin Tarafından Yönetilen Parametreler

| Parametre | Detay |
|---|---|
| Timeout süreleri | Her adım için ayrı ayarlanabilir |
| Ödeme timeout aralığı | Min-max ve varsayılan değer |
| Komisyon oranı | Değiştirilebilir |
| İşlem limitleri | Min/max tutar, eşzamanlı işlem limiti |
| İptal limiti ve cooldown | Dinamik belirlenir |
| Yeni hesap işlem limiti | Dinamik belirlenir |
| Gas fee koruma eşiği | Değiştirilebilir |
| Fraud sapma eşiği | Piyasa fiyatından sapma yüzdesi |
| Yüksek hacim eşikleri | Tutar eşiği, işlem sayısı eşiği ve kontrol periyodu (saat) |
| Dormant hesap anomali eşikleri | Minimum hesap yaşı (gün) ve tek işlem tutar eşiği — birlikte değerlendirilir (§14.3, §14.4) |
| Alıcı belirleme yöntemi | Yöntem 2'yi aktif/pasif yapabilir |
| Timeout uyarı eşiği | Süre dolmadan ne zaman uyarı gönderileceği (oran olarak) |
| Platform Steam hesapları | Durum izleme |
| Flag'lenmiş işlem yönetimi | İnceleme, onay ve red aksiyonları |
| Flag'lenmiş hesap yönetimi | Listeleme, sinyal/evidence görüntüleme, not düşme, flag kaldırma, geçici blok, kalıcı askıya alma. Tüm aksiyonlar audit log'a kaydedilir |
| Emergency hold yönetimi | Hold'daki işlemleri listeleme, hold kaldırma (devam ettirme) veya iptal etme |
| Audit log görüntüleme | Fon hareketleri, admin aksiyonları, güvenlik olayları |

---

## 17. Kullanıcı Dashboard Gereksinimleri

| Gereksinim | Detay |
|---|---|
| Aktif işlemler | İşlem durumu ve adım takibi |
| İşlem geçmişi | Tamamlanan ve iptal olan işlemler (süresiz saklanır) |
| Cüzdan/ödeme bilgileri | Varsayılan cüzdan adresi yönetimi |
| Profil | İtibar skoru ve hesap bilgileri |
| Bildirimler | Platform içi bildirimler |

---

## 18. Bildirim Gereksinimleri

### 18.1 Kanallar

- Platform içi bildirim
- Email
- Telegram/Discord bot

> **Veri modeli notu:** Email bildirim gönderimi için tek otorite `UserNotificationPreference` tablosudur. `User.Email` profil bilgisi olarak saklanır, gönderim kararı preference tablosundan okunur (06 §3.1).

> **Veri modeli notu:** Dış kanal bildirimleri (email, Telegram, Discord) `NotificationDelivery` entity'sinde kalıcı olarak takip edilir — teslimat başarısı/başarısızlığı ve retry durumu kaydedilir (06 §3.13a).

### 18.2 Tetikleyiciler

| Hedef | Bildirimler |
|---|---|
| Satıcı | Alıcı işlemi kabul etti, ödeme geldi, işlem tamamlandı, ödeme gönderildi |
| Alıcı | Yeni işlem daveti, item platforma ulaştı — ödeme yapabilirsin, item gönderildi — trade offer'ı kabul et, işlem tamamlandı, dispute sonucu |
| Her iki taraf | Timeout yaklaşıyor, işlem iptal oldu |
| Admin | Flag'lenmiş işlem, anormal davranış tespiti |

---

## 19. Hesap Yönetimi Gereksinimleri

| Gereksinim | Detay |
|---|---|
| Hesap silme/deaktif etme | Kullanıcı hesabını silebilir veya deaktif edebilir |
| Aktif işlem varken | Hesap silinemez — önce işlemlerin tamamlanması veya iptal edilmesi gerekir |
| Veri saklama | Hesap silindiğinde kişisel veriler temizlenir, işlem geçmişi ve audit logları anonim olarak saklanır (audit trail) |

> **Veri modeli notu:** Anonimleştirme formatı: `User.SteamId → ANON_{kısa GUID}` (UNIQUE + NOT NULL korunur), `SteamDisplayName → 'Deleted User'`. Bağlı entity'ler (UserNotificationPreference, RefreshToken) de temizlenir. Ek olarak NotificationDelivery.TargetExternalId (gönderim anı email/chat ID snapshot'ı) masked formata dönüştürülür — delivery kaydı korunur ama kişisel hedef bilgisi anonimleştirilir (06 §6.2).

---

## 20. Platform Sorumluluğu

### 20.1 Platformun Garanti Ettiği

- Ödeme doğrulama (blockchain)
- Platform kontrolündeki süreçlerde doğru custody akışı, teslim ve iade prosedürünün uygulanması (Steam kaynaklı yaptırım, el koyma, ban ve üçüncü taraf müdahaleleri bu kapsamın dışındadır — §20.2)
- Timeout'larda ve iptal durumlarında varlıkların iadesi (platformun desteklediği varlıklar kapsamında — istisnalar §4.4)

### 20.2 Platformun Sorumlu Olmadığı

- Steam'in item'a el koyması veya hesap banlaması
- Item'ın çalıntı çıkması
- Blockchain ağındaki olağandışı durumlar
- Steam'in trade sistemini değiştirmesi

### 20.3 Genel Yaklaşım

Platform kendi sürecini garanti eder, üçüncü taraflardan (Steam, blockchain) kaynaklanan sorunlarda sorumluluk kabul etmez.

---

## 21. Erişim ve Platform Gereksinimleri

| Gereksinim | Detay |
|---|---|
| MVP platformu | Web |
| Mobil uygulama | MVP sonrası |
| Landing page | MVP'de olacak |
| Hedef pazar | Global (erişim politikası §21.1'e tabidir) |
| Dil desteği | İngilizce, Çince, İspanyolca, Türkçe |
| İşlem geçmişi saklama | Süresiz |
| Audit log saklama | Süresiz — fon hareketleri, admin aksiyonları, güvenlik olayları kalıcı olarak DB'de tutulur |

### 21.1 Erişim ve Uyumluluk Politikası

| Gereksinim | Detay |
|---|---|
| Yasaklı bölgeler | OFAC/AB/BM yaptırım listesindeki ülkelerden erişim engellenir (geo-block). Yasaklı ülke listesi admin tarafından yönetilir ve güncellenebilir |
| Geo-block mekanizması | IP bazlı coğrafi engelleme uygulanır. Engellenen kullanıcıya bilgilendirme sayfası gösterilir |
| Yaş kısıtı | Platform kullanımı için minimum 18 yaş gereklidir. MVP'de Steam hesap yaşı ve kullanıcının kendi beyanı ile kontrol edilir. Steam hesap yaşı minimum eşiği admin tarafından `auth.min_steam_account_age_days` SystemSetting (default 30 gün) üzerinden yönetilir — burner/fake hesap caydırıcısı, gerçek yaş doğrulaması değildir |
| Sanctions screening | MVP'de cüzdan adresi bazlı temel tarama uygulanır — bilinen yaptırımlı adreslerle eşleşen adresler engellenir. Eşleşme tespit edildiğinde: yeni işlem/adres kaydı engellenir, hesap flag'lenir, kullanıcının tüm aktif işlemlerine otomatik EMERGENCY_HOLD uygulanır (§7). Tarama listesi admin tarafından güncellenebilir |
| VPN/proxy tespiti | MVP'de destekleyici sinyal olarak kullanılır — tek başına engelleme sebebi değil, diğer risk sinyalleriyle birlikte değerlendirilir |

---

## 22. Kullanıcı Sözleşmesi

- Kullanıcı sözleşmesi / Terms of Service olacak
- Detayları ileriye bırakıldı

---

## 23. Downtime Yönetimi

| Durum | Davranış |
|---|---|
| Platform bakımı | Aktif işlemlerin timeout süreleri dondurulur, bakım bitince kaldığı yerden devam eder. Kullanıcılara önceden bildirim gönderilir |
| Steam kesintisi | Aynı yaklaşım — timeout süreleri dondurulur |

---

*Skinora — Product Requirements v2.5*
