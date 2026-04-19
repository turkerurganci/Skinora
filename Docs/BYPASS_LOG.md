# Skinora — Direct Push & Process Violation Log

T11 discipline-only branch protection rejiminde `SKINORA_ALLOW_DIRECT_PUSH=1` ile yapılan tüm direct push'lar burada kayıt altına alınır. Pre-push hook (T11.2 ile genişletildi) her bypass'ta bu dosyaya otomatik satır ekler.

**Kural:** Bypass kullanıldığında hook bu dosyaya yazar. Kullanıcı bypass commit'inden sonraki **ilk normal commit'te** bu dosyadaki değişikliği commit'lemelidir.

**[kind] önekleri (T11.2):**
- `[direct-push]` — `main`/`develop`'a direct push bypass (`SKINORA_ALLOW_DIRECT_PUSH=1`, pre-push Layer 1)
- `[ci-failure]` — push edilen branch'in son CI run'ı failure iken bypass (`SKINORA_ALLOW_DIRECT_PUSH=1`, pre-push Layer 2)
- `[bundled-pr]` — task branch isolation bypass: `task/TXX-*` branch'inde kendi TXX'i dışında commit. İlk kullanımı retro-kayıt (T15/T16/T17-T19). T11.2 follow-up ile mekanik tespit: commit-msg hook + pre-push Layer 3 (`SKINORA_ALLOW_BUNDLED=1`). Ayrıca task PR'ı açılmadan başka bir PR'a gömme (task-chat bitiş kapısı ihlali) da bu tag'i kullanır.

**T11.2 düzeltme (2026-04-12):** T11.1 sırasında hatalı olarak "retro direct-push" kaydedilen T14 satırı kaldırıldı — T14 aslında PR #8 ile düzgün merge olmuş (merge commit `0a503891`, `gh pr view 8` ile doğrulandı). T15+T16 satırları birleştirilip `[bundled-pr]` pattern'iyle yeniden sınıflandırıldı.

---

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
