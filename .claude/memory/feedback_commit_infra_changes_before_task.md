---
name: Infra degisikliklerini task basindan once commit et
description: Meta/infrastructure degisiklikleri working tree'de birakma — sonraki task basiyorsa zorla commit+PR akisini baslat
type: feedback
---
Hook/skill/INSTRUCTIONS/CLAUDE.md/config gibi meta degisiklikleri tamamladiktan sonra, kullanicinin "ne zaman commit edelim?" sorusunu beklemeden **proaktif olarak commit+PR akisini tetikle** — ozellikle bir task'a (T21 vb.) yaklasiliyorsa.

**Why:** Bu kararin gerekcesi bir ironidir — branch isolation hook'larini (commit-msg + pre-push Layer 3) ekledik, sonra kullanici "commit istemezsem ne olur?" diye sordu. Working tree'de kalan hook kodlari, `task/TXX-*` dali acildiginda kullaniciyla birlikte gelir; dikkatsiz bir `git add` onlari task PR'ina bundle eder — yani hook'larin kendisi kendi engelledikleri ihlalin ilk vakasi olur. Kullanici aciklama uzerine "bundan sonra boyle bir sey olursa beni commit icin zorla" dedi (2026-04-12).

**How to apply:**
- Herhangi bir infra/meta degisikligi bittikten sonra (hook, skill, INSTRUCTIONS, CLAUDE.md, config, .gitattributes, scripts/, git hooks), kullanici daha sormadan commit+PR akisini oner.
- Cercevele: "T21'in temiz baslamasi icin bu once commit'lensin. Onay verirsen chore dali acip PR aciyorum." — seyir degil, aktif tavsiye.
- Ozellikle `main` uzerinde dirty working tree ile bir sonraki task'a gecis riskliyse bu zorunluluk. Kullanici "daha sonra" derse `git stash`'i asgari tavsiye et; degisikliklerin task dalina tasinmasina izin verme.
- Kategori: TXX prefix'i kullanma. `chore(...):`, `hooks:`, `docs:`, `infra:` gibi prefix'ler uygun. Commit-msg hook zaten bu prefix'leri yabanci TXX saymaz ama task PR'ina da ait olamazlar — ayri PR.
- Bypass reflex'ine direnc: kullanici zaman baskisi hissettirirse bile, "bir commit daha ne olur" **yasak**. Bir once hazirlik commit'i disipline yatirimin en ucuz yoludur.
