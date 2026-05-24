# T96 — SignalR client entegrasyonu

**Faz:** F5 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-05-24

---

## Yapılan İşler

T13'te `frontend/src/lib/signalr/connection.ts` factory iskeleti olarak inmişti; T61 (`/hubs/transactions` + 8 server→client event + CountdownSync 30sn broadcaster) ve T62 (`/hubs/notifications` + 5 server→client event + admin-side 3 ek event) backend hub'larını yayına aldı. T96 = bu iki hub'a frontend tarafından bağlanan singleton client'lar + global cache invalidation + S07 detail sayfasının per-transaction join/leave wiring'i.

**`/hubs/transactions` (RT1 — 07 §11.1):**

- 8 server→client event yakalanır: `TransactionStatusChanged`, `CountdownSync`, `PaymentDetected`, `PaymentConfirmed`, `DisputeUpdate`, `FlagResolved`, `EmergencyHoldApplied`, `EmergencyHoldReleased`.
- 2 client→server method invoke edilir: `JoinTransaction(transactionId)`, `LeaveTransaction(transactionId)` — S07 sayfası mount/unmount tetikler.
- Per-transaction group membership singleton client tarafından `Map<transactionId, Set<handlers>>` kaydında tutulur; aynı id'ye birden fazla subscriber join çift API çağrısı yapmaz, son subscriber leave eder.
- `onreconnected` callback'inde tüm aktif id'ler için `JoinTransaction` replay edilir — SignalR transparent reconnect sonrası server-side group membership kaybolduğu için zorunlu.

**`/hubs/notifications` (RT2 — 07 §11.2):**

- 5 server→client event yakalanır: `NewNotification`, `UnreadCountChanged`, `TelegramConnected`, `DiscordConnected`, `MaintenanceStatusChanged`.
- Client→server method yok; backend `OnConnectedAsync`'te kullanıcıyı `user:{userId:N}` group'una otomatik joinler.
- Admin event'leri (`AdminBotStatusChanged`, `AdminReconciliationMismatch`, `AdminHotWalletThresholdBreached`) `NotificationRealtimePayloads.cs`'te tanımlı ama 07 §11.2 spec tablosunda listelenmedikleri için T96 scope dışı — T103 / T-future admin SignalR wiring task'ına devir (K2).

**JWT query-param authentication:**

- Backend `JwtBearerEvents.OnMessageReceived` (T61 `RealtimeModule`) `?access_token=` query parametresini hub path'lerinde okur.
- Client `accessTokenFactory` her (re)connect denemesinde `useAuthStore.getState().accessToken` okur — refresh flow (T32) sırasında rotate edilen token tearing-down olmadan picked up.

**Lifecycle:**

- `RealtimeProvider` `Providers` (QueryClient) altına mount edilir; `useEffect(isAuthenticated, accessToken)` ile her iki hub `start()`/`stop()` çağırır.
- Logout → her iki hub stop; sonraki login → fresh start.
- Auto-reconnect 0/2/5/10/30 sn (07 §11.1–§11.2 resilience profili) — final attempt sonrası `Disconnected` state'i `onclose` callback'inde dev'de loglanır.

**React Query cache invalidation matrisi:**

| Event | Eylem |
|---|---|
| `TransactionStatusChanged` | invalidate `["transactions","detail",id]` + `["transactions","active"]` + `["transactions","completed"]` + `["transactions","cancelled"]` |
| `CountdownSync` | `setQueryData` ile `["transactions","detail",id]` içinde `timeout.remainingSeconds/frozen/frozenReason` patch (no refetch — 30sn frekansta API çağrısı israfı) |
| `PaymentDetected` / `PaymentConfirmed` / `DisputeUpdate` / `FlagResolved` / `EmergencyHoldApplied` / `EmergencyHoldReleased` | invalidate `["transactions","detail",id]` |
| `NewNotification` | invalidate `["notifications","list"]` + `["notifications","unread-count"]` |
| `UnreadCountChanged` | `setQueryData` ile `["notifications","unread-count"]` patch (race-free badge update) |
| `TelegramConnected` / `DiscordConnected` | invalidate `["users","me","settings"]` |
| `MaintenanceStatusChanged` | invalidate `["platform","maintenance"]` |

