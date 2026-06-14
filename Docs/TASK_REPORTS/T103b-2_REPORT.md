# T103b-2 — Bot Recovery/Failover (Steam hesapları backend tamamlama, S18)

**Faz:** F5 (geç-ekleme) | **Durum:** ⟳ 4 bulgu düzeltildi (2026-06-14) — bağımsız yeniden-doğrulama bekliyor | **Tarih:** 2026-06-13 (yapım) · 2026-06-14 (doğrulama ✗ FAIL → düzeltme)

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
  `BotRecoveryMaterialiser.cs` (F3 — ortak tek-tx materyalizasyon: consumer + webhook),
  `AdminBotRecoveryDtos.cs`, `IAdminBotRecoveryService.cs`, `AdminBotRecoveryService.cs`,
  migration `20260613201648_T103b2_AddBotRecovery`.
- **Değişen (backend):** `AuditAction.cs`, `PlatformSteamBot.cs`,
  `PlatformSteamBotConfiguration.cs`, `SteamWebhookHandler.cs` (F3 inline safety-net),
  `BotRestrictionRecoveryConsumer.cs` (materialiser'a delege + doc-comment fix),
  `AdminBotRecoveryService.cs` (F1 enum guard),
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
| 4 | `MANAGE_STEAM_RECOVERY` aksiyonları (Manual Recovery / not / sorumlu admin) | ✓ | Manual Recovery + Not + permission-split ✓ (AD26 PATCH + `UpdateRecovery_WithViewButNotManage_Returns403`); **"Sorumlu Admin Ata/Değiştir" dropdown F2 düzeltmesiyle eklendi** (`RecoveryQueuePanel` `<select>` ← `useAdminUsers`, 04 §8.7 satır 1727). |
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
| Doğrulama durumu | ✗ **FAIL** (bağımsız validator, ayrı chat, 2026-06-14) |
| Bulgu sayısı | 4 (1×S1 + 2×S3 + 1×NOTE); adversarial review 21 ham → 4 onaylı / 17 çürütüldü |
| Düzeltme gerekli mi | Evet — owner kararı: 4 bulgunun **hepsi** düzeltilecek (erteleme yok) |

**Kanıt tabanı:** Task-branch CI HEAD `d129c86` run [`27479328219`](https://github.com/turkerurganci/Skinora/actions/runs/27479328219) tüm job success (Lint/Build/Unit/**Integration**/**Contract**/**Migration dry-run**/Docker×2/Gate). Entegrasyon testleri lokalde Docker yokluğundan çalışmadı → CI authoritative. Lokal: unit yeşil (Shared 361 / Realtime 25 / Steam unit 20 / Platform unit 106), FE tsc 0 / eslint 0 / `next build` ✓ / i18n parity **1131×4** (recovery alt-ağacı **24×4** — rapordaki "44" miscount, parite korunuyor). Güvenlik: secret/dep yok, permission split testli — istisna F1.

### Validator Bulguları

- **F1 — S1 Sapma — AD26 enum range guard yok** (`AdminBotRecoveryService.cs:122-129`). `JsonStringEnumConverter` default (`allowIntegerValues=true`) + `Enum.IsDefined` guard yokluğu → `{"recoveryStatus":99}` bağlanır ve `"99"` persist edilir. 07 §9.29 `VALIDATION_ERROR` kontratını + kod tabanı `Enum.IsDefined` emsalini (`TransactionCreationService.cs:92`) ihlal eder; bozuk satır `RecoveryTransactionCount`/`FailoverStatus` canlı metriğini kirletir (99 ≠ RESOLVED → açık sayılır). **Fix:** `UpdateAsync`'te status atamasından önce `if (request.RecoveryStatus is { } s && !Enum.IsDefined(s)) return Failure(... VALIDATION_ERROR ...)` + test. **MANAGE_STEAM_RECOVERY-gated → auth bypass değil; düşük exploitability ama gerçek kontrat sapması.**
- **F2 — S3 Eksik — "Sorumlu Admin Ata/Değiştir" UI aksiyonu yok** (`RecoveryQueuePanel.tsx:122-126,174-204`). AC#4'ün açık parçası (plan satır 2163) + 04 §8.7 satır 1727 spec aksiyonu. Backend AD26 tam destekler, UI'da yalnız okuma kolonu var. Rapor K1 deferral'ı **owner-onaylı değildi** (DEFERRED_BACKLOG'da kayıtsız). **Owner kararı (2026-06-14): şimdi uygula** — FE'ye admin-seçim dropdown'u (`useAdminUsers` hook mevcut) eklenecek.
- **F3 — S3 Eksik (dar/edge) — Boundary race** (`BotRestrictionRecoveryConsumer.cs:88-96`). Consumer'ın nokta-zaman stuck sorgusundan SONRA gelen escrow `trade_offer.accepted` (kısıtlama anında uçuşta olan teklif) `AcceptEscrowAsync`'te restricted-bot kontrolü olmadan item'ı bota yerleştirir → recovery satırı + auto-hold OLUŞMAZ; reconciliation safety-net yok (delivery dispatch hatasıyla kısmen yüzeye çıkar). 03 §11.2a step 3 garanti edilmiyor. **Owner kararı (2026-06-14): çöz** — reconciliation/safety-net (ör. `BotHealthCheck`'te periyodik custody re-scan, VEYA `AcceptEscrowAsync` sonrası bot RESTRICTED/BANNED ise tek-tx recovery materyalizasyonu). Tasarım yapım chat'inde owner ile netleşir.
- **F4 — NOTE — Stale docstring** (`app/[locale]/admin/steam-accounts/page.tsx:11-13`). Recovery kuyruğu "boş kalır / T69'a deferred" diyor — T103b-2 sonrası yanlış (kardeş `SteamAccountsView.tsx` doğru güncel; `page.tsx` bu branch'te dokunulmamış). JSDoc-only, runtime etkisi yok. **Fix:** docstring güncelle.

**Çürütülen dikkate değer bulgular (17/21):** consumer'ın `SaveChangesAsync`'i kendi çağırması "S2 batch-rollback coupling" iddiasıyla raporlandı → **çürütüldü** (OutboxDispatcher per-message try/catch, batch rollback yok; idempotent consumer at-least-once kontratına uygun) — ancak no-self-commit yazma-consumer konvansiyonundan bir **deviation** + sınıf doc-comment'inin "tek SaveChanges tüm batch'i rollback eder" ifadesinin dispatcher bağlamında **yanlış** olduğu not edildi (cleanup-grade; yapım chat'i ele alabilir). Diğer çürütülenler: enum string-persist (mid-enum AuditAction güvenli), 3. FK index EF-convention ile mevcut, AD10 kontratı S12 için korunuyor, atomik outbox commit, RESTRICTED/BANNED trigger + OFFLINE exclusion doğru, TS↔DTO 17-alan birebir.

### Düzeltmeler (2026-06-14, yapım chat'i — owner: erteleme yok)

- **F1 ✓ (S1) Enum range guard.** `AdminBotRecoveryService.UpdateAsync` artık değişiklik
  uygulamadan önce `if (request.RecoveryStatus is { } s && !Enum.IsDefined(s)) → VALIDATION_ERROR`
  döner (`TransactionCreationService.cs:92` emsali; 07 §9.29 kontratı). `{"recoveryStatus":99}`
  artık reddedilir, canlı `RecoveryTransactionCount`/`FailoverStatus` metrikleri kirletilmez.
  Test: `AdminBotRecoveryServiceTests.Update_OutOfRangeRecoveryStatus_ReturnsValidationError`
  (geçersiz bind reddedilir + satır PENDING kalır).
- **F2 ✓ (S3) "Sorumlu Admin Ata/Değiştir" dropdown.** `RecoveryQueuePanel` Sorumlu Admin
  kolonu artık `useAdminUsers` (AD15, pageSize 100) ile beslenen bir `<select>`: rol sahibi
  (staff) kullanıcılar + zaten-atanmış admin fallback (rolü sonradan kaldırılsa bile görünür);
  değişimde AD26 `responsibleAdminId` PATCH'i. RESOLVED satırlarda salt-okunur. i18n
  `recovery.actions.assignAdmin` 4-locale eklendi. AC#4 artık tam karşılanıyor (Manual Recovery
  + Not + **Sorumlu Admin** + permission-split).
- **F3 ✓ (S3 edge) Boundary race safety-net — owner kararı Option B (inline, AskUserQuestion
  2026-06-14).** Tek-tx materyalizasyon mantığı ortak `IBotRecoveryMaterialiser` /
  `BotRecoveryMaterialiser`'a çıkarıldı; consumer ona delege eder (tek doğru kaynak, sapma yok).
  `SteamWebhookHandler.AcceptEscrowAsync`: escrow baca­ğı ITEM_ESCROWED'a ilerledikten sonra
  alıcı bot RESTRICTED/BANNED ise **aynı UoW'da** recovery satırı + auto-EMERGENCY_HOLD
  materyalize eder (idempotent — `TransactionId` UQ backstop; `AdjustEscrowCountAsync`'in zaten
  yüklediği bot `Local`'dan okunur, ekstra sorgu yok). DI `SteamModule`. Tests:
  `SteamWebhookHandlerTests.TradeOfferAccepted_Escrow_OnRestrictedBot_OpensRecoveryAndHolds`
  + `…_OnActiveBot_DoesNotOpenRecovery` (ACTIVE kontrol). 03 §11.2a step 3 artık garanti.
  *(Option A — periyodik custody re-scan — reddedildi: auto-hold'un ortadan kaldırmak için var
  olduğu hold-gecikmesini geri getirir + backend'de olmayan yeni hosted-service yükü.)*
- **F4 ✓ (NOTE) Stale docstring.** `app/[locale]/admin/steam-accounts/page.tsx` JSDoc'u
  güncellendi (recovery kuyruğu artık her kısıtlı/yasaklı bot için canlı — AD25/AD26).
- **Opsiyonel temizlik ✓:** `BotRestrictionRecoveryConsumer` sınıf doc-comment'i düzeltildi
  (yanlış "tek SaveChanges tüm batch'i rollback eder" → outbox dispatcher per-message try/catch;
  yalnız bu consumer'ın kendi UoW'u geri alınır). `SteamAccountCard.tsx` prettier sarımı düzeltildi.

**Düzeltme sonrası lokal doğrulama:** backend `dotnet build` 0W/0E (sln) + `dotnet format
--verify-no-changes --severity error` temiz; FE `tsc --noEmit` 0 + `eslint` 0 + `next build` ✓
+ touched i18n/TSX prettier (`--end-of-line auto`) temiz. Yeni backend testleri **entegrasyon**
(SQL Server gerekir) → lokalde Docker yok, CI authoritative.

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

- **K1 — Sorumlu admin atama UI'sı (dropdown):** ✓ **F2 ile kapatıldı (2026-06-14).**
  `RecoveryQueuePanel` Sorumlu Admin kolonu `useAdminUsers` (AD15) ile beslenen bir
  `<select>` oldu → AD26 `responsibleAdminId` PATCH'i. Artık ertelenmiş değil.
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
