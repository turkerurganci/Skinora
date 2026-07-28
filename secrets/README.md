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

### `secrets/steam-bots.json` — Steam escrow bot kimlik bilgileri

Steam sidecar'a `:ro` mount edilir; `STEAM_BOTS_CONFIG_PATH=/run/secrets/steam-bots.json`
ile okunur ([`BotConfig.ts`](../sidecar-steam/src/bot/BotConfig.ts) önce dosya
yoluna bakar, sonra `STEAM_BOTS_JSON`'a düşer). Dosya yoksa sidecar **skeleton
mode**'da açılır: `/health` çalışır ama `selectBot()` null döner ve trade offer
gönderilemez.

Şablon (gerçek değerleri siz doldurun):

```json
{
  "bots": [
    {
      "accountName": "<bot Steam hesap adi>",
      "password": "<bot Steam sifresi>",
      "sharedSecret": "<maFile icindeki shared_secret>",
      "identitySecret": "<maFile icindeki identity_secret>"
    }
  ]
}
```

- `sharedSecret` → `steam-totp` ile 2FA kodu üretir (giriş).
- `identitySecret` → `steamcommunity` mobil onay anahtarı (trade confirmation).
- İkisi de bot hesabının **Mobile Authenticator `maFile`**'ından gelir. `maFile`'ın
  kendisini repo dizinine kopyalamayın — `*.maFile` de gitignored ve hook tarafından
  bloklanır, ama en güvenlisi repo dışında tutmaktır.
- Birden fazla bot desteklenir (`bots` dizisine ekleyin); `BotManager` round-robin seçer.

Her bot için ayrıca veritabanına bir `PlatformSteamBots` satırı gerekir —
bkz. [`scripts/bootstrap/02-register-bot.sql`](../scripts/bootstrap/02-register-bot.sql).

---

## `.env` (repo kökü) — dosya değil env olarak taşınan sırlar

`secrets/` dizininde durmayan ama yine de gizli olan her şey `.env`'dedir
(`.env` gitignored; şablonu [`.env.example`](../.env.example)). Öne çıkanlar:

| Anahtar | Kim kullanır | Not |
|---|---|---|
| `HD_WALLET_MNEMONIC` | blockchain sidecar | Deposit adresi türetme (`m/44'/195'/0'/0/{index}`) |
| `HOT_WALLET_PRIVATE_KEY` | blockchain sidecar | Payout/refund/sweep imzası + Energy delegation. **Backend'e geçilmez.** |
| `STEAM_API_KEY` | steam sidecar + backend | Envanter + OpenID profil |
| `TRON_API_KEY` / `_SECONDARY` | blockchain sidecar | TronGrid rate limit + failover |
| `JWT_SECRET`, `WEBHOOK_SECRET`, `*_INTERNAL_KEY` | backend + sidecar'lar | ≥32 karakter rastgele |

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

1. Anahtarı **hemen döndürün** (Steam API key'i iptal edip yenileyin, bot şifresini
   değiştirin, Tron cüzdanını boşaltıp yenisini üretin). Git geçmişini temizlemek
   ikinci adımdır — sızan anahtar artık yakılmış sayılır.
2. Sonra geçmişten temizleyin (`git filter-repo` veya BFG) ve force-push edin.
3. `Docs/BYPASS_LOG.md`'de ilgili `[secret-guard]` kaydı varsa nedenini not edin.
