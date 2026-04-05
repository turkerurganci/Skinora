# GPT Cross-Review Raporu — 09_CODING_GUIDELINES.md (Round 3)

**Tarih:** 2026-03-20
**Model:** OpenAI o3 (ChatGPT, manuel)
**Round:** 3
**Sonuç:** ⚠️ 3 bulgu (0 KRİTİK, 3 ORTA)

---

## GPT Çıktısı

### BULGU-1: Hangfire scheduling akışı atomikmiş gibi yazılmış
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk, Edge Case
- **Konum:** §13.3, §13.6
- **Sorun:** Job scheduling ve business DB commit farklı transaction'lardır. Orphan/duplicate job riski var ama doküman bunu söylemiyor.
- **Öneri:** "Atomik" ifadesini daralt, reconciliation kuralı ekle.

### BULGU-2: Job silme kuralı çalışmaya başlamış job'ı güvenli durdurmıyor
- **Seviye:** ORTA
- **Kategori:** Edge Case, UX
- **Konum:** §13.3, §13.6
- **Sorun:** BackgroundJob.Delete henüz çalışmamış job için geçerli. Processing'e başlamış job için delete geç kalabilir.
- **Öneri:** Job handler başında transaction state/deadline/frozen doğrulaması zorunlu kıl.

### BULGU-3: CSRF maddesi API çağrıları için fazla geniş yazılmış
- **Seviye:** ORTA
- **Kategori:** Güvenlik, Teknik Doğruluk
- **Konum:** §16.3
- **Sorun:** Next.js origin check yalnızca Server Actions'a uygulanır, Route Handlers için otomatik CSRF koruması yok. Doküman bunu ayırmıyor.
- **Öneri:** Server Actions vs Route Handlers CSRF stratejisini ayrı ayrı tanımla.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Uygulanan Aksiyon |
|---|-------------|---------------|-------------------|-------------------|
| 1 | Hangfire atomiklik sınırı | ✅ KABUL | Hangfire kendi storage'ına ayrı yazar. `BackgroundJob.Schedule` başarılı olup `SaveChangesAsync` başarısız olursa orphan job kalır. Doğru tespit. | §13.3'e atomiklik sınırı notu eklendi: farklı transaction olduğu, orphan job riski, reconciliation zorunluluğu. §13.6'da "atomik" ifadesi daraltılarak "business state atomik, Hangfire ayrı adım" şeklinde düzeltildi. |
| 2 | Job handler state doğrulama eksik | ✅ KABUL | `BackgroundJob.Delete` yalnızca scheduled/enqueued job'ları durdurur. Processing'deki job için geç kalır. Handler'ların savunmacı kontrol yapması zorunlu. | §13.3'e job handler state doğrulama kuralı ve kod örneği eklendi: status, frozen flag, deadline kontrolü — koşul tutmuyorsa no-op. |
| 3 | CSRF kapsamı fazla geniş | ✅ KABUL | Next.js origin check resmi dokümantasyonda yalnızca Server Actions bağlamında anlatılıyor. Route Handlers için otomatik koruma yok — bu ayrım güvenlik için kritik. | §16.3'teki CSRF notu ikiye ayrıldı: Server Actions (built-in origin check) vs Route Handlers (manual Origin/Referer doğrulaması veya CSRF token). |

### Claude'un Ek Bulguları

Yok — Round 3'te ek bulgu tespit edilmedi.

---

## Özet

| Metrik | Değer |
|--------|-------|
| GPT bulgu sayısı | 3 (0 KRİTİK, 3 ORTA) |
| Claude kararları | 3 KABUL, 0 KISMİ, 0 RET |
| Claude ek bulgu | 0 |
| Toplam düzeltme | 3 |
| Doküman versiyonu | v0.5 → v0.6 |

---

## Genel İlerleme (3 Round)

| Round | Bulgu | KRİTİK | ORTA | DÜŞÜK | Claude Ek |
|-------|-------|--------|------|-------|-----------|
| 1 | 7 | 3 | 4 | 0 | 1 |
| 2 | 3 | 0 | 3 | 0 | 0 |
| 3 | 3 | 0 | 3 | 0 | 0 |
| **Toplam** | **13** | **3** | **10** | **0** | **1** |

---

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [ ] Round 4 tetiklendi — GPT "SONUÇ: TEMİZ" hedefleniyor
