---
name: feedback_ask_questions_via_modal
description: Soruları düz metinde numaralı liste olarak değil, AskUserQuestion (modal) ile sor
type: feedback
---

Kullanıcıya bir karar sorarken **AskUserQuestion aracını** kullan; soruyu mesajın içine numaralı liste olarak gömme.

**Why:** 2026-08-24'te kullanıcı bunu doğrudan istedi: *"soruları bundan sonra modal olarak sor, böyle cevaplaması zor oluyor."* Düz metindeki çoklu soru, kullanıcıyı hangi maddeye cevap verdiğini yazarak belirtmeye zorluyor; bir turda birden fazla karar sorulduğunda ("1", "2" gibi) hangi listeye ait olduğu da belirsizleşiyor.

**How to apply:**
- Karar gerektiren her şey modal'a gider — merge onayı, "şunu da düzelteyim mi", yaklaşım seçimi.
- Bir turda birden çok karar varsa hepsini **tek** AskUserQuestion çağrısında ayrı sorular olarak ver (araç 4 soruya kadar destekliyor), art arda mesajlarla değil.
- Önerdiğin seçenek **ilk** sırada ve etiketinde `(Önerilen)` olsun.
- Ölçüm sonuçları, bulgular ve durum özeti mesajda kalmaya devam eder — modal'a yalnız **karar** taşınır.
- Kendi başına verebileceğin rutin kararlar için modal açma; [[feedback_no_edit_permission_asks]] hâlâ geçerli — onay verilmiş bir akışın ara adımları sorulmaz.
