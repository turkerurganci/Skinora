# T170 — İzleme

**Faz:** F9 | **Durum:** ⏳ Yapım bitti, doğrulama bekliyor | **Tarih:** 2026-10-23

---

## Yapılan İşler

**Kabul kriteri:** ağ başına alarmlar (zincir ilerlemiyor, sağlayıcı hatası, yakıt/sıcak cüzdan bakiyesi) ve panolar üretim değerleriyle.

### Başlangıçtaki durum (ölçüldü — `rules.yml` 40 kural, 5 pano)

| Ne | TRON | Ethereum |
|---|---|---|
| Zincir ilerlemiyor | **YOK** — solid blok yalnız `/health` isteğinde okunuyordu, metrik yoktu | `ethereum-finalized-stalled` var (1800 sn) |
| Sağlayıcı hatası | `tron-api-errors-high` var ama **cevapsız istek (DNS/bağlantı reddi) sayılmıyordu** — TronGrid tamamen erişilemezse sayaç 0'da kalıyordu; kota kuralları var | `ethereum-monitor-scan-failing`, `ethereum-rpc-latency-high` var; **sağlayıcı başına hata oranı alarmı yok** (biri ölünce sessizce tek sağlayıcıya düşülüyordu) |
| Sıcak cüzdan üst limiti | `hot-wallet-threshold-breached` (T165) | **YOK** — izleyici yalnız TRON'u okuyordu (backlog `EthereumHotWalletUsdtLimitUnmonitored`) |
| Yakıt alt eşiği | TRX tabanı (aynı kural) | `ethereum-wallet-eth-low` var |
| Ağın servisi sağlıksız (ağ yeni işleme kapalı) | yalnız admin paneli (PLATFORM_OUTAGE_DETECTED); `blockchain-sidecar-down` yalnız süreç ölünce | yalnız admin paneli; **`up` kuralı da yok** |
| Panolar | 5 pano (T16): Business'ta var olmayan metrikler (`skinora_active_transactions` …), System'de yanlış iş adları (`job="skinora-steam"`, `skinora-blockchain`) ve kurulu olmayan cAdvisor serileri, Application'da yanlış etiket (`status` yerine `code`); **hiçbir ağ başına panel ve hiçbir Ethereum paneli yok** | |

Eşiklerin hiçbiri deneme/test değeri değildi; deneme yayını overlay'i (`docker-compose.trial.yml`) hiçbir alarm eşiğini ezmiyor (grep ile doğrulandı). Üretim değerleri ve kaynakları aşağıda.

### Eklenenler

