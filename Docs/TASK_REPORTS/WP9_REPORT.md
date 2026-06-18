# WP9 — Realtime tamlık (Steam push + admin abonelik + hub bypass)

**Faz:** F6 öncesi (PRE_F6_PLAN) | **Durum:** ✓ Tamamlandı (bağımsız validator PASS 2026-06-18) | **Tarih:** 2026-06-18

---

## Yapılan İşler

WP9, realtime (SignalR) katmanının altı boşluğunu kapatır (kaynak: [`PRE_F6_PLAN.md`](../PRE_F6_PLAN.md) WP9 + [`DEFERRED_BACKLOG.md`](../DEFERRED_BACKLOG.md)). Owner kararları (AskUserQuestion 2026-06-18): Steam push = **tek generic event**; admin event'leri = **tam paket** (güvenlik + FE + doc); admin kapısı = **role claim**; FE = **C09 toast + admin tx-detay realtime + drift WP13'e**.

1. **Steam geçiş push'ları (T61-SteamTransitionRealtimePush + WP1 G2).** Yeni generic `TransactionStatusChangedEvent(txId, from, to, occurredAt)` — Steam pipeline'ın push'suz dört geçişinde producer'lar outbox'a yayınlar (aynı `SaveChanges`'te, atomik): `SendTradeOfferToSeller` (ACCEPTED→TRADE_OFFER_SENT_TO_SELLER) + `SendTradeOfferToBuyer` (PAYMENT_RECEIVED→TRADE_OFFER_SENT_TO_BUYER) `TradeOfferDispatchJob`'ta; `EscrowItem` (→ITEM_ESCROWED) + `DeliverItem` (→ITEM_DELIVERED) `SteamWebhookHandler`'da. Tek `TransactionStatusChangedRealtimeConsumer` RT1 push'una çevirir. `Complete`→COMPLETED için WP1'in mevcut `PayoutCompletedEvent`'i yeniden kullanılır → yeni `PayoutCompletedRealtimeConsumer` (ITEM_DELIVERED→COMPLETED push'lar). Mevcut event'i olan geçişler (BuyerAccepted/PaymentReceived/cancel/timeout/dispute/flag/hold) kendi consumer'larını korur → **çift-push yok**.

2. **Admin-only group scope (T69-K4) — güvenlik düzeltmesi.** Üç admin event'i (`AdminBotStatusChanged`/`AdminReconciliationMismatch`/`AdminHotWalletThresholdBreached`) şimdiye dek `Clients.All` ile **tüm bağlı kullanıcılara** (non-admin dahil) gidiyordu — bot SteamId'leri, cüzdan bakiyeleri, reconciliation delta'ları sızıyordu. `NotificationsHub.OnConnectedAsync` admin (`role ∈ {admin, super_admin}`) bağlantılarını `admins` grubuna katar; publisher üç event'i `Clients.Group(admins)`'e yollar. Non-admin istemciler artık bu payload'ları **almaz**.

3. **TransactionsHub admin-join bypass (T61-K3).** `JoinTransaction` artık admin'in (role claim) buyer/seller olmasa da herhangi bir işlem odasına katılmasına izin verir; admin transaction-detay yüzeyi (S16) canlı güncelleme alabilir.

4. **group-failure observability.** `SignalRNotificationRealtimePublisher` beş ayrı `try/catch` bloğundan tek `SendToGroupAsync`/`SendToAllAsync` yardımcısına indirgendi — her grup/broadcast push hatası tek-tip yapısal `Warning` ile loglanır (`group {Group} method {Method}`), Loki'de sorgulanabilir. (RT1 `SignalRTransactionRealtimePublisher` zaten tek-tip logluyordu.) Backend metrik altyapısı yok (Prometheus yalnız sidecar'larda) → observability = yapısal log, yeni bağımlılık yok.

5. **FE — C09 toast.** `RealtimeProvider` `NewNotification` push'unda mevcut `ToastNotification` (`useToast`) ile toast gösterir. `ToastProvider` admin layout'tan global `Providers`'a taşındı (RealtimeProvider'ı sarar; tek toast stack tüm sayfalara hizmet eder).

