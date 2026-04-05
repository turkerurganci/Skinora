# Deep Review — Doküman Kalite ve Tutarlılık Analizi

> **Ne zaman kullanılır:** Herhangi bir dokümanın içeriğinin yeterliliğini, tutarlılığını ve sağlamlığını doğrulamak için.
>
> **Tetikleme:** Proje sahibi "deep review yap", "dokümanı kontrol et", "review et" veya "zayıf noktaları bul" dediğinde bu skill çalıştırılır.

## Parametreler

| Parametre | Zorunlu | Açıklama | Örnek |
|---|---|---|---|
| `hedef` | Evet | Kontrol edilecek doküman numarası | `05` |
| `bağlam` | Hayır | Karşılaştırılacak dokümanlar. Belirtilmezse hedef dokümanın `Bağımlılıklar` alanındaki dokümanlar + `10_MVP_SCOPE.md` kullanılır | `02,03,10` |
| `odak` | Hayır | Belirli bir konuya odaklanma. Belirtilmezse tüm doküman analiz edilir | `ödeme altyapısı`, `state machine` |

## Ön Hazırlık

1. **Hedef dokümanı oku** — baştan sona, tam olarak.
2. **Bağlam dokümanlarını oku** — hedef dokümanın `Bağımlılıklar` alanındaki tüm dokümanlar + `10_MVP_SCOPE.md` (zaten yoksa).
3. **Metodoloji kontrolü** — `00_PROJECT_METHODOLOGY.md`'den hedef dokümanın aşamasını oku. O aşamanın "amaç", "yaklaşım" ve "çıktılar" bölümlerini referans al — doküman bu beklentileri karşılıyor mu?
4. Eğer `odak` parametresi verilmişse, tüm katmanlarda sadece o konuya odaklan.

---

## Analiz Katmanları

### Katman 1 — Kapsam Doğrulaması (Coverage)

**Soru:** Bağlam dokümanlarındaki her gereksinim/akış/karar hedef dokümanda karşılık buluyor mu?

**Yöntem:**
- Bağlam dokümanlarındaki her bölümü, her tabloyu, her kural maddesini tara
- Her birinin hedef dokümanda karşılığını ara
- Karşılığı olmayan maddeleri `BOŞLUK (GAP)` olarak işaretle
- Hedef dokümanda olup bağlam dokümanlarında kaynağı olmayan maddeleri `KAYNAKSIZ EKLEME` olarak işaretle

**Kontrol listesi:**
- [ ] Her gereksinim (02) hedef dokümanda adresleniyor mu?
- [ ] Her akış adımı (03) hedef dokümanda teknik karşılığını buluyor mu?
- [ ] Her UI aksiyonu (04) hedef dokümanda teknik karşılığını buluyor mu?
- [ ] MVP kapsamındaki (10) her özellik hedef dokümanda yer alıyor mu?

---

### Katman 2 — Tutarlılık Kontrolü (Consistency)

**Soru:** Aynı kavram tüm dokümanlarda aynı şekilde tanımlanmış mı?

**Yöntem:**
- Hedef dokümandaki her kavramı (state isimleri, terimler, akış sırası, roller, modül isimleri) bağlam dokümanlarıyla karşılaştır
- Sayısal değerleri kontrol et (timeout süreleri, oranlar, limitler)
- Akış sırasını kontrol et — hedef dokümandaki sıralama bağlam dokümanlarıyla çelişiyor mu?

**Kontrol listesi:**
- [ ] State/durum isimleri tüm dokümanlarda aynı mı?
- [ ] Sayısal değerler tutarlı mı?
- [ ] Akış sıralaması tutarlı mı?
- [ ] Roller ve sorumluluklar tutarlı mı?
- [ ] Hedef doküman kendi içinde tutarlı mı? (aynı doküman içinde çelişki yok mu?)

---

### Katman 3 — Teknik Derinlik (Depth)

