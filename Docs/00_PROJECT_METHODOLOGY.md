# Skinora — Project Methodology Playbook

**Versiyon: v0.4** | **Bağımlılıklar:** Yok (kök doküman) | **Son güncelleme:** 2026-03-28

> Bu doküman, bir yazılım projesini fikir aşamasından kodlamaya kadar taşımak için izlenen yol haritasını, her aşamadaki yaklaşımı ve öğrenimleri tanımlar.
> Herhangi bir projede şablon olarak kullanılabilir.

---

## 1. Genel Yol Haritası

| Sıra | Aşama | Doküman | Açıklama |
|---|---|---|---|
| 0 | Proje Metodolojisi | `00_PROJECT_METHODOLOGY.md` | Bu doküman — sürecin kendisini tanımlar |
| 1 | Ürün Vizyonu | `01_PROJECT_VISION.md` | Ne yapıyoruz, neden yapıyoruz, kimin için yapıyoruz |
| 2 | Ürün Gereksinimleri | `02_PRODUCT_REQUIREMENTS.md` | Tüm iş kuralları ve ürün kararları |
| 3 | Kullanıcı Akışları | `03_USER_FLOWS.md` | Her aktörün adım adım deneyimi |
| 4 | UI Spesifikasyonları | `04_UI_SPECS.md` | Ekran bazında kullanıcı arayüzü tanımları |
| 5 | Teknik Mimari | `05_TECHNICAL_ARCHITECTURE.md` | Sistem mimarisi ve teknoloji kararları |
| 6 | Veri Modeli | `06_DATA_MODEL.md` | Entity'ler, ilişkiler, şema |
| 7 | API Tasarımı | `07_API_DESIGN.md` | Endpoint'ler, request/response yapıları |
| 8 | Entegrasyon Spesifikasyonları | `08_INTEGRATION_SPEC.md` | Üçüncü parti servis entegrasyonları |
| 9 | Kodlama Kılavuzu | `09_CODING_GUIDELINES.md` | Kod standartları, klasör yapısı, hata yönetimi |
| 10 | MVP Kapsamı | `10_MVP_SCOPE.md` | Ne var, ne yok, sınırlar |
| 11 | Implementation Planı | `11_IMPLEMENTATION_PLAN.md` | Sıralı task listesi, bağımlılıklar, kabul kriterleri |
| 12 | Doğrulama Protokolü | `12_VALIDATION_PROTOCOL.md` | Cross-check kuralları, reviewer prosedürleri |

---

## 2. Aşama 1 — Product Discovery

### 2.1 Amaç

Kodlamaya geçmeden önce ürün netliğini kusursuz hale getirmek. Şu soruların cevabını netleştirmek:

- Bu proje tam olarak ne yapıyor?
- Hangi problemi çözüyor?
- Kim için yapılıyor?
- Kullanıcı neden buna ihtiyaç duyar?
- Bu projenin iş değeri nedir?
- İlk sürümde ne var, ne yok?
- Bu ürünün sınırları nelerdir?
- Başarılı sayılması için ne olması gerekir?

### 2.2 Yaklaşım

Product discovery bir **workshop** formatında yürütülür.

**Soru bazlı ilerleme:** Tüm konular tek seferde değil, sırayla tek tek ele alınır. Her konu derinlemesine tartışılıp netleştirildikten sonra bir sonrakine geçilir. Bu sayede her karar bağlamında değerlendirilir ve tutarsızlıklar erken yakalanır.

**Kararları zincirleme bağlama:** Her karar bir sonrakini etkiler. Doğru sırayla ilerlemek gerekir. Örneğin ödeme yöntemi kararı → ödeme akışı detayları → komisyon yapısı → edge case'ler şeklinde doğal bir zincir oluşur.

**Edge case'leri anında ele alma:** Her karardan sonra "burada ne ters gidebilir?" sorusu sorulur. Olası hata senaryoları, timeout durumları, iptal senaryoları hemen tartışılır.

**Teknik detaya girmeme kuralı:** Bu aşamada teknoloji seçimi, mimari, API, veritabanı gibi konulara girilmez. Odak tamamen ürün tanımı, problem tanımı, kapsam ve kullanıcı değeri üzerindedir.

**İnkremental dokümantasyon:** Tartışma ilerledikçe kararlar bir discovery status dosyasına kaydedilir. Bu dosya versiyonlanır ve her önemli karar grubunda güncellenir. Hiçbir karar kaybolmaz.

### 2.3 Soru Sıralaması Rehberi

Konular şu mantıkla sıralanır — her soru bir sonrakinin temelini oluşturur:

| Öncelik | Konu Grubu | Neden Önce |
|---|---|---|
| 1 | Ödeme / gelir modeli | Tüm akışı, yasal yükümlülükleri ve hedef kitleyi belirler |
| 2 | Temel işlem yapısı | Akışın karmaşıklığını ve kapsamını belirler |
| 3 | Zaman kuralları (timeout vb.) | İşlem güvenliğinin temelini oluşturur |
| 4 | İptal ve anlaşmazlık kuralları | Kullanıcı haklarını tanımlar |
| 5 | Komisyon ve fiyatlandırma | Gelir modeli ve kullanıcı deneyimi |
| 6 | Kapsam sınırları | MVP'de ne var ne yok |
| 7 | Hedef pazar ve dil | Erişim ve lokalizasyon |
| 8 | Kullanıcı kimliği ve giriş | Platform güvenliğinin temeli |
| 9 | Güven mekanizmaları | İtibar, skor, değerlendirme |
| 10 | Rekabet ve farklılaşma | Konumlandırma |
| 11 | Başarı kriterleri | Ölçülebilir hedefler |
| 12 | Detaylı akış senaryoları | Edge case'ler, hata durumları |
| 13 | Fraud ve güvenlik önlemleri | Kötüye kullanım senaryoları |
| 14 | Operasyonel konular | Downtime, bakım, sorumluluk |
| 15 | Platform ve erişim | Web, mobil, landing page |
| 16 | Hesap yönetimi ve yasal | GDPR, kullanıcı hakları, sözleşme |

