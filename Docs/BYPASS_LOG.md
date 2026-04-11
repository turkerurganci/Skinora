# Skinora — Direct Push Bypass Log

T11 discipline-only branch protection rejiminde `SKINORA_ALLOW_DIRECT_PUSH=1` ile yapılan tüm direct push'lar burada kayıt altına alınır. Pre-push hook her bypass'ta bu dosyaya otomatik satır ekler.

**Kural:** Bypass kullanıldığında hook bu dosyaya yazar. Kullanıcı bypass commit'inden sonraki **ilk normal commit'te** bu dosyadaki değişikliği commit'lemelidir.

---

## Log

| Tarih | Kullanıcı | Branch | Commit | Sebep |
|---|---|---|---|---|
| *(T11 close-out, 2026-04-08)* | *turkerurganci* | *main* | *0327315, e44e3d2, 8d7c3b1, 7255c33* | *T11 close-out: hook kurulmadan önceki direct push'lar — tek seferlik istisna* |
| *(retro, 2026-04-09 20:36 UTC)* | *turkerurganci* | *main* | *`0a50389`* | *T14 Steam Sidecar: direct push (PR atlandi). T11.1 retro-kayit: T11 discipline ihlali, CI zaten `@parcel/watcher` nedeniyle kirildi — bypass usage goze carpmadi* |
| *(retro, 2026-04-09 20:55 UTC)* | *turkerurganci* | *main* | *`6314591`* | *T15 Blockchain Sidecar: direct push (PR atlandi). T11.1 retro-kayit* |
| *(retro, 2026-04-09 21:52 UTC)* | *turkerurganci* | *main* | *`e8ddd38`* | *T16 Monitoring altyapisi: direct push (PR atlandi). T11.1 retro-kayit. Bu donemde T11 discipline-only rejim unutulmus; CI zaten yesil olmadigi icin "CI PASS zorunlu" kural fiilen dusmustu* |
| *(process-violation note, 2026-04-11)* | *turkerurganci* | *task/T20* | *T17, T18, T19 (T20 PR #11 icine gomuldu)* | *Sira bozumu: T17/T18/T19 ayri PR + validator chat olmadan T20 branch'ine dogrudan commit edildi, T20 squash merge (`be0cc24`) ile tek PR olarak geldi. INSTRUCTIONS.md §3.1 "her task ayri bir chat'te yapilir" ihlali — ama bu bir **direct-push bypass** degil, **task isolation** ihlali. T11.1 retro-kayit.* |
| 2026-04-10 16:56 UTC | Türker urgancı | main | `529f4a2` | T15+T16 push eksigi + F0 gate check PR#10 ile birlikte esitleme |
| 2026-04-10 17:03 UTC | Türker urgancı | main | `9b56767` | chore: bypass log + CONTEXT.md guncelleme (F0 gate check devami) |
| 2026-04-11 16:23 UTC | Türker urgancı | main | `4fa6494` | T20 post-merge 1-satir cosmetic drift fix (pending squash -> be0cc24); ayri PR 1 satir icin overkill, T11.1 debt zaten acik |
