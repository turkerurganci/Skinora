---
name: feedback_new_chat_prompt
description: "Yeni chat'te yapılacak bir iş önerdiğimde, o chat'e yapıştırılacak hazır metni de yaz"
metadata:
  type: feedback
  node_type: memory
  originSessionId: dbaf6b9b-07cf-437a-bbfe-6beac45faf00
  modified: 2026-10-03T20:46:39.443Z
---

Türker'e "bunu yeni chat'te yap" dediğim her durumda (doğrulama, FAIL sonrası düzeltme, ayrı iş kolu), yeni chat'e yazacağı **hazır metni** de veririm: kod bloğu içinde, kopyala-yapıştır yapılabilir.

**Why:** Türker, 2026-10-03 (#333 FAIL sonrası): "bundan sonra yeni chatte bir şey yapmamı istediğimde yeni chate ne yazacağımı da yaz".

**How to apply:** Metin kısa ve kendi başına yeterli olsun: dal adı, PR numarası, nereden okunacağı (PR yorumu linki, backlog satırı), verilmiş kararlar (modal kararları) ve beklenen çıktı. Yeni chat bu chat'in bağlamını görmez; anchoring yaratmamak için doğrulama chat'ine yapım detayı koyma (bkz. [[feedback_validation_separate_chat]]).