Bu sıralama kesin bir kural değil, projeye göre uyarlanabilir. Önemli olan bağımlılık zincirini takip etmektir.

### 2.4 Karar Alma Akışı

Her konu için şu akış izlenir:

1. **Konu tanıtımı:** Neden bu konuyu konuşmamız gerektiği kısaca açıklanır
2. **Seçenekler sunulur:** Her seçeneğin artı ve eksileri ürün perspektifinden değerlendirilir
3. **Öneri yapılır:** Ürün mantığına en uygun seçenek önerilir, gerekçesiyle birlikte
4. **Karar alınır:** Proje sahibi onaylar veya farklı yön belirler
5. **Edge case kontrolü:** "Bu kararın yaratacağı risk veya boşluk var mı?" sorusu sorulur
6. **Kayıt:** Karar discovery status dosyasına eklenir

### 2.5 Çıktılar

Bu aşamanın sonunda şu dokümanlar üretilmiş olur:

| Dosya | Tür | Açıklama |
|---|---|---|
| `PRODUCT_DISCOVERY_STATUS.md` | Ara doküman | Tüm kararların kayıt dosyası — süreç boyunca güncellenir |
| `01_PROJECT_VISION.md` | Final doküman | Ürün vizyonu, problem, hedef, konumlandırma |
| `02_PRODUCT_REQUIREMENTS.md` | Final doküman | Tüm ürün gereksinimleri |
| `10_MVP_SCOPE.md` | Final doküman | MVP kapsamı, sınırlar, yol haritası |

### 2.6 Prensipler

- **"Sence nasıl olmalı?" sorusuna hazırlıklı ol.** Proje sahibi sıklıkla görüş sorar. Her konuda ürün perspektifinden net bir öneriye sahip olmak süreci hızlandırır.
- **Edge case'leri proje sahibinden önce düşün.** "X durumunda ne olur?" sorularını sen sor. Bu güven oluşturur ve kararların sağlamlığını artırır.
- **Teknik detaya kaymayı engelle.** Bazı konular doğal olarak teknik tarafa kayar. "Bu teknik bir karar, şimdi ürün kararını alalım" diyerek odağı koru.
- **Admin esnekliği her yerde.** Rakamsal parametreleri (süreler, oranlar, limitler) admin tarafından değiştirilebilir yapmak, "doğru rakam ne?" tartışmasını ürün aşamasından çıkarır.
- **Belirsizliği kabul et ama sınırla.** Bazı konuların detayları ileriye bırakılabilir. Ama "olacak mı olmayacak mı" kararı her zaman bu aşamada alınır. Belirsiz bırakılan sadece detaydır, varlık değil.

### 2.7 Öğrenimler

- Sıralı soru yaklaşımı çalışıyor. Tüm konuları aynı anda açmak yerine zincirleme ilerlemek hem tutarlılığı artırır hem proje sahibini bunaltmaz.
- Discovery status dosyası kritik. Kararlar anında kaydedildiği için geriye dönük tutarsızlık oluşmaz. Versiyonlama önemli.
- Edge case'ler erken yakalanmalı. Hata senaryolarını erken tartışmak, ileride akış değişikliğine gerek kalma riskini ortadan kaldırır.
- "MVP'de yok" kararları "var" kararları kadar önemli. Kapsam dışı bırakılan her özellik bilinçli bir karar olarak kaydedilmeli ve gerekçelendirilmeli.
- Süreç tıkanma riski her zaman göz önünde tutulmalı. Karar alınamayan konularda tıkanmak yerine "detayı ileriye bırak, varlık kararını şimdi al" yaklaşımı uygulanmalı.

---

## 3. Aşama 2 — Kullanıcı Akışları

### 3.1 Amaç

Ürün gereksinimlerini somut kullanıcı deneyimlerine dönüştürmek. Her aktörün platformda adım adım ne yapacağını tanımlamak. Bu aşamada gereksinim dokümanındaki olası boşluklar ve çelişkiler de yakalanır.

### 3.2 Yaklaşım

**Aktör bazlı ilerleme:** Her aktör (son kullanıcı, admin vb.) için ayrı akışlar oluşturulur.

**Normal akış + hata akışları:** Her ana akış için önce "her şey yolunda giderse" senaryosu yazılır, sonra "ne ters gidebilir?" sorusu sorularak hata ve alternatif akışlar eklenir.

**Durum makinesi mantığı:** İşlemlerin olası durumları (status) tanımlanır. Her adım bir durumdan diğerine geçiştir. Bu, ileride teknik implementasyonu kolaylaştırır.

**Bildirim entegrasyonu:** Her akış adımında "burada kime bildirim gitmeli?" sorusu sorulur ve bildirim tetikleyicileri akış içine gömülür.

