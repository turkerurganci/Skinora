# Skinora — Validation Protocol

**Versiyon: v0.5** | **Bağımlılıklar:** `02_PRODUCT_REQUIREMENTS.md`, `03_USER_FLOWS.md`, `05_TECHNICAL_ARCHITECTURE.md`, `06_DATA_MODEL.md`, `07_API_DESIGN.md`, `08_INTEGRATION_SPEC.md`, `09_CODING_GUIDELINES.md`, `10_MVP_SCOPE.md`, `11_IMPLEMENTATION_PLAN.md` | **Son güncelleme:** 2026-03-29

---

## 1. Amaç ve Kapsam

### 1.1 Amaç

Bu protokolün amacı, Skinora MVP'sinin belirlenen iş kurallarına, güvenlik sınırlarına ve operasyonel beklentilere uygun çalıştığını sistematik olarak doğrulamaktır.

Bu doküman bir test case deposu değildir. Amacı şu soruyu cevaplamaktır: **"Bu ürün, bir sonraki aşamaya geçilebilecek olgunlukta mı?"**

Her doğrulama maddesi için neyin kontrol edileceği, başarı kriterinin ne olduğu, kanıtın nasıl üretileceği ve başarısızlık durumunda ne yapılacağı tanımlıdır.

### 1.2 Kapsam

Doğrulama kapsamı `10_MVP_SCOPE.md` ile birebir hizalıdır. MVP'de olan her özellik doğrulama kapsamındadır; MVP'de olmayan hiçbir özellik doğrulama kriteri olarak kullanılamaz.

**Kapsam dahili:**

| Alan | Kapsam |
|---|---|
| Temel escrow akışı | İşlem oluşturma → item emanet → ödeme → teslim → payout (uçtan uca) |
| Ödeme | USDT/USDC (TRC-20), otomatik doğrulama, edge case yönetimi (eksik/fazla/yanlış/parçalı/gecikmeli tutar) |
| Timeout sistemi | 4 adım timeout, state bazlı sonuçlar, timeout dondurma (downtime) |
| Kullanıcı yönetimi | Steam giriş, MA zorunluluğu, profil, cüzdan, itibar skoru, hesap silme |
| İptal yönetimi | Kullanıcı iptali, admin iptali, emergency hold, cooldown |
| Dispute | Otomatik doğrulama (ödeme/teslim/yanlış item), admin eskalasyonu |
| Fraud/abuse | Wash trading, iptal limiti, yeni hesap limiti, çoklu hesap, AML flag |
| Item yönetimi | Envanter okuma, item doğrulama, tradeable kontrolü |
| Bot yönetimi | Çoklu bot, kısıtlama durumunda yönlendirme, admin izleme |
| Admin paneli | Rol/yetki, parametre yönetimi, flag inceleme, emergency hold, audit log |
| Bildirimler | Platform içi, email, Telegram, Discord |
| Downtime yönetimi | Platform/Steam/blockchain kesintilerinde timeout dondurma |
| Erişim ve uyumluluk | Geo-block (OFAC/AB/BM), yaş kısıtı |

**Kapsam dışı:**

| Alan | Neden |
|---|---|
| Barter / çoklu item / trade lock'lu item | MVP dışı (10 §3.1) |
| Platform cüzdanı / ek blockchain / fiat | MVP dışı (10 §3.2) |
| Mobil uygulama | MVP dışı (10 §3.3) |
| KYC | MVP dışı (10 §3.5) |
| İleri faz fraud sistemleri | MVP dışı |
| Dispute ops tooling detayları | Admin eskalasyon süreci detayları MVP'de belirlenmedi (10 §3.6) |

---

## 2. Doğrulama Prensipleri

### P1 — Gereksinim bazlı doğrulama

Her doğrulama maddesi bir iş kuralına, akış adımına veya teknik spesifikasyona dayanır. "Çalışıyor gibi görünüyor" kabul edilmez; beklenen sonuç önceden tanımlanmış ve kanıtlanabilir olmalıdır.

### P2 — Kanıt zorunluluğu

Kanıtsız PASS geçersizdir. Her doğrulama maddesinin sonucu en az bir kanıt türüyle desteklenmelidir (API response, DB kaydı, log çıktısı vb.). Kanıt türleri §7'de tanımlıdır.

### P3 — Çok katmanlı kanıt

Kritik akışlarda (fon hareketi, item sahiplik değişimi, state transition) tek bir UI gözlemi yeterli kanıt sayılmaz. State, DB, log ve event seviyesinde tutarlılık aranır.

### P4 — Failure ve rollback dahil

Sadece happy path doğrulamak yeterli değildir. Her kritik akış için failure senaryosu ve rollback/iade davranışı da doğrulanır. Bir akışın "başarılı olduğunda çalışması" kadar "başarısız olduğunda güvenli duruma dönmesi" de kanıtlanmalıdır.

### P5 — Yapan ve denetleyen ayrılığı

Kodlama yapan agent kendi çıktısını doğrulayamaz. Doğrulama farklı bir context'te, farklı bir agent tarafından yapılır. Bu ayrım hem task bazlı hem entegrasyon bazlı doğrulamada geçerlidir.

### P6 — Tekrar edilebilirlik

Doğrulama adımları mümkün olan yerlerde tekrar edilebilir olmalıdır. Manuel doğrulama geçici olarak kabul edilebilir, ancak otomatize edilebilen kontroller zaman içinde tekrar edilebilir hale getirilmelidir.

### P7 — MVP scope hizalaması

Doğrulama kapsamı `10_MVP_SCOPE.md` ile birebir hizalıdır. MVP'de olmayan bir özellik doğrulama kriteri olamaz; MVP'de olan bir özellik doğrulama kapsamından çıkarılamaz.

### P8 — Süreç tıkanmasını engelleme

Doğrulama süreci implementasyonu tıkamamalıdır. FAIL durumunda net bir düzeltme akışı tanımlıdır (§8). KRİTİK olmayan açık bulgular, documented risk olarak kabul edilip sonraki aşamaya geçişe izin verebilir — ancak bu karar kayıt altına alınır.

---

## 3. Doğrulama Seviyeleri

Doğrulama altı seviyede yapılır. Her seviye farklı bir soruyu cevaplar ve farklı zamanda tetiklenir.

### 3.1 Seviye A — Gereksinim Doğrulama (Requirement Validation)

**Soru:** İş kuralları gerçekten karşılanıyor mu?

**Tetiklenme:** İlgili iş kuralını implement eden task tamamlandığında.

**Kaynak:** `02_PRODUCT_REQUIREMENTS.md`, `10_MVP_SCOPE.md`

**Kontrol alanları:**

| Alan | Örnek kontroller |
|---|---|
| İşlem kuralları | Her işlem tek item içerir. İşlem detayları oluşturulduktan sonra değiştirilemez. Sadece tradeable item kabul edilir. |
| Timeout kuralları | Her adım için ayrı timeout çalışır. Timeout sonucu state'e göre doğru iade davranışını tetikler. Admin timeout parametrelerini değiştirebilir. |
| İptal kuralları | Ödeme yapılmışsa tek taraflı iptal engellenir. İptal sebebi zorunludur. Cooldown uygulanır. |
| Komisyon kuralları | Platform fee doğru hesaplanır. Gas fee politikası (payout: komisyondan, iade: tutardan) uygulanır. |
| Fraud kuralları | Wash trading koruması çalışır. Yeni hesap limiti uygulanır. AML flag tetiklenir. |
| Erişim kuralları | Geo-block uygulanır. 18+ yaş beyanı zorunludur. |

### 3.2 Seviye B — Fonksiyonel Doğrulama (Functional Validation)

**Soru:** Uçtan uca akışlar doğru çalışıyor mu?

**Tetiklenme:** Bir akışı oluşturan tüm task'lar tamamlandığında.

**Kaynak:** `03_USER_FLOWS.md`

**Kontrol alanları:**

| Grup | Senaryolar |
|---|---|
| Happy path | İşlem oluşturma → alıcı kabul → item emanet → ödeme → teslim → payout → COMPLETED |
| Timeout senaryoları | Her 4 adım için: timeout dolması → doğru iptal state'i + doğru iade davranışı |
| İptal senaryoları | Satıcı iptali (ödeme öncesi), alıcı iptali (ödeme öncesi), admin iptali, admin emergency hold |
| Ödeme edge case'leri | Eksik tutar, fazla tutar, yanlış token, desteklenmeyen token, parçalı ödeme, gecikmeli ödeme (timeout sonrası) |
| Dispute senaryoları | Ödeme itirazı, teslim itirazı, yanlış item itirazı → otomatik doğrulama → admin eskalasyonu |
| Fraud senaryoları | Flag tetikleme → admin inceleme → onay/red → işlem devam/iptal |
| Kullanıcı yönetimi | Kayıt, profil güncelleme, cüzdan adresi değişikliği (ek doğrulama), hesap silme (aktif işlem varken engel) |
| Downtime | Platform/Steam/blockchain kesintisinde timeout dondurma → normale dönüş |

### 3.3 Seviye C — State Machine Doğrulama

**Soru:** State geçişleri tanımlı kurallara uyuyor mu? Yasak geçişler engelleniyor mu?

**Tetiklenme:** State machine implement edildiğinde ve her state'i etkileyen task tamamlandığında.

**Kaynak:** `03_USER_FLOWS.md` §1.2, `06_DATA_MODEL.md`

Her state için doğrulanacaklar:

