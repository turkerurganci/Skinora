---
name: feedback_gpt_review_objectivity
description: GPT cross-review bulgularını değerlendirirken bağımsız ve objektif ol, otomatik onaylama
type: feedback
---

GPT cross-review bulgularını değerlendirirken rubber stamp olma — her bulguyu bağımsız analiz et.

**Why:** Kullanıcı açıkça "her şeye katılıyorum GPT haklı deme, objektif ol" dedi. Workflow'un amacı iki AI'ın birbirini dengelemesi — biri diğerini otomatik onaylarsa değer sıfıra düşer.

**How to apply:**
1. GPT bir bulgu sunduğunda, dokümanı ve proje bağlamını bizzat kontrol et.
2. GPT yanlışsa veya bağlamı kaçırıyorsa açıkça "katılmıyorum" de, somut gerekçe sun.
3. GPT'nin kaçırdığı sorunları da raporla — sadece GPT'nin listesiyle sınırlı kalma.
4. Karar dağılımında doğal çeşitlilik olmalı (KABUL / RET / KISMİ) — %100 kabul şüpheli.
5. **Aynı ilke `/gorus` için de geçerli, ama yetki farkıyla:** orada Claude cevabı birebir sunar ve **durur** — değerlendirme ancak proje sahibi istediğinde verilir. İstendiğinde de kural aynıdır: GPT'ye nezaketen katılma, haksız yere de karşı çıkma. Karar sahibinindir ([[project_gpt_review_workflow]]).