6. **FE — admin event aboneliği + admin tx-detay realtime.** `RealtimeProvider` üç admin event'ine abone (backend yalnız admin'e yolladığından non-admin'de inert): `AdminBotStatusChanged`→`["admin","steam-accounts"]`+`["admin","dashboard"]` invalidate; reconciliation/hot-wallet→`["admin","audit-logs"]`+`["admin","dashboard"]` (kalıcı yüzeyleri audit log). Admin tx-detay sayfası `useTransactionRealtime` ile hub'a abone (hub bypass'in tüketicisi) → state-değiştiren push'ta `refetch`.

**Doc:** 07 §11.2 RT2 spec tablosuna üç admin event'i + admin-group scope notu eklendi (owner "tam paket" kararı).

## Etkilenen Modüller / Dosyalar

**Backend (yeni):**
- `Skinora.Shared/Events/TransactionStatusChangedEvent.cs` — generic geçiş event'i.
- `Skinora.Realtime/Application/EventHandlers/TransactionStatusChangedRealtimeConsumer.cs` — generic event → RT1 push.
- `Skinora.Realtime/Application/EventHandlers/PayoutCompletedRealtimeConsumer.cs` — PayoutCompleted → ITEM_DELIVERED→COMPLETED push.
- `Skinora.Realtime/Hubs/HubClaims.cs` — `IsAdmin(ClaimsPrincipal?)` role gate (group + bypass ortak).

**Backend (değişen):**
- `Skinora.Realtime/Hubs/NotificationsHub.cs` — `AdminGroup` sabiti + admin auto-join.
- `Skinora.Realtime/Hubs/TransactionsHub.cs` — admin-join bypass.
- `Skinora.Realtime/Infrastructure/SignalRNotificationRealtimePublisher.cs` — admin event'leri → admin group; `SendToGroupAsync`/`SendToAllAsync` konsolidasyon + observability.
- `Skinora.Realtime/Application/Contracts/NotificationRealtimePayloads.cs` — admin payload doc-comment güncel (stale "no per-role group yet" kaldırıldı).
- `Skinora.Steam/Application/Dispatch/TradeOfferDispatchJob.cs` — escrow+delivery başarı dalında `TransactionStatusChangedEvent` publish.
- `Skinora.Steam/Application/Webhooks/SteamWebhookHandler.cs` — EscrowItem+DeliverItem'da publish.

**Frontend:**
- `lib/signalr/events.ts` — 3 admin payload tipi + event-name sabitleri.
- `lib/signalr/NotificationsHubClient.ts` — 3 admin handler + listener.
- `lib/signalr/RealtimeProvider.tsx` — C09 toast + 3 admin handler (cache invalidation).
- `lib/providers.tsx` — `ToastProvider` global hoist.
- `app/[locale]/admin/layout.tsx` — `ToastProvider` kaldırıldı (artık global).
- `app/[locale]/admin/transactions/[id]/page.tsx` — `useTransactionRealtime` aboneliği.

**Test (yeni/değişen):** `Skinora.Realtime.Tests` (`RealtimeConsumerTests` +6, yeni `HubClaimsTests`, yeni `NotificationRealtimePublisherTests`, csproj: AspNetCore FrameworkReference + Auth ref) · `TransactionsHubEndpointTests` (admin-bypass) · `TradeOfferDispatchJobTests` + `SteamWebhookHandlerTests` (producer event assertion).

**Doc:** `Docs/07_API_DESIGN.md` §11.2.

## Kabul Kriterleri Kontrolü

| # | Kriter (WP9 backlog kalemi) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Steam geçiş push'ları (RealtimeConsumer) | ✓ | `TransactionStatusChangedEvent` + consumer; 4 producer site; `RealtimeConsumerTests.TransactionStatusChanged_RelaysFromAndToVerbatim` (4 geçiş) + dispatch/webhook producer assertion'ları. |
| 2 | COMPLETED push (WP1 G2) | ✓ | `PayoutCompletedRealtimeConsumer`; `PayoutCompleted_PushesStatusChanged_ItemDelivered_To_Completed`. |
| 3 | FE 3 admin event aboneliği | ✓ | `RealtimeProvider` + `NotificationsHubClient` 3 handler; cache invalidation steam-accounts/audit-logs/dashboard. |
| 4 | Admin-only group scope (Clients.All sızıntısı kapatıldı) | ✓ | `NotificationsHub.AdminGroup` join + publisher `Clients.Group(admins)`; `NotificationRealtimePublisherTests` (admin→group, user→group, maintenance→all). |
| 5 | Hub admin-join bypass | ✓ | `TransactionsHub` `HubClaims.IsAdmin`; `TransactionsHubEndpointTests.JoinTransaction_AsAdmin_NonParticipant_Succeeds` (CI) + `HubClaimsTests` 4 case. |
| 6 | C09 toast realtime | ✓ | `RealtimeProvider.onNewNotification` → `useToast().push`; `ToastProvider` global. |
| 7 | group-failure observability | ✓ | RT2 `SendToGroupAsync`/`SendToAllAsync` tek-tip `Warning` log; RT1 zaten mevcut. |
| 8 | admin tx-detay realtime (hub bypass tüketicisi) | ✓ | `admin/transactions/[id]/page.tsx` `useTransactionRealtime` → refetch. |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit — Realtime | ✓ 41/41 | `dotnet test Skinora.Realtime.Tests` (yeni: 4 status-relay + 1 payout + 4 HubClaims + 5 publisher-routing). |
| Unit — Shared | ✓ 369/369 | `FullyQualifiedName!~Integration` (16 Integration fail = lokal Docker yok). |
| Build | ✓ 0W/0E | `dotnet build Skinora.sln` Debug **ve** Release. |
| Format | ✓ exit 0 | `dotnet format --verify-no-changes`. |
| FE tsc | ✓ 0 | `npx tsc --noEmit`. |
| FE eslint | ✓ 0 | `npx eslint`. |
| FE prettier | ✓ | WP9-dokunulan dosyalar temiz (33 pre-existing drift = WP18). |
| FE build | ✓ | `next build` (admin/transactions/[id] ƒ). |
| Integration | CI-authoritative | dispatch/webhook producer assertion + `TransactionsHubEndpointTests` admin-bypass + migration dry-run = Testcontainers MsSql (lokal Docker yok). |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS (bağımsız validator, ayrı chat, 2026-06-18) |
| Bulgu sayısı | 0 bloke-edici |
| Düzeltme gerekli mi | Hayır |

**Bağımsız validator (yapım raporu görülmeden kendi verdict'i oluşturuldu):**
- **Kapılar:** Adım -1 working tree temiz ✓ · Adım 0 main son-3 CI success (`27719423184`/`27719423181`/`27717764161`) ✓ · Adım 0b repo memory WP9 satırı mevcut ✓ · Adım 8a task CI HEAD `38e7f30` run [`27744825726`](https://github.com/turkerurganci/Skinora/actions/runs/27744825726) **tüm job success** (+ `ed186c8` run `27744298511` success).
- **Validator lokal koşumu:** Realtime unit **41/41** + Steam **103/103** (değişen producer integration testleri dahil) + Shared unit **364/364** (`~Unit` filtre; regresyon temiz) + Release build **0W/0E** + FE tsc 0 / eslint 0 (WP9 yolları) / prettier temiz (WP9 dosyaları) / `next build` ✓.
- **Bağımsız teyit — 6 kalem:** (1) generic `TransactionStatusChangedEvent` 4 Steam geçişinde producer'larca `fromStatus` Fire() öncesi yakalanıp atomik olarak (SaveChanges öncesi) outbox'a yazılır; consumer saf relay; **çift-push yok** (4 geçişin hiçbirinin önceden realtime consumer'ı yoktu; COMPLETED yalnız yeni `PayoutCompletedRealtimeConsumer`'da, WP1 `PayoutCompletedEvent` tek tüketici). Producer integration testleri başarısız dalda status event'i **yazmadığını** da kanıtlar. (2) Admin-group scope güvenlik düzeltmesi gerçek: `NotificationsHub.OnConnectedAsync` admin'i `admins` grubuna katar, publisher 3 admin event'ini `Clients.Group(admins)`'e yollar (önceden `Clients.All` → bot/cüzdan/reconciliation sızıntısı). (3) `HubClaims.IsAdmin` JWT `role` claim'ini **ham** okur — `AuthModule.cs:68 MapInboundClaims=false` ile claim remap yok; `AuthRoles.Admin`/`SuperAdmin` aynı değerleri admin REST policy (`AuthModule.cs:114 RequireClaim`) ve `AdminAuthorityResolver` üretir → hub kapısı REST yetkisiyle **birebir tutarlı**. (4) TransactionsHub admin-bypass `JoinTransaction_AsAdmin_NonParticipant_Succeeds` ile gerçek SignalR bağlantısı + admin JWT üzerinden uçtan uca test edilir. (5) C09 toast `ToastProvider` global hoist'i [`[locale]/layout.tsx:32`](../../frontend/src/app/[locale]/layout.tsx#L32) `<Providers>` ile admin'in atası → regresyon yok. (6) group-failure observability tek-tip `LogWarning(group, method)`.
- **Mini güvenlik:** Secret sızıntısı yok · Auth **iyileşti** (T69-K4 sızıntı kapatıldı, gate REST ile tutarlı) · input typed-enum (free-text yok) · yeni runtime bağımlılık yok (yalnız test-only FrameworkReference + Auth ProjectReference).
- **Doc/contract uyumu:** 07 §11.2 admin-group tablosu (3 event + scope notu) FE `events.ts` payload'larıyla ve backend record'larıyla birebir; `NotificationRealtimePayloads.cs` stale "no per-role group yet" yorumu güncellendi. Migration yok (doğrulandı — diff'te migration dosyası yok).
- **Yapım raporu karşılaştırması:** Tam uyumlu, 0 uyuşmazlık. (Tek kozmetik: rapor Shared "369/369" `!~Integration` filtresini, validator "364/364" `~Unit` filtresini kullandı — ikisi de yeşil, kapsam farkı; substansif fark değil.)

## Altyapı Değişiklikleri

- **Migration:** Yok — yeni entity/EF config/DbContext değişikliği yok. `TransactionStatusChangedEvent` yalnızca outbox mesajı (tablo mevcut). `has-pending-model-changes` etkilenmez. WP9 PRE_F6 migration-taşıyan paket listesinde değil.
- **Config/env:** Yok.
- **Docker:** Yok.
- **Yeni bağımlılık:** Yok (SignalR T61'den beri var). Test-only: `Skinora.Realtime.Tests` csproj'una `Microsoft.AspNetCore.App` FrameworkReference + `Skinora.Auth` ProjectReference (publisher routing fake + HubClaims test sabitleri için).

## Commit & PR

- Branch: `task/WP9-realtime-completeness`
- Commit: `8d62c56` (kod) + `ed186c8` (rapor/PR refs)
- PR: [#179](https://github.com/turkerurganci/Skinora/pull/179)
- CI: ✓ PASS — HEAD `ed186c8` run [`27744298511`](https://github.com/turkerurganci/Skinora/actions/runs/27744298511) tüm job success (Detect/Lint/Build/Unit/Integration/Contract/Migration dry-run/Docker×2/Gate). Integration + migration dry-run gerçek SQL Server'da geçti.

## Known Limitations / Follow-up

- **Non-realtime drift → WP13 (owner kararı).** `signalr-toast-countdown` kaleminin realtime-olmayan alt-kalemleri (verification countdown, email cooldown Retry-After gösterimi, LanguageSelector drift) WP9 kapsamı dışı; WP13 (FE tamlık) alır.
- **Redis backplane ölçek-dışı.** Tek-instance MVP (PRE_F6_PLAN §3); multi-instance group-failure/backplane post-MVP.
- **Admin reconciliation/hot-wallet için ayrı FE sorgu yüzeyi yok.** Bu iki event audit-log + dashboard cache'ine invalidate eder (kalıcı yüzeyleri AuditLog satırı); ayrılmış reconciliation/hot-wallet ekranı MVP'de yok.
- **Prettier repo-geneli drift (33 dosya)** WP9 dışı, WP18.

## Notlar

- **Startup kapıları:** Adım -1 working tree temiz ✓ · Adım 0 main son-3 CI run success (`27719423181`/`27719423184`/`27717764161`) ✓ · WP1–WP8 hepsi merged ✓.
- **Dış Varsayımlar:** Yok. SignalR dahili; `Microsoft.AspNetCore.SignalR` T61'den beri mevcut; Redis backplane PRE_F6 §3 ile kapsam-dışı.
- **Owner kararları (AskUserQuestion 2026-06-18):** (1) Steam push = tek generic `TransactionStatusChangedEvent` + 1 consumer (Complete için WP1 `PayoutCompletedEvent`). (2) Admin event'leri = tam paket (admin-group güvenlik + FE abonelik + 07 §11.2 doc). (3) Admin kapısı = `role ∈ {admin, super_admin}`. (4) FE = C09 toast + admin tx-detay realtime + non-realtime drift WP13'e.
- **Güvenlik:** Net iyileşme — `Clients.All` admin-veri sızıntısı admin-group'a daraltıldı; hub admin-join role-gated; yeni secret/dep yok; FE admin event'leri salt cache invalidation (PII render yok).
