# WP4a — Fraud accept-gate + canlı fiyat (wiring)

| Alan | Değer |
|---|---|
| **İş paketi** | WP4a (PRE_F6_PLAN — P2 Fraud/uyum) |
| **Branch** | `task/WP4a-fraud-gate-price-wiring` |
| **PR** | #172 |
| **Tarih** | 2026-06-16 |
| **Durum** | Yapım bitti — bağımsız doğrulama bekliyor |
| **Açtığı** | T111 (E2E — Fraud/flag senaryoları) |
| **Tamamladığı yetenek** | Flag'li hesap accept'te engellenir, PRICE_DEVIATION kuralı canlı çalışır |

---

## 1. Özet

WP4a, plana göre **iki bağımsız değişiklik**tir (spec birbirine karıştırmayı yasaklar — 02 §14.0 hesap-flag vs §14.4 işlem-flag):

- **Part A — Accept-path account-flag GATE.** `IAccountFlagChecker` bugüne dek yalnız create yolunda satıcıyı sertçe gate'liyordu; accept yolunda ([`TransactionAcceptanceService.AcceptAsync`](../../backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionAcceptanceService.cs)) hiç çağrılmıyordu → flag'li alıcı işlem kabul edebiliyordu. 02 §14.0:320 "işlem kabul etme" engelini normatif kılar. **Saf wiring** — kontrat (`HasActiveAccountFlagAsync(userId)`) yeterli, DI'da zaten kayıtlı.
- **Part B — PRICE_DEVIATION wiring.** `NullMarketPriceProvider` her zaman `null` döndürüyordu → kural asla tetiklenmiyordu; `MarketPriceAtCreation` hep null. Mevcut T81 Steam Market stack'i (gerçek, 0 prod çağıranı) ince bir köprüyle bağladım — **T81 yeniden yazılmadı**. Kural yalnız **CREATE** anında çalışır (spec: accept'te asla).

## 2. Owner kararları (AskUserQuestion — bu chat)

| Karar | Seçim | Gerekçe |
|---|---|---|
| Part A gate kapsamı | **Yalnız alıcı** | 02 §14.0 per-aktör ("flag'li kullanıcının kendi fon-akışı"); "aktif işlemler devam eder" → temiz alıcıyı sonradan-flag'li satıcı bloklamaz |
| Part B marketHashName | **Capture-at-creation** | Değer sidecar DTO'da zaten var, reader'da düşürülüyordu; create-anı yeterli (kural create-only); migration yok |
| Part B threshold | **Validator'ı düzelt + %100 (1.0) seed** | 08 §7.3 ≥%100 öneriyor ama validator `0<x<1` dayatıyordu — pre-existing kod defekti; deviation ratio'su meşruen 1'i aşar |
| Part A HTTP/kod (önerimle) | 403 + mevcut `ACCOUNT_FLAGGED` | 403-arm (NotAParty/SanctionsMatch) ile tutarlı; kod zaten mevcut |
| Part B denominasyon (önerimle) | USD↔stablecoin 1:1, denomination yok sayılır | T81 tek-para (USD); geniş eşik mikro-varyansı yutar (08 §7.3) |

## 3. Yapılan işler

### Part A — Accept-gate (Skinora.Transactions)
- `TransactionAcceptanceService`: ctor'a `IAccountFlagChecker` inject + **buyer-load'dan hemen sonra, state guard'dan ve her mutasyondan önce** gate: `HasActiveAccountFlagAsync(buyerId)` → `Failure(AccountFlagged, ACCOUNT_FLAGGED)`. Fail-fast → reddedilen accept **sıfır DB yazımı/outbox** yapar.
- `AcceptTransactionStatus` enum'a `AccountFlagged` üyesi; `TransactionsController` accept switch'inde mevcut 403-arm'a eklendi (500 default'a düşmez).
- `ACCOUNT_FLAGGED` error kodu zaten mevcuttu (yeni sabit yok).

