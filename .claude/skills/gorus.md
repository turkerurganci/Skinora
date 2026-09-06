# Görüş — ChatGPT'den anlık ikinci görüş

> **Ne zaman kullanılır:** Proje sahibi herhangi bir anda, herhangi bir konuda ChatGPT'nin görüşünü istediğinde. Tipik tetikleyici bir şüphe cümlesidir: *"bu yöntem kesin doğru mu?"*, *"bunu GPT'ye sor"*, *"ikinci bir görüş alalım"*.
>
> **GPT Cross-Review ile KARIŞTIRILMAZ.** `/gpt-cross-review` bir **dokümanı** round'lar hâlinde denetler ve "SONUÇ: TEMİZ" gelene kadar döner. `/gorus` **tek bir sorunun tek atışlık görüşünü** alır ve orada biter. İkisi ayrı dizinlere yazar (`GPT_REVIEW_REPORTS/` ↔ `GPT_OPINIONS/`).
>
> **Tetikleme:** `/gorus <konu>` — ya da sahibi yukarıdaki gibi bir cümle kurduğunda.
>
> **Parametre:** `konu` — görüş istenen karar/yöntem/soru.

## Parametreler

| Parametre | Zorunlu | Açıklama | Örnek |
|---|---|---|---|
| `konu` | Evet | Görüş istenen konu | `gas fee tahmin yöntemi doğru mu` |
| `--transport manual` | Hayır | Otomatik yolu atla, doğrudan yapıştırma yoluna git | |
| `--model` | Hayır | Codex modeli (`gpt-5.6-sol` varsayılan, `gpt-5.6-terra` hızlı) | |
| `--allow-hex` | Hayır | Soru bilerek tam bir Tron tx hash'i içeriyorsa | |
| `--dry-run` | Hayır | Metni göster, hiçbir şey gönderme | |

---

## Yetki sınırı — bu skill'in kalbi

Bu akışta **karar verme yetkisi Claude'da değildir.** Üç yerde durulur:

1. **Soru gönderilmeden önce** — sahibi metni onaylamadan hiçbir şey gönderilmez.
2. **Cevap geldikten sonra** — Claude cevabı sunar ve **durur**. Kendi başına kod değiştirmez, commit atmaz, "GPT haklı, düzeltiyorum" demez.
3. **Ne yapılacağına** sahibi karar verir; Claude o talimatı uygular.

Claude'un kendi görüşünü söylemesi yasak değildir — **istendiğinde** söyler. İstenmeden değerlendirme tablosu üretmek, cevabı "yorumlamak" ya da uygulamaya geçmek bu skill'in ihlalidir.

---

## Faz 1 — Soruyu hazırla

**Amaç:** GPT projeyi bilmez. Cevabın kalitesi, sorunun taşıdığı bağlamın kalitesidir.

**Yap:** `Docs/GPT_OPINIONS/<tarih>_<slug>.prompt.md` dosyasına şu yapıda, **≤ 1 sayfa**, Türkçe bir metin yaz:

```markdown
## Soru
<tek cümle — evet/hayır ya da A/B. Sahibinin sorduğu şey, genişletilmemiş hâliyle.>

## Bağlam
<≤ 10 satır. Dosya:satır + kısa alıntı + ölçülmüş sayılar. GPT'nin bilmediği
her şey burada olmalı; burada olmayan hiçbir şeyi bildiğini varsayma.>

## Mevcut karar / yöntem ve gerekçesi
<≤ 8 madde. Neden böyle yapıldı.>

## Elenen alternatifler
| Alternatif | Neden elendi |

## Kabul edilen bedeller / riskler
<biliniyorsa. Bilinen bir zayıflığı gizlemek cevabı işe yaramaz kılar.>
```

**Sır kuralı:** private key, mnemonic, gerçek parola, API anahtarı **girmez**. Tron tx hash'lerini ilk 12 karaktere kısalt — tam 64-hex bir private key ile aynı şekildedir ve script onu **bloklar** (bilerek gerekiyorsa `--allow-hex`). Script göndermeden önce tarar; bulursa gönderim olmaz (çıkış kodu 5). Bu bekçi commit bekçisinden **daha katıdır**: dışa giden bir sır geri alınamaz.

**Karar kuralı:** Soru sahibinin sorduğundan **geniş olmamalı**. "Bu doğru mu?" sorusunu "tüm mimariyi değerlendir" hâline getirmek, sahibinin sormadığı bir işi başlatmaktır.