| Kontrol | Açıklama |
|---|---|
| İzin verilen giriş koşulları | Bu state'e sadece tanımlı önceki state'lerden geçilebilir |
| İzin verilen çıkış geçişleri | Bu state'ten sadece tanımlı sonraki state'lere geçilebilir |
| Yasak geçişler | Tanımsız geçiş denemeleri reject edilir |
| Terminal state davranışı | COMPLETED ve CANCELLED_* state'lerinden çıkış yapılamaz |
| Idempotency | Aynı geçiş isteği iki kez gelirse duplicate işlem oluşmaz |
| Concurrency | Aynı anda iki farklı geçiş isteği gelirse sadece biri başarılı olur (optimistic concurrency — `RowVersion`) |
| EMERGENCY_HOLD | Hold aktifken state geçişi engellenir, timeout durur |
| FLAGGED | Flag aktifken işlem admin kararı bekler, otomatik geçiş olmaz |

State transition tablosunun detayı Ek A'dadır (§12).

### 3.4 Seviye D — Veri Doğrulama (Data Validation)

**Soru:** DB kayıtları tutarlı, eksiksiz ve doğru mu?

**Tetiklenme:** Her task tamamlandığında (task bazlı), her entegrasyon kontrolünde (bütünsel).

**Kaynak:** `06_DATA_MODEL.md`

| Kontrol | Açıklama |
|---|---|
| Monetary tutarlılık | `ItemPrice + BuyerFeeAmount = TotalBuyerAmount`. `ItemPrice - SellerFeeAmount = SellerPayoutAmount`. Rounding kuralı uygulanıyor (06 §8.1). |
| Entity ilişkileri | Seller/Buyer/Bot mapping tutarlı. Foreign key bütünlüğü korunuyor. |
| Timestamp'ler | Her state geçişinde ilgili timestamp set ediliyor. Null olmaması gereken alanlar dolu. |
| Audit trail | Her state geçişi `TransactionHistory`'de kayıtlı. Her fon hareketi ve admin aksiyonu `AuditLog`'da kayıtlı. |
| Correlation | İlişkili kayıtlar arasında correlation ID/trace zinciri izlenebilir. |
| Soft delete | Silinen kayıtlarda `IsDeleted = true`, `DeletedAt` set. Filtered unique index'ler doğru çalışıyor. |
| Outbox tutarlılığı | İş operasyonu ve outbox kaydı aynı transaction'da yazılıyor. ProcessedEvent idempotency kaydı oluşuyor. |

### 3.5 Seviye E — Entegrasyon Doğrulama (Integration Validation)

**Soru:** Dış servislerle etkileşim doğru çalışıyor mu?

**Tetiklenme:** İlgili entegrasyon task'ları tamamlandığında.

**Kaynak:** `08_INTEGRATION_SPEC.md`

| Entegrasyon | Kontrol alanları |
|---|---|
| Steam (OpenID) | Giriş akışı, session yönetimi |
| Steam (WebAPI) | Envanter okuma, item doğrulama, MA kontrolü (`GetTradeHoldDurations`) |
| Steam (Trade Offer) | Offer gönderme/kabul/iptal/timeout takibi, bot atama, bot kısıtlama durumunda yönlendirme |
| Tron (TRC-20) | Adres üretme (HD wallet), ödeme izleme (solidified endpoint + finality), payout gönderme, iade |
| Tron edge case'ler | Yanlış token tespiti (iki aşamalı izleme + spam koruması), gas fee yönetimi, rate-limit/outage ayrımı |
| Email (Resend) | 5 event gönderimi, webhook teslim takibi |
| Telegram | Webhook güvenlik, idempotency, 403 ayrıştırma |
| Discord | OAuth2 akışı, guild-install, 401/403 ayrıştırma |
| Piyasa fiyatı | Steam Market API fiyat çekimi, fallback zinciri |

MVP'de fake/mock client kullanılan entegrasyonlar için:
- Mock ile doğrulama yapılır ve MOCK PASS kabul edilir — bu, geliştirme sırasında ilerlemeyi sağlar
- Real entegrasyon bağlandığında ilgili maddeler yeniden doğrulanır (re-validation — §11)
- **Çekirdek entegrasyonlar (Steam OpenID, Steam Trade Offer, Tron ödeme izleme/payout) için MOCK PASS final release gate'i (§6.3) geçmez** — bu maddeler real veya sandbox entegrasyon ile REAL PASS almadan MVP release verilemez

### 3.6 Seviye F — Operasyonel Doğrulama

**Soru:** Sistem üretim benzeri koşullarda güvenilir şekilde çalışıyor mu?

**Tetiklenme:** Tüm task'lar tamamlandıktan sonra, final doğrulama aşamasında.

**Kaynak:** `05_TECHNICAL_ARCHITECTURE.md`, `08_INTEGRATION_SPEC.md`

| Kontrol | Açıklama |
|---|---|
| Retry sonrası duplicate | Aynı webhook/callback iki kez geldiğinde duplicate işlem oluşmuyor |
| Servis restart | Restart sonrası order state bozulmuyor, bekleyen job'lar devam ediyor |
| Stuck order tespiti | Belirli bir state'te anormal süre kalan işlemler tespit edilebiliyor |
| Log izlenebilirlik | Bir order'ın baştan sona izi loglardan sürülebiliyor (correlation ID) |
| Timeout dondurma | Downtime başladığında timeout'lar duruyor, bittiğinde kaldığı yerden devam ediyor |
| Cold wallet transfer | Hot → cold wallet transferi çalışıyor, ledger kaydı oluşuyor |
| Rate limit davranışı | Dış servis rate limit'ine ulaşıldığında graceful degradation |

---

## 4. Doğrulama Matrisi

Bu matris, doğrulanacak her maddeyi izlenebilir şekilde tanımlar. Her madde bir doğrulama seviyesine (§3) aittir ve benzersiz bir ID taşır.

### 4.1 Matris Yapısı

| Sütun | Açıklama |
|---|---|
| **ID** | Benzersiz tanımlayıcı (`VAL-NNN`) |
| **Seviye** | Doğrulama seviyesi (A–F) |
| **Kural / Gereksinim** | Doğrulanacak iş kuralı veya davranış |
| **Kaynak** | Kuralın tanımlandığı doküman ve bölüm |
| **Ön Koşul** | Doğrulama öncesi gerekli durum |
| **Beklenen Sonuç** | PASS için sağlanması gereken koşul |
| **Kanıt Türü** | Kabul edilen kanıt(lar) — §7'ye referans |
| **Severity** | KRİTİK / ORTA / DÜŞÜK — §8'e referans |
| **Durum** | BEKLEMEDE / PASS / FAIL / KABUL EDİLMİŞ RİSK |

### 4.2 Seviye A — Gereksinim Doğrulama

