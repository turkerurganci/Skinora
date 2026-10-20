# Skinora — Implementation Plan

**Versiyon: v1.18** | **Bağımlılıklar:** `02_PRODUCT_REQUIREMENTS.md`, `03_USER_FLOWS.md`, `04_UI_SPECS.md`, `05_TECHNICAL_ARCHITECTURE.md`, `06_DATA_MODEL.md`, `07_API_DESIGN.md`, `08_INTEGRATION_SPEC.md`, `09_CODING_GUIDELINES.md`, `10_MVP_SCOPE.md` | **Son güncelleme:** 2026-10-20 — plan faz başına bölündü (Docs/plan/), atıf haritası eklendi; içerik değişmedi. · Değişiklik geçmişi: [DOC_CHANGELOG.md](DOC_CHANGELOG.md#11_implementation_planmd)

**Atıf haritası (v1.18):** Plan faz başına bölündü; bir görevin kabul kriteri için tüm bu dosyayı değil yalnız ilgili faz dosyasını oku. Bölüm numaraları ve başlık metinleri taşındığı dosyada aynen korunur ("11 §F8", "11 §7.6", "11 T150" atıfları anlamını korur).

| Atıf | Dosya |
|---|---|
| §F0–F7 (T01–T140), §7.1–§7.5, §6.2 F7 gate notu | [`plan/11_F0-F7_ARCHIVE.md`](plan/11_F0-F7_ARCHIVE.md) |
| §F8 (T141–T160, K1–K9, "Txxx kararları", T159a, T159b), §7.6, FK8 (§6.2 F8 satırı) | [`plan/11_F8.md`](plan/11_F8.md) |
| §F9 (T161–T175) | [`plan/11_F9.md`](plan/11_F9.md) |
| §1–§4, §5 girişi, §6 (faz kapıları özeti), §7 girişi, §8, §9 | bu dosya |

---

## 1. Amaç ve Kapsam

Bu doküman, Skinora escrow platformunun MVP implementasyonunu sıralı, bağımlılıklı ve test beklentili task'lara böler. Her task'ın ne olduğunu, neye bağımlı olduğunu, hangi dokümanlarla yapılacağını ve nasıl kabul edileceğini tanımlar.

**Kapsam:** 10_MVP_SCOPE §2'de "dahil" olarak tanımlanan tüm özellikler (MVP-IN-001 – MVP-IN-069). "Hariç" özellikler §9 Post-MVP Parkı'nda kayıt altındadır.

**Denetim mekanizması:**
- **Traceability Matrix** (§7): Her doküman çıktısı en az bir task'a eşlenmiştir. Eşlenmeyen çıktı = eksik task.
- **Boşluk kontrolü**: Çift yönlü — ileriye (çıktı→task) ve geriye (task→çıktı).
- **MVP filtresi**: 10_MVP_SCOPE "hariç" öğeler task listesinden çıkarılmış, §9'da kayıt altındadır.

---

## 2. Kaynak Envanteri Özeti

Aşağıdaki dokümanlardan implementasyon öğeleri çıkarılmıştır:

| Doküman | Öğe Sayısı | Kapsam |
|---|---|---|
| 02 Product Requirements | 23 section | İş kuralları, kısıtlamalar, edge case'ler |
| 03 User Flows | 336 öğe | Akış adımları, dallanmalar, validasyonlar, bildirimler |
| 04 UI Specs | 199 öğe | 25 ekran, 17 ortak bileşen, 8 modal, 20 form, 62 state varyantı, 18 validasyon |
| 05 Technical Architecture | 113 öğe | Servisler, altyapı, middleware, job'lar, güvenlik, monitoring |
| 06 Data Model | 211 öğe | 25 entity, 23 enum, 56 constraint, 35 index, 3 seed data |
| 07 API Design | 299 öğe | 67 endpoint, 103 DTO, 40 validasyon, 16 SignalR event, 58 hata tanımı |
| 08 Integration Spec | 157 öğe | Entegrasyonlar, API çağrıları, webhook'lar, hata yönetimi |
| 09 Coding Guidelines | 66 öğe | Proje iskeleti, middleware, pattern, EF Core config, CI/CD, test altyapısı |
| 10 MVP Scope | 87 öğe | 69 dahil, 18 hariç |

> Detaylı envanter listeleri bu dokümanın hazırlanmasında kullanılmış olup doküman içine dahil edilmemiştir. Traceability Matrix (§7) her task'ın hangi kaynak öğeleri kapsadığını gösterir.

---

## 3. Faz Tanımları

Tüm task'lar 10 faza ayrılmıştır (F0–F9). Her faz bir önceki faz tamamlanmadan başlamaz (§3.1'de belirtilen kontrollü paralellik istisnaları hariç).

