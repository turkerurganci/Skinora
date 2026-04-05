# GPT Cross-Review Raporu — 11_IMPLEMENTATION_PLAN.md

**Tarih:** 2026-03-28
**Model:** OpenAI o3 (manuel)
**Round:** 1
**Sonuç:** ⚠️ 6 bulgu

---

## GPT Çıktısı

### BULGU-1: Faz bağımlılığı kuralı kendi içinde çelişiyor
- **Seviye:** ORTA
- **Kategori:** Tutarlılık
- **Konum:** §3 Faz Tanımları, §3.1 Faz Bağımlılık Diyagramı
- **Sorun:** §3'te "Her faz bir önceki faz tamamlanmadan başlamaz" deniyor. Ancak §3.1'de F4'ün kısmen F2 ile paralel başlayabileceği yazıyor. Bu iki ifade birlikte kullanıldığında faz kuralı net değil.
- **Öneri:** Faz kuralını tek cümlede netleştir: ya "fazlar tamamen sıralıdır" denmeli, ya da "yalnızca açıkça belirtilen ön hazırlık task'ları istisnadır" şeklinde istisnalı kural yazılmalı.

### BULGU-2: F2 fazı, kendi kabul kriterleri nedeniyle F3 tamamlanmadan kapanamıyor
- **Seviye:** ORTA
- **Kategori:** Tutarlılık
- **Konum:** T38, T61–T62, §6 Gate Check
- **Sorun:** T38'in kabul kriterinde "SignalR ile real-time push (T61'de bağlanacak)" deniyor. T61 F3'te. Gate check tüm kabul kriterlerini gerektiriyor. F2 tasarım gereği F3'e bağımlı hale geliyor.
- **Öneri:** T38'in kabul kriterinden real-time push kısmını çıkarıp SignalR entegrasyonunu T61/T62'ye taşı.

### BULGU-3: Public platform endpoint'leri için backend implementasyon task'ı yok
- **Seviye:** ORTA
- **Kategori:** Eksiklik
- **Konum:** T86, §7.2 API → Task Eşleme
- **Sorun:** T86 landing page GET /platform/stats ve GET /platform/maintenance çağrılarını bekliyor. Traceability'de API-066–067 yalnızca T86 (frontend) ile eşlenmiş; backend task yok.
- **Öneri:** Bu iki public endpoint için ayrı backend task eklenmeli ya da mevcut bir backend task'a dahil edilmeli.

### BULGU-4: Retention işleri traceability'de var, task planında yok
- **Seviye:** ORTA
- **Kategori:** Tutarlılık
- **Konum:** §7.1 Veri Modeli → Task Eşleme
- **Sorun:** Traceability'de Retention DM-195–199 → T18, T25, T45 yazıyor. Ancak T45 işlem oluşturma akışıdır; kabul kriterlerinde retention yok. Retention job'ları somut task olarak tanımlanmamış.
- **Öneri:** Retention için ayrı task tanımlanmalı veya mevcut task kabul kriterlerine eklenmelidir.

### BULGU-5: Bildirim metinleri Post-MVP'ye atılmışken bildirim servisi MVP'de içerik üretmek zorunda bırakılmış
- **Seviye:** ORTA
- **Kategori:** Tutarlılık
- **Konum:** T37, §9 Post-MVP Parkı
- **Sorun:** T37'de lokalizasyon için 4 dilde .resx resource dosyaları bekleniyor. Ancak Post-MVP parkında "Bildirim mesaj içerikleri (tetikleyiciler var, metinler yazılmadı)" deniyor. İki karar birlikte doğru olamaz.
- **Öneri:** Karar tekleştirilmeli.

