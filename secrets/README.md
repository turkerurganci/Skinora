# `secrets/` — lokal sır dosyaları

Bu dizin **gitignored**'dır. Tek istisna okuduğunuz bu dosyadır (`.gitignore`'da
`!secrets/README.md`). Buraya konan hiçbir değer GitHub'a gitmez.

Savunma katmanları:

| Katman | Nerede | Ne yapar |
|---|---|---|
| 1 | [`.gitignore`](../.gitignore) | `secrets/`, `.env`, `*.maFile` takip edilmez |
| 2 | [`scripts/git-hooks/pre-commit`](../scripts/git-hooks/pre-commit) | `git add -f` ile zorlanan veya yanlış dosyaya yapıştırılan sırrı **commit anında** bloklar |
| 3 | Servis izolasyonu | İmza sırları (tohumlar, cüzdan anahtarları) env değil **Docker secret** dosyasıdır ve her biri yalnız kendi ağının servisine bağlanır — TRON'unkiler yalnız blockchain sidecar'a (T163), Ethereum'unkiler yalnız Ethereum servisine (T150); backend hiçbirini görmez (F4 gate check garantisi) |

Hook'ları kurmak için (yeni clone sonrası bir kez):

```bash
bash scripts/git-hooks/install.sh
```

---

## Beklenen dosyalar

### TRON servisi (T163 — her stack'te)

TRON servisi (`skinora-blockchain-sidecar`) depozit tohumunu ve sıcak cüzdan
anahtarını **Docker secret** olarak alır (DEFERRED_BACKLOG
`SigningSecretsInPlainEnv`, proje sahibi kararı 2026-09-17). Compose iki dosyayı
yalnız bu servise, `/run/secrets/<ad>` yoluna bağlar. Servis her stack'te
olduğu için dosyalar **her zaman** gerekir — boş olabilirler.

| Dosya | İçerik | Zorunlu mu |
|---|---|---|
| `secrets/tron_hd_mnemonic.txt` | TRON depozit tohumu — BIP-39 ifadesi (tırnaksız, tek satır), BIP-39 parolası boş, türetme yolu `m/44'/195'/0'/0/{index}` BIP-32 kök düğümünden (08 §3.2). **Ethereum tohumundan farklı** (K7) | Dosya **evet**, içerik hayır: boşsa servis açılır ama `/api/wallet/derive` 503 döner (yeni işlem açılamaz). BIP-39 sağlaması tutmayan ifade servisi durdurur (`HD wallet initialization failed`) |
| `secrets/tron_hot_wallet_private_key.txt` | TRON sıcak cüzdanının özel anahtarı (64 hex, `0x` isteğe bağlı) — payout, iade, sweep imzası | Dosya **evet**, içerik hayır: boşsa payout/iade/sweep reddedilir. Doluysa `.env`'deki `HOT_WALLET_ADDRESS`'i kontrol etmeli, yoksa servis açılmaz (`HOT_WALLET_ADDRESS_MISMATCH`); 64 hex değilse açılmaz (`INVALID_SIGNING_KEY`) |

**Env yolu kapalı.** `HD_WALLET_MNEMONIC` ve `HOT_WALLET_PRIVATE_KEY` artık
`.env`'de **durmaz** ve compose onları geçmez. Servis `NODE_ENV` tam olarak
`development` ya da `test` değilken bu iki değişkenden biri doluysa **açılmaz**
(`SIGNING_SECRET_IN_ENV`) — yalnız konteyner dışında `npm run dev` ile koşan
geliştirme süreci ve testler env'den okuyabilir. Dosya ve değişken birlikte
verilirse servis yine açılmaz (`SIGNING_SECRET_AMBIGUOUS`).

**Eksik dosya tuzağı (ölçüldü 2026-10-21, Docker Desktop + Compose v5.1.0):**
compose kaynak dosyası olmayan bir secret'ı hata vermeden başlatır ve Docker o
yolda — hem konteynerde hem **host'ta**, ör. `secrets/tron_hd_mnemonic.txt/` —
**boş bir dizin** oluşturur. Servis bunu okuyamaz ve açılmaz
(`SIGNING_SECRET_UNREADABLE`; Ethereum servisinde de aynı). Düzeltme: o dizini
silin (`rmdir`), dosyayı oluşturun, servisi yeniden başlatın. Geri yükleme
betiği boş dizini kendisi kaldırır (DEPLOY_RUNBOOK §L).

**Mevcut bir geliştirme `.env`'inden geçiş (bir kez):** değerleri ekrana
yazdırmadan dosyaya taşıyın, sonra `.env`'den silin:

