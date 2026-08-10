# Skinora — Deploy Runbook

**Oluşturma:** WP14 (2026-06-19) · **Kapsam:** Production deploy öncesi sağlanması zorunlu/önerilen environment değişkenleri + sidecar config parity + runtime-tunable ayar davranışı.

> Bu runbook, "uygulama prod'da açılması için neyin set edilmesi gerekir?" sorusunun tek doğru kaynağıdır. `06_DATA_MODEL §3.17` (SystemSetting kataloğu) ve `08_INTEGRATION_SPEC` (sidecar env) ile tutarlıdır. Değer kaynakları: backend `SystemSettingSeed.cs` (59 satır), `SettingsBootstrapService` (06 §8.9 fail-fast), sidecar `config/index.ts`.

---

## 0. Hızlı özet

| Katman | Zorunluluk | Davranış |
|---|---|---|
| **A. Zorunlu SystemSetting (19)** | **Prod açılışı için ZORUNLU** | Eksikse `SettingsBootstrapService` startup'ta **fail-fast** eder (06 §8.9). Bilinçli güvenlik — iş-kritik değere yanlış default sessizce prod'a kaçmaz. |
| **B. Operasyonel secret/altyapı** | **ZORUNLU** | DB / JWT / wallet / webhook / internal key olmadan servis açılmaz veya kör çalışır. |
| **C. Production'da önerilen** | Önerilir | Seed default ile açılır ama ilgili kapsam (reconciliation, geo-block, PRICE_DEVIATION fiyat kaynağı) çalışmaz. |
| **D. Sidecar parity (cadence/sweep)** | Sidecar env'i otoriter | Backend DB kopyası admin-görünür; **runtime'a yansımaz** — sidecar env değişimi + sidecar restart gerekir. |
| **E. Runtime-tunable** | — | Admin UI'dan değişir; cron'lar **restart'sız** re-register olur (WP14), gas/retry her çalıştırmada taze okunur. |
| **G. Lokal gerçek-konfigürasyon provası** | — | Uçtan uca çalıştırma reçetesi: sır yönetimi, migration, bootstrap SQL'leri, doğrulama listesi, bilinen tuzaklar. |

---

## A. Zorunlu SystemSetting env var'ları (19)

