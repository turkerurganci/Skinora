# GPT Cross-Review Raporu — 09_CODING_GUIDELINES.md (Round 6)

**Tarih:** 2026-03-20
**Model:** OpenAI GPT-5.4 Thinking (ChatGPT, manuel)
**Round:** 6
**Sonuç:** ⚠️ 2 bulgu (0 KRİTİK, 2 ORTA)

---

## GPT Çıktısı

### BULGU-1: Webhook callback contract'ında idempotency ve imza alanları tek otoriteden tanımlanmamış
- **Seviye:** ORTA
- **Kategori:** Tutarlılık, Belirsizlik, Teknik Doğruluk
- **Konum:** §8.4, §11.3, §12.2, §17.5
- **Sorun:** Webhook payload örneğinde eventId yok. timestamp/nonce hem body'de hem header'da görünüyor. Kanonik alan seti belirsiz.
- **Öneri:** eventId body'de zorunlu, timestamp/nonce yalnızca header'da.

### BULGU-2: Retry matrisi bütün 4xx'leri kalıcı hata gibi ele alıyor
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk, Edge Case
- **Konum:** §11.4
- **Sorun:** 429 hariç tüm 4xx "retry yapma" — 408 gibi geçici hatalar da kapsamda.
- **Öneri:** 408'i ayrı retryable olarak tanımla, entegrasyon-spesifik geçici 4xx'ler için açık kapı bırak.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Uygulanan Aksiyon |
|---|-------------|---------------|-------------------|-------------------|
| 1 | Webhook contract belirsizliği | ✅ KABUL | §12.2 payload'unda timestamp/nonce body'de varken §17.5/§11.3 bunları header olarak kullanıyor. eventId (idempotency key) payload'da hiç yok. Üç farklı bölüm arasında net çelişki. | §12.2 webhook payload'undan timestamp/nonce kaldırıldı, eventId eklendi. Kanonik alan kuralı notu yazıldı: eventId=body (idempotency), timestamp/nonce=header (HMAC), X-Correlation-Id=header (trace). |
| 2 | Retry matrisi fazla geniş 4xx kuralı | ✅ KABUL | HTTP 408 RFC'de geçici hata olarak tanımlı. Blanket "tüm 4xx retry yapma" kuralı 408'i gereksiz yere kalıcı sayıyor. Ayrıca bazı entegrasyonlar 409/423'ü geçici döndürebilir. | 408 ayrı satır olarak retryable eklendi. Diğer 4xx için "entegrasyon-spesifik override'lar 08'de tanımlanır" notu eklendi. |

### Claude'un Ek Bulguları

Yok.

---

## Özet

| Metrik | Değer |
|--------|-------|
| GPT bulgu sayısı | 2 (0 KRİTİK, 2 ORTA) |
| Claude kararları | 2 KABUL, 0 KISMİ, 0 RET |
| Claude ek bulgu | 0 |
| Toplam düzeltme | 2 |
| Doküman versiyonu | v0.8 → v0.9 |

---

## Genel İlerleme (6 Round)

| Round | Bulgu | KRİTİK | ORTA | DÜŞÜK | Claude Ek |
|-------|-------|--------|------|-------|-----------|
| 1 | 7 | 3 | 4 | 0 | 1 |
| 2 | 3 | 0 | 3 | 0 | 0 |
| 3 | 3 | 0 | 3 | 0 | 0 |
| 4 | 2 | 0 | 2 | 0 | 0 |
| 5 | 3 | 0 | 3 | 0 | 0 |
| 6 | 2 | 0 | 2 | 0 | 0 |
| **Toplam** | **20** | **3** | **17** | **0** | **1** |

---

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [ ] Round 7 tetiklendi — GPT "SONUÇ: TEMİZ" hedefleniyor
