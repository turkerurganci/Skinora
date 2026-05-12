# T64 — Steam Sidecar Bot Session Yönetimi

**Faz:** F4 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-05-12

---

## Yapılan İşler

- Steam bot session lifecycle yönetimi `BotSession` state machine ile implement edildi: `INITIALIZING → LOGGING_IN → READY ⇄ SESSION_EXPIRED → RECONNECTING → READY | FAILED`; `BANNED`/`STOPPED` terminal state'leri ayrı sınıflandırıldı.
- 2FA login: `steam-totp` üzerinden `SteamTotp.generateAuthCode(sharedSecret)` ile TOTP kodu üretilip her `logOn` çağrısına geçiriliyor.
- Cookie/session geçişi: `steam-user webSession` event'inden gelen cookies `SteamCommunity.setCookies()` ile aktarılıyor, ardından `startConfirmationChecker(20000, identitySecret)` ile mobile confirmation auto-accept hookup yapıldı (T65 trade offer akışı kullanacak).
- Session expire tespiti: `steamcommunity sessionExpired` event'i SESSION_EXPIRED state'e geçirip recovery loop tetikliyor. Network drop edge-case'i için `BotHealthCheck` 60sn periyodik probe ikinci savunma katmanı olarak eklendi (05 §3.2).
- Otomatik re-login: 08 §2.7 retry tablosuna uygun **5s / 15s / 45s** exponential backoff; her seferinde yeni TOTP kodu üretilir; tüm denemeler tükendiğinde state FAILED'a düşer ve `onFatalFailure('session_recovery_failed')` callback'i tetiklenir.
- Failover: BotManager `bot.session_failed` event'i ile backend'e webhook gönderir, ardından havuzdan çıkarır ve `bot.removed_from_pool` event'ini publish eder. T68 backend handler bu event'leri admin alert'e dönüştürecek (şimdilik webhook 404'ü graceful log'a iniyor).
- Çoklu bot yönetimi: `BotManager.initialize()` config'i okuyup paralel `BotSession.start()` çağırıyor; `selectBot()` round-robin yapıyor (T64 scope, capacity-based seçim T69'a forward-devir 05 §3.2).
- Bot credential kaynak şeması: `STEAM_BOTS_CONFIG_PATH` (K8s Secret mount / dosya yolu) öncelikli, `STEAM_BOTS_JSON` (inline JSON) fallback; hiçbiri yoksa sidecar yine başlar (skeleton mode).
- Permanent eResult tespiti: `EResult` 5/6/18/56 → `FAILED` (login_failed), 3/70 → `BANNED`. Transient eResult'lar (ör. 84 RateLimitExceeded) state değiştirmez, recovery loop drives retries.
- Health endpoint zenginleştirildi: `/health` artık bot sayılarını döner (`healthy/total`), `/api/bots/status` (internalKeyAuth altında) tam pool snapshot expose ediyor.
- Prometheus gauge `skinora_steam_active_bot_sessions` her state transition'da güncelleniyor.
- Vitest test framework eklendi (sidecar'da daha önce test runner yoktu). 42/42 unit test PASS: BotConfig (9) + BotSession state machine (13) + BotManager lifecycle/selector (10) + BotHealthCheck failover (6) + WebhookPayloads contract (4).

## Etkilenen Modüller / Dosyalar

### Oluşturulan
- `sidecar-steam/src/bot/BotConfig.ts` — credential schema + loader (`loadBotCredentials`)
- `sidecar-steam/src/bot/BotConfig.test.ts`
- `sidecar-steam/src/bot/BotSession.test.ts`
- `sidecar-steam/src/bot/BotManager.test.ts`
- `sidecar-steam/src/bot/BotHealthCheck.test.ts`
- `sidecar-steam/src/webhook/WebhookPayloads.test.ts` — contract test (event isimleri + payload shape pinned)
- `sidecar-steam/vitest.config.ts`

### Güncellenen (T14 stub'lardan full implementation'a)
- `sidecar-steam/src/bot/BotSession.ts` — state machine, steam-user + steamcommunity + steam-totp wire, re-login loop
- `sidecar-steam/src/bot/BotManager.ts` — N bot lifecycle, round-robin selector, webhook event publisher, pool snapshot
- `sidecar-steam/src/bot/BotHealthCheck.ts` — 60sn periyodik probe, recovery/removal orchestration
- `sidecar-steam/src/webhook/WebhookPayloads.ts` — `BotEventName`, `BotEventPayload<T>`, `BotSessionFailedData`, `BotRemovedFromPoolData` tipleri eklendi
- `sidecar-steam/src/health/HealthController.ts` — factory pattern (`healthCheckFactory`, `botStatusFactory`)
- `sidecar-steam/src/api/routes.ts` — `buildRouter(botManager)` factory; `/api/bots/status` artık 501 değil snapshot dönüyor
- `sidecar-steam/src/index.ts` — `BotHealthCheck` start/stop wiring + graceful shutdown sırası
- `sidecar-steam/package.json` — `vitest@^2.1.0` + `@types/steam-user@^5.1.1` + `@types/steamcommunity@^3.50.0` + `@types/steam-totp@^2.1.2` devDep, `test` script

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Bot login: username, password, shared_secret ile oturum açma | ✓ | `BotSession.loginAttempt()` → `client.logOn({ accountName, password, twoFactorCode: SteamTotp.generateAuthCode(sharedSecret) })`. Test: `BotSession.test.ts` "start() calls logOn with TOTP code" |
| 2 | Session expire tespiti ve otomatik re-login | ✓ | `community.on('sessionExpired')` → `transition('SESSION_EXPIRED')` → `runReloginLoop()`. Test: `BotSession.test.ts` "sessionExpired triggers SESSION_EXPIRED transition and recovery loop" — re-login sonrası READY'ye dönüş doğrulandı |
| 3 | Health check: 60sn periyodik Steam bot session kontrolü | ✓ | `BotHealthCheck.DEFAULT_INTERVAL_MS = 60_000`. Test: `BotHealthCheck.test.ts` "marks every READY bot as healthy", "triggers recoverSession for non-ready", "start/stop manage the scheduler exactly once" |
| 4 | Failover: session başarısız → cookie yenileme → re-login → bot havuzdan çıkarma → admin alert | ✓ | `BotSession.runReloginLoop()` 08 §2.7 backoff (5s/15s/45s); exhausted → `FAILED` + `onFatalFailure('session_recovery_failed')` → `BotManager.handleFatalFailure()` → `bot.session_failed` + `bot.removed_from_pool` webhook'lar. Test: `BotSession.test.ts` "recoverSession exhausts backoff and declares FAILED" + `BotManager.test.ts` "onFatalFailure callback removes the bot and emits webhook events" |
| 5 | steam-totp ile mobile confirmation otomatik onayı | ✓ | `community.startConfirmationChecker(20_000, identitySecret)` `webSession` event'inde çağrılır, sonraki webSession'larda idempotent (sadece bir kez). Test: `BotSession.test.ts` "confirmation checker is started exactly once" |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (sidecar) | ✓ 42/42 PASS | `npm test` — BotConfig 9 + BotSession 13 + BotManager 10 + BotHealthCheck 6 + WebhookPayloads contract 4. Vitest 2.1.9 |
| Build (sidecar) | ✓ | `npm run build` — `tsc` 0 error |
| Lint (sidecar) | ✓ | `npm run lint` — ESLint 0 hata |
| Format check (sidecar) | ~ Kısmi | T64 dosyaları (7) Prettier temiz; T14'ten kalma 10 dosya drift (KL'ye taşındı) |
| Build (backend) | ✓ | `dotnet build backend/Skinora.sln -c Release` — 0 Warning(s), 0 Error(s) |
| Contract test | ✓ | `WebhookPayloads.test.ts` event isimlerini ve payload shape'ini pinler — T68 backend handler'ı bu kontrata göre yazılacak |