| Faz | Ad | Kapsam | Task Aralığı |
|---|---|---|---|
| F0 | Proje İskeleti | Solution yapısı, Docker, middleware, logging, CI/CD, test altyapısı, shared kernel | T01–T16 |
| F1 | Veri Katmanı | Entity'ler, enum'lar, constraint'ler, index'ler, migration, seed data | T17–T28 |
| F2 | Çekirdek Servisler | Auth, kullanıcı yönetimi, bildirim altyapısı, admin altyapısı, audit log | T29–T43 |
| F3 | İş Mantığı | Escrow akışı, state machine, timeout, komisyon, fraud, dispute, SignalR, retention | T44–T63b |
| F4 | Entegrasyonlar | Steam sidecar, blockchain sidecar, email, Telegram, Discord, sanctions, geo-block | T64–T83 |
| F5 | Kullanıcı Arayüzü | Tüm ekranlar, bileşenler, formlar, state varyantları, responsive, i18n | T84–T106 |
| F6 | Uçtan Uca Doğrulama | E2E testler, senaryo testleri, regresyon | T107–T114 |
| F7 | P2P Geçişi | Item custody'nin kaldırılması, teslimat doğrulama, bot katmanının emekliye ayrılması | T115–T140 |
| F8 | Çok Ağlı Ödeme | TRON'un yanına Ethereum (USDT ERC-20): ağ boyutu, Ethereum servisi, ağ seçimi, ağ ücreti politikası; ağdan bağımsız iki açılış hatası (HD yolu, tutar) | T141–T160 |
| F9 | Canlıya Çıkış | Sunucu, HTTPS, sır ve veritabanı yedeği, bildirimler, MASAK akışı, hukuk metni, kurulum, prova, mainnet pilotu, çıkış kapısı | T161–T175 |

> **F8 neden var (2026-10-17):** TRON'da bir satışın ağ ücreti platforma 5–7 $ (kilitle ~0,67 $ ama ~35 bin $ TRX bağlanıyor); %2 komisyonla küçük satışlar zarar ediyor. Ucuz ağ araştırması: Türkiye'deki 9 borsanın hepsinde bulunan yalnız TRON ve Ethereum var; Ethereum'da satış başı ağ ücreti ~0,05 $. TRON kalır, Ethereum eklenir, açılış ikisi hazır olunca (ayrıntı §F8). **F9** canlıya çıkış listesidir ve iki ağı birlikte kurar.

> **F7 neden var:** MVP (F0–F6) custodial bot escrow modeliyle tamamlandı. Steam Trade Protection (16.07.2025) ve trade cooldown reworku (02.2026) sonrası item bir envantere trade ile girdiğinde 7 gün transfer edilemiyor; bot item'ı emanete aldığı anda alıcıya gönderemez. Çift-trade modeli bu kural altında çalışamadığı için item custody kaldırılıp P2P modeline geçilmektedir (02 §2.1). Para escrow'u değişmemektedir.

### 3.1 Faz Bağımlılık Diyagramı

```
F0 → F1 → F2 → F3 → F4 → F5 → F6
                 ↘         ↗
                  F4 (kısmen F2 ile paralel başlayabilir — sidecar iskeletleri F0'da kurulur)
```

