# WP8 — Admin bildirim/alert + audit tamamlama

**Faz:** F6-öncesi (PRE_F6_PLAN) | **Durum:** ⏳ Devam ediyor (doğrulama bekliyor) | **Tarih:** 2026-06-17

---

## Yapılan İşler

4 admin `NotificationType` değeri (`ADMIN_FLAG_ALERT`, `ADMIN_ESCALATION`, `ADMIN_PAYMENT_FAILURE`, `ADMIN_STEAM_BOT_ISSUE`) enum'da tanımlıydı ama **üretimde hiç emit edilmiyordu**. WP8 hepsini gerçek üretici event'lere bağlar; admin yaşam-döngüsü olayları artık admin inbox'ta görünür (yalnız Loki log / geçici realtime banner değil).

- **Admin alıcı çözümü:** yeni `IAdminRecipientResolver` (Shared abstraction) → aktif `AdminUserRole` tutan **tüm adminler** (impl Admin modülünde; Notifications→Admin modül bağımlılığı yok). Owner kararı = broadcast-to-all-admins.
- **6 yeni consumer** (mevcut `NotificationConsumerBase` deseni + yeni `AdminBroadcastNotificationConsumerBase`):
  - `FraudFlagCreatedEvent` → `ADMIN_FLAG_ALERT` (FlagId link) — tüm adminler
  - `DisputeEscalatedEvent` → `ADMIN_ESCALATION` — tüm adminler (mevcut taraf-bildirimi consumer'ına dokunulmadı; ayrı consumer-name)
  - `RefundBlockedAdminAlertEvent` → `ADMIN_PAYMENT_FAILURE` — tüm adminler
  - `TransferDispatchFailedEvent` → `ADMIN_PAYMENT_FAILURE` — tüm adminler
  - `SellerPayoutIssueEscalatedEvent` → `ADMIN_PAYMENT_FAILURE` — **atanan admin** (`EscalatedToAdminId`, event sözleşmesi gereği tek hedef)
  - yeni `BotSessionFailedEvent` → `ADMIN_STEAM_BOT_ISSUE` — tüm adminler
- **`Notification.FlagId`** (nullable Guid, filtreli index, FK yok) — `ADMIN_FLAG_ALERT` flag inbox-link'i artık ayrı kolondan türetilir (önceki `TransactionId` yeniden-yorumu yerine). `NotificationTargetMapper` `flagId` parametresi alır; dispatcher + inbox servisi geçirir.
- **`BOT_SESSION_FAILED` AuditAction** (SECURITY_EVENT) — Steam webhook handler'da mevcut `BOT_STATUS_CHANGED` kaydının **yanına additive** eklendi (T69 sözleşmesi + testleri korunur); incident JSON envelope (`{event, reason, status}`) taşır + `BotSessionFailedEvent` publish eder (ADMIN_STEAM_BOT_ISSUE alert'i).

### Owner kararları (AskUserQuestion, bu chat)
1. **Alıcı modeli:** Tüm adminler.
2. **Bot audit:** `BOT_SESSION_FAILED` ekle (additive).
3. **audit-detail-schema kapsamı:** Dar — yalnız WP8'in kendi yeni audit yazımları OldValue taşır; "RestartRecovery audit" + eski action OldValue backfill → ertelendi.
4. **Consumer kapsamı:** 4 tipin tamamı.
5. **`TradeOfferDispatchFailedEvent`:** Hariç (follow-up) — `admin-alert-consumers` backlog'unda isimlendirilmemiş + mevcut 4 NotificationType'a temiz oturmuyor.

### Stale-evidence düzeltmeleri (WP1 deseni)
- WP8 kanıtı "bot lifecycle yalnız Warning log" **stale'di** — T69/T103b-2 zaten status mutasyonu + `BOT_STATUS_CHANGED` audit + `BotRestrictedEvent` + realtime push kuruyordu. Gerçek boşluk = **in-app admin Notification** + `BOT_SESSION_FAILED` incident audit'i.
- `admin-alert-consumers` listesindeki `stranded-delegation` / `STOPPED` / `spam-token` event'leri **mevcut değil** (kapsam dışı bırakıldı).