---

## Faz 2 — ONAY (HARD STOP)

**Amaç:** Gönderilen metin sahibinin gördüğü ve kabul ettiği metin olmalı.

**Yap:**
1. Hazırladığın metnin **tamamını** sohbette göster (kısaltma, "özetle şöyle" yok).
2. `AskUserQuestion` ile sor: **Gönder** / **Düzelt** / **Vazgeç**.
3. "Düzelt" → sahibinin söylediği değişikliği uygula, **yeniden onaya sun**.

**Karar kuralı (HARD STOP):** Onay alınmadan `gpt-ask.mjs` çalıştırılmaz. Onay **tek bir soru içindir**; sonraki soru için yeniden alınır. Bir önceki onay bir sonrakini kapsamaz.

---

## Faz 3 — Gönder

**Yap:**
```bash
node scripts/gpt-ask.mjs --question Docs/GPT_OPINIONS/<tarih>_<slug>.prompt.md --slug <slug>
```

Taşıyıcılar sırayla denenir: **codex** (ChatGPT aboneliği, API anahtarı gerekmez) → **api** (`OPENAI_API_KEY` varsa) → **manuel**.

Çıkış kodları yalnız **taşımayı** anlatır, cevabın içeriğini değil:

| Kod | Anlam | Ne yap |
|---|---|---|
| 0 | Cevap alındı ve kaydedildi | Faz 4 |
| 10 | Manuel yol — yapıştırma bekleniyor | Sahibine talimatı ilet, cevap gelince `--resume <kayıt.md>` |
| 5 | Soru gönderilmedi (sır kalıbı / çok uzun) | Metni düzelt, **yeniden onay al** |
| 1 | Hiçbir taşıyıcı çalışmadı | Sebebi söyle; `--transport manual` öner |

**Codex oturumu düşerse:** `codex login --device-auth` çıktısındaki kod ve bağlantı sahibine iletilir — girişi sahibi yapar. Not: `codex login status` bayat token'la da *"Logged in"* der; **gerçek kanıt çağrının kendisidir**.

---

## Faz 4 — Cevabı getir ve DUR

**Yap:**
1. GPT cevabını **birebir** sun. Kısaltma yok, "özetle diyor ki" yok. Uzunsa kayıt dosyasının yolunu ver ve tam metni de göster.
2. Kaynağı söyle: hangi taşıyıcı, hangi model, ne kadar sürdü.
3. **Dur.** Kapanış cümlesi: *"Ne yapmamı istersin?"*

**Karar kuralı (HARD STOP):** Cevaba dayanarak kod değiştirme, commit atma, PR açma, backlog satırı kapatma **yok** — sahibi söyleyene kadar. Sahibi Claude'un görüşünü sorarsa, bağımsız ve dürüst söyle: GPT'ye nezaketen katılma, ama haksız yere de karşı çıkma ([[feedback_gpt_review_objectivity]] ile aynı ilke, burada da geçerli).

---

## Faz 5 — Kayıt

**Yap:** Script `Docs/GPT_OPINIONS/YYYY-MM-DD_<slug>.md` dosyasını ve `README.md` index satırını zaten yazdı (Soru · GPT Cevabı · boş "Proje Sahibinin Kararı" bölümü · Kaynak). Aynı gün aynı slug ikinci kez sorulursa dosya `-2` ekiyle açılır — kayıt asla ezilmez. Sahibi kararını söyledikten **sonra**:

1. "Proje Sahibinin Kararı" bölümünü tek satırla doldur (ne yapılacağı + varsa gerekçesi).
2. `README.md` index satırındaki `⏳` işaretini güncelle.
3. Karar bir PR / backlog satırı / task raporuyla ilgiliyse oraya tek satır bağlantı düş.
4. Ara dosyalar (`*.prompt.md`, `*.answer.md`, `*.send.md`) gitignore'ludur; yalnız kayıt commit'lenir.

---

## Dosya yapısı

```
Docs/GPT_OPINIONS/
├── README.md                        # amaç, şablon, çalışan çağrı, index
├── 2026-09-03_ornek-konu.md         # kayıt (commit'lenir)
├── 2026-09-03_ornek-konu.prompt.md  # senin yazdığın soru (gitignore)
├── 2026-09-03_ornek-konu.send.md    # gönderilen tam metin — sistem promptu + soru (gitignore)
└── 2026-09-03_ornek-konu.answer.md  # manuel yolun cevabı (gitignore)
```
