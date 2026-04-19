---
name: Status sorusunda her zaman IMPLEMENTATION_STATUS.md'yi oku
description: "Sırada ne var / hangi task / nerede kaldık" sorularında MEMORY snapshot'a güvenme, Docs/IMPLEMENTATION_STATUS.md'den oku
type: feedback
---

"Sırada ne var?", "hangi task?", "nerede kaldık?", "F1'de kalan ne?" gibi durum sorularına **MEMORY.md "Current Status" / "Next" alanlarına bakarak** cevap verme. Önce [`Docs/IMPLEMENTATION_STATUS.md`](../../Docs/IMPLEMENTATION_STATUS.md) dosyasını oku, oradaki tabloyu (✓/⏳/⬚ kolonları) kaynak kabul et.

**Why:** 2026-04-19'da kullanıcıya "sıradaki T26" dedim — gerçekte T26 ✓ PASS olmuştu (`c090b14` #30, validate `a1bf832`), sıradaki T27'ydi. Hatanın kaynağı: T26 validator chat'inde MEMORY.md "Next" satırı güncellenmemişti, IMPLEMENTATION_STATUS.md ise doğru duruyordu. Ben kontrol etmeden hafızadan konuştum, yanlış cevap verdim. Memory snapshot her zaman bir adım geride olabilir; tracker dosyası git'te tutulduğu için her commit'te güncellenir, tek doğru kaynak odur.

**How to apply:**
- Kullanıcı "sırada ne var / nerede kaldık / hangi task" diye sorduğu **her seferde**, önce IMPLEMENTATION_STATUS.md'yi `Read` ile aç, sonra cevap ver.
- MEMORY.md'deki "Next" / "Current Status" alanını bilgilendirici bir özet say, otoriter kaynak sayma. İki kaynak çelişirse tracker'ı kabul et ve memory'i güncelle.
- Validator/task kapanış akışında MEMORY.md "Current Status" + "Next" alanlarını güncellemeyi unutma — bu boşluk yine bu hatayı doğurur.
- Aynı kural diğer "snapshot" memory alanları için de geçerli: completed docs versiyonları, audit/GPT review listeleri, checkpoint sayısı vs. — kullanıcıya rakam/durum söylemeden önce kaynak dosyadan teyit et.
