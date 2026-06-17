# WP5 — Dispute çözüm (admin)

**Faz:** F6-öncesi (PRE_F6_PLAN) | **Durum:** ⏳ Devam ediyor (bağımsız validator bekliyor) | **Tarih:** 2026-06-17

---

## Yapılan İşler

ESCALATED dispute **çıkmaz sokağı** kapatıldı: bir dispute ESCALATED'a düştükten sonra onu kapatacak hiçbir kod yolu yoktu → `Transaction.HasActiveDispute` kalıcı `true` → WP1 `SellerPayoutQueueJob` / WP3 `SweepQueueJob` o işlemi süresiz bloke ediyordu; `Dispute.AdminId`/`AdminNote` ölü alanlardı.

**Owner kararları (AskUserQuestion 2026-06-17):** çözüm modeli = **yapısal statü + migration** · kapsam = **full-stack** · permission = **VIEW_DISPUTES + MANAGE_DISPUTES çifti** · minör kalemler = **ikisi de WP5'te**.

- **Yapısal statü:** yeni `DisputeStatus.RESOLVED_FOR_SELLER` / `RESOLVED_FOR_BUYER` (admin çözüm terminalleri; `CLOSED` yalnız sistem auto-resolution'a ayrıldı) + yeni terminal `TransactionStatus.REFUNDED` + yeni `TransactionTrigger.AdminResolveRefund` (4 disputed state → REFUNDED) + uygulama-katmanı `DisputeResolutionOutcome { SELLER_FAVOR, BUYER_FAVOR }` (kalıcı değil; resolve request input'u).
- **`AdminDisputeService`** (API composition layer, port Disputes modülünde): AD27 `GET /admin/disputes` (default ESCALATED kuyruk) + AD28 `GET /admin/disputes/:id` + AD29 `POST /admin/disputes/:id/resolve`.
  - **Seller-favor:** dispute → RESOLVED_FOR_SELLER; `HasActiveDispute` temizlenir (başka aktif dispute yoksa) → ITEM_DELIVERED'da WP1 payout devam eder; state geçişi yok.
  - **Buyer-favor:** dispute → RESOLVED_FOR_BUYER; işlem `AdminResolveRefund`→REFUNDED; alıcı ödediyse (`PaymentReceivedAt != null`) `PaymentRefundToBuyerRequestedEvent` (WP2) + item platformdaysa `ItemRefundToSellerRequestedEvent`. ITEM_DELIVERED'da item alıcıdadır → fiziksel geri-alma WP6/manuel (known-limitation).
  - Her iki sonuç: `AdminId`/`AdminNote`/`ResolvedAt` set; `IAuditLogger` `DISPUTE_RESOLVED`; yeni `DisputeResolvedEvent` → `DisputeResolvedNotificationConsumer` (buyer + seller `DISPUTE_RESULT`). Tüm yan etkiler **tek `SaveChangesAsync`** ile atomik.
  - Emergency-hold altındaki işlem reddedilir (`TRANSACTION_ON_HOLD` — önce AD19c release). Yalnız ESCALATED çözülebilir (`DISPUTE_NOT_ESCALATED`). adminNote 1..2000; outcome `Enum.IsDefined` range-guard.
- **Minör kalemler (ikisi de WP5):** `availableActions.disputableTypes: DisputeType[]` (per-type, paylaşılan `DisputeEligibility` matrisi — `canDispute` korunur) + `ACTIVE_DISPUTE_EXISTS` 07 §7.8'den kaldırıldı (03 §6 farklı-tip eşzamanlı dispute'a izin verir → tasarım-gereği erişilemez).
- **Permissions:** `VIEW_DISPUTES` (AD27/28) + `MANAGE_DISPUTES` (AD29) — backend `PermissionCatalog` + FE mirror (kod-only, migration yok).
- **REFUNDED ripple:** terminal-state listeleri (`AdminTransactionService.IsTerminalState` + bulk-hold filtresi, `AdminTransactionQueryService._terminalStates`/`_cancelledStates`, `AdminDashboardService._terminalStates`, `TransactionListService._cancelledStatuses`) REFUNDED'ı terminal/iptal-grubu olarak ele alır (CANCELLED grubu altında filtrelenir; distinct status değeri korunur).
- **FE (full-stack):** `/admin/disputes` kuyruk sayfası (status/type filtre, default ESCALATED) + `DisputeResolveModal` (AD28 detail fetch + outcome radio + note + AD29 resolve) + `DisputeQueueTable` + `DisputeStatusBadge` + 3 hook + `admin.ts` AD27-29 + AdminSidebar + `enums.ts` + i18n `adminDisputes` ×4 + `adminNav.disputes`.

## Etkilenen Modüller / Dosyalar