| ID | Kural / Gereksinim | Kaynak | Ön Koşul | Beklenen Sonuç | Kanıt Türü | Severity |
|---|---|---|---|---|---|---|
| VAL-A001 | Her işlem tek bir item içerir | 02 §2.2 | İşlem oluşturma akışı çalışır | Birden fazla item seçimi engellenir | API response | ORTA |
| VAL-A002 | İşlem detayları oluşturulduktan sonra değiştirilemez | 02 §2.2 | İşlem CREATED state'te | PUT/PATCH ile değişiklik isteği 400/403 döner | API response + DB record | KRİTİK |
| VAL-A003 | Sadece tradeable item ile işlem başlatılabilir | 02 §2.2 | Envanterde tradeable ve non-tradeable item var | Non-tradeable item ile işlem oluşturma reddedilir | API response + DB | ORTA |
| VAL-A004 | Ödeme yapılmışsa tek taraflı iptal engellenir | 02 §5.1 | İşlem PAYMENT_RECEIVED veya sonrası | Satıcı ve alıcı iptal isteği reddedilir | API response + DB record | KRİTİK |
| VAL-A005 | Platform fee doğru hesaplanır | 02 §4.1 | İşlem fiyatı ve komisyon oranı belirlenmiş | `ItemPrice × CommissionRate = BuyerFeeAmount` (06 §8.1 rounding kuralı uygulanır) | DB record + API response | KRİTİK |
| VAL-A006 | Gas fee politikası: payout komisyondan, iade tutardan düşülür | 02 §4.4, 10 §2.2 | Payout ve iade senaryoları | Payout'ta gas fee komisyondan karşılanır + koruma eşiği. İade'de gas fee tutardan düşülür | DB record + blockchain TX | KRİTİK |
| VAL-A007 | İptal sebebi zorunludur | 02 §5.1 | İptal akışı çalışır | Sebep boş bırakılırsa iptal reddedilir | API response | ORTA |
| VAL-A008 | İptal sonrası cooldown uygulanır | 02 §5.3 | Kullanıcı iptal yapmış | Cooldown süresi içinde yeni işlem başlatma engellenir | API response + DB | ORTA |
| VAL-A009 | Wash trading koruması: aynı çift, 1 ay kuralı | 02 §14.1 | Aynı seller-buyer çifti 1 ay içinde işlem tamamlamış | İşlem engellenmez ancak yeni işlem itibar skoruna etki etmez (skor etkisi kaldırılır) | DB record (User itibar alanları) | ORTA |
| VAL-A010 | Yeni hesap işlem limiti uygulanır | 02 §7.2 | Yeni kayıtlı hesap | Limit aşımında işlem engellenir | API response + DB | ORTA |
| VAL-A011 | Geo-block uygulanır | 02 §12.1, 10 §2.16 | Yasaklı bölgeden erişim denemesi | Erişim engellenir | API response + structured log | KRİTİK |
| VAL-A012 | 18+ yaş beyanı zorunludur | 02 §12.2, 10 §2.16 | İlk giriş yapan kullanıcı | Yaş beyanı kabul edilmeden platform kullanılamaz | API response + DB record | KRİTİK |
| VAL-A013 | Admin doğrudan iptal yapabilir (ayrı yetki, sebep zorunlu) | 10 §2.6 | Admin yetkilendirilmiş, aktif işlem var | İşlem CANCELLED_ADMIN state'ine geçer, iade tetiklenir, sebep kaydedilir | DB record + AuditLog | KRİTİK |
| VAL-A014 | Admin emergency hold uygulayabilir | 10 §2.6 | Aktif işlem var | İşlem dondurulur, timeout durur, devam ettirme veya iptal mümkün | DB record (IsOnHold) + AuditLog | KRİTİK |
| VAL-A015 | Min/max işlem tutarı uygulanır | 02 §8 | İşlem oluşturma akışı çalışır | Limitler dışında tutar ile işlem oluşturma reddedilir | API response | ORTA |
| VAL-A016 | Eşzamanlı aktif işlem limiti uygulanır | 02 §8 | Kullanıcı limit kadar aktif işleme sahip | Yeni işlem başlatma reddedilir | API response + DB | ORTA |
| VAL-A017 | Sanctions screening: yaptırımlı cüzdan adresi engellenir | 02 §21.1 | Yaptırımlı adres girilmiş | Adres kaydı engellenir, hesap flag'lenir, aktif işlemlere otomatik EMERGENCY_HOLD uygulanır | API response + DB (FraudFlag, IsOnHold) + AuditLog | KRİTİK |
| VAL-A018 | Stablecoin seçimi: satıcı seçer, alıcı aynı token ile öder | 02 §4.2 | İşlem oluşturulmuş, stablecoin seçilmiş | Farklı token ile ödeme kabul edilmez (yanlış token kuralı uygulanır) | API response + DB | ORTA |
| VAL-A019 | Dispute sadece alıcı tarafından açılabilir | 02 §10.2 | İşlem aktif | Satıcı dispute açma isteği reddedilir | API response | ORTA |
| VAL-A020 | Dispute açılması timeout'u durdurmaz | 02 §10.2 | Dispute açılmış, timeout çalışıyor | Timeout normal devam eder, dispute açık işlem timeout ile iptal olabilir | DB record | ORTA |
| VAL-A021 | Aynı türde dispute tekrar açılamaz | 02 §10.2 | Bir dispute türü zaten açılmış | Aynı tür dispute tekrar açma reddedilir | API response | ORTA |
| VAL-A022 | Internal/admin endpoint'ler public'e sızmaz | 07, 09 | API deploy edilmiş | Public erişim denemesi 401/404 döner. Admin endpoint'ler ayrı route prefix ve middleware arkasında | API response + structured log | KRİTİK |
| VAL-A023a | Webhook signature/token doğrulama: Steam, Tron, Resend, Telegram | 08 §2, §3, §4, §5 | Webhook endpoint'leri aktif | Geçersiz imza/secret_token ile gelen callback'ler 401 döner. Her provider kendi doğrulama mekanizmasıyla test edilir (Steam: trade offer callback, Tron: blockchain callback, Resend: Svix signature, Telegram: X-Telegram-Bot-Api-Secret-Token) | API response + structured log | KRİTİK |
| VAL-A023b | OAuth/OpenID callback doğrulama: Steam OpenID, Discord OAuth2 | 08 §1, §6 | OAuth/OpenID redirect endpoint'leri aktif | Geçersiz/manipüle edilmiş assertion (Steam) veya authorization code (Discord) ile gelen callback reddedilir. Replay koruması (nonce) çalışır. State parametresi doğrulanır | API response + structured log | KRİTİK |
| VAL-A024 | Cross-user order isolation: kullanıcı başka kullanıcının order'ına erişemez | 02, 07 | Birden fazla kullanıcı ve işlem mevcut | Seller başka seller'ın, buyer başka buyer'ın order'ına erişim/aksiyon denemesi 403 döner | API response + DB record | KRİTİK |
| VAL-A025 | Bot authorization boundary: bot sadece atandığı order için aksiyon alır | 08 §2 | Birden fazla bot ve işlem mevcut | Atanmamış order için trade offer gönderme engellenir | API response + structured log | KRİTİK |

### 4.3 Seviye B — Fonksiyonel Doğrulama

| ID | Kural / Gereksinim | Kaynak | Ön Koşul | Beklenen Sonuç | Kanıt Türü | Severity |
|---|---|---|---|---|---|---|
| VAL-B001 | Happy path: uçtan uca işlem tamamlanır | 03 §2, §3 | Tüm aktörler ve servisler hazır | CREATED → … → COMPLETED, tüm state'ler sırayla geçilir, item alıcıda, ödeme satıcıda | DB + API + log | KRİTİK |
| VAL-B002 | Alıcı kabul timeout: işlem iptal olur | 03 §4.1 | İşlem CREATED, timeout süresi dolmuş | State → CANCELLED_TIMEOUT, henüz varlık transferi yok | DB record + structured log | KRİTİK |
| VAL-B003 | Satıcı trade offer timeout: işlem iptal olur | 03 §4.2 | İşlem ACCEPTED/TRADE_OFFER_SENT_TO_SELLER, timeout dolmuş | State → CANCELLED_TIMEOUT, henüz varlık transferi yok | DB record + structured log | KRİTİK |
| VAL-B004 | Ödeme timeout: item satıcıya iade, adres izlemeye devam | 03 §4.3 | İşlem ITEM_ESCROWED, timeout dolmuş | State → CANCELLED_TIMEOUT, item iade trade offer'ı gönderilir, adres monitoring devam eder | DB + TradeOffer + PaymentAddress | KRİTİK |
| VAL-B005 | Teslim timeout: item satıcıya iade, ödeme alıcıya iade | 03 §4.4 | İşlem TRADE_OFFER_SENT_TO_BUYER, timeout dolmuş | State → CANCELLED_TIMEOUT, item satıcıya iade, ödeme alıcıya iade | DB + TradeOffer + BlockchainTransaction | KRİTİK |
| VAL-B006 | Gecikmeli ödeme (timeout sonrası): otomatik iade | 03 §4.3 | İşlem CANCELLED_TIMEOUT (ödeme adımı), ödeme sonra geldi | Gelen ödeme alıcıya otomatik iade edilir | BlockchainTransaction + DB | KRİTİK |
| VAL-B007 | Eksik tutar gönderimi: reddedilir ve iade edilir | 02 §4.4, 08 | Alıcı beklenen tutardan az gönderdi (tek transfer) | Eksik tutar payment olarak kabul edilmez (state değişmez, ITEM_ESCROWED kalır). Gelen tutar alıcıya iade kuyruğuna alınır (gas fee düşülerek). İşlem doğru tutarda ödeme beklemeye devam eder | DB + BlockchainTransaction | KRİTİK |
| VAL-B008 | Fazla tutar gönderimi: fark iade edilir | 02 §4.4, 08 | Alıcı fazla tutar gönderdi | İşlem ilerler, fark alıcıya iade edilir | DB + BlockchainTransaction | KRİTİK |
| VAL-B009 | Yanlış token gönderimi: iki aşamalı izleme + spam koruması | 08 §3 | Alıcı yanlış token gönderdi | Yanlış token tespit edilir, kullanıcı bilgilendirilir, spam koruması çalışır | DB + log | KRİTİK |
| VAL-B010 | Hesap silme: aktif işlem varken engellenir | 02 §9, 10 §2.4 | Kullanıcının aktif işlemi var | Hesap silme isteği reddedilir | API response | ORTA |
| VAL-B011 | Hesap silme: soft-delete, PII temizleme, audit trail korunur | 02 §9, 10 §2.4 | Kullanıcının aktif işlemi yok | Hesap soft-delete, PII temizlenir, işlem geçmişi ve audit logları anonim olarak kalır | DB record | ORTA |
| VAL-B012 | Cüzdan adresi değişikliğinde ek doğrulama + cooldown | 02 §12.3, 10 §2.4 | Kullanıcı cüzdan adresi değiştirmek istiyor | Ek doğrulama (Steam onayı) uygulanır. Değişiklik sonrası rol bazlı cooldown: satıcı → yeni işlem başlatma engeli, alıcı → yeni işlem başlatma + kabul engeli. Mevcut aktif işlemler eski adresle devam eder | API flow + DB | ORTA |
| VAL-B013 | Downtime'da timeout dondurma çalışır | 02 §3.3, 10 §2.14 | Platform/Steam/blockchain kesintisi tespit edildi | Aktif işlemlerde timeout durur, normale dönünce kaldığı yerden devam eder | DB (TimeoutFreezeReason) + SystemHeartbeat | KRİTİK |
| VAL-B014 | Timeout uyarısı zamanında gönderilir | 02 §3.4 | Timeout süresi dolmaya yaklaşıyor | Admin tarafından ayarlanan eşikte ilgili tarafa uyarı bildirimi gider | Notification kaydı + log | ORTA |
| VAL-B015 | Parçalı ödeme birleştirilmez | 02 §4.4 | Alıcı birden fazla ayrı transfer göndermiş | Yalnızca ilk tam tutarlı (ExpectedAmount ile eşleşen) tek transfer kabul edilir. Parçalı transferler birleştirilmez. Tam tutara ulaşmayan tüm transferler ayrı ayrı iade kuyruğuna alınır (gas fee düşülerek) | DB + BlockchainTransaction | KRİTİK |
| VAL-B016 | Her kritik state geçişinde ilgili tarafa bildirim gider | 02 §18.2 | İşlem state geçişi gerçekleşmiş | 02 §18.2 tablosundaki her tetikleyici için doğru tarafa bildirim oluşur | Notification kaydı + NotificationDelivery | ORTA |
| VAL-B017 | Satıcı gönüllü iptali: ödeme öncesi state'lerde işlem iptal olur | 02 §5.1, 03 §2 | İşlem CREATED, ACCEPTED, TRADE_OFFER_SENT_TO_SELLER veya ITEM_ESCROWED state'te (ödeme öncesi) | Satıcı iptal ister → State → CANCELLED_SELLER. İptal sebebi kaydedilir. State'e göre doğru iade davranışı tetiklenir (§13.5). Cooldown uygulanır | DB record + API response | KRİTİK |
| VAL-B018 | Alıcı gönüllü iptali: ödeme öncesi state'lerde işlem iptal olur | 02 §5.1, 03 §2 | İşlem ACCEPTED, TRADE_OFFER_SENT_TO_SELLER veya ITEM_ESCROWED state'te (ödeme öncesi) | Alıcı iptal ister → State → CANCELLED_BUYER. İptal sebebi kaydedilir. State'e göre doğru iade davranışı tetiklenir (§13.5). Cooldown uygulanır | DB record + API response | KRİTİK |
| VAL-B019 | Satıcı trade offer reddi/counter: işlem iptal olur | 03 §2, Ek A §12.1 | İşlem TRADE_OFFER_SENT_TO_SELLER, satıcı trade offer'ı reddetti veya counter gönderdi | State → CANCELLED_SELLER. Henüz item bot'a geçmemiş, iade gerekmez | DB record + TradeOffer record | KRİTİK |
| VAL-B020 | Alıcı delivery trade offer reddi/counter: item ve ödeme iade | 03 §3, Ek A §12.1 | İşlem TRADE_OFFER_SENT_TO_BUYER, alıcı trade offer'ı reddetti veya counter gönderdi | State → CANCELLED_BUYER. Item satıcıya iade edilir, ödeme alıcıya iade edilir | DB + TradeOffer + BlockchainTransaction | KRİTİK |

