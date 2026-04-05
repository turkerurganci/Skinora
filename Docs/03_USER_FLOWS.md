# Skinora — User Flows

**Versiyon: v2.2** | **Bağımlılıklar:** `01_PROJECT_VISION.md`, `02_PRODUCT_REQUIREMENTS.md` | **Son güncelleme:** 2026-03-22

---

## 1. Genel Bakış

Bu doküman, Skinora platformundaki tüm kullanıcı akışlarını adım adım tanımlar. Her aktörün (satıcı, alıcı, admin) normal akışları, hata senaryoları ve alternatif yolları içerir.

### 1.1 Aktörler

| Aktör | Tanım |
|---|---|
| Satıcı | İşlemi başlatan, item'ı emanet eden, ödemeyi alan taraf |
| Alıcı | İşlemi kabul eden, ödemeyi gönderen, item'ı teslim alan taraf |
| Admin | Platformu yöneten, flag'lenmiş işlemleri inceleyen taraf |
| Platform (Sistem) | Otomatik doğrulama, transfer ve bildirim işlemlerini gerçekleştiren sistem |

### 1.2 İşlem Durumları

| Durum | Açıklama |
|---|---|
| CREATED | İşlem oluşturuldu, alıcı bekleniyor |
| ACCEPTED | Alıcı kabul etti, satıcıdan item bekleniyor |
| TRADE_OFFER_SENT_TO_SELLER | Satıcıya trade offer gönderildi, kabul etmesi bekleniyor |
| ITEM_ESCROWED | Item platforma emanet edildi, ödeme bekleniyor |
| PAYMENT_RECEIVED | Ödeme doğrulandı, item teslimi başlıyor |
| TRADE_OFFER_SENT_TO_BUYER | Alıcıya trade offer gönderildi, kabul etmesi bekleniyor |
| ITEM_DELIVERED | Item alıcıya teslim edildi, satıcıya ödeme işleniyor. Payout durumu (pending/retry/failed) BlockchainTransaction seviyesinde takip edilir — ayrı state değil (06 §3.8). İşlem payout başarılı olana kadar bu state'te kalır |
| COMPLETED | İşlem tamamlandı |
| CANCELLED_TIMEOUT | Timeout nedeniyle iptal |
| CANCELLED_SELLER | Satıcı tarafından iptal |
| CANCELLED_BUYER | Alıcı tarafından iptal |
| CANCELLED_ADMIN | Admin tarafından iptal (flag reddi) |
| FLAGGED | Fraud tespiti nedeniyle durduruldu, admin onayı bekleniyor |

> **Not:** EMERGENCY_HOLD bir state değil, herhangi bir aktif state üzerine uygulanan dondurma mekanizmasıdır — `IsOnHold` flag'i + `TimeoutFreezeReason` ile yönetilir (05 §4.5). Dispute (anlaşmazlık) da ayrı bir işlem durumu değildir. Dispute başlatıldığında işlem mevcut durumunda kalır, dispute ayrı bir bayrak olarak takip edilir.

---

## 2. Satıcı Akışları

### 2.1 İlk Giriş ve Kayıt

1. Satıcı platforma gelir
2. "Steam ile Giriş" butonuna tıklar
3. Steam kimlik doğrulama sayfasına yönlendirilir
4. Steam hesabıyla onay verir
5. Platform geri döner
6. **Sistem kontrolü (deterministik pipeline — 04 §S02):**
   - **Geo-block? →** Yasaklı bölgeden erişim → erişim engellenir (§11a.1)
   - **Sanctions eşleşmesi? →** Mevcut profil adresi yaptırımlı → hesap flag'lenir, aktif işlemlere auto EMERGENCY_HOLD (§11a.3)
   - **Hesap askıya alınmış mı? →** Askıya alınmışsa → kısıtlı (suspended) oturum başlatılır: fon akışı aksiyonları engellenir, aktif işlemler salt okunur erişilebilir
   - **İlk kez giriş mi? →** 18+ yaş beyanı + Kullanıcı Sözleşmesi (ToS) gösterilir — her ikisi kabul edilmeden devam edemez. Yaş gate başarısızsa erişim engellenir (§11a.2)
   - ~~Steam Mobile Authenticator kontrolü login'de yapılmaz~~ — MA kontrolü **trade URL kaydı sırasında** yapılır (08 §2.2). Login'de yalnızca yukarıdaki kontroller geçerlidir.
7. İlk kez geliyorsa hesabı otomatik oluşturulur (Steam ID, profil bilgileri çekilir)
8. Kullanıcı profil ayarlarından **trade URL'ini kaydeder** → bu adımda MA kontrolü yapılır (08 §2.2: `GetTradeHoldDurations` çağrısı trade URL'den parse edilen `trade_offer_access_token` ile). MA aktif değilse → uyarı gösterilir, işlem başlatamaz ama platformu gezebilir. MA aktifse → işlem başlatma yetkisi verilir.
8. Kullanıcı dashboard'a yönlendirilir (davet linkinden geldiyse → işlem detay sayfasına)

### 2.2 İşlem Başlatma (Normal Akış)

1. Satıcı dashboard'dan "Yeni İşlem Başlat" butonuna tıklar
2. **Sistem kontrolü:** Eşzamanlı aktif işlem limitine ulaşılmış mı?
   - **Evet →** "Aktif işlem limitinize ulaştınız" uyarısı gösterilir. İşlem başlatamaz.
   - **Hayır →** Devam eder
3. **Sistem kontrolü:** İptal cooldown süresi aktif mi?
   - **Evet →** "Geçici işlem başlatma yasağınız var, X süre sonra tekrar deneyebilirsiniz" uyarısı gösterilir.
   - **Hayır →** Devam eder
4. **Sistem kontrolü:** Yeni hesap işlem limiti aşılmış mı? (Yeni hesaplar için)
   - **Evet →** "Yeni hesap işlem limitinize ulaştınız" uyarısı gösterilir.
   - **Hayır →** Devam eder
5. Platform satıcının Steam envanterini okur
6. Satıcıya tradeable item listesi gösterilir
7. Satıcı item'ı seçer
8. **Sistem kontrolü:** Item tradeable mi?
   - **Hayır →** "Bu item şu an takas edilemez" uyarısı gösterilir
   - **Evet →** Devam eder
9. Satıcı stablecoin türünü seçer (USDT veya USDC)
10. Satıcı fiyatı girer (stablecoin miktarı olarak, örn: 100 USDT)
11. **Sistem kontrolü:** Fiyat min/max işlem tutarı aralığında mı?
    - **Hayır →** "İşlem tutarı X ile Y arasında olmalıdır" uyarısı gösterilir
    - **Evet →** Devam eder
