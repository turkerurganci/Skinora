# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 20
**Sonuç:** 2 bulgu

---

## GPT Çıktısı

### BULGU-1: Retry bekleyen kayıtlar için durum makinesi eksik tanımlı
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk / Operasyonel Tutarlılık
- **Konum:** §3.18, §3.21, §5.2
- **Sorun:** OutboxMessage ve ExternalIdempotencyRecord'da "retry beklenir" deniyor ama retry state machine'i yok.
- **Öneri:** Her iki yapı için retry semantiği açık yazılmalı.

### BULGU-2: Transaction tarafı kullanıcı invariantları admin tarafı kadar net değil
- **Seviye:** ORTA
- **Kategori:** Güvenlik / Tutarlılık
- **Konum:** §3.11, §3.8a, §8.5
- **Sorun:** Admin invariantı var ama Dispute.OpenedByUserId=BuyerId, SellerPayoutIssue.SellerId=SellerId gibi kurallar normatif değil.
- **Öneri:** Transaction tarafı aktör invariantları bölümü eklenmeli.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | Retry state machine | ✅ KABUL | "Retry beklenir" ama nasıl dönüyor belirsiz | OutboxMessage: dispatcher PENDING+FAILED çeker, indeks güncellendi. ExternalIdempotencyRecord: failed→in_progress→completed retry akışı yazıldı |
| 2 | Transaction aktör invariantları | ✅ KABUL | Admin invariantı §8.5'te var, transaction tarafı eşdeğer değil | §8.6 Transaction Tarafı Aktör Invariantları — 6 aksiyon için invariant tablosu eklendi |

### Claude'un Ek Bulguları

Ek bulgu yok.

---

## Uygulanan Düzeltmeler

- [x] OutboxMessage retry semantiği: dispatcher PENDING+FAILED çeker, FAILED→PENDING dönüşü yok, maks retry sonrası admin alert
- [x] §5.2 OutboxMessage performans indeksi: Filtered (Pending) → Filtered (Pending + Failed)
- [x] ExternalIdempotencyRecord retry semantiği: failed→in_progress→completed akışı, UNIQUE constraint desteği
- [x] §8.6 Transaction Tarafı Aktör Invariantları bölümü eklendi (dispute, payout issue, oluşturma, kabul, iptal, cüzdan)
- [x] §8 section numaraları 8.1-8.11 olarak düzenlendi, internal referanslar güncellendi
- [x] Versiyon v4.1 → v4.2

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 21 tetiklendi
