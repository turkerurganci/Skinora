# Skinora — User Flows

**Versiyon: v3.7** | **Bağımlılıklar:** `01_PROJECT_VISION.md`, `02_PRODUCT_REQUIREMENTS.md` | **Son güncelleme:** 2026-08-19 (**T133a** — custodial kalıntı turu: §1.1 aktör tanımından "item'ı emanet eden" ifadesi kaldırıldı; §3.3/6, §5.3a/5 ve §8.7/6'daki item-iade adımları silindi, §5.4/1 "iade edilecek eşya yoktur" kuralına (02 §3.2) çekildi; §4.1/3'ün "item henüz platformda değil" kalıbı §4.3/§4.4 ile aynı dile getirildi; §5.3a/3'teki canlı `ITEM_ESCROWED` → `SELLER_CONFIRMED`; §8.1 admin dashboard'ından Steam hesapları maddesi kaldırıldı (kodda `AdminDashboardResponse` `steamAccounts` taşımaz); §8.7/2 ve §8.8/8'deki emekli `TRADE_OFFER_SENT_TO_BUYER` aralık ucu state machine'in izin verdiği kümeye (`CREATED → PAYMENT_RECEIVED` + FLAGGED) çekildi; §12.1'in "item'ını gönder" satırı `BUYER_ACCEPTED` şablonuna, §12.2'nin "teslim süreniz" uyarısı ödeme süresine hizalandı. Hijyen: §2.1 ve §2.4'teki yinelenen adım numaraları ve onlara bağlı iki çapraz atıf düzeltildi, satır 5'teki T118 borç notu kapanışa çevrildi, altbilgi başlıkla hizalandı. Davranış değişikliği yok — doküman koda hizalandı.) · 2026-08-18 (**T131 düzeltme turu** — §6.4'teki "satıcı lehine karar imzayı serbest bırakır" kuralına iki alt madde eklendi. (1) **B1:** "kayda kusur yazılmaz" bir **kayıt kuralıdır**, yalnız bir cümle değil — serbest bırakma `PAYMENT_RECEIVED → CANCELLED_TIMEOUT` üretir ve 06 §3.1 haritası o geçişi satıcıya yazardı; satır artık `Transactions.TimeoutReleasedByAdminRulingAt` ile damgalanır ve **itibar + cooldown'ın ikisinde de** sayım dışı kalır (sınıf gerekçesi `CANCELLED_ADMIN` ile aynı, 02 §13). Kanıtla ispatlanmış olağan teslimat timeout'u bu damgayı almaz. (2) **N2:** kapıyı yalnız imzayı **görmüş** bir karar açar — karar imzadan önce verilmişse o admin başka bir vakayı okumuştur; serbest bırakma uygulanmaz, dispute yeni bulguyla `ESCALATED`'a döner (02 §10.2) ve timeout beklemeye devam eder. Karşılaştırma `Disputes.ResolvedAt` ile imzanın **ilk** kayda geçtiği an arasındadır; eşit an ve boş `ResolvedAt` "görmemiş" sayılır.) · 2026-08-17 (**T131** — §6.4'e iki kural eklendi: **gerekçe kapısı** (`ITEM_DELIVERED`'da alıcı lehine karar, admin notundan ayrı bir `overrideReason` olmadan reddedilir; daha erken durumlarda alıcı lehine karar olağan sonuçtur ve gerekçe istenmez — olağan sonuç için gerekçe istemek admin'i alanı geçiştirmeye alıştırır) ve **satıcı lehine kararın bekletilen teslimat imzasını serbest bırakması** (dispute "satıcı kusurlu mu" sorusunu kapatır, para 02 §9.2 kanıt kuralına göre akar; satıcı payout'u yalnız `ITEM_DELIVERED`'da işlediği için "işlem onaylanır" bu durumda "satıcıya öde" anlamına gelmez). "(02 §10)" atfı **§10.4**'e sabitlendi.) · 2026-08-17 (**T130** — §6.2'ye **Sonuç E** eklendi (kanıt var + kapı kapalı → dispute OPEN kalır, alıcı admin'e yükseltebilir, otomatik eskalasyon yapılmaz; mesaj `DELIVERY_EVIDENCE_UNDER_REVIEW`). §6.3'e iki kural: (a) yanlış item karşılaştırmasının **referans noktası** `BuyerBaselineClassIds`'dir — sınıf-kapsamlı sayı farklı bir sınıfın gelişini göremez, baseline yoksa karşılaştırma yapılmaz; (b) **ad yalnız tek bir yeni sınıf geldiyse** yazılır (`Disputes.DeliveredItemName`), birden fazlaysa eskalasyon yine yapılır ama ad boş bırakılır — yanlış item'ı yazmak hiç yazmamaktan kötüdür.) · 2026-08-17 (T129 düzeltme turu — §2.4 adım 2'ye beşinci dal (karar girdisi hiç üretilememiş → eşik beklemeden admin, 07 §9.22b ile satıcı lehine kapatılır) ve "bir kez ayrılma gözlenmişse karar geri alınmaz" kuralı eklendi; geri alma dalı artık ayrılmanın gözlenmiş olmasını da şart koşuyor ve itibar güncellemesini de sayıyor.) · 2026-08-16 (T129 — §2.4 adım 2 mutabakat son kontrolü dört dala ayrıldı: item duruyor / geri alınmış (iki taraflı kanıt) / ayırt edilemeyen ayrılma → admin / okunamıyor → eşikten sonra admin.) · 2026-08-10

> **v3.1 (T118):** §3.4 adım 1, **§3.5 adım 3** ve §12 bildirim kataloğu koda hizalandı — ödeme penceresi item emanetiyle değil satıcı hazırlık onayıyla açılıyor (`PAYMENT_WINDOW_OPEN`), satıcıya `DELIVERY_EXPECTED` satırı eklendi, emekli `ITEM_RETURNED` / trade-offer / Steam-bot satırları kaldırıldı, eksik `ADMIN_PLATFORM_OUTAGE` eklendi. §3.5 adım 3 alıcıya var olmayan bir inbox bildirimi vaat ediyordu — adım 9'un kalıbına çekildi (gerçek-zamanlı güncelleme; bu geçişin iki bildirimi de satıcıya tanımlı). **Kapsam dışı kalan custodial kalıntılar** T118 raporunda listelendi (§1.1 aktör tanımı, §3.3/6, §5.3a/3+5, §5.4/1, §8.7 iade kuralları) ve **T133a turunda kapatıldı.**

---

## 1. Genel Bakış

Bu doküman, Skinora platformundaki tüm kullanıcı akışlarını adım adım tanımlar. Her aktörün (satıcı, alıcı, admin) normal akışları, hata senaryoları ve alternatif yolları içerir.

### 1.1 Aktörler

| Aktör | Tanım |
|---|---|
| Satıcı | İşlemi başlatan, item'ı doğrudan alıcıya gönderen, ödemeyi alan taraf |
| Alıcı | İşlemi kabul eden, ödemeyi gönderen, item'ı teslim alan taraf |
| Admin | Platformu yöneten, flag'lenmiş işlemleri inceleyen taraf |
| Platform (Sistem) | Otomatik doğrulama, transfer ve bildirim işlemlerini gerçekleştiren sistem |

### 1.2 İşlem Durumları

| Durum | Açıklama |
|---|---|
| CREATED | İşlem oluşturuldu, alıcı bekleniyor |
| ACCEPTED | Alıcı kabul etti, satıcının hazırlık onayı bekleniyor |
| SELLER_CONFIRMED | Satıcı item'ı göndermeye hazır olduğunu onayladı, alıcıdan ödeme bekleniyor |
| PAYMENT_RECEIVED | Ödeme doğrulandı ve emanete alındı, satıcının item'ı alıcıya göndermesi bekleniyor |
| ITEM_DELIVERED | Teslimat doğrulandı, satıcıya ödeme işleniyor. Payout durumu (pending/retry/failed) BlockchainTransaction seviyesinde takip edilir — ayrı state değil (06 §3.8). İşlem payout başarılı olana kadar bu state'te kalır |
| COMPLETED | İşlem tamamlandı |
| CANCELLED_TIMEOUT | Timeout nedeniyle iptal |
| CANCELLED_SELLER | Satıcı tarafından iptal |
| CANCELLED_BUYER | Alıcı tarafından iptal |
| CANCELLED_ADMIN | Admin tarafından iptal (flag reddi) |
| REFUNDED | Dispute admin tarafından alıcı lehine sonuçlandırıldı, ödeme iade edildi (terminal) |
| FLAGGED | Fraud tespiti nedeniyle durduruldu, admin onayı bekleniyor |

> **v3.0 (P2P geçişi) durum değişiklikleri:** `TRADE_OFFER_SENT_TO_SELLER` yerini `SELLER_CONFIRMED`'a bıraktı; `ITEM_ESCROWED` ve `TRADE_OFFER_SENT_TO_BUYER` kaldırıldı. Platform artık trade offer göndermediği ve item emanete alınmadığı için bu durumların karşılığı yoktur (02 §2.1). `REFUNDED` daha önce bu tabloda eksikti, eklendi.

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
9. Kullanıcı dashboard'a yönlendirilir (davet linkinden geldiyse → işlem detay sayfasına)

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

### 2.3 Satıcı Hazırlık Onayı (Adım 3)

Bu adımın amacı, alıcı parasını göndermeden önce satışın hâlâ gerçekleşebilir olduğunu teyit etmektir. Ödeme adresi alıcıya ancak bu adım geçildikten sonra açılır — böylece alıcı bayat bir ilana ödeme yapmaz.

1. Alıcı işlemi kabul ettikten sonra satıcıya "Alıcı hazır, item'ı göndermeye hazır mısın?" bildirimi gider
2. Satıcı işlem detay sayfasında **"Göndermeye hazırım"** butonuna basar
3. Platform üç kontrolü birden yapar:
   - **Item hâlâ satıcının envanterinde mi ve tradeable mı?** (envanter taze okunur, önbellek kullanılmaz)
   - **Alıcının Steam Mobile Authenticator'ı aktif mi?** (02 §9.1 — değilse trade 15 gün Steam escrow'una düşer)
   - **Alıcının envanteri okunabilir mi?** Okunabiliyorsa teslimat doğrulaması için referans anlık görüntü (baseline) alınır; gizliyse alıcı uyarılır ve teslimat yalnızca alıcı onayı ile doğrulanabilir (02 §9.2)
