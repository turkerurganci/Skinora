# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 11
**Sonuç:** 3 bulgu

---

## GPT Çıktısı

### BULGU-1: InviteToken arşivleme sonrası benzersizlik kuralı eksik
- **Seviye:** ORTA
- **Kategori:** Güvenlik / Veri Bütünlüğü
- **Konum:** §3.5, §5.1, §8.4
- **Sorun:** InviteToken §8.4 benzersizlik değerlendirmesinde yok.
- **Öneri:** Arşivleme sonrası reuse riski açıkça değerlendirilmeli.

### BULGU-2: Notification yaşam döngüsü dokümanın farklı bölümlerinde farklı anlatılıyor
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Eksiklik
- **Konum:** §1.3, §6.1, §8.4
- **Sorun:** §1.3 sadece soft delete diyor, §6.1/§8.4 arşivleme + retention anlatıyor.
- **Öneri:** §1.3 güncellenmeli.

### BULGU-3: ExternalIdempotencyRecord status tamamlanmışlık kuralı yok
- **Seviye:** DÜŞÜK
- **Kategori:** Teknik Doğruluk / Edge Case
- **Konum:** §3.21
- **Sorun:** completed iken CompletedAt = NULL mümkün.
- **Öneri:** Status-dependent constraint eklenmeli.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | InviteToken arşivleme benzersizlik | ✅ KABUL | CSPRNG token, collision imkansız + terminal state'te artık aktif değil — ama §8.4'te eksikti | §8.4 benzersizlik değerlendirmesine InviteToken eklendi |
| 2 | Notification lifecycle | ✅ KABUL | §1.3 ile §6.1/§8.4 tutarsız — Notification hem soft delete hem arşivleme/retention | §1.3 lifecycle tablosuna Notification ayrımı (transaction-bağlı vs bağımsız) eklendi |
| 3 | ExternalIdempotencyRecord status constraint | ✅ KABUL | completed→CompletedAt NOT NULL olmalı, ResultPayload opsiyonel | Status-dependent constraint eklendi + ResultPayload NULL gerekçesi yazıldı |

### Claude'un Ek Bulguları

Ek bulgu yok.

---

## Uygulanan Düzeltmeler

- [x] §8.4 arşivleme benzersizlik bölümüne InviteToken değerlendirmesi eklendi (CSPRNG, terminal state)
- [x] §1.3 lifecycle tablosuna Notification ayrımı eklendi (TransactionId bağlı→arşiv, bağımsız→retention)
- [x] ExternalIdempotencyRecord: completed→CompletedAt NOT NULL, failed→CompletedAt NULL constraint eklendi
- [x] Versiyon v3.2 → v3.3

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 12 tetiklendi
