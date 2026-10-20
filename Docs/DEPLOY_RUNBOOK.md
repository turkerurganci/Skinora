# Skinora — Deploy Runbook

**Oluşturma:** WP14 (2026-06-19) · **Son güncelleme:** 2026-10-19 — T158 Ethereum kurulumu — yeni §J (kurulum sırası, açılış, alarm tablosu, uzun kesinti, yanlış ağ kurtarma). · **Kapsam:** Production deploy öncesi sağlanması zorunlu/önerilen environment değişkenleri + sidecar config parity + runtime-tunable ayar davranışı. · Değişiklik geçmişi: [DOC_CHANGELOG.md](DOC_CHANGELOG.md#deploy_runbookmd)

> Bu runbook, "uygulama prod'da açılması için neyin set edilmesi gerekir?" sorusunun tek doğru kaynağıdır. `06_DATA_MODEL §3.17` (SystemSetting kataloğu) ve `08_INTEGRATION_SPEC` (sidecar env) ile tutarlıdır. Değer kaynakları: backend `SystemSettingSeed.cs` (75 satır — T149 sonrası), `SettingsBootstrapService` (06 §8.9 fail-fast), sidecar `config/index.ts`.

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
| **J. Ethereum kurulumu ve alarmları (F8)** | Ethereum açılmadan ZORUNLU | Sırlar, RPC, fiyat kaynağı probu, cüzdan doldurma, açılış adımı (`ETHEREUM_SIDECAR_BASE_URL`), her Ethereum alarmının anlamı ve yordamı, uzun kesinti ve yanlış EVM ağı kurtarması (T158). |
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
| 7 | `SKINORA_SETTING_MIN_TRANSACTION_AMOUNT` | min_transaction_amount | decimal | 5.0 | Minimum işlem tutarı (USDT). **1 önerilmez:** sıcak cüzdanın ödeme enerjisi bittiğinde bir payout 6,43–13,03 TRX yakar (TRX 0,34 $ ile ~2,2–4,4 USDT); fiyatı bunun altındaki satışta satıcıya kalan tutar sıfırın altına düşer ve ödeme ertelenir (02 §4.7). Ucuz satışları açık tutup tutmamak iş kararıdır, kod zorlamaz |
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
| `STEAM_SIDECAR_LIMITED_ACCOUNT_SAMPLES` (+ `_SAMPLE_DELAY_MS`, `_DEADLINE_MS`, `_CACHE_TTL_SECONDS`) | steam sidecar | Limited-account probunun örnekleme ayarları (08 §2.2a). Aynı Community kotasını harcar. `0` (kısıtlı değil) ancak bu kadar **ardışık** örnek aynı sonucu verirse kabul edilir; `1` tek okumada bağlayıcıdır. Boş → **3 örnek / 3 sn ara / 20 sn üst sınır / 24 sa temiz-önbellek**. Varsayılan 3, §G.5'teki insan yordamının **5**'inin altındadır ve bilerek öyledir: oradaki kural tek seferlik koşar, buradaki her soğuk kullanıcı için örnek sayısı kadar Community isteği harcar. Gerçek IP limiti ölçüldüğünde **rebuild'siz** yükseltilir — yalnız sidecar restart gerekir. `_DEADLINE_MS` backend istemcisinin 30 sn'lik timeout'unun **altında** kalmalı, yoksa arıza kodlu 503 yerine "backend timeout" olarak görünür |
| `HD_WALLET_MNEMONIC` | blockchain sidecar | Deposit adresi türetme (08 §3.2) |
| `TRON_USDT_CONTRACT` / `TRON_USDC_CONTRACT` | blockchain sidecar **+ backend** | **Yalnız testnet'te (nile/shasta) zorunlu** — mainnet adresleri koda gömülü. Boşsa desteklenen-token allowlist'i boş kalır ve gelen her transfer wrong/spam token sayılır (08 §3.3). **Aynı iki değer backend'e de `StablecoinContracts__Usdt` / `__Usdc` olarak geçer** (`docker-compose.yml`); ikisi ayrışırsa ödeme sessizce ölür — backend izleyiciyi `expectedContract` ile kurar, sidecar geleni ona göre sınıflar, ve eşleşmeyen **doğru** bir ödeme `wrong_token` sayılıp otomatik iadeye gider |
| `TRONSCAN_TX_BASE_URL` | frontend (**build-time**) | İşlem hash'lerinin bağlandığı blok gezgini. `NEXT_PUBLIC_*` derleme anında bundle'a gömülür, bu yüzden `build.args` ile geçer — **değiştirmek frontend imajının yeniden kurulmasını gerektirir**. Boş = mainnet `tronscan.org`. Nile'da `https://nile.tronscan.org/#/transaction/` |
| `ETHERSCAN_TX_BASE_URL` *(T156)* | frontend (**build-time**) | Ethereum işlem hash'lerinin bağlandığı gezgin; kural `TRONSCAN_TX_BASE_URL` ile aynı (derleme anında gömülür, değiştirmek yeni frontend imajı ister). Boş = mainnet `etherscan.io`. Sepolia'da `https://sepolia.etherscan.io/tx/`. T156'dan önce boş bırakılan Tronscan değeri istemcide boş metin olarak kalıyordu (`??`); artık boş değer de mainnet'e düşer |
| `HOT_WALLET_ADDRESS` | blockchain sidecar **+ backend** | Platformun sıcak cüzdan adresi. Sidecar sweep hedefini bununla karşılaştırır ve başka adrese imzalamaz; backend aynı değeri `IPlatformWalletAddressProvider` üzerinden okur (sweep `ToAddress`, reconciliation kapsamı, hot wallet monitörü). **Tek kaynak `.env`** — ikisi ayrışırsa sweep `DESTINATION_NOT_ALLOWED` ile durur. Değiştirmek iki servisin de yeniden başlatılmasını gerektirir (05 §3.3, owner kararı 2026-09-16) |
| `COLD_WALLET_ADDRESS` | blockchain sidecar **+ backend** | Soğuk cüzdan adresi, aynı sabitleme kuralı. Boş bırakılırsa hot→cold konsolidasyon **kapalıdır** (AD20 `COLD_WALLET_NOT_CONFIGURED`) ve reconciliation'ın cold kapsamı atlanır |
| `MAX_SINGLE_TRANSFER_USDT` / `MAX_DAILY_OUTFLOW_USDT` | blockchain sidecar | Tek gönderim ve sıcak cüzdanın 24 saatlik çıkış tavanı (ondalık USDT, 08 §3.3a). **Fail-closed: boş ya da bozuksa payout ve iade yapılmaz.** Boyutlandırma §C.3 |
| `STAKE_ACCOUNT_ADDRESS` (+ `STAKE_ACCOUNT_PERMISSION_ID`) | blockchain sidecar | Enerji kilidinin durduğu ayrı hesabın **adresi**. Owner anahtarı çevrimdışıdır ve hiçbir servise verilmez; sıcak cüzdanın anahtarı bu hesapta yalnız devretme/geri alma yetkisiyle listelidir. Boş = sıcak cüzdan kendi kilidini kullanır (eski düzen, kırılma yok). Bozuk ya da base58 olmayan adres (`41…` hex dahil) ya da 2'den küçük / tam sayı olmayan izin kimliği sidecar'ı açılışta durdurur (`STAKE_ACCOUNT_MISCONFIGURED`); açılan sidecar hangi hesaptan devredeceğini `Energy delegation:` satırında yazar. Kurulum, ödeme payının sıcak cüzdana kalıcı devretilmesi ve bant boyutlandırması §C.2. **F8 (T141): kilit uykuda — boş bırakın** (§C.2 başındaki not); T149'dan sonra `ENERGY_STAKING_ENABLED` kapalıyken bu iki değer okunmaz bile |
| `ENERGY_STAKING_ENABLED` | blockchain sidecar | **T149 — hibrit kilidin kapama anahtarı, varsayılan `false`** (compose `:-false`). Kapalıyken kilit okunmaz, devretme yapılmaz, her depozit transferi yakar; `true` yalnız proje sahibi kilidi yeniden açarsa (§C.2). Yalnız `true`/`false`/boş kabul edilir — başka değer sidecar'ı açılışta durdurur. Açılış log'unun `Energy delegation:` satırı `stakingEnabled` alanını yazar |
| `HOT_WALLET_PRIVATE_KEY` | blockchain sidecar | Payout/refund/sweep imzası + sweeper Energy delegation. **Bugün düz env değişkeni** (`docker inspect` ile görünür); Docker secret'a taşınması ve çevrimdışı yedek prosedürü owner kararı 2026-09-17 ile ayrı bir tura bırakıldı (05 §3.3/§3.5) |
| `TRON_NETWORK` (+ `TRON_*_CONTRACT` testnet'te) | blockchain sidecar | mainnet/nile/shasta + token kontratları (08 §3.3) |
| `TRON_API_KEY` (+ `TRON_API_KEY_SECONDARY`) | blockchain sidecar | TronGrid rate-limit + failover (WP10). **Aynı TronGrid hesabının anahtarları toplam kotayı paylaşabilir** (TronGrid belgesi, 2026-09-08) — ikinci anahtar bütçe ekleyecekse başka bir hesaptan alınmalıdır |
| `TRONGRID_DAILY_REQUEST_BUDGET` | blockchain sidecar | Planın günlük istek bütçesi; açılışta `skinora_blockchain_trongrid_daily_request_budget` gauge'una yazılır ve `tron-quota-projection` alarmı ölçülen istek hızını buna böler (oran > 0,8 → uyarı). Varsayılan 100.000 = 2026'da raporlanan ücretsiz plan; TronGrid bütçeyi ne yanıtta ne belgede veriyor, **gerçek planın değeri girilir**. Pozitif tam sayı değilse sidecar açılmaz. Kapasite hesabı 08 §3.4 |
| `ETHEREUM_SIDECAR_BASE_URL` / `ETHEREUM_SIDECAR_INTERNAL_KEY` | backend (+ iç anahtar Ethereum servisine) | **T148 — Ethereum servisinin adresi ve iç anahtarı (`EthereumSidecar__*`).** Servis T158 itibarıyla **tamamdır** (izleme T151, gönderim T152, yakıt T153, ücret ve kur T154). `ETHEREUM_SIDECAR_BASE_URL`'i doldurmak Ethereum'u **yeni işlemlere açar** — bu bir açılış kararıdır, varsayılan değil: §J.1'in sırası (sırlar, RPC, fiyat kaynağı probu, cüzdanlar, alarmlar) tamamlanmadan doldurmayın. Değer: `http://skinora-ethereum-sidecar:5300`. Boş = backend Ethereum'a hiçbir istek göndermez; Ethereum'a ait her çağrı "yapılamadı" döner, Ethereum gönderim satırları `PENDING` bekler (yeniden deneme hakkı harcanmaz), sağlık yoklaması Ethereum'u yoklamaz. TRON bundan etkilenmez: bekleyen Ethereum satırları TRON'un gönderim/onay/adres/sweep sırasını tutmaz (iş adayları sorguda yapılandırılmış ağlarla sınırlı — 05 §3.3a, T148 düzeltme turu 1). **Yapılandırılmış ama erişilemeyen** Ethereum servisi de TRON'u tutmaz (T150): toplu işler ağ başına ayrı parti alır, sırayla işler, bir ağın servisi ilk "erişilemez" cevabında o turu bırakır; sağlık yoklaması kesinti ilan edince o ağa hiç dokunulmaz; outbox başarısız olayı geri çekilmeyle (5 sn'den 15 dk'ya) yeniden dener. İç anahtar TRON'unkinden (`BLOCKCHAIN_SIDECAR_INTERNAL_KEY`/`INTERNAL_KEY`) **farklı** olmalı; Ethereum servisi kendi iç anahtarı olmadan ya da 32 karakterden kısa anahtarla **açılmaz** (`INTERNAL_KEY_MISSING` / `INTERNAL_KEY_TOO_SHORT`) — `INTERNAL_KEY`'e düşen bir yedek yoktur |
| **Ethereum servisi — Docker secret dosyaları** (`secrets/ethereum_hd_mnemonic.txt`, `secrets/ethereum_hot_wallet_private_key.txt`, `secrets/ethereum_fuel_wallet_private_key.txt`) | **yalnız** Ethereum servisi | **T150 — 11 §F8 K7.** Tohum (BIP-39, yol `m/44'/60'/0'/0/{index}` kök düğümden) ve iki imza anahtarı env değil **dosya** olarak, compose `secrets:` ile `/run/secrets/` altına bağlanır; başka hiçbir konteynere gitmez, TRON tohumu bu servise hiç girmez. Tohum dosyası yoksa ya da boşsa servis açılmaz (`HD_WALLET_NOT_CONFIGURED`). Anahtar dosyaları T152/T153'e kadar boş olabilir; doluysa sıcak anahtar `ETHEREUM_HOT_WALLET_ADDRESS`'i kontrol etmeli (`HOT_WALLET_ADDRESS_MISMATCH`), yakıt anahtarı sıcak anahtardan farklı olmalı ve sıcak/soğuk adresi kontrol etmemeli (`WALLET_KEYS_NOT_DISTINCT`, öneriyle kaydedildi — ⚠). Dosyalar yalnız `ethereum` profiliyle gerekir: `docker compose -f docker-compose.yml --profile ethereum up -d`. Hazırlama: [`secrets/README.md`](../secrets/README.md) |
| `ETHEREUM_NETWORK` (+ `ETHEREUM_SIDECAR_PORT`) | Ethereum servisi | `sepolia` (chainId 11155111, varsayılan) ya da `mainnet` (chainId 1) (08 §3.7.1). Sağlayıcının `eth_chainId`'si bu zincir değilse o sağlayıcı hiç kullanılmaz (log'a yazılır, yedek cevap verir) |
| `ETHEREUM_RPC_PRIMARY_URL` / `ETHEREUM_RPC_BACKUP_URL` (+ `_NAME`, `_MAX_BLOCK_RANGE`, `_MAX_ADDRESSES`) | Ethereum servisi | **T150 — birincil + yedek JSON-RPC (08 §3.7.1, öneriyle kaydedildi — ⚠: Alchemy Free birincil, Chainstack Developer yedek).** URL'ler sağlayıcının API anahtarını taşır — sır gibi davranın; log'lara yalnız sunucu adı yazılır. Birincil zorunlu (yoksa servis açılmaz: `RPC_NOT_CONFIGURED`); yedek yoksa açılır ama uyarır (yedeğe geçiş yok). `eth_getLogs` sınırları sağlayıcı başına ayardır (08 §3.7.4): blok aralığı Alchemy Free **10**, Chainstack Developer **100**; arama konumu başına en çok **1.000** adres. Sağlayıcı yine "çok büyük" derse istek yarıya bölünür. Taşıma hatası (bağlantı, zaman aşımı, 429/5xx, `-32005`, arşiv reddi) alan sağlayıcı 30 sn atlanır, diğeri cevap verir. Anahtarlı sınırlar belgeden alındı; anahtarlar gelince T142 probu bu uçlara koşulmalı (11 §F8 T150 ⚠) |
| `ETHEREUM_FINALIZED_POLL_INTERVAL_MS` / `ETHEREUM_FINALIZED_STALL_SECONDS` | Ethereum servisi | `finalized` başı 30 sn'de bir okunur (08 §3.7.4); son `finalized` bloğun zaman damgası **1.800 sn**'den (30 dk) eskiyse servis `/health`'te **503** verir (backend Ethereum'u dondurur, yeni işleme kapatır; TRON etkilenmez) ve `ethereum-finalized-stalled` alarmı çalar (08 §3.7.8). Eşik `skinora_ethereum_finalized_stall_threshold_seconds` gauge'una yazılır; alarm aynı değere böler. Backend'deki `blockchain.ethereum.finality_stall_alarm_minutes` ile parite §D |
| `ETHEREUM_MAX_SINGLE_TRANSFER_USDT` / `ETHEREUM_MAX_DAILY_OUTFLOW_USDT` (+ `ETHEREUM_FUEL_MAX_SINGLE_ETH` / `ETHEREUM_FUEL_MAX_DAILY_ETH`) | Ethereum servisi | **T152 — gönderim tavanları (08 §3.7.6, TRON'un `MAX_*` kuralının eşi; boyutlandırma §C.3).** Tek gönderim tavanı payout ve iadeye, günlük tavan sıcak cüzdanın 24 saatlik payout çıkışına uygulanır (zincirden okunur: son 7.200 blok, soğuk aktarım hariç). **Boş = o gönderim reddedilir (fail-closed);** bozuk değerle (sıfır, eksi, 6'dan fazla ondalık) servis **açılmaz** (`INVALID_CONFIG`). **Yakıt tavanları (T153) ETH cinsindendir** ve her depozit yakıtına uygulanır: tek yakıt `ETHEREUM_FUEL_MAX_SINGLE_ETH`'i, son 24 saatin yakıtı — değer **ve** yakıt işleminin kendi en kötü ücreti, servisin günlüğünden sayılır — `ETHEREUM_FUEL_MAX_DAILY_ETH`'i aşamaz. **Boş = hiç yakıt gönderilmez: Ethereum sweep'leri ve sweep öncesi iadeler `FUEL_REQUIRED` ile bekler** (satır `PENDING`, `FAILED` olmaz); tavana takılan yakıt `FUEL_LIMIT_REACHED` ile bekler. Önerilen değerler §C.3 |
| `ETHEREUM_TX_JOURNAL_DIR` + volume `skinora-ethereum-tx-journal` | Ethereum servisi | **T152 — imzalanan her işlemin kaydı (08 §3.7.6).** Compose yolu sabit verir (`/var/lib/skinora-ethereum/tx-journal`) ve adlandırılmış volume bağlar. Dizin yazılamıyorsa ya da bir kayıt okunamıyorsa servis **açılmaz** (`TX_JOURNAL_UNAVAILABLE` / `TX_JOURNAL_CORRUPT`). Çift ödemeyi imkânsız kılan kayıt budur: **silmeyin**, veritabanıyla birlikte yedekleyin (§D "Ethereum gönderim günlüğü") |
| `ETHEREUM_JOURNAL_ACKNOWLEDGED_NONCES` | Ethereum servisi | **T152 düzeltme turu 2 — varsayılan boş; yalnız §D adım 7 ile doldurulur.** Biçim `0xadres:nonce[,0xadres:nonce]`: "bu hesabın zinciri bu nonce'a kadar hesaplandı". Hesabın zincirdeki nonce'u günlüğün bildiğinin ilerisindeyse servis o hesaptan imza atmaz (`JOURNAL_BEHIND_CHAIN`); bu değer, operatör inceledikten sonra durmayı kaldırır. Bozuk değerle servis **açılmaz** (`INVALID_CONFIG`). Değer hesabın o anki zincir nonce'undan (`pending` dahil) **büyükse** (yazım hatası, ör. 10 yerine 1000) servis onu **yok sayar** — durma kalkmaz, `ethereum-journal-ack-ahead-of-chain` alarmı çalar (yönetici kararı, T152 doğrulaması). İş bitince satırı boşaltmak gerekmez (değer yalnız o nonce'a kadarını kapsar; sonraki yabancı kullanım yine durdurur) |
| `ETHEREUM_FEE_CAP_WAIT_ALARM_SECONDS` (+ `ETHEREUM_FEE_BUMP_AFTER_SECONDS`) | Ethereum servisi | **T152.** Tavan ya da ETH kuru yüzünden imzalanmamış ya da imzalanıp bloğa girmemiş bir gönderim bu kadar beklerse `ethereum-transfer-wait` alarmı (varsayılan 10.800 sn = 3 sa; 08 §3.7.7). Backend'deki `blockchain.ethereum.fee_cap_wait_alarm_minutes` ile parite §D. Sıradaki gönderim 300 sn içinde bloğa girmezse aynı nonce ile ücret artırılır |
| `ETHEREUM_FUEL_WALLET_MIN_ETH` / `ETHEREUM_HOT_WALLET_MIN_ETH` (+ `ETHEREUM_WALLET_BALANCE_INTERVAL_MS`) | Ethereum servisi | **T153 — yakıt ve sıcak cüzdanın ETH tabanı** (varsayılan **0,06** ETH, T142; 08 §3.7.5). Servis iki cüzdanın ETH bakiyesini 5 dk'da bir (300.000 ms) okur ve `skinora_ethereum_wallet_eth_balance{wallet}` ile tabanı `skinora_ethereum_wallet_eth_floor{wallet}` yan yana yayımlar; bakiye tabanın altına inince `ethereum-wallet-eth-low` alarmı (§D adım 8). Anahtarı bağlanmamış cüzdan okunmaz. Backend'deki `blockchain.ethereum.fuel_wallet_eth_balance_minimum` / `hot_wallet_eth_balance_minimum` ile parite §D. Bozuk değer (sıfır, eksi, 18'den fazla ondalık) servisi **açmaz** (`INVALID_CONFIG`) |
| `ETHEREUM_MONITOR_RESTART_LOOKBACK_BLOCKS` | Ethereum servisi | **T151.** Varsayılan **7.200** blok (≈ 24 sa). **T158'den beri yalnız başlangıç bloğu bilinmeyen (T158 öncesi ayrılmış) depozitler için geçerlidir:** backend her yeni Ethereum depozitinin türetildiği andaki `finalized` bloğu (`PaymentAddresses.MonitorFromBlock`) saklar ve her izleme başlatmasında gönderir; servis o depoziti o bloktan itibaren okur, kesinti ne kadar uzun olursa olsun (§J.3). Başlangıç bloğu olmayan depozit eski kurala bağlıdır: servis yeniden başladıktan sonraki 5 dakikada backend'in yeniden kurduğu (ya da HOLDING / post-cancel) depozit bu kadar geri okunur; taze PAYMENT depoziti bakiyesi sıfırsa geri okunmaz. Böyle depozitler varken servis bundan uzun kapalı kaldıysa değeri kesinti süresini kapsayacak kadar büyütüp servisi yeniden başlatın (08 §3.7.4 "İmleç") |
| `ETHEREUM_RECONCILIATION_INTERVAL_MS` / `ETHEREUM_RECONCILIATION_CU_PER_SECOND` / `ETHEREUM_RECONCILIATION_MAX_FINALIZED_AGE_SECONDS` | Ethereum servisi | **T151.** Saatlik (3.600.000 ms) bakiye mutabakatı; okumalar **200** CU/s ile tempolu (Alchemy Free sınırı 300 CU/s — bunu aşmayın, tarayıcıya pay kalsın); `finalized` **1.500 sn**'den (25 dk) eskiyse tur atlanır (arşiv derinliği). Alarmlar: `ethereum-deposit-balance-mismatch` (critical — bir transfer kaçmış olabilir), `ethereum-deposit-native-eth` (warning — depozite yakıt olmayan ETH geldi, otomatik iade yok), `ethereum-reconciliation-stale` (warning — 2,5 aralık tur yok), `ethereum-usdt-status` (critical — USDT ücret/durdurma/kullanımdan kaldırma anahtarı açık; servis `acceptsPayments=false` yayınlar, backend Ethereum'u yeni işlemlere kapatır) |
| `WEBHOOK_SECRET` (Ethereum servisi) / `Webhook__EthereumSharedSecret` (backend) | ikisi | **T151'den itibaren zorunlu.** Boşsa servis açılır ama ödemeyi bildiremez: `/health` `acceptsPayments: false` (`WEBHOOK_SECRET_MISSING`) ve backend Ethereum'u `supportedNetworks`'e almaz. İki taraf aynı değeri taşır, TRON'un sırrından farklı olmalı (T148) |
| `ETHEREUM_WEBHOOK_SECRET` | backend + Ethereum servisi (`WEBHOOK_SECRET`; servis T151'den itibaren imzalar) | **T148 — Ethereum servisinin kendi webhook HMAC sırrı (`Webhook__EthereumSharedSecret`).** Boş = hiçbir webhook Ethereum adına doğrulanamaz. **TRON'un `BLOCKCHAIN_WEBHOOK_SECRET`'ından farklı olmak zorunda** — aynı verilirse backend **açılmaz** (`OptionsValidationException`, mesaj iki ayarı adlandırır; T148 düzeltme turu 1). Backend, gövdedeki `network`'ü imzalayan sırrın ağıyla karşılaştırır: bir servisin sırrı başka ağın adına webhook üretemez |
| `ETHEREUM_USDT_CONTRACT` / `ETHEREUM_USDC_CONTRACT` | backend + Ethereum servisi | **T148 — Ethereum'un token kontratları (`EthereumStablecoinContracts__*`).** Boş = Ethereum mainnet USDT `0xdac17f…1ec7` / USDC `0xa0b869…eb48`. Sepolia'da T159'un kurduğu USDT kopyası yazılır; Ethereum servisinin izlediği adresle aynı olmalı (TRON'daki `TRON_*_CONTRACT` kuralının eşi) |
| `ETHEREUM_HOT_WALLET_ADDRESS` / `ETHEREUM_COLD_WALLET_ADDRESS` | backend + Ethereum servisi | **T148 — Ethereum'un sıcak/soğuk cüzdan adresi** (TRON'un `HOT/COLD_WALLET_ADDRESS` kuralının eşi; her ağın çifti ayrıdır, 05 §3.3a). `0x` + 40 hex; karışık harfli değer geçerli EIP-55 sağlaması taşımalı, yoksa backend açılmaz. Boş = Ethereum depozitleri sweep kuyruğuna girmez, Ethereum mutabakatının sıcak/soğuk kapsamı atlanır. Panelde `blockchain.ethereum.{hot,cold}_wallet_address` olarak salt okunur görünür |
| `OUTBOUND_PENDING_ROWS_ALARM_THRESHOLD` (`OutboundQueueAlarm__PendingRowsThreshold`) | backend | **T158 (a).** Bir ağda bu sayıdan fazla `PENDING` giden satır (payout, iade, sweep) 15 dk boyunca beklerse `outbound-transfer-backlog` alarmı. Varsayılan **25** (bir gönderim partisinden — ağ başına dakikada 20 satır — fazlası). Sıfır ya da eksi değer varsayılana düşer. Backend `skinora_backend_outbound_pending_rows_alarm_threshold{network}` olarak yayımlar; kural ona böler (§J.2) |
| `TELEGRAM_BOT_TOKEN` / `TELEGRAM_CHAT_ID` / `ALERT_EMAIL_TO` | monitoring (Grafana) | Alert kanalı — **ayrıca Grafana'nın açılması için zorunlu.** `contactpoints.yml` bir Telegram kanalı tanımlar; Grafana provisioning'inde koşul yoktur, tanımlı kanal doğrulanamazsa Grafana startup'ta abort eder ve container crash-loop'a girer. Değerler `skinora-grafana-provisioning` render adımıyla yerine konur (§G.6) |