**Operasyonel senaryolar:** Platform bakımı ve üçüncü parti servis kesintileri (Steam, blockchain) gibi operasyonel durumlar için de akışlar tanımlanır. Timeout dondurma, kullanıcı bilgilendirme ve normale dönüş adımları ayrı akışlar olarak ele alınır.

### 3.3 Oluşturulacak Akışlar

| Kategori | Akışlar |
|---|---|
| Ana akışlar | Her aktör tipi için temel kullanım senaryosu (baştan sona) |
| Hata akışları | Timeout senaryoları, iptal senaryoları, ödeme hataları |
| Dispute akışları | İtiraz türleri ve çözüm yolları |
| Fraud akışları | Flag'leme ve admin inceleme süreçleri |
| Destekleyici akışlar | Kayıt, profil yönetimi, hesap silme |
| Operasyonel akışlar | Downtime, bakım senaryoları |

### 3.4 Çıktılar

| Dosya | Açıklama |
|---|---|
| `03_USER_FLOWS.md` | Tüm kullanıcı akışları, hata senaryoları, bildirim özeti |

### 3.5 Prensipler

- Her akış adımında "sistem burada ne kontrol etmeli?" sorusunu sor. Eksik kontroller bu aşamada yakalanır.
- Akışları oluşturduktan sonra ürün gereksinimleri dokümanına geri dön — tutarsızlık varsa düzelt.
- Edge case akışları ana akışlar kadar önemli. Kullanıcı her zaman happy path'te ilerlemez.
- Bildirim listesini akış içinden çıkar, ayrı bir bölümde özetle. Bu ileride bildirim sistemi tasarımını kolaylaştırır.

### 3.6 Öğrenimler

- State machine yaklaşımı UI tasarımından önce gelmelidir. İşlem durumlarının (13 adet) akışlarda netleştirilmesi, sonraki aşamada ekran bazlı varyantların kolayca türetilmesini sağladı.
- Timeout yönetimi ayrı bir bölüm hak ediyor. Timeout kurallarını aktör akışlarına gömmek yerine ayrı bir bölümde toplamak (section 4) hem okunabilirliği artırdı hem tutarsızlıkları önledi.
- Admin akışları kullanıcı akışları kadar karmaşık. Admin iş akışlarını "sonra ekleriz" yaklaşımıyla bırakmak riskli — aynı derinlikte ele alınmalı.
- Edge case senaryoları sadece if/then ile anlatılamaz. Gecikmiş ödeme, yanlış token gibi senaryolar uçtan uca narrative anlatım gerektiriyor.
- Bildirim haritası akışlardan ayrı özetlenmeli. Her state değişiminde kime bildirim gideceğini ayrı bir tabloda toplamak, bildirim sistemi tasarımını kolaylaştırdı.
- Hata akışları happy path kadar önemli. Her iki perspektife de eşit ağırlık vermek "happy path only" sendromunu önledi.

---

## 4. Aşama 3 — UI/UX Tasarım

### 4.1 Amaç

Kullanıcı akışlarını görsel arayüz tanımlarına dönüştürmek. Her ekranın ne gösterdiğini, kullanıcının ne yapabildiğini ve ekranlar arası geçişleri tanımlamak.

### 4.2 Rol

Bu aşamada bir **Senior Product Designer / UX Architect** rolünde çalış. Uzmanlık alanları:

- **Bilgi mimarisi (information architecture):** Her ekranda neyin nerede gösterileceği, bilgi hiyerarşisi, kullanıcının dikkatinin yönlendirilmesi
- **Etkileşim tasarımı (interaction design):** Kullanıcı aksiyonları, form validasyonları, feedback mekanizmaları, loading/error/empty state'ler
- **Component-based düşünme:** Tekrar eden UI pattern'leri ortak bileşen olarak tanımlama (status badge, modal, countdown timer vb.)
- **State × Role matrisi:** Her ekranın farklı kullanıcı rolleri ve işlem durumlarına göre varyantlarını düşünme
- **Responsive design pattern'leri:** Web-first ama mobil uyumlu layout kararları
- **Lokalizasyon farkındalığı:** Çoklu dil desteğinin UI'a etkisi (metin uzunluk farkları, RTL potansiyeli)

### 4.3 Yaklaşım

**Ekran bazlı ilerleme:** Her ekran ayrı bir birim olarak tanımlanır — içerdiği bilgiler, aksiyonlar, validasyonlar ve yönlendirmeler.

**Kullanıcı akışlarına sadık kalma:** UI spesifikasyonu kullanıcı akışlarının görsel karşılığıdır. Akışta olmayan bir ekran olmamalı, ekranda olmayan bir akış adımı olmamalı.

**Wireframe düzeyinde tanım:** Pixel-perfect tasarım değil, bilgi mimarisi ve kullanıcı etkileşimi odaklı tanımlar. Görsel tasarım ayrı bir süreçtir.

**Traceability Matrix:** Ekran envanterini oluşturmadan önce, tüm akış adımlarını (03) ve gereksinimleri (02) ekranlara eşleyen iki yönlü izlenebilirlik matrisi oluşturulur. İleri (kaynak → ekran) ve geri (ekran → kaynak) kontrolle boşluklar tespit edilir, proje sahibinden karar alınır, ardından ekran tanımlarına geçilir.

### 4.4 Çıktılar

| Dosya | Açıklama |
|---|---|
| `04_UI_SPECS.md` | Ekran bazında UI tanımları, bilgi hiyerarşisi, aksiyon tanımları |

### 4.5 Prensipler