4. **Tüm kontroller geçerse →** İşlem SELLER_CONFIRMED durumuna geçer. Alıcıya "Satıcı hazır, ödeme yapabilirsin" bildirimi ve ödeme adresi gösterilir
5. **Item envanterde yoksa veya tradeable değilse →** Satıcıya "Item artık gönderilebilir durumda değil" hatası gösterilir, işlem ilerlemez. Satıcı işlemi iptal edebilir veya item tekrar tradeable olduğunda yeniden deneyebilir
6. **Alıcının MA'sı aktif değilse →** Her iki tarafa bilgilendirme gider; alıcı MA'yı aktif edip tekrar denenebilir
7. **Satıcı hazırlık onayı yerine iptali seçerse →** İşlem CANCELLED_SELLER durumuna geçer. İade gerekmez (para henüz gönderilmedi). Alıcıya "Satıcı işlemi iptal etti" bildirimi gider. İptal kaydı satıcının profiline eklenir
8. **Satıcı süresi içinde onay vermezse →** §4.2 timeout akışı işler

> **Not:** Bu adımda hiçbir Steam trade offer'ı oluşturulmaz. Platform trade'in tarafı değildir; item satıcıda kalmaya devam eder ve yalnızca adım 6'da doğrudan alıcıya gönderilir (02 §2.2).

> **Not:** Alıcı baseline'ı bu adımda alınır, ödeme onayında değil. Satıcı teknik olarak ödemeden önce de item'ı gönderebilir; baseline daha geç alınsaydı erken gelen item referansa dahil olur ve teslimat hiçbir zaman doğrulanamazdı (02 §9.2).

