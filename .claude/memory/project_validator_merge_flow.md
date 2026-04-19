---
name: Validator merge akışı (B — skill §17 disiplini korunur)
description: T27'den itibaren task chat merge etmez, validator chat PASS verince squash merge eder (T26 istisnai sapma sonrası teyit)
type: project
---

T27'den itibaren task merge akışı [validate.md](.claude/skills/validate.md) §17'e sıkı sıkıya uyar: **validator chat PASS verince squash merge eder.** Task chat CI PASS alır, PR açıp bırakır — merge etmez.

**Why:** T26 akışı skill §17'yi ihlal etti — task chat CI PASS sonrası squash merge etti (`c090b14`, PR #30), validator chat sadece post-hoc finalize yapabildi. Bu akışı resmîleştirmek (opsiyon A) validator'ın merge-öncesi veto yetkisini tamamen kaldıracaktı; nadiren kullanılan ama "spec conformance reviewer" rolünün gerçek kapısı olan bir yetki. Kullanıcı 2026-04-19'da meta-karar tartışmasında **B'yi seçti** — validator'ın veto yetkisini koru, tempo kaybına rağmen. T26 tek seferlik istisna, emsal oluşturmaz.

**How to apply:**
- **Task chat sonu:** CI PASS → PR açık bırak, merge etme. "Sen merge et" demez validator'a — onun sorumluluğu zaten.
- **Validator chat sonu:** PASS verildikten sonra `gh pr merge <PR> --squash` çağır (skill §17). Sadece PASS durumunda merge; FAIL/BLOCKED'da merge yok.
- **Önceki chat kararı ile çelişki:** Bu chat'te A savunulduysa (T26 task chat'te "böyle olacak" ısrarı) o karar geçersiz. Mevcut pozisyon B.
- **Feedback ihlali kaydı:** T26 için alınan akış kararı memory'e yazılmamıştı — bu feedback_dont_flip_recommendations'ın işlemesi için ön şart olan "kararları kaydet" disiplinine aykırıydı. Bundan sonra akış değişikliği kararları her zaman bu dosyaya ya da yenisine yansıtılacak.