12. Satıcı ödeme timeout süresini seçer (admin'in belirlediği aralık içinde)
13. Satıcı alıcıyı belirler:
    - **Yöntem 1 (Steam ID):** Alıcının Steam ID'sini girer
    - **Yöntem 2 (Açık link — aktifse):** Açık link seçeneğini tercih eder
14. Satıcı cüzdan adresi belirler:
    - Profilinde varsayılan adres varsa → otomatik gösterilir, isterse değiştirebilir
    - Profilinde yoksa → cüzdan adresi girmesi zorunlu
15. Satıcıya işlem özeti gösterilir (item, fiyat, stablecoin, timeout, alıcı, cüzdan adresi)
16. Satıcı onaylar
17. **Sistem kontrolü (arka plan):** Piyasa fiyatından sapma eşiği aşılıyor mu?
    - **Evet →** İşlem FLAGGED durumuna geçer, admin onayı beklenir. Satıcıya "İşleminiz incelemeye alındı" bilgisi gösterilir.
    - **Hayır →** Devam eder
18. İşlem CREATED durumuna geçer
19. **Alıcıya bildirim:**
    - Alıcı platformda kayıtlıysa → platform bildirimi gider
    - Alıcı kayıtlı değilse → satıcıya davet linki gösterilir, kendisi alıcıya iletir
20. Satıcı alıcının kabul etmesini bekler

> **Not:** İşlem oluşturulduktan sonra detaylar (item, fiyat, stablecoin türü, timeout süresi) değiştirilemez. Satıcı değişiklik yapmak isterse işlemi iptal edip yeniden başlatmalıdır.

### 2.3 Item Emaneti (Adım 3)

1. Alıcı işlemi kabul ettikten sonra satıcıya "Alıcı hazır, item'ını gönder" bildirimi gider
2. Platform satıcıya Steam trade offer gönderir
   - **Trade offer gönderilemezse** (Steam API hatası, item durumu değişmişse) → sistem otomatik yeniden dener. Satıcıya "Trade offer gönderilmeye çalışılıyor" bilgisi gösterilir. Sorun devam ederse timeout süresi içinde çözülmezse işlem iptal olur.
3. İşlem TRADE_OFFER_SENT_TO_SELLER durumuna geçer
4. Satıcı Steam üzerinde trade offer'ı görür
5. **Satıcı trade offer'ı reddederse →** İşlem anında CANCELLED_SELLER durumuna geçer. Item iade gerekmez. Alıcıya "Satıcı işlemi iptal etti" bildirimi gider. İptal kaydı satıcının profiline eklenir.
5a. **Satıcı counter offer yaparsa (Steam state 4) →** Skinora counter offer desteklemez. Orijinal offer iptal sayılır, satıcıya "Counter offer desteklenmiyor, işlem iptal edildi" bildirimi gönderilir. İşlem CANCELLED_SELLER durumuna geçer (08 §2.4).
6. Satıcı trade offer'ı kabul eder
7. Item platformun envanterine geçer

> **Not:** Trade offer kabul sonrası Steam yeni asset ID atar. Bu ID `Transaction.EscrowBotAssetId` olarak kaydedilir. Alıcıya teslim trade offer'ında bu ID kullanılarak doğru item seçilir (06 §8.4).

> **Not:** TradeOffer FAILED durumu iki alt senaryo kapsar: (1) Steam API'ye ulaşılamadan başarısız (SentAt NULL), (2) gönderildi ama sonrasında hata aldı (SentAt dolu). Retry mekanizması her iki senaryoda da geçerlidir (06 §3.9).

8. **Sistem doğrulaması:** Emanet alınan item, işlemdeki item ile eşleşiyor mu?
   - **Evet →** İşlem ITEM_ESCROWED durumuna geçer. Alıcıya "Item emanete alındı, ödeme yapabilirsin" bildirimi gider
   - **Hayır →** Item satıcıya iade edilir. İşlem CANCELLED_ADMIN durumuna geçer. Satıcıya "Emanet alınan item eşleşmedi, item'ınız iade edildi" bildirimi gider. Alıcıya "İşlem teknik nedenlerle iptal edildi" bildirimi gider. Hata loglanır ve admin'e bildirim gider. Satıcı isterse yeni işlem başlatabilir.

### 2.4 Satıcıya Ödeme (Adım 8)

1. Item alıcıya teslim edildikten ve doğrulandıktan sonra
2. Platform komisyonu hesaplar ve keser
3. Gas fee komisyonun %10'unu (veya admin'in belirlediği eşiği) aşıyor mu kontrol edilir:
   - **Aşmıyorsa →** Gas fee komisyondan karşılanır
   - **Aşıyorsa →** Gas fee satıcının payından kesilir
4. Platform kalan tutarı satıcının cüzdan adresine gönderir
   - **Ödeme gönderimi başarısız olursa →** Sistem otomatik yeniden dener. Tekrarlayan başarısızlıkta admin'e bildirim gider. İşlem COMPLETED'a geçmez, ödeme başarılı olana kadar bekler.
5. Satıcıya "Ödemeniz gönderildi" bildirimi gider
6. İşlem COMPLETED durumuna geçer

### 2.4a Satıcı Payout Sorunu Bildirimi (02 §10.3)

**Senaryo A — İşlem COMPLETED, satıcı ödemeyi almadığını iddia ediyor (chain anomaly):**

1. İşlem COMPLETED durumunda — sistem payout'u başarılı olarak kaydetmiş
2. Satıcı işlem detay sayfasında "Ödeme Sorunu Bildir" butonuna tıklar
3. Satıcı sorunu açıklar ve gönderir
4. Sistem payout tx hash'ini blockchain üzerinden otomatik doğrular
5. **Blockchain'de onaylı →** Satıcıya tx hash ve onay bilgisi gösterilir ("Ödemeniz gönderildi, cüzdanınızı kontrol edin")
6. **Blockchain'de sorun tespit edilirse (chain anomaly, reorg vb.) →** Admin'e eskale edilir, admin manuel çözüm uygular
7. Satıcıya her adımda bildirim gider

**Senaryo B — İşlem ITEM_DELIVERED, payout stuck/başarısız (pre-COMPLETED):**

> **Not:** Bu senaryo §2.4 adım 4'te tanımlı retry mekanizması kapsamındadır. Payout başarısız olduğunda işlem COMPLETED'a geçmez, ITEM_DELIVERED'da kalır. Retry otomatik çalışır (exponential backoff, 3 deneme — 06 §3.8). 3 deneme sonrası admin'e eskale edilir. Satıcının ayrıca bildirim yapmasına gerek yoktur — sistem otomatik yönetir.

### 2.5 Satıcı İptal Akışı

1. Satıcı aktif bir işlemin detay sayfasına gider
2. "İşlemi İptal Et" butonuna tıklar
3. **Sistem kontrolü:** Alıcı ödemeyi göndermiş mi?
   - **Evet →** "Alıcı ödeme gönderdiği için iptal edilemez" uyarısı gösterilir. İptal butonu devre dışı.
   - **Hayır →** Devam eder
4. Satıcıdan iptal sebebi istenir (zorunlu)
5. Satıcı sebebi yazar ve iptal onaylar
6. Eğer item zaten platformdaysa → item satıcıya iade edilir
7. İşlem CANCELLED_SELLER durumuna geçer
8. İptal kaydı satıcının profiline eklenir (itibar skoru etkilenir)
9. Alıcıya "İşlem satıcı tarafından iptal edildi" bildirimi gider