- Her ekran için "kullanıcı buraya geldiğinde ilk ne görmeli?" sorusunu sor.
- Hata durumları için de UI tanımla — boş durum, hata mesajları, yükleme durumları.
- Mobil uyumluluk MVP'de web olsa bile göz önünde tutulmalı.

### 4.6 Öğrenimler

- Component library (paylaşılan bileşenler) erken tanımlanmalı. Status badge'ler, modal'lar ve loading state'ler gibi ortak bileşenler ayrı bir bölüm olarak iyi çalıştı ama daha erken konumlandırılabilirdi.
- State × Role matrisi eksik kombinasyonları yakalar. Transaction Detail ekranı (S07) 13 durum × 3 rol = ~52 varyant gerektirdi — {ekran, kullanıcı rolü, işlem durumu} matris görünümü eksik vakaları önler.
- Lokalizasyon yüzeysel değil, bilgi mimarisini etkiler. Farklı dil uzunlukları (Türkçe vs İngilizce) layout ve bileşen boyutlarını etkiliyor — bu UI spec'te erken ele alınmalı.
- Ekran navigasyon haritası başta olmalı. Ekranlar arası geçiş haritası erken konumlandırılmalı — bu öğrenim uygulandı ve 04_UI_SPECS.md'de navigasyon haritası section 4 olarak ekran tanımlarından önce yerleştirildi.
- Admin ekranları toplam ekranların ~%50'sini oluşturdu. Admin UX'in ikincil bir endişe olarak görülmemesi gerektiğini kanıtladı.
- Timeout UX'i ayrı component tasarımı gerektiriyor. Geri sayım göstergeleri, yaklaşan timeout uyarıları gibi unsurlar özel bileşen tasarımı hak ediyor.
- Mobile-first düşünce faydalı olabilirdi. Çok kolonlu listeler gibi ekranlar mobile-first yaklaşımla daha iyi sonuç verebilirdi.
- Traceability Matrix ekran envanterinden önce yapılmalı. İki yönlü eşleme (akışlar+gereksinimler ↔ ekranlar) 7 boşluk (GAP) yakaladı — bunlar dokümanların hiçbirinde doğrudan adreslenmemiş ama UI'da cevap gerektiren sorulardı (ToS kabul adımı, davet linki landing, bildirim kanal tercihleri vb.).

---

## 5. Aşama 4 — Teknik Mimari

### 5.1 Amaç

Sistemin nasıl inşa edileceğini belirlemek. Teknoloji seçimi, servis yapısı, deployment stratejisi.

### 5.2 Rol

Bu aşamada bir **Senior Software Architect / System Designer** rolünde çalış. Uzmanlık alanları:

- **Sistem tasarımı:** Monolith vs microservice, servis sınırları, katmanlı mimari kararları
- **Teknoloji seçimi:** Her gereksinim için "en basit çalışan" teknolojiyi seçme — over-engineering'den kaçınma
- **Güvenlik mimarisi:** Escrow, kripto ödeme, Steam entegrasyonu gibi güven-kritik sistemlerde güvenlik katmanları
- **Blockchain entegrasyonu:** TRC-20 (Tron) üzerinde stablecoin işlemleri, cüzdan yönetimi, transaction monitoring
- **Steam API mimarisi:** Trade offer yönetimi, envanter okuma, bot hesap stratejisi
- **Ölçeklenebilirlik farkındalığı:** MVP'de basit tut ama ileride darboğaz yaratacak kararlardan kaçın
- **Deployment & DevOps:** CI/CD, container stratejisi, ortam yapısı (staging/prod)
- **Event-driven düşünme:** İşlem state machine'i, timeout yönetimi, asenkron süreçler (blockchain doğrulama, Steam trade takibi)

### 5.3 Yaklaşım

**Ürün gereksinimlerinden türetme:** Teknik kararlar ürün ihtiyaçlarına dayanır, tersi değil. "X teknolojisini kullanmak istiyorum" değil, "şu gereksinimi en iyi karşılayan teknoloji hangisi?" yaklaşımı.

**Basitlikten başlama:** MVP için en basit çalışan mimariyi seç. Over-engineering'den kaçın. Ölçekleme ihtiyacı gerçekten ortaya çıktığında ölçekle.

### 5.4 Çıktılar

| Dosya | Açıklama |
|---|---|
| `05_TECHNICAL_ARCHITECTURE.md` | Sistem mimarisi, teknoloji kararları, deployment yapısı |

### 5.5 Öğrenimler

- **"MVP olarak düşünme, sonrası için de düşün" yaklaşımı kritik.** Escrow platformunda event kaybı, audit eksikliği gibi riskler MVP sonrasında düzeltilmesi çok maliyetli konular. Baştan doğru mimariyi kurmak (Outbox Pattern, hybrid audit trail) ileride büyük refactor'ı önledi.
- **Workshop formatı (konu konu ilerleme) teknik aşamada da çalışıyor.** 12 konuyu sırayla ele almak, her birini bağımsız tartışıp onaylamak tutarlılığı artırdı. Bir konudaki karar sonrakini doğal olarak şekillendirdi (ör: Redis → broker olarak da Redis Streams).
- **Sidecar pattern karmaşıklık eklese de doğru durumlarda kaçınılmaz.** Steam ve blockchain entegrasyonları farklı runtime gerektiriyor (.NET yerine Node.js). Docker Compose ile yönetim kolaylaştı — monolith ruhuna sadık kalındı.
- **Monitoring stack'te maliyet kararı erken alınmalı.** "Ücretsiz olsun" kısıtı mimari kararları etkiledi (Sentry yerine Loki-based hata takibi, UptimeRobot yerine Uptime Kuma). Bu tercih baştan bilinse daha hızlı karar alınırdı.
- **Kullanıcının teknoloji deneyimi mimari kararları doğrudan etkiler.** .NET + SQL Server seçimi kullanıcının review edebilirliğinden türetildi — bu doğru bir yaklaşım. Frontend'de ise AI kod üretim kalitesi belirleyici oldu.

