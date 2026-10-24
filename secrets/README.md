# `secrets/` — lokal sır dosyaları

Bu dizin **gitignored**'dır. Tek istisna okuduğunuz bu dosyadır (`.gitignore`'da
`!secrets/README.md`). Buraya konan hiçbir değer GitHub'a gitmez.

Savunma katmanları:

| Katman | Nerede | Ne yapar |
|---|---|---|
| 1 | [`.gitignore`](../.gitignore) | `secrets/`, `.env`, `*.maFile` takip edilmez |
| 2 | [`scripts/git-hooks/pre-commit`](../scripts/git-hooks/pre-commit) | `git add -f` ile zorlanan veya yanlış dosyaya yapıştırılan sırrı **commit anında** bloklar |
| 3 | Servis izolasyonu | İmza sırları (tohumlar, cüzdan anahtarları) env değil **Docker secret** dosyasıdır ve her biri yalnız kendi ağının servisine bağlanır — TRON'unkiler yalnız blockchain sidecar'a (T163), Ethereum'unkiler yalnız Ethereum servisine (T150); backend hiçbirini görmez (F4 gate check garantisi). **Ortak sırlar** (JWT, webhook, iç anahtarlar, bildirim kanalı sırları) da Docker secret dosyasıdır ve yalnız backend'e + karşı uçtaki tek servise bağlanır (T171a). **Üçüncü taraf kimlik bilgileri** (DB/Redis/Grafana parolaları, Steam/TronGrid anahtarları, Ethereum RPC adresleri) da dosyadır, yalnız onları kullanan servislere (T171b) |

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
# Kontrol — yalnız ADRESLER yazılır (DEPLOY_RUNBOOK §L.6):
node scripts/wallet-backup/wallet-manifest.mjs addresses secrets
```

`.env`'de değişken satırı kalırsa compose onu artık konteynere geçmez (zarar
vermez), ama sırrın düz metin kopyası diskte durur — silin. Deneme yayını için
aynısını `secrets/trial/` ve `.env.trial` ile yapın.

### Ortak sırlar (T171a — her stack'te)

Backend ile karşı uçtaki servisin **paylaştığı** sırlar da artık env değil
**Docker secret** dosyasıdır (DEFERRED_BACKLOG `SharedSecretsInPlainEnv`,
DEPLOY_RUNBOOK §L.9). Her sır **tek dosyadır** ve compose onu backend'e **ve**
karşı uçtaki servise aynı dosyadan bağlar — iki uç birbirinden kayamaz. Backend
her stack'te olduğu için on bir dosyanın **hepsi her zaman var olmalıdır**;
isteğe bağlı olanlar boş olabilir.

| Dosya | Kim okur | Zorunlu mu |
|---|---|---|
| `secrets/jwt_secret.txt` | backend (JWT imzası) | **Evet**, ≥ 32 karakter |
| `secrets/blockchain_webhook_secret.txt` | backend + TRON servisi (webhook HMAC) | **Evet**, ≥ 32 karakter |
| `secrets/steam_sidecar_internal_key.txt` | backend + Steam servisi (`X-Internal-Key`) | **Evet**, ≥ 32 karakter |
| `secrets/blockchain_sidecar_internal_key.txt` | backend + TRON servisi (`X-Internal-Key`) | **Evet**, ≥ 32 karakter |
| `secrets/ethereum_sidecar_internal_key.txt` | backend + Ethereum servisi | Dosya **evet**; boş = Ethereum kapalı (Ethereum servisi açılmaz). Doluysa ≥ 32 karakter |
| `secrets/ethereum_webhook_secret.txt` | backend + Ethereum servisi | Dosya **evet**; boş = Ethereum ödemeleri bildirilemez (`acceptsPayments=false`). Doluysa ≥ 32 karakter ve TRON'unkinden **farklı** |
| `secrets/resend_api_key.txt` · `secrets/resend_webhook_signing_secret.txt` | backend (e-posta kanalı, T165) | Dosya **evet**; boş = kanal "misconfigured" (kapalı + alarm, DEPLOY_RUNBOOK §P) |
| `secrets/telegram_bot_token.txt` | backend + Grafana render adımı | Dosya **evet**. **Boşsa Grafana açılmaz** (contact point doğrulaması) — geliştirmede herhangi bir yer tutucu yazın |
| `secrets/telegram_webhook_secret_token.txt` | backend (Telegram webhook) | Dosya **evet**; boş = Telegram kanalı "misconfigured" |
| `secrets/grafana_smtp_password.txt` | Grafana (`GF_SMTP_PASSWORD__FILE`) | Dosya **evet**; boş = Grafana e-posta alarmı gönderemez (Telegram gider). SMTP açıkken (deneme yayını) dosya yoksa Docker yerine dizin açar ve Grafana açılmaz (`GRAFANA_SMTP_PASSWORD_UNREADABLE`) |

Dört zorunlu sır ve dolu Ethereum sırları birbirinden **farklı** rastgele
değerlerdir (`openssl rand -hex 32`). Değer dosyada tek satır, tırnaksız durur;
baştaki/sondaki boşluk ve satır sonu okunurken atılır.

**Env yolu kapalı.** Backend `ASPNETCORE_ENVIRONMENT=Development` dışındaki
her ortamda bu sırlardan birini bir ortam değişkeninde ya da komut satırında
görürse **açılmaz** (`SHARED_SECRET_IN_ENV`); sidecar'lar `NODE_ENV` tam
`development`/`test` değilken `INTERNAL_KEY` / `WEBHOOK_SECRET` görürse
açılmaz. Dosya + değişken birlikte: `SHARED_SECRET_AMBIGUOUS`. Eksik ya da boş
zorunlu dosya: `SHARED_SECRET_MISSING` (backend) / `INTERNAL_KEY_MISSING` /
`WEBHOOK_SECRET_MISSING` (sidecar); kısa: `SHARED_SECRET_TOO_SHORT` /
`INTERNAL_KEY_TOO_SHORT` / `WEBHOOK_SECRET_TOO_SHORT`. Yukarıdaki **eksik dosya
tuzağı** burada da geçerlidir: Docker dosyanın yerine dizin açar →
`SHARED_SECRET_UNREADABLE` (Grafana render adımında
`TELEGRAM_BOT_TOKEN_UNREADABLE`) — dizini silin, dosyayı oluşturun.

Açılışta backend hangi sırrın nereden geldiğini yazar, **değeri asla**:
`Shared secrets loaded from /run/secrets (environment path closed): Jwt:Secret=file, …`
(sidecar'larda `internalKeySource` / `webhookSecretSource`).

**Geliştirme ortamı, sıfırdan (bir kez):**

```bash
umask 077
for f in jwt_secret blockchain_webhook_secret steam_sidecar_internal_key \
         blockchain_sidecar_internal_key ethereum_sidecar_internal_key ethereum_webhook_secret; do
  [ -s "secrets/$f.txt" ] || openssl rand -hex 32 > "secrets/$f.txt"