---

## 3. Alıcı Akışları

### 3.1 İlk Giriş ve Kayıt

Kayıt ve giriş süreci satıcı akışı ile aynıdır (bkz. §2.1) — Steam ile giriş, ilk kullanıcı için hesap oluşturma ve Kullanıcı Sözleşmesi kabulü adımları aynen geçerlidir. Mobile Authenticator kontrolü login'de değil, trade URL kaydı sırasında yapılır (08 §2.2). Tek fark: alıcı genellikle davet linki üzerinden platforma gelir ve kayıt/giriş sonrası işlem detay sayfasına yönlendirilir.

**Davet linki üzerinden gelen alıcı akışı:**

1. Alıcı davet linkine tıklar (`/invite/:token` — opaque, tek kullanımlık, 06 §3.5 InviteToken)
2. Platforma yönlendirilir
3. Kayıtlı değilse → "Steam ile Giriş" ekranı gösterilir
4. Steam ile giriş yapar (bkz. §2.1 adım 1-7). MA kontrolü ayrıca trade URL kaydında yapılır (§2.1 adım 8).
5. İlk kez geliyorsa hesabı otomatik oluşturulur, Kullanıcı Sözleşmesi gösterilir (bkz. §2.1 adım 7-8)
6. İşlem detay sayfasına yönlendirilir

### 3.2 İşlemi Kabul Etme (Adım 2)

1. Alıcı işlem detay sayfasını görür:
   - Satılan item (isim, görsel, detaylar)
   - Fiyat (örn: 100 USDT)
   - Komisyon (örn: 2 USDT)
   - Toplam ödeyeceği tutar (örn: 102 USDT)
   - Stablecoin türü
   - Ödeme timeout süresi
   - Satıcı bilgileri ve itibar skoru
2. **Yöntem 1 (Steam ID ile):** Sistem alıcının Steam ID'sini kontrol eder
   - Eşleşmiyorsa → "Bu işlem size ait değil" uyarısı gösterilir, kabul butonu devre dışı
   - Eşleşiyorsa → devam eder
3. **Yöntem 2 (Açık link):** İlk gelen kişi kabul edebilir
   - Birisi zaten kabul ettiyse → "Bu işlem başka bir kullanıcı tarafından kabul edildi" gösterilir
4. Alıcı iade adresini belirler:
   - Profilinde varsayılan iade adresi varsa → otomatik gösterilir, isterse değiştirebilir
   - Profilinde yoksa → iade adresi girmesi zorunlu
   - İade adresi olmadan işlem kabul edilemez
5. Alıcı "Kabul Ediyorum" butonuna tıklar
6. İşlem ACCEPTED durumuna geçer
7. Satıcıya "Alıcı işlemi kabul etti, item'ını gönder" bildirimi gider
8. Alıcı satıcının item'ı göndermesini bekler

### 3.3 Alıcı İptal Akışı

1. Alıcı aktif bir işlemin detay sayfasına gider
2. "İşlemi İptal Et" butonuna tıklar
3. **Sistem kontrolü:** Alıcı ödemeyi göndermiş mi?
   - **Evet →** "Ödeme gönderildiği için iptal edilemez" uyarısı gösterilir. İptal butonu devre dışı.
   - **Hayır →** Devam eder
4. Alıcıdan iptal sebebi istenir (zorunlu)
5. Alıcı sebebi yazar ve iptal onaylar
6. Eğer item zaten platformdaysa → item satıcıya iade edilir
7. İşlem CANCELLED_BUYER durumuna geçer
8. Satıcıya "İşlem alıcı tarafından iptal edildi" bildirimi gider

### 3.4 Ödeme Gönderme (Adım 4)

1. Item platforma emanet edildikten sonra alıcıya "Item emanete alındı, ödeme yapabilirsin" bildirimi gider
2. Alıcı işlem detay sayfasına gider
3. Ödeme bilgileri gösterilir:
   - Platform tarafından üretilen benzersiz ödeme adresi
   - Gönderilmesi gereken tutar (fiyat + komisyon)
   - Stablecoin türü
   - Blockchain ağı (Tron TRC-20)
   - Kalan timeout süresi
   - **Uyarı:** "Exchange'den gönderim yapmayın, iade durumunda iade adresinize ulaşamayabilir"
4. Alıcı kendi kripto cüzdanını açar
5. Belirtilen adrese, belirtilen tutarı, belirtilen token ile gönderir
6. Platform blockchain üzerinde adresi izler
7. Ödeme doğrulanır → İşlem PAYMENT_RECEIVED durumuna geçer
8. Satıcıya "Ödeme geldi" bildirimi gider

### 3.5 Item Teslim Alma (Adım 6)

1. Ödeme doğrulandıktan sonra platform alıcıya Steam trade offer gönderir
   - **Trade offer gönderilemezse** (Steam API hatası) → sistem otomatik yeniden dener. Alıcıya "Trade offer gönderilmeye çalışılıyor" bilgisi gösterilir. Sorun devam ederse timeout süresi içinde çözülmezse item satıcıya iade, ödeme alıcıya iade edilir.
