# T165 — Bildirim kanalları ve dış alarm

**Faz:** F9 | **Durum:** ✓ Tamamlandı — ilk doğrulama ✗ FAIL (HEAD `f568396b`: 1 × S3 ⚠ PARA, 1 × S1 ⚠ GÜVENLİK, 6 × S4 — bkz. "Düzeltme turu"), yeniden doğrulama ✓ PASS (HEAD `869198a9`: 3 × S4, üçü ⚠ PARA — bkz. "Kapanış") | **Tarih:** 2026-10-23

---

## Yapılan İşler

- **Kanal durumu (kriter 1).** Yeni `NotificationChannelAvailability` (Skinora.Shared): her dış kanal (e-posta / Telegram / Discord) açılışta `Provider` + sırlarından **bir kez** değerlendirilir → `stub` (logging), `live`, `disabled` (yeni `Provider=disabled`), `misconfigured` (gerçek sağlayıcı seçili, sır boş / yer tutucu / biçimi yanlış — Resend `re_…`, `whsec_…`; BotFather `<rakam>:<35 karakter>`; Telegram webhook sırrı `A–Z a–z 0–9 _ -`, 1–256). Program.cs, Notifications ve Users modülleri aynı kararı kullanır: gerçek HttpClient yalnız `live`'da kaydedilir (gerçek istemciler boş anahtarda kurucu hatası verir). `misconfigured` kanal **kapalı gibi davranır** ama site açılır: açılış logunda hata satırı (eksik anahtar **adları**, değer asla), `skinora_backend_notification_channel_state{channel,state}` ve `notification-channel-misconfigured` alarmı (`NotificationChannelStatusReporter`). Dağıtıcı (`NotificationDispatcher`) kullanılamayan kanala teslimat satırı açmaz (tercih korunur). Ayarlar API'si (07 §5.6) her kanala `available` ekler; U7 `enabled:true`, U9, U10, U15 kullanılamayan kanalda **409 `CHANNEL_UNAVAILABLE`**; kapatma ve bağlantı kaldırma serbest.
- **Üretim seçimi (kriter 1, sahip kararı 2026-10-21).** `docker-compose.yml` backend'e `Resend__*`, `Telegram__*`, `Discord__Provider` env'lerini geçirir (geliştirmede varsayılan `logging`). Deneme yayını overlay'i `Resend__Provider=resend`, `Telegram__Provider=telegram`, `Discord__Provider=disabled` **zorlar** → anahtarlar `.env.trial`'a yazılıp backend yeniden başlatılınca kanal açılır. `.env.example`'a kanal + Grafana SMTP + heartbeat satırları.
- **Discord kapalı (kriter 1).** `Provider=disabled`: arayüzde Discord bağlama satırı ve bildirim anahtarı gizli (eskiden bağlamış kullanıcı yalnız "bağlantıyı kaldır"ı görür), bağlama ucu 409, teslimat yok. Telegram/e-posta yapılandırılmamışsa düğme/anahtar devre dışı + "şu an kullanılamıyor" notu (4 dil).
- **Sağlayıcı sözleşmesi (kriter 1).** Resmî API'ye karşı testler eklendi ve **iki üretim hatası** bulundu/kapandı:
  - **Resend `User-Agent`:** Resend `User-Agent`'sız isteği geçerli anahtarla bile **403** (hata 1010) ile reddeder (resend.com/docs/knowledge-base/403-error-1010); .NET `HttpClient` başlık göndermez → T165'ten önce üretimdeki **her e-posta kalıcı hata** alacaktı. `ResendEmailClient` artık `User-Agent: Skinora/1.0` gönderir. Gövde biçimi (`from`, `to[]`, `subject`, `html`, `text`, `tags[{name,value}]`) ve belgelenen hata gövdeleri (`missing_api_key`, `invalid_api_key`, doğrulanmamış alan adı 403, `invalid_from_address`, `daily_quota_exceeded`, `rate_limit_exceeded`, 500) testli.
  - **Telegram token loglanıyordu (⚠ GÜVENLİK):** Bot API token'ı URL yolunda (`/bot<token>/…`); `IHttpClientFactory`'nin varsayılan kayıt işleyicileri bu URL'yi Information seviyesinde console + Loki'ye yazardı. `TelegramTransportRegistration` istemciyi `RemoveAllLoggers()` ile kaydeder; test karşılaştırmalı (varsayılan kayıt token'ı loglar — ölçüldü; bizimki loglamaz). `sendMessage` / `setWebhook` gövdeleri (`chat_id`, `parse_mode`, `secret_token`, `allowed_updates`, `drop_pending_updates`), 401 (iptal edilmiş token) / 404 / 400 kalıcı, hata mesajında token yok — testli.
- **Para alarmları (kriter 2).** Genel **"para alarmı" kategorisi** iki yolda:
  - **Grafana:** `category: money` etiketi (23 mevcut zincir/sweep/payout/iade/cüzdan/ücret kuralı + 3 yeni) → policies.yml'de **ilk** rota → yeni `money-alarm` contact point'i (**Telegram + e-posta birlikte**), ilk bildirim ~10 sn, sürdükçe 4 sa'te bir, bitince "resolved". Yeni kurallar ayrı grupta, dosyanın başında (paralel T164/T169a ekleriyle çakışmasın): `outbound-awaiting-approval` (T169 onay kapısında bekleyen ödeme/iade — yeni `skinora_backend_outbound_awaiting_approval_rows|oldest_seconds{network}`, dispatch işi her dakika ağ başına yayımlar), `hot-wallet-threshold-breached` (T77 izleyicisi artık `skinora_backend_hot_wallet_threshold_breached{network,asset,direction}` yayımlar; önceden yalnız AuditLog + açık paneldeki SignalR bandı), `admin-money-alarm-raised` (backend para olayları), `notification-channel-misconfigured`, `notification-delivery-exhausted` (yeni `PrometheusNotificationAdminAlertSink` — denemeleri tükenen teslimat).
  - **Backend:** `IAdminMoneyAlarmObserver` (Skinora.Shared) — `AdminBroadcastNotificationConsumerBase`'in para kurucusu; para tüketicileri (`TransferDispatchFailed`, `RefundBlocked`, `SellerPayoutDeferred`, `SettlementReview`) panel + her admin'in kendi e-posta/Telegram kanalına gitmeye ek olarak `skinora_backend_admin_money_alarm_last_seconds{kind}` yayımlar → `admin-money-alarm-raised` 1 saat boyunca ateşler — **admin yok / kanal bağlamamış olsa da** (olay alıcılar çözülmeden önce raporlanır). Yeni para alarmı = Grafana'da `category: money`, backend'de bu kurucu; testler listeyi sabitler.
  - **Grafana e-postası:** `contactpoints.yml` başlığındaki SMTP eksiği kapandı — `GF_SMTP_*` → Resend SMTP (`smtp.resend.com:587`, STARTTLS zorunlu, kullanıcı `resend`, parola ayrı Sending access anahtarı); deneme yayınında `GF_SMTP_ENABLED=true`.
  - **Grafana mesaj şablonu hatası kapandı:** T16 şablonları Helm tarzı backtick kaçışıyla (`{{` + backtick …) yazılmıştı; dosyayı Grafana'dan önce şablon olarak işleyen bir adım yok → Grafana iç metni **olduğu gibi** basardı (her alarm ham şablon koduyla gelirdi). Düz Grafana şablonları; testle sabit.
- **Dış izleme (kriter 3).** Araştırma (resmî sayfalar, 2026-10-23) + seçim (⚠): **Healthchecks.io heartbeat birincil** (PC/Docker/yığın/internet düşünce ping durur → e-posta + Telegram), **UptimeRobot HTTP ikincil** (tünel/DNS yolu). Yeni `skinora-heartbeat` (deneme yayını overlay'i, `curlimages/curl:8.21.0`, `scripts/heartbeat/heartbeat.sh`): dakikada bir `http://skinora-reverse-proxy/api/health` + ön sayfa; ikisi 2xx → ping; 3 tur üst üste hata → **bir kez** `/fail`; `restart: unless-stopped` (yeniden başlatmada Docker Desktop ile döner), URL yoksa bekler, URL'yi asla yazmaz, port yok. Herkese açık **`/api/health`** yalnız `{"status":"…"}` (DB + Redis, 200/503, `no-store`); nginx'in açık `/health`'i artık bu uca gider — ayrıntılı `/health` (kontrol adları + hata metni) **internete açıktı** (⚠ GÜVENLİK), şimdi yalnız compose ağı içinden.
- **Belgeler:** DEPLOY_RUNBOOK yeni **§P** (P.1 durumlar, P.2 Resend adımları, P.3 Telegram + sohbet kimliği, P.4 para alarmı listesi ve kanal eşlemesi, P.5 dış izleme kurulumu, P.6 T172 doğrulama adımları); RESEND/TELEGRAM/DISCORD runbook notları; 05 §9.3/§9.5 (v3.34); 07 §5.6/§5.7/§5.9/§5.11/§5.12 (v4.24); DOC_CHANGELOG; DEFERRED_BACKLOG `SharedSecretsInPlainEnv`'e T165 sırları eklendi (T171'de aynı taşıma); CI backend filtresine `infra/grafana/**`.

## Etkilenen Modüller / Dosyalar

- **Backend yeni:** `Skinora.Shared/Notifications/NotificationChannelAvailability.cs`, `…/IAdminMoneyAlarmObserver.cs`; `Skinora.API/Monitoring/NotificationChannelStatusReporter.cs`, `…/PrometheusAdminMoneyAlarmObserver.cs`, `…/PrometheusNotificationAdminAlertSink.cs`; `Skinora.API/HealthChecks/PublicHealthResponseWriter.cs`; `Skinora.API/Configuration/TelegramTransportRegistration.cs`; `Skinora.API/Services/HotWallet/IHotWalletThresholdObserver.cs`.
- **Backend değişen:** `Program.cs`, `Configuration/UsersModule.cs`, `Configuration/TransactionsModule.cs`, `Controllers/UsersController.cs`, `Monitoring/PrometheusOutboundQueueObserver.cs`, `Services/HotWallet/HotWalletMonitorService.cs`, `Skinora.Shared/Email/ResendEmailClient.cs`, Notifications: `NotificationsModule.cs`, `NotificationDispatcher.cs`, `AdminBroadcastNotificationConsumerBase.cs` + 4 para tüketicisi; Transactions: `IOutboundQueueObserver.cs`, `OutgoingTransferDispatchJob.cs`; Users: `AccountSettingsService.cs`, `SettingsDtos.cs`, `SettingsErrorCodes.cs`.
- **Backend test:** yeni `Shared.Tests/Unit/Notifications/NotificationChannelAvailabilityTests.cs`, `API.Tests/Integration/PublicHealthEndpointTests.cs`, `API.Tests/Unit/Notifications/TelegramTransportRegistrationTests.cs`, `API.Tests/Unit/Monitoring/NotificationAlertingProvisioningTests.cs`; değişen `ResendEmailClientTests`, `TelegramBotClientTests`, `AdminAlertNotificationConsumerTests`, `NotificationDispatcherTests`, `AccountSettingsEndpointTests`, `HotWalletMonitorServiceTests`, `OutgoingTransferDispatchJobTests`.
- **Frontend:** `lib/api/settings.ts`, `components/settings/LinkedAccountsSection.tsx`, `NotificationPreferencesSection.tsx`, yeni `NotificationChannelAvailability.test.tsx`, `i18n/messages/{en,tr,es,zh}.json` (+2 anahtar).
- **Altyapı:** `infra/grafana/provisioning/alerting/{contactpoints,policies,rules}.yml`, `docker-compose.yml`, `docker-compose.trial.yml`, `.env.example`, `nginx/nginx.conf`, yeni `scripts/heartbeat/heartbeat.sh` + `heartbeat.test.mjs`, `scripts/compose-exposure.test.mjs`, `.github/workflows/ci.yml`.
- **Belgeler:** `DEPLOY_RUNBOOK.md` §P, `INTEGRATION_RUNBOOKS/{RESEND,TELEGRAM,DISCORD}_SETUP.md`, `05` v3.37 (dalda v3.35; kapanışta main T169a ile yeniden numaralandı), `07` v4.25 (dalda v4.24), `DOC_CHANGELOG.md`, `DEFERRED_BACKLOG.md`, `IMPLEMENTATION_STATUS.md`, `STATUS_CHANGELOG.md`, `.claude/memory/MEMORY.md`.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1a | E-posta / Telegram üretimde ortam ayarıyla açılabiliyor | ✓ | Overlay `Resend__Provider=resend`, `Telegram__Provider=telegram` zorlar; sırlar `.env.trial`'dan (`docker-compose.yml` backend env). `NotificationChannelAvailabilityTests.RealProviders_WithCompleteSecrets_AreLive`; compose testi `T165: production notification channels …` (CI, `docker compose config`) |
| 1b | Anahtar yoksa açılış davranışı net (fail-closed değil, alarm + log) | ✓ | `misconfigured`: site açılır, kanal kapalı; `…_WithEmptySecrets_AreMisconfigured_AndNameEveryMissingKey`, `…PlaceholderOrMalformedToken…`, `…UnknownProviderValue…`, `Problems_NeverCarryTheSecretValue`; dağıtıcı `DispatchAsync_UnavailableChannels_GetNoDeliveryRow_AndNoJob`; uçlar `UnavailableChannel_ConnectOrVerify_Returns409ChannelUnavailable` (3 kanal), `…CannotBeSwitchedOn_ButCanBeSwitchedOff`; alarm `ChannelMisconfiguredRule_ReadsTheStateTheReporterPublishes` |
| 1c | Sırlar T163 desenine tutarlı | ✓ | Ortak sırlar gibi env (`SharedSecretsInPlainEnv` → T171; satıra T165 sırları eklendi); Telegram token loglara yazılmıyor (`TelegramTransportRegistrationTests`, karşılaştırmalı) |
| 1d | Discord üretimde kapalı, arayüzde gizli/devre dışı | ✓ | Overlay `Discord__Provider=disabled`; `GetSettings_EveryChannelCarriesAvailable`; vitest `hides Discord entirely…`, `keeps a still-connected Discord visible…`, `has no Discord toggle…`; uç 409 |
| 1e | Sağlayıcı istemcileri gerçek API sözleşmesine göre (mock HTTP: istek + hata biçimi) | ✓ | `ResendEmailClientTests` (+`User-Agent`, gövde şekli, 7 belgeli hata gövdesi), `TelegramBotClientTests` (+URL yolu, `sendMessage`/`setWebhook` gövdeleri, 401/404/400, token mesajda yok) |
| 2a | Para alarmları panelden başka kanalla (genel kategori) | ✓ | `NotificationAlertingProvisioningTests`: 26 para kuralı `category: money` (liste sabit), policy ilk rota `money-alarm` (continue yok, 4 sa), contact point Telegram + e-posta, şablonlar düz; backend `MoneyAlarmCategory_IsExactlyTheMoneyConsumers`, `MoneyAlarm_IsRaised_EvenWhenNoAdminExists`, `…OncePerEvent_NotPerAdmin_AndNotOnReplay`; para admin bildirimleri admin'in kendi kanallarına mevcut dağıtıcı yoluyla |
| 2b | Grafana e-postası için yol (SMTP) + gerekçe | ✓ | `GF_SMTP_*` → Resend SMTP (base kapalı, overlay açık); compose testi; gerekçe §P.2 |
| 2c | T169 onay kapısında bekleyen ödeme/iade için admine bildirim, tekrar sınırlı | ✓ | `T165_Awaiting_Approval_Is_Reported_Per_Network_And_Clears_On_Approval`, `T165_Gate_Open_Reports_Nothing_Awaiting_Approval`, `AwaitingApprovalRule_ReadsThePublishedGauge`; tekrar: bekleme başlayınca 1, sonra 4 sa'te bir + "resolved" (policy) |
| 3a | Site çökerse haber veren dış izleme — araştırma + seçim | ✓ | §P.5 (Healthchecks.io + UptimeRobot; Cloudflare Health Checks ücretsizde yok, Better Stack Telegram yok — resmî sayfalar aşağıda) |
| 3b | Herkese açık sağlık ucu, sır/iç bilgi sızdırmıyor | ✓ | `PublicHealthEndpointTests` (200 `{"status":"healthy"}` + `no-store`; 503 `{"status":"unhealthy"}`, kontrol adı / ana makine adı yok; karşılaştırma: ayrıntılı `/health` hâlâ içeriyor); nginx `location = /health` → `/api/health` |
| 3c | Heartbeat yönü, PC yeniden başlayınca otomatik devam | ✓ | `heartbeat.test.mjs` 7/7 (ping, URL basılmaz, 3 turda tek `/fail`, ön sayfa da sayılır, bağlantı reddi çökme değil, URL'siz bekler, geçersiz ayar çıkış 2); compose testi `T165: the heartbeat publishes nothing, restarts with Docker …` (`unless-stopped`, salt-okunur betik, port yok) |
| 3d | Hesabı sahip açacak: adımlar runbook'ta | ✓ | DEPLOY_RUNBOOK §P.2 / §P.3 / §P.5, doğrulama §P.6 |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend build | ✓ 0 hata | `dotnet build Skinora.sln` |
| Backend — Shared.Tests | ✓ 622/622 | `dotnet test tests/Skinora.Shared.Tests` |
| Backend — Notifications.Tests | ✓ 214/214 | unit + integration (LocalDB) |
| Backend — Users.Tests | ✓ 49/49 | |
| Backend — Transactions.Tests | ✓ 1884/1884 | unit + integration (LocalDB), 7 dk 22 sn |
| Backend — API.Tests | ✓ 1007/1007 | unit + integration (SQLite / LocalDB), 8 dk 27 sn. Secret-guard düzeltmesinden sonra (iki test satırı yeniden adlandırıldı, `FromConfiguration` yansıma ile) yeniden: Shared 622/622, API hedefli 74/74, dağıtıcı 9/9 |
| `dotnet format --verify-no-changes --severity error` (değişen dosyalar) | ✓ | çıktı yok |
| Frontend | ✓ | `eslint` 0, `tsc --noEmit` temiz, `i18n:check` parity OK (4 × 1700), `vitest` 47 dosya / 469 test, `next build` ✓ |
| Betik — heartbeat | ✓ 7/7 | `node --test scripts/heartbeat/heartbeat.test.mjs` (sh + curl) |
| Betik — compose-exposure | ? yerelde koşulmadı | Docker kullanma talimatı; CI "3c. Script test" koşar (yeni 3 test) |
| Grafana YAML | ✓ ayrıştı | PyYAML (yalnız sözdizimi): 3 grup (5 + 13 + 15 kural), 26 para kuralı, 4 contact point |

## Düzeltme turu (2026-10-23 — doğrulama ✗ FAIL HEAD `f568396b` sonrası)

Doğrulama raporu: S1 1 · S3 1 · S4 6. Her bulgu → yapılan → kanıt.

### Bulgu 1 — S3 ⚠ PARA: mutabakat uyuşmazlığı yalnız panelde (FAIL nedeni)

**Yapılan.** Sıcak cüzdan eşiğiyle aynı desen, iki bağımsız yol:
- **Metrik + Grafana para kuralı:** `IReconciliationRunObserver.ReportMismatches(network, scope, n)` → `skinora_backend_reconciliation_mismatches{network,scope}` (`PrometheusReconciliationRunObserver`). Tur, yalnız gerçekten karşılaştırdığı kapsamı raporlar: deposit her tamamlanan turda (aktif depozit yoksa 0), sıcak/soğuk cüzdan yalnız adresi ayarlı ve anlık görüntüde varsa; yarıda kalan tur ya da karşılaştırılmayan kapsam **hiçbir şey** raporlamaz — kontrol edilmemiş cüzdan "dengede" okunmaz, son değer kalır. Yeni kural `reconciliation-mismatch` (T165 grubunda, `max by (network, scope) (…) or vector(0) > 0`, `for: 0s`, **critical, `category: money`** → `money-alarm`: Telegram + e-posta); aynı kapsamın sonraki dengeli turu 0 yazar → "çözüldü". Özet: `Reconciliation: TRON HotWallet does not match the chain (2 mismatch(es))`.
- **Mevcut admin bildirim yolu:** tur başına (ağ, ≥1 uyuşmazlık) bir `ReconciliationMismatchAdminAlertEvent` outbox'a → yeni `ReconciliationMismatchAdminNotificationConsumer` (WP8 admin-broadcast) → yeni **`ADMIN_RECONCILIATION_MISMATCH`** (NotificationType sona eklendi, 29 değer; şablon 4 dilde; e-posta kategorisi Account; hedef `null` — ağ başına) her admin'in gelen kutusuna **ve** bağladığı e-posta/Telegram'a. İşleme bağlı `ADMIN_PAYMENT_FAILURE` kullanılamadı (şablonu `{TransactionId}` ister). Tüketici **para gözlemcisi olmadan** kuruldu: operatör yolu kendi metriği ve "çözüldü"lü kuralı; gözlemci verilse aynı uyuşmazlık bir de `admin-money-alarm-raised` olarak gelirdi (`MoneyAlarmCategory_IsExactlyTheMoneyConsumers` bunu sabitler). Uyuşmazlık sürdükçe her günlük tur yeni admin bildirimi üretir (hatırlatma).
- Belgeler: 05 §3.3 + §9.3, 06 §2.13 (v6.39; dalda v6.38), 07 §8.1, DEPLOY_RUNBOOK §P.4.

**Kanıt.**
- `ReconciliationServiceTests` +5: `T165_Mismatch_IsReportedPerScope_AndRaisesOneAdminEvent`, `T165_BalancedRound_ReportsZeroForEveryComparedScope_SoTheAlarmResolves_AndRaisesNoEvent`, `T165_ScopeNotCompared_ReportsNothing_SoAnUncheckedWalletNeverReadsBalanced`, `T165_AbortedRound_ReportsNoMismatchCount_AndNoEvent`, `T165_EthereumMismatch_CarriesItsNetwork`.
- `NotificationAlertingProvisioningTests.ReconciliationMismatchRule_ReadsThePublishedGauge_PerNetworkAndScope` (gösterge değeri var/yok, ağ başına; kural doğru metrik + etiketleri okuyor, `gt 0`, critical); `MoneyRules` listesine eklendi (27 para kuralı; `OnlyTheListedRules_AreMoneyRules`).
- `AdminAlertNotificationConsumerTests.ReconciliationMismatch_FansOutToEveryAdmin_WithNetworkAndCount` (+ yeniden teslimatta tekrar yok), `NotificationTargetMapperTests` +1, `EnumTests` 28 → 29, şablon eşliği (`NotificationTemplateParityTests`) 4 dil.
- `MoneyAlarmCompositionTests.Host_ReconciliationMismatch_ReachesTheAdminConsumer` (gerçek host: MediatR olayı tüketiciye ulaşıyor), `Host_PublishesReconciliationRounds_ToPrometheus`.
- **Canlı (Grafana 11.3.0 + Prometheus 2.53.3 + sahte metrik ucu + mailpit):** `…mismatches{network="TRON",scope="HotWallet"} 2` → `Ledger And Chain Do Not Match` TRON/HotWallet **Alerting**, DepositAddress Normal; `ALERT_EMAIL_TO`'ya `[Skinora] PARA ALARMI (1)` gövdede `Reconciliation: TRON HotWallet does not match the chain (2 mismatch(es))`. Değer 0'a çekilince 16:02:11'de `[Skinora] Para alarmı çözüldü` (gövde: `1 resolved … Ledger And Chain Do Not Match`).

### Bulgu 2 — S1 ⚠ GÜVENLİK: Grafana ilk açılışta alarmları `example@email.com`'a gönderiyor

**Kök neden (ölçüldü).** Boş veritabanında Grafana 11.3 Alertmanager'ı **provisioning'den önce** kendi varsayılan yapılandırmasıyla başlatır (`configHash=d2c56fac…` — tek rota `grafana-default-email` → `<example@email.com>`), provisioning yönlendirmemizi veritabanına yazar ama çalışan Alertmanager onu ancak bir sonraki yoklamada yükler (`alertmanager_config_poll_interval`, 60 sn); kural zamanlayıcısı ise provisioning biter bitmez başlar. 11.3'te varsayılan yapılandırma ayarlanamıyor (ikili dosyada / defaults.ini'de anahtar yok — bakıldı); kök politikayı ya da `grafana-default-email` adını provision etmek çalışan bellek içi yapılandırmayı değiştirmez, yoklama aralığını düşürmek pencereyi daraltır ama kapatmaz.

**Yapılan (kalıcı).** `scripts/grafana/first-boot.sh` — `skinora-grafana`'nın giriş noktası (`docker-compose.yml`, salt-okunur bağlı): veri volume'unda `.skinora-alerting-provisioned` işareti yoksa Grafana bir kez **kural çalıştırma kapalı** (`GF_UNIFIED_ALERTING_EXECUTE_ALERTS=false`) ve yalnız loopback'te açılır; log `msg="finished to provision alerting"` deyince SIGTERM ile kapatılır, işaret yazılır, sonra normal açılır. Gerçek açılışta Alertmanager yapılandırmayı **başlarken** veritabanından (bizimki) yükler, zamanlayıcıdan önce. Başarısızlıkta (Grafana provisioning bitmeden düşer / 300 sn) çıkış ≠ 0, işaret yazılmaz, Grafana açılmaz (container yeniden dener). Sonraki açılışlarda koruma atlanır.

**Kanıt — canlı, boş volume, sahte SMTP (mailpit) ile; pencere bilerek 5 dakikaya büyütüldü (`GF_UNIFIED_ALERTING_ALERTMANAGER_CONFIG_POLL_INTERVAL=5m`) ki sonuç zamanlama şansına kalmasın:**

| Koşu | Yapılandırma | Alertmanager | Sonuç (mailpit alıcıları) |
|---|---|---|---|
| 0 | korumasız, yoklama 60 sn (üretim) | 15:50:34 varsayılan `d2c56fac…` → 15:51:39 bizimki | bu koşuda alarmlar 15:51:49'dan sonra ateşledi: 4 e-posta `ops@skinora.test` — yarış şansa bağlı (doğrulayıcının koşusunda 3 alarm `example@email.com`'a gitti) |
| 1 | **korumasız**, yoklama 5 dk | 15:55:07 varsayılan, provisioning 15:55:09, zamanlayıcı 15:55:09 | **3 e-posta `example@email.com`'a**: `[FIRING:2] DatasourceNoData …`, `[FIRING:1] Backend Money Alarm Raised …`, `[FIRING:1] Hot Wallet Beyond Its Threshold …` (15:56:00–15:56:12) — hata tekrarlandı |
| 2 | **korumalı**, yoklama 5 dk | korumalı koşu 15:57:08 varsayılan → provisioning 15:57:08 → kapatıldı; gerçek açılış 15:57:11 **bizim** yapılandırma (`da1dfc9f…`) → zamanlayıcı 15:57:11 | **0 e-posta `example@email.com`'a**; 5 e-posta `ops@skinora.test` (para alarmları + `reconciliation-mismatch` + Steam NoData); konu satır sonsuz `[Skinora] PARA ALARMI (1)` |
| 2b | koşu 2'nin container'ı yeniden başlatıldı | koruma atlandı (log'da `skinora:` satırı yok), Alertmanager açılışta bizim yapılandırma | — |

Testler: `scripts/grafana/first-boot.test.mjs` 4/4 (sahte `/run.sh`: boş volume → önce `execute_alerts=false addr=127.0.0.1`, durdurma, sonra normal açılış + işaret; işaretli volume → doğrudan açılış; erken ölüm → çıkış ≠ 0, işaret yok, açılış yok; zaman aşımı → durdurma, işaret yok). `compose-exposure.test.mjs` +1: `skinora-grafana` giriş noktası betik, salt-okunur bağlama, veri volume'u kalıcı. Runbook §P.4 (koruma) + §P.6 adım 8.

### Bulgu 3 — S4: para e-postası konusunun sonunda satır sonu

**Yapılan.** `contactpoints.yml` `subject: |` → `subject: >-`. **Kanıt:** canlı koşu 2'de konu baytları `b'[Skinora] PARA ALARMI (1)'` (önce `…(1)\n`); `NotificationAlertingProvisioningTests.MoneyEmailSubject_HasNoTrailingNewline`.

### Bulgu 4 — S4 ⚠ PARA: `PrometheusAdminMoneyAlarmObserver` kaydı silinince sessiz ölüm (M24)

**Yapılan / kanıt.** `Integration/MoneyAlarmCompositionTests` (gerçek API host'u, `HangfireBypassFactory`): `Host_PublishesBackendMoneyAlarms_ToPrometheus` (`IAdminMoneyAlarmObserver` → Prometheus uygulaması), `Host_MoneyConsumers_GetThePrometheusObserver` (host'tan çözülen `RefundBlocked` tüketicisi `IsMoneyAlarm`), `Host_PublishesReconciliationRounds_ToPrometheus`, `Host_ReconciliationMismatch_ReachesTheAdminConsumer`. M24 bozması artık yakalanıyor (aşağıda).

### Bulgu 5 — S4: bozmada kaçan 4 test boşluğu

| Bozma | Yapılan |
|---|---|
| M23 U7 Telegram `enabled:true` 409 | `AccountSettingsEndpointTests.UnavailableTelegram_LinkedButOff_CannotBeSwitchedOnAgain` (bağlı ama kapalı Telegram, kanal kullanılamaz → 409, tercih kapalı kalır). Bağlı tercih doğrudan veritabanına yazılır: connect + webhook yolu sınıfın paylaşılan anonim hız sınırını tüketip Discord callback testini 429'a düşürüyordu (ölçüldü) |
| M25 misconfigured Telegram'a gerçek işleyici | `Notifications.Tests/Unit/Channels/ChannelHandlerRegistrationTests` (misconfigured → yalnız `LoggingTelegramNotificationChannelHandler`; live → `TelegramNotificationChannelHandler`, karşılaştırma) |
| M26 misconfigured logu Error → Information | `API.Tests/Unit/Monitoring/NotificationChannelStatusReporterTests.MisconfiguredChannel_IsLoggedAsError_WithKeyNamesOnly` |
| FE2 Telegram anahtarı "kullanılamıyor"da devre dışı değil | vitest `disables a connected Telegram toggle with the note while Telegram is unavailable` + karşılaştırma `keeps a connected Telegram toggle usable while Telegram is available` |

### Bulgu 6 — S4 ⚠ PARA: aynı türde ikinci para olayı bir saat içinde ayrı bildirim üretmiyor

**Karar (öneriyle kaydedildi — ⚠ PARA): olay kimliği grup anahtarına eklenmedi; davranış korunur, belgelendi.** Gerekçe: (1) her olay zaten her admin'e ayrı bildirim (işlem kimliğiyle) panelde ve admin'in kanallarında gelir — operatör alarmının işi "para tarafında bakılacak bir şey var", olay defteri değil; (2) olay başına etiket Prometheus'ta sınırsız seri; (3) bir arıza dalgası (ör. sıcak cüzdan boşken 30 ödemenin tükenmesi) operatöre 30 e-posta atar ve kullanıcı bildirimleriyle paylaşılan günlük 100 Resend kotasını tüketir. Alarmın bir saatlik penceresi son olaydan yeniden başlar, sürdükçe 4 sa hatırlatma. DEPLOY_RUNBOOK §P.4 "Aynı türde ikinci olay".

### Bulgu 7 — S4: nginx `/health` değişikliğinin otomatik testi yok

**Yapılan / kanıt.** `scripts/nginx/edge-health.test.mjs` (CI "3c. Script test" glob'u kapsar; docker yoksa yerelde atlanır, CI'da kırmızı — compose-exposure düzeni): gerçek `nginx:alpine` + repo `nginx/nginx.conf` + URI'yi yankılayan iki sahte upstream, istemci normalleştirmesi olmadan ham yol. `/health`, `//health`, `/./health`, `/api/../health`, `/api/%2e%2e/health`, `/hubs/../health` → `backend /api/health`; `/health/`, `/health/x`, `/HEALTH`, `/healthz` → frontend; `/api/health` değişmeden backend'e. Yerelde 11/11.

### Bulgu 8 — S4: main birleştirmesi

**Yapılan.** `origin/main` `792f8ed2` (T164 #372, T174 #374) dala birleştirildi (`4c468184`). Çakışmalar yalnız belgelerde: `.claude/memory/MEMORY.md` (iki tarafın satırları), `05` (ikisi de v3.34 → T165 **v3.35**, DOC_CHANGELOG notu), `DEPLOY_RUNBOOK` başlığı (T165 cümlesi; bölüm sırası §L T163 · §M T169 · §N T164 · §O T171 · §P T165), `DOC_CHANGELOG` (iki girdi korundu, T165 üstte), `IMPLEMENTATION_STATUS` (main'in T164 ✓ satırı + T165 satırı), `STATUS_CHANGELOG` (main tarafı + T174 ✓ satırı en üste; T165 dalındaki T168 tekrar girdisi düşürüldü — main'deki daha ayrıntılı). Kod/altyapı otomatik birleşti (`rules.yml`: T164'ün iki kuralı para dışı — `OnlyTheListedRules_AreMoneyRules` yeşil). DEFERRED_BACKLOG sayımı (dosyanın kendi yöntemi, §1'den itibaren, ilk hücre): **50 aktif / 167 çözülmüş** — main ile aynı (T165 yalnız `SharedSecretsInPlainEnv` satırını genişletti; düzeltme turu satır açmadı/kapatmadı). 06 v6.37 → v6.38 (T165), 07 v4.24 (main 4.23; çakışma yok).

### Bozma turu (düzeltme turu)

| Bozma | Sonuç | Test çıktısı |
|---|---|---|
| S3-1 hot scope not reported | yakalandı | Failed:     2, Passed:    58, Skipped:     0, Total:    60 |
| S3-2 deposit always 0 | yakalandı | Failed:     1, Passed:    59, Skipped:     0, Total:    60 |
| S3-3 no admin event | yakalandı | Failed:     3, Passed:    57, Skipped:     0, Total:    60 |
| S3-4 event also when balanced | yakalandı | Failed:     1, Passed:    59, Skipped:     0, Total:    60 |
| S3-5 unchecked hot reported as 0 | yakalandı | Failed:     2, Passed:    58, Skipped:     0, Total:    60 |
| S3-6 gauge always 0 | yakalandı | Failed:     1, Passed:    59, Skipped:     0, Total:    60 |
| S3-7 rule loses category: money | yakalandı | Failed:     2, Passed:    58, Skipped:     0, Total:    60 |
| S3-8 rule reads wrong metric | yakalandı | Failed:     1, Passed:    59, Skipped:     0, Total:    60 |
| S3-9 rule severity warning | yakalandı | Failed:     1, Passed:    59, Skipped:     0, Total:    60 |
| S3-10 consumer wrong type | yakalandı | Failed:     1, Passed:    13, Skipped:     0, Total:    14 |
| S3-11 email category missing | yakalandı | Failed:     1, Passed:     8, Skipped:     0, Total:     9 |
| S3-12 tr template missing | yakalandı | Failed:     1, Passed:     3, Skipped:     0, Total:     4 |
| S4-M24 host observer registration removed | yakalandı | Failed:     1, Passed:     3, Skipped:     0, Total:     4 |
| S4-M23 telegram enabled 409 removed | yakalandı | Failed:     1, Passed:    33, Skipped:     0, Total:    34 |
| S4-M25 misconfigured telegram gets real handler | yakalandı | Failed:     1, Passed:     1, Skipped:     0, Total:     2 |
| S4-M26 misconfigured logged as information | yakalandı | Failed:     1, Passed:     0, Skipped:     0, Total:     1 |
| S4-subject literal block | yakalandı | Failed:     1, Passed:    38, Skipped:     0, Total:    39 |
| S1-1 guard without execute_alerts=false | yakalandı | ℹ pass 1 ℹ fail 3 |
| S1-2 guard skipped (marker check inverted) | yakalandı | ℹ pass 0 ℹ fail 4 |
| S1-3 marker written on failure path | yakalandı | ℹ pass 3 ℹ fail 1 |
| S1-4 compose entrypoint removed | yakalandı | ℹ pass 13 ℹ fail 1 |
| S4-nginx exact /health removed | yakalandı | ℹ pass 5 ℹ fail 6 |
| S4-nginx /health prefix match | yakalandı | ℹ pass 8 ℹ fail 3 |
| S4-FE2 telegram toggle ignores availability | yakalandı | vitest: 1 failed, 9 passed (10) |

**24 bozma, 24 yakalandı.**

`S3-*` mutabakat uyuşmazlığı yolu, `S1-*` ilk açılış koruması, `S4-*` diğer bulgular. Betik: her bozma tek dosyada metin değişikliği → ilgili test projesi derlenip filtreli testler / `node --test` / vitest → dosya geri yüklenir.

### Düzeltme turu test sonuçları

| Tür | Sonuç | Komut |
|---|---|---|
| Backend build (Release) | ✓ 0 hata | `dotnet build Skinora.sln -c Release` (uyarılar dokunulmayan dosyalarda) |
| Backend unit (tümü) | ✓ 2483/2483 (11 assembly) | `dotnet test Skinora.sln -c Release --no-build --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` |
| Backend integration (dokunulanlar) | ✓ API 91/91 (dokunulan sınıflar) + Notifications 218/218 (unit + integration, LocalDB) | `INTEGRATION_TEST_SQL_SERVER=(localdb)\MSSQLLocalDB`, filtre MoneyAlarmComposition, AccountSettingsEndpoint, PublicHealthEndpoint, NotificationInboxEndpoint, Reconciliation |
| `dotnet format --verify-no-changes --severity error` | ✓ çıkış 0 | |
| Frontend | ✓ | `tsc --noEmit` temiz; vitest `NotificationChannelAvailability.test.tsx` 10/10; prettier (`--end-of-line auto`) temiz |
| Betikler | ✓ 36/36 (first-boot 4, edge-health 11, compose-exposure 14, heartbeat 7) | `node --test scripts/grafana/first-boot.test.mjs scripts/nginx/edge-health.test.mjs scripts/compose-exposure.test.mjs scripts/heartbeat/heartbeat.test.mjs` |
| Canlı Grafana | ✓ | yukarıda (koşu 0/1/2/2b) |

### Düzeltme turu kararları (öneriyle kaydedildi — ⚠)

17. **⚠ PARA — Uyuşmazlık ölçümü kapsam başına, karşılaştırılmayan kapsam raporlanmaz** (yarıda kalan tur / adresi ayarsız cüzdan alarmı çözmez). *Kapanışta düzeltildi (yeniden doğrulama Y2):* deposit kapsamında anlık görüntüde eksik adres varken bu cümle doğru değildi, kapsam 0 raporlanıyordu. Artık eksik adres varken geri kalanı dengeliyse deposit kapsamı raporlanmaz.
18. **⚠ PARA — `reconciliation-mismatch` critical** (05 §3.3 "critical alert"), `for: 0s`.
19. **⚠ PARA — Yeni `ADMIN_RECONCILIATION_MISMATCH`** (admin gelen kutusu + kanalları), tüketici para gözlemcisiz (çift bildirim olmasın); her uyuşmazlıklı günlük turda yeniden.
20. **⚠ PARA — Aynı türde ikinci para olayı ayrı operatör bildirimi üretmez** (Bulgu 6).
21. **⚠ GÜVENLİK — Grafana ilk açılış koruması** giriş noktası betiğiyle (iki aşamalı açılış); 11.3'te başka kalıcı yol yok.
22. **⚠ — Yeniden başlatmada uyuşmazlık ölçümü kaybolur**, alarm sonraki tura kadar "çözüldü" görünür (admin bildirimi kalıcı); ölçümü veritabanından yeniden kurmak kapsam dışı.

### Açık kalanlar

- Gerçek Resend / Telegram / Healthchecks.io / UptimeRobot hesapları ile uçtan uca gönderim: sahip hesabı gerekir — T171/T172 (§P.6, adım 8 dahil).
- Uyuşmazlık "çözüldü" bildirimi yalnız operatör kanallarına (Grafana); admin gelen kutusuna "düzeldi" bildirimi gönderilmiyor.

## Doğrulama

| Tur | HEAD | Verdict | Bulgular | Sonuç |
|---|---|---|---|---|
| 1. doğrulama (bağımsız alt ajan) | `f568396b` | ✗ FAIL | 8 — S3: 1 (⚠ PARA, mutabakat uyuşmazlığı yalnız panelde) · S1: 1 (⚠ GÜVENLİK, Grafana ilk açılışta `example@email.com`) · S4: 6 | "Düzeltme turu": sekizi de ele alındı |
| Yeniden doğrulama (bağımsız alt ajan, Opus high) | `869198a9` | ✓ PASS | 3 — S4: 3, üçü ⚠ PARA (Y1 Grafana izlenmiyor + koruma bir log satırına bağlı, Y2 eksik deposit adresi, Y3 main T169a birleştirmesi) | Önceki 8 bulgu kapandı. S3 (Telegram + e-posta, "çözüldü" dahil) ve S1 (korumalı 0, korumasız kontrol 3 e-posta) canlı ölçüldü. **Bozma 29/29**, kabul kriterleri ✓. Üç S4 kapanışta kapandı, bkz. "Kapanış" |

Kaynak raporlar (yerel, repo dışında): `C:\Users\teknodeveloper\.claude\projects\c--projects-Escrow\f9-handoff\T165_VALIDATION.md` (1. tur) ve `C:\Users\teknodeveloper\.claude\projects\c--projects-Escrow\f9-handoff\T165_REVALIDATION.md` (yeniden doğrulama).

## Kapanış (2026-10-23 — yeniden doğrulama ✓ PASS sonrası)

**Y3 — main birleştirmesi (⚠ PARA).** `origin/main` `fcc94d8d` (T169a #376), merge commit `eb3a6c5e` ile alındı. Kod otomatik birleşti. 9 belgede çakışma vardı:
- MEMORY ve BYPASS_LOG: iki tarafın satırları korundu.
- 05 **v3.37**, 06 **v6.39**, 07 **v4.25**: T169a v3.35/v3.36, v6.38 ve v4.24'ü aldığı için T165 bir üstüne çıktı; DOC_CHANGELOG'daki T165 girdileri yeniden numaralandı.
- DEPLOY_RUNBOOK başlığı birleştirildi, bölüm sırasına §P eklendi.
- IMPLEMENTATION_STATUS / STATUS_CHANGELOG: main'in T169a satırları korundu.
- DEFERRED_BACKLOG otomatik birleşti. T165 yalnız bir satırın metnini değiştiriyor; sayı main'deki **50 aktif / 168 çözülmüş**, yeni satır yok.

Para sınıflandırması:
- T169a'nın `deposit-refund-daily-limit-held` kuralına `category: money` eklendi (**28 para kuralı**).
- Yeni `NonMoneyRules` listesi ve `EveryRule_IsClassified_AsMoneyOrNotMoney` testi: rules.yml'deki her kural (40) ya para ya para dışı listesinde olmalı. Başka daldan etiketsiz gelen bir kural artık CI'ı kırar.
- `DepositRefundLimitHeldAdminNotificationConsumer` bilinçli olarak **para dışı** (gözlemcisiz). Kendi göstergesi ve para kuralı var; gözlemci aynı beklemeyi `admin-money-alarm-raised` olarak ikinci kez duyururdu (`ReconciliationMismatch` ile aynı gerekçe).
- Runbook §P.4 tablosu ve "yeni para alarmı eklemek" paragrafı güncellendi.
- Bozma: etiket silinince 2 test kırıldı (`MoneyRule_CarriesTheMoneyCategory`, `OnlyTheListedRules_AreMoneyRules`).

**Y1 — Grafana'nın kendisi izlenmiyordu, koruma bir log satırına bağlıydı (⚠ PARA).**
1. Deneme yayını heartbeat'inde `HEARTBEAT_CHECK_URLS`'e `http://skinora-grafana:3000/api/health` eklendi (konteyner ağı adresi; Grafana dışarıya yalnız 127.0.0.1'de açık). Grafana düşerse ya da ilk açılış koruması onu açmazsa ping durur, Healthchecks sahibe haber verir. compose-exposure testi bu kontrolü ister. `.env.example` notu, runbook §P.5 (+ P.6 adım 7a) ve 05 §9.5 güncellendi.
2. `first-boot.sh` korumalı açılışta kendi log ayarını sabitler: `GF_LOG_LEVEL=info`, `GF_LOG_MODE=console`, `GF_LOG_CONSOLE_LEVEL=info`, `GF_LOG_CONSOLE_FORMAT=text`, `GF_LOG_FILTERS=provisioning.alerting:info`. Operatörün `GF_LOG_*` ayarı normal açılışta geçerli kalır.
   - **Neden yalnız test değil, sabitleme:** test yalnız repodaki ayarı korur; operatörün `.env`'ine ya da elle eklenen değişkene bakamaz. Sabitleme, Grafana hangi ayarla açılırsa açılsın korumayı çalıştırır. Sabitlemeyi de bir test zorluyor.
   - Testler: `first-boot.test.mjs` +2. Birincisi: warn/file/json/filtre altında koruma tamamlanır, normal açılış operatörün ayarını alır. İkincisi kontrol testi: sahte Grafana o ayarda satırı gerçekten gizler.
   - Bozma: sabitlemenin üç parçasından her biri silinince test kırıldı (3/3).
   - **Canlı ölçüm:** gerçek `grafana/grafana:11.3.0`, boş veri, `GF_LOG_LEVEL=warn GF_LOG_CONSOLE_FORMAT=json GF_LOG_FILTERS=provisioning.alerting:error`. Faz 1 metin satırını yazdı (`level=info msg="finished to provision alerting"`), işaret yazıldı, faz 2 operatörün json/warn ayarıyla açıldı, `/api/health` 200 (`database: ok`). Konteyner kaldırıldı.
3. Sürüm: compose testi imajı `grafana/grafana:11.3.0`'a sabitler. Betik başlığı ve runbook "yükseltmeden önce log satırını yeniden ölç" der.

**Y2 — anlık görüntüde eksik deposit adresi (⚠ PARA).**
- `ReconciliationService`: eksik adres varken geri kalan deposit adresleri dengeliyse deposit kapsamı **raporlanmaz**. Önceki değer kalır, yanlış "çözüldü" çıkmaz.
- Uyuşmazlık bulunduysa yine raporlanır; pozitif bir sayı alarmı ancak ateşte tutabilir.
- Eksik adres sayısı uyarı olarak loglanır.
- İki test eklendi: eksik + dengeli → rapor yok, olay yok; eksik + uyuşmazlık → 1 raporlanır, olay var.
- Bozma: koşul kaldırılınca ilk test kırıldı.
- `IReconciliationRunObserver` yorumu ve karar 17 güncellendi.
- Kapsam dışı: `MaxDepositAddressesPerRun` kesmesi hep aynı (en eski) adresleri seçer. Kesilen adreslerde uyuşmazlık hiç ölçülmediği için yanlış "çözüldü" üretemez; olduğu gibi bırakıldı.

**Kapanış testleri.**

| Tür | Sonuç |
|---|---|
| Backend build (Release) | ✓ 0 hata |
| Backend unit (tümü) | ✓ 2501/2501 (11 assembly; main T169a testleri dahil) |
| LocalDB integration (Reconciliation, MoneyAlarmComposition, AdminAlertNotificationConsumer, DepositRefund, AdminOutboundApprovals, AccountSettingsEndpoint, NotificationDispatcher, HotWalletMonitor, ChannelHandlerRegistration) | ✓ API 69/69 + Notifications 9/9 |
| Betikler | ✓ 27/27 (first-boot 6, compose-exposure 14, heartbeat 7) |

**Kapanışta açık kalan:** yok. Gerçek hesaplarla uçtan uca gönderim T171/T172'de (değişmedi).

## Altyapı Değişiklikleri

- Migration: **Yok**.
- Config/env: **Var** — backend `Resend__*`, `Telegram__*`, `Discord__Provider` (compose), Grafana `GF_SMTP_*`, `.env.example` yeni satırlar (`RESEND_*`, `TELEGRAM_PROVIDER`, `TELEGRAM_BOT_USERNAME`, `TELEGRAM_WEBHOOK_SECRET_TOKEN`, `DISCORD_PROVIDER`, `GRAFANA_SMTP_*`, `HEARTBEAT_*`).
- Docker: **Var** — deneme yayını overlay'inde yeni `skinora-heartbeat` servisi (port yok); nginx açık `/health` → `/api/health`.
- CI: backend yol filtresine `infra/grafana/**`.
- Yeni Prometheus serileri: `skinora_backend_notification_channel_state`, `skinora_backend_admin_money_alarm_last_seconds`, `skinora_backend_notification_delivery_exhausted_last_seconds`, `skinora_backend_outbound_awaiting_approval_rows`, `skinora_backend_outbound_awaiting_approval_oldest_seconds`, `skinora_backend_hot_wallet_threshold_breached`.

## Commit & PR

- Branch: `task/T165-bildirim-kanallari`
- Commit: `2751c55e` — kod/altyapı/test; `c064ee91` — belgeler; `58d6569c` + bu commit — rapor, status, memory
- Kapanış: `eb3a6c5e` (main `fcc94d8d` merge + T169a para sınıflandırması), `b4b3e4d9` (Y1/Y2 kod + test), ardından kapanış kayıt commit'i
- PR: [#375](https://github.com/turkerurganci/Skinora/pull/375)
- CI: run `39863197815` (HEAD `c064ee91`); son HEAD'in run'ı PR'da

## Para alarmı listesi ve kanal eşlemesi

| Alarm (kural uid) | Önem | Kanal |
|---|---|---|
| `outbound-awaiting-approval` (yeni) | warning | Grafana `money-alarm`: Telegram + e-posta |
| `hot-wallet-threshold-breached` (yeni, TRON) | warning | Telegram + e-posta |
| `admin-money-alarm-raised` (yeni: transfer FAILED, iade yapılamadı, payout ertelendi, settlement park) | critical | Telegram + e-posta; ayrıca panel + admin'in kendi e-posta/Telegram'ı |
| `reconciliation-mismatch` (düzeltme turu: günlük mutabakat defter ↔ zincir uyuşmazlığı, ağ + kapsam) | critical | Telegram + e-posta (dengeli turda "çözüldü"); ayrıca `ADMIN_RECONCILIATION_MISMATCH` → panel + admin'in kendi kanalları |
| `blockchain-sidecar-down`, `ethereum-finalized-stalled`, `ethereum-deposit-balance-mismatch`, `ethereum-usdt-status`, `ethereum-nonce-conflict`, `ethereum-journal-behind-chain`, `ethereum-journal-ack-ahead-of-chain`, `ethereum-monitor-scan-failing`, `ethereum-webhook-signature-refused`, `ethereum-transfer-status-unverified`, `ethereum-gas-estimate-abnormal`, `tron-quota-rejections` | critical | Telegram + e-posta |
| `tron-api-errors-high`, `tron-quota-projection`, `ethereum-deposit-native-eth`, `ethereum-reconciliation-stale`, `ethereum-transfer-wait`, `ethereum-wallet-eth-low` (yakıt), `outbound-transfer-stuck`, `ethereum-rpc-latency-high`, `outbound-transfer-backlog`, `reconciliation-network-failed`, `gas-fee-fallback-charged` (kesinti yedeği) | warning | Telegram + e-posta |
| `deposit-refund-daily-limit-held` (T169a; kapanışta main birleştirilirken etiketlendi) | warning | Telegram + e-posta; ayrıca `ADMIN_PAYMENT_FAILURE` → panel + admin kanalları (tüketici gözlemcisiz) |
| Para dışı: `notification-channel-misconfigured`, `notification-delivery-exhausted` | warning | Telegram (4 sa) |

## Kararlar (öneriyle kaydedildi — ⚠)

1. **⚠ — Eksik sır = `misconfigured`:** site açılır, kanal kapalı + hata logu + alarm; stub'a düşmez, fail-fast değil.
2. **⚠ — Overlay üretim seçimini zorlar** (e-posta + Telegram gerçek, Discord `disabled`), `.env.trial` değiştiremez; yalnız sırlar gerekir.
3. **⚠ GÜVENLİK — Bildirim sırları env'de** (T163 ortak sırlarıyla aynı yol; `SharedSecretsInPlainEnv` → T171'e eklendi).
4. **⚠ — Tek Telegram botu** backend bildirimleri + Grafana alarmları için.
5. **⚠ GÜVENLİK — Grafana için ayrı Resend anahtarı** (Sending access).
6. **⚠ — Grafana e-postası Resend SMTP** (587, STARTTLS zorunlu); ikinci sağlayıcı yok.
7. **⚠ PARA — Para alarmı kategorisi** = `category: money` etiketi; 23 mevcut kural sınıflandı (zincir/sağlayıcı/sweep/payout/iade/cüzdan/ücret), Telegram + e-posta birlikte, 4 sa hatırlatma, ilk rota.
8. **⚠ PARA — Backend para alarmı** = 4 admin tüketicisi; zaman damgası göstergesi, 1 sa pencere, critical; admin/kanal yoksa da Grafana'ya ulaşır.
9. **⚠ PARA — Onay bekleyen ödeme bildirimi Grafana yolundan** (ağ başına gösterge, `for: 1m`, 4 sa hatırlatma); satır başına backend bildirimi yok (S22 paneli listeler, yeni bildirim türü/migration gerekmedi).
10. **⚠ PARA — Sıcak cüzdan eşik göstergesi** (TRON; Ethereum USDT limiti T170) warning.
11. **⚠ GÜVENLİK — Açık sağlık ucu** yalnız durum; nginx `/health` bu uca, ayrıntılı uç yalnız iç ağ.
12. **⚠ — Dış izleme:** Healthchecks.io heartbeat birincil + UptimeRobot HTTP ikincil (ikisi ücretsiz).
13. **⚠ — Heartbeat ayarı:** 60 sn, 3 hatalı turda tek `/fail`, kontrol `/api/health` + ön sayfa, `curlimages/curl` imajı (CI'daki HTTP yolu), Healthchecks'te period 1 dk / grace 5 dk önerisi.
14. **⚠ GÜVENLİK — Telegram HttpClient varsayılan logları kaldırıldı** (token URL'de).
15. **⚠ — Kullanılamayan kanal:** açma/bağlama/doğrulama 409, kapatma/kaldırma serbest, var olan tercih silinmez (teslimat açılmaz).
16. **⚠ — CI backend filtresine `infra/grafana/**`** (sağlama testleri yalnız-altyapı PR'ında da koşsun).

## Dış Varsayımlar (kanıtlı)

| Varsayım | Kanıt |
|---|---|
| Resend ücretsiz plan: ayda 3.000, günde 100 e-posta, 3 alan adı, 1 webhook | resend.com/pricing (2026-10-23) |
| Resend SMTP: `smtp.resend.com`, 465/2465 SMTPS, 25/587/2587 STARTTLS, kullanıcı `resend`, parola API anahtarı | resend.com/docs/send-with-smtp |
| Resend `POST /emails` gövdesi, `tags[{name,value}]`, `Idempotency-Key` başlığı, `{id}` cevabı | resend.com/docs/api-reference/emails/send-email |
| Resend `User-Agent`'sız isteği 403 (1010) | resend.com/docs/knowledge-base/403-error-1010 |
| Healthchecks.io Hobbyist: 20 kontrol, 100 log/kontrol, ticari kısıt yok | healthchecks.io/pricing |
| Healthchecks.io ping `hc-ping.com/<uuid>` (+`/fail`), HEAD/GET/POST, dakikada ≤ 5 ping; period + grace | healthchecks.io/docs/http_api, healthchecks.io/docs |
| Healthchecks.io Telegram (HealthchecksBot `/start`) | healthchecks.io/integrations/telegram (arama sonucu) |
| UptimeRobot ücretsiz: 50 monitör, 5 dk; şartlar ticari kullanıma izin veriyor (2026-05-26 sürümü) | uptimerobot.com/pricing, uptimerobot.com/terms |
| Better Stack ücretsiz: 10 monitör+heartbeat, e-posta/Slack (Telegram yok) | betterstack.com/pricing |
| Cloudflare Standalone Health Checks ücretsiz planda yok (Pro+) | developers.cloudflare.com/health-checks |
| Grafana `[smtp]` seçenekleri (`startTLS_policy` = `MandatoryStartTLS` …), `GF_<SECTION>_<KEY>`; Docker imajı `__FILE` | grafana.com/docs/…/configure-grafana/#smtp, …/configure-docker |
| `curlimages/curl:8.21.0` var (2026-06-24) | hub.docker.com API |
| `IHttpClientFactory` varsayılan logları URL yolunu (token) yazar | `TelegramTransportRegistrationTests.DefaultRegistration_WouldLogTheToken_Contrast` (ölçüldü) |

## Known Limitations / Follow-up

- **(İlk yapım; düzeltme turunda Grafana ve nginx canlı koşuldu — aşağıda)** Canlı Grafana / nginx / compose bu makinede koşulmadı (talimat: Docker yok). Grafana dosyaları PyYAML ile ayrıştırıldı + metin testleri; nginx `-t` koşulmadı (E2E yığını CI'da aynı `nginx.conf`'u yükler); `compose-exposure` CI'da. Gerçek gönderim, contact point "Test" ve heartbeat → Healthchecks yolu T172 provasında (§P.6).
- **Resend `Idempotency-Key` gönderilmiyor:** kanal işleyici arayüzü teslimat kimliğini taşımıyor; Resend kabul edip zaman aşımına düşen bir istek yeniden denenince çift e-posta mümkün (seyrek). Arayüz değişikliği (6 işleyici) kapsam dışı bırakıldı.
- **`daily_quota_exceeded` (429) geçici sayılır:** gün içinde yeniden deneme ertelenmiş katmanlarda (30 dk / 1 sa / 4 sa) tükenir → teslimat FAILED + `notification-delivery-exhausted`. Ücretsiz planın günde 100 e-postası kullanıcı bildirimleri ve Grafana arasında paylaşılır.
- **Backend para alarmı zaman damgası bellekte:** backend yeniden başlarsa alarm erken çözülür (panel bildirimi kalır). Aynısı `reconciliation-mismatch` için: yeniden başlatmada ölçüm kaybolur, alarm bir sonraki günlük tura kadar "çözüldü" görünür; tur uyuşmazlığı yeniden bulursa yeniden ateşler (admin bildirimi kalıcı). Runbook §P.4.
- **Onay bekleyen ödeme için panel zili yok:** yalnız Grafana yolu + S22 listesi; admin'in kendi Telegram'ına satır başına mesaj gitmez.
- **Ethereum sıcak cüzdan USDT limiti** hâlâ izlenmiyor (DEFERRED_BACKLOG `EthereumHotWalletUsdtLimitUnmonitored`, T170).
- **Webhook'lar alan adı + tünel ister** (T162): Resend olayları ve Telegram `/start` bağlantısı o zamana kadar çalışmaz; e-posta gönderimi ve Grafana Telegram alarmı webhook'suz çalışır.
- **Telegram sohbet kimliği** `getUpdates` ile webhook kurulmadan **önce** alınmalı (§P.3; sonra 409).
- Bildirim sırları ve `HEARTBEAT_URL` env'de (`docker inspect` gösterir) — T171.
- ~~T169a (paralel dal) kuralına `category: money` eklemezse para kanalına gitmez; `OnlyTheListedRules_AreMoneyRules` testi birleştirmede bunu yakalar.~~ Bu yanlıştı: test yalnız etiketli kuralları karşılaştırıyordu (yeniden doğrulama Y3). Kapanışta kural etiketlendi; `EveryRule_IsClassified_AsMoneyOrNotMoney` artık her kuralın bilinçli sınıflandırılmasını zorlar.

## Notlar

- **Pre-commit secret guard:** ilk commit üç satırda engellendi (yapılandırma okuma satırı, bir test sabitinin adı ve bir test webhook değeri) — hepsi sahte pozitif; bypass kullanılmadı, satırlar yeniden yazıldı.
- **Working tree (Adım −1):** temiz (`git status` boş, dal `task/T165-bildirim-kanallari` = `origin/main` `69c68df6`).
- **Main CI (Adım 0):** son 3 tamamlanmış run `success` — `39850436226` (CI, T169 `21c93062`), `39850436112` (Docker Publish, T169), `39844542864` (Docker Publish, T163); T168 `69c68df6` CI `39854740137` başlangıçta koşuyordu, sonra ✓ (Docker Publish `39854740205` ✓) → STATUS_CHANGELOG'a merge teyidi yazıldı; T169 CI "koşuyor" → ✓ işlendi.
- **Bağımlılıklar:** T161, T163, T169 ✓ (STATUS). T162 (alan adı/tünel) bekliyor — webhook'lar ona bağlı (sınırlama).
- **Paralel dallar:** T164 (#372) `rules.yml`'ye iki grubun sonuna ekliyor; T165 kuralları dosyanın başında ayrı grupta, mevcut kurallarda yalnız `category: money` satırları (T164'ün bloklarına bitişik değil). T164 `docker-compose.yml`/`.env.example`/`05`/`DEPLOY_RUNBOOK` (§N) da değiştiriyor — çakışma olursa yönetici çözer; T165 bölümü §P.
- **Kapsam dışı bırakılan:** Discord kodu silinmedi (yalnız kapalı). (İlk yapımda bildirim türü eklenmemişti; düzeltme turunda mutabakat uyuşmazlığı için `ADMIN_RECONCILIATION_MISMATCH` eklendi — int saklanır, sona eklendi, migration yok.)