### 4.4 Seviye C — State Machine Doğrulama

| ID | Kural / Gereksinim | Kaynak | Ön Koşul | Beklenen Sonuç | Kanıt Türü | Severity |
|---|---|---|---|---|---|---|
| VAL-C001 | İzin verilen state geçişleri doğru çalışır | 03 §1.2, Ek A | Her state için | Sadece tanımlı geçişler başarılı olur | DB record + API response | KRİTİK |
| VAL-C002 | Yasak state geçişleri reject edilir | 03 §1.2, Ek A | Her state için | Tanımsız geçiş denemesi hata döner, state değişmez | API response (400/409) + DB record | KRİTİK |
| VAL-C003 | Terminal state'lerden çıkış yapılamaz | 03 §1.2 | İşlem COMPLETED veya CANCELLED_* | Herhangi bir state geçiş denemesi reddedilir | API response + DB record | KRİTİK |
| VAL-C004 | Aynı state geçiş isteği iki kez gelirse duplicate oluşmaz | 06, 09 | Geçiş isteği zaten işlenmiş | İkinci istek idempotent — aynı sonuç, yeni kayıt yok | DB record + API response | KRİTİK |
| VAL-C005 | Concurrent geçiş isteklerinde sadece biri başarılı olur | 06 (RowVersion) | Aynı anda iki farklı geçiş isteği | Biri başarılı, diğeri concurrency hatası alır (409) | API response + DB | KRİTİK |
| VAL-C006 | EMERGENCY_HOLD aktifken state geçişi engellenir | 03 §1.2, 05 §4.5 | İşlem hold'da | Geçiş isteği reddedilir, timeout durmuş | API response + DB (IsOnHold) | KRİTİK |
| VAL-C007 | FLAGGED state'te admin kararı beklenir | 03 §8 | İşlem FLAGGED | Otomatik geçiş olmaz, admin onay/red ile devam eder | DB + AuditLog | KRİTİK |
| VAL-C008 | Her state geçişi TransactionHistory'de kayıtlıdır | 06 §3.6 | Herhangi bir state geçişi | TransactionHistory kaydı oluşur: önceki state, yeni state, timestamp, tetikleyen aktör | DB record + structured log | KRİTİK |
| VAL-C009 | EMERGENCY_HOLD kaldırma — RESUME: işlem kaldığı yerden devam eder | 03 §1.2, 05 §4.5, Ek A §12.2 | İşlem hold'da, admin RESUME kararı verdi | State değişmez, timeout kalan süreden devam eder (`TimeoutRemainingSeconds` doğru hesaplanmış), `IsOnHold = false` | DB record + AuditLog | KRİTİK |
| VAL-C010 | EMERGENCY_HOLD kaldırma — CANCEL: state'e göre doğru iade tetiklenir | 03 §1.2, 05 §4.5, Ek A §12.2, §13.5 | İşlem hold'da, admin CANCEL kararı verdi | İşlem CANCELLED_ADMIN'e geçer. State'e göre doğru iade davranışı tetiklenir (§13.5 tablosuyla uyumlu) | DB record + AuditLog + blockchain TX (iade varsa) | KRİTİK |
| VAL-C011 | ITEM_DELIVERED state'inde hold'dan yalnızca RESUME ile çıkılır | Ek A §12.2 | İşlem ITEM_DELIVERED + hold aktif | CANCEL denemesi reddedilir — yalnızca RESUME mümkün (item alıcıda, standart iade uygulanamaz) | API response + DB record | KRİTİK |
| VAL-C012 | EMERGENCY_HOLD apply/release audit trail eksiksiz | 06 §3.23, 05 §4.5 | Hold uygulanmış ve kaldırılmış | Hold apply ve release için AuditLog kaydı mevcut: admin ID, sebep, timestamp, karar (RESUME/CANCEL) | DB record (AuditLog) + structured log | KRİTİK |

### 4.5 Seviye D — Veri Doğrulama

| ID | Kural / Gereksinim | Kaynak | Ön Koşul | Beklenen Sonuç | Kanıt Türü | Severity |
|---|---|---|---|---|---|---|
| VAL-D001 | Monetary aritmetik: `ItemPrice + BuyerFeeAmount = TotalBuyerAmount` | 06 §8.1 | Tamamlanmış işlem | Eşitlik sağlanır, rounding kuralı uygulanmış | DB record + API response | KRİTİK |
| VAL-D002 | Monetary aritmetik: `ItemPrice - SellerFeeAmount = SellerPayoutAmount` | 06 §8.1 | Tamamlanmış işlem | Eşitlik sağlanır | DB record + API response | KRİTİK |
| VAL-D003 | Tüm timestamp alanları set ediliyor | 06 | İşlem herhangi bir state'e geçmiş | İlgili timestamp alanı null değil, mantıksal sıra doğru (CreatedAt < AcceptedAt < …) | DB record | ORTA |
| VAL-D004 | Audit trail eksiksiz: fon hareketleri AuditLog'da | 06 §3.23 | Fon hareketi gerçekleşmiş (ödeme, payout, iade) | AuditLog kaydı mevcut, aktör ve aksiyon doğru | DB record + structured log | KRİTİK |
| VAL-D005 | Audit trail eksiksiz: admin aksiyonları AuditLog'da | 06 §3.23 | Admin aksiyon almış | AuditLog kaydı mevcut, admin user ID ve aksiyon doğru | DB record + structured log | KRİTİK |
| VAL-D006 | Outbox tutarlılığı: iş operasyonu + outbox aynı TX'te | 05 §5.1, 06 §3.18 | State geçişi veya fon hareketi | OutboxMessage kaydı aynı DB transaction'da yazılmış | DB record + structured log | KRİTİK |
| VAL-D007 | Consumer idempotency: ProcessedEvent kaydı oluşuyor | 06 §3.19 | Event consume edilmiş | ProcessedEvent kaydı mevcut, aynı event tekrar işlenmiyor | DB record + structured log | KRİTİK |
| VAL-D008 | Soft delete: filtered unique index doğru çalışıyor | 06 | Kayıt silinmiş (IsDeleted = true) | Aynı unique kombinasyonla yeni kayıt oluşturulabiliyor | DB record | ORTA |
| VAL-D009 | Seller-Buyer-Bot mapping tutarlı | 06 §3.5 | İşlem oluşturulmuş ve bot atanmış | Foreign key'ler doğru, ilişkili entity'ler mevcut | DB record | ORTA |
| VAL-D010 | Cüzdan adresi snapshot prensibi: işlem anında sabitlenir | 02 §12.3 | İşlem başlatılmış/kabul edilmiş, sonra profil adresi değişmiş | İşlemdeki adres orijinal snapshot'ı korur, profil değişikliği aktif işlemi etkilemez | DB record + API response | KRİTİK |
| VAL-D011 | İtibar skoru bileşenleri doğru hesaplanır | 02 §13 | Kullanıcının tamamlanmış/iptal olmuş işlemleri var | Tamamlanan işlem sayısı, başarılı işlem oranı, hesap yaşı doğru hesaplanır | DB record (User itibar alanları) | ORTA |
| VAL-D012 | Asset lineage zinciri doğru takip ediliyor | 06 §3.5 | İşlem uçtan uca tamamlanmış (COMPLETED) | `ItemAssetId` (seller orijinal) işlem oluşturmada set edilmiş. `EscrowBotAssetId` (bot'a geçtikten sonra Steam'in atadığı yeni ID) ITEM_ESCROWED'da set edilmiş. `DeliveredBuyerAssetId` (alıcıya teslim sonrası Steam'in atadığı yeni ID) ITEM_DELIVERED'da set edilmiş. Üç ID birbirinden farklı ve her biri ilgili Steam trade'in sonucuyla tutarlı | DB record + TradeOffer record | KRİTİK |