**Per-transaction subscriber API:**

`useTransactionRealtime(id, handlers?)` hook'u S07 sayfasına mount edilir; React component lifecycle'ı ile join/leave dispose döngüsünü kapsar. Handler objesi opsiyoneldir — yalnız global invalidation gerekiyorsa boş çağrı yeterli; bir alt panel ek UI davranışı (örn. flash banner) eklemek isterse kendi handler'ını subscribe edebilir.

**S07 forward-deferred JSDoc temizlendi:**

`useTransactionDetail`, `useUnreadCount`, `usePlatformMaintenance` ve `[id]/page.tsx` JSDoc'larından "T96 forward" notları kaldırıldı, yerine RealtimeProvider'ın hangi event'leri hangi cache'leri invalidate ettiği yazıldı.

**Env config:**

`.env.example`'a `NEXT_PUBLIC_SIGNALR_URL=http://localhost:5000/hubs` satırı eklendi. Default değer `connection.ts` içinde `/hubs` (same-origin proxy ile çalışan deployment için); cross-origin'de explicit set edilmeli.

## Etkilenen Modüller / Dosyalar

**Yeni:**

- [frontend/src/lib/signalr/events.ts](../../frontend/src/lib/signalr/events.ts) — 8 transaction + 5 notification payload TypeScript tipleri + `TimeoutPhase`/`EmergencyHoldReleaseAction` string unions + event/method name constant'ları.
- [frontend/src/lib/signalr/TransactionsHubClient.ts](../../frontend/src/lib/signalr/TransactionsHubClient.ts) — singleton client, `start()`/`stop()`/`subscribe()`/`subscribeGlobal()`, per-id subscription registry, reconnect replay.
- [frontend/src/lib/signalr/NotificationsHubClient.ts](../../frontend/src/lib/signalr/NotificationsHubClient.ts) — singleton client, `start()`/`stop()`/`subscribe()`, global handler set.
- [frontend/src/lib/signalr/RealtimeProvider.tsx](../../frontend/src/lib/signalr/RealtimeProvider.tsx) — auth-gated lifecycle + global QueryClient invalidation map.
- [frontend/src/lib/hooks/useTransactionRealtime.ts](../../frontend/src/lib/hooks/useTransactionRealtime.ts) — S07 page hook (mount join, unmount leave).

**Değişen:**