1. **Ethereum sıcak cüzdan USDT limiti (⚠ PARA)** — `HotWalletMonitorService` artık `IBlockchainServiceRegistry.ConfiguredNetworks` üzerinde döner (mutabakat servisinin deseni): her ağın sıcak adresi (`IPlatformWalletAddressProvider.For(network)`) kendi servisinden okunur, USDT/USDC `hot_wallet_limit` ile karşılaştırılır. Gaz tabanı ağ başına kendi yerinde kaldı: TRX tabanı izleyicide, ETH tabanı Ethereum servisinin `ethereum-wallet-eth-low` alarmında (çift alarm yok — `ChecksGasFloor(network)`). AuditLog yükü ve SignalR `AdminHotWalletThresholdBreached` yüküne `network` alanı (eklemeli; frontend yalnız sorgu geçersizliyor). Bir ağın servisi kapalı / adresi yoksa yalnız o ağ atlanır. Yeni metrikler: `skinora_backend_hot_wallet_balance{network,asset}`, `skinora_backend_hot_wallet_threshold{network,asset,direction}` (pano çizgisi ayardan), `skinora_backend_hot_wallet_last_check_timestamp_seconds{network}` (ilk denemede tohumlanır).
2. **Ağın servisi sağlıksız** — `PlatformHealthMetrics`: sağlık probu her turda `skinora_backend_platform_component_up{component}` (ham sonuç) ve `skinora_backend_blockchain_service_degraded{network}` (debounce'lu kesinti durumu — dondurma ve "ağ yeni işleme kapalı" kuralının okuduğu) yayımlar; geri alma (`Revert`) sonrası yayımlanır. Kural `blockchain-service-degraded` (kritik, para). Ethereum'un ayrı bir `up` kuralı olmamasının boşluğunu da kapatır (yapılandırılmamış ağ probe edilmez → seri yok → yanlış alarm yok).
3. **TRON zincir ilerlemesi** — `sidecar-blockchain/src/chain/SolidBlockTracker.ts`: solid başı 60 sn'de bir okur (`TronGridClient.getNowSolidBlockHeader`, blok zaman damgasıyla), `skinora_blockchain_tron_solid_block_number`, `…_age_seconds` (scrape anında bloğun kendi zamanından), `…_stall_threshold_seconds`, `…_read_failures_total`. Hiç okuyamamışsa yaş açılıştan sayılır (seri hiç boş kalmaz). Kural `tron-solid-block-stalled` (kritik, para, oran > 1, 2 dk).
4. **TRON sağlayıcı hatası** — `TronGridClient.fetchResilient` cevapsız isteği `error_type="network"` olarak sayar; mevcut `tron-api-errors-high` artık erişilemeyen TronGrid'i görür.
5. **Ethereum sağlayıcı hatası** — kural `ethereum-rpc-provider-failing` (uyarı, para): sağlayıcı başına taşıma/zincir hatası oranı > 0,5 (10 dk pencere, 10 dk). `rpc` (talep hakkında cevap, ör. revert) ve `limit` (bölünüp tekrar denenir) sayılmaz.
6. **İzleyici kör** — kural `hot-wallet-monitor-stale` (uyarı, para): bir ağın sıcak cüzdanı 2 saattir okunamadıysa.
7. **Takılı enerji kilidi** (backlog `admin-alert-consumers` kalanı) — `skinora_blockchain_energy_delegation_stranded_total` (etiketsiz, açılışta 0) + kural `tron-energy-delegation-stranded` (uyarı, para; kilit uykudayken ateşlemez).
8. **Panolar** — yeni **Skinora — Networks (TRON + Ethereum)** (`skinora-networks`): `network` değişkeni, satırlar *Her iki ağ* / *TRON* / *Ethereum* / *Erişilebilirlik*; her alarm değeri servisin yayımladığı eşikle yan yana. Diğer beş pano gerçek metrik ve iş adlarına bağlandı (var olmayan iş metrikleri ve cAdvisor paneli kaldırıldı → backlog `BusinessMetricsNotPublished`), veri kaynakları `{type, uid}` ile sabitlendi, bozuk başlık kodlaması (`Skinora � …`) düzeltildi.
9. **Metrik adı bekçisi** — `scripts/grafana/dashboard-metrics.test.mjs` (CI "3c. Script test"): her pano sorgusunun ve her alarm ifadesinin, servislerin kaynakta tanımladığı (prom-client `name:` / prometheus-net `Metrics.Create*`) ya da kütüphanenin varsayılan toplayıcısının bir metriğini okuduğunu; `job` etiketlerinin Prometheus'ta olduğunu; Networks panosunun her alarm değerini eşiğiyle çizdiğini sınar.
10. **Ölçülen erişilebilirlik** (T166 notu) — Networks panosunda `avg_over_time(up[30d])` ve `avg_over_time(skinora_backend_platform_component_up[30d])`. Açılış sayfasına geri konmadı (⚠, aşağıda).

### Ağ başına kapsam (sonuç) ve üretim değerleri

Tam tablo DEPLOY_RUNBOOK §P.7'de. Özet:

| Eşik | Üretim değeri | Kaynak | Karar |
|---|---|---|---|
| Sıcak cüzdan üst limiti (ağ başına) | 4000 USDT | `hot_wallet_limit` (env, boşsa backend açılmaz — `SettingsBootstrapService` fail-fast) | T169 karar 10 (⚠) |
| TRON TRX tabanı | 1500 TRX | `hot_wallet.trx_balance_minimum` (seed 58) | T169 karar 11 (⚠) |
| Ethereum yakıt / sıcak ETH tabanı | 0,06 ETH | `ETHEREUM_*_WALLET_MIN_ETH` (seed 74/75 ile eş) | T142/T153 (⚠) |
| TRON solid blok durma eşiği | 300 sn (olağan 60–120 sn, canlı ölçüldü) | `TRON_SOLID_BLOCK_STALL_SECONDS` | **T170 (öneriyle kaydedildi — ⚠)** |
| Ethereum finalized durma eşiği | 1800 sn | `ETHEREUM_FINALIZED_STALL_SECONDS` | T142 ölçümü |
| TronGrid günlük bütçe | 100.000 | `TRONGRID_DAILY_REQUEST_BUDGET` | T169 karar 18 (⚠) |
| Ethereum sağlayıcı hata oranı | > 0,5, 10 dk | kural | **T170 (öneriyle kaydedildi — ⚠)** |
| Sıcak cüzdan izleyicisi bayat | 7200 sn | kural | **T170 (öneriyle kaydedildi — ⚠)** |
| Servis sağlıksız | 3 ardışık prob | `HealthProbe:FailureThreshold` | WP1 |
| Giden kuyruk satır eşiği | 25 / ağ | `OUTBOUND_PENDING_ROWS_ALARM_THRESHOLD` | T158 (⚠) |
| Ethereum gönderim bekleme | 10.800 sn | `ETHEREUM_FEE_CAP_WAIT_ALARM_SECONDS` | T152 |
| Depozitten iade günlük tavanı | 100 USDT / ağ | `blockchain.*.deposit_refund_daily_limit_usdt` | T169a (sahip) |

## Kararlar (öneriyle kaydedildi — ⚠)

- **⚠ PARA — TRON durma eşiği 300 sn.** Olağan solid yaşı canlı Nile'da 66–114 sn ölçüldü (19 blok × 3 sn + 60 sn okuma aralığı). 300 sn yavaş bir blok değil kesintidir; ödeme süresi (dakikalar) yanında 5 + 2 dk tepki yeterli. Düşük tutmak yanlış alarm, yüksek tutmak geç haber demek.
- **⚠ PARA — Ethereum sağlayıcı hatası oranı 0,5 / 10 dk.** Ölü sağlayıcı soğuma süresi başına bir kez sorulur ve her seferinde düşer (oran ≈ 1, canlı ölçüldü); ara sıra 429 alan sağlıklı sağlayıcı çok altında kalır. Uyarı (kritik değil): ödemeler diğer sağlayıcıdan sürer; ikisi de düşerse `ethereum-finalized-stalled` + `blockchain-service-degraded` kritik gelir.
- **⚠ PARA — izleyici bayat eşiği 2 saat.** Varsayılan 15 dk cron'da 8 kaçırılmış tur; admin cron'u 2 saatten seyrek yaparsa kural da yükseltilmeli (runbook'ta yazıyor).
- **Gaz tabanı çift alarm yok:** Ethereum ETH tabanı backend izleyicisine eklenmedi; Ethereum servisinin `ethereum-wallet-eth-low` kuralı (hem yakıt hem sıcak cüzdan) zaten var. Böylece 18 ondalık ölçek sorunu da doğmadı.
- **Ethereum `up` kuralı yok:** servis kurulmayan kurulumda sürekli ateşlerdi; backend'de yapılandırılmışsa `blockchain-service-degraded` yakalar (süreç ölünce prob başarısız olur).
- **Spam token alarm değil** (zincir gürültüsü, iade yok, para etkisi yok) → panoda *Gelen olaylar*. **İzleme `STOPPED`** iptal sonrası 30 günlük izlemenin olağan sonu → alarm değil. Böylece `admin-alert-consumers` satırı kapandı.
- **Uptime açılış sayfasına geri konmadı:** ölçüm makinenin içinden; makine düşünce ölçüm de durur. Kullanıcıya gösterilecek bir sayı dış ölçümden (UptimeRobot) gelmeli — ürün kararı, para/güvenlik etkisi yok.
- **İş metrikleri** (aktif işlem, tamamlanma oranı, günlük hacim) yayımlanmıyor; boş paneller kaldırıldı, backlog ⚪ `BusinessMetricsNotPublished` (raporlama; para etkisi yok).
- T170 kuralları ayrı grupta (`Skinora Network Alerts`, dosya sonu) — paralel dalların eklediği satırlara dokunmamak için (T165 deseni).

## Etkilenen Modüller / Dosyalar

- Backend: `Skinora.API/Services/HotWallet/HotWalletMonitorService.cs`, `IHotWalletThresholdObserver.cs`, yeni `Skinora.API/Monitoring/PlatformHealthMetrics.cs`, `PlatformHealthProbeJob.cs`, `Skinora.Realtime/Application/Contracts/NotificationRealtimePayloads.cs`.
- Backend test: `HotWalletMonitorServiceTests` (+5 test, EVM testi Ethereum ağına taşındı), `NotificationAlertingProvisioningTests` (+4 test, sınıflandırma 28 → 33), `PlatformHealthProbeJobTests` (gauge doğrulaması), `NotificationRealtimePublisherTests` (yük alanı).
- TRON servisi: yeni `src/chain/SolidBlockTracker.ts` (+ test), `metrics.ts`, `tron/TronGridClient.ts` (+ test), `wallet/EnergyDelegationService.ts` (+ test), `config/index.ts` (`positiveInt`), `index.ts`, `alertRuleWiring.test.ts`.
- Ethereum servisi: `alertRuleWiring.test.ts` (yeni kural; kod değişikliği yok).
- Frontend: `lib/signalr/events.ts` (`network` alanı).
- Grafana: `alerting/rules.yml` (+5 kural, yeni grup; `hot-wallet-threshold-breached` açıklaması), `dashboards/json/networks.json` (yeni) + 5 pano yeniden yazıldı.
- Script: yeni `scripts/grafana/dashboard-metrics.test.mjs`.
- Yapılandırma: `.env.example`, `docker-compose.yml` (`TRON_SOLID_BLOCK_POLL_INTERVAL_MS`, `TRON_SOLID_BLOCK_STALL_SECONDS`).
- Belgeler: `DEPLOY_RUNBOOK.md` (yeni §P.7, §A #18, §P.4), `05_TECHNICAL_ARCHITECTURE.md` v3.38 (§9.2/§9.3/§9.5), `07_API_DESIGN.md` v4.26, `04_UI_SPECS.md` v4.16, `DEFERRED_BACKLOG.md`, `DOC_CHANGELOG.md`, `IMPLEMENTATION_STATUS.md`, `STATUS_CHANGELOG.md`, `.claude/memory/MEMORY.md`, bu rapor.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Ağ başına alarm — zincir ilerlemiyor | ✓ | TRON 🆕 `tron-solid-block-stalled` (canlı ateşledi, aşağıda), Ethereum `ethereum-finalized-stalled` (var); `SolidBlockTracker.test.ts`, `alertRuleWiring.test.ts` |
| 2 | Ağ başına alarm — sağlayıcı hatası | ✓ | TRON `tron-api-errors-high` + 🆕 `network` sayımı (`TronGridClient.test.ts`), kota kuralları; Ethereum 🆕 `ethereum-rpc-provider-failing` (canlı ateşledi); iki ağ 🆕 `blockchain-service-degraded` (`PlatformHealthProbeJobTests`, provisioning testi) |
| 3 | Ağ başına alarm — yakıt / sıcak cüzdan bakiyesi | ✓ | Sıcak limit iki ağda (`HotWalletMonitorServiceTests` Ethereum testleri), TRX tabanı, ETH tabanı (var), 🆕 `hot-wallet-monitor-stale` |
| 4 | Panolar | ✓ | `networks.json` + 5 pano; `dashboard-metrics.test.mjs` 13/13; canlı Grafana'da panel verisi (aşağıda) |
| 5 | Üretim değerleriyle | ✓ | Eşik tablosu (her eşik için kaynak + karar); deneme overlay'i hiçbirini ezmiyor; pano çizgileri yayımlanan ayardan |
| 6 | Kapsamdaki backlog satırları | ✓ | `EthereumHotWalletUsdtLimitUnmonitored` ✅, `admin-alert-consumers` ✅, `uptime-cache-scaleout` notu (ölçüm T170'te), yeni ⚪ `BusinessMetricsNotPublished` |

## Canlı Ölçüm (Docker — ayrı compose projesi `t170mon`)

LIVE_PLACEHOLDER

## Test Sonuçları

TESTS_PLACEHOLDER

## Altyapı Değişiklikleri

- Migration: **Yok**.
- Config/env: **Var** — `TRON_SOLID_BLOCK_POLL_INTERVAL_MS` (60000), `TRON_SOLID_BLOCK_STALL_SECONDS` (300) — `.env.example`, `docker-compose.yml`. Geçersiz değer varsayılana düşer (`positiveInt`).
- Docker: **Var** (yalnız iki env satırı). Grafana provisioning: yeni kural grubu + yeni pano; Grafana yeniden başlatılınca yüklenir (DEFERRED_BACKLOG `GrafanaProvisioningRestartCoupling`).
- Sözleşme: SignalR `AdminHotWalletThresholdBreached` yüküne eklemeli `network` alanı; AuditLog `HOT_WALLET_THRESHOLD_BREACHED` yükünde `network`.

## Commit & PR

COMMIT_PLACEHOLDER

## Known Limitations / Follow-up

- Backend metrikleri (sıcak cüzdan, servis sağlığı, kuyruk) canlı ölçümde backend çalıştırılmadığı için Grafana'da boştu; kural/metrik bağlantısı birim testleriyle (`NotificationAlertingProvisioningTests`, `HotWalletMonitorServiceTests`, `PlatformHealthProbeJobTests`) ve metrik adı bekçisiyle doğrulandı. Uçtan uca T172 provasında (DEPLOY_RUNBOOK §P.7 doğrulama tablosu).
- `ethereum-wallet-eth-low` hiç okunamamış bir cüzdan için sessizdir (`or vector(1)`); RPC kesintisinde `ethereum-finalized-stalled` / `blockchain-service-degraded` haber verir. Değiştirilmedi.
- `hot-wallet-monitor-stale` cron'a bağlı değil (sabit 2 sa); cron 2 saatten seyrek yapılırsa kural elle yükseltilmeli.
- Backend yeniden başlarsa `last_check` serisi ilk denemeyle yeniden tohumlanır (yaş sıfırlanır) — kesintiyi en çok 2 saat geciktirir.
- İş metrikleri yayımlanmıyor → `BusinessMetricsNotPublished`.

## Notlar

- Working tree: temiz (dal `task/T170-izleme`, origin/main `44d32f54`'ten; görev Adım 6–7 yönetici tarafından yapıldı).
- Main CI startup: son 3 tamamlanmış main run — `39995711969` success (T169a CI), `39995712182` success (T169a Docker Publish), `39930836478` success (T174). T165 (`44d32f54`) run'ları başlangıçta sürüyordu (sayılmadı).
- Dış varsayımlar: (1) TronGrid `walletsolidity/getnowblock` cevabında `block_header.raw_data.timestamp` (ms) var — canlı Nile'da ölçüldü (yaş 66 sn); (2) prometheus-net 8 `UseHttpMetrics` etiketi `code` (prometheus-net README); (3) prom-client v15 varsayılan metrik adları — canlı TRON/Ethereum servislerinin `/metrics` çıktısında doğrulandı; (4) Grafana 11.3.0 provisioning'i yeni grubu ve panoları hatasız yükler — canlı ölçüldü ("finished to provision alerting/dashboards", 45 kural).
- Docker: WSL'de başka bir ajanın test konteyneri (testcontainers) çalışıyordu; `escrow` volume'larına ve başka konteynerlere dokunulmadı, ölçüm `t170mon` projesiyle yapılıp kapatıldı.