### Part B — Price wiring (capture-at-creation + köprü)
- `InventoryItemSnapshot`'a **`MarketHashName`** (required) eklendi; `SidecarSteamInventoryReader` `item.MarketHashName`'i map'liyor (önceden düşürülüyordu). Persist edilmez — create-anı fraud pre-check'e geçer (kural create-only).
- `IMarketPriceProvider.TryGetMarketPriceAsync` + `IFraudPreCheckService.EvaluateAsync` imzaları `(classId, instanceId)` → `marketHashName` olarak değişti (classId/instanceId yalnız fiyat-lookup içindi; persist ayrı yoldan). `TransactionCreationService` `inventoryItem.MarketHashName` geçirir.
- **Yeni köprü `PriceServiceMarketPriceProvider`** (Skinora.Fraud) — `IMarketPriceProvider` → `IPriceService.GetMarketPriceAsync` delege eder; boş/whitespace anahtarı stack'e gitmeden `null` döner (fail-open); `IAccountFlagChecker` emsali gibi Fraud'da (cycle yok).
- DI: `TransactionsModule` `NullMarketPriceProvider` `TryAddScoped`'unu **explicit `AddScoped<IMarketPriceProvider, PriceServiceMarketPriceProvider>()`** ile değiştirdi (TryAdd "Null kazanır" sessiz-inert tuzağını engeller).

### Part B — Threshold + validator (owner: Validator düzelt + %100)
- `SystemSettingsValidator`: `price_deviation_threshold` `IsRatioKey` (0<x<1) setinden çıkarıldı → açık `>0` branch (1.0/1.5/2.82 geçerli; 0/negatif reddedilir). `min_refund_threshold_ratio` emsali.
- Seed: row 18 `Unconfigured` → **`Default(… "1.0" …)`** (oran; 1.0=%100, 08 §7.3 geniş-eşik).
- Migration `20260616153550_WP4a_SeedPriceDeviationThreshold` — saf `UpdateData` (Id `…0012`=`IdFor(18)`); şema değişikliği yok; Down → Unconfigured(null,false).

### Docs
- `06 §3.17`: validation-rules listesine `price_deviation_threshold: >0` + tablo default `—`→`1.0`.
- `07 §7.6`: accept hata kataloğuna `403 ACCOUNT_FLAGGED` (+ pre-existing eksik `403 NOT_A_PARTY`).
- `PRE_F6_PLAN`: migration-taşıyan-paketler notuna WP4a eklendi (owner kararı).

## 4. Etkilenen dosyalar

**Üretim (Skinora.Transactions):** `TransactionAcceptanceService.cs`, `TransactionLifecycleDtos.cs`, `IFraudPreCheckService.cs`, `FraudPreCheckService.cs`, `TransactionCreationService.cs`, `Pricing/IMarketPriceProvider.cs`, `Pricing/NullMarketPriceProvider.cs`, `Steam/ISteamInventoryReader.cs` · **Skinora.Steam:** `SidecarSteamInventoryReader.cs` · **Skinora.Fraud:** `Application/Pricing/PriceServiceMarketPriceProvider.cs` (yeni) · **Skinora.Platform:** `SystemSettingsValidator.cs`, `SystemSettingSeed.cs` · **Skinora.API:** `Configuration/TransactionsModule.cs`, `Controllers/TransactionsController.cs` · **Migration:** `20260616153550_WP4a_SeedPriceDeviationThreshold.cs` (+Designer + snapshot).

