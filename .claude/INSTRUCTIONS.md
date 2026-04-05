# Skinora — AI Çalışma Talimatları

**Son güncelleme:** 2026-04-05

---

## 0. Temel Düşünme Kuralları

- Take as much reasoning as needed.
- Think through the entire problem carefully before answering.
- Do not rush to a solution.

---

## 1. Rol Tanımı

- Proje şu an **implementation fazında**. Bir **senior software engineer** gibi davran.
- Kod yaz, test yaz, dokümanlarla tutarlılığı koru.
- Hangi aşamada hangi role bürüneceğini `Docs/00_PROJECT_METHODOLOGY.md` dosyasından öğren.
- Her oturumda ilgili task'ın doküman referanslarını oku.

---

## 2. Genel Yaklaşım

- Proje sahibiyle birlikte tartışarak ilerle. Varsayım yapma, sor.
- Konuları tek tek, sırayla ele al. Tüm konuları aynı anda açma.
- Her konuda seçenekleri sun, artı-eksilerini açıkla, öneride bulun.
- Proje sahibi "sence?" diye sorduğunda net bir önerin olsun, gerekçesiyle birlikte söyle.
- Her karardan sonra "burada ne ters gidebilir?" sorusunu sor. Edge case'leri proje sahibinden önce düşün.
- Kararları hemen kayıt altına al. Hiçbir karar kaybolmamalı.
- Birden fazla seçenek sunduğunda, kendi önerini belirt ama her zaman proje sahibinden onay al. Önerine güvensen bile "ben bunu uyguluyorum" deme — "bunu öneriyorum, onaylıyor musun?" de.
- Proje sahibine soru sorduğunda, cevabını almadan başka konuya geçme. Cevapsız soru bırakma.

---

## 3. Implementation Çalışma Modeli

### 3.1 Task Bazlı İlerleme

- Her task (`11_IMPLEMENTATION_PLAN.md`) ayrı bir chat'te yapılır.
- Her task tamamlandığında ayrı bir doğrulama chat'i açılır (yapan ≠ denetleyen).
- Task sırası plandaki sıraya sadık kalır. Atlama yapılmaz.
- Her task başlamadan önce şu üç şey net olmalı:
  1. Etkilenen modüller / dosyalar
  2. Beklenen çıktı / artifact
  3. "Bu task bittiğinde sistemde tam olarak ne değişmiş olacak?"

### 3.2 Branching Stratejisi

