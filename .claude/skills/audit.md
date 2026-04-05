# Audit — Sistematik Doküman Denetimi

> **Ne zaman kullanılır:** Bir dokümanın eksik, tutarsız veya yetersiz noktalarını **eksiksiz** tespit etmek için.
>
> **Temel fark:** Klasik review "dokümanı oku, ne bulursan raporla" der — her seferinde farklı şeylere dikkat eder, farklı şeyler atlar. Audit "önce kontrol edilecek her şeyi listele, sonra her birini tek tek denetle" der. Envanter sabittir, atlama olmaz.
>
> **Tetikleme:** Proje sahibi "audit yap", "sistematik kontrol", "eksiksiz review" veya "audit" dediğinde bu skill çalıştırılır.

## Parametreler

| Parametre | Zorunlu | Açıklama | Örnek |
|---|---|---|---|
| `hedef` | Evet | Denetlenecek doküman numarası | `05` |
| `bağlam` | Hayır | Kaynak dokümanlar. Belirtilmezse hedefin `Bağımlılıklar` alanı + `10_MVP_SCOPE.md` kullanılır | `02,03,10` |
| `odak` | Hayır | Belirli bir konuya odaklanma. Verilirse envantere sadece o konuyla ilgili öğeler dahil edilir | `ödeme altyapısı` |

---

## Faz 0 — Ön Hazırlık

1. **Hedef dokümanı oku** — baştan sona, tam olarak.
2. **Bağlam dokümanlarını belirle:**
   - `bağlam` parametresi verilmişse → onu kullan
   - Verilmemişse → hedef dokümanın header'ındaki `Bağımlılıklar` alanındaki dokümanlar + `10_MVP_SCOPE.md`
3. **Bağlam dokümanlarını oku** — baştan sona, tam olarak.
4. **Metodoloji kontrolü** — `00_PROJECT_METHODOLOGY.md`'den hedef dokümanın aşamasını oku. O aşamanın "amaç", "yaklaşım" ve "çıktılar" bölümlerini referans al.

**Kural:** Tüm dokümanlar tam olarak okunmadan Faz 1'e geçilmez.

---

## Faz 1 — Envanter Çıkarma

**Amaç:** Analiz başlamadan önce, kontrol edilecek HER öğeyi çıkar ve numaralandır. Envanter = neyin kontrol edileceğinin sözleşmesi.

### 1.1 Kaynak Dokümanlardan Çıkarma

Bağlam dokümanlarındaki şu yapıları öğe olarak çıkar:

| Yapı | Kural |
|---|---|
| Tablo satırları | Her satır bir öğe |
| Madde işaretli listeler | Her madde bir öğe |
| Numaralı adımlar | Her adım bir öğe |
| Alt başlık altındaki kurallar | Her kural bir öğe |
| Edge case tabloları | Her senaryo satırı bir öğe |

**Numaralandırma formatı:**
```
[DokümanNo]-§[BölümNo]-[SıraNo]
Örnek: 02-§4.4-02 → "02 dokümanı, §4.4, 2. öğe"
```

**Odak parametresi verilmişse:** Sadece o konuyla doğrudan ilgili öğeleri envantere dahil et.

### 1.2 Hedef Dokümanın İç Envanteri

Hedef dokümanın kendisinden de öğe çıkar — bunlar iç tutarlılık ve kalite kontrolü için:

- Her bileşen, servis veya modül
- Her teknoloji seçimi veya mimari karar
- Her sayısal değer (timeout, oran, limit, eşik)
- Her dış bağımlılık (API, servis, kütüphane)
- Her iletişim kanalı (servisler arası, kullanıcı↔platform, platform↔üçüncü parti)
- Her veri varlığı veya veri akışı

Bu öğeler `HH-§[BölümNo]-[SıraNo]` formatında numaralandırılır (HH = hedef dokümanın numarası).

### 1.3 Envanter Çıktısı

Envanter her kaynak doküman için ayrı tablo olarak çıktıya yazılır:

