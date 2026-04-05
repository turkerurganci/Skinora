# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 3
**Sonuç:** 4 bulgu

---

## GPT Çıktısı

### BULGU-1: FK envanteri hâlâ tam değil
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Eksiklik
- **Konum:** §3.5, §3.22, §4.1
- **Sorun:** Transaction.EmergencyHoldByAdminId ve ColdWalletTransfer.InitiatedByAdminId entity tanımlarında FK olarak var ama §4.1 listesinde yok.
- **Öneri:** §4.1 FK listesi entity alanlarıyla birebir hizalanmalı.

### BULGU-2: Timeout freeze ile emergency hold modeli birbirine karışıyor
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Teknik Doğruluk
- **Konum:** §2.20, §3.5, §8.1
- **Sorun:** Emergency hold için ayrı alan seti var ama TimeoutFreezeReason'da da EMERGENCY_HOLD değeri var. İki mekanizmanın ilişkisi belirsiz.
- **Öneri:** Tek net model seçilmeli veya ilişki açıklanmalı.

### BULGU-3: Freeze resume mantığı iki farklı şekilde anlatılıyor
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Belirsizlik
- **Konum:** §3.5, §8.1
- **Sorun:** §3.5'te TimeoutRemainingSeconds tabanlı reschedule, §8.1'de deadline extension modeli anlatılıyor.
- **Öneri:** Tek resmi resume algoritması seçilmeli.

### BULGU-4: "Zorunlu" denilen bazı alanlar veri modeli seviyesinde zorunlu hale getirilmemiş
- **Seviye:** ORTA
- **Kategori:** Edge Case / Teknik Doğruluk
- **Konum:** §3.5
- **Sorun:** CancelReason ve EmergencyHoldReason NULL tanımlı ama "(zorunlu)" deniyor; state-dependent CHECK constraint yok.
- **Öneri:** State-dependent constraint'ler açıkça yazılmalı.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | FK envanteri eksik | ✅ KABUL | Doğrulandı — Transaction.EmergencyHoldByAdminId ve ColdWalletTransfer.InitiatedByAdminId §4.1'de yok | §4.1'e 2 FK eklendi |
| 2 | Emergency hold / freeze karışıklığı | ⚠️ KISMİ | İki mekanizma birbirini dışlamıyor, 05 §4.5'te birlikte çalıştıkları açık. Sorun 06'da bu ilişkinin dokümante edilmemiş olması | §3.5 Emergency Hold bölümüne açıklayıcı not eklendi |
| 3 | İki farklı resume modeli | ✅ KABUL | §8.1 deadline extension, §3.5+05 §4.4 TimeoutRemainingSeconds modeli — çelişiyor | §8.1 TimeoutRemainingSeconds tabanlı modele güncellendi, otorite belirtildi |
| 4 | State-dependent constraint eksikliği | ✅ KABUL | CancelReason, EmergencyHoldReason NULL ama parantezde "(zorunlu)" — DB seviyesinde garanti yok | İptal, hold ve freeze için state-dependent CHECK constraint'ler tanımlandı |

### Claude'un Ek Bulguları

Ek bulgu yok.

---

## Uygulanan Düzeltmeler

- [x] §4.1'e Transaction.EmergencyHoldByAdminId ve ColdWalletTransfer.InitiatedByAdminId FK'ları eklendi
- [x] §3.5 Emergency Hold bölümüne freeze mekanizması ilişkisini açıklayan not eklendi
- [x] §8.1 Timeout Dondurma bölümü TimeoutRemainingSeconds tabanlı modele güncellendi (freeze/resume ayrımı, otorite notu)
- [x] Transaction entity'sine state-dependent CHECK constraint'ler eklendi (iptal, hold, freeze)
- [x] Versiyon v2.4 → v2.5

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 4 tetiklendi