## Etkilenen Modüller / Dosyalar

**Shared:** `Enums/AuditAction.cs` (+BOT_SESSION_FAILED), `Events/BotSessionFailedEvent.cs` (yeni), `Interfaces/IAdminRecipientResolver.cs` (yeni)
**Platform:** `Application/Audit/AuditLogCategoryMap.cs` (+SECURITY_EVENT eşleme)
**Admin:** `Application/Notifications/AdminRecipientResolver.cs` (yeni), `AdminModule.cs` (DI)
**Notifications:** `Domain/Entities/Notification.cs` (+FlagId), `Infrastructure/Persistence/NotificationConfiguration.cs` (filtreli index), `Application/Notifications/NotificationRequest.cs` + `NotificationDispatcher.cs` (FlagId), `Application/Inbox/NotificationTargetMapper.cs` + `NotificationInboxService.cs` (FlagId), `Application/EventHandlers/` (1 base + 6 consumer, yeni)
**Steam:** `Application/Webhooks/SteamWebhookHandler.cs` (BOT_SESSION_FAILED audit + BotSessionFailedEvent publish)
**Migration:** `Skinora.Shared/Persistence/Migrations/20260617194020_WP8_AddNotificationFlagId.{cs,Designer.cs}` + snapshot
**Tests:** `Skinora.Notifications.Tests/Unit/AdminAlertNotificationConsumerTests.cs` (yeni, 10), `…/Unit/NotificationTargetMapperTests.cs` (FlagId), `…/Integration/NotificationDispatcherTests.cs` (FlagId), `Skinora.Admin.Tests/Integration/AdminRecipientResolverTests.cs` (yeni, 2), `Skinora.Steam.Tests/Integration/SteamWebhookHandlerTests.cs` (bot-event), `Skinora.Shared.Tests/Unit/EnumTests.cs` + `Skinora.Platform.Tests/Unit/Audit/AuditLogCategoryMapTests.cs` (parity)
**Docs:** `06_DATA_MODEL.md` §3.13 + §5.2 index (FlagId)

## Kabul Kriterleri Kontrolü