### 2.4 Satıcıya Ödeme (Adım 8)

1. Teslimat doğrulandıktan sonra (§3.5) işlem **mutabakat süresine** girer — varsayılan **8 gün** (02 §4.5.1)
   - Bu süre Steam'in 7 günlük trade geri alma penceresini kapsar. Süre boyunca para platformda tutulur, satıcıya hiçbir ödeme yapılmaz
   - Satıcıya ve alıcıya ödemenin hangi tarihte yapılacağı gösterilir
   - Süre içinde dispute açılırsa ödeme durur ve dispute sonucunu bekler
2. **Süre dolduğunda, ödeme yapılmadan hemen önce son kontrol:** item hâlâ alıcının envanterinde mi?
   - **Evet →** trade kesinleşmiştir, ödeme akışı devam eder (adım 3)
   - **Hayır, ve item teslimatta satıcıdan ayrıldığı görülmüşken satıcının envanterinde yeniden belirdiyse →** trade geri alınmıştır. **Satıcıya ödeme yapılmaz.** Para alıcıya iade edilir, işlem REFUNDED durumuna geçer, satıcı hesabına dolandırıcılık işareti konur (02 §4.5.1, §14.2) ve satıcının itibar paydası güncellenir (06 §3.1). Her iki tarafa bildirim gider. *Launch'ta bu dal `settlement.reversal_auto_refund_enabled` kapalı olduğu için otomatik işlemez; imza admin'e eskale edilir (DEPLOY_RUNBOOK §I)*
   - **Hayır, ama satıcıya döndüğü görünmüyorsa — ya da satıcıda duruyor olsa bile oradan ayrıldığı hiç gözlenmediyse →** karar verilmez, admin'e düşer. İki simetrik yanlış-pozitif de burada durdurulur: alıcının item'ı başkasına devretmesi geri almayla tek taraflı okumada aynı görünür (Steam'in 7 günlük kısıtı 8 günlük pencerenin bir gün öncesinde biter), ve satıcının aynı sınıftan başka bir kopya göndermiş olması orijinal asset'i yerinde bırakır (02 §4.5.1 iki taraflı kontrol + ayrılma notları)
   - **Envanter okunamıyorsa →** karar verilmez, kontrol tekrarlanır. `settlement.unreadable_escalation_hours` (varsayılan 48 saat) boyunca sonuca varılamazsa admin'e düşer. Ödeme her hâlükârda parkta kalır
   - **Kontrolün karar girdisi hiç üretilememişse →** (alıcı envanteri `SELLER_CONFIRMED` anında gizliydi ve teslimat alıcı onayıyla kapandı; ne asset kimliği ne baseline var, ikisi de sonradan doldurulamaz) karar verilmez ve **eşik beklenmez** — ilk turda admin'e düşer. Admin mutabakatı satıcı lehine kapatabilir (07 §9.22b); prosedür DEPLOY_RUNBOOK §I.5
   - **Bir kez ayrılma gözlenmişse karar geri alınmaz:** sonraki turun "item duruyor" okuması ödemeyi serbest bırakmaz, vaka admin'de kalır (02 §4.5.1 launch kapısı notu)
3. Platform komisyonu hesaplar ve keser
4. Gas fee komisyonun %10'unu (veya admin'in belirlediği eşiği) aşıyor mu kontrol edilir:
   - **Aşmıyorsa →** Gas fee komisyondan karşılanır
   - **Aşıyorsa →** Gas fee satıcının payından kesilir
5. Platform kalan tutarı satıcının cüzdan adresine gönderir
   - **Ödeme gönderimi başarısız olursa →** Sistem otomatik yeniden dener. Tekrarlayan başarısızlıkta admin'e bildirim gider. İşlem COMPLETED'a geçmez, ödeme başarılı olana kadar bekler.
6. Satıcıya "Ödemeniz gönderildi" bildirimi gider
7. İşlem COMPLETED durumuna geçer

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

> **Not:** Bu senaryo §2.4 adım 5'te tanımlı retry mekanizması kapsamındadır. Payout başarısız olduğunda işlem COMPLETED'a geçmez, ITEM_DELIVERED'da kalır. Retry otomatik çalışır (exponential backoff, 3 deneme — 06 §3.8). 3 deneme sonrası admin'e eskale edilir. Satıcının ayrıca bildirim yapmasına gerek yoktur — sistem otomatik yönetir.

### 2.5 Satıcı İptal Akışı

1. Satıcı aktif bir işlemin detay sayfasına gider
2. "İşlemi İptal Et" butonuna tıklar
3. **Sistem kontrolü:** Alıcı ödemeyi göndermiş mi?
   - **Evet →** Satıcı yine de iptal edebilir — bu, item'ı göndermekten vazgeçmek anlamına gelir. Ekranda "Ödeme alındı. İptal ederseniz para alıcıya iade edilir ve itibar puanınız etkilenir" uyarısı gösterilir (02 §7)
   - **Hayır →** Devam eder
4. Satıcıdan iptal sebebi istenir (zorunlu)
5. Satıcı sebebi yazar ve iptal onaylar
6. Ödeme alınmışsa → alıcıya iade edilir (iade tutarı = fiyat + komisyon − gas fee). Item hiçbir zaman platformda bulunmadığı için item iadesi diye bir adım yoktur
7. İşlem CANCELLED_SELLER durumuna geçer
8. İptal kaydı satıcının profiline eklenir (itibar skoru etkilenir). Ödeme alındıktan sonraki iptaller teslim etmeme olarak değerlendirilir ve tekrarı yaptırıma tabidir (02 §14.2)
9. Alıcıya "İşlem satıcı tarafından iptal edildi, ödemeniz iade ediliyor" bildirimi gider

> **Neden bu yol açık:** Ödeme sonrası satıcı iptali kapatılsaydı, item'ı göndermek istemeyen satıcı hiçbir şey yapmayıp timeout'u beklerdi — alıcı parasına daha geç kavuşurdu. Açık bırakmak, kaçınılmaz sonucu hızlandırır.

