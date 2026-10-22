# T165 — Bildirim kanalları ve dış alarm

**Faz:** F9 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-10-23

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
- **Belgeler:** `DEPLOY_RUNBOOK.md` §P, `INTEGRATION_RUNBOOKS/{RESEND,TELEGRAM,DISCORD}_SETUP.md`, `05` v3.34, `07` v4.24, `DOC_CHANGELOG.md`, `DEFERRED_BACKLOG.md`, `IMPLEMENTATION_STATUS.md`, `STATUS_CHANGELOG.md`, `.claude/memory/MEMORY.md`.

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
| Backend — Transactions.Tests | __TX__ | unit + integration (LocalDB) |
| Backend — API.Tests | __API__ | unit + integration (SQLite / LocalDB) |
| `dotnet format --verify-no-changes --severity error` (değişen dosyalar) | ✓ | çıktı yok |
| Frontend | ✓ | `eslint` 0, `tsc --noEmit` temiz, `i18n:check` parity OK (4 × 1700), `vitest` 47 dosya / 469 test, `next build` ✓ |
| Betik — heartbeat | ✓ 7/7 | `node --test scripts/heartbeat/heartbeat.test.mjs` (sh + curl) |
| Betik — compose-exposure | ? yerelde koşulmadı | Docker kullanma talimatı; CI "3c. Script test" koşar (yeni 3 test) |
| Grafana YAML | ✓ ayrıştı | PyYAML (yalnız sözdizimi): 3 grup (5 + 13 + 15 kural), 26 para kuralı, 4 contact point |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor (yönetici düzeni — ayrı doğrulama alt ajanı) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- Migration: **Yok**.
- Config/env: **Var** — backend `Resend__*`, `Telegram__*`, `Discord__Provider` (compose), Grafana `GF_SMTP_*`, `.env.example` yeni satırlar (`RESEND_*`, `TELEGRAM_PROVIDER`, `TELEGRAM_BOT_USERNAME`, `TELEGRAM_WEBHOOK_SECRET_TOKEN`, `DISCORD_PROVIDER`, `GRAFANA_SMTP_*`, `HEARTBEAT_*`).
- Docker: **Var** — deneme yayını overlay'inde yeni `skinora-heartbeat` servisi (port yok); nginx açık `/health` → `/api/health`.
- CI: backend yol filtresine `infra/grafana/**`.
- Yeni Prometheus serileri: `skinora_backend_notification_channel_state`, `skinora_backend_admin_money_alarm_last_seconds`, `skinora_backend_notification_delivery_exhausted_last_seconds`, `skinora_backend_outbound_awaiting_approval_rows`, `skinora_backend_outbound_awaiting_approval_oldest_seconds`, `skinora_backend_hot_wallet_threshold_breached`.

## Commit & PR

- Branch: `task/T165-bildirim-kanallari`
- Commit: __COMMIT__
- PR: __PR__
- CI: __CI__

## Para alarmı listesi ve kanal eşlemesi

| Alarm (kural uid) | Önem | Kanal |
|---|---|---|
| `outbound-awaiting-approval` (yeni) | warning | Grafana `money-alarm`: Telegram + e-posta |
| `hot-wallet-threshold-breached` (yeni, TRON) | warning | Telegram + e-posta |
| `admin-money-alarm-raised` (yeni: transfer FAILED, iade yapılamadı, payout ertelendi, settlement park) | critical | Telegram + e-posta; ayrıca panel + admin'in kendi e-posta/Telegram'ı |
| `blockchain-sidecar-down`, `ethereum-finalized-stalled`, `ethereum-deposit-balance-mismatch`, `ethereum-usdt-status`, `ethereum-nonce-conflict`, `ethereum-journal-behind-chain`, `ethereum-journal-ack-ahead-of-chain`, `ethereum-monitor-scan-failing`, `ethereum-webhook-signature-refused`, `ethereum-transfer-status-unverified`, `ethereum-gas-estimate-abnormal`, `tron-quota-rejections` | critical | Telegram + e-posta |
| `tron-api-errors-high`, `tron-quota-projection`, `ethereum-deposit-native-eth`, `ethereum-reconciliation-stale`, `ethereum-transfer-wait`, `ethereum-wallet-eth-low` (yakıt), `outbound-transfer-stuck`, `ethereum-rpc-latency-high`, `outbound-transfer-backlog`, `reconciliation-network-failed`, `gas-fee-fallback-charged` (kesinti yedeği) | warning | Telegram + e-posta |
| T169a günlük iade tavanı (paralel dal) | — | Kuralına `category: money` yazması yeter; backend olayıysa `IAdminMoneyAlarmObserver` kurucusu |
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