done
for f in resend_api_key resend_webhook_signing_secret telegram_webhook_secret_token grafana_smtp_password; do
  [ -e "secrets/$f.txt" ] || : > "secrets/$f.txt"
done
[ -s secrets/telegram_bot_token.txt ] || echo "dev-placeholder-telegram-token" > secrets/telegram_bot_token.txt
```

(Ethereum'u geliştirmede açmıyorsanız iki Ethereum dosyasını boş bırakabilirsiniz:
`: > secrets/ethereum_sidecar_internal_key.txt` gibi.)

**Mevcut bir geliştirme `.env`'inden geçiş (bir kez):** değerleri ekrana
yazdırmadan dosyaya taşıyın, sonra `.env`'den silin. Eski geri düşüşler
(`BLOCKCHAIN_WEBHOOK_SECRET` boşsa `WEBHOOK_SECRET`, iç anahtar boşsa
`INTERNAL_KEY`) burada bir kez çözülür:

```bash
umask 077
val() { grep "^$1=" .env | tail -1 | cut -d= -f2- | tr -d '"'; }
put() { [ -n "$2" ] && printf '%s' "$2" > "secrets/$1.txt" || : > "secrets/$1.txt"; }
put jwt_secret                      "$(val JWT_SECRET)"
put blockchain_webhook_secret       "$(val BLOCKCHAIN_WEBHOOK_SECRET)"
[ -s secrets/blockchain_webhook_secret.txt ] || put blockchain_webhook_secret "$(val WEBHOOK_SECRET)"
put steam_sidecar_internal_key      "$(val STEAM_SIDECAR_INTERNAL_KEY)"
[ -s secrets/steam_sidecar_internal_key.txt ] || put steam_sidecar_internal_key "$(val INTERNAL_KEY)"
put blockchain_sidecar_internal_key "$(val BLOCKCHAIN_SIDECAR_INTERNAL_KEY)"
[ -s secrets/blockchain_sidecar_internal_key.txt ] || put blockchain_sidecar_internal_key "$(val INTERNAL_KEY)"
put ethereum_sidecar_internal_key   "$(val ETHEREUM_SIDECAR_INTERNAL_KEY)"
put ethereum_webhook_secret         "$(val ETHEREUM_WEBHOOK_SECRET)"
put resend_api_key                  "$(val RESEND_API_KEY)"
put resend_webhook_signing_secret   "$(val RESEND_WEBHOOK_SIGNING_SECRET)"
put telegram_bot_token              "$(val TELEGRAM_BOT_TOKEN)"
put telegram_webhook_secret_token   "$(val TELEGRAM_WEBHOOK_SECRET_TOKEN)"
put grafana_smtp_password           "$(val GRAFANA_SMTP_PASSWORD)"
grep -vE '^(JWT_SECRET|WEBHOOK_SECRET|BLOCKCHAIN_WEBHOOK_SECRET|INTERNAL_KEY|STEAM_SIDECAR_INTERNAL_KEY|BLOCKCHAIN_SIDECAR_INTERNAL_KEY|ETHEREUM_SIDECAR_INTERNAL_KEY|ETHEREUM_WEBHOOK_SECRET|RESEND_API_KEY|RESEND_WEBHOOK_SIGNING_SECRET|TELEGRAM_BOT_TOKEN|TELEGRAM_WEBHOOK_SECRET_TOKEN|GRAFANA_SMTP_PASSWORD|HEARTBEAT_URL)=' .env > .env.new && mv .env.new .env
# Kontrol — yalnız UZUNLUKLAR yazılır, değer değil:
for f in secrets/*.txt; do printf '%s %s\n' "$(tr -d '\r\n' < "$f" | wc -c)" "$f"; done
```

Kontrolde dört zorunlu dosya 32'den kısaysa (eski `.env`'de kısa bir değer
vardıysa — eski `.env.example` yer tutucusu `your_internal_api_key_here` 26
karakterdir) servisler açılmaz: yerine `openssl rand -hex 32` yazın. Deneme
yayını için aynısını `secrets/trial/` ve `.env.trial` ile yapın; orada ayrıca
`HEARTBEAT_URL` → `secrets/trial/heartbeat_url.txt`.

### Üçüncü taraf kimlik bilgileri (T171b — her stack'te)

Veritabanı ve Redis parolası, Grafana yönetici parolası, Steam / TronGrid API
anahtarları ve Ethereum RPC adresleri de **Docker secret** dosyasıdır
(DEFERRED_BACKLOG `ThirdPartyCredentialsInPlainEnv`, proje sahibi kararı
2026-10-24; DEPLOY_RUNBOOK §L.9 "Üçüncü taraf kimlik bilgileri"). Her dosya
yalnız onu kullanan servislere bağlanır; `docker inspect`, komut satırı ve
bağlantı dizeleri parola taşımaz (`DB_CONNECTION_STRING`,
`REDIS_CONNECTION_STRING`, `STEAM_SIDECAR_REDIS_URL` artık **parolasız**).

| Dosya | Kim okur | Zorunlu mu |
|---|---|---|
| `secrets/mssql_sa_password.txt` | SQL Server (`MSSQL_SA_PASSWORD_FILE`, önünde `scripts/db/mssql-entrypoint.sh`), yedek konteyneri, backend | **Evet**, boş olamaz. Tek satır; sonda bir LF ya da CRLF olabilir, içinde CR ya da baş/son boşluk olamaz. SQL Server parola kuralı: büyük/küçük harf, rakam, sembolden üçü ve 8–128 karakter — `openssl rand -hex` **tek başına uymaz** (yalnız küçük harf + rakam); giriş betiği böyle bir dosyada SQL Server'ı başlatmaz (`CREDENTIAL_WEAK`). Üretim / deneme: `printf '%sAa1!\n' "$(openssl rand -hex 24 \| tr -d '\r')" > secrets/mssql_sa_password.txt` |
| `secrets/redis_password.txt` | Redis (`scripts/redis/redis-entrypoint.sh`), backend, Steam servisi | **Evet**, boş olamaz; yalnız `A-Z a-z 0-9 . _ ~ + = -` (bağlantı dizesine ve `redis://` adresine kaçışsız girer) |
| `secrets/grafana_admin_password.txt` | Grafana (`GF_SECURITY_ADMIN_PASSWORD__FILE`) | **Evet**, boş olamaz ve **CR içeremez** (Grafana CR'yi parolanın parçası sayar — ölçüldü). Boş volume'ün **ilk** açılışında uygulanır |
| `secrets/steam_api_key.txt` | backend (OpenID profili) + Steam servisi | Dosya **evet**; boş = trade-hold probu 503, profil yer tutucu |
| `secrets/tron_api_key.txt` · `secrets/tron_api_key_secondary.txt` | TRON servisi | Dosya **evet**; boş = anahtarsız TronGrid (429) / yedek anahtar yok |
| `secrets/ethereum_rpc_primary_url.txt` · `secrets/ethereum_rpc_backup_url.txt` | Ethereum servisi | Yalnız `--profile ethereum` ile; birincil zorunlu (`RPC_NOT_CONFIGURED`), yedek boş olabilir |
| `secrets/discord_client_secret.txt` · `secrets/discord_bot_token.txt` | backend — **compose bağlamaz** | Discord kanalı üretimde kapalı. Açılırsa backend'in `secrets:` listesine eklenir (DEPLOY_RUNBOOK §L.9); değişken olarak verilirse backend açılmaz |

**Kurallar ve hata kodları (her yerde aynı):** değişken → `CREDENTIAL_IN_ENV`
(backend `Development` dışında; sidecar'lar `NODE_ENV` tam
`development`/`test` değilken; SQL Server, Redis ve Grafana betikleri her
zaman), eksik/boş zorunlu dosya → `CREDENTIAL_MISSING`, dosyanın yerinde dizin
ya da okunamayan bağlama (Docker'ın eksik kaynak tuzağı) →
`CREDENTIAL_UNREADABLE`, izinsiz karakter / satır sonu → `CREDENTIAL_INVALID`,
dosya + değişken → `CREDENTIAL_AMBIGUOUS`. Değer hiçbir mesaja yazılmaz.

**Windows notu (ölçüldü 2026-10-24):** Git Bash'in `openssl`'i dosyayı
**CRLF** ile yazar. Backend, sidecar'lar, SQL Server ve Redis betiği sondaki
CRLF'i atar; Grafana atmaz. Bu yüzden aşağıdaki komutlar `tr -d '\r'` ile
yazar.

**Geliştirme ortamı, sıfırdan (bir kez):**

```bash
umask 077
gen() { openssl rand -hex "$1" | tr -d '\r'; }
[ -s secrets/mssql_sa_password.txt ] || printf '%sAa1!\n' "$(gen 16)" > secrets/mssql_sa_password.txt
[ -s secrets/redis_password.txt ] || gen 32 > secrets/redis_password.txt
[ -s secrets/grafana_admin_password.txt ] || gen 24 > secrets/grafana_admin_password.txt
for f in steam_api_key tron_api_key tron_api_key_secondary ethereum_rpc_primary_url ethereum_rpc_backup_url; do
  [ -e "secrets/$f.txt" ] || : > "secrets/$f.txt"     # gerçek değerleri printf '%s\n' '<değer>' > ile yazın
done
```

**Mevcut bir geliştirme `.env`'inden geçiş (bir kez).** Veritabanı ve Grafana
volume'leri eski parolayı saklar — dosyaya `.env`'deki **aynı** değer yazılır
(yenisi değil). Değerler ekrana yazdırılmaz; sonra `.env` temizlenir ve
bağlantı dizelerinden parola çıkarılır:

```bash
umask 077
val() { grep "^$1=" .env | tail -1 | cut -d= -f2- | tr -d '"\r'; }
put() { if [ -n "$2" ]; then printf '%s\n' "$2" > "secrets/$1.txt"; else : > "secrets/$1.txt"; fi; }
put mssql_sa_password      "$(val MSSQL_SA_PASSWORD)"
put redis_password         "$(val REDIS_PASSWORD)"
put grafana_admin_password "$(val GRAFANA_ADMIN_PASSWORD)"   # boşsa eski kurulum 'admin' ile açılmıştı — aşağıya bakın
put steam_api_key          "$(val STEAM_API_KEY)"
put tron_api_key           "$(val TRON_API_KEY)"
put tron_api_key_secondary "$(val TRON_API_KEY_SECONDARY)"
put ethereum_rpc_primary_url "$(val ETHEREUM_RPC_PRIMARY_URL)"
put ethereum_rpc_backup_url  "$(val ETHEREUM_RPC_BACKUP_URL)"
grep -vE '^(MSSQL_SA_PASSWORD|REDIS_PASSWORD|GRAFANA_ADMIN_PASSWORD|STEAM_API_KEY|TRON_API_KEY|TRON_API_KEY_SECONDARY|ETHEREUM_RPC_PRIMARY_URL|ETHEREUM_RPC_BACKUP_URL)=' .env > .env.new && mv .env.new .env
sed -i -E 's/;[[:space:]]*(Password|Pwd)=[^;]*//I; s/,password=[^,]*//I; s#redis://:[^@]*@#redis://#' .env
# Kontrol — yalnız UZUNLUKLAR, değer değil; ikinci komut BOŞ dönmeli:
for f in mssql_sa_password redis_password grafana_admin_password; do printf '%s %s\n' "$(tr -d '\r\n' < secrets/$f.txt | wc -c)" "$f"; done
grep -nE '[Pp](assword|wd)=|redis://:' .env
```

- **Redis parolası** karakter kuralına uymuyorsa (eski `.env.example`
  yer tutucusu `YourRedisPassword123` uyar) Redis açılmaz
  (`CREDENTIAL_INVALID`): yeni değer yazın — Redis verisi parolayla
  şifrelenmediği için değiştirmek güvenlidir.
- **Grafana** eski kurulumda `GRAFANA_ADMIN_PASSWORD` boşken `admin` ile
  açıldıysa dosyaya yeni bir parola yazın (`gen 24 > …`) ve Grafana açıldıktan
  sonra bir kez:
  `docker compose -f docker-compose.yml exec skinora-grafana sh -c 'grafana cli admin reset-admin-password "$(cat /run/secrets/grafana_admin_password)"'`.

Deneme yayını için aynısını `secrets/trial/` ve `.env.trial` ile yapın. Deneme yayınında ve üretimde parolalar **yeni** üretilir (DEPLOY_RUNBOOK §L.9); sa parolası için `openssl rand -hex` tek başına SQL Server kuralına uymaz (`CREDENTIAL_WEAK`):

```bash
umask 077
[ -s secrets/trial/mssql_sa_password.txt ] || printf '%sAa1!\n' "$(openssl rand -hex 24 | tr -d '\r')" > secrets/trial/mssql_sa_password.txt
```

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
| `secrets/trial/` altında on bir ortak sır dosyası (T171a — `jwt_secret.txt`, `blockchain_webhook_secret.txt`, `ethereum_webhook_secret.txt`, `steam_sidecar_internal_key.txt`, `blockchain_sidecar_internal_key.txt`, `ethereum_sidecar_internal_key.txt`, `resend_api_key.txt`, `resend_webhook_signing_secret.txt`, `telegram_bot_token.txt`, `telegram_webhook_secret_token.txt`, `grafana_smtp_password.txt`) | Yukarıdaki "Ortak sırlar" dosyalarının deneme yayını kopyası — **hepsi yeni** değer (geliştirmenin JWT anahtarıyla imzalanmış bir token deneme yayınında geçmesin) | **Her zaman**; kurallar yukarıdaki gibi. Deneme yayını Production'dır: dört zorunlu dosya eksik/boş/kısaysa backend açılmaz |
| `secrets/trial/` altında üçüncü taraf kimlik bilgileri (T171b — `mssql_sa_password.txt`, `redis_password.txt`, `grafana_admin_password.txt`, `steam_api_key.txt`, `tron_api_key.txt`, `tron_api_key_secondary.txt`, `ethereum_rpc_primary_url.txt`, `ethereum_rpc_backup_url.txt`) | Yukarıdaki "Üçüncü taraf kimlik bilgileri"nin deneme yayını kopyası — üç parola **yeni** değer | **Her zaman** (RPC adresleri yalnız `--profile ethereum` ile); kurallar yukarıdaki gibi. Grafana yönetici parolası boşsa Grafana açılmaz |
| `secrets/trial/heartbeat_url.txt` (T171a) | Healthchecks.io ping URL'si (`https://hc-ping.com/<uuid>`) — dış izleme (DEPLOY_RUNBOOK §P.5) | **Her zaman**; boş = heartbeat konteyneri boşta bekler (`HEARTBEAT_DISABLED`). Yerinde dizin varsa çıkar (`HEARTBEAT_URL_UNREADABLE`) |

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

