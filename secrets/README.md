# `secrets/` — lokal sır dosyaları

Bu dizin **gitignored**'dır. Tek istisna okuduğunuz bu dosyadır (`.gitignore`'da
`!secrets/README.md`). Buraya konan hiçbir değer GitHub'a gitmez.

Savunma katmanları:

| Katman | Nerede | Ne yapar |
|---|---|---|
| 1 | [`.gitignore`](../.gitignore) | `secrets/`, `.env`, `*.maFile` takip edilmez |
| 2 | [`scripts/git-hooks/pre-commit`](../scripts/git-hooks/pre-commit) | `git add -f` ile zorlanan veya yanlış dosyaya yapıştırılan sırrı **commit anında** bloklar |
| 3 | Servis izolasyonu | `docker-compose.yml` her servise **yalnız kendi** env'ini geçer — özellikle `HOT_WALLET_PRIVATE_KEY` sadece blockchain sidecar'a gider, backend hiç görmez (F4 gate check garantisi) |

Hook'ları kurmak için (yeni clone sonrası bir kez):

```bash
bash scripts/git-hooks/install.sh
```

---

## Beklenen dosyalar

**Bu dizinde beklenen bir dosya yoktur (T133).** Tek beklenen dosya
`secrets/steam-bots.json` idi: Steam escrow bot'unun hesap adı, parolası ve iki
Mobile Authenticator sırrı. Platform artık hiçbir Steam hesabı çalıştırmıyor ve
trade offer göndermiyor (05 §3.2, 02 §2.1) — Steam sidecar'ın tek kimlik bilgisi
`.env`'deki `STEAM_API_KEY`'dir ve o env olarak taşınır, dosya olarak değil.

Dizin ve savunma katmanları **kaldırılmadı**: `.gitignore` + `pre-commit` kuralı
buraya yanlışlıkla yapıştırılan herhangi bir sırrı bloklamaya devam eder, ve
ileride dosya olarak taşınması gereken bir sır çıkarsa yeri burasıdır.

> **Bu dosyayı daha önce doldurduysanız:** lokal `secrets/steam-bots.json` T133'te
> silindi. Repo'ya hiç girmedi (gitignored) — ama içindeki Steam hesabı parolası
> diskte açık metin durduğu için o hesabın parolasını **döndürün**.
>
> **Rotasyon isteğe bağlı değildir (T133 doğrulama turu bulgusu).** Aynı parola
> `scripts/git-hooks/pre-commit`'in bir yorumunda **açık metin** duruyordu ve o
> dosya **takip ediliyor** — yani parola yalnız lokal diskte değil, **git
> geçmişinde** de var. Literal bu turda maskelendi, fakat maskeleme geçmişi
> temizlemez: sızan anahtar yakılmış sayılır (aşağıdaki "Sır sızdıysa ne
> yapmalı" md.1). Hesap emekli olsa bile parola başka yerde tekrar
> kullanılmışsa risk sürer.

---

## `.env` (repo kökü) — dosya değil env olarak taşınan sırlar

`secrets/` dizininde durmayan ama yine de gizli olan her şey `.env`'dedir
(`.env` gitignored; şablonu [`.env.example`](../.env.example)). Öne çıkanlar:

| Anahtar | Kim kullanır | Not |
|---|---|---|
| `HD_WALLET_MNEMONIC` | blockchain sidecar | Deposit adresi türetme (`m/44'/195'/0'/0/{index}`) |
| `HOT_WALLET_PRIVATE_KEY` | blockchain sidecar | Payout/refund/sweep imzası + Energy delegation. **Backend'e geçilmez.** |
| `STEAM_API_KEY` | steam sidecar + backend | Envanter okuma + trade-hold probu + OpenID profil |
| `TRON_API_KEY` / `_SECONDARY` | blockchain sidecar | TronGrid rate limit + failover |
| `JWT_SECRET`, `*_INTERNAL_KEY` | backend + sidecar'lar | ≥32 karakter rastgele |
| `WEBHOOK_SECRET` | backend + **blockchain** sidecar | ≥32 karakter rastgele. Steam sidecar webhook göndermez (T133) |

---

## Sır üretme

```bash
# 32+ karakter rastgele secret (JWT / webhook / internal key)
openssl rand -hex 32
```

Tron testnet cüzdanı (mnemonic + hot wallet) için TronLink veya `tronweb`
kullanılabilir; **mainnet anahtarlarını bu dizine koymayın** — lokal prova
Nile testnet üzerinde yapılır.

---

## Sır sızdıysa ne yapmalı

1. Anahtarı **hemen döndürün** (Steam API key'i iptal edip yenileyin, Tron
   cüzdanını boşaltıp yenisini üretin). Git geçmişini temizlemek ikinci adımdır —
   sızan anahtar artık yakılmış sayılır.
2. Sonra geçmişten temizleyin (`git filter-repo` veya BFG) ve force-push edin.
3. `Docs/BYPASS_LOG.md`'de ilgili `[secret-guard]` kaydı varsa nedenini not edin.
