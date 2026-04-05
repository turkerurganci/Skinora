# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 19
**Sonuç:** 2 bulgu

---

## GPT Çıktısı

### BULGU-1: SystemSetting "henüz yapılandırılmamış" durum modeli net değil
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Teknik Doğruluk
- **Konum:** §3.17, §8.8
- **Sorun:** Value NOT NULL ama bazı parametrelerin varsayılanı "—" — placeholder semantiği belirsiz.
- **Öneri:** IsConfigured flag + startup fail-fast + Value nullable.

### BULGU-2: Admin FK alanlarında admin olma invariantı tanımlı değil
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Güvenlik / Audit
- **Konum:** 7+ admin FK alanı
- **Sorun:** FK → User, ama admin rolü kontrolü DB'de yok.
- **Öneri:** Uygulama katmanı invariantı + audit tarafında ActorType güvencesi normatif yazılmalı.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | SystemSetting yapılandırma modeli | ✅ KABUL | NOT NULL + "—" placeholder runtime hatası riski | Value nullable + IsConfigured flag + startup fail-fast kuralı eklendi |
| 2 | Admin FK invariantı | ✅ KABUL | DB seviyesinde enforce edilemez ama normatif kural şart | §8.5 Admin Aksiyonu Invariantı bölümü eklendi — command handler guard + ActorType audit güvencesi |

### Claude'un Ek Bulguları

- **EK-1:** §8 section numaraları yeniden düzenlendi (8.1-8.10), tüm internal referanslar güncellendi.

---

## Uygulanan Düzeltmeler

- [x] SystemSetting.Value: NOT NULL → NULL + `IsConfigured` bool field eklendi
- [x] §8.8 seed data: yapılandırılmamış parametreler Value=NULL, IsConfigured=false + startup fail-fast kuralı
- [x] §3.17 parametre tablosu: "—" placeholder semantiği açıklandı
- [x] §8.5 Admin Aksiyonu Invariantı bölümü eklendi — command handler guard + ActorType tarihsel audit güvencesi
- [x] §8 section numaraları 8.1-8.10 olarak düzenlendi, tüm internal referanslar (§8.6→§8.7, §8.7→§8.8) güncellendi
- [x] Versiyon v4.0 → v4.1

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 20 tetiklendi
