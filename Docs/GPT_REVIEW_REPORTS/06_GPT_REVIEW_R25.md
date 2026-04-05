# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 25
**Sonuç:** 4 bulgu

---

## GPT Çıktısı

### BULGU-1: NotificationDelivery kişisel veriyi anonimleştirme modelinin dışına kaçırıyor
- **Seviye:** KRİTİK
- **Kategori:** Lifecycle / Güvenlik / Veri koruma
- **Konum:** §3.13a NotificationDelivery, §6.2 Hesap Silme ve Anonimleştirme, §8.8 Arşivleme
- **Sorun:** Hesap silme bölümünde User.Email, UserNotificationPreference.ExternalId ve diğer doğrudan kişisel veriler temizleniyor. Ama NotificationDelivery.TargetExternalId alanı, gönderim anındaki email / Telegram chat ID / Discord user ID snapshot'ını tutuyor ve transaction'a bağlıysa archive set ile birlikte uzun süre korunuyor. Dokümanın kendi anonimleştirme prensibi "bağlı kişisel veriler temizlenir" dese de, dış kanal hedefleri tarihsel teslimat kayıtlarında yaşamaya devam ediyor.
- **Öneri:** NotificationDelivery için açık veri koruma kuralı eklenmeli: ya TargetExternalId hesap silmede anonymize/hash edilir, ya yalnızca masked snapshot tutulur, ya da delivery audit'i için kişisel hedef yerine ayrı delivery token / channel handle snapshot mantığı kullanılır.

### BULGU-2: NotificationDelivery için kanonik satır kuralı eksik
- **Seviye:** ORTA
- **Kategori:** Constraint bütünlüğü / Operasyonel semantik
- **Konum:** §3.13a NotificationDelivery, §5.1 Unique indeksler
- **Sorun:** Model NotificationDelivery'yi fiilen "notification + channel başına tek workflow kaydı" gibi kuruyor: Status, AttemptCount, LastError, SentAt aynı row üzerinde güncelleniyor. Fakat NotificationId + Channel için unique kural yok. Aynı notification için aynı kanalda birden fazla delivery row oluşabilir.
- **Öneri:** UNIQUE(NotificationId, Channel) eklenmeli.

### BULGU-3: SystemSetting için soft delete semantiği, seed/fail-fast modeliyle çakışıyor
- **Seviye:** ORTA
- **Kategori:** Yapısal tutarlılık / Lifecycle / Operasyonel semantik
- **Konum:** §1.3 Silme Stratejisi, §3.17 SystemSetting, §8.9 Seed Data
- **Sorun:** SystemSetting "Soft Delete (Kalıcı)" sınıfında. Aynı doküman tüm platform parametrelerinin seed edildiğini ve startup'ta fail-fast kontrolünden geçtiğini söylüyor. Bir admin bir ayarı soft delete ederse global filter onu normal sorgulardan gizler, uygulama o anahtarı "yok / yapılandırılmamış" sayabilir ve startup veya runtime davranışı bozulabilir, ama Key benzersizliği unfiltered olduğu için aynı key ile yeni satır da açılamaz.
- **Öneri:** SystemSetting için delete yerine daha açık bir model seçilmeli: ya soft delete tamamen yasak olmalı, ya IsDeprecated / IsActive benzeri ayrı lifecycle alanı getirilmeli, ya da soft delete edilen setting'in nasıl restore/recreate edileceği açıkça yazılmalı.

### BULGU-4: Dış kanal kimliklerinin hesaplar arası sahiplik invariantı tanımlı değil
- **Seviye:** ORTA
- **Kategori:** Güvenlik / UX / Operasyonel tutarlılık
- **Konum:** §3.4 UserNotificationPreference, §3.13a NotificationDelivery, §5.1 Unique indeksler
- **Sorun:** UserNotificationPreference için yalnızca UserId + Channel unique. Aynı email adresi, aynı Telegram chat ID'si veya aynı Discord user ID'si birden fazla kullanıcı hesabına aynı anda bağlanabilir. Doküman bunun bilinçli ürün kararı mı yoksa yasak mı olduğunu söylemiyor.
- **Öneri:** Açık bir invariant yazılmalı: ya Channel + ExternalId aktif kayıtlar arasında unique olmalı, ya da paylaşım bilinçli destekleniyorsa bunun güvenlik/UX sonucu açıkça tanımlanmalı.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | NotificationDelivery TargetExternalId anonimleştirme dışı | ✅ KABUL | §6.2 UserNotificationPreference.ExternalId'yi temizliyor ama NotificationDelivery.TargetExternalId — aynı verinin snapshot'ı — hiç geçmiyor. Entity archive set ile yıllarca yaşayabilir. Dokümanın kendi "bağlı kişisel veriler temizlenir" prensibine aykırı. | §6.2'ye NotificationDelivery satırı eklendi: hesap silmede TargetExternalId masked formata dönüştürülür |
| 2 | NotificationDelivery unique constraint eksik | ✅ KABUL | §3.13a tek-satır workflow modeli kuruyor ama §5.1'de bunu koruyan constraint yok. Race condition veya uygulama hatası duplicate row riski. | §5.1'e UNIQUE(NotificationId, Channel) eklendi |
| 3 | SystemSetting soft delete + seed/fail-fast çakışması | ✅ KABUL | Yapısal çelişki somut: soft delete → global filter gizler → app "yok" der → fail-fast/runtime hata. Key unfiltered unique → aynı key yeniden oluşturulamaz. Operasyonel kilitlenme. | §1.3'te SystemSetting "Mutable Catalog (Delete Yasak)" kategorisine taşındı. §3.17'den IsDeleted/DeletedAt kaldırıldı, silme yasağı notu eklendi |
| 4 | Dış kanal ExternalId hesaplar arası sahiplik invariantı | ✅ KABUL | §5.1'de UserNotificationPreference unique: UserId + Channel. Aynı ExternalId farklı kullanıcılara bağlanmasını engellemez. Doküman sessiz — ne bilinçli paylaşım ne yasak. | §5.1'e UNIQUE(Channel, ExternalId WHERE IsDeleted = 0 AND ExternalId IS NOT NULL) eklendi. §3.4'e sahiplik invariantı notu eklendi |

---

## Uygulanan Düzeltmeler

- [x] §6.2: NotificationDelivery.TargetExternalId masked formata dönüştürme kuralı eklendi
- [x] §5.1: UNIQUE(NotificationId, Channel) — delivery tek-satır modeli koruması
- [x] §1.3: SystemSetting "Soft Delete (Kalıcı)"dan çıkarıldı, yeni "Mutable Catalog (Delete Yasak)" kategorisi oluşturuldu
- [x] §1.3 retention tablosu: Mutable Catalog satırı eklendi
- [x] §3.17: IsDeleted/DeletedAt field'ları kaldırıldı, silme yasağı notu eklendi
- [x] §5.1: UNIQUE(Channel, ExternalId WHERE IsDeleted = 0 AND ExternalId IS NOT NULL) — hesaplar arası sahiplik invariantı
- [x] §3.4: Hesaplar arası sahiplik invariantı açıklama notu eklendi
- [x] Versiyon v4.7 → v4.8

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [ ] Round 26 tetiklendi
