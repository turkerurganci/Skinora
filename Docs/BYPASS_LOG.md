# Skinora — Direct Push & Process Violation Log

T11 discipline-only branch protection rejiminde `SKINORA_ALLOW_DIRECT_PUSH=1` ile yapılan tüm direct push'lar burada kayıt altına alınır. Pre-push hook (T11.2 ile genişletildi) her bypass'ta bu dosyaya otomatik satır ekler.

**Kural:** Bypass kullanıldığında hook bu dosyaya yazar. Kullanıcı bypass commit'inden sonraki **ilk normal commit'te** bu dosyadaki değişikliği commit'lemelidir.

**[kind] önekleri (T11.2):**
- `[direct-push]` — `main`/`develop`'a direct push bypass (`SKINORA_ALLOW_DIRECT_PUSH=1`, pre-push Layer 1)
- `[ci-failure]` — push edilen branch'in son CI run'ı failure iken bypass (`SKINORA_ALLOW_DIRECT_PUSH=1`, pre-push Layer 2)
- `[bundled-pr]` — task branch isolation bypass: `task/TXX-*` branch'inde kendi TXX'i dışında commit. İlk kullanımı retro-kayıt (T15/T16/T17-T19). T11.2 follow-up ile mekanik tespit: commit-msg hook + pre-push Layer 3 (`SKINORA_ALLOW_BUNDLED=1`). Ayrıca task PR'ı açılmadan başka bir PR'a gömme (task-chat bitiş kapısı ihlali) da bu tag'i kullanır.

**T11.2 düzeltme (2026-04-12):** T11.1 sırasında hatalı olarak "retro direct-push" kaydedilen T14 satırı kaldırıldı — T14 aslında PR #8 ile düzgün merge olmuş (merge commit `0a503891`, `gh pr view 8` ile doğrulandı). T15+T16 satırları birleştirilip `[bundled-pr]` pattern'iyle yeniden sınıflandırıldı.

**T11.3 hot-fix closing note (2026-04-19):** PR #34 (`5f6a8cb`) integration test step'ine `-m:1` + `xunit.runner.json parallelizeTestCollections=false/parallelizeAssembly=false` ekleyerek TestContainers OOM'unu (T26 validator zinciri) çözdü. Bu bir bypass değil hot-fix'tir; BYPASS_LOG'da satırı yoktur — ancak T11.3 (`task/T11.3-shared-mssql-fixture`) job-level shared SQL Server (CI services:mssql) + unique DB per test class pattern'i ile bu hot-fix'i kalıcı çözüme bağladı. Hot-fix varsayılanları (serial execution) geri alındı; `AppDbContext.RegisterModuleAssembly` paralel test runner'ın ortaya çıkardığı race condition için thread-safe hale getirildi.

