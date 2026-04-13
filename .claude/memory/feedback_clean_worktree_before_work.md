---
name: Session basinda dirty working tree'yi coz
description: Her session (task, validate, chore, herhangi) basinda git status dirty ise kullaniciya sor ve cozulmeden baslatma
type: feedback
---
Her session basinda, herhangi bir ise baslamadan once `git status` calistir. Working tree dirty ise (uncommitted staged/unstaged degisiklikler, untracked dosyalar) kullaniciya hemen goster ve aksiyon iste:
- **Commit + PR** (aslina uygun: docs, chore, infra)
- **Stash** (gecici park — task bitince pop)
- **Discard** (kullanici onayi zorunlu, geri donussuz)

**Why:** PR #16 konusmasinda (2026-04-12) fark edildi: GATE_CHECK_F0 retro (80 satir), IMPLEMENTATION_STATUS (2 satir), settings.local.json (13 satir) degisiklikleri session basinda zaten vardi. Hicbiri commit'lenmemisti. 3 PR boyunca (T11.2 squash, hook isolation PR, INSTRUCTIONS guncelleme) working tree'de kimsenin fark etmeden yasadilar. Kullanici "bunlar ne oldu?" diye sordu. Eger task/T21 acilsaydi, `git add` sirasinda bu dosyalar task PR'ina bundled girebilirdi — commit-msg hook bunu yakalamaz cunku dosya degisiklikleri TXX subject pattern'i olusturmaz. Bu rasyonelizasyonlar yasak: "sonra hallederiz", "onemsiz", "benim degil", "task'la ilgisiz".

**How to apply:**
- Bu kural task skill'e (Adim -1) ve validate skill'e (Adim -1) olarak eklendi, ama SADECE task/validate icin gecerli degil.
- **Her Claude session'i basinda** (task, validate, chore konusmasi, debug, herhangi), ilk tool call'dan once veya ilk substantive islemden once `git status` kontrol et.
- Dirty ise kullaniciya commit/stash/discard kararini sor. Karar olmadan ileriye gecme.
- "Temiz" sonucu da TXX_REPORT notlarina yaz (task/validate icin). Diger session turleri icin sozel belirt.
- Ozellikle `main` dalindayken dirty state tehlikelidir — yeni task dali acildiginda tum unstaged degisiklikler working tree'de task'la birlikte gelir.
