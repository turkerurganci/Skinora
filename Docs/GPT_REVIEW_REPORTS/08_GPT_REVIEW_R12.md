# GPT Cross-Review Raporu — 08_INTEGRATION_SPEC.md

**Tarih:** 2026-03-22
**Model:** OpenAI o3 (ChatGPT, manuel)
**Round:** 12
**Sonuç:** ⚠️ 3 bulgu (0 KRİTİK, 3 ORTA) — GPT altıncı kez "bunların dışında yeni daha güçlü teknik/güvenlik bulgusu görmüyorum" dedi

---

## GPT Çıktısı

### BULGU-1: Steam 503 hatası iki farklı aksiyona bağlanmış
- **Seviye:** ORTA
- **Kategori:** Belirsizlik
- **Konum:** §2.7
- **Sorun:** Aynı 503 kodu "geçici hata → retry" ve "bakım → timeout dondurma" olarak iki farklı davranışa bağlı. Ayrıştırma kuralı yok.
- **Öneri:** 503 karar ağacı ekle: retry → başarısızsa health check → bakım mı izole hata mı ayrıştır.

### BULGU-2: Email outbox "terminal" vs "kuyrukta bekletme" çelişkisi
- **Seviye:** ORTA
- **Kategori:** Tutarlılık
- **Konum:** §4.3, §4.4
- **Sorun:** §4.3 "tüm retry'lar başarısız → log + alert" terminal gibi, §4.4 "outbox'ta bekler, servis dönünce gönderilir" — iki farklı davranış.
- **Öneri:** Geçici hatalar için DEFERRED state, kalıcı hatalar için FAILED state netleştir.

### BULGU-3: Webhook "Retry: Hayır" semantiği yanlış
- **Seviye:** ORTA
- **Kategori:** Edge Case
- **Konum:** §4.3
- **Sorun:** Webhook satırında "Hayır" yazılmış ama Svix redelivery bekleniyor. Persistence başarısızsa event kaybolabilir.
- **Öneri:** "Platform retry yok, inbound redelivery provider tarafından" olarak düzelt. Non-2xx ile başarısız persistence'ı modelle.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Uygulanan Aksiyon |
|---|-------------|---------------|-------------------|-------------------|
| 1 | Steam 503 ayrıştırma | ✅ KABUL | Aynı HTTP 503 için iki farklı davranış: "retry" vs "timeout dondurma". Ayrıştırma kuralı olmadan implementasyon hangi yolu seçeceğini bilemez. Health check + retry sonucu karar ağacı mantıklı: retry → başarısızsa health check → bakım tespiti → timeout dondurma. | §2.7'de 503 satırları birleştirildi, 503 karar ağacı eklendi: retry (3 deneme) → başarısızsa health check → başarısız ise bakım kabul et + timeout dondurma, başarılı ise izole hata + log. |
| 2 | Outbox state tutarlılık | ✅ KABUL | §4.3 "tüm retry'lar başarısız" terminal gibi okunuyor ama §4.4 "outbox'ta bekler" diyor. İki farklı senaryo: geçici hata (provider down) ve kalıcı hata (422, suppressed). Geçici hatada outbox kaydı DEFERRED kalmalı, kalıcı hatada FAILED olmalı. | §4.3 hata tablosu ikiye ayrıldı: geçici hata → DEFERRED (arka plan job ile artan aralıklarla retry), kalıcı hata → FAILED (retry yok). §4.4 ile tutarlı. |
| 3 | Webhook retry semantiği | ✅ KABUL | "Hayır" ifadesi platform outbound retry olmadığını söylüyor ama Svix inbound redelivery bekleniyor. Persistence başarısızsa 2xx dönülürse event kaybolur. Normatif kural: persistence başarısızsa 2xx dönülmez → Svix retry → idempotency ile duplicate koruması. | Webhook satırı "Platform outbound retry yok; inbound redelivery Resend/Svix tarafından" olarak düzeltildi. Güvenlik tablosuna persistence başarısızlık kuralı eklendi: 5xx dön → Svix redelivery, event-id idempotency ile korunur. |

### Claude'un Ek Bulguları

Ek bulgu yok.

### Özet

| Karar | Sayı |
|-------|------|
| ✅ KABUL | 3 |
| **Toplam düzeltme** | **3** |

**Toplam: 12 round, 57 düzeltme, v1.3 → v2.5**

**Sonraki adım:** v2.5 → GPT'ye R13 gönderilecek. TEMİZ bekleniyor.
