# Skinora Project Memory

## Project Overview
- **Name:** Skinora — CS2 item escrow platform
- **Type:** Implementation phase (product discovery complete)
- **Language:** Turkish docs, English code

## Current Status (2026-10-21)
> **Not:** Bu özet stale olabilir. "Sırada ne var?" sorularına cevap vermeden önce **her zaman** [`Docs/IMPLEMENTATION_STATUS.md`](../../Docs/IMPLEMENTATION_STATUS.md) oku — kaynak orası, burası snapshot.
- **T164 ⏳ (2026-10-22, PR #372, dal `task/T164-veritabani-yedegi`, `95110e5c`; yapım bitti, doğrulama bekliyor)** — Express'te SQL Agent yok → compose servisi `skinora-db-backup` (`scripts/db-backup/db-backup.sh`): tam 24 sa + log 15 dk (FULL), 14 gün + en yeni 2 tam, `RESTORE VERIFYONLY`, host klasörü `backups/` (gitignored; trial `backups/trial/db`), yaşa göre takvim (yeniden başlatmada yetişir); `restore` yeni ada + point-in-time, `swap` canlıyı `Skinora_before_restore_*` olarak saklar. Trial `MSSQL_PID=Express` zorlanır. Backend `DatabaseStorageMonitorService` (`sys.database_files` + `msdb`) → 4 Grafana kuralı (%70/%85, tam 26 sa, log 45 dk). Prova Express'te: 32 tablo checksum eşit, `down -v` sonrası host yedeğinden geri, PIT doğru, alarmlar firing. DEPLOY_RUNBOOK §M (geri dönüş + geri yükleme sonrası para mutabakatı). Backlog 🟡 ⚠ PARA `PostRestoreChainReconciliationTooling` (TRON kapatma aracı + HD indeks tabanı yok).
- **T167 ✓ (2026-10-21, PR #368, dal `task/T167-masak-akisi`, doğrulama PASS 5/5, 6 × S4; S4-1/2/3/5/6 kapanışta kapandı — "takas", günlük/aylık TOPLAM sınır, iade beyanı, 07 §10.3 P3 —, S4-4 T168'e ⚪ `MasakDeclarationEntityDetails`)** — MASAK notları alıcı ödeme bloğu + kabul formu + satıcı görünümleri (ITEM_DELIVERED, COMPLETED, profil) + destek `#masak`; P3 `GET /platform/payout-sources` (ağ başına sabit sıcak cüzdan, herkese açık — ⚠ GÜVENLİK); payout tek adres ve depozit işlem başına kodda ölçüldü (değişiklik yok). Resmî Gazete farkları: 48 sa / ilk çekimde 72 sa (yurt içi/dışı ayrımı yok), 3.000 $ yalnız stablecoin çekimi + aylık 50.000 $, beyan kuralı Yönetmelik 24/A-6 (Tebliğ 29 değil), 20 karakter açıklama, eşik 7/10/2026'dan beri 30.000 TL. 7518/SPK tanım+izin maddeleri sahibe bilgi; T169'a 3.000 $ / en yüksek tutar girdisi.
- **T161 ✓ (2026-10-21, PR #367, dal `task/T161-sunucu-sikilastirma`, doğrulama PASS 7/7, 6 × S4; kapanışta `--env-file` koruması eklendi)** — sahip kararı: deneme yayını bu PC'de, Docker Desktop + Cloudflare Tunnel, yalnız Nile+Sepolia, modemde port yok. `docker-compose.yml` host portları `127.0.0.1` (edge `NGINX_BIND_ADDRESS`), dev override portsuz; yeni `docker-compose.trial.yml` (`skinora-trial`, `.env.trial`, `secrets/trial/`, iç port yok, Production zorunlu, cloudflared `tunnel` profili token'sız açılmaz); Steam+TRON servisi iç anahtarsız/kısa anahtarla açılmaz; CI `scripts/compose-exposure.test.mjs`; runbook §K. ⚠ `!reset` YAML anchor'ı üzerinden düşüyor (Compose v5.1.0). T162'ye: gerçek istemci IP (CF-Connecting-IP), https şeması. Kapanış: deneme overlay'i `.env.trial`'daki `SKINORA_TRIAL_STACK` işareti olmadan render etmez (`--env-file` unutulup geliştirme `.env`'iyle sessiz açılış engellendi). RAM: tam deneme yığını boşta ≈1,56 GiB (3 GB'a sığar); yığın açıkken soğuk imaj derlemesi swap'ı doldurdu → T172'de önce derle sonra aç. Host notu: bu PC'de IIS `0.0.0.0:80` (Skinora dışı).
- **T166 ✓ (2026-10-21, PR #366, dal `task/T166-kucuk-duzeltmeler`, doğrulama PASS 6/6, 4 × S4; B2 kapanışta düzeltildi: compose alt ağı 172.20.0.0/16 sabit; B1→T161, B3→T162, B4→backlog 🟡 hedef T168)** — altı küçük düzeltme tek PR'da: `_diag` yalnız Development'ta kayıtlı (diğer ortamda 404) · ToS + 18+ sunucuda zorunlu (global `TosAcceptanceGateFilter`, 403 `TOS_NOT_ACCEPTED`, oturum yüzeyi hariç; sürüm zorunlu değil ⚠; FE global kapı ilk kabulü de gösterir) · ülke başlığı yalnız güvenilir proxy zincirinden (backend doğrudan TCP eşi + nginx `$skinora_trusted_edge`, ağ geçidi hariç; canlı ölçüldü; tünel arkası istemci IP'si T162'ye) · sabit %99,9 uptime kalktı (4 dil + API alanı) · USDC ürün yüzeyinden kalktı (yanlış token iadesi + admin araçları kalır) · yaş eşiği repo değeri zaten 30, yeni açılış teşhisi 30 altını uyarır. Rapor `T166_REPORT.md`.
- **T160b ✓ (2026-10-21, PR #364, doğrulama PASS 4/4, 6 × S4; düzeltme turu B1–B4 kapandı)** — ⚠ PARA iade gas tahmini geçici hatada (`retryable`, 5xx/408/429, bağlantı, istemci zaman aşımı) 3 çağrı / 1 sn + 3 sn / 12 sn bütçe; yedek = **min(aynı ağ+gönderen+token son kesilen tahmin ≤ 6 sa, ağın sabit ayarı)**, ölçüm yoksa ayar (B1 yönetici kararı: ölçüm alıcının adres durumunu bilmez, T159b Sepolia 0,87/0,18 ≈4,8×; alıcı T160b öncesinden fazla ödemez); her yedek `gas-fee-fallback-charged` alarmı, seriler açılışta `GasFeeFallbackSeriesPrimer` ile (B2); T159b `block not found` ortak örnekle (`sidecar-ethereum/contracts/estimate-fee/error.block-not-found.json`) iki tarafta test; payout değişmedi; backlog `RefundGasEstimateSingleAttempt` ✅ (51 aktif / 158 çözülmüş). Kalan: seed açıklamaları eski sırayı anlatıyor (migration), Ethereum revert de `retryable: true` (B5), ölçüm bellekte.
- **T160a ✓ (2026-10-21, PR #363, doğrulama PASS 4/4, 5 × S4)** — F9'un ilk işi: `next` + `eslint-config-next` 16.2.3 → 16.3.8 (16.4.x 6 günlük; danışmalar ≤16.3.8'de kapalı), `npm audit fix` (force değil) transitive high'ları temizledi → `npm audit --omit=dev` 0; CI lint job'una `Frontend npm audit (blocking — prod highs)`; `src/middleware.ts` → `src/proxy.ts`; backlog `FrontendNextAdvisories` 🔴 → ✅. Kalan: dev-only `braces` high (eslint-config-next altında, npm'de düzeltme yok) → ⚪ `FrontendDevToolingBracesAdvisory`.
- **F8 Gate Check ✓ PASS (2026-10-21, T160, PR #361, ölçüm `c1026b8c`; squash hash + tag `phase/F8-pass` sahibin ⚠ listesine bakmasından sonra yöneticide)** — backend 4157/4157 (unit 2218 + integration 1843 + contract 96, CI ile proje bazında birebir) · frontend vitest 391 · sidecar steam 138 / blockchain 585 / ethereum 868 / fake 120 · E2E 11/11 · 60 migration temiz + idempotent, seed 75 · §7.6 36/36 boşluk 0 · FK8-1…6 ✓ (FK8-3 14/14 üçüncü bağımsız uygulamayla) · 0 bloke edici, 7 bloke etmeyen (F8-N1…N7). **F8 dışı 🔴 F8-N4:** frontend `next@16.2.3` 3 critical npm danışması → F9 canlı öncesi. Sahip onayı bekleyen: PARA 23 · GÜVENLİK 7 (rapor `Docs/CHECKPOINT_REPORTS/GATE_CHECK_F8.md`).
- **T160c ✓** (2026-10-21, dal `task/T160c-iade-kesinti-kalintisi`, PR #365; ⚠ PARA; ilk doğrulama ✗ FAIL `c5dab67c` F1, yeniden doğrulama PASS `995070a9` 3/3, 6 × S4) — iadede alıcıdan kesilip depozitte kalan ağ ücreti sıcak cüzdana: satışın `SWEEP`'i onaylanmış depozit iadelerinin kesintisini katlıyor (`RefundFeeRecovered`), kalanı yeni `REFUND_FEE_SWEEP` (`RefundFeeSweepQueueJob`, 10 dk; yalnız canlı tahmin < tutar; Ethereum USDC sweep'i açıldı); AD38 `refundFees` defteri; migration `T160c_RefundFeeSweep`; backlog `RefundGasDeductionDustStaysOnDeposit` ✅. **Düzeltme turu (doğrulama ✗ FAIL F1–F7):** AD38 gideri artık platformun gerçek zincir harcaması (`PlatformNetworkCost` — TRON: depozite gönderilen TRX + ücretleri, gönderim cevabında `platformSpendSun`; Ethereum: yakıt değeri + yakıt ücreti, kesin durumda `platformSpendWei`), USDT'si onay anı fiyatıyla (`PlatformNetworkCostUsdt`, `nativePriceUsdt`), `netUsdt` + `costUnmeasured`; migration `T160c_PlatformNetworkCost`; T159b sayılarıyla eski ekran gerçek giderin %40,8'i — entegrasyon testi pinliyor. Zararına toplama yok = sahip kararı 2026-10-21. Sweep `CONFIRMED` şartı testle korunuyor (M8 öldü), USDC imza bayrağı testli (S3 öldü). main `2e502e99`, `e7e8acf1` (T166), `d81d91f1` (T161) ve `daf0e301` (T167) dala merge edildi (dört merge commit). **Kapanış:** S4-1 TRON `platformSpendSun` sözleşme örneği (`sidecar-blockchain/contracts/transfer-{sweep,refund}`; handler + backend contract testi), S4-2 net USDT toplama gideri testi, S4-4 USDT değerlemesinde kolon taşma koruması (NULL + log), S4-6 belge CRLF geri; S4-3 backlog ⚪ `RefundFeeSpendOnFailedAttemptsNotBooked`; backlog 53/159.
- **Next:** F9 (Canlıya Çıkış, T161–T175) — açılışta yönetici düzeni / doğrulama akışı yeniden sorulur (INSTRUCTIONS §3.3); önce sahip ⚠ listesine bakar, sonra tag.
- **F8 (Çok Ağlı Ödeme, T141–T160) ilerleyişi:** T141–T159 ✓ (T157 ✓ main `e0e9a9d8`, CI ✓) · T159a ✓ (2026-10-21, PR #356, doğrulama PASS, 5/5 kriter, bozma 14/14; reddedilen ödeme iadesinde gas iki ağda alıcıdan; 4 bulgu S4 — ⚠ PARA kesinti tozu depozitte kalıyor, F8 gate'te sahibe sorulacak, `DEFERRED_BACKLOG.md`) · T159b ✓ (2026-10-21, PR #360, doğrulama PASS, 27/27 hash zincirden okundu, 5 × S4 — Sepolia giden bacağı elle müdahalesiz: 7/7 yakıt status 1, sweep + iade + yanlış token + reddedilen ödeme kesintisi zincirde ✓, TRON regresyonu ✓; fee cap 1,00 → 0,50 geri alındı; B1 ⚠ PARA küçük: iade gas tahmini tek denemelik, hatada 0,50 sabit yedek — hedef F9 canlı öncesi, `DEFERRED_BACKLOG.md`; çoklu ödeme iadesi Ethereum'da koşulmadı, T160'a not) · T160 ✓ (F8 Gate Check PASS, yukarıda). Tek doğru kaynak: `Docs/IMPLEMENTATION_STATUS.md`.
- **T157 ✓** (2026-10-20, PR #354, kapanış: main birleştirmesi `d31bdc4c` + 4 test + runbook §D 7.4; merge yöneticide) — S22 cüzdan ekranı + ⚠ PARA iki aşamalı giden satır kapatma aracı (verify MATCH; elle yolda verify MATCH + AuditLog, N özdeş satırda toplam sayı+tutar). Ayrıntı `MEMORY_ARCHIVE.md`.
- **⚠️ Hâlâ geri alınacak:** yerel prova DB'sinde `auth.min_steam_account_age_days` = 1 (repo/üretim değeri 30 — T166 doğruladı ve açılış uyarısı ekledi; prova DB'si sahip kararıyla elle geri alınacak).
- **Ayrıntılı task changelog** (T23–T97 + gate-check / validator / tasarım / GPT-review) ve **F8 görev başına eski durum satırları (T141–T159, 2026-10-20'de taşındı)** → [`MEMORY_ARCHIVE.md`](MEMORY_ARCHIVE.md); tek bir görev için arşivi `grep`'le.

## Key Files
- Takip: `Docs/IMPLEMENTATION_STATUS.md` (tek doğru kaynak) · `Docs/PRODUCT_DISCOVERY_STATUS.md` · görev raporları `Docs/TASK_REPORTS/` · metodoloji `Docs/00_PROJECT_METHODOLOGY.md`
- Talimatlar: `.claude/INSTRUCTIONS.md`, `.claude/GUARDRAILS.md`; skill'ler `.claude/skills/{task,validate,gate-check}.md`
- Raporlar: `Docs/AUDIT_REPORTS/`, `Docs/GPT_REVIEW_REPORTS/`, `Docs/CHECKPOINT_REPORTS/`; GPT görüş kayıtları (ayrı akış, onayla gider, karar sahibinindir) `Docs/GPT_OPINIONS/`

## User
- [user_profile.md](user_profile.md) — teknik profil: .NET/SQL Server, frontend yok

## Feedback
- [feedback_think_through_fully.md](feedback_think_through_fully.md) — yapısal değişiklikte tüm sonuçları baştan düşün
- [feedback_question_destructive_proposals.md](feedback_question_destructive_proposals.md) — silme/kaldırma önermeden önce sorgula
- [feedback_validate_placement.md](feedback_validate_placement.md) — "X'e ekle" denince yerin doğruluğunu sorgula
- [feedback_dont_flip_recommendations.md](feedback_dont_flip_recommendations.md) — öneriyi çabuk değiştirme, gerekçeni savun
- [feedback_gpt_review_objectivity.md](feedback_gpt_review_objectivity.md) — GPT review'da rubber stamp olma
- [feedback_propagate_effects.md](feedback_propagate_effects.md) — review sonrası etkiyi diğer belgelere yansıt
- [feedback_check_external_assumptions.md](feedback_check_external_assumptions.md) — dış varsayımları (plan, limit, imleç yönü) önce doğrula
- [feedback_validation_separate_chat.md](feedback_validation_separate_chat.md) — doğrulama: SubAgentIndependentValidation akışı; F8 istisnası
- [feedback_commit_infra_changes_before_task.md](feedback_commit_infra_changes_before_task.md) — infra değişikliğini task'tan önce commit et
- [feedback_claude_watches_ci_always.md](feedback_claude_watches_ci_always.md) — açtığım PR'ın CI'sini ben izlerim
- [feedback_clean_worktree_before_work.md](feedback_clean_worktree_before_work.md) — session başında kirli ağaç: kullanıcıya sor
- [feedback_verify_status_before_quoting.md](feedback_verify_status_before_quoting.md) — durum sorusunda IMPLEMENTATION_STATUS oku; en çok 3 satır
- [feedback_no_edit_permission_asks.md](feedback_no_edit_permission_asks.md) — onaydan sonra edit/commit/PR için tekrar sorma
- [feedback_respond_in_turkish.md](feedback_respond_in_turkish.md) — yanıtlar Türkçe; kod İngilizce
- [feedback_differential_before_causal_claim.md](feedback_differential_before_causal_claim.md) — nedene bağlamadan önce ayırt edici ölçüm yap
- [feedback_plain_language.md](feedback_plain_language.md) — sade yaz: kısa cümle, az tablo, tek konu
- [feedback_ask_questions_via_modal.md](feedback_ask_questions_via_modal.md) — kararları AskUserQuestion modalıyla sor
- [feedback_be_concise.md](feedback_be_concise.md) — kısa yaz; detay rapora/PR'a
- [feedback_verify_metric_definition.md](feedback_verify_metric_definition.md) — devralınan sayıyı üreten komutu oku
- [feedback_verify_probe_subject.md](feedback_verify_probe_subject.md) — probe'a kimin cevap verdiğini doğrula
- [feedback_run_full_unit_suite_for_enum_changes.md](feedback_run_full_unit_suite_for_enum_changes.md) — enum eklerken tüm Unit suite'i koş
- [feedback_branch_from_main_after_squash.md](feedback_branch_from_main_after_squash.md) — squash sonrası yeni dalı origin/main'den kes
- [feedback_refetch_branch_before_verdict.md](feedback_refetch_branch_before_verdict.md) — verdict öncesi git fetch'i tekrarla
- [feedback_merge_teyit_not_direct_pushable.md](feedback_merge_teyit_not_direct_pushable.md) — merge teyit run ID'leri main'e doğrudan push edilmez
- [feedback_single_session_worktree.md](feedback_single_session_worktree.md) — worktree'de paralel session yok
- [feedback_measure_the_failure_shape.md](feedback_measure_the_failure_shape.md) — dış probun başarısızlık biçimini de ölç
- [feedback_measure_effective_on_mainnet.md](feedback_measure_effective_on_mainnet.md) — etkin sonucu ana ağ makbuzlarından ölç
- [feedback_added_latency_check_caller_timeout.md](feedback_added_latency_check_caller_timeout.md) — bekleme eklemeden çağıranın zaman aşımına bak
- [feedback_scripted_doc_edits_verify_against_head.md](feedback_scripted_doc_edits_verify_against_head.md) — betikli doküman düzenlemesini HEAD'e karşı diff'le
- [feedback_vary_fixture_against_fallback.md](feedback_vary_fixture_against_fallback.md) — "testle pinli" demeden fixture'ı sabitten ayrıştır
- [feedback_resume_after_limit.md](feedback_resume_after_limit.md) — limit dolunca işe kendiliğinden devam et
- [feedback_red_ci_guard_no_requeue.md](feedback_red_ci_guard_no_requeue.md) — kırmızı CI guard'ında yeniden kuyruğa alma
- [feedback_new_chat_prompt.md](feedback_new_chat_prompt.md) — yeni chat önerince hazır metni ver

## Project
- [project_phase_history.md](project_phase_history.md) — faz geçmişi, gate sonuçları, tur özetleri
- [project_gpt_review_workflow.md](project_gpt_review_workflow.md) — GPT cross-review süreci ve etki yansıtma
- [project_implementation_decisions.md](project_implementation_decisions.md) — implementasyon fazı çalışma modeli, ortam, CI/CD
- [project_validator_merge_flow.md](project_validator_merge_flow.md) — merge akışı (F8: yönetici); eski akış tarihçede
- [project_module_test_initializer.md](project_module_test_initializer.md) — modül testinde TestAssemblyModuleInitializer ekle
- [project_ci_advisory_vs_setup_failure.md](project_ci_advisory_vs_setup_failure.md) — CI failure ama bloke işler yeşil: Set up job'a bak
- [e2e-prettier-crlf-local-artifact.md](e2e-prettier-crlf-local-artifact.md) — yerel e2e prettier CRLF uyarısı bulgu değil
- [wp5-validation-pass.md](wp5-validation-pass.md) — WP5 admin dispute doğrulama PASS (2026-06-17)
- [project_playbook_template.md](project_playbook_template.md) — Skinora metodu → public template, v1.1.0
- [project_f8_yonetici_duzeni.md](project_f8_yonetici_duzeni.md) — F8 yönetici düzeni; 2026-10-21'de F9 için aynen uzatıldı (sahip: "F8 gibi çalışacağız")
- [project_f8_devir_notu.md](project_f8_devir_notu.md) — F8 BİTTİ (2026-10-21, phase/F8-pass): F9'a devreden T160a–c, devir/maliyet dosyalarının yeri
- [feedback_model_by_task_weight.md](feedback_model_by_task_weight.md) — alt ajan: model+effort birlikte (haiku/sonnet/opus tablosu)
- [project_backlog_round_2026_10_02.md](project_backlog_round_2026_10_02.md) — 2026-10-02 dört backlog satırı, #331
- [project_wallet_policy_round.md](project_wallet_policy_round.md) — cüzdan politikası turu (2026-09), #324/#325

## Reference
- [reference_remote_control.md](reference_remote_control.md) — VS Code /remote-control (/rc)
- [reference_local_stack_runbook_g.md](reference_local_stack_runbook_g.md) — yerel Docker: runbook §G, bayat upstream tuzağı
- [reference_local_payment_leg_nile.md](reference_local_payment_leg_nile.md) — lokal ödeme bacağı Nile kurulumu
- [reference_workflow_subagents_blocked.md](reference_workflow_subagents_blocked.md) — subagent durumu değişir; önce ucuz probe
- [reference_gh_secondary_rate_limit.md](reference_gh_secondary_rate_limit.md) — paralel ajanda gh run watch → 403; yavaş yokla