**Soru:** Her karar yeterince detaylandırılmış mı, yoksa uygulamacıyı belirsizlikle mi bırakıyor?

**Yöntem:**
- Her mimari karar, her teknoloji seçimi, her mekanizma tanımı için sor: "Bunu okuyan bir geliştirici uygulamaya başlayabilir mi, yoksa 'ama nasıl?' diye soracak mı?"
- "Gereksinime göre belirlenecek", "ileride düşünülecek", "detaylar sonra" gibi belirsiz ifadeleri tespit et
- Her belirsiz ifade için: bu belirsizlik kabul edilebilir mi (detay ilerideki dokümanın sorumluluğunda) yoksa bu dokümanda çözülmesi mi gerekiyor?

**Kontrol listesi:**
- [ ] Her teknoloji seçiminin gerekçesi var mı?
- [ ] Her mekanizmanın çalışma şekli açıklanmış mı?
- [ ] Belirsiz ifadeler ("belki", "gereksinime göre", "düşünülebilir") tespit edildi mi?
- [ ] Belirsiz olan hangilerinin bu dokümanda, hangilerinin sonraki dokümanlarda çözülmesi gerektiği ayrıştırıldı mı?

---

### Katman 4 — Güvenlik Analizi (Security)

**Soru:** Güven-kritik bir sistemde (escrow, kripto, Steam) güvenlik açıkları var mı?

**Yöntem:**
- Her dış iletişim kanalını (servisler arası, kullanıcı↔platform, platform↔üçüncü parti) tara
- Her veri akışında "bu manipüle edilebilir mi?" sorusunu sor
- Kimlik doğrulama, yetkilendirme, input validation, encryption katmanlarını kontrol et
- OWASP Top 10 perspektifinden değerlendir

**Kontrol listesi:**
- [ ] Servisler arası iletişimde kimlik doğrulama var mı?
- [ ] Webhook/callback'lerde imza doğrulama var mı?
- [ ] Hassas veriler (private key, secret, token) için saklama stratejisi net mi?
- [ ] Rate limiting tüm kritik endpoint'lerde tanımlı mı?
- [ ] Dış saldırı yüzeyleri (API, webhook, blockchain) korunuyor mu?

---

### Katman 5 — Hata Modu Analizi (Failure Mode Analysis)

**Soru:** Her bileşen çökerse ne olur? Sistem bunu nasıl tolere eder?

**Yöntem:**
- Hedef dokümandaki her bileşeni (servis, veritabanı, cache, üçüncü parti bağımlılık) listele
- Her biri için "bu 10 dakika erişilemez olursa ne olur?" sorusunu sor
- Tek nokta hatası (single point of failure) olan bileşenleri tespit et
- Recovery mekanizması tanımlı mı kontrol et

**Kontrol listesi:**
- [ ] Her bileşen için hata senaryosu düşünülmüş mü?
- [ ] Tek nokta hatası olan bileşenler tespit edildi mi?
- [ ] Recovery/fallback stratejisi var mı?
- [ ] Dış bağımlılıkların (Steam API, Tron node) kesintisi ele alınmış mı?
- [ ] Veri kaybı riski olan senaryolar var mı?

---

### Katman 6 — Veri Akışı İzleme (Data Flow Tracing)

**Soru:** Kritik veri parçaları dokümanlar boyunca tutarlı şekilde yönetiliyor mu?

**Yöntem:**
- Hedef dokümandaki kritik veri parçalarını belirle (ör: cüzdan adresi, item ID, ödeme tutarı, transaction state, kullanıcı kimliği)
- Her veri parçası için dokümanlar boyunca yolculuğunu izle: nerede oluşuyor → nerede doğrulanıyor → nerede kullanılıyor → nerede güncelleniyor → nerede siliniyor/arşivleniyor
- Kopukluk olan noktaları tespit et: bir dokümanda "X bilgisi saklanır" denmiş ama hedef dokümanda bunun teknik karşılığı yok