---

## 6. Aşama 5 — Veri Modeli

### 6.1 Amaç

Sistemdeki tüm veri yapılarını, entity'leri ve ilişkileri tanımlamak.

### 6.2 Yaklaşım

**Ürün gereksinimlerinden ve akışlardan türetme:** Her iş kuralı bir veri yapısına karşılık gelir. Gereksinimlerdeki her "X bilgisi saklanır" ifadesi bir field'a dönüşür.

**Normalizasyon ama pragmatizm:** Temiz veri modeli önemli ama performans için denormalizasyon gerekebilir. MVP'de basitlik öncelikli.

**Traceability Matrix:** Entity envanterini oluşturmadan önce, tüm gereksinimleri (02) ve akış adımlarını (03) veri yapılarına eşleyen iki yönlü izlenebilirlik matrisi oluşturulur. "X bilgisi saklanır" → hangi entity, hangi field? Eşlenmeyen gereksinim = eksik entity veya field.

### 6.3 Çıktılar

| Dosya | Açıklama |
|---|---|
| `06_DATA_MODEL.md` | Entity'ler, ilişkiler, field tanımları, indeksler |

### 6.4 Öğrenimler

- **Traceability Matrix veri modelinde de işe yarıyor.** Gereksinimler + akışlar → entity eşlemesi yapılırken 7 GAP tespit edildi. Traceability'siz ilerleseydi eksik field'lar ve entity'ler API aşamasında ortaya çıkacak, geriye dönük düzeltme gerekecekti.
- **Enum tanımları önceki dokümanlarla birebir eşleşmeli.** TransactionStatus, NotificationType gibi enum'lar 03 ve 05'ten türetildi. Farklı dokümanlardan kopyalanan enum değerleri kolayca uyumsuzluk yaratır — tek kaynak prensibini korumak kritik.
- **Silme stratejisi erken tanımlanmalı.** Soft delete, asla silinmez ve retention-based kategorilere ayırma (§1.3) entity başına karar almayı basitleştirdi. Bu üç kategori Skinora'nın audit ve compliance gereksinimlerini doğal olarak karşıladı.
- **Concurrency control baştan eklenmeli.** Transaction entity'sine `RowVersion` eklenmesi deep review'da tespit edildi. State machine + concurrent webhook callback'ler düşünüldüğünde optimistic concurrency MVP'de bile zorunlu — sonradan eklemek riskli.
- **Denormalized field'lar bilinçli bir karar olmalı.** İtibar skoru hesaplama field'ları (CompletedTransactionCount, SuccessfulTransactionRate) denormalize edildi. Her denormalizasyon kararı "nerede güncellenir, tutarsızlık riski nedir?" sorularıyla birlikte alınmalı.
- **AuditLog generic olmamalı, projeye özel olmalı.** Generic template'ten gelen `PASSWORD_CHANGED`, `KYC_STATUS_CHANGED` gibi değerler Skinora'da uygulanamaz çıktı — cross-check'te yakalandı. Enum değerleri ürün kararlarından türetilmeli, template'ten kopyalanmamalı.
- **Junction table'larda composite PK + soft delete kullanılmamalı.** AdminUserRole'de composite PK (UserId + AdminRoleId) ile soft delete bir arada kullanılınca, silinen kaydın aynı kombinasyonla tekrar oluşturulması PK violation veriyor. Çözüm: surrogate PK (guid Id) + filtered unique index (WHERE IsDeleted = 0). Bu pattern projedeki diğer entity'lerde (UserNotificationPreference, AdminRolePermission) zaten doğru uygulanmıştı — tutarlılık kontrolü bunu yakaladı.

---

## 7. Aşama 6 — API Tasarımı

### 7.1 Amaç

Frontend ile backend arasındaki iletişimi tanımlamak. Endpoint'ler, request/response yapıları, yetkilendirme.

### 7.2 Yaklaşım

**UI spesifikasyonlarından türetme:** Her ekrandaki her aksiyon bir API çağrısına karşılık gelir. UI'da gösterilen her veri bir endpoint'ten gelir.

**Tutarlı yapı:** Tüm endpoint'ler aynı konvansiyonu takip eder — naming, hata formatı, pagination, filtering.

**Traceability Matrix:** Endpoint envanterini oluşturmadan önce, tüm ekran aksiyonlarını (04) ve akış adımlarını (03) API endpoint'lerine eşleyen iki yönlü izlenebilirlik matrisi oluşturulur. Ekrandaki her aksiyon ve veri ihtiyacı → hangi endpoint? Eşlenmeyen ekran aksiyonu = eksik endpoint.

### 7.3 Çıktılar

| Dosya | Açıklama |
|---|---|
| `07_API_DESIGN.md` | Endpoint'ler, request/response, yetkilendirme, hata kodları |

### 7.4 Öğrenimler

