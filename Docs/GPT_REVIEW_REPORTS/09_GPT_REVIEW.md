# GPT Cross-Review Raporu — 09_CODING_GUIDELINES.md

**Tarih:** 2026-03-20
**Model:** OpenAI o3 (ChatGPT, manuel)
**Round:** 1
**Sonuç:** ⚠️ 7 bulgu (3 KRİTİK, 4 ORTA) + 1 Claude ek bulgu

---

## GPT Çıktısı

### BULGU-1: State machine side effect kuralı kendi içinde çelişiyor
- **Seviye:** KRİTİK
- **Kategori:** Tutarlılık, Teknik Doğruluk
- **Konum:** §4.2.1, §9.2, §24, §25
- **Sorun:** Doküman bir yandan `Domain/StateMachine/SideEffects/` klasörü tanımlıyor, bir yandan "state machine side effect üretmez, OnEntry/OnExit yasak" diyor. Ayrıca §24/§25 checklistlerinde "guard + side effect" ifadesi konumun nerede olduğu konusunda karışıklık yaratıyor.
- **Öneri:** State machine yalnızca geçiş ve guard tanımlar; tüm side effect'ler Application event handler'larda çalışır. SideEffects/ klasörünü kaldır, checklisti netleştir.

### BULGU-2: Modüller arası iletişim kuralı iki farklı şekilde tanımlanmış
- **Seviye:** KRİTİK
- **Kategori:** Tutarlılık, Mimari
- **Konum:** §4.2.2, §6.3
- **Sorun:** §4.2.2 "yalnızca event contract + MediatR" derken §6.3 ek olarak "read-only query interface" tanımlıyor. "Yalnızca" ifadesi çelişki yaratıyor.
- **Öneri:** §4.2.2'yi §6.3 ile hizala.

### BULGU-3: UTC standardı teknik olarak eksik tanımlanmış
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk, Güvenlik
- **Konum:** §7.1, §11.3
- **Sorun:** EF Core, SQL Server'dan okunan DateTime'da Kind'ı Unspecified olarak döndürür. Bu, zaman-duyarlı akışlarda sessiz hata üretir.
- **Öneri:** EF Core value converter ile UTC enforce et.

### BULGU-4: Frontend auth modeli, seçilen Next.js mimarisiyle uyumlu değil
- **Seviye:** KRİTİK
- **Kategori:** Güvenlik, Teknik Doğruluk, UX
- **Konum:** §16.2, §16.3, §4.2.1
- **Sorun:** §16.2 server component'leri veri çekme ana yolu olarak tanımlarken, §16.3'teki API client `Bearer ${getAccessToken()}` kullanıyor — bu server component'larda çalışmaz.
- **Öneri:** Server ve client fetch pattern'lerini ayır, auth akışını netleştir.

### BULGU-5: Outbox dispatcher örneği çoklu instance/restart senaryosunda çoğalabilir
- **Seviye:** KRİTİK
- **Kategori:** Edge Case, Teknik Doğruluk
- **Konum:** §13.2, §13.4
- **Sorun:** Self-rescheduling pattern, deploy/restart/çoklu instance durumunda birden fazla dispatcher zinciri başlatabilir.
- **Öneri:** Distributed lock ile tekillik garantisi ekle.

### BULGU-6: Timeout warning job temizliği eksik bırakılmış
- **Seviye:** ORTA
- **Kategori:** Edge Case, UX
- **Konum:** §13.3
- **Sorun:** Timeout job'ının jobId'si saklanıyor ve iptalde siliniyor ama warning job'ının jobId'si tutulmuyor. İptal/tamamlanma sonrası hatalı uyarı gönderebilir.
- **Öneri:** Warning job için de kimlik sakla ve iptal et.

### BULGU-7: Webhook güvenlik modeli net değil
- **Seviye:** ORTA
- **Kategori:** Güvenlik, Belirsizlik
- **Konum:** §11.3, §15, §17.5
- **Sorun:** §17.5 hem HMAC imza hem X-Internal-Key header'ı gönderiyor. §11.3'te yalnızca HMAC doğrulaması var. X-Internal-Key doğrulanıyor mu belirsiz.
- **Öneri:** Tek otorite belirle — HMAC yeterliyse X-Internal-Key'i webhook'tan kaldır.

---

## Claude Bağımsız Değerlendirmesi

