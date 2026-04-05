# GPT Cross-Review Raporu — 09_CODING_GUIDELINES.md (Round 2)

**Tarih:** 2026-03-20
**Model:** OpenAI o3 (ChatGPT, manuel)
**Round:** 2
**Sonuç:** ⚠️ 3 bulgu (0 KRİTİK, 3 ORTA)

---

## GPT Çıktısı

### BULGU-1: Outbox consumer idempotency örneği dış yan etkiler için tam güvence vermiyor
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk, Edge Case
- **Konum:** §9.3
- **Sorun:** Consumer'da önce dış etki (SendBuyerInvite) çalışıyor, sonra ProcessedEvent işaretleniyor. Crash durumunda çift bildirim riski var.
- **Öneri:** Sınırlamayı dokümanda açıkça belirt. Downstream çağrılarda EventId bazlı idempotency key zorunlu kıl.

### BULGU-2: Outbox dispatcher için job tipi aynı dokümanda iki farklı şekilde anlatılıyor
- **Seviye:** ORTA
- **Kategori:** Tutarlılık
- **Konum:** §13.2, §13.4
- **Sorun:** §13.2 outbox dispatcher'ı "Recurring job" örneği olarak veriyor, §13.4 recurring modelin uygun olmadığını söylüyor.
- **Öneri:** §13.2'deki tabloyu düzelt, outbox dispatcher'ı recurring'den çıkar.

### BULGU-3: Cookie tabanlı server-side auth yolu için CSRF sınırı eksik
- **Seviye:** ORTA
- **Kategori:** Güvenlik, Belirsizlik
- **Konum:** §16.3
- **Sorun:** Cookie-forwarding tanımlanmış ama CSRF/SameSite/Origin-check politikası yazılmamış.
- **Öneri:** SameSite, Secure, CSRF token veya Origin doğrulaması kuralını ekle.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Uygulanan Aksiyon |
|---|-------------|---------------|-------------------|-------------------|
| 1 | Consumer idempotency dış yan etki sınırı | ✅ KABUL | Kod örneğinde `SendBuyerInvite` → crash → retry = çift bildirim. ProcessedEvent yalnızca DB-seviyesi idempotency sağlıyor, dış çağrılar için exactly-once garanti yok. Gerçek teknik boşluk. | §9.3'e detaylı uyarı notu eklendi: EventId bazlı deduplication key zorunluluğu, alternatif stratejiler (at-most-once vs at-least-once) ve iş gereksinimi bazlı karar. |
| 2 | §13.2 vs §13.4 outbox dispatcher çelişkisi | ✅ KABUL | §13.2 tablosunda outbox dispatcher "Recurring job" örneği, §13.4 açıkça "recurring uygun değil, self-rescheduling delayed job" diyor. Net iç çelişki — Round 1'de §13.4'ü düzeltirken §13.2'yi kontrol etmemiştik. | §13.2'de outbox dispatcher örneği "Recurring job"dan çıkarıldı, yerine "Retention cleanup: her gece eski job kayıtlarını temizle" konuldu. |
| 3 | CSRF sınırı eksik | ✅ KABUL | Round 1'de cookie-forwarding ekledik ama CSRF'i tanımlamadık. Cookie-based auth = CSRF riski. Minimum SameSite/Secure/Origin doğrulaması dokümante edilmeli. | §16.3'e CSRF koruması notu eklendi: SameSite=Lax + Secure + HttpOnly zorunlu, Next.js Origin doğrulaması aktif, detaylar 05 §6'ya referans. |

### Claude'un Ek Bulguları

Yok — Round 2'de ek bulgu tespit edilmedi.

---

## Özet

| Metrik | Değer |
|--------|-------|
| GPT bulgu sayısı | 3 (0 KRİTİK, 3 ORTA) |
| Claude kararları | 3 KABUL, 0 KISMİ, 0 RET |
| Claude ek bulgu | 0 |
| Toplam düzeltme | 3 |
| Doküman versiyonu | v0.4 → v0.5 |

---

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [ ] Round 3 tetiklendi
