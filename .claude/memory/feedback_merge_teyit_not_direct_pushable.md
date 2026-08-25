---
name: feedback_merge_teyit_not_direct_pushable
description: Validate chat'inde post-merge "merge teyit" run ID'leri doğrudan main'e push edilemez — lokal pre-push hook bloke eder
type: feedback
---

Validate akışında PR squash-merge edildikten sonra eklemek istediğin "merge teyit" satırları (main squash SHA + post-merge CI/Docker Publish run ID'leri) **post-merge** üretildiği için o PR'ın squash'ına dahil değildir ve **doğrudan main'e commit+push EDİLEMEZ**: repo'da lokal `pre-push` hook `main`/`develop`'a doğrudan push'u bloke eder (`SKINORA_ALLOW_DIRECT_PUSH=1` yalnız acil-durum bypass'ı — kozmetik doc için kullanma). CI'daki `0. Guard (direct push)` job'undaki `[skip-guard]` ayrı bir katmandır ve pre-push hook'u atlamaz.

**Why:** WP15 doğrulamasında merge-teyit run ID'lerini main'e push etmeye çalıştım → pre-push hook reddetti; yerel commit reset'lenip atıldı. Established desen: merge-teyit satırları **bir sonraki task chat'inin feature branch'inde** eklenir (örn. WP3 "→ WP3 merge teyit: main `882834f`" sonradan eklenmiş; WP1 "→ WP1 merge teyit" de öyle).

**How to apply:** Validate PASS finalize'ında rapor + status + plan + repo memory'i **merge'den ÖNCE** branch'e commit+push et (squash bunları içersin). Merge + post-merge CI watch sonrası ham run ID'lerini yalnızca chat özetinde raporla; merge-teyit doc satırını commit'lemeye çalışma — bir sonraki task'ın branch'inde eklenecek. İlgili: [[feedback_validation_separate_chat]], [[feedback_claude_watches_ci_always]].
