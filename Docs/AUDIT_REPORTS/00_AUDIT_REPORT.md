# Audit Raporu — 00_PROJECT_METHODOLOGY.md

**Tarih:** 2026-03-15
**Hedef:** 00 — Project Methodology Playbook (v0.2)
**Baglam:** 01, 02, 03, 04, 05, 06, 10, PRODUCT_DISCOVERY_STATUS
**Odak:** Tam denetim

---

## Envanter Ozeti

| Kaynak | Toplam Oge | ✓ | ⚠ | ✗ |
|---|---|---|---|---|
| 10 (MVP Scope) | 8 | 7 | 1 | 0 |
| 01 (Project Vision) | 5 | 5 | 0 | 0 |
| 02 (Product Requirements) | 6 | 6 | 0 | 0 |
| 03 (User Flows) | 5 | 5 | 0 | 0 |
| 04 (UI Specs) | 4 | 3 | 1 | 0 |
| 05 (Technical Architecture) | 5 | 5 | 0 | 0 |
| 06 (Data Model) | 4 | 4 | 0 | 0 |
| Discovery Status | 4 | 4 | 0 | 0 |
| Hedef (ic) | 32 | 27 | 4 | 1 |
| **Toplam** | **73** | **66** | **6** | **1** |

---

## Envanter ve Eslestirme Detayi

### Envanter — 10_MVP_SCOPE

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 1 | 10-§1-01 | MVP amaci: temel escrow akisini kanitlamak | §1 MVP Amaci | §2.1 Amac | ✓ |
| 2 | 10-§2.1-01 | Temel escrow akisi (8 adim) | §2.1 | §2.5 Ciktilar (02 referansi) | ✓ |
| 3 | 10-§2.15-01 | 4 dil destegi (EN, ZH, ES, TR) | §2.15 | — | ✓ |
| 4 | 10-§3-01 | MVP disinda kalan ozellikler listesi | §3 | §2.6 Prensipler | ✓ |
| 5 | 10-§4-01 | MVP sinirlari ve kisitlamalar | §4 | §2.6 Prensipler | ✓ |
| 6 | 10-§5-01 | Basari kriterleri (buyume, guvenilirlik, gelir, guven) | §5 | §2.1 Amac (dolayli) | ✓ |
| 7 | 10-§6-01 | MVP sonrasi yol haritasi | §6 | §2.6 Prensipler | ✓ |
| 8 | 10-§3.6-01 | Detaylari sonraya birakilan konular (5 konu) | §3.6 | §2.6 Prensipler ("belirsizlik") | ⚠ |

**Toplam: 8 oge (7 ✓, 1 ⚠, 0 ✗)**

---

### Envanter — 01_PROJECT_VISION

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 1 | 01-§1-01 | Skinora marketplace degil, escrow servisi | §1 Urun Ozeti | §2.1 Amac | ✓ |
| 2 | 01-§3.3-01 | 4 aktor: Satici, Alici, Platform, Admin | §3.3 Aktorler | §3.2 Yaklasim | ✓ |
| 3 | 01-§5.1-01 | Gelir modeli: %2 komisyon | §5.1 | §2.5 Ciktilar | ✓ |
| 4 | 01-§6-01 | Kisa/orta/uzun vade vizyon | §6 | §2.6 Prensipler | ✓ |
| 5 | 01-§8-01 | Temel ilkeler (guvenlik, otomasyon, adalet, seffaflik, tarafsizlik) | §8 | §2.6 Prensipler | ✓ |

**Toplam: 5 oge (5 ✓, 0 ⚠, 0 ✗)**

---

