# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 24
**Sonuç:** 2 bulgu

---

## GPT Çıktısı

### BULGU-1: Dış kanal bildirimleri için kalıcı teslimat kaydı yok
- **Seviye:** ORTA
- **Sorun:** Email/Telegram/Discord gönderim durumu sadece loglarda — kalıcı DB kaydı yok.
- **Öneri:** NotificationDelivery entity eklenmeli.

### BULGU-2: FraudFlag ACCOUNT_LEVEL yaşam döngüsü belirsiz
- **Seviye:** ORTA
- **Sorun:** Arşivleme sadece TRANSACTION_PRE_CREATE'i kapsıyor, ACCOUNT_LEVEL için karar yok.
- **Öneri:** Scope bazında yaşam döngüsü ayrımı yazılmalı.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | NotificationDelivery | ✅ KABUL | Dispute/support'ta "bildirim gitti mi?" sorusu DB'den cevaplanmalı | Yeni entity (25.), enum, FK, archive set, traceability, silme stratejisi — tümü propagate edildi |
| 2 | FraudFlag scope-based lifecycle | ✅ KABUL | ACCOUNT_LEVEL transaction'a bağlı değil, arşivleme kapsamı dışı | §1.3 + §8.8: TRANSACTION_PRE_CREATE→arşivlenebilir, ACCOUNT_LEVEL→kalıcı soft delete |

---

## Uygulanan Düzeltmeler

- [x] NotificationDelivery entity: §1.1 envanter (#25), §1.2 ilişki diyagramı, §1.3 silme stratejisi
- [x] §2.23 DeliveryStatus enum (PENDING, SENT, FAILED)
- [x] §3.13a entity tanımı: field'lar, status-dependent constraint'ler, retry notu
- [x] §4.1 FK listesi, §7.1 traceability, §7.2 entity sayısı (24→25), §8.8 archive set
- [x] FraudFlag lifecycle: §1.3'te scope bazında ayrım (TRANSACTION_PRE_CREATE→arşivlenebilir, ACCOUNT_LEVEL→kalıcı)
- [x] §8.8 arşivleme: FraudFlag scope ayrımı, ACCOUNT_LEVEL arşivlenmeyen listesine eklendi
- [x] Versiyon v4.5 → v4.6

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [ ] Round 25 tetiklendi
