# GPT Cross-Review Raporu — 09_CODING_GUIDELINES.md (Round 5)

**Tarih:** 2026-03-20
**Model:** OpenAI GPT-5.4 Thinking (ChatGPT, manuel)
**Round:** 5
**Sonuç:** ⚠️ 3 bulgu (0 KRİTİK, 3 ORTA)

---

## GPT Çıktısı

### BULGU-1: Freeze sonrası warning job tekrar planlanınca duplikasyon riski var
- **Seviye:** ORTA
- **Kategori:** Edge Case, UX
- **Konum:** §13.3, §13.6
- **Sorun:** Warning gönderildikten sonra freeze → resume olursa ikinci warning üretilebilir. Warning'in gönderilip gönderilmediğine dair state yok.
- **Öneri:** TimeoutWarningSentAt benzeri alan tut, resume'da yalnızca gönderilmemiş warning'i schedule et.

### BULGU-2: Outbox dispatcher lock süresi sabit ve işlem süresinden kopuk
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk, Edge Case
- **Konum:** §13.4
- **Sorun:** 30 saniyelik sabit lock süresi, ProcessPendingEventsAsync süresini aşabilecek backlog durumlarını karşılamıyor. Lock dolunca ikinci dispatcher çalışabilir.
- **Öneri:** Lock süresini processing süresine göre tasarla, batch size ile sınırla.

### BULGU-3: AI kontrol listesi state transition gerekliliklerini eksik taşıyor
- **Seviye:** ORTA
- **Kategori:** Tutarlılık
- **Konum:** §24, §25
- **Sorun:** §24'te TransactionHistory + AuditLog (gerekiyorsa) var, §25'te TransactionHistory eksik ve AuditLog koşulsuz.
- **Öneri:** §25'i §24 ile birebir hizala.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Uygulanan Aksiyon |
|---|-------------|---------------|-------------------|-------------------|
| 1 | Warning duplikasyon riski | ✅ KABUL | Freeze → resume sonrası warning tekrarı gerçek edge case. Mevcut handler state doğrulaması (Round 3) state/frozen kontrol eder ama "zaten gönderildi mi" kontrolü yok. | Resume kodunda warning yalnızca `TimeoutWarningSentAt == null` ise schedule ediliyor. Handler'a da `TimeoutWarningSentAt` kontrolü eklendi. |
| 2 | Lock süresi sabit | ✅ KABUL | Sabit 30s lock, backlog durumunda yetersiz kalır. Processing süresi lock'u aşarsa tekillik bozulur. | Sabit süre configurable `_lockLeaseDuration`'a çevrildi. Batch size kuralı eklendi. Lock lease = batch süre üst sınırı + marj. Consumer idempotency son savunma hattı olarak belirtildi. |
| 3 | §24 vs §25 uyumsuzluğu | ✅ KABUL | §24'te "TransactionHistory + AuditLog (gerekiyorsa)" var, §25'te TransactionHistory yok ve AuditLog koşulsuz. Net tutarsızlık. | §25 checklist'i §24 ile hizalandı: `TransactionHistory + AuditLog (gerekiyorsa)` açıkça eklendi. |

### Claude'un Ek Bulguları

Yok.

---

## Özet

| Metrik | Değer |
|--------|-------|
| GPT bulgu sayısı | 3 (0 KRİTİK, 3 ORTA) |
| Claude kararları | 3 KABUL, 0 KISMİ, 0 RET |
| Claude ek bulgu | 0 |
| Toplam düzeltme | 3 |
| Doküman versiyonu | v0.7 → v0.8 |

---

## Genel İlerleme (5 Round)

| Round | Bulgu | KRİTİK | ORTA | DÜŞÜK | Claude Ek |
|-------|-------|--------|------|-------|-----------|
| 1 | 7 | 3 | 4 | 0 | 1 |
| 2 | 3 | 0 | 3 | 0 | 0 |
| 3 | 3 | 0 | 3 | 0 | 0 |
| 4 | 2 | 0 | 2 | 0 | 0 |
| 5 | 3 | 0 | 3 | 0 | 0 |
| **Toplam** | **18** | **3** | **15** | **0** | **1** |

---

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [ ] Round 6 tetiklendi — GPT "SONUÇ: TEMİZ" hedefleniyor