**TRON, Ethereum, ortak sırlar, üçüncü taraf kimlik bilgileri ve deneme yayını dışında beklenen bir dosya yoktur (T133, T161, T163, T171a, T171b).** Tek beklenen dosya
`secrets/steam-bots.json` idi: Steam escrow bot'unun hesap adı, parolası ve iki
Mobile Authenticator sırrı. Platform artık hiçbir Steam hesabı çalıştırmıyor ve
trade offer göndermiyor (05 §3.2, 02 §2.1) — Steam sidecar'ın tek kimlik bilgisi
Steam Web API anahtarıdır ve T171b'den beri o da bu dizindedir
(`secrets/steam_api_key.txt`, yukarıda).

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

## `.env` (repo kökü) — artık sır taşımaz (T171b)

T171b'den beri `.env`'de **sır kalmadı**: yalnız adresler, portlar, ayarlar ve
parolasız bağlantı dizeleri (`.env` gitignored; şablonu
[`.env.example`](../.env.example)). Taşınan eski değişkenler:

| Anahtar | Kim kullanır | Not |
|---|---|---|
| ~~`HD_WALLET_MNEMONIC`~~ · ~~`HOT_WALLET_PRIVATE_KEY`~~ | — | **T163'ten beri `.env`'de değil:** `secrets/tron_hd_mnemonic.txt` / `secrets/tron_hot_wallet_private_key.txt` (yukarıda). T143 notu geçerli: 2026-10-18'den önce açılmış Nile depozitleri koddan erişilemez (DEPLOY_RUNBOOK §G.0b). Sıcak cüzdan tohumdan **türemez** |
| ~~`STEAM_API_KEY`~~ | — | **T171b:** `secrets/steam_api_key.txt` (steam sidecar + backend) |
| ~~`TRON_API_KEY`~~ / ~~`_SECONDARY`~~ | — | **T171b:** `secrets/tron_api_key.txt`, `secrets/tron_api_key_secondary.txt` |
| ~~`JWT_SECRET`~~ · ~~`WEBHOOK_SECRET`~~ · ~~`BLOCKCHAIN_WEBHOOK_SECRET`~~ · ~~`INTERNAL_KEY`~~ · ~~`STEAM_/BLOCKCHAIN_/ETHEREUM_SIDECAR_INTERNAL_KEY`~~ · ~~`ETHEREUM_WEBHOOK_SECRET`~~ · ~~`RESEND_API_KEY`~~ · ~~`RESEND_WEBHOOK_SIGNING_SECRET`~~ · ~~`TELEGRAM_BOT_TOKEN`~~ · ~~`TELEGRAM_WEBHOOK_SECRET_TOKEN`~~ · ~~`GRAFANA_SMTP_PASSWORD`~~ · ~~`HEARTBEAT_URL`~~ | — | **T171a'dan beri `.env`'de değil:** yukarıdaki "Ortak sırlar" dosyaları. compose bu değişkenleri hiçbir konteynere geçmez; Development dışında servisler onları görürse açılmaz |
| ~~`MSSQL_SA_PASSWORD`~~ · ~~`REDIS_PASSWORD`~~ · ~~`GRAFANA_ADMIN_PASSWORD`~~ | — | **T171b:** `secrets/mssql_sa_password.txt`, `secrets/redis_password.txt`, `secrets/grafana_admin_password.txt`. `DB_CONNECTION_STRING` / `REDIS_CONNECTION_STRING` / `STEAM_SIDECAR_REDIS_URL` **parolasız** kalır; parolalı biçimleri Development dışında servisleri durdurur |
| ~~`ETHEREUM_RPC_PRIMARY_URL`~~ / ~~`ETHEREUM_RPC_BACKUP_URL`~~ | — | **T171b:** `secrets/ethereum_rpc_primary_url.txt`, `secrets/ethereum_rpc_backup_url.txt` (URL'ler sağlayıcının API anahtarını taşır — 08 §3.7.1) |

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
