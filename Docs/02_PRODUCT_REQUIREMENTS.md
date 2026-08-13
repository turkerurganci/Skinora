# Skinora — Product Requirements

**Versiyon: v3.1** | **Bağımlılıklar:** `01_PROJECT_VISION.md`, `PRODUCT_DISCOVERY_STATUS.md` | **Son güncelleme:** 2026-08-13

> **v3.1 (2026-08-13, T122):** §9.2 canlı Steam ölçümüne göre revize edildi — item eşleştirmesinin **sayım**
> tabanlı olma zorunluluğu kanıtla gerekçelendirildi; aşınma/desen kapsam dışılığının gerekçesi "veri yok"tan
> "kanıt eşiği ürün kararı"na düzeltildi (veri `asset_properties` içinde anonim olarak mevcut); anonim okumanın
> Trade Protection kilit tarihini göremediği normatif not olarak eklendi. Davranış değişikliği yok.

---

## 1. Genel Bakış

Bu doküman, Skinora escrow platformunun ürün gereksinimlerini tanımlar. Tüm gereksinimler product discovery sürecinde alınan kararlara dayanmaktadır.

---

## 2. İşlem Akışı Gereksinimleri

### 2.1 Escrow Modeli — P2P (item doğrudan, para emanette)

Platform **item'a hiçbir zaman dokunmaz**. Item satıcıdan alıcıya tek bir Steam trade'i ile doğrudan geçer; emanete alınan şey **paradır**.

**Neden:** Steam Trade Protection (16 Temmuz 2025) ve trade cooldown reworku (Şubat 2026) sonrası, bir CS2 item'ı trade ile bir envantere girdiğinde **7 gün boyunca transfer edilemez** (kullanılabilir ama trade/market/storage unit/sticker işlemi yapılamaz). Platform botu item'ı emanete aldığı anda 7 gün boyunca alıcıya gönderemez — item'ın platform üzerinden geçtiği çift-trade modeli bu kural altında çalışamaz. Tek trade ile item doğrudan alıcıya gider; oluşan 7 günlük kilit alıcının kendi envanterinde kalır ve akışı bloklamaz.

**Sonuç — sıra tersine döner:** önce para emanete girer, sonra item teslim edilir. Item tutulamadığı için alıcıyı koruyan tek mekanizma paranın emanette olmasıdır.

### 2.2 Temel Akış

Platformdaki her işlem aşağıdaki 8 adımdan oluşur:

| Adım | Açıklama | Doğrulama |
|---|---|---|
| 1. İşlem oluşturma | Satıcı item'ı seçer, stablecoin türünü belirler, fiyat ve ödeme timeout süresini girer | Platform envanter okuyarak item'ın var ve tradeable olduğunu doğrular |
| 2. Alıcı kabulü | Alıcı işlem detaylarını görür, kabul eder, iade adresini ve Steam trade URL'ini verir. Henüz ödeme yapmaz | Platform alıcının Mobile Authenticator'ının aktif olduğunu doğrular (§9) |
| 3. Satıcı hazırlık onayı | Satıcı item'ı göndermeye hazır olduğunu onaylar | Platform item'ın hâlâ satıcının envanterinde ve tradeable olduğunu yeniden doğrular, alıcı envanteri için referans anlık görüntü (baseline) alır. Ödeme adresi alıcıya **ancak bu adımdan sonra** açılır |
| 4. Ödeme | Platform benzersiz ödeme adresi üretir, alıcı bu adrese toplam tutarı (fiyat + komisyon) gönderir | Platform blockchain üzerinden otomatik doğrular |
| 5. Ödeme doğrulama | Blockchain üzerinden otomatik | Otomatik |
| 6. Item teslimi | **Satıcı, alıcıya doğrudan Steam trade offer gönderir** — platform taraf değildir, yalnızca satıcıya alıcının trade URL'ini içeren hazır bağlantıyı sunar | — |
| 7. Teslim doğrulama | Alıcının "teslim aldım" onayı **veya** envanter kanıtı (item satıcının envanterinden düştü **ve** alıcının envanterinde beklenen item sayısı arttı) | Yarı otomatik — detay §9.2 |
| 8. Satıcıya ödeme | Bekleme penceresi (§4.5) dolduktan sonra platform komisyonu keser, kalan tutarı satıcının cüzdan adresine gönderir | Blockchain üzerinden doğrulanır |