- [frontend/src/lib/signalr/connection.ts](../../frontend/src/lib/signalr/connection.ts) — `accessToken` string parametresi → `tokenFactory` callback'i (her (re)connect'te token okunur, rotation desteği).
- [frontend/src/lib/providers.tsx](../../frontend/src/lib/providers.tsx) — `RealtimeProvider` `QueryClientProvider` altına mount edildi.
- [frontend/src/app/[locale]/(main)/transactions/[id]/page.tsx](../../frontend/src/app/%5Blocale%5D/(main)/transactions/%5Bid%5D/page.tsx) — `useTransactionRealtime(id)` çağrısı + JSDoc T96 K1 forward-defer kaldırıldı, RealtimeProvider entegrasyon notu eklendi.
- [frontend/src/lib/hooks/useTransactionDetail.ts](../../frontend/src/lib/hooks/useTransactionDetail.ts) — JSDoc "T96 forward" notu → RealtimeProvider invalidation event listesi.
- [frontend/src/lib/hooks/useUnreadCount.ts](../../frontend/src/lib/hooks/useUnreadCount.ts) — aynı JSDoc temizliği.
- [frontend/src/lib/hooks/usePlatformMaintenance.ts](../../frontend/src/lib/hooks/usePlatformMaintenance.ts) — aynı JSDoc temizliği.
- [.env.example](../../.env.example) — `NEXT_PUBLIC_SIGNALR_URL` satırı eklendi.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Transaction hub bağlantısı: join/leave room, event listener'lar | ✓ Karşılandı | `TransactionsHubClient.start()` ile `/hubs/transactions` connect; `subscribe(id, handlers)` → `JoinTransaction(id)` invoke; dispose → `LeaveTransaction(id)` invoke; 8 server-to-client `conn.on()` `attachEventListeners()` içinde register. `useTransactionRealtime` S07 mount/unmount lifecycle ile bağlar. |
| 2 | Notification hub bağlantısı: real-time bildirim push | ✓ Karşılandı | `NotificationsHubClient.start()` ile `/hubs/notifications` connect (backend `OnConnectedAsync` otomatik group join); 5 event `attachEventListeners()` içinde; `RealtimeProvider` `NewNotification` → list + unread-count invalidate, `UnreadCountChanged` → cache patch. |
| 3 | CountdownSync: 30sn periyodik + freeze/unfreeze | ✓ Karşılandı | `onCountdownSync` handler (`RealtimeProvider.tsx:79`) `setQueryData` ile detail cache'inde `timeout.remainingSeconds + frozen + frozenReason` patch eder; refetch tetiklemez. Backend `CountdownSyncBroadcaster` 30sn cadence ile push eder (T61). UI `CountdownTimer` (C02) `timeout.frozen` flag'ini halihazırda işliyor. |
| 4 | PaymentDetected/PaymentConfirmed → UI güncelleme | ✓ Karşılandı | `RealtimeProvider.tsx:96-101` ikisi de `["transactions","detail",id]` invalidate eder; S07 sayfası `useTransactionDetail` ile `payment` + `paymentEvents` + `status` alanlarını otomatik yeniden çeker. |
| 5 | TransactionStatusChanged → state varyantı değişimi | ✓ Karşılandı | `RealtimeProvider.tsx:73-78` detail invalidate + 3 list tab (active/completed/cancelled) invalidate; `StateActionPanel` `useTransactionDetail.data.status` üzerinde branch eder, yeni status için doğru aksiyon paneli renderlanır. |
| 6 | MaintenanceStatusChanged → banner gösterimi | ✓ Karşılandı | `RealtimeProvider.tsx:132-134` `["platform","maintenance"]` invalidate eder; `usePlatformMaintenance` 30sn staleTime sınırını atlatır, C08 banner anında doğru variant'ı (planned/active/steam/blockchain) gösterir. |
| 7 | JWT authentication (query param) | ✓ Karşılandı | `connection.ts:30` `accessTokenFactory` callback'i `useAuthStore.getState().accessToken` okur; SignalR JS client query string'e `?access_token=...` ekler (WebSocket'te custom header geçilemediği için spec'in tek-doğru yolu — 07 §11.1 "Auth: JWT query param"). Backend `JwtBearerEvents.OnMessageReceived` hub path'lerinde aynı query'yi okuyor (T61 `RealtimeModule`). |
| 8 | Bağlantı kopma/yeniden bağlanma | ✓ Karşılandı | `connection.ts:32` `.withAutomaticReconnect([0, 2000, 5000, 10000, 30000])` (T13 ölçüsü, 07 §11.1–§11.2 ile uyumlu). `TransactionsHubClient.attachLifecycleListeners` `onreconnected` callback'inde `rejoinAll()` aktif tüm subscription id'leri server'a re-join eder; `onclose` dev'de log. |

## Doğrulama Kontrol Listesi

