# T103b-2 — Bot Recovery/Failover (Steam hesapları backend tamamlama, S18)

**Faz:** F5 (geç-ekleme) | **Durum:** ✓ Yapım bitti — bağımsız doğrulama bekliyor | **Tarih:** 2026-06-13

---

## Bağlam

T103 (S18 UI, salt-frontend) recovery kuyruğunu **boş/yapısal** bırakmış, gerçek
boşluğu "→ T69 forward" diye işaretlemişti. T103b yeniden ele alınırken (2026-06-13)
boşluğun iki ön-koşulu olduğu görüldü: **(a)** escrow→bot wiring ve **(b)**
recovery/failover spec'i. (a) **T106a** (PR #166, merged `648eba9`) ile karşılandı.
Bu task kalan **(b)**'yi kapsar.

Owner kararı (AskUserQuestion 2026-06-13, bu chat): **tasarla + uygula birleşik**
(T103b-2 discovery + T103b-3 impl tek task) · recovery kuyruğu modeli = **eager
materyalize entity** · bot kısıtlandığında = **otomatik bildirim + otomatik
EMERGENCY_HOLD** · tetik kapsamı = **RESTRICTED + BANNED** (OFFLINE geçici kabul) ·
bildirim mekanizması = **mevcut SignalR push + kalıcı kuyruk** (yeni push metodu yok).

**Önemli yeniden çerçeveleme:** Kod taraması, failover'ın "tespit + yeni-işlem
yönlendirme" yarısının **zaten çalıştığını** ortaya koydu (sidecar eresult →
`bot.session_failed`/`removed_from_pool` webhook → `SteamWebhookHandler` `bot.Status`
flip + `SqlBotSelectionService` yalnız ACTIVE bot seçer). Eksik olan, **kısıtlı botta
zaten emanette duran item'ların kurtarılması** (recovery kuyruğu + triage + auto-hold).

## Yapılan İşler

**Trigger (event-driven, T106a consumer deseni):**
- `SteamWebhookHandler.HandleBotEventAsync`: RESTRICTED/BANNED'e geçişte
  `PlatformSteamBot.RestrictionReason` set + outbox `BotRestrictedEvent` yayını
  (status flip + audit + `AdminBotStatusChanged` SignalR ile aynı UoW). OFFLINE
  geçişi event yayınlamaz (geçici kabul; yeni işlemler zaten ACTIVE-filtresiyle
  yönlendiriliyor).
- `BotRestrictionRecoveryConsumer` (`INotificationHandler<BotRestrictedEvent>`):
  bota ait **stuck escrow** sorgusu (`EscrowBotId==bot && EscrowBotAssetId!=null &&
  DeliveredBuyerAssetId==null && kabul edilmiş RETURN_TO_SELLER yok`) → her işlem
  için `BotRecoveryItem` (PENDING) materyalize + non-terminal & !IsOnHold ise
  `ITimeoutFreezeService.FreezeAsync` ön-pass + `TransactionStateMachine.ApplyEmergencyHold`
  (SystemUser) + `EmergencyHoldAppliedEvent` + audit. Tek `SaveChangesAsync` (atomik);
  `BotRecoveryItem.TransactionId` UQ + var-olan kontrolü → idempotent.

**Domain:**
- Yeni `BotRecoveryItem` entity (`Skinora.Steam`; FK bot + transaction[UQ] +
  responsibleAdmin?, `RecoveryStatus` PENDING/IN_REVIEW/RESOLVED, `StatusAtRestriction`
  snapshot, `AdminNote`, `ResolvedAt`; mutable + `IAuditableEntity`) + EF config
  (UQ TransactionId, IX (PlatformSteamBotId, RecoveryStatus), 3 FK NoAction).
- `PlatformSteamBot.RestrictionReason` (`string?`, maxlen 200).
- `BotRecoveryStatus` enum (Shared.Enums) + `BotRestrictedEvent` (Shared.Events) +
  `AuditAction.BOT_RECOVERY_ITEM_CREATED` / `BOT_RECOVERY_UPDATED`.
- Migration `T103b2_AddBotRecovery` (BotRecoveryItems tablosu + PlatformSteamBots.RestrictionReason kolonu).

