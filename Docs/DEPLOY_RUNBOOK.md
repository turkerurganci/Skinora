# Skinora — Deploy Runbook

**Oluşturma:** WP14 (2026-06-19) · **Kapsam:** Production deploy öncesi sağlanması zorunlu/önerilen environment değişkenleri + sidecar config parity + runtime-tunable ayar davranışı.

> Bu runbook, "uygulama prod'da açılması için neyin set edilmesi gerekir?" sorusunun tek doğru kaynağıdır. `06_DATA_MODEL §3.17` (SystemSetting kataloğu) ve `08_INTEGRATION_SPEC` (sidecar env) ile tutarlıdır. Değer kaynakları: backend `SystemSettingSeed.cs` (59 satır), `SettingsBootstrapService` (06 §8.9 fail-fast), sidecar `config/index.ts`.

---

## 0. Hızlı özet

| Katman | Zorunluluk | Davranış |
|---|---|---|
| **A. Zorunlu SystemSetting (19)** | **Prod açılışı için ZORUNLU** | Eksikse `SettingsBootstrapService` startup'ta **fail-fast** eder (06 §8.9). Bilinçli güvenlik — iş-kritik değere yanlış default sessizce prod'a kaçmaz. |
| **B. Operasyonel secret/altyapı** | **ZORUNLU** | DB / JWT / wallet / webhook / internal key olmadan servis açılmaz veya kör çalışır. |
| **C. Production'da önerilen** | Önerilir | `NONE` default ile açılır ama ilgili kapsam (reconciliation, geo-block) çalışmaz. |
| **D. Sidecar parity (cadence/sweep)** | Sidecar env'i otoriter | Backend DB kopyası admin-görünür; **runtime'a yansımaz** — sidecar env değişimi + sidecar restart gerekir. |
| **E. Runtime-tunable** | — | Admin UI'dan değişir; cron'lar **restart'sız** re-register olur (WP14), gas/retry her çalıştırmada taze okunur. |

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
| `STEAM_API_KEY` | steam sidecar | Steam Web API |
| `HD_WALLET_MNEMONIC` | blockchain sidecar | Deposit adresi türetme (08 §3.2) |
| `HOT_WALLET_ADDRESS` / `HOT_WALLET_PRIVATE_KEY` | blockchain sidecar | Payout/refund/sweep imzası + sweeper Energy delegation (Docker secret olarak mount, 05 §3.3/§3.5) |
| `TRON_NETWORK` (+ `TRON_*_CONTRACT` testnet'te) | blockchain sidecar | mainnet/nile/shasta + token kontratları (08 §3.3) |
| `TRON_API_KEY` (+ `TRON_API_KEY_SECONDARY`) | blockchain sidecar | TronGrid rate-limit + failover (WP10) |
| `TELEGRAM_BOT_TOKEN` / `TELEGRAM_CHAT_ID` | monitoring | Alert kanalı |

---

## C. Production'da ayarlanması önerilen SystemSetting'ler

Seed default'u `NONE`/varsayılan ile açılır ama set edilmezse ilgili kapsam **çalışmaz** (warn log ile atlanır, fail-fast DEĞİL). Admin UI'dan veya `SKINORA_SETTING_*` env ile set edilir.

| Key | Default | Set edilmezse |
|---|---|---|
| `reconciliation.hot_wallet_address` | NONE | Günlük reconciliation hot wallet kapsamı atlanır (warn) — 05 §3.3 |
| `reconciliation.cold_wallet_address` | NONE | Cold wallet reconciliation kapsamı atlanır (info) |
| `auth.banned_countries` | NONE | Geo-block uygulanmaz (02 §21.1) |
| `multi_account.exchange_addresses` | NONE | Çoklu-hesap kontrolünde exchange adres allowlist'i boş |

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