---

## 3. Alıcı Akışları

### 3.1 İlk Giriş ve Kayıt

Kayıt ve giriş süreci satıcı akışı ile aynıdır (bkz. §2.1) — Steam ile giriş, ilk kullanıcı için hesap oluşturma ve Kullanıcı Sözleşmesi kabulü adımları aynen geçerlidir. Mobile Authenticator kontrolü login'de değil, trade URL kaydı sırasında yapılır (08 §2.2). Tek fark: alıcı genellikle davet linki üzerinden platforma gelir ve kayıt/giriş sonrası işlem detay sayfasına yönlendirilir.

**Davet linki üzerinden gelen alıcı akışı:**

1. Alıcı davet linkine tıklar (`/invite/:token` — opaque, tek kullanımlık, 06 §3.5 InviteToken)
2. Platforma yönlendirilir
3. Kayıtlı değilse → "Steam ile Giriş" ekranı gösterilir
4. Steam ile giriş yapar (bkz. §2.1 adım 1-7). MA kontrolü ayrıca trade URL kaydında yapılır (§2.1 adım 8).
5. İlk kez geliyorsa hesabı otomatik oluşturulur, Kullanıcı Sözleşmesi gösterilir (bkz. §2.1 adım 6-7)
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
5. Alıcı **Steam trade URL'ini** verir:
   - Profilinde kayıtlı trade URL'i varsa otomatik gösterilir
   - Satıcının item'ı doğrudan gönderebilmesi için zorunludur (02 §2.2 adım 6)
   - Bu URL satıcıya gösterilecektir — taraflar birbirinin Steam profilini görür, bu P2P modelinin kaçınılmaz sonucudur
6. Alıcı "Kabul Ediyorum" butonuna tıklar
7. **Sistem alıcının Steam Mobile Authenticator'ını doğrular** (02 §9.1) — bu kontrol tıklamadan **sonra**, sunucuda yapılır:
   - Kontrol adım 5'te girilen trade URL'den parse edilen `trade_offer_access_token` ile canlı yapılır (08 §2.2), dolayısıyla URL verilmeden önce sonucu bilinemez — kabul butonu MA gerekçesiyle önden devre dışı bırakılamaz
   - Aktif değilse → kabul reddedilir, "İşlem kabul edebilmek için Steam Mobile Authenticator'ınız aktif olmalı" uyarısı gösterilir ve kullanıcı kurulum rehberine yönlendirilir
   - Steam'e ulaşılamazsa → kabul yine reddedilir (fail-closed, 08 §2.2) ama mesaj farklıdır: "Steam'e şu anda ulaşılamadı, birazdan tekrar deneyin". Alıcının MA'sı sağlam olabilir; düzeltemeyeceği bir işe yönlendirilmez
   - Gerekçe: MA aktif değilse satıcının göndereceği trade 15 gün Steam escrow'una düşer
8. İşlem ACCEPTED durumuna geçer
9. Satıcıya "Alıcı işlemi kabul etti, göndermeye hazır mısın?" bildirimi gider
10. Alıcı satıcının hazırlık onayını bekler — **ödeme adresi bu aşamada henüz gösterilmez** (§2.3)

### 3.3 Alıcı İptal Akışı

1. Alıcı aktif bir işlemin detay sayfasına gider
2. "İşlemi İptal Et" butonuna tıklar
3. **Sistem kontrolü:** Alıcı ödemeyi göndermiş mi?
   - **Evet →** "Ödeme gönderildiği için iptal edilemez" uyarısı gösterilir. İptal butonu devre dışı.
   - **Hayır →** Devam eder
4. Alıcıdan iptal sebebi istenir (zorunlu)
5. Alıcı sebebi yazar ve iptal onaylar
6. İşlem CANCELLED_BUYER durumuna geçer
7. Satıcıya "İşlem alıcı tarafından iptal edildi" bildirimi gider

### 3.4 Ödeme Gönderme (Adım 4)

1. Satıcı hazırlık onayını verdikten sonra (§2.3, işlem `SELLER_CONFIRMED`) alıcıya "Satıcı hazır, ödeme yapabilirsin" bildirimi gider (`PAYMENT_WINDOW_OPEN`, 06 §2.13). Item satıcının envanterinde kalmaya devam eder — platform hiçbir zaman emanete almaz (02 §2.1)
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

### 3.5 Item Teslimi ve Doğrulanması (Adım 6–7)

Bu adımda trade **doğrudan satıcı ile alıcı arasında** geçer. Platform trade'in tarafı değildir, offer oluşturmaz ve Steam'den "trade kabul edildi" bildirimi alamaz — teslimatı kendi gözlemiyle doğrulamak zorundadır (02 §9.2).

1. Ödeme doğrulandıktan sonra işlem PAYMENT_RECEIVED durumuna geçer
2. **Satıcıya** "Ödeme alındı, item'ı şimdi gönder" bildirimi gider. İşlem detay sayfasında alıcının trade URL'ine giden hazır bağlantı gösterilir
3. Alıcıya ödemenin emanete alındığı **gerçek-zamanlı durum güncellemesi** (PAYMENT_RECEIVED) ile gösterilir — bu geçişte alıcıya giden ayrı bir inbox/email bildirim tipi yoktur. 06 §2.13 kataloğunda bu geçişin iki bildirimi de (`PAYMENT_RECEIVED`, `DELIVERY_EXPECTED`) **satıcıya** tanımlıdır; adım 9 ile aynı kalıp
4. Satıcı Steam üzerinden alıcıya trade offer gönderir
5. Alıcı Steam üzerinde offer'ı kabul eder, item alıcının envanterine geçer
6. **Teslimat doğrulaması — iki bağımsız yoldan biri yeterlidir:**
   - **Alıcı onayı:** Alıcı işlem sayfasındaki "Teslim aldım" butonuna basar → işlem anında ITEM_DELIVERED durumuna geçer. Onay alıcının kendi aleyhine olduğu için (onaylayınca parası satıcıya gider) tek başına yeterlidir
   - **Envanter kanıtı:** Item satıcının envanterinden düşmüş **ve** alıcının envanterinde beklenen item sayısı referans anlık görüntüye göre artmışsa → işlem ITEM_DELIVERED durumuna geçer. Bu kontrol alıcı onay verdiğinde, dispute açıldığında ve teslimat süresi dolmadan hemen önce çalışır
