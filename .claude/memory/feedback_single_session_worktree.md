---
name: feedback_single_session_worktree
description: Bu worktree tek session ile calisilir - paralel session pratigi 2026-08-17'de birakildi
type: feedback
---

`c:\projects\Escrow` worktree'sinde **paralel session calistirilmayacak** (proje sahibi karari, 2026-08-17). Bu, T137a doneminde uygulanan "P5 ile paralel gorev, izole git worktree (`c:/projects/Escrow-T137a`)" pratiginin **sonu** demektir — repo hafizasindaki T137a girisi o karari kaydeder ama artik gecerli desen degildir.

**Why:** T131 yapimi sirasinda baska bir session T137a'yi (#243) main'e merge etti ve ayni worktree'nin HEAD'ini `main`'e aldi. Sonuclari: (1) task dali worktree'den dustu — commit push edilmis oldugu icin is kaybolmadi, ama kaybolabilirdi; (2) T137a benim de dokundugum uc ortak dokumana (`IMPLEMENTATION_STATUS.md`, `11_IMPLEMENTATION_PLAN.md`, `.claude/memory/MEMORY.md`) dokundugu icin PR `CONFLICTING` dogdu ve **GitHub catismali PR'da CI run'i hic yaratmadi** — [[feedback_branch_from_main_after_squash]] ile ayni imza, farkli sebep.

**Tekrarlandi (2026-08-17 23:44):** karar kayda gectikten ~27 dk sonra ikinci bir session bu worktree'de `fed6689`'u (T131 rapor/status/memory finalize'i) commit'leyip **push etti**; o sirada bu session ayni uc dosyaya ayni icerigi yazmak uzereydi — cift/celisen yazim kil payi onlendi. Proje sahibi bunun uzerine tekrarladi: **her sey tek session'dan devam edecek.**

**How to apply:** Session ortasinda `git branch --show-current` beklenmedik sekilde degistiyse bunu artik "paralel session olsa gerek" diye normallestirme — **anomali** olarak kullaniciya bildir. Ayni sinifin ikinci imzasi (23:44 vakasi): **calisma agaci temiz kaldigi halde HEAD ilerler** ve bir dosyanin ayni tur icindeki iki okumasi farkli icerik dondurur (`tail` placeholder gosterir, dakikalar sonra `Read` dolu gosterir). Bu goruldugunde **yazmayi durdur**, once `git log -1 --format='%h %ad %s' --date=iso` + `git status` ile ne olduguna bak, sonucu kullaniciya bildir ve devam etmeden once planlanan edit'in zaten yapilip yapilmadigini dogrula — ustune yazma. Bir task PR'i `CONFLICTING` ise once "main ilerledi mi" diye bak (`git fetch && git log --oneline origin/main -3`), CI run'inin yoklugunu Actions arizasi sanma. Cakisma her zaman ayni uc ortak dokumanda cikar; cozerken **iki tarafi da koru** (tamamlanan gorevin girisi "Onceki guncelleme"ye iner, dal kendi girisini "Son guncelleme"de tutar).
