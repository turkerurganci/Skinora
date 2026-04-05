# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 23
**Sonuç:** 2 bulgu

---

## GPT Çıktısı

### BULGU-1: ExternalIdempotencyRecord exactly-once sınırı
- **Seviye:** ORTA
- **Sorun:** "DB seviyesinde ortadan kaldırılır" iddiası fazla güçlü — post-effect crash senaryosu açık.
- **Öneri:** Downstream provider idempotency + read-before-retry.

### BULGU-2: SYSTEM sentinel dışlama sadece SteamId sorguları için tanımlı
- **Seviye:** ORTA
- **Sorun:** Genel kullanıcı listeleri, metrikler, admin seçicilerde sentinel dahil olabilir.
- **Öneri:** Genel operasyonel kullanıcı predicate'i tanımlanmalı.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | Exactly-once sınırı | ✅ KABUL | Claim çözülmüş ama post-effect crash → duplicate risk | "DB seviyesinde ortadan kaldırılır" → "aynı anda iki worker çalışmasını engeller" olarak yumuşatıldı. Çift katmanlı garanti: receiver-side + provider-side idempotency + read-before-retry |
| 2 | Operasyonel kullanıcı predicate | ✅ KABUL | Sadece SteamId sorguları değil, tüm user-facing sorgular sentinel'i dışlamalı | §1.3'e sorgu tipi × predicate matrisi eklendi. §8.9 seed data referansı §1.3'e bağlandı |

### Claude'un Ek Bulguları

Ek bulgu yok.

---

## Uygulanan Düzeltmeler

- [x] ExternalIdempotencyRecord: "duplicate side effect ortadan kaldırılır" → "aynı anda iki worker engellenir" + exactly-once sınırı + çift katmanlı garanti (receiver + provider + read-before-retry)
- [x] §1.3: sorgu tipi × predicate matrisi eklendi (user-facing, tarihsel/audit, admin yönetimi)
- [x] §1.3: "IsDeactivated kontrolü operasyonel sorgularda zorunlu" normatif notu
- [x] §8.9 seed data sentinel dışlama metni kısaltılıp §1.3'e referans verildi
- [x] Versiyon v4.4 → v4.5

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 24 tetiklendi