7. **Satıcı yanlış bir item gönderirse veya item'ı üçüncü bir kişiye gönderirse** (item satıcıdan düşmüş ama alıcıya ulaşmamış) → işlem sessizce iptal edilmez, otomatik olarak dispute'a yükseltilir ve admin incelemesine düşer (§6.2, §6.3)
8. **Alıcı offer'ı reddederse veya hiç kabul etmezse →** kanıt oluşmaz; süre dolduğunda §4.4 işler
9. Alıcıya item'ın teslim edildiği **gerçek-zamanlı durum güncellemesi** ile gösterilir — ITEM_DELIVERED için ayrı bir inbox/email bildirim tipi yoktur (02 §18.2 / 06 §2.13 bildirim kataloğunda tanımlı değil; WP19). İnbox "İşlem tamamlandı" bildirimi ancak payout başarılı olup COMPLETED'a geçtikten sonra gönderilir
10. Bekleme penceresi dolduktan sonra satıcıya ödeme gönderilir (bkz. §2.4)

> **Not:** Alıcının Steam envanteri gizliyse envanter kanıtı üretilemez. Bu durumda teslimatın tek doğrulama yolu alıcının onayıdır; alıcı hazırlık onayı adımında (§2.3) bu konuda uyarılır.

> **Not:** Counter offer senaryosu artık platformun sorunu değildir — trade'i platform oluşturmadığı için taraflar Steam üzerinde ne yaparsa yapsın, platform yalnızca sonucu (item el değiştirdi mi) gözlemler.

---

## 4. Timeout Akışları

> **Not:** Ödeme aşaması (SELLER_CONFIRMED) per-transaction Hangfire delayed job ile yönetilir. Diğer aşamaların deadline'ları periyodik scanner/poller ile enforce edilir (06 §3.5).

### 4.1 Alıcı Kabul Timeout'u (Adım 2)

**Tetikleyici:** Alıcı belirlenen süre içinde işlemi kabul etmedi.

1. Timeout süresi dolar
2. İşlem CANCELLED_TIMEOUT durumuna geçer
3. Ne ödeme alınmıştır ne de item satıcının envanterinden çıkmıştır → iade gerekmez
4. Satıcıya "Alıcı zamanında kabul etmedi, işlem iptal oldu" bildirimi gider
5. Alıcıya (kayıtlıysa) "İşlem zaman aşımı nedeniyle iptal oldu" bildirimi gider

### 4.2 Satıcı Hazırlık Onayı Timeout'u (Adım 3)

**Tetikleyici:** Satıcı belirlenen süre içinde hazırlık onayı vermedi.

1. Timeout süresi dolar
2. İşlem CANCELLED_TIMEOUT durumuna geçer
3. Para henüz gönderilmedi, item satıcıda → iade gerekmez
4. Satıcıya "Zamanında onay vermediniz, işlem iptal oldu" bildirimi gider
5. Alıcıya "Satıcı işleme devam etmedi, işlem iptal oldu" bildirimi gider
6. Timeout satıcının sorumluluğuna yazılır (02 §3.1, §13)

### 4.3 Ödeme Timeout'u (Adım 4)

**Tetikleyici:** Alıcı belirlenen süre içinde ödemeyi göndermedi veya ödeme doğrulanamadı.

1. Timeout süresi dolar
2. İşlem CANCELLED_TIMEOUT durumuna geçer
3. Item hiçbir zaman platformda olmadı, satıcıda kaldı → item iadesi diye bir işlem yoktur
4. **Platform adresi izlemeye devam eder** — gecikmeli ödeme gelirse:
   - Gelen ödeme alıcının iade adresine otomatik iade edilir (iade tutarından gas fee düşülür)
5. Satıcıya "Alıcı ödeme yapmadı, işlem iptal oldu" bildirimi gider
6. Alıcıya "Zamanında ödeme yapılmadı, işlem iptal oldu" bildirimi gider

### 4.4 Satıcı Teslimat Timeout'u (Adım 6–7)

**Tetikleyici:** Ödeme emanete alındıktan sonra belirlenen süre içinde teslimat doğrulanamadı.

> **Sorumluluk değişti:** Bu timeout eski modelde alıcıya aitti ("alıcı teslim offer'ını kabul etmedi"). P2P'de trade'i satıcı gönderdiği için gecikme **satıcıya** yazılır (02 §3.1).

1. Timeout süresi dolmadan hemen önce platform **son bir teslimat doğrulaması** yapar (02 §9.2):
   - **Kanıt bulunursa →** işlem iptal edilmez, ITEM_DELIVERED durumuna geçer. Bu, satıcı item'ı gönderdiği hâlde alıcı onay vermediğinde haksız iadeyi önler
   - **Item satıcıdan düşmüş ama alıcıya ulaşmamışsa →** işlem iptal edilmez, dispute'a yükseltilir (§6.2)
   - **Kanıt yoksa →** aşağıdaki iptal akışı işler
