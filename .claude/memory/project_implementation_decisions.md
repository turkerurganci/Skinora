---
name: Implementation Phase Decisions
description: Kodlama fazına geçiş kararları — çalışma modeli, doğrulama, ortam, CI/CD, kalite katmanları
type: project
---

Implementation fazına geçiş kararları (2026-04-05):

| Konu | Karar |
|---|---|
| Çalışma modeli | Her task ayrı bir chat (task-per-chat) |
| Doğrulama döngüsü | Her task için ayrı doğrulama chat'i (yapan ≠ denetleyen) |
| .NET | 9.0 (dokümanlar güncellendi) |
| SQL Server | LocalDB |
| Redis | Docker container olarak |
| Git hosting | GitHub, private repo (turkerurganci/Skinora) |
| CI/CD | GitHub Actions (build + test on push/PR) |
| Task hızı | 114 task olduğu gibi, sırasıyla, birleştirme yok |
| Branching | Feature branch per task, squash merge, phase tags |
| Branch protection | T11 sonrası **discipline-only** rejim — sistem-enforced koruma yok (GitHub Free + private repo'da paid feature). Lokal `scripts/git-hooks/pre-push` hook + manuel `gh pr merge --squash` + validator chat. Bypass: `SKINORA_ALLOW_DIRECT_PUSH=1`. Yükseltme yolu açık (Pro $4/ay). |
| Kalite katmanları | 3 katman: task validation + PR CI gate + phase gate |
| Task durumları | 5 durum: Bekliyor, Devam ediyor, Tamamlandı, FAIL, BLOCKED |
| BLOCKED akışı | 4 alt tür: SPEC_GAP, DEPENDENCY_MISMATCH, PLAN_CORRECTION, EXTERNAL |
| Doğrulama durumları | 4 durum: Karşılandı, Karşılanmadı, Kısmi, Doğrulanamadı |
| Validator izolasyonu | Rapor görmeden başlar, kanıt zorunlu, sapma avcısı persona |
| Raporlama sırası | Rapor finalize → sonra status tablosu güncellenir |
| Mini güvenlik | Her task validation'da 4 madde (secret, auth, input, dependency) |
| Skill'ler | /task, /validate, /gate-check |
| Process baseline | İlk 3-5 task gözlendikten sonra gerekirse revize, şimdi değil |

**Why:** Product discovery tamamlandı, GPT cross-review ile süreç olgunlaştırıldı. Kullanıcı net ve disiplinli ilerleme tercih ediyor.

**How to apply:** Her yeni task chat'inde `/task TXX` ile başla. Doğrulama `/validate TXX` ile. Faz sonu `/gate-check FX` ile. Process baseline'ı 3-5 task boyunca gözlemle.