```bash
umask 077
grep '^HD_WALLET_MNEMONIC=' .env | cut -d= -f2- | tr -d '"' > secrets/tron_hd_mnemonic.txt
grep '^HOT_WALLET_PRIVATE_KEY=' .env | cut -d= -f2- | tr -d '"' > secrets/tron_hot_wallet_private_key.txt
grep -v -e '^HD_WALLET_MNEMONIC' -e '^HOT_WALLET_PRIVATE_KEY' .env > .env.new && mv .env.new .env
# Kontrol — yalnız ADRESLER yazılır (DEPLOY_RUNBOOK §L.5):
node scripts/wallet-backup/wallet-manifest.mjs addresses secrets
```

`.env`'de değişken satırı kalırsa compose onu artık konteynere geçmez (zarar
vermez), ama sırrın düz metin kopyası diskte durur — silin. Deneme yayını için
aynısını `secrets/trial/` ve `.env.trial` ile yapın.

### Ethereum servisi (T150 — yalnız `ethereum` compose profiliyle)

Ethereum servisi (`skinora-ethereum-sidecar`, 11 §F8 K7) tohumunu ve iki imza
anahtarını **Docker secret** olarak alır — düz env değişkeni olarak değil
(`docker inspect` göstermesin diye). Compose bu üç dosyayı yalnız bu servise,
`/run/secrets/<ad>` yoluna bağlar; TRON servisi, backend ve diğer konteynerler
bunları görmez. Profil kapalıyken (`docker compose -f docker-compose.yml up`)
dosyalar gerekmez.

| Dosya | İçerik | Zorunlu mu |
|---|---|---|
| `secrets/ethereum_hd_mnemonic.txt` | Ethereum depozit tohumu — BIP-39 ifadesi, BIP-39 parolası boş, türetme yolu `m/44'/60'/0'/0/{index}` BIP-32 kök düğümünden (08 §3.7.2). **TRON'un `HD_WALLET_MNEMONIC`'inden farklı bir tohum** (K7); kendi çevrimdışı yedeği vardır (T163) | **Evet** — yoksa ya da boşsa servis açılmaz (`HD_WALLET_NOT_CONFIGURED`) |
| `secrets/ethereum_hot_wallet_private_key.txt` | Ethereum sıcak cüzdanının özel anahtarı (64 hex, `0x` isteğe bağlı) — payout ve soğuk aktarım imzası (T152) | Hayır — T152'ye kadar boş dosya olabilir. Doluysa `ETHEREUM_HOT_WALLET_ADDRESS`'i kontrol etmeli, yoksa servis açılmaz (`HOT_WALLET_ADDRESS_MISMATCH`) |
| `secrets/ethereum_fuel_wallet_private_key.txt` | Yakıt cüzdanının özel anahtarı — depozitlere gas için ETH (T153). **Sıcak cüzdan anahtarından ayrı bir anahtar** (öneriyle kaydedildi — ⚠) | Hayır — T153'e kadar boş dosya olabilir. Sıcak cüzdanla aynı anahtar/adres ya da sıcak/soğuk adresi kontrol eden bir anahtar servisi durdurur (`WALLET_KEYS_NOT_DISTINCT`) |

Üç dosya da gitignored'dır (`secrets/*`) ve `pre-commit` hook'u commit'lenmelerini
bloklar. Linux sunucuda compose secret'ı dosyanın sahibi ve izinleriyle bağlar:
servis süreci dosyayı okuyabilmelidir (okuyamazsa açılmaz ve sebebi yazar).

```bash
# Örnek (testnet tohumu — mainnet tohumu bu makineye girmez):
umask 077
printf '%s\n' "<12 ya da 24 kelime>" > secrets/ethereum_hd_mnemonic.txt
: > secrets/ethereum_hot_wallet_private_key.txt     # T152'ye kadar boş
: > secrets/ethereum_fuel_wallet_private_key.txt    # T153'e kadar boş
```

### Deneme yayını (T161 — `secrets/trial/`, DEPLOY_RUNBOOK §K)

Deneme yayını (`docker-compose.trial.yml`, proje `skinora-trial`) geliştirme
sırlarını **kullanmaz**: overlay bütün secret'ları `secrets/trial/` altından
bağlar. Dizin `secrets/*` kuralıyla gitignored'dır ve `pre-commit` aynı
korumayı sürdürür.

