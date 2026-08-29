# Skinora — Deploy Runbook

**Oluşturma:** WP14 (2026-06-19) · **Kapsam:** Production deploy öncesi sağlanması zorunlu/önerilen environment değişkenleri + sidecar config parity + runtime-tunable ayar davranışı.

> Bu runbook, "uygulama prod'da açılması için neyin set edilmesi gerekir?" sorusunun tek doğru kaynağıdır. `06_DATA_MODEL §3.17` (SystemSetting kataloğu) ve `08_INTEGRATION_SPEC` (sidecar env) ile tutarlıdır. Değer kaynakları: backend `SystemSettingSeed.cs` (63 satır), `SettingsBootstrapService` (06 §8.9 fail-fast), sidecar `config/index.ts`.

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
| **H. Launch checklist — teslimat kanıtı kapısı** | **Launch'ta ZORUNLU adım** | `delivery.inventory_evidence_auto_release_enabled` `false` seed edilir. İlk N gerçek teslimatın kanıtı insan tarafından incelenmeden envanter kanıtına dayalı otomatik para bırakma **açılmaz** (02 §9.2, T125). |

---

## A. Zorunlu SystemSetting env var'ları (19)

Bu 19 ayar `SystemSettingSeed.cs`'te **Unconfigured** (default'suz) gelir. `SettingsBootstrapService` startup'ta her birini `SKINORA_SETTING_<UPPER_KEY>` env var'ından hydrate eder; **herhangi biri eksik/hatalı ise `InvalidOperationException` ile fail-fast** (test: `SettingsBootstrapTests`). Zaten configured (admin UI'dan girilmiş) bir satır env ile **override edilmez** (06 §8.9 güvenlik klozu).

> **Neden default yok?** Bunlar iş-kritik değerler (işlem limitleri, hot wallet limiti, dormant eşiği). Yanlış bir seed-default sessizce prod'da çalışır; fail-fast bilinçli tercihtir. WP14 owner kararı (2026-06-19): seed-default DEĞİL → runbook.

| # | Env var | SystemSetting key | Tip | Örnek | Anlam |
|---|---|---|---|---|---|
| 1 | `SKINORA_SETTING_ACCEPT_TIMEOUT_MINUTES` | accept_timeout_minutes | int | 60 | Alıcı kabul timeout |
| 2 | `SKINORA_SETTING_SELLER_CONFIRM_TIMEOUT_MINUTES` | seller_confirm_timeout_minutes | int | 60 | Satıcı hazırlık onayı penceresi (03 §2.3) — T123'te yeniden adlandırıldı |
| 3 | `SKINORA_SETTING_PAYMENT_TIMEOUT_MIN_MINUTES` | payment_timeout_min_minutes | int | 15 | Ödeme timeout min |
| 4 | `SKINORA_SETTING_PAYMENT_TIMEOUT_MAX_MINUTES` | payment_timeout_max_minutes | int | 60 | Ödeme timeout max |
| 5 | `SKINORA_SETTING_PAYMENT_TIMEOUT_DEFAULT_MINUTES` | payment_timeout_default_minutes | int | 30 | Ödeme timeout varsayılan (min ≤ x ≤ max) |
| 6 | `SKINORA_SETTING_DELIVERY_TIMEOUT_MINUTES` | delivery_timeout_minutes | int | 60 | Satıcı teslimat penceresi (02 §2.2 adım 6) — T123'te yeniden adlandırıldı, T124'te tüketilmeye başlandı; **60 bağlayıcı değil**, aşağıdaki uyarıya bak |
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

> **Anahtar yeniden adlandırma (T123, 2026-08-13):** #2 ve #6'nın hem SystemSetting anahtarı hem env var adı değişti (`trade_offer_seller_timeout_minutes` → `seller_confirm_timeout_minutes`, `trade_offer_buyer_timeout_minutes` → `delivery_timeout_minutes`). Env adı anahtardan türetildiği için (`SettingsBootstrapService`: `SKINORA_SETTING_{KEY_UPPER}`) **eski env var adları artık hiçbir şeyi doldurmaz** — bu ikisini `.env`'inde eski adla taşıyan bir ortam startup'ta fail-fast eder. `.env.example`, `docker-compose.yml` ve `docker-compose.e2e.yml` güncellendi; kendi `.env` dosyanı elle güncelle. DB tarafında migration `T123_RenameTimeoutSettings` bir `UpdateData`'dır (satır `Id`'leri sabit) → admin UI'dan girilmiş değerler korunur.

> **#6 uyarısı (T122 doğrulaması, 2026-08-13; anahtar adı T123'te düzeltildi, tüketici T124'te bağlandı, **doğrulama turu T127'de canlı**) — `delivery_timeout_minutes` örneği (60 dk) ölçülmemiş bir sayıdır.** Bu ayar **satıcının teslimat penceresini** besliyor: `TimeoutSchedulingService.ArmDeliveryDeadlineAsync` ödeme onayında (`ConfirmPayment`) okuyup `DeliveryDeadline`'ı yazıyor. **T127 itibarıyla bu değeri düşük vermek doğrudan para hareketi üretir** — süre dolduğunda scanner bir doğrulama turu çalıştırır ve tur "item hâlâ satıcıda" derse iptal + alıcıya iade + satıcıya kusur uygular (03 §4.4). T127 öncesinde süre dolması bir şey tetiklemiyordu; artık tetikliyor. T122'nin canlı ölçümü teslimat gecikmesini **ölçemedi** (trade yapılamadı — 11 §P2.5), dolayısıyla 60 dk bir teslimat penceresi olarak **doğrulanmadı**; T122 runbook §7.3 launch'ta **muhafazakâr yüksek** bir değerle açılmasını ve ölçüm üretimden geldiğinde daraltılmasını öneriyor. Bu satırdaki 60, `SettingsBootstrapTests` ile hizalı bir **örnek**tir — launch değeri olarak kopyalanmamalıdır. Kapanış T125 launch kapısına bağlı ([`INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md`](INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md) §7).

---

## B. Operasyonel secret / altyapı env'leri (ZORUNLU)

SystemSetting değil; servisin açılması ve dış entegrasyonlar için zorunlu. `.env.example` referans alınır.

| Env var | Servis | Anlam |
|---|---|---|
| `DB_CONNECTION_STRING` / `ConnectionStrings__DefaultConnection` | backend | SQL Server connection string (Hangfire de aynı bağlantıyı kullanır) |
| `REDIS_CONNECTION_STRING` | backend | Redis (distributed lock, cache) |
| `REVERSE_PROXY_KNOWN_NETWORK` | backend | **Reverse proxy arkasında ZORUNLU (F3a).** Backend istemci IP'sini yalnız bu ağdan gelen `X-Forwarded-For` başlığına güvenerek çözer. Boş bırakılırsa forwarded header **hiç işlenmez** ve `RemoteIpAddress` proxy'nin IP'si olarak kalır → `auth` rate limit izolasyonu, geo-block ve VPN sinyali **sessizce etkisiz** olur. Compose varsayılanı `172.20.0.0/16` (skinora-network). **Üretimde yalnız reverse proxy'nin bulunduğu ağ/adres yazılmalıdır** — geniş aralık güveni sulandırır |
| `REVERSE_PROXY_FORWARD_LIMIT` | backend | Zincirdeki proxy sayısı, varsayılan `1` (tek nginx). Önünde CDN varsa artırın. **Olduğundan büyük vermek istemcinin zincire sahte girdi eklemesine izin verir** — ölçüldü: `ForwardLimit=1` ile nginx üzerinden sahte `X-Forwarded-For` gönderen istemci kendi kovasından kaçamıyor |
| `JWT_SECRET` (≥32 char) | backend | Access/refresh token imzası |
| `JWT_ISSUER` / `JWT_AUDIENCE` | backend | Token issuer/audience |
| `WEBHOOK_SECRET` (≥32 char) | backend + **blockchain** sidecar | Sidecar→backend HMAC-SHA256 webhook imzası (05 §3.4). Steam sidecar webhook göndermez (T133) — kalan tek imzalı yüzey blockchain'dir |
| `INTERNAL_KEY` | backend + sidecar'lar | Backend↔sidecar internal API `X-Internal-Key` auth |
| `STEAM_API_KEY` | steam sidecar + backend | Steam Web API (envanter + trade-hold probu) + OpenID profil adı/avatar (`SteamOpenId__WebApiKey`; boşsa login çalışır ama profil placeholder'a düşer). **Steam sidecar'ın tek credential'ı budur** — bot hesabı gerekmez (T133, 08 §9.1) |
| `STEAM_OPENID_REALM` / `_RETURN_TO` / `_REVERIFY_RETURN_TO` / `_FRONTEND_CALLBACK` | backend | Steam OpenID 2.0 (08 §2.1). **`appsettings.json` default'u `https://skinora.com`** — override edilmezse gerçek login ölü bir domaine yönlenir |
| `PUBLIC_ORIGIN` | backend | Tarayıcıya bakan tek origin → `Cors__AllowedOrigins__0` |
| `STEAM_SIDECAR_REDIS_URL` | steam sidecar | Envanter cache (08 §2.3); boşsa in-memory fallback |
| `STEAM_SIDECAR_COMMUNITY_REQUESTS_PER_MINUTE` | steam sidecar | Steam Community envanter ucunun kuyruk tavanı, istek/dakika (08 §2.6, T120). Web API kuyruğundan **ayrı**. Boş/geçersiz → **10/dk** (tahmini 10-20/dk/IP aralığının muhafazakâr ucu; aşım IP bloğuyla cezalandırılır). Her teslimat doğrulaması **iki** okuma harcadığı için (satıcı + alıcı) bu değer aynı zamanda eşzamanlı doğrulama tavanıdır (10 §4). Yalnız proxy havuzu arkasında veya T122 gerçek limiti ölçtükten sonra yükseltilir. Değişiklik sidecar restart gerektirir |
| `HD_WALLET_MNEMONIC` | blockchain sidecar | Deposit adresi türetme (08 §3.2) |
| `TRON_USDT_CONTRACT` / `TRON_USDC_CONTRACT` | blockchain sidecar **+ backend** | **Yalnız testnet'te (nile/shasta) zorunlu** — mainnet adresleri koda gömülü. Boşsa desteklenen-token allowlist'i boş kalır ve gelen her transfer wrong/spam token sayılır (08 §3.3). **Aynı iki değer backend'e de `StablecoinContracts__Usdt` / `__Usdc` olarak geçer** (`docker-compose.yml`); ikisi ayrışırsa ödeme sessizce ölür — backend izleyiciyi `expectedContract` ile kurar, sidecar geleni ona göre sınıflar, ve eşleşmeyen **doğru** bir ödeme `wrong_token` sayılıp otomatik iadeye gider |
| `TRONSCAN_TX_BASE_URL` | frontend (**build-time**) | İşlem hash'lerinin bağlandığı blok gezgini. `NEXT_PUBLIC_*` derleme anında bundle'a gömülür, bu yüzden `build.args` ile geçer — **değiştirmek frontend imajının yeniden kurulmasını gerektirir**. Boş = mainnet `tronscan.org`. Nile'da `https://nile.tronscan.org/#/transaction/` |
| `HOT_WALLET_ADDRESS` / `HOT_WALLET_PRIVATE_KEY` | blockchain sidecar | Payout/refund/sweep imzası + sweeper Energy delegation (Docker secret olarak mount, 05 §3.3/§3.5) |
| `TRON_NETWORK` (+ `TRON_*_CONTRACT` testnet'te) | blockchain sidecar | mainnet/nile/shasta + token kontratları (08 §3.3) |
| `TRON_API_KEY` (+ `TRON_API_KEY_SECONDARY`) | blockchain sidecar | TronGrid rate-limit + failover (WP10) |
| `TELEGRAM_BOT_TOKEN` / `TELEGRAM_CHAT_ID` / `ALERT_EMAIL_TO` | monitoring (Grafana) | Alert kanalı — **ayrıca Grafana'nın açılması için zorunlu.** `contactpoints.yml` bir Telegram kanalı tanımlar; Grafana provisioning'inde koşul yoktur, tanımlı kanal doğrulanamazsa Grafana startup'ta abort eder ve container crash-loop'a girer. Değerler `skinora-grafana-provisioning` render adımıyla yerine konur (§G.6) |

---

## C. Production'da ayarlanması önerilen SystemSetting'ler

Seed default'u `NONE`/varsayılan ile açılır ama set edilmezse ilgili kapsam **çalışmaz** (warn log ile atlanır, fail-fast DEĞİL).

> **⚠ Bunlar `SKINORA_SETTING_*` env ile set EDİLEMEZ — yalnızca admin UI'dan.** Aşağıdaki dokuz satırın hepsi `SystemSettingSeed`'de `Default(...)` ile, yani `IsConfigured = true` olarak gelir. `SettingsBootstrapService` yalnız `IsConfigured = false` satırları env'den hydrate eder ve configured bir satırı **asla** override etmez (06 §8.9 güvenlik klozu). §A'daki 19 satır `Unconfigured(...)` olduğu için env yolu yalnız orada çalışır. (Doğrulandı 2026-07-29 — `SystemSettingSeed.cs`'te tam 19 `Unconfigured` satırı var.)

| Key | Default | Set edilmezse |
|---|---|---|
| `reconciliation.hot_wallet_address` | NONE | Günlük reconciliation hot wallet kapsamı atlanır (warn) — 05 §3.3 |
| `reconciliation.cold_wallet_address` | NONE | Cold wallet reconciliation kapsamı atlanır (info) |
| `auth.banned_countries` | NONE | Geo-block uygulanmaz (02 §21.1) |
| `multi_account.exchange_addresses` | NONE | Çoklu-hesap kontrolünde exchange adres allowlist'i boş |
| `price_deviation_threshold` | 1.0 (= %100) | Seed default'la PRICE_DEVIATION fraud kuralı pratikte hiç ateşlemez (WP4a bilinçli geniş default). Prod'da daraltılmalı (02 §14.4) — **aşağıdaki `SteamMarket__Provider` ile birlikte** anlamlı olur |
| `delivery.inventory_evidence_auto_release_enabled` | `false` | **Launch'ta bilinçli olarak `false` kalır** — envanter kanıtı kaydedilir ve gösterilir ama parayı tek başına serbest bırakmaz. Açma prosedürü §H'de; alıcının kendi onayı bundan etkilenmez (T125, 02 §9.2) |
| `settlement.reversal_auto_refund_enabled` | `false` | **Launch'ta bilinçli olarak `false` kalır** — mutabakat sonu kontrolü geri alma imzası bulursa admin'e eskale eder, otomatik iade + fraud flag uygulamaz. Açma prosedürü §I'de (T129, 02 §4.5.1) |
| `payout_settlement_days` | `8` | Seed default'u zaten doğru değerdir (7 günlük Steam penceresi + 1 gün marj). Değiştirilecekse **7'nin altına inilemez** — validator reddeder (02 §16.2) |
| `settlement.unreadable_escalation_hours` | `48` | Mutabakat kontrolü envanter okunamadığı için sonuca varamadığında admin'e ne kadar sonra düşeceği. Kısaltmak admin kuyruğunu şişirir, uzatmak satıcının ödemesini geciktirir — ödeme her iki hâlde de parkta kalır (03 §2.4) |

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

> **Doğrulama — backend açılış log'u (WP1/T81).** Bu iki ayarın birlikte doğru olup olmadığı artık tahmin edilmiyor: backend her açılışta verdict'i basar. Kesin olarak birini göreceksiniz:
>
> ```
> PRICE_DEVIATION rule ACTIVE — SteamMarket:Provider=steam-market, price_deviation_threshold=0.3 (30% deviation flags a transaction).
> ```
>
> ```
> PRICE_DEVIATION rule INEFFECTIVE — it will never flag a transaction. Price source: NO PRICE (logging stub, fail-open) ...
> ```
>
> `INEFFECTIVE` bir hata değildir — fail-open bir deploy geçerli bir tercihtir (yukarıdaki kontrol) — ama **sessiz** olmamalıdır. Bu satır `ForwardedHeadersNotRegistered` dersinin fraud config'e uygulanmış hâlidir: varsayılan duruşta etkisiz kalan bir kontrolün bunu açılışta söylemesi gerekir, yoksa fark etmenin tek yolu "hiçbir şey flaglenmemiş" olduğunu bir gün fark etmektir. Kontrol komutu:
>
> ```bash
> docker logs skinora-backend 2>&1 | grep "PRICE_DEVIATION rule"
> ```

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

> **F3a — proxy güveni açık mı?** Backend başlangıç logunda tam olarak bir satır olmalı:
> `Forwarded headers ENABLED — trusting networks [...]`. Bunun yerine
> `Forwarded headers DISABLED` görüyorsanız istemci IP'si çözülmüyor demektir ve
> üç güvenlik kontrolü hata vermeden etkisizdir. **Uç doğrulama:** bir giriş sonrası
> `UserLoginLogs.IpAddress` proxy'nin IP'si DEĞİL, gerçek istemcinin IP'si olmalı.

1. **Startup fail-fast kontrolü:** 19 zorunlu env eksikse backend açılırken `InvalidOperationException` log'u + container çıkışı → eksik anahtar mesajda görünür.
2. **SystemSetting listesi:** `GET /api/v1/admin/settings` ile tüm katalog + configured/value kontrol.
3. **Cron re-register:** Admin UI'dan `reconciliation.schedule_cron` değiştir → log `ReconciliationJob re-registered with cron '...'` → Hangfire dashboard'da recurring job cron'u güncel.
4. **Sidecar parity:** cadence/sweep değişikliği sonrası sidecar env güncellenip restart edildiğini doğrula.

---

### F.1 T132 öncesi bir veritabanında ölü yetki satırları (WP5)

`VIEW_STEAM_ACCOUNTS` ve `MANAGE_STEAM_RECOVERY` v3.0'da (T132) kaldırıldı. **Taze bir kurulum etkilenmez** — ölçüldü: bu iki anahtar hiçbir migration'da seed edilmiyor ve `PermissionCatalog`'da yok, dolayısıyla yeni bir DB onları hiç görmez.

Yalnızca T132 **öncesinde** bir admin tarafından bir role elle atanmışlarsa `AdminRolePermissions` satırları olarak kalmış olabilirler (tipik olarak uzun ömürlü bir dev/staging DB'si). Bu bir tuzak değildir ve kendiliğinden temizlenir:

- `PermissionAuthorizationHandler` bu anahtarları isteyen bir policy bulamaz → fazladan yetki vermezler;
- rolün ilk düzenlemesinde `UpdateAsync` gönderilen küme dışındaki satırları soft-delete eder;
- frontend gönderilecek kümeyi katalogdan türettiği için ölü anahtar geri yollanamaz → `INVALID_PERMISSION` 400 doğmaz.

Kalan etki kozmetiktir: AD14 rol detayında fazladan bir eleman görünür. Migration yazılmadı — seed edilmemiş ve kendiliğinden düzelen satırlar için migration maliyeti karşılığını vermez. Erken temizlemek isteyen operatör için ilgili rolü bir kez kaydetmek yeterlidir.

## G. Lokal gerçek-konfigürasyon provası

> **Bağlam (2026-07-29).** F6'nın 8 E2E süiti self-contained `docker-compose.e2e.yml` + tek `sidecar-fake` container'ı üzerinde koştu; Steam OAuth ve on-chain finality backend seam'inde simüle edildi. Asıl `docker-compose.yml` **hiç boot edilmemişti** ve ayağa kalkmayı engelleyen eksikleri vardı (backend'e 19 `SKINORA_SETTING_*` geçilmiyordu → fail-fast; iki sidecar'a `INTERNAL_KEY` geçilmiyordu; bot/hot-wallet/testnet-kontrat env'leri ve `SteamOpenId__*` yoktu). Bu bölüm o boşluğu kapatan çalışmanın sonucudur — gerçek Steam hesabı + Nile testnet ile `http://localhost:8080` üzerinde tam stack. **T133 notu:** o turdaki "gerçek bot" ön koşulu kalktı; sidecar hiçbir Steam hesabı taşımaz (§G.0, 05 §3.2), prova için gereken tek Steam credential'ı `STEAM_API_KEY`'dir. **T133b notu:** §G.4'ün happy path anlatısı v3.0 P2P akışına çekildi ve **ikiye ayrıldı** — tek oturumda gözlenebilen kısım (kontrol 10) ile mutabakat penceresinin ardındaki payout kuyruğu (kontrol 10a). Bölünmenin sebebi bir doküman tercihi değil, ölçülen bir kısıt: `payout_settlement_days`'in tabanı **7 gün**dür ve admin altına inemez (`SystemSettingsValidator.MinimumSettlementDays`, 02 §16.2), dolayısıyla eski tek satırın vaat ettiği "→ `COMPLETED` + payout" bir oturumda **hiçbir zaman** gözlenemezdi.

### G.0 Ön koşullar

| Gereken | Not |
|---|---|
| Docker Desktop (çalışır durumda) | 11 container + SQL Server |
| .NET 9 SDK + `dotnet-ef` | Migration host'tan uygulanır — backend startup'ta **auto-migrate yoktur** |
| `STEAM_API_KEY` | steamcommunity.com/dev/apikey. Steam sidecar'ın **tek** kimlik bilgisi — bot hesabı gerekmez (T133) |
| Tron testnet cüzdanı | HD mnemonic + ayrı hot wallet (adres + private key), içinde faucet TRX |
| `TRON_API_KEY` | trongrid.io ücretsiz plan — yoksa monitor 429 yer |
| Nile USDT/USDC kontrat adresleri | Testnet'te koda gömülü değil, env'den gelir |

### G.0a F6 → F7 yükseltmesi (mevcut bir ortam için)

> **Kimin için:** bu bölümü **F7 öncesi** (2026-08-08'den önce) kurmuş ve `.env` + veritabanını o günden beri taşımış bir ortam için. **Sıfırdan kuranlar bu adımı atlar** — `.env.example` ve migration zinciri zaten v3.0'dır.
>
> **Neden gerekli (F7 Gate Check, 2026-08-22 — bulgu F7-N4):** F7 üç yerde geriye dönük uyumsuz değişiklik yaptı ve hiçbiri kendiliğinden uygulanmaz. Backend startup'ta **auto-migrate yoktur** (§G.0), env anahtar adları koda gömülü değildir ve `SettingsBootstrapService` yalnız **yapılandırılmamış** satırlara dokunur (06 §8.9) — yani eski adlı satırlar sessizce yerinde kalır. Gate ölçümünde bu makinedeki ortam tam olarak bu durumdaydı: image'ler 2026-07-26 tarihli, veritabanında 31/40 migration, `SystemSettings`'te hâlâ custody adları.

| # | Adım | Komut / kontrol | Neden |
|---|---|---|---|
| 1 | **Şemayı en güncel migration'a çıkar** | `dotnet ef database update --project src/Skinora.Shared/Skinora.Shared.csproj --startup-project src/Skinora.API/Skinora.API.csproj --context AppDbContext` (host'tan, §G.2 deseni) | F7 dokuz migration ekledi: `T117_P2P_Pivot` · `T123_RenameTimeoutSettings` · `T125_DeliveryEvidenceCapture` · `T127_AddDeliveryRoundAt` · `T129_SettlementCheckColumns` · `T129_SettlementEscalationColumns` · `T130_WrongItemEvidenceColumns` · `T131_DisputeResolutionOverrideReason` · `T131_TimeoutReleasedByAdminRulingAt`. `T117_P2P_Pivot` emekli tabloları (`TradeOffers`, `PlatformSteamBots`, `BotRecoveryItems`) düşürür |
| 2 | **Doğrula: migration sayısı repo ile eşit** | `SELECT COUNT(*) FROM __EFMigrationsHistory;` → repo'daki migration **dosyası** sayısına eşit olmalı. Beklenen sayı **sabit değildir, türetilir**: `ls backend/src/Skinora.Shared/Persistence/Migrations/*.cs \| grep -vE "Designer\|ModelSnapshot" \| wc -l` (2026-08-23 itibarıyla **41**; F7 kapanışında 40 idi, [#262](https://github.com/turkerurganci/Skinora/pull/262) `OutboxSequenceOrdering` ekledi) | Adım 1'in kanıtı. **Sabit sayı bilinçli olarak yazılmıyor:** bu satır F7 gate'inde **40** olarak yazıldı ve ertesi gün tek bir chore PR'ı onu bayatlattı. Aynı kusur aynı gün `DEFERRED_BACKLOG` başlık sayacında da yaşandı — türetilebilir bir sayının elle tutulan kopyası her PR'da yeniden bozulur |
| 3 | **`.env`'de iki anahtarı yeniden adlandır** | `SKINORA_SETTING_TRADE_OFFER_SELLER_TIMEOUT_MINUTES` → **`SKINORA_SETTING_SELLER_CONFIRM_TIMEOUT_MINUTES`**; `SKINORA_SETTING_TRADE_OFFER_BUYER_TIMEOUT_MINUTES` → **`SKINORA_SETTING_DELIVERY_TIMEOUT_MINUTES`** | T123 adlandırma kararı. Eski adlar custodial dönemden kalma ve **sorumluluğu ters** anlatıyordu (teslimat penceresi satıcınındır, alıcının değil — T119 denetimi). Yapmazsan `docker compose config` iki `variable is not set` uyarısı verir ve `SettingsBootstrapService` bu iki satırı hidrate edemez |
| 4 | **`.env`'den `STEAM_BOTS_CONFIG_PATH` satırını sil** | — | T133'te konusuz kaldı: `secrets/steam-bots.json` ve onu okuyan katman silindi; sidecar hiçbir Steam hesabı kimlik bilgisi taşımaz |
| 5 | **Doğrula: uyarısız config** | `docker compose config --quiet` → `variable is not set` uyarısı **yok** | Adım 3–4'ün kanıtı |
| 6 | **Doğrula: v3.0 ayar adları DB'de** | `SELECT [Key] FROM SystemSettings WHERE [Key] LIKE '%timeout_minutes%';` → `accept_timeout_minutes`, **`delivery_timeout_minutes`**, **`seller_confirm_timeout_minutes`** (custody adları **yok**) | Migration `UpdateData` ile satır Id'leri sabit tutup adı değiştirir; **admin tarafından girilmiş değerler korunur** |
| 7 | **Image'leri F7 kodundan yeniden kur** | `docker compose build` → `docker compose up -d` | Eski image F7 şemasını okuyamaz. Image tarihini `docker inspect --format '{{.Created}}' escrow-skinora-backend` ile teyit et |
| 8 | **Doğrula: 63 SystemSetting** | `SELECT COUNT(*) FROM SystemSettings;` → **63** (F6: 59; T125 `delivery_verification` + T129 üç `settlement` anahtarı) | 07 §9.8 ile hizalı |

> **Sıra bağlayıcıdır:** adım 7'yi adım 1'den önce koşarsan F7 backend'i F6 şemasına bakar. Adım 3'ü atlarsan stack ayağa kalkar ama iki timeout ayarı env'den hidrate edilemez.

### G.1 Sırlar

Değerler **hiçbir zaman** repo'ya girmez. Yerleri:

- `.env` — env olarak taşınan sırların **tamamı** (gitignored; şablon `.env.example`). T133'ten beri dosya olarak taşınan sır yoktur; `secrets/` dizini ve koruma katmanları duruyor ama boş — bkz. [`secrets/README.md`](../secrets/README.md)
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

# 2) Veritabanı + Redis
docker compose -f docker-compose.yml up -d skinora-db skinora-redis
#    healthy olmasını bekleyin:
docker inspect --format '{{.State.Health.Status}}' skinora-db

# 3) Şema (host'tan; backend auto-migrate ETMEZ)
cd backend
ConnectionStrings__DefaultConnection='Server=localhost,1433;Database=Skinora;User Id=sa;Password=<MSSQL_SA_PASSWORD>;TrustServerCertificate=True;' \
  dotnet ef database update \
    --project src/Skinora.Shared/Skinora.Shared.csproj \
    --startup-project src/Skinora.API/Skinora.API.csproj \
    --context AppDbContext
cd ..

# 4) Tüm stack — -f ZORUNLU (bkz. G.3)
docker compose -f docker-compose.yml up -d --build --wait

# 5) Steam ile giriş yapın → http://localhost:8080

# 6) Süper admin (scripts/bootstrap/README.md)
#    Sonrasında çıkış+giriş yapın: super_admin claim'i yeni token'da gelir
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
| 4 | `curl http://localhost:5100/health` | 200 döner ve `status: healthy` der — ama **bu satır bir bağlantı kanıtı DEĞİLDİR** (ölçüm 2026-08-23): tek check `steam-api`'nin sonucu kaynakta sabittir (`sidecar-steam/src/health/HealthController.ts:29`, mesaj *"Connectivity probe deferred to T67"*), yani uç Steam'e hiç çıkmaz ve 200'den başkasını döndüremez. Aynısı blockchain sidecar'ın `tron-node` / `hot-wallet` check'leri için de geçerlidir. Bu satır yalnız **sürecin ayakta ve HTTP'ye cevap verir** olduğunu gösterir; bot kimlik bilgisi **beklenmez** (T133). Gerçek bağlantı kanıtı **kontrol 5–6'daki log satırlarıdır**. Backlog: `SidecarHealthChecksArePlacebo` |
| 5 | `docker logs skinora-steam-sidecar` | `Steam sidecar listening {port: 5100}`; bot/credential satırı **yok** |
| 6 | `docker logs skinora-blockchain-sidecar` | nile bağlantısı + USDT/USDC allowlist dolu |
| 7 | Tarayıcı → `http://localhost:8080` | gerçek Steam login, profil adı/avatar gerçek |
| 8 | `GET /api/v1/admin/settings` | **63** satır — `SeedDataTests` bu sayıyı assert eder; başarılı boot'ta **hiçbir satırın `value`'su null değildir**. Yanıt `isConfigured` **taşımaz** (07 §9.8 gereği configured'lık bilinçli olarak projekte edilmez; gözlenebilir vekil `value != null`), dolayısıyla 44 seed + 19 env dağılımı bu uçtan **okunamaz** — 19'un kanıtı kontrol 3'teki `SystemSetting bootstrap complete — {N} env-hydrated` log satırı, 44'ünki `SELECT COUNT(*) FROM SystemSettings WHERE IsConfigured = 1` (seed sonrası, boot öncesi) |
| 9 | Envanter listesi | gerçek CS2 envanteri (`steam-inventory` limiti 5/dk) |
| 10 | Happy path — **tek oturumda gözlenebilen** kısım | işlem oluşturma → alıcı kabulü (`POST /transactions/:id/accept`; trade URL + iade adresi, alıcının MA'sı canlı probe ile doğrulanır) → `ACCEPTED` → satıcı hazırlık onayı (`POST /transactions/:id/confirm-ready`; item hâlâ tradeable + alıcı MA aktif + alıcı envanterinin **baseline**'ı alınır) → `SELLER_CONFIRMED` — **deposit adresi alıcıya ancak burada açılır** → ödeme izleyicisi **aynı geçişte otomatik kurulur** (T139; kurulum kanıtı aşağıdaki notta) → Nile USDT transferi → `payment-detected` → 20 blok → `payment-confirmed` → `PAYMENT_RECEIVED` (+ `DeliveryDeadline` kurulur) → **satıcı item'ı doğrudan alıcıya gönderir** (detay yanıtındaki `steamTradeOfferUrl` = alıcının kendi trade URL'i, **yalnız satıcıya** döner; platform trade offer oluşturmaz, taraf değildir — 02 §2.2 adım 6) → alıcı "teslim aldım" (`POST /transactions/:id/confirm-receipt`) → `ITEM_DELIVERED`, `PayoutEligibleAt` damgalandı |
| 10a | Mutabakat kuyruğu — **pencere dolduktan sonra** | `settlement-verification` (cron `*/5 * * * *`) alıcının envanterini yeniden okur → item hâlâ alıcıdaysa `SettlementVerifiedAt` damgalanır → `seller-payout-queue` (cron `* * * * *`) `SELLER_PAYOUT` satırını kuyruklar → `OutgoingTransferDispatchJob` yayınlar, `OutgoingTransferConfirmationJob` on-chain onaylar, `PayoutCompletedConsumer` `Complete` geçişini ateşler → `COMPLETED`. **Tek oturumda gözlenemez:** `payout_settlement_days` varsayılanı 8 gün, **tabanı 7** (`SystemSettingsValidator.MinimumSettlementDays`, 02 §16.2) — admin altına inemez, ayardan kısaltılamaz. Kuyruğu aynı gün görmek için aşağıdaki prova kısayolu |

> **Kontrol 10'un ödeme bacağı artık otomatik kurulur (T139).** `confirm-ready` geçişi, geçişle aynı `SaveChanges` içinde `PaymentMonitorStartRequestedEvent`'i outbox'a yazar; `PaymentMonitorStartDispatcher` bunu tüketip sidecar'ın `POST /api/monitor/start` ucunu çağırır ve `EnsurePaymentMonitorJob` (cron `* * * * *`) açık penceredeki her adresi her dakika **yeniden** kurar — yani backend ya da sidecar prova sırasında yeniden başlarsa izleyici kendi kendine geri gelir. Bu satır T133b provasında ölçülen boşluğun kapanmasıdır: o tarihte `POST /api/monitor/start`'ı çağıran hiçbir backend kodu yoktu ve prova `SELLER_CONFIRMED`'da sessizce dururdu.
>
> **Kurulduğunu doğrulayın** (Nile transferini göndermeden önce — izleyici kurulu değilse transfer `payment-detected` üretmez):
>
> ```bash
> docker logs skinora-backend   2>&1 | grep "Payment monitor armed with sidecar"
> docker logs skinora-blockchain-sidecar 2>&1 | grep "Monitor started"
> curl -s http://localhost:5200/metrics | grep skinora_blockchain_active_monitors
> ```
>
> Üç kanıt da aynı şeyi söylemelidir: adres kayıtlı ve `skinora_blockchain_active_monitors` ≥ 1. Hiçbiri görünmüyorsa outbox teslimi düşmüş demektir — bir dakika bekleyin (reconciler kurar) ve `EnsurePaymentMonitorJob complete: ... armed=` log satırını arayın. `PaymentAddress` satırı hiç yoksa izleyici de kurulamaz; önce `ensure-payment-address` job'ının adresi tahsis etmesini bekleyin.
>
> **Pencerenin kapanışı da gözlenebilir:** işlem terminal olduğunda veya depozit sweep edildiğinde aynı reconciler `POST /api/monitor/stop` çağırır ve satırı `MonitoringStatus = STOPPED` damgalar. Prova sonunda `SELECT MonitoringStatus FROM PaymentAddresses WHERE TransactionId = '<transaction-id>'` → `STOPPED` beklenir; `ACTIVE` kalmışsa disarm kolu çalışmamıştır.

> **Kontrol 10a'nın prova kısayolu.** Mutabakat penceresi bir ayar değişikliğiyle kısaltılamaz — `payout_settlement_days` için 7 günlük taban `SystemSettingsValidator`'da sert kuraldır (02 §16.2) ve Steam'in 7 günlük trade geri alma penceresini kapatmak için vardır (02 §4.5.1). Kuyruğu aynı oturumda görmek isteyen prova, **yalnız provada**, işlemin uygunluk saatini geri alır:
>
> ```sql
> UPDATE Transactions SET PayoutEligibleAt = SYSUTCDATETIME() WHERE Id = '<transaction-id>';
> ```
>
> Kısayol hiçbir guard'ı zayıflatmaz ve **kuyruğu doğrudan açmaz**: `UPDATE` satırı yalnız `settlement-verification`'a **aday** yapar (o iş de `PayoutEligibleAt <= now` ile seçer). `SettlementVerificationJob` alıcının envanterini **gerçekten** yeniden okur (item alıcıda kesin olarak bulunamazsa satıcınınkini de) ve `SettlementVerifiedAt`'i ancak item alıcıda durduğu için damgalar; `seller-payout-queue` ise o damgayı **önkoşul** olarak arar (`SettlementVerifiedAt != null`, `SellerPayoutQueueJob`). Yani doğrulama kuyruğun ardılı değil **öncülüdür** ve kuyruk tek adımda değil **iki iş turunda** görünür (≤5 dk + ≤1 dk). Sahte olan tek şey saattir — verdict değil. **Üretimde yapılmaz:** orada beklemenin kendisi korumanın parçasıdır.
>
> **Kısayolun ön koşulu.** Alıcının Steam envanteri hem `confirm-ready` anında (baseline alınırken) hem de mutabakat turunda **okunabilir** olmalıdır. Değilse işlemin teslimat referansı oluşmaz, verdict `NoDeliveryReference` olur ve satır kuyruğa girmek yerine **eskale edilir** (§I.5) — kısayol beklendiği gibi çalışmaz ve bunu söyleyen bir hata mesajı da yoktur.
>
> **Kontrol 10 bilinçli olarak alıcı onayı yolundan geçer.** 02 §9.2'nin ikinci yolu (envanter kanıtı) launch'ta otomatik para hareketine bağlı değildir — `delivery.inventory_evidence_auto_release_enabled` kapalıyken kanıt üretilir ve kaydedilir ama teslimatı tek başına onaylamaz (§H). Alıcı onayı bu kapıdan etkilenmez, dolayısıyla provanın kendiliğinden ilerleyen tek yolu odur. Envanter kanıtı bacağını da görmek isteyen prova `DeliveryEvidenceCaptures` satırlarını okur (§H.3 sorgusu).

### G.5 Bilinen tuzaklar

- **Steam trade hold (en büyük risk).** Prova hesaplarında Mobile Authenticator 7 günden yeniyse Steam trade'i 15 günlük kendi escrow'una alır (02 §9.1). Riskin iki ayrı yüzü vardır ve P2P'de ağır olanı ikincisidir:
  - **Platform kapıları — üç tane, ama ikisi canlı.** Satıcı için işlem oluştururken (`TransactionEligibilityService`), alıcı için hem kabul (`TransactionAcceptanceService`) hem **hazırlık onayı** (`TransactionReadinessService`) adımında. **Alıcı tarafındaki iki kapı** canlı `GetTradeHoldDurations` probuyla çalışır (U17 `SidecarTradeHoldChecker`, WP6) ve **fail-closed**'dır: Steam sorgulanamazsa 503 `STEAM_UNAVAILABLE`, MA aktif değilse işlem ilerlemez. **Satıcı kapısı Steam'e hiç çıkmaz** — kalıcı `User.MobileAuthenticatorVerified` bayrağını okur (bayrak U17 trade-URL kaydında yazılır) ve MA yoksa `MOBILE_AUTHENTICATOR_REQUIRED` döner; yani **bayat bir `true` satıcıyı geçirebilir**. Alıcı tarafının canlı probe kullanmasının sebebi tam olarak bu bayatlama riskidir. Provada satıcının MA'sını Steam üzerinden **kendiniz** teyit edin, platformun geçirmesine güvenmeyin.
  - **Steam'in kendi hold'u.** Kapıları geçen bir işlemde bile, trade'i **satıcı** gönderdiği için (02 §2.2 adım 6) taraflardan birindeki hold item'ı 15 gün Steam escrow'unda tutar. Platform bu trade'e taraf değildir — onu göremez, hızlandıramaz. Kontrol 10 `PAYMENT_RECEIVED`'da durur ve `delivery_timeout_minutes` dolduğunda tarama turu **ne gördüğüne göre** davranır (§H.2): item satıcının envanterinde duruyorsa iptal + alıcıya iade + satıcıya kusur (03 §4.4); envanterinden düşmüş ama alıcıda belirmemişse iptal **etmez**, dispute'a yükseltir (02 §9.2). İkisi de provanın happy path'ini bitirir. Custodial modelde bu riskin ağır tarafı alıcıydı; P2P'de **satıcıdır**.

  Her iki prova hesabının da MA yaşını **baştan** kontrol edin — hold ortaya çıktıktan sonra provayı kurtaran bir yol yoktur.

  **MA'nın 7 günü dolmadan prova BAŞLATILAMAZ — ve bu, bekleyerek çözülür, kısayolla değil (2026-08-29'da ölçüldü).** Alıcı kapıları `User.MobileAuthenticatorVerified` bayrağını **okumuyor**; `TransactionAcceptanceService.cs:209` bunu yorumunda açıkça yazıyor (*"Live probe, not the persisted `User.MobileAuthenticatorVerified` flag"*), `TransactionReadinessService` de aynı canlı probe'u kullanıyor. Dolayısıyla **bayrağı veritabanında elle `1` yapmak kapıyı AÇMAZ** — denendi, işe yaramadı, geri alındı. Bayrak yalnız **satıcı** kapısını (`TransactionEligibilityService`) besler. Beklenecek tarih Steam'in verdiği `escrow_end_date` **değildir**: o alan *şimdi başlatılacak bir takasın* bitişidir (ölçümde MA 4 günlükken 15 gün sonrası), MA'nın geçerli olacağı tarih ise **MA'nın etkinleştirildiği gün + 7**. İkisi karıştırılırsa prova gereksiz yere ~2 hafta ertelenir.

- **Alıcının envanteri okunamıyorsa prova DURMAZ — CS2 kurmayın (2026-08-29'da ölçüldü).** `TransactionReadinessService` Stage 6 teslimat referansını **bilerek non-blocking** tutuyor (*"Blocking here would punish both parties for the buyer's privacy setting"*, 03 §2.3 adım 3). Sonucu: kanıt yolu kapanır ve teslimat yalnız **alıcı onayıyla** ilerler (02 §9.2) — ki §G.4 kontrol 10 zaten o yoldan geçiyor. Prova hesabına yalnız envanter açılsın diye ~30 GB CS2 indirmek **gereksizdir**. **401'i teşhis ederken:** `730/2` 401 verip `753/6` 200 veriyorsa sebep gizlilik değil, o hesapta **CS2 envanterinin hiç olmamasıdır** (oyun bir kez bile çalıştırılmamış); gizlilik kapalıysa ikisi de 401 verir.
- **⚠️ PROVA SONRASI GERİ ALINACAK: `auth.min_steam_account_age_days` 30 → 1 (2026-08-29'da değiştirildi).** Prova hesabının Steam hesabı 4 günlüktü ve **giriş kapısında** duruyordu, bu yüzden eşik admin API üzerinden `1`'e çekildi (audit kaydı var). **Bu bir prova ayarıdır ve unutulursa yaş kapısı üretimde fiilen açık kalır** — 30 günlük eşik, taze hesapla dolandırıcılığın ilk bariyeridir (02 §14.3). Prova biter bitmez AD9'dan **30**'a geri alın ve DB'den teyit edin. Bu satır, değişikliğin hiçbir yerde yazılı olmadığı fark edildiği için eklendi; ölçülmeyen bir geri alma, yapılmamış bir geri almadır.

- **Prova öncesi kod/imaj kontrol listesi ([#306](https://github.com/turkerurganci/Skinora/pull/306) sonrası).** Provanın üç adımı, kodun kendisi düzeltilmeden geçilemiyordu; düzeltmeler merge edildi ama **çalışan imajlara ancak yeniden kurulumla** girer:
  1. `.env` → `TRONSCAN_TX_BASE_URL=https://nile.tronscan.org/#/transaction/`. Boş bırakılırsa ekrandaki her işlem hash'i **mainnet** gezginine gider ve provada hiçbir şey bulunmaz. **Build-time değer** — sonradan değiştirmek yeni bir frontend imajı gerektirir.
  2. `StablecoinContracts__Usdt` ek giriş **istemez** — mevcut `TRON_USDT_CONTRACT`'tan besleniyor. Ama backend imajı yeniden kurulmadan düzeltme devreye girmez ve **eski imaj ödemeyi sessizce otomatik iadeye gönderir**.
  3. `docker compose -f docker-compose.yml up -d --build --wait` → ardından **mutlaka** `docker compose restart skinora-reverse-proxy` (bir alttaki bayat upstream IP tuzağı).
  4. Doğrulama, "healthy" değil **davranış**: `GET /api/v1/transactions/params` → `paymentTimeout.minHours` **0 DEĞİL** (0 görüyorsanız eski imaj çalışıyor).

- **nginx'in bayat upstream IP'si — yeniden kurduktan sonra reverse proxy'yi yeniden başlatın (2026-08-23'te ölçüldü).** `docker compose up -d --build` backend ve frontend container'larını **yeniden yaratır** ve yeni IP alırlar; `skinora-reverse-proxy` değişmediği için **yeniden başlatılmaz** ve eski IP'leri tutmaya devam eder. Sebep [`nginx/nginx.conf:29-34`](../nginx/nginx.conf#L29-L34)'te: `upstream` blokları hostname taşıyor (`skinora-backend:5000`, `skinora-frontend:3000`) ve dosyada **`resolver` direktifi yok** — nginx bu adları config yüklenirken **bir kez** çözer ve süreç ömrü boyunca saklar.

  **Tuzağı tehlikeli yapan şey belirtisi:** `docker compose ps` **11/11 `healthy`** der, `--wait` başarıyla döner, backend ve frontend kendi portlarında (`:5000`, `:3000`) doğru cevap verir — ama tek giriş kapısı olan `:8080` **502** döndürür. nginx'in kendi healthcheck'i bunu göremez, çünkü `docker-compose.yml`'de `wget http://127.0.0.1:80/nginx-health` olarak tanımlıdır: **kendi kendine** bakan, upstream'e hiç dokunmayan bir kontrol. Yani container "sağlıklı"lığını yapısal olarak upstream'den bağımsız ilan eder.

  **Çözüm:** `docker compose -f docker-compose.yml restart skinora-reverse-proxy`. **Teşhis:** `docker logs skinora-reverse-proxy` içinde `connect() failed (111: Connection refused) while connecting to upstream, upstream: "http://172.20.0.X:5000/..."` satırındaki IP ile `docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' skinora-backend` çıktısını karşılaştırın; tutmuyorsa tuzak budur.
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

---

## H. Launch checklist — teslimat kanıtı doğrulama kapısı (T125)

> **Sahiplik:** T125 (11 §P3 kabul kriteri) · **Kaynak ölçüm:** [`INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md`](INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md) §7 · **İş kuralı:** 02 §9.2

**Kapı bu:** `delivery.inventory_evidence_auto_release_enabled` (bool, seed default **`false`**). Bu ayar `false` iken envanter kanıtına dayalı **otomatik para bırakma açılmaz**.

### H.1 Neden bir kapı var

T122'nin canlı Steam ölçümü, iki gerçek hesap arasında **trade yapılamadığı** için üç bilinmeyeni kapatamadı (runbook §7):

| # | Bilinmeyen | T125'e etkisi |
|---|---|---|
| B1 | Kabul → item'ın alıcı envanterinde görünmesi arasındaki gecikme | Kanıtın ne zaman aranacağını belirler; mantığı değil **sabiti** etkiler |
| B2 | `assetid`'nin trade'de gerçekten değişmesi | Alıcı tarafında sınıf bazlı eşleşmenin gerekçesi (ikincil kaynak var, ölçüm yok) |
| B3 | `Item Certificate`'in trade'i hayatta kalması | İleride daha güçlü bir eşleştirme anahtarı olabilir; bugün kullanılmıyor |

Proje sahibi kararı (2026-08-13): manuel spike yerine **ölçüm üretimden gelsin**. İlk gerçek teslimatlarda platformun ne gördüğü kaydedilir, bir insan okur, kapı **ondan sonra** açılır.

### H.2 Kapı kapalıyken ne olur / ne olmaz

| Yol | Kapı kapalı (`false`) |
|---|---|
| Alıcı "teslim aldım" onayı | **Etkilenmez.** Onay alıcının kendi aleyhinedir (parası satıcıya gider), platformun çıkarımı değildir — normal akar (02 §9.2). |
| `SELLER_ASSET_GONE ∧ INVENTORY_DELTA` | Kanıt üretilir, `DeliveryEvidenceCaptures`'a yazılır, verdict `InventoryEvidencePendingReview` olur. **Para otomatik hareket etmez.** İşlem iptal de **edilmez** — kanıt item'ın ulaştığını söylüyor. |
| Yanlış-teslimat imzası (`SELLER_ASSET_GONE`, delta yok) | **Etkilenmez.** Bu bir para hareketi değil, admin'e yükseltmedir (02 §10.1) — kapı bastırmaz. |
| Okunamayan envanter (private/unavailable) | Kapıdan bağımsız `Inconclusive`. Bilgi yokluğu asla olumsuz bulgu sayılmaz (08 §2.3). |
| Alıcının açtığı **teslim itirazı** (T130) | Aynı tur taze koşar. `InventoryEvidencePendingReview` → dispute **OPEN kalır ve eskale edilebilir**, alıcıya "teslimat kanıtı bulundu, inceleniyor" denir (03 §6.2 Sonuç E). "Teslim edildi" diye **kapatılmaz** — kapatılırsa otomatik yol kapalı, elle yol da kapalı olur ve alıcının parasının çıkışı kalmaz. |

> **Teslimat timeout'u da bu tabloya uyar (T127, 2026-08-15).** `DeliveryDeadline` dolduğunda scanner
> aynı turu çalıştırır ve **yalnız turun sonucuna** göre davranır: `Delivered` → `ITEM_DELIVERED`;
> `InventoryEvidencePendingReview` → **iptal etmez, teslim de etmez** (satır burada birikir, bu bölümün
> incelemesi tam olarak o satırları okur); yanlış-teslimat imzası → dispute'a yükseltir; okunamayan
> envanter → hiçbir şey yapmaz, sonraki taramada tekrar dener. İptal **tek bir** olumlu kanıtla üretilir:
> satıcının envanteri okunabildi **ve** item hâlâ orada. Kapı kapalıyken teslimatı doğrulanmış bir işlemin
> parası bu yüzden emanette bekler — bekleme, kapının kabul edilmiş maliyetidir (§H.3 ile kapatılır).

> **Biriken satırların tarama maliyeti (T127 düzeltme turu, 2026-08-15).** Kapıda bekleyen satırlar
> sorgudan çıkmadığı için tarama penceresi **deadline sırasına göre değil**, `Transaction.DeliveryRoundAt`
> ("bu satıra en son ne zaman bakıldı", NULL'lar önce) sırasına göre doldurulur; bir satır
> `Timeouts:DeliveryRoundRecheckSeconds` (varsayılan **900 sn**) geçmeden pencereye geri giremez.
> Operasyonel sonucu: (1) süresi yeni dolan bir teslimat, kaç satır birikmiş olursa olsun **ilk taramada**
> incelenir; (2) biriken satırların her biri saatte ~4 kez yeniden değerlendirilir — okunamayan bir envanter
> okunur hâle geldiğinde en geç bir saat içinde yakalanır; (3) `DeliveryEvidenceCaptures`'a yazılan satır
> sayısı da bu ritme bağlıdır, §H.3'ün sorgusu aynı işleme ait birden çok gözlem satırı görebilir —
> `ORDER BY ObservedAt` ile en güncel olan okunur.

### H.3 Kapıyı açma adımları

1. **İlk N gerçek teslimatı topla.** N'yi deploy sahibi belirler; öneri **≥ 5** ayrı işlem (farklı satıcı/alıcı çiftleri, en az biri alıcının o skinden zaten kopyası olduğu vaka). Kayıtlar:
   ```sql
   SELECT Id, TransactionId, ObservedAt, Verdict, Evidence, Payload
   FROM   DeliveryEvidenceCaptures
   WHERE  AutoReleaseGated = 1
   ORDER  BY ObservedAt;
   ```
2. **Her satırı insan incele.** `Payload` JSON'unda cevaplanacaklar:
   - **B1** — `ObservedAt` − `PaymentReceivedAt` farkı: teslimat gerçekten ne kadar sürüyor? Bu sayı `delivery_timeout_minutes`'in (§A #6) launch değerini daraltmanın tek meşru dayanağıdır.
   - **B2** — `SellerItemAssetId` ile `NewAssetIds` farklı mı? Farklıysa asset ID rotasyonu ölçümle teyit edilmiş olur.
   - **B3** — satıcı tarafındaki `SellerAssetProperties` ile alıcı tarafındaki `ObservedAssets[].Properties` içindeki `Item Certificate` aynı mı?
   - **Yanlış pozitif taraması** — kanıtın gerçekten bu işlemin teslimatını anlattığından emin ol (`BaselineClassCount` → `ObservedClassCount` artışı, `BaselineAssetIdsTruncated = false`).
3. **Bulguları yaz.** İnceleme sonucu `INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md` §7'ye işlenir (B1–B3 "ölçülemedi" satırları kapatılır veya revize edilir).
4. **Kapıyı aç.** Admin UI → Ayarlar → *Teslimat Doğrulama* → `Envanter kanıtıyla otomatik teslimat onayı` = `true`.
   > `SKINORA_SETTING_*` env ile **açılamaz**: satır `IsConfigured = true` olarak seed edilir ve `SettingsBootstrapService` configured bir satırı asla override etmez (06 §8.9). Bu bilinçlidir — kapı bir insan kararıdır, bir deploy değişkeni değil.
5. **Geri alınabilir.** Şüpheli bir vaka görülürse aynı ayar `false` yapılır; kanıt toplanmaya devam eder, otomatik bırakma durur.

### H.4 Kapı açılmadan yapılmaması gerekenler

- `delivery_timeout_minutes`'i (§A #6) düşürmek. T127'den beri süre dolması gerçekten iptal üretebiliyor, dolayısıyla ölçüm gelmeden daraltmak teslim etmiş satıcıyı haksız iptale sokar.
- `DeliveryEvidenceCaptures` satırlarını silmek/düzenlemek. Tablo append-only'dir (06 §4.2); UPDATE/DELETE uygulama katmanında reddedilir.

---

## I. Launch checklist — mutabakat geri alma kapısı (T129)

### I.1 Neden ikinci bir kapı var

Teslimat kapısı (§H) "item geldi mi?" sorusunun otomatik cevabını tutar; bu kapı "**trade geri mi alındı?**" sorusununkini tutar. İkisi ayrı sorulardır ve ikinci sorunun yanlış cevabı daha pahalıdır: yanlış "geri alındı" kararı alıcıya tam iade yapar, satıcıya `DELIVERY_REVERSED` fraud flag'i koyar ve item'ı kimsede bırakmaz. T122 gerçek bir geri almayı **ölçemedi** (runbook §7), dolayısıyla imza — "item alıcıdan gitti, satıcının orijinal asset'i geri döndü" — doğrulanmamış bir çıkarımdır.

Kapı kapalıyken imza **kaybolmaz**: işlem `ITEM_DELIVERED`'da parkta kalır, `SettlementEscalatedAt` + `SettlementEscalationReason` damgalanır ve admin'lere `ADMIN_ESCALATION` bildirimi gider. İmza **yapışkandır**: ayrılmayı gözlemlemiş bir eskalasyon (`SETTLEMENT_AMBIGUOUS_DEPARTURE` / `SETTLEMENT_REVERSAL_GATED`) açıkken sonraki tur "item duruyor" dese bile kontrol `SettlementVerifiedAt` damgalamaz — sayım rotası, alıcı aynı skinden başka kopya edindiği anda "duruyor" der ve o okuma parayı admin haberdar edilmeden serbest bırakırdı.

Yapışkanlık gerekçe kolonunun **değerine** bağlı olduğu için gerekçe de korunur: kayıtlı kod **yalnız güçlendirilebilir** (`UNREADABLE` = `NO_DELIVERY_REFERENCE` < `AMBIGUOUS_DEPARTURE` < `REVERSAL_GATED`), düşürülemez. Operasyonel sonucu: **§I.3'ün ilk sorgusunda gördüğünüz gerekçe, o işlem için gözlenmiş en güçlü bulgudur** — sonraki turların zayıf okumaları onu değiştirmez. Bu, kapı açma kararının kanıt sayımını korur: sıra olmadan, satıcı geri dönen item'ı devrettiğinde bir sonraki tur `AMBIGUOUS_DEPARTURE` okuyup `REVERSAL_GATED` kaydını siliyordu ve kuyruk "hiç gerçek geri alma gözlenmedi" gibi görünüyordu; alıcı envanterini gizlediğinde ise gerekçe `UNREADABLE`'a düşüyor, ardından ilk "item duruyor" okuması parayı açık eskalasyonun üstünden serbest bırakıyordu (T129 ikinci düzeltme turu, bulgu B4).

> **Admin'in iki kolu ayrı sonuçlar üretir — karıştırmayın (T129 düzeltme turu).** `admin_resolve_refund` (AD29) **alıcı lehine** karardır: yalnız ESCALATED bir dispute üzerinden ateşlenir, dispute'u yalnız alıcı açabilir, `DeliveryReversedAt` **yazmaz** — dolayısıyla ne satıcının itibar paydasına girer (06 §3.1) ne de `DELIVERY_REVERSED` fraud flag'i yazılır. **Satıcı lehine** karar için ayrı bir kol vardır: AD32 `POST /admin/transactions/:id/clear-settlement` (yetki `MANAGE_DISPUTES`), dispute gerektirmez ve `SettlementVerifiedAt` + `SettlementClearedByAdminId` damgalayarak payout'u açar. Bir vaka her iki kolla da "aynı sonuca" varmaz; hangi tarafın lehine karar verildiği kolun kendisidir.

### I.2 Kapı kapalıyken ne olur / ne olmaz

| Durum | Kapı kapalı (`false`) | Kapı açık (`true`) |
|---|---|---|
| Item hâlâ alıcıda | `SettlementVerifiedAt` damgalanır → payout + sweep akar | Aynı |
| Item gitti, satıcıda geri belirdi **ve teslimatta satıcıdan ayrıldığı gözlenmişti** | Admin'e eskale (`SETTLEMENT_REVERSAL_GATED`), para parkta | `delivery_reversed` → REFUNDED + alıcıya iade + satıcıya fraud flag |
| Item gitti, satıcıda görünüyor ama **ayrıldığı hiç gözlenmedi** | Admin'e eskale (`SETTLEMENT_AMBIGUOUS_DEPARTURE`) | Admin'e eskale (kapıdan bağımsız) |
| Item gitti, satıcıda görünmüyor | Admin'e eskale (`SETTLEMENT_AMBIGUOUS_DEPARTURE`) | Admin'e eskale (kapıdan bağımsız — ayrım kanıtlanamıyor) |
| Envanter okunamıyor | Eşik dolunca admin'e eskale (`SETTLEMENT_UNREADABLE`) | Aynı |
| Kontrolün karar girdisi hiç üretilememiş | **İlk turda** admin'e eskale (`SETTLEMENT_NO_DELIVERY_REFERENCE`) — §I.5 | Aynı (kapıdan bağımsız) |

> **Launch öncesi nakit akışı kontrolü — sıcak cüzdan işletme bakiyesi (T129 ikinci düzeltme turu, bulgu N9).** Bu kapının kendisinden bağımsız olarak, mutabakat kontrolü hem ödemenin hem **süpürmenin** kapısıdır (05 §3.3 "Sweep tetikleyicisi"; `SweepQueueJob` de `SettlementVerifiedAt NOT NULL ∧ DeliveryReversedAt NULL` çiftini okur). T129 öncesi süpürme `ITEM_DELIVERED`'da (T+0), ödeme `PayoutEligibleAt`'te (T+8g) çalışıyordu; yani sıcak cüzdan ilgili depoziti ödeme çıkışından günler önce görüyor ve bu gecikme onu kendiliğinden fonluyordu. Artık iki kapı aynı ana açılıyor: iki job birbirini beklemez, dolayısıyla ödeme karşılık gelen depozitle aynı pencerede ısmarlanır ve ön fonlama ortadan kalkar. **Yapılacak:** launch öncesi sıcak cüzdanın işletme bakiyesi beklenen günlük ödeme hacmine göre boyutlandırılır ve `hot_wallet_limit` (§A satır 18) buna göre ayarlanır — limit, ödeme kuyruğunun aynı pencerede ihtiyaç duyduğu bakiyenin altında kalırsa alert deploy'un ilk gününden itibaren gürültü üretir. Kapının kendisi doğrudur ve değiştirilmemelidir — depozit, geri alma tespit edilirse iadenin çekilebileceği yerde kalmalıdır (02 §4.5.1 "Bilinen sonuçları"). Süpürme başarısız olursa fallback zaten depozit adresinden doğrudan gönderim yapar (05 §3.3 "Sweep hatası").

Son dört satır kapının **dışındadır**. Alıcının item'ı başkasına devretmesi ile geri alma tek taraflı okumada aynı görünür ve Steam'in 7 günlük kısıtı 8 günlük pencerenin bir gün öncesinde biter, yani devir meşru bir ihtimaldir. Üçüncü satır aynı kuralın satıcı tarafındaki karşılığıdır: 02 §4.5.1 item'ın satıcıya **dönmesini** arar, oysa alıcı onayıyla kapanan bir teslimatta platform satıcı envanterini hiç okumaz — satıcı aynı sınıftan başka bir kopya göndermiş olabilir (§9.2 sayım kuralı bunu geçerli teslimat sayar) ve orijinal asset'i yerinde durur. Bunu geri alma saymak dürüst satıcıyı cezalandırırdı, o yüzden imza artık **ayrılmanın gözlenmiş olmasını** da şart koşar (`DeliveryEvidence.SELLER_ASSET_GONE`).

### I.3 Kapıyı açma adımları

1. **En az bir gerçek geri alma vakası gözlenmiş olmalı.** Eskale edilmiş işlemler:
   ```sql
   SELECT Id, SellerId, BuyerId, ItemDeliveredAt, PayoutEligibleAt,
          SettlementCheckedAt, SettlementEscalatedAt, SettlementEscalationReason,
          SettlementVerifiedAt, SettlementClearedByAdminId, DeliveryReversedAt
   FROM   Transactions
   WHERE  SettlementEscalatedAt IS NOT NULL
   ORDER  BY SettlementEscalationReason, SettlementEscalatedAt;
   ```
   Son üç kolon **kapalı** vakaları ayırt etmek içindir: `SettlementVerifiedAt` doluysa mutabakat kapanmıştır (`SettlementClearedByAdminId` doluysa kararı bir admin verdi, boşsa kontrol kendi sonuçlandı), `DeliveryReversedAt` doluysa geri alma uygulanmıştır. `SettlementEscalationReason` ile gruplayın — `SETTLEMENT_NO_DELIVERY_REFERENCE` satırları bu kapıyla ilgili **değildir**, prosedürleri §I.5'tedir.
2. **Her vakayı insan incele:** Steam trade geçmişi gerçekten bir rollback gösteriyor mu, yoksa alıcı item'ı mı devretti? Sonuç `INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md` §7'ye işlenir — geri alma sonrası asset ID'nin korunup korunmadığı bu incelemede öğrenilir ve servisin imzası ona göre daraltılabilir.
3. **Karar veren kolu doğru seç.** Satıcı lehine kapatma AD32 (`clear-settlement`); alıcı lehine iade AD29 (`admin_resolve_refund`, dispute gerektirir). §I.1'deki uyarıya bakın — ikisi aynı sonucu üretmez.
4. **Kapıyı aç.** Admin UI → Ayarlar → *Mutabakat* → `Geri alma tespitinde otomatik iade` = `true`. Env ile açılamaz (§C'deki aynı gerekçe).
5. **Geri alınabilir.** Şüpheli bir vaka görülürse `false` yapılır; tespit ve eskalasyon sürer, otomatik iade durur.

### I.4 Kapı açılmadan yapılmaması gerekenler

- `payout_settlement_days`'i 7'ye indirmek. Validator 7'nin altını reddeder ama tam 7 de marjsızdır: kontrol penceresi Steam'in geri alma penceresiyle tam örtüşür ve saat farkı kadar bir açık bırakır.
- Eskale edilmiş işlemi **veritabanında** `SettlementVerifiedAt` elle damgalayarak "çözmek". O damga payout'u serbest bırakır ve `COMPLETED` guard'ını açar, ama ne audit satırı ne history satırı ne de kararı verenin kaydı kalır. Satıcı lehine karar için **AD32** (`POST /admin/transactions/:id/clear-settlement`) kullanılır: aynı damgayı atar, üstüne `SettlementClearedByAdminId` + `SETTLEMENT_CLEARED_ADMIN` audit satırı + `AdminClearSettlement` history satırı yazar ve en az 10 karakterlik gerekçe ister. Alıcı lehine karar dispute üzerinden AD29'dur.

### I.5 Karar girdisi üretilememiş vakalar (`SETTLEMENT_NO_DELIVERY_REFERENCE`)

Bu sınıf kapıyla ilgili **değildir** ve §I.3'ün "Steam trade geçmişi rollback gösteriyor mu" triyajı buna **uymaz** — ortada bir imza yoktur, ölçülecek referans hiç doğmamıştır.

**Nasıl oluşur:** alıcının envanteri `SELLER_CONFIRMED` anında gizliyse baseline bilinçli olarak NULL bırakılır (03 §2.3 — gizli envanter işlemi durdurmaz, yalnız kanıt yolunu kapatır) ve teslimat alıcı onayıyla kapanırsa envanter hiç okunmadığı için `DeliveredBuyerAssetId` de yazılmaz. Mutabakat kontrolünün iki rotası da girdisiz kalır ve **iki kolon da ITEM_DELIVERED'dan sonra hiçbir yolla dolmaz**. Bu yüzden bu sınıf eşiği beklemez: ilk turda eskale edilir, çünkü tekrar denemenin kazanacağı bir şey yoktur.

**Triyaj:**

```sql
SELECT Id, SellerId, BuyerId, ItemName, ItemDeliveredAt, PayoutEligibleAt, SettlementEscalatedAt
FROM   Transactions
WHERE  SettlementEscalationReason = 'SETTLEMENT_NO_DELIVERY_REFERENCE'
  AND  SettlementVerifiedAt IS NULL
  AND  DeliveryReversedAt IS NULL
ORDER  BY SettlementEscalatedAt;
```

1. **Alıcının teslimatı onayladığını doğrula.** `BuyerConfirmedReceiptAt` dolu ve `DeliveryEvidence` `BUYER_CONFIRMED` içeriyorsa alıcı item'ı aldığını kendisi beyan etmiştir; bu, platformun elindeki tek kanıttır ve 02 §9.2 onu yeterli sayar.
2. **Alıcı tarafında açık bir şikâyet olmadığını doğrula.** Dispute varsa AD29 yolu işler; AD32 zaten aktif dispute'ta reddeder.
3. **Satıcı lehine kapat.** AD32 → `POST /admin/transactions/:id/clear-settlement`, gerekçe alanına neye bakıldığı yazılır. Payout bir sonraki `SellerPayoutQueueJob` turunda kuyruğa girer.
4. **Alıcı sonradan geri alma bildirirse** olağan yol dispute'tur (03 §8.8); AD32 kararı `SETTLEMENT_CLEARED_ADMIN` audit satırıyla izlenebilir durumdadır.

**Hacim beklentisi:** bu sınıf gizli envanterli alıcılarla sınırlıdır. Kuyruk şişerse çare admin sayısı değil, alıcı envanterini `SELLER_CONFIRMED` anında okunabilir hâle getiren ürün tarafı düzeltmesidir — kayıt: `Docs/DEFERRED_BACKLOG.md`.