**Backend (yeni):** `Skinora.Shared/Enums/DisputeResolutionOutcome.cs`, `Skinora.Shared/Events/DisputeResolvedEvent.cs`, `Skinora.Shared/Domain/DisputeEligibility.cs`, `Skinora.Disputes/Application/Admin/{AdminDisputeDtos,IAdminDisputeService}.cs`, `Skinora.API/Services/AdminDisputeService.cs`, `Skinora.API/Controllers/AdminDisputesController.cs`, `Skinora.Notifications/Application/EventHandlers/DisputeResolvedNotificationConsumer.cs`, migration `20260617071029_WP5_AddDisputeResolution.cs`.
**Backend (değişen):** `DisputeStatus.cs`, `TransactionStatus.cs`, `TransactionTrigger.cs`, `TransactionStateMachine.cs`, `DisputeConfiguration.cs`, `TransactionConfiguration.cs`, `DisputeService.cs` (shared matrix + active-flag probe + docstring), `DisputeErrorCodes.cs`, `TransactionDetailService.cs`/`TransactionDetailDto.cs` (disputableTypes), `AdminTransactionService.cs`, `AdminTransactionQueryService.cs`, `AdminDashboardService.cs`, `TransactionListService.cs`, `PermissionCatalog.cs`, `Program.cs`.
**Frontend (yeni):** `app/[locale]/admin/disputes/page.tsx`, `components/admin/{DisputeQueueTable,DisputeResolveModal,DisputeStatusBadge}.tsx`, `lib/hooks/{useAdminDisputeList,useAdminDisputeDetail,useAdminDisputeResolve}.ts`.
**Frontend (değişen):** `lib/api/admin.ts`, `types/enums.ts`, `lib/admin/permissionCatalog.ts`, `components/layout/AdminSidebar.tsx`, `components/admin/index.ts`, `components/common/StatusBadge.tsx`, `i18n/messages/{en,tr,es,zh}.json`.
**Docs:** 02 §10.4, 03 §6.4, 06 §2.1/§2.10/§3.5/§3.11, 07 §7.5/§7.8/§9.11/§9.30(AD27-29)/§9.20-22(exceptional resolution), PRE_F6_PLAN, DEFERRED_BACKLOG, IMPLEMENTATION_STATUS.
**Tests:** `AdminDisputeServiceTests.cs` (yeni, 11), `TransactionStateMachineTests.cs` (+5 AdminResolveRefund), `EnumTests.cs` (DisputeStatus 5 / TransactionStatus 14 / TransactionTrigger 16 / DisputeResolutionOutcome 2 + total 29), `DisputeEntityTests.cs` (CHECK rename + RESOLVED_* boundary), `AdminRolesEndpointTests.cs` (12→14), `TransactionAcceptanceUnitTests.cs` (DTO).

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | AD27 `GET /admin/disputes` ESCALATED kuyruğu (VIEW_DISPUTES) | ✓ | `AdminDisputesController`, `AdminDisputeService.ListAsync`; test `List_DefaultsToEscalated_AndFiltersByType` |
| 2 | AD28 `GET /admin/disputes/:id` detay (VIEW_DISPUTES) | ✓ | `GetAsync`; test `Get_ReturnsDetail_WithTransactionAndParties` + `Get_UnknownDispute_ReturnsNull` |
| 3 | AD29 seller-favor → RESOLVED_FOR_SELLER + HasActiveDispute clear, payout açılır | ✓ | test `Resolve_SellerFavor_SetsResolvedForSeller_ClearsActiveDispute_NoStateChange` + `..._WithOtherActiveDispute_KeepsActiveDisputeTrue` |
| 4 | AD29 buyer-favor → REFUNDED + refund event(ler) | ✓ | test `..._AtItemDelivered_RefundsBuyer_NoItemReturn` + `..._AtPaymentReceived_RefundsBuyer_AndReturnsItem` |
| 5 | AdminId/AdminNote/ResolvedAt + DISPUTE_RESOLVED audit + DisputeResolvedEvent notify | ✓ | resolve service Stage 7-8; testlerde audit/outbox assert |
| 6 | Guard'lar: not-escalated / on-hold / validation / not-found | ✓ | testler `Resolve_NonEscalatedDispute` / `_TransactionOnHold` / `_MissingNote` / `_DisputeNotFound` |
| 7 | Per-type `disputableTypes` + `ACTIVE_DISPUTE_EXISTS` kaldırıldı | ✓ | `TransactionDetailService` + `DisputeEligibility`; 07 §7.5/§7.8 |
| 8 | Full-stack FE `/admin/disputes` (VIEW/MANAGE_DISPUTES + i18n ×4) | ✓ | `next build` `/admin/disputes` ƒ; i18n 1177×4; permission mirror |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| State machine (unit) | ✓ 258/258 | `TransactionStateMachineTests` (+5 AdminResolveRefund: 4 transition tablo + REFUNDED stamping) |
| Enum (unit) | ✓ 202/202 | DisputeStatus 5 / TransactionStatus 14 / TransactionTrigger 16 / DisputeResolutionOutcome 2 / total 29 |
| AdminDisputeService (integration) | ✓ 11/11 | seller/buyer-favor × state + 4 guard + list/detail (gerçek SQL Server) |
| Disputes (integration) | ✓ 38/38 | active-flag probe regresyonu + CHECK rename + RESOLVED_* boundary (+3) |
| TransactionDetail/List (integration) | ✓ 56/56 | disputableTypes envelope + cancelled-tab REFUNDED |
| AdminRoles (integration) | ✓ 12/12 | availablePermissions 14 + VIEW/MANAGE_DISPUTES |
| AdminTransactionService (integration) | ✓ 24/24 | REFUNDED terminal/hold-filtre regresyonu |
| Backend build | ✓ 0 error | `dotnet build Skinora.sln` |
| Migration drift | ✓ no drift | `has-pending-model-changes` → "No changes" |
| FE tsc / eslint / prettier | ✓ temiz | `tsc --noEmit` 0 + `eslint` 0 + `format:check` clean |
| FE i18n parity | ✓ 1177×4 | 4 locale identical key set |
| FE build | ✓ | `next build` — `/admin/disputes` ƒ Dynamic |
| Integration (geniş kapsam) | CI-authoritative | tam suite CI'da |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validator bekliyor (ayrı chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- **Migration:** `20260617071029_WP5_AddDisputeResolution` — iki CHECK recreate (`CK_Disputes_Closed_ResolvedAt` → `CK_Disputes_Resolved_ResolvedAt` RESOLVED_* kapsar; `CK_Transactions_Cancel` REFUNDED ekler). **Şema-only, seed yok** (string-stored enum'lar kolon değişikliği gerektirmez; SystemSettings sayısı değişmez).
- **Config/env değişikliği:** Yok.
- **Docker değişikliği:** Yok.
- **Yeni dış bağımlılık:** Yok.

