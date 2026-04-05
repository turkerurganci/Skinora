# Skinora — Project Vision

**Versiyon: v1.1** | **Bağımlılıklar:** `PRODUCT_DISCOVERY_STATUS.md` | **Son güncelleme:** 2026-03-15

---

## 1. Ürün Özeti

**Skinora**, CS2 oyuncuları arasında platform dışında anlaşılmış item satışlarını güvenli hale getiren otomatik bir escrow (emanet) servisidir.

**Tek cümlelik tanım:** Skinora, CS2 item ticaretinde alıcı ve satıcı arasında güvenilir, otomatik ve izlenebilir bir escrow aracısıdır.

Skinora bir marketplace değildir. Fiyat belirleme, listeleme, arama veya eşleştirme yapmaz. Platformun tek odağı, zaten anlaşmış iki taraf arasındaki item satışını güvenli şekilde tamamlamaktır.

---

## 2. Çözülen Problem

### 2.1 Ana Problem

CS2 item alışverişleri çoğunlukla Discord, Steam chat, sosyal medya ve topluluk kanalları üzerinden gerçekleşiyor. Bu kanallar üzerinden yapılan işlemlerde güvenli, sistematik ve otomatik bir escrow mekanizması bulunmuyor. Bu durum yüksek dolandırıcılık riskine yol açıyor.

### 2.2 Alt Problemler

- **Güven eksikliği:** Alıcı parayı gönderdiğinde item'ı alacağından, satıcı item'ı verdiğinde parayı alacağından emin olamıyor.
- **Kişiye bağımlı middleman hizmetleri:** Mevcut güvenli takas çözümleri çoğunlukla bireysel middleman'lara (aracı kişilere) dayanıyor. Bu kişiler her zaman ulaşılabilir değil, hata yapabilir, hatta kendileri dolandırıcı olabilir.
- **Manuel süreçler:** Mevcut çözümlerde süreç büyük ölçüde manuel yürütülüyor — doğrulama, teslim ve ödeme kontrolü insan müdahalesine bağlı.
- **İzlenebilirlik eksikliği:** İşlem geçmişi, kullanıcı güvenilirliği ve anlaşmazlık çözümü için sistematik bir altyapı yok.

---

## 3. Hedef Kullanıcılar

### 3.1 Birincil Kullanıcılar

**CS2 item satıcıları ve alıcıları:** Discord, Steam grupları ve topluluk kanallarında item alışverişi yapan oyuncular. Platform dışında fiyat ve item üzerinde anlaşıp güvenli bir takas mekanizmasına ihtiyaç duyan kişiler.

### 3.2 Kullanıcı Özellikleri

- Steam hesabı sahibi aktif CS2 oyuncuları
- Kripto (stablecoin) kullanımına aşina veya aşinalık kazanmaya açık kişiler
- Platform dışında (Discord, Steam chat vb.) alım-satım anlaşması yapmış taraflar

### 3.3 Aktörler

| Aktör | Rol |
|---|---|
| Satıcı | İşlemi başlatır, item'ı platforma emanet eder, ödemeyi alır |
| Alıcı | İşlemi kabul eder, ödemeyi gönderir, item'ı teslim alır |
| Platform (Skinora) | Escrow aracısı — item ve ödemeyi geçici olarak tutar, doğrulamaları yapar, teslim ve ödemeyi gerçekleştirir |
| Admin | Platform ayarlarını yönetir, flag'lenmiş işlemleri inceler, eskalasyonları çözer |

---

## 4. Değer Önerisi

### 4.1 Kullanıcıya Sunulan Değer

- **Güvenlik:** Alıcı ve satıcı birbirine güvenmek zorunda değil — platform aracı olarak her iki tarafın varlığını korur.
- **Otomasyon:** Ödeme doğrulama (blockchain) ve item teslim doğrulama (Steam) otomatik yapılır, insan müdahalesine gerek yok.
- **İzlenebilirlik:** Her işlem kaydedilir, her adımda durum takibi yapılabilir, kullanıcı itibar skoru oluşur.
- **Hız:** Süreç tamamen dijital ve otomatik — middleman beklemek yok.
- **Adalet:** Herhangi bir adımda sorun çıkarsa (timeout, hata) varlıklar otomatik olarak sahiplerine iade edilir.

### 4.2 Kullanıcı Neden Skinora'yı Kullanır?

- Dolandırılma korkusu olmadan item alıp satabilir
- Kişiye bağımlı middleman'a gerek kalmaz
- İşlem her aşamada şeffaf ve takip edilebilir
- Anlaşmazlıklarda otomatik doğrulama ile hızlı çözüm
- İtibar sistemiyle güvenilir kullanıcıları ayırt edebilir

---

## 5. İş Değeri

### 5.1 Gelir Modeli

- **MVP'de:** Her işlemden alıcıdan alınan %2 komisyon (admin tarafından değiştirilebilir)
- **İleride:** Ek gelir kanalları planlanıyor (detaylar MVP sonrası)

### 5.2 Büyüme Potansiyeli

- CS2 skin pazarı milyarlarca dolarlık bir ekosistem
- Platform dışında anlaşılan işlemler bu pazarın önemli bir bölümünü oluşturuyor
- MVP sonrası Dota 2, TF2, Rust gibi diğer Steam oyunlarına genişleme potansiyeli

### 5.3 Rekabet Avantajı

- Marketplace'lerden (CS.Money, Skinport, Buff163) farklı konumlanma — eşleştirme değil, güvenli teslim
- Middleman hizmetlerinden farkı — otomatik, kişiye bağımlı değil, 7/24 çalışır
- Kripto ödeme ile global erişim, chargeback riski yok

---

## 6. Ürün Vizyonu

### 6.1 Kısa Vadeli (MVP)

CS2 item ticareti için güvenilir, otomatik, kripto (Tron TRC-20) tabanlı bir escrow platformu. Tek item, işlem başına tek stablecoin ödeme (USDT veya USDC — satıcı işlem başlatırken seçer), web platformu.

### 6.2 Orta Vadeli

- Diğer Steam oyunlarına genişleme (Dota 2, TF2, Rust)
- Mobil uygulama
- Kullanıcı yorum/değerlendirme sistemi
- Ek gelir kanalları
- Fiyat referansı gösterimi

### 6.3 Uzun Vadeli

- Çoklu item ve barter desteği
- Ek blockchain ağları
- Yüksek tutarlı işlemler için KYC
- Trade lock'lu item desteği

---

## 7. Başarı Kriterleri

| Alan | Metrik |
|---|---|
| Büyüme | Haftalık/aylık tamamlanan işlem sayısı, yeni kullanıcı kazanımı, geri dönüş oranı |
| Güvenilirlik | Başarılı işlem tamamlanma oranı, otomatik doğrulama hata oranı, dispute/eskalasyon oranı |
| Gelir | Aylık komisyon geliri |
| Güven | Tekrar kullanım oranı |

Hedef rakamlar MVP lansmanı sonrası belirlenecektir.

---

## 8. Temel İlkeler

- **Güvenlik önceliklidir:** Her tasarım kararında güvenlik ön planda tutulur.
- **Otomasyon esastır:** İnsan müdahalesine gerek kalmadan çalışan bir sistem hedeflenir.
- **Adalet sağlanır:** Herhangi bir aksaklıkta varlıklar sahiplerine iade edilir.
- **Şeffaflık:** Kullanıcı her aşamada işlemin durumunu görebilir.
- **Platform tarafsızdır:** Skinora bir aracıdır, taraf değildir. Fiyata müdahale etmez, sadece güvenli teslimatı sağlar.

---

*Skinora — Project Vision v1.1*