## Doğrulama Kontrol Listesi

- [x] 08 §2.7 hata yönetimi zinciri doğru mu? — Retry tablosu 5s/15s/45s + permanent vs transient eResult ayrımı `BotSession.PERMANENT_LOGIN_ERESULTS` ve `BANNED_ERESULTS` ile uygulandı.
- [x] Bot health check periyodu ve logic doğru mu? — 60sn (05 §3.2), inFlight guard, recoverable vs terminal ayrımı, recovery sonrası havuzdan çıkarma + webhook alert.

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bekliyor (bağımsız validator chat'i — INSTRUCTIONS.md §3.3 izolasyon) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- **Migration:** Yok.
- **Config/env değişikliği:** Yeni — `STEAM_BOTS_CONFIG_PATH` (öncelikli, JSON file path) ve `STEAM_BOTS_JSON` (fallback, inline JSON string). Hiçbiri yoksa sidecar skeleton modunda çalışır (production'da uyarı log'u atar). Docker compose / K8s manifest'leri T64'te güncellenmedi — env injection deploy zamanı yapılır.
- **Docker değişikliği:** Yok (Dockerfile T14'ten beri geçerli, yeni runtime dep yok).
- **package.json:** Yeni devDep: `vitest@^2.1.0`, `@types/steam-user@^5.1.1`, `@types/steamcommunity@^3.50.0`, `@types/steam-totp@^2.1.2`. `test` + `test:watch` script'leri eklendi.

## Mini Güvenlik Kontrolü

| Konu | Kontrol | Sonuç |
|---|---|---|
| Secret sızıntısı | `BotCredentials` sadece env/file kaynaklı; kod içinde hardcode yok | ✓ |
| Logger redaction | `logger.ts REDACT_PATHS` (`*.password`, `*.secret`) BotSession log'larında devrede | ✓ |
| Webhook payload | `BotSessionStatus` döner — password/secret içermez; sadece accountName + state + retryCount + lastError | ✓ |
| Auth | `/api/bots/status` → `internalKeyAuth` middleware altında; `/health` public ama detay sızdırmaz | ✓ |
| Input validation | `BotConfig.parseBotsJson` JSON + required field + tip check; readFileSync path admin-controlled env | ✓ |
| Yeni runtime dep | Yok (sadece devDep'ler eklendi) | ✓ |

## Commit & PR

- Branch: `task/T64-steam-bot-session`
- Commit: (pending push)
- PR: (pending — yapım kapısı sonrası `gh pr create`)
- CI: ⏳ Bekliyor

## Known Limitations / Follow-up

- **K1 — T14 prettier drift (10 dosya, T64 dışı):** `src/api/middleware.ts`, `src/config/index.ts`, `src/errors/SidecarError.ts`, `src/logger.ts`, `src/metrics.ts`, `src/queue/RateLimitedQueue.ts`, `src/trade/InventoryService.ts`, `src/trade/TradeOfferService.ts`, `src/types/express.d.ts`, `src/webhook/WebhookClient.ts` — Prettier format check fail veriyor (T14'ten beri böyle; CI workflow'da format check job'u yok, fark edilmemiş). T64 scope'u dışında, ayrı chore PR önerisi.
- **K2 — Capacity-based bot seçimi T69'a forward-devir:** `selectBot()` round-robin; 05 §3.2'deki "en az aktif emanet item'a sahip bot" T69 (Steam Sidecar — bot failover ve capacity-based seçim) task'ında implement edilecek. Şu an pool boyutu artarsa load eşit dağılır ama escrow count bilgisi sidecar'da tutulmuyor — T21 PlatformSteamBot.ActiveEscrowCount backend'de var; T69'da sidecar bu count'u sorgulayıp/cache'leyip seçim kriterini yükseltecek.
- **K3 — Backend webhook handler T68'e forward-devir:** `bot.session_failed` ve `bot.removed_from_pool` event'leri sidecar'dan publish ediliyor; backend'de handler şu an yok. T68 (Steam Sidecar webhook callback ve backend entegrasyonu) bu event'leri tüketip admin notification'a dönüştürecek. T64 graceful: webhook 404 alırsa log.warn ile yutar, sidecar çökmez.
- **K4 — Steam health endpoint probe T67'ye devir:** `/health` "steam-api" check'i şu an statik "healthy" döner (mesaj: "Connectivity probe deferred to T67"). Gerçek Steam Community/Web API connectivity probe T67 (envanter okuma) implementasyonunda kütüphane çağrı pattern'i netleştikten sonra eklenecek.
- **K5 — npm audit transitive vulnerabilities (T14 mirası, T64 dışı):** 20 vulnerability (12 mod / 5 high / 3 critical) — request@2.88, tough-cookie ve diğer steam-* transitive bağımlılıklar. Steam community libraries kendi major güncelleme döngülerine sahip; T64 scope dışı, ayrı chore PR (npm overrides veya bekleme).
- **K6 — Confirmation checker auto-accept "accept all" stratejisi:** `startConfirmationChecker(20000, identitySecret)` tüm pending confirmation'ları otomatik onaylar. T65 trade offer gönderme akışı bu davranışı kullanır; "sadece bizim gönderdiğimiz offer'ları onayla" filtresi `acceptAllConfirmations` callback override ile T65'te eklenebilir.

## Notlar

- **Working tree check (task.md Adım -1):** Temiz (git status --short boş).
- **Main CI startup check (task.md Adım 0):** Son 3 main run tümü `success` — run ID'ler: 25756831861, 25756831923, 25695200200.
- **Dış varsayım kontrolü (task.md Adım 4):**
  - `steam-user@^5.x` npm'de mevcut (5.0.0–5.1.x); doğrulandı: `npm view steam-user@5 version` → 5.0.0+ ✓
  - `steam-totp@^2.x` npm'de mevcut (2.0.0–2.1.2); ✓
  - `steamcommunity@^3.x` npm'de mevcut (3.0.0–3.50.0); ✓
  - `@types/steam-user@5.1.1`, `@types/steamcommunity@3.50.0`, `@types/steam-totp@2.1.2` DefinitelyTyped'da mevcut; ✓
  - Test framework için `vitest@^2.x` aktif maintenance, ESM-friendly (sidecar tsconfig commonjs olsa da TS-source ESM-style import path'lerle yazılı, vitest uyumlu); ✓
  - Bot credential kaynağı (env JSON vs file mount) — proje sahibi onayı alındı: JSON file mount + ENV fallback hybrid.
- **Scope kararı (proje sahibi onayı, 2026-05-12):** Bot config = `STEAM_BOTS_CONFIG_PATH` (öncelikli) + `STEAM_BOTS_JSON` (fallback) hybrid; test framework = Vitest.
- **F0 ⇆ T14 stub kontrolü:** T14 raporu (Docs/TASK_REPORTS/T14_REPORT.md) `BotManager/BotSession/BotHealthCheck` üçlüsünü "stub (T64)" olarak işaretlemişti — T64 bu stub'ları tam implementation'la doldurdu, dış API'lar değişmedi (constructor tek argümandan multi-arg DI'a yükseldi, ancak default değerler korundu — `new BotManager()` ve `new BotHealthCheck(manager)` yine geçerli).
- **State machine kararı:** `INITIALIZING` → `LOGGING_IN` ayrımı `start()` çağrılmadan önce hesabın "kayıtlı ama login başlamamış" durumunu gözlemlenebilir kılmak için; T69 capacity-based selector eligibility kontrolünde kullanılabilir.
- **Confirmation checker hookup yeri:** `webSession` event handler'ında, çünkü cookies olmadan steamcommunity confirmation endpoint'i çağrılamaz; `confirmationCheckerStarted` flag idempotency için (sessionExpired sonrası gelen yeni webSession başka bir checker daha başlatmaz).
