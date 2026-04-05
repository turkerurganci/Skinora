# GPT Cross-Review Raporu — 08_INTEGRATION_SPEC.md

**Tarih:** 2026-03-22
**Model:** OpenAI o3 (ChatGPT, manuel)
**Round:** 5
**Sonuç:** ⚠️ 4 bulgu (1 KRİTİK, 3 ORTA)

---

## GPT Çıktısı

### BULGU-1: Wrong-token tespiti vs contract_address filtresi çelişkisi
- **Seviye:** KRİTİK
- **Kategori:** Tutarlılık
- **Konum:** §3.4
- **Sorun:** contract_address filtresi beklenen tokena sabitlenmiş — wrong-token transferleri bu sorgudan asla dönmez. Wrong-token tespit ve refund akışı teknik olarak tetiklenemez.
- **Öneri:** Filtresiz ikinci sorgu veya uygulama tarafı ayrıştırma tanımla.

### BULGU-2: Telegram webhook secret_token credential envanterinde yok
- **Seviye:** ORTA
- **Kategori:** Güvenlik
- **Konum:** §5.2, §5.3, §9.2
- **Sorun:** Webhook secret_token zorunlu ama §9.2'de ayrı credential olarak listelenmemiş.
- **Öneri:** §9.2'ye ayrı satır ekle, bot token ile farkını belirt.

### BULGU-3: Telegram webhook update_id idempotency eksik
- **Seviye:** ORTA
- **Kategori:** Edge Case
- **Konum:** §5.2, §5.4
- **Sorun:** Telegram başarısız webhook'ları yeniden dener ama update_id bazlı deduplication tanımlanmamış.
- **Öneri:** update_id unique kontrolü ekle, tekrar gelen update no-op yapılsın.

### BULGU-4: Discord MVP guild-install akışı tanımlanmamış
- **Seviye:** ORTA
- **Kategori:** Eksiklik
- **Konum:** §6.1, §6.2
- **Sorun:** Mutual guild MVP yaklaşımı seçilmiş ama botun Skinora sunucusuna nasıl kurulacağı, invite scope'ları ve bot permissions tanımlanmamış.
- **Öneri:** Guild install adımlarını, scope'ları ve minimum permission setini tanımla.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Uygulanan Aksiyon |
|---|-------------|---------------|-------------------|-------------------|
| 1 | Wrong-token vs contract_address filtresi | ✅ KABUL | Açık mantık çelişkisi: `contract_address` filtresi beklenen tokena sabitlenmiş → yanlış token transferleri bu sorguda görünmez → §3.4'teki WRONG_TOKEN_INCOMING akışı tetiklenemez. İki aşamalı izleme gerekli: (1) beklenen token için filtrelenmiş sorgu, (2) tüm TRC-20 transferleri için filtresiz sorgu + uygulama tarafı beklenen/beklenmeyen ayrımı. | §3.4 izleme akışı iki aşamalı olarak yeniden yazıldı: Aşama 1 (birincil) contract_address filtreli, Aşama 2 (ikincil) filtresiz — beklenen token dışı transferler WRONG_TOKEN_INCOMING olarak işaretlenir. İdempotency kuralı her iki aşamaya uygulanır. |
| 2 | Telegram webhook secret credential eksik | ✅ KABUL | §5.2/5.3'te webhook secret_token zorunlu tutulmuş ve "Production secret (§9.2)" referansı verilmiş. Ancak §9.2'de yalnızca "Telegram Bot Token" var. Telegram'da bot token (API çağrıları) ve webhook secret token (gelen update doğrulama) farklı credential'lardır — ayrı saklanmalı. | §9.2'ye "Telegram Webhook Secret Token" satırı eklendi. Bot token ile webhook secret'ın farklı amaçlara hizmet ettiği not olarak belirtildi. |
| 3 | Telegram webhook update_id idempotency | ✅ KABUL | Telegram başarısız webhook teslimatlarını yeniden dener (resmi doküman). update_id monoton artan ve tekrar kontrolü için kullanılabilir. Özellikle /start deep-link bağlama akışında duplicate update işlenirse "kod zaten kullanıldı" hatasına yol açar. | §5.2 webhook tablosuna idempotency kuralı eklendi: update_id Redis cache'te (TTL 24h) tutulur, daha önce işlenmiş update_id tekrar gelirse no-op. |
| 4 | Discord MVP guild-install eksik | ✅ KABUL | §6.1'de "MVP'de Skinora Discord sunucusu + mutual guild yöntemi" seçilmiş ama botun sunucuya nasıl kurulacağı, invite URL scope'ları (bot + applications.commands) ve minimum permission seti tanımlanmamış. OAuth2 identify ile kullanıcı bağlama ≠ bot guild kurulumu — ikisi ayrı akışlar. | §6.1'e "MVP Guild Install" alt bölümü eklendi: sunucu oluşturma, bot invite (scope: bot + applications.commands), minimum permission (Send Messages), kullanıcı katılım yönlendirmesi. OAuth2 identify ve guild install ayrımı netleştirildi. |

### Claude'un Ek Bulguları

Ek bulgu yok.

### Özet

| Karar | Sayı |
|-------|------|
| ✅ KABUL | 4 |
| ⚠️ KISMİ | 0 |
| ❌ RET | 0 |
| Claude ek bulgu | 0 |
| **Toplam düzeltme** | **4** |

---

**Sonraki adım:** v1.8 → GPT'ye R6 gönderilecek.