> **Sonraki fazlar:** F6 → F7 → F8 → F9 sıralıdır (F8 içindeki paralellik §F8 "Sıra" notunda).

> **Not:** F4'ün sidecar iskeletleri F0'da kurulur (T14, T15). F4'ün iş mantığı task'ları F3'e bağımlıdır. F5 frontend task'ları F2+F3+F4 API'lerine bağımlıdır.

---

## 4. Hata Sınıflandırması ve Çözüm Akışı

### 4.1 Hata Seviyeleri

| Seviye | Tanım | Örnek |
|---|---|---|
| S1 — Sapma | Task tamamlandı ama dokümanla uyumsuz | Endpoint path dokümanla eşleşmiyor, entity field eksik, validasyon kuralı atlanmış |
| S2 — Kırılma | Bir task başka bir task'ın çıktısını bozuyor | Migration değişikliği mevcut servisi kırıyor, API sözleşmesi değişmiş ama consumer güncellenmemiş |
| S3 — Eksik | Traceability'de eşlenmiş bir öğe implement edilmemiş | Bir iş kuralı hiçbir yerde uygulanmamış, bir endpoint tanımlı ama kodu yok |

### 4.2 Tespit Mekanizması

| Seviye | Tespit Yöntemi | Zamanlama |
|---|---|---|
| Tümü | Task kabul kriterleri + doğrulama kontrol listesi | Her task tamamlandığında |
| S1, S3 | Traceability matrix "implemented" kolonu kontrolü | Her faz sonunda (gate check) |
| S2 | Regresyon testi (önceki fazların testleri tekrar çalıştırılır) | Her faz sonunda (gate check) |
| S3 | Boşluk taraması (eşlenip de implement edilmeyen öğeler) | Her faz sonunda (gate check) |

> **Not:** Doğrulama kontrol listesi, kodu yazan agent'tan farklı bir context'te çalıştırılır. Detaylı kurallar, VAL maddeleri, kanıt standardı ve süreç tanımı `12_VALIDATION_PROTOCOL.md`'de tanımlıdır.

### 4.3 Çözüm Akışı

```
Hata tespit edildi
  → Seviye belirlenir (S1/S2/S3)
  → Etki analizi: başka hangi task'lar etkileniyor?
  → Düzeltme task'ı oluşturulur (mevcut faza eklenir)
  → Düzeltme task'ı tamamlanır
  → Etkilenen task'ların doğrulama kontrol listeleri tekrar çalıştırılır
  → Gate check tekrar değerlendirilir
```

**Kritik kural:** Düzeltme task'ı bir sonraki faza ertelenmez — hatanın oluştuğu fazda çözülür.

### 4.4 Tıkanma Stratejisi

Bir task ilerleyemiyorsa:
1. Task daha küçük alt task'lara bölünür
2. Alt task'ların bağımlılıkları güncellenir
3. Faz kapısı (gate check) yeniden değerlendirilir
4. Tıkanmanın sebebi (eksik bilgi, teknik kısıt, dış bağımlılık) kayıt altına alınır

---

## 5. Task Listesi

### Task Yapısı

Her task aşağıdaki bilgileri içerir:

```
Task TXX: [Task adı]
  Bağımlılık: [Önceden tamamlanmış olması gereken task'lar]
  Dokümanlar: [Agent'a verilecek dosyalar]
  Kabul kriterleri: [Ne olduğunda "tamam"]
  Test beklentisi: [Unit / Integration / Contract / Yok]
  Doğrulama kontrol listesi: [Cross-check'te neye bakılacak]
```

---

> **§F0–F7 (T01–T140) görev listeleri** taşındı → [`plan/11_F0-F7_ARCHIVE.md`](plan/11_F0-F7_ARCHIVE.md)

> **§F8 — Çok Ağlı Ödeme: TRON + Ethereum (T141–T160)** taşındı → [`plan/11_F8.md`](plan/11_F8.md)

> **§F9 — Canlıya Çıkış (T161–T175)** taşındı → [`plan/11_F9.md`](plan/11_F9.md)

