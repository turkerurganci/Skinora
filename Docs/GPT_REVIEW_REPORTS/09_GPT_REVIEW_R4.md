# GPT Cross-Review Raporu — 09_CODING_GUIDELINES.md (Round 4)

**Tarih:** 2026-03-20
**Model:** OpenAI GPT-5.4 Thinking (ChatGPT, manuel)
**Round:** 4
**Sonuç:** ⚠️ 2 bulgu (0 KRİTİK, 2 ORTA) — GPT: "NEREDEYSE TEMİZ"

---

## GPT Çıktısı

### BULGU-1: Freeze akışında warning job kapsamı net değil
- **Seviye:** ORTA
- **Kategori:** Edge Case, Tutarlılık
- **Konum:** §13.3, §13.6
- **Sorun:** §13.6 freeze başlatmada sadece "timeout job'larını durdur" diyor, warning job'ın dahil olup olmadığı belirsiz.
- **Öneri:** §13.6'da hem timeout hem warning job'larının silindiğini ve resume'da ikisinin yeniden schedule edildiğini net yaz.

### BULGU-2: Correlation ID taşıma kuralı ile webhook örneği uyumsuz
- **Seviye:** ORTA
- **Kategori:** Tutarlılık, Belirsizlik
- **Konum:** §17.4, §17.5, §18.4
- **Sorun:** sendCallback örneğinde X-Correlation-Id header'ı gönderilmiyor. Trace zinciri callback noktasında kopuyor.
- **Öneri:** Webhook callback örneğine X-Correlation-Id ekle.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Uygulanan Aksiyon |
|---|-------------|---------------|-------------------|-------------------|
| 1 | Freeze'de warning job belirsizliği | ✅ KABUL | §13.3 iptal akışında her iki job'ı temizliyor ama §13.6 freeze "timeout job'ları" diyor, warning'i açıkça kapsamıyor. Basit ama gerçek belirsizlik. | §13.6 freeze koduna `PaymentTimeoutJobId` ve `TimeoutWarningJobId` silme açıkça eklendi. Resume koduna her iki job'ın yeniden schedule edilmesi eklendi. |
| 2 | Webhook callback'te correlation ID eksik | ✅ KABUL | §17.4 ve §18.4 correlation ID'nin tüm zincirde taşınmasını zorunlu kılıyor. §17.5 sendCallback örneğinde X-Correlation-Id yok — trace zinciri kopuyor. | `sendCallback` fonksiyonuna `correlationId` parametresi eklendi, header'a `X-Correlation-Id` eklendi, trace zinciri kuralı yorum olarak belirtildi. |

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
| Doküman versiyonu | v0.6 → v0.7 |

---

## Genel İlerleme (4 Round)

| Round | Bulgu | KRİTİK | ORTA | DÜŞÜK | Claude Ek |
|-------|-------|--------|------|-------|-----------|
| 1 | 7 | 3 | 4 | 0 | 1 |
| 2 | 3 | 0 | 3 | 0 | 0 |
| 3 | 3 | 0 | 3 | 0 | 0 |
| 4 | 2 | 0 | 2 | 0 | 0 |
| **Toplam** | **15** | **3** | **12** | **0** | **1** |

Trend: 7 → 3 → 3 → 2 (azalan), KRİTİK: 3 → 0 → 0 → 0

---

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [ ] Round 5 tetiklendi — GPT "SONUÇ: TEMİZ" hedefleniyor
