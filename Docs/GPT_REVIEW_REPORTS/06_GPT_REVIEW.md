# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 1
**Sonuç:** 6 bulgu

---

## GPT Çıktısı

### BULGU-1: SellerPayoutIssue entity'si dokümana eklenmiş ama modelin geri kalanına işlenmemiş
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Eksiklik
- **Konum:** §1.1, §3.8a, §4.1, §5, §6, §7.2
- **Sorun:** SellerPayoutIssue §3.8a'da ayrı entity olarak tanımlanmış, fakat entity envanterinde yok. Ayrıca FK referansları, indeks stratejisi, silme/retention politikası ve traceability matrix içinde de karşılığı bulunmuyor. §7.2'de "Tüm 20 entity" deniyor; bu sayı hem envanterdeki 23 ile hem de fiili entity sayısıyla çelişiyor.
- **Öneri:** SellerPayoutIssue için envanter, FK listesi, retention/silme stratejisi, index planı ve traceability matrix güncellenmeli. Toplam entity sayısı düzeltilmeli.

### BULGU-2: Kullanılan bazı enum/type'lar tanımlı değil
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk / Eksiklik
- **Konum:** §2, §3.5, §3.8a, §3.12
- **Sorun:** TimeoutFreezeReason, FraudFlagScope ve PayoutIssueStatus enum'ları entity field'larında referans veriliyor ama §2'de tanımları yok.
- **Öneri:** Bu üç enum §2'ye eklenmeli; değer setleri, anlamları ve state geçiş kuralları yazılmalı.

### BULGU-3: FraudFlag için scope semantiği veri bütünlüğü seviyesinde korunmuyor
- **Seviye:** ORTA
- **Kategori:** Edge Case / Teknik Doğruluk
- **Konum:** §3.12
- **Sorun:** FraudFlag'te Scope alanı iki farklı anlam taşıyor ama CHECK constraint yalnızca "en az biri dolu" diyor — yanlış kombinasyonlara izin veriyor.
- **Öneri:** Scope'a bağlı CHECK constraint tanımlanmalı.

### BULGU-4: Hesap silme/anonymization kuralı, User.SteamId şemasıyla çelişiyor
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Güvenlik
- **Konum:** §3.1, §5.1, §6.2
- **Sorun:** User.SteamId UNIQUE, NOT NULL ama anonimleştirme bölümünde "hash'lenir veya temizlenir" deniyor. "Temizlenir" NOT NULL ile çelişir, hash uzunluğu string(20)'ye sığmayabilir.
- **Öneri:** Tek strateji belirlenmeli — anonymized unique replacement.

### BULGU-5: Aynı finansal kavram farklı tablolarda farklı tip ile tutuluyor
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Teknik Doğruluk
- **Konum:** §2.2, §3.8, §3.22
- **Sorun:** BlockchainTransaction.Token StablecoinType enum (int), ColdWalletTransfer.Token string(10). Aynı kavram iki farklı representation.
- **Öneri:** Token tipi tüm finansal tablolarda StablecoinType enum'una bağlanmalı.

### BULGU-6: Filtered index örnekleri SQL Server implementasyonu için pseudo kalmış
- **Seviye:** DÜŞÜK
- **Kategori:** Teknik Doğruluk
- **Konum:** §5.2
- **Sorun:** `CANCELLED_*` ve `POST_CANCEL_*` wildcard notasyonu SQL Server filtered index predicate'lerinde geçersiz.
- **Öneri:** Tüm enum değerleri açıkça enumerate edilmeli.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | SellerPayoutIssue propagasyon eksikliği | ✅ KABUL | Doğrulandı — entity envanteri, ilişki diyagramı, silme stratejisi, FK referansları, indeks, traceability ve entity sayısı güncellenmemiş | Tüm bölümlere propagate edildi, sayı 24 olarak düzeltildi |
| 2 | 3 eksik enum tanımı | ✅ KABUL | Doğrulandı — TimeoutFreezeReason, FraudFlagScope, PayoutIssueStatus §2'de yok, inline tanımlar standart dışı | §2.20, §2.21, §2.22 olarak eklendi, state geçişleri tanımlandı |
| 3 | FraudFlag scope CHECK constraint | ⚠️ KISMİ | Sorun gerçek ama GPT'nin çözümü kısmen hatalı — TRANSACTION_PRE_CREATE scope'unda da TransactionId NULL (henüz işlem yok) | Scope-based constraint tanımlandı: her iki scope'ta UserId NOT NULL, TransactionId NULL |
| 4 | SteamId anonymization çelişkisi | ✅ KABUL | Doğrulandı — "temizlenir" NOT NULL ile çelişir, hash uzunluğu string(20)'ye sığmaz | `ANON_{kısa GUID}` stratejisi belirlendi |
| 5 | Token tip uyumsuzluğu | ✅ KABUL | Doğrulandı — aynı domain kavramı iki farklı representation | ColdWalletTransfer.Token → int (StablecoinType enum) |
| 6 | Filtered index wildcard notasyonu | ⚠️ KISMİ | Kısmen doğru — doküman SQL üretme amaçlı değil ama netlik eksikliği var | Wildcard'lar tam enum değerleriyle açıldı |

### Claude'un Ek Bulguları

- **EK-1:** SellerPayoutIssue cascade kuralları eksikti — BULGU-1 kapsamında eklendi.
- **EK-2:** §7.2'deki "doğrulanmıştır" ifadesi yanıltıcıydı — "izlenebilir" olarak düzeltildi.

---

## Uygulanan Düzeltmeler

- [x] SellerPayoutIssue: envanter, ilişki diyagramı, silme stratejisi, FK referansları, cascade kuralları, indeks stratejisi, traceability matrix, silme politikası notu eklendi
- [x] Entity sayısı 20 → 24 düzeltildi, "doğrulanmıştır" → "izlenebilir" olarak değiştirildi
- [x] TimeoutFreezeReason (§2.20), FraudFlagScope (§2.21), PayoutIssueStatus (§2.22) enum tanımları eklendi
- [x] FraudFlag CHECK constraint scope-based olarak yeniden tanımlandı
- [x] User.SteamId anonimleştirme stratejisi `ANON_{kısa GUID}` olarak netleştirildi
- [x] ColdWalletTransfer.Token tipi string(10) → int (StablecoinType enum) olarak düzeltildi
- [x] Filtered index wildcard'ları (`CANCELLED_*`, `POST_CANCEL_*`) tam enum değerleriyle açıldı
- [x] Versiyon v2.2 → v2.3

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 2 tetiklendi