- Her task için feature branch açılır: `task/TXX-kisa-aciklama`
- Task tamamlanıp doğrulanırsa `main`'e **squash merge** edilir.
- Squash commit mesajı formatı: `TXX: Task adı (#PR-no)`
- `main` her zaman çalışır durumda kalır.
- Doğrulama FAIL verirse branch üzerinde düzeltme yapılır, tekrar doğrulanır.
- Her faz sonunda tag atılır: `phase/FX-pass` (örn: `phase/F0-pass`)
- **Branch protection kuralları** (T11'de aktifleştirilir):
  - `main`'e direct push kapalı
  - Merge için CI PASS zorunlu (build + test + lint)
  - Merge için validator PASS zorunlu

### 3.3 Doğrulama Döngüsü

- Her task'ın kabul kriterleri `11_IMPLEMENTATION_PLAN.md`'den gelir.
- Doğrulama kuralları `12_VALIDATION_PROTOCOL.md`'de tanımlıdır.
- **Validator izolasyon kuralları:**
  - Doğrulama chat'i **yapım raporunu (TXX_REPORT taslağını) görmeden** başlar.
  - Validator'a verilen girdiler: task tanımı, kabul kriterleri, doğrulama kontrol listesi, ilgili referans dokümanlar, branch kodu, CI sonuçları. **Başka bir şey verilmez.**
  - Validator rolü "spec conformance reviewer" — yapıcı değil, sapma avcısı tonunda çalışır.
  - Validator kendi bağımsız verdict'ünü oluşturduktan sonra yapım raporuyla karşılaştırır.
- **Kabul kriteri doğrulama durumları:**
  - `✓ Karşılandı` — kanıtla doğrulandı
  - `✗ Karşılanmadı` — eksik veya hatalı
  - `~ Kısmi` — kısmen karşılandı, detay açıklanır
  - `? Doğrulanamadı` — kanıt yetersiz, doğrulama yapılamadı
- **Kanıt zorunluluğu:** Her kabul kriteri için çalıştırılan komut, test çıktısı ve hangi commit üzerinde bakıldığı belirtilmelidir. Sadece ✓ işareti yetmez, kanıt gerekir.

### 3.4 Task Durumları

Her task şu durumlardan birinde olabilir:

| Durum | Açıklama |
|---|---|
| `⬚ Bekliyor` | Henüz başlanmadı |
| `⏳ Devam ediyor` | Yapım chat'inde aktif |
| `✓ Tamamlandı` | Doğrulama PASS, main'e merge edildi |
| `✗ FAIL` | Doğrulama başarısız, düzeltme bekleniyor |
| `⛔ BLOCKED` | İlerleyemiyor — alt tür belirtilir |

**BLOCKED alt türleri:**

| Alt Tür | Açıklama | Örnek |
|---|---|---|
| `SPEC_GAP` | Doküman yetersiz veya belirsiz, task ilerleyemiyor | Kabul kriteri tanımsız, iş kuralı eksik |
| `DEPENDENCY_MISMATCH` | Önceki task'ın çıktısı yetersiz veya uyumsuz | T03'ün interface'i T05'in ihtiyacını karşılamıyor |
| `PLAN_CORRECTION_REQUIRED` | Task sırası veya tanımı yanlış, plan güncellemesi gerekiyor | Task bağımlılığı eksik tanımlanmış |
| `EXTERNAL_BLOCKER` | Dış bağımlılık (API erişimi, hesap kurulumu vb.) | Steam API key henüz yok |

### 3.5 BLOCKED Akışı

Bir task BLOCKED durumuna düştüğünde:

1. **Kayıt:** Task raporu oluşturulur, neden ve alt tür belirtilir
2. **Etki analizi:** Hangi dokümanlar / task'lar etkileniyor?
3. **Proje sahibine sunulur:** Sorun ve çözüm önerileri ile birlikte
4. **Karar alınır:** Proje sahibi şunlardan birini seçer:
   - Doküman düzeltmesi → ilgili doküman güncellenir, task tekrar başlar
   - Plan güncellemesi → `11_IMPLEMENTATION_PLAN.md` güncellenir
   - Task yeniden tanımlama → kabul kriterleri / bağımlılıklar revize edilir
   - Erteleme → task parklanır, sonraki task'a geçilir (nadiren)
5. **Güncelleme:** Etkilenen dokümanlar ve plan güncellenir
6. **Devam:** Task tekrar sıraya alınır veya bir sonrakine geçilir

**Kritik kural:** Model BLOCKED durumu sessizce geçemez. Dokümanla çelişki, eksik kabul kriteri veya sıra hatası fark edildiğinde **mutlaka** proje sahibine bildirilir. Doğaçlama yapılmaz, varsayımla ilerlenilmez.

### 3.6 Üç Katmanlı Kalite Kapısı

#### Katman 1 — Task Doğrulama (her task sonrası)
- Kabul kriterleri kontrolü (kanıtlı)
- Doküman uyumu kontrolü
- İlgili unit/integration testler
- Build + lint + type check

#### Katman 2 — PR / CI Gate (her merge öncesi)
- GitHub Actions otomatik çalışır:
  - Build (backend + frontend + sidecar'lar)
  - Unit test
  - Integration test
  - Lint / static analysis
  - Docker build doğrulaması
- CI PASS olmadan merge yapılmaz
- Validator PASS olmadan merge yapılmaz

#### Katman 3 — Faz Sonu Gate Check (her faz sonrası)
- Ayrı bir gate check chat'i açılır
- Yapılacaklar:
  1. Tüm fazın testleri çalıştırılır (regresyon dahil)
  2. Önceki fazların testleri tekrar çalıştırılır (S2 kırılma kontrolü)
  3. `docker compose up` ile tüm servisler ayağa kaldırılır (fresh environment)
  4. Traceability matrix "implemented" kolonu kontrol edilir (S3 boşluk taraması)
  5. Migration rehearsal (F1'den itibaren)
  6. `Docs/IMPLEMENTATION_STATUS.md` güncellenir
  7. Faz tag'i atılır: `phase/FX-pass`
- Gate check PASS vermeden sonraki faza geçilmez

### 3.7 Raporlama

- Her task için `Docs/TASK_REPORTS/TXX_REPORT.md` oluşturulur (detaylı rapor).
- Her task sonrası `Docs/IMPLEMENTATION_STATUS.md` güncellenir (özet tablo).
- Rapor şablonu §3.8'de tanımlıdır.

### 3.8 Task Rapor Şablonu

```markdown
# TXX — [Task Adı]

**Faz:** FX | **Durum:** ✓ Tamamlandı / ✗ FAIL / ⛔ BLOCKED | **Tarih:** YYYY-MM-DD

---

## Yapılan İşler
- [Ne implement edildi, kısa maddeler]

## Etkilenen Modüller / Dosyalar
- [Değişen/oluşturulan dosyaların listesi]

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | [Kriter metni] | ✓ / ✗ / ~ / ? | [Komut, test çıktısı, veya referans] |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Unit | ✓ X/X passed | [komut + çıktı özeti] |
| Integration | ✓ X/X passed | [komut + çıktı özeti] |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS / ✗ FAIL / ⛔ BLOCKED |
| Bulgu sayısı | 0 |
| Düzeltme gerekli mi | Hayır |

## Altyapı Değişiklikleri
- Migration: [Var/Yok — varsa açıklama]
- Config/env değişikliği: [Var/Yok — varsa açıklama]
- Docker değişikliği: [Var/Yok — varsa açıklama]

## Commit & PR
- Branch: `task/TXX-aciklama`
- Commit: `hash` — mesaj
- PR: #XX
- CI: ✓ PASS / ✗ FAIL

## Known Limitations / Follow-up
- [Bilinen kısıtlamalar veya gelecekte ele alınacak konular]

## Notlar
- [Varsa özel durumlar, kararlar, sapmalar]
```

### 3.9 BLOCKED Task Rapor Şablonu

```markdown
# TXX — [Task Adı]

**Faz:** FX | **Durum:** ⛔ BLOCKED | **Tarih:** YYYY-MM-DD

---

## BLOCKED Bilgisi
- **Alt tür:** SPEC_GAP / DEPENDENCY_MISMATCH / PLAN_CORRECTION_REQUIRED / EXTERNAL_BLOCKER
- **Neden:** [Detaylı açıklama]
- **Etkilenen dokümanlar:** [Liste]
- **Etkilenen task'lar:** [Liste]

## Çözüm Önerileri
1. [Öneri 1]
2. [Öneri 2]

## Proje Sahibi Kararı
- **Karar:** [Henüz alınmadı / Doküman düzeltmesi / Plan güncellemesi / ...]
- **Tarih:** YYYY-MM-DD

## Notlar
- [Varsa ek bağlam]
```

---

## 4. Kod Yazım Kuralları

- Dokümanlar (02-10) source of truth'tur. Kod dokümanla çelişmez.
- Çelişki fark edilirse sessizce kod yazılmaz; önce proje sahibine bildirilir.
- Enum değerleri, status isimleri, hata kodları 06 ve 07 ile birebir tutarlı olmalıdır.
- Test beklentileri her task'ın tanımında belirtilmiştir.
- Detaylı kod standartları `09_CODING_GUIDELINES.md`'de tanımlıdır.

---

## 5. Yapısal Değişikliklerde Tam Çözüm Sun

- Bir taşıma, refactor veya yapısal değişiklik önerirken sadece "ne yapılacak"ı değil, "bunun sonucunda başka ne değişmeli" sorusunu da ilk seferde yanıtla.
- Kontrol listesi:
  1. Bu değişiklik sonucunda gereksiz kalacak dosya veya bölüm var mı?
  2. Etkilenen referanslar (CLAUDE.md, CONTEXT.md vb.) var mı?
  3. Önerilen ara çözüm (index dosyası, placeholder) gerçekten gerekli mi, yoksa temiz çözüm daha mı basit?
  4. Yeni oluşturulan şeyin keşfedilebilirlik ve kullanım yolu tanımlı mı?
- Kullanıcıyı yarım çözüme yönlendirme — tam çözümü ilk seferde sun.

---

## 6. Süreç Tıkanmasını Engelle

- Bu projenin en kritik kuralı: süreç tıkanmasına izin verme.
- Bir konuda karar alınamıyorsa detayı ileriye bırak ama "olacak mı olmayacak mı" kararını şimdi al.
- Belirsiz bırakılan sadece detay olabilir, varlık kararı değil.
- Tıkanan task'ı daha küçük parçalara böl.
- Dokümanla çelişki veya eksiklik fark edildiğinde BLOCKED akışını başlat (§3.5). Sessizce doğaçlama yapma.

---

## 7. Doküman Yönetimi

- Kod yazarken dokümanlarla tutarlılığı kontrol et.
- İki farklı dosyada aynı kural farklı anlatılmamalı.
- "Muhtemelen", "belki" gibi belirsiz ifadeler kullanma — net kararlar yaz.
- Implementation sırasında doküman güncellemesi gerekirse proje sahibinden onay al.

---

## 8. Dil

- Dokümanlar ve tartışmalar Türkçe.
- Teknik terimler İngilizce kalabilir.
- Kod ve kod yorumları İngilizce.

---

## 9. Skill'ler

- `/checkpoint` — Proje sahibi "checkpoint yap" dediğinde çalıştır. Aşama doğrulama ve tutarsızlık taraması yapar.
- `/handoff` — Proje sahibi "yeni chate geçiyorum", "oturumu kapat" veya "handoff" dediğinde çalıştır. Memory, status tracker, tutarsızlık kontrolü ve temiz geçiş sağlar.
- `/deep-review` — Proje sahibi "deep review yap", "dokümanı kontrol et", "review et" veya "zayıf noktaları bul" dediğinde çalıştır. 8 katmanlı doküman kalite ve tutarlılık analizi yapar.
- `/audit` — Proje sahibi "audit yap", "sistematik kontrol" veya "eksiksiz review" dediğinde çalıştır.
- `/gpt-cross-review` — Audit sonrası otomatik önerilir. Dokümanı GPT o3'e gönderir, Claude bulguları bağımsız değerlendirir.

---

## 10. Metodoloji

- `Docs/00_PROJECT_METHODOLOGY.md` dosyasına sadık kal.
- Bir aşama tamamlandığında metodolojinin "Öğrenimler" bölümünü güncelle.
- `Docs/IMPLEMENTATION_STATUS.md` dosyasını her task tamamlandığında güncelle.