2. İşlem TRADE_OFFER_SENT_TO_BUYER durumuna geçer
3. Alıcıya "Item'ın gönderildi, Steam'de trade offer'ı kabul et" bildirimi gider
4. Alıcı Steam üzerinde trade offer'ı görür
5. **Alıcı trade offer'ı reddederse →** İşlem anında iptal olur. Item satıcıya iade edilir, ödeme alıcıya iade edilir (iade tutarı = fiyat + komisyon - gas fee). İşlem CANCELLED_BUYER durumuna geçer. Her iki tarafa bildirim gider.
5a. **Alıcı counter offer yaparsa (Steam state 4) →** Skinora counter offer desteklemez. Orijinal offer iptal sayılır, alıcıya "Counter offer desteklenmiyor, işlem iptal edildi" bildirimi gönderilir. Item satıcıya iade edilir, ödeme alıcıya iade edilir. İşlem CANCELLED_BUYER durumuna geçer (08 §2.4).
6. Alıcı trade offer'ı kabul eder
7. Item alıcının envanterine geçer
8. **Sistem doğrulaması:** Teslim Steam üzerinden doğrulanır
9. İşlem ITEM_DELIVERED durumuna geçer
10. Alıcıya "Item'ınız teslim edildi" bildirimi gider (Not: "İşlem tamamlandı" bildirimi ancak payout başarılı olup COMPLETED'a geçtikten sonra gönderilir)
11. Satıcıya ödeme gönderilir (bkz. 2.4)

---

## 4. Timeout Akışları

> **Not:** Ödeme aşaması (ITEM_ESCROWED) per-transaction Hangfire delayed job ile yönetilir. Diğer aşamaların deadline'ları periyodik scanner/poller ile enforce edilir (06 §3.5).

### 4.1 Alıcı Kabul Timeout'u (Adım 2)

**Tetikleyici:** Alıcı belirlenen süre içinde işlemi kabul etmedi.

1. Timeout süresi dolar
2. İşlem CANCELLED_TIMEOUT durumuna geçer
3. Item henüz platformda değil (adım 3'e geçilmemişti) → iade gerekmez
4. Satıcıya "Alıcı zamanında kabul etmedi, işlem iptal oldu" bildirimi gider
5. Alıcıya (kayıtlıysa) "İşlem zaman aşımı nedeniyle iptal oldu" bildirimi gider

### 4.2 Satıcı Trade Offer Timeout'u (Adım 3)

**Tetikleyici:** Satıcı belirlenen süre içinde trade offer'ı kabul edip item'ı göndermedi.

1. Timeout süresi dolar
2. İşlem CANCELLED_TIMEOUT durumuna geçer
3. Item henüz platformda değil → iade gerekmez
4. Satıcıya "Zamanında item göndermediniz, işlem iptal oldu" bildirimi gider
5. Alıcıya "Satıcı item'ı göndermedi, işlem iptal oldu" bildirimi gider

### 4.3 Ödeme Timeout'u (Adım 4)

**Tetikleyici:** Alıcı belirlenen süre içinde ödemeyi göndermedi veya ödeme doğrulanamadı.

1. Timeout süresi dolar
2. İşlem CANCELLED_TIMEOUT durumuna geçer
3. Item platformdaydı → satıcıya iade edilir
4. **Platform adresi izlemeye devam eder** — gecikmeli ödeme gelirse:
   - Gelen ödeme alıcının iade adresine otomatik iade edilir (iade tutarından gas fee düşülür)
5. Satıcıya "Alıcı ödeme yapmadı, işlem iptal oldu, item'ınız iade edildi" bildirimi gider
6. Alıcıya "Zamanında ödeme yapılmadı, işlem iptal oldu" bildirimi gider

### 4.4 Teslim Trade Offer Timeout'u (Adım 6)

**Tetikleyici:** Alıcı belirlenen süre içinde teslim trade offer'ını kabul etmedi.

1. Timeout süresi dolar
2. İşlem CANCELLED_TIMEOUT durumuna geçer
3. Item platformda → satıcıya iade edilir
4. Ödeme platformda → alıcıya iade edilir (iade tutarı = fiyat + komisyon - gas fee)
5. Satıcıya "Alıcı item'ı teslim almadı, item'ınız iade edildi" bildirimi gider
6. Alıcıya "Zamanında teslim alınmadı, işlem iptal oldu, ödemeniz iade edildi" bildirimi gider

### 4.5 Timeout Yaklaşıyor Uyarısı

**Tüm timeout'lar için:**

1. Sürenin admin tarafından belirlenen oranı dolduğunda
2. İlgili tarafa "Süreniz dolmak üzere, X dakika/saat kaldı" bildirimi gider
3. Bu bildirim platform içi, email ve Telegram/Discord üzerinden gider

---

## 5. Ödeme Edge Case Akışları

### 5.1 Eksik Tutar

1. Alıcı ödemeyi gönderir
2. Platform blockchain üzerinde tutarı kontrol eder
3. Gelen tutar beklenen tutardan az
4. Platform ödemeyi kabul etmez
5. Gelen tutar alıcıya iade edilir (iade tutarından gas fee düşülür)
6. Alıcıya "Eksik tutar gönderildi, ödemeniz iade edildi, lütfen doğru tutarı gönderin" bildirimi gider
7. Timeout süresi devam eder — alıcı süre dolmadan doğru tutarı gönderebilir

### 5.2 Fazla Tutar

1. Alıcı ödemeyi gönderir
2. Platform blockchain üzerinde tutarı kontrol eder
3. Gelen tutar beklenen tutardan fazla
4. Platform doğru tutarı kabul eder
5. Fazla kısım alıcıya iade edilir (iade tutarından gas fee düşülür)
6. İşlem normal akışla devam eder
7. Alıcıya "Fazla tutar gönderildi, X USDT iade edildi" bildirimi gider

### 5.3 Yanlış Token (Desteklenen TRC-20)

1. Alıcı ödeme adresine yanlış ama desteklenen bir TRC-20 token gönderir (örn: USDT yerine USDC veya tersi)
2. Platform token türünü kontrol eder
3. Beklenen token ile eşleşmiyor ama platform bu token'ı işleyebiliyor
4. Platform ödemeyi kabul etmez
5. Gelen token alıcının iade adresine otomatik iade edilir (gas fee düşülür)
6. Alıcıya "Yanlış token gönderildi, lütfen X token ile gönderin" bildirimi gider
7. Timeout süresi devam eder

### 5.3a Desteklenmeyen Token/Kontrat

1. Alıcı ödeme adresine platform tarafından desteklenmeyen bir token/kontrat gönderir
2. Platform bu varlığı tespit eder ancak işleyemez
3. **İşlem state'i değişmez** — desteklenmeyen token ödeme olarak kabul edilmediği için işlem mevcut durumunda (ITEM_ESCROWED) kalır
4. **Timeout devam eder** — alıcı süre dolmadan doğru token ile ödeme gönderebilir
5. **Item emanette kalır** — normal akıştan bağımsız, emanet durumu etkilenmez
6. Desteklenmeyen varlık için ayrı bir admin review süreci başlatılır (otomatik iade garanti edilemez)
7. Alıcıya "Desteklenmeyen varlık tespit edildi. Lütfen doğru token ile ödeme gönderin. Desteklenmeyen varlık için admin incelemesi başlatıldı" bildirimi gider
8. Admin durumu değerlendirir ve mümkünse desteklenmeyen varlığı manuel iade eder (02 §4.4)

### 5.4 Gecikmeli Ödeme (Timeout Sonrası)

1. Ödeme timeout'u dolmuş, işlem iptal edilmiş, item satıcıya iade edilmiş
2. Platform ödeme adresini izlemeye devam eder
3. Gecikmeli ödeme platforma ulaşır
4. Platform ödemeyi otomatik olarak alıcının iade adresine iade eder (iade tutarından gas fee düşülür)
5. Alıcıya "Gecikmeli ödemeniz tespit edildi ve iade edildi" bildirimi gider

### 5.5 Çoklu/Parçalı Ödeme

1. Alıcı aynı ödeme adresine birden fazla transfer gönderir
2. **Senaryo A — İlk transfer doğru tutarda:** İlk transfer kabul edilir, işlem ilerler. Sonraki transferler fazla tutar olarak değerlendirilir → otomatik iade (§5.2)
3. **Senaryo B — Parçalı gönderim (her biri eksik):** Her parçalı transfer ayrı ayrı değerlendirilir. Hiçbiri tek başına beklenen tutara ulaşmadığından her biri §5.1 kuralıyla iade edilir. Platform parçalı ödemeleri birleştirmez — alıcının tek seferde doğru tutarı göndermesi gerekir
4. **Senaryo C — İşlem COMPLETED sonrası ek transfer:** İşlem tamamlanmış, ödeme adresi hâlâ izleniyor. Gelen ek transfer alıcının iade adresine otomatik iade edilir (gecikmeli ödeme kuralı — §5.4)
5. Alıcıya her durumda ilgili bildirim gider

---

## 6. Dispute (Anlaşmazlık) Akışları

> **Not:** Dispute açılması timeout sürelerini durdurmaz. Dispute açık bir işlem timeout nedeniyle iptal olabilir. Bu durumda dispute otomatik olarak kapanır ve standart iade kuralları uygulanır.
>
> **Not:** Dispute yalnızca alıcı tarafından açılabilir. Satıcıya yapılan ödemeler platform tarafından otomatik gerçekleştirildiği için satıcı tarafında dispute mekanizması gerekmez.
>
> **Not:** Bir işlem için aynı türde dispute tekrar açılamaz (rate limiting).
>
> **Not:** Farklı türlerde eşzamanlı aktif dispute'lar mümkündür (ör: PAYMENT + WRONG_ITEM aynı anda). Her biri bağımsız incelenir. `Transaction.HasActiveDispute` en az bir dispute OPEN/ESCALATED olduğunda true'dur (06 §3.11).

### 6.1 Ödeme İtirazı

**Senaryo:** Alıcı "ödedim ama sistem görmüyor" diyor.

1. Alıcı işlem detay sayfasından "İtiraz Et" butonuna tıklar
2. İtiraz türünü seçer: "Ödeme gönderildi ama doğrulanmadı"
3. Sistem blockchain üzerinden otomatik kontrol yapar
4. **Sonuç A — Ödeme gerçekten gelmemiş:**
   - Alıcıya "Blockchain üzerinde ödeme bulunamadı" cevabı gösterilir
   - Transaction hash girme imkanı sunulur, sistem tekrar kontrol eder
5. **Sonuç B — Ödeme gelmiş ama sistem gecikmeli tespit etmiş:**
   - Ödeme doğrulanır, işlem normal akışla devam eder
   - Alıcıya "Ödemeniz doğrulandı, işlem devam ediyor" bildirimi gider

### 6.2 Teslim İtirazı

**Senaryo:** Alıcı "item teslim edilmedi" diyor.

1. Alıcı işlem detay sayfasından "İtiraz Et" butonuna tıklar
2. İtiraz türünü seçer: "Item teslim edilmedi"
3. Sistem Steam üzerinden trade offer durumunu otomatik kontrol eder
4. **Sonuç A — Trade offer henüz kabul edilmemiş:**
   - Alıcıya "Trade offer'ınız aktif, lütfen Steam üzerinden kabul edin" cevabı gösterilir
5. **Sonuç B — Trade offer kabul edilmiş, item alıcının envanterinde:**
   - Alıcıya "Item envanterinize teslim edilmiş durumda" cevabı gösterilir
6. **Sonuç C — Gerçekten bir sorun var:**
   - Sistem çözemezse → admin'e eskalasyon seçeneği sunulur

### 6.3 Yanlış Item İtirazı

**Senaryo:** Alıcı "yanlış item geldi" diyor.

1. Alıcı işlem detay sayfasından "İtiraz Et" butonuna tıklar
2. İtiraz türünü seçer: "Yanlış item teslim edildi"
3. Sistem emanet alınan item ile işlemdeki item'ı otomatik karşılaştırır
4. **Sonuç A — Item eşleşiyor:**
   - Alıcıya "Teslim edilen item, işlemdeki item ile eşleşiyor" cevabı gösterilir
5. **Sonuç B — Item eşleşmiyor (sistem hatası):**
   - İşlem durdurulur
   - Admin'e otomatik eskalasyon
   - Her iki tarafa "İşleminiz incelemeye alındı" bildirimi gider

### 6.4 Admin Eskalasyonu

**Senaryo:** Otomatik çözüm kullanıcıyı tatmin etmedi.

1. Kullanıcı otomatik çözüm sonrası "Admin'e İlet" butonuna tıklar
2. Kullanıcı itiraz detayını yazar
3. İşlem admin kuyruğuna düşer
4. Kullanıcıya "İtirazınız admin ekibine iletildi" bildirimi gider
5. *Admin eskalasyon sürecinin detayları ileriye bırakıldı*

---

## 7. Fraud / Flag Akışları

> **Flag Kategorileri (02 §14.0):**
> - **Hesap flag'i** — Çoklu hesap, anormal davranış gibi hesap seviyesi sinyaller. Kullanıcı yeni işlem başlatamaz. Mevcut aktif işlemler normal devam eder (istisna: yüksek risk durumlarında admin emergency hold uygulayabilir — §8.8).
> - **İşlem flag'i (pre-create)** — AML sapması, yüksek hacim gibi işlem seviyesi sinyaller. İşlem CREATED öncesi durdurulur, timeout başlamaz. Admin onaylarsa devam eder, reddederse iptal olur.

> **Not:** FLAGGED state'inde tüm milestone field'ları (BuyerId, deadline'lar, timestamp'lar) NULL kalır. Timeout motoru çalışmaz. Admin onayı ile CREATED'a geçişte deadline/job initialization yapılır (06 §3.5).

### 7.1 Piyasa Fiyatı Sapma Flag'i (İşlem Flag'i — Pre-Create)

1. Satıcı işlem oluşturur
2. Sistem arka planda item'ın piyasa fiyatını kontrol eder
3. Girilen fiyat, piyasa fiyatından admin'in belirlediği eşikten fazla sapıyorsa:
4. İşlem FLAGGED durumuna geçer (CREATED öncesi — timeout henüz başlamamıştır)
5. Satıcıya "İşleminiz incelemeye alındı" bilgisi gösterilir
6. Admin'e "Flag'lenmiş işlem — fiyat sapması" bildirimi gider
7. **Admin "İşleme Devam Et" →** İşlem CREATED durumuna geçer, normal akış ve timeout başlar
8. **Admin "İptal Et" →** İşlem iptal edilir, satıcıya bildirilir

### 7.2 Yüksek Hacim Flag'i

1. Kullanıcı yeni bir işlem başlatır veya mevcut işlem tamamlanır
2. Sistem kullanıcının belirli süredeki toplam işlem hacmini kontrol eder
3. Admin'in belirlediği eşiği aşıyorsa:
4. Yeni işlemler FLAGGED durumuna geçer
5. Admin'e "Yüksek hacim tespiti" bildirimi gider
6. Admin inceler ve onay/red verir

### 7.3 Anormal Davranış Flag'i (Hesap Flag'i)

1. Sistem kullanıcı davranışını izler
2. Anormal patern tespit edilirse (örn: hiç işlem yapmayan hesap aniden yüksek hacimli işlem yapıyor):
3. İlgili hesap flag'lenir (hesap flag'i — kullanıcı yeni işlem başlatamaz, mevcut aktif işlemler etkilenmez)
4. Admin'e "Anormal davranış tespiti" bildirimi gider
5. Admin inceler ve karar verir (flag kaldırma, geçici blok veya kalıcı askıya alma)

> **Not:** Wash trading (aynı alıcı-satıcı çifti arasında 1 aydan kısa aralıkla tekrarlayan işlemler) anormal davranış flag'lemesinden farklı çalışır. Wash trading tespit edildiğinde işlem engellenmez ve flag'lenmez — sadece bu işlemlerin itibar skoruna etkisi kaldırılır (bkz. 02 §14.1).

### 7.4 Çoklu Hesap Tespiti (Hesap Flag'i)

1. Sistem cüzdan adreslerini çapraz kontrol eder:
   - **Güçlü sinyal:** Satıcı ödeme adresi veya alıcı iade adresi birden fazla hesapta eşleşiyorsa → hesap flag'lenir
   - **Destekleyici sinyal:** Ödeme gönderim adresi (kaynak adres) birden fazla hesapta görünüyorsa → tek başına flag sebebi değildir. Bilinen exchange/custodial adresleri bu kontrolden hariç tutulur (02 §14.3)
2. Aynı cüzdan adresi (güçlü sinyal) birden fazla hesapta tespit edilirse:
3. İlgili hesaplar flag'lenir (hesap flag'i — yeni işlem engeli, mevcut aktif işlemler etkilenmez)
4. Admin'e "Çoklu hesap tespiti — aynı cüzdan adresi" bildirimi gider
5. **Destekleyici sinyal:** Aynı IP veya cihaz parmak izinden birden fazla hesapla işlem yapılması da destekleyici sinyal olarak değerlendirilir
6. Admin inceler ve karar verir (flag kaldırma, geçici blok veya kalıcı askıya alma)