### 4.6 Seviye E — Entegrasyon Doğrulama

| ID | Kural / Gereksinim | Kaynak | Ön Koşul | Beklenen Sonuç | Kanıt Türü | Severity |
|---|---|---|---|---|---|---|
| VAL-E001 | Steam giriş akışı çalışır | 08 §1 | Steam OpenID yapılandırılmış | Kullanıcı Steam ile giriş yapabilir, User kaydı oluşur | API response + DB | KRİTİK |
| VAL-E002 | MA kontrolü trade URL kaydında yapılır | 08 §2.2 | Kullanıcı trade URL kaydetmeye çalışıyor | MA aktif değilse uyarı, işlem başlatma engeli | API response + DB record | KRİTİK |
| VAL-E003 | Bot trade offer gönderme/takip çalışır | 08 §2 | Bot atanmış, item bilgisi doğru | Trade offer gönderilir, durum takip edilir | TradeOffer record + log | KRİTİK |
| VAL-E004 | Bot kısıtlama durumunda yeni işlem diğer bot'a yönlendirilir | 08 §2, 10 §2.10 | Bir bot kısıtlanmış | Yeni işlem uygun bot'a atanır | DB (PlatformSteamBot) + log | KRİTİK |
| VAL-E005 | HD wallet'tan benzersiz ödeme adresi üretilir | 08 §3 | İşlem ITEM_ESCROWED | Benzersiz adres üretilir, PaymentAddress kaydı oluşur | DB record + structured log | KRİTİK |
| VAL-E006 | Blockchain ödeme izleme (solidified + finality) çalışır | 08 §3 | Ödeme adresi üretilmiş, alıcı ödeme göndermiş | Ödeme doğru tespit edilir, finality beklenir | DB (BlockchainTransaction) + log | KRİTİK |
| VAL-E007 | Payout gönderimi çalışır | 08 §3 | İşlem ITEM_DELIVERED | Satıcıya doğru tutar gönderilir | BlockchainTransaction + log | KRİTİK |
| VAL-E008 | Email bildirimi (Resend) 5 event için gönderilir | 08 §4 | İlgili event tetiklenmiş | Email gönderilir, NotificationDelivery kaydı oluşur | DB + log | ORTA |
| VAL-E009 | Telegram webhook güvenlik + idempotency | 08 §5 | Telegram entegrasyonu aktif | Webhook doğrulanır, duplicate callback işlenmez | log | ORTA |
| VAL-E010 | Steam Market fiyat çekimi + fallback zinciri | 08 §7 | Piyasa fiyat kontrolü tetiklenmiş | Fiyat alınır veya fallback zinciri çalışır | log | ORTA |
| VAL-E011 | Discord OAuth2 bağlantı akışı + bildirim gönderimi | 08 §6 | Discord entegrasyonu aktif | OAuth2 ile kullanıcı bağlantısı kurulur, guild-install çalışır, bildirim gönderilir, 401/403 ayrıştırması doğru yapılır | API response + log | ORTA |

### 4.7 Seviye F — Operasyonel Doğrulama

| ID | Kural / Gereksinim | Kaynak | Ön Koşul | Beklenen Sonuç | Kanıt Türü | Severity |
|---|---|---|---|---|---|---|
| VAL-F001 | Duplicate webhook/callback ikinci kez gelirse duplicate işlem oluşmaz | 05 §5.1, 06 §3.19 | Aynı callback iki kez gönderilmiş | İlk işlenir, ikinci idempotent olarak geçer | DB + log | KRİTİK |
| VAL-F002 | Servis restart sonrası order state bozulmuyor | 05 | Aktif işlemler var, servis restart | State'ler korunmuş, bekleyen job'lar devam ediyor | DB record + log | KRİTİK |
| VAL-F003 | Stuck order tespit edilebiliyor | 05, 09 | Bir işlem anormal süre aynı state'te | Tespit mekanizması (log/alert) çalışıyor | log / alert | ORTA |
| VAL-F004 | Correlation ID ile order izlenebilirliği | 09 | Bir işlem uçtan uca tamamlanmış | Tüm loglardan correlation ID ile zincir izlenebiliyor | structured log | ORTA |
| VAL-F005 | Hot → cold wallet transferi çalışıyor | 05 §3.3, 06 §3.22 | Hot wallet bakiyesi eşik üzerinde | Transfer gerçekleşir, ColdWalletTransfer kaydı oluşur | DB record + blockchain TX | KRİTİK |
| VAL-F006 | Rate limit durumunda graceful degradation | 08 | Dış servis rate limit'e ulaşılmış | Retry/backoff uygulanır, işlem bozulmaz | log | ORTA |

---

## 5. Giriş Kriterleri

Bir doğrulama maddesi ancak aşağıdaki ön koşullar sağlandığında çalıştırılabilir. Ön koşulları karşılanmayan madde BEKLEMEDE kalır — PASS veya FAIL verilemez.

### 5.1 Task Bazlı Doğrulama (Seviye A–D)

| Kriter | Açıklama |
|---|---|
| Implementation tamamlanmış | İlgili task `11_IMPLEMENTATION_PLAN.md`'de tanımlı kabul kriterlerini karşılamış olmalı |
| Kod derlenebilir | Build hatasız tamamlanıyor |
| Birim testler geçiyor | Task'ın kendi test suite'i PASS |
| Bağımlı servisler ayakta | Task'ın ihtiyaç duyduğu servisler (DB, Redis, sidecar vb.) çalışır durumda |
| Test/seed data mevcut | Doğrulama senaryosu için gerekli veriler oluşturulmuş |
| Konfigürasyon hazır | İlgili SystemSetting kayıtları (timeout süreleri, komisyon oranları, limitler vb.) set edilmiş |

### 5.2 Entegrasyon Doğrulama (Seviye E)

Seviye A–D kriterlerine ek olarak:

| Kriter | Açıklama |
|---|---|
| Entegrasyon client'ı hazır | Real veya mock client implement edilmiş ve yapılandırılmış |
| Dış servis erişimi | Real entegrasyonlarda dış servis erişimi doğrulanmış (API key, webhook URL vb.) |
| Mock/fake ayrımı belirtilmiş | Hangi entegrasyonun mock, hangisinin real olduğu kayıt altında |

### 5.3 Operasyonel Doğrulama (Seviye F)

Seviye A–E kriterlerine ek olarak:

| Kriter | Açıklama |
|---|---|
| Tüm task'lar tamamlanmış | `11_IMPLEMENTATION_PLAN.md`'deki tüm task'lar DONE |
| Staging ortamı hazır | Üretim benzeri ortamda deploy edilmiş |
| Gözlemlenebilirlik açık | Structured logging, correlation ID, outbox monitoring çalışır durumda |
| Uçtan uca akış en az bir kez geçmiş | Seviye B happy path (VAL-B001) PASS almış |

---

## 6. Çıkış Kriterleri

Doğrulama sürecinden geçiş ancak aşağıdaki koşullar sağlandığında onaylanır. Çıkış kriterleri üç seviyede uygulanır: task bazlı, faz bazlı ve final.

### 6.1 Task Bazlı Çıkış

Bir task'ın doğrulaması tamamlanmış sayılması için:

| Kriter | Açıklama |
|---|---|
| İlgili VAL maddeleri PASS | Task'a ait tüm doğrulama matrisi maddeleri PASS almış |
| KRİTİK açık bulgu yok | Task'a ait KRİTİK severity'de FAIL madde yok |
| ORTA açık bulgular kayıt altında | ORTA severity'de FAIL madde varsa documented risk olarak kabul edilmiş ve kayıt altına alınmış |
| Kanıt üretilmiş | Her PASS madde için §7'ye uygun kanıt mevcut |

### 6.2 Faz Bazlı Çıkış

`11_IMPLEMENTATION_PLAN.md`'deki bir faz tamamlandığında:

| Kriter | Açıklama |
|---|---|
| Faz içi tüm task'lar çıkış kriterini karşılamış | Her task §6.1'e uygun |
| Entegrasyon kontrolü yapılmış | Faz içi task'lar arası etkileşim doğrulanmış (Seviye B/C) |
| Regresyon kontrolü yapılmış | Önceki fazlarda PASS almış maddelerden etkilenebilecekler yeniden doğrulanmış |
| Açık risk envanteri güncel | Tüm documented risk kayıtları güncel ve proje sahibi tarafından kabul edilmiş |

### 6.3 Final Çıkış (MVP Release Gate)

Tüm fazlar tamamlandıktan sonra, MVP release için:

| Kriter | Açıklama |
|---|---|
| Tüm KRİTİK VAL maddeleri PASS | Matriste KRİTİK severity'deki hiçbir madde FAIL veya BEKLEMEDE olamaz |
| Tüm ORTA VAL maddeleri PASS veya KABUL EDİLMİŞ RİSK | ORTA severity'de açık FAIL kalmamış |
| DÜŞÜK maddeler bloklamaz | DÜŞÜK severity maddeler release'i engellemez, ancak kayıt altında |
| Seviye F operasyonel doğrulama tamamlanmış | Tüm VAL-F maddeleri PASS |
| Happy path uçtan uca geçmiş | VAL-B001 PASS |
| Çekirdek entegrasyonlar real/sandbox ile doğrulanmış | Steam OpenID, Steam Trade Offer ve Tron ödeme/payout akışlarına ait KRİTİK VAL-E maddeleri mock PASS ile final gate'i geçemez — real veya sandbox entegrasyon ile PASS zorunlu. Mock PASS bu maddeler için BEKLEMEDE statüsünde kalır |
| Kanıt arşivi oluşturulmuş | Tüm PASS maddelerinin kanıtları erişilebilir durumda |
| Proje sahibi onayı | Final çıkış kararı proje sahibi tarafından onaylanmış |

