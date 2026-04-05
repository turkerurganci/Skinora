# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 15
**Sonuç:** 4 bulgu (1 KRİTİK, 3 ORTA)

---

## GPT Çıktısı

### BULGU-1: Escrow sonrası güncel Steam asset kimliği saklanmıyor
- **Seviye:** KRİTİK
- **Kategori:** Teknik Doğruluk / Edge Case
- **Konum:** §3.5, §3.9
- **Sorun:** Steam trade sonrası asset ID değişir — modelde sadece orijinal ItemAssetId var.
- **Öneri:** EscrowBotAssetId + DeliveredBuyerAssetId eklenmeli.

### BULGU-2: SteamId anonimleştirme kuralı dokümanın kendi istisna tanımıyla çelişiyor
- **Seviye:** ORTA
- **Kategori:** Tutarlılık
- **Konum:** §6.2, §8.5
- **Sorun:** "Bilinen tek istisna" deniyor ama ANON_{GUID} ikinci istisna.
- **Öneri:** Her iki istisna da açıkça yazılmalı.

### BULGU-3: Aynı transaction için birden fazla aktif dispute açılabiliyor
- **Seviye:** ORTA
- **Kategori:** Edge Case / UX
- **Konum:** §3.11, §3.5, §5.1
- **Sorun:** Farklı türde eşzamanlı dispute mümkün ama HasActiveDispute boolean tek.
- **Öneri:** İş kuralı netleştirilmeli.

### BULGU-4: Finansal alanlarda rounding kuralı normatif değil
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk
- **Konum:** §3.5, §3.7
- **Sorun:** Hesaplama sırası, rounding modu, tolerance tanımsız.
- **Öneri:** Tek normatif kural yazılmalı.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | Asset ID yaşam döngüsü | ✅ KABUL — KRİTİK | Steam trade sonrası asset ID değişir, aynı classId'den çok item olabilir — implementasyon blocker | EscrowBotAssetId + DeliveredBuyerAssetId eklendi, state constraint'ler + §8.4 lineage notu |
| 2 | SteamId istisna tutarsızlığı | ✅ KABUL | "Tek istisna" → iki istisna (sentinel + ANON) | İstisna metni güncellendi — her iki durum açıkça listelendi |
| 3 | Çoklu aktif dispute | ⚠️ KISMİ | 02 §10.2 sadece aynı türde tekrarı yasaklar — farklı türde eşzamanlı bilinçli tasarım | İş kuralı netleştirildi: farklı türde eşzamanlı dispute mümkün, HasActiveDispute semantiği açıklandı |
| 4 | Rounding kuralı | ✅ KABUL | decimal(18,6) var ama hesaplama sırası/modu yok | §8.3 rounding kuralları eklendi: AwayFromZero, hesaplama sırası, tolerance = yok |

### Claude'un Ek Bulguları

- **EK-1:** §8 section numaraları yeni bölümlerle kayıp — tümü yeniden numaralandırıldı (8.1-8.9).
- **EK-2:** TransactionHistory.ActorId referansı §8.5 → §8.7 düzeltildi.

---

## Uygulanan Düzeltmeler

- [x] Transaction'a EscrowBotAssetId + DeliveredBuyerAssetId field'ları eklendi
- [x] State-dependent constraint: ITEM_ESCROWED→EscrowBotAssetId NOT NULL, ITEM_DELIVERED→DeliveredBuyerAssetId NOT NULL
- [x] §8.4 Item Asset Lineage implementasyon notu eklendi
- [x] SteamId domain istisnası "tek" → "iki" olarak düzeltildi (sentinel + ANON)
- [x] Çoklu aktif dispute iş kuralı ve HasActiveDispute semantiği netleştirildi
- [x] §8.3 Finansal Hesaplama ve Rounding Kuralları eklendi (AwayFromZero, sıra, tolerance yok)
- [x] §8 section numaraları 8.1-8.9 olarak düzeltildi, internal referanslar güncellendi
- [x] Versiyon v3.6 → v3.7

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 16 tetiklendi