### BULGU-6: Doğrulama protokolü fiilen bağımlı ama üst bağımlılık listesinde yok
- **Seviye:** DÜŞÜK
- **Kategori:** Eksiklik
- **Konum:** Doküman başlığı "Bağımlılıklar", §4.2
- **Sorun:** Header'daki bağımlılıklar listesinde 12_VALIDATION_PROTOCOL.md yok. Ancak §4.2'de bu dokümana referans var.
- **Öneri:** 12_VALIDATION_PROTOCOL.md bağımlılıklar listesine eklenmeli.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | Faz bağımlılığı çelişkisi | ✅ KABUL | §3 satır 42'de "Her faz bir önceki faz tamamlanmadan başlamaz" mutlak kuralı var. §3.1 satır 59'da F4'ün kısmen F2 ile paralel başlayabileceği söyleniyor. Not (satır 62) açıklama getiriyor ama ana kuralı düzeltmiyor. Çelişki gerçek. | §3 satır 42'deki kural istisnalı hale getirilmeli: "Her faz bir önceki faz tamamlanmadan başlamaz (§3.1'de belirtilen kontrollü paralellik istisnaları hariç)." |
| 2 | T38 F2/F3 bağımlılığı | ✅ KABUL | T38 satır 798: "Notification tablosuna yazma + SignalR ile real-time push (T61'de bağlanacak)". "(T61'de bağlanacak)" ifadesi erteleme niyetini gösteriyor ama kabul kriteri olarak yazılmış. Gate check (§6.1 G2) tüm kabul kriterlerini gerektirdiğinden F2 kapanmaz. | T38 kabul kriterini ikiye böl: "Notification tablosuna yazma" F2'de kalır; "SignalR ile real-time push" ifadesi çıkarılıp T62'nin kabul kriterine "T38'den gelen Notification entity'lerini real-time push olarak iletir" eklenir. |
| 3 | Public endpoint backend boşluğu | ✅ KABUL | API-066–067 (satır 2109) sadece "T86 (frontend)" ile eşlenmiş. T86 frontend task'ı (satır 1601). /platform/stats ve /platform/maintenance backend endpoint'leri hiçbir backend task'ta yok. Gerçek boşluk. | İki seçenek: (a) Yeni backend task T63a oluştur (F3'te, admin/platform API'leri yanında), veya (b) T63'ün kabul kriterlerine bu iki public endpoint'i ekle. Traceability §7.2'de API-066–067 mapping güncellenmeli. |
| 4 | Retention traceability boşluğu | ✅ KABUL | Satır 2092: Retention DM-195–199 → T18, T25, T45. T45 kabul kriterleri (satır 915-925) tamamen işlem oluşturma akışıyla ilgili, retention yok. T18 (User entity) ve T25 (altyapı entity'leri) retention *entity'lerini* oluşturabilir ama retention *job'larını* çalıştıracak davranışsal task yok. | Retention job'ları için ayrı task oluşturulmalı (F3'te, T45 sonrası). Traceability §7.1 güncellenmeli. |
| 5 | Bildirim metinleri çelişkisi | ⚠️ KISMİ | GPT'nin tespiti doğru: T37 (satır 782) ".resx resource dosyaları, 4 dil, kanal bazlı format" diyor, MVP-OUT-016 (satır 2199) "metinler yazılmadı" diyor. Ancak GPT'nin kaçırdığı nüans: MVP-OUT-016'daki "metinler yazılmadı" büyük olasılıkla *profesyonel copywriting* anlamında — yani son kullanıcıya sunulacak polished metinler. T37 ise teknik altyapıyı (resource file yapısı, dil seçimi, kanal formatı) placeholder/geliştirici metinleriyle kurabilir. Çelişki gerçek ama çözüm yapısal değil, ifade netleştirmesi. | T37 kabul kriterine "placeholder metinlerle" ibaresi eklenmeli. MVP-OUT-016 açıklaması "metinler yazılmadı" → "final/polished mesaj metinleri yazılmadı (MVP'de placeholder metinler kullanılır)" olarak netleştirilmeli. |
| 6 | 12_VALIDATION_PROTOCOL bağımlılık eksikliği | ❌ RET | Bağımlılık yönü ters. Header'daki "Bağımlılıklar" listesi, 11'in *içeriğini üretmek için okuduğu* dokümanları listeler (02–10 arası). 11, 12'den içerik almıyor — aksine 12, 11'e bağımlı olacak. §4.2'deki referans bir *ileri işaretçi* (forward pointer): "detaylar 12'de tanımlanacak." Bu, bir bağımlılık değil, bir delegasyon. 12 henüz yazılmamış olması da bunu doğruluyor. | Aksiyon gerekmez. Mevcut forward pointer yeterli. |

### Claude'un Ek Bulguları

> _GPT'nin kaçırdığı ama Claude'un tespit ettiği sorunlar:_

Yok. GPT'nin 6 bulgusu dokümanın asıl sorunlu noktalarını kapsamlı şekilde tespit etmiş.

---

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı (v0.2 → v0.3)
- [x] Round 2 tetiklendi
