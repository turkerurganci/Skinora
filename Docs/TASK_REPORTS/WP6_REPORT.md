# WP6 — Steam dispute checker'ları + auto-resolve doğrulama

**Faz:** F6-öncesi (PRE_F6_PLAN) | **Durum:** ⏳ Doğrulama bekliyor | **Tarih:** 2026-06-17

---

## Yapılan İşler

WP6 üç kalemden oluşur (PRE_F6_PLAN §2 WP6): (1) gerçek sidecar-destekli **trade-hold + Mobile Authenticator checker**'ları (iki canlı stub), (2) mevcut `SidecarSteamInventoryReader` **auto-resolve yolunu doğrula/sertleştir**, (3) `TradeOfferMonitor` **hot-add re-attach**'i doğrula.

**Owner kararları (AskUserQuestion 2026-06-17):** sidecar yaklaşımı = **doğrudan Steam Web API** (`IEconService/GetTradeHoldDurations/v1`, bot session gerekmez) · Item 2/3 derinliği = **doğrula + regresyon + by-design** (10_MVP_SCOPE §3.6/§6 scope-fence, dinamik re-attach hook'u kurulmadı).

### 1. Gerçek trade-hold + MA checker (iki stub değişimi)

- **Sidecar yeni endpoint** `GET /api/trade-hold/:steamId?accessToken=...` → `TradeHoldService` (`IEconService/GetTradeHoldDurations/v1`'i `config.steamApiKey` + `steamid_target` + `trade_offer_access_token` ile çağırır). **08 §2.2 birebir:** API-key çağrısı (bot session yok), anahtar `x-webapi-key` **header**'ında (query'de değil → log sızıntısı yok), `their_escrow.escrow_end_duration_seconds === 0` → MA aktif. Rate-limit mevcut `RateLimitedQueue` (1 req/s) üzerinden. `config.steamApiKey` zaten tanımlıydı (T14'ten beri kullanılmıyordu) → yeni env/secret yok.
- **Backend paylaşılan port** `ISteamTradeHoldProbe` (`Skinora.Shared/Steam`) + `HttpSteamTradeHoldClient` (`Skinora.Steam`, kendi typed HttpClient'i, `SteamSidecarOptions` + `X-Internal-Key` paylaşır; `HttpSteamSidecarInventoryClient` deseni). Port Shared'de çünkü iki kardeş modül (`Users`/`Auth`) cross-reference olmadan tüketir — `SidecarSteamInventoryReader` düzenlemesinin aynısı.
- **`SidecarTradeHoldChecker`** (`Skinora.Users` → `ITradeHoldChecker`, U17 trade-URL kaydı): probe Unavailable → `(Available:false)` → 07 §5.16a `STEAM_API_UNAVAILABLE`; active → `(true,true,null)`; inactive → `(true,false,SetupGuideUrl)`. **DI swap** `SteamModule.Replace` (Users stub TryAddScoped → Sidecar).
- **`SidecarMobileAuthenticatorCheck`** (`Skinora.Auth` → `IMobileAuthenticatorCheck`, A7 re-verify): A7 kontratı ikili (Available alanı yok) → **fail-closed**: unreachable probe → `(Active:false, SetupGuideUrl)` = konservatif stub default'uyla özdeş → Steam outage asla "MA aktif" göstermez. **DI swap** `SteamAuthenticationModule` (Skinora.Steam Auth'u referans etmediğinden swap API katmanında).

### 2. Auto-resolve yolu — doğrula/sertleştir

- **Doğrulandı:** `DeliveryDisputeAutoChecker` + `WrongItemDisputeAutoChecker` zaten gerçek `SidecarSteamInventoryReader`'ı (`SteamModule.cs` `Replace`) tüketiyor — sıfırdan kurulmadı.
- **Sertleştirme regresyonu:** yeni `Open_WrongItem_DeliveredAssetSet_ButSidecarProbeNull_StaysOpen` — teslim asset'i set ama sidecar probe `null` döndüğünde (gerçek reader'ın `Unavailable`/`InventoryPrivate` → null eşlemesi) WRONG_ITEM dispute'unun **fail-closed** kaldığını (yanlış auto-escalate/auto-resolve YOK; OPEN + manuel escalate) kanıtlar.

### 3. TradeOfferMonitor hot-add — resolved-by-design

- **Doğrulandı:** `BotManager` dinamik bot-add yolu içermez (yalnız `initialize`/`removeFromPool`; `sessions.set` sadece `startBot`'ta) → her canlı bot startup'ta attach edilir, hiçbir bot hot-add edilmez. Session recovery (SESSION_EXPIRED→RECONNECTING→READY) aynı `BotSession`/`TradeOfferManager` instance'ını kullanır → listener reconnect'te bağlı kalır. İdempotent `attachToSession` hook'u gelecekteki dinamik pool (T69) için zaten yerinde + test edilmiş (`TradeOfferMonitor.test.ts`).
- **Çıktı:** `TradeOfferMonitor.ts` "Pool dynamics" doc-comment'i WP6 doğrulamasını yansıtacak şekilde güncellendi (kod davranışı değişmedi — statik pool'da re-attach gerekmez).

## Etkilenen Modüller / Dosyalar

**Sidecar (yeni):** `src/trade/TradeHoldService.ts`, `src/trade/TradeHoldService.test.ts`.
**Sidecar (değişen):** `src/api/routes.ts` (+`/trade-hold/:steamId` route + handler), `src/api/routes.test.ts` (+6 route testi), `src/index.ts` (TradeHoldService + RateLimitedQueue wiring), `src/trade/TradeOfferMonitor.ts` (pool-dynamics doc-comment).
**Backend (yeni):** `Skinora.Shared/Steam/ISteamTradeHoldProbe.cs`, `Skinora.Steam/Application/Inventory/HttpSteamTradeHoldClient.cs`, `Skinora.Users/Application/Settings/SidecarTradeHoldChecker.cs`, `Skinora.Auth/Application/MobileAuthenticator/SidecarMobileAuthenticatorCheck.cs`.
**Backend (değişen):** `Skinora.API/Configuration/SteamModule.cs` (trade-hold typed client + `ISteamTradeHoldProbe` bridge + `ITradeHoldChecker` Replace), `Skinora.API/Configuration/SteamAuthenticationModule.cs` (`IMobileAuthenticatorCheck` → Sidecar swap).
**Tests (yeni):** `Skinora.Steam.Tests/Unit/HttpSteamTradeHoldClientTests.cs` (8), `Skinora.Users.Tests/Unit/SidecarTradeHoldCheckerTests.cs` (4), `Skinora.Auth.Tests/Unit/SidecarMobileAuthenticatorCheckTests.cs` (4).
**Tests (değişen):** `Skinora.Disputes.Tests/Integration/DisputeServiceTests.cs` (+1 fail-closed regresyon), `Skinora.API.Tests/Integration/AuthReVerifyEndpointTests.cs` (A7 testi yeniden adlandırıldı — gerçek fail-closed zincirini doğrular).

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Sidecar `GetTradeHoldDurations` endpoint (08 §2.2 Web-API, x-webapi-key header, escrow=0→active) | ✓ | `TradeHoldService` + `routes.ts`; `TradeHoldService.test.ts` 7 (0sn→active, 15g→inactive, header vs query, key-missing, 5xx, transport, malformed) + route test 6 |
| 2 | Gerçek `ITradeHoldChecker` (U17) stub yerine bağlandı | ✓ | `SidecarTradeHoldChecker` + `SteamModule.Replace`; `SidecarTradeHoldCheckerTests` 4 (active/inactive/unavailable/forward) |
| 3 | Gerçek `IMobileAuthenticatorCheck` (A7) stub yerine bağlandı | ✓ | `SidecarMobileAuthenticatorCheck` + `SteamAuthenticationModule`; `SidecarMobileAuthenticatorCheckTests` 4 + `AuthReVerify..._FailsClosed` E2E |
| 4 | Steam erişilemez → fail-closed (U17 503 `STEAM_API_UNAVAILABLE` / A7 active:false) | ✓ | `HttpSteamTradeHoldClient` Unavailable eşlemesi (5xx/transport/config); checker testleri + A7 E2E |
| 5 | Auto-resolve yolu gerçek `SidecarSteamInventoryReader`'a bağlı (doğrulandı) | ✓ | `SteamModule.cs:56` `Replace`; mevcut `DisputeServiceTests` DELIVERY/WRONG_ITEM auto-resolve 6 testi |
| 6 | Auto-resolve sidecar-null durumunda fail-closed (sertleştirme) | ✓ | yeni `Open_WrongItem_DeliveredAssetSet_ButSidecarProbeNull_StaysOpen` (OPEN, escalate/resolve event yok) |
| 7 | TradeOfferMonitor hot-add: statik pool → resolved-by-design | ✓ | `BotManager` dinamik-add yok (grep); `attachToSession` idempotent + test edilmiş; doc-comment güncellendi |
| 8 | Migration yok / yeni dep yok / build+test+format yeşil | ✓ | şema değişmedi; 0 yeni paket; Debug+Release 0W/0E; `dotnet format` exit 0; sidecar `tsc` exit 0 |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend unit (Steam) | ✓ 103/103 | `Skinora.Steam.Tests` (95 baseline + 8 `HttpSteamTradeHoldClient`) |
| Backend unit (Users) | ✓ 20/20 | `Skinora.Users.Tests` (16 + 4 `SidecarTradeHoldChecker`) |
| Backend unit (Auth) | ✓ 119/119 | `Skinora.Auth.Tests` (115 + 4 `SidecarMobileAuthenticatorCheck`) |
| Backend integration (Disputes) | ✓ 39/39 | `Skinora.Disputes.Tests` (38 + 1 fail-closed regresyon; lokal SQLite) |
| Sidecar (vitest) | ✓ 158/158 | 10 dosya (145 baseline + 13 yeni: 7 service + 6 route) |
| Build | ✓ 0W/0E | `dotnet build -c Debug` + `-c Release` |
| Format | ✓ temiz | `dotnet format Skinora.sln --verify-no-changes --severity error` exit 0 |
| Sidecar typecheck | ✓ | `npx tsc --noEmit` exit 0 (CI sidecar gate'i) |
| Backend integration (API.Tests — A7 vb.) | CI-authoritative | TestContainers/SQL Server gerektirir (lokal Docker yok) → task CI'da doğrulanır |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validator bekliyor |
| Bulgu sayısı (yapım self-check) | 0 bloke-edici |
| Düzeltme gerekli mi | Hayır |

## Altyapı Değişiklikleri

- **Migration:** Yok — şema değişmedi (salt davranış/wiring + sidecar endpoint).
- **Config/env değişikliği:** Yok — `STEAM_API_KEY` (sidecar) zaten tanımlı; `SteamSidecar:BaseUrl`/`InternalKey` (backend) zaten mevcut.
- **Docker değişikliği:** Yok.
- **Yeni bağımlılık:** Yok — sidecar global `fetch` (Node ≥20, `WebhookClient` deseni); backend mevcut typed-HttpClient altyapısı.

## Commit & PR

- Branch: `task/WP6-steam-dispute-checkers`
- Commit: `<hash>` — WP6 implementation
- PR: #<TBD>
- CI: ⏳ izleniyor

## Known Limitations / Follow-up

- **Operasyonel aktivasyon:** Gerçek trade-hold/MA kontrolü prod'da `STEAM_API_KEY` set edilmiş + sidecar erişilebilir olduğunda canlanır; aksi halde fail-closed (U17 503 / A7 active:false). T111/T113 staging E2E gerçek key ile doğrular.
- **A7 "unavailable" durumu:** A7 `MobileAuthenticatorResult` ikili kontratı (Active + SetupGuideUrl) "Steam erişilemez" ayrımını taşımaz — bu ayrım birincil U17 yolunda `TradeHoldResult.Available` ile yapılır. A7 outage'i konservatif olarak inactive sayar (kontrat reshape WP6 kapsamı dışı, scope-fence).
- **Dinamik bot pool re-attach:** statik pool MVP olduğundan resolved-by-design; T69 kapasite-ölçeklemesi dinamik hot-add eklerse idempotent `attachToSession(newSession)` çağrılır (hook hazır, davranış değişikliği gerekmez).
- **Sidecar prettier drift:** repo-geneli pre-existing prettier drift (untouched `WebhookClient.ts`/`BotSession.ts` de fail; CI sidecar'da `tsc` gate'ler, `format:check` değil) → WP18. WP6 yeni dosyaları çevre koduyla tutarlı el-yazımı stilde.

## Notlar

- **Adım -1 (working tree):** temiz (WP5 merge sonrası).
- **Adım 0 (main CI):** son 3 run `success` (`27679584356` / `27679583169` / `27650617814`).
- **Dış varsayımlar (Adım 4 — hepsi doğrulandı):** (1) sidecar `config.steamApiKey` mevcut (kod okuması) ✓; (2) Node ≥20 global `fetch` (`package.json` engines + `WebhookClient.ts` kullanımı) ✓; (3) `IEconService/GetTradeHoldDurations/v1` 08 §2.2'de kanonik + API-key çağrısı (bot session gerekmez) ✓; (4) `RateLimitedQueue` + `steamWebApiRequestsPerSecond:1` mevcut ✓; yeni paket yok.
- **Bağımlılıklar:** WP1–WP5 hepsi ✓ merged → WP6 unblocked.
- **Çıktığı yetenek:** T111 (fraud creation-check) + T113 (dispute kapsamı) — DELIVERY/WRONG_ITEM otomatik çözüm + MA gate artık gerçek sidecar destekli.