> Claude, GPT'nin her bulgusunu proje bağlamı ve dokümanlarla çapraz kontrol ederek bağımsız değerlendirmiştir.
> Karar: ✅ KABUL (GPT haklı) | ❌ RET (GPT yanlış) | ⚠️ KISMİ (kısmen haklı, farklı çözüm)

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Uygulanan Aksiyon |
|---|-------------|---------------|-------------------|-------------------|
| 1 | State machine side effect çelişkisi | ⚠️ KISMİ | `SideEffects/` klasörü ile §9.2 kuralı arasındaki çelişki gerçek. Ancak §24/25 checklist'i side effect'in konumunu değil tamamlık kontrolünü ifade ediyor — GPT burada aşırı yorumluyor. | `SideEffects/` klasörü §4.2.1'den kaldırıldı. §24/25'te "application-layer side effect (event handler)" olarak netleştirildi. |
| 2 | Modüller arası iletişim çelişkisi | ✅ KABUL | §4.2.2 "yalnızca event + MediatR" diyor, §6.3 ek olarak read-only query interface tanımlıyor. Açık çelişki. | §4.2.2'de "yalnızca §6.3'te tanımlanan iki yolla" şeklinde düzeltildi, §6.3'e referans eklendi. |
| 3 | UTC standardı eksik | ✅ KABUL | EF Core'un DateTime Kind davranışı bilinen bir sorun. Value converter olmadan Kind=Utc garantisi yok. Replay window, timeout gibi akışlarda sessiz hata riski gerçek. | §7.1'e EF Core UTC value converter zorunluluğu eklendi (`UtcDateTimeConverter`, `ConfigureConventions`). |
| 4 | Frontend auth modeli | ⚠️ KISMİ | Server component'ların Bearer token kullanamayacağı tespiti doğru. Ancak GPT'nin önerdiği tam auth model tanımlama (token storage, refresh flow) coding guidelines'ın kapsamını aşıyor — bu 05 §6'nın sorumluluğu. | §16.3'e client/server ayrımı notu eklendi: API client'ın client component'ler için olduğu, server component'larda cookie-forwarding kullanılacağı ve detayların 05 §6'da olduğu belirtildi. |
| 5 | Outbox dispatcher çoğalması | ✅ KABUL | Startup'ta `Enqueue` + self-rescheduling = restart/multi-instance'da çoklu chain. Hangfire delayed job deduplication yapmaz. | §13.4'e `IDistributedLockProvider` ile tekillik garantisi eklendi. Lock alınamazsa job sessizce sonlanır. |
| 6 | Warning job temizliği eksik | ✅ KABUL | Kod örneğinde timeout job ID saklanıp iptal ediliyor, warning job ID hiç saklanmıyor. Açık bir omission. | Warning job ID (`TimeoutWarningJobId`) entity'de saklanıyor ve iptal akışına eklendi. |
| 7 | Webhook X-Internal-Key belirsizliği | ✅ KABUL | §17.5 gönderici X-Internal-Key ekliyor, §11.3 alıcı doğrulamıyor. Belirsizlik gerçek. X-Internal-Key §11.2'de sidecar HTTP client istekleri için tanımlı — webhook callback'lerde HMAC yeterli. | §17.5'ten X-Internal-Key kaldırıldı. Yorum eklenerek HMAC'ın webhook auth mekanizması, X-Internal-Key'in sidecar HTTP client mekanizması olduğu netleştirildi. |

### Claude'un Ek Bulguları

| # | Bulgu | Seviye | Konum | Uygulanan Aksiyon |
|---|-------|--------|-------|-------------------|
| EK-1 | Outbox dispatcher'da `ProcessPendingEventsAsync()` exception fırlatırsa self-rescheduling satırına ulaşılamaz, chain kırılır. Hangfire retry mekanizması devreye girer ama araya boşluk düşer. | ORTA | §13.4 | `try/finally` ile rescheduling garanti altına alındı. Exception olsa bile bir sonraki polling döngüsü schedule edilir. |

---

## Özet

| Metrik | Değer |
|--------|-------|
| GPT bulgu sayısı | 7 (3 KRİTİK, 4 ORTA) |
| Claude kararları | 4 KABUL, 2 KISMİ, 0 RET |
| Claude ek bulgu | 1 (ORTA) |
| Toplam düzeltme | 8 |
| Doküman versiyonu | v0.3 → v0.4 |

---

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [ ] Round 2 tetiklendi
