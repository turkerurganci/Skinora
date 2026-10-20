---
name: reference_gh_secondary_rate_limit
description: Paralel ajanlar gh run watch kullanınca GitHub ikincil hız sınırı (403) — core kotası dolu görünmese de; yavaş yoklama kullan
metadata:
  type: reference
---

2026-10-18 F8'de 3–5 paralel ajan + yönetici aynı anda `gh run watch` (birkaç saniyede bir istek) çalıştırınca `gh run view` 403 "API rate limit exceeded" döndü; `gh api rate_limit` core 4696/5000 gösteriyordu, yani **ikincil (secondary) sınır**. Bu hata `gh run watch`'ı da kırıp çıkış kodu 1 verdi — "run başarısız" sanılabilir, run hâlâ sürüyordu.

**Nasıl uygulanır:** paralel ajan çokken CI'ı `while … gh api repos/<o>/<r>/actions/runs/<id> --jq .status; sleep 120` gibi 1–2 dk aralıklı yoklamayla izle; `gh run watch` çıkış kodu 1 ise önce run'ın gerçekten `completed/failure` olduğunu `gh api` ile doğrula. Ajan istemlerinde de "gh run watch --interval 60" ya da yavaş yoklama iste. İlgili: [[project_f8_yonetici_duzeni]].

**Ek (aynı gün, worktree):** Agent `isolation: "worktree"` "Refusing to use c:\projects\Escrow\.claude\worktrees\agent-… (core.worktree redirect)" diye reddederse sebep oturumun çalışma dizininin küçük harf `c:` olması. Bozuk worktree'yi `git worktree remove -f -f` + dalını sil, sonra önce `cd /c/projects/Escrow && pwd` çalıştır (dizin `C:` olur), ajanı hemen ardından aç. Paralel iki ajanı aynı mesajda açmak da worktree yarışına yol açabiliyor — ayrı mesajlarda aç.

**Ek (2026-10-19, Docker):** Paralel ajanlardan biri Docker yığınıyla canlı prova yaparken başka bir ajana "bitince `docker desktop stop`" demek provanın Docker motorunu ortasında kapattı (T157 doğrulayıcısı ↔ T159 provası). Paralel Docker kullanan ajan varken diğerlerine "Docker'ı açma/kapatma, yalnız Testcontainers" de; kapatmayı yalnız son biten ajana ya da yöneticiye bırak.