**Admin yüzeyi:**
- `AdminSteamBotQueryService` (AD10) artık **canlı**: `RestrictionReason` (entity'den),
  `RecoveryTransactionCount` (açık=non-RESOLVED recovery item sayısı), `FailoverStatus`
  türetimi (ACTIVE→NONE / non-ACTIVE & 0→RESTRICTED_NEW_TXN_DIVERTED / non-ACTIVE & >0→
  ACTIVE_TXN_IN_RECOVERY).
- `IAdminBotRecoveryService` + `AdminBotRecoveryService`: AD25 `GetQueueAsync` (bot
  recovery kuyruğu, Transaction + seller/buyer/admin User join'leri) + AD26 `UpdateAsync`
  (note / responsible admin / status; RESOLVED terminal-kilitli; `BOT_RECOVERY_UPDATED` audit).
- `AdminController`: `GET /admin/steam-accounts/{botId}/recovery-queue` (AD25,
  `VIEW_STEAM_ACCOUNTS`) + `PATCH /admin/steam-accounts/recovery/{id}` (AD26,
  **`MANAGE_STEAM_RECOVERY`** — katalogda var olan ama hiçbir endpoint'te enforce
  edilmeyen yetkinin ilk enforcement noktası). DI `SteamModule`.

**Frontend (S18):**
- `lib/api/admin.ts`: AD25/AD26 tipleri + `getBotRecoveryQueue` / `updateBotRecoveryItem`
  (AD10 yorumu canlı alanlara güncellendi).
- `useBotRecoveryQueue` / `useUpdateBotRecovery` hook'ları (kuyruk + liste invalidation).
- `RecoveryQueuePanel` boş→canlı: 8 kolon (İşlem ID→S16 link / Item / Taraflar / State
  `StatusBadge` + hold rozeti / Recovery Durumu rozeti / Sorumlu Admin / Not inline-editor /
  Aksiyonlar) + Manual Recovery (→IN_REVIEW) / Çözüldü (→RESOLVED) / Not Ekle aksiyonları.
- `BotRecoveryQueue` wrapper (per-bot AD25 fetch + AD26 mutation) — `SteamAccountsView`
  her kısıtlı/yasaklı bot için bir tane render eder; `SteamAccountCard` emanet notu
  "recovery kuyruğunda listelenir"e güncellendi.
- i18n `adminSteamAccounts.recovery` 4-locale (44 leaf×4 IDENTICAL).

## Etkilenen Modüller / Dosyalar

- **Yeni (backend):** `BotRecoveryStatus.cs`, `BotRestrictedEvent.cs`, `BotRecoveryItem.cs`,
  `BotRecoveryItemConfiguration.cs`, `BotRestrictionRecoveryConsumer.cs`,
  `AdminBotRecoveryDtos.cs`, `IAdminBotRecoveryService.cs`, `AdminBotRecoveryService.cs`,
  migration `20260613201648_T103b2_AddBotRecovery`.
- **Değişen (backend):** `AuditAction.cs`, `PlatformSteamBot.cs`,
  `PlatformSteamBotConfiguration.cs`, `SteamWebhookHandler.cs`,
  `AdminSteamBotQueryService.cs`, `AdminSteamBotDtos.cs`, `AdminController.cs`, `SteamModule.cs`.
- **Yeni (frontend):** `BotRecoveryQueue.tsx`.
- **Değişen (frontend):** `lib/api/admin.ts`, `useAdminSteamAccounts.ts`,
  `RecoveryQueuePanel.tsx`, `SteamAccountsView.tsx`, `SteamAccountCard.tsx`,
  `components/admin/index.ts`, 4× i18n.
- **Test:** `BotRestrictionRecoveryConsumerTests.cs` (7), `AdminBotRecoveryServiceTests.cs` (9),
  `SteamWebhookHandlerTests.cs` (uzatıldı), `AdminT63EndpointTests.cs` (+6).
- **Doc:** 06 §3.10a, 07 §9.10/§9.28 (AD25)/§9.29 (AD26), 11_IMPLEMENTATION_PLAN, DEFERRED_BACKLOG.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Kısıtlı/banned bot kısıtlandığında recovery kuyruğu materyalize olur | ✓ | `BotRestrictionRecoveryConsumerTests.Restriction_MaterialisesAndHolds_StuckEscrows` |
| 2 | Emanetteki item'lar listelenir (kısıtlı hesap) | ✓ | AD25 `GetQueue_ReturnsRows_WithJoinedTransactionAndParties` + FE `RecoveryQueuePanel` |
| 3 | Recovery Queue satır verisi (state/recovery durumu/sorumlu admin/not) | ✓ | `BotRecoveryQueueItemDto` + FE 8 kolon |
| 4 | `MANAGE_STEAM_RECOVERY` aksiyonları (Manual Recovery / not / sorumlu admin) | ✓ | AD26 PATCH + `UpdateRecovery_WithViewButNotManage_Returns403` (enforcement) |
| 5 | Otomatik bildirim + otomatik EMERGENCY_HOLD | ✓ | Consumer auto-hold + `AdminBotStatusChanged` push + RecoveryTransactionCount badge |
| 6 | AD10 RestrictionReason/FailoverStatus/RecoveryTransactionCount canlı | ✓ | `QueryService_DerivesFailoverStatusAndRecoveryCount` |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Steam.Tests | ✓ 92/92 | `dotnet test` (76 → +16: consumer 7 + recovery service/derivation 9; webhook tests uzatıldı) |
| API.Tests (AdminT63) | ✓ 30/30 | (24 → +6: AD25 3 + AD26 3, permission split dahil) |
| Build | ✓ | `dotnet build src/Skinora.API` 0W/0E |
| Format | ✓ | `dotnet format --verify-no-changes` temiz (5 proje) |
| FE tsc / eslint | ✓ | `tsc --noEmit` 0 + `eslint` 0 |
| FE prettier | ✓ | `--end-of-line auto` temiz (Windows CRLF artefaktı) |
| FE i18n parity | ✓ | 1131×4 IDENTICAL (adminSteamAccounts 44 leaf) |
| FE build | ✓ | `next build` 30 route (`/admin/steam-accounts` ƒ) |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Yapım bitti — bağımsız validator bekliyor (ayrı chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- **Migration:** Var — `T103b2_AddBotRecovery` (BotRecoveryItems tablosu + PlatformSteamBots.RestrictionReason kolonu).
- **Config/env:** Yok.
- **Docker:** Yok.
- **Yeni dış bağımlılık:** Yok.

## Commit & PR

- Branch: `task/T103b-2-bot-recovery`
- PR: [#167](https://github.com/turkerurganci/Skinora/pull/167)
- Commit: `dbac988` (kod+migration+test) + `048a013` (rapor+status+doc+memory)
- CI: ⏳ (Claude izler — [[feedback_claude_watches_ci_always]])

## Known Limitations / Follow-up

- **K1 — Sorumlu admin atama UI'sı (dropdown) ertelendi:** Backend AD26
  `responsibleAdminId` tam destekler ve `responsibleAdminName` okunur; FE şimdilik
  yalnız note + status aksiyonlarını açar. Admin-listesi dropdown'u polish follow-up.
- **K2 — OFFLINE bot recovery tetiklemez:** Geçici session kaybı kabul edilir
  (owner kararı). Kalıcı OFFLINE'da emanet item'lar BotHealthCheck'in restricted/banned'e
  yükseltmesine veya gelecekteki manuel tetiğe bağlı.
- **K3 — Dedicated "X işlem etkilendi" SignalR push'u yok:** Mevcut `AdminBotStatusChanged`
  push'u + kalıcı kuyruk/RecoveryTransactionCount kullanılır (owner kararı; FE admin
  SignalR aboneliği zaten ertelenmiş — DEFERRED_BACKLOG).
- **K4 — Recovery RESUME orkestrasyonu yok:** Auto-hold uygulanan işlemler bot
  düzeldiğinde otomatik RESUME edilmez; admin AD19c (release-hold) ile manuel devam ettirir.
- **K5 — Terminal stuck (CANCELLED, refund bekleyen) item'lar materyalize olur ama
  hold edilmez** (zaten terminal); recovery aksiyonu manuel iade/Steam support.

## Notlar

- **Dış varsayımlar (Adım 4):** (1) T106a merged & main'de — `gh pr view 166` MERGED
  `648eba9` ✓; (2) `MANAGE_STEAM_RECOVERY` katalogda mevcut ama enforce edilmiyordu —
  `PermissionCatalog.cs:20,40` okundu, bu task ilk enforcement'ı ekledi; (3) bot
  restriction pipeline (sidecar→webhook→status flip) zaten çalışıyor — `SteamWebhookHandler`
  + `SqlBotSelectionService.cs:29` ACTIVE filtresi okundu.
- **Adım -1 (working tree):** session başı temiz.
- **Adım 0 (main CI son-3):** `27476215481`/`27476215495` (T106a #166 CI+Docker) +
  `27471077378` (F-INVITE-01) → hepsi `success`.
- Yapım-içi adversarial inceleme yapılmadı (validator ayrı chat'te bağımsız çalışacak —
  [[feedback_validation_separate_chat]]).