2. İşlem CANCELLED_TIMEOUT durumuna geçer
3. Item hiçbir zaman platformda olmadı → item iadesi yoktur
4. Ödeme emanette → alıcıya iade edilir (iade tutarı = fiyat + komisyon − gas fee)
5. Satıcıya "Item'ı zamanında göndermediniz, işlem iptal oldu ve ödeme alıcıya iade edildi" bildirimi gider
6. Alıcıya "Satıcı item'ı göndermedi, ödemeniz iade edildi" bildirimi gider
7. Timeout satıcının sorumluluğuna yazılır; tekrarlanan ihlaller fraud flag'i ve otomatik askıya alma üretir (02 §14.2)

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
3. **İşlem state'i değişmez** — desteklenmeyen token ödeme olarak kabul edilmediği için işlem mevcut durumunda (`SELLER_CONFIRMED`) kalır
4. **Timeout devam eder** — alıcı süre dolmadan doğru token ile ödeme gönderebilir
5. Desteklenmeyen varlık için ayrı bir admin review süreci başlatılır (otomatik iade garanti edilemez)
6. Alıcıya "Desteklenmeyen varlık tespit edildi. Lütfen doğru token ile ödeme gönderin. Desteklenmeyen varlık için admin incelemesi başlatıldı" bildirimi gider
7. Admin durumu değerlendirir ve mümkünse desteklenmeyen varlığı manuel iade eder (02 §4.4)

### 5.4 Gecikmeli Ödeme (Timeout Sonrası)

1. Ödeme timeout'u dolmuş ve işlem iptal edilmiştir; item baştan beri satıcının envanterinde olduğu için iade edilecek bir eşya yoktur
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
3. Sistem §9.2 (02) kanıt kurallarını **taze** olarak çalıştırır — her iki tarafın envanteri önbelleksiz okunur
4. **Sonuç A — Teslimat kanıtı bulundu** (item satıcıdan düştü ve alıcıya ulaştı):
   - İşlem ITEM_DELIVERED durumuna geçer, dispute anında kapanır
   - Alıcıya "Item envanterinize teslim edilmiş durumda" cevabı gösterilir
5. **Sonuç B — Item hâlâ satıcının envanterinde:**
   - Satıcı henüz göndermemiştir. Alıcıya "Satıcı item'ı henüz göndermedi, süre dolduğunda ödemeniz iade edilecek" cevabı gösterilir
   - Alıcı isterse admin'e yükseltebilir
6. **Sonuç C — Item satıcıdan düşmüş ama alıcıya ulaşmamış:**
   - Yanlış item gönderimi veya üçüncü kişiye gönderim imzasıdır
   - **Otomatik olarak admin'e yükseltilir** (kullanıcı aksiyonu beklenmez), her iki tarafa "İşleminiz incelemeye alındı" bildirimi gider
7. **Sonuç D — Alıcının envanteri gizli veya Steam okunamıyor:**
   - Alıcıya "Envanterinizi herkese açık yapın veya item'ı aldıysanız 'Teslim aldım' butonunu kullanın" cevabı gösterilir
   - Alıcı isterse admin'e yükseltebilir