### 2.3 İşlem Kuralları

- Her işlem tek bir item içerir
- Sadece item karşılığı kripto ödeme yapılır (barter yok)
- İşlemi her zaman satıcı başlatır
- İşlem detayları (item, fiyat, stablecoin türü) oluşturulduktan sonra değiştirilemez — değiştirmek isteyen satıcı iptal edip yeniden başlatır
- Sadece tradeable item'larla işlem yapılabilir (trade lock'lu ve trade-protected item'lar desteklenmez)
- Aynı item aynı anda birden fazla açık işlemde kullanılamaz — ikinci işlem oluşturma denemesi reddedilir
- Her iki tarafın da Steam Mobile Authenticator'ı aktif olmalıdır (§9.1)
- Tüm CS2 item türleri desteklenir

---

## 3. Timeout Gereksinimleri

### 3.1 Timeout Yapısı

Her işlem adımı için ayrı timeout süresi bulunur:

| Adım | Sorumlu taraf | Timeout Kuralı |
|---|---|---|
| Alıcının işlemi kabul etmesi (adım 2) | Alıcı | Admin tarafından ayarlanabilir |
| Satıcının hazırlık onayı vermesi (adım 3) | Satıcı | Admin tarafından ayarlanabilir |
| Alıcının ödemeyi göndermesi (adım 4) | Alıcı | Admin min-max ve varsayılan belirler, satıcı bu aralıkta seçer |
| Satıcının item'ı teslim etmesi (adım 6–7) | **Satıcı** | Admin tarafından ayarlanabilir |

> Teslimat adımının sorumlusu, custodial modelin aksine **satıcıdır**. Eski modelde bu adım "alıcının teslim trade offer'ını kabul etmesi" idi ve alıcıya aitti; P2P'de trade'i satıcı gönderdiği için gecikme de satıcıya yazılır (§13 itibar, §14 fraud).

### 3.2 Timeout Sonucu

- Herhangi bir adımda timeout dolarsa işlem iptal olur
- Ödeme alınmışsa alıcıya otomatik iade edilir (platformun desteklediği ve teknik olarak işleyebildiği varlıklar kapsamında — istisnalar §4.4). **Item iadesi diye bir işlem yoktur** — item hiçbir aşamada platformda bulunmadığı için iade edilecek bir eşya da yoktur
- Teslimat adımında timeout dolmadan hemen önce platform son bir teslimat doğrulaması yapar (§9.2); kanıt bulunursa işlem iptal edilmez, teslim edilmiş sayılır. Bu, satıcı item'ı gönderdiği hâlde alıcı onay vermediğinde haksız iadeyi önler
- Ödeme adımında timeout dolduğunda platform adresi izlemeye devam eder — gecikmeli ödeme gelirse alıcıya otomatik iade edilir

### 3.3 Timeout Dondurma

