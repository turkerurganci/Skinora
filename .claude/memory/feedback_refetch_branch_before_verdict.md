---
name: feedback_refetch_branch_before_verdict
description: Validate'te dal başka session'dan ilerleyebilir — verdict'i kapatmadan ÖNCE git fetch'i tekrarla, yoksa CI kanıtı eski commit'i gösterir
type: feedback
---

Validate skill'inin Adım 3'ü (`git fetch origin`) doğrulamanın **başında** bir kez yapılır, ama task chat'i / mobil session / kullanıcı doğrulama sürerken dala commit atmaya devam edebilir. `git fetch`'i **verdict'i kapatmadan önce tekrarla** ve dal HEAD'inin kendi CI run'ını doğrula — Adım 8a'nın kanıtı dal HEAD'ine ait olmalı, oturum başındaki commit'e değil.

**Why:** T128 doğrulamasında (2026-08-16) oturum `16cd40b` üzerinde başladı; `gh run list --branch task/T128-*` çıktısında tanımadığım bir SHA'ya (`887b431`) ait `in_progress` run görünce fark ettim — dal oturum sırasında ilerlemişti. Fark yalnız rapor dosyasıydı (`git diff 16cd40b 887b431 -- backend/ frontend/ Docs/` → **0 satır**), yani inceleme geçerli kaldı; ama fark etmeseydim rapora **eski commit'in** CI run ID'sini yazacaktım ve doğrulama kanıtı gerçekte merge edilen ağacı göstermeyecekti. Bu, Adım 0'daki "validator'ın kanıt standardı lokal temizlikten yüksektir" kuralının aynı sınıftan bir açığı.

**How to apply:** (1) Verdict'ten hemen önce `git fetch origin --prune` + `git rev-parse origin/task/TXX-*` ile dal HEAD'ini yeniden oku. (2) Fark varsa `git diff <eski> <yeni> -- backend/ frontend/ Docs/` ile **kod diff'inin boş olduğunu kanıtla** — boş değilse inceleme yeniden yapılır, boşsa raporda açıkça belirt. (3) Adım 8a CI kanıtı olarak dal HEAD'inin run'ını göster. (4) Ayrıca `git status --short` çıktısını da tekrar kontrol et — Adım -1 sadece başlangıç için değil. İlgili: [[feedback_clean_worktree_before_work]], [[feedback_claude_watches_ci_always]], [[feedback_merge_teyit_not_direct_pushable]].