- **Canlı Grafana / nginx / compose bu makinede koşulmadı** (talimat: Docker yok). Grafana dosyaları PyYAML ile ayrıştırıldı + metin testleri; nginx `-t` koşulmadı (E2E yığını CI'da aynı `nginx.conf`'u yükler); `compose-exposure` CI'da. Gerçek gönderim, contact point "Test" ve heartbeat → Healthchecks yolu T172 provasında (§P.6).
- **Resend `Idempotency-Key` gönderilmiyor:** kanal işleyici arayüzü teslimat kimliğini taşımıyor; Resend kabul edip zaman aşımına düşen bir istek yeniden denenince çift e-posta mümkün (seyrek). Arayüz değişikliği (6 işleyici) kapsam dışı bırakıldı.
- **`daily_quota_exceeded` (429) geçici sayılır:** gün içinde yeniden deneme ertelenmiş katmanlarda (30 dk / 1 sa / 4 sa) tükenir → teslimat FAILED + `notification-delivery-exhausted`. Ücretsiz planın günde 100 e-postası kullanıcı bildirimleri ve Grafana arasında paylaşılır.
- **Backend para alarmı zaman damgası bellekte:** backend yeniden başlarsa alarm erken çözülür (panel bildirimi kalır).
- **Onay bekleyen ödeme için panel zili yok:** yalnız Grafana yolu + S22 listesi; admin'in kendi Telegram'ına satır başına mesaj gitmez.
- **Ethereum sıcak cüzdan USDT limiti** hâlâ izlenmiyor (DEFERRED_BACKLOG `EthereumHotWalletUsdtLimitUnmonitored`, T170).
- **Webhook'lar alan adı + tünel ister** (T162): Resend olayları ve Telegram `/start` bağlantısı o zamana kadar çalışmaz; e-posta gönderimi ve Grafana Telegram alarmı webhook'suz çalışır.
- **Telegram sohbet kimliği** `getUpdates` ile webhook kurulmadan **önce** alınmalı (§P.3; sonra 409).
- Bildirim sırları ve `HEARTBEAT_URL` env'de (`docker inspect` gösterir) — T171.
- T169a (paralel dal) kuralına `category: money` eklemezse para kanalına gitmez; `OnlyTheListedRules_AreMoneyRules` testi birleştirmede bunu yakalar (liste güncellenmeli).

## Notlar

- **Working tree (Adım −1):** temiz (`git status` boş, dal `task/T165-bildirim-kanallari` = `origin/main` `69c68df6`).
- **Main CI (Adım 0):** son 3 tamamlanmış run `success` — `39850436226` (CI, T169 `21c93062`), `39850436112` (Docker Publish, T169), `39844542864` (Docker Publish, T163); T168 `69c68df6` CI `39854740137` başlangıçta koşuyordu, sonra ✓ (Docker Publish `39854740205` ✓) → STATUS_CHANGELOG'a merge teyidi yazıldı; T169 CI "koşuyor" → ✓ işlendi.
- **Bağımlılıklar:** T161, T163, T169 ✓ (STATUS). T162 (alan adı/tünel) bekliyor — webhook'lar ona bağlı (sınırlama).
- **Paralel dallar:** T164 (#372) `rules.yml`'ye iki grubun sonuna ekliyor; T165 kuralları dosyanın başında ayrı grupta, mevcut kurallarda yalnız `category: money` satırları (T164'ün bloklarına bitişik değil). T164 `docker-compose.yml`/`.env.example`/`05`/`DEPLOY_RUNBOOK` (§N) da değiştiriyor — çakışma olursa yönetici çözer; T165 bölümü §P.
- **Kapsam dışı bırakılan:** bildirim türü eklenmedi (enum/migration yok), Discord kodu silinmedi (yalnız kapalı).