### Envanter — 02_PRODUCT_REQUIREMENTS

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 1 | 02-§2-01 | 8 adimli islem akisi | §2 | §2.5 Ciktilar | ✓ |
| 2 | 02-§3-01 | Timeout yapisi (4 adim icin ayri timeout) | §3 | §2.3 Soru Siralamasi (#3) | ✓ |
| 3 | 02-§4-01 | Odeme gereksinimleri (kripto, stablecoin, TRC-20) | §4 | §2.3 Soru Siralamasi (#1) | ✓ |
| 4 | 02-§10-01 | Dispute gereksinimleri (3 itiraz turu) | §10 | §2.3 Soru Siralamasi (#4) | ✓ |
| 5 | 02-§14-01 | Fraud/abuse onleme gereksinimleri | §14 | §2.3 Soru Siralamasi (#13) | ✓ |
| 6 | 02-§16-01 | Admin paneli gereksinimleri (roller, yetkiler, parametreler) | §16 | §2.3 Soru Siralamasi (dolayli) | ✓ |

**Toplam: 6 oge (6 ✓, 0 ⚠, 0 ✗)**

---

### Envanter — 03_USER_FLOWS

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 1 | 03-§1.2-01 | 13 islem durumu (state machine) | §1.2 | §3.6 Ogrenimler | ✓ |
| 2 | 03-§2-01 | Satici akislari (giris, islem, emanet, odeme, iptal) | §2 | §3.2 Yaklasim | ✓ |
| 3 | 03-§4-01 | Timeout akislari (4 senaryo + uyari) | §4 | §3.6 Ogrenimler | ✓ |
| 4 | 03-§6-01 | Dispute akislari (3 tur + eskalasyon) | §6 | §3.3 Olusturulacak Akislar | ✓ |
| 5 | 03-§12-01 | Bildirim ozeti (satici, alici, admin) | §12 | §3.6 Ogrenimler | ✓ |

**Toplam: 5 oge (5 ✓, 0 ⚠, 0 ✗)**

---

### Envanter — 04_UI_SPECS

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 1 | 04-§2-01 | 19 ekran envanteri (3 genel, 7 kullanici, 9 admin) | §2 | §4.6 Ogrenimler | ✓ |
| 2 | 04-§3-01 | Traceability Matrix (ileri + geri izlenebilirlik) | §3 | §4.3 Yaklasim | ✓ |
| 3 | 04-§4-01 | Ekran navigasyon haritasi | §4 | §4.6 Ogrenimler | ⚠ |
| 4 | 04-§5-01 | Ortak bilesen kutuphanesi (status badge, modal, countdown) | §5 | §4.6 Ogrenimler | ✓ |

**Toplam: 4 oge (3 ✓, 1 ⚠, 0 ✗)**

---

### Envanter — 05_TECHNICAL_ARCHITECTURE

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 1 | 05-§1.1-01 | Moduler Monolith mimari yaklasimi | §1.1 | §5.3 Yaklasim | ✓ |
| 2 | 05-§2.1-01 | Teknoloji stack'i (.NET 8, Next.js, SQL Server, Redis, EF Core, Hangfire, SignalR) | §2.1 | §5.2 Rol | ✓ |
| 3 | 05-§2.1-02 | Sidecar pattern (Steam/blockchain icin Node.js) | §2 (sidecar) | §5.5 Ogrenimler | ✓ |
| 4 | 05-§2.1-03 | Event-driven yaklasim (Outbox Pattern) | §2 | §5.5 Ogrenimler | ✓ |
| 5 | 05-§2.1-04 | Monitoring stack (ucretsiz tercih) | §2 | §5.5 Ogrenimler | ✓ |

**Toplam: 5 oge (5 ✓, 0 ⚠, 0 ✗)**

---

### Envanter — 06_DATA_MODEL

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 1 | 06-§1.1-01 | 20 entity envanteri | §1.1 | §6.3 Ciktilar | ✓ |
| 2 | 06-§2.1-01 | TransactionStatus enum (13 durum) | §2.1 | §6.4 Ogrenimler | ✓ |
| 3 | 06-§7-01 | Traceability Matrix (gereksinim → entity esleme) | §7 | §6.2 Yaklasim | ✓ |
| 4 | 06-§1.3-01 | Silme stratejisi (soft delete, asla silinmez, retention) | §1.3 | §6.4 Ogrenimler | ✓ |

**Toplam: 4 oge (4 ✓, 0 ⚠, 0 ✗)**

---

### Envanter — PRODUCT_DISCOVERY_STATUS

| # | ID | Oge Ozeti | Kaynak Bolum | Hedef Bolum | Durum |
|---|---|---|---|---|---|
| 1 | DS-§1-01 | Dokuman durum tablosu (tamamlanan ve baslanmamis) | §1 | §1 Genel Yol Haritasi | ✓ |
| 2 | DS-§9-01 | Sonraki adim: API Tasarimi (07) | §9 | §7 Asama 6 | ✓ |
| 3 | DS-§10-01 | 5 tamamlanmis checkpoint | §10 | — (00'da checkpoint kavrami yok ama surec izleniyor) | ✓ |
| 4 | DS-§8-01 | Detaylandirilacak konular (5 konu) | §8 | §2.6 Prensipler | ✓ |

**Toplam: 4 oge (4 ✓, 0 ⚠, 0 ✗)**

---

### Envanter — Hedef (00_PROJECT_METHODOLOGY ic envanter)

| # | ID | Oge Ozeti | Kaynak Bolum | Durum |
|---|---|---|---|---|
| 1 | 00-§1-01 | Genel Yol Haritasi tablosu (12 asama, 12 dokuman) | §1 | ✓ |
| 2 | 00-§1-02 | Dokuman numaralama ve isimlendirme tutarliligi | §1 | ✓ |
| 3 | 00-§2.1-01 | Product Discovery amaci (8 soru) | §2.1 | ✓ |
| 4 | 00-§2.2-01 | Workshop formati yaklasimi | §2.2 | ✓ |
| 5 | 00-§2.2-02 | Soru bazli ilerleme kurali | §2.2 | ✓ |
| 6 | 00-§2.2-03 | Kararlari zincirleme baglama | §2.2 | ✓ |
| 7 | 00-§2.2-04 | Edge case'leri aninda ele alma | §2.2 | ✓ |
| 8 | 00-§2.2-05 | Teknik detaya girmeme kurali | §2.2 | ✓ |
| 9 | 00-§2.2-06 | Inkremental dokumantasyon | §2.2 | ✓ |
| 10 | 00-§2.3-01 | Soru siralamasi rehberi (16 konu grubu) | §2.3 | ✓ |
| 11 | 00-§2.4-01 | Karar alma akisi (6 adim) | §2.4 | ✓ |
| 12 | 00-§2.5-01 | Ciktilar tablosu (4 dokuman) | §2.5 | ✓ |
| 13 | 00-§2.6-01 | Product Discovery prensipleri (5 madde) | §2.6 | ✓ |
| 14 | 00-§2.7-01 | Product Discovery ogrenimleri (5 madde) | §2.7 | ✓ |
| 15 | 00-§3.3-01 | Olusturulacak akislar tablosu (6 kategori) | §3.3 | ✓ |
| 16 | 00-§3.6-01 | Kullanici akislari ogrenimleri (6 madde) — "section 4.1" referansi | §3.6 | ⚠ |
| 17 | 00-§4.2-01 | UI/UX asama rolu (Senior Product Designer / UX Architect) | §4.2 | ✓ |
| 18 | 00-§4.3-01 | UI yaklasim (ekran bazli, akislara sadik, wireframe, traceability) | §4.3 | ✓ |
| 19 | 00-§4.6-01 | UI ogrenimleri (7 madde) — "section 8" referansi | §4.6 | ⚠ |
| 20 | 00-§5.2-01 | Teknik mimari rolu (Senior Software Architect) | §5.2 | ✓ |
| 21 | 00-§5.3-01 | Teknik mimari yaklasimi (gereksinimlerden turetme, basitlikten baslama) | §5.3 | ✓ |
| 22 | 00-§5.5-01 | Teknik mimari ogrenimleri (5 madde) | §5.5 | ✓ |
| 23 | 00-§6.2-01 | Veri modeli yaklasimi (gereksinimlerden turetme, normalizasyon, traceability) | §6.2 | ✓ |
| 24 | 00-§6.4-01 | Veri modeli ogrenimleri (7 madde) | §6.4 | ✓ |
| 25 | 00-§7-01 | API Tasarimi asama tanimi | §7 | ✓ |
| 26 | 00-§8-01 | Entegrasyon Spesifikasyonlari asama tanimi | §8 | ✓ |
| 27 | 00-§9-01 | Kodlama Kilavuzu asama tanimi | §9 | ✓ |
| 28 | 00-§10-01 | Implementation Plani asama tanimi | §10 | ✓ |
| 29 | 00-§11-01 | Dogrulama Protokolu asama tanimi | §11 | ✓ |
| 30 | 00-§12-01 | Genel Prensipler (surec tikanmasi, traceability, belirsizlik vb.) | §12 | ✓ |
| 31 | 00-§13-01 | Agent'a is verme yaklasimi (3 alt bolum) | §13 | ✓ |
| 32 | 00-§3.2-01 | Kullanici akislari yaklasiminda "operasyonel akislar" kategorisi | §3.3 | ✗ |

**Toplam: 32 oge (27 ✓, 4 ⚠, 1 ✗)**

---

## Bulgular

| # | Envanter ID | Tur | Seviye | Bulgu | Oneri |
|---|---|---|---|---|---|
| 1 | 00-§4.6-01 | Tutarsizlik | High | §4.6 Ogrenimler'de "Ekran navigasyon haritasi (section 8) spec'in sonunda degil basinda yer alsaydi baglami daha hizli kurardi" yazilmis. Ancak 04_UI_SPECS.md'nin gercek yapisinda section 8 "Admin Ekranlari"dir, "Ekran Navigasyon Haritasi" ise section 4'tur. Ogrenimde belirtilen iyilestirme fiilen 04'te zaten uygulanmis (navigasyon haritasi section 4'e alinmis) ancak 00'daki referans guncellenmemis. | §4.6'daki "(section 8)" ifadesi "(section 4)" olarak duzeltilmeli ve ogrenim iyilestirmenin uygulandigini yansitmali |
| 2 | 00-§3.2-01 | GAP | Medium | §3.3 "Olusturulacak Akislar" tablosunda "Operasyonel akislar: Downtime, bakim senaryolari" kategorisi listeleniyor. Ancak §3.2 Yaklasim bolumunde "durum makinesi mantigi" ve "bildirim entegrasyonu" yaklasim olarak tanimlanirken, operasyonel akislar icin bir yaklasim tanimlanmamis. Diger taraftan 03_USER_FLOWS.md'de section 11'de downtime akislari basarili sekilde tanimlanmis — yani fiilen uygulanmis ama 00'da yaklasim eksik. | §3.2 Yaklasim'a operasyonel akislar (downtime, bakim) icin bir yaklasim ilkesi eklenebilir. Ancak fiilen surec basarili tamamlanmis, bu daha cok dokumantasyon eksikligi. |
| 3 | 00-§3.6-01 | Tutarsizlik | Low | §3.6 Ogrenimler'de "Timeout kurallarini aktor akislarina gommek yerine ayri bir bolumde toplamak (section 4.1)" ifadesi var. 03_USER_FLOWS.md'de section 4.1 spesifik olarak "Alici Kabul Timeout'u (Adim 2)"dur — dogru referans section 4'tur (tum Timeout Akislari bolumu). Kucuk bir referans hatasi, anlam bozulmuyor. | "(section 4.1)" yerine "(section 4)" yazilmali |
| 4 | 10-§3.6-01 | Kismi | Low | MVP Scope §3.6'da "Detaylari sonraya birakilan 5 konu" listelenmis. 00 §2.6 Prensipler'de "belirsizlik" kavramina deginiliyor ama bu konularin spesifik olarak nerede izlenecegi (PRODUCT_DISCOVERY_STATUS §8) 00'da belirtilmemiyor. Discovery Status'ta izleniyor, ancak 00'da bu izleme mekanizmasina acik referans yok. | Duzeltme gerektirmez — izleme mekanizmasi PRODUCT_DISCOVERY_STATUS'ta zaten isliyor. Farkindelik yeterli. |
| 5 | 04-§4-01 | Kismi | Medium | 00 §4.6 Ogrenimler'de navigasyon haritasinin "spec'in sonunda degil basinda yer alsa" onerisinin fiilen 04'te uygulandigini (section 4 olarak) yansitmiyor. Ogrenim gecersiz/guncel degil bilgi tasimakta. | Bu bulgu #1 ile birlikte cozulur — ogrenim metninin guncellenmesi yeterli. |
| 6 | 00-§1-01 | Kalite | Medium | §1 Genel Yol Haritasi tablosunda "Sira" sutununda 0'dan 12'ye kadar numaralar listelenirken, bu numaralar asama numaralarini degil dokuman numaralarini gosteriyor. Ancak alt bolum numaralandirmasi "Asama 1", "Asama 2" seklinde ilerleyerek farkli bir numaralam kullanmakta (orn: §2 = "Asama 1", §3 = "Asama 2"... §11 = "Asama 10"). Bu, Genel Yol Haritasi'ndaki "Sira" ile bolum numaralari arasinda sistematik bir kayma (offset) yaratmakta. Yani Asama 1 = §2, Asama 2 = §3. Bu yapisal olarak dogru ve tutarli ama kafa karistirici olabilir. | Duzeltme gerektirmez — yapi tutarli. Farkindelik yeterli. |
| 7 | 00-§2.5-01 | Kalite | High | §2.5 Ciktilar tablosunda Product Discovery asamasinin ciktisi olarak 4 dokuman listeleniyor: PRODUCT_DISCOVERY_STATUS.md, 01_PROJECT_VISION.md, 02_PRODUCT_REQUIREMENTS.md, 10_MVP_SCOPE.md. Ancak 03_USER_FLOWS.md dosyasi §3 (Asama 2) icinde tanimlanmasina ragmen, Asama 1 (Product Discovery) icinde tartisilan akislarin kaydi ve 02 ile 03 arasindaki sicak gecis dolayisiyla bu belirsizlik var. Gercekte surec sirasiyla calistigina gore bu dogru, sadece netlik icin asama sinirlari daha belirgin yapilabilir. Ayrica tablo "ara dokuman" notasyonuyla PRODUCT_DISCOVERY_STATUS.md'yi isaretiyor ama diger 3 dokuman icin final/ara ayrimini gostermiyor. | Bu bir kalite onerisidir. PRODUCT_DISCOVERY_STATUS.md'nin "(ara dokuman)" olarak isaretlenmesi iyi, diger 3 dokuman icin de "(final dokuman)" notu eklenmesi tutarliligi artirabilir. |

---

### Aksiyon Plani

**Critical:**
- (Yok)

**High:**
- [x] Bulgu #1: §4.6 Ogrenimler'deki "(section 8)" referansini "(section 4)" olarak duzelt ve ogrenimin fiilen uygulandigini belirt
- [x] Bulgu #7: §2.5 Ciktilar tablosunda dokuman turlerini netlestir

**Medium:**
- [x] Bulgu #2: §3.2 Yaklasim'a operasyonel akislar icin bir yaklasim notu ekle → bu dokumanda cozulebilir
- [x] Bulgu #5: Bulgu #1 ile birlikte cozulur

**Low:**
- Bulgu #3: "(section 4.1)" → "(section 4)" referans hatasi — duzeltme uygulandi
- Bulgu #4: Birakilan konularin izleme mekanizmasina referans yok — farkindelik yeterli
- Bulgu #6: Asama numarasi vs bolum numarasi kaymasi — yapi tutarli, farkindelik yeterli

---

*Audit tamamlandi: 2026-03-15*