**T28 validator retro note (2026-04-20):** `a4b9578` + `8cb3d9e` iki ardışık direct-push, commit mesajlarında `[skip-guard]` tag'i eksikti → main CI `0. Guard (direct push)` job'ları FAIL verdi (a4b9578 run'ı concurrency group nedeniyle cancelled, 8cb3d9e run `24688063473` FAIL). Validator finalize akışı için **öğrenilen ders:**
1. Pre-push hook (`SKINORA_ALLOW_DIRECT_PUSH=1`) push'u geçirse bile **CI guard job**'u ayrı bir katman — commit mesajı PR referansı (`(#NN)`) veya `[skip-guard]` içermezse job fail verir. İki disiplin mekanizması birlikte düşünülmeli.
2. Direct-push bypass döngüsü (bypass push → hook otomatik BYPASS_LOG satırı ekler → working tree dirty → yeni bypass commit lazım → loop) **cycle yaratır**. Çözüm: Post-merge cosmetic status drift için direct-push yerine **chore PR** tercih edilmeli (T28 validator closure PR #43 bu pattern'in ilk uygulaması).
3. T20 `4fa6494` + T26 `a1bf832` pattern'i "1 satır için PR overkill" gerekçesiyle kullanılmış, ancak o commit'ler de guard job'ını FAIL'lamış olmalı — retro-kontrol yapılmadı. Bundan sonra post-squash status drift **yalnızca chore PR ile** kapatılır, direct-push pattern'i validator finalize için **deprecated**.

## Log

| Tarih | Kullanıcı | Branch | Commit | Sebep |
|---|---|---|---|---|
| *(T11 close-out, 2026-04-08)* | *turkerurganci* | *main* | *0327315, e44e3d2, 8d7c3b1, 7255c33* | *T11 close-out: hook kurulmadan önceki direct push'lar — tek seferlik istisna* |
| *(process-violation note, 2026-04-09)* | *turkerurganci* | *task/T15, task/T16* | *T15 (`6314591`), T16 (`e8ddd38`)* | **[bundled-pr]** *T15 + T16 kendi PR'larini acmadi; kodlari F0 Gate Check PR #10 (`529f4a2`) icine bundled geldi. INSTRUCTIONS.md §3.1 "her task ayri chat + ayri PR" ihlali. T11.2 retro-duzeltme: bu bir **direct-push bypass** degil, **bundled-PR / task-chat bitis kapisi atlanmasi** ihlali.* |
| *(process-violation note, 2026-04-11)* | *turkerurganci* | *task/T20* | *T17, T18, T19 (T20 PR #11 icine gomuldu)* | **[bundled-pr]** *Sira bozumu: T17/T18/T19 ayri PR + validator chat olmadan T20 branch'ine dogrudan commit edildi, T20 squash merge (`be0cc24`) ile tek PR olarak geldi. INSTRUCTIONS.md §3.1 "her task ayri bir chat'te yapilir" ihlali — direct-push bypass degil, task isolation ihlali. T11.1 retro-kayit.* |
| 2026-04-10 16:56 UTC | Türker urgancı | main | `529f4a2` | **[direct-push]** T15+T16 push eksigi + F0 gate check PR#10 ile birlikte esitleme |
| 2026-04-10 17:03 UTC | Türker urgancı | main | `9b56767` | **[direct-push]** chore: bypass log + CONTEXT.md guncelleme (F0 gate check devami) |
| 2026-04-11 16:23 UTC | Türker urgancı | main | `4fa6494` | **[direct-push]** T20 post-merge 1-satir cosmetic drift fix (pending squash -> be0cc24); ayri PR 1 satir icin overkill, T11.1 debt zaten acik |
| 2026-04-19 08:21 UTC | Türker urgancı | task/T26-seed-data | `70bb576` | [ci-failure] T26 CI lint fix — dotnet format whitespace normalization (son CI run lint failure'ını düzeltiyor) |
| 2026-04-19 08:47 UTC | Türker urgancı | task/T26-seed-data | `4b63ba6` | [ci-failure] T26 CI failure follow-up — HangfireBypassFactory scrub for SettingsBootstrapHook |
| 2026-04-19 09:12 UTC | Türker urgancı | task/T26-seed-data | `67bb972` | [ci-failure] T26 CI failure follow-up — SQLite RowVersion seed value |
| 2026-04-19 09:37 UTC | Türker urgancı | task/T26-seed-data | `1a71dec` | [ci-failure] T26 CI failure follow-up — provider-conditional RowVersion mapping |
| 2026-04-19 11:00 UTC | Türker urgancı | main | `a1bf832` | [direct-push] T26 validator finalize — rapor+status+memory update, T18 pattern: validate PASS sonrası doğrudan main'e commit |
| 2026-04-19 16:14 UTC | Türker urgancı | chore/ci-docs-only-skip | `a07404f` | [ci-failure] PR #35 paths-filter fix: pull-requests:read permission + changes job added to CI Gate needs (kendi broken push'umu duzeltiyor) |
| 2026-04-20 19:37 UTC | Türker urgancı | task/T28-initial-migration | `6d536d6` | [ci-failure] T28 fix for known pending-model-changes CI failure in prior run 24686011102 |
| 2026-04-20 20:13 UTC | Türker urgancı | main | `a4b9578` | [direct-push] T28 validator finalize — post-squash hash reference update (3f6ba9a), T20/T26 pattern: cosmetic 1-line status drift fix |
| 2026-04-20 20:14 UTC | Türker urgancı | main | `8cb3d9e` | [direct-push] T28 validator finalize — BYPASS_LOG commit (hook auto-add kapanisi) |
| 2026-04-21 18:41 UTC | Türker urgancı | task/T29-steam-openid-auth | `f99d565` | [ci-failure] T29 S1 fix push sonrasi CI run 24739083342 FAIL (test infra flakiness — EF model cache); commit f99d565 TestAssemblyModuleInitializer ile root cause fix'leniyor, Layer 2 bypass |
| 2026-04-21 20:27 UTC | Türker urgancı | task/T30-tos-age-geoblock | `7fbb043` | [ci-failure] T30 CI fix — prev run failed on SeedDataTests (28→30 row count) + Auth integration unique key collision; this commit is the fix |
| 2026-04-21 21:14 UTC | Türker urgancı | main | `3463279` | [direct-push] T30 squash merge subject `(#49)` PR ref'i içermiyordu (`gh pr merge --squash --subject "T30: ..."` CLI default'u PR numarasını subject'e eklemedi) → main CI run 24746790247 `0. Guard (direct push)` + `CI Gate` FAIL. Kod job'ları (Lint/Build/Unit/Integration/Contract/Migration/Docker) hepsi ✓ — yalnız disiplin katmanı FAIL. Cosmetic, kod iş değeri etkilenmedi. T28 validator retro note'undaki pattern'in aynısı; ders: `gh pr merge --squash --subject "TXX: ... (#NN)"` formatı kullanılmalı (validate.md Adım 17 follow-up) |
| 2026-04-23 10:35 UTC | Türker urgancı | task/T32-refresh-token | `b65862d` | [ci-failure] T32 lint fix push — prior run 24830392733 failed on whitespace lint, this commit (b65862d) IS the fix (dotnet format applied). Guard blocks its own remedy without bypass. |
| 2026-05-02 07:45 UTC | Türker urgancı | task/T44-transaction-state-machine | `e8ae6e5` | [ci-failure] T44 unit-test job FAIL fix: EnumTests AllEnums count 23->24 + TransactionTrigger guard tests; root cause çözüldü |
