# T82 — Sanctions screening servisi

**Faz:** F4 | **Durum:** ⏳ Yapım bitti — bağımsız doğrulama bekliyor | **Tarih:** 2026-05-19

> **Bitiş Kapısı (task.md §"Bitiş Kapısı") — 8/8 ✓**
>
> 1. ✓ Branch push edildi (`task/T82-sanctions-screening`)
> 2. ✓ PR açıldı ([PR #125](https://github.com/turkerurganci/Skinora/pull/125))
> 3. ✓ PR numarası rapora yazıldı (bkz. "Commit & PR")
> 4. ✓ Rapor + status + memory commit edilip push edildi (`9b6c52e` + `1b027ae` + `df96739` + `ded3a96` + `3739cdd`)
> 5. ✓ CI run tamamlandı (`conclusion=success`)
> 6. ✓ CI run sonucu **success** — HEAD `3739cdd` run [`26087784183`](https://github.com/turkerurganci/Skinora/actions/runs/26087784183) **10/10 SUCCESS** (Detect+Lint+Build+Unit+Integration+Contract+Migration+Docker backend+CI Gate)
> 7. ✓ Branch izolasyon check temiz — `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+'` → yalnız `T82`
> 8. ✓ Repo memory'de T82 satırı eklendi ([`.claude/memory/MEMORY.md`](../../.claude/memory/MEMORY.md))

> **2× ci-failure remediation push:** İlk impl push `9b6c52e` Auth.Tests `SteamAuthenticationPipelineTests` ctor yeni `ISanctionsViolationHandler` arg eksikti (run [`26086942610`](https://github.com/turkerurganci/Skinora/actions/runs/26086942610) Build job fail) → `1b027ae` fix; ikinci push `1b027ae` Platform.Tests `AuditLogCategoryMapTests.ActionsInCategory_SECURITY_EVENT` ordering assertion T82'nin 2 yeni audit action'ını yansıtmıyordu (run [`26087230040`](https://github.com/turkerurganci/Skinora/actions/runs/26087230040) Unit test job fail) → `ded3a96` fix. Her ikisi de aynı kök neden — task `dotnet build src/Skinora.API` sonrası ayrı test proje build'i atlandı; tüm test projeleri `dotnet test --filter Category!=Integration` ile birlikte koşulmadığı için isabetsiz kaldı. 2× `[ci-failure]` BYPASS_LOG entry.

---

## Yapılan İşler

### Spec drift kapama (T82 ön-çalışması — PR #124)

T81 paterni izlenerek docs-only PR önce açıldı ([PR #124](https://github.com/turkerurganci/Skinora/pull/124) `7cd4a95` squash, 2026-05-19, CI 3/3 docs-only success):

- **06 §2.11** FraudFlagType: `SANCTIONS_MATCH` enum değeri.
- **06 §3.25** `SanctionedAddress` entity (yeni alt-bölüm) — Address (case-sensitive TRC-20 MVP), Network/Source CHECK constraint, Reason, ListedAt, AddedByAdminId (opsiyonel FK), IsActive soft-deactivate; filtered UQ `UQ_SanctionedAddresses_Address_Active` + soft deactivation + re-add semantiği.
- **06 §4.1** SanctionedAddress.AddedByAdminId FK satırı.
- **07 §9.11** availablePermissions: `MANAGE_SANCTIONS` (12. yetki, MANAGE_SETTINGS'ten ayrı least-privilege not).
- **07 §9.23–§9.25** AD22 (`GET /admin/sanctions/addresses` list), AD23 (`POST` add + retroaktif eşleşme cascade), AD24 (`DELETE` soft deactivate).
- **07 §0 traceability**: §11a.3 → AD19b/AD19c + AD22/AD23/AD24.
- **04 §8.8** yetki matrisi: "Sanctions listesi yönet" satırı.
- **02 §21.1** MANAGE_SANCTIONS referansı + MVP scope notu.

### Domain entity + EF config + migration (Skinora.Platform)

- **`SanctionedAddress` entity** (06 §3.25) — `BaseEntity` inherit + Address (string 64) + Network (string 20) + Source (string 20) + Reason (string 500 NULL) + ListedAt (datetime2 NN) + AddedByAdminId (Guid? FK→User) + IsActive (bit default true).
- **`SanctionedAddressNetworks`** + **`SanctionedAddressSources`** — string allowlist constants + `IsKnown(value)` helper'lar (CHECK constraint mirror).
- **`SanctionedAddressConfiguration`** — `CK_SanctionedAddresses_Network` (`'TRC-20'` MVP) + `CK_SanctionedAddresses_Source` (`'OFAC' | 'EU' | 'UN' | 'MANUAL'`) + FK→User NO ACTION + filtered UQ `UQ_SanctionedAddresses_Address_Active WHERE IsActive=1`. **Not:** İlk migration EF Core merge davranışı nedeniyle indeks adını `IX_SanctionedAddresses_Address` olarak üretti (06 §3.25 satır 2 "non-filtered IX" ek satırı redundant — query filter `WHERE Address=@ AND IsActive=1` filtered UQ ile birebir örtüştüğünden tek indeks yeterli); migration/snapshot manuel olarak `UQ_SanctionedAddresses_Address_Active` ismine düzeltildi, config tek HasIndex çağrısına indirildi.
- **Migration `20260519080131_T82_AddSanctionedAddresses`** — `CreateTable SanctionedAddresses` (10 sütun + PK + 2 CHECK + FK User) + `IX_SanctionedAddresses_AddedByAdminId` + filtered UQ `UQ_SanctionedAddresses_Address_Active`.

### Read port (Skinora.Shared/Sanctions/)

- **`ISanctionedAddressLookup`** + **`SanctionedAddressMatch` record** — `FindActiveAsync(string address, ct) → SanctionedAddressMatch?` (null = no match). Port Shared'de yaşar; Platform impl detayı `SanctionedAddressLookup` (AppDbContext + AsNoTracking + filtered UQ hit). Skinora.Shared'e yerleşmesinin nedeni: Skinora.Platform → Skinora.Users yön dependency'sini koruyup Users'ın da consume edebilmesi (Users → Platform ters yön circular dep yaratır).

### Sanctions check real impl + DI swap

- **`DbWalletSanctionsCheck`** (Skinora.Users.Application.Wallet) — `IWalletSanctionsCheck` impl. T34 `NoMatchWalletSanctionsCheck` stub'unun yerine geçer. Lookup → Match(source) / NoMatch.
- **`DbLoginSanctionsCheck`** (Skinora.Auth.Application.SteamAuthentication) — `ISanctionsCheck` impl. T29 `NoMatchSanctionsCheck` stub'unun yerine geçer. SteamId64 → User lookup (`IgnoreQueryFilters` — soft-deleted user'lar da yakalanır) → DefaultPayoutAddress + DefaultRefundAddress aktif sanctions listesine karşı kontrol. Yeni user (henüz provisioning olmamış) → no-match.
- **DI swap:** `UsersModule.cs` `AddSingleton<NoMatchWalletSanctionsCheck>` → `AddScoped<DbWalletSanctionsCheck>`; `SteamAuthenticationModule.cs` `AddSingleton<NoMatchSanctionsCheck>` → `AddScoped<DbLoginSanctionsCheck>`.

### Violation handler (port: Shared, impl: Fraud)

- **`ISanctionsViolationHandler`** (Skinora.Shared.Sanctions) — match path tetikleyicisi. 3 metod: `RecordWalletAttemptAsync(userId, attemptedAddress, matchedList, ct)` (wallet pipeline match), `RecordLoginAttemptAsync(steamId64, matchedList, ct)` (login pipeline match — handler kullanıcıyı SteamId üzerinden çözer; yoksa no-op), `RecordRetroactiveMatchAsync(userId, matchedAddress, matchedList, ct)` (admin AD23 yeni adres ekleme sonrası eşleşen kullanıcılar için).
- **`SanctionsViolationHandler`** (Skinora.Fraud.Application.Sanctions) — `IFraudFlagService.StageAccountFlagAsync(SANCTIONS_MATCH, cascadeEmergencyHold:true)` + `AppDbContext.SaveChangesAsync` çağrısı. **Idempotency:** User'da zaten PENDING account-level SANCTIONS_MATCH flag varsa skip — duplicate flag oluşturulmaz; emergency-hold cascade FraudFlagService içinde zaten `!t.IsOnHold` filtresi ile idempotent. `JsonSerializer.Serialize` ile FraudFlag.Details JSON envelope (`source`, `matchedList`, vb.).

### FraudFlagType + Audit + Permission catalog

- **`FraudFlagType.SANCTIONS_MATCH`** (Skinora.Shared.Enums) — 5. enum değeri.
- **`AuditAction.SANCTIONS_LIST_ADDRESS_ADDED` / `SANCTIONS_LIST_ADDRESS_REMOVED`** (Skinora.Shared.Enums) — admin AD23/AD24 aksiyonları.
- **`AuditLogCategoryMap`** — her iki yeni audit action SECURITY_EVENT kategorisinde (wallet-address-changed / reconciliation-mismatch ile aynı admin güvenlik kuyruğunda).
- **`PermissionCatalog.Keys.ManageSanctions = "MANAGE_SANCTIONS"`** + `All` listesinde 12. entry "Sanctions listesi yönet". `IsKnown` test'i geçer.

### Match path wiring

- **`WalletAddressService.UpdateWalletAsync`** — `_sanctions.EvaluateAsync(candidate)` `IsMatch=true` ise: `await _sanctionsViolation.RecordWalletAttemptAsync(userId, candidate, matchedList, ct)` (savunma kaydı + cascade) + `WalletUpdateResult.Failure(SanctionsMatch, matchedList)` döner. Aday adres kullanıcının `DefaultPayoutAddress` / `DefaultRefundAddress`'e **yazılmaz** (eski davranış korunur). Constructor'da yeni `ISanctionsViolationHandler` ctor injection.
- **`SteamAuthenticationPipeline.ExecuteAsync`** — `_sanctions.EvaluateAsync(steamId64)` `IsMatch=true` ise: `await _sanctionsViolation.RecordLoginAttemptAsync(steamId64, matchedList, ct)` (handler User'ı SteamId üzerinden çözer — yeni user no-op) + `AuthenticationOutcome.SanctionsMatch` döner. Constructor'da yeni `ISanctionsViolationHandler` ctor injection.

### Admin endpoints — AdminSanctionsService + AdminSanctionsController

- **`Skinora.API/Services/AdminSanctions/`** — cross-module orchestrator (T76 ReconciliationService paterni):
  - **`IAdminSanctionsService`** — `ListAsync(query, ct)`, `AddAsync(adminId, request, ip, ct)`, `DeactivateAsync(adminId, id, ip, ct)`.
  - **`AdminSanctionsService`** — AppDbContext + IAuditLogger + ITrc20AddressValidator + ISanctionsViolationHandler + TimeProvider deps. List: pagination (max 100), `network`/`source`/`search` (LIKE) filtreler, `isActive` default `true`, `sortBy=listedAt|address` `sortOrder=desc` default. Add: TRC-20 format validate + Network/Source allowlist check + reason ≤500 + pre-insert active duplicate check (defensive) + filtered UQ violation catch → 409 + retroaktif eşleşme scan (DefaultPayoutAddress OR DefaultRefundAddress = address) + SanctionsViolationHandler.RecordRetroactiveMatchAsync foreach. Deactivate: row lookup + `IsActive=false` + audit + SaveChanges. Audit: `SANCTIONS_LIST_ADDRESS_ADDED` JSON envelope `{address, network, source, reason}` + `SANCTIONS_LIST_ADDRESS_REMOVED` `{address, source}`.
  - **`AdminSanctionsErrorCodes`** — `VALIDATION_ERROR`, `INVALID_WALLET_ADDRESS`, `SANCTIONS_ADDRESS_ALREADY_LISTED`, `SANCTIONS_ADDRESS_NOT_FOUND`, `SANCTIONS_ADDRESS_ALREADY_INACTIVE`.
- **`AdminSanctionsController`** (`/api/v1/admin/sanctions/addresses`) — `[Authorize(Policy = "Permission:MANAGE_SANCTIONS")]` + `[RateLimit("admin-read" | "admin-write")]` per endpoint. AD22 GET (200 list), AD23 POST (201 Created + 400 validation + 400 invalid address + 409 duplicate), AD24 DELETE (200 deactivate + 404 not found + 409 already inactive).

### DI registration

- **`Skinora.Platform.PlatformModule.cs`** — `AddScoped<ISanctionedAddressLookup, SanctionedAddressLookup>()`.
- **`Skinora.Fraud.FraudModule.cs`** — `AddScoped<ISanctionsViolationHandler, SanctionsViolationHandler>()`.
- **`Skinora.API.Program.cs`** — `AddScoped<IAdminSanctionsService, AdminSanctionsService>()` (AddFraudModule registration sonrası, ISanctionsViolationHandler wired olmadan tüketim olmaması için).

### Test'ler

- **`AdminSanctionsEndpointTests`** (yeni — Skinora.API.Tests/Integration, 12 test PASS) — AD22/AD23/AD24 full path: Unauthenticated→401, NonAdmin→403, AdminWithoutPermission→403, SuperAdmin list active-only default, IsActiveFalse list inactive-only; Add Valid→201+persist, Add InvalidTrc20→400, Add Duplicate→409, Add Retroactive→eşleşen user için PENDING SANCTIONS_MATCH flag staged; Deactivate Active→200+IsActive=false (filtered UQ re-add allowed), Deactivate AlreadyInactive→409, Deactivate NotFound→404. Factory `EnsureDeleted + EnsureCreated` Reset paterni (T82 fraud cascade rows nedeniyle granular RemoveRange fragile).
- **`AdminRolesEndpointTests`** — `availablePermissions` array length 11→12 ve `MANAGE_SANCTIONS` `Contains` assertion eklendi.
- **`WalletAddressEndpointTests`** — Factory `Reset()` `EnsureDeleted + EnsureCreated`'a geçti; T82 wallet sanctions match path FraudFlag + emergency-hold cascade + AuditLog rows staging yaptığı için eski Transaction+User-only RemoveRange yetersiz kaldı. Mevcut `UpdateSellerWallet_SanctionsMatch_Returns403AndLeavesAddressUnchanged` test korundu — 403 + persisted address unchanged davranışı T82 ile aynı, ek olarak FraudFlag staged side-effect arka planda çalışıyor.
- **`Skinora.Shared.Tests.Unit.EnumTests`** — `FraudFlagType_ShouldHave4Values` → 5; `AuditAction_ShouldHave24Values` → 26; yeni Theory entries `SANCTIONS_MATCH`, `SANCTIONS_LIST_ADDRESS_ADDED`, `SANCTIONS_LIST_ADDRESS_REMOVED`.

## Etkilenen Modüller / Dosyalar

### Skinora.Shared (yeni Sanctions/)

- [`backend/src/Skinora.Shared/Sanctions/ISanctionedAddressLookup.cs`](../../backend/src/Skinora.Shared/Sanctions/ISanctionedAddressLookup.cs) (yeni)
- [`backend/src/Skinora.Shared/Sanctions/SanctionedAddressMatch.cs`](../../backend/src/Skinora.Shared/Sanctions/SanctionedAddressMatch.cs) (yeni)
- [`backend/src/Skinora.Shared/Sanctions/ISanctionsViolationHandler.cs`](../../backend/src/Skinora.Shared/Sanctions/ISanctionsViolationHandler.cs) (yeni)
- [`backend/src/Skinora.Shared/Enums/FraudFlagType.cs`](../../backend/src/Skinora.Shared/Enums/FraudFlagType.cs) — `SANCTIONS_MATCH`
- [`backend/src/Skinora.Shared/Enums/AuditAction.cs`](../../backend/src/Skinora.Shared/Enums/AuditAction.cs) — 2 yeni satır
- [`backend/src/Skinora.Shared/Persistence/Migrations/20260519080131_T82_AddSanctionedAddresses.cs`](../../backend/src/Skinora.Shared/Persistence/Migrations/20260519080131_T82_AddSanctionedAddresses.cs) (yeni)
- [`backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs`](../../backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs) — SanctionedAddresses table snapshot

### Skinora.Platform

- [`backend/src/Modules/Skinora.Platform/Domain/Entities/SanctionedAddress.cs`](../../backend/src/Modules/Skinora.Platform/Domain/Entities/SanctionedAddress.cs) (yeni)
- [`backend/src/Modules/Skinora.Platform/Domain/Entities/SanctionedAddressNetworks.cs`](../../backend/src/Modules/Skinora.Platform/Domain/Entities/SanctionedAddressNetworks.cs) (yeni)
- [`backend/src/Modules/Skinora.Platform/Domain/Entities/SanctionedAddressSources.cs`](../../backend/src/Modules/Skinora.Platform/Domain/Entities/SanctionedAddressSources.cs) (yeni)
- [`backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SanctionedAddressConfiguration.cs`](../../backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SanctionedAddressConfiguration.cs) (yeni)
- [`backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SanctionedAddressLookup.cs`](../../backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SanctionedAddressLookup.cs) (yeni)
- [`backend/src/Modules/Skinora.Platform/Application/Audit/AuditLogCategoryMap.cs`](../../backend/src/Modules/Skinora.Platform/Application/Audit/AuditLogCategoryMap.cs) — 2 yeni AuditAction → SecurityEvent map
- [`backend/src/Modules/Skinora.Platform/PlatformModule.cs`](../../backend/src/Modules/Skinora.Platform/PlatformModule.cs) — `AddScoped<ISanctionedAddressLookup, SanctionedAddressLookup>()`

### Skinora.Users

- [`backend/src/Modules/Skinora.Users/Application/Wallet/DbWalletSanctionsCheck.cs`](../../backend/src/Modules/Skinora.Users/Application/Wallet/DbWalletSanctionsCheck.cs) (yeni)
- [`backend/src/Modules/Skinora.Users/Application/Wallet/WalletAddressService.cs`](../../backend/src/Modules/Skinora.Users/Application/Wallet/WalletAddressService.cs) — ISanctionsViolationHandler ctor + match path `RecordWalletAttemptAsync` çağrısı

### Skinora.Auth

- [`backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/DbLoginSanctionsCheck.cs`](../../backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/DbLoginSanctionsCheck.cs) (yeni)
- [`backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/SteamAuthenticationPipeline.cs`](../../backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/SteamAuthenticationPipeline.cs) — ISanctionsViolationHandler ctor + match path `RecordLoginAttemptAsync` çağrısı

### Skinora.Admin

- [`backend/src/Modules/Skinora.Admin/Application/Permissions/PermissionCatalog.cs`](../../backend/src/Modules/Skinora.Admin/Application/Permissions/PermissionCatalog.cs) — Keys.ManageSanctions + All 12. entry

### Skinora.Fraud

- [`backend/src/Modules/Skinora.Fraud/Application/Sanctions/SanctionsViolationHandler.cs`](../../backend/src/Modules/Skinora.Fraud/Application/Sanctions/SanctionsViolationHandler.cs) (yeni)
- [`backend/src/Modules/Skinora.Fraud/FraudModule.cs`](../../backend/src/Modules/Skinora.Fraud/FraudModule.cs) — `AddScoped<ISanctionsViolationHandler, SanctionsViolationHandler>()`

### Skinora.API

- [`backend/src/Skinora.API/Services/AdminSanctions/IAdminSanctionsService.cs`](../../backend/src/Skinora.API/Services/AdminSanctions/IAdminSanctionsService.cs) (yeni)
- [`backend/src/Skinora.API/Services/AdminSanctions/AdminSanctionsService.cs`](../../backend/src/Skinora.API/Services/AdminSanctions/AdminSanctionsService.cs) (yeni)
- [`backend/src/Skinora.API/Services/AdminSanctions/AdminSanctionsDtos.cs`](../../backend/src/Skinora.API/Services/AdminSanctions/AdminSanctionsDtos.cs) (yeni)
- [`backend/src/Skinora.API/Services/AdminSanctions/AdminSanctionsErrorCodes.cs`](../../backend/src/Skinora.API/Services/AdminSanctions/AdminSanctionsErrorCodes.cs) (yeni)
- [`backend/src/Skinora.API/Controllers/AdminSanctionsController.cs`](../../backend/src/Skinora.API/Controllers/AdminSanctionsController.cs) (yeni)
- [`backend/src/Skinora.API/Configuration/UsersModule.cs`](../../backend/src/Skinora.API/Configuration/UsersModule.cs) — IWalletSanctionsCheck → DbWalletSanctionsCheck DI swap
- [`backend/src/Skinora.API/Configuration/SteamAuthenticationModule.cs`](../../backend/src/Skinora.API/Configuration/SteamAuthenticationModule.cs) — ISanctionsCheck → DbLoginSanctionsCheck DI swap
- [`backend/src/Skinora.API/Program.cs`](../../backend/src/Skinora.API/Program.cs) — AddScoped<IAdminSanctionsService, AdminSanctionsService>

### Test'ler

- [`backend/tests/Skinora.API.Tests/Integration/AdminSanctionsEndpointTests.cs`](../../backend/tests/Skinora.API.Tests/Integration/AdminSanctionsEndpointTests.cs) (yeni — 12 test)
- [`backend/tests/Skinora.API.Tests/Integration/AdminRolesEndpointTests.cs`](../../backend/tests/Skinora.API.Tests/Integration/AdminRolesEndpointTests.cs) — availablePermissions 11→12 + MANAGE_SANCTIONS contains
- [`backend/tests/Skinora.API.Tests/Integration/WalletAddressEndpointTests.cs`](../../backend/tests/Skinora.API.Tests/Integration/WalletAddressEndpointTests.cs) — Factory.Reset → `EnsureDeleted + EnsureCreated`
- [`backend/tests/Skinora.Shared.Tests/Unit/EnumTests.cs`](../../backend/tests/Skinora.Shared.Tests/Unit/EnumTests.cs) — FraudFlagType 5 + AuditAction 26 + 3 yeni Theory satırı

## Kabul Kriterleri Kontrolü

Plan kabul kriterleri ([`Docs/11_IMPLEMENTATION_PLAN.md`](../../Docs/11_IMPLEMENTATION_PLAN.md) Task T82, 02 §21.1, 03 §11a.3):

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Cüzdan adresi yaptırımlı adres listesiyle karşılaştırma | ✓ Karşılandı | `SanctionedAddress` entity + `ISanctionedAddressLookup.FindActiveAsync` filtered UQ lookup; `DbWalletSanctionsCheck` ve `DbLoginSanctionsCheck` real impl'leri stub'ların yerini aldı |
| 2 | Eşleşme: yeni işlem/adres kaydı engellenir, hesap flag'lenir | ✓ Karşılandı | `WalletAddressService.UpdateWalletAsync` match path: candidate adres save edilmez (`SanctionsMatch` failure 403 SANCTIONS_MATCH); `SanctionsViolationHandler.RecordWalletAttemptAsync` → `StageAccountFlagAsync(SANCTIONS_MATCH)` + `SaveChangesAsync`. `AdminSanctionsEndpointTests.AddAddress_RetroactiveScan_FlagsExistingUserWithSanctionedWallet` retroaktif eşleşme için aynı yolu test eder |
| 3 | Yüksek risk: aktif işlemlere otomatik EMERGENCY_HOLD | ✓ Karşılandı | `StageAccountFlagAsync(cascadeEmergencyHold:true, emergencyHoldReason:"Sanctions match ...")` → `FraudFlagService.ApplyEmergencyHoldCascadeAsync` mevcut aktif tx'lere `IsOnHold=true` + `TimeoutFreezeReason=EMERGENCY_HOLD` (06 §3.5) + `EmergencyHoldAppliedEvent` outbox. Cascade `!t.IsOnHold` filtresi idempotent |
| 4 | Tarama listesi admin tarafından güncellenebilir | ✓ Karşılandı | AD22 (GET list) + AD23 (POST add) + AD24 (DELETE soft deactivate) `AdminSanctionsController` + `MANAGE_SANCTIONS` permission. 12 integration test (`AdminSanctionsEndpointTests`) full path coverage |
| 5 | Merkezi doğrulama pipeline'ın parçası | ✓ Karşılandı | `WalletAddressService` (T34 pipeline — profil §9.1, işlem başlatma §2.2, işlem kabul §3.2, adres değiştirme §9.2 dört giriş noktası aynı service'ten geçer) ve `SteamAuthenticationPipeline` (Steam login pipeline) `IWalletSanctionsCheck` / `ISanctionsCheck` üzerinden aynı `ISanctionedAddressLookup` portunu çağırır — tek kaynak |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit — Shared | ✓ 363/363 PASS | `dotnet test Skinora.Shared.Tests --filter Category!=Integration` Release 10s; `EnumTests.FraudFlagType_ShouldHave5Values` + `EnumTests.AuditAction_ShouldHave26Values` + 3 yeni Theory satırı geçer |
| Integration — API | ✓ 415/415 PASS | `dotnet test Skinora.API.Tests --filter Category!=Integration` Release 3:40; yeni 12 `AdminSanctionsEndpointTests` + 22 `WalletAddressEndpointTests` (Reset refactor sonrası) + `AdminRolesEndpointTests` 11→12 permission güncellemesi |
| Unit — Transactions | ✓ 641/641 PASS | regresyon — `dotnet test Skinora.Transactions.Tests --filter Category!=Integration` Release 1:34 |
| Unit — Fraud | ✓ 62/62 PASS | regresyon — `dotnet test Skinora.Fraud.Tests --filter Category!=Integration` Release 26s |
| Unit — Notifications | ✓ 86/86 PASS | regresyon — Release 15s |
| Unit — Platform | ✓ 113/113 PASS | regresyon — `AuditLogCategoryMap` 2 yeni satır SECURITY_EVENT haritası dahil |
| Build Release | ✓ 0W/0E | `dotnet build src/Skinora.API/Skinora.API.csproj -c Release` 0 warning / 0 error |
| dotnet format | ✓ Δ=0 | `dotnet format Skinora.sln --verify-no-changes` exit 0 |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validator chat'i bekleniyor |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- **Migration:** Var — `20260519080131_T82_AddSanctionedAddresses` (yeni `SanctionedAddresses` tablosu, 2 CHECK constraint, FK→User, filtered UQ, IX AddedByAdminId).
- **Config/env değişikliği:** Yok (SystemSetting eklenmesi gerekmiyor — admin AD23 ile run-time list yönetir).
- **Docker değişikliği:** Yok.
- **Yeni dış dep:** Yok.

## Commit & PR

- Branch: `task/T82-sanctions-screening`
- Docs PR: [PR #124](https://github.com/turkerurganci/Skinora/pull/124) (`7cd4a95` squash, CI ✓ `26083956756` 3/3 docs-only)
- Impl PR: [PR #125](https://github.com/turkerurganci/Skinora/pull/125) (MERGEABLE)
- Commits:
  - `9b6c52e` — T82 ana impl
  - `1b027ae` — Auth.Tests `SteamAuthenticationPipelineTests` ctor fix (CI failure remediation)
  - `df96739` — BYPASS_LOG entry (Auth fix)
  - `ded3a96` — Platform.Tests `AuditLogCategoryMapTests` SECURITY_EVENT ordering fix (CI failure remediation)
  - `3739cdd` — BYPASS_LOG entry (Platform.Tests fix) — HEAD
- CI: ✓ HEAD `3739cdd` run [`26087784183`](https://github.com/turkerurganci/Skinora/actions/runs/26087784183) **10/10 SUCCESS**

## Known Limitations / Follow-up

- **K1 — OFAC SDN / EU / UN feed auto-sync post-MVP:** MVP'de yalnız `Source = 'MANUAL'` admin entry; `'OFAC' / 'EU' / 'UN'` değerleri reserved. Hangfire daily job + JSON feed parser post-MVP genişleme (06 §3.25 not).
- **K2 — Multi-network genişleme:** `CK_SanctionedAddresses_Network` MVP'de sabit `'TRC-20'`. ERC-20 / BTC eklenince CHECK constraint + `SanctionedAddressNetworks.All` genişler.
- **K3 — Wallet pipeline match → cancel non-active tx:** EMERGENCY_HOLD cascade yalnız aktif tx'leri kapsar (`ApplyEmergencyHoldCascadeAsync` !COMPLETED && !CANCELLED_*). Geçmiş tamamlanmış işlemler audit trail amaçlı dokunulmaz — 06 §3.5 matrix gereği.
- **K4 — Fraud Flag dedup window:** Idempotency PENDING flag varlığına bakar. Admin onaylayıp Approved/Rejected statusa geçirdikten sonra yeni bir match yeni flag oluşturur — istenen davranış (admin re-incelemeli).
- **K5 — `Reason` UI surface:** AD22 response `reason` döner ama admin UI (T-future) gösterimi T100+ S-screen task'ında entegre edilecek.
- **K6 — Test fixture `EnsureDeleted+EnsureCreated`:** WalletAddressEndpointTests ve AdminSanctionsEndpointTests Factory.Reset full schema rebuild paterni — T82 cascade rows nedeniyle granular RemoveRange'in karmaşıklığından kaçınmak için seçildi. Performance penalty ~50ms per test; alternatif TestContainers SQL Server T-future.
- **K7 — Login pipeline check before provisioning:** `SteamAuthenticationPipeline` sanctions check provisioning'den önce çalışır. Yeni user için User satırı yok → DbLoginSanctionsCheck no-match. İlk login sonrası wallet kaydederken `DbWalletSanctionsCheck` eşleşmeyi yakalar. Edge case: yeni user provisioning sırasında admin retroaktif scan ile başka kullanıcının adresini liste eklerse — bu user bu login pipeline'da match olmaz (henüz wallet yok); next login'de yakalanır (wallet artık saved).
- **K8 — Filtered UQ EF Core merge:** İlk migration generate sırasında `HasIndex(Address).IsUnique().HasFilter` + `HasIndex(Address)` iki çağrı EF tarafından merge edildi (aynı sütun) ve hatalı isim üretti. Config tek HasIndex'e indirildi + migration ismi manuel düzeltildi. Documented karar.

## Notlar

- **Working tree hygiene:** Task başlangıcında temiz (T81 PR #123 ve docs PR #124 sırayla merge edildi).
- **Main CI startup check:** 2026-05-19 task başında main CI 3/3 success (T81 + docs(06): T81 ön-çalışması).
- **Dış varsayım kontrolü:** "MVP'de yalnız admin-managed manuel liste" doğrulandı — 02 §21.1 + 03 §11a.3 spec'leri OFAC feed sync gerektirmiyor, "admin tarafından güncellenebilir" kuralı manual entry ile karşılanıyor. Hiçbir paid API / external feed dependency yok.
- **Architectural karar — port placement:** `ISanctionedAddressLookup` + `ISanctionsViolationHandler` Skinora.Shared/Sanctions/ altında. Sebebi: Skinora.Platform → Skinora.Users ve Skinora.Fraud → Skinora.Users mevcut dep yönü; Users → Platform/Fraud ters yön circular dep yaratırdı. Port-impl separation ile dep grafı korundu (Users + Auth → Shared, Platform/Fraud impl → Shared port).
- **Architectural karar — cross-module admin service:** `AdminSanctionsService` `Skinora.API/Services/AdminSanctions/` altında. Sebebi: SanctionedAddress (Platform entity) + retroaktif scan (Fraud `ISanctionsViolationHandler`) + AppDbContext User query'leri tek service'te orchestrate edilir; tek modül owner'lığı zorlamak fazla constraint olurdu (T76 `ReconciliationService` paterni).
- **Memory yansıtması:** Bu task tamamlanınca `.claude/memory/MEMORY.md` "Current Status" bloğunda T82 satırı eklenecek (Bitiş Kapısı kuralı).