> **Deploy sırası — blockchain servisi backend'le birlikte ya da ondan önce (T148).** T148'den itibaren servis her webhook'a `network` yazar, backend her isteğe `network` yazar. İki tarafın da eski karşı tarafla çalışma yolu var ama dar: backend `network`'süz webhook'u **yalnız TRON'un sırrıyla imzalanmışsa** TRON sayar (açıkça yazılmış yanlış değer yine reddedilir), servis de `network`'süz isteği kabul eder. Yine de backend ile `sidecar-blockchain` imajını **aynı deploy'da** (ya da önce servisi) güncelleyin: servisin 4xx'i kalıcı saydığı webhook'lar yeniden gönderilmez, iki tarafın sürümlerinin ayrı kalması elle kurtarma gerektiren bir ödeme olayı kaybına yol açabilir. Ethereum servisi (T150) baştan `network` yazar; onun için bu istisna yoktur.

> **Ethereum servisi — ETH/USDT kuru dışarıdan alınır (T154; T154 doğrulaması).** Servis kuru anahtarsız iki kaynaktan okur: Binance `https://api.binance.com/api/v3/avgPrice?symbol=ETHUSDT` ve CoinGecko `https://api.coingecko.com/api/v3/simple/price` (08 §3.7.7 md. 5). **Açılışta iki kaynağın %2 içinde uyuşması şarttır** (yönetici kararı ⚠) ve ±%20'lik tek kaynak kontrolünün tabanı yalnız iki kaynağın uyuştuğu fiyatla ilerler. Binance bazı bölgelerden ve bulut sağlayıcılarından erişimi **HTTP 451** (bölge engeli) ile reddeder; üretim sunucusu böyle bir yerdeyse servis açıldığından beri hiç fiyat kabul edemez — `ETH_PRICE_UNAVAILABLE`: Ethereum iadesinde ücret tahmini yerine statik yedek kesilir, tavan kur istediği için sweep ve payout bekler. Bu durum bir kesinti gibi geçici değildir, sunucunun konumundan gelir ve kendiliğinden düzelmez. Deploy öncesi üretim sunucusundan `bash scripts/probe-eth-price-sources.sh` koşun (T158 (e); §J.1 adım 4): iki ucu servisin kurallarıyla dener ve 451 / 403 / 429 / cevapsızlık durumunu adlandırır; çıkış 0 olmalıdır. 451 / 403 dönerse Ethereum ağını açmadan önce karar gerekir (başka çıkış noktası ya da kaynak değişikliği — kod değişikliği ister). Servisin log'unda `Binance ETHUSDT quote rejected` (`HTTP 451`) satırı aynı durumu gösterir. Probun kendisi T158'in kabul kriteridir (11 T158 (e)).

---

## C. Production'da ayarlanması önerilen SystemSetting'ler

Seed default'u `NONE`/varsayılan ile açılır ama set edilmezse ilgili kapsam **çalışmaz** (warn log ile atlanır, fail-fast DEĞİL).

> **⚠ Bunlar `SKINORA_SETTING_*` env ile set EDİLEMEZ — yalnızca admin UI'dan.** (Platformun iki cüzdan adresi 2026-09-16'da bu listeden çıktı: artık `HOT_WALLET_ADDRESS` / `COLD_WALLET_ADDRESS` ortam değişkenidir, §B.) Aşağıdaki yedi satırın hepsi `SystemSettingSeed`'de `Default(...)` ile, yani `IsConfigured = true` olarak gelir. `SettingsBootstrapService` yalnız `IsConfigured = false` satırları env'den hydrate eder ve configured bir satırı **asla** override etmez (06 §8.9 güvenlik klozu). §A'daki 19 satır `Unconfigured(...)` olduğu için env yolu yalnız orada çalışır. (Doğrulandı 2026-07-29 — `SystemSettingSeed.cs`'te tam 19 `Unconfigured` satırı var.)