**Kontrol listesi:**
- [ ] Her kritik veri parçasının yaşam döngüsü (oluşturma → kullanma → güncelleme → silme) izlenebilir mi?
- [ ] Dokümanlar arasında veri akışında kopukluk var mı?
- [ ] Veri snapshot vs referans kararları net mi? (ör: "aktif işlem eski adresle tamamlanır" → snapshot mı referans mı?)
- [ ] Veri dönüşümleri (ör: fiyat + komisyon = toplam) tüm dokümanlarda aynı mı?

---

### Katman 7 — Ölçeklenebilirlik Değerlendirmesi (Scalability)

**Soru:** 10x trafik gelirse mimari dayanır mı? MVP kararları ilerideki büyümeyi engelliyor mu?

**Yöntem:**
- Her bileşenin yük altındaki davranışını değerlendir
- "Bu karar ileride darboğaz yaratır mı?" sorusunu sor
- Büyüme yolu tanımlı olan ve olmayan kararları ayır
- Veri büyümesi perspektifinden değerlendir (süresiz saklama kararlarının etkisi)

**Kontrol listesi:**
- [ ] Horizontal scaling yolu açık mı?
- [ ] Veri büyümesi stratejisi (partitioning, arşivleme) var mı?
- [ ] Stateful bileşenlerin (session, WebSocket) ölçeklenme planı var mı?
- [ ] Büyüme yolu tanımlı olan kararlar net mi? ("büyüdüğünde X'e geçilir" ifadeleri gerçekçi mi?)

---

### Katman 8 — Bağımlılık Risk Analizi (Dependency Risk)

**Soru:** Dış bağımlılıkların değişme, kapanma veya kısıtlanma riskleri değerlendirilmiş mi?

**Yöntem:**
- Hedef dokümandaki tüm dış bağımlılıkları listele (Steam API, Tron blockchain, kütüphaneler, servisler)
- Her biri için: API değişirse ne olur? Rate limit'e takılırsak? Servis kapanırsa?
- Bağımlılık soyutlama katmanı var mı? (ör: blockchain servisi değiştiğinde ana uygulama etkilenir mi?)

**Kontrol listesi:**
- [ ] Tüm dış bağımlılıklar listelenmiş mi?
- [ ] Her bağımlılığın rate limit'leri ve kısıtlamaları belirtilmiş mi?
- [ ] Bağımlılık değişikliği durumunda etki alanı sınırlı mı? (soyutlama katmanı)
- [ ] Kritik bağımlılıklar için alternatif veya fallback düşünülmüş mü?

---

## Çıktı Formatı

