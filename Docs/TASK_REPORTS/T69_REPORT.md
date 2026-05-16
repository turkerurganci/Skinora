# T69 — Steam Sidecar — Bot Failover ve Capacity-Based Seçim

**Faz:** F4 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekleniyor) | **Tarih:** 2026-05-16

---

## Yapılan İşler

### Backend — Bot lifecycle webhook handler genişletme (acceptance #2, #3, #4)

- `backend/src/Skinora.Shared/Enums/AuditAction.cs` — yeni `BOT_STATUS_CHANGED` değeri (toplam 21 enum üye). Sidecar-driven RESTRICTED/BANNED transition'ları AuditLog'a yazılır.
- `backend/src/Modules/Skinora.Platform/Application/Audit/AuditLogCategoryMap.cs` — `BOT_STATUS_CHANGED` `SECURITY_EVENT` kategorisine eklendi (`WALLET_ADDRESS_CHANGED` yanına; iki SECURITY_EVENT girdisi var).
- `backend/src/Modules/Skinora.Steam/Application/Webhooks/SteamWebhookHandler.cs` — `HandleBotEventAsync` artık T68'in log-only davranışını genişletip:
  1. `BotEventData.AccountName` ile `PlatformSteamBot.DisplayName` üzerinden bot satırını çeker.
  2. `BotEventData.Reason` (sidecar `BotFailureReason` union) → `PlatformSteamBotStatus` map: `banned`→BANNED, `restricted`/`rate_limited`→RESTRICTED, `login_failed`/`session_recovery_failed`/bilinmeyen→OFFLINE.
  3. Status değişirse `PlatformSteamBot.Status` + `LastHealthCheckAt` günceller, `IAuditLogger.LogAsync` ile `BOT_STATUS_CHANGED` audit row yazar (ActorType.SYSTEM, OldValue=eski Status, NewValue=`"{yeni};reason={sidecarReason};event={evt}"`), tek `SaveChangesAsync`.
  4. Aynı status idempotent — yalnızca `LastHealthCheckAt` refresh, audit + push yok (operator dashboard duplicate banner görmesin).
  5. Bilinmeyen `accountName` veya `accountName` boş → log + ack (state değişikliği yok).
  6. `INotificationRealtimePublisher.PublishAdminBotStatusChangedAsync` ile admin SignalR push (acceptance #4).
- `backend/src/Modules/Skinora.Steam/Skinora.Steam.csproj` — yeni `Skinora.Platform` + `Skinora.Realtime` ProjectReference (Audit + Realtime publisher için).

### Backend — Capacity-based bot selection (acceptance #1)

- `backend/src/Modules/Skinora.Steam/Application/BotSelection/IBotSelectionService.cs` — yeni interface. Tek metod `SelectAsync(ct)` → `PlatformSteamBot?`.
- `backend/src/Modules/Skinora.Steam/Application/BotSelection/SqlBotSelectionService.cs` — EF Core impl. `Status == ACTIVE` filtre + `ActiveEscrowCount` ASC + `LastHealthCheckAt ?? DateTime.MinValue` ASC + `Id` ASC. `AsNoTracking` (forward-deferred caller `EscrowBotId` atamasını ayrı bir tracked read ile yapar). Soft-delete query filter PlatformSteamBotConfiguration tarafından zaten enforce ediliyor.
- `backend/src/Skinora.API/Configuration/SteamModule.cs` — `services.AddScoped<IBotSelectionService, SqlBotSelectionService>()` kaydı.

### Backend — Admin SignalR push adapter (acceptance #4)

- `backend/src/Modules/Skinora.Realtime/Application/Contracts/NotificationRealtimePayloads.cs` — yeni `AdminBotStatusChanged(BotId, SteamId, DisplayName, PreviousStatus, NewStatus, Reason, ChangedAt)` record.
- `backend/src/Modules/Skinora.Realtime/Application/INotificationRealtimePublisher.cs` — yeni `PublishAdminBotStatusChangedAsync(payload, ct)` metodu. Broadcast (per-role group abstraction henüz yok); event `AdminBotStatusChanged`. Frontend admin guard'lı sayfada dinler, normal user'lar event ismini bilmiyor → fanout güvenli. Yorumda T62 maintenance push pattern'ine referans + best-effort kontrat.
- `backend/src/Modules/Skinora.Realtime/Infrastructure/SignalRNotificationRealtimePublisher.cs` — `AdminBotStatusChanged` event sabiti + `Hub.Clients.All.SendAsync` fanout. Exception → `LogWarning` (audit row + Status update zaten persisted; missed push admin dashboard refresh ile recover).

### Sidecar — RESTRICTED/BANNED eresult mapping (acceptance #2)

- `sidecar-steam/src/bot/BotSession.ts`:
  - `BotFailureReason` union'a `'restricted'` eklendi.
  - `BANNED_ERESULTS` set genişletildi (3, 17, 40, 43, 51, 70, 73, 105 — T69 dış varsayım doğrulamasına göre).
  - Yeni `RESTRICTED_ERESULTS` set (11, 15, 25, 82, 85, 95, 96, 97, 112, 116). `84 RateLimitExceeded` kasıtlı olarak burada **değil** — T65 backoff ile in-process retry semantiği korunmalı; sadece kalıcı limit-class kodlar (95/96/97) burada.
  - `client.on('error')` handler'ı RESTRICTED kontrolü ekledi: bulunursa `transition('FAILED')` + `emitFatal('restricted')`. BotManager bunu `bot.session_failed` webhook'una `reason='restricted'` ile aktarır; backend RESTRICTED Status'a map eder.

### Tests

- `backend/tests/Skinora.Steam.Tests/TestSupport/RecordingNotificationRealtimePublisher.cs` — yeni local recorder (cross-module test reference büyütmemek için). T69 testlerinin admin push'unu inceleyebileceği `(Method, Payload)` tuple listesi.
- `backend/tests/Skinora.Notifications.Tests/TestSupport/RecordingNotificationRealtimePublisher.cs` — yeni `PublishAdminBotStatusChangedAsync` override (interface kontratı senkron tutmak için).
- `backend/tests/Skinora.Steam.Tests/Integration/SteamWebhookHandlerTests.cs`:
  - `CreateSut()` artık gerçek `AuditLogger(Context, TimeProvider.System)` + RecordingNotificationRealtimePublisher + `TimeProvider.System` enjekte ediyor; eski `BotEvent_LogsAndAcks_WithoutDbWrite` testi 6 yeni testle değiştirildi:
    - `BotEvent_Restricted_UpdatesStatusAuditsAndPushes` — Status=RESTRICTED, AuditLog row, SignalR call kontratı tam doğrulanır
    - `BotEvent_Banned_UpdatesStatusToBanned` — Status=BANNED + reason="banned"
    - `BotEvent_SessionRecoveryFailed_SetsOffline` — Status=OFFLINE
    - `BotEvent_IdempotentSameStatus_DoesNotAuditOrPush` — aynı status: audit + push yok, LastHealthCheckAt güncellenir
    - `BotEvent_UnknownAccount_LogsAndAcksWithoutStateChange` — bilinmeyen account: state değişmez
    - `BotEvent_MissingAccountName_LogsAndAcks` — boş accountName: skip
- `backend/tests/Skinora.Steam.Tests/Integration/SqlBotSelectionServiceTests.cs` — yeni dosya, 5 test:
  - `SelectAsync_PrefersLowestActiveEscrowCount` — capacity-based ordering ana doğrulama
  - `SelectAsync_SkipsRestrictedBannedAndOfflineBots` — Status filter
  - `SelectAsync_SkipsSoftDeletedBots` — IsDeleted filter
  - `SelectAsync_TiesBrokenByOldestLastHealthCheck` — tie-break LRU
  - `SelectAsync_ReturnsNullWhenNoActiveBots` — empty pool guard
- `backend/tests/Skinora.Platform.Tests/Unit/Audit/AuditLogCategoryMapTests.cs` — `ActionsInCategory_SECURITY_EVENT_*` testi güncellendi (artık `WALLET_ADDRESS_CHANGED + BOT_STATUS_CHANGED` döner).
- `backend/tests/Skinora.Shared.Tests/Unit/EnumTests.cs` — `AuditAction_ShouldHave20Values` → `21Values` + `BOT_STATUS_CHANGED` parametric test inline data.
- `sidecar-steam/src/bot/BotSession.test.ts`:
  - `it.each([17, 40, 43, 51, 73, 105])` — yeni BANNED eresult parametrik test
  - `it.each([11, 15, 25, 82, 85, 95, 96, 97, 112, 116])` — yeni RESTRICTED eresult parametrik test (reason='restricted' kontrat)
  - `rate-limit eresult 84 stays transient` — 84'ün RESTRICTED'a düşmediği regresyon koruması

## Etkilenen Modüller / Dosyalar

- Skinora.Shared.Enums (AuditAction)
- Skinora.Platform.Application.Audit (AuditLogCategoryMap)
- Skinora.Realtime.Application (INotificationRealtimePublisher interface + Contracts)
- Skinora.Realtime.Infrastructure (SignalRNotificationRealtimePublisher)
- Skinora.Steam.Application.BotSelection (yeni alt-namespace)
- Skinora.Steam.Application.Webhooks (SteamWebhookHandler)
- Skinora.API.Configuration (SteamModule DI)
- Skinora.Steam.csproj (yeni ProjectReference'lar)
- sidecar-steam/src/bot/BotSession.ts
- Test dosyaları (Steam.Tests + Notifications.Tests + Platform.Tests + Shared.Tests + sidecar)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Capacity-based bot seçimi: en az emanet item olan aktif bot | ✓ | `SqlBotSelectionService.SelectAsync` query: `Status==ACTIVE` filter + `OrderBy(ActiveEscrowCount).ThenBy(LastHealthCheckAt).ThenBy(Id)`. Test `SqlBotSelectionServiceTests.SelectAsync_PrefersLowestActiveEscrowCount` direkt doğrular. Caller (TransactionStateMachine entry-point) forward-deferred — K1. |
| 2 | Kısıtlı bot tespiti: yeni işlemler diğer botlara yönlendirme | ✓ | İki katman: (a) Sidecar `BotSession.RESTRICTED_ERESULTS` 10 yeni eresult → `emitFatal('restricted')` → `BotManager.removeFromPool` → pool'dan çıkar; (b) Backend `SteamWebhookHandler` `restricted` reason → `PlatformSteamBot.Status = RESTRICTED`; `IBotSelectionService` `Status == ACTIVE` filter → otomatik diğer botlara yönelir. Test: sidecar `it.each([11,15,25,...])`, backend `BotEvent_Restricted_*` + `SqlBotSelectionServiceTests.SelectAsync_SkipsRestrictedBannedAndOfflineBots`. |
| 3 | Kısıtlı botta emanet item'lar: recovery/manual intervention akışı | ~ Kısmi (minimal — proje sahibi kararı) | AuditLog `BOT_STATUS_CHANGED` row (OldValue=eski Status, NewValue=`{yeni};reason=…;event=…`) + admin SignalR push admin operatöre sinyal verir. Aktif `Transaction.EscrowBotId` değişmez; otomatik recovery state machine T-future. Known Limitation K2. |
| 4 | Admin bildirim: bot kısıtlandı uyarısı | ✓ | `INotificationRealtimePublisher.PublishAdminBotStatusChangedAsync` broadcast event `AdminBotStatusChanged`. Test `BotEvent_Restricted_UpdatesStatusAuditsAndPushes` payload alanları (BotId/SteamId/DisplayName/PreviousStatus/NewStatus/Reason/ChangedAt) doğrular. |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend Release build | ✓ 0W/0E | `dotnet build --configuration Release` 26 proje (Elapsed 20.57s) |
| Backend unit (filter Unit) | ✓ 774/774 PASS | Notifications 49 + Steam 13 + Transactions 333 + Shared 181 + Auth 57 + Platform 102 + Fraud 14 + Realtime 25 |
| Backend format | ✓ | `dotnet format --verify-no-changes` 0 değişiklik |
| Sidecar build | ✓ 0W/0E | `npm run build` (tsc) |
| Sidecar tests | ✓ 140/140 PASS | `vitest run` 9 test file; BotSession.test.ts 37 (+ T69 RESTRICTED 10 + BANNED 6 + rate-limit 1 regresyon) |
| Sidecar lint | ✓ | `npm run lint` 0 ihlal |
| Backend integration | ⏳ CI'da | Docker Desktop lokalde yok; TestContainers SQL Server CI ortamında çalışacak. T69 integration testleri (`SteamWebhookHandlerTests.BotEvent_*` 6 yeni + `SqlBotSelectionServiceTests` 5 yeni) CI run'ında doğrulanacak |

## Altyapı Değişiklikleri

- **Migration:** Yok. `PlatformSteamBotStatus` enum'unun tüm 4 değeri (`ACTIVE`/`RESTRICTED`/`BANNED`/`OFFLINE`) zaten F1'de eklenmişti.
- **Config/env değişikliği:** Yok.
- **Docker değişikliği:** Yok.
- **Yeni dış bağımlılık:** Yok.
- **Yeni cross-module reference:** `Skinora.Steam → Skinora.Platform` ve `Skinora.Steam → Skinora.Realtime` (audit + admin notification için). Döngüsel değil — Platform ve Realtime, Steam'i referans almıyor.

## Commit & PR

- Branch: `task/T69-bot-failover-capacity`
- Commit: TBD (T69 push sonrası)
- PR: TBD (push sonrası `gh pr create`)
- CI: TBD (gh run watch)

## Known Limitations / Follow-up

- **K1 — Backend trade offer dispatch caller'ı T-future devir:** Sidecar `POST /api/trade-offers/send` endpoint'ini çağıracak backend caller (örn. `TransactionStateMachine` `EscrowItem` trigger hook'u veya `ITradeOfferDispatcher` host) plan'da ayrı bir task olarak tanımsız. T69 yalnızca `IBotSelectionService`'ı kullanılabilir hale getirir; gerçek tüketici eklenince hemen plug-in olur. Proje sahibi onayı: minimal scope, caller T-future (2026-05-16).
- **K2 — Tam otomatik bot recovery / "transfer aktif emanet item" workflow T-future:** Acceptance #3 minimal yaklaşımla (audit + admin SignalR push) karşılandı. Otomatik state machine (örn. `BotRecoveryIssue` entity, `PENDING → RESOLVED / MANUAL_INTERVENTION`) plan'da tanımsız; T-future task'a devredildi. Proje sahibi onayı (2026-05-16).
- **K3 — Sidecar in-memory capacity counter yok (Yaklaşım A pure):** Capacity-based seçim tek otorite backend DB. Sidecar `BotManager.selectBot()` round-robin'i T64'ten beri korunuyor; backend dispatch caller T-future olduğu için pratik etkisi yok. Caller eklendiğinde `botAccountName` zorunlu hint geçirir.
- **K4 — Admin per-role SignalR group abstraction'ı yok:** `AdminBotStatusChanged` broadcast (`Clients.All`) — frontend admin route guard event'i sadece admin sayfalarında dinler. Per-role group T96 SignalR client task'ı zamanında değerlendirilecek; T69 broadcast ile yetinir (maintenance push patternine uygun).
- **K5 — AuditLog kategorisi SECURITY_EVENT — bot lifecycle "Güvenlik" group'a girdi:** 06 §2.19 group tanımı dışında bir karar; gerekçe `AuditLogCategoryMap.cs` yorumda: RESTRICTED/BANNED bir güvenlik olayı (bot trade privilege kaybı) ama platform-altyapı değişikliği de. ADMIN_ACTION yerine SECURITY_EVENT seçildi çünkü WALLET_ADDRESS_CHANGED'in komşuluğunda mantıklı durur ve admin queue'da yüksek-hacimli wallet-tx satırlarıyla karışmaz. Doc-uyum sonradan eklenebilir.

## Notlar

### Dış Varsayım Doğrulaması (T11/T14 dersi)

- **steam-user EResult enum** (`github.com/DoctorMcKay/node-steam-user/enums/EResult.js`) WebFetch ile doğrulandı; BANNED ve RESTRICTED kategorisine giren tüm kodlar enum'da mevcut.
- **steam-tradeoffer-manager EResult** (`github.com/DoctorMcKay/node-steam-tradeoffer-manager/resources/EResult.js`) WebFetch ile doğrulandı; trade offer hatalarındaki kodlar steam-user ile uyumlu (aynı SteamKit-derived numaralar).
- **TradeOfferError.cause string** kontrolü (TradeBan, NotAvailable, vs.) WebFetch'in `lib/index.js` yetersiz olduğunu raporladı; eresult-based mapping yeterli olduğu için doğrulama kapatıldı (sidecar zaten `error.eresult` üzerinden çalışıyor, cause string'i yardımcı sinyal değil).

### Working Tree + Main CI Check (task.md Adım -1 + Adım 0)

- Working tree: temiz (`git status --short` boş, branch `main` 11246ca commit'inde)
- Main CI son 3 run: 25957448055 ✓ success / 25957448051 ✓ success / 25939547135 ✓ success (T68 squash + chore + T67)

### Scope Kararları (2026-05-16 chat)

- Capacity-based seçim yeri: **Yaklaşım A (Backend seçer, sidecar `botAccountName` hint alır)** — `IBotSelectionService` backend'de. Sidecar `selectBot()` round-robin'de bırakıldı çünkü backend dispatch caller'ı yokken seçim authoritativeliği DB'ye taşımak yeterli.
- Recovery scope: **Minimal (audit log + admin SignalR push)** — otomatik state machine T-future K2.
- Dış varsayım: **Önce doğrula, sonra implement et** — T11/T14 dersi tatbik edildi.
- Dispatch caller scope dışı (K1): plan'da tanımsız caller'ı T69'a sıkıştırmamak için "minimal scope" tercih edildi.

### Branch Isolation Check

```bash
$ git log main..HEAD --format='%s' | grep -oE '^T[0-9]+(\.[0-9]+)?[a-z]?' | sort -u
# (push sonrası kontrol edilecek — sadece T69 satırı bekleniyor)
```