---

## 7. Kanıt Standardı

Her PASS sonucu en az bir kanıt türüyle desteklenmelidir. Kanıtsız PASS geçersizdir (P2).

### 7.1 Kabul Edilen Kanıt Türleri

| Kanıt Türü | Açıklama | Örnek |
|---|---|---|
| **API response** | Endpoint'e yapılan isteğin tam response'u (status code + body) | `POST /api/transactions` → 201 + response body |
| **DB record** | İlgili tablodaki kayıt(lar)ın snapshot'ı | Transaction kaydı: Status = COMPLETED, SellerPayoutAmount = 95.00 |
| **Blockchain TX** | Blockchain transaction hash ve detayları | TxHash, tutar, gönderen/alıcı adres, onay sayısı |
| **Structured log** | İlgili akışa ait log çıktısı (correlation ID ile filtrelenmiş) | `[TraceId: abc-123] PaymentConfirmed → StateTransition: ITEM_ESCROWED → PAYMENT_RECEIVED` |
| **TradeOffer record** | Steam trade offer durumu (DB kaydı + Steam API response) | TradeOffer kaydı: Status = Accepted, SteamTradeOfferId = 123456 |
| **Event/outbox kaydı** | OutboxMessage ve/veya ProcessedEvent kayıtları | OutboxMessage: EventType = PaymentConfirmed, ProcessedAt = ... |
| **Test report** | Otomatik test çıktısı (birim veya entegrasyon) | `Tests passed: 12/12, Coverage: TransactionService` |
| **Alert/monitoring** | Monitoring sisteminden gelen alert veya dashboard görüntüsü | Stuck order alert tetiklendi, Grafana panel snapshot |

### 7.2 Severity'ye Göre Kanıt Gereksinimleri

| Severity | Minimum kanıt |
|---|---|
| **KRİTİK** | En az 2 farklı türde kanıt (çok katmanlı — P3). Örnek: API response + DB record |
| **ORTA** | En az 1 kanıt türü yeterli |
| **DÜŞÜK** | En az 1 kanıt türü yeterli |

### 7.3 Kanıt Kuralları

- Kanıt, doğrulama anında üretilmiş olmalıdır — eski veya farklı ortamdan alınan kanıt kabul edilmez.
- Kanıtlar doğrulama maddesinin ID'si (VAL-xxx) ile eşleştirilmiş şekilde saklanır.
- Tek bir UI gözlemi (ekran görüntüsü) KRİTİK akışlarda tek başına yeterli kanıt sayılmaz — state, DB veya log seviyesinde desteklenmelidir.
- Otomatik test çıktısı (test report) geçerli bir kanıt türüdür ve tekrar edilebilirlik açısından tercih edilir.

---

## 8. Severity ve Hata Yönetimi

### 8.1 Severity Sınıflandırması

| Severity | Tanım | Örnekler |
|---|---|---|
| **KRİTİK** | Para kaybı, item kaybı, yanlış sahiplik, yasak state geçişi, duplicate completion, güvenlik ihlali. Release'i mutlak bloklar. | Payout yanlış hesaplanır. Ödeme yapılmışken tek taraflı iptal mümkün. Aynı callback ile çift teslim oluşur. Başka kullanıcının order'ına erişim sağlanır. |
| **ORTA** | Yanlış retry davranışı, eksik log, recovery zayıflığı, iş kuralı defect'i (para/item kaybı yaratmayan). Documented risk olarak kabul edilebilir. | İptal cooldown'u uygulanmıyor. Timestamp sırası yanlış. Bildirim gönderilmiyor. Stuck order tespit edilemiyor. |
| **DÜŞÜK** | UI/UX kusuru, metin hatası, operasyonu bloklamayan görünürlük sorunları. Release'i bloklamaz. | Hata mesajı belirsiz. Loading state eksik. Log formatı tutarsız. |

### 8.2 Hata Durumları (Durum Akışı)

Bir doğrulama maddesi FAIL aldığında şu akış izlenir:

```
FAIL tespit edilir
    │
    ├── 1. Defect kaydı açılır
    │       - VAL ID referansı
    │       - Severity atanır (§8.1)
    │       - Beklenen vs gerçekleşen sonuç
    │       - Etkilenen diğer VAL maddeleri listelenir
    │
    ├── 2. Düzeltme yapılır
    │       - Coding agent düzeltmeyi implement eder
    │       - Düzeltmenin etki alanı belirlenir
    │
    ├── 3. Yeniden doğrulama
    │       - FAIL alan madde tekrar çalıştırılır
    │       - Regresyon kontrolü: düzeltmeden etkilenebilecek
    │         komşu VAL maddeleri de yeniden doğrulanır (§11)
    │
    └── 4. Sonuç
            - PASS → madde kapatılır, kanıt eklenir
            - Tekrar FAIL → döngü 2'ye döner
```

### 8.3 Regresyon Kuralları

- Bir düzeltme yapıldığında, düzeltmenin dokunduğu kod alanıyla ilişkili diğer VAL maddeleri belirlenir.
- KRİTİK severity'deki state-flow veya money-flow düzeltmelerinde, aynı akışa ait tüm VAL maddeleri yeniden doğrulanır.
- ORTA ve DÜŞÜK düzeltmelerde sadece doğrudan etkilenen maddeler yeniden doğrulanır.

### 8.4 Documented Risk (Kabul Edilmiş Risk)

Bir FAIL maddesinin düzeltilmesinin orantısız maliyet yaratacağı veya MVP kapsamını aşacağı durumlarda, madde KABUL EDİLMİŞ RİSK olarak işaretlenebilir.

Koşullar:
- Severity KRİTİK olamaz — KRİTİK bulgu risk olarak kabul edilemez
- Proje sahibi onayı zorunlu
- Risk kaydı oluşturulur: VAL ID, risk açıklaması, olası etki, mitigasyon planı (varsa), kabul tarihi
- Risk kaydı §6.3 final çıkış kontrolünde değerlendirilir

### 8.5 Süreç Tıkanmasını Engelleme

- Bir FAIL maddesi düzeltilirken diğer task'ların doğrulaması devam edebilir — bağımlılık yoksa paralel ilerlenir.
- Düzeltme 2 iterasyondan sonra çözülemezse, proje sahibine eskalasyon yapılır: kapsam daraltma, alternatif yaklaşım veya documented risk kararı alınır.
- Doğrulama süreci hiçbir koşulda implementasyonu süresiz tıkamamalıdır (P8).

---

## 9. Agent Çalışma Modeli

Bu projede kodlama ve doğrulama agent'lar tarafından yapılır. Yapan ve denetleyen ayrılığı (P5) agent context seviyesinde uygulanır.

### 9.1 Agent Rolleri

| Rol | Sorumluluk | Context |
|---|---|---|
| **Coding Agent** | Task'ı implement eder, birim testleri yazar | Kendi context'inde çalışır |
| **Reviewer Agent** | Task çıktısını doğrular, VAL maddelerini çalıştırır, kanıt üretir | Ayrı context'te çalışır — coding agent'ın context'ini görmez |

Aynı agent aynı task için hem coding hem reviewer rolünü üstlenemez.

### 9.2 Agent'a Verilecek Dokümanlar

Reviewer agent'a görevine göre doküman katmanları verilir (00 §13.1):

| Doğrulama Seviyesi | Reviewer Agent'a Verilecek Dokümanlar |
|---|---|
| Seviye A (Gereksinim) | `02`, `10`, `12` (bu doküman) |
| Seviye B (Fonksiyonel) | `02`, `03`, `10`, `12` |
| Seviye C (State Machine) | `03` §1.2, `06`, `12`, Ek A |
| Seviye D (Veri) | `06`, `09`, `12` |
| Seviye E (Entegrasyon) | `08`, `12` |
| Seviye F (Operasyonel) | `05`, `08`, `09`, `12` |

Ek olarak her seviyede:
- İlgili task'ın `11_IMPLEMENTATION_PLAN.md`'deki tanımı (kabul kriterleri, test beklentisi, doğrulama kontrol listesi)
- Coding agent'ın ürettiği kod (review edilecek dosyalar)
- **Kaynak-güdümlü ek dokümanlar:** VAL maddesinin "Kaynak" sütununda referans verilen ancak seviye bazlı temel sette bulunmayan dokümanlar da reviewer agent'a dahil edilir. Örnek: Seviye E temel seti 08 ve 12'dir, ancak VAL-E004 kaynağı "08 §2, 10 §2.10" olduğundan 10 de eklenir. Bu kural, reviewer agent'ın bir VAL maddesini kaynak dokümanı okumadan değerlendirmesini engeller

### 9.3 Reviewer Agent Çalışma Akışı

```
1. Task tamamlandı bildirimi alınır
       │
2. Giriş kriterleri kontrol edilir (§5)
       │
3. İlgili VAL maddeleri belirlenir
   (task → VAL eşlemesi, 11'deki doğrulama kontrol listesinden)
       │
4. Her VAL maddesi için:
   a. Ön koşul sağlanıyor mu? → Hayır → BEKLEMEDE
   b. Doğrulama çalıştırılır
   c. Kanıt üretilir (§7)
   d. Sonuç: PASS veya FAIL
       │
5. Sonuç raporu üretilir:
   - PASS alan maddeler + kanıt referansları
   - FAIL alan maddeler + beklenen vs gerçekleşen
   - Regresyon riski taşıyan komşu maddeler
       │
6. FAIL varsa → §8 hata yönetimi akışı başlar
   Tüm maddeler PASS → task doğrulaması tamamlanır
```