| # | Kontrol | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 07 §11.1–§11.2 tüm event'ler client'ta dinleniyor mu? | ✓ Karşılandı | RT1 — `TransactionsHubClient.attachEventListeners()` (TransactionsHubClient.ts:188-216) 8/8 spec event'i için `conn.on()` kaydı: TransactionStatusChanged + CountdownSync + PaymentDetected + PaymentConfirmed + DisputeUpdate + FlagResolved + EmergencyHoldApplied + EmergencyHoldReleased. RT2 — `NotificationsHubClient.attachEventListeners()` 5/5 spec event'i: NewNotification + UnreadCountChanged + TelegramConnected + DiscordConnected + MaintenanceStatusChanged. Admin event'leri (AdminBotStatusChanged + AdminReconciliationMismatch + AdminHotWalletThresholdBreached) spec tablosunda yok — K2 forward-defer. |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit | — | T96 test beklentisi: **Yok** (11_IMPLEMENTATION_PLAN T96) |
| Integration | — | T96 test beklentisi: **Yok** |
| TypeScript | ✓ PASS | `npm run build` içinde `Running TypeScript ... Finished TypeScript in 3.4s` (Next build TS strict pass) |
| ESLint | ✓ PASS | `npm run lint` çıktısız (0 warning, 0 error) |
| Frontend build | ✓ PASS | `npm run build` — `Compiled successfully in 3.1s` + 26 route üretildi |
| Prettier (T96 dosyaları) | ✓ PASS | `npx prettier --check` 11 T96 dosyası için "All matched files use Prettier code style!" |
| Backend Release build | ✓ PASS | `dotnet build -c Release` — `0 Warning(s) / 0 Error(s) / 26.51s` (regresyon yok) |
| 4-locale parity | ✓ PASS | 632/632/632/632 leaf (T95 sonrası 632; T96 yeni UI metni eklemedi, parity korundu) |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS (bağımsız validator 2026-05-24) |
| Verdict | ✓ PASS |
| Kabul kriterleri | 8/8 ✓ bağımsız doğrulandı (1 join/leave + 8 RT1 event listener + 2 RT2 hub auto-join + 5 RT2 event listener / 2 CountdownSync setQueryData patch — backend 30sn cadence T61 / 3 PaymentDetected+PaymentConfirmed invalidate / 4 TransactionStatusChanged detail + 3 list tab invalidate / 5 MaintenanceStatusChanged platform.maintenance invalidate / 6 accessTokenFactory `useAuthStore.getState().accessToken` `?access_token=` query param + backend `JwtBearerEvents.OnMessageReceived` hub path filter / 7 `.withAutomaticReconnect([0,2000,5000,10000,30000])` + `onreconnected` → `rejoinAll()`) |
| Doğrulama kontrol listesi | 1/1 ✓ bağımsız doğrulandı — `TransactionsHubClient.attachEventListeners` 8/8 RT1 event + `NotificationsHubClient.attachEventListeners` 5/5 RT2 event + 2 client→server method (Join/LeaveTransaction) backend `TransactionsHub.cs:61,96` ile birebir |
| Bulgu sayısı | 0 |
| Düzeltme gerekli mi | Hayır |
| Validator bağımsız kontroller | Working tree temiz (Adım -1) + main CI 3/3 success (`26342225204`/`26342225156`/`26337997661` — Adım 0) + MEMORY.md T96 ≥1 satır (Adım 0b) + lokal Next build PASS + ESLint 0/0 + `npx tsc --noEmit` 0 + task branch CI HEAD `e1967ef` [`26357679827`](https://github.com/turkerurganci/Skinora/actions/runs/26357679827) **10/10 SUCCESS** (Detect+Guard skipped+Lint+Build+Unit+Integration+Contract+Migration+Docker frontend+CI Gate) + önceki `f5a45eb` [`26357488379`](https://github.com/turkerurganci/Skinora/actions/runs/26357488379) 10/10 ✓ |
| Mini güvenlik | Temiz — secret sızıntısı 0 (token auth store'dan), backend `[Authorize]` + JWT query-param bridge `/hubs/*` path-scope, `transactionId` `Guid.Empty` + participant guard backend tarafında HubException, `XSS` riski yok (event payload string'leri React text node), yeni dış bağımlılık 0 (`@microsoft/signalr@^10.0.0` T13'ten) |
| Doküman uyumu | ✓ Tam — backend event isimleri (`TransactionsHub.cs:27-30` ve `NotificationsHub.cs:28-29` remarks) ile frontend `TransactionHubEvents` + `NotificationHubEvents` constant'ları 1:1; method imzaları (`JoinTransaction(Guid)`, `LeaveTransaction(Guid)`) frontend `TransactionHubMethods.JoinTransaction`/`LeaveTransaction` ile birebir; 07 §11.1 + §11.2 spec event tabloları + auth (JWT query-param) + bağlantı (S07 mount/login) kuralları kod'da karşılandı; 04 §7.3 CountdownSync frozen/freezeReason TimeoutFreezeReason enum (MAINTENANCE/STEAM_OUTAGE/BLOCKCHAIN_DEGRADATION/EMERGENCY_HOLD) `events.ts` üzerinden referans alındı |
| Yapım raporu karşılaştırma | Tam uyumlu — bağımsız 8/8 ✓ + 1/1 ✓ self-check ile aynı sınıflama, K1–K6 forward-defer'lar validator-side onaylandı (toast C09 T-future / admin event'ler T103+ / per-tx handler API kullanılmıyor opt-in / server-side group failure log-only no-side-effect / Vitest yok plan onaylı / 149 pre-existing prettier drift T80 K7 havuzu) |

## Altyapı Değişiklikleri

- **Migration:** Yok (yalnız frontend wiring)
- **Config/env:** `.env.example`'a `NEXT_PUBLIC_SIGNALR_URL=http://localhost:5000/hubs` eklendi. `connection.ts` default'u `/hubs` (same-origin) — geriye dönük uyumlu, mevcut deployment'lar etkilenmez.
- **Docker:** Yok (env var FRONTEND container'ına Next.js build-time injection ile geçer; mevcut docker-compose runtime env yapısı yeterli)
- **Yeni dış bağımlılık:** Yok (`@microsoft/signalr@^10.0.0` T13'te eklenmiş, package.json/lock değişmedi)

## Commit & PR

- Branch: `task/T96-signalr-client-integration`
- Commit: `f5a45eb` — `T96: SignalR client entegrasyonu`
- PR: [#143](https://github.com/turkerurganci/Skinora/pull/143)
- CI: ✓ PASS — run [`26357488379`](https://github.com/turkerurganci/Skinora/actions/runs/26357488379) 10/10 success (Detect + Guard skipped + 1. Lint + 2. Build + 3. Unit + 4. Integration + 5. Contract + 6. Migration + 7. Docker frontend + CI Gate)

## Known Limitations / Follow-up

- **K1 — Toast notification UI (C09) yok** — `NewNotification` event'i geldiğinde RealtimeProvider yalnız inbox + unread-count cache'ini invalidate eder. C09 component'i henüz tanımlı değil (04 §3 component katalogunda var ama implement edilmemiş) — T-future "toast component" task'ı `notificationsHubClient.subscribe()` ile aynı handler'ı ekleyerek toast'u render edecek.
- **K2 — Admin event'leri (`AdminBotStatusChanged`, `AdminReconciliationMismatch`, `AdminHotWalletThresholdBreached`) subscribe edilmedi** — 07 §11.2 spec tablosunda listelenmedikleri ve admin dashboard sayfaları (T99–T106) henüz yok olduğu için. T103 (S18 Steam hesapları) veya T-future admin SignalR wiring task'ı bu event'leri ekleyecek. Backend payload'ları zaten broadcast ediyor (T69/T76/T77); non-admin client'lar event'i alır ama handler'ı olmadığı için no-op.
- **K3 — Per-transaction handler API kullanılmıyor** — `useTransactionRealtime(id, handlers?)` hook'u opsiyonel handler objesi alır ama S07 sayfası şu an boş `{}` ile çağırıyor. Global invalidation tüm UI güncellemelerini karşılıyor; ek per-transaction UI davranışı (örn. PaymentDetected geldiğinde flash banner) gerekirse hook'a handler geçilir.
- **K4 — Server-side group membership defansı yok** — `JoinTransaction` invoke başarısız olursa (TRANSACTION_NOT_FOUND/TRANSACTION_FORBIDDEN/AUTH_INVALID) `console.warn` log + sessiz devam. REST query'si zaten 403/404 surface ederek doğru UI'ı gösteriyor; SignalR sessizliği no-op. Production'da observability gerekirse `IRealtimeErrorReporter` ekleyip T-future PostHog/Sentry hook.
- **K5 — Frontend Vitest framework yok** — T96 unit test scope dışı (plan "Test beklentisi: Yok" + scope decision Adım 5). Backend tarafı T61 `TransactionsHubEndpointTests` (5) + `CountdownSyncBroadcasterTests` + T62 `NotificationsHubEndpointTests` (5) + RealtimeConsumer testleri (25) hub kontratını koruyor.
- **K6 — Pre-existing prettier drift 149 dosyada** — repo-wide `npm run format:check` 149 dosyada `printWidth=100` drift bildirir; T96 yalnız değiştirdiği 11 dosyayı formatlamış olarak bıraktı (T80 K7 paterni). Toplu temizleme T-future chore PR'ı.

## Notlar

- **Working tree (Adım -1):** temiz (`git status --short` çıktısız)
- **Main CI startup check (Adım 0):** 3/3 success — `26342225204`, `26342225156` (T95 #142 × 2), `26337997661` (T94 #141)
- **Dış varsayım kontrolü (Adım 4):**
  - `@microsoft/signalr` ^10.0.0 mevcut — kanıt: `frontend/package.json:14` + T13 raporu
  - Backend `MapHub<TransactionsHub>("/hubs/transactions")` + `MapHub<NotificationsHub>("/hubs/notifications")` — kanıt: `backend/src/Skinora.API/Program.cs:419-420`
  - Client→server method imzaları (`JoinTransaction(Guid)`, `LeaveTransaction(Guid)`) — kanıt: `TransactionsHub.cs:61,96`
  - 8 transaction event + 5 spec notification event + 3 admin event (scope dışı) — kanıt: `TransactionRealtimePayloads.cs` + `NotificationRealtimePayloads.cs`
  - JWT query-param pipeline aktif — kanıt: T61 raporu + `Program.cs:266` `AddSignalR().AddJsonProtocol`
  - SignalR JSON protokolü `JsonStringEnumConverter` ile string enum names emit eder — kanıt: `Program.cs:267-268`
  - `ReviewStatus` (PENDING/APPROVED/REJECTED), `DisputeStatus` (OPEN/ESCALATED/CLOSED), `TimeoutFreezeReason` (MAINTENANCE/STEAM_OUTAGE/BLOCKCHAIN_DEGRADATION/EMERGENCY_HOLD), `TransactionStatus` (13 değer) — frontend `types/enums.ts` birebir mirror eder, T96 yeni enum eklemedi; yalnız `TimeoutPhase` (Accept/TradeOfferToSeller/Payment/Delivery — PascalCase wire) ve `EmergencyHoldReleaseAction` (RESUME/CANCEL) `events.ts` içinde string union olarak tanımlandı (sadece CountdownSync ve EmergencyHoldReleased payload'ında kullanılıyor; UI'da branch etmediği için enum object'e gerek yok).
- **Mini güvenlik:**
  - Secret sızıntısı: Temiz (accessToken auth store'dan okunuyor, kodda sabit yok)
  - Auth: Hub'lar backend tarafında `[Authorize]` zorunluluğu; client tokenFactory tetiklenir, login öncesi hub'lar `start` edilmez (`RealtimeProvider` auth gate)
  - Input validation: `transactionId` UUID format kontrolü backend `HubException("TRANSACTION_NOT_FOUND")` ile yapılıyor; client tarafında subscribe'da type check (`string | undefined`)
  - Yeni bağımlılık: yok — package.json/lock değişmedi
  - XSS: Event payload'larındaki string alanlar React text rendering ile yazdırılır; `dangerouslySetInnerHTML` / `eval` / `innerHTML` / `new Function` kullanımı yok
- **Locale parity:** 4-locale 632/632/632/632 leaf — T96 yeni UI metni eklemedi (silent state sync), parity korundu
- **Backend testler:** dokunulmadı; mevcut T61 25 (Realtime.Tests) + T61 5 (TransactionsHubEndpointTests) + T62 5 (NotificationsHubEndpointTests) hub kontratını koruyor
- **Scope kararları (proje sahibi onayı 2026-05-24, AskUserQuestion):**
  - Scope: **Yukarıdaki kapsam + dosya listesi ile devam** (Recommended) — toast C09 ve admin event'ler K1/K2 forward-defer
  - Test: **Sadece build + lint + format check** (Recommended) — frontend Vitest framework yok, backend hub testleri kontratı koruyor