---

## 6. Faz Geçiş Kapıları (Gate Check)

Her faz tamamlandığında aşağıdaki kontroller yapılır. Tümü geçmedikçe bir sonraki faza geçilmez.

### 6.1 Genel Gate Check (tüm fazlarda)

| # | Kontrol | Açıklama |
|---|---|---|
| G1 | Task tamamlanma | Bu fazdaki tüm task'lar "tamamlandı" durumunda mı? |
| G2 | Kabul kriterleri | Her task'ın kabul kriterleri karşılandı mı? |
| G3 | Doğrulama kontrol listesi | Her task'ın kontrol listesi geçti mi? |
| G4 | Test | Bu fazdaki tüm task'ların test beklentileri karşılandı mı? |
| G5 | CI | CI pipeline (build + test) yeşil mi? |
| G6 | Regresyon | Önceki fazların testleri hâlâ geçiyor mu? |
| G7 | Boşluk | Traceability matrix'te bu faza ait eşlenip implement edilmeyen öğe var mı? |

### 6.2 Faz-Spesifik Kontroller

| Faz | Ek Kontrol |
|---|---|
| F0 | Docker-compose up ile tüm servisler ayağa kalkıyor mu? Monitoring dashboard'lar çalışıyor mu? |
| F1 | Migration temiz DB'ye hatasız uygulanıyor mu? Seed data doğru mu? Tüm constraint'ler test edildi mi? |
| F2 | Auth akışı uçtan uca çalışıyor mu? Admin RBAC doğru mu? |
| F3 | State machine tüm geçişleri doğru mu? Timeout scheduling doğru mu? Finansal hesaplamalar boundary value ile test edildi mi? |
| F4 | Sidecar'lar health check'te yeşil mi? Webhook iletişimi çift yönlü çalışıyor mu? |
| F5 | Tüm ekranlar 3 breakpoint'te doğru mu? 4 dil çalışıyor mu? SignalR real-time çalışıyor mu? |
| F6 | Tüm E2E senaryoları geçiyor mu? Staging ortamında test edildi mi? |
| F7 | **FK1** — P2P ileri yolun her geçişinin bir üretim üreticisi var mı? (`BuyerAccept` · `SellerConfirmReady` · `ConfirmPayment` · `DeliverItem` · `Complete` · `DeliveryReversed` · `AdminResolveRefund`) · **FK2** — custody yüzeyi gerçekten kalktı mı? (prod kaynakta `PlatformSteamBot`/`BotRecoveryItem`/`TradeOffer` ve emekli enum değerleri sıfır; `Webhook.SteamSharedSecret` prod konfigde yok; `PermissionCatalog` 12; FE'de `/admin/steam` rotası yok; migration provasında emekli tablolar oluşmuyor) · **FK3** — 06 §2.13 bildirim kataloğundaki her tipin bir üretim üreticisi var mı? · **FK4** — her domain event'in ≥1 yayıncısı **ve** ≥1 tüketicisi var mı? |
| F8 | **FK8-1 … FK8-6** — ayrıntı → [`plan/11_F8.md`](plan/11_F8.md) (§6.2 F8 satırı) |
| F9 | 12 §6.3 çıkış kapısı + mainnet pilotu (iki ağ, soğuk aktarım dahil) + yedek geri yükleme provası (veritabanı ve anahtarlar) |

> **F7 satırı neden bu içerikle yazıldı** (F7 Gate Check, bulgu F7-N3) notu taşındı → [`plan/11_F0-F7_ARCHIVE.md`](plan/11_F0-F7_ARCHIVE.md)

---

## 7. Traceability Matrix

Bu bölüm her task'ın hangi kaynak doküman öğelerini kapsadığını gösterir. "Implemented" kolonu, task tamamlandığında ✓ olarak işaretlenir.

> **Not:** Öğe ID'leri envanter taramasından alınmıştır. Her satır "bu öğe şu task'ta implement ediliyor" ilişkisini gösterir. Eşlenmeyen öğe = eksik task.

> **§7.1–§7.5** (Veri Modeli / API / Entegrasyon / UI eşlemeleri + F7 kaynak öğeleri) taşındı → [`plan/11_F0-F7_ARCHIVE.md`](plan/11_F0-F7_ARCHIVE.md)

> **§7.6 F8 — Çok ağlı ödeme kaynak öğeleri** taşındı → [`plan/11_F8.md`](plan/11_F8.md)

---

## 8. Boşluk Raporu

Kaynak envanteri taraması ve traceability matrix eşlemesi sonucunda tespit edilen boşluklar:

| # | Açıklama | Durum |
|---|---|---|
| — | Tüm kaynak öğeleri en az bir task'a eşlenmiştir | ✓ Boşluk yok |
| F-INVITE-01 | F5 Gate Check'te tespit edildi: S07 OPEN_LINK `/invite/:token` davet-tüketim rotası uçtan uca eksikti (FE rota + backend token-çözümleme endpoint'i). §7.4 S07→T90/T91/T92 eşlemesinin alt-özelliği; F3'te "T45 OPEN_LINK invitation path → backlog" forward-deferred edilmişti. | ✓ Kapatıldı (F5 gate, 2026-06-13): backend `GET /transactions/by-invite/:token` + FE `/invite/[token]` + 07 §7.5a + 6 test. Detay [`GATE_CHECK_F5.md`](CHECKPOINT_REPORTS/GATE_CHECK_F5.md). |