### 9.4 Implementation Plan (11) ile Entegrasyon

Her task'ın `11_IMPLEMENTATION_PLAN.md`'deki tanımı doğrulama ile doğrudan bağlantılıdır:

| 11'deki Alan | 12'deki Karşılığı |
|---|---|
| Kabul kriterleri | Reviewer agent'ın kontrol ettiği temel koşullar |
| Test beklentisi | Coding agent'ın üretmesi gereken testler (birim/entegrasyon) |
| Doğrulama kontrol listesi | Bu dokümanın VAL maddelerine eşlenen kontroller |

Faz geçişlerinde (11 §3):
- Faz içi tüm task'lar doğrulanmış olmalı (§6.2)
- Entegrasyon kontrolü (Seviye B/C) faz bazlı yapılır
- Regresyon kontrolü önceki fazları kapsar

---

## 10. Roller ve Onay

Doğrulama sürecinde sorumluluklar rol bazlı tanımlanır. Proje tek kişi tarafından yürütülse bile roller ayrı tutulur — bu, hangi şapkayla hangi kararın alındığını netleştirir.

### 10.1 Rol Tanımları

| Rol | Sorumluluk | Kim Üstlenir |
|---|---|---|
| **Coding Agent** | Task implementasyonu, birim test yazımı, defect düzeltmesi | AI agent (coding context) |
| **Reviewer Agent** | VAL maddelerini çalıştırma, kanıt üretme, PASS/FAIL verme | AI agent (review context) |
| **Proje Sahibi** | İş kuralı doğrulama, documented risk kabulü, çıkış onayı, eskalasyon kararları | İnsan |
| **Teknik Karar Verici** | Mimari etki değerlendirmesi, regresyon kapsamı belirleme, alternatif yaklaşım önerisi | İnsan (proje sahibi ile aynı kişi olabilir) |

### 10.2 Onay Gerektiren Kararlar

| Karar | Onay Veren |
|---|---|
| VAL maddesi PASS | Reviewer Agent (kanıt ile birlikte) |
| VAL maddesi FAIL → defect kaydı | Reviewer Agent |
| Documented risk kabulü (§8.4) | Proje Sahibi |
| Faz bazlı çıkış (§6.2) | Proje Sahibi |
| Final çıkış / MVP release gate (§6.3) | Proje Sahibi |
| Eskalasyon kararı (§8.5) | Proje Sahibi |
| Regresyon kapsamı genişletme | Teknik Karar Verici |

### 10.3 Sorumluluk Sınırları

- Reviewer Agent PASS/FAIL verir ama documented risk kararı alamaz — bu proje sahibinin yetkisindedir.
- Coding Agent kendi ürettiği kodu review edemez (P5).
- Proje sahibi teknik doğrulama yapmaz ama iş kuralı doğrulamasında (Seviye A) son söz hakkına sahiptir.
- Hiçbir rol tek başına final çıkış kararı alamaz — reviewer agent'ın PASS raporu + proje sahibi onayı birlikte gereklidir.

---

## 11. Yeniden Doğrulama Kuralları

Daha önce PASS almış bir VAL maddesi belirli koşullarda yeniden doğrulanmalıdır. Bu bölüm hangi durumlarda re-validation gerektiğini tanımlar.

### 11.1 Tetikleyiciler

| Tetikleyici | Re-validation Kapsamı |
|---|---|
| **Defect düzeltmesi** | FAIL alan madde + düzeltmenin dokunduğu kod alanıyla ilişkili komşu VAL maddeleri (§8.3) |
| **Mock → real entegrasyon geçişi** | İlgili entegrasyona ait tüm VAL-E maddeleri + bu entegrasyona bağımlı VAL-B maddeleri |
| **Faz geçişi** | Yeni fazda implement edilen kodun etkileyebileceği önceki faz VAL maddeleri |
| **Konfigürasyon değişikliği** | Değişen parametreye bağımlı VAL maddeleri (ör: timeout süresi değişirse VAL-B002–B005) |
| **Altyapı değişikliği** | DB migration, Docker config değişikliği, sidecar güncellemesi → etkilenen seviyedeki tüm maddeler |

### 11.2 Kapsam Belirleme Kuralları

- **KRİTİK state-flow veya money-flow düzeltmesi:** Aynı akışa ait tüm VAL maddeleri yeniden doğrulanır. Dar kapsam kabul edilmez.
- **ORTA/DÜŞÜK düzeltme:** Sadece doğrudan etkilenen maddeler yeniden doğrulanır. Etki alanı reviewer agent tarafından belirlenir.
- **Mock → real geçişi:** Mock ile PASS almış tüm maddeler geçersiz sayılır ve real entegrasyonla yeniden çalıştırılır. Önceki kanıtlar arşivlenir ama geçerliliğini yitirir.
- **Şüphe durumu:** Bir maddenin etkilenip etkilenmediği belirsizse, yeniden doğrulanır. Şüpheli maddeyi atlamak kabul edilmez.

### 11.3 Re-validation Sonuçları

- Re-validation sonucu orijinal maddenin durumunu günceller (PASS kalır veya FAIL'e döner).
- Re-validation'da FAIL çıkarsa §8 hata yönetimi akışı başlar — regresyon olarak kaydedilir.
- Re-validation kanıtları orijinal kanıtın üzerine yazılır (en güncel kanıt geçerlidir).

---

## 12. Ek A — State Transition Doğrulama Tablosu

Bu tablo, state machine doğrulama maddeleri (VAL-C001 – VAL-C012) için detaylı referanstır. Her state için izin verilen geçişler, yasak geçişler ve doğrulama kuralları tanımlanır.

**Kaynak:** `03_USER_FLOWS.md` §1.2, §2–§8

### 12.1 State Transition Matrisi

| Mevcut State | İzin Verilen Sonraki State'ler | Tetikleyici |
|---|---|---|
| **FLAGGED** | CREATED, CANCELLED_ADMIN | Admin onay → CREATED. Admin red → CANCELLED_ADMIN. |
| **CREATED** | ACCEPTED, CANCELLED_TIMEOUT, CANCELLED_SELLER | Alıcı kabul → ACCEPTED. Timeout → CANCELLED_TIMEOUT. Satıcı iptal → CANCELLED_SELLER. |
| **ACCEPTED** | TRADE_OFFER_SENT_TO_SELLER, CANCELLED_TIMEOUT, CANCELLED_SELLER, CANCELLED_BUYER, CANCELLED_ADMIN | Trade offer gönderildi → TRADE_OFFER_SENT_TO_SELLER. Timeout / iptal seçenekleri. |
| **TRADE_OFFER_SENT_TO_SELLER** | ITEM_ESCROWED, CANCELLED_TIMEOUT, CANCELLED_SELLER, CANCELLED_BUYER, CANCELLED_ADMIN | Satıcı kabul → ITEM_ESCROWED. Satıcı red/counter → CANCELLED_SELLER. Timeout / iptal seçenekleri. |
| **ITEM_ESCROWED** | PAYMENT_RECEIVED, CANCELLED_TIMEOUT, CANCELLED_SELLER, CANCELLED_BUYER, CANCELLED_ADMIN | Ödeme doğrulandı → PAYMENT_RECEIVED. Timeout → CANCELLED_TIMEOUT (item iade). Satıcı iptal → CANCELLED_SELLER (item iade — ödeme öncesi, 02 §7). Alıcı iptal → CANCELLED_BUYER (item iade). |
| **PAYMENT_RECEIVED** | TRADE_OFFER_SENT_TO_BUYER, CANCELLED_ADMIN | Alıcıya trade offer gönderildi → TRADE_OFFER_SENT_TO_BUYER. Admin iptal → CANCELLED_ADMIN (item + ödeme iade). Not: Ödeme sonrası kullanıcı tek taraflı iptal edemez (02 §7), sadece admin yetkisiyle. |
| **TRADE_OFFER_SENT_TO_BUYER** | ITEM_DELIVERED, CANCELLED_TIMEOUT, CANCELLED_BUYER, CANCELLED_ADMIN | Alıcı kabul → ITEM_DELIVERED. Alıcı red/counter → CANCELLED_BUYER (item + ödeme iade). Timeout → CANCELLED_TIMEOUT (item + ödeme iade). |
| **ITEM_DELIVERED** | COMPLETED | Payout başarılı → COMPLETED. |
| **COMPLETED** | *(terminal — çıkış yok)* | — |
| **CANCELLED_TIMEOUT** | *(terminal — çıkış yok)* | — |
| **CANCELLED_SELLER** | *(terminal — çıkış yok)* | — |
| **CANCELLED_BUYER** | *(terminal — çıkış yok)* | — |
| **CANCELLED_ADMIN** | *(terminal — çıkış yok)* | — |

### 12.2 Özel Durumlar

| Durum | Kural |
|---|---|
| **EMERGENCY_HOLD** | State değil, overlay mekanizma. Herhangi bir aktif state üzerine uygulanır (`IsOnHold = true`). Hold aktifken: state geçişi engellenir, timeout durur. Hold kaldırılınca: RESUME (kaldığı yerden devam) veya CANCEL (iptal + iade). ITEM_DELIVERED state'inde hold'dan yalnızca RESUME ile çıkılır — CANCEL uygulanamaz (item alıcıda). |
| **FLAGGED** | Pre-create flag. İşlem CREATED öncesi durdurulur. Timeout başlamaz. Milestone field'ları (BuyerId, deadline'lar) NULL kalır. Admin onayı ile CREATED'a geçişte initialization yapılır. |
| **Dispute** | Ayrı state değil, bayrak. İşlem mevcut state'inde kalır, dispute ayrı entity olarak takip edilir. State geçişini etkilemez. |
| **ITEM_DELIVERED → CANCELLED_*** | Bu geçiş yoktur. Item alıcıya teslim edilmiş olduğundan standart iptal/iade uygulanamaz. Exceptional durumlar admin tarafından manuel süreçle çözülür. |
| **Ödeme sonrası tek taraflı iptal** | PAYMENT_RECEIVED ve sonrası state'lerde satıcı/alıcı tek taraflı iptal edemez. Sadece admin CANCELLED_ADMIN yapabilir (item + ödeme iade ile). |