---

## 8. Admin Akışları

### 8.1 Admin Giriş

1. Admin platforma giriş yapar
2. Admin paneline yönlendirilir
3. Dashboard'da özet bilgiler görünür:
   - Aktif işlem sayısı
   - Flag'lenmiş işlem sayısı (bekleyen)
   - Günlük/haftalık tamamlanan işlem sayısı
   - Platform Steam hesaplarının durumu

### 8.2 Flag'lenmiş İşlem İnceleme

1. Admin işlem flag kuyruğunu görür (yalnızca işlem flag'leri — pre-create: fiyat sapması, yüksek hacim)
2. Flag'lenmiş işlemi seçer
3. İşlem detaylarını görür:
   - İşlem bilgileri (item, fiyat, taraflar)
   - Flag sebebi (fiyat sapması, yüksek hacim)
   - İlgili kullanıcıların profilleri ve itibar skorları
   - Piyasa fiyatı karşılaştırması
4. Admin karar verir:
   - **İşleme Devam Et →** Flag false positive — işlem normal akışa döner, taraflara bildirim gider
   - **İptal Et →** Fraud doğrulanmış — işlem iptal edilir, taraflara bildirim gider

> **Not:** Hesap flag'leri (anormal davranış, çoklu hesap) bu kuyrukta görünmez — bunlar ayrı bir hesap flag yönetim yüzeyinden incelenir (02 §14.0, §16.2).

### 8.3 İşlem Listesi ve Arama

1. Admin "İşlemler" bölümüne gider
2. Tüm işlemleri listeler ve filtreler:
   - Duruma göre (aktif, tamamlanmış, iptal, flag'lenmiş)
   - Tarih aralığına göre
   - Kullanıcıya göre (Steam ID veya kullanıcı adı)
   - Tutara göre
3. İşlem detayına tıklayarak tam bilgi görüntüler (taraflar, item, fiyat, durum geçmişi, bildirimler)

### 8.4 Parametre Yönetimi

1. Admin "Ayarlar" bölümüne gider
2. Değiştirilebilir parametreleri görür ve düzenler:
   - Timeout süreleri (her adım için ayrı)
   - Ödeme timeout aralığı (min, max, varsayılan)
   - Komisyon oranı
   - İşlem limitleri (min/max tutar, eşzamanlı işlem)
   - İptal limiti ve cooldown süresi
   - Yeni hesap işlem limiti
   - Gas fee koruma eşiği
   - Fraud sapma eşiği
   - Yüksek hacim eşikleri
   - Alıcı belirleme yöntemi 2 (aktif/pasif)
3. Değişikliği kaydeder
4. Değişiklik anında aktif olur (aktif işlemleri etkilemez, yeni işlemler için geçerli)

### 8.5 Platform Steam Hesapları İzleme

1. Admin "Steam Hesapları" bölümüne gider
2. Tüm platform Steam hesaplarının durumunu görür:
   - Aktif / kısıtlı / banned durumu
   - Her hesaptaki emanet item sayısı
   - Günlük trade offer sayısı
3. Kısıtlı hesap varsa uyarı gösterilir

### 8.6 Rol ve Yetki Yönetimi (Sadece Süper Admin)

1. Süper admin "Rol Yönetimi" bölümüne gider
2. Yeni rol oluşturabilir
3. Role yetki atayabilir (hangi bölümleri görüp düzenleyebileceği)
4. Kullanıcıları rollere atayabilir

### 8.7 Admin Doğrudan İşlem İptali

**Senaryo:** Admin, flag mekanizması dışında operasyonel bir sebepten (yasal talep, kullanıcı şikayeti, teknik sorun) bir işlemi doğrudan iptal etmek istiyor.

1. Admin işlem detay sayfasına gider (S16)
2. İşlem CREATED'dan TRADE_OFFER_SENT_TO_BUYER'a kadar olan aktif bir state'teyse (+ FLAGGED) "İşlemi İptal Et" butonu görünür
3. Admin butona tıklar
4. İptal sebebi girmesi istenir (zorunlu)
5. Admin sebebi yazar ve iptal onaylar
6. **İade kuralları (standart iptal kurallarıyla aynı):**
   - Item platformda emanetteyse → satıcıya iade edilir
   - Ödeme alınmışsa → alıcıya iade edilir (fiyat + komisyon - gas fee)
   - Her iki varlık da varsa → ikisi de iade edilir
7. İşlem CANCELLED_ADMIN durumuna geçer
8. Her iki tarafa "İşleminiz admin tarafından iptal edildi" bildirimi gider (admin notu dahil)
9. İptal kaydı AuditLog'a yazılır

> **Not:** ITEM_DELIVERED aşamasında item alıcıya teslim edilmiş olduğundan standart iptal/iade uygulanamaz. Bu aşamadan sonra admin yalnızca exceptional resolution (manuel inceleme ve müdahale) başlatabilir (02 §7).
>
> **Not:** Bu akış, flag reddi iptali (§8.2/4) ile aynı sonuç durumunu (CANCELLED_ADMIN) üretir ama farklı tetikleyiciye sahiptir. Flag reddi otomatik flag mekanizması üzerinden gelirken, doğrudan iptal admin'in kendi inisiyatifiyle yapılır.
>
> **Not:** Admin doğrudan iptal için ayrı bir yetki gereklidir (`CANCEL_TRANSACTIONS`). Flag yönetim yetkisi (`MANAGE_FLAGS`) bu yetkiyi otomatik vermez.

### 8.8 Admin Emergency Hold

**Senaryo:** Admin, sanctions eşleşmesi, hesap ele geçirme şüphesi veya benzer yüksek risk durumlarında aktif bir işlemi acil olarak dondurmak istiyor.

1. Admin işlem detay sayfasına gider (S16)
2. İşlem herhangi bir aktif state'teyse "Emergency Hold Uygula" butonu görünür
3. Admin butona tıklar
4. Hold sebebi girmesi istenir (zorunlu)
5. Admin sebebi yazar ve hold onaylar
6. **Hold etkileri:**
   - İşlemin timeout süreleri durur
   - İşlem akışı bekler — hiçbir otomatik adım ilerlemez
   - Taraflara "İşleminiz inceleme nedeniyle geçici olarak donduruldu" bildirimi gider
7. İşlem mevcut state'inde kalır, `IsOnHold` flag'i aktif edilir, `TimeoutFreezeReason = EMERGENCY_HOLD` kaydedilir (05 §4.5)
8. Admin incelemesini tamamlar:
   - **Devam ettir →** Hold kaldırılır (`IsOnHold = false`), timeout kaldığı yerden devam eder
   - **İptal et (CREATED → TRADE_OFFER_SENT_TO_BUYER arası) →** Standart admin iptal kuralları uygulanır (§8.7)
   - **İptal et (ITEM_DELIVERED) →** Standart iptal uygulanamaz (item alıcıda). Admin exceptional resolution başlatır — manuel inceleme ve müdahale (§8.7 notu)
9. Tüm hold aksiyonları AuditLog'a yazılır

> **Not:** Emergency hold için ayrı bir yetki gereklidir (`EMERGENCY_HOLD`). Bu yetki `CANCEL_TRANSACTIONS` yetkisinden bağımsızdır (02 §7).

> **Not:** ITEM_DELIVERED state'indeki bir işlem hold'a alınabilir ancak hold'dan CANCEL ile çıkılamaz — yalnızca RESUME izinlidir. Item zaten alıcıya teslim edilmiş olduğundan standart iptal/iade uygulanamaz; exceptional durumlar admin tarafından manuel süreçle çözülür (07 AD19c).

---

## 9. Profil ve Cüzdan Yönetimi Akışları

> **Merkezi Cüzdan Adresi Doğrulama Kuralı:** Cüzdan adresi hangi ekran veya akıştan girilirse girilsin (profil §9.1, işlem başlatma §2.2 adım 14, işlem kabul §3.2 adım 4, adres değiştirme §9.2) aynı doğrulama pipeline'ından geçer: (1) Tron TRC-20 format geçerliliği, (2) sanctions screening (§11a.3). Geçersiz veya yaptırımlı adres hiçbir noktada kaydedilmez/kullanılmaz (02 §12.3).

### 9.1 Cüzdan Adresi Tanımlama

1. Kullanıcı profil sayfasına gider
2. "Cüzdan Adresi" bölümüne tıklar
3. Tron (TRC-20) cüzdan adresini girer
4. **Sistem doğrulaması:** Adres formatı geçerli Tron (TRC-20) adresi mi? (02 §12.3)
   - **Geçersiz →** "Geçerli bir Tron adresi girin" uyarısı gösterilir, kayıt engellenir
   - **Geçerli →** Devam eder
5. **Sanctions kontrolü:** Adres yaptırımlı adres listesiyle karşılaştırılır (§11a.3)
   - **Eşleşme →** Adres kaydedilmez, hesap flag'lenir
   - **Eşleşme yok →** Devam eder
6. Kullanıcıya adres onayı gösterilir ("Bu adres doğru mu?")
7. Kullanıcı onaylar
8. Adres kaydedilir

### 9.2 Cüzdan Adresi Değişikliği

1. Kullanıcı profil sayfasından cüzdan adresini değiştirmek ister
2. Yeni adresi girer
3. **Ek doğrulama:** Steam üzerinden tekrar onay istenir (yeniden kimlik doğrulama)
4. Kullanıcı Steam onayını tamamlar
5. **Cooldown (rol bazlı):** Adres değişikliği sonrası belirli bir süre (admin tarafından ayarlanabilir) fon akışı aksiyonları engellenir — session hijack koruması. **Satıcı payout-address cooldown:** yeni işlem başlatma engellenir; mevcut CREATED davetler eski snapshot adresle devam edebilir. **Alıcı refund-address cooldown:** yeni işlem başlatma ve işlem kabul etme engellenir. Mevcut aktif işlemler eski adresle devam eder (02 §12.3)
6. Yeni adres kaydedilir
7. Kullanıcıya "Cüzdan adresiniz değiştirildi. Güvenlik nedeniyle X saat boyunca yeni işlem başlatılamaz ve mevcut davetleri kabul edemezsiniz." bildirimi gider
8. **Not:** Aktif işlemler eski adresle tamamlanır, yeni adres sadece yeni işlemler için geçerli olur

### 9.3 Profil Görüntüleme

1. Kullanıcı kendi veya başka bir kullanıcının profilini görür
2. Gösterilen bilgiler:
   - Steam profil bilgileri
   - İtibar skoru
   - Tamamlanan işlem sayısı
   - Başarılı işlem oranı
   - Platformdaki hesap yaşı

---

## 10. Hesap Yönetimi Akışları

### 10.1 Hesap Deaktif Etme

1. Kullanıcı profil ayarlarından "Hesabı Deaktif Et" seçeneğini tıklar
2. **Sistem kontrolü:** Aktif işlem var mı?
   - **Evet →** "Aktif işlemleriniz tamamlanmadan hesabınızı deaktif edemezsiniz" uyarısı gösterilir
   - **Hayır →** Devam eder
3. Kullanıcıya onay istenir: "Hesabınız deaktif edilecek, tekrar giriş yaparak aktif edebilirsiniz"
4. Kullanıcı onaylar
5. Hesap deaktif edilir

### 10.2 Hesap Silme

1. Kullanıcı profil ayarlarından "Hesabı Sil" seçeneğini tıklar
2. **Sistem kontrolü:** Aktif işlem var mı?
   - **Evet →** "Aktif işlemleriniz tamamlanmadan hesabınızı silemezsiniz" uyarısı gösterilir
   - **Hayır →** Devam eder
3. Kullanıcıya ciddi uyarı gösterilir: "Bu işlem geri alınamaz. Tüm kişisel verileriniz silinecek."
4. Kullanıcı onaylar
5. Kişisel veriler temizlenir
6. İşlem geçmişi ve audit logları anonim olarak saklanır (audit trail — TransactionHistory + AuditLog korunur)
7. Hesap silinir

---

## 11. Downtime Akışları

### 11.1 Planlı Platform Bakımı

1. Admin bakım planlar
2. Bakımdan önce tüm kullanıcılara bildirim gönderilir (platform içi, email, Telegram/Discord)
3. Aktif işlemlerin timeout süreleri dondurulur
4. Platform bakıma girer
5. Bakım tamamlanır
6. Timeout süreleri kaldığı yerden devam eder
7. Kullanıcılara "Platform tekrar aktif" bildirimi gider

### 11.2 Global Steam Kesintisi

1. Platform Steam servislerinin global olarak çalışmadığını tespit eder (tüm bot'ların health check'i başarısız veya admin manuel tetikleme)
2. Aktif işlemlerin Steam bağımlı adımlarındaki timeout süreleri dondurulur
3. Kullanıcılara "Steam servisleri geçici olarak kullanılamıyor, işlemleriniz etkilenmeyecek" bildirimi gider
4. Steam normale döner
5. Timeout süreleri kaldığı yerden devam eder
6. Kullanıcılara "Steam servisleri normale döndü" bildirimi gider

### 11.2a Tekil Bot Hesabı Kısıtlanması

1. Platform belirli bir bot hesabının kısıtlandığını/banlandığını tespit eder (bot health check veya admin manuel tetikleme)
2. Yeni işlemler aktif diğer bot hesaplarına yönlendirilir
3. Kısıtlanan hesapta emanette olan item'larla ilgili aktif işlemler için recovery/manual intervention akışı başlatılır (02 §15)
4. Admin'e "Bot hesabı kısıtlandı — X aktif işlem etkileniyor" bildirimi gider
5. Admin etkilenen işlemleri değerlendirir ve manuel çözüm uygular (item recovery mümkünse iade, değilse exceptional resolution)

### 11.3 Blockchain Altyapısı Degradasyonu

1. Platform blockchain doğrulama altyapısının sağlıksız olduğunu tespit eder (node/indexer health check başarısız veya admin manuel tetikleme)
2. Ödeme adımındaki aktif işlemlerin timeout süreleri dondurulur
3. Kullanıcılara "Ödeme doğrulama geçici olarak yavaşlayabilir, işlemleriniz etkilenmeyecek" bildirimi gider
4. Altyapı normale döner
5. Gecikmeli ödeme tespiti otomatik yapılır — bekleyen ödemeler doğrulanır
6. Timeout süreleri kaldığı yerden devam eder
7. Kullanıcılara "Ödeme doğrulama normale döndü" bildirimi gider (02 §3.3)

---

## 11a. Erişim Kontrol Akışları (02 §21.1)

### 11a.1 Geo-Block Kontrolü

1. Kullanıcı platforma erişim isteği gönderir
2. Sistem IP adresinden coğrafi konum tespiti yapar
3. **Yasaklı bölge (OFAC/AB/BM yaptırım listesi) →** Kullanıcıya bilgilendirme sayfası gösterilir, platforma erişim engellenir
4. **İzin verilen bölge →** Normal akış devam eder
5. Yasaklı ülke listesi admin tarafından yönetilir ve güncellenebilir

### 11a.2 Yaş Gate'i (Soft — MVP)

1. Kullanıcı kayıt/giriş sürecinde
2. Sistem 18 yaş gereksinimini soft gate olarak kontrol eder. **MVP yöntemi:** Steam hesap yaşı + kullanıcı beyanı (self-attestation). Bu gerçek yaş doğrulaması değildir — biyolojik yaş teyidi sağlamaz, ancak caydırıcı bir katman olarak uygulanır (02 §21.1)
3. **18 yaş altı beyanı veya Steam hesap yaşı uyumsuzluğu →** Platforma erişim engellenir, bilgilendirme gösterilir
4. **18 yaş ve üstü →** Normal akış devam eder

### 11a.3 Sanctions Screening

1. Kullanıcı cüzdan adresi tanımlar veya ödeme gönderir
2. Sistem cüzdan adresini yaptırımlı adres listesiyle karşılaştırır
3. **Eşleşme →** Yeni işlem/adres kaydı engellenir, hesap flag'lenir (hesap flag'i), admin'e bildirim gider
4. **Aktif işlem varsa →** Kullanıcının tüm aktif işlemlerine otomatik EMERGENCY_HOLD uygulanır (§8.8). Timeout durur, akışlar bekler. Admin inceleyip karar verir (devam/iptal)
5. **Eşleşme yok →** Normal akış devam eder
6. Tarama listesi admin tarafından güncellenebilir

---

## 12. Bildirim Özeti

### 12.1 Satıcı Bildirimleri

| Tetikleyici | Bildirim |
|---|---|
| Alıcı işlemi kabul etti | "Alıcı hazır, item'ını gönder" |
| Ödeme doğrulandı | "Ödeme geldi" |
| İşlem tamamlandı | "İşlem tamamlandı" |
| Satıcıya ödeme gönderildi | "Ödemeniz cüzdan adresinize gönderildi" |
| Timeout yaklaşıyor (satıcı aksiyonu gereken) | "Item gönderme süreniz dolmak üzere" |
| Alıcı işlemi iptal etti | "İşlem alıcı tarafından iptal edildi" |
| İşlem iptal oldu | "İşlem iptal oldu" + sebep |
| Item iade edildi (timeout/iptal sonrası) | "Item'ınız iade edildi" |
| İşlem flag'lendi | "İşleminiz incelemeye alındı" |

### 12.2 Alıcı Bildirimleri

| Tetikleyici | Bildirim |
|---|---|
| Yeni işlem daveti | "Sizin için bir işlem oluşturuldu" |
| Item emanete alındı | "Item platforma ulaştı, ödeme yapabilirsin" |
| Eksik/fazla/yanlış ödeme | İlgili uyarı mesajı |
| Item gönderildi | "Item'ın gönderildi, trade offer'ı kabul et" |
| Item teslim edildi | "Item'ınız teslim edildi" (ITEM_DELIVERED — payout henüz işleniyor) |
| İşlem tamamlandı | "İşlem tamamlandı" (yalnızca COMPLETED state'inde gönderilir) |
| Gecikmeli ödeme iadesi | "Gecikmeli ödemeniz iade edildi" |
| Timeout yaklaşıyor (alıcı aksiyonu gereken) | "Ödeme/teslim süreniz dolmak üzere" |
| Satıcı işlemi iptal etti | "İşlem satıcı tarafından iptal edildi" |
| İşlem iptal oldu (timeout) | "İşlem iptal oldu" + sebep |

### 12.3 Admin Bildirimleri

| Tetikleyici | Bildirim |
|---|---|
| Fiyat sapması flag'i | "Flag: Piyasa fiyatından sapma — İşlem #X" |
| Yüksek hacim flag'i | "Flag: Yüksek işlem hacmi — Kullanıcı Y" |
| Anormal davranış | "Flag: Anormal davranış — Kullanıcı Y" |
| Çoklu hesap tespiti | "Flag: Çoklu hesap tespiti — aynı cüzdan adresi — Kullanıcı Y" |
| Eskalasyon | "Yeni eskalasyon — İşlem #X" |
| Satıcıya ödeme başarısız (tekrarlayan) | "Ödeme gönderim hatası — İşlem #X" |
| Steam hesabı sorunu | "Platform Steam hesabı kısıtlandı — Hesap Z" |

> **Not:** Dış kanal bildirimleri (email, Telegram, Discord) `NotificationDelivery` entity'sinde kalıcı olarak takip edilir — teslimat başarısı/başarısızlığı, retry sayısı ve hata mesajı kaydedilir (06 §3.13a).

---

*Skinora — User Flows v2.2*