- **Traceability Matrix API tasarımında da kritik.** 04 (ekranlar) + 03 (akışlar) → endpoint eşlemesi yapıldığında 8 GAP tespit edildi. Bu GAP'lerin bir kısmı (maintenance status, email doğrulama) başka hiçbir dokümanda açıkça adreslenmemişti — API tasarımı bunları yüzeye çıkardı.
- **Konvansiyonlar endpoint'lerden önce belirlenmeli.** 10 konvansiyon (URL yapısı, envelope, auth, pagination vb.) ilk tanımlanıp onaylandığında, endpoint detayları çok daha hızlı ve tutarlı yazıldı. Konvansiyonlar her endpoint'te tekrar tartışılmadı.
- **`availableActions` pattern'i frontend karmaşıklığını azaltır.** T5 response'undaki `availableActions` (canAccept, canCancel, canDispute, canEscalate) state × role iş mantığını backend'de tutarak frontend'in bu mantığı duplicate etmesini önledi.
- **Enum tutarlılığı audit'te yakalandı, yazım anında kaçtı.** NotificationType isimleri 06'dan farklı yazılmıştı. K10 kuralı ("06 ile birebir") doğruydu ama ilk yazımda uygulanmadı. Audit bu tutarsızlığı sistematik olarak yakaladı — "Doküman Tamamlama Protokolü" çalışıyor.
- **Downstream etki önceden planlanmalı.** GAP-5 (admin doğrudan iptal) gibi yeni bir iş kararı API tasarımı sırasında alındığında, etkilenen tüm dokümanlar önceden listelenmeli. Bu sefer 5 doküman güncellendi — ertelemek yerine hemen yansıtmak tutarlılığı korudu.
- **Response envelope vs Problem Details tartışması erken yapılmalı.** K4 kararı (envelope) kullanıcı tercihiyle alındı. Her iki yaklaşımın trade-off'ları açıkça sunulması ve kullanıcının gerekçesiyle ("response'u yorumlamak kolay olsun") karar alması iyi çalıştı.

---

## 8. Aşama 7 — Entegrasyon Spesifikasyonları

### 8.1 Amaç

Üçüncü parti servislerle (ödeme, kimlik doğrulama, bildirim vb.) entegrasyonların detaylı planını oluşturmak.

### 8.2 Yaklaşım

**Her entegrasyon ayrı bir bölüm:** Her üçüncü parti servis için ayrı detay — API limitleri, hata senaryoları, retry stratejisi, fallback planı.

**Bağımlılık riski değerlendirmesi:** "Bu servis çökerse ne olur?" sorusu her entegrasyon için sorulur.

### 8.3 Çıktılar

| Dosya | Açıklama |
|---|---|
| `08_INTEGRATION_SPEC.md` | Her entegrasyon için detaylı plan, hata yönetimi, fallback |

### 8.4 Öğrenimler

- **Field mapping doğrulaması zorunlu.** Entegrasyon dokümanında dış API response'larını iç entity field'larına eşlerken, field adlarının veri modeliyle birebir eşleştiğini doğrula. Bu aşamada SteamPersonaName/SteamDisplayName uyumsuzluğu ve 06'da olmayan SteamProfileUrl referansı yakalandı. Öğrenim: Mapping tablosu yazarken 06'yı açık tut ve her field adını kontrol et.
- **"Ücretsiz olabilir mi?" sorusu erken sorulmalı.** Piyasa fiyat verisi için önce ücretli agregator seçildi, sonra ücretsiz alternatif (Steam Market API) yeterli olduğu anlaşıldı. Entegrasyon kararlarında önce ücretsiz seçeneğin MVP'ye yetip yetmeyeceği değerlendirilmeli.
- **Geriye dönük endpoint etkisi kaçınılmaz.** Entegrasyon dokümanı yazılırken webhook endpoint'leri (Telegram) ortaya çıktı — bu endpoint 07'de yoktu. Entegrasyon dokümanı doğası gereği yeni endpoint ihtiyaçlarını ortaya çıkarır. Öğrenim: 08 tamamlandığında 07'ye geriye dönük ekleme beklenmeli.
- **Minimum iade eşiği gibi edge case kuralları 08'e ait.** Bu kurallar 05'te tanımlansa da, blockchain entegrasyon davranışını doğrudan etkiledikleri için 08'de de belirtilmeli. Genel prensip: Bir kural dış servis çağrısının yapılıp yapılmayacağını belirleyecekse, entegrasyon dokümanında yer almalı.

---

## 9. Aşama 8 — Kodlama Kılavuzu

### 9.1 Amaç

Kodlama standartlarını, klasör yapısını, naming convention'ları ve hata yönetimi yaklaşımını tanımlamak. Agent'ların tutarlı kod üretmesi için.

### 9.2 Yaklaşım

**Tek kaynak, tek doğru:** Tüm kod standartları bu dosyada. Agent bu dosyayı her görevde referans alır.

**Örneklerle tanımlama:** Soyut kurallar yerine somut örnekler ver. "Hata yönetimi yapılmalı" yerine "hata yönetimi şöyle yapılır: [örnek]".

### 9.3 Çıktılar

| Dosya | Açıklama |
|---|---|
| `09_CODING_GUIDELINES.md` | Kod standartları, klasör yapısı, naming, hata yönetimi, test yaklaşımı |

### 9.4 Öğrenimler