```
### Envanter — 02_PRODUCT_REQUIREMENTS

| # | ID | Öğe Özeti | Kaynak Bölüm |
|---|---|---|---|
| 1 | 02-§2.1-01 | İşlem oluşturma: satıcı item seçer, fiyat girer | §2.1 Temel Akış |
| 2 | 02-§2.1-02 | Alıcı kabulü: alıcı detayları görür ve kabul eder | §2.1 Temel Akış |
| ... | ... | ... | ... |

**Toplam: N öğe**
```

**Kural:** Envanter çıktıya yazılmadan ve toplam öğe sayısı belirtilmeden Faz 2'ye geçilmez.

---

## Faz 2 — Eşleştirme

Her envanter öğesi için hedef dokümanda karşılığını bul ve durumunu işaretle:

```
### Eşleştirme — 02_PRODUCT_REQUIREMENTS → [Hedef Doküman]

| # | Envanter ID | Öğe Özeti | Hedef Bölüm | Durum |
|---|---|---|---|---|
| 1 | 02-§2.1-01 | İşlem oluşturma | §3.2 Transaction Flow | ✓ |
| 2 | 02-§4.4-02 | Fazla tutar iadesi | — | ✗ |
| 3 | 02-§3.1-03 | Ödeme timeout kuralı | §5.1 Timeout Mgmt | ⚠ |
```

**Durum tanımları:**
- **✓** — Hedef dokümanda açık karşılığı var
- **✗** — Hedef dokümanda karşılığı yok (GAP)
- **⚠** — Var ama eksik, belirsiz veya kısmi

**Kural:** Envanterdeki her öğenin durumu ✓, ⚠ veya ✗ olarak işaretlenir. Durumu belirsiz bırakılan öğe olamaz. Faz 2 sonunda her kaynak için `Toplam: N öğe (X ✓, Y ⚠, Z ✗)` sayımı yazılır. Bu sayım Faz 1'deki toplam öğe sayısıyla eşleşmelidir.

---

## Faz 3 — Analiz

### 3.1 ✗ (GAP) Olan Öğeler İçin

Bu bir eksiklik bulgusudur. Her biri için:
- **Ne eksik:** Kaynak dokümanda ne tanımlı, hedefte ne yok
- **Seviye:** Bu dokümanın kapsamında mı (High), yoksa sonraki dokümanın doğal sorumluluğunda mı (Medium)?
- **Öneri:** Nereye, ne eklenmeli

### 3.2 ⚠ (Kısmi) Olan Öğeler İçin

Var ama yetersiz. Her biri için:
- **Ne var, ne eksik:** Hedefte bulunan kısım vs kaynak dokümandaki tam tanım
- **Seviye ve öneri**

### 3.3 ✓ Olan Öğeler İçin — Kalite Denetimi

Her ✓ öğesini aşağıdaki sorularla denetle. **Her soru her öğe için geçerli değildir** — sadece ilgili olanları uygula:

| Soru | Ne Zaman Uygulanır |
|---|---|
| **Tutarlılık:** Kaynak dokümandaki ifadeyle birebir aynı mı? Terimler, sayısal değerler, sıralama eşleşiyor mu? | Her zaman |
| **Yeterlilik:** Bu bilgiyle bir geliştirici uygulamaya başlayabilir mi, yoksa "ama nasıl?" diye soracak mı? | Mekanizma, akış veya teknik karar içeren öğeler |
| **Güvenlik:** Dış etkileşim, veri saklama veya yetkilendirme içeriyorsa — güvenlik açısından ele alınmış mı? | Dış iletişim, hassas veri, auth içeren öğeler |
| **Dayanıklılık:** Bu bileşen/bağımlılık çökerse ne olur? Hata senaryosu tanımlı mı? | Bileşen, servis veya dış bağımlılık içeren öğeler |
| **Veri bütünlüğü:** Bu veri nerede oluşuyor, nerede doğrulanıyor, nerede kullanılıyor? Yaşam döngüsü izlenebilir mi? | Veri akışı veya veri dönüşümü içeren öğeler |

**Sorun yoksa bulgu üretme.** "Sorun yok" geçerli ve istenen bir sonuçtur. Bulgu üretmek için zorlanma.

