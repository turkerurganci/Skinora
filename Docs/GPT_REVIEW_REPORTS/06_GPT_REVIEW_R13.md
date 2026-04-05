# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 13
**Sonuç:** 3 bulgu

---

## GPT Çıktısı

### BULGU-1: Freeze/resume notu timeout enforcement modeliyle hizalı değil
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Teknik Doğruluk
- **Konum:** §3.5, §7.1, §8.1
- **Sorun:** §8.1 genel "Hangfire job iptal/reschedule" diyor ama §3.5 sadece ödeme aşaması per-job.
- **Öneri:** §8.1 iki kola ayrılmalı (per-job vs poller).

### BULGU-2: Email bilgisi iki yerde, source of truth belirsiz
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Güvenlik
- **Konum:** §3.1, §3.4, §6.2
- **Sorun:** User.Email ve UserNotificationPreference.ExternalId (EMAIL) çift kaynak.
- **Öneri:** Tek otorite belirlenip senkronizasyon kuralı yazılmalı.

### BULGU-3: BlockchainTransaction FAILED için constraint eksik
- **Seviye:** DÜŞÜK
- **Kategori:** Veri Bütünlüğü
- **Konum:** §2.6, §3.8
- **Sorun:** FAILED iken ConfirmedAt dolu kalabilir.
- **Öneri:** FAILED → ConfirmedAt NULL kuralı eklenmeli.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | Freeze/resume enforcement hizalaması | ✅ KABUL | §8.1 generic, §3.5 specific — tutarsız | §8.1 ITEM_ESCROWED (job) vs diğer (poller) olarak iki kola ayrıldı, §7.1 güncellendi |
| 2 | Email source of truth | ✅ KABUL | İki yerde aynı veri — drift riski | User.Email = profil, UserNotificationPreference = gönderim otoritesi olarak netleştirildi |
| 3 | BlockchainTransaction FAILED | ✅ KABUL | Aynı status-dependent pattern | FAILED → ConfirmedAt NULL, ConfirmationCount korunur (audit) |

### Claude'un Ek Bulguları

Ek bulgu yok.

---

## Uygulanan Düzeltmeler

- [x] §8.1 freeze/resume iki kola ayrıldı: ITEM_ESCROWED → job cancel/reschedule, diğer → deadline güncelle + poller
- [x] §7.1 traceability: "Hangfire ile schedule" → "Ödeme: Hangfire delayed job; diğer: scanner/poller"
- [x] User.Email açıklaması: profil alanı, gönderim otoritesi UserNotificationPreference, senkronizasyon kuralı yazıldı
- [x] BlockchainTransaction FAILED: ConfirmedAt NULL constraint + ConfirmationCount audit notu
- [x] Versiyon v3.4 → v3.5

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 14 tetiklendi