| Key | Default | Set edilmezse |
|---|---|---|
| `auth.banned_countries` | NONE | Geo-block uygulanmaz (02 §21.1) |
| `multi_account.exchange_addresses` | NONE | Çoklu-hesap kontrolünde exchange adres allowlist'i boş |
| `price_deviation_threshold` | 1.0 (= %100) | Seed default'la PRICE_DEVIATION fraud kuralı pratikte hiç ateşlemez (WP4a bilinçli geniş default). Prod'da daraltılmalı (02 §14.4) — **aşağıdaki `SteamMarket__Provider` ile birlikte** anlamlı olur |
| `delivery.inventory_evidence_auto_release_enabled` | `false` | **Launch'ta bilinçli olarak `false` kalır** — envanter kanıtı kaydedilir ve gösterilir ama parayı tek başına serbest bırakmaz. Açma prosedürü §H'de; alıcının kendi onayı bundan etkilenmez (T125, 02 §9.2) |
| `settlement.reversal_auto_refund_enabled` | `false` | **Launch'ta bilinçli olarak `false` kalır** — mutabakat sonu kontrolü geri alma imzası bulursa admin'e eskale eder, otomatik iade + fraud flag uygulamaz. Açma prosedürü §I'de (T129, 02 §4.5.1) |
| `payout_settlement_days` | `8` | Seed default'u zaten doğru değerdir (7 günlük Steam penceresi + 1 gün marj). Değiştirilecekse **7'nin altına inilemez** — validator reddeder (02 §16.2) |
| `settlement.unreadable_escalation_hours` | `48` | Mutabakat kontrolü envanter okunamadığı için sonuca varamadığında admin'e ne kadar sonra düşeceği. Kısaltmak admin kuyruğunu şişirir, uzatmak satıcının ödemesini geciktirir — ödeme her iki hâlde de parkta kalır (03 §2.4) |

### C.3 Gönderim tavanları ve platform adresleri (SystemSetting değil)

> **Owner kararı 2026-09-16/17.** Platformun kendi adresleri ve giden transferlerin tutar tavanları imzalayan servisin yapılandırmasıdır; panelden değiştirilemezler (panelde salt okunur görünürler, 07 §9.8).

**Tavanları boyutlandır:**

1. **Tek gönderim tavanı** = platformun izin verdiği en büyük işlem tutarı (`max_transaction_amount`, §A satır 8). Payout bundan büyük olamaz; büyükse hesap hatasıdır.
2. **Günlük çıkış tavanı** = beklenen günlük ödeme hacmi × 3. Üç kat, yoğun bir günün ödemelerini ertesi güne itmeden bırakırken ele geçirilmiş bir backend'in bir günde çıkarabileceği tutarı sınırlar.
3. Tavan aşılırsa gönderim kalıcı reddedilir ve mevcut "gönderim başarısız" alarmı admin'e gider. Soğuk cüzdana konsolidasyon günlük tavana **sayılmaz** (hedefi sabittir).
4. **Ethereum yakıt tavanı (T153) farklıdır:** aşılırsa yakıt gönderilmez ve sweep / iade **bekler** (`FUEL_LIMIT_REACHED`, satır `PENDING`) — reddedilseydi satır `FAILED` olur, yeni kimlikle yeniden açılır ve depozit ikinci kez yakıtlanırdı. Günlük tavan son 24 saatin yakıtını servisin günlüğünden sayar; saat ilerledikçe kendiliğinden açılır. Tek yakıt tavanı ETH fiyatı çok düşerse (tavan ETH cinsinden büyür) yetmeyebilir — o zaman değeri yükseltip servisi yeniden başlatın.

```bash
# Örnek: en büyük işlem 10.000 USDT, beklenen günlük hacim 500 USDT
MAX_SINGLE_TRANSFER_USDT=10000
MAX_DAILY_OUTFLOW_USDT=1500
# Ethereum servisinin kendi tavanları (T152) — aynı kural, ağ başına ayrı değer
ETHEREUM_MAX_SINGLE_TRANSFER_USDT=10000
ETHEREUM_MAX_DAILY_OUTFLOW_USDT=1500
# Ethereum yakıt tavanları (T153) — ETH cinsinden. Tavanda bir sweep yakıtı
# bugün ~0,000145 ETH (0,50 $ × 46.201 / 67.201 ÷ ETH fiyatı); 0,001 ETH, ETH
# ~344 $'a düşene kadar tek yakıta yeter. Günlük: en yoğun gün ~30 gönderim ×
# ETH 500 $'daki tavan yakıtı (~0,000687) ≈ 0,02 ETH. Günlük toplam yakıt
# işleminin kendi ücretini de sayar (T153 doğrulaması): ETH 500 $'da en kötü
# hâlde yakıt + ücreti ≤ 0,50 $ ≈ 0,001 ETH → 0,02 ETH ~20 yakıt; aşan gün
# FUEL_LIMIT_REACHED ile bekler (para kaybı değil), gerekirse değeri yükseltin.
ETHEREUM_FUEL_MAX_SINGLE_ETH=0.001
ETHEREUM_FUEL_MAX_DAILY_ETH=0.02
```

**Adres değiştirme yordamı** (sıcak ya da soğuk cüzdan taşınıyorsa):

1. `.env`'de `HOT_WALLET_ADDRESS` / `COLD_WALLET_ADDRESS` değerini değiştir.
2. **Backend ve blockchain sidecar'ı birlikte** yeniden başlat. Yalnız birini başlatmak sweep'i `DESTINATION_NOT_ALLOWED` ile durdurur — para yanlış adrese gitmez ama kuyruk birikir.
3. `GET /api/v1/admin/settings` çıktısında iki satırın yeni değeri gösterdiğini doğrula (`isEditable: false`).
4. Sıcak cüzdan değiştiyse mutabakat beklentisi de değişir: yeni adres eski bakiyeyi taşımıyorsa ilk `reconciliation` turu uyuşmazlık yazar; taşıma tamamlanana kadar beklenir.

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

### C.2 Enerji kilidi ve kilit hesabı (hibrit enerji kararı — SystemSetting değil) — **UYKUDA (F8, T141)**

> **Bu bölüm uykudadır — kurulumda UYGULANMAZ (öneriyle kaydedildi — ⚠) (2026-10-17; 11 §F8).** Hibrit kararın (2026-09-16) yerini 11 §F8 **K4/K8** aldı: TRON'da sweep ücretini alıcı ("TRON ağ ücreti" satırı, kabul anında sabitlenir), payout ücretini satıcı öder (02 §4.6–§4.7, 08 §3.1a). Platformun TRX kilitleyerek ücreti düşürmesine gerek kalmaz ve ~35 bin $ TRX'in 14 gün çözülemeyecek şekilde bağlanması önlenir. Kilit kodu silinmedi; açık bir kapama anahtarının arkasında **varsayılan kapalı** durur ve kullanılmazsa sonra kaldırılır. **Anahtar (T149): `ENERGY_STAKING_ENABLED`** — yalnız blockchain servisine gider, compose varsayılanı `false`. Kapalıyken servis kilidi hiç okumaz, devretmez, `STAKE_ACCOUNT_ADDRESS` / `STAKE_ACCOUNT_PERMISSION_ID`'yi yok sayar (doğrulamaz da) ve her depozit transferi yakar; açılış log'u `Energy delegation: OFF (ENERGY_STAKING_ENABLED is not true)` yazar — kurulumdan sonra bu satırı kontrol edin. Yalnız `true` / `false` / boş kabul edilir; başka bir değer (`1`, `yes`, yazım hatası) servisi açılışta durdurur. Kilidi yeniden açmak = `.env`'de `ENERGY_STAKING_ENABLED=true` + aşağıdaki adımlar + servisin yeniden başlatılması; TRON ağ ücreti satırı yine yakma yolunu fiyatladığı için açılırsa satır yeniden ele alınır (08 §3.1a). Aşağıdaki adımlar yalnız proje sahibi kilidi yeniden açmaya karar verirse geçerlidir; o durumda backlog `StakeAccountUnmonitored` (kilit hesabı bakiyesi alarmı) kilit açılmadan önce kapatılır.

