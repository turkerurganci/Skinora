# GPT Cross-Review Raporu — 08_INTEGRATION_SPEC.md

**Tarih:** 2026-03-22
**Model:** OpenAI o3 (ChatGPT, manuel)
**Round:** 6
**Sonuç:** ⚠️ 6 bulgu (1 KRİTİK, 4 ORTA, 1 DÜŞÜK)

---

## GPT Çıktısı

### BULGU-1: 20 blok finality kuralı için solid block kaynağı tanımlanmamış
- **Seviye:** ORTA
- **Kategori:** Eksiklik
- **Konum:** §3.1, §3.4
- **Sorun:** Finality hesabı için mevcut solid block yüksekliğini verecek endpoint tanımlı değil. gettransactioninfobyid tek başına yetmez.
- **Öneri:** walletsolidity/getnowblock ekle, finality formülünü normatif yaz.

### BULGU-2: Wrong-token akışı spam token'ları da otomatik refund'a sokuyor
- **Seviye:** KRİTİK
- **Kategori:** Güvenlik
- **Konum:** §3.4
- **Sorun:** Filtresiz taramadaki tüm bilinmeyen token'lar WRONG_TOKEN_INCOMING → otomatik iade. Public deposit adreslerine spam token gönderilebilir → TRX tüketimi, operasyonel DoS.
- **Öneri:** Desteklenen token allowlist'i ekle, spam/bilinmeyen token'lar için ignore + log kuralı koy.

### BULGU-3: İkincil wrong-token taramasında cursor/pagination yok
- **Seviye:** ORTA
- **Kategori:** Edge Case
- **Konum:** §3.4
- **Sorun:** Birincil sorguda fingerprint var, ikincilde yok — 20+ spam transfer kaçırılabilir.
- **Öneri:** İkincil taramaya ayrı fingerprint cursor ekle.

### BULGU-4: Steam Market fallback davranışı üç bölüm arasında tutarsız
- **Seviye:** ORTA
- **Kategori:** Tutarlılık
- **Konum:** §7.3, §7.4, §7.5
- **Sorun:** Aynı senaryo için "cache kullan" vs "kontrolü atla" tutarsızlığı.
- **Öneri:** Tek canonical karar ağacı yaz.

### BULGU-5: Discord OAuth2 callback hata senaryoları tanımsız
- **Seviye:** ORTA
- **Kategori:** Eksiklik
- **Konum:** §6.1, §6.2, §6.4
- **Sorun:** Başarılı akış tanımlı ama access_denied, state_mismatch, invalid_grant, token exchange failure gibi hata durumları tanımsız.
- **Öneri:** OAuth2 callback hata matrisi ekle.

### BULGU-6: Steam Web API fallback "bekletme" vs "bloke etme" belirsizliği
- **Seviye:** DÜŞÜK
- **Kategori:** Belirsizlik
- **Konum:** §8
- **Sorun:** "Trade URL kaydı bekletilir veya işlem başlatma bloke edilir" — iki farklı davranış.
- **Öneri:** Tek davranış seç.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Uygulanan Aksiyon |
|---|-------------|---------------|-------------------|-------------------|
| 1 | Solid block kaynağı eksik | ✅ KABUL | `gettransactioninfobyid` tx'in block numarasını verir ama finality hesabı için mevcut solid block yüksekliği de gerekli. Doküman "20 blok" kuralını tanımlıyor ama `currentSolidBlock` nereden gelecek yazmıyor. TronGrid'de `walletsolidity/getnowblock` bunu sağlar. | §3.1'e `POST /walletsolidity/getnowblock` endpoint'i eklendi. §3.4'te finality formülü normatif olarak yazıldı: `currentSolidBlock - txBlock >= 20` ise PAYMENT_RECEIVED. |
| 2 | Wrong-token spam/griefing | ✅ KABUL | Çok değerli bulgu. Public deposit adreslerine herkes TRC-20 token gönderebilir. Filtresiz taramada görünen her bilinmeyen token otomatik refund'a sokulursa: (1) TRX/Energy tüketilir, (2) energy delegation + refund tx maliyeti oluşur, (3) düşük maliyetli griefing saldırısı mümkün olur. Yalnızca desteklenen token'lar (USDT/USDC) otomatik iade akışına girmeli, geri kalanı ignore + log. | §3.4'te wrong-token işleme kuralı eklendi: desteklenen allowlist'teki token'lar otomatik iade, bilinmeyen/spam token'lar SPAM_TOKEN_INCOMING → ignore + log. Tutar doğrulama tablosu da güncellendi. |
| 3 | İkincil tarama cursor eksik | ✅ KABUL | Birincil sorguda fingerprint cursor tanımlı ama ikincilde yok. Spam senaryosunda 20+ transfer gelirse eski kayıtlar kaçırılır veya aynı kayıtlar tekrar taranır. | İkincil tarama tablosuna `fingerprint` parametresi eklendi: birincil cursor'dan bağımsız, ikincil taramaya özel ayrı cursor tutulur. |
| 4 | Steam Market fallback tutarsızlık | ✅ KABUL | §7.3 cache stratejisini tanımlıyor, §7.4 "API erişilemez → kontrolü atla" diyor ama cache'e bakmıyor, §7.5 "cache 48 saate kadar geçerli" diyor. Aynı senaryo için üç farklı davranış — tek karar ağacı gerekli. | §7.4'e canonical karar ağacı eklendi: fresh cache → kullan, stale cache → kullan + yenile, expired/yok + API başarısız → atla + log. §7.5 bu karar ağacına referans verecek şekilde güncellendi. |
| 5 | Discord OAuth2 callback hataları | ✅ KABUL | §6.4 yalnızca DM gönderim hatalarını kapsıyor. OAuth2 bağlantı kurulumunun hata durumları (access_denied, invalid_grant, state_mismatch, token exchange failure, already_linked) tanımlanmamış. Eksik hata yönetimi UX sorunlarına ve güvenlik açıklarına yol açar (örn: state kontrolsüz CSRF). | §6.4'e "OAuth2 callback hataları" tablosu eklendi: access_denied, invalid_grant, state_mismatch, token exchange 5xx, /users/@me başarısız, already_linked senaryoları ve her biri için aksiyon tanımlandı. |
| 6 | Steam Web API fallback belirsizliği | ✅ KABUL | "Bekletilir veya bloke edilir" iki farklı davranış. Pending state = kullanıcı UI'da bekler ama devam edemez; hard block = işlem reddedilir. Tek davranış seçilmeli. | §8'de tek davranış olarak "pending state" seçildi: trade URL kaydı pending'e alınır, kullanıcıya bildirim gösterilir, API dönene kadar işlem başlatılamaz. |

### Claude'un Ek Bulguları

Ek bulgu yok.

### Özet

| Karar | Sayı |
|-------|------|
| ✅ KABUL | 6 |
| ⚠️ KISMİ | 0 |
| ❌ RET | 0 |
| Claude ek bulgu | 0 |
| **Toplam düzeltme** | **6** |

---

**Sonraki adım:** v1.9 → GPT'ye R7 gönderilecek.