Bu 19 ayar `SystemSettingSeed.cs`'te **Unconfigured** (default'suz) gelir. `SettingsBootstrapService` startup'ta her birini `SKINORA_SETTING_<UPPER_KEY>` env var'ından hydrate eder; **herhangi biri eksik/hatalı ise `InvalidOperationException` ile fail-fast** (test: `SettingsBootstrapTests`). Zaten configured (admin UI'dan girilmiş) bir satır env ile **override edilmez** (06 §8.9 güvenlik klozu).

> **Neden default yok?** Bunlar iş-kritik değerler (işlem limitleri, hot wallet limiti, dormant eşiği). Yanlış bir seed-default sessizce prod'da çalışır; fail-fast bilinçli tercihtir. WP14 owner kararı (2026-06-19): seed-default DEĞİL → runbook.

| # | Env var | SystemSetting key | Tip | Örnek | Anlam |
|---|---|---|---|---|---|
| 1 | `SKINORA_SETTING_ACCEPT_TIMEOUT_MINUTES` | accept_timeout_minutes | int | 60 | Alıcı kabul timeout |
| 2 | `SKINORA_SETTING_TRADE_OFFER_SELLER_TIMEOUT_MINUTES` | trade_offer_seller_timeout_minutes | int | 60 | Satıcı trade offer timeout |
| 3 | `SKINORA_SETTING_PAYMENT_TIMEOUT_MIN_MINUTES` | payment_timeout_min_minutes | int | 15 | Ödeme timeout min |
| 4 | `SKINORA_SETTING_PAYMENT_TIMEOUT_MAX_MINUTES` | payment_timeout_max_minutes | int | 60 | Ödeme timeout max |
| 5 | `SKINORA_SETTING_PAYMENT_TIMEOUT_DEFAULT_MINUTES` | payment_timeout_default_minutes | int | 30 | Ödeme timeout varsayılan (min ≤ x ≤ max) |
| 6 | `SKINORA_SETTING_TRADE_OFFER_BUYER_TIMEOUT_MINUTES` | trade_offer_buyer_timeout_minutes | int | 60 | Alıcı trade offer timeout |
| 7 | `SKINORA_SETTING_MIN_TRANSACTION_AMOUNT` | min_transaction_amount | decimal | 1.0 | Minimum işlem tutarı (USDT) |
| 8 | `SKINORA_SETTING_MAX_TRANSACTION_AMOUNT` | max_transaction_amount | decimal | 10000.0 | Maksimum işlem tutarı (USDT) |
| 9 | `SKINORA_SETTING_MAX_CONCURRENT_TRANSACTIONS` | max_concurrent_transactions | int | 5 | Eşzamanlı aktif işlem limiti |
| 10 | `SKINORA_SETTING_NEW_ACCOUNT_TRANSACTION_LIMIT` | new_account_transaction_limit | int | 3 | Yeni hesap işlem limiti |
| 11 | `SKINORA_SETTING_NEW_ACCOUNT_PERIOD_DAYS` | new_account_period_days | int | 14 | Kaç gün yeni hesap sayılır |
| 12 | `SKINORA_SETTING_CANCEL_LIMIT_COUNT` | cancel_limit_count | int | 3 | Periyotta izin verilen iptal sayısı |
| 13 | `SKINORA_SETTING_CANCEL_LIMIT_PERIOD_HOURS` | cancel_limit_period_hours | int | 24 | İptal limit periyodu (saat) |
| 14 | `SKINORA_SETTING_CANCEL_COOLDOWN_HOURS` | cancel_cooldown_hours | int | 1 | İptal sonrası cooldown (saat) |
| 15 | `SKINORA_SETTING_HIGH_VOLUME_AMOUNT_THRESHOLD` | high_volume_amount_threshold | decimal | 5000.0 | Yüksek hacim tutar eşiği |
| 16 | `SKINORA_SETTING_HIGH_VOLUME_COUNT_THRESHOLD` | high_volume_count_threshold | int | 10 | Yüksek hacim işlem sayısı eşiği |
| 17 | `SKINORA_SETTING_HIGH_VOLUME_PERIOD_HOURS` | high_volume_period_hours | int | 24 | Yüksek hacim kontrol periyodu (saat) |
| 18 | `SKINORA_SETTING_HOT_WALLET_LIMIT` | hot_wallet_limit | decimal | 100000.0 | Hot wallet max bakiye limiti (aşılırsa admin alert) |
| 19 | `SKINORA_SETTING_DORMANT_ACCOUNT_VALUE_THRESHOLD` | dormant_account_value_threshold | decimal | 1000.0 | Dormant hesap tek-işlem tutar eşiği (USDT) |

> Örnek değerler `SettingsBootstrapTests.AllRequiredEnvVars()` ile birebir; gerçek prod değerleri risk profiline göre **bilinçli** belirlenir. `payment_timeout_*` cross-key invariant: `min < max` ve `min ≤ default ≤ max` (`SystemSettingsValidator`). Tarihsel olarak 21 sayılıyordu; WP4a (`price_deviation_threshold`) ve WP12 (`timeout_warning_ratio`) seed-default verince **19**'a indi.

---

## B. Operasyonel secret / altyapı env'leri (ZORUNLU)

SystemSetting değil; servisin açılması ve dış entegrasyonlar için zorunlu. `.env.example` referans alınır.

| Env var | Servis | Anlam |
|---|---|---|
| `DB_CONNECTION_STRING` / `ConnectionStrings__DefaultConnection` | backend | SQL Server connection string (Hangfire de aynı bağlantıyı kullanır) |
| `REDIS_CONNECTION_STRING` | backend | Redis (distributed lock, cache) |
| `JWT_SECRET` (≥32 char) | backend | Access/refresh token imzası |
| `JWT_ISSUER` / `JWT_AUDIENCE` | backend | Token issuer/audience |
| `WEBHOOK_SECRET` (≥32 char) | backend + sidecar'lar | Sidecar→backend HMAC-SHA256 webhook imzası (05 §3.4) |
| `INTERNAL_KEY` | backend + sidecar'lar | Backend↔sidecar internal API `X-Internal-Key` auth |
| `STEAM_API_KEY` | steam sidecar + backend | Steam Web API (envanter) + OpenID profil adı/avatar (`SteamOpenId__WebApiKey`; boşsa login çalışır ama profil placeholder'a düşer) |
| `STEAM_OPENID_REALM` / `_RETURN_TO` / `_REVERIFY_RETURN_TO` / `_FRONTEND_CALLBACK` | backend | Steam OpenID 2.0 (08 §2.1). **`appsettings.json` default'u `https://skinora.com`** — override edilmezse gerçek login ölü bir domaine yönlenir |
| `PUBLIC_ORIGIN` | backend | Tarayıcıya bakan tek origin → `Cors__AllowedOrigins__0` |
| `STEAM_BOTS_CONFIG_PATH` (+ `secrets/steam-bots.json` mount) | steam sidecar | Escrow bot kimlik bilgileri (08 §2.5). **Yoksa sidecar skeleton mode'da açılır ve trade offer gönderemez.** Alternatif: `STEAM_BOTS_JSON` inline |
| `STEAM_SIDECAR_REDIS_URL` | steam sidecar | Envanter cache (08 §2.3); boşsa in-memory fallback |
| `STEAM_SIDECAR_COMMUNITY_REQUESTS_PER_MINUTE` | steam sidecar | Steam Community envanter ucunun kuyruk tavanı, istek/dakika (08 §2.6, T120). Web API kuyruğundan **ayrı**. Boş/geçersiz → **10/dk** (tahmini 10-20/dk/IP aralığının muhafazakâr ucu; aşım IP bloğuyla cezalandırılır). Her teslimat doğrulaması **iki** okuma harcadığı için (satıcı + alıcı) bu değer aynı zamanda eşzamanlı doğrulama tavanıdır (10 §4). Yalnız proxy havuzu arkasında veya T122 gerçek limiti ölçtükten sonra yükseltilir. Değişiklik sidecar restart gerektirir |
| `HD_WALLET_MNEMONIC` | blockchain sidecar | Deposit adresi türetme (08 §3.2) |
| `TRON_USDT_CONTRACT` / `TRON_USDC_CONTRACT` | blockchain sidecar | **Yalnız testnet'te (nile/shasta) zorunlu** — mainnet adresleri koda gömülü. Boşsa desteklenen-token allowlist'i boş kalır ve gelen her transfer wrong/spam token sayılır (08 §3.3) |
| `HOT_WALLET_ADDRESS` / `HOT_WALLET_PRIVATE_KEY` | blockchain sidecar | Payout/refund/sweep imzası + sweeper Energy delegation (Docker secret olarak mount, 05 §3.3/§3.5) |
| `TRON_NETWORK` (+ `TRON_*_CONTRACT` testnet'te) | blockchain sidecar | mainnet/nile/shasta + token kontratları (08 §3.3) |
| `TRON_API_KEY` (+ `TRON_API_KEY_SECONDARY`) | blockchain sidecar | TronGrid rate-limit + failover (WP10) |
| `TELEGRAM_BOT_TOKEN` / `TELEGRAM_CHAT_ID` / `ALERT_EMAIL_TO` | monitoring (Grafana) | Alert kanalı — **ayrıca Grafana'nın açılması için zorunlu.** `contactpoints.yml` bir Telegram kanalı tanımlar; Grafana provisioning'inde koşul yoktur, tanımlı kanal doğrulanamazsa Grafana startup'ta abort eder ve container crash-loop'a girer. Değerler `skinora-grafana-provisioning` render adımıyla yerine konur (§G.6) |

---

## C. Production'da ayarlanması önerilen SystemSetting'ler

Seed default'u `NONE`/varsayılan ile açılır ama set edilmezse ilgili kapsam **çalışmaz** (warn log ile atlanır, fail-fast DEĞİL).

> **⚠ Bunlar `SKINORA_SETTING_*` env ile set EDİLEMEZ — yalnızca admin UI'dan.** Aşağıdaki beş satırın hepsi `SystemSettingSeed`'de `Default(...)` ile, yani `IsConfigured = true` olarak gelir. `SettingsBootstrapService` yalnız `IsConfigured = false` satırları env'den hydrate eder ve configured bir satırı **asla** override etmez (06 §8.9 güvenlik klozu). §A'daki 19 satır `Unconfigured(...)` olduğu için env yolu yalnız orada çalışır. (Doğrulandı 2026-07-29 — `SystemSettingSeed.cs`'te tam 19 `Unconfigured` satırı var.)

| Key | Default | Set edilmezse |
|---|---|---|
| `reconciliation.hot_wallet_address` | NONE | Günlük reconciliation hot wallet kapsamı atlanır (warn) — 05 §3.3 |
| `reconciliation.cold_wallet_address` | NONE | Cold wallet reconciliation kapsamı atlanır (info) |
| `auth.banned_countries` | NONE | Geo-block uygulanmaz (02 §21.1) |
| `multi_account.exchange_addresses` | NONE | Çoklu-hesap kontrolünde exchange adres allowlist'i boş |
| `price_deviation_threshold` | 1.0 (= %100) | Seed default'la PRICE_DEVIATION fraud kuralı pratikte hiç ateşlemez (WP4a bilinçli geniş default). Prod'da daraltılmalı (02 §14.4) — **aşağıdaki `SteamMarket__Provider` ile birlikte** anlamlı olur |

### C.1 PRICE_DEVIATION için uygulama config'i (SystemSetting değil)

Fraud PRICE_DEVIATION kuralının kod yolu tamdır (WP4a: `IMarketPriceProvider` → `PriceServiceMarketPriceProvider` → `IPriceService` → `ISteamMarketPriceClient`), ancak **fiyat kaynağı varsayılan olarak kapalıdır**: `Program.cs` yalnız `SteamMarket:Provider == "steam-market"` iken gerçek HTTP istemcisini kaydeder; aksi halde `LoggingSteamMarketPriceClient` her çağrıda `NoPrice()` döner ve kural fail-open davranır (08 §7.4). Bu, taze checkout / CI'ın kazara `steamcommunity.com`'a çıkmasını engellemek için bilinçli bir default'tur.

| Env var | Default | Prod'da |
|---|---|---|
| `SteamMarket__Provider` | `logging` | PRICE_DEVIATION isteniyorsa **`steam-market`** yapılmalı; aksi halde kural sessiz kalır |
| `SteamMarket__BaseUrl` | `https://steamcommunity.com` | Değiştirmeye gerek yok |
| `SteamMarket__RateLimitPerMinute` | 20 | Steam Market rate-limit penceresi (08 §7.2) |
| `SteamMarket__TimeoutSeconds` | 10 | HTTP timeout |
| `SteamMarket__FreshTtlHours` / `SteamMarket__StaleTtlHours` | 24 / 48 | Fiyat cache TTL'leri (08 §7.3) |

> **Kontrol:** `Provider=logging` bırakılırsa işlem oluşturmada `MarketPriceAtCreation` null kalır, PRICE_DEVIATION flag'i üretilmez ve admin flag kuyruğunda bu tip hiç görünmez. Bu, deploy'un **bilinçli** bir kararı olmalıdır.

---

## D. Sidecar config parity (cadence / sweep) — env otoriter, restart-bound

Aşağıdaki ayarlar **hem** backend SystemSetting **hem** sidecar env olarak yaşar. Sidecar config'i **env-only boot** okur (`sidecar-blockchain/config/index.ts`); backend SystemSetting kopyası **admin görünürlüğü** ve tek-kanonik-kaynak içindir ama **çalışan sidecar'a runtime'da yansımaz**. Bu değerlerin değişimi sidecar env güncellemesi + **sidecar restart** gerektirir.

> **WP14 owner kararı (2026-06-19):** runtime push/pull DEĞİL → env parity + runbook. Runtime propagasyon post-MVP (T74 K1 / T96). MVP'de bu cadence'ler nadiren değişen operasyonel knob'lar; env + restart yeterli.

| Backend SystemSetting | Sidecar env | Default | Anlam |
|---|---|---|---|
| monitoring_post_cancel_24h_polling_seconds | `POST_CANCEL_CADENCE_24H_MS` | 30 sn | İptal sonrası 0-24 saat polling |
| monitoring_post_cancel_7d_polling_seconds | `POST_CANCEL_CADENCE_7D_MS` | 300 sn | 1-7 gün polling |
| monitoring_post_cancel_30d_polling_seconds | `POST_CANCEL_CADENCE_30D_MS` | 3600 sn | 7-30 gün polling |
| blockchain.sweep_energy_delegation_sun | `SWEEP_ENERGY_DELEGATION_SUN` | 200000000 | Sweep öncesi Energy delegation (SUN) |
| blockchain.sweep_trx_fallback_sun | `SWEEP_TRX_FALLBACK_SUN` | 15000000 | Energy delegation fallback TRX (SUN) |

**Parite kuralı:** Bir cadence/sweep değerini değiştirirken **hem** backend SystemSetting'i (admin görünürlüğü/audit için) **hem** sidecar env'ini güncelle, sonra sidecar'ı restart et. Yalnız backend SystemSetting'i değiştirmek runtime davranışı değiştirmez.

---

## E. Runtime-tunable ayarlar (restart gerektirmez)

| Ayar | Davranış | Kaynak |
|---|---|---|
| **`reconciliation.schedule_cron`**, **`hot_wallet.monitor_cron`** | Admin UI'dan değişince Hangfire recurring job **restart'sız re-register** olur (WP14). Geçersiz cron → 400 (validator). | `CronSettingChangePropagator` → registrar `Reconfigure` |
| `blockchain.refund_gas_fee_estimate_usdt`, `blockchain.payout_gas_fee_estimate_usdt` | Her operasyonda DB'den taze okunur — değişim bir sonraki çalıştırmada etkili. | `GasFeeSettingsProvider` (cache yok) |
| `blockchain.transfer_retry_intervals_minutes` | Her dispatcher tick'inde taze okunur. | `SystemSettingsTransferRetryPolicy` |
| Diğer limit/timeout/fraud SystemSetting'leri | İlgili servis okuma anında taze okur. | per-run reader |

> **Not (cron):** Cron değeri artık admin değişiminde anında re-register olduğu için seed yorumundaki "host restart gerekir / T96 devir" ifadesi WP14 ile **kapandı**. `monitoring_post_cancel_*` ve `sweep_*` ise (Bölüm D) hâlâ sidecar-restart-bound.

---

## F. Doğrulama (deploy sonrası)

1. **Startup fail-fast kontrolü:** 19 zorunlu env eksikse backend açılırken `InvalidOperationException` log'u + container çıkışı → eksik anahtar mesajda görünür.
2. **SystemSetting listesi:** `GET /api/v1/admin/settings` ile tüm katalog + configured/value kontrol.
3. **Cron re-register:** Admin UI'dan `reconciliation.schedule_cron` değiştir → log `ReconciliationJob re-registered with cron '...'` → Hangfire dashboard'da recurring job cron'u güncel.
4. **Sidecar parity:** cadence/sweep değişikliği sonrası sidecar env güncellenip restart edildiğini doğrula.

---

## G. Lokal gerçek-konfigürasyon provası

> **Bağlam (2026-07-29).** F6'nın 8 E2E süiti self-contained `docker-compose.e2e.yml` + tek `sidecar-fake` container'ı üzerinde koştu; Steam OAuth ve on-chain finality backend seam'inde simüle edildi. Asıl `docker-compose.yml` **hiç boot edilmemişti** ve ayağa kalkmayı engelleyen eksikleri vardı (backend'e 19 `SKINORA_SETTING_*` geçilmiyordu → fail-fast; iki sidecar'a `INTERNAL_KEY` geçilmiyordu; bot/hot-wallet/testnet-kontrat env'leri ve `SteamOpenId__*` yoktu). Bu bölüm o boşluğu kapatan çalışmanın sonucudur — gerçek Steam hesabı + gerçek bot + Nile testnet ile `http://localhost:8080` üzerinde tam stack.

### G.0 Ön koşullar

| Gereken | Not |
|---|---|
| Docker Desktop (çalışır durumda) | 11 container + SQL Server |
| .NET 9 SDK + `dotnet-ef` | Migration host'tan uygulanır — backend startup'ta **auto-migrate yoktur** |
| Steam bot hesabı | accountName / password / sharedSecret / identitySecret (Mobile Authenticator `maFile`'dan) |
| `STEAM_API_KEY` | steamcommunity.com/dev/apikey |
| Tron testnet cüzdanı | HD mnemonic + ayrı hot wallet (adres + private key), içinde faucet TRX |
| `TRON_API_KEY` | trongrid.io ücretsiz plan — yoksa monitor 429 yer |
| Nile USDT/USDC kontrat adresleri | Testnet'te koda gömülü değil, env'den gelir |

### G.1 Sırlar

Değerler **hiçbir zaman** repo'ya girmez. Yerleri:

- `.env` — env olarak taşınan sırlar (gitignored; şablon `.env.example`)
- `secrets/steam-bots.json` — bot kimlik bilgileri (gitignored; şablon [`secrets/README.md`](../secrets/README.md))
- `scripts/git-hooks/pre-commit` — staged içerikte sır kalıbı bulursa **commit'i** bloklar (`bash scripts/git-hooks/install.sh` ile kurulur)

`docker-compose.yml` yalnız `${VAR}` referansı taşır. Her servis **yalnız kendi** env'ini alır: `HOT_WALLET_PRIVATE_KEY` ve `HD_WALLET_MNEMONIC` sadece blockchain sidecar'a gider — backend imza materyalini hiç görmez (F4 gate check garantisi).

### G.2 Adımlar

```bash
# 0) Hook'lar (yeni clone sonrası bir kez)
bash scripts/git-hooks/install.sh

# 1) .env'i şablondan üret ve doldur (§A'daki 19 + §B'deki secret'lar + §G.0 dış değerler)
cp .env.example .env
#    Rastgele secret üretimi:  openssl rand -hex 32
#    PUBLIC_ORIGIN ve STEAM_OPENID_* → http://localhost:8080

# 2) Bot kimlik bilgileri
#    secrets/steam-bots.json — şablon secrets/README.md'de

# 3) Veritabanı + Redis
docker compose -f docker-compose.yml up -d skinora-db skinora-redis
#    healthy olmasını bekleyin:
docker inspect --format '{{.State.Health.Status}}' skinora-db

# 4) Şema (host'tan; backend auto-migrate ETMEZ)
cd backend
ConnectionStrings__DefaultConnection='Server=localhost,1433;Database=Skinora;User Id=sa;Password=<MSSQL_SA_PASSWORD>;TrustServerCertificate=True;' \
  dotnet ef database update \
    --project src/Skinora.Shared/Skinora.Shared.csproj \
    --startup-project src/Skinora.API/Skinora.API.csproj \
    --context AppDbContext
cd ..

# 5) Tüm stack — -f ZORUNLU (bkz. G.3)
docker compose -f docker-compose.yml up -d --build --wait

# 6) Steam ile giriş yapın → http://localhost:8080

# 7) Süper admin + bot kaydı (scripts/bootstrap/README.md)
#    01 sonrası çıkış+giriş yapın: super_admin claim'i yeni token'da gelir
```

### G.3 `-f docker-compose.yml` neden zorunlu

Çıplak `docker compose up` komutu `docker-compose.override.yml`'i de katmanlar. Override, frontend ve iki sidecar'a host kaynağını bind-mount eder (`./frontend:/app` vb.) — ama bu imajlar **production build**'dir (Next.js `.next/standalone`, sidecar `dist/`). Bind mount entrypoint'leri gizler ve container'lar açılmaz. Override bir **dev şablonu**dur; gerçek çalıştırmada devre dışı bırakılır.

Aynı sebeple `NEXT_PUBLIC_API_URL`'in compose'daki runtime değeri **etkisizdir**: Next.js `NEXT_PUBLIC_*`'ı build-time inline eder ve frontend Dockerfile'ı build-arg almaz. İmaj same-origin default'larıyla (`/api/v1`, `/hubs`) build edilmiştir — nginx tek-origin kurulumunda doğru olan da budur.

### G.4 Doğrulama

| # | Kontrol | Beklenen |
|---|---|---|
| 1 | `docker compose -f docker-compose.yml ps` | tüm servisler `healthy` |
| 2 | `curl http://localhost:8080/health` | 200 |
| 3 | `docker logs skinora-backend` | fail-fast **yok**; `SettingsBootstrap` 19 anahtarı configured yaptı |
| 4 | `curl http://localhost:5100/health` | `1/1 bots ready` — skeleton mode değil |
| 5 | `docker logs skinora-steam-sidecar` | `Bot credentials loaded {source: file, count: 1}` |
| 6 | `docker logs skinora-blockchain-sidecar` | nile bağlantısı + USDT/USDC allowlist dolu |
| 7 | Tarayıcı → `http://localhost:8080` | gerçek Steam login, profil adı/avatar gerçek |
| 8 | `GET /api/v1/admin/settings` | 59 satır; 19'u configured |
| 9 | Envanter listesi | gerçek CS2 envanteri (`steam-inventory` limiti 5/dk) |
| 10 | Happy path | işlem → kabul → trade offer → `ITEM_ESCROWED` → deposit adresi → Nile USDT transferi → `payment-detected` → 20 blok → `payment-confirmed` → teslim → `COMPLETED` + payout |

### G.5 Bilinen tuzaklar

- **Steam trade hold (en büyük risk).** Bot hesabında Mobile Authenticator 7 günden yeniyse Steam 15 günlük trade hold uygular; item bot'a girer ama teslim aşaması bekler. Sistem bunu doğru yönetir (U17 `SidecarTradeHoldChecker`, WP6) ama happy path'i canlı görmek gecikir — bot'un MA yaşını baştan kontrol edin.
- **Nile kontratları.** Testnet USDT/USDC adresleri sabit değildir; faucet'in verdiğini kullanın ve sidecar'ın bakiye çağrısıyla teyit edin. Boş bırakılırsa allowlist boş kalır.
- **Energy.** Sweep `delegateresource` ile 200 TRX delege eder; hot wallet'ta yeterli testnet TRX yoksa 15 TRX fallback'i de tükenir → `OUT_OF_ENERGY`.
- **`SteamMarket__Provider`.** Provada `logging` (default) bırakmak önerilir — PRICE_DEVIATION sessiz kalır ve `steamcommunity.com`'a çıkılmaz (§C.1).
- **Hangfire dashboard.** nginx `/hangfire`'ı proxy'lemez (`/` frontend'e gider); doğrudan `http://localhost:5000/hangfire` kullanın.

### G.6 Grafana provisioning render adımı

`skinora-grafana` artık `./infra/grafana/provisioning` dizinini **doğrudan mount etmez**; `skinora-grafana-provisioning` adlı tek seferlik bir servis dizini bir volume'e kopyalar, `${TELEGRAM_BOT_TOKEN}` / `${TELEGRAM_CHAT_ID}` / `${ALERT_EMAIL_TO}` yer tutucularını doldurur ve çıkar. Grafana bu **render edilmiş kopyayı** okur (`depends_on: service_completed_successfully`).

**Neden:** Grafana'nın kendi `${VAR}` interpolasyonu, yerine koyduğu değeri yeniden tipler. Telegram chat ID'leri her zaman numeriktir → JSON *number* olarak gelir → entegrasyonun *string* alanına oturmaz:

```
cannot unmarshal number into Go struct field Config.chatid of type string
```

Grafana bunun üzerine startup'ı iptal eder. `grafana/grafana:11.3.0` üzerinde ölçüldü: literal tırnaklı chat id ile **açılıyor**, `${VAR}` ve `$__env{VAR}` ile **açılmıyor**. Render'ı Grafana dosyayı ayrıştırmadan önce yapmak değeri YAML tırnakları içinde bırakır, string olarak kalır.

**Sonuçları:**
- Provisioning dosyalarını değiştirdikten sonra render'ı yeniden koşturun: `docker compose -f docker-compose.yml up -d --force-recreate skinora-grafana` (bağımlılık zinciri render'ı otomatik tetikler).
- `TELEGRAM_*` boş bırakılamaz — bkz. §B. Gerçek bot yoksa lokal provada boş olmayan herhangi bir değer yeterlidir.
- Render adımı ayrıca `plugins/` dizinini oluşturur (Grafana yokluğunda hata log'lar; zararsız ama gürültülü).
