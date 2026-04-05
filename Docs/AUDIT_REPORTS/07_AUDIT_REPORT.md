# Audit Raporu — 07_API_DESIGN.md

**Tarih:** 2026-03-16
**Hedef:** 07_API_DESIGN.md (v1.0 → v1.1)
**Bağlam:** 02, 03, 04, 05, 06, 10
**Odak:** Tam denetim

---

## Envanter Özeti

| Kaynak | Toplam Öğe | ✓ | ⚠ | ✗ |
|--------|-----------|---|---|---|
| 02 (İş kuralları → API validation/hata kodları) | 38 | 35 | 2 | 1 |
| 03 (Akış adımları → endpoint eşlemesi) | 42 | 41 | 1 | 0 |
| 04 (Ekran aksiyonları + veri → endpoint eşlemesi) | 35 | 32 | 2 | 1 |
| 05 (Teknik kararlar → API yansıması) | 18 | 17 | 1 | 0 |
| 06 (Enum/field isimleri → API tutarlılığı) | 22 | 16 | 3 | 3 |
| 10 (MVP kapsam sınırları) | 12 | 12 | 0 | 0 |
| 07 İç tutarlılık | 15 | 13 | 1 | 1 |
| **Toplam** | **182** | **166** | **10** | **6** |

---

## Bulgular

| # | Envanter ID | Tür | Seviye | Bulgu | Öneri | Durum |
|---|------------|-----|--------|-------|-------|-------|
| 1 | 06-§2.13-ALL | Tutarsızlık | High | NotificationType enum 06 ile eşleşmiyor: 6 isim farkı, 3 fazla, 3 eksik tip | 06 ve 07 senkronize edildi | ✓ Düzeltildi |
| 2 | 04-§5-C08 | GAP | High | Maintenance Banner veri kaynağı eksik | P2 endpoint + RT2 MaintenanceStatusChanged eklendi | ✓ Düzeltildi |
| 3 | 04-§7.6-EMAIL | GAP | High | Email doğrulama endpoint'leri eksik | U15 (send-verification) + U16 (verify) eklendi | ✓ Düzeltildi |
| 4 | 04-§7.3-S07 | Kısmi | Medium | T5'te ödeme edge case banner verisi yok | T5'e `paymentEvents` bölümü eklendi | ✓ Düzeltildi |
| 5 | 06-§2.15-04 | Tutarsızlık | Low | Bot status'ta OFFLINE eksik | OFFLINE eklendi (AD1, AD10) | ✓ Düzeltildi |
| 6 | 05-§6.3-BRUTE | Kısmi | Medium | Auth brute force kilitleme tanımsız | A1'e temporarily_locked hatası eklendi | ✓ Düzeltildi |
| 7 | 07-§2.10-K10 | İç tutarsızlık | Medium | K10 kuralı ihlali (notification, bot status) | Bulgu #1 ve #5 ile otomatik çözüldü | ✓ Düzeltildi |
| 8 | 02-§18-ADMIN | Kısmi | Medium | Admin bildirim tipleri N1'de eksik | N1 tablo'ya 4 admin tipi eklendi | ✓ Düzeltildi |
| 9 | 06-§3.1-FIELD | Bilgi | Low | API vs entity field isim farkı | K10'a istisna notu eklendi | ✓ Düzeltildi |
| 10 | 03-§2.4-RETRY | Kısmi | Low | Ödeme hatası admin bildirimi eksik | ADMIN_PAYMENT_FAILURE tipi 06 ve 07'ye eklendi | ✓ Düzeltildi |

---

## Uygulanan Değişiklikler

### 07_API_DESIGN.md (v1.0 → v1.1)

1. **N1 notification type tablosu** — 06 §2.13 ile senkronize edildi (isimler eşleştirildi, admin tipleri eklendi)
2. **P2 endpoint eklendi** — `GET /platform/maintenance` (C08 banner verisi)
3. **RT2'ye MaintenanceStatusChanged event'i eklendi**
4. **U15 + U16 endpoint'leri eklendi** — Email doğrulama akışı
5. **T5'e `paymentEvents` bölümü eklendi** — Ödeme edge case banner verileri
6. **AD1/AD10 bot status'a OFFLINE eklendi**
7. **A1'e brute force kilitleme hatası eklendi**
8. **K10'a field isim istisna notu eklendi**
9. **Endpoint sayıları güncellendi** — 60 → 63 REST, 62 → 65 toplam
10. **Section numaraları düzeltildi** — Users grubunda çakışma giderildi

### 06_DATA_MODEL.md (v1.8 → v1.9)

1. **§2.13 NotificationType** — 4 yeni tip eklendi: `ITEM_RETURNED`, `PAYMENT_REFUNDED`, `FLAG_RESOLVED`, `ADMIN_PAYMENT_FAILURE`

---

## Sonuç

10 bulgu tespit edildi (3 High, 3 Medium, 4 Low), tamamı düzeltildi. Critical bulgu yok. 07_API_DESIGN.md v1.1 ve 06_DATA_MODEL.md v1.9 olarak güncellendi.
