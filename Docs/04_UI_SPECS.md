# Skinora — UI Specifications

**Versiyon: v3.0** | **Bağımlılıklar:** `02_PRODUCT_REQUIREMENTS.md`, `03_USER_FLOWS.md`, `10_MVP_SCOPE.md` | **Son güncelleme:** 2026-03-16

---

## İçindekiler

1. [Genel Bakış](#1-genel-bakış)
2. [Ekran Envanteri](#2-ekran-envanteri)
3. [Traceability Matrix](#3-traceability-matrix)
4. [Ekran Navigasyon Haritası](#4-ekran-navigasyon-haritası)
5. [Ortak Bileşen Kütüphanesi](#5-ortak-bileşen-kütüphanesi)
6. [Genel Ekranlar](#6-genel-ekranlar)
7. [Kullanıcı Ekranları](#7-kullanıcı-ekranları)
8. [Admin Ekranları](#8-admin-ekranları)
9. [Responsive Tasarım Notları](#9-responsive-tasarım-notları)
10. [Lokalizasyon Notları](#10-lokalizasyon-notları)

---

## 1. Genel Bakış

Bu doküman, Skinora platformunun ekran bazında kullanıcı arayüzü tanımlarını içerir. Her ekranın bilgi hiyerarşisini, kullanıcı aksiyonlarını, state varyantlarını ve ekranlar arası geçişleri tanımlar.

**Tasarım seviyesi:** Wireframe düzeyinde bilgi mimarisi ve etkileşim tanımı. Pixel-perfect görsel tasarım bu dokümanın kapsamı dışındadır.

**Platform:** Web-first, mobil uyumlu (responsive). MVP'de mobil uygulama yok.

**Dil desteği:** İngilizce, Çince, İspanyolca, Türkçe.

---

## 2. Ekran Envanteri

### 2.1 Özet

| Kategori | Ekran Sayısı | Oran |
|----------|-------------|------|
| Genel (public + auth + erişim kontrolü) | 7 | %29 |
| Kullanıcı | 7 | %29 |
| Admin | 10 | %42 |
| **Toplam** | **24** | **%100** |

### 2.2 Ekran Listesi

| # | Ekran | Kategori | URL Pattern |
|---|-------|----------|-------------|
| S01 | Landing Page | Genel | `/` |
| S02 | Steam Login (+ ToS modal) | Genel | `/auth/steam`, `/auth/callback` |
| S03 | Mobile Authenticator Uyarısı | Genel | `/auth/authenticator-required` |
| S03a | Erişim Engeli (Geo-Block) | Genel | `/access-denied/geo` |
| S03b | Yaş Gate | Genel | `/access-denied/age` |
| S03c | Sanctions Uyarı | Genel | `/access-denied/sanctions` |
| S03d | Hesap Askıya Alındı | Genel | `/account-suspended` |
| S05 | Dashboard | Kullanıcı | `/dashboard` |
| S06 | İşlem Oluşturma | Kullanıcı | `/transactions/new` |
| S07 | İşlem Detay | Kullanıcı | `/transactions/:id`, `/invite/:token` (public invite) |
| S08 | Profil (Kendi) | Kullanıcı | `/profile` |
| S09 | Profil (Başkası — Public) | Kullanıcı | `/users/:steamId` |
| S10 | Hesap Ayarları | Kullanıcı | `/settings` |
| S11 | Bildirimler | Kullanıcı | `/notifications` |
| S12 | Admin Dashboard | Admin | `/admin` |
| S13 | Flag Kuyruğu | Admin | `/admin/flags` |
| S14 | Flag Detay / İnceleme | Admin | `/admin/flags/:id` |
| S15 | İşlem Listesi & Arama | Admin | `/admin/transactions` |
| S16 | İşlem Detay (Admin) | Admin | `/admin/transactions/:id` |
| S17 | Parametre Yönetimi | Admin | `/admin/settings` |
| S18 | Platform Steam Hesapları | Admin | `/admin/steam-accounts` |
| S19 | Rol & Yetki Yönetimi | Admin | `/admin/roles` |
| S20 | Kullanıcı Detay (Admin) | Admin | `/admin/users/:steamId` |
| S21 | Audit Log | Admin | `/admin/audit-log` |

> **Not:** S04 numarası atlanmıştır. ToS kabul adımı S02 içinde modal olarak yer alır (ayrı ekran gerektirmez).

---

## 3. Traceability Matrix

### 3.1 İleri İzlenebilirlik: Akışlar → Ekranlar

#### Satıcı Akışları (03 §2)

| Akış | Adım | Açıklama | Ekran |
|------|------|----------|-------|
| 2.1 | 1-2 | Platforma gelir, "Steam ile Giriş" tıklar | S01 → S02 |
| 2.1 | 3-5 | Steam auth + callback | S02 (harici redirect) |
| 2.1 | 6 | Mobile Authenticator kontrolü | S03 (işlem blocker — navigasyon serbest, işlem başlatma engeli) |
| 2.1 | 7 | Hesap oluşturma + ToS kabul | S02 (ToS modal) |
| 2.1 | 8 | Dashboard'a yönlendirilir | S05 |
| 2.2 | 1 | "Yeni İşlem Başlat" | S05 → S06 |
| 2.2 | 2-4 | Limit/cooldown kontrolleri | S06 (error state'ler) |
| 2.2 | 5-8 | Envanter okuma, item seçimi, tradeable kontrolü | S06 (item picker) |
| 2.2 | 9-12 | Stablecoin, fiyat, timeout | S06 (işlem detayları) |
| 2.2 | 13-14 | Alıcı ve cüzdan adresi belirleme | S06 (alıcı & cüzdan) |
| 2.2 | 15-16 | Özet ve onay | S06 (review step) |
| 2.2 | 17-20 | Oluşturma, bildirim, bekleme | S07 (seller, CREATED) |
| 2.3 | 1-8 | Item emaneti süreci | S07 (seller, ACCEPTED → ITEM_ESCROWED) |
| 2.4 | 1-6 | Satıcıya ödeme | S07 (seller, ITEM_DELIVERED → COMPLETED) |
| 2.5 | 1-9 | Satıcı iptal akışı | S07 (cancel modal) |

#### Alıcı Akışları (03 §3)

| Akış | Adım | Açıklama | Ekran |
|------|------|----------|-------|
| 3.1 | 1 | Davet linkine tıklar | S07 (public varyant) |
| 3.1 | 2-5 | Login + hesap oluşturma | S02 → S07 |
| 3.1 | 6 | İşlem detay sayfasına yönlendirilir | S07 (buyer, CREATED) |
| 3.2 | 1-8 | İşlemi kabul etme (iade adresi dahil) | S07 (buyer, CREATED → ACCEPTED) |
| 3.3 | 1-8 | Alıcı iptal akışı | S07 (cancel modal) |
| 3.4 | 1-8 | Ödeme gönderme | S07 (buyer, ITEM_ESCROWED) |
| 3.5 | 1-11 | Item teslim alma | S07 (buyer, PAYMENT_RECEIVED → COMPLETED) |

#### Timeout Akışları (03 §4)

| Akış | Açıklama | Ekran |
|------|----------|-------|
| 4.1 | Alıcı kabul timeout'u | S07 (countdown + CANCELLED_TIMEOUT) |
| 4.2 | Satıcı trade offer timeout'u | S07 (countdown + CANCELLED_TIMEOUT) |
| 4.3 | Ödeme timeout'u | S07 (countdown + CANCELLED_TIMEOUT + iade) |
| 4.4 | Teslim trade offer timeout'u | S07 (countdown + CANCELLED_TIMEOUT + çift iade) |
| 4.5 | Timeout yaklaşıyor uyarısı | S07 (uyarı banner) + S11 (bildirim) |

#### Ödeme Edge Case Akışları (03 §5)

| Akış | Açıklama | Ekran |
|------|----------|-------|
| 5.1 | Eksik tutar | S07 (ödeme error state) |
| 5.2 | Fazla tutar | S07 (fazla iade bilgisi) |
| 5.3 | Yanlış token | S07 (ödeme error state) |
| 5.4 | Gecikmeli ödeme | S07 (CANCELLED state + iade bildirimi) |

#### Dispute Akışları (03 §6)

| Akış | Açıklama | Ekran |
|------|----------|-------|
| 6.1-6.3 | Ödeme / teslim / yanlış item itirazı | S07 (dispute form + sonuç) |
| 6.4 | Admin eskalasyonu | S07 → S13/S14 |

#### Fraud / Flag Akışları (03 §7)

| Akış | Açıklama | Ekran |
|------|----------|-------|
| 7.1-7.3 | Flag'leme (fiyat sapma, hacim, davranış) | S07 (FLAGGED state) + S13/S14 (admin) |
| 7.4 | Çoklu hesap tespiti | S13/S14 (admin) |

#### Admin Akışları (03 §8)

| Akış | Adım | Açıklama | Ekran |
|------|------|----------|-------|
| 8.1 | 1-3 | Admin giriş, dashboard | S12 |
| 8.2 | 1-4 | Flag inceleme | S13, S14 |
| 8.3 | 1-3 | İşlem listesi ve detay | S15, S16 |
| 8.4 | 1-4 | Parametre yönetimi | S17 |
| 8.5 | 1-3 | Steam hesapları izleme | S18 |
| 8.6 | 1-4 | Rol ve yetki yönetimi | S19 |

#### Profil, Hesap ve Diğer Akışlar (03 §9-12)

| Akış | Açıklama | Ekran |
|------|----------|-------|
| 9.1-9.2 | Cüzdan adresi tanımlama / değişikliği | S08 |
| 9.3 | Profil görüntüleme (kendi / başkası) | S08, S09 |
| 10.1-10.2 | Hesap deaktif / silme | S10 |
| 11.1-11.2 | Downtime / bakım | Global banner bileşeni |
| 12.1-12.3 | Bildirimler | S11 |

### 3.2 İleri İzlenebilirlik: Gereksinimler → Ekranlar

| Gereksinim (02) | Ref | Ekran(lar) |
|-----------------|-----|------------|
| Temel işlem akışı | §2 | S06, S07 |
| Timeout yapısı | §3 | S07 (countdown), S17 (config) |
| Ödeme altyapısı + edge case | §4 | S06 (seçim), S07 (ödeme bölümü) |
| Komisyon | §5 | S06, S07 (gösterim), S17 (config) |
| Alıcı belirleme | §6 | S06 (input), S17 (yöntem 2 toggle) |
| İptal kuralları | §7 | S07 (iptal modal) |
| İşlem limitleri | §8 | S06 (validation), S17 (config) |
| Item yönetimi | §9 | S06 (envanter + seçim) |
| Dispute | §10 | S07 (dispute flow) |
| Kullanıcı kimlik / giriş | §11 | S01, S02, S03 |
| Cüzdan güvenliği | §12 | S07 (alıcı iade adresi), S08, S06 |
| İtibar skoru | §13 | S08, S09, S07 |
| Fraud / abuse | §14 | S07 (FLAGGED), S13, S14, S17 |
| Platform Steam hesapları | §15 | S18 |
| Admin paneli | §16 | S12-S21 |
| Kullanıcı dashboard | §17 | S05 |
| Bildirimler | §18 | S11, S10 (tercihler) |
| Hesap yönetimi | §19 | S10 |
| Platform sorumluluğu | §20 | S01 (landing), S02 (ToS modal) |
| Erişim / dil desteği | §21 | Tüm ekranlar (responsive + i18n) |
| Kullanıcı sözleşmesi | §22 | S02 (ToS modal) |
| Downtime yönetimi | §23 | Global banner bileşeni |

### 3.3 Geri İzlenebilirlik: Ekranlar → Kaynaklar

| Ekran | Akışlar (03) | Gereksinimler (02) |
|-------|--------------|--------------------|
| S01 | 2.1/1-2 | §11, §20, §21 |
| S02 | 2.1/2-7, 3.1/2-5 | §11, §22 |
| S03 | 2.1/6 | §11 |
| S03a | 11a.1 (geo-block) | §21.1 (yasaklı bölgeler, geo-block) |
| S03b | 11a.2 (yaş gate) | §21.1 (yaş kısıtı) |
| S03c | 11a.3 (sanctions) | §21.1 (sanctions screening) |
| S03d | S14 hesap flag → Askıya Al kararı | §14.0 (hesap flag'i), §16.2 (flag'lenmiş hesap yönetimi) |
| S05 | 2.1/8, 2.2/1 | §17 |
| S06 | 2.2/2-16 | §2, §4, §5, §6, §8, §9, §12 |
| S07 | 2.2/17-20, 2.3-2.5, 3.2-3.5, 4.x, 5.x, 6.x, 7.x | §2-§5, §7, §10, §12.2, §13, §14 |
| S08 | 9.1-9.3 | §12, §13 |
| S09 | 9.3 | §13 |
| S10 | 10.1-10.2 | §18.1, §19 |
| S11 | 12.1-12.3 | §18 |
| S12 | 8.1 | §16 |
| S13 | 8.2/1-2, 7.x | §14, §16 |
| S14 | 8.2/3-4 | §14, §16 |
| S15 | 8.3/1-2 | §16 |
| S16 | 8.3/3 | §16 |
| S17 | 8.4 | §3, §4.6, §5, §6.2, §8, §14, §16.2 |
| S18 | 8.5 | §15, §16 |
| S19 | 8.6 | §16.1 |
| S20 | — (GAP-6'dan türetildi) | §16 (implied) |
| S21 | — | §16.2 (audit log görüntüleme) |

---

## 4. Ekran Navigasyon Haritası

### 4.1 Genel Akış

```
[Dış dünya]
    │
    ├─── Direkt ziyaret ──→ S01 Landing ──→ S02 Steam Login
    │                                              │
    ├─── Davet linki ────→ S07 Public ────→ S02 ───┤  (kaynak: davet/direkt)
    │                      varyant                  │
    │                                               ▼
    │                                    ┌─ 0. Auth başarısız → Hata
    │                                    ├─ 1. Geo-block? → S03a
    │                                    ├─ 2. Sanctions? → S03c (+ auto-hold)
    │                                    ├─ 3. Askıya alınmış? → S03d
    │                                    └─ Geçti ↓
    │                                         İlk giriş?
    │                                          ├─ Evet → 18+ beyan + ToS modal
    │                                          │          ├─ Yaş gate başarısız → S03b
    │                                          │          └─ Geçti → MA kontrolü
    │                                          └─ Hayır → MA kontrolü
    │                                                        │
    │                                                   MA aktif?
    │                                                    ├─ Hayır → S03
    │                                                    └─ Evet ─→ Kaynak davet?
    │                                                                ├─ Evet → S07
    │                                                                └─ Hayır → S05
    │
    └─── Admin giriş ───→ S02 ──→ S12 Admin Dashboard
```

> MA = Mobile Authenticator. Pipeline sırası S02 §Callback ile birebir hizalıdır.

### 4.2 Kullanıcı Navigasyonu

```
S05 Dashboard
 ├── "Yeni İşlem Başlat" ──→ S06 İşlem Oluşturma ──→ S07 İşlem Detay
 ├── İşlem satırı tıkla ───→ S07 İşlem Detay
 ├── Bildirim ikonu ────────→ S11 Bildirimler
 ├── Profil ikonu ──────────→ S08 Profil (Kendi)
 └── Ayarlar ───────────────→ S10 Hesap Ayarları

S07 İşlem Detay
 ├── Satıcı/alıcı avatarı tıkla ──→ S09 Profil (Public)
 ├── İtiraz Et ────────────────────→ S07 (dispute form, inline)
 ├── Admin'e İlet ─────────────────→ S07 (eskalasyon formu, inline)
 └── İşlemi İptal Et ─────────────→ S07 (iptal modal)

S08 Profil (Kendi)
 └── Ayarlar linki ──→ S10

S11 Bildirimler
 └── Bildirim tıkla ──→ S07 (ilgili işlem)
```

### 4.3 Admin Navigasyonu

```
S12 Admin Dashboard
 ├── Flag sayısı kartı ────→ S13 Flag Kuyruğu
 ├── İşlem sayısı kartı ──→ S15 İşlem Listesi
 ├── Steam hesapları ──────→ S18 Steam Hesapları
 ├── Ayarlar ──────────────→ S17 Parametre Yönetimi
 ├── Roller ───────────────→ S19 Rol & Yetki
 └── Audit Log ────────────→ S21 Audit Log

S13 Flag Kuyruğu
 └── Flag satırı tıkla ──→ S14 Flag Detay

S14 Flag Detay
 ├── Kullanıcı adı tıkla ──→ S20 Kullanıcı Detay
 └── İşlem ID tıkla ───────→ S16 İşlem Detay (Admin)

S15 İşlem Listesi
 └── İşlem satırı tıkla ──→ S16 İşlem Detay (Admin)

S16 İşlem Detay (Admin)
 └── Kullanıcı adı tıkla ──→ S20 Kullanıcı Detay

S20 Kullanıcı Detay
 └── İşlem satırı tıkla ──→ S16 İşlem Detay (Admin)
```

---

## 5. Ortak Bileşen Kütüphanesi

Bu bölüm, birden fazla ekranda tekrar eden UI pattern'lerini tanımlar. Her ekran tanımında bu bileşenlere referans verilir.

### C01 — Status Badge

İşlem durumunu gösteren renk kodlu etiket.

| Durum | Etiket Metni | Renk Tonu |
|-------|-------------|-----------|
| CREATED | Oluşturuldu | Mavi |
| ACCEPTED | Kabul Edildi | Mavi |
| TRADE_OFFER_SENT_TO_SELLER | Item Bekleniyor | Sarı |
| ITEM_ESCROWED | Ödeme Bekleniyor | Sarı |
| PAYMENT_RECEIVED | Ödeme Alındı | Yeşil (açık) |
| TRADE_OFFER_SENT_TO_BUYER | Teslim Bekleniyor | Sarı |
| ITEM_DELIVERED | Teslim Edildi | Yeşil (açık) |
| COMPLETED | Tamamlandı | Yeşil |
| CANCELLED_TIMEOUT | Zaman Aşımı | Kırmızı |
| CANCELLED_SELLER | Satıcı İptal | Kırmızı |
| CANCELLED_BUYER | Alıcı İptal | Kırmızı |
| CANCELLED_ADMIN | Admin İptal | Turuncu-Kırmızı |
| FLAGGED | İnceleniyor | Turuncu |
| EMERGENCY_HOLD | Donduruldu | Kırmızı-Turuncu |

**Lokalizasyon notu:** Etiket metinleri 4 dilde farklı uzunluklarda olabilir. Badge genişliği metin uzunluğuna göre esnemelidir.

### C02 — Countdown Timer

Timeout geri sayım göstergesi. Birden fazla ekranda (S07) farklı timeout'lar için kullanılır.

**Görünüm:**
- Kalan süre: `2s 14dk` veya `00:14:32` formatında
- Renk geçişi (admin tarafından belirlenen uyarı eşiğine göre — bkz. S17 "Timeout uyarı eşiği"):
  - %0 – eşiğin yarısı → Yeşil
  - Eşiğin yarısı – eşik → Sarı
  - Eşik – %100 → Kırmızı (yanıp sönen)
- Uyarı eşiğine ulaşıldığında inline uyarı metni gösterilir: "Süreniz dolmak üzere"

**Davranış:**
- Gerçek zamanlı güncellenir
- Timeout dolduğunda → ekran otomatik güncellenir (state transition)
- Timeout dondurulduysa (bakım/kesinti) → "Donduruldu" etiketi + donma sebebi

### C03 — Item Card

CS2 item'ını gösteren kart bileşeni.

**İçerik:**
- Item görseli (Steam CDN)
- Item adı
- Item tipi (silah, bıçak, eldiven vb.)
- Wear durumu (Factory New, Minimal Wear vb.) — varsa
- Tradeable badge (yeşil tik) veya Non-tradeable badge (kırmızı kilit)

**Görsel yüklenemezse:** Varsayılan CS2 item placeholder görseli gösterilir.

**Varyantlar:**
- **Compact:** Liste görünümü için (küçük görsel + ad)
- **Detailed:** Detay sayfası için (büyük görsel + tam bilgi)
- **Selectable:** İşlem oluşturmada seçim için (tıklanabilir, seçili state)

### C04 — User Card

Kullanıcı bilgilerini gösteren kart bileşeni.

**İçerik:**
- Steam avatar
- Kullanıcı adı
- İtibar skoru (yıldız veya sayısal)
- Tamamlanan işlem sayısı
- Platformdaki hesap yaşı

**Varyantlar:**
- **Compact:** İşlem detayında satıcı/alıcı gösterimi (avatar + ad + skor)
- **Detailed:** Profil sayfasında tam bilgi

### C05 — Transaction Timeline

İşlemin hangi adımda olduğunu gösteren ilerleme göstergesi.

**Yapı:** 8 adımlık yatay progress bar:
1. Oluşturuldu → 2. Kabul Edildi → 3. Item Emanet → 4. Ödeme → 5. Ödeme Doğrulama → 6. Teslim → 7. Teslim Doğrulama → 8. Tamamlandı

- Tamamlanan adımlar: dolu (yeşil)
- Aktif adım: vurgulu (mavi, animasyonlu)
- Bekleyen adımlar: boş (gri)
- İptal durumunda: aktif adımda kırmızı X
- FLAGGED durumunda: aktif adımda turuncu duraklatma ikonu

**Responsive:** Mobilde dikey layout'a geçer.

### C06 — Cancel Modal

İptal onay modal'ı. S07'de satıcı ve alıcı için kullanılır.

**İçerik:**
1. Uyarı başlığı: "İşlemi iptal etmek istediğinize emin misiniz?"
2. İptal sebebi (zorunlu textarea, min 10 karakter)
3. İade bilgisi: "Item satıcıya iade edilecektir" (duruma göre değişir)
4. "İptal Et" butonu (kırmızı) + "Vazgeç" butonu (gri)

### C07 — Dispute Form

İtiraz formu. S07'de alıcı için kullanılır.

**Adım 1 — Tür Seçimi:**
- Ödeme itirazı: "Ödeme gönderildi ama doğrulanmadı"
- Teslim itirazı: "Item teslim edilmedi"
- Yanlış item: "Yanlış item teslim edildi"

**Adım 2 — Otomatik Kontrol:**
- Sistem kontrolü çalışır (loading state)
- Sonuç gösterilir (başarılı çözüm veya çözümsüz)

**Adım 3 — Eskalasyon (opsiyonel):**
- Otomatik çözüm tatmin etmediyse → "Admin'e İlet" butonu
- İtiraz detayı textarea (zorunlu)
- Gönder butonu

### C08 — Maintenance Banner

Global bakım/kesinti banner'ı. Tüm ekranların üstünde gösterilir.

**Varyantlar:**
- **Planlı bakım:** Sarı banner — "Platform bakımı: [tarih/saat]. Aktif işlemlerin süreleri dondurulacaktır."
- **Aktif bakım:** Kırmızı banner — "Platform şu an bakımda. İşlem süreleri donduruldu."
- **Steam kesintisi:** Turuncu banner — "Steam servisleri geçici olarak kullanılamıyor. İşlem süreleri donduruldu."
- **Blockchain degradasyonu:** Turuncu banner — "Ödeme doğrulama geçici olarak yavaşlayabilir. Ödeme adımındaki işlem süreleri donduruldu." (02 §3.3)

**Davranış:** Kapatılamaz (aktif bakım/kesinti sırasında). Planlı bakım bildirimi kapatılabilir.

### C09 — Toast Notification

Anlık bildirim göstergesi. Ekranın sağ üst köşesinde belirip kaybolan mesaj.

**Varyantlar:**
- **Bilgi:** Mavi — genel bilgilendirme
- **Başarı:** Yeşil — işlem başarılı
- **Uyarı:** Sarı — dikkat gerektiren durum
- **Hata:** Kırmızı — hata oluştu

**Davranış:** 5 saniye sonra otomatik kapanır. Tıklanarak kapatılabilir. Birden fazla toast üst üste yığılır.

### C10 — Language Selector

Dil seçici. Header'da her zaman erişilebilir.

**Görünüm:** Aktif dilin bayrağı veya kodu + dropdown
**Seçenekler:** EN | 中文 | ES | TR
**Davranış:** Seçim anında sayfa içeriği değişir. Tercih localStorage + kullanıcı profili (giriş yapıldıysa) olarak saklanır.

### C11 — Wallet Address Input

Cüzdan adresi giriş bileşeni. S06 ve S08'de kullanılır.

**İçerik:**
- TRC-20 adresi input alanı
- Format validation (T ile başlar, 34 karakter)
- Sanctions screening kontrolü (02 §12.3, 03 §11a.3) — geçersiz format veya yaptırımlı adres → error state, kayıt engellenir
- Onay adımı: "Bu adres doğru mu?" + adresin tam gösterimi
- "Onayla" + "Düzenle" butonları

> **Merkezi doğrulama:** Bu bileşen tüm cüzdan adresi giriş noktalarında (S06, S07, S08, profil) aynı doğrulama pipeline'ını uygular (02 §12.3).

### C12 — Copy Button

Tek tıkla kopyalama butonu. Adresler, linkler, ID'ler için.

**Davranış:** Tıklandığında → içerik clipboard'a kopyalanır → buton ikonu "✓ Kopyalandı" olarak değişir → 2 saniye sonra eski haline döner.

### C13 — Empty State

Veri olmadığında gösterilen boş durum bileşeni.

**İçerik:**
- İlgili ikon
- Açıklayıcı mesaj (örn: "Henüz işleminiz yok")
- CTA butonu (opsiyonel, örn: "İlk işlemini başlat")

### C14 — Loading State

Veri yüklenirken gösterilen durum.

**Varyantlar:**
- **Skeleton:** Tablo ve kart alanları için iskelet animasyonu
- **Spinner:** Tek bir aksiyon için dönen yükleme ikonu
- **Progress:** Uzun süren işlemler için ilerleme çubuğu

### C15 — Error State

Hata durumlarında gösterilen bileşen.

**İçerik:**
- Hata ikonu
- Hata mesajı (kullanıcı dostu, teknik değil)
- "Tekrar Dene" butonu (uygunsa)

### C16 — Pagination

Liste ve tablolar için sayfalama.

**Görünüm:** « Önceki | 1 2 3 ... 10 | Sonraki »
**Davranış:** URL parametresi olarak sayfa numarası (`?page=2`)

### C17 — Filter Bar

Filtreleme çubuğu. Admin ekranlarında (S13, S15) ve dashboard'da kullanılır.

**Yapı:**
- Filtre alanları (dropdown, date picker, text input)
- "Filtrele" butonu
- "Temizle" linki
- Aktif filtreler chip olarak gösterilir

---

## 6. Genel Ekranlar

### 6.1 S01 — Landing Page

**Amaç:** Ziyaretçiyi platforma tanıtmak ve kayıt/giriş yaptırmak.

**Erişim:** Herkese açık (auth gerekmez). Giriş yapmış kullanıcı `/` adresine gelirse S05'e yönlendirilir.

**Bilgi Hiyerarşisi:**

1. **Hero Section**
   - Başlık: Platform değer önerisi (örn: "CS2 Item Ticaretinde Güvenli Escrow")
   - Alt başlık: Tek cümlelik açıklama
   - CTA: "Steam ile Giriş" butonu (birincil, büyük)

2. **Nasıl Çalışır**
   - 4 adımlık görsel akış:
     1. Satıcı işlemi başlatır
     2. Item platforma emanet edilir
     3. Alıcı ödemeyi gönderir
     4. Otomatik teslim ve ödeme
   - Her adım: ikon + kısa açıklama

3. **Güven Göstergeleri**
   - Toplam tamamlanan işlem sayısı
   - Platform çalışma süresi (uptime)
   - Otomatik doğrulama vurgusu

4. **Footer**
   - Terms of Service linki
   - Privacy Policy linki (ileride)
   - Dil seçici (C10)

**Aksiyonlar:**

| Aksiyon | Hedef |
|---------|-------|
| "Steam ile Giriş" tıkla | → S02 (Steam OAuth) |
| Dil değiştir | Sayfa içeriği güncellenir |
| ToS linki | Yeni sekmede açılır |

**State'ler:**

| State | Görünüm |
|-------|---------|
| Normal | Tam sayfa, tüm bölümler |
| Bakım (C08 aktif) | Banner üstte, CTA devre dışı |

---

### 6.2 S02 — Steam Login

**Amaç:** Steam OAuth üzerinden kullanıcı girişi ve ilk kayıt.

**Erişim:** Giriş yapmamış kullanıcılar.

**Akış:**

1. **Pre-redirect:** Kullanıcı "Steam ile Giriş" tıklar → loading indicator → Steam'e yönlendirilir
2. **Steam sayfası:** Harici (platform kontrolü dışı)
3. **Callback:** Steam'den döner → `/auth/callback` → loading indicator gösterilir

**Callback Sonrası Kontrol Pipeline'ı (deterministik sıra):**

> Kontroller aşağıdaki sırayla çalışır. Bir blocker tetiklendiğinde sonraki kontroller çalışmaz.

| Sıra | Kontrol | Sonuç |
|------|---------|-------|
| 0 | Steam auth başarısız | → Hata mesajı + "Tekrar Dene" butonu |
| 1 | Geo-block (IP kontrolü) | → S03a — erişim engeli (en önce, auth sonucu fark etmez) |
| 2 | Sanctions eşleşmesi (mevcut profil adresi) | → S03c — sanctions uyarı + aktif işlemlere auto EMERGENCY_HOLD. **Not:** Sanctions side-effect (auto-hold) her durumda çalışır — askıya alınmış hesaplarda bile |
| 3 | Hesap askıya alınmış mı? | → S03d — suspended session |
| 4a | Yeni kullanıcı: 18+ beyan + ToS | → ToS modal (18+ checkbox + ToS checkbox). Yaş gate başarısızsa → S03b |
| 4b | Mevcut kullanıcı: MA kontrolü | → MA aktifse → S05 (veya davet linkinden geldiyse S07). MA aktif değilse → S03 |

**ToS Kabul Modal'ı (İlk Kayıt):**

- Başlık: "Skinora'ya Hoş Geldiniz"
- ToS özeti (kısa maddeler)
- "En az 18 yaşında olduğumu beyan ederim" checkbox (yaş gate — 03 §11a.2)
- "Kullanıcı Sözleşmesi'ni okudum ve kabul ediyorum" checkbox
- Tam sözleşme linki (yeni sekmede açılır)
- "Devam Et" butonu (her iki checkbox işaretlenmeden devre dışı)
- 18+ beyan + Steam hesap yaşı kontrolü → Yaş gate başarısızsa → S03b. Geçerse → MA kontrolü → MA aktifse: kaynak davetse S07, değilse S05. MA aktif değilse → S03

---

### 6.3 S03 — Mobile Authenticator Uyarısı

**Amaç:** Steam Mobile Authenticator aktif olmayan kullanıcıları bilgilendirmek.

**Erişim:** MA kontrolü başarısız olan giriş yapmış kullanıcılar.

**Bilgi Hiyerarşisi:**

1. **Uyarı ikonu ve başlık:** "Steam Mobile Authenticator Gerekli"
2. **Açıklama:** "İşlem başlatabilmek için Steam Mobile Authenticator'ınızın aktif olması gerekiyor."
3. **Adım adım talimatlar:** MA nasıl aktif edilir (Steam mobil uygulama linki)
4. **"Kontrol Et" butonu:** MA durumunu tekrar kontrol eder
5. **"Dashboard'a Git" linki:** Kullanıcı platformu gezebilir ama işlem başlatamaz

**Davranış:**
- Bu ekran blocker değil, kullanıcı dashboard'a geçebilir
- İşlem başlatma sayfasına (S06) giderse aynı uyarı S06'da da gösterilir
- MA aktif edildiğinde "Kontrol Et" → S05'e yönlendirilir

### 6.4 S03a — Erişim Engeli Ekranı (Geo-Block)

**Amaç:** Yasaklı bölgeden erişen kullanıcıyı bilgilendirmek (02 §21.1, 03 §11a.1).

**Tetikleme:** Login öncesi veya sonrası IP bazlı geo-block tespiti.

**İçerik:**
1. **Uyarı ikonu ve başlık:** "Erişim Engellendi"
2. **Açıklama:** "Bulunduğunuz bölgeden bu platforma erişim kısıtlanmıştır."
3. **Ek bilgi:** Platforma erişilemeyen bölgeler hakkında genel bilgi (spesifik ülke listesi gösterilmez)
4. **Destek linki:** İletişim bilgileri

**Davranış:** Tam blocker — platform hiçbir şekilde kullanılamaz. Login yapılamaz.

### 6.5 S03b — Yaş Gate Ekranı

**Amaç:** 18 yaş altı beyan eden kullanıcıyı bilgilendirmek (02 §21.1, 03 §11a.2).

**Tetikleme:** Kayıt/giriş sürecinde yaş beyanı kontrolü.

**İçerik:**
1. **Uyarı ikonu ve başlık:** "Yaş Gereksinimi Karşılanmıyor"
2. **Açıklama:** "Bu platformu kullanmak için en az 18 yaşında olmanız gerekmektedir."

**Davranış:** Tam blocker — hesap oluşturulamaz.

### 6.6 S03c — Sanctions Uyarı Ekranı

**Amaç:** Cüzdan adresi yaptırım listesiyle eşleşen kullanıcıyı bilgilendirmek (02 §21.1, 03 §11a.3).

**Tetikleme varyantları:**
- **Login/profil kontrolü:** Login sonrası mevcut profil adresi sanctions listesiyle eşleşiyor (S02 callback kontrolü)
- **Adres girişi:** Yeni cüzdan adresi tanımlama veya değiştirme sırasında eşleşme (C11 validation pipeline)
- **Ödeme tespiti:** Gelen ödemenin kaynak adresi sanctions listesiyle eşleşiyor

**İçerik:**
1. **Uyarı ikonu ve başlık:** "İşlem Engellenmiştir"
2. **Açıklama:** "Platform politikaları nedeniyle hesabınız kısıtlanmıştır."
3. **Destek linki:** İletişim bilgileri

**Davranış:**
- İlgili aksiyon engellenir (adres kaydı, ödeme kabulü veya platform erişimi)
- Hesap flag'lenir (hesap flag'i)
- Kullanıcının tüm aktif işlemlerine otomatik EMERGENCY_HOLD uygulanır (03 §11a.3, S17 runtime etki)
- Admin inceleme başlatılır

### 6.7 S03d — Hesap Askıya Alındı Ekranı

**Amaç:** Hesabı askıya alınmış kullanıcıyı bilgilendirmek (S14 hesap flag → Askıya Al kararı).

**Tetikleme:** Login sonrası hesap askıya alınmış durumda tespit edildiğinde. Kullanıcı oturum açıkken askıya alınırsa normal oturum sona erer ve yerine kısıtlı (suspended) oturum başlatılır — kullanıcı S03d'ye yönlendirilir (real-time SignalR bildirimi ile).

**İçerik:**
1. **Uyarı ikonu ve başlık:** "Hesabınız Askıya Alınmıştır"
2. **Açıklama:** "Hesabınız platform politikaları nedeniyle askıya alınmıştır. Yeni işlem başlatamaz, mevcut işlemleri kabul edemezsiniz."
3. **Aktif işlem bilgisi:** Kullanıcının aktif işlemleri varsa: "Bazı aktif işlemleriniz otomatik olarak devam edebilir. Ancak sizden aksiyon gerektiren adımlarda işlem ilerlemeyebilir veya süre aşımına düşebilir. Detayları işlem ekranında görüntüleyebilirsiniz." (salt okunur erişim)
4. **Destek linki:** İletişim bilgileri

**Davranış:** Normal oturum yerine kısıtlı (suspended) oturum verilir. Bu oturumda: fon akışı aksiyonları engellenir (yeni işlem, kabul, açık link). Aktif işlem detay sayfaları salt okunur erişilebilir — kullanıcı durumu görebilir ama aksiyon alamaz. Diğer tüm korumalı sayfalar erişilemez (S05 dashboard hariç — salt okunur). Trade offer kabul gibi Steam tarafı aksiyonlar etkilenmez (platform kontrolü dışında).

---

## 7. Kullanıcı Ekranları

### 7.1 S05 — Dashboard

**Amaç:** Kullanıcının ana sayfası. Aktif işlemler, geçmiş, hızlı istatistikler.

**Erişim:** Giriş yapmış kullanıcılar.

**Layout:**

```
┌──────────────────────────────────────────────┐
│  Header: Logo | Bildirimler (S11) | Profil   │
│          (C10 dil) | Ayarlar (S10)           │
├──────────────────────────────────────────────┤
│  [ + Yeni İşlem Başlat ]     Hızlı İstatistik│
│                               ┌─────────────┐│
│  ┌─ Tab: Aktif | Tamamlanan   │ İşlem: 24   ││
│  │         | İptal            │ Başarı: %96 ││
│  │                            │ Skor: 4.8   ││
│  │  İşlem Listesi             └─────────────┘│
│  │  ┌────────────────────────┐               │
│  │  │ #1234 | Item | Status  │               │
│  │  │ Fiyat | Tarih | Karşı  │               │
│  │  └────────────────────────┘               │
│  │  ┌────────────────────────┐               │
│  │  │ #1235 | Item | Status  │               │
│  │  └────────────────────────┘               │
│  └───────────────────────────                │
│                                    Pagination │
└──────────────────────────────────────────────┘
```

**Bilgi Hiyerarşisi:**

1. **Üst bar (global)**
   - Logo (→ S05)
   - Bildirim ikonu + okunmamış sayısı (→ S11)
   - Profil avatarı (→ S08)
   - Dil seçici (C10)

2. **CTA: "Yeni İşlem Başlat"** (birincil buton, → S06)

3. **Hızlı İstatistikler** (sağ panel veya üst kartlar)
   - Toplam tamamlanan işlem sayısı
   - Başarılı işlem oranı (%)
   - İtibar skoru

4. **İşlem Listesi** (tab'lı)
   - **Aktif tab:** Terminal olmayan tüm işlemler (CREATED → ITEM_DELIVERED + FLAGGED + EMERGENCY_HOLD)
   - **Tamamlanan tab:** COMPLETED durumundaki işlemler
   - **İptal tab:** CANCELLED_* durumundaki işlemler

**İşlem Satırı İçeriği:**
- İşlem ID
- Item görseli (küçük) + adı
- Status badge (C01)
- Fiyat + stablecoin
- Karşı taraf (avatar + ad)
- Tarih
- Aktif işlemlerde: countdown timer (C02) — varsa

**Aksiyonlar:**

| Aksiyon | Koşul | Hedef |
|---------|-------|-------|
| "Yeni İşlem Başlat" | MA aktif, limit aşılmamış | → S06 |
| "Yeni İşlem Başlat" | MA aktif değil | → Uyarı (S03 içeriği inline) |
| "Yeni İşlem Başlat" | Limit aşılmış / cooldown | → Hata mesajı (sebep + kalan süre) |
| İşlem satırı tıkla | — | → S07 |
| Tab değiştir | — | Liste güncellenir |

**Suspended Session Override (S03d kısıtlı oturumda):**
- "Yeni İşlem Başlat" butonu gizlenir
- İşlem listesi salt okunur görüntülenir (tıklama → S07 salt okunur)
- Üstte turuncu banner: "Hesabınız askıya alınmıştır. İşlemlerinizi görüntüleyebilirsiniz ancak yeni işlem başlatamaz veya mevcut işlemlere müdahale edemezsiniz."

**State'ler:**

| State | Görünüm |
|-------|---------|
| Yeni kullanıcı (hiç işlem yok) | Empty state (C13): "Henüz işleminiz yok" + "İlk İşlemini Başlat" CTA |
| Aktif işlem var | İşlem listesi, aktif tab varsayılan |
| Yükleniyor | Skeleton (C14) |
| Hata | Error state (C15) |

---

### 7.2 S06 — İşlem Oluşturma

**Amaç:** Satıcının yeni bir escrow işlemi başlatması. Çok adımlı form.

**Erişim:** Giriş yapmış, MA aktif, limit/cooldown uygun kullanıcılar.

**Form Adımları:**

#### Adım 1: Item Seçimi

**İçerik:**
- Başlık: "Satmak istediğiniz item'ı seçin"
- Steam envanter grid (2-4 kolon)
  - Her item: C03 (Selectable varyant)
  - Non-tradeable item'lar: C03 (gri, devre dışı) + "Takas edilemez" tooltip
- Arama/filtre: Item adına göre
- Seçili item vurgulanır (border + tik)

**Büyük Envanter:** İlk 50 item gösterilir, aşağı scroll ile daha fazla yüklenir (infinite scroll). Toplam item sayısı grid üstünde gösterilir: "X tradeable item".

**State'ler:**
- Yükleniyor: Skeleton grid
- Envanter boş: "Steam envanterinizde tradeable item bulunamadı"
- Steam API hatası: "Envanter okunamadı, lütfen tekrar deneyin"

#### Adım 2: İşlem Detayları

**İçerik:**
- Seçili item özeti (C03 Compact, değiştirilebilir → adım 1'e geri)
- **Stablecoin seçimi:** USDT / USDC toggle butonları
- **Fiyat girişi:**
  - Input alanı + seçili stablecoin etiketi (örn: `[___] USDT`)
  - Min/max bilgisi altında gösterilir: "Min: X, Max: Y"
  - Validation: boş, min altı, max üstü
- **Ödeme timeout süresi:**
  - Slider veya select: admin'in belirlediği min–max aralığında
  - Varsayılan değer ön seçili
  - Açıklama: "Alıcının ödeme yapması gereken süre"
- **Komisyon bilgisi (salt okunur):** "Alıcı %2 komisyon ödeyecek: X USDT"

#### Adım 3: Alıcı ve Cüzdan

**İçerik:**
- **Alıcı belirleme:**
  - **Yöntem 1 (varsayılan):** "Alıcının Steam ID'si" input alanı
    - Format validation
    - "Steam ID nasıl bulunur?" yardım linki
  - **Yöntem 2 (admin aktif ettiyse):** "Açık link oluştur" toggle
    - Aktifleştirildiğinde Steam ID alanı gizlenir
    - Açıklama: "İlk kabul eden kişi alıcı olur"
    - Oluşturulan link opaque invite token kullanır: `/invite/:token` (transaction ID bazlı değil — enumeration koruması). Token tek kullanımlık, kabul sonrası geçersiz olur (06 §3.5 InviteToken)
- **Satıcı Ödeme Adresi** (satıcıya payout yapılacak TRC-20 adresi — 02 §12.1):
  - Profilde kayıtlı satıcı adresi varsa: ön doldurulmuş, değiştirilebilir
  - Profilde yoksa: boş, zorunlu
  - C11 (Wallet Address Input) bileşeni — label: "Ödeme Alacağınız Cüzdan Adresi"

#### Adım 4: Özet ve Onay

**İçerik:**

```
┌─────────────────────────────────┐
│  İşlem Özeti                    │
├─────────────────────────────────┤
│  Item:     [görsel] AK-47 | .. │
│  Fiyat:    100 USDT             │
│  Komisyon: 2 USDT (alıcı öder) │
│  Token:    USDT (TRC-20)        │
│  Timeout:  24 saat              │
│  Alıcı:    Steam ID: 7656...   │
│  Cüzdan:   TXyz...abc          │
├─────────────────────────────────┤
│  [Geri]          [İşlemi Başlat]│
└─────────────────────────────────┘
```

- Tüm bilgiler salt okunur gösterilir
- "Geri" butonu → önceki adımlara dönüş
- "İşlemi Başlat" butonu (birincil)
- Onay sonrası → loading → S07'ye yönlendirilir

**Genel Form Davranışı:**
- Adım göstergesi: `1 ── 2 ── 3 ── 4` (hangi adımda olduğu)
- Geri/ileri navigasyon — girilen veriler korunur
- Tarayıcı geri butonu → önceki adıma döner (veri kaybı yok)

**Error State'ler (Form Öncesi Engeller):**

| Engel | Görünüm |
|-------|---------|
| Eşzamanlı işlem limiti | "Aktif işlem limitinize ulaştınız (X/Y). Mevcut işlemleriniz tamamlandığında yeni işlem başlatabilirsiniz." |
| İptal cooldown'u | "Geçici işlem başlatma yasağınız var. Y süre sonra tekrar deneyebilirsiniz." + Countdown |
| Yeni hesap limiti | "Yeni hesap işlem limitinize ulaştınız (X/Y)." |
| MA aktif değil | S03 içeriği inline gösterilir |
| Satıcı payout-address cooldown | "Ödeme adresiniz yakın zamanda değiştirildi. Güvenlik nedeniyle Y süre boyunca yeni işlem başlatılamaz." + Countdown. Not: mevcut CREATED davetler eski snapshot adresle devam edebilir (S07 Cooldown Karar Matrisi) |
| Alıcı refund-address cooldown | "İade adresiniz yakın zamanda değiştirildi. Güvenlik nedeniyle Y süre boyunca yeni işlem başlatılamaz ve mevcut davetler kabul edilemez." + Countdown (03 §9.2) |
| Hesap flag'i aktif | "Hesabınız inceleme altında. Yeni işlem başlatılamaz." (02 §14.0) |

---

### 7.3 S07 — İşlem Detay

**Amaç:** Bir işlemin tüm detaylarını göstermek ve duruma göre aksiyon almak. Platformun en karmaşık ekranı.

**Erişim:**
- Public varyant: Herkese açık (sadece CREATED durumunda, sınırlı bilgi)
- Buyer/Seller varyant: İşlemin tarafları (giriş yapmış)
- İşlemin tarafı olmayan giriş yapmış kullanıcı: "Bu işleme erişiminiz yok"

**Sabit Layout (Tüm State'lerde):**

```
┌──────────────────────────────────────────────┐
│  Header (global)                             │
├──────────────────────────────────────────────┤
│  İşlem #1234          [Status Badge C01]     │
│  Transaction Timeline (C05)                  │
├──────────────────────────────────────────────┤
│  ┌─────────────────┐  ┌────────────────────┐ │
│  │  Item Card (C03) │  │  İşlem Bilgileri   │ │
│  │  Detailed        │  │  Fiyat: 100 USDT   │ │
│  │                  │  │  Komisyon: 2 USDT   │ │
│  │                  │  │  Toplam: 102 USDT   │ │
│  │                  │  │  Token: USDT TRC-20 │ │
│  └─────────────────┘  │  Ödeme Timeout: 24s │ │
│                        │  Oluşturma: tarih   │ │
│                        └────────────────────┘ │
│  Satıcı (C04) ←──────→ Alıcı (C04)          │
├──────────────────────────────────────────────┤
│  === Aksiyon Alanı (state'e göre değişir) === │
│                                              │
│  [Countdown Timer C02] (varsa)               │
│  [Aksiyon butonları / bilgi mesajları]       │
│                                              │
├──────────────────────────────────────────────┤
│  === İkincil Aksiyonlar ===                  │
│  [İtiraz Et] [İşlemi İptal Et] (koşullu)    │
└──────────────────────────────────────────────┘
```

#### S07 — State × Role Varyant Matrisi

##### CREATED Durumu

| Alan | Satıcı (Seller) | Alıcı (Buyer) | Public (Unauth) |
|------|-----------------|----------------|-----------------|
| Aksiyon alanı | "Alıcının kabul etmesi bekleniyor" | "Kabul Ediyorum" butonu (birincil) | "Bu işlemi kabul etmek için giriş yapın" + Login CTA |
| Countdown | Alıcı kabul timeout'u (C02) | Alıcı kabul timeout'u (C02) | Gösterilmez |
| Davet linki / bildirim bilgisi | Alıcı kayıtlı değilse: kopyalanabilir davet linki (C12). Alıcı kayıtlıysa: "Alıcıya bildirim gönderildi" bilgi metni | — | — |
| İade adresi | — | Profildeki iade adresi gösterilir (varsa, maskeli) + "Değiştir" linki → yalnızca bu işlem için geçerli adres değişikliği, profil adresi etkilenmez (02 §12.2). Yoksa C11 (Wallet Address Input) zorunlu alan. Adres olmadan "Kabul Ediyorum" devre dışı. | — |
| İptal | "İşlemi İptal Et" butonu aktif | "İşlemi İptal Et" butonu aktif | — |
| Ek bilgi | — | Satıcı skoru, item detayı, toplam tutar | Sınırlı: item adı, fiyat, satıcı adı |

> **Public varyant (unauthenticated):** Davet linkiyle gelen anonim kullanıcı sınırlı bilgi görür: item adı, görseli, fiyat, satıcı adı. Fiyat dışındaki detaylar (komisyon, timeout, cüzdan) gizlidir. "Giriş Yap ve Kabul Et" CTA'sı gösterilir.

> **Steam ID kontrolü (Yöntem 1):** Giriş yapmış kullanıcının Steam ID'si işlemdeki alıcı Steam ID'si ile eşleşmiyorsa → "Bu işlem size ait değil" uyarısı, kabul butonu devre dışı.

> **Açık link kontrolü (Yöntem 2):** Birisi zaten kabul ettiyse → "Bu işlem başka bir kullanıcı tarafından kabul edildi" mesajı.

##### ACCEPTED Durumu

| Alan | Satıcı | Alıcı |
|------|--------|-------|
| Aksiyon alanı | "Alıcı kabul etti! Item'ınızı gönderin." + "Platform trade offer'ınızı hazırlıyor..." bilgisi | "Satıcının item'ı göndermesi bekleniyor" |
| Countdown | Satıcı trade offer timeout'u (C02) | Satıcı trade offer timeout'u (C02) |
| İptal | "İşlemi İptal Et" aktif | "İşlemi İptal Et" aktif |

##### TRADE_OFFER_SENT_TO_SELLER Durumu

| Alan | Satıcı | Alıcı |
|------|--------|-------|
| Aksiyon alanı | "Steam'de trade offer'ı kabul edin" + Steam'e git linki | "Satıcının item'ı göndermesi bekleniyor" |
| Countdown | Satıcı trade offer timeout'u (C02) | Satıcı trade offer timeout'u (C02) |
| İptal | "İşlemi İptal Et" aktif | "İşlemi İptal Et" aktif |

##### ITEM_ESCROWED Durumu

| Alan | Satıcı | Alıcı |
|------|--------|-------|
| Aksiyon alanı | "Item emanete alındı. Alıcının ödemesi bekleniyor." | Ödeme bilgileri bölümü (aşağıda detaylı) |
| Countdown | Ödeme timeout'u (C02) | Ödeme timeout'u (C02) |
| İptal | "İşlemi İptal Et" aktif (ödeme gelmediği sürece) | "İşlemi İptal Et" aktif |

**Alıcı — Ödeme Bilgileri Bölümü:**

```
┌──────────────────────────────────┐
│  Ödeme Bilgileri                 │
├──────────────────────────────────┤
│  Ödeme Adresi:                   │
│  ┌──────────────────────────┐    │
│  │ TXyz...full_address      │[📋]│
│  └──────────────────────────┘    │
│                                  │
│  Gönderilecek Tutar: 102 USDT   │
│  Token: USDT (TRC-20)           │
│  Ağ: Tron                       │
│                                  │
│  ⏱ Kalan Süre: [Countdown C02]  │
│                                  │
│  ⚠ Uyarılar:                    │
│  • Sadece USDT (TRC-20) gönderin │
│  • Tam tutarı tek seferde gönderin│
│  • Farklı token göndermeyin      │
│  • Exchange'den gönderim yapmayın │
│    — iade adresinize ulaşamayabilir│
└──────────────────────────────────┘
```

##### PAYMENT_RECEIVED Durumu

| Alan | Satıcı | Alıcı |
|------|--------|-------|
| Aksiyon alanı | "Ödeme doğrulandı. Item alıcıya teslim ediliyor." | "Ödemeniz doğrulandı. Item'ınız gönderiliyor." |
| Countdown | — | — |
| İptal | Devre dışı | Devre dışı |

##### TRADE_OFFER_SENT_TO_BUYER Durumu

| Alan | Satıcı | Alıcı |
|------|--------|-------|
| Aksiyon alanı | "Item alıcıya gönderildi. Kabul etmesi bekleniyor." | "Steam'de trade offer'ı kabul edin" + Steam'e git linki |
| Countdown | Teslim trade offer timeout'u (C02) | Teslim trade offer timeout'u (C02) |
| İptal | Devre dışı | Devre dışı |

##### ITEM_DELIVERED Durumu

| Alan | Satıcı | Alıcı |
|------|--------|-------|
| Aksiyon alanı | "Item teslim edildi. Ödemeniz işleniyor." + loading | "Item'ınız teslim edildi!" |
| Countdown | — | — |

##### COMPLETED Durumu

| Alan | Satıcı | Alıcı |
|------|--------|-------|
| Aksiyon alanı | Ödeme özeti (aşağıda) | "İşlem başarıyla tamamlandı." |
| Ek bilgi | Blockchain tx hash (satıcıya gönderilen ödeme) | — |

**Satıcı — Ödeme Özeti (COMPLETED):**

> **Not:** Komisyon alıcı tarafından fiyata ek olarak ödenir — satıcının alacağından düşülmez (bkz. 02 §5). Gas fee ise önce komisyondan karşılanır; komisyonun admin tarafından belirlenen eşiğini (%10 varsayılan) aşan kısım satıcının alacağından kesilir (bkz. 02 §4.7).

```
┌──────────────────────────────────┐
│  Ödeme Özeti                     │
├──────────────────────────────────┤
│  Item Fiyatı:       100.00 USDT  │
│  Gas Fee (sizden):   -0.30 USDT  │
│  ──────────────────────────────  │
│  Net Ödeme:          99.70 USDT  │
│                                  │
│  Gas Fee Detay:                  │
│   Toplam gas fee:    0.50 USDT   │
│   Komisyondan:      -0.20 USDT   │
│   Sizden kesilen:    0.30 USDT   │
│                                  │
│  Gönderim Adresi: TXyz...abc     │
│  TX Hash: abc123... [📋]         │
│  Gönderim Tarihi: 2026-03-14     │
└──────────────────────────────────┘
```

**Varyant — Gas fee tamamen komisyondan karşılandığında:**

```
┌──────────────────────────────────┐
│  Ödeme Özeti                     │
├──────────────────────────────────┤
│  Item Fiyatı:       100.00 USDT  │
│  ──────────────────────────────  │
│  Net Ödeme:         100.00 USDT  │
│                                  │
│  Gönderim Adresi: TXyz...abc     │
│  TX Hash: abc123... [📋]         │
│  Gönderim Tarihi: 2026-03-14     │
└──────────────────────────────────┘
```

##### CANCELLED_* Durumları

| Alan | Satıcı | Alıcı |
|------|--------|-------|
| Aksiyon alanı | İptal bilgisi (sebep, tür) | İptal bilgisi (sebep, tür) |
| İade bilgisi (CANCELLED_TIMEOUT, ödeme sonrası) | "Item'ınız iade edildi" | İade özeti: orijinal tutar (fiyat + komisyon), gas fee kesintisi, net iade tutarı, tx hash |
| İade bilgisi (CANCELLED_TIMEOUT, ödeme öncesi) | "Item'ınız iade edildi" (emanetteyse) | — |
| İade bilgisi (CANCELLED_SELLER/BUYER, emanet sonrası) | "Item'ınız iade edildi" | — |
| İade bilgisi (CANCELLED_ADMIN) | Varlık transferi olduysa iade bilgisi gösterilir | Aynı |
| Ek bilgi (CANCELLED_ADMIN) | "İşleminiz admin tarafından reddedildi" + admin notu (varsa) | Aynı |

##### FLAGGED Durumu

| Alan | Satıcı | Alıcı |
|------|--------|-------|
| Banner | Turuncu: "İşleminiz incelemeye alındı. Sonuç size bildirilecektir." | Aynı |
| Aksiyonlar | Tümü devre dışı | Tümü devre dışı |
| Countdown | Donduruldu (C02 frozen state) | Donduruldu |

##### EMERGENCY_HOLD Durumu

| Alan | Satıcı | Alıcı |
|------|--------|-------|
| Banner | Kırmızı-Turuncu: "İşleminiz inceleme nedeniyle geçici olarak donduruldu. Süreç hakkında bilgilendirileceksiniz." | Aynı |
| Aksiyonlar | Tümü devre dışı | Tümü devre dışı |
| Countdown | Donduruldu (C02 frozen state) — "Süre donduruldu" etiketi gösterilir | Donduruldu |
| Timeline | "İşlem admin tarafından donduruldu" girişi (tarih, hold sebebi gösterilmez — güvenlik) | Aynı |

##### Suspended Session Override (tüm state'ler — S05 ve S07 ortak)

**Suspended Header:** Askıya alınmış oturumda global header yalnızca güvenli öğeleri gösterir: Logo (→ S05), Dil seçici (C10), Destek linki, Çıkış butonu. Korumalı navigasyon girişleri gizlenir: Bildirimler (S11), Profil (S08), Ayarlar (S10). Tıklama ile erişim denemesi → S03d'ye redirect.

**S07'de:** Tüm aksiyonlar devre dışı/gizli: "Kabul Ediyorum", "İşlemi İptal Et", "İtiraz Et", "Ödeme Sorunu Bildir", adres "Değiştir" linki. Üstte turuncu banner: "Hesabınız askıya alınmıştır. İşlem detaylarını görüntüleyebilirsiniz ancak aksiyon alamazsınız." İşlem bilgileri, timeline, countdown salt okunur görüntülenir.

#### S07 — Ödeme Edge Case Gösterimleri

| Senaryo | Alıcı Görünümü |
|---------|----------------|
| Eksik tutar gönderildi | Uyarı banner: "Eksik tutar gönderildi (X USDT). Ödemeniz iade edildi. Lütfen doğru tutarı gönderin: Y USDT" |
| Fazla tutar gönderildi | Bilgi banner: "Fazla tutar gönderildi. Z USDT iade edildi. İşlem devam ediyor." |
| Yanlış token gönderildi | Uyarı banner: "Yanlış token gönderildi. İade edildi. Lütfen USDT (TRC-20) gönderin." |
| Gecikmeli ödeme (iptal sonrası) | CANCELLED state'te bilgi: "Gecikmeli ödemeniz tespit edildi ve iade edildi." + tx hash |

#### S07 — Dispute Gösterimi

Dispute aktifken işlem detay sayfasında ek bir bölüm gösterilir:

```
┌──────────────────────────────────┐
│  ⚠ Aktif İtiraz                  │
├──────────────────────────────────┤
│  Tür: Ödeme itirazı              │
│  Durum: Otomatik kontrol yapıldı │
│  Sonuç: "Blockchain üzerinde     │
│   ödeme bulunamadı"              │
│                                  │
│  [TX Hash Gir]  [Admin'e İlet]   │
└──────────────────────────────────┘
```

#### S07 — Conditional Buton Kuralları

| Buton | Görünür Olma Koşulu | Aktif Olma Koşulu |
|-------|--------------------|--------------------|
| "Kabul Ediyorum" | Alıcı + CREATED | Steam ID eşleşiyor (veya açık link) + geçerli iade adresi mevcut (profil veya işlem bazlı) + alıcının refund-address cooldown'u aktif değil (cüzdan değişikliği sonrası bekleme süresi — 03 §9.2). Not: iptal cooldown'u yalnızca yeni işlem başlatmayı etkiler, kabul etmeyi etkilemez |
| "İşlemi İptal Et" | Satıcı veya alıcı + aktif state | Ödeme gönderilmemiş |
| "İtiraz Et" | Alıcı + ITEM_ESCROWED/PAYMENT_RECEIVED/TRADE_OFFER_SENT_TO_BUYER/ITEM_DELIVERED | Aktif dispute yok VE aynı türde daha önce çözülmüş dispute yok (bkz. 02 §10.2) |
| "Admin'e İlet" | Dispute sonucu gösterildikten sonra | — |

**Cooldown Karar Matrisi (03 §9.2, 02 §12.3):**

| Cooldown Türü | Yeni İşlem Başlat (S06) | İşlem Kabul Et (S07) | ACCEPTED+ Aktif İşlem |
|---------------|------------------------|---------------------|----------------------|
| Satıcı payout-address cooldown | Engellenir | Satıcı olarak etkisiz (alıcı aksiyonu). Mevcut CREATED davet satıcının eski adresiyle snapshot'lanmıştır — alıcı kabul edebilir, eski adresle devam eder | Eski adresle devam eder (snapshot prensibi — 02 §12.3) |
| Alıcı refund-address cooldown | Engellenir | Engellenir (alıcı yeni iade adresiyle kabul edemez) | Eski adresle devam eder (snapshot prensibi) |
| İptal cooldown'u | Engellenir | Etkilenmez | Etkilenmez |

---

### 7.4 S08 — Profil (Kendi)

**Amaç:** Kullanıcının kendi profil bilgilerini ve cüzdan adresini yönetmesi.

**Erişim:** Giriş yapmış kullanıcı.

**Bilgi Hiyerarşisi:**

1. **Profil Başlığı**
   - Steam avatar (büyük)
   - Kullanıcı adı
   - Steam ID (kopyalanabilir, C12)
   - Platformdaki hesap yaşı

2. **İtibar Skoru**
   - Genel skor (sayısal veya yıldız)
   - Detay:
     - Tamamlanan işlem sayısı
     - Başarılı işlem oranı (%)
     - İptal oranı (%)

3. **Cüzdan Adresleri**

   **Satıcı Ödeme Adresi** (satıcı olarak ödeme almak için):
   - Mevcut adres (kısmen maskeli gösterim: `TXyz...abc`)
   - "Tüm Adresi Göster" toggle
   - "Adresi Değiştir" butonu → ek doğrulama akışı (aşağıda)
   - Henüz adres yoksa: C11 (Wallet Address Input) + "Kaydet" butonu

   **Alıcı İade Adresi** (alıcı olarak iade almak için):
   - Aynı yapı: mevcut adres (maskeli), "Tüm Adresi Göster" toggle, "Adresi Değiştir" butonu
   - Henüz adres yoksa: C11 (Wallet Address Input) + "Kaydet" butonu
   - Bilgi notu: "İade adresi olmadan işlem kabul edemezsiniz"

4. **Hızlı Linkler**
   - "Hesap Ayarları" → S10
   - "İşlem Geçmişi" → S05

**Cüzdan Adresi Değişikliği Akışı:**
1. "Adresi Değiştir" tıkla
2. Steam ile tekrar doğrulama istenir (re-auth)
3. Kullanıcı Steam onayını tamamlar
4. Yeni adres giriş alanı açılır (C11)
5. Onay adımı: "Bu adres doğru mu?"
6. Kaydet
7. Uyarı notu: "Aktif işlemleriniz mevcut eski adresle tamamlanacaktır."

---

### 7.5 S09 — Profil (Başkası — Public)

**Amaç:** Başka bir kullanıcının public profil bilgilerini görüntülemek.

**Erişim:** Herkese açık (giriş zorunlu değil).

**Bilgi Hiyerarşisi:**

1. **Profil Başlığı**
   - Steam avatar
   - Kullanıcı adı
   - Platformdaki hesap yaşı

2. **İtibar Skoru**
   - Genel skor
   - Tamamlanan işlem sayısı
   - Başarılı işlem oranı (%)

**Gösterilmeyenler:**
- Cüzdan adresi
- İptal oranı detayı
- Steam ID (tam)
- Ayarlar veya düzenleme butonları

---

### 7.6 S10 — Hesap Ayarları

**Amaç:** Bildirim tercihleri, bağlı hesaplar, dil tercihi, hesap silme/deaktif.

**Erişim:** Giriş yapmış kullanıcı.

**Bölümler:**

#### Bildirim Tercihleri

| Kanal | Kontrol | Not |
|-------|---------|-----|
| Platform içi | Her zaman açık (devre dışı bırakılamaz) | — |
| Email | Toggle + email adresi input | Email doğrulama gerekli |
| Telegram | Toggle + bağlantı durumu | Bağlı değilse "Bağla" butonu |
| Discord | Toggle + bağlantı durumu | Bağlı değilse "Bağla" butonu |

#### Bağlı Hesaplar

| Hesap | Durum | Aksiyon |
|-------|-------|---------|
| Telegram | Bağlı / Bağlı Değil | "Bağla" → bot link + doğrulama kodu |
| Discord | Bağlı / Bağlı Değil | "Bağla" → Discord OAuth |

**Telegram bağlama akışı:**
1. "Bağla" tıkla
2. Platform bir doğrulama kodu gösterir
3. Telegram bot linkine yönlendirilir
4. Kullanıcı bot'a kodu gönderir
5. Platform doğrular → "Bağlandı" durumuna geçer

#### Dil Tercihi

- Dropdown: EN | 中文 | ES | TR
- Değişiklik anında kaydedilir

#### Hesap Yönetimi

**Hesabı Deaktif Et:**
1. "Hesabı Deaktif Et" butonu (gri)
2. Aktif işlem kontrolü → varsa hata mesajı
3. Onay modal: "Hesabınız deaktif edilecek. Tekrar giriş yaparak aktif edebilirsiniz."
4. "Deaktif Et" + "Vazgeç"

**Hesabı Sil:**
1. "Hesabı Sil" butonu (kırmızı)
2. Aktif işlem kontrolü → varsa hata mesajı
3. Ciddi uyarı modal: "Bu işlem geri alınamaz. Tüm kişisel verileriniz silinecek. İşlem geçmişiniz anonim olarak saklanacaktır."
4. Onay: Kullanıcı "SİL" yazarak onaylar (input confirmation)
5. "Hesabı Sil" + "Vazgeç"

---

### 7.7 S11 — Bildirimler

**Amaç:** Tüm platform bildirimlerini listelemek.

**Erişim:** Giriş yapmış kullanıcı.

**Bilgi Hiyerarşisi:**

1. **Üst bar:** "Tüm Bildirimleri Okundu İşaretle" linki
2. **Bildirim listesi** (kronolojik, en yeni üstte)

**Bildirim Satırı:**
- Okunmamış göstergesi (mavi nokta)
- Bildirim ikonu (türe göre)
- Bildirim metni
- Zaman damgası (göreli: "5 dk önce", "2 saat önce", "dün")
- Tıklanabilir → ilgili ekrana yönlendirir (genellikle S07)

**Bildirim Türleri ve İkonları:**

| Tür | İkon | Örnek |
|-----|------|-------|
| İşlem güncellemesi | 🔄 | "Alıcı işlemi kabul etti" |
| Ödeme | 💰 | "Ödeme doğrulandı" |
| Uyarı | ⚠ | "Süreniz dolmak üzere" |
| Tamamlanma | ✅ | "İşlem tamamlandı" |
| İptal | ❌ | "İşlem iptal oldu" |
| Flag | 🔍 | "İşleminiz incelemeye alındı" |

**State'ler:**

| State | Görünüm |
|-------|---------|
| Bildirim yok | Empty state (C13): "Bildiriminiz yok" |
| Yeni bildirimler var | Okunmamışlar üstte, vurgulu |
| Yükleniyor | Skeleton (C14) |

**Pagination:** C16 — sayfa başına 20 bildirim.

---

## 8. Admin Ekranları

### 8.1 S12 — Admin Dashboard

**Amaç:** Admin'in platform durumunu tek bakışta görmesi.

**Erişim:** Admin rolü olan kullanıcılar.

**Layout:**

```
┌──────────────────────────────────────────────┐
│  Admin Header: Logo | Admin Adı | Çıkış      │
├──────────────────────────────────────────────┤
│  Sol Menü         │  Dashboard İçeriği        │
│  ┌─────────────┐  │                           │
│  │ Dashboard    │  │  ┌─────┐ ┌─────┐ ┌─────┐│
│  │ Flag'ler     │  │  │Aktif│ │Flag │ │Günlük││
│  │ İşlemler     │  │  │ 42  │ │ 5   │ │ 128 ││
│  │ Ayarlar      │  │  └─────┘ └─────┘ └─────┘│
│  │ Steam Hsb    │  │                           │
│  │ Roller       │  │  Steam Hesapları Durumu   │
│  │ Audit Log    │  │                           │
│  │              │  │  ┌──────────────────────┐ │
│  │              │  │  │ Hsb1: ✅ | Hsb2: ✅  │ │
│  │              │  │  │ Hsb3: ⚠️ kısıtlı     │ │
│  │              │  │  └──────────────────────┘ │
│  │              │  │                           │
│  │              │  │  Son Flag'lenmiş İşlemler │
│  │              │  │  (son 5, tablo)           │
│  └─────────────┘  │                           │
└──────────────────────────────────────────────┘
```

**Özet Kartları:**

| Kart | Değer | Tıklanabilir |
|------|-------|--------------|
| Aktif İşlemler | Toplam sayı | → S15 (aktif filtre) |
| Bekleyen Flag'ler | Sayı (kırmızı badge — acil) | → S13 |
| Günlük Tamamlanan | Sayı | → S15 (bugün filtre) |
| Haftalık Tamamlanan | Sayı | → S15 (bu hafta filtre) |

**Steam Hesapları Durumu:**
- Her hesap: ad + durum ikonu (✅ aktif, ⚠ kısıtlı, ❌ banned)
- Kısıtlı/banned hesap varsa kart vurgulu (kırmızı border)
- Tıklanabilir → S18

**Son Flag'lenmiş İşlemler:**
- Son 5 flag (tablo: ID, tür, tarih, durum)
- "Tümünü Gör" linki → S13

---

### 8.2 S13 — Flag Kuyruğu

**Amaç:** Flag'lenmiş işlemleri listelemek ve filtrelemek.

**Erişim:** Admin (flag görüntüleme yetkisi).

**İçerik:**

**Filter Bar (C17):**
- Flag kategorisi: Tümü / İşlem Flag'leri / Hesap Flag'leri
- Flag türü: Tümü / Fiyat Sapması / Yüksek Hacim / Anormal Davranış / Çoklu Hesap / Sanctions Match (02 §14.0, §21.1)
- Durum (kategori duyarlı):
  - İşlem flag'leri: Bekliyor / Devam Etti / İptal Edildi
  - Hesap flag'leri: Bekliyor / Kaldırıldı / Askıya Alındı / Hold Uygulandı
- Tarih aralığı

**Tablo Kolonları — İşlem Flag'leri:**

| Kolon | Açıklama |
|-------|----------|
| İşlem ID | Tıklanabilir → S14 |
| Flag Türü | Fiyat Sapması / Yüksek Hacim / Anormal Davranış |
| Kullanıcı | Satıcı adı + avatar |
| Item | Item adı (kısa) |
| Tutar | Fiyat + stablecoin |
| Piyasa Fiyatı | Referans fiyat (fiyat sapması flag'i için) |
| Tarih | Oluşturulma tarihi |
| Durum | Bekliyor / Devam Etti / İptal Edildi |

**Tablo Kolonları — Hesap Flag'leri:**

| Kolon | Açıklama |
|-------|----------|
| Kullanıcı | Kullanıcı adı + avatar → S20 |
| Flag Türü | Çoklu Hesap / Anormal Davranış / Sanctions Match |
| Sinyal Detayı | Eşleşen adres, IP/cihaz bilgisi (kısa) |
| İlişkili Hesaplar | Eşleşen diğer hesap sayısı |
| Aktif İşlem Sayısı | Kullanıcının mevcut aktif işlem sayısı |
| Tarih | Flag oluşturulma tarihi |
| Durum | Bekliyor / Kaldırıldı / Askıya Alındı / Hold Uygulandı |

**Sıralama:** Varsayılan olarak en yeni üstte. Kolon başlıkları tıklanarak sıralama değiştirilebilir.

**Pagination:** C16

**Bekleyen flag sayısı:** Sayfa başlığında vurgulu gösterilir: "Flag Kuyruğu (5 bekleyen)"

---

### 8.3 S14 — Flag Detay / İnceleme

**Amaç:** Tek bir flag'lenmiş işlemi incelemek ve karar vermek.

**Erişim:** Admin (flag yönetim yetkisi).

**Bilgi Hiyerarşisi:**

1. **Flag Bilgisi**
   - Flag türü (badge)
   - Flag sebebi detayı:
     - Fiyat sapması: "Girilen fiyat: 100 USDT, Piyasa fiyatı: 50 USDT, Sapma: %100"
     - Yüksek hacim: "Son 24 saatte X işlem, toplam Y USDT"
     - Anormal davranış: Tespit edilen patern açıklaması
     - Çoklu hesap: "Aynı cüzdan adresi: TXyz...abc — Hesap A, Hesap B" veya "Aynı IP/cihaz: Hesap A, Hesap B"

2. **İşlem Detayları**
   - İşlem ID, durum, oluşturulma tarihi
   - Item (C03 Detailed)
   - Fiyat, stablecoin, timeout

3. **Taraf Bilgileri**
   - **Satıcı:** Avatar, ad, skor, işlem sayısı, hesap yaşı, son işlemler → S20 linki
   - **Alıcı:** Aynı bilgiler (alıcı belirlendiyse)
   - İki taraf arasındaki geçmiş işlem sayısı (wash trading göstergesi)

4. **Aksiyon Alanı**

```
┌──────────────────────────────────┐
│  Admin Notu (opsiyonel textarea) │
├──────────────────────────────────┤
│  [İşleme Devam Et ✓] [İptal Et ✗]│
│  (yeşil)               (kırmızı) │
└──────────────────────────────────┘
```

**İşleme Devam Et** (eski: Onayla): Flag yanlış alarm — işlem flag'lenmeden önceki durumuna döner ve normal akışla devam eder. Taraflara bildirim gider.
**İptal Et** (eski: Reddet): Flag doğrulanmış — işlem iptal edilir, taraflara bildirim gider.

Her iki aksiyonda onay modal'ı: "Bu işlemi [devam ettirmek/iptal etmek] istediğinize emin misiniz?"

> **Terminoloji notu:** Buton isimleri işlem sonucunu yansıtır, flag kararını değil. "Devam Et" = flag false positive, "İptal Et" = fraud doğrulandı.

#### S14 — Hesap Flag Varyantı

S13'te hesap flag'i seçildiğinde S14 farklı bir layout gösterir:

**İçerik:**
1. **Flag Bilgisi:** Flag türü (Çoklu Hesap / Anormal Davranış / Sanctions Match), sinyal detayı (eşleşen adresler, IP/cihaz bilgisi, sanctions match için: eşleşen cüzdan adresi + yaptırım listesi kaynağı)
2. **Kullanıcı Bilgileri:** Avatar, ad, skor, işlem sayısı, hesap yaşı → S20 linki
3. **İlişkili Hesaplar:** Eşleşen diğer hesaplar listesi (tıklanabilir → S20)
4. **Aktif İşlemler:** Kullanıcının mevcut aktif işlem sayısı ve listesi

**Aksiyon Alanı:**

```
┌────────────────────────────────────────────┐
│  Admin Notu (opsiyonel textarea)            │
├────────────────────────────────────────────┤
│ [Flag Kaldır ✓] [Askıya Al ⚠] [Hold ⛔]   │
│ (yeşil)         (turuncu)      (kırmızı)   │
└────────────────────────────────────────────┘
```

**Flag Kaldır:** False alarm — kullanıcı kısıtlamaları kalkar, fon akışı aksiyonları tekrar aktif olur.
**Askıya Al:** Hesap askıya alınır — kullanıcı fon akışı aksiyonlarını kullanamaz (S03d kısıtlı oturum). Mevcut aktif işlemlerin otomatik/Steam-side adımları (ödeme doğrulama, trade offer gönderimi vb.) devam eder. Ancak kullanıcı aksiyonu gerektiren adımlarda (ödeme gönderme, trade offer kabul, işlem kabul) kullanıcı platform tarafında aksiyon alamaz — bu adımlar timeout'a düşer veya Steam tarafında bağımsız ilerler. Admin, gerekirse aktif işlemlere ayrıca EMERGENCY_HOLD uygulayabilir.
**Aktif İşlemleri Hold'a Al:** Kullanıcının tüm aktif işlemlerine EMERGENCY_HOLD uygulanır (yüksek risk durumlarında — 03 §8.8).

---

### 8.4 S15 — İşlem Listesi & Arama

**Amaç:** Tüm işlemleri listelemek, filtrelemek, aramak.

**Erişim:** Admin (işlem görüntüleme yetkisi).

**Filter Bar (C17):**
- Durum: Tümü / Aktif / Tamamlanan / İptal / Flag'lenmiş
- Tarih aralığı: Başlangıç — Bitiş
- Kullanıcı: Steam ID veya kullanıcı adı (arama)
- Tutar aralığı: Min — Max
- Stablecoin: USDT / USDC / Tümü

**Tablo Kolonları:**

| Kolon | Açıklama |
|-------|----------|
| İşlem ID | Tıklanabilir → S16 |
| Item | Görsel (küçük) + ad |
| Fiyat | Tutar + stablecoin |
| Satıcı | Ad + avatar, tıklanabilir → S20 |
| Alıcı | Ad + avatar (varsa), tıklanabilir → S20 |
| Durum | Status badge (C01) |
| Oluşturulma | Tarih |
| Tamamlanma/İptal | Tarih (varsa) |

**Sıralama:** Kolon başlıklarına tıklanarak. Varsayılan: en yeni üstte.

**Pagination:** C16

---

### 8.5 S16 — İşlem Detay (Admin)

**Amaç:** Tek bir işlemin tam admin görünümü.

**Erişim:** Admin (işlem görüntüleme yetkisi).

**İçerik:**

S07'deki tüm bilgiler + admin'e özel ek bilgiler:

1. **İşlem Bilgileri** (S07 ile aynı)
   - Item, fiyat, stablecoin, komisyon, taraflar

2. **Durum Geçmişi (Timeline)**
   - Her state değişikliği: tarih/saat + eski durum → yeni durum
   - Örn: "2026-03-14 14:32 — CREATED → ACCEPTED"

3. **Taraf Detayları**
   - Satıcı: Ad, skor, tıklanabilir → S20
   - Alıcı: Ad, skor, tıklanabilir → S20

4. **Ödeme Detayları** (ödeme yapıldıysa)
   - Alıcının gönderdiği tutar
   - Blockchain tx hash (tıklanabilir → explorer)
   - Doğrulama zamanı

5. **Satıcıya Ödeme Detayları** (tamamlandıysa)
   - Brüt tutar, komisyon, gas fee, net tutar
   - Blockchain tx hash
   - Gönderim zamanı

6. **İade Detayları** (iptal olduysa)
   - İade edilen varlıklar (item ve/veya para)
   - İade tx hash'leri

7. **Bildirim Geçmişi**
   - Gönderilen tüm bildirimler: tarih, alıcı, kanal, içerik

8. **Dispute Geçmişi** (varsa)
   - Dispute türü, açılma tarihi, otomatik kontrol sonucu, eskalasyon durumu

9. **Flag Geçmişi** (varsa)
   - Flag türü, tarih, admin kararı, admin notu

**Aksiyonlar:**
- FLAGGED durumundaysa: yalnızca "İşleme Devam Et" / "İptal Et" butonları gösterilir (S14 terminolojisi). Genel "İşlemi İptal Et" butonu FLAGGED state'te gizlenir — flag resolution aksiyonları ile operasyonel cancel karışmasını önlemek için
- CREATED → TRADE_OFFER_SENT_TO_BUYER arası aktif state'lerde (FLAGGED hariç — FLAGGED için yukarıdaki flag resolution aksiyonları geçerlidir): "İşlemi İptal Et" butonu (kırmızı) — `CANCEL_TRANSACTIONS` yetkisi gerektirir. Tıklandığında iptal modal'ı açılır: sebep (zorunlu textarea) + iade bilgisi + onay/vazgeç butonları (03 §8.7)
- ITEM_DELIVERED state'inde: "İşlemi İptal Et" butonu görünmez. Bunun yerine "Exceptional Resolution Başlat" butonu (turuncu) — item alıcıya teslim edilmiş, standart iptal uygulanamaz (02 §7)
- Tüm aktif state'lerde: "Emergency Hold Uygula" butonu (sarı) — `EMERGENCY_HOLD` yetkisi gerektirir. Tıklandığında hold modal'ı açılır: sebep (zorunlu textarea) + onay/vazgeç butonları (03 §8.8)
- EMERGENCY_HOLD state'inde: "Hold Kaldır (Devam)" ve "Hold Kaldır (İptal Et)" butonları + hold sebebi ve süresi gösterilir. Otomatik tetiklenen hold'larda (sanctions match) "Auto-Hold — Sanctions Match" etiketi gösterilir. ITEM_DELIVERED'da "İptal Et" → exceptional resolution akışına yönlendirir (03 §8.8)
- Terminal state'lerde (COMPLETED, CANCELLED_*): salt okunur

---

### 8.6 S17 — Parametre Yönetimi

**Amaç:** Platform parametrelerini görüntülemek ve düzenlemek.

**Erişim:** Admin (parametre yönetim yetkisi).

**Yapı:** Kategorize edilmiş parametre grupları. Her parametre inline düzenlenebilir.

#### Timeout Süreleri

| Parametre | Açıklama | Tür | Birim |
|-----------|----------|-----|-------|
| Alıcı kabul timeout'u | Alıcının işlemi kabul etme süresi | Sayı | Saat |
| Satıcı trade offer timeout'u | Satıcının item'ı gönderme süresi | Sayı | Saat |
| Ödeme timeout — minimum | Satıcının seçebileceği minimum süre | Sayı | Saat |
| Ödeme timeout — maksimum | Satıcının seçebileceği maksimum süre | Sayı | Saat |
| Ödeme timeout — varsayılan | Ön seçili süre | Sayı | Saat |
| Teslim trade offer timeout'u | Alıcının teslim kabul süresi | Sayı | Saat |
| Timeout uyarı eşiği | Süre dolmadan ne zaman uyarı gönderileceği (oran olarak). Aynı eşik C02 countdown timer renk değişimi için de kullanılır | Sayı | % |

#### Komisyon

| Parametre | Açıklama | Tür | Birim |
|-----------|----------|-----|-------|
| Komisyon oranı | Alıcıdan alınan komisyon | Sayı | % |

#### İşlem Limitleri

| Parametre | Açıklama | Tür | Birim |
|-----------|----------|-----|-------|
| Minimum işlem tutarı | İzin verilen en düşük fiyat | Sayı | USD |
| Maksimum işlem tutarı | İzin verilen en yüksek fiyat | Sayı | USD |
| Eşzamanlı aktif işlem limiti | Kullanıcı başına | Sayı | Adet |

#### İptal Kuralları

| Parametre | Açıklama | Tür | Birim |
|-----------|----------|-----|-------|
| İptal limiti | X iptalden sonra cooldown | Sayı | Adet |
| İptal periyodu | İptal sayısının sayıldığı süre | Sayı | Gün |
| Cooldown süresi | Geçici yasak süresi | Sayı | Saat |

#### Yeni Hesap

| Parametre | Açıklama | Tür | Birim |
|-----------|----------|-----|-------|
| Yeni hesap işlem limiti | İlk N gün içinde max işlem | Sayı | Adet |
| Yeni hesap süresi | Kaç gün "yeni" sayılır | Sayı | Gün |

#### Gas Fee

| Parametre | Açıklama | Tür | Birim |
|-----------|----------|-----|-------|
| Koruma eşiği | Gas fee komisyonun bu oranını aşarsa karşı taraftan kesilir | Sayı | % |

#### Fraud Tespiti

| Parametre | Açıklama | Tür | Birim |
|-----------|----------|-----|-------|
| Piyasa fiyatı sapma eşiği | Bu orandan fazla sapma → flag | Sayı | % |
| Yüksek hacim tutarı | Bu tutarı aşan işlem hacmi → flag | Sayı | USD |
| Yüksek hacim periyodu | Hacim sayıldığı süre | Sayı | Saat |

#### Alıcı Belirleme

| Parametre | Açıklama | Tür |
|-----------|----------|-----|
| Açık link (Yöntem 2) | Aktif / Pasif | Toggle |

#### Erişim ve Uyumluluk (02 §21.1)

| Parametre | Açıklama | Tür | Birim |
|-----------|----------|-----|-------|
| Geo-blocking | Aktif / Pasif | Toggle | — |
| Yasaklı ülke listesi | Engellenen ülke kodları | Liste | ISO 3166 |
| Sanctions screening | Aktif / Pasif | Toggle | — |
| Yaptırımlı adres listesi | Engellenen cüzdan adresleri | Liste | TRC-20 |
| Minimum yaş | Platform kullanım yaşı | Sayı | Yıl |
| VPN tespiti | Aktif / Pasif (destekleyici sinyal) | Toggle | — |

#### Blockchain Health Check

| Parametre | Açıklama | Tür | Birim |
|-----------|----------|-----|-------|
| Blockchain health check | Aktif / Pasif | Toggle | — |
| Health check aralığı | Kontrol sıklığı | Sayı | Saniye |
| Otomatik timeout freeze | Health check başarısız olunca otomatik freeze | Toggle | — |

**Her Parametre Satırı:**
- Parametre adı + açıklama
- Mevcut değer
- "Düzenle" ikonu → inline edit mode
- Kaydet / İptal butonları
- Kaydetme sonrası: toast bildirimi (C09): "Parametre güncellendi"

**Etki Kapsamı Notu:**
- **Yalnızca yeni işlem parametreleri** (timeout süreleri, komisyon oranı, işlem limitleri, iptal kuralları, yeni hesap limiti, gas fee eşiği, fraud eşikleri, alıcı belirleme): Değişiklikler yeni işlemler için geçerli olur, aktif işlemleri etkilemez.
- **Runtime etkili parametreler** (geo-blocking, sanctions screening, yaş kontrolü, blockchain health check): Değişiklikler anında aktif olur, mevcut oturumları ve aktif işlemleri de etkiler. **Aktif işlem davranışı:** Sanctions eşleşmesi → kullanıcının tüm aktif işlemlerine otomatik EMERGENCY_HOLD (03 §11a.3). Geo-blocking/yaş değişiklikleri → yeni oturumları etkiler, aktif işlemler mevcut state'te devam eder (kullanıcı zaten doğrulanmış). Blockchain health check → ödeme adımında timeout freeze (03 §11.3).
- **Destekleyici sinyal parametreleri** (VPN tespiti): Yalnızca risk skorlama ve flag değerlendirmesinde kullanılır. Tek başına blocker veya EMERGENCY_HOLD tetikleyicisi değildir (02 §21.1). Aktif işlemlere doğrudan etkisi yoktur.

Bu ayrım sayfanın üstünde bir bilgi kutusu olarak gösterilir. Her parametre satırında etki kapsamı (yeni işlem / runtime) etiketi bulunur.

---

### 8.7 S18 — Platform Steam Hesapları

**Amaç:** Platform Steam hesaplarının durumunu izlemek.

**Erişim:** Admin (Steam hesap izleme yetkisi).

**İçerik:** Her hesap için bir kart.

**Hesap Kartı:**

```
┌──────────────────────────────────┐
│  🎮 Platform Hesap 1             │
│  Steam ID: 7656...               │
├──────────────────────────────────┤
│  Durum:    ✅ Aktif               │
│  Emanet:   12 item               │
│  Günlük Trade: 45 / 200          │
│  Son Kontrol: 5 dk önce          │
└──────────────────────────────────┘
```

**Durum Göstergeleri:**

| Durum | Görünüm |
|-------|---------|
| Aktif | Yeşil ✅ — normal |
| Kısıtlı | Turuncu ⚠ — kart vurgulu, uyarı mesajı |
| Banned | Kırmızı ❌ — kart vurgulu, acil uyarı |

**Kısıtlı/Banned Hesap Uyarısı:**
- Uyarı banner: "X hesap kısıtlı. Yeni işlemler diğer hesaplara yönlendirildi."
- Kısıtlı hesabın emanetindeki item'lar listelenir
- Emanette item varsa ek uyarı: "Bu hesapta Y aktif işlem için emanette item bulunuyor. Bu işlemler otomatik devam ettirilemez — recovery/manual intervention gerektirir." (02 §15, 03 §11.2a)

**Recovery Queue (emanetteki item'larla ilgili aktif işlemler):**

| Kolon | Açıklama |
|-------|----------|
| İşlem ID | Tıklanabilir → S16 |
| Item | Emanetteki item adı |
| Satıcı / Alıcı | Taraflar |
| İşlem State | Mevcut state (kısıtlama öncesi) |
| Recovery Durumu | Bekliyor / İnceleniyor / Çözüldü |
| Sorumlu Admin | Atanan admin (opsiyonel) |
| Admin Notu | İnceleme notları |

**Aksiyonlar:**
- "İncele" → S16'da işlem detayına git
- "Not Ekle" → admin inceleme notu
- "Sorumlu Admin Ata/Değiştir" → dropdown'dan admin seçimi, atama AuditLog'a kaydedilir
- "EMERGENCY_HOLD Uygula" → işlem henüz hold'da değilse, timeout dondurulur (herhangi bir aktif state'te uygulanabilir)
- "İptal Et" → standart admin iptal kuralları (yalnızca CREATED → TRADE_OFFER_SENT_TO_BUYER arası)
- "Manual Recovery Başlat" → bot recovery'ye özel akış: Steam support talebi oluşturma notu, alternatif bot'tan teslim deneme, kullanıcı iletişim. Bu, S16'daki exceptional resolution'dan ayrıdır — exceptional resolution ITEM_DELIVERED sonrası iptal imkansızlığı için, manual recovery ise bot kısıtlaması nedeniyle emanet item'a erişim kaybı için kullanılır.
- Tüm aksiyonlar AuditLog'a kaydedilir.

---

### 8.8 S19 — Rol & Yetki Yönetimi

**Amaç:** Admin rollerini ve yetkilerini yönetmek.

**Erişim:** Sadece süper admin.

**İçerik:**

#### Roller Listesi

| Kolon | Açıklama |
|-------|----------|
| Rol Adı | Tıklanabilir → yetki düzenleme |
| Açıklama | Rolün kısa tanımı |
| Atanmış Kullanıcı | Sayı |
| Aksiyonlar | Düzenle / Sil |

**"Yeni Rol Oluştur" butonu** → modal:
- Rol adı (zorunlu)
- Açıklama (opsiyonel)
- Yetki seçimi (checkbox listesi)

#### Yetki Matrisi

| Yetki | Açıklama |
|-------|----------|
| Flag'leri görüntüle | S13, S14'e erişim |
| Flag'leri yönet | İşlem flag: Devam Et / İptal Et aksiyonları. Hesap flag: Flag Kaldır / Askıya Al / Hold aksiyonları |
| İşlemleri görüntüle | S15, S16'ya erişim |
| Parametreleri yönet | S17'ye erişim |
| Steam hesaplarını görüntüle | S18'e erişim (salt okunur) |
| Steam recovery yönet | S18'de Manual Recovery Başlat, not düşme, sorumlu admin atama (fon/item güvenliği etkili — görüntüleme yetkisinden ayrı) |
| Kullanıcı detay görüntüle | S20'ye erişim |
| Rolleri yönet | S19'a erişim (sadece süper admin) |
| Audit log görüntüle | S21'e erişim |
| İşlemleri iptal et | S16'da aktif işlemleri doğrudan iptal etme — CREATED → TRADE_OFFER_SENT_TO_BUYER arası (03 §8.7) |
| Emergency hold uygula/kaldır | S16'da aktif işlemlere emergency hold uygulama ve kaldırma (03 §8.8) |

#### Kullanıcı-Rol Atama

- Admin kullanıcı listesi
- Her kullanıcı yanında: atanmış rol
- "Rol Ata" / "Rol Değiştir" dropdown

---

### 8.9 S20 — Kullanıcı Detay (Admin)

**Amaç:** Admin'in tek bir kullanıcıyı derinlemesine incelemesi.

**Erişim:** Admin (kullanıcı detay görüntüleme yetkisi).

**Bilgi Hiyerarşisi:**

1. **Kullanıcı Profili**
   - Steam avatar, ad, Steam ID
   - Platformdaki hesap yaşı
   - Hesap durumu: Aktif / Askıya Alındı / Deaktif / Silinmiş. Badge'ler ayrı: askıya alınmış + aktif işlem varsa "Aktif İşlem Var" (sarı); gerçekten EMERGENCY_HOLD uygulanmışsa "Hold Altında Aktif İşlem Var" (kırmızı). İkisi farklı durumlar — askıya alma otomatik hold uygulamaz (S14 ayrı aksiyon)
   - İtibar skoru (detaylı breakdown)

2. **İstatistikler**
   - Toplam işlem sayısı
   - Tamamlanan / İptal / Flag'lenmiş işlem sayıları
   - Başarılı işlem oranı
   - Toplam işlem hacmi (USD)
   - Son işlem tarihi

3. **Cüzdan Adresi Geçmişi**
   - Mevcut adres
   - Önceki adresler (değişiklik tarihleriyle)

4. **İşlem Geçmişi (Tablo)**
   - Son işlemler listesi (tablo formatı, S15 ile aynı kolonlar)
   - Her satır tıklanabilir → S16
   - "Tümünü Gör" → S15 (kullanıcı filtreli)

5. **Flag Geçmişi**
   - Bu kullanıcıyı içeren flag'ler
   - Flag türü, tarih, sonuç

6. **Dispute Geçmişi**
   - Bu kullanıcının taraf olduğu dispute'lar
   - Dispute türü, işlem ID (tıklanabilir → S16), tarih, sonuç (otomatik çözüm / eskalasyon)

7. **Alıcı-Satıcı İlişkileri**
   - En sık işlem yaptığı karşı taraflar (wash trading tespiti için)
   - Her çift: karşı taraf adı, işlem sayısı, son işlem tarihi

---

### 8.10 S21 — Audit Log

**Amaç:** Platform üzerindeki fon hareketlerini, admin aksiyonlarını ve güvenlik olaylarını kronolojik olarak görüntülemek (bkz. 02 §16.2).

**Erişim:** Admin (audit log görüntüleme yetkisi).

**Filter Bar (C17):**
- Kategori: Tümü / Fon Hareketleri / Admin Aksiyonları / Güvenlik Olayları
- Tarih aralığı: Başlangıç — Bitiş
- Kullanıcı: Steam ID veya kullanıcı adı (arama)
- İşlem ID: Belirli bir işlemle ilgili loglar

**Tablo Kolonları:**

| Kolon | Açıklama |
|-------|----------|
| Tarih/Saat | Log zamanı |
| Kategori | Fon Hareketi / Admin Aksiyonu / Güvenlik Olayı |
| Aksiyon | Yapılan işlem (örn: "Satıcıya ödeme gönderildi", "İşlem flag — devam kararı verildi", "Hesap flag kaldırıldı", "Sanctions match — auto-hold uygulandı", "Cüzdan adresi değiştirildi") |
| Kullanıcı | İlgili kullanıcı (varsa), tıklanabilir → S20 |
| İşlem ID | İlgili işlem (varsa), tıklanabilir → S16 |
| Detay | Ek bilgi (tutar, tx hash, eski/yeni değer vb.) |

**Sıralama:** Varsayılan olarak en yeni üstte. Kolon başlıkları tıklanarak sıralama değiştirilebilir.

**Pagination:** C16

**State'ler:**

| State | Görünüm |
|-------|---------|
| Log var | Tablo görünümü |
| Filtre sonucu boş | "Filtrelere uygun kayıt bulunamadı" |
| Yükleniyor | Skeleton (C14) |

---

## 9. Responsive Tasarım Notları

### 9.1 Breakpoint'ler

| Breakpoint | Genişlik | Hedef |
|-----------|----------|-------|
| Desktop | ≥ 1024px | Birincil deneyim |
| Tablet | 768–1023px | Uyumlu layout |
| Mobil | < 768px | Temel kullanılabilirlik |

### 9.2 Ekran Bazlı Responsive Kurallar

| Ekran | Desktop | Tablet | Mobil |
|-------|---------|--------|-------|
| S05 Dashboard | İşlem listesi + sağ panel (istatistikler) | Tek kolon, istatistikler üstte | Tek kolon, kompakt kart |
| S06 İşlem Oluşturma | Merkezi form (max 600px) | Aynı | Tam genişlik |
| S07 İşlem Detay | Item sol, bilgiler sağ | Tek kolon | Tek kolon, kompakt |
| S12-S21 Admin | Sol menü + içerik | Hamburger menü + içerik | Hamburger menü + tam genişlik |
| Tablolar (S13, S15) | Tam tablo | Yatay scroll | Kart görünümüne geçiş |

### 9.3 C05 Transaction Timeline

- Desktop/Tablet: Yatay 8 adım
- Mobil: Dikey timeline (adımlar alt alta)

### 9.4 Tablo → Kart Dönüşümü

Mobilde tablo kolonları kart içine alınır:

```
Desktop:
| ID | Item | Fiyat | Durum | Tarih |

Mobil:
┌─────────────────────┐
│ #1234        [Badge] │
│ AK-47 | 100 USDT    │
│ 2026-03-14           │
└─────────────────────┘
```

---

## 10. Lokalizasyon Notları

### 10.1 Metin Uzunluk Farkları

| Dil | Ortalama Uzunluk (EN baz) | Etki |
|-----|--------------------------|------|
| EN | 1x (baz) | — |
| TR | ~1.3x | Buton ve badge genişliği etkilenir |
| ES | ~1.3x | Aynı etki |
| 中文 | ~0.6x | Daha kompakt, ama karakter genişliği farklı |

**Tasarım kuralı:** Tüm metin alanları EN'nin 1.5x uzunluğuna kadar kırılmadan destek vermeli. Badge ve buton genişlikleri metin uzunluğuna göre esnemeli.

### 10.2 Tarih/Saat Formatı

| Dil | Tarih Formatı | Saat Formatı |
|-----|--------------|-------------|
| EN | Mar 14, 2026 | 2:30 PM |
| TR | 14 Mar 2026 | 14:30 |
| ES | 14 mar 2026 | 14:30 |
| 中文 | 2026年3月14日 | 14:30 |

### 10.3 Sayı Formatı

| Dil | Binlik Ayracı | Ondalık Ayracı | Örnek |
|-----|-------------|---------------|-------|
| EN | , | . | 1,234.56 |
| TR | . | , | 1.234,56 |
| ES | . | , | 1.234,56 |
| 中文 | , | . | 1,234.56 |

> **Not:** Stablecoin tutarları her zaman `.` (nokta) ile gösterilir — blockchain standardı. Sadece non-crypto tutarlar lokalize formatı kullanır.

### 10.4 Çevrilmeyecek Terimler

Aşağıdaki terimler tüm dillerde İngilizce kalır:
- USDT, USDC, TRC-20, Tron
- Steam, Steam ID, Mobile Authenticator
- Trade offer
- CS2
- Gas fee

---

*Skinora — UI Specifications v3.0*