- **GPT cross-review süreci doküman kalitesini önemli ölçüde artırıyor.** 09 için 7 round'da 21 düzeltme yapıldı (3 KRİTİK, 17 ORTA). Claude audit'inin kaçırdığı çelişkiler (state machine side effect klasörü, modüller arası iletişim "yalnızca" ifadesi, webhook contract belirsizliği) farklı bir AI tarafından yakalandı.
- **Cross-review sırasında eklenen yeni field'lar downstream etki yaratıyor.** 09'da timeout job yönetimi için eklenen 3 entity field'ı (PaymentTimeoutJobId, TimeoutWarningJobId, TimeoutWarningSentAt) 06'da tanımlı değildi — checkpoint yakaladı. Kalite döngüsünün tamamı (audit → cross-review → checkpoint) bu tür boşlukları kapatmak için kritik.
- **Coding guidelines dokümanı diğer dokümanlardan farklı bir zorluk seviyesinde.** Hem 9 farklı dokümanla tutarlılık gerektiriyor hem de implementation-level detay içeriyor. Bu nedenle review süreci diğer dokümanlardan daha uzun sürdü (7 round).

---

## 10. Aşama 9 — Implementation Planı

### 10.1 Amaç

Tüm işi küçük, bağımsız, sıralı task'lara bölmek. Her task'ın ne olduğunu, neye bağımlı olduğunu, hangi dokümanlarla yapılacağını ve nasıl kabul edileceğini tanımlamak.

### 10.2 Yaklaşım

**Modüler task'lar:** Her task kendi başına test edilebilir olmalı. "Tüm sistemi yaz" değil, "Steam authentication servisini yaz" gibi küçük parçalar.

**Bağımlılık zinciri:** Task'lar arası bağımlılıklar açıkça tanımlanır. Hangi task'ın hangisinden önce tamamlanması gerektiği bellidir.

**Doküman referansı:** Her task için agent'a hangi dokümanların verileceği belirtilir. Agent gereksiz bilgiyle boğulmaz, sadece ihtiyacı olan dosyaları alır.

**Traceability Matrix:** Task listesini oluşturmadan önce, tüm önceki dokümanların çıktılarını (entity'ler, endpoint'ler, ekranlar, entegrasyonlar) task'lara eşleyen izlenebilirlik matrisi oluşturulur. Eşlenmeyen çıktı = eksik task.

### 10.3 Task Yapısı

Her task şu bilgileri içerir:

```
Task N: [Task adı]
  Bağımlılık: [Hangi task'lar önceden tamamlanmış olmalı]
  Dokümanlar: [Agent'a verilecek dosyalar]
  Kabul kriterleri: [Ne olduğunda "tamam" diyeceğiz]
  Test beklentisi: [Hangi test türleri (Unit/Integration/E2E) ve neyin test edileceği]
  Doğrulama kontrol listesi: [Cross-check'te neye bakılacak]
```

### 10.4 Çıktılar

| Dosya | Açıklama |
|---|---|
| `11_IMPLEMENTATION_PLAN.md` | Sıralı task listesi, bağımlılıklar, doküman referansları, kabul kriterleri |

### 10.5 Öğrenimler

- **Task şablonuna "Test beklentisi" alanı eklendi.** Audit sırasında 11'deki her task'ın test beklentisi içerdiği ama metodoloji şablonunda bu alan olmadığı fark edildi. Şablon güncellendi (§10.3).
- **Suffix'li task numaraları (T63a, T63b) esneklik sağladı.** GPT cross-review sırasında yeni task'lar gerektiğinde faz aralığını (T44–T63) bozmamak için T63a/T63b formatı kullanıldı. Bu yaklaşım mevcut bağımlılık zincirini korurken boşlukları kapatmaya izin verdi.
- **Forward pointer vs bağımlılık ayrımı önemli.** 12_VALIDATION_PROTOCOL.md henüz yazılmamışken §4.2'de referans verildi. GPT bunu 3 tur boyunca "eksik bağımlılık" olarak raporladı. Forward pointer'ın doğasını açıkça belirtmek (henüz yazılmadı, input bağımlılığı değil) bu tür karışıklıkları önler.
- **Tüketici dokümanlar geriye etki yaratmaz.** 11, diğer dokümanları (02–10) tüketen bir doküman olduğu için GPT review'da yapılan düzeltmeler (T63a, T63b, T77 vb.) yukarı yönde etki yansıtma gerektirmedi — yalnızca 11'in kendisi hizalandı.

---

## 11. Aşama 10 — Doğrulama Protokolü

### 11.1 Amaç

Kodlama yapan agent'ın çıktısını denetleyen bir mekanizma tanımlamak. Hata bulunduğunda düzeltme akışını belirlemek. Süreç tıkanmasını engellemek.

### 11.2 Yaklaşım

**Yapan ve denetleyen ayrı olmalı:** Kodlama yapan agent kendi işini doğrulayamaz. Kontrol eden farklı bir context'te çalışır.

**Üç katmanlı doğrulama:**

| Katman | Ne Yapılır | Ne Zaman |
|---|---|---|
| Task bazlı doğrulama | Her task'ın kabul kriterlerine göre checklist kontrolü | Her task tamamlandığında |
| Cross-reference kontrolü | Kodun ilgili dokümanlara uygunluk kontrolü | Her task tamamlandığında |
| Entegrasyon kontrolü | Tamamlanan task'ların birlikte çalışma kontrolü | Her 3-4 task'tan sonra |

**Hata bulunduğunda düzeltme akışı:** Bulunan hatalar için net bir geri bildirim ve düzeltme prosedürü tanımlanır. Süreç tıkanmamalı.

