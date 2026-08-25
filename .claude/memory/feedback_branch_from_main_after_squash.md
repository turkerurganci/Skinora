---
name: feedback_branch_from_main_after_squash
description: Squash merge sonrası yeni dal daima origin/main'den kesilmeli; task dalından kesilen dal doğduğu anda çakışmalı olur ve GitHub o hâlde CI run'ı hiç yaratmaz
type: feedback
---

Bir task dalı `main`'e **squash** merge edildikten sonra açılacak yeni dal (chore/docs/follow-up) **`origin/main`'den** kesilmelidir — merge edilmiş task dalının üstünden değil.

**Why:** Squash merge, task dalının tüm commit'lerini `main`'de **tek yeni commit** olarak yeniden yazar. Ağaç birebir aynıdır ama ortak ata eskide kalır. Task dalının üstüne kurulan dal bu yüzden doğduğu anda `CONFLICTING` olur, ve GitHub PR `DIRTY` durumdayken **hiçbir workflow run'ı yaratmaz** — ne queued, ne waiting, ne check-run. Bu, "Actions çalışmıyor / dakika limiti doldu" gibi tamamen yanlış bir teşhise götürür (2026-08-17, T129 sonrası chore PR #241'de tam olarak bu oldu; kullanıcı boş yere billing kontrolüne yönlendirildi).

**How to apply:**
- Merge'den sonra: `git fetch origin && git checkout -b <yeni-dal> origin/main`.
- Hata yapıldıysa düzeltme ucuzdur ve içerik kaybettirmez: önce `git diff <eski-taban> origin/main` ile ağaçların aynı olduğunu doğrula (squash'ta boş çıkar), sonra `git rebase --onto origin/main <eski-taban> <dal>` + `git push --force-with-lease`.
- **Teşhis kuralı:** bir PR için hiç workflow run'ı yoksa, billing/Actions ayarlarına gitmeden **önce** `gh pr view <no> --json mergeable,mergeStateStatus` bak. `CONFLICTING/DIRTY` tek başına bu semptomu üretir.

İlgili: [[feedback_merge_teyit_not_direct_pushable]], [[feedback_claude_watches_ci_always]]
