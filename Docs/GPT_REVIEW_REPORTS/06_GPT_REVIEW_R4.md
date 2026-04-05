# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 4
**Sonuç:** 4 bulgu

---

## GPT Çıktısı

### BULGU-1: FraudFlag scope semantiği kendi içinde hâlâ çelişkili
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Teknik Doğruluk
- **Konum:** §2.1, §2.21, §3.12
- **Sorun:** TRANSACTION_PRE_CREATE adı "işlem oluşmadan durur" izlenimi veriyor ama model TransactionId NOT NULL ve FLAGGED state tanımlıyor.
- **Öneri:** Scope adı veya açıklaması netleştirilmeli.

### BULGU-2: Hesap silme/anonymization akışı bağlı kişisel verileri tam kapsamıyor
- **Seviye:** ORTA
- **Kategori:** Güvenlik / Eksiklik
- **Konum:** §3.3, §3.4, §6.2
- **Sorun:** UserNotificationPreference.ExternalId, RefreshToken session/cihaz verisi §6.2'de yok.
- **Öneri:** Bağlı entity cleanup kuralları eklenmeli.

### BULGU-3: ProcessedEvent ile OutboxMessage ilişkisi mantıksal olarak var, şematik olarak yarım
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Eksiklik
- **Konum:** §3.19, §4.1, §4.2
- **Sorun:** EventId "OutboxMessage.Id referansı" deniyor ama FK listesinde yok, cascade kurallarında yok.
- **Öneri:** FK mı mantıksal link mi açıkça belirtilmeli.

### BULGU-4: SYSTEM sentinel user SteamId domain kuralını bozuyor
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk / Modelleme
- **Konum:** §3.1, §8.5
- **Sorun:** SteamId = "SYSTEM" bir Steam 64-bit ID değil; domain invariant bozuluyor.
- **Öneri:** Format-uyumlu sentinel veya açık istisna dokümantasyonu.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | FraudFlagScope isim/açıklama çelişkisi | ✅ KABUL | "Pre-create" CREATED state öncesini ifade eder, kayıt yokluğunu değil — bu nüans açıklamada yoktu | §2.21 açıklaması genişletildi: Transaction kaydı FLAGGED state'inde mevcut, "pre-create" = pre-CREATED-state |
| 2 | Anonymization bağlı verileri kapsamıyor | ✅ KABUL | UserNotificationPreference.ExternalId kişisel veri, RefreshToken session/cihaz verisi — §6.2'de yok | §6.2'ye bağlı entity cleanup kuralları eklendi (UserNotificationPreference, RefreshToken, Notification) |
| 3 | ProcessedEvent→OutboxMessage FK belirsizliği | ✅ KABUL | İlişki var ama FK mı mantıksal mı belirsiz. Retention-based cleanup için FK zorlaştırıcı | FK koymama kararı dokümante edildi, cleanup sırası belirtildi |
| 4 | SYSTEM sentinel SteamId | ⚠️ KISMİ | "SYSTEM" string gerçekten domain kuralını bozar. Ayrı model yerine format-uyumlu sentinel daha pragmatik | SteamId → "00000000000000001" (17 hane), domain istisnası açıkça dokümante edildi |

### Claude'un Ek Bulguları

Ek bulgu yok.

---

## Uygulanan Düzeltmeler

- [x] §2.21 FraudFlagScope.TRANSACTION_PRE_CREATE açıklaması genişletildi — "pre-create" = pre-CREATED-state, Transaction kaydı FLAGGED state'inde mevcut
- [x] §6.2'ye bağlı entity cleanup kuralları eklendi: UserNotificationPreference (soft delete + ExternalId temizleme), RefreshToken (revoke + soft delete + DeviceInfo/IpAddress temizleme), Notification (retention notu)
- [x] ProcessedEvent.EventId → OutboxMessage.Id: DB-level FK değil, mantıksal referans olarak dokümante edildi + cleanup sırası belirtildi
- [x] SYSTEM sentinel SteamId "SYSTEM" → "00000000000000001" (format-uyumlu), domain istisnası ve dışlama stratejisi açıkça yazıldı
- [x] Versiyon v2.5 → v2.6

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 5 tetiklendi