- Platform bakımı sırasında aktif işlemlerin timeout süreleri dondurulur
- Steam kesintileri sırasında da aynı yaklaşım uygulanır. Tespit: Steam envanter/trade-hold sorguları sürekli başarısız olduğunda otomatik algılanır; admin manuel olarak da tetikleyebilir. (v3.0 öncesinde tespit bot health check'ine dayanıyordu; platform Steam hesabı kalmadığı için sinyal salt okunur API çağrılarının sağlığına taşındı — §15)
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
| Zamanlama | Item teslimi doğrulandıktan (§9.2) **ve mutabakat süresi dolduktan** sonra — bkz. §4.5.1 |
| Mutabakat süresi | Varsayılan **8 gün**. Steam'in trade geri alma penceresi (7 gün) kapanana kadar ödeme yapılmaz. Süre admin tarafından ayarlanır (§16.2) |
| Ödeme öncesi son kontrol | Süre dolduğunda, ödeme yapılmadan **hemen önce** item'ın hâlâ alıcının envanterinde olduğu doğrulanır. Değilse trade geri alınmıştır → ödeme yapılmaz, para alıcıya iade edilir (§4.5.1) |
| Akış | Platform komisyonu keser, kalan tutarı satıcının cüzdan adresine gönderir |
| Cüzdan adresi | Satıcı profilinde varsayılan adres tanımlar; işlem başlatırken isterse farklı adres girebilir, girmezse profildeki kullanılır |

### 4.5.1 Mutabakat Süresi ve Trade Geri Alma Koruması

**Sorun.** Steam, trade ile el değiştiren bir item'ı sonraki **7 gün** içinde geri alınabilir tutar (Trade Protection). Geri alma işlemi Steam Support kararı gerektirmez — **trade'in her iki tarafı da** kendi trade geçmişinden tek tıkla başlatabilir. Bu, satıcı için doğrudan bir dolandırıcılık yolu açar:

> Satıcı item'ı gönderir → teslimat doğrulanır → ödemesini alır → trade'i geri alır. Sonuç: item satıcıya döner, para da satıcıda kalır. Alıcı hem item'sız hem parasız kalır.

Steam'in tek caydırıcısı, geri almayı başlatan hesaba uygulanan 30 günlük trade/market yasağıdır. Yüksek değerli tek seferlik bir dolandırıcılık için bu caydırıcı yeterli değildir.

**Çözüm — bekle ve doğrula.**

| Kural | Değer |
|---|---|
| Mutabakat süresi | Teslimat doğrulandıktan sonra **8 gün** (7 günlük geri alma penceresi + 1 gün marj) |
| Süre boyunca | Para platformda tutulur, satıcıya hiçbir ödeme yapılmaz |
| Süre sonunda | Ödeme yapılmadan hemen önce alıcının envanteri kontrol edilir |
| Item hâlâ alıcıda | Trade kesinleşmiştir → komisyon kesilir, kalan satıcıya gönderilir → işlem tamamlanır |
| Item alıcıda değil | Trade geri alınmıştır → **satıcıya ödeme yapılmaz**, para alıcıya iade edilir, işlem `REFUNDED` olur |

Beklemek tek başına korumaz; korumayı sağlayan, sürenin **sonundaki kontroldür**. Bu iki adım birlikte uygulandığında trade geri alma riski tamamen kapanır.

**Geri alma tespit edilirse:**
- Alıcıya tam iade yapılır (iade kuralları §4.6)
- Satıcı hesabına dolandırıcılık işareti konur ve tekrarı yaptırıma tabidir (§14.2)
- Olay audit kaydına yazılır

**Bilinen sonuçları (MVP'de kabul edildi, iyileştirme sonraya bırakıldı):**
- Satıcı parasını 8 gün sonra alır. Sektördeki diğer platformlar da benzer bir gecikme uygular
- Aynı anda platformda tutulan toplam para artar — sıcak/soğuk cüzdan politikası buna göre gözden geçirilmelidir
- İşlem 8 gün boyunca açık kalır; iptal ve anlaşmazlık kuralları bu süreyi de kapsar

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
| Ödeme öncesi — Satıcı | Satıcı iptal edebilir. İade gerekmez — item satıcıda, para henüz gönderilmemiştir |
| Ödeme öncesi — Alıcı | Alıcı iptal edebilir. İade gerekmez |
| Alıcı ödemeyi gönderdiyse — Alıcı | Alıcı tek taraflı iptal edemez |
| Alıcı ödemeyi gönderdiyse — Satıcı | Satıcı iptal edebilir (item'ı göndermekten vazgeçebilir). Para alıcıya iade edilir, satıcıya itibar cezası ve cooldown uygulanır (§13). Alıcının parasını satıcının insafına bırakmamak için bu yol açık tutulmuştur — kapatılırsa satıcı hiçbir şey yapmayıp timeout'u bekler, alıcı daha uzun süre beklemiş olur |
| Satıcı item'ı teslim etmezse (timeout) | Para alıcıya iade, işlem iptal, teslimat gecikmesi satıcıya yazılır (§3.1). İptalden hemen önce son bir teslimat doğrulaması yapılır (§3.2) |
| İptal sonrası cooldown | Var — süre admin tarafından dinamik belirlenir |
| İptal sebebi | Zorunlu — iptal eden taraf sebep belirtmek zorunda |
| Admin doğrudan iptal | Admin, CREATED'dan PAYMENT_RECEIVED'a kadar olan aktif işlemleri (+ FLAGGED) doğrudan iptal edebilir. Sebep zorunludur. İade kuralları standart iptal iade kurallarıyla aynıdır. İşlem CANCELLED_ADMIN durumuna geçer. Ayrı bir yetki (`CANCEL_TRANSACTIONS`) gerektirir |
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
| Item custody | **Yok.** Platform item'ı hiçbir aşamada tutmaz, taşımaz veya emanete almaz (§2.1) |
| Envanter okuma | Platform satıcının Steam envanterini okur, satıcı listeden item seçer |
| Item doğrulama | Platform item'ın var olduğunu ve tradeable olduğunu işlem oluştururken doğrular; satıcı hazırlık onayı verirken (adım 3) ve ödeme onaylandığında (adım 5) yeniden doğrular |
| Transfer sırası | Önce ödeme emanete girer (adım 4), sonra satıcı item'ı doğrudan alıcıya gönderir (adım 6) |
| Trade offer'ı gönderen | Satıcı. Platform taraf değildir; yalnızca alıcının trade URL'ini içeren hazır bağlantıyı satıcıya sunar |
| Desteklenen türler | Tüm CS2 item türleri |
| Trade lock / Trade Protection | Desteklenmez — sadece tradeable item'lar. Trade ile yeni edinilmiş (7 gün korumalı) item'lar da bu kapsamdadır |

### 9.1 Mobile Authenticator Zorunluluğu

- **Her iki tarafın** da Steam Mobile Authenticator'ı aktif olmalıdır
- Gerekçe: taraflardan birinin MA'sı yoksa Steam trade'i 15 günlük kendi escrow'una alır. Bu, P2P'ye geçişin çözdüğü bekleme problemini geri getirir
- Satıcı için kontrol işlem oluşturmada, alıcı için kabul adımında (adım 2) yapılır
- MA durumu Steam üzerinden `GetTradeHoldDurations` ile doğrulanır; hold süresi 0 değilse taraf işleme giremez

### 9.2 Teslimat Doğrulama

Platform, taraf olmadığı bir Steam trade'ini doğrudan göremez (Steam API yalnızca kendi hesabının trade offer'larını gösterir). Teslimat iki bağımsız yoldan doğrulanır:

| Yol | Koşul | Sonuç |
|---|---|---|
| Alıcı onayı | Alıcı "teslim aldım" der | Teslim edilmiş sayılır. Onay alıcının kendi aleyhinedir (onaylayınca parası satıcıya gider), bu yüzden tek başına yeterlidir |
| Envanter kanıtı | Item satıcının envanterinden düştü **ve** alıcının envanterinde beklenen item sayısı referans anlık görüntüye (adım 3) göre arttı | Teslim edilmiş sayılır |

- İki koşulun **birlikte** aranması zorunludur: yalnız "alıcıda item belirdi" yetmez (alıcı aynı item'ı başka bir yerden edinmiş olabilir → para yanlış kişiye gider), yalnız "satıcıdan düştü" de yetmez (satıcı üçüncü bir kişiye göndermiş olabilir)
- Doğrulama şu anlarda çalışır: alıcı onay verdiğinde, dispute açıldığında ve teslimat timeout'u dolmadan hemen önce
- **Item satıcıdan düşmüş ama alıcıya ulaşmamışsa** — yanlış item gönderimi veya üçüncü kişiye gönderim imzasıdır — işlem sessizce iptal edilmez, otomatik olarak dispute'a yükseltilir (§10)
- Alıcının Steam envanteri gizliyse envanter kanıtı üretilemez; bu durumda alıcı onayı tek yoldur ve kullanıcı bu konuda uyarılır
- Item eşleştirmesi item sınıfı üzerinden yapılır (asset ID trade sonrası değiştiği için alıcı tarafında kullanılamaz). Eşleştirme **sayım** üzerinden kurulur, varlık üzerinden değil: bir item sınıfının aynı envanterde birden çok kopyası bulunabilir (T122 ölçümü: 199 asset → 159 ayrık sınıf, en kalabalık sınıfın **9 kopyası**), dolayısıyla "o skin envanterde var mı" kontrolü alıcının o skinden zaten bir kopyası olduğu durumda teslimatı hiç göremez
- Aynı sınıftan iki item arasındaki aşınma/desen farkı **otomatik doğrulamanın kapsamı dışındadır** — bu ayrım `WRONG_ITEM` dispute'una tabidir. **Gerekçe (T122, 2026-08-13):** bu kapsam dışılık bir *veri yokluğu* değil, bir **kanıt eşiği** kararıdır. Steam'in envanter yanıtı asset başına `Wear Rating` (float) ve `Pattern Template` alanlarını anonim olarak döndürüyor (`asset_properties`, bkz. [`INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md`](INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md) §5) — yani veri teslimat doğrulamasının zaten yaptığı okumanın içinde geliyor. Kapsam dışı bırakılmasının sebebi, hangi float farkının "yanlış item" sayılacağının bir **ürün kararı** olması ve bu eşiğin otomatik para hareketine bağlanmasının §9.2'nin konjonksiyon kuralından daha zayıf bir kanıt üretmesidir

> **Veri modeli notu:** Steam trade sonrası asset ID değişir. Platform iki asset referansı takip eder: orijinal (satıcı envanterindeki) ve teslim sonrası alıcıda tespit edilen. Bot/escrow asset ID'si P2P modelinde yoktur. Detay: 06 §8.4.

> **Anonim okuma sınırı (T122 ölçümü, 2026-08-13):** platform envanterleri **anonim** okur (sidecar Steam
> kimlik bilgisi taşımaz — v3.0 kararı). Steam, `owner_descriptions` ve `cache_expiration` alanlarını yalnız
> **sahibinin kendi oturumuna** döndürür; bir item'ın 7 günlük Trade Protection kilidinin **bitiş tarihi bu
> yüzden okunamaz**. Ayrıca `tradable` alanı item **sınıfı** düzeyindedir, asset düzeyinde değil — aynı
> skinin biri kilitli biri serbest iki kopyası tek kayıtla temsil edilir. Teslimat doğrulaması bu nedenle
> kilit durumuna **dayandırılamaz**; yukarıdaki iki yol (alıcı onayı / sayım tabanlı envanter kanıtı)
> tek dayanaktır. Ham ölçüm: [`INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md`](INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md) §6.

---

## 10. Dispute (Anlaşmazlık) Gereksinimleri

### 10.1 Otomatik Çözüm

| İtiraz Türü | Çözüm |
|---|---|
| Ödeme itirazı ("ödedim ama sistem görmüyor") | Blockchain üzerinden otomatik doğrulama |
| Teslim itirazı ("item teslim edilmedi") | §9.2 kanıt kuralları taze olarak çalıştırılır. Kanıt bulunursa işlem teslim edilmiş sayılır ve dispute kapanır; item satıcıdan düşmüş ama alıcıya ulaşmamışsa admin'e yükseltilir; hiçbir hareket yoksa satıcı henüz göndermemiştir |
| Yanlış item itirazı | Sistem, alıcının envanterine referans anlık görüntüden (adım 3) sonra giren item'ları tespit eder ve işlemdeki item ile karşılaştırır. Farklı bir item geldiyse gelen item'ın adı kayda geçirilerek admin'e yükseltilir |

> **Teslim itirazı P2P modelinde birincil risktir.** Custodial modelde teslimat platformun kendi botu tarafından yapıldığı için itiraz nadirdi; artık trade'i satıcı gönderdiğinden teslim edilmeme, eksik gönderim ve yanlış item senaryolarının tamamı bu başlık altında toplanır.

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
- **Admin çözümü (WP5 — minimal):** Admin eskale edilmiş itirazı **satıcı lehine** (işlem onaylanır, satıcı ödenir) veya **alıcı lehine** (işlem geri alınır, alıcıya iade) sonuçlandırır; her iki tarafa bildirim gider, audit kaydı tutulur (03 §6.4, 07 §9.30 AD27–AD29).
- SLA/atama/şablon-kural gibi gelişmiş süreç detayları MVP-sonrasına bırakıldı.

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
| Teslim etmeme etkisi | Ödeme alındıktan sonra item'ı teslim etmeyen satıcının işlemi `CANCELLED_TIMEOUT` veya `CANCELLED_SELLER` ile kapanır ve **satıcının** paydasına yazılır. P2P modelinde teslimat fazının sorumlusu satıcı olduğu için (§3.1) bu, itibar skorunun en belirleyici girdisidir |
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

### 14.2 Sahte İşlem Başlatma ve Teslim Etmeme

- Belirli sürede belirli sayıda iptal yapan kullanıcıya geçici işlem başlatma yasağı
- İptal limiti ve yasak süresi admin tarafından dinamik belirlenir
- İptal oranı itibar skorunu etkiler
- İptal sebebi belirtmek zorunludur

**Teslim etmeme (non-delivery) — P2P modelinin birincil abuse vektörü:**

- Ödemeyi aldıktan sonra item'ı göndermeyen satıcı, alıcının parasını iade süresi boyunca bloke etmiş olur. Para emanette güvendedir ve iade edilir, ancak tekrarlanan davranış platformun kullanılabilirliğini doğrudan bozar
- Yuvarlanan bir zaman penceresi içinde: eşiği aşan ilk tekrarda hesaba otomatik `ABNORMAL_BEHAVIOR` flag'i yazılır, sonraki tekrarda hesap otomatik askıya alınır. Eşikler admin tarafından belirlenir (§16.2)
- Satıcının item'ı üçüncü bir kişiye gönderdiği tespit edilirse (item satıcının envanterinden düşmüş ama alıcıya ulaşmamış — §9.2) olay tek başına yeterli sinyaldir ve doğrudan admin incelemesine yükseltilir

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

**Bu gereksinim kaldırılmıştır (v3.0, P2P geçişi).**

Platform artık Steam hesabı işletmez. Item custody kalktığı için (§2.1, §9) trade offer gönderen/alan bir platform botu yoktur; Steam ile tek etkileşim **salt okunur** envanter ve trade-hold sorgularıdır ve bunlar hesap oturumu değil Web API anahtarı ile yapılır.

Bu bölümün kaldırılmasıyla ortadan kalkan gereksinimler: bot havuzu ve risk dağıtımı, bot kısıtlanma/ban durumunda item recovery akışı, bot sağlık izleme ve admin bot yönetim ekranı. Bunlara bağlı operasyonel risklerin tamamı (bot ban'i, bot oturum kaybı, botta mahsur kalan item) artık mevcut değildir.

> Bölüm numarası bilinçli olarak korunmuştur — 03/04/05/06/07 dokümanlarındaki `02 §16`+ referanslarının kayması engellenmiştir.

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
| Mutabakat süresi | Teslimat doğrulandıktan sonra satıcı ödemesinin kaç gün bekletileceği (varsayılan 8 gün — §4.5.1). Steam'in geri alma penceresinden kısa ayarlanmamalıdır |
| Teslimat ihlali eşikleri | Kaçıncı teslim etmeme olayında fraud flag'i, kaçıncısında otomatik askıya alma uygulanacağı (§14.2) |
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
- Emanetteki **paranın** §2.2 akışına uygun yönetimi: teslimat doğrulanana kadar tutulması, doğrulanınca satıcıya aktarılması, doğrulanamazsa alıcıya iade edilmesi
- Teslimat doğrulamasının §9.2'de tanımlı kanıt kurallarına göre işletilmesi ve kanıt üretilemediğinde işlemin sessizce kapatılmayıp dispute'a yükseltilmesi
- Timeout'larda ve iptal durumlarında paranın iadesi (platformun desteklediği varlıklar kapsamında — istisnalar §4.4)

> Platform **item custody'si garanti etmez, edemez** — item hiçbir aşamada platformda bulunmaz (§2.1, §9). Satıcının item'ı gerçekten gönderip göndermeyeceği platformun kontrolünde değildir; platformun garantisi, item gönderilmediğinde alıcının parasının iade edilmesidir. Steam kaynaklı yaptırım, el koyma, ban, trade reversal ve üçüncü taraf müdahaleleri bu kapsamın dışındadır (§20.2).

### 20.2 Platformun Sorumlu Olmadığı

- Steam'in item'a el koyması veya hesap banlaması
- Item'ın çalıntı çıkması
- Steam'in trade sistemini değiştirmesi
- Blockchain ağındaki olağandışı durumlar

> **Trade geri alma (reversal) — kapatılmış risktir, §4.5.1.** Steam'in 7 günlük geri alma penceresi, satıcının item'ı gönderip parayı aldıktan sonra trade'i geri alması yoluyla istismar edilebilir. Platform bu senaryoyu **satıcı ödemesini 8 gün bekleterek ve süre sonunda item'ın hâlâ alıcıda olduğunu doğrulayarak** engeller. Geri alma tespit edilirse satıcıya ödeme yapılmaz, para alıcıya iade edilir.
>
> Bu nedenle trade geri alma, platformun sorumsuz olduğu bir durum **değildir** — aksine platformun aktif olarak koruduğu bir senaryodur. Platformun sorumsuz kaldığı hâl, Steam'in item'a doğrudan el koyması veya hesabı banlaması gibi platformun gözlemleyip önleyemeyeceği durumlardır.

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
| Sanctions screening | MVP'de cüzdan adresi bazlı temel tarama uygulanır — bilinen yaptırımlı adreslerle eşleşen adresler engellenir. Eşleşme tespit edildiğinde: yeni işlem/adres kaydı engellenir, hesap flag'lenir, kullanıcının tüm aktif işlemlerine otomatik EMERGENCY_HOLD uygulanır (§7). Tarama listesi admin tarafından güncellenebilir — ayrı bir yetki (`MANAGE_SANCTIONS`) gerektirir (07 §9.23–§9.25 AD22/AD23/AD24). MVP'de yalnız manuel admin entry; OFAC/EU/BM JSON feed auto-sync post-MVP |
| VPN/proxy tespiti | MVP'de destekleyici sinyal olarak kullanılır — tek başına engelleme sebebi değil, diğer risk sinyalleriyle birlikte değerlendirilir |

---

## 22. Kullanıcı Sözleşmesi

- Kullanıcı sözleşmesi / Terms of Service olacak
- Detayları ileriye bırakıldı
- **P2P modeliyle sözleşmede yer alması zorunlu hale gelen konular:** platformun item custody'si üstlenmediği ve teslimatı garanti edemediği (§20.1), Steam kaynaklı trade reversal riskinin kullanıcıda olduğu (§20.2), tarafların birbirinin Steam profilini ve trade URL'ini göreceği (§21)

---

## 23. Downtime Yönetimi

| Durum | Davranış |
|---|---|
| Platform bakımı | Aktif işlemlerin timeout süreleri dondurulur, bakım bitince kaldığı yerden devam eder. Kullanıcılara önceden bildirim gönderilir |
| Steam kesintisi | Aynı yaklaşım — timeout süreleri dondurulur. P2P modelinde kesinti trade'in kendisini engellemez (trade doğrudan taraflar arasında geçer), ancak platform teslimatı doğrulayamaz; bu yüzden teslimat fazındaki işlemlerin süresi dondurulur ve satıcı haksız yere teslim etmemiş sayılmaz |

---

*Skinora — Product Requirements v3.1*
