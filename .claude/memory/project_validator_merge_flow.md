---
name: project_validator_merge_flow
description: "Merge yetkisi: F8 boyunca yönetici (PASS + CI yeşilse) birleştirir; doğrulama genel SubAgentIndependentValidation akışıyla (F9'dan itibaren); T27 sonrası validator-chat-merge akışı tarihçede"
metadata:
  type: project
  node_type: memory
---
**Güncel durum (2026-10-20):** Aşağıdaki "validator chat merge eder" akışı (T27 → F7) F8'de yönetici düzeniyle değişti: F8 boyunca doğrulama ajanı PASS + PR CI yeşil verirse yönetici squash-merge eder ([[project_f8_yonetici_duzeni]]; doğrulama ajanı `opus` + `high`). Doğrulama işinin kendisi genel `SubAgentIndependentValidation` skill'iyle yürür (talimat → Türker'e göster → "başlat" → izole `general-purpose` alt ajan → rapor dosyasından oku); F8 bitene kadar yönetici düzeni istisnası geçerli, skill akışı F9'dan itibaren ([[feedback_validation_separate_chat]]). Birleştirme yetkisi F9'da skill ve Türker'in o günkü kararına göre yeniden belirlenir; bu dosya o zaman güncellenmeli.

## Tarihçe — T27'den itibaren validator chat merge akışı (B)

T27'den itibaren task merge akışı [validate.md](.claude/skills/validate.md) §17'e sıkı sıkıya uyar: **validator chat PASS verince squash merge eder.** Task chat CI PASS alır, PR açıp bırakır — merge etmez.

**Why:** T26 akışı skill §17'yi ihlal etti — task chat CI PASS sonrası squash merge etti (`c090b14`, PR #30), validator chat sadece post-hoc finalize yapabildi. Bu akışı resmîleştirmek (opsiyon A) validator'ın merge-öncesi veto yetkisini tamamen kaldıracaktı; nadiren kullanılan ama "spec conformance reviewer" rolünün gerçek kapısı olan bir yetki. Kullanıcı 2026-04-19'da meta-karar tartışmasında **B'yi seçti** — validator'ın veto yetkisini koru, tempo kaybına rağmen. T26 tek seferlik istisna, emsal oluşturmaz.

**How to apply:**
- **Task chat sonu:** CI PASS → PR açık bırak, merge etme. "Sen merge et" demez validator'a — onun sorumluluğu zaten.
- **Validator chat sonu:** PASS verildikten sonra `gh pr merge <PR> --squash` çağır (skill §17). Sadece PASS durumunda merge; FAIL/BLOCKED'da merge yok.
- **Önceki chat kararı ile çelişki:** Bu chat'te A savunulduysa (T26 task chat'te "böyle olacak" ısrarı) o karar geçersiz. Mevcut pozisyon B.
- **Feedback ihlali kaydı:** T26 için alınan akış kararı memory'e yazılmamıştı — bu feedback_dont_flip_recommendations'ın işlemesi için ön şart olan "kararları kaydet" disiplinine aykırıydı. Bundan sonra akış değişikliği kararları her zaman bu dosyaya ya da yenisine yansıtılacak.
