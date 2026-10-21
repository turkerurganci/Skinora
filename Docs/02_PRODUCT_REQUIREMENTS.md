# Skinora — Product Requirements

**Versiyon: v4.17** | **Bağımlılıklar:** `01_PROJECT_VISION.md`, `PRODUCT_DISCOVERY_STATUS.md` | **Son güncelleme:** 2026-10-21 — T160c — §4.7 iadede kesilen ağ ücretinin akıbeti: depozitten sıcak cüzdana toplanır (proje sahibi kararı, ⚠ PARA). · Değişiklik geçmişi: [DOC_CHANGELOG.md](DOC_CHANGELOG.md#02_product_requirementsmd)

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
| 1. İşlem oluşturma | Satıcı item'ı seçer, kabul ettiği ödeme ağlarını seçer (§4.2), fiyat ve ödeme timeout süresini girer | Platform envanter okuyarak item'ın var ve tradeable olduğunu doğrular |
| 2. Alıcı kabulü | Alıcı işlem detaylarını görür, satıcının kabul ettiği ağlardan birini seçer, kabul eder, o ağ için iade adresini ve Steam trade URL'ini verir. Henüz ödeme yapmaz | Platform alıcının Mobile Authenticator'ının aktif olduğunu doğrular (§9) |
| 3. Satıcı hazırlık onayı | Satıcı item'ı göndermeye hazır olduğunu onaylar | Platform item'ın hâlâ satıcının envanterinde ve tradeable olduğunu yeniden doğrular, alıcı envanteri için referans anlık görüntü (baseline) alır. Ödeme adresi alıcıya **ancak bu adımdan sonra** açılır |
| 4. Ödeme | Platform seçilen ağda benzersiz ödeme adresi üretir, alıcı bu adrese toplam tutarı (fiyat + komisyon; TRON'da + TRON ağ ücreti — §5) gönderir | Platform blockchain üzerinden otomatik doğrular |
| 5. Ödeme doğrulama | Blockchain üzerinden otomatik | Otomatik |
| 6. Item teslimi | **Satıcı, alıcıya doğrudan Steam trade offer gönderir** — platform taraf değildir, yalnızca satıcıya alıcının trade URL'ini içeren hazır bağlantıyı sunar | — |
| 7. Teslim doğrulama | Alıcının "teslim aldım" onayı **veya** envanter kanıtı (item satıcının envanterinden düştü **ve** alıcının envanterinde beklenen item sayısı arttı) | Yarı otomatik — detay §9.2 |
| 8. Satıcıya ödeme | Bekleme penceresi (§4.5) dolduktan sonra platform komisyonu keser, kalan tutarı satıcının cüzdan adresine gönderir | Blockchain üzerinden doğrulanır |

### 2.3 İşlem Kuralları

- Her işlem tek bir item içerir
- Sadece item karşılığı kripto ödeme yapılır (barter yok)
- İşlemi her zaman satıcı başlatır
- İşlem detayları (item, fiyat, kabul edilen ağlar) oluşturulduktan sonra değiştirilemez — değiştirmek isteyen satıcı iptal edip yeniden başlatır
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
- **Uyarılan taraf, o fazda eylemi borçlu olan taraftır** (WP7): ödeme fazında **alıcı** (`PaymentDeadline`), teslimat fazında **satıcı** (`DeliveryDeadline`). P2P'de teslim etmeyen satıcı kusurlu taraf olarak kaydedilir (06 §3.1), dolayısıyla teslimat saatinin uyarısız dolması hiç haberdar edilmemiş birini cezalandırır.
- **Uygulama farkı bilinçlidir:** ödeme fazı uyarısı zamanlanmış bir Hangfire job'ıdır, teslimat fazı uyarısı ise `DeadlineScannerJob` süpürmesinden gelir — 05 §4.4 teslimat fazının tamamını scanner-driven yapar ve oraya gecikmeli bir job eklemek faza ikinci bir bağımsız yürütücü verirdi. Tekrar gönderimi `TimeoutWarningSentAt` engeller; iki faz aynı damgayı paylaşır, bu yüzden migration gerekmedi.

---

## 4. Ödeme Gereksinimleri

### 4.1 Ödeme Altyapısı

| Gereksinim | Detay |
|---|---|
| Ödeme yöntemi | Kripto (stablecoin) |
| Desteklenen stablecoin | **Yalnız USDT** (11 §F8 K2, 2026-10-17). USDC ürün yüzeyinden kalkar — bugünkü kodda satıcı hâlâ USDC seçebiliyor, kaldıran görev T166. USDC yalnız yanlış token izin listesinde kalır: yanlışlıkla gönderilen USDC otomatik iade edilir (§4.4) |
| Blockchain ağları | **TRON (USDT TRC-20) ve Ethereum (USDT ERC-20)** (11 §F8 K1). Bugün yalnız TRON çalışır; Ethereum F8'de eklenir (T150–T159) ve açılış iki ağ hazır olunca yapılır. İki ağ Türkiye'deki 9 lisanslı borsanın hepsinde bulunur, bu yüzden her alıcı–satıcı çiftinin en az bir ortak yolu vardır |
| Ödeme modeli | Dış cüzdan — platformda kullanıcı bakiyesi tutulmaz, kullanıcılar kendi cüzdanlarından gönderim yapar. Escrow, iade ve payout işlemleri için platform kontrolündeki operasyonel adres altyapısı kullanılır (detaylar 05 §3.2) |
| Adres üretimi | Her işlem için platform, alıcının seçtiği ağda benzersiz bir ödeme adresi üretir (adres işlem başınadır — 08 §3.0) |
| Doğrulama | Blockchain üzerinden otomatik — ödeme, blockchain üzerinde nihai (final) kabul edildikten sonra onaylanır (teknik doğrulama kriterleri: 05 §3.2) |

### 4.2 Token ve Ağ Seçimi

> **v4.4 (T141, 11 §F8 K2–K3):** eski kural "satıcı USDT veya USDC'den birini seçer" idi. Hedef kural aşağıdadır; ağ seçimi T147'de, arayüzü T156'da, USDC'nin kalkması T166'da uygulanır.
>
> **Uygulandı (T147, backend):** ilan kabul ettiği ağları taşır (07 §7.2 `acceptedNetworks`), alıcı kabul ederken birini seçer (07 §7.6 `network`), satıcının o ağdaki ödeme adresi ve alıcının o ağdaki iade adresi işleme sabitlenir, depozit adresi seçilen ağda kabul anında ayrılır. İstemci ağ vermezse işlem **TRON** sayılır — ağ seçen arayüz (T156) gelene kadar bugünkü istemci aynen çalışsın diye (öneriyle kaydedildi — ⚠). Servisi olmayan (Ethereum, `EthereumSidecar:BaseUrl` boşken — T151–T154 tamamlanana kadar; T150 iskeleti yalnız adres türetir) ya da sağlık dondurmasındaki ağ ilanda da kabulde de reddedilir.
>
> **Uygulandı (T156, arayüz):** satıcı ilan sihirbazında yalnız o an açık ağlar (`supportedNetworks`) arasından, profilinde ödeme adresi olanları seçer; alıcı kabul formunda ilanın ağlarından birini, her ağın ödeyeceği tutarı görerek seçer (TRON'da ağ ücreti ayrı satır). İstemci ağı her istekte açıkça gönderir (04 §7.2, §7.3).

- Ödeme token'ı her zaman **USDT**'dir; satıcı token seçmez
- Satıcı işlem başlatırken **kabul ettiği ağları** seçer (TRON, Ethereum ya da ikisi). Yalnız profilinde o ağ için ödeme adresi kayıtlı olan ağları seçebilir (§12.1)
- Alıcı işlemi kabul ederken satıcının kabul ettiği ağlardan **birini** seçer ve o ağ için iade adresini verir (§12.2)
- Bir işlemde tek ağ ve tek token kabul edilir. Ödeme, satıcıya payout ve her türlü iade **alıcının seçtiği ağda** yapılır; ağlar arası para taşıma, köprü ya da borsa üzerinden dengeleme yoktur
- Ağın ücret farkı alıcıya ağ seçerken gösterilir (TRON'da ayrı bir ağ ücreti satırı vardır, Ethereum'da yoktur — §5). **Uygulandı (T149):** işlem detayı kabulden önce her ağ için `networkQuotes[].networkFeeAmount` ve o ağdaki `totalAmount`'ı verir (07 §7.5); kabulde aynı (5 dakikaya kadar önbellekteki) tahminden sabitlenen satır kabul yanıtında `networkFeeAmount` olarak döner (07 §7.6)

### 4.3 Fiyatlandırma

- Satıcı fiyatı doğrudan stablecoin miktarı olarak girer (örn: 100 USDT)
- Platform fiyata müdahale etmez — iki taraf anlaştıysa fiyat serbesttir
- MVP'de kullanıcıya piyasa fiyatı gösterilmez
- Arka planda piyasa fiyat verisi çekilir ancak sadece fraud tespiti için kullanılır

### 4.4 Ödeme Edge Case'leri

| Senaryo | Davranış |
|---|---|
| Eksik tutar | Platform kabul etmez, gelen tutar iadenin ağ ücreti düşülerek iade edilir (aşağıdaki not), alıcı doğru tutarı baştan gönderir |
| Fazla tutar | Platform doğru tutarı kabul eder, fazlayı iadenin ağ ücreti düşülerek alıcıya iade eder (aşağıdaki not), işlem devam eder |
| Yanlış token (izin listesindeki token — TRON'da TRC-20 USDC, Ethereum'da ERC-20 USDC) | Platform kabul etmez, alıcının **iade adresine** otomatik iade edilir (aşağıdaki not, K9). *Veri modeli notu: Yanlış token ile gelen transfer `WRONG_TOKEN_INCOMING` tipiyle blockchain audit kaydı oluşturulur; `ActualTokenAddress` field'ında yanlış token'ın contract adresi saklanır (06 §3.8).* |
| Desteklenmeyen token/kontrat | Platform bu varlığı işleyemez — otomatik iade garanti edilemez, manuel incelemeye (admin review) düşer |
| Timeout sonrası gecikmeli ödeme | İşlem zaten iptal, platform adresi izlemeye devam eder, gelen ödeme iadenin ağ ücreti düşülerek alıcıya otomatik iade edilir (aşağıdaki not) |
| Çoklu/parçalı ödeme | Platform parçalı ödemeleri birleştirmez — tek seferde doğru tutarın gönderilmesi gerekir. İlk doğru transfer kabul edilir, sonraki transferler fazla tutar kuralıyla iade edilir. İşlem tamamlandıktan sonra gelen ek transferler gecikmeli ödeme kuralıyla iade edilir |

> **Tek iade hedefi: alıcının iade adresi (11 §F8 K9, proje sahibi kararı 2026-10-17 — T110-K1'i tersine çevirir).**
>
> - **Bu tablodaki reddedilen ödeme iadeleri** (eksik tutar · fazla tutarın fazlası · yanlış token · timeout sonrası gecikmeli ödeme · çoklu transferin fazlası) de, §4.6'daki `BUYER_REFUND` gibi, **alıcının kabul anında o ağ için belirlediği iade adresine** gider (`BuyerRefundAddress`, 06 §3.5). Depozit adresi yalnız satıcının hazırlık onayından sonra gösterildiği için (§2.2 adım 3) depozite para geldiğinde bu adres her zaman bellidir. Normatif kaynak: **08 §3.4 "Refund politikası"**.
> - **Eski kural (T110-K1; 2026-10-17'ye kadar geçerliydi):** reddedilen ödemeler "paranın geldiği adrese" dönüyordu; gerekçe, kabul edilmemiş bir transferi beyan edilmiş bir adrese yönlendirmemekti. **Tersine dönme gerekçesi:** MASAK düzenlemesiyle alıcıların çoğu doğrudan bir Türk borsasından ödeyecek; borsanın gönderen adresi paylaşılan bir sıcak cüzdandır ve oraya dönen iade ya kaybolur ya da destek talebi ister. İade adresi ise alıcının kendisinin girdiği, biçim ve yaptırım kontrolünden geçmiş ve adres değişikliği korumasına (§12.3) tabi bir adrestir.
> - **Uygulandı (T147):** eksik tutar, fazla tutarın fazlası, çoklu transferin fazlası, yanlış token ve iptal sonrası gecikmeli ödeme iadeleri alıcının kabul anında o ağ için verdiği iade adresine gider; gönderen adres (`FromAddress`) yalnız kanıt olarak kayıtta kalır. İade adresi yoksa (normal akışta olamaz — depozit kabulden sonra gösterilir) iade gönderen adrese **düşmez**: gönderilmez ve admin uyarılır, para depozitte elle karara bekler (öneriyle kaydedildi — ⚠).
> - **İadenin ağ ücreti (gas) alıcıdan (proje sahibi kararı 2026-10-20 — ⚠ PARA; uygulayan T159a):** bu tablodaki **her** reddedilen ödeme iadesinde — eksik tutar, fazla tutarın fazlası, yanlış token, timeout sonrası gecikmeli ödeme, çoklu transferin fazlası — iadenin ağ ücreti iade edilen tutardan **her iki ağda** (TRON ve Ethereum) düşülür: alıcı **gelen tutar (fazla tutarda yalnız fazlası) − iadenin gas fee'si** alır. Gas fee, §4.6'daki iade ile aynı yoldan hesaplanır (depozitten iade adresine yapılacak transferin canlı tahmini — TRON 08 §3.1a, Ethereum 08 §3.7.7; tahmin alınamazsa ağın yedek ayarı, §4.7 "Gas fee tutarının kaynağı"). En düşük iade eşiği (§4.6) aynen geçerlidir: net iade < çarpan × gas fee (varsayılan 2) ya da negatifse iade yapılmaz, admin'e alarm gider ve para depozitte elle karara bekler. Yanlış tokende kesinti gelen token cinsinden yapılır (USDC ≈ USDT varsayımıyla, mevcut eşik kuralı gibi). **Uygulandı (T159a):** iade satırı net tutarı taşır, düşülen tahmin satırın `GasFee` alanında saklanır (06 §3.8); alıcı bildirimleri net tutarı ve düşülen ücreti gösterir. *Önceki durum (T72–T159a öncesi):* bu iadeler brüt gönderiliyor, ağ ücretini platform ödüyordu — belgeler "gas fee düşülerek" diyordu, kod kesmiyordu (T159 provası).

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
| Item alıcıda değil **ve satıcıya dönmüşse** | Trade geri alınmıştır → **satıcıya ödeme yapılmaz**, para alıcıya iade edilir, işlem `REFUNDED` olur |
| Item alıcıda değil, satıcıya döndüğü de görünmüyor | **Karar verilmez** — admin incelemesine düşer (aşağıdaki not) |
| Kontrolün karar girdisi hiç üretilememişse | **Karar verilmez** — ilk turda admin incelemesine düşer; eşik beklenmez (aşağıdaki not) |

Beklemek tek başına korumaz; korumayı sağlayan, sürenin **sonundaki kontroldür**. Bu iki adım birlikte uygulandığında trade geri alma riski tamamen kapanır.

> **Kontrol neden iki taraflıdır (T129, proje sahibi kararı 2026-08-16):** "item alıcıda değil = geri alınmıştır" eşitliği pencerenin tamamı için geçerli değildir. Steam trade ile edinilen item'ı **7 gün** kısıtlar (T122 runbook §6.1), mutabakat süresi ise **8 gündür** — yani son bir gün boyunca alıcı skini meşru şekilde devredebilir. Tek taraflı okuma bu alıcıya tam iade verir, item'ı da bırakır ve teslim etmiş satıcıyı fraud flag'iyle cezalandırır; yani bu kuralın satıcıya karşı kapattığı dolandırıcılığın **simetriğini alıcıya açar**. Ayırt edici sinyal ucuzdur: geri alma item'ı **satıcıya döndürür**, alıcının devri döndürmez. Bu yüzden alıcı tarafı "item gitti" dediğinde satıcı envanteri de okunur ve yalnızca item'ın satıcıda yeniden belirmesi geri alma sayılır. Ayırt edilemeyen vaka otomatik karara bağlanmaz — ne ödeme akar ne iade yapılır, insan bakar.

> **Geri alma imzası ayrılmayı da şart koşar (T129 düzeltme turu):** Kural item'ın satıcıya **dönmesini** arar; "satıcıda duruyor" bunu tek başına söylemez. Alıcı onayıyla kapanan bir teslimatta platform satıcı envanterini hiç okumaz, dolayısıyla satıcı aynı sınıftan **başka bir kopya** göndermiş olabilir — §9.2 sayım kuralı bunu geçerli teslimat sayar — ve orijinal asset'i envanterinde durur. Bunu geri alma saymak dürüst satıcıyı cezalandırırdı. Bu yüzden imza iki gözlemin birleşimidir: teslimat anında asset'in satıcıdan **ayrıldığı** görülmüş olmalı **ve** kontrol anında yeniden belirmiş olmalı. Yalnız ikincisi varsa vaka ayırt edilemeyen ayrılmadır ve insana düşer.

> **Karar girdisi hiç üretilemeyen vakalar (T129 düzeltme turu):** Kontrolün alıcı tarafını okuyabilmesi için ya teslimatta kaydedilmiş bir asset kimliği ya da `SELLER_CONFIRMED` anında alınmış bir envanter baseline'ı gerekir. Alıcının envanteri o anda gizliyse baseline **bilinçli olarak** alınmaz (03 §2.3 — gizli envanter işlemi durdurmaz, yalnız kanıt yolunu kapatır) ve teslimat alıcı onayıyla kapanırsa asset kimliği de yazılmaz. İki alan da teslimattan **sonra** hiçbir yolla dolmaz, dolayısıyla bu işlemlerde kontrol hiçbir turda sonuca varamaz. Bu vaka "envanter okunamadı" ile aynı şey **değildir**: beklemenin kazanacağı bir şey yoktur, o yüzden eşik beklenmeden ilk turda admin'e düşer ve admin mutabakatı satıcı lehine kapatabilir (07 §9.22b). Aksi hâlde dürüst satıcı hiç ödenemez ve alıcının parası süresiz donar.

> **Launch kapısı (T129):** Geri alma imzası ölçülmemiş bir çıkarımdır (T122 gerçek bir rollback gözleyemedi). Bu yüzden otomatik iade `settlement.reversal_auto_refund_enabled` ayarının arkasındadır ve launch'ta **kapalıdır**: imza kaydedilir, admin'e eskale edilir, para parkta bekler. Kaydedilen imza **yapışkandır** — ayrılmayı gözlemlemiş bir eskalasyon açıkken sonraki turun "item duruyor" okuması ödemeyi serbest bırakmaz; kararı insan verir. Yapışkanlık kaydedilen gerekçeyi de kapsar: sonraki turlar gerekçeyi **yalnız güçlendirebilir**, düşüremez (T129 ikinci düzeltme turu — aksi hâlde tek bir "okunamadı" turu imzayı siliyor ve bir sonraki okuma parayı açık eskalasyonun üstünden bırakıyordu; güç sırası 06 §3.5). Teslimat kanıtı kapısının (§9.2) ikizidir; prosedür DEPLOY_RUNBOOK §I.

**Geri alma tespit edilirse:**
- Alıcıya tam iade yapılır (iade kuralları §4.6)
- Satıcı hesabına dolandırıcılık işareti konur ve tekrarı yaptırıma tabidir (§14.2). İşaret **hesap düzeyindedir** ve kendi tipini taşır (`DELIVERY_REVERSED`) — §14.2'nin saydığı şey tekrar olduğu için vakanın admin kuyruğunda ayırt edilebilir olması gerekir
- İşlem satıcının itibar oranının paydasına yazılır (06 §3.1). Bu, `REFUNDED`'ın paydaya giren tek türüdür: admin dispute iadesi platform kararıdır ve dışarıda kalır
- Olay audit kaydına yazılır

**Bilinen sonuçları (MVP'de kabul edildi, iyileştirme sonraya bırakıldı):**
- Satıcı parasını 8 gün sonra alır. Sektördeki diğer platformlar da benzer bir gecikme uygular
- Aynı anda platformda tutulan toplam para artar — sıcak/soğuk cüzdan politikası buna göre gözden geçirilmelidir
- **Ödeme, kendi depozitinin süpürülmesini bekler — işletme bakiyesi gerekmez (owner kararı 2026-09-17).** Mutabakat kontrolü yalnız ödemenin değil, depozitin sıcak cüzdana süpürülmesinin de kapısıdır (05 §3.3 "Sweep tetikleyicisi"): süpürme teslimat anında değil, kontrol geçtikten sonra çalışır. Bu kapı bilinçlidir — mutabakat süresi boyunca iade hâlâ mümkündür ve iade depozit adresinden çıkar, dolayısıyla depozit iadenin çekilebileceği yerde kalmalıdır. T129 sonrası iki kapı da `SettlementVerifiedAt` olduğu için ödeme ile süpürme **aynı pencerede** ısmarlanıyordu: ödeme saniyeler içinde çıkıyor, süpürme hesap açma + enerji devretme adımlarını geçiyordu ve aradaki farkı platformun sıcak cüzdanda park ettiği bir işletme bakiyesi kapatıyordu (DEPLOY_RUNBOOK §I'de en büyük tek ödeme kadar boyutlandırılmıştı). Ödeme kuyruğu artık işlemin kendi SWEEP satırı `CONFIRMED` olana kadar beklediği için o bakiyeye gerek yoktur: sıcak cüzdan yalnız o işlem için eline geçmiş parayı öder, orada biriken şey komisyondur. **Bedeli:** satıcı parasını süpürme süresi kadar (birkaç dakika) daha geç alır ve süpürme kalıcı olarak başarısızsa ödeme bekler — bu durumda mevcut süpürme alarmı operatöre gider
- İşlem 8 gün boyunca açık kalır; iptal ve anlaşmazlık kuralları bu süreyi de kapsar

### 4.6 İade Politikası

| Gereksinim | Detay |
|---|---|
| İade kapsamı | Tam iade — komisyon dahil (alıcı hizmet almadığı için komisyon da iade edilir). **TRON ağ ücreti satırı da** (§5, K8) sweep'ten önce yapılan iadede komisyon gibi **tam** iade edilir — sweep hiç yapılmadığı için ücret harcanmamıştır (uygulayan T149). **Sweep'ten sonra** yapılan bir iadede satır iade **edilmez** (PARA — proje sahibi kararı (2026-10-18); T149): satır, alıcının işleminin sweep'inin bedelidir ve sweep yapıldığı için harcanmıştır — K8'in "sweep hiç yapılmadığı için ücret harcanmamıştır" gerekçesinin tersi. Fiyat ve komisyon yine tam iade edilir. "Sweep yapıldı" = işlemin SWEEP gönderimi zincire yayınlandı ya da onaylandı; kuyrukta bekleyen ya da başarısız olan sweep yapılmamış sayılır. Bugünkü akışta otomatik iade depozitten çıktığı ve sweep mutabakattan sonra çalıştığı için bu durum yalnız sweep ile payout arasındaki dar aralıkta (ör. admin iptali) oluşur ve boş depozitten çıkamayan iade admin'e düşer; kural elle yapılacak iadenin tutarını belirler |
| Alıcıya iade tutarı | Fiyat + komisyon (+ TRON'da ağ ücreti satırı, sweep'ten önceki iadede tam — yukarıdaki satır) − iadenin gas fee'si. Örnek (§5): 20 $ TRON, toplam 23,30; sweep'ten önce iade gas'ı 2,00 ise alıcı **21,30** alır; sweep'ten sonra **18,40** (20,40 − 2,00) |
| İade adresi | Alıcının işlem kabul ederken **seçtiği ağ için** belirlediği iade adresine gönderilir (detaylar §12.2). Reddedilen ödeme iadeleri de aynı adrese gider (§4.4 notu, K9) |
| Gas fee | İade işleminin gas fee'si iade tutarından düşülür (alıcı karşılar) |
| Platform maliyeti | Alıcı, iadenin **zincirde yanan** ücretini karşılar; platform bu kalemden zarar etmez. **Sıfır değildir:** yakma olmadığında (delege enerji ya da kontratın kendi kullanıcılarının enerjisini ödemesi) kesinti 0 çıkar ve gönderimi mümkün kılan kaynağı platform sağlamış olur. O kaynağın bedeli ayrı bir kalemdir — kullanıcıya "gas fee" adıyla yansıtılmaz. **Kaynak kararı: hibrit** (proje sahibi, 2026-09-16 — hot cüzdan taban hacim kadar TRX kilitler, kalanı yakılır; 08 §3.3). **F8'de bu kararın yerini K4/K8 aldı:** sweep ücretini alıcı, payout ücretini satıcı öder; kilit kodu kapalı bir anahtarın arkasında uykuda kalır (öneriyle kaydedildi — ⚠) (11 §F8) — **uygulandı (T149):** blockchain servisinin `ENERGY_STAKING_ENABLED` ortam değişkeni, varsayılan `false`; kapalıyken servis hiçbir kilidi okumaz, devretme yapmaz ve kilit hesabı ayarlarını yok sayar, her depozit transferi yakma yoluyla gider (08 §3.3). **Depozit açılışı alıcıya yansır** (proje sahibi kararı 2026-10-02, `RefundActivationCostNotCharged`): hiç TRX almamış bir deposit adresinden iade yapılabilmesi için adresin önce açılması gerekir ve zincir açılış ücretini (1 TRX + hot cüzdanın kilitli bandı yetmezse 0,1 TRX; 1,1 TRX × 0,336 $ ≈ 0,37 $) hot cüzdandan yakar. Bu yakma yalnız iade gönderildiği için oluştuğundan iadenin gas fee'sine eklenir ve iade tutarından düşülür; ücretler zincirden okunur (08 §3.1a adım 4). Sweep'teki açılış bugün platformun maliyetidir; F8 hedefinde sweep'in tamamı (açılış dahil) alıcının kabul anında ödediği TRON ağ ücreti satırıyla karşılanır (§5, K8; T149) |
| En düşük iade eşiği | İadenin ağ ücreti iade tutarının büyük kısmını yutuyorsa (net iade < 2 × gas fee) iade yapılmaz, admin'e alarm gider (05 §3.3, 08 §3.4). Çarpan **ağ başına** bir ayardır (öneriyle kaydedildi — ⚠) — **uygulandı (T149):** TRON `min_refund_threshold_ratio`, Ethereum `blockchain.ethereum.min_refund_threshold_ratio`, ikisi de 2,0 (06 §3.17). Sweep'ten önceki iadede eşiğin tabanı ağ ücreti satırını da içerir. Eşik canlı ücret tahminini kullandığı için Ethereum'da yalnız çok küçük tutarları, TRON'da ~8–15 $ altındaki iadeleri engeller |

### 4.7 Gas Fee Yönetimi

| Gereksinim | Detay |
|---|---|
| Alıcının ödeme gas fee'si | Alıcı karşılar (kendi cüzdanından gönderiyor) |
| Sweep (depozit → sıcak cüzdan) gas fee'si | **TRON: alıcı karşılar** — kabul anında ayrı bir "TRON ağ ücreti" satırı olarak sabitlenir ve ödenecek toplama eklenir (§5; 11 §F8 K8; 08 §3.1a). **Ethereum: platform karşılar**, bir tavana kadar; tavanın üstünde sweep bekler ve alarm verir (öneriyle kaydedildi — ⚠) (08 §3.7.7). **Uygulandı (T149):** satır kabul anında yazılır ve toplamla birlikte ödenir; sweep'in TRX'ini sıcak cüzdan öder, satır USDT olarak sweep'le sıcak cüzdana geçer |
| Satıcıya gönderim gas fee'si | **TRON:** platform karşılar (komisyondan düşülür) — aşağıdaki koruma eşiğiyle; TRON'da 2,29–4,53 $, olağan durum 13,03 TRX'lik koldur çünkü borsa depozit adresleri boşaltılır (11 §F8 K8). **Ethereum:** platform bir tavana kadar karşılar, satıcının alacağından düşülmez; tavanın üstünde payout bekler ve alarm verir (öneriyle kaydedildi — ⚠) (08 §3.7.7). **Uygulandı (T149):** payout hesabı ağ başına — TRON'da koruma eşiği kuralı, Ethereum'da satıcı fiyatın tamamını alır; tavanın üstündeki bekleme ve alarm T153/T158 |
| İade gas fee'leri | İade tutarından düşülür (alıcı karşılar — alıcı alır: fiyat + komisyon (+ TRON'da ağ ücreti satırı, sweep'ten önceki iadede tam — §4.6) − gas fee). **Reddedilen ödeme iadelerinde de** (eksik / fazla / çoklu transfer / yanlış token / gecikmeli ödeme) alıcı gelen tutar (fazla tutarda fazlası) − gas fee alır, her iki ağda (proje sahibi kararı 2026-10-20 — ⚠ PARA; uygulayan T159a — §4.4 notu). **Kesilen tutarın akıbeti** (proje sahibi kararı 2026-10-21 — toplanır; uygulayan T160c): iadenin zincir ücretini platform öder, depozitten çıkan iadede kesilen tutar depozitte kalır ve sıcak cüzdana taşınır — satış tamamlanırsa satışın sweep'iyle (ek maliyetsiz), sweep'e ulaşmayan işlemde ya da başka token'da (yanlış token iadesi) ayrı bir toplama transferiyle; ayrı toplama yalnız ücreti toplanan tutardan küçükse yapılır (platform toplamada zarar etmez), değilse tutar depozitte bekler ve yeniden fiyatlanır (05 §3.3) |
| Koruma eşiği | Satıcıya gönderim gas fee'si komisyonun belirli bir yüzdesini aşarsa, aşan kısım satıcının alacağından düşülür (TRON; Ethereum'da uygulanmaz — yukarıdaki satır) |
| Varsayılan eşik | %10 |
| Eşik esnekliği | Admin tarafından değiştirilebilir |
| Gas fee tutarının kaynağı | Kesilen/split'e giren tutar sabit değildir: gönderim öncesi zincirden hesaplanır (TRON: transfer simülasyonu + platform enerji kaynakları + güncel zincir fiyatları + canlı TRX/USDT kuru — 08 §3.1a; Ethereum: gas tahmini wei cinsinden + canlı ETH/USDT kuru — 08 §3.7.7, T154). Hesaplama yapılamazsa iadede tahmin geçici hatada kısa süre yeniden sorulur (12 sn içinde en çok 3 kez); yine yoksa aynı ağ, gönderen ve token için son ölçülen tahmin (en çok 6 sa eski) ile admin ayarlarındaki fallback değerden **küçüğü**, ölçüm yoksa fallback değer kullanılır ve alarm verilir — alıcı yedekte T160b öncesinden fazla ödemez (T160b, öneriyle kaydedildi — ⚠ PARA — 08 §3.1a); payout'ta doğrudan fallback. Para akışı hesaplamaya asla bloke olmaz |
| Satıcıya kalan tutar sıfır ya da altındaysa | Gas tahmini fiyatı ve platformun payını birlikte aşarsa satıcıya gönderilecek bir şey kalmaz — ödeme gönderilmez. İşlem ödeme kuyruğunu tıkamadan bekletilir ve 1 sa → 4 sa → 12 sa → her 24 sa arayla yeniden fiyatlanır: sıcak cüzdanın enerjisi yenilendikçe tahmin düşer ve ödeme kendiliğinden çıkar. Üçüncü ertelemede admin'e bir kez alarm gider; denemeler sürer. En düşük satış fiyatını gas maliyetinin üstünde tutmak bu durumu seyrekleştirir (DEPLOY_RUNBOOK §A) |

---

## 5. Komisyon Gereksinimleri

| Gereksinim | Detay |
|---|---|
| Komisyonu ödeyen | Alıcı |
| Alıcının ödediği toplam | Item fiyatı + komisyon; **TRON'da + TRON ağ ücreti satırı** (11 §F8 K8 — sweep maliyeti, kabul anında sabitlenir, 2 haneye yukarı yuvarlanır; örnek 2,90 USDT, TRX fiyat tamponu dahil — aşağıdaki örnek tutarlar notu). Ethereum'da ağ ücreti satırı yoktur |
| TRON ağ ücreti satırı (T149) | `satır = YUKARI_2( sweep maliyeti SUN ÷ 1.000.000 × canlı TRX/USDT × (1 + tampon) )`. Sweep maliyeti = hesap açma (1 TRX + sıcak cüzdanın kilitli bandı yetmezse 0,1 TRX) + transferin **tamamı** için yakılacak TRX'in %10 paylı gönderimi (64.285 enerji × 100 SUN × 1,1 = 7,07 TRX; depozitte kalan pay geri toplanmaz) + o gönderimin bandı (270 bayt × 1.000 SUN = 0,27 TRX) — blockchain servisinin canlı tahmini (08 §3.1a). Tampon `blockchain.tron.network_fee_buffer_ratio` (varsayılan 0,007); tahmin alınamazsa `blockchain.tron.network_fee_fallback_usdt` (varsayılan 3,00) yazılır ve kabul beklemez (öneriyle kaydedildi — ⚠). Satır bir kez sabitlenir, sonradan düzeltilmez: sweep pahalıya gelirse eksik, ucuza gelirse artı platformda kalır |
| Varsayılan oran | %2 |
| Oran esnekliği | Admin tarafından değiştirilebilir |
| Gelir modeli | MVP'de sadece komisyon |

> **Komisyon yuvarlama kuralı (T144 — 06 §8.3, 07 §2.8, 09 §7.2/§14.3 aynı kuralı yazar):** komisyon kuruşa **aşağı** kesilir: `komisyon = ROUND(fiyat × oran, 2, sıfıra doğru)`. Fiyat 2 ondalıklı girildiği için (07 §7.2) alıcının ödeyeceği toplam (fiyat + komisyon; TRON'da + ağ ücreti satırı) her zaman **2 ondalıklıdır**; ekranda ve bildirimde gösterilen tutar, ödeme doğrulamasının beklediği tutarın kendisidir. Örnek: 10,55 × %2 = 0,211 → komisyon **0,21**, toplam **10,76**; 10,25 × %2 = 0,205 → **0,20**, toplam **10,45**.
>
> - **Yön: aşağı (öneriyle kaydedildi — ⚠).** Alıcıya en adil olan budur: alıcı hiçbir zaman `fiyat × oran`dan fazla ödemez, platform işlem başına 1 sentten azını bırakır. Yarımı yukarı ya da en yakına yuvarlama alıcıdan yarım sente kadar fazla alırdı. 09 §14.3'ün "kullanıcı aleyhine yuvarlama yapılmaz" kuralıyla aynı yöndür.
> - **Neden 2 hane:** BtcTurk ve Paribu USDT'yi 2 haneden fazla göndermez. T144'ten önce komisyon 6 haneyle tutuluyordu: 10,55 fiyatta beklenen tutar 10,761, ekrandaki "10,76" idi ve 10,00–99,99 fiyatlarının yalnız %2'sinde (50'nin katı olan kuruşlar) toplam 2 haneye sığıyordu. Çözüm daha fazla hane göstermek değil, toplamı 2 haneli yapmaktır.
> - **Doğrulama:** tolerans yok — gelen tutar beklenen tutarla tam eşleşmeli. 2'den fazla haneli bir zincir tutarı (ör. 10,761 ya da 10,759) mevcut kurallara düşer: büyükse **fazla tutar** (beklenen kabul edilir, fazlası iade edilir; fazla en düşük iade eşiğinin altındaysa iade yapılmaz, admin'e alarm gider — §4.6), küçükse **eksik tutar** (gelen tutar iade edilir) (§4.4).
> - **Depolama** ölçeği 6 ondalık kalır; 6 hane zincirden ölçülen tutarlar (gas fee, gelen transfer) içindir (06 §8.3). TRON ağ ücreti satırı ayrı bir kuraldır ve 2 haneye **yukarı** yuvarlanır (K8, T149).

**Örnek tutarlar (11 §F8 K8; komisyon %2, TRX 0,34 $, TRON ağ ücreti 2,90 — örnek, TRX fiyat tamponu dahil: 1,1 + 6,43 + 0,64 + ~0,3 = 8,47 TRX ≈ 2,88 $, 2,90 ~%0,7 tampon içerir; tamponun oranı ağ başına ayardır — T149: `blockchain.tron.network_fee_buffer_ratio` = 0,007). Kodun hesabı (T149): 1.100.000 + 7.071.349 + 270.000 = 8.441.349 SUN = 8,441349 TRX × 0,34 = 2,870059 $ × 1,007 = 2,8901 → yukarı **2,90**. Yuvarlama yalnız sonda yapılır (önce 2,87'yi 2,88'e yuvarlayıp sonra tampon eklemek 2,91 verirdi):** satıcının alacağı = fiyat − max(0, payout ücreti − komisyonun %10'u). TRON payout ücreti 2,29–4,53 $.

| Satış | Ağ | Alıcı gönderir | Satıcı alır |
|---|---|---|---|
| 20 $ | TRON | 20 + 0,40 + 2,90 = **23,30** | 20 − (4,53 − 0,04) = **15,51** ile 20 − (2,29 − 0,04) = **17,75** arası |
| 20 $ | Ethereum | 20 + 0,40 = **20,40** | **20,00** (ağ ücretini platform üstlenir) (öneriyle kaydedildi — ⚠) |
| 200 $ | TRON | 200 + 4 + 2,90 = **206,90** | 200 − (4,53 − 0,40) = **195,87** ile 200 − (2,29 − 0,40) = **198,11** arası |
| 200 $ | Ethereum | 200 + 4 = **204,00** | **200,00** |

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

### 9.1a Steam Takas Uygunluğu — MA tek koşul değildir

> **§9.1'in düzeltmesi (2026-09-14).** "Hold süresi 0" **takas edebilirlik değildir**. Steam'in iki ayrı kısıtı daha var ve ikisi de hold süresini 0 gösterir; platform bunları 2026-09-02 canlı provasına kadar hiç okumuyordu (prova alıcının parası zincirde onaylandıktan sonra teslimat adımında durdu).

- **Limited hesap.** Steam, ömrü boyunca **en az 5 USD** harcamamış hesapların takas yapmasına izin vermez. Bu kısıt MA'dan ve hold süresinden bağımsızdır; çaresi harcamadır, beklemek değildir.
- **15 günlük bekleme.** Steam yeni hesapların ilk **15 gün** takas yapmasına izin vermez. Çaresi beklemektir.
- **Her iki taraf için de** geçerlidir: satıcı gönderemez, alıcı alamaz. Bu yüzden kontrol her iki yüzde de yapılır — satıcıda işlem oluştururken, alıcıda kabul ve hazırlık onayı adımlarında.
- Üç koşul (**limited değil · 15 gün dolmuş · hold 0**) **ayrı ayrı** taşınır ve ayrı hata kodlarıyla reddedilir. Tek bir "uygun/uygun değil" bayrağına indirgenmez: kullanıcıya yanlış çareyi söylemek, engeli hiç söylememek kadar zararlıdır.
- Steam'e sorulamazsa kapı **kapanır** ve bu **geçici** bir hata olarak bildirilir — "kısıtlısın" denmez. Ayrıntı: 08 §2.2a.
- **15 günlük beklemede kalan gün sayısı kullanıcıya gösterilir** (satıcının ilan açma kapısı; 04 §7.2, 07 §7.3). Bu, bu listedeki tek "beklemekle biten" engeldir: bitiş tarihini kullanıcı hiçbir yerden göremez, dolayısıyla "ilk 15 gün" demek ne zaman geçtiğini söylemez. Sayı yukarı yuvarlanır ve en az **1**'dir — hâlâ engelliyken "0 gün kaldı" yanlış olurdu. Diğer iki engele gün sayısı **iliştirilmez**: kısıtlı hesabın çaresi harcamadır, "sorulamadı"nın çaresi tekrar denemektir.
- Süresi dolan bir işlemde engel **karşı tarafın** hesabından geliyorsa kusur kimseye yazılmaz (§13, §14.2 ile aynı sınıf: platform/dış kısıt kaydı, kullanıcı kusuru değil).

### 9.2 Teslimat Doğrulama

Platform, taraf olmadığı bir Steam trade'ini doğrudan göremez (Steam API yalnızca kendi hesabının trade offer'larını gösterir). Teslimat iki bağımsız yoldan doğrulanır:

| Yol | Koşul | Sonuç |
|---|---|---|
| Alıcı onayı | Alıcı "teslim aldım" der | Teslim edilmiş sayılır. Onay alıcının kendi aleyhinedir (onaylayınca parası satıcıya gider), bu yüzden tek başına yeterlidir |
| Envanter kanıtı | Item satıcının envanterinden düştü **ve** alıcının envanterinde beklenen item sayısı referans anlık görüntüye (adım 3) göre arttı | Teslim edilmiş sayılır |

- İki koşulun **birlikte** aranması zorunludur: yalnız "alıcıda item belirdi" yetmez (alıcı aynı item'ı başka bir yerden edinmiş olabilir → para yanlış kişiye gider), yalnız "satıcıdan düştü" de yetmez (satıcı üçüncü bir kişiye göndermiş olabilir)
- Doğrulama şu anlarda çalışır: alıcı onay verdiğinde, dispute açıldığında, teslimat timeout'u dolmadan hemen önce **ve teslimat süresi içinde periyodik taramayla** (`P2P-DeliveryPollingJob`, proje sahibi kararı 2026-10-02). Tarama her işleme en çok 10 dakikada bir, dakikada en çok bir işleme bakar; önce yalnız satıcının envanterini okur ve item hâlâ oradaysa tur orada biter (tek okuma). Yalnız **aynı turda** iki koşul birlikte görülürse davranır — önceki turlardan kalan yarım gözlemlerle birleştirmez: kapı (aşağıda) kapalıyken kanıtı kaydeder ve alıcıya "item'ın gelmiş görünüyor, kontrol edip onayla" bildirimi (`DELIVERY_DETECTED`) gönderir; kapı açıkken işlemi teslim edilmiş sayar. Yanlış-teslimat imzasına dokunmaz — teslimat süresi dolmadan alıcı tarafı henüz güncellenmemiş olabilir; o karar dispute ve süre sonu turlarına kalır
- **Item satıcıdan düşmüş ama alıcıya ulaşmamışsa** — yanlış item gönderimi veya üçüncü kişiye gönderim imzasıdır — işlem sessizce iptal edilmez, otomatik olarak dispute'a yükseltilir (§10)
- Alıcının Steam envanteri gizliyse envanter kanıtı üretilemez; bu durumda alıcı onayı tek yoldur ve kullanıcı bu konuda uyarılır
- Item eşleştirmesi item sınıfı üzerinden yapılır (asset ID trade sonrası değiştiği için alıcı tarafında kullanılamaz). Eşleştirme **sayım** üzerinden kurulur, varlık üzerinden değil: bir item sınıfının aynı envanterde birden çok kopyası bulunabilir (T122 ölçümü: 199 asset → 159 ayrık sınıf, en kalabalık sınıfın **9 kopyası**), dolayısıyla "o skin envanterde var mı" kontrolü alıcının o skinden zaten bir kopyası olduğu durumda teslimatı hiç göremez
- Aynı sınıftan iki item arasındaki aşınma/desen farkı **otomatik doğrulamanın kapsamı dışındadır** — bu ayrım `WRONG_ITEM` dispute'una tabidir. **Gerekçe (T122, 2026-08-13):** bu kapsam dışılık bir *veri yokluğu* değil, bir **kanıt eşiği** kararıdır. Steam'in envanter yanıtı asset başına `Wear Rating` (float) ve `Pattern Template` alanlarını anonim olarak döndürüyor (`asset_properties`, bkz. [`INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md`](INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md) §5) — yani veri teslimat doğrulamasının zaten yaptığı okumanın içinde geliyor. Kapsam dışı bırakılmasının sebebi, hangi float farkının "yanlış item" sayılacağının bir **ürün kararı** olması ve bu eşiğin otomatik para hareketine bağlanmasının §9.2'nin konjonksiyon kuralından daha zayıf bir kanıt üretmesidir

> **Launch kapısı (T125, 2026-08-14):** Yukarıdaki iki yoldan **envanter kanıtı** yolu, launch anında
> otomatik para hareketine bağlı DEĞİLDİR. `delivery.inventory_evidence_auto_release_enabled`
> (06 §8, seed default `false`) kapalıyken `SELLER_ASSET_GONE ∧ INVENTORY_DELTA` kanıtı üretilir,
> kaydedilir (06 §3.5a) ve gösterilir ama tek başına ödemeyi serbest bırakmaz — işlem iptal de edilmez.
> **Alıcı onayı yolu bu kapıdan etkilenmez** (onay alıcının kendi aleyhinedir, platformun çıkarımı
> değildir). Gerekçe: T122 gerçek bir trade yapamadığı için teslimat gecikmesini, `assetid` rotasyonunu
> ve `Item Certificate` kalıcılığını ölçemedi; kapı bu ölçümün ilk gerçek teslimatlardan gelmesini ve
> bir insan tarafından okunmasını şart koşar. Açma prosedürü: DEPLOY_RUNBOOK §H.

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
| Teslim itirazı ("item teslim edilmedi") | §9.2 kanıt kuralları taze olarak çalıştırılır. Kanıt bulunursa işlem teslim edilmiş sayılır ve dispute kapanır; item satıcıdan düşmüş ama alıcıya ulaşmamışsa admin'e yükseltilir; hiçbir hareket yoksa satıcı henüz göndermemiştir. **İstisna (T130):** envanter kanıtı yeterli ama otomatik onay kapısı kapalıysa dispute kapanmaz — OPEN kalır ve eskale edilebilir (03 §6.2 Sonuç E), çünkü kapı zaten parayı bırakmıyorken dispute'u da kapatmak alıcıya hiçbir çıkış bırakmaz |
| Yanlış item itirazı | Sistem, alıcının envanterine referans anlık görüntüden (adım 3) sonra giren item'ları tespit eder ve işlemdeki item ile karşılaştırır. Farklı bir item geldiyse gelen item'ın adı kayda geçirilerek admin'e yükseltilir |

> **Teslim itirazı P2P modelinde birincil risktir.** Custodial modelde teslimat platformun kendi botu tarafından yapıldığı için itiraz nadirdi; artık trade'i satıcı gönderdiğinden teslim edilmeme, eksik gönderim ve yanlış item senaryolarının tamamı bu başlık altında toplanır.

### 10.2 Dispute Kuralları

| Gereksinim | Detay |
|---|---|
| Dispute açma yetkisi | Yalnızca alıcı dispute açabilir. Satıcıya yapılan ödemeler platform tarafından otomatik gerçekleştirildiği için satıcı tarafında dispute mekanizması gerekmez |
| Timeout etkisi | Dispute açılması timeout sürelerini durdurmaz. Dispute açık bir işlem timeout nedeniyle iptal olabilir — bu durumda dispute otomatik kapanır ve standart iade kuralları uygulanır |
| Rate limiting | Bir işlem için aynı türde dispute tekrar açılamaz |
| Yeniden eskalasyon (T131) | Kural **ikinci bir dispute SATIRI** yasağıdır (`UQ_Disputes_TransactionId_Type`), karara bağlanmış bir satırın bir daha ele alınamayacağı anlamına gelmez. Karardan **sonra** platformun kendisi yeni bir olgu gözlemlerse — teslimat turu, admin kararının ardından item'ın satıcıdan ayrıldığını tespit ederse — **aynı satır** `ESCALATED`'a döndürülür, `SystemCheckResult` yeni bulguyla yazılır, `ResolvedAt` temizlenir ve iki tarafa inceleme bildirimi gider. Önceki karar silinmez: `AdminNote` + `AdminId` yerinde kalır, kararın kendisi değiştirilemez audit kaydındadır (06 §3.20). Yeniden açan **yalnız platformun yeni gözlemidir**; kullanıcı itirazı bu kapıyı açmaz ve eski kararın yeniden tartışılması bir gerekçe değildir. `CLOSED` (sistemin kendi otomatik çözümü, §2.10) bu kuralın **dışındadır** — orada bir insan kararı yoktur ve yeni bir olgu da gelmemiştir |

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
- **Alıcı lehine karar item iadesi içermez.** Platform item'ı hiçbir aşamada tutmadığı için geri alabileceği bir eşya yoktur (§3.2); karar yalnızca parayı hareket ettirir.
- **Kanıtlanmış teslimatta alıcı lehine karar istisnadır ve gerekçesi ayrıca kayda geçirilir (T131).** Teslimat §9.2 uyarınca kanıtlanmış bir işlemde (işlem `ITEM_DELIVERED` durumundaysa teslimat kanıtlanmıştır — bu duruma girişin ön koşulu odur) alıcı lehine karar vermek, zararı **geri alma imkânı olmadan** satıcıya devreder. Bu nedenle o noktada **varsayılan karar satıcı lehinedir**; alıcı lehine karar verilecekse admin, kararın gerekçesini admin notundan **ayrı** bir alanda yazmak zorundadır ve bu gerekçe kalıcı olarak saklanır (06 §3.11 `ResolutionOverrideReason`, 07 §9.30 AD29). Gerekçe kapısı **duruma** bağlıdır, hangi kanıt bayrağının okunabildiğine değil: aksi hâlde admin'in gerekçe yazma yükümlülüğü vakanın gücüne değil Steam envanterinin o an okunabilir olmasına bağlanırdı.
- **Admin kararı, teslimat imzası nedeniyle bekletilen işlemi serbest bırakır (T131).** §9.2, yanlış-teslimat imzası taşıyan bir işlemin **sessizce** iptal edilmesini yasaklar. Bir admin o işleme bakıp karar verdikten sonra iptal artık sessiz değildir: dispute satıcı lehine sonuçlandıysa işlem, süresi dolmuş teslimat penceresinin olağan seyrini izler (03 §4.4 — iptal + alıcıya iade). Satıcı lehine karar bu durumda **ödeme anlamına gelmez**; satıcı payout'u yalnızca `ITEM_DELIVERED`'da işler (07 §9.30). Sistemin kendi otomatik çözümüyle kapanmış (`CLOSED`) bir itiraz bu kapıyı **açmaz** — orada kimse bakmamıştır.
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

> **F8 (T141; uygulayan T145/T146 — ikisi de uygulandı):** cüzdan adresleri **ağ başınadır**. Kullanıcı her ağ (TRON, Ethereum) için ayrı ödeme ve ayrı iade adresi kaydeder; bu bölümdeki her kural (zorunluluk, ek doğrulama, cooldown, snapshot) her ağın adresine ayrı ayrı uygulanır (06 §3.1a, 07 §5.3). Ağ seçimi işlem akışına T147'de geldi: ilan oluşturma kabul edilen her ağın adresini ve cooldown'unu ayrı denetler, kabul seçilen ağın iade adresini ve cooldown'unu kullanır.

| Gereksinim | Detay |
|---|---|
| Varsayılan adres | Satıcı profilinde **her ağ için ayrı** tanımlar |
| Ağ seçimiyle ilişkisi | Satıcı ilanda yalnız ödeme adresi kayıtlı olan ağları kabul edebilir (§4.2). Kabul ettiği her ağın adresi işlem oluşturulurken sabitlenir; alıcı ağ seçince o ağın adresi payout hedefi olur |
| İşlem bazlı adres | Satıcı işlem başlatırken farklı adres girebilir, girmezse profildeki kullanılır |
| Adres zorunluluğu | Cüzdan adresi olmadan işlem başlatılamaz (profil veya işlem bazlı en az biri tanımlı olmalı) |
| Adres değişikliği | Ek doğrulama istenir (Steam üzerinden tekrar onay) |
| Aktif işlem varken değişiklik | Profildeki adres değiştirilse bile aktif işlemler eski adresle tamamlanır |
| Yanlış adres koruması | Adres girişinde kullanıcıya onay adımı gösterilir |

### 12.2 Alıcı İade Adresi

| Gereksinim | Detay |
|---|---|
| Varsayılan adres | Alıcı profilinde **her ağ için ayrı** tanımlar |
| İşlem bazlı adres | Alıcı işlemi kabul ederken seçtiği ağ için farklı adres girebilir, girmezse profildeki o ağın adresi kullanılır |
| Kapsamı | İade adresi kabul edilmiş ödemenin iadesinin **ve** reddedilen ödeme iadelerinin (eksik/fazla tutar, yanlış token, gecikmeli ödeme, fazladan transfer) tek hedefidir (§4.4 notu, K9) |
| Adres zorunluluğu | İade adresi olmadan işlem kabul edilemez (profil veya işlem bazlı en az biri tanımlı olmalı) |
| Adres değişikliği | Ek doğrulama istenir (Steam üzerinden tekrar onay) |
| Aktif işlem varken değişiklik | Profildeki adres değiştirilse bile aktif işlemler eski adresle tamamlanır |
| Yanlış adres koruması | Adres girişinde kullanıcıya onay adımı gösterilir |
| Exchange uyarısı | **F8 hedefinde kalkar** (K9 — iadeler artık gönderen adrese değil iade adresine gider, borsadan ödemek iadeyi riske atmaz; uygulayan T156). Bugünkü ekranda uyarı hâlâ "Exchange'den gönderim yapmayın, iade adresinize ulaşamayabilir" der. Yerine ödeme ekranında **ağ uyarısı** gösterilir: "Yalnız <ağ> ağında USDT gönderin" — aynı `0x` adresi birçok EVM ağında geçerli olduğu için yanlış ağ seçimi en olası hatadır. **Uygulandı (T156):** borsa uyarısı kalktı; ağ uyarısı ve Ethereum'da "başka bir EVM ağından gönderilen ödeme bu işleme sayılmaz, otomatik iade edilmez — destek talebi gerekir" notu gösterilir (08 §3.0) |

### 12.3 Ortak Kurallar

| Gereksinim | Detay |
|---|---|
| Adres formatı | Adresin ait olduğu ağın biçimi: **TRON** — base58check, `T` ile başlar, 34 karakter; sürüm baytı ve sağlama doğrulanır (T146 — öncesinde yalnız alfabe, uzunluk ve önek bakılıyordu). **Ethereum** (T146) — `0x` + 40 hex; karışık harfli girilirse EIP-55 sağlaması zorunlu, tümü küçük (ya da tümü büyük) harfli girilirse kullanıcıdan ikinci onay istenir — tek harfli girdide sağlama kanıtı yoktur (öneriyle kaydedildi — ⚠). EVM adresleri küçük harfle saklanır ve karşılaştırılır, kullanıcıya EIP-55 biçiminde gösterilir (06 §1.4) |
| Merkezi doğrulama pipeline | Cüzdan adresi hangi noktadan girilirse girilsin (profil, işlem başlatma, işlem kabul, adres değiştirme) aynı doğrulama pipeline'ından geçer: (1) o ağın format geçerliliği, (2) sanctions screening (§21.1; F8 hedefinde yaptırım listesi ağ başına). Geçersiz veya yaptırımlı adres hiçbir noktada kaydedilmez |
| Adres değişikliği doğrulaması | Tüm adres değişiklikleri Steam üzerinden ek onay gerektirir. Kullanıcının başka bir ağda aynı türde (ödeme/iade) adresi varken yeni bir ağa **ilk** adresini eklemesi de ek onay ister — ele geçirilmiş bir oturum ikinci bir ödeme hedefi ekleyemesin (T147, 11 §F8 T146 kararı xv). Yalnız o türde hiç adresi olmayan kullanıcının ilk kaydı ek onaysızdır |
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
- **Sayılan olaylar** — üçü de alıcının ödediği ama item'a kavuşmadığı durumdur: (1) ödeme alındıktan sonra **teslimat süresinin dolması**, (2) ödeme alındıktan sonra **satıcının iptal etmesi** (03 §2.5 adım 8), (3) teslimattan sonra satıcının item'ı **geri alması** (mutabakat kontrolünün otomatik dalı, §4.5.1). İtibar hesabı (§13) bu üçünü zaten satıcıya yazar; yaptırım aynı tanımı kullanır.
- **Sayılmayanlar:** admin'in bir anlaşmazlık kararıyla serbest bıraktığı teslimat süresi, alıcının Steam hesabı takas edemediği için dolan süre (§9.1a), admin iptali ve admin kararıyla yapılan iade. Bunlar kullanıcı kusuru değil, platform kararı ya da dış kısıttır.
- **Varsayılanlar** (proje sahibi kararı, 2026-09-16): pencere **30 gün**, **2.** olayda flag, **3.** olayda askı. Eşiklerden biri 0 ya da okunamazsa kural **kapanır** — tahmin edilmiş bir değerle kimse askıya alınmaz.
- **Askı süresizdir** ve admin incelemesiyle kalkar. Askı girişi engellemez, para hareketi yollarını kapatır. Askıyla birlikte, incelemede kullanılacak işlem listesini taşıyan bir flag da açılır (zaten bekleyen bir flag yoksa).
- **Yaptırım yalnız yeni bir olayla tetiklenir.** Admin askıyı kaldırdıktan sonra pencere hâlâ eşiğin üstündeyse, satıcının başka bir işleminin tamamlanması askıyı geri getirmez; ancak yeni bir teslim etmeme olayı getirir. Hesap flag'i açık işlemleri dondurmaz — emergency hold yalnız yaptırım listesi eşleşmesi ve hesap ele geçirme içindir (§14.0).
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
| Ağ başına ödeme ayarları (F8; T148 ✓, T149 ✓) | En düşük iade eşiği çarpanı, TRON ağ ücreti tamponu, Ethereum ücret tavanı ve "bekle" eşiği her ağ için ayrı. Sıcak/soğuk adresler ve gönderim tavanları ayar değil deployment yapılandırmasıdır (05 §3.3). **T148:** Ethereum'un ücret tavanı, "bekle" eşiği, ücret yedekleri, `finalized` durma alarmı ve sıcak/yakıt cüzdanı ETH tabanları ayar olarak eklendi (06 §3.17). **T149:** Ethereum'un en düşük iade eşiği çarpanı (TRON'unki eski anahtarıyla kaldı), TRON ağ ücreti satırının tamponu ve yedeği. Kilit (hibrit enerji) anahtarı admin ayarı değil, blockchain servisinin ortam değişkenidir (`ENERGY_STAKING_ENABLED`, varsayılan kapalı) |
| Fraud sapma eşiği | Piyasa fiyatından sapma yüzdesi |
| Yüksek hacim eşikleri | Tutar eşiği, işlem sayısı eşiği ve kontrol periyodu (saat) |
| Dormant hesap anomali eşikleri | Minimum hesap yaşı (gün) ve tek işlem tutar eşiği — birlikte değerlendirilir (§14.3, §14.4) |
| Alıcı belirleme yöntemi | Yöntem 2'yi aktif/pasif yapabilir |
| Timeout uyarı eşiği | Süre dolmadan ne zaman uyarı gönderileceği (oran olarak) |
| Mutabakat süresi | Teslimat doğrulandıktan sonra satıcı ödemesinin kaç gün bekletileceği (varsayılan 8 gün — §4.5.1). Steam'in geri alma penceresinden kısa ayarlanmamalıdır |
| Teslimat ihlali eşikleri | Sayım penceresi (gün) ile kaçıncı teslim etmeme olayında fraud flag'i, kaçıncısında otomatik askıya alma uygulanacağı — varsayılan 30 gün · 2. · 3. (§14.2) |
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
| Satıcı | Alıcı işlemi kabul etti, ödeme emanete alındı — item'ı alıcıya gönder, işlem tamamlandı, ödeme gönderildi |
| Alıcı | Yeni işlem daveti, satıcı hazır — ödeme yapabilirsin, işlem tamamlandı, dispute sonucu |
| Her iki taraf | Timeout yaklaşıyor, işlem iptal oldu |
| Admin | Flag'lenmiş işlem, anormal davranış tespiti |

> **Kaynak:** Bildirim tipi kataloğunun tek doğru kaynağı 06 §2.13'tür (kod enum'u `NotificationType`, **26 tip**); 07 §8.1 onun aynasıdır. Yukarıdaki tablo tetikleyicilerin ürün seviyesindeki özetidir — tip listesi değildir. v3.0'da item teslimatını satıcı yaptığı için `DELIVERY_EXPECTED` **satıcıya** gider ve alıcıya teslimat anında ayrı bir inbox bildirimi gönderilmez (03 §3.5, T133a).

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

*Skinora — Product Requirements v4.13*