8. **Sonuç E — Teslimat kanıtı bulundu ama otomatik onay kapısı kapalı** (T130):
   - Envanter kanıtı (`SELLER_ASSET_GONE ∧ INVENTORY_DELTA`) tamam, ancak `delivery.inventory_evidence_auto_release_enabled` kapalı olduğu için platform kendi çıkarımıyla para bırakmaz ([`DEPLOY_RUNBOOK.md` §H](DEPLOY_RUNBOOK.md#h-launch-checklist--teslimat-kanıtı-doğrulama-kapısı-t125))
   - Alıcıya "Teslimat kanıtı bulundu, işleminiz inceleniyor" cevabı gösterilir
   - Dispute **OPEN kalır ve alıcı admin'e yükseltebilir.** Bu şart kapının kendisi kadar önemlidir: kapı kapalıyken dispute "teslim edildi" diye kapatılırsa otomatik yol kapalı, elle yol da kapalı olur ve alıcının parasının hiçbir çıkışı kalmaz
   - Otomatik eskalasyon **yapılmaz** — kapı, launch döneminde toplu olarak incelenen bir kayıt kuyruğudur (§H.3), tekil bir olay değil

### 6.3 Yanlış Item İtirazı

**Senaryo:** Alıcı "yanlış item geldi" diyor.

1. Alıcı işlem detay sayfasından "İtiraz Et" butonuna tıklar
2. İtiraz türünü seçer: "Yanlış item teslim edildi"
3. Sistem, alıcının envanterine referans anlık görüntüden (§2.3) sonra giren item'ları tespit eder ve işlemdeki item ile karşılaştırır
   > **Referans nedir (T130):** §2.3'te alınan anlık görüntü iki parçalıdır — işlemin kendi item sınıfının **sayısı** (teslimat kanıtı için, 02 §9.2) ve alıcı envanterinin **tüm sınıf kimlikleri** (`BuyerBaselineClassIds`, 06 §3.5). Yanlış item sınıf-kapsamlı sayıyı hiç yükseltmediği için karşılaştırma ikinciye dayanır: dispute anında envanter taze okunur, baseline'da olmayan sınıflar "sonradan gelen" olur. Baseline alınamamışsa (alıcı envanteri o an gizliydi) karşılaştırmanın dayanağı yoktur ve sistem bunu söyler — boş bir kümeyle karşılaştırmak alıcının sahip olduğu her item'ı "yeni geldi" diye okurdu.
4. **Sonuç A — Beklenen item gelmiş:**
   - Alıcıya "Teslim edilen item, işlemdeki item ile eşleşiyor" cevabı gösterilir
5. **Sonuç B — Farklı bir item gelmiş:**
   - İşlem durdurulur, **gelen item'ın adı kayda geçirilerek** (`Disputes.DeliveredItemName`, 06 §3.11) admin'e otomatik eskalasyon yapılır — admin karşılaştırmayı elle yapmak zorunda kalmaz
   - Her iki tarafa "İşleminiz incelemeye alındı" bildirimi gider
   > **Ad tek bir sınıf geldiyse yazılır (T130).** Baseline ile dispute arasındaki pencerede alıcı kendi hesabından da trade yapmış olabilir; birden fazla yeni sınıf varsa hangisinin satıcıdan geldiği belirsizdir. Eskalasyon her hâlükârda yapılır, ama ad **boş bırakılır** — admin'in kanıt alanına yanlış item'ı yazmak hiç yazmamaktan kötüdür (06 §8.4'ün aynı kuralı).
6. **Sonuç C — Hiçbir yeni item gelmemiş:**
   - Bu bir yanlış item değil, teslim edilmeme vakasıdır → §6.2 akışına yönlendirilir

> **Not:** Otomatik karşılaştırma item sınıfı üzerinden yapılır. Aynı sınıftan iki item arasındaki aşınma/desen farkı ("aynı skin ama daha kötü float") otomatik tespitin kapsamı dışındadır ve admin incelemesine tabidir (02 §9.2).

### 6.4 Admin Eskalasyonu

**Senaryo:** Otomatik çözüm kullanıcıyı tatmin etmedi.

1. Kullanıcı otomatik çözüm sonrası "Admin'e İlet" butonuna tıklar
2. Kullanıcı itiraz detayını yazar
3. İşlem admin kuyruğuna düşer (dispute `ESCALATED`)
4. Kullanıcıya "İtirazınız admin ekibine iletildi" bildirimi gider
5. **Admin çözümü (WP5 — minimal):** Admin eskalasyon kuyruğunu (`GET /admin/disputes`, AD27) görür, itirazı inceler (AD28) ve karar verir (AD29):
   - **Satıcı lehine** → dispute `RESOLVED_FOR_SELLER`; işlem onaylanır, satıcı payout devam eder.
   - **Alıcı lehine** → dispute `RESOLVED_FOR_BUYER`; işlem `REFUNDED`, alıcıya iade. **Item iadesi diye bir adım yoktur** — item hiçbir zaman platformda bulunmadığı için platformun geri alabileceği bir eşya yoktur.
     - Bunun sonucu açıkça kabul edilmelidir: teslimat kanıtlanmış bir işlemde alıcı lehine karar vermek, zararı **geri alma imkânı olmadan** satıcıya devreder. Bu nedenle teslimat kanıtı mevcutken varsayılan karar satıcı lehinedir; alıcı lehine karar istisnadır ve gerekçesi ayrıca kayda geçirilir (02 §10.4)
     - **Gerekçe kapısı (T131):** İşlem `ITEM_DELIVERED` durumundaysa teslimat kanıtlanmıştır (bu duruma girişin ön koşulu 02 §9.2 kanıtıdır) ve alıcı lehine karar, admin notundan **ayrı** bir gerekçe alanı olmadan verilemez — sistem kararı reddeder (07 §9.30 AD29 `overrideReason`, 06 §3.11 `ResolutionOverrideReason`). Daha erken durumlarda alıcı lehine karar olağan sonuçtur ve gerekçe istenmez: olağan sonuç için gerekçe istemek, admin'i alanı geçiştirmeye alıştırır
   - **Satıcı lehine karar, bekletilen bir teslimat imzasını serbest bırakır (T131).** Yanlış-teslimat imzası nedeniyle bekletilen (§6.2 Sonuç C) ve teslimat penceresi dolmuş bir işlemde satıcı lehine karar, işlemi `ITEM_DELIVERED`'a taşımaz — satıcı payout'u yalnızca orada işler. İşlem, süresi dolmuş pencerenin olağan seyrini izler: iptal edilir ve alıcıya iade yapılır (§4.4). Bu bir çelişki değil, iki ayrı sorunun iki ayrı cevabıdır: dispute "satıcı kusurlu mu" sorusunu kapatır (kayda kusur yazılmaz), para ise 02 §9.2 kanıt kuralına göre akar. Karar öncesinde iptal edilemezdi çünkü 02 §9.2 **sessiz** iptali yasaklar; admin baktıktan sonra iptal sessiz değildir
     - **"Kayda kusur yazılmaz" bir kayıt kuralıdır, yalnız bir cümle değil (T131 doğrulaması bulgu B1, 2026-08-18).** Serbest bırakma `PAYMENT_RECEIVED → CANCELLED_TIMEOUT` üretir ve 06 §3.1 sorumluluk haritası o geçişi **satıcıya** yazar — yani düzeltilmeseydi admin'in akladığı satıcının `SuccessfulTransactionRate`'i düşer ve aynı satır cancel cooldown penceresine girerdi (02 §14.2); itibarın admin düzeltme yüzeyi olmadığı için ceza da **kalıcı** olurdu. Bu nedenle serbest bırakılan satır `Transactions.TimeoutReleasedByAdminRulingAt` ile damgalanır ve **itibar ile cooldown'ın ikisinde de sayım dışı** kalır (06 §3.1). Sınıf gerekçesi `CANCELLED_ADMIN` ile aynıdır (02 §13): platform kararı, kullanıcı kusuru değil. Kanıtla ispatlanmış olağan teslimat timeout'u bu damgayı **almaz** — orada kusur satıcınındır ve sayılır
     - **Kapıyı yalnız imzayı GÖRMÜŞ bir karar açar (T131 doğrulaması bulgu N2, 2026-08-18).** Serbest bırakmayı meşru kılan tek şey "bir insan bu vakayı okudu"dur. Karar imzadan **önce** verilmişse (alıcı erken bir dispute açtı, admin karara bağladı, imza ancak sonra oluştu) o admin başka bir vakayı okumuştur; kararına dayanarak iptal etmek, sahibinin hiç görmediği bir kanıtla satıcı lehine kararı sessizce geri çevirir ve item'ı çoktan envanterinden çıkmış satıcıyı hem item'sız hem parasız bırakır. Bu durumda serbest bırakma **uygulanmaz**: dispute yeni bulguyla birlikte `ESCALATED`'a döner (02 §10.2 yeniden eskalasyon kuralı), timeout beklemeye devam eder. Karşılaştırma `Disputes.ResolvedAt` ile imzanın **ilk** kayda geçtiği an arasındadır; eşit an ve `ResolvedAt` boşluğu da "görmemiş" sayılır — gereksiz bir inceleme admin'in zamanına, yanlış bir serbest bırakma satıcının malına mal olur
   - Her iki tarafa `DISPUTE_RESULT` bildirimi gider; `DISPUTE_RESOLVED` audit kaydı yazılır. Emergency hold altındaki işlem önce AD19c ile release edilir.
   - **Kapsam dışı (MVP-sonrası):** SLA, sorumlu-admin atama, yanıt şablonu, çok-adımlı state machine.

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
   - Son 5 flag kaydı (`recentFlags`)

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

**Bu akış kaldırılmıştır (v3.0, P2P geçişi).**

Platform Steam hesabı işletmez (02 §15); izlenecek bot durumu, emanet item sayısı veya günlük trade offer kotası yoktur. Steam tarafında izlenen tek şey salt okunur API çağrılarının sağlığıdır ve bu, platform sağlık göstergeleri içinde yer alır.

> Alt bölüm numarası bilinçli korundu — §8.6 ve sonrası referanslarının kayması engellendi.

### 8.6 Rol ve Yetki Yönetimi (Sadece Süper Admin)

1. Süper admin "Rol Yönetimi" bölümüne gider
2. Yeni rol oluşturabilir
3. Role yetki atayabilir (hangi bölümleri görüp düzenleyebileceği)
4. Kullanıcıları rollere atayabilir

### 8.7 Admin Doğrudan İşlem İptali

**Senaryo:** Admin, flag mekanizması dışında operasyonel bir sebepten (yasal talep, kullanıcı şikayeti, teknik sorun) bir işlemi doğrudan iptal etmek istiyor.

1. Admin işlem detay sayfasına gider (S16)
2. İşlem CREATED, ACCEPTED, SELLER_CONFIRMED veya PAYMENT_RECEIVED durumundaysa (+ FLAGGED) "İşlemi İptal Et" butonu görünür
3. Admin butona tıklar
4. İptal sebebi girmesi istenir (zorunlu)
5. Admin sebebi yazar ve iptal onaylar
6. **İade kuralları (standart iptal kurallarıyla aynı):**
   - Ödeme alınmışsa → alıcıya iade edilir (fiyat + komisyon − gas fee)
   - **Item iadesi diye bir adım yoktur** — item hiçbir zaman platformda bulunmadığı için platformun geri verebileceği tek varlık paradır (02 §3.2)
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
   - **İptal et (CREATED → PAYMENT_RECEIVED arası) →** Standart admin iptal kuralları uygulanır (§8.7)
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

1. Platform Steam servislerinin global olarak çalışmadığını tespit eder (envanter ve trade-hold sorguları sürekli başarısız veya admin manuel tetikleme)
2. Aktif işlemlerin Steam bağımlı adımlarındaki timeout süreleri dondurulur — özellikle teslimat fazı: kesinti sırasında trade taraflar arasında gerçekleşebilir ama platform bunu doğrulayamaz, dolayısıyla satıcı haksız yere teslim etmemiş sayılmamalıdır (02 §23)
3. Kullanıcılara "Steam servisleri geçici olarak kullanılamıyor, işlemleriniz etkilenmeyecek" bildirimi gider
4. Steam normale döner
5. Timeout süreleri kaldığı yerden devam eder
6. Kullanıcılara "Steam servisleri normale döndü" bildirimi gider

### 11.2a Tekil Bot Hesabı Kısıtlanması

**Bu akış kaldırılmıştır (v3.0, P2P geçişi).**

Platform Steam hesabı işletmediği için kısıtlanacak, banlanacak veya item'ı içinde mahsur kalacak bir bot hesabı yoktur (02 §15). Bu akışla birlikte bot havuzu yönlendirmesi, item recovery ve bota bağlı exceptional resolution senaryolarının tamamı ortadan kalkmıştır.

> Alt bölüm numarası bilinçli korundu — §11.3 referanslarının kayması engellendi.

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
| Alıcı işlemi kabul etti | "Alıcı işlemi kabul etti — hazırlık onayı ver" (`BUYER_ACCEPTED`) |
| Ödeme doğrulandı | "Ödeme geldi" |
| İşlem tamamlandı | "İşlem tamamlandı" |
| Ödeme emanete alındı | "Ödeme alındı, item'ı şimdi gönder" (`DELIVERY_EXPECTED`) |
| Satıcıya ödeme gönderildi | "Ödemeniz cüzdan adresinize gönderildi" |
| Timeout yaklaşıyor (satıcı aksiyonu gereken) | "Item gönderme süreniz dolmak üzere" |
| Alıcı işlemi iptal etti | "İşlem alıcı tarafından iptal edildi" |
| İşlem iptal oldu | "İşlem iptal oldu" + sebep |
| İşlem flag'lendi | "İşleminiz incelemeye alındı" |

### 12.2 Alıcı Bildirimleri

| Tetikleyici | Bildirim |
|---|---|
| Yeni işlem daveti | "Sizin için bir işlem oluşturuldu" |
| Satıcı hazırlık onayı verdi | "Satıcı hazır, ödeme yapabilirsin" (`PAYMENT_WINDOW_OPEN`) |
| Eksik/fazla/yanlış ödeme | İlgili uyarı mesajı |
| Item teslim edildi | Gerçek-zamanlı durum güncellemesi (ITEM_DELIVERED) ile gösterilir — ayrı inbox/email bildirimi yoktur; inbox "İşlem tamamlandı" bildirimi COMPLETED'da gönderilir (02 §18.2 / 06 §2.13; WP19) |
| İşlem tamamlandı | "İşlem tamamlandı" (yalnızca COMPLETED state'inde gönderilir) |
| Gecikmeli ödeme iadesi | "Gecikmeli ödemeniz iade edildi" |
| Timeout yaklaşıyor (alıcı aksiyonu gereken) | "Ödeme süreniz dolmak üzere" (`TIMEOUT_WARNING`) |
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
| Platform kesintisi | "Platform kesintisi tespit edildi" (`ADMIN_PLATFORM_OUTAGE`) |

> **Not:** Dış kanal bildirimleri (email, Telegram, Discord) `NotificationDelivery` entity'sinde kalıcı olarak takip edilir — teslimat başarısı/başarısızlığı, retry sayısı ve hata mesajı kaydedilir (06 §3.13a).

---

*Skinora — User Flows v3.7*