### 11.3 Çıktılar

| Dosya | Açıklama |
|---|---|
| `12_VALIDATION_PROTOCOL.md` | Doğrulama kuralları, cross-check prosedürü, hata düzeltme akışı |

### 11.4 Öğrenimler

*Bu bölüm aşama tamamlandığında doldurulacak.*

---

## 12. Genel Prensipler (Tüm Aşamalar İçin)

- **Süreç tıkanmasına izin verme.** Bir konuda karar alınamıyorsa detayı ileriye bırak ama varlık kararını şimdi al.
- **Her aşama bir öncekinin çıktısına dayanır.** Sırayı atlamak tutarsızlık yaratır.
- **Dokümanlar arası tutarlılık kritik.** İki farklı dosyada aynı kural farklı anlatılmamalı. Tutarlılığın birincil savunma hattı checkpoint değil, doküman yazım anıdır (bkz. "Doküman Tamamlama Protokolü").
- **Traceability Matrix ile doğrula.** Bir aşamanın çıktısı önceki aşamaların girdilerinden türetiliyorsa, çıktıyı üretmeden önce iki yönlü izlenebilirlik matrisi oluştur:
  - **İleri izlenebilirlik:** Her kaynak madde (akış adımı, gereksinim) → hangi çıktıya eşlendi? Eşlenmeyen madde = eksik çıktı (boşluk).
  - **Geri izlenebilirlik:** Her çıktı → hangi kaynaktan besleniyor? Kaynağı olmayan çıktı = gerekçesiz ekleme.
  - Matris sonucunda tespit edilen boşluklar (GAP) proje sahibine sunulur, karar alınır, ardından çıktı üretilir.
  - Bu kural şu aşamalarda uygulanır: UI Specs (akışlar + gereksinimler → ekranlar), Veri Modeli (gereksinimler + akışlar → entity'ler), API Tasarımı (ekranlar + akışlar → endpoint'ler), Implementation Plan (tüm dokümanlar → task'lar).
- **Belirsizlik agent'ın düşmanıdır.** Kodlama aşamasına geçmeden önce tüm "muhtemelen", "belki" ifadeleri net kararlara dönüşmeli.
- **Modüler ilerleme.** Hem doküman üretiminde hem kodlamada küçük, yönetilebilir parçalarla ilerle.
- **Cross-check mekanizması şart.** Kodlama yapan agent ile denetleyen mekanizma aynı olmamalı.
- **Hata bulunduğunda düzeltme akışı tanımlı olmalı.** Cross-check'te bulunan hatalar için net bir geri bildirim ve düzeltme prosedürü olmalı, süreç tıkanmamalı.
- **Doküman Tamamlama Protokolü.** Bir doküman "✓ Tamamlandı" işareti almadan önce aşağıdaki üç kontrol uygulanır. Bu kontroller checkpoint'in güvenlik ağı olarak kalmasını sağlar — asıl kalite kapısı burasıdır:
  1. **Çapraz referans doğrulaması:** Dokümanda başka bir dokümandan alınan her bilgi (enum değerleri, sayısal parametreler, iş kuralları, terimler) kaynak dokümanla birebir eşleşmeli. Mümkünse kaynak referansı belirtilmeli (ör: "02 §4.7 ile tutarlı").
  2. **İç tutarlılık kontrolü:** Dokümanın bir bölümünde söylenen, aynı dokümanın başka bir bölümüyle çelişmemeli. Özellikle özet tabloları ile detay bölümleri arasında.
  3. **Bağımlılık dokümanları taraması:** Dokümanın header'ında listelenen her bağımlılık dokümanı hedefli taranmalı — orada tanımlı olup bu dokümanda farklı ifade edilen veya eksik kalan kural var mı?

---

## 13. Agent'a İş Verme Yaklaşımı

### 13.1 Doküman Katmanları

Agent'a görevine göre sadece ihtiyacı olan dokümanları ver:

| Katman | Dokümanlar | Ne Zaman Verilir |
|---|---|---|
| Ürün bağlamı | `01`, `02`, `10` | Agent'ın "ne yapıyoruz" sorusunu anlaması gerektiğinde |
| Akış ve tasarım | `03`, `04` | Kullanıcı deneyimiyle ilgili görevlerde |
| Teknik | `05`, `06`, `07`, `08` | Kodlama görevlerinde |
| Standartlar | `09` | Her kodlama görevinde |
| Görev planı | `11` | Task atama ve takipte |
| Doğrulama | `12` | Cross-check görevlerinde |

### 13.2 Task Yapısı

Her task şu bilgileri içermelidir:

- **Task tanımı:** Ne yapılacak
- **Bağımlılıklar:** Hangi task'lar önceden tamamlanmış olmalı
- **Verilecek dokümanlar:** Agent'a hangi dosyalar verilecek
- **Kabul kriterleri:** Ne olduğunda "tamam" diyeceğiz
- **Doğrulama kontrol listesi:** Cross-check'te neye bakılacak

### 13.3 Altın Kurallar

- Agent'a "tüm sistemi yaz" deme, küçük ve bağımsız parçalar ver
- Her task kendi başına test edilebilir olmalı
- Task'lar arası bağımlılıklar açıkça tanımlanmalı
- Her 3-4 task'tan sonra entegrasyon review yap
- Tıkanma durumunda task'ı daha küçük parçalara böl

---

*Skinora — Project Methodology Playbook v0.4*