Bulunan her sorun için:
- **Envanter ID'si** ile ilişkilendir
- **Seviye** belirle (Critical / High / Medium / Low)
- **Somut öneri** yaz

---

## Çıktı Formatı

```
## Audit Raporu — [Hedef Doküman Adı]
**Tarih:** [Tarih]
**Hedef:** [Doküman numarası ve adı]
**Bağlam:** [Kaynak dokümanlar]
**Odak:** [Varsa odak alanı, yoksa "Tam denetim"]

---

### Envanter Özeti

| Kaynak | Toplam Öğe | ✓ | ⚠ | ✗ |
|---|---|---|---|---|
| 02 | 47 | 41 | 4 | 2 |
| 03 | 23 | 20 | 2 | 1 |
| 10 | 15 | 15 | 0 | 0 |
| Hedef (iç) | 18 | 16 | 1 | 1 |
| **Toplam** | **103** | **92** | **7** | **4** |

---

### Envanter ve Eşleştirme Detayı

[Her kaynak doküman için Faz 1 + Faz 2 birleşik tablosu]

---

### Bulgular

| # | Envanter ID | Tür | Seviye | Bulgu | Öneri |
|---|---|---|---|---|---|
| 1 | 02-§4.4-02 | GAP | High | Fazla tutar iadesi mekanizması hedef dokümanda yok | §5.3'e iade akışı detayı eklenmeli |
| 2 | 03-§2.3-05 | Kısmi | Medium | Timeout freeze tetikleme mekanizması belirsiz | Hangi bileşenin freeze tetiklediği açıklanmalı |
| 3 | HH-§3.1-02 | Tutarsızlık | High | Timeout süresi burada 48h, 02-§3.1'de 24h | Kaynak dokümanla eşleştirilmeli |

Seviye tanımları:
- **Critical:** Güvenlik açığı, veri kaybı riski, temel işlevsellik eksikliği — düzeltilmeden ilerlenmemeli
- **High:** Bu dokümanda düzeltilmeli
- **Medium:** Bu veya sonraki dokümanda ele alınabilir
- **Low:** Farkındalık yeterli

---

### Aksiyon Planı

**Critical:**
- [ ] ...

**High:**
- [ ] ...

**Medium:**
- [ ] ... → [Hangi dokümanda]

**Low:**
- ...
```

---

## Kurallar

1. **Envanter her şeyden önce gelir.** Envanter çıkarılmadan analiz başlamaz. Envanter çıktıya yazılmadan eşleştirmeye geçilmez. Eşleştirme tamamlanmadan analize geçilmez. Faz sırası kesindir.

2. **Her öğenin durumu işaretlenir.** Envanterdeki hiçbir öğe durumu belirsiz bırakılamaz. ✓, ⚠ veya ✗ olmalı.

3. **Sayılar tutmalı.** Faz 1'deki toplam öğe sayısı = Faz 2'deki eşleştirme sayısı. Eşit değilse öğe atlanmış demektir — geri dön ve tamamla.

4. **"Sorun yok" geçerli bir sonuçtur.** Bir öğede, bir katmanda veya tüm dokümanda sorun yoksa "sorun yok" de. Bulgu üretmek zorunluluğu yok.

5. **Seviye ayrımını doğru yap.** Her şey Critical değil:
   - Güvenlik açığı veya veri kaybı → Critical
   - Bu dokümanda düzeltilmeli → High
   - Sonraki dokümanda çözülebilir → Medium
   - Farkındalık yeterli → Low

6. **Sonraki dokümanların kapsamını bil.** Hedef dokümandaki bir eksiklik sonraki dokümanların (07 API, 08 Integration, 09 Coding Guidelines) doğal kapsamına giriyorsa → Medium, hangi dokümanda çözüleceğini belirt.

7. **Pozitif bulguları envanter özeti gösterir.** Ayrıca "iyi yapılmış" listesi yazmaya gerek yok — ✓ sayısı dokümanın güçlü yönlerini yansıtır.

8. **Hedef dokümanın kendi iç tutarlılığı da envanterin parçasıdır.** Hedef dokümanın bir bölümünde söylenen, aynı dokümanın başka bölümüyle çelişmemeli.
