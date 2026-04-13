---
name: Yerleşim kontrolü yap
description: Proje sahibi bir içeriği belirli bir yere ekle dediğinde, o yerin doğru olup olmadığını değerlendir — körü körüne uyma
type: feedback
---

Proje sahibi "bunu X dosyasına ekle" dediğinde, içeriğin gerçekten oraya ait olup olmadığını sorgula.

**Why:** Skill oluşturup dosya haritasına eklerken, tetikleyiciyi INSTRUCTIONS.md'ye ekleme gerekliliği gözden kaçtı. Proje sahibi fark ettirdi. Benzer şekilde, proje sahibi yanlış yeri işaret etse bile AI körü körüne uygulamamalı.

**How to apply:** "Bunu X'e ekle" talimatı geldiğinde:
1. İçeriğin doğası ne? (kural, talimat, sınır, bağlam, görev tanımı)
2. Belirtilen dosyanın amacı ne?
3. İçerik ile dosya uyumlu mu?
4. Daha uygun bir yer var mı? (mevcut dosyada farklı bölüm veya yeni dosya)
Uyumsuzluk varsa doğru yeri gerekçesiyle öner.
Bu kural GUARDRAILS.md madde 7'ye de eklendi.