### 12.3 Doğrulama Kontrol Listesi

Her state için reviewer agent şunları doğrular:

| # | Kontrol |
|---|---|
| 1 | §12.1'deki izin verilen geçişlerin tamamı çalışıyor |
| 2 | §12.1'de listelenmeyen her geçiş denemesi reject ediliyor (400/409) |
| 3 | Terminal state'lerden herhangi bir geçiş denemesi reject ediliyor |
| 4 | Her başarılı geçişte TransactionHistory kaydı oluşuyor (önceki state, yeni state, timestamp, aktör) |
| 5 | EMERGENCY_HOLD aktifken geçiş denemesi reject ediliyor |
| 6 | FLAGGED state'te otomatik geçiş tetiklenmiyor (timeout dahil) |
| 7 | Concurrent geçiş denemelerinde sadece biri başarılı oluyor (RowVersion) |
| 8 | İade gerektiren iptal geçişlerinde doğru iade davranışı tetikleniyor (state'e göre: sadece item, sadece ödeme, veya her ikisi) |

---

## 13. Ek B — Proje-Spesifik Kontroller

Bu ek, Skinora'nın escrow/kripto/Steam yapısından kaynaklanan ve standart doğrulama seviyelerine sığmayan çapraz kontrolleri tanımlar. Bu kontroller §4 matrisindeki VAL maddelerini tamamlar.

### 13.1 Idempotency Doğrulama

Aynı aksiyon iki kez tetiklendiğinde sistemin güvenli davranması doğrulanır.

| Senaryo | Beklenen Davranış | İlişkili VAL |
|---|---|---|
| Aynı Steam trade offer callback'i iki kez gelir | İlk işlenir, ikinci yok sayılır. Duplicate TradeOffer/state geçişi oluşmaz | VAL-C004, VAL-F001 |
| Aynı blockchain payment confirmation iki kez gelir | İlk işlenir, ikinci yok sayılır. Duplicate BlockchainTransaction oluşmaz | VAL-C004, VAL-F001 |
| Aynı payout komutu iki kez tetiklenir | İlk gönderilir, ikinci engellenir. Çift ödeme yapılmaz | VAL-D006, VAL-D007, VAL-F001 |
| Aynı bot assignment iki kez çalışır | İlk atanır, ikinci yok sayılır. Duplicate atama oluşmaz | VAL-D007 |
| Worker restart sonrası aynı job tekrar çalışır | Idempotent — aynı sonuç, side effect yok | VAL-F001, VAL-F002 |

### 13.2 Monetary Doğrulama

Para alanlarında "yaklaşık doğru" kabul edilmez. Aritmetik eşitlik kanıtlanmalıdır.

**Alan tanımları** (06 §3.5 ve §8.1'deki kanonik alan adlarına karşılık gelir):
- `BuyerFeeAmount`: Alıcının ödediği platform komisyonu (`ItemPrice × CommissionRate`, 06 §8.1 rounding kuralı)
- `SellerFeeAmount`: Satıcıdan kesilen tutar (mevcut modelde 0 — komisyon yalnızca alıcıdan; alan gelecek esneklik için mevcut)
- `TotalBuyerAmount`: Alıcının toplam ödeyeceği tutar (`ItemPrice + BuyerFeeAmount`)
- `SellerPayoutAmount`: Satıcıya gönderilecek tutar (`ItemPrice - SellerFeeAmount`)
- `GasFee`: Blockchain gas maliyeti (payout'ta komisyondan, iade'de tutardan düşülür)
- `ActualPayoutSent` / `ActualRefundSent`: Gas fee düşüldükten sonra gerçekte gönderilen tutar

| Kontrol | Formül | İlişkili VAL |
|---|---|---|
| Alıcı toplam tutarı | `ItemPrice + BuyerFeeAmount = TotalBuyerAmount` | VAL-D001 |
| Satıcı payout tutarı | `ItemPrice - SellerFeeAmount = SellerPayoutAmount` | VAL-D002 |
| Gas fee — payout | `SellerPayoutAmount - GasFee = ActualPayoutSent` (gas fee komisyondan karşılanır, koruma eşiği dahilinde) | VAL-A006 |
| Gas fee — iade | `RefundAmount - GasFee = ActualRefundSent` (gas fee tutardan düşülür) | VAL-A006 |
| Rounding kuralı | Tüm monetary hesaplamalarda 06 §8.1'deki rounding kuralı uygulanmış | VAL-D001, VAL-D002 |
| Minimum iade eşiği | Gas fee > iade tutarı durumunda iade yapılmaz, kayıt oluşur | VAL-A006 |
| Persisted vs calculated | DB'deki monetary field'lar hesaplanan değerlerle birebir eşleşir (drift yok) | VAL-D001, VAL-D002 |

### 13.3 Ownership / Authorization Doğrulama

| Kontrol | Beklenen Davranış | İlişkili VAL |
|---|---|---|
| Seller sadece kendi order'ında işlem yapar | Başka seller'ın order'ına erişim/aksiyon denemesi 403 döner | VAL-A024 |
| Buyer sadece kendi order'ını görür | Başka buyer'ın order'ına erişim denemesi 403 döner | VAL-A024 |
| Bot sadece atanmış order için aksiyon alır | Atanmamış order için trade offer gönderme engellenir | VAL-A025 |
| Admin yetkileri rol bazlı | Yetkisiz admin aksiyonu (ör: emergency hold yetkisi olmadan hold) 403 döner | VAL-A013, VAL-A014 |
| Internal/admin endpoint'ler public'e sızmaz | Public erişim denemesi 401/404 döner | VAL-A022 |
| Spoofed callback/forged request reddedilir | Geçersiz imza/token ile gelen webhook 401 döner | VAL-A023a, VAL-A023b |

### 13.4 Audit Trail Doğrulama

Bir order için sonradan aşağıdaki soruların cevaplanabilmesi doğrulanır.

| Soru | Kanıt Kaynağı | İlişkili VAL |
|---|---|---|
| Kim başlattı? | Transaction.SellerId + AuditLog | VAL-D004, VAL-D005 |
| Hangi bot işledi? | Transaction.AssignedBotId + TradeOffer kayıtları | VAL-D009 |
| Hangi offer seçildi? | TradeOffer.SteamTradeOfferId | VAL-E003 |
| Hangi tarihte payment alındı? | BlockchainTransaction.ConfirmedAt + TransactionHistory | VAL-D003, VAL-D004 |
| Hangi event completion'a götürdü? | TransactionHistory (son geçiş kaydı) + OutboxMessage | VAL-C008, VAL-D006 |
| Admin neden iptal etti? | AuditLog (aksiyon + sebep) | VAL-D005, VAL-A013 |
| Dispute sonucu ne oldu? | Dispute entity + AuditLog | VAL-D004 |
| Cüzdan adresi ne zaman değiştirildi? | AuditLog (güvenlik olayı) | VAL-D005, VAL-B012 |

### 13.5 State Bazlı İade Davranışı Doğrulama

İptal/timeout durumunda state'e göre doğru iade davranışının tetiklenmesi doğrulanır.

| İptal Anındaki State | Item İadesi | Ödeme İadesi | İlişkili VAL |
|---|---|---|---|
| CREATED (timeout) | Gerekmez (henüz transfer yok) | Gerekmez | VAL-B002 |
| CREATED (satıcı iptal) | Gerekmez (henüz transfer yok) | Gerekmez | VAL-B017 |
| ACCEPTED (timeout) | Gerekmez (henüz transfer yok) | Gerekmez | VAL-B003 |
| ACCEPTED (satıcı/alıcı iptal) | Gerekmez (henüz transfer yok) | Gerekmez | VAL-B017, VAL-B018 |
| TRADE_OFFER_SENT_TO_SELLER (timeout) | Gerekmez (offer iptal edilir) | Gerekmez | VAL-B003 |
| TRADE_OFFER_SENT_TO_SELLER (satıcı red/counter) | Gerekmez (offer iptal edilir) | Gerekmez | VAL-B019 |
| TRADE_OFFER_SENT_TO_SELLER (satıcı/alıcı iptal) | Gerekmez (offer iptal edilir) | Gerekmez | VAL-B017, VAL-B018 |
| ITEM_ESCROWED (timeout) | Satıcıya iade | Gerekmez (henüz ödeme yok) | VAL-B004 |
| ITEM_ESCROWED (satıcı/alıcı iptal) | Satıcıya iade | Gerekmez (henüz ödeme yok) | VAL-B017, VAL-B018 |
| PAYMENT_RECEIVED (admin iptal) | Satıcıya iade | Alıcıya iade | VAL-A013 |
| TRADE_OFFER_SENT_TO_BUYER (timeout) | Satıcıya iade (offer iptal) | Alıcıya iade | VAL-B005 |
| TRADE_OFFER_SENT_TO_BUYER (alıcı red/counter) | Satıcıya iade (offer iptal) | Alıcıya iade | VAL-B020 |
| ITEM_DELIVERED | İade uygulanamaz (item alıcıda) | İade uygulanamaz | §12.2 |