```
## Deep Review Raporu — [Hedef Doküman Adı]
**Tarih:** [Tarih]
**Hedef:** [Doküman numarası ve adı]
**Bağlam:** [Karşılaştırılan dokümanlar]
**Odak:** [Varsa odak alanı, yoksa "Tam analiz"]

---

### Özet Skor Tablosu

| # | Katman | Skor | Kritik Bulgu |
|---|--------|------|-------------|
| 1 | Kapsam (Coverage) | ✓/⚠/✗ | [varsa kısa açıklama] |
| 2 | Tutarlılık (Consistency) | ✓/⚠/✗ | [varsa kısa açıklama] |
| 3 | Teknik Derinlik (Depth) | ✓/⚠/✗ | [varsa kısa açıklama] |
| 4 | Güvenlik (Security) | ✓/⚠/✗ | [varsa kısa açıklama] |
| 5 | Hata Modu (Failure Mode) | ✓/⚠/✗ | [varsa kısa açıklama] |
| 6 | Veri Akışı (Data Flow) | ✓/⚠/✗ | [varsa kısa açıklama] |
| 7 | Ölçeklenebilirlik (Scalability) | ✓/⚠/✗ | [varsa kısa açıklama] |
| 8 | Bağımlılık Riski (Dependency) | ✓/⚠/✗ | [varsa kısa açıklama] |

**Genel Değerlendirme:** ✓ Sağlam / ⚠ İyileştirme gerekli / ✗ Kritik eksikler var

Skor açıklaması:
- ✓ = Bu katmanda sorun yok veya sadece minor notlar var
- ⚠ = İyileştirme önerileri var, doküman bu haliyle kullanılabilir ama güçlendirilmeli
- ✗ = Kritik eksik veya hata var, düzeltilmeden sonraki aşamaya geçilmemeli

---

### Katman Detayları

Her katman için:

#### Katman N — [Katman Adı]

**Bulgular:**

| # | Seviye | Bulgu | Kaynak | Öneri |
|---|--------|-------|--------|-------|
| 1 | Critical/High/Medium/Low | [Ne tespit edildi] | [Hangi bölüm/satır] | [Ne yapılmalı] |

Seviye tanımları:
- **Critical:** Düzeltilmeden sonraki aşamaya geçilmemeli. Güvenlik açığı, veri kaybı riski, temel işlevsellik eksikliği.
- **High:** Bu dokümanda düzeltilmeli. Önemli eksiklik ama sonraki aşamayı bloklamaz.
- **Medium:** Bu dokümanda veya sonraki ilgili dokümanda (06, 07, 08) ele alınabilir.
- **Low:** Farkında olunması yeterli, iyileştirme fırsatı.

---

### Aksiyon Planı

Tüm katmanlardan çıkan bulgular birleştirilir ve öncelik sırasına dizilir:

**Hemen düzeltilmesi gerekenler (Critical):**
- [ ] ...

**Bu dokümanda düzeltilmesi gerekenler (High):**
- [ ] ...

**Sonraki dokümanlarda ele alınabilecekler (Medium):**
- [ ] ... → [Hangi dokümanda]

**Notlar (Low):**
- ...

---

### Cross-reference Haritası

Hedef dokümandaki her ana bölümün hangi bağlam dokümanlarıyla eşleştiğini gösteren matris:

| Hedef Bölüm | 01 | 02 | 03 | 04 | 10 | Durum |
|---|---|---|---|---|---|---|
| [Bölüm adı] | - | §X | §Y | - | §Z | ✓/⚠/✗ |
```

---

## Önemli Kurallar

1. **Önce oku, sonra yargıla.** Tüm dokümanları tamamen okumadan analiz başlatma. Bir bölümdeki "eksiklik" başka bölümde karşılanmış olabilir.

2. **Seviye ayrımını doğru yap.** Her şey "Critical" değil. Bir eksikliğin seviyesi, o eksikliğin yaratacağı somut riske göre belirlenir:
   - Güvenlik açığı veya veri kaybı → Critical
   - Uygulama belirsizliği → High
   - Sonraki dokümanda çözülebilir → Medium
   - Farkındalık yeterli → Low

3. **Sonraki dokümanların kapsamını bil.** 06 (Data Model), 07 (API Design), 08 (Integration Spec), 09 (Coding Guidelines) henüz yazılmamış olabilir. Hedef dokümandaki bir eksiklik bu dokümanların doğal kapsamına giriyorsa, seviyeyi buna göre ayarla (Medium) ve hangi dokümanda çözülmesi gerektiğini belirt.

4. **Pozitif bulguları da belirt.** Sadece sorun listesi değil, iyi yapılmış kısımları da özet tabloda not et. Bu, dokümanın güçlü yönlerini korumaya yardımcı olur.

5. **Odak parametresi verilmişse:** Tüm 8 katmanı sadece o konuya odaklanarak çalıştır. Diğer konulardaki bulguları dahil etme.

6. **Çıktı uzunluğu:** Bulgu sayısına göre uyarla. Az bulgu varsa kısa tut, çok bulgu varsa tam detay ver. Gereksiz dolgu yazma.