| # | Kriter (WP8 "İş") | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Bot lifecycle → admin notification (`ADMIN_STEAM_BOT_ISSUE`) | ✓ | `BotSessionFailedAdminNotificationConsumer` + `SteamWebhookHandler` publish; `AdminAlertNotificationConsumerTests.BotSessionFailed_*` + `SteamWebhookHandlerTests.BotEvent_*` (6/6) |
| 2 | `BOT_SESSION_FAILED` audit | ✓ | `AuditAction.BOT_SESSION_FAILED` + handler audit row; `SteamWebhookHandlerTests` iki-audit assert; EnumTests 30 + AuditLogCategoryMapTests |
| 3 | `bot.session_failed`/`removed_from_pool` handler | ✓ | Zaten mevcuttu (T68); WP8 + notification + incident audit ekledi (`HandleBotEventAsync`) |
| 4 | `Notification.FlagId` (flag inbox link) | ✓ | Entity + config + dispatcher + mapper; migration `WP8_AddNotificationFlagId`; `NotificationDispatcherTests.DispatchAsync_AdminFlagAlert_*` + `NotificationTargetMapperTests` |
| 5 | admin-alert kanal consumer'ları | ✓ | 6 consumer (FLAG/ESCALATION/PAYMENT_FAILURE×3/BOT_ISSUE); `AdminAlertNotificationConsumerTests` 10/10 (fan-out + idempotency + zero-admin + single-admin) |
| 6 | central AuditLog wiring + OldValue (dar kapsam) | ✓ | WP8 audit yazımları `IAuditLogger` üzerinden + OldValue=previousStatus; RestartRecovery/backfill owner-onaylı ertelendi |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (tam suite, `Category!=Integration`) | ✓ | Shared 375 · Platform 124 · Notifications 100 (+10 yeni consumer) · + tüm diğer projeler — 0 fail |
| Consumer unit | ✓ 10/10 | `AdminAlertNotificationConsumerTests` |
| Mapper unit | ✓ | `NotificationTargetMapperTests` (FlagId flag-target + null) |
| AdminRecipientResolver integration | ✓ 2/2 | distinct adminler, soft-deleted+non-admin hariç |
| Dispatcher integration | ✓ 1/1 | FlagId round-trip + flag-target push (gerçek SQL Server) |
| Steam handler integration | ✓ 6/6 | `BotEvent_*` (iki-audit + BotSessionFailedEvent) |
| Build | ✓ | `dotnet build Skinora.sln` Debug 0W/0E |
| Format | ✓ | `dotnet format --verify-no-changes` exit 0 |
| Migration drift | ✓ | `has-pending-model-changes` → "No changes" |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validator bekliyor (ayrı chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- **Migration:** Var — `WP8_AddNotificationFlagId` (`Notification.FlagId` nullable Guid + filtreli index `IX_Notifications_FlagId WHERE [FlagId] IS NOT NULL`; FK yok; şema-only, seed yok). 21-mandatory etkilenmez.
- **Config/env:** Yok.
- **Docker:** Yok.
- **Yeni dependency:** Yok.

## Commit & PR

- Branch: `task/WP8-admin-alerts-audit`
- Commit (kod+test+migration): `df6b38b` — WP8: admin notification/alert + audit completion
- Docs commit: `fdeddb8` — WP8: report + status + plan + repo memory + 06 §3.13
- PR: **#177**
- CI: ✓ PASS — task CI HEAD `fdeddb8` run [`27716215159`](https://github.com/turkerurganci/Skinora/actions/runs/27716215159) **tüm job success** (Lint/Build/Unit/Integration/Contract/Migration dry-run/Docker/Gate — gerçek SQL Server'da integration + migration uygulandı)

## Known Limitations / Follow-up

- **`TradeOfferDispatchFailedEvent`** consumer'ı **hariç** (owner-onaylı follow-up) — promised-but-unwired admin alert; temiz NotificationType eşleşmesi yok.
- **i18n:** Admin template gövdeleri yalnız neutral (EN) resx'te; tr/es/zh neutral'a fallback eder (mevcut durum, WP17 `backend-i18n-migration` kapsamı).
- **audit-detail-schema:** "RestartRecovery audit" + eski action OldValue backfill owner-onaylı **ertelendi** (dar kapsam kararı).
- **Frontend `enums.ts` AuditAction:** `BOT_SESSION_FAILED` eklenmedi — FE enum zaten 17 değer geride (WP7 `MAINTENANCE_MODE_CHANGED` de eklenmemişti); tam FE enum sync WP13 `FE-enums-ts-lag` işi.
- Admin bildirimi her admin için ayrı `Notification` satırı (per-user model, 06 §3.13). Sıfır-admin → no-op + warning log.

## Notlar

- **Working tree:** Adım -1 temiz (session başında `git status --short` boş).
- **Adım 0 (main CI startup):** son 3 main run success (`27712112969`/`27712112694`/`27686562037`).
- **Dış varsayımlar:** (1) EF migration akışı — kanıt: çok sayıda mevcut migration + CI migration dry-run job. (2) Cross-module abstraction (Notifications→Shared `IAdminRecipientResolver`, impl Admin) yeni paket/dep gerektirmez — kanıt: `dotnet build` 0W/0E, mevcut modül referans yönü korundu. (3) `EmailCategoryMap` 4 admin tipini zaten map'liyor (Account) → email kanalı çalışır; admin template'leri neutral resx'te zaten var (T37/T38). Kırık varsayım yok.
- **Bot audit additive kararı:** Mevcut tek `BOT_STATUS_CHANGED` yazıcısı (üretimde) yalnız `SteamWebhookHandler`'dı; replace etmek T69 sözleşmesini + `SteamWebhookHandlerTests`'i bozardı → GUARDRAILS §4 (varsayılan koru) gereği additive seçildi (her non-idempotent geçişte iki SECURITY_EVENT satırı: terse transition + incident envelope).
