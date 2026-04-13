# Skinora Project Memory

## Project Overview
- **Name:** Skinora — CS2 item escrow platform
- **Type:** Implementation phase (product discovery complete)
- **Language:** Turkish docs, English code

## Current Status (2026-04-13)
- **Completed docs:** 00 (v0.4), 01 (v1.1), 02 (v2.4), 03 (v2.2), 04 (v3.0), 05 (v2.3), 06 (v4.9), 07 (v2.1), 08 (v2.5), 09 (v0.9), 10 (v1.3), 11 (v0.5), 12 (v0.5)
- **Implementation:** F1 aktif — T01-T22 + T11.1 + T11.2 tümü ✓ PASS. T22 validator 2026-04-13 (PR #22 squash `863df04`, CI run 24316344485 ✓ 11/11 job, 23 integration test PASS). CI pipeline tam canlı + 4 savunma katmanı canlı.
- **T22 tamamlanan iş:** Dispute entity (06 §3.11, 13 field, 1 CHECK, unfiltered unique TransactionId+Type, soft delete). FraudFlag entity (06 §3.12, 13 field, 4 CHECK — scope-based + review-based, soft delete). DisputesModuleDbRegistration + FraudModuleDbRegistration + Program.cs registration. 23 integration test (11 Dispute + 12 FraudFlag: CRUD + CHECK + unique + FK + soft delete).
- **T14 not:** steam-tradeoffer-manager ^3.x npm'de mevcut değil, ^2.13.x kullanıldı, 08 §2.5 güncellendi
- **Next:** T23 (Notification, NotificationDelivery, UserNotificationPreference entity'leri). Geçmiş borç yok.
- **F0 Gate Check bulguları:** OutboxStartupHook DI fix (singleton/scoped), Frontend Dockerfile fix (alpine→slim)
- **Checkpoints completed:** 19 (CP1-CP18, CP18 = 12 audit + GPT review + etki yansıtma + checkpoint)
- **Audits completed:** 00-12
- **GPT cross-reviews completed:** 02 (6R, TEMİZ), 03 (5R, TEMİZ), 04 (14R, TEMİZ), 05 (8R, TEMİZ), 06 (26R, TEMİZ), 07 (6R, TEMİZ), 08 (12R, TEMİZ), 09 (9R, TEMİZ), 11 (4R, TEMİZ), 12 (4R, TEMİZ)
- **GPT cross-reviews pending:** yok
- **Uyumluluk yansıtma:** 02→(03-07) tamamlandı, 03→(02,04,05,07) tamamlandı, 04→(02,03,07) tamamlandı, 06→(02,03,05,07,08) tamamlandı, 06 R25-R26→(02,05,09) tamamlandı, 07→(02,03,08) tamamlandı, 08→(03,06,07) tamamlandı, 09 R8→(06) tamamlandı

## Key Files
- Status tracker: `Docs/PRODUCT_DISCOVERY_STATUS.md`
- Implementation tracker: `Docs/IMPLEMENTATION_STATUS.md`
- Task reports: `Docs/TASK_REPORTS/`
- Methodology: `Docs/00_PROJECT_METHODOLOGY.md` (v0.4)
- AI instructions: `.claude/INSTRUCTIONS.md`, `.claude/GUARDRAILS.md`
- Implementation skills: `.claude/skills/task.md`, `.claude/skills/validate.md`, `.claude/skills/gate-check.md`
- Audit reports: `Docs/AUDIT_REPORTS/` (00-12)
- GPT review reports: `Docs/GPT_REVIEW_REPORTS/` (02: R1-R6, 03: R1-R5, 04: R1-R14, 05: R1-R8, 06: R1-R26, 07: R1-R6, 08: R1-R12, 09: R1-R9, 11: R1-R4, 12: R1-R4)
- Checkpoint reports: `Docs/CHECKPOINT_REPORTS/` (CP1-CP18)

## User
- [user_profile.md](user_profile.md) — Kullanicinin teknik deneyimi (.NET, C#, SQL Server) ve projedeki rolu

## Feedback
- [feedback_think_through_fully.md](feedback_think_through_fully.md) — Yapisal degisiklik onerirken tum sonuclari ilk seferde dusun, yarim cozume yonlendirme
- [feedback_question_destructive_proposals.md](feedback_question_destructive_proposals.md) — Silme/kaldirma onermeden once gercekten gerekli mi diye sorgula
- [feedback_validate_placement.md](feedback_validate_placement.md) — "Bunu X'e ekle" dendiginde yerin dogrulugunu sorgula, koru korune uyma
- [feedback_dont_flip_recommendations.md](feedback_dont_flip_recommendations.md) — Onerileri hizlica degistirme, ilk gerekceni savun, kullanicinin her sorusuna "haklisin" deme
- [feedback_gpt_review_objectivity.md](feedback_gpt_review_objectivity.md) — GPT cross-review'da rubber stamp olma, bagimsiz ve objektif degerlendir
- [feedback_propagate_effects.md](feedback_propagate_effects.md) — GPT review sonrasi diger dokumanlara etki yansitmayi unutma
- [feedback_check_external_assumptions.md](feedback_check_external_assumptions.md) — Implementasyona baslamadan once paid feature/plan tier/API limit varsayimlarini dogrula (T11 dersi)
- [feedback_validation_separate_chat.md](feedback_validation_separate_chat.md) — Dogrulama (validate) her zaman ayri chat'te yapilir, yapim chat'inde baslatilmaz
- [feedback_commit_infra_changes_before_task.md](feedback_commit_infra_changes_before_task.md) — Infra/meta degisiklikleri working tree'de birakma, task basindan once commit+PR akisini proaktif baslat
- [feedback_claude_watches_ci_always.md](feedback_claude_watches_ci_always.md) — Her actigim PR'in CI'sini ben izlerim — task/chore/infra/docs ayrimi yok, "sen mi izleyeceksin" sorusu yasak
- [feedback_clean_worktree_before_work.md](feedback_clean_worktree_before_work.md) — Session basinda dirty working tree'yi gormezden gelme, commit/stash/discard kararini kullanicidan al

## Project
- [project_gpt_review_workflow.md](project_gpt_review_workflow.md) — GPT cross-review sureci ve etki yansitma akisi
- [project_implementation_decisions.md](project_implementation_decisions.md) — Implementation fazi calisma modeli, dogrulama, ortam, CI/CD kararlari

## Reference
- [reference_remote_control.md](reference_remote_control.md) — VS Code Claude Code /remote-control (/rc) komutu — mobil session izleme/kontrol