## Commit & PR

- Branch: `task/WP5-admin-dispute-resolution`
- Commit: `2529338` — WP5: Admin dispute çözüm (yapısal statü + REFUNDED + AD27-29 + FE)
- PR: [#174](https://github.com/turkerurganci/Skinora/pull/174)
- CI: ⏳ izleniyor (Claude izler — [[feedback_claude_watches_ci_always]])

## Known Limitations / Follow-up

- **ITEM_DELIVERED buyer-favor item geri-alma:** alıcı item'ı aldıysa monetary iade yapılır ama fiziksel item geri-alınmaz → WP6 (Steam) / manuel ops (07 §9.30 + §9.20-22 exceptional resolution).
- **Eşzamanlı çoklu dispute:** her dispute bağımsız çözülür; ilk buyer-favor işlemi terminalize eder (REFUNDED) → sonraki seller-favor çözüm fon-etkisiz no-op olur (çift fon hareketi yok; UpdateActiveDisputeFlag diğer aktif dispute'u korur). Davranış güvenli; daha ince çoklu-dispute orkestrasyonu MVP-dışı.
- **Realtime push:** resolve sonucu in-app/email bildirimiyle gider; SignalR canlı push WP9 kapsamı.
- **disputableTypes duplicate-guard:** envelope yalnız state-bazlıdır (canDispute paritesi); aynı-tür-tekrar guard'ı `open` endpoint'inde enforce edilir.
- `ItemRefundTrigger.AdminCancel` buyer-favor item-return için reuse edildi (informational alan; sidecar log korelasyonu) — yeni `DisputeResolution` değeri eklenmedi (minimal).

## Notlar

- **Adım -1 (working tree):** temiz.
- **Adım 0 (main CI):** son 3 run `success` (WP4b #173 ×2 `27650617814`/`27650617812`, WP4a #172 `27644750670`).
- **Adım 2 (bağımlılık):** WP1 + WP2 **merged** ✓.
- **Dış varsayımlar:** Yok — saf iç backend + FE; sidecar refund/payout/sweep yolları zaten gerçek (WP1/WP2/T72/T73). Yeni dış API/plan-tier/paket varsayımı yok.
- **Anlama fazı:** 7-ajanlı keşif workflow (6 paralel discovery + completeness critic, 612k subagent token, file:line kanıtlı) — çekirdek boşluk + buyer-favor state-transition tasarım açığı + per-state refund-gate (`PaymentReceivedAt` vs status-based `PaymentWasReceived`) bağımsız tespit; bulgular owner kararlarına foldedildi.
- **Mimari:** resolve servisi API composition layer'da (port Disputes modülünde) — `AdminTransactionQueryService` emsali; cross-module (Disputes + Transactions state-machine/events + Platform audit) tek-yön cycle'sız.
- **Cross-module matris:** `DisputeEligibility` Shared'a kondu (Transactions, Disputes'e referans veremez → dependency yönü) — `DisputeService` open-guard + `TransactionDetailService` envelope tek kaynak.
