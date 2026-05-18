# T81 — Steam Market fiyat API + on-demand cache + fraud hookpoint

**Faz:** F4 | **Durum:** ⏳ Yapım bitti — bağımsız doğrulama bekliyor | **Tarih:** 2026-05-18

> **Bitiş Kapısı (task.md §"Bitiş Kapısı") — 8/8 ✓**
>
> 1. ✓ Branch push edildi (`task/T81-steam-market-price-api`)
> 2. ✓ PR açıldı ([PR #123](https://github.com/turkerurganci/Skinora/pull/123))
> 3. ✓ PR numarası rapora yazıldı (bkz. "Commit & PR")
> 4. ✓ Rapor + status + memory commit edilip push edildi (`b031aeb`)
> 5. ✓ CI run tamamlandı (`conclusion=success`)
> 6. ✓ CI run sonucu **success** — `aa48028` run [`26054430680`](https://github.com/turkerurganci/Skinora/actions/runs/26054430680) 10/10 + `b031aeb` run [`26054842163`](https://github.com/turkerurganci/Skinora/actions/runs/26054842163) 10/10
> 7. ✓ Branch izolasyon check temiz — `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+'` → yalnız `T81`
> 8. ✓ Repo memory'de T81 satırı eklendi ([`.claude/memory/MEMORY.md`](../../.claude/memory/MEMORY.md))

---

## Yapılan İşler

### Spec drift kapama (T81 ön-çalışması — PR #122)

`Plan 11 §F4/T81` ve `08 §7.3` "SQL Server `ItemPriceCache` tablosu" referansı veriyordu ama `06_DATA_MODEL.md` entity şemasında (§3.1–§3.23) tanım yoktu. T81 yapım chat'i Adım 4 (dış varsayım doğrulama) bunu yakaladı; INSTRUCTIONS §3.5 SPEC_GAP akışı ile proje sahibi onayı (2026-05-18) alındı: "Önce ayrı docs-only PR ile 06 §3.24, sonra T81 implementation" (Seçenek B). Ayrıca scope onayı "Infra-only — IPriceService + cache + parse + rate limit" (T-future fraud pipeline'ı `Transaction.MarketPriceAtCreation` set + `PRICE_DEVIATION` `FraudFlag` oluşturmayı devralır).

[PR #122](https://github.com/turkerurganci/Skinora/pull/122) (`9dce181` squash):
- `06 §1.1` envanter — 26. satır `ItemPriceCache` (Fraud grubu)
- `06 §3.24` entity tanımı — 8 field + sabitler (AppId=730, Currency=USD) + TTL semantiği + negative caching + silme politikası
- `06 §5.1` unique index — `ItemPriceCache.MarketHashName`
- `06 §5.2` performance index — `ItemPriceCache.FetchedAt` (stale tarama)
- `06` versiyon v5.0 → v5.1 + drift kapama notu
- `08 §7.3` cross-ref — "SQL Server — `ItemPriceCache` tablosu (06 §3.24)"

### T81 — Steam Market transport (Skinora.Shared/SteamMarket/)

- **`SteamMarketSettings`** — Provider switch (`steam-market` / `logging` fail-closed), BaseUrl, AppId=730, Currency=1=USD, TimeoutSeconds=10, RateLimitPerMinute=20, FreshTtlHours=24, StaleTtlHours=48.
- **`SteamMarketPriceQuote`** — record + factory'ler (`Median(median, lowest?)` / `Lowest(lowest)` / `NoPrice()`) + `EffectivePrice` (median ?? lowest).
- **`SteamMarketPriceParser`** — pure static:
  - `TryParsePrice(string?)` — non-digit/non-dot character strip + `decimal.Parse(InvariantCulture, NumberStyles.Number)`; locale-aware parse yasak (08 §7.2).
  - `ParseResponse(JsonElement)` — 08 §7.2 fallback chain median → lowest → no-price; `success: false` → `SteamMarketPermanentException`.
- **`SteamMarketExceptions`** — `SteamMarketException` base + `Transient` (5xx/timeout/transport) + `RateLimited` (429 + Retry-After) + `Permanent` (4xx-non-429 + invalid JSON + success=false).
- **`ISteamMarketRateLimiter` + `SteamMarketRateLimiter`** — Sliding-window 60s (default 20 req/dk); `TimeProvider` injection (FakeTimeProvider test). `Task.Delay(wait, _timeProvider, ct)` clock-aware. `RegisterRetryAfter(TimeSpan)` 429 server-side cool-down honour (`max(now + retryAfter, _nextAllowedUtc)`).
- **`ISteamMarketPriceClient` + `SteamMarketPriceClient`** — Raw HttpClient (T78/T79/T80 precedent — Discord/Telegram/Email aynı patern), `IDisposable` `using var _ = response;` lifecycle. URL build: `/market/priceoverview/?appid=730&currency=1&market_hash_name={Uri.EscapeDataString}`. Status code mapping: 200 → parser; 429 → `RegisterRetryAfter` + `RateLimitedException`; 5xx → `TransientException`; 4xx-other → `PermanentException`; `TaskCanceledException` (no ct cancel) → `TransientException`; `HttpRequestException` → `TransientException`; `JsonException` → `PermanentException`; `success: false` → swallow + return `NoPrice`.
- **`LoggingSteamMarketPriceClient`** — Provider=logging stub, her çağrı `NoPrice` döner (08 §7.4 adım 3b'ye düşer). CI/dev fail-closed.

### T81 — Cache + orchestrator (Skinora.Fraud)

- **`ItemPriceCache` entity** (06 §3.24) — `BaseEntity` inherit (Id Guid + CreatedAt + UpdatedAt + RowVersion) + `MarketHashName` (string 450) + `MedianPrice`/`LowestPrice` (decimal(18,6) NULL) + `FetchedAt` (datetime2 NN) + `Source` (string 20 NN). `ItemPriceSources.SteamMarket` const.
- **`ItemPriceCacheConfiguration`** — UQ `MarketHashName` (`UQ_ItemPriceCaches_MarketHashName`) + IX `FetchedAt` (`IX_ItemPriceCaches_FetchedAt`) + CHECK `Source = 'STEAM_MARKET'`.
- **`IPriceService` + `PriceService`** — cache-first state machine:
  | Cache durumu | Aksiyon |
  |---|---|
  | Miss | Sync API + insert + return effective; API fail → null |
  | Fresh (≤24h) | Return cached, API yok |
  | Stale (24-48h) | Return cached + `Hangfire.Enqueue<PriceService>(s => s.RefreshAsync(name))` background refresh |
  | Expired (>48h) | Sync API + upsert; API fail + cache age ≤48h → fallback (defensive); cache yok / >48h → null |
- **`RefreshAsync(string)`** — Hangfire background entry point (FraudModule'de `services.AddScoped<PriceService>()` + `services.AddScoped<IPriceService>(sp => sp.GetRequiredService<PriceService>())` — Hangfire `Enqueue<PriceService>` concrete tip ile resolve eder, normal consumer `IPriceService` ile çalışır). `try`/`catch (Exception)` swallow + `LogWarning` — fraud pipeline degrade etmez (08 §7.4 ruhu).
- **CS4014 false positive** — `Expression<Action<PriceService>> call = s => s.RefreshAsync(...)` lambda body Task döndürüyor diye compiler uyarı veriyor; `#pragma warning disable CS4014` ile yerel suppress (Hangfire `Action<T>` Expression sözleşmesi gereği).

### Migration

- `20260518185310_T81_AddItemPriceCache` — `dotnet ef migrations add T81_AddItemPriceCache --project src/Skinora.Shared --startup-project src/Skinora.API`. Up: CreateTable + UQ + IX + CHECK. Down: DropTable. `AppDbContextModelSnapshot.cs` güncellendi.

### DI wiring

- **`Skinora.API/Program.cs`** — yeni T81 block (Discord block sonrası, UsersModule registration öncesi):
  - `using Skinora.Shared.SteamMarket;` import.
  - `Configure<SteamMarketSettings>(GetSection("SteamMarket"))` bind.
  - `AddSingleton<ISteamMarketRateLimiter, SteamMarketRateLimiter>()` (ctor `IOptions<SteamMarketSettings>` + `TimeProvider`, ikinci dep `TryAddSingleton(TimeProvider.System)` zaten FraudModule'de var).
  - Provider switch: `steam-market` → `AddHttpClient<ISteamMarketPriceClient, SteamMarketPriceClient>` (timeout settings'ten); default `logging` → `AddSingleton<ISteamMarketPriceClient, LoggingSteamMarketPriceClient>` (fail-closed).
- **`Skinora.Fraud/FraudModule.cs`** — `services.AddScoped<PriceService>()` + `services.AddScoped<IPriceService>(sp => sp.GetRequiredService<PriceService>())` (Hangfire `Enqueue<PriceService>` concrete-resolve + consumer interface alias).
- **`appsettings.json`** — yeni `SteamMarket` section: Provider="logging" default + BaseUrl="https://steamcommunity.com" + AppId=730 + Currency=1 + TimeoutSeconds=10 + RateLimitPerMinute=20 + FreshTtlHours=24 + StaleTtlHours=48.

### Test'ler

- **`Skinora.Shared.Tests/Unit/SteamMarket/SteamMarketPriceParserTests`** (15 test) — TryParsePrice 6 valid token + 6 empty/symbol-only + 3 strip-and-thousands invariant; ParseResponse 6 cases (median preferred + lowest fallback + median unparseable → lowest + both missing → no-price + both empty → no-price + success=false throw + not-object throw).
- **`Skinora.Shared.Tests/Unit/SteamMarket/SteamMarketRateLimiterTests`** (9 test) — FakeTimeProvider + first-call no-wait + under-limit immediate + register zero/negative no-op + register retry-after blocks + longer-deadline-wins + window-full drops expired + window-full waits-until-oldest + ctor non-positive throws + ctor null throws.
- **`Skinora.Shared.Tests/Unit/SteamMarket/SteamMarketPriceClientTests`** (13 test) — OK median parse + canonical URL build (appid/currency/escape) + success=false swallow → NoPrice + empty prices → NoPrice + 429 retry-after + 429 default 30s + 5xx Transient + 4xx Permanent + transport HttpRequestException → Transient + TaskCanceledException → Transient + invalid JSON → Permanent + empty name → ArgumentException + missing BaseUrl ctor → InvalidOperation.
- **`Skinora.Fraud.Tests/Integration/PriceServiceTests`** (10 test, `IntegrationTestBase` SQL Server) — Miss + Fresh + Stale (+ enqueue spy) + Expired + ApiFail+CacheTooOld → null + ApiFail+NoCache → null + NoPrice cached nulls + LowestOnly + RefreshAsync swallow + RefreshAsync upsert. `Microsoft.Extensions.TimeProvider.Testing 9.0.0` Skinora.Shared.Tests csproj'a eklendi.

## Etkilenen Modüller / Dosyalar

### Skinora.Shared (yeni `SteamMarket/` klasörü)

- [`backend/src/Skinora.Shared/SteamMarket/SteamMarketSettings.cs`](../../backend/src/Skinora.Shared/SteamMarket/SteamMarketSettings.cs) (yeni)
- [`backend/src/Skinora.Shared/SteamMarket/SteamMarketPriceQuote.cs`](../../backend/src/Skinora.Shared/SteamMarket/SteamMarketPriceQuote.cs) (yeni — record + factory)
- [`backend/src/Skinora.Shared/SteamMarket/SteamMarketPriceParser.cs`](../../backend/src/Skinora.Shared/SteamMarket/SteamMarketPriceParser.cs) (yeni — pure static)
- [`backend/src/Skinora.Shared/SteamMarket/SteamMarketExceptions.cs`](../../backend/src/Skinora.Shared/SteamMarket/SteamMarketExceptions.cs) (yeni)
- [`backend/src/Skinora.Shared/SteamMarket/ISteamMarketRateLimiter.cs`](../../backend/src/Skinora.Shared/SteamMarket/ISteamMarketRateLimiter.cs) (yeni)
- [`backend/src/Skinora.Shared/SteamMarket/SteamMarketRateLimiter.cs`](../../backend/src/Skinora.Shared/SteamMarket/SteamMarketRateLimiter.cs) (yeni — sliding-window + TimeProvider)
- [`backend/src/Skinora.Shared/SteamMarket/ISteamMarketPriceClient.cs`](../../backend/src/Skinora.Shared/SteamMarket/ISteamMarketPriceClient.cs) (yeni)
- [`backend/src/Skinora.Shared/SteamMarket/SteamMarketPriceClient.cs`](../../backend/src/Skinora.Shared/SteamMarket/SteamMarketPriceClient.cs) (yeni — raw HttpClient)
- [`backend/src/Skinora.Shared/SteamMarket/LoggingSteamMarketPriceClient.cs`](../../backend/src/Skinora.Shared/SteamMarket/LoggingSteamMarketPriceClient.cs) (yeni — Provider=logging stub)

### Skinora.Fraud

- [`backend/src/Modules/Skinora.Fraud/Domain/Entities/ItemPriceCache.cs`](../../backend/src/Modules/Skinora.Fraud/Domain/Entities/ItemPriceCache.cs) (yeni — entity + `ItemPriceSources` const)
- [`backend/src/Modules/Skinora.Fraud/Infrastructure/Persistence/ItemPriceCacheConfiguration.cs`](../../backend/src/Modules/Skinora.Fraud/Infrastructure/Persistence/ItemPriceCacheConfiguration.cs) (yeni — UQ + IX + CHECK)
- [`backend/src/Modules/Skinora.Fraud/Application/Pricing/IPriceService.cs`](../../backend/src/Modules/Skinora.Fraud/Application/Pricing/IPriceService.cs) (yeni)
- [`backend/src/Modules/Skinora.Fraud/Application/Pricing/PriceService.cs`](../../backend/src/Modules/Skinora.Fraud/Application/Pricing/PriceService.cs) (yeni — cache orchestrator + RefreshAsync background entry)
- [`backend/src/Modules/Skinora.Fraud/FraudModule.cs`](../../backend/src/Modules/Skinora.Fraud/FraudModule.cs) — PriceService concrete + IPriceService alias DI register

### Migration

- [`backend/src/Skinora.Shared/Persistence/Migrations/20260518185310_T81_AddItemPriceCache.cs`](../../backend/src/Skinora.Shared/Persistence/Migrations/20260518185310_T81_AddItemPriceCache.cs) (yeni)
- [`backend/src/Skinora.Shared/Persistence/Migrations/20260518185310_T81_AddItemPriceCache.Designer.cs`](../../backend/src/Skinora.Shared/Persistence/Migrations/20260518185310_T81_AddItemPriceCache.Designer.cs) (yeni)
- [`backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs`](../../backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs) — `ItemPriceCache` entity model bloğu eklendi

### Skinora.API

- [`backend/src/Skinora.API/Program.cs`](../../backend/src/Skinora.API/Program.cs) — SteamMarket using import + Configure + rate limiter singleton + provider switch
- [`backend/src/Skinora.API/appsettings.json`](../../backend/src/Skinora.API/appsettings.json) — `SteamMarket` section (8 alan)

### Testler

- [`backend/tests/Skinora.Shared.Tests/Skinora.Shared.Tests.csproj`](../../backend/tests/Skinora.Shared.Tests/Skinora.Shared.Tests.csproj) — `Microsoft.Extensions.TimeProvider.Testing 9.0.0` eklendi
- [`backend/tests/Skinora.Shared.Tests/Unit/SteamMarket/SteamMarketPriceParserTests.cs`](../../backend/tests/Skinora.Shared.Tests/Unit/SteamMarket/SteamMarketPriceParserTests.cs) (yeni — 15 test)
- [`backend/tests/Skinora.Shared.Tests/Unit/SteamMarket/SteamMarketRateLimiterTests.cs`](../../backend/tests/Skinora.Shared.Tests/Unit/SteamMarket/SteamMarketRateLimiterTests.cs) (yeni — 9 test)
- [`backend/tests/Skinora.Shared.Tests/Unit/SteamMarket/SteamMarketPriceClientTests.cs`](../../backend/tests/Skinora.Shared.Tests/Unit/SteamMarket/SteamMarketPriceClientTests.cs) (yeni — 13 test)
- [`backend/tests/Skinora.Fraud.Tests/Integration/PriceServiceTests.cs`](../../backend/tests/Skinora.Fraud.Tests/Integration/PriceServiceTests.cs) (yeni — 10 test, IntegrationTestBase SQL Server)

### Spec drift kapama PR'ı

- [`Docs/06_DATA_MODEL.md`](../06_DATA_MODEL.md) — §3.24 + §1.1 + §5.1 + §5.2 (PR #122)
- [`Docs/08_INTEGRATION_SPEC.md`](../08_INTEGRATION_SPEC.md) — §7.3 cross-ref (PR #122)

## Kabul Kriterleri Kontrolü (11 §F4/T81 → 08 §7.1–§7.4)

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Steam Market priceoverview çağrısı (public, auth yok) | ✓ | [`SteamMarketPriceClient.GetPriceAsync`](../../backend/src/Skinora.Shared/SteamMarket/SteamMarketPriceClient.cs) `Uri.EscapeDataString` + Authorization header yok. `SteamMarketPriceClientTests.GetPriceAsync_BuildsCanonicalUrl` |
| 2 | Fiyat parse: median → lowest → no-price (kontrol atla) | ✓ | [`SteamMarketPriceParser.ParseResponse`](../../backend/src/Skinora.Shared/SteamMarket/SteamMarketPriceParser.cs) 08 §7.2 fallback chain. `SteamMarketPriceParserTests` 6 ParseResponse senaryosu (median preferred + lowest fallback + median unparseable + both missing + both empty + success=false) |
| 3 | Currency sembolü strip, binlik ayracı kaldır, nokta ondalık | ✓ | [`SteamMarketPriceParser.TryParsePrice`](../../backend/src/Skinora.Shared/SteamMarket/SteamMarketPriceParser.cs#L26-L50) non-digit/non-dot strip + `InvariantCulture` parse. `SteamMarketPriceParserTests.TryParsePrice_StripsCurrencySymbolAndThousandsSeparator` ($/€/USD prefix tüm formatlar) |
| 4 | Cache: SQL Server `ItemPriceCache`, 24s fresh / 48s stale / 48+ expired | ✓ | [`ItemPriceCache`](../../backend/src/Modules/Skinora.Fraud/Domain/Entities/ItemPriceCache.cs) + [config](../../backend/src/Modules/Skinora.Fraud/Infrastructure/Persistence/ItemPriceCacheConfiguration.cs) + migration `20260518185310_T81_AddItemPriceCache`. [`PriceService.GetMarketPriceAsync`](../../backend/src/Modules/Skinora.Fraud/Application/Pricing/PriceService.cs) — FreshTtlHours=24, StaleTtlHours=48 settings'ten. `PriceServiceTests` Miss/Fresh/Stale/Expired 4 test |
| 5 | On-demand fetch: cache kontrol → stale ise arka plan yenileme → expired ise API | ✓ | [`PriceService` state machine](../../backend/src/Modules/Skinora.Fraud/Application/Pricing/PriceService.cs#L60-L90) — Miss/Expired sync API; Fresh return cached; Stale return cached + `EnqueueRefresh`. `RefreshAsync` Hangfire entry point exception swallow + upsert. `PriceServiceTests` Stale enqueue spy + RefreshAsync 2 test |
| 6 | `IPriceService` interface ile abstraction | ✓ | [`IPriceService`](../../backend/src/Modules/Skinora.Fraud/Application/Pricing/IPriceService.cs) Skinora.Fraud.Application.Pricing namespace. FraudModule DI `AddScoped<PriceService>()` + `AddScoped<IPriceService>(sp => sp.GetRequiredService<PriceService>())` (Hangfire concrete-resolve + consumer interface). 08 §7.5 büyüme yolu için `ISteamMarketPriceClient` ayrı port (provider switch) |
| 7 | Rate limit: ~20 req/dk, bekleme + cache kullan | ✓ | [`SteamMarketRateLimiter`](../../backend/src/Skinora.Shared/SteamMarket/SteamMarketRateLimiter.cs) sliding-window 60s + `Task.Delay(wait, _timeProvider, ct)` clock-aware + `RegisterRetryAfter(TimeSpan)` 429 propagation. RateLimitPerMinute=20 default. Cache-hit fresh path limiter çağırmaz (08 §7.4 "Bekleme + cache kullan"). `SteamMarketRateLimiterTests` 9 test (window-full waits, retry-after blocks, longer-deadline-wins) |
| 8 | Erişilemez → cache ≤48s kullan, yoksa kontrol atla + log | ✓ | [`PriceService.FetchAndUpsertAsync` catch](../../backend/src/Modules/Skinora.Fraud/Application/Pricing/PriceService.cs#L113-L142) — `SteamMarketException` catch + `LogWarning` + cache age ≤ StaleTtl ise fallback return; yoksa `null` (08 §7.4 adım 3b). Background refresh `RefreshAsync` exception swallow + log. `PriceServiceTests.ApiTransientAndCacheTooOld_ReturnsNull` + `_ApiTransientAndNoCache_ReturnsNull` |

## Doğrulama Kontrol Listesi (11 §T81)

- [x] **08 §7.1–§7.4 tüm kurallar uygulanmış mı?** — §7.1 veri kaynağı (public, auth yok, ücretsiz) ✓; §7.2 endpoint detayları + URL paramları + fallback chain median→lowest→no-price + format strip kuralı ✓; §7.3 SQL Server `ItemPriceCache` + 24/48/48+ TTL + on-demand fetch flow ✓; §7.4 karar ağacı 5 adımı + 4 hata satırı (API down → fallback / rate limit → bekleme / item yoksa → atla / API kalıcı değişiklik → IPriceService abstraction T-future swap) ✓.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (Skinora.Shared.Tests/Unit/SteamMarket) | ✓ **42/42 PASS** | `dotnet test --filter ~SteamMarket` (Release, no-build, 77ms). Parser 15 + RateLimiter 9 + Client 13 + 5 ek edge (Empty/Lowest etc.) |
| Unit (Skinora.Shared.Tests tam suite) | ✓ **370/370 PASS** | `dotnet test Skinora.Shared.Tests --no-build` (Release, 22s). T80 sonrası 328 + T81 yeni 42 |
| Unit (Skinora.Fraud.Tests non-integration) | ✓ **14/14 PASS** | `dotnet test --filter "FullyQualifiedName!~Integration"` (Release, 190ms) — regresyon yok |
| Integration (Skinora.Fraud.Tests Integration) | ✓ CI testcontainer PASS | `PriceServiceTests` 10 test — `4. Integration test` job ✓ (HEAD `aa48028` run [`26054430680`](https://github.com/turkerurganci/Skinora/actions/runs/26054430680) + HEAD `b031aeb` run [`26054842163`](https://github.com/turkerurganci/Skinora/actions/runs/26054842163)) |
| Build | ✓ **0W/0E** | `dotnet build Skinora.sln -c Release` (18.76s) |
| Format | ✓ **Δ=0** | `dotnet format Skinora.sln --verify-no-changes` (0 diff) |
| CI run (impl `aa48028`) | ✓ **10/10 SUCCESS** | [run `26054430680`](https://github.com/turkerurganci/Skinora/actions/runs/26054430680) (Detect + Lint + Build + Unit + Integration + Contract + Migration + Docker backend + CI Gate; Guard skipped — PR) |
| CI run (docs `b031aeb`) | ✓ **10/10 SUCCESS** | [run `26054842163`](https://github.com/turkerurganci/Skinora/actions/runs/26054842163) (Detect + Lint + Build + Unit + Integration + Contract + Migration + Docker backend + CI Gate; Guard skipped — PR) |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS bağımsız validator (2026-05-18)** |
| Bulgu sayısı | 0 S-bulgu, 3 minor advisory (A1 IMarketPriceProvider köprüsü T-future K1 ile aynı / A2 singleton limiter local state K2 / A3 Provider=logging default K4) |
| Düzeltme gerekli mi | Hayır |
| Validator CI kanıtı | HEAD `8713fba` run [`26055607338`](https://github.com/turkerurganci/Skinora/actions/runs/26055607338) **10/10 SUCCESS** (Detect+Guard skipped+Lint+Build+Unit+Integration+Contract+Migration+Docker backend+CI Gate); önceki commit `b031aeb` run [`26054842163`](https://github.com/turkerurganci/Skinora/actions/runs/26054842163) 10/10 ✓; impl `aa48028` run [`26054430680`](https://github.com/turkerurganci/Skinora/actions/runs/26054430680) 10/10 ✓ |
| Validator lokal re-run | Shared.Tests SteamMarket filter **42/42 PASS** (191 ms) + Fraud.Tests non-int **14/14 PASS** (42 ms) + Release build **0W/0E** (57.67 s) + `dotnet format` **Δ=0** |
| Main CI startup (Adım 0) | ✓ 3/3 success ([`26053127683`](https://github.com/turkerurganci/Skinora/actions/runs/26053127683) + [`26053123083`](https://github.com/turkerurganci/Skinora/actions/runs/26053123083) + [`26037146810`](https://github.com/turkerurganci/Skinora/actions/runs/26037146810)) |
| Rapor uyumu | Tam — 8 kabul + 1 doğrulama listesi + 8 K1–K8 rapor ile bağımsız değerlendirme arasında sapma yok |

## Altyapı Değişiklikleri

- **Migration:** ✓ Var — `20260518185310_T81_AddItemPriceCache` (`ItemPriceCaches` tablosu: 9 sütun + UQ + IX + CHECK)
- **Config/env değişikliği:** ✓ Var — `appsettings.json` yeni `SteamMarket` section (8 alan); Provider="logging" default → CI/dev fail-closed
- **Docker değişikliği:** Yok
- **DI:** `ISteamMarketRateLimiter` singleton, `ISteamMarketPriceClient` provider-conditional (typed HttpClient veya singleton stub), `PriceService` scoped + `IPriceService` alias. `TimeProvider.System` zaten `FraudModule` `TryAddSingleton` ile mevcut.
- **Yeni dış bağımlılık:** Runtime yok (raw HttpClient + System.Text.Json — T78/T79/T80 paterni); test-only `Microsoft.Extensions.TimeProvider.Testing 9.0.0` Skinora.Shared.Tests'e eklendi.
- **Spec drift kapama PR'ı:** PR #122 (`9dce181`) merged — 06 §3.24 ItemPriceCache entity tanımı.

## Commit & PR

- **Branch:** `task/T81-steam-market-price-api`
- **Commits:**
  - `aa48028` — `T81: Steam Market fiyat API + on-demand cache + fraud price-deviation hookpoint` (24 dosya / +4864 / 0)
  - `b031aeb` — `T81: rapor + status + memory yansıt` (3 dosya / +203 / -2)
- **PR:** [#123](https://github.com/turkerurganci/Skinora/pull/123) — `task/T81-steam-market-price-api` → `main`
- **CI:** ✓ **HEAD `b031aeb` run [`26054842163`](https://github.com/turkerurganci/Skinora/actions/runs/26054842163) 10/10 SUCCESS** (Detect + Lint + Build + Unit + Integration + Contract + Migration + Docker backend + CI Gate; Guard skipped — PR). Impl HEAD `aa48028` run [`26054430680`](https://github.com/turkerurganci/Skinora/actions/runs/26054430680) da 10/10 ✓ — yeni docs commit concurrency cancel etmedi (full job suite docs commit'te de tetiklendi, integration testleri yine PASS).
- **Branch izolasyon check:** `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+'` → yalnız `T81` (PR #122 zaten main'e merge edilmiş, izolasyon temiz).
- **Spec drift kapama PR:** [PR #122](https://github.com/turkerurganci/Skinora/pull/122) — `chore/docs-itemprice-cache-spec` → `main`, squash `9dce181`, CI run [`26052987351`](https://github.com/turkerurganci/Skinora/actions/runs/26052987351) ✓ (docs-only — Detect+Lint+CI Gate, build/test jobs skipped).

## Known Limitations

- **K1 — Consumer wire-up T-future** — `Transaction.MarketPriceAtCreation` set + `PRICE_DEVIATION` `FraudFlag` oluşturma + `price_deviation_threshold` SystemSetting comparison T81'in scope'unda **değil** (proje sahibi onayı 2026-05-18). Bu PR `IPriceService` portunu sağlar; consumer wire-up ayrı fraud task'ında. 06 §3.5 `Transaction.MarketPriceAtCreation` alanı T19'da zaten oluşturulmuştu.
- **K2 — Singleton rate limiter local state** — `SteamMarketRateLimiter` AddSingleton; multi-instance backend deployment'ta (05 §3.1 stateless service modeli) local-state olur. Gerçek `~20 req/dk` global cap için Redis-backed limiter T-future scale milestone. MVP hacminde tek instance yeterli.
- **K3 — Hangfire enqueue silent fail** — `EnqueueRefresh` try/catch + `LogWarning`; başarısız enqueue fraud pipeline'ı durdurmaz (hot path cached değer döner). Stale veri >48h sonra expired path API'ye gider, sonsuza kadar stale kalmaz.
- **K4 — `LoggingSteamMarketPriceClient` her zaman NoPrice** — Provider=logging default CI/dev; production override (`SteamMarket__Provider=steam-market` env veya `appsettings.Production.json`) gerekli, yoksa fraud kontrolü tüm transaction'larda atlanır (08 §7.4 adım 3b).
- **K5 — Cache retention yok** — Upsert pattern; satırlar UQ `MarketHashName` ile bounded (CS2 item set sınırlı). Eski item'lar (örn. silinmiş item) zamanla stale kalır. T-future opsiyonel `RetentionJob` ekleyebilir (cache size > threshold ise FetchedAt'ın eski olanlarını sil). 06 §6.1 satırı bu task'ta eklenmedi.
- **K6 — Stale-while-revalidate Hangfire-bağımlı** — Hangfire down ise enqueue warn-log + hot path cache kullanır. Stale veri >48h sonra expired path API'ye gider.
- **K7 — Multi-tenant / multi-currency** — `SteamMarketSettings.AppId` ve `Currency` env config knob ama MVP tek tenant tek currency. Multi-app desteği için 06 §3.24 entity'sine kolon eklenmesi gerekir (UQ MarketHashName → composite UQ (MarketHashName, AppId)). 06 §3.24 "Sabitler" tablo karar.
- **K8 — Spec drift kapama ayrı PR'da** — 06 §3.24 ItemPriceCache tanımı PR #122'de eklendi, T81 implementation PR #123'te ayrı geldi. Kullanıcı onayıyla iki-PR yaklaşımı (Seçenek B). Tek PR'da implement + doc daha kompakt olurdu (Seçenek A) ama temizlik tercihi.

## Notlar

- **Working tree check (Adım -1):** `git status --short` boş — temiz başlangıç.
- **Main CI startup check (Adım 0):** ✓ 3/3 success (T80 [`26037146810`](https://github.com/turkerurganci/Skinora/actions/runs/26037146810) + [`26037146351`](https://github.com/turkerurganci/Skinora/actions/runs/26037146351) + T79 [`26027258676`](https://github.com/turkerurganci/Skinora/actions/runs/26027258676)).
- **Dış varsayım doğrulama (Adım 4):**
  - **K1 — Cache tablosu 06'da tanımlı**: ❌ KIRIK → PR #122 ile kapatıldı (06 §3.24 eklendi).
  - **K2 — Steam Market `priceoverview` public + ücretsiz**: ✓ 08 §7.2 + Valve community docs confirm.
  - **K3 — `~20 req/dk` rate limit**: ✓ 08 §7.1 bilinen kısıtlama tablosu + 3rd party reports.
  - **K4 — T67 ✓ tamamlandı**: ✓ IMPLEMENTATION_STATUS.md confirmed.
- **Scope onayı (Adım 5):** İki karar proje sahibi onayı (2026-05-18):
  1. SPEC_GAP yaklaşımı: **B — Önce ayrı docs-only PR ile 06 §3.24, sonra T81** (Recommended A bütünleşik PR'a karşı temiz separation tercih edildi).
  2. Wire-up scope: **Infra-only — IPriceService + cache + parse + rate limit** (Plan 11 §T81 kabul kriterleri 1:1; consumer wire-up T-future fraud task'ı).
- **Bağımlılık kontrolü (Adım 2):** T67 ✓ Tamamlandı (PR #107, bağımsız validator PASS).
- **Architectural karar — IPriceService alias DI** — Hangfire `Enqueue<PriceService>(s => s.RefreshAsync(...))` concrete tip ile resolve eder. Eğer `services.AddScoped<IPriceService, PriceService>()` yapsaydık, Hangfire `PriceService` resolve edemeyebilirdi (registration interface üzerinden, concrete kayıt yok). Çözüm: concrete + interface alias `services.AddScoped<IPriceService>(sp => sp.GetRequiredService<PriceService>())`. Consumer her zaman `IPriceService` üzerinden çalışır.
- **Architectural karar — Provider switch fail-closed** — Default `Provider=logging` → `LoggingSteamMarketPriceClient` her zaman `NoPrice` döner. Production `SteamMarket__Provider=steam-market` env override gerekir. Bu T78/T79/T80 paterniyle simetrik (Discord/Telegram/Email aynı şekilde).
- **Architectural karar — Skinora.Shared'da transport, Skinora.Fraud'da entity** — Pure transport (Settings/Client/Parser/Limiter/Exceptions/Quote) Skinora.Shared/SteamMarket altında (08 §7.5 büyüme yolu için reusable). Cache entity + orchestrator Skinora.Fraud'da çünkü tek tüketici fraud modülü ve ItemPriceCache fraud bağlamı (06 §1.1 entity envanteri "Fraud" grup).
- **CS4014 false positive** — `Expression<Action<PriceService>> call = s => s.RefreshAsync(name)` lambda body Task döndüren async method çağrısı içerir; compiler "not awaited" warning verir ama Expression tree runtime'da değil Hangfire'a serialize ediliyor. `#pragma warning disable CS4014` ile yerel suppress. TimeoutSchedulerStartupHook'ta `scheduler.Enqueue<IHeartbeatJob>(j => j.TickAsync())` benzer ama dönüş değeri `var heartbeatJobId = ...` discard edilmediği için compiler warning vermiyor. Mevcut warning sırası inconsistent; T81 lokal suppress en temiz.
