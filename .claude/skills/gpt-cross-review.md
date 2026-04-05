# GPT Cross-Review — İkinci AI Review Döngüsü

> **Ne zaman kullanılır:** Bir dokümanın audit'i tamamlandıktan sonra, bağımsız bir ikinci AI (GPT o3) ile cross-review yapmak için.
>
> **Temel fark:** Audit ve deep-review Claude'un kendi iç denetimidir. GPT Cross-Review ise farklı bir AI modeline dokümanı okutarak Claude'un kaçırmış olabileceği sorunları yakalar. Claude, GPT bulgularını objektif şekilde değerlendirerek rubber stamp olmaktan kaçınır.
>
> **Tetikleme:** Kalite döngüsünde audit sonrası otomatik önerilir. Proje sahibi "GPT review", "cross-review" veya "ikinci görüş" dediğinde de çalıştırılır.

## Parametreler

| Parametre | Zorunlu | Açıklama | Örnek |
|---|---|---|---|
| `hedef` | Evet | Review edilecek doküman yolu | `Docs/09_CODING_GUIDELINES.md` |
| `round` | Hayır | Kaçıncı review round'u (varsayılan: 1) | `2` |

---

## Faz 1 — GPT Review Çalıştırma

1. **Scripti çalıştır:**
   ```bash
   node scripts/gpt-review.mjs Docs/XX_DOKUMAN.md --round N
   ```
2. **Script otomatik olarak:**
   - Dokümanı GPT o3'e gönderir (7 kriter: tutarlılık, eksiklik, belirsizlik, teknik doğruluk, edge case, güvenlik, UX).
   - GPT bulgularını yapılandırılmış formatta alır (BULGU-N veya SONUÇ: TEMİZ).
   - Raporu `Docs/GPT_REVIEW_REPORTS/XX_GPT_REVIEW.md` (veya `_R{N}.md`) dosyasına yazar.
3. **Script çıktısını tam olarak oku** — GPT'nin her bulgusunu not al.

---

## Faz 2 — Claude Bağımsız Değerlendirmesi

**KRİTİK KURAL:** GPT'nin her bulgusuna otomatik "katılıyorum" deme. Bağımsız ve objektif ol.

Her GPT bulgusu için:

1. **Dokümanı bizzat kontrol et** — GPT'nin işaret ettiği bölümü oku.
2. **Proje bağlamını değerlendir** — İlgili diğer dokümanları kontrol et (GPT bunlara erişimi olmadığı için bağlam kaçırabilir).
3. **Karar ver:**
   - ✅ **KABUL** — GPT haklı, düzeltme gerekli. Düzeltme önerisi sun.
   - ❌ **RET** — GPT yanlış veya bağlamı kaçırıyor. Somut gerekçe zorunlu (neden yanlış olduğunu açıkla).
   - ⚠️ **KISMİ** — Sorun gerçek ama GPT'nin önerdiği çözüm uygun değil. Alternatif çözüm sun.

4. **GPT'nin kaçırdığı sorunları da raporla** — Sadece GPT'nin listesiyle sınırlı kalma. Dokümanı okurken fark ettiğin ek sorunları "Claude Ek Bulguları" olarak ekle.

**Objektivite kuralları:**
- %100 KABUL şüphelidir — her zaman kendi analizini yap.
- GPT farklı bir AI'dır, projenin bağlamını bilmez. Bu hem avantaj (taze göz) hem dezavantaj (bağlam eksikliği).
- RET gerekçesi "ben böyle düşünüyorum" değil, somut referans olmalı (doküman bölümü, proje kararı vb.).

---

## Faz 3 — Kullanıcıya Sunum

1. **Rapor dosyasındaki "Claude Bağımsız Değerlendirmesi" tablosunu doldur.**
2. **Kullanıcıya özet sun:**
   - Kaç bulgu geldi, kaçını kabul/ret/kısmi ettin.
   - Her bulgu için kısa açıklama ve kararın.
   - Ek bulguların varsa onları da sun.
3. **Kullanıcıdan onay al** — hangi düzeltmelerin uygulanacağına kullanıcı karar verir.

---

## Faz 4 — Düzeltme ve Tekrar

1. **Onaylanan düzeltmeleri uygula.**
2. **Tekrar GPT'ye gönder:** `--round N+1` ile scripti çalıştır.
3. **Faz 1'den itibaren tekrarla.**
4. **Çıkış koşulu:** GPT "SONUÇ: TEMİZ" döndüğünde döngü tamamlanır.

---

## Rapor Dosya Yapısı

```
Docs/GPT_REVIEW_REPORTS/
├── XX_GPT_REVIEW.md        # Round 1
├── XX_GPT_REVIEW_R2.md     # Round 2
├── XX_GPT_REVIEW_R3.md     # Round 3 (gerekirse)
└── ...
```

Her rapor dosyası şunları içerir:
- GPT çıktısı (ham bulgular)
- Claude bağımsız değerlendirme tablosu
- Claude ek bulguları
- Kullanıcı onay checklist'i