> **Not:** Bu bölüm ilk yazılım sırasında boştur. İmplementasyon sürecinde yeni boşluklar tespit edilirse buraya eklenir ve ilgili düzeltme task'ları oluşturulur.

---

## 9. Post-MVP Parkı

Aşağıdaki özellikler MVP kapsamı dışıdır (10_MVP_SCOPE §3). Task listesine dahil edilmemiştir ancak kayıp olmaması için kayıt altındadır.

| ID | Özellik | Kaynak |
|---|---|---|
| MVP-OUT-001 | Barter (item-item takas) | 10 §3.1 |
| MVP-OUT-002 | Çoklu item işlemleri | 10 §3.1 |
| MVP-OUT-003 | Trade lock'lu item desteği | 10 §3.1 |
| MVP-OUT-004 | Diğer Steam oyunları (Dota 2, TF2, Rust) | 10 §3.1 |
| MVP-OUT-005 | Platform cüzdanı (bakiye yükleme) | 10 §3.2 |
| MVP-OUT-006 | Ek blockchain ağları — **Ethereum F8'e alındı (2026-10-17, K1)**; Arbitrum/Avalanche açılış sonrası ayrı karar, diğerleri kapsam dışı | 10 §3.2 |
| MVP-OUT-007 | Fiat ödeme desteği | 10 §3.2 |
| MVP-OUT-008 | Mobil uygulama | 10 §3.3 |
| MVP-OUT-009 | Kullanıcı yorum/değerlendirme sistemi | 10 §3.3 |
| MVP-OUT-010 | Kullanıcıya piyasa fiyatı gösterimi | 10 §3.3 |
| MVP-OUT-011 | Premium üyelik | 10 §3.4 |
| MVP-OUT-012 | Ek gelir kanalları (komisyon dışı) | 10 §3.4 |
| MVP-OUT-013 | KYC | 10 §3.5 |
| MVP-OUT-014 | Admin eskalasyon süreci detayları | 10 §3.6 |
| MVP-OUT-015 | Kullanıcı sözleşmesi içeriği (metin yazılmadı) | 10 §3.6 |
| MVP-OUT-016 | Bildirim mesaj içerikleri — final/polished metinler (MVP'de placeholder metinler kullanılır, T37) | 10 §3.6 |
| MVP-OUT-017 | Platform Steam hesapları yönetim detayları | 10 §3.6 |
| MVP-OUT-018 | Steam Mobile Authenticator kontrol mekanizması detayları | 10 §3.6 |

---

*Skinora — Implementation Plan v1.7*