**Test:** `TransactionAcceptanceServiceTests.cs` (+stub +flagged-buyer testi), `TestSetupHelpers.cs`, `TransactionCreationServiceTests.cs`, `TransactionLifecycleEndpointTests.cs`, `DisputeServiceTests.cs` (InventoryItemSnapshot site'ları), `SystemSettingsValidatorTests.cs`, `SettingsBootstrapTests.cs`, `SeedDataTests.cs`, `PriceServiceMarketPriceProviderTests.cs` (yeni).

**Docs:** `06_DATA_MODEL.md`, `07_API_DESIGN.md`, `PRE_F6_PLAN.md`.

## 5. Kabul kriterleri (self-check)

| # | Kriter | Sonuç |
|---|---|---|
| AC1 | Flag'li alıcı accept'te 403 `ACCOUNT_FLAGGED` ile engellenir; tx `CREATED` kalır, BuyerId null, outbox boş | ✓ (`Flagged_Buyer_Cannot_Accept_Returns_AccountFlagged`) |
| AC2 | Temiz alıcı kabul edebilir; gate buyer-only (satıcı-flag temiz alıcıyı bloklamaz) | ✓ (14 mevcut accept testi yeşil; `AccountFlagChecker` yalnız `buyerId`) |
| AC3 | Üretim `IMarketPriceProvider` = köprü (NullProvider değil); köprü Fraud'da, Transactions→Fraud cycle yok | ✓ (tek explicit `AddScoped`; csproj tek-yön) |
| AC4 | `marketHashName` capture-at-creation ile uçtan uca; canonical anahtar (ItemName değil) | ✓ (sidecar DTO→snapshot→creation→fraud→bridge; köprü unit testi anahtarı verbatim doğrular) |
| AC5 | PRICE_DEVIATION canlı fiyatla çalışır; fail-open korunur (null fiyat→flag yok, creation bloklanmaz) | ✓ (`CalculatePriceDeviation` null→no-flag; köprü boş-anahtar→null; SteamMarketException→null) |
| AC6 | `price_deviation_threshold` validator ≥1'e izin verir (>0); seed default 1.0 | ✓ (`ValidateSingle_PriceDeviationThreshold_AllowsAboveOne`) |
| AC7 | Migration temiz (UpdateData, şema yok, Down revert); model-drift yok; docs hizalı | ✓ (`has-pending-model-changes` → drift yok) |
| AC8 | Spec-conformance — PRICE_DEVIATION yalnız creation'da, accept'te değil; deviation decimal | ✓ (accept yolunda fiyat çağrısı yok; math decimal) |

## 6. Test sonuçları

**Lokal (Docker yok → entegrasyon CI-authoritative):**
- `dotnet build Skinora.sln -c Debug` → **0 Warning / 0 Error**
- `SystemSettingsValidatorTests` + `SystemSettingsCatalogTests` → **75/75 PASS** (validator değişimi dahil)
- `PriceServiceMarketPriceProviderTests` (köprü) → **4/4 PASS** (boş-anahtar guard + delegation + fail-open)
- `dotnet ef migrations has-pending-model-changes` → **"No changes … since the last migration"** (drift yok)
- `dotnet format Skinora.sln --verify-no-changes` → **temiz**

**CI-authoritative entegrasyon:** `TransactionAcceptanceServiceTests`, `TransactionCreationServiceTests`, `SeedDataTests`, `SettingsBootstrapTests`, `DisputeServiceTests`, `TransactionLifecycleEndpointTests` → CI (Integration + Migration dry-run job'ları).

**Task CI HEAD `63b8a6b` run [`27639903294`](https://github.com/turkerurganci/Skinora/actions/runs/27639903294) — TÜM JOB SUCCESS** (Lint / Build / **Unit** / **Integration** / Contract / **Migration dry-run** / Docker / Gate). Integration yeşil → S1 fix (`SeedDataTests` 21→20) + accept-gate/creation entegrasyonu + seed `UpdateData` migration SQL Server'da temiz uygulandı.

## 7. Migration / altyapı

- `20260616153550_WP4a_SeedPriceDeviationThreshold` — **seed-only** (`UpdateData` Id `…0012`, şema değişikliği YOK). Up: Value=`1.0`/IsConfigured=true; Down: null/false. Seed key COUNT değişmez (1-for-1 swap, toplam 59).
- **Yan-etki (owner kararının kasıtlı sonucu):** `price_deviation_threshold` artık seed-default → deploy-zorunlu (fail-fast) ayar setinden çıktı (21→20 unconfigured). Bootstrap fail-fast `IsConfigured`-temelli dinamik türetir (kod değişmedi); `SettingsBootstrapTests` + `SeedDataTests` sayım/comment'leri propagate edildi.
- Yeni dış bağımlılık YOK.

## 8. Mini güvenlik kontrolü

- **Secret:** yok. **Auth/authorization:** Part A accept-gate'i **güçlendirir** (flag'li hesap fon-akışı engellenir); gate backend-enforced. **Input validation:** validator `price_deviation_threshold` için `>0` korur (geçersiz değer kaydedilemez). **Yeni dep:** yok.

## 9. Yapım-içi adversarial review (6-boyut/refute-default + bağımsız verify)

İki workflow turu (transient API hatası nedeniyle 2 boyut yeniden çalıştırıldı). **Verdict: PASS** — 4 boyut (accept-gate / price-wiring / threshold-validator / docs-migration) **0 bloke-edici**, kapsamlı refutation; money-spec **PASS** (5 NOTE hepsi refuted: creation-only, fail-open, exception-escape yok, decimal, FLAGGED-write atomik).

**Onaylı 1 bulgu (S1) — düzeltildi:**
- **`SeedDataTests.cs` seed-flip için güncellenmemişti** (`Assert.Equal(21, unconfigured.Count)` + hard-coded 38-key configured listesi `price_deviation_threshold` içermiyordu). `SettingsBootstrapTests`'i propagate etmiştim ama bu paralel sayımları kaçırmıştım (Docker olmadığı için lokal yakalanamadı). **Düzeltildi:** 21→20, configured listesine `price_deviation_threshold` sıralı pozisyonda eklendi, comment 38→39/21→20.

**2 NOTE (non-blocking):**
- 07 §7.6 accept hata kataloğu yeni 403'ü listelemiyordu → **bu PR'da düzeltildi** (`ACCOUNT_FLAGGED` + `NOT_A_PARTY`).
- FE i18n `transactionDetail.accept.errors.ACCOUNT_FLAGGED` yok → graceful generic fallback (`AcceptForm.tsx` `t.has` guard, crash yok); WP4a salt-backend → **FE follow-up** (WP11/WP13).

## 10. Known limitations

- **FE i18n:** flag'li-accept için özel mesaj yok (generic fallback) → WP11/WP13.
- **Operasyonel aktivasyon:** committed `SteamMarket:Provider=logging` (CI deterministik) → kural canlı tetiklenmesi için prod'da `Provider=steam-market` gerekir; `price_deviation_threshold` artık 1.0 seed'li (admin S17'den ayarlanabilir). Runtime/staging doğrulaması T111 E2E.
- **Variant market_hash_name eşleşmesi** (StatTrak/Souvenir/Doppler) yalnız happy-path'te kanıtlı → T111/staging doğrular (statik teyit edilemez).
- **06 §3.17 "58 anahtar" metni** aslında 59 (WP1'in eklediği key 59) — **pre-existing drift** (WP4a regresyonu değil; count'u değiştirmedim).

## 11. Notlar (audit trail)

- **Adım -1 (working tree):** temiz (WP3 merge sonrası).
- **Adım 0 (main son-3 CI):** hepsi `success` — `27577316206` / `27577316202` / `27567370074`.
- **Dış varsayımlar (ön-uçuş):** Modül yön Fraud→Transactions tek-yön (csproj okundu) ✓ · marketHashName create-anında DTO'da mevcut, snapshot sınırında düşürülüyordu ✓ · `SteamMarket:Provider` default `logging` (operasyonel ön-koşul) ✓ · `IAccountFlagChecker` kontratı yeterli, DI'da kayıtlı ✓ · **Runtime'da doğrulanacak (statik teyit edilemez):** canlı Steam priceoverview gerçek bir CS2 item için kullanılır fiyat döner mi + variant anahtar eşleşmesi → T111/staging.
- **Owner kararı sapması:** Plan WP4a'yı migration-taşımaz sayıyordu; owner threshold-seed kararı seed `UpdateData` migration ekledi (PRE_F6_PLAN güncellendi).

## 12. Commit & PR

- **Branch:** `task/WP4a-fraud-gate-price-wiring`
- **PR:** #172
- **Migration:** var (`WP4a_SeedPriceDeviationThreshold`, seed-only)
- **memory:** WP4a satırı `.claude/memory/MEMORY.md`'ye yansıtıldı