| Dosya | İçerik | Zorunlu mu |
|---|---|---|
| `secrets/trial/tron_hd_mnemonic.txt`, `secrets/trial/tron_hot_wallet_private_key.txt` (T163) | Yukarıdaki iki TRON dosyasının deneme yayını kopyası — **geliştirme stack'inden farklı** tohum ve sıcak cüzdan (aynı gerekçe: aynı depozit adresleri, aynı imzacı) | **Her zaman** (servis her stack'te); boş olabilir, kurallar yukarıdaki gibi |
| `secrets/trial/ethereum_hd_mnemonic.txt`, `secrets/trial/ethereum_hot_wallet_private_key.txt`, `secrets/trial/ethereum_fuel_wallet_private_key.txt` | Yukarıdaki üç Ethereum dosyasının deneme yayını kopyası — **geliştirme stack'inden farklı** tohum ve anahtarlar (iki veritabanı aynı tohumdan aynı indeksleri türetir ve aynı depozit adreslerini izler; iki imzacı aynı Ethereum hesabının nonce'unu yarıştırır) | Yalnız `--profile ethereum` ile, kurallar yukarıdaki gibi |
| `secrets/trial/cloudflare_tunnel_token.txt` | Cloudflare Tunnel token'ı — tek satır, tırnaksız (Cloudflare Zero Trust → Networks → Tunnels) | Yalnız `--profile tunnel` ile. **Boşsa tünel açılmaz:** `skinora-tunnel-preflight` `TUNNEL_TOKEN_MISSING` ile çıkar ve cloudflared başlatılmaz. T162'ye kadar boş dosya olarak durabilir |

```bash
umask 077
mkdir -p secrets/trial
: > secrets/trial/cloudflare_tunnel_token.txt    # T162'de token yazılır
: > secrets/trial/tron_hd_mnemonic.txt           # T163 — deneme yayınının kendi TRON tohumu
: > secrets/trial/tron_hot_wallet_private_key.txt
```

Linux sunucuda cloudflared konteyneri uid `65532` ile çalışır; token dosyası o
kullanıcının okuyabileceği izinde olmalıdır (`chmod 444` dosya, `chmod 700`
dizin) — okuyamazsa tünel açılmaz.

### Başka dosya

**TRON, Ethereum ve deneme yayını dışında beklenen bir dosya yoktur (T133, T161, T163).** Tek beklenen dosya
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
| ~~`HD_WALLET_MNEMONIC`~~ · ~~`HOT_WALLET_PRIVATE_KEY`~~ | — | **T163'ten beri `.env`'de değil:** `secrets/tron_hd_mnemonic.txt` / `secrets/tron_hot_wallet_private_key.txt` (yukarıda). T143 notu geçerli: 2026-10-18'den önce açılmış Nile depozitleri koddan erişilemez (DEPLOY_RUNBOOK §G.0b). Sıcak cüzdan tohumdan **türemez** |
| `STEAM_API_KEY` | steam sidecar + backend | Envanter okuma + trade-hold probu + OpenID profil |
| `TRON_API_KEY` / `_SECONDARY` | blockchain sidecar | TronGrid rate limit + failover |
| `JWT_SECRET`, `*_INTERNAL_KEY` | backend + sidecar'lar | ≥32 karakter rastgele |
| `WEBHOOK_SECRET` | backend + **blockchain** sidecar | ≥32 karakter rastgele. Steam sidecar webhook göndermez (T133) |
| `ETHEREUM_SIDECAR_INTERNAL_KEY` / `ETHEREUM_WEBHOOK_SECRET` | backend + **Ethereum** servisi | ≥32 karakter rastgele, TRON'unkilerden **farklı** (05 §3.3a). İç anahtar boşsa ya da 32 karakterden kısaysa Ethereum servisi açılmaz |
| `ETHEREUM_RPC_PRIMARY_URL` / `ETHEREUM_RPC_BACKUP_URL` | Ethereum servisi | Sağlayıcı URL'leri API anahtarını taşır (Alchemy, Chainstack — 08 §3.7.1); servis log'larına yalnız sunucu adı yazılır |

---

## Sır üretme

```bash
# 32+ karakter rastgele secret (JWT / webhook / internal key)
openssl rand -hex 32
```

Tron testnet cüzdanı (mnemonic + hot wallet) için TronLink veya `tronweb`
kullanılabilir; **mainnet anahtarlarını bu dizine koymayın** — lokal prova
Nile testnet üzerinde yapılır. Mainnet cüzdanları kurulum günü (T171) yeniden
üretilir ve aynı gün çevrimdışı yedeklenir (DEPLOY_RUNBOOK §L).

## Çevrimdışı yedek

Bu dizindeki beş imza dosyasının (iki ağ) şifreli yedeği, geri yüklemesi ve
provası: **DEPLOY_RUNBOOK §L**, betik `scripts/wallet-backup/wallet-backup.sh`.
Diskteki bu dosyalar tek kopya değildir ancak o prosedür uygulandıysa —
sunucu diski kaybolursa depozitlerde bekleyen para ve iadeler yalnız yedekten
geri alınır.

---

## Sır sızdıysa ne yapmalı

1. Anahtarı **hemen döndürün** (Steam API key'i iptal edip yenileyin, Tron
   cüzdanını boşaltıp yenisini üretin). Git geçmişini temizlemek ikinci adımdır —
   sızan anahtar artık yakılmış sayılır.
2. Sonra geçmişten temizleyin (`git filter-repo` veya BFG) ve force-push edin.
3. `Docs/BYPASS_LOG.md`'de ilgili `[secret-guard]` kaydı varsa nedenini not edin.