> **Proje sahibi kararı (2026-09-16): HİBRİT.** Beklenen günlük **taban** satış hacmi kadar TRX ENERGY için kilitlenir; kilidin karşılayamadığı her transfer otomatik yakar. Kilit yapılmazsa hiçbir şey kırılmaz — her sweep ve payout yakma yoluna düşer ve satış başına ~15,2–21,8 TRX harcanır (08 §3.3). Deposit'ten yapılan transferlerde devredilecek miktar **ayar değildir**; sidecar her transfer için güncel orandan hesaplar.
>
> **Proje sahibi kararı (2026-09-17): kilit AYRI BİR HESAPTA durur.** Kilitli TRX, sıcak anahtarın arkasındaki en büyük değerdir (satışların parası T129'dan beri kendi depozit adreslerinde bekler; sıcak cüzdanda biriken komisyondur). Kilit sıcak cüzdanda dururken, anahtarı çalan kişi hesabın izinlerini değiştirip kilidi kendine geçirebilirdi. Artık kilit kendi hesabındadır: **owner anahtarı çevrimdışıdır**, sıcak cüzdanın anahtarı o hesapta yalnız *enerji devret / geri al* yetkisi olan bir active permission ile listelidir. Çalınan sıcak anahtar enerjiyi başka adrese devredebilir (mainnet en fazla ~30 günlük yakma zararı) ama **kilitli TRX'e dokunamaz**: unstake, TRX transferi ve izin değiştirme `SIGERROR "Permission denied"` alır (Nile'da ölçüldü 2026-09-17).

**Kilit miktarını hesapla:**

1. Beklenen günlük taban satış sayısını belirle (`N`). Zirveleri değil, her gün kesin kullanılacak hacmi al — kullanılmayan enerji birikmez, boşa gider.
2. Güncel oranı zincirden oku (hot cüzdan adresiyle):
   ```bash
   curl -s -X POST https://api.trongrid.io/wallet/getaccountresource \
     -H 'Content-Type: application/json' \
     -d '{"address":"<HOT_WALLET_ADDRESS>","visible":true}'
   # oran = TotalEnergyLimit / TotalEnergyWeight   (2026-09-16: ≈ 9,52)
   ```
3. Kilit iki paydan oluşur (proje sahibi kararı 2026-09-18, kalıcı devretme):
   - **Ödeme payı** `P = N × 130.285 ÷ oran` TRX. Payout'u sıcak cüzdan kendisi gönderir ve enerjiyi **kendi havuzundan** kullanır; bu pay adım 8'de sıcak cüzdana kalıcı devredilir. 130.285 = alıcısı token'ı ilk kez alan bir payout (üst sınır); satıcıların çoğu USDT tutuyorsa alt sınır `N × 64.285 ÷ oran`.
   - **Depozit payı** `D = (N + 0,1) × 64.285 ÷ oran` TRX. Sweep ve depozitten yapılan iade için sidecar her transferde anlık devreder, transfer bitince geri alır. Her devretme %10 pay içerir; gönderim işi transferleri sırayla yaptığı için kilitte aynı anda yalnız bir devretmenin payı durur — `+ 0,1` odur. Depozitten iade oranı yüksekse `N`'i o kadar artır.
   - Kilit = `P + D`. Örnek, `N = 5`, oran 9,52: `P = 651.425 ÷ 9,52 ≈ 68.430`, `D = 327.854 ÷ 9,52 ≈ 34.440`, kilit ≈ **102.870 TRX**.

**Kilit hesabını kur (hesap başına bir kez):**

4. **Yeni bir Tron hesabı üret ve owner anahtarını çevrimdışı sakla.** Bu anahtar hiçbir sunucuya, hiçbir konteynere, hiçbir `.env` dosyasına girmez — yalnız kilidi çözmek ya da izni değiştirmek gerektiğinde elle kullanılır. **Hesabı fonla:** kilit (`P + D`, adım 3) + **100 TRX izin güncelleme ücreti** (adım 5 — `getUpdateAccountPermissionFee` = 100.000.000 SUN, mainnet ve Nile'da ölçüldü) + bant kilidi (aşağıda "Bant genişliği") + birkaç TRX serbest tampon. İlk gönderim hesabı açar; açma ücretini gönderen öder.
5. **Sıcak cüzdanın anahtarına yalnız devretme yetkisi veren active permission ekle.** Bu işlem kilit hesabından **100 TRX** alır — bakiye yetmezse zincir reddeder. İşlemi **düğüm API'siyle** kur — TronWeb 5.3.5 ile kurma: `updateAccountPermissions` işlemi zincirde `CONTRACT_VALIDATE_ERROR No contract!` alıyor ve `permissionId` seçeneğini sessizce düşürüyor (2026-09-17'de ölçüldü; iki farklı izin aynı txid'i üretti, yani "reddedilmeli" testleri izin hiç kurulmadığı hâlde geçmiş göründü).
   ```bash
   # owner anahtarıyla imzalanır (çevrimdışı makinede), sonra broadcasthex ile yayınlanır
   curl -s -X POST https://api.trongrid.io/wallet/accountpermissionupdate \
     -H 'Content-Type: application/json' \
     -d '{
       "owner_address": "<STAKE_ACCOUNT_ADDRESS>",
       "owner": {"type":0,"permission_name":"owner","threshold":1,
                 "keys":[{"address":"<STAKE_ACCOUNT_ADDRESS>","weight":1}]},
       "actives": [{"type":2,"permission_name":"energy-delegation","threshold":1,
                    "operations":"0000000000000006000000000000000000000000000000000000000000000000",
                    "keys":[{"address":"<HOT_WALLET_ADDRESS>","weight":1}]}],
       "visible": true
     }'
   # operations = yalnız DelegateResource + UnDelegateResource. Başka hiçbir bit açık değil.
   ```
6. **İzni doğrula — kabul ve ret ayrı ayrı görülmeli.** `getaccount` çıktısında `active_permission` içinde `id: 2` ve yukarıdaki `operations` dizisi görünmeli. Ardından kilit hesabı adına iki işlem kur, ikisinin gövdesine de `"Permission_id": 2` ekle ve **sıcak anahtarla** imzala: bir **devretme** (kabul edilmeli) ve bir **TRX transferi** (`SIGERROR` almalı; mesaj `Permission denied`). Aynı kimlikle bir `unfreezebalancev2` denemesi de `Permission denied` almalı. Retleri de koşmadan izin kurulmuş sayılmaz.
   ```bash
   curl -s -X POST https://api.trongrid.io/wallet/createtransaction \
     -H 'Content-Type: application/json' \
     -d '{"owner_address":"<STAKE_ACCOUNT_ADDRESS>","to_address":"<HOT_WALLET_ADDRESS>",
          "amount":1,"Permission_id":2,"visible":true}'
   # sıcak anahtarla imzala → broadcasttransaction → SIGERROR, "Permission denied"
   ```
   **`Permission_id` vermeden kurulan ret hiçbir şey kanıtlamaz.** Kimlik 0 (owner izni) olur; zincir önce kimliği 0'dan farklı işlemlerde izin kapsamını, sonra imzacıyı denetler (java-tron `TransactionCapsule.checkPermission` → `checkWeight`). Kimliksiz denemede ilk denetim atlanır ve sıcak anahtar owner izninde olmadığı için `"… is not contained of permission"` gelir — active izin "her şeye yetki" diye kurulmuş olsa bile. İznin kapsamını yalnız `Permission denied` gösterir.
7. **Kilit miktarını (`P + D`) bu hesapta** ENERGY için kilitle (Stake 2.0 `freezebalancev2`, kaynak `ENERGY`). Kilit **14 gün** (mainnet) beklemeden çözülemez.
8. **Ödeme payını sıcak cüzdana kalıcı devret** (proje sahibi kararı 2026-09-18). Kilit bu hesaba taşınınca sıcak cüzdanın kendi havuzu boşalır ve her payout 6,43–13,03 TRX yakar; bu adım o havuzu geri doldurur. Bir kez ve **kilitsiz** (`lock: false`) devret — devretme sürekli durur. Sidecar ona dokunmaz: yalnız depozitlere yaptığı devretmeleri geri alır.
   ```bash
   # P_SUN = P × 1.000.000 (TRX → SUN). İmza: kurulum oturumunda owner anahtarıyla (çevrimdışı), ya da
   # gövdeye "Permission_id": 2 eklenip sıcak anahtarla — izin devretmeyi kapsar. Sonra broadcasttransaction.
   curl -s -X POST https://api.trongrid.io/wallet/delegateresource \
     -H 'Content-Type: application/json' \
     -d '{"owner_address":"<STAKE_ACCOUNT_ADDRESS>","receiver_address":"<HOT_WALLET_ADDRESS>",
          "balance":<P_SUN>,"resource":"ENERGY","lock":false,"visible":true}'
   ```
   Doğrula: sıcak cüzdanın `getaccountresource` çıktısında `EnergyLimit` ≈ `P × oran` olmalı. Devredilen enerji alıcının kendi `EnergyLimit`'inde görünür (Nile'da ölçüldü 2026-09-18: 1 TRX için 7.370 → 7.444); payout ve payout ücret tahmini bu havuzu okur.
9. Doğrula — devredilebilir miktar **depozit payına** (`D`) yakın dönmeli; kalıcı devretme bu değerden zaten düşülür:
   ```bash
   curl -s -X POST https://api.trongrid.io/wallet/getcandelegatedmaxsize \
     -H 'Content-Type: application/json' \
     -d '{"owner_address":"<STAKE_ACCOUNT_ADDRESS>","type":1,"visible":true}'
   # {"max_size": <SUN>}   — boş gövde {} = devredilebilir hiçbir şey yok
   ```
10. `.env`'e `ENERGY_STAKING_ENABLED=true` (T149 — kapalıyken aşağıdaki değerler okunmaz) ve **yalnız adresi** yaz: `STAKE_ACCOUNT_ADDRESS=<...>`, `T…` ile başlayan base58 biçiminde (ve izin kimliği 2 değilse `STAKE_ACCOUNT_PERMISSION_ID`). Blockchain sidecar'ı yeniden başlat — iki değişken yalnız o servise gider. Adres geçersizse ya da base58 değilse (`41…` hex biçimi dahil — düğüm okumaları onu reddeder ve kilit 0 görünürdü) ya da izin kimliği 2'den küçük / tam sayı değilse sidecar **açılmaz** ve sebebi logda yazar (`STAKE_ACCOUNT_MISCONFIGURED`). Açıldıysa ilk sweep'i beklemeden doğrula:
   ```bash
   docker logs skinora-blockchain-sidecar 2>&1 | grep "Energy delegation:"
   # delegationOwner ve refundEstimateReadsStakeFrom = kilit hesabı, permissionId = 2 (ya da verdiğin kimlik).
   # Sıcak cüzdan görünüyorsa değişken sidecar'a ulaşmamıştır: her sweep ve iade yakar.
   ```
   Boş bırakılırsa sıcak cüzdan kendi kilidini kullanır — eski düzen: o durumda kilidi (`P + D`) sıcak cüzdanın kendisinde yap, kalıcı devretme gerekmez.
11. İlk sweep'ten sonra sidecar logunda `Deposit transfer resource plan` satırının `kind: "delegate"` olduğunu, ardından `Energy delegation confirmed in a block` geldiğini gör. `Energy delegation broadcast` satırındaki `owner` alanı **kilit hesabının** adresi olmalı; sıcak cüzdanınki görünüyorsa `STAKE_ACCOUNT_ADDRESS` sidecar'a ulaşmamıştır. `kind: "burn"` + `reason: "insufficient-stake"` ise kilit o transfer için yetmemiştir. Plan transferin **tamamı** için yapılır (kontrat sahibinin payı düşülmez) ve devretme %10 pay içerir: mainnet'te bir sweep için o an ~7.430 TRX devredilebilir olmalıdır (64.285 ÷ 9,52 × 1,1). İlk payout'tan sonra makbuza bak (`gettransactioninfobyid`): `receipt.energy_usage` > 0 ve `receipt.energy_fee` 0 olmalı — enerji sıcak cüzdanın havuzundan geldi, TRX yakılmadı. `energy_fee` > 0 ise adım 8 yapılmamış ya da yetmemiştir.

**Transfer çağrısının süresi (backend).** Depozitten yapılan her gönderimde sidecar her adımın bir bloğa girmesini bekler (08 §3.3 "Blok onayı"); bir çağrı olağan durumda ~10–25 sn sürer (Nile ölçümü, hesap açma + yakma: 11,6 sn). Backend bu çağrıya `BlockchainSidecar__TransferTimeoutSeconds` kadar (varsayılan **300**) bekler; sidecar çağrının 150. saniyesinden sonra transfer yayınlamaz. **300'ün altına indirme:** backend sidecar'dan önce vazgeçerse yeniden dener, ilk çağrının yayınladığı transfer kaydedilmez. Logda `TRANSFER_WINDOW_ELAPSED` görmek, zincir okumalarının ya da blokların yavaşladığını gösterir — transfer yayınlanmamıştır, backend bir sonraki denemede baştan başlar.

**`DEPOSIT_TRANSFER_WOULD_REVERT` görürsen.** Sidecar transferi zincirde simüle etti ve transfer düşüyor; hiçbir şey gönderilmedi, hiçbir şey yayınlanmadı. Backend yeniden dener (varsayılan 1, 5, 15 dk — `blockchain.transfer_retry_intervals_minutes`), sonra satır `FAILED` olur ve admin uyarısı gelir. **İadeyi ya da sweep'i elle yeniden başlatmadan önce** depozitin giden TRC-20 transferlerine bak (`GET /v1/accounts/<DEPOZIT>/transactions/trc20?only_from=true`): en olası sebep, önceki bir denemenin token'ı göndermiş ama backend'in bunu kaydedememiş olmasıdır. Transfer oradaysa para yerine ulaşmıştır; yeniden başlatmak ikinci bir iade demektir.

**Bant genişliği — kilit hesabında da BANDWIDTH kilitle (proje sahibi kararı 2026-09-17).** Devretme ve geri alma işlemlerinin ücreti **kilit hesabının** bakiyesinden çıkar, sıcak cüzdanınkinden değil: imza sıcak anahtarla atılsa da işlemin sahibi kilit hesabıdır. Her hesabın günlük ücretsiz bandı 600 bayttır ve **bir devret+geri al çifti bunun 558 baytını yer** (Nile'da ölçüldü 2026-09-18) — yani ücretsiz kota günde ancak tek bir çifte yeter. Ötesinde işlem başına ~0,28 TRX yakılır (ölçülen: devretme 279.000 SUN, geri alma 281.000 SUN).

Boyutlandırma: günlük `N` satış → `2N` işlem × ~280 bayt. `N = 5` için ~2.800 bayt/gün, yani ~2,8 TRX/gün yakma (yılda ~1.000 TRX). Bunun yerine kilit hesabında **BANDWIDTH için** birkaç yüz TRX kilitlemek aynı işlemleri ücretsiz hâle getirir ve kilitlenen TRX geri alınabilir (mainnet 14 gün). Kilitledikten sonra doğrula:

```bash
curl -s -X POST https://api.trongrid.io/wallet/getaccountresource \
  -H 'Content-Type: application/json' \
  -d '{"address":"<STAKE_ACCOUNT_ADDRESS>","visible":true}'
# NetLimit  = kilitten gelen bant (0 ise yalnız ücretsiz 600 bayt var)
# freeNetUsed / freeNetLimit = günün ücretsiz kotası
```

Kilit hesabında yine de küçük bir serbest TRX tamponu bırak: bant kilidi yetmezse işlemler yakmaya düşer ve bakiye biterse **devretme başarısız olur**, akış pahalı yakma yoluna geçer. **Bu bakiyeyi izleyen bir alarm henüz yok** (backlog `StakeAccountUnmonitored`) — launch'a kadar haftada bir elle bak (`getaccount` → `balance`). Sıcak cüzdanın kendi bandı ayrı bir kalemdir — payout ve depozite gönderilen TRX oradan çıkar.

**`SELLER_PAYOUT_DEFERRED` alarmı gelirse.** Bir satıcı ödemesinin gas tahmini fiyatı tüketti ve ödeme üç kez ertelendi (02 §4.7); iş yeniden denemeye devam ediyor — alarmdan sonraki deneme 12 saat sonra, ondan sonrakiler 24 saatte bir. En olası sebep sıcak cüzdanın ödeme enerjisinin bitmesidir: `getaccountresource` ile sıcak cüzdanın `EnergyLimit − EnergyUsed` değerine bak. Gün içindeki ödeme sayısı `N`'i aştıysa enerji 24 saat içinde kendiliğinden yenilenir ve ödeme bir sonraki denemede çıkar; sık tekrarlıyorsa adım 8'deki kalıcı devretmeyi büyüt. İşleme elle dokunmak gerekmez — enerji geri geldiğinde sıradaki deneme ödemeyi kuyruğa alır. **Kilit uykudayken (F8 varsayılanı)** sıcak cüzdanın yenilenen enerjisi yoktur: payout her seferinde yakar (2,29–4,53 $) ve tahmin kendiliğinden düşmez. Satış fiyatı payout ücretinin altında kalan bir TRON işleminde ödeme her denemede yeniden ertelenir; bu, TRON için ayrı en düşük satış tutarı kararına (T169) bağlı açık kalemdir.

**Hacim değişince** 1–3. adımları yeniden koş. Oran ağın toplam kilidiyle oynar; aylık kontrol yeterlidir. Kilidi büyütmek owner anahtarını ister (adım 7). Kalıcı devretmeyi büyütmek ya da küçültmek (adım 8) owner anahtarı olmadan da yapılır: sıcak anahtarın izni devretme ve geri almayı kapsar (`Permission_id: 2`).

---

## D. Sidecar config parity (cadence / sweep) — env otoriter, restart-bound

Aşağıdaki ayarlar **hem** backend SystemSetting **hem** sidecar env olarak yaşar. Sidecar config'i **env-only boot** okur (`sidecar-blockchain/config/index.ts`); backend SystemSetting kopyası **admin görünürlüğü** ve tek-kanonik-kaynak içindir ama **çalışan sidecar'a runtime'da yansımaz**. Bu değerlerin değişimi sidecar env güncellemesi + **sidecar restart** gerektirir.

> **WP14 owner kararı (2026-06-19):** runtime push/pull DEĞİL → env parity + runbook. Runtime propagasyon post-MVP (T74 K1 / T96). MVP'de bu cadence'ler nadiren değişen operasyonel knob'lar; env + restart yeterli.

| Backend SystemSetting | Sidecar env | Default | Anlam |
|---|---|---|---|
| monitoring_post_cancel_24h_polling_seconds | `POST_CANCEL_CADENCE_24H_MS` | 30 sn | İptal sonrası 0-24 saat polling |
| monitoring_post_cancel_7d_polling_seconds | `POST_CANCEL_CADENCE_7D_MS` | 300 sn | 1-7 gün polling |
| monitoring_post_cancel_30d_polling_seconds | `POST_CANCEL_CADENCE_30D_MS` | 3600 sn | 7-30 gün polling |
| blockchain.sweep_trx_fallback_sun | `SWEEP_TRX_FALLBACK_SUN` | 15000000 | Kaynak planı hesaplanamazsa (zincir probu arızası) depozite gönderilen sabit TRX (SUN). Devretme miktarı artık ayar değil, transfer başına hesaplanır (08 §3.3) |
| blockchain.ethereum.finality_stall_alarm_minutes | `ETHEREUM_FINALIZED_STALL_SECONDS` (Ethereum servisi) | 30 dk / 1800 sn | **T150.** `finalized` bloğu bu kadar eskiyse Ethereum servisi sağlıksız ve `ethereum-finalized-stalled` alarmı çalar (08 §3.7.8). Servisin env'i otoriterdir (sağlık + alarm aynı gauge'u okur); backend satırı görünürlük içindir — ikisini aynı değerde tutun (dakika × 60 = saniye). T158 ayrı bir backend alarmı eklemedi: servis kapalıyken `ethereum-finalized-stalled` susar (`or vector(0)`), o durumu backend'in sağlık yoklaması (`BLOCKCHAIN_ETHEREUM` kesinti bildirimi + dondurma) görür (§J.2) |
| blockchain.ethereum.fee_cap_wait_alarm_minutes | `ETHEREUM_FEE_CAP_WAIT_ALARM_SECONDS` (Ethereum servisi) | 180 dk / 10800 sn | **T152.** Tavan/kur yüzünden bekleyen ya da imzalanıp bloğa girmemiş Ethereum gönderimi bu kadar sürerse `ethereum-transfer-wait` alarmı (08 §3.7.7). Servisin env'i otoriterdir (alarm `skinora_ethereum_transfer_wait_alarm_seconds`'a böler); ikisini aynı değerde tutun. **Not:** ücret tavanının kendisi (`blockchain.ethereum.fee_cap_usd_per_transfer`) paritede değildir — backend onu her gönderim isteğinde gönderir, panelden değişiklik bir sonraki turda geçerlidir |
| blockchain.ethereum.fuel_wallet_eth_balance_minimum | `ETHEREUM_FUEL_WALLET_MIN_ETH` (Ethereum servisi) | 0,06 ETH | **T153.** Yakıt cüzdanının ETH'si bu tabanın altına inerse `ethereum-wallet-eth-low` alarmı (`wallet="fuel"`, 08 §3.7.5). Servisin env'i otoriterdir (alarm `skinora_ethereum_wallet_eth_floor`'a böler); backend satırı görünürlük içindir — ikisini aynı değerde tutun |
| blockchain.ethereum.hot_wallet_eth_balance_minimum | `ETHEREUM_HOT_WALLET_MIN_ETH` (Ethereum servisi) | 0,06 ETH | **T153.** Sıcak cüzdanın ETH'si (payout ve soğuk aktarımın gas'ı) bu tabanın altına inerse aynı alarm (`wallet="hot"`). Aynı parite kuralı |

**Ethereum gönderim günlüğü (T152, 08 §3.7.6).** Servis imzaladığı her işlemi `skinora-ethereum-tx-journal` volume'ünde kayıt başına bir dosya olarak tutar; bir istek kimliğine bir nonce, yeniden deneme aynı baytlar. Kurallar:

1. **Volume'ü silmeyin, `docker compose down -v` kullanmayın.** Günlük kaybolursa (ya da eski bir yedekten geri yüklenirse) servis kayıtlı işlemleri unutur: bekleyen (henüz bloğa girmemiş) bir gönderimin baytları kaybolur ve backend satırı `DETECTED`'da kalır — durum ucu 404 `TRANSFER_NOT_FOUND` döner. Zincirde nonce'u kullanılmış ama günlükte olmayan işlem varsa servis o hesaptan **yeni imza atmaz** (adım 7) — kaybolan bir kimliğin yeni nonce'la ikinci kez imzalanmasını (çift ödeme) bu durdurma önler.
2. **Yedek:** veritabanı yedeğiyle aynı anda alın (`docker run --rm -v skinora-ethereum-tx-journal:/j -v "$PWD":/b alpine tar czf /b/eth-journal.tgz -C /j .`). Geri yüklerken önce servisi durdurun.
3. **`ethereum-nonce-conflict` alarmı (kritik):** bir hesabın nonce'unu servisin imzalamadığı bir işlem kullandı. Servisi durdurun, hesabın işlemlerini Etherscan'de inceleyin (anahtar ele geçmiş olabilir ya da biri elle gönderim yaptı). Servis bu kaydı **otomatik yeniden imzalamaz**; satır `DETECTED`'da kalır. Para gerçekten gönderilmediyse yeniden gönderim kararı admin'indir — aynı satırı yeniden kuyruğa almak yeni bir kimlik ister.
4. **`FAILED` bir Ethereum gönderim satırını elle yeniden göndermeden önce** günlükte o satırın kaydına bakın: `docker run --rm -v skinora-ethereum-tx-journal:/j alpine ls /j | grep <BlockchainTransaction.Id>`. T152 düzeltme turundan beri (proje sahibi kararı 2026-10-18) Ethereum satırı **yalnız servisin kalıcı reddiyle** `FAILED` olur (USDT durdurulmuş/kaldırılmış, imzacı reddi, kara liste, simülasyon geri dönüşü, aynı kimlik farklı parametre — satırın `ErrorMessage`'ındaki kod); geçici hatada satır `PENDING` kalır ve aynı kimlikle denemeye devam eder, elle hiçbir şey yapılmaz. Kalıcı retten sonra kayıt **yoksa** hiçbir şey imzalanmamıştır — sebebi giderip (ör. USDT yeniden çalışınca) yeni gönderim kararı admin'indir. Kayıt **varsa** (beklenmez: ret imzadan önce verilir) servis o işlemi imzalamış ve teslim ediyor demektir — **yeniden göndermeyin**, kayıttaki hash'i Etherscan'de izleyin.
5. **`ethereum-transfer-wait` alarmı:** `skinora_ethereum_transfer_wait_seconds`'ın `kind` etiketine bakın — `fee_cap`: ağ ücreti tavanın üstünde ya da ETH fiyatı yok (tavanı panelden geçici yükseltmek bir sonraki turda etkili olur); `unmined`: imzalanmış gönderim bloğa girmiyor (göndericinin ETH'si, sağlayıcı yayını, tavanda takılan ücret). Gas eksikliği (`kind="gas"`: yakıt bekleyen ya da yakıtlanamayan depozit, ETH'si bitmiş sıcak cüzdan) bu alarma girmez — `ethereum-wallet-eth-low` (adım 8) ve backend'in `outbound-transfer-stuck`'ı (adım 6) görür.
6. **`outbound-transfer-stuck` alarmı (backend, ağ başına):** bir ağın en eski `PENDING` giden satırı eşiği aştı (Ethereum: `blockchain.ethereum.fee_cap_wait_alarm_minutes` × 60 = 3 sa). Ethereum satırları geçici hatada `FAILED` olmadığı için servise ulaşılamaması, ücretin tavan üstünde kalması ya da ETH fiyatının olmaması burada görünür. `skinora_backend_outbound_pending_rows{network}` kaç satırın beklediğini söyler; Ethereum servisinin `/health`'ine, `skinora_ethereum_transfer_wait_seconds`'a ve backend log'undaki "still failing transiently after the retry budget" satırlarına bakın. Satırları elle `FAILED` yapmayın ve yeniden göndermeyin — servis dönünce aynı kimliklerle kendiliğinden gider.
7. **`ethereum-journal-behind-chain` alarmı (kritik; yönetici kararı, T152 doğrulaması 2026-10-18):** yeni bir imzadan önce servis bir hesabın zincirdeki nonce'unu (`pending` dahil) günlüğün bildiğinin ilerisinde buldu; o hesaptan **hiçbir yeni işlem imzalanmıyor** (gönderim ucu 503 `JOURNAL_BEHIND_CHAIN`, backend satırları `PENDING` kalır, kendiliğinden yeniden dener). Servis log'undaki `JOURNAL BEHIND CHAIN` satırı hesabı, zincirin nonce'unu (`chainNonce`) ve günlüğün bildiğini (`journalNext`) yazar. Sırayla:
   1. **Servisi durdurun** (`docker compose --profile ethereum stop skinora-ethereum-sidecar`) — sebep belli olana kadar başka bir şey imzalanmasın.
   2. **Sebebi bulun.** Volume değişti mi / geri yükleme yapıldı mı (adım 2), biri anahtarla elle gönderim yaptı mı, anahtar sızmış olabilir mi (sızma şüphesinde dur, adım 3'teki gibi anahtarı döndürme kararı yöneticinin).
   3. **Daha yeni bir günlük yedeği varsa onu geri yükleyin** (servis durmuşken; adım 2'deki `tar` komutunun tersi). Genelde bu yeter: günlük aradaki nonce'ları yeniden tanır, durma kendiliğinden kalkar — 4. ve 5. adıma gerek kalmaz.
   4. **Yedek yoksa** aradaki her nonce'un (`journalNext` … `chainNonce − 1`) işlemini Etherscan'de bulun ve bir Skinora satırıyla eşleyin (alıcı adresi + tutar → `BlockchainTransactions` satırı). Eşlenen satır için servis kaydı **yoktur**: o satır aynı kimlikle yeniden sorulursa servis onu **yeniden imzalar** (çift ödeme). Bu satırların gönderimini açmadan önce yöneticiyle karar verin (satır zincirdeki hash'le elle kapatılır ya da gönderim işi o satır için durdurulur) — bu adım bitmeden 5. adıma geçmeyin. Bu adım şimdilik **elle** yapılır; yetkili, audit log'lu ve iki aşamalı onaylı bir admin aracı T157'de gelecek (11 §F8, T152 doğrulamasından).
   5. `.env`'e `ETHEREUM_JOURNAL_ACKNOWLEDGED_NONCES=<hesap>:<chainNonce>` yazın (birden çok hesap virgülle) ve servisi yeniden oluşturun (`docker compose --profile ethereum up -d skinora-ethereum-sidecar`). Açılışta değer okunur; bozuksa servis açılmaz. Yeniden başlatma göstergeyi sıfırlar (`skinora_ethereum_journal_behind_chain` 0); onay yetmiyorsa o hesabın bir sonraki gönderim denemesinde alarm yeniden çalar.
   6. **`ethereum-journal-ack-ahead-of-chain` alarmı (kritik):** yazılan onay hesabın zincirdeki nonce'undan (`pending` dahil) büyük — yazım hatası ya da eski bir değer. Servis bu onayı **yok sayar** (zincirde olmamış nonce'ları "hesaplandı" saymak, günlük kaybında aynı kimliğin yeniden imzalanmasına yol açardı); günlük gerideyse durma sürer ve iki alarm birlikte görünür. Servis log'undaki `JOURNAL ACKNOWLEDGEMENT AHEAD OF CHAIN` satırı yazılan değeri (`acknowledged`) ve zincirin nonce'unu (`chainNonce`) verir. Değeri alt adım 5'teki gibi `chainNonce`'a düzeltip servisi yeniden oluşturun.

   **İlk kurulum notu:** daha önce (Skinora dışında, ya da başka bir kurulumda) kullanılmış bir sıcak cüzdanla servis ilk kez açıldığında günlük boştur ama hesabın zincirde nonce'u vardır — ilk gönderim denemesi hemen `JOURNAL_BEHIND_CHAIN` verir ve bu alarm çalar. Bu bir arıza değildir; onay adımıdır: alt adım 2 ve 4'ü (o nonce'ların hiçbiri bir Skinora satırına ait olmamalı) yapıp alt adım 5 ile onaylayın.

8. **Yakıt ve sıcak cüzdan ETH'si (T153; `ethereum-wallet-eth-low` alarmı, warning).** Depozitler kendi gas'ını tutmaz: her Ethereum sweep'i ve sweep öncesi iadesi önce **yakıt cüzdanından** depozite tam gerektiği kadar ETH alır (08 §3.7.5); payout ve soğuk aktarımın gas'ını **sıcak cüzdan** kendi ETH'siyle öder. İkisi de **elle** doldurulur.
   1. **Adresler:** yakıt cüzdanının adresi servisin açılış log'undaki `Ethereum service configured` satırında `fuelWalletAddress` (anahtardan türetilir — yapılandırmada ayrıca yazılmaz); sıcak cüzdan `ETHEREUM_HOT_WALLET_ADDRESS`.
   2. **Doldurma:** operatörün kendi cüzdanından o adrese düz ETH gönderin (en az 0,06 ETH tabanı + bir haftalık pay; T142 hesabı 7 günlük en yoğun yük ~0,030 ETH). Gelen ETH nonce kullanmaz — `JOURNAL_BEHIND_CHAIN`'i tetiklemez. **Yakıt ya da sıcak cüzdan anahtarıyla servis dışında işlem göndermeyin**: günlüğün bilmediği nonce o hesaptan imzayı durdurur (adım 7).
   3. **Alarmı okuyun:** `skinora_ethereum_wallet_eth_balance{wallet}` / `skinora_ethereum_wallet_eth_floor{wallet}` < 1, 10 dk boyunca. Anahtarı bağlanmamış cüzdan seri yayımlamaz (alarm susar) — yakıt anahtarı yoksa sweep'ler `FUEL_REQUIRED` ile bekler ve `outbound-transfer-stuck` 3 sa sonra çalar.
   4. **Bekleme sebepleri** (Ethereum servisi log'u ve backend satırının `ErrorMessage`'ı; hepsi 503 `deferred`, satır `PENDING` kalır, `FAILED` olmaz): `FUEL_PENDING` — yakıt gönderildi, bloğa girmesi bekleniyor (olağan, ~1 dk); `FUEL_WALLET_LOW` — yakıt cüzdanı bu yakıtı ödeyemiyor (doldurun); `FUEL_LIMIT_REACHED` — tek ya da 24 saatlik yakıt tavanı (§C.3; günlük tavan kendiliğinden açılır); `FUEL_REQUIRED` — yakıt anahtarı ya da yakıt tavanları ayarlı değil, ya da bloğa girmiş yakıt bugünkü ücrete yetmiyor (ücret düşünce gider; bir gönderime ikinci yakıt gönderilmez); `HOT_WALLET_GAS_LOW` — sıcak cüzdanın ETH'si payout'a yetmiyor (doldurun).
   5. **Depozitte kalan ETH:** gönderimden sonra depozitte küçük bir ETH artığı kalır ve **toplanmaz** (08 §3.7.5); artık servisin tohumuyla her zaman kurtarılabilir. Bu bir alarm değildir; kayıtlı yakıt `ethereum-deposit-native-eth`'i tetiklemez.

**Yalnız sidecar'da yaşayan sıklık (backend kopyası yok):** `PAYMENT_HOLDING_POLLING_INTERVAL_MS` (varsayılan 900000 = 15 dk) — ödemesi onaylanmış bir depozit adresinin sweep'e kadar yoklanma sıklığı (08 §3.4). Ödeme beklenirken `PAYMENT_POLLING_INTERVAL_MS` (3 sn) geçerlidir. Değer ödeme aralığından kısa ya da sayı değilse sidecar açılmaz. İkisi de açılışta `Payment monitoring:` satırında, bütçeyle birlikte yazılır — restart sonrası oradan doğrulanır.

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
5. **TronGrid bütçesi ve izleme sıklığı:** blockchain sidecar açılış logunda `Payment monitoring:` satırı `paymentPollingIntervalMs` / `holdingPollingIntervalMs` / `tronGridDailyRequestBudget` değerlerini gösterir; bütçe planın gerçek değeri olmalı (§B). Grafana'da `TronGrid Daily Quota Projection` kuralı `health: ok` görünmeli.
6. **Eski ilanlarda iade adresi yok (T147):** T147 öncesi oluşturulmuş ilanlar kabulden önce de bir TRON depozit adresi taşır. Böyle bir ilan kabulden önce iptal edilir ve adrese başıboş transfer gelirse alıcı iade adresi yoktur: iade gönderilmez, `REFUND_BLOCKED` admin uyarısı (sebep `RefundAddressMissing`) çıkar, para depozitte kalır ve admin elle karar verir (08 §3.4).

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
| Tron testnet cüzdanı | HD mnemonic + ayrı hot wallet (adres + private key), içinde faucet TRX. Mnemonic T143'ten önce depozit açmak için kullanıldıysa önce §G.0b |
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

### G.0b T143 HD türetme yolu düzeltmesi (2026-10-18'den önce kullanılmış bir Nile ortamı için)

> **Kimin için:** `HD_WALLET_MNEMONIC`'i T143'ten önce depozit açmak için kullanılmış bir ortam. **Sıfırdan kuranlar atlar.** Mainnet'te bu tohumla hiç depozit açılmadı (canlıya çıkış F9), yani adım yalnız Nile içindir.
>
> **Neden gerekli (T143, 08 §3.2):** T143 öncesi kod depozitleri `m/44'/60'/0'/0/0/44'/195'/0'/0/{i}`'den türetiyordu; T143'ten sonra standart `m/44'/195'/0'/0/{i}`'den türetir. Aynı mnemonic ve aynı indeks artık **başka bir adres** verir. Üç sonucu var: (1) eski depozitlerdeki bakiye yeni koddan imzalanamaz — sweep ve iade, yeniden türetilen adres kayıtlı adresle uyuşmadığı için `DEPOSIT_ADDRESS_MISMATCH` (400, yeniden denenmez) ile reddedilir. Para kaybolmaz (tohum aynı) ama yalnız eski yolu bilen elle bir araçla çıkarılabilir. (2) Veritabanındaki `PaymentAddresses` satırları eski adresleri taşır; ödeme penceresi açık bir işlem ödeme alabilir ama iadesi ve sweep'i yapılamaz — iptal sonrası 30 günlük geç ödeme izlemesi de aynı adresleri izlemeye devam eder. (3) Yeni işlemler sıradaki indeksten yeni yolla doğru açılır; indeks çakışması olmaz. Bu yüzden yerel veritabanı sıfırlanır.

| # | Adım | Komut / kontrol | Neden |
|---|---|---|---|
| 1 | **Yükseltmeden ÖNCE envanter** | `SELECT HdWalletIndex, Address, MonitoringStatus, TransactionId FROM PaymentAddresses ORDER BY HdWalletIndex;` → adres listesiyle `POST /api/wallet/balances` (en çok 100 adres). Ürün dışında elle açılmış test depozitlerini de ekleyin (2026-09-16 enerji ölçümü: indeks 9001, 9003, 9004, 9005 — veritabanında satırları yok) | Hangi eski depozitte TRX/USDT kaldığını bilmeden sıfırlarsanız o adresler kayıttan da düşer |
| 2 | **Bakiyeleri boşaltın ya da bilerek bırakın** | Karar operatörün; Nile değeri yoktur. Boşaltmak için anahtar **eski yolla** türetilir: `HDNodeWallet.fromMnemonic(Mnemonic.fromPhrase(mnemonic))` (yol verilmeden — `m/44'/60'/0'/0/0` düğümü) `.derivePath("44'/195'/0'/0/<indeks>")`; türetilen adresin adım 1'deki adresle aynı olduğunu **önce** doğrulayın, sonra TronWeb ile USDT'yi (`trc20` `transfer`) ve TRX'i sıcak cüzdana gönderin. Mnemonic `.env`'den okunur, ekrana ve log'a basılmaz | Sıfırlamadan sonra bu paranın kayıtla bağı kalmaz; eski yol yalnız bu dipnotta yaşar |
| 3 | **İmajı T143 kodundan yeniden kurun** | `docker compose -f docker-compose.yml up -d --build --wait` (§G.3) | Eski imaj eski yolla türetmeye devam eder |
| 4 | **Yerel veritabanını sıfırlayın** | `docker compose -f docker-compose.yml down` → `docker volume ls \| grep skinora-db-data` ile bulunan birimi `docker volume rm <ad>` → §G.2 adım 2, 3, 4 ve 6 (veritabanı, migration, stack, süper admin) | Eski adres satırlarının hiçbiri artık imzalanabilir değil (yukarıdaki (1)–(2)). **Sıfırlamanın bedeli:** kullanıcılar, ilanlar ve admin ayarları gider; satıcı ödeme adresi yeniden girildiğinde **24 saatlik bekleme** baştan başlar ve prova planı ona göre kurulur (§G.5, `wallet.payout_address_cooldown_hours`) |
| 5 | **Doğrulayın: yeni yol** | `POST /api/wallet/derive {"index":0}` → `derivationPath` = `m/44'/195'/0'/0/0` ve adres, adım 1'deki eski indeks-0 adresinden **farklı**. Bağımsız kontrol: aynı mnemonic'i **çevrimdışı** çalışan bir BIP-44 aracına (coin 195, hesap 0, değişim 0) girip indeks 0'ı karşılaştırın — mnemonic çevrimiçi bir sayfaya yapıştırılmaz | Kod yolu artık standart: aynı tohumu tanıyan her TRON cüzdanı aynı adresi görmeli |
| 6 | **Sıcak cüzdan ve kilit hesabı değişmez** | Bu ikisi HD tohumundan türemez (05 §3.3 — sıcak cüzdan ayrı anahtardır). **Ölçüldü (2026-10-18, bu makinedeki `.env`):** `HOT_WALLET_ADDRESS` ve `STAKE_ACCOUNT_ADDRESS`, tohumun eski ve yeni yolunda indeks 0–50 ve 9001–9005 ile iki kök düğümün hiçbirine eşit değil. Başka bir ortamda sıcak cüzdan bu tohumdan türetilmişse: yeni sıcak cüzdan üretin, `HOT_WALLET_ADDRESS` + `HOT_WALLET_PRIVATE_KEY`'i birlikte değiştirin (adres backend'e de aynı `.env`'den gider), iki servisi yeniden başlatın ve eski sıcak cüzdanın bakiyesini taşıyın | Sıcak cüzdan değişmediği sürece payout ve sweep hedefi aynı kalır |

> **Sıra bağlayıcıdır:** adım 1–2 eski imaj ve eski veritabanı ayaktayken koşulur; adım 3'ten sonra eski adreslerin anahtarı yalnız adım 2'deki elle yolla türetilebilir.

> **T144 notu (ödenecek tutar 2 ondalıklı):** T144 için ayrı bir migration ya da backfill yoktur (06 §8.3 "Mevcut veri"). Adım 4'teki sıfırlama T144 öncesi açılmış işlemlerin 6 haneli `TotalAmount` / `ExpectedAmount` değerlerini de temizler. Sıfırlanmamış bir ortamda böyle bir işlem ekranda tutarını yuvarlamadan tam gösterir (ör. `10.761`) ve 2 haneli bir borsa ödemesiyle eşleşmez; o işlem iptal edilip yeniden açılır. T144'ten sonra açılan her işlemin tutarı 2 ondalıklıdır.

### G.1 Sırlar

Değerler **hiçbir zaman** repo'ya girmez. Yerleri:

- `.env` — env olarak taşınan sırların **tamamı** (gitignored; şablon `.env.example`). Dosya olarak taşınan sırlar yalnız Ethereum servisinindir (T150): `secrets/ethereum_*.txt`, compose `secrets:` ile yalnız o servise bağlanır ve yalnız `ethereum` profiliyle gerekir — bkz. [`secrets/README.md`](../secrets/README.md)
- `scripts/git-hooks/pre-commit` — staged içerikte sır kalıbı bulursa **commit'i** bloklar (`bash scripts/git-hooks/install.sh` ile kurulur)

`docker-compose.yml` yalnız `${VAR}` referansı taşır. Her servis **yalnız kendi** env'ini alır: `HOT_WALLET_PRIVATE_KEY` ve `HD_WALLET_MNEMONIC` sadece blockchain sidecar'a gider — backend imza materyalini hiç görmez (F4 gate check garantisi). Ethereum'un tohumu ve anahtarları yalnız Ethereum servisine, Docker secret olarak gider (T150, K7).

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
| 6 | `docker logs skinora-blockchain-sidecar` | nile bağlantısı + USDT/USDC allowlist dolu + `Energy delegation:` satırı beklenen kilit sahibini gösteriyor (§C.2 adım 10) |
| 7 | Tarayıcı → `http://localhost:8080` | gerçek Steam login, profil adı/avatar gerçek |
| 8 | `GET /api/v1/admin/settings` | **66** giriş — 64 `SystemSetting` satırı (`SeedDataTests` bu sayıyı assert eder) + ortam değişkeninden okunan 2 cüzdan adresi (`isEditable: false`, değerleri `HOT_WALLET_ADDRESS`/`COLD_WALLET_ADDRESS` boşsa `null`); başarılı boot'ta **hiçbir satırın `value`'su null değildir**. Yanıt `isConfigured` **taşımaz** (07 §9.8 gereği configured'lık bilinçli olarak projekte edilmez; gözlenebilir vekil `value != null`), dolayısıyla 45 seed + 19 env dağılımı bu uçtan **okunamaz** — 19'un kanıtı kontrol 3'teki `SystemSetting bootstrap complete — {N} env-hydrated` log satırı, 45'inki `SELECT COUNT(*) FROM SystemSettings WHERE IsConfigured = 1` (seed sonrası, boot öncesi) |
| 9 | Envanter listesi | gerçek CS2 envanteri (`steam-inventory` limiti 5/dk) |
| 10 | Happy path — **tek oturumda gözlenebilen** kısım | işlem oluşturma → alıcı kabulü (`POST /transactions/:id/accept`; trade URL + iade adresi, alıcının MA'sı canlı probe ile doğrulanır) → `ACCEPTED` → satıcı hazırlık onayı (`POST /transactions/:id/confirm-ready`; item hâlâ tradeable + alıcı MA aktif + alıcı envanterinin **baseline**'ı alınır) → `SELLER_CONFIRMED` — **deposit adresi alıcıya ancak burada açılır** → ödeme izleyicisi **aynı geçişte otomatik kurulur** (T139; kurulum kanıtı aşağıdaki notta) → Nile USDT transferi → `payment-detected` → 20 blok → `payment-confirmed` → `PAYMENT_RECEIVED` (+ `DeliveryDeadline` kurulur) → **satıcı item'ı doğrudan alıcıya gönderir** (detay yanıtındaki `steamTradeOfferUrl` = alıcının kendi trade URL'i, **yalnız satıcıya** döner; platform trade offer oluşturmaz, taraf değildir — 02 §2.2 adım 6) → alıcı "teslim aldım" (`POST /transactions/:id/confirm-receipt`) → `ITEM_DELIVERED`, `PayoutEligibleAt` damgalandı |
| 10a | Mutabakat kuyruğu — **pencere dolduktan sonra** | `settlement-verification` (cron `*/5 * * * *`) alıcının envanterini yeniden okur → item hâlâ alıcıdaysa `SettlementVerifiedAt` damgalanır → `seller-payout-queue` (cron `* * * * *`) `SELLER_PAYOUT` satırını kuyruklar → `OutgoingTransferDispatchJob` yayınlar, `OutgoingTransferConfirmationJob` on-chain onaylar, `PayoutCompletedConsumer` `Complete` geçişini ateşler → `COMPLETED`. **Tek oturumda gözlenemez:** `payout_settlement_days` varsayılanı 8 gün, **tabanı 7** (`SystemSettingsValidator.MinimumSettlementDays`, 02 §16.2) — admin altına inemez, ayardan kısaltılamaz. Kuyruğu aynı gün görmek için aşağıdaki prova kısayolu |


> **§G.4 kontrolleri 1–9 + satıcı uygunluğu 2026-08-29'da koşuldu (prova provası — alıcı bacağı MA beklemesi yüzünden koşulamadı).** Sonuçlar: `:8080/health` 200 · `SystemSetting bootstrap complete`, fail-fast yok · `:5100/health` `steam-api reachable` · steam sidecar `listening`, bot/credential satırı yok · blockchain sidecar `network: nile` · `GET /admin/settings` **63 satır / 0 null** (o günkü sayı; bugün 64 satır + 2 env girişi) · `GET /steam/inventory` **gerçek CS2 envanteri** (`Tec-9 | Groundwater`, `assetId 18514036356`). **Kontrol 7 (tarayıcıda Steam girişi) bu turda koşulmadı** — JWT enjeksiyon yolu kullanıldı, o yol OpenID callback'ini atlar.
>
> **Satıcı uygunluğu (`GET /transactions/eligibility`) prova gününden ÖNCE okunmalıdır.** Bu turda okundu ve tek bir engel çıktı: `SELLER_WALLET_ADDRESS_MISSING` → adres kaydedildikten sonra `PAYOUT_ADDRESS_COOLDOWN_ACTIVE` (24 saat). Ayrıntı ve zamanlama kuralı §G.5'te. Diğer kapılar temizdi: MA aktif · eşzamanlı 0/5 · iptal cooldown'ı yok · yeni hesap limiti 0/3.

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

> **Prova raporu:** son canlı prova ve bulguları [`Docs/TEST_REPORTS/REHEARSAL_2026-09-02.md`](TEST_REPORTS/REHEARSAL_2026-09-02.md)'de; §8 prova öncesi kontrol listesidir.

- **⛔ PROVA HESAPLARININ İKİSİ DE “limited” OLMAMALI — 2026-09-02'de canlı provada ölçüldü ve provayı tam da teslimat adımında durdurdu.** Steam, **en az 5 USD** harcamamış hesabı *limited* sayar ve **takas etmesini tümüyle engeller**. Bu, MA'dan ve trade hold'dan **bağımsız** bir kapıdır.
  **✅ 2026-09-14 itibarıyla platform bu kısıtı OKUYOR** (08 §2.2a): üç kapının üçü de sorguluyor, kısıtlı hesap kabul/ilan/hazırlık adımında **kendi hata koduyla** reddediliyor ve kullanıcıya ne yapması gerektiği söyleniyor. Aşağıdaki prova kontrolü yine de geçerli — ürün artık engeli erken söylüyor ama **kısıtı kaldıran yine harcamadır**, ve provayı kısıtlı hesapla kurmak yalnız kapıya çarpmakla sonuçlanır. **Eski durum (tarihsel):** platform kısıtı hiç okumuyordu, tüm kapılar geçiliyordu ve engel ancak satıcı trade offer'ı açmaya çalıştığında görünüyordu — o noktada alıcının parası çoktan zincirde onaylanmış oluyordu.
  **Neden MA probu bunu yakalamıyor:** `GetTradeHoldDurations` *“takas ne kadar bekletilir”* sorusuna cevap verir, *“bu hesap takas edebilir mi”* sorusuna değil; limited hesap da `escrow_end_duration_seconds = 0` döndürür. `TradeHoldService.ts:8-13` MA'yı bu değerden **çıkardığını** kendi yorumunda söylüyor — çıkarımın üçüncü adımı (“demek ki takas edebilir”) yanlıştır.
  **Prova öncesi kontrol (ucuz, tek komut, HER İKİ hesap için):**
  ```bash
  curl -s "https://steamcommunity.com/profiles/<steamid64>?xml=1" | grep -o '<isLimitedAccount>[01]</isLimitedAccount>'
  # 0 = takas edebilir · 1 = LIMITED, prova bu hesapla teslimat adımını GEÇEMEZ
  ```
  **⚠️ Bu uç GÜVENİLİR DEĞİL — tek okumaya karar bağlamayın (2026-09-02'de iki kez yanlış alarm verdi).** Ürün tarafında aynı kural koda geçti: `1` tek okumada bağlayıcı, `0` varsayılan **3 ardışık** örnekle teyit ediliyor (08 §2.2a). Buradaki **5** insan prosedürüdür ve tek seferlik koştuğu için daha katı kalabilir. Profil XML'i arada bir `0` döndürüyor (bayat/CDN'li cevap) ve hemen ardından yine `1` diyor. İki turda da “limit kalktı” sanıldı, peş peşe 5 örnekleme ikisini de yalanladı. **Kural: en az 5 ardışık okuma aynı değeri vermeden karar vermeyin**, ve nihai testi ürün üzerinden yapın (trade offer sayfası açılıyor mu).

  **İkinci kapı: 15 GÜNLÜK BEKLEME.** Limited kalksa bile Steam ayrıca hesap yaşı / Steam Guard süresi için **15 gün** bekletiyor. Yani takas edebilirlik **üç bağımsız koşul**: (1) limited değil · (2) 15 gün dolmuş · (3) escrow bekletmesi 0 (MA, 7 gün). **Prova hesabı açılırken sayaç (2) için de başlar** — hesabı prova gününden **en az 15 gün önce** açın ve 5 USD'yi o zaman harcayın.
  **Çözümü beklemekle olmaz, harcamayla olur:** hesaba gerçek bir ödeme yöntemiyle 5+ USD (cüzdan yüklemesi veya oyun). **Prova gününden önce yapılmalı.**
  **Bedeli neden ağır:** teslimat süresi dolunca tarama turu item'ı satıcıda bulur, işlemi iptal eder ve **kusuru SATICIYA yazar** (03 §4.4) — oysa engel tamamen alıcının hesap kısıtıdır. Süre dolmadan admin iptali (02 §16, `CANCEL_TRANSACTIONS`) bu kusuru önler.

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
  3. `docker compose -f docker-compose.yml up -d --build --wait` → ardından **mutlaka** `docker compose restart skinora-reverse-proxy` (bir alttaki bayat upstream IP tuzağı) **ve `docker compose restart skinora-grafana`** (bir alttaki ölü alarm tuzağı).
  4. Doğrulama, "healthy" değil **davranış**: `GET /api/v1/transactions/params` → `paymentTimeout.minHours` **0 DEĞİL** (0 görüyorsanız eski imaj çalışıyor). **2026-08-29'da koşuldu: `minHours: 1` döndü, ayrıca backend `StablecoinContracts__Usdt` ile sidecar `TRON_USDT_CONTRACT` aynı Nile adresini taşıyor ve frontend bundle'ında `nile.tronscan.org` gömülü — #306'nın üç yarısı da canlı.**

- **⏳ PROVADAN 24 SAAT ÖNCE YAPILMASI GEREKEN TEK ŞEY: satıcının ödeme adresi (2026-08-29'da ölçüldü).** İlan açma yolu iki kapıdan geçiyor ve ikincisi **zaman** kapısıdır. `POST /transactions` önce `SELLER_WALLET_ADDRESS_MISSING` ile 422 döner; adres `PUT /users/me/wallet/seller` ile kaydedilince kapı `PAYOUT_ADDRESS_COOLDOWN_ACTIVE`'e dönüşür ve **`wallet.payout_address_cooldown_hours` = 24 saat** boyunca açılmaz. Yani **adres prova günü girilirse prova bir gün kayar** — ayarın kendisi bir güvenlik kararıdır, kısayolu yoktur. Prova hesabının uygunluğu `GET /api/v1/transactions/eligibility` ile önceden okunabilir; `reasons` boş olmalıdır.

- **⚠️ TERSİ DE DOĞRU: alıcı prova öncesinde profil iade adresine DOKUNMAMALI (2026-08-29'da kod okunarak ölçüldü).** Sezgi "alıcı da adresini önceden girsin" der; **yanlıştır.** Kabul adımı iade adresini **işlem bazlı anlık görüntü** olarak alır (WP12/T90 K4 — profil `DefaultRefundAddress` yazılmaz, profil cooldown'ı başlamaz), yani alıcının profilde adresi **olması gerekmez**. Ama `TransactionAcceptanceService` Stage 5 kapısı **profil** cooldown'ına bakar (T146'dan beri alıcının işlemin ağındaki `UserWalletAddress` `REFUND` satırının `ChangedAt`'i; T147'ye kadar TRON — T146 öncesi `RefundAddressChangedAt`): alıcı iyi niyetle profilinden adres girerse **kabul 24 saat bloke olur**. Adresi olmayan alıcı bloke değildir (satır yok → kapı geçer). Başka ağın (Ethereum) iade adresini değiştirmek TRON kabulünü bloke etmez.


- **Grafana'nın ölü alarmları — yeniden kurmak YETMEZ, container'ı yeniden başlatın (2026-08-29'da ölçüldü).** [#307](https://github.com/turkerurganci/Skinora/pull/307) `datasources/prometheus.yml`'ye `uid: prometheus` ekledi, ama `docker compose -f docker-compose.yml up -d --build --wait` **Grafana'yı yeniden başlatmaz**: servis tanımı değişmemiştir, değişen tek şey mount edilen volume'ün *içeriği*dir. Render servisi (`skinora-grafana-provisioning`, §G.6) volume'ü günceller; Grafana provisioning'i **yalnız açılışta** okur. **Ölçüm:** render 13:12'de koştu, Grafana 18 saattir ayaktaydı, yedi kuralın yedisi de hâlâ `health: error / failed to build query 'A': data source not found` idi — yani düzeltme merge edilmiş, imajlar tazelenmiş, stack 11/11 `healthy`, **ve alarmlar hâlâ ölü**. `docker compose -f docker-compose.yml restart skinora-grafana` sonrası yedisi de **ilk kez** `health: ok` oldu. **Kanıt "healthy" değil, kuralın kendi sağlığıdır:**

  ```bash
  curl -s -u admin:$GRAFANA_ADMIN_PASSWORD \
    http://localhost:3001/api/prometheus/grafana/api/v1/rules \
    | grep -c '"health":"error"'   # 0 olmalı
  ```

  Yeniden başlatmanın hemen ardından kısa süre `firing` görmek normaldir — hata döneminden **devreden** durumdur; bir değerlendirme turu sonra (1-2 dk) `inactive`'e döner. §G.6 bu adımı zaten `--force-recreate skinora-grafana` olarak yazıyordu, ama yukarıdaki prova kontrol listesi onu çağırmıyordu; iki bölüm pratikte çelişiyordu.
- **nginx'in bayat upstream IP'si — yeniden kurduktan sonra reverse proxy'yi yeniden başlatın (2026-08-23'te ölçüldü).** `docker compose up -d --build` backend ve frontend container'larını **yeniden yaratır** ve yeni IP alırlar; `skinora-reverse-proxy` değişmediği için **yeniden başlatılmaz** ve eski IP'leri tutmaya devam eder. Sebep [`nginx/nginx.conf:29-34`](../nginx/nginx.conf#L29-L34)'te: `upstream` blokları hostname taşıyor (`skinora-backend:5000`, `skinora-frontend:3000`) ve dosyada **`resolver` direktifi yok** — nginx bu adları config yüklenirken **bir kez** çözer ve süreç ömrü boyunca saklar.

  **Tuzağı tehlikeli yapan şey belirtisi:** `docker compose ps` **11/11 `healthy`** der, `--wait` başarıyla döner, backend ve frontend kendi portlarında (`:5000`, `:3000`) doğru cevap verir — ama tek giriş kapısı olan `:8080` **502** döndürür. nginx'in kendi healthcheck'i bunu göremez, çünkü `docker-compose.yml`'de `wget http://127.0.0.1:80/nginx-health` olarak tanımlıdır: **kendi kendine** bakan, upstream'e hiç dokunmayan bir kontrol. Yani container "sağlıklı"lığını yapısal olarak upstream'den bağımsız ilan eder.

  **Çözüm:** `docker compose -f docker-compose.yml restart skinora-reverse-proxy`. **Teşhis:** `docker logs skinora-reverse-proxy` içinde `connect() failed (111: Connection refused) while connecting to upstream, upstream: "http://172.20.0.X:5000/..."` satırındaki IP ile `docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' skinora-backend` çıktısını karşılaştırın; tutmuyorsa tuzak budur.
- **Nile kontratları.** Testnet USDT/USDC adresleri sabit değildir; faucet'in verdiğini kullanın ve sidecar'ın bakiye çağrısıyla teyit edin. Boş bırakılırsa allowlist boş kalır.
- **Energy (2026-09-17 ölçüldü).** Nile test USDT'sinde enerjiyi kontrat sahibi öder, ama akış yine **transferin tamamı** için plan yapar (08 §3.3). Bir transfer 14.584 enerji ister → 14.584 ÷ 73,7 × 1,1 ≈ **218 TRX** devretme gerekir; hot cüzdandaki 100 TRX'lik kilit buna yetmediği için Nile'da her sweep/iade **yakma yoluna** düşer: hiç TRX almamış depozit için hesap açma 1,1 TRX (sweeper öder) + depozite 1.604.239 SUN (~1,6 TRX). Sahip ödediği için bu TRX yakılmaz, depozitte kalır. Hot cüzdanda işlem başına ~3 TRX bulunsun; sabit 15 TRX yedeği yalnız zincir probları okunamazsa gider.
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

> **Teslimat taraması (2026-10-02, `P2P-DeliveryPollingJob`).** Kapı kapalıyken artık süre sonunu beklemeden de kanıt toplanıyor: tarama her `PAYMENT_RECEIVED` işlemine en çok 10 dk'da bir bakar ve item'ın satıcıdan çıkıp alıcıya geldiğini **aynı turda** görürse `DeliveryEvidenceCaptures`'a satır yazar (verdict `InventoryEvidencePendingReview`) ve alıcıya `DELIVERY_DETECTED` bildirimi gönderir — alıcı kendi onayıyla (kapıdan bağımsız) işlemi kapatabilir. Sonuç: aşağıdaki B1 ölçümünün `ObservedAt`'ı artık teslimattan en çok ~10 dk sonrasını gösterir — aynı anda en çok 10 açık teslimat varken; tarama dakikada tek işleme baktığı için N > 10 açık teslimatta her işleme ~N dakikada bir bakılır ve `ObservedAt` o kadar gecikir (B1'i okurken o günkü açık teslimat sayısına bakın); önceden yalnız süre sonunda ya da dispute'ta yazıldığı için gecikmeyi değil kontrolün zamanını ölçüyordu. Kapı açıkken tarama işlemi doğrudan `ITEM_DELIVERED`'a geçirir (süre sonu turuyla aynı geçiş). Ayarlar `DeliveryPolling` bölümünde (`Enabled`, `BatchSize` = 1, `RecheckSeconds` = 600; env `DeliveryPolling__Enabled` vb.) ve restart-bound'dur; Steam 429 fırtınasında `DeliveryPolling__Enabled=false` taramayı durdurur.

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

> **Sıcak cüzdanda işletme bakiyesi tutmaya gerek yok (owner kararı 2026-09-17 — T129'un bu başlıktaki N9 notunun yerini alır).** Mutabakat kontrolü hem ödemenin hem süpürmenin kapısıdır (05 §3.3 "Sweep tetikleyicisi"; `SweepQueueJob` da `SettlementVerifiedAt NOT NULL ∧ DeliveryReversedAt NULL` çiftini okur), yani T129'dan sonra iki job aynı pencerede açılıyordu: ödeme saniyeler içinde çıkıyor, delegasyonlu süpürme hesap açma + enerji adımlarını geçiyordu. Aradaki farkı platformun sıcak cüzdanda park ettiği bir işletme bakiyesi kapatıyordu ve bu satır onun **en büyük tek ödeme kadar** boyutlandırılmasını istiyordu. **Artık `SellerPayoutQueueJob`, işlemin kendi `SWEEP` satırı `CONFIRMED` olmadan payout kuyruklamıyor:** sıcak cüzdan yalnız o işlem için eline geçmiş parayı ödüyor, orada biriken şey komisyon oluyor ve `hot_wallet_limit` (§A satır 18) komisyon birikimine göre **küçük** ayarlanıyor — aşılınca admin AD20 ile soğuk cüzdana aktarıyor (hedef sabit, §C.3). **Bedeli kayda geçti:** satıcı parasını süpürme süresi kadar (birkaç dakika) daha geç alır; süpürme kalıcı olarak başarısızsa ödeme bekler ve mevcut süpürme alarmı operatöre gider. Kapının kendisi doğrudur ve değiştirilmemelidir — depozit, geri alma tespit edilirse iadenin çekilebileceği yerde kalmalıdır (02 §4.5.1 "Bilinen sonuçları").

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

---

## J. Ethereum — kurulum, açılış, alarmlar ve elle kurtarma (F8, T158)

> **Kapsam.** Ethereum servisinin (`skinora-ethereum-sidecar`, 08 §3.7) üretimde kurulup açılması, alarmlarının ne dediği ve ne yapılacağı, iki elle kurtarma yordamı (uzun kesinti, yanlış EVM ağına gelen para). Env satırlarının tek tek anlamı §B'de, gönderim günlüğü / yakıt / sıcak cüzdan ETH'si yordamları §D'dedir; burası sıra ve karar noktalarını verir. Gerçek ağda ilk prova T159 (Sepolia), canlı açılış F9'dur.

### J.1 Kurulum sırası

1. **Sırlar (Docker secret, env değil — K7).** `secrets/ethereum_hd_mnemonic.txt` (TRON'unkinden **farklı**, yeni BIP-39 tohum; çevrimdışı yedeği alınmadan devam etmeyin — T163), `secrets/ethereum_hot_wallet_private_key.txt`, `secrets/ethereum_fuel_wallet_private_key.txt` (sıcak anahtardan farklı). Hazırlama ve izinler: [`secrets/README.md`](../secrets/README.md). Sıcak ve yakıt anahtarı artık **kullanılır** (T152/T153): boş bırakılırsa payout / soğuk aktarım `HOT_WALLET_NOT_CONFIGURED` ile reddedilir, sweep ve sweep öncesi iade `FUEL_REQUIRED` ile bekler.
2. **RPC.** Birincil Alchemy (Free) + yedek Chainstack (Developer) anahtarlı URL'leri (`ETHEREUM_RPC_PRIMARY_URL` / `_BACKUP_URL`; sır gibi davranın). Anahtarlı uçların sınır probu (T142 probunun anahtarla tekrarı) **henüz koşulmadı** — anahtar yok; T169'da koşulur, o zamana kadar §B'deki belge değerleri (Alchemy 10 blok, Chainstack 100 blok) geçerlidir.
3. **Env (§B, §C.3, §D).** `ETHEREUM_NETWORK`, `ETHEREUM_SIDECAR_INTERNAL_KEY` (≥ 32, TRON'unkinden farklı), `ETHEREUM_WEBHOOK_SECRET` (TRON'unkinden farklı; servis tarafında `WEBHOOK_SECRET` olarak aynı değer), `ETHEREUM_HOT_WALLET_ADDRESS` / `ETHEREUM_COLD_WALLET_ADDRESS` (backend + servis aynı değer), Sepolia'da `ETHEREUM_USDT_CONTRACT` (T159'un kopyası), tavanlar (`ETHEREUM_MAX_SINGLE_TRANSFER_USDT`, `ETHEREUM_MAX_DAILY_OUTFLOW_USDT`, `ETHEREUM_FUEL_MAX_SINGLE_ETH`, `ETHEREUM_FUEL_MAX_DAILY_ETH` — boş = o gönderim yok), §D parite satırları.
4. **Fiyat kaynağı probu — ÜRETİM SUNUCUSUNDAN (11 T158 (e)).** `bash scripts/probe-eth-price-sources.sh` (ya da `--json`). Betik servisin çağırdığı iki ucu (Binance `avgPrice`, CoinGecko `simple/price`) aynı kurallarla dener: HTTP 200, 1–1.000.000 USDT bandı, kaynak zaman damgası ≤ 10 dk, iki fiyat ≤ %2 farkla. **Çıkış 0 değilse Ethereum açılmaz.** Başarısızlık biçimleri: `HTTP 451` (Binance bölge engeli — kalıcıdır, sunucu konumundan gelir), `HTTP 403` (WAF / bulut sağlayıcı engeli), `HTTP 429` (hız sınırı), `no HTTP answer` (DNS/TLS/güvenlik duvarı). Yalnız bir kaynak çalışıyorsa da başarısızdır: yeni açılmış servis iki kaynak şartı arar (T154 doğrulaması yönetici kararı) ve hiç fiyat kabul etmez. İkisinden biri kalıcı kapalıysa karar gerekir (başka çıkış noktası ya da kaynak değişikliği — kod değişikliği ister). Yerel ölçüm 2026-10-18 21:48 UTC (Türkiye ev bağlantısı): iki kaynak 200, fark < %0,1; 451 biçimi `PROBE_BINANCE_URL=https://httpbin.org/status/451` ile prova edildi (T158 raporu).
5. **Cüzdanları doldurun.** Sıcak cüzdan ve yakıt cüzdanı ETH'si (§D adım 8; taban 0,06 ETH + bir haftalık pay). Sıcak cüzdana payout için USDT de gerekir (sweep'ler gelene kadar). Yakıt cüzdanının adresi açılış log'unda `fuelWalletAddress`.
6. **Servisi açın.** `docker compose -f docker-compose.yml --profile ethereum up -d skinora-ethereum-sidecar`. Açılış log'unda `Ethereum service configured` satırı; `curl -s http://skinora-ethereum-sidecar:5300/health` → 200, `acceptsPayments: true` (izleyici çalışıyor ∧ webhook sırrı var ∧ USDT anahtarları kapalı).
7. **İzleme.** Prometheus `skinora-ethereum-sidecar:5300`'ü kazıyor olmalı (`infra/prometheus/prometheus.yml`); Grafana'da §J.2'deki kurallar yüklü olmalı (`infra/grafana/provisioning/alerting/rules.yml`).
8. **Açın (geri dönülebilir).** Backend `.env`'inde `ETHEREUM_SIDECAR_BASE_URL=http://skinora-ethereum-sidecar:5300`, backend'i yeniden başlatın. Doğrulama: sağlık yoklaması Ethereum'u yokluyor, `supportedNetworks` Ethereum'u içeriyor. Kapatmak için değeri boşaltıp backend'i yeniden başlatmak yeter (açık işlemler sürer; yeni Ethereum işlemi açılmaz).
9. **Sürüm sırası.** Backend ile Ethereum servisini **aynı deploy'da** güncelleyin. T158'den itibaren backend Ethereum derive cevabında `watchFromBlock` arar (yoksa adres ayırmaz, sonraki turda dener), sweep sonrası iadeyi `source: "HOT_WALLET"` gövdesiyle ister ve durum sorgusuna beklenen transferi ekler; eski servis bunları tanımaz.

### J.2 Ethereum alarmları — ne demek, ne yapılır

| Alarm | Önem | Ne demek | Ne yapılır |
|---|---|---|---|
| `ethereum-finalized-stalled` | critical | Son `finalized` blok 30 dk'dan eski — yeni ödeme kabul edilemez; servis `/health` 503 verir, backend Ethereum işlemlerini dondurur | Ağın kendisi mi (Etherscan'de finality), sağlayıcı mı (`skinora_ethereum_rpc_failover_total`, `ethereum-rpc-latency-high`)? Sağlayıcıysa `ETHEREUM_RPC_PRIMARY_URL`'i değiştirip servisi yeniden başlatın. **Not (11 T158 (d)):** kural `… or vector(0)` ile yazıldığı için **servis kapalıyken bu alarm susar** (seri yok → 0). Servisin kendisinin düştüğünü bu alarm değil, backend'in sağlık yoklaması görür: `BLOCKCHAIN_ETHEREUM` bileşeni eşik sayıda üst üste düşünce admin kesinti bildirimi (`PlatformOutageAlertEvent`) ve Ethereum dondurması. Grafana'da ayrı bir "Ethereum servisi kapalı" kuralı yoktur — Ethereum açılmamış kurulumlarda sürekli çalardı |
| `ethereum-monitor-scan-failing` | critical | 15 dk'da 20'den fazla izleme turu düştü — yeni ödeme backend'e bildirilmiyor (aralık her turda yeniden okunur, kayıp yok) | Servis log'unda `payment monitor pass failed` satırının `reason`'ı: RPC (iki sağlayıcı da `eth_getLogs` düşürüyor) ya da webhook (5xx/408/429/401 — backend'e ulaşılamıyor). 401 ise bir sonraki satır |
| `ethereum-webhook-signature-refused` | critical | Backend Ethereum webhook imzasını reddediyor (401): iki taraf farklı sır taşıyor | Servisteki `WEBHOOK_SECRET` ile backend'deki `ETHEREUM_WEBHOOK_SECRET` (`Webhook__EthereumSharedSecret`) aynı olmalı; düzeltip ikisini de yeniden başlatın. Olaylar kaybolmaz: servis aralığı sırlar uyuşana kadar yeniden okur ve gönderir (T158 (j); önceden 401 kalıcı sayılıp olay düşürülüyordu) |
| `ethereum-transfer-status-unverified` | critical | Günlüğün tanımadığı bir hash'in (günlük kaybı / eski yedek) makbuzu, backend satırının beklediği transferi (kontrat, gönderici, alıcı, tutar) taşımıyor ya da backend beklenti göndermedi; servis `UNKNOWN` dedi | §J.5 "Doğrulanamayan gönderim" |
| `ethereum-deposit-balance-mismatch` | critical | Bir depozitin bakiyesi izleyicinin gördüğü transferlerle uyuşmuyor — bir log kaçmış olabilir | Servis log'undaki adres/varlık/blok aralığı; Etherscan'de o aralıktaki transferler; kaçan ödeme varsa backend kaydı yok demektir — admin incelemesi (otomatik düzeltme yok) |
| `ethereum-usdt-status` | critical | USDT ücret / durdurma / kullanımdan kaldırma anahtarı açık ya da okunamıyor; servis ödemeleri kabul etmiyor | Etherscan'de USDT kontratı; Tether duyurusu. Kendiliğinden düzelince alarm kapanır |
| `ethereum-nonce-conflict` / `ethereum-journal-behind-chain` / `ethereum-journal-ack-ahead-of-chain` | critical | Gönderim günlüğü ile zincir uyuşmuyor | §D "Ethereum gönderim günlüğü" adım 3 ve 7 |
| `ethereum-transfer-wait` | warning | Tavan / kur yüzünden ya da bloğa girmeden 3 sa bekleyen gönderim | §D adım 5 |
| `ethereum-wallet-eth-low` | warning | Yakıt ya da sıcak cüzdanın ETH'si tabanın altında | §D adım 8 |
| `ethereum-deposit-native-eth` | warning | Depozite yakıt olmayan ETH geldi (otomatik iade yok) | Gönderen ve tutar log'da; iade kararı admin'in (§J.4'teki elle gönderim yolu, Ethereum mainnet üzerinde) |
| `ethereum-reconciliation-stale` | warning | Servis içi saatlik mutabakat 2,5 aralıktır tamamlanmadı | `skinora_ethereum_reconciliation_skipped_total{reason}`: `finalized_stale` (finality), `read_failed` (sağlayıcı / arşiv reddi) |
| `ethereum-rpc-latency-high` | warning | RPC çağrılarının p95'i 15 dk boyunca 3 sn'nin üstünde | Sağlayıcının durum sayfası; sürüyorsa birincili değiştirin. Yavaş okuma izlemeyi, mutabakatı ve backend'in bakiye parçalarını (`reconciliation-network-failed`) etkiler |
| `outbound-transfer-stuck` | warning | Bir ağın en eski `PENDING` giden satırı eşiği aştı (Ethereum 3 sa) | §D adım 6 |
| `outbound-transfer-backlog` | warning | Bir ağda 15 dk boyunca eşikten fazla `PENDING` giden satır (varsayılan 25, `OUTBOUND_PENDING_ROWS_ALARM_THRESHOLD`) — kesintide satırlar birikiyor; TRON'da yaş eşiği olmadığı için oradaki tek birikme alarmı budur | O ağın servisinin `/health`'i, gönderim işinin log'u ("waits", "transient failure"). Satırları elle `FAILED` yapmayın |
| `reconciliation-network-failed` | warning | Backend'in günlük defter mutabakatı bir ağın bakiyelerini okuyamadı — o turda defter kontrol edilmedi (uyuşmazlık değildir) | Ethereum'da bakiyeler 10'luk parçalarla, her parça 30 sn zincir okuma bütçesi altında okunur (T158 (i), düzeltme turu); bir parça düşerse tur düşer. Servis `/health`'i, `ethereum-rpc-latency-high`. Sonraki başarılı tur alarmı kapatır; backend yeniden başlarsa gösterge sıfırlanır |

### J.3 Uzun kesinti (servis 24 saatten uzun kapalı)

- **T158'den sonra ayrılan depozitler** kesintiden etkilenmez: backend her depozitin türetildiği andaki `finalized` bloğu (`PaymentAddresses.MonitorFromBlock`) saklar ve her izleme başlatmasında (dakikada bir yeniden kurma, iptal sonrası devir, açılış kurtarması) servise gönderir; servis depoziti o bloktan itibaren, sıfır bakiye tabanıyla okur.
- **İki şerit (T158 düzeltme turu, yönetici kararı — ⚠ PARA):** yeniden başlatmadan sonra her depozitin son ~24 saati (7.200 blok; taze depozitte yalnız kendi başlangıç bloğundan sonrası) **canlı şeritte** her tikte okunur — taze ödeme T158 öncesi kadar hızlı bildirilir (ölçüm: 15 sn), geçmiş ne kadar uzun olursa olsun. Başlangıç bloğundan oraya kadar olan geçmiş **tarih şeridinde**, arka planda, tikin yalnız ilk yarısında okunur. **Bedel artık her yeniden başlatmada taze ödemeye değil, yalnız bu arka plan okumasına düşer:** 30 günlük bir iptal sonrası depozitin geçmişi tempolu (200 CU/s, 10 blokluk aralık) ~7,4 sa, 8 günlük bir depozitinki ~1,9 sa sürer; maliyet aynıdır (30 günde ≈ 216.000 blok ≈ 43.200 `eth_getLogs` ≈ 2,6 M CU — Alchemy Free aylık 30 M CU; kota planlamasında sayın). Bu sürede o depozitin eski geçmişindeki bir geç ödeme geç bildirilir (kaybolmaz) ve depozit saatlik mutabakata girmez. İzleyin: `skinora_ethereum_monitor_history_deposits` (geçmişi okunan depozit sayısı) sıfıra inince arka plan okuması bitmiştir; `skinora_ethereum_monitor_catch_up_deposits` canlı şeridin geri okumasıdır (dakikalar).
- **T158'den önce ayrılmış depozitler** (`MonitorFromBlock` NULL) eski kurala bağlıdır: yeniden başlatmadan sonra yalnız `ETHEREUM_MONITOR_RESTART_LOOKBACK_BLOCKS` (7.200 ≈ 24 sa) geri okunur. Kesinti daha uzunsa değeri kesinti süresini kapsayacak kadar büyütüp servisi yeniden başlatın (§B), iş bitince eski değere döndürün.
- **İptal sonrası izleme yeniden kurulumu:** backend iptal sonrası (30 günlük) adresleri **yalnız kendi açılışında** servise yeniden kaydeder (dakikalık yeniden kurma yalnız aktif pencereyi kapsar). Ethereum servisi tek başına yeniden başlarsa iptal sonrası adresler backend yeniden başlayana kadar izlenmez — **Ethereum servisini yeniden başlattıktan sonra backend'i de yeniden başlatın.** (TRON'da da aynıdır; para kaybı yok, geç tespit — günlük mutabakat ~24 sa ya da sonraki backend açılışı. Kalıcı çözüm `DEFERRED_BACKLOG` §4 `PostCancelRearmOnlyOnBackendStart` — F9 öncesi kapanmalı.)

### J.4 Yanlış EVM ağına gelen para — elle kurtarma (08 §3.0 "Yanlış ağ", K6/K7)

Alıcı ödemeyi Ethereum depozit adresine ama başka bir EVM ağında (BSC, Arbitrum, Polygon, Base …) gönderdiyse para kaybolmaz: aynı anahtar her EVM ağında aynı adresi kontrol eder. **Otomatik kurtarma yoktur;** alıcı destek talebi açar.

1. **Talebi doğrulayın.** Alıcıdan zinciri ve işlem hash'ini alın; o ağın gezgininde (bscscan, arbiscan, polygonscan …) hash'in Skinora depozit adresine (işlemin `PaymentAddresses.Address`'i) gittiğini, token'ı ve tutarı doğrulayın. Depozitin `HdWalletIndex`'ini veritabanından alın. Hash başka bir adrese gidiyorsa kurtarılacak bir şey yoktur.
2. **İade adresini teyit edin.** Para alıcının **o ağdaki** bir adresine gönderilecek. Alıcıdan o ağdaki adresini yazılı alın ve sahipliğini teyit edin (ör. o adresten küçük bir test transferi ya da imzalı mesaj); borsa adresi ise borsanın o ağı desteklediğini doğrulayın.
3. **Anahtarı çevrimdışı türetin.** Ethereum tohumunun çevrimdışı yedeğini **ağ bağlantısı olmayan** bir makinede kullanın; yol `m/44'/60'/0'/0/{HdWalletIndex}` (BIP-32 kök düğümünden, BIP-39 parolası boş — 08 §3.7.2). Türetilen adresin depozit adresiyle aynı olduğunu doğrulayın. Tohumu ya da anahtarı hiçbir çevrimiçi araca, tarayıcı eklentisine, servise girmeyin. **TRON tohumu bu işlemde kullanılmaz** (K7).
4. **Gas'ı o ağda sağlayın.** Depozit adresine o ağın yerel token'ından (BNB, ETH, POL …) gönderim için gereken kadarını operatörün kendi cüzdanından gönderin. Skinora'nın yakıt / sıcak cüzdanını kullanmayın: onların her işlemi servisin günlüğüne girmelidir (§D adım 7 — Ethereum mainnet'teki nonce'ları etkilemez ama alışkanlık olarak ayrı tutun).
5. **Transferi imzalayın.** Çevrimdışı makinede, o ağın `chainId`'siyle, token kontratına `transfer(alıcı, tutar)` işlemini imzalayıp çevrimiçi makineden yayınlayın. Tutar: **gelen tutarın tamamı** iade edilir; kurtarma gas'ını platform üstlenir, alıcıdan kesinti ya da hizmet ücreti alınmaz (⚠ PARA — yönetici kararı, T158 düzeltme turu; 11 §F8 "T158 kararları").
6. **Kaydedin.** Destek talebine iki hash'i (gelen, iade), ağı, tutarı, işlemi yapan iki kişiyi yazın; admin audit notu düşün. Bu transferler Skinora defterine (`BlockchainTransactions`) girmez — Ethereum mainnet mutabakatını etkilemez.
7. **Anahtarı imha edin.** Türetilmiş özel anahtar çevrimdışı makinede bellekte kaldıysa makineyi kapatın; anahtarı hiçbir yere yazmayın.

İki kişi kuralı: adım 3–5'i iki kişi birlikte yapar (biri imzalar, öteki adres / tutar / zinciri sesli kontrol eder).

### J.5 Doğrulanamayan gönderim (`ethereum-transfer-status-unverified`)

Ne zaman olur: servisin gönderim günlüğü bir satırın hash'ini tanımıyor (günlük volume'ü kayboldu ya da eski yedekten geri yüklendi — §D adım 1–2) ve zincirdeki makbuz backend satırının beklediği `Transfer` olayını (kontrat, gönderici, alıcı, tam tutar) taşımıyor. Servis `UNKNOWN` döner; backend satırı **onaylamaz ve başarısız saymaz**: `DETECTED` kalır, `ErrorMessage` `TRANSFER_UNVERIFIED: …` (11 T158 (g) — ⚠ PARA, fail-closed).

1. Satırı bulun: `SELECT Id, TransactionId, Type, Network, TxHash, FromAddress, ToAddress, Amount, Token FROM BlockchainTransactions WHERE Status = 'DETECTED' AND ErrorMessage LIKE 'TRANSFER_UNVERIFIED%';`
2. Hash'i Etherscan'de açın; makbuzun `Transfer` olaylarını satırla karşılaştırın.
3. **Para satırın alıcısına gitmişse** (ör. sıcak cüzdandan, doğru tutar — günlük kaybolduğu için servis bilemedi; ya da satırın `FromAddress`'i boş bir payout ve backend'de `ETHEREUM_HOT_WALLET_ADDRESS` tanımlı değildi): satır zincirdeki hash'le elle kapatılır — T157'nin admin aracı gelene kadar elle (§D adım 7.4 ile aynı kural: iki kişi, audit notu). **Yeniden göndermeyin.**
4. **Para gitmemişse** (başka bir transfer, başka alıcı/tutar): o hash bu satırın gönderimi değildir. Satırın gerçek durumunu günlük yedeğinden ve zincirden (gönderen hesabın o nonce'taki işlemi) çıkarın; yeniden gönderim kararı yöneticinindir ve yeni kimlik ister (§D adım 3).
5. Günlüğü kurtardıysanız (yedekten geri yükleme) servis hash'i yeniden tanır ve satır kendiliğinden ilerler.

### J.6 Sweep sonrası iade (bilgi)

Sweep olmuş bir Ethereum işleminin alıcı iadesi (`BUYER_REFUND` — mutabakat geri alma, uyuşmazlık sonucu) **sıcak cüzdandan** çıkar (08 §3.7.6 "Kaynaklar", 11 T158 (f)): ücret tahmini sıcak cüzdanı fiyatlar, gönderim `source: "HOT_WALLET"` ile imzalanır, sıcak cüzdanın günlük çıkış tavanına sayılır, satırın `FromAddress`'i sıcak cüzdandır (defter mutabakatı onu sıcak cüzdan çıkışı sayar). Sweep yoldayken (`PENDING` / `DETECTED`) iade bekler (`SWEEP_IN_FLIGHT`, satır `PENDING`, deneme hakkı harcanmaz); sweep başarısızsa iade depozitten çıkar. ⚠ PARA: sweep yoldayken kuyruğa giren iade sıcak cüzdan ücretiyle fiyatlanır; sweep sonra başarısız olur ve iade depozitten (yakıtlı, daha pahalı) çıkarsa alıcı yine satırdaki tutarı alır, **gas farkını platform üstlenir** (yakıt cüzdanı öder). Gönderimde yeniden fiyatlanmaz — gönderim işi her denemede koşar, yeniden fiyat imzalanmış olabilecek bir satırın tutarını değiştirirdi (T152: bir kimlik, bir imza). Yalnız sweep başarısız olursa (platform tarafı arıza, nadir) olur (yönetici kararı, T158 düzeltme turu). Fazla ödeme, yanlış token ve geç ödeme iadeleri sweep'e girmediği için her zaman depozitten çıkar. TRON'da davranış değişmedi (iade hep depozitten). Sıcak cüzdanda USDT ve gas için ETH bulunmalıdır (§D adım 8).
