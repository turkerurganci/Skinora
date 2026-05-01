# T40 — Admin RBAC (policy-based authorization)

**Faz:** F2 | **Durum:** ⏳ Devam ediyor (yapım bitti, validate chat'i bekleniyor) | **Tarih:** 2026-05-01

---

## Yapılan İşler

- **`IAdminAuthorityResolver` + `AdminAuthorityResolver`:** `Skinora.Auth/Application/SteamAuthentication/`. JWT issuance sırasında bir userId verildiğinde `AdminUserRole → AdminRole → AdminRolePermission` join'ini koşar ve `AdminAuthority(Role, Permissions)` döner. Üç dal: super-admin (`role=super_admin`, `permissions=[]` — handler bypass'i nedeniyle claim'ler gereksiz), regular admin (`role=admin`, sıralı permission listesi), atanmamış user (`role=user`, `permissions=[]`). Soft-delete query filter'ları (T24 entity config) tombstone'lanmış assignment'leri otomatik gizler.

- **`AccessTokenGenerator` async geçiş + permission claim emit:** `IAccessTokenGenerator.Generate(User)` → `GenerateAsync(User, CancellationToken)`. Yeni imza authority resolver ile DB lookup yapar, sonra her `permission` için bir `Claim(AuthClaimTypes.Permission, key)` ekler. Super-admin için claim eklenmez (handler short-circuit'a güvenir). `claims.Add` döngüsü, `AuthClaimTypes.Permission` zaten `permission` (T06'da sabit).

- **Caller adaptasyonu:** `SteamAuthenticationPipeline.ExecuteAsync` (login akışı) ve `RefreshTokenService.RotateAsync` (refresh akışı) artık `await GenerateAsync(user, cancellationToken)` çağırır. Refresh path'i kritik: kullanıcı admin permission değişikliğinden sonraki ilk refresh'te yeni claim setine geçer (≤ 15 dk default access token TTL).

- **403 INSUFFICIENT_PERMISSION envelope:** `AuthModule.AddAuthModule` içindeki `AddJwtBearer` config'ine `JwtBearerEvents.OnForbidden` handler'ı eklendi. Authenticated ama policy gereksinimini karşılamayan istek artık 403 + `ApiResponse<object>.Fail("INSUFFICIENT_PERMISSION", "You do not have permission to access this resource.", traceId)` envelope'u döner — boş gövde yerine 07 §2.4 standart hata sözleşmesi. 401 (kimliksiz) davranışı dokunulmadı (07 §2.5'te 401 için error code spec'i yok).

- **DI kaydı:** `SteamAuthenticationModule.AddSteamAuthenticationModule` içine `services.AddScoped<IAdminAuthorityResolver, AdminAuthorityResolver>()` (AccessTokenGenerator kaydından bir satır önce — sırası önemli değil ama okunabilirlik için).

- **`Skinora.Auth.csproj` proje referansı:** Resolver'ın Admin module entity'lerine erişebilmesi için `Skinora.Admin.csproj`'a project reference eklendi. Skinora.Admin halen yalnız Shared+Users'a referans veriyor; cycle yok. Gerekçe: Resolver kavramsal olarak Auth-domain (kullanıcının role+permission'ı), entity'ler ise Admin module'ün storage'ı.

- **Mevcut endpoint'ler tetiklenmedi:** T39'un `AdminController` (8 endpoint, AD11–AD18) zaten `[Authorize(Policy = AuthPolicies.PermissionPrefix + "MANAGE_ROLES")]` / `Permission:VIEW_USERS` ile dekore edilmişti. T40 yalnız claim üreticisini doğru besleyerek bu policy'lerin **non-super-admin admin'ler için de** çalışmasını sağlar. Yeni `[Authorize]` attribute'u eklenmedi, çünkü 07 §9'un diğer endpoint'leri henüz tanımlanmadı (T41 settings, T42 audit log, T63 transaction admin actions, T103 steam recovery).

## Etkilenen Modüller / Dosyalar

**Yeni — `backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/`:**
- `IAdminAuthorityResolver.cs` — interface + `AdminAuthority(string Role, IReadOnlyList<string> Permissions)` record.
- `AdminAuthorityResolver.cs` — EF Core impl (3 case: super → admin → user).

**Değişiklik — `backend/src/Modules/Skinora.Auth/Application/SteamAuthentication/`:**
- `IAccessTokenGenerator.cs` — `Generate` → `GenerateAsync(User, CancellationToken)`.
- `AccessTokenGenerator.cs` — ctor'da `IAdminAuthorityResolver` parametresi, `GenerateAsync` içinde authority resolve + permission claim emisyonu.
- `SteamAuthenticationPipeline.cs` — login sonunda `await GenerateAsync(...)`.

**Değişiklik — `backend/src/Modules/Skinora.Auth/Application/Session/`:**
- `RefreshTokenService.cs` — rotate sonunda `await GenerateAsync(...)`.

**Değişiklik — `backend/src/Modules/Skinora.Auth/Skinora.Auth.csproj`:**
- `<ProjectReference Include="..\Skinora.Admin\Skinora.Admin.csproj" />` eklendi.

**Değişiklik — `backend/src/Skinora.API/Configuration/`:**
- `AuthModule.cs` — `JwtBearerEvents.OnForbidden` 403 envelope handler + private static `ForbiddenJsonOptions`.
- `SteamAuthenticationModule.cs` — `IAdminAuthorityResolver` DI kaydı (1 satır + 2 satır comment).

**Yeni — `backend/tests/Skinora.Auth.Tests/Integration/`:**
- `AdminAuthorityResolverTests.cs` — 5 integration test (no admin, super_admin, regular admin ordered, soft-delete demote, dynamic revocation reflects on next resolve).

**Yeni — `backend/tests/Skinora.API.Tests/Integration/`:**
- `AdminRbacEndpointTests.cs` — 5 integration test (anonymous 401, regular user 403+envelope, admin without perm 403+envelope, admin with perm 200, super_admin bypass 200). Hedef: `GET /api/v1/admin/roles` (AD11, `Permission:MANAGE_ROLES`).

**Değişiklik — `backend/tests/Skinora.Auth.Tests/`:**
- `Unit/AccessTokenGeneratorTests.cs` — async API + 4 senaryo (no admin / super_admin / regular admin permission emit / resolver çağrı parametresi). Moq tabanlı.
- `Unit/SteamAuthenticationPipelineTests.cs` — `_access.Setup`/`Verify` sync `Generate` → async `GenerateAsync` migration.
- `Integration/RefreshTokenServiceTests.cs` — `CreateSut` içinde `new AdminAuthorityResolver(Context)` ile gerçek resolver enjekte; static ctor'a `AdminModuleDbRegistration.RegisterAdminModule()`.

**Migration:** **yok**. Entity'ler T24'te tanımlı.

**Package reference:** **yok**.

## Kabul Kriterleri Kontrolü

| # | Kriter (11 §T40) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Her admin endpoint'inde permission kontrolü | ✓ | T39 mevcut 8 endpoint dynamic policy ile dekore (`Permission:MANAGE_ROLES`/`Permission:VIEW_USERS`). T40 sonrası non-super-admin admin'ler de claim'lerle policy'i geçer. Test: `AdminRbacEndpointTests.ListRoles_AdminWithManageRolesPermission_Returns200`, `..._RegularUser_Returns403WithInsufficientPermissionEnvelope`. |
| 2 | Policy-based authorization .NET built-in ile | ✓ | T06'da kurulu `PermissionPolicyProvider : IAuthorizationPolicyProvider` + `PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>` tamamen `Microsoft.AspNetCore.Authorization` üzerinde; T40 yalnız claim üreticisini besler. `[Authorize(Policy="Permission:KEY")]` yerli attribute. |
| 3 | Dinamik rol grupları (DB'den okunan yetkiler) | ✓ | `AdminAuthorityResolver` her token issuance/refresh'te `AdminUserRole → AdminRole → AdminRolePermission` chain'ini DB'den yeniden çözer. Hard-coded permission yok; aynı kullanıcı için ardışık `ResolveAsync` çağrıları DB değişikliğini yansıtır. Test: `AdminAuthorityResolverTests.ResolveAsync_PermissionRevokedAfterAssignment_NewLookupReturnsRemainingPermissions`. |
| 4 | INSUFFICIENT_PERMISSION (403) hata dönüşü | ✓ | `AuthModule.AddJwtBearer` `OnForbidden` handler 403 status + `ApiResponse.Fail("INSUFFICIENT_PERMISSION", ..., traceId)` JSON gövdesi yazar. Test: `AdminRbacEndpointTests` envelope assert helper'ı (`success=false`, `error.code=INSUFFICIENT_PERMISSION`, `traceId` mevcut). |

| Doğrulama listesi (11 §T40) | Sonuç | Kanıt |
|---|---|---|
| 07 §9'daki her endpoint'in hangi yetkiyi gerektirdiği belirli mi? | ✓ | 07 §9.1-§9.22 her endpoint'in **Permission** satırı net. AD1 (admin genel), AD2/AD3 VIEW_FLAGS, AD4/AD5 MANAGE_FLAGS, AD6/AD7 VIEW_TRANSACTIONS, AD8/AD9 MANAGE_SETTINGS, AD10 VIEW_STEAM_ACCOUNTS, AD11–AD15 + AD17 MANAGE_ROLES, AD16/AD16b VIEW_USERS, AD18 VIEW_AUDIT_LOG, AD19 CANCEL_TRANSACTIONS, AD19b/AD19c EMERGENCY_HOLD, S18 manuel recovery MANAGE_STEAM_RECOVERY (07 §9.11 not). |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Auth Unit | ✓ 57/57 | `dotnet test tests/Skinora.Auth.Tests --filter "Category!=Integration"` — Duration 252 ms. AccessTokenGeneratorTests 4 yeni test dahil. |
| API Integration (SQLite in-memory) | ✓ 231/231 | `dotnet test tests/Skinora.API.Tests` — Duration 22 m 52 s. AdminRbacEndpointTests 5 yeni test dahil; T39'un 26 admin endpoint testi yeşil kaldı. |
| Shared Tests | ✓ 156/156 | Cross-cutting (logging, exception, persistence) — etki yok. |
| Notifications Tests | ✓ 26/26 | Etkilenmedi. |
| Transactions Tests | ✓ 58/58 | Solo run; full-solution run'da bir flake (timeout) görüldü, solo-run temiz. |
| Auth Integration (Testcontainers MsSql) | ⏸ Lokal Docker yok — CI'da koşacak | `AdminAuthorityResolverTests` 5 + `RefreshTokenServiceTests` 9 + diğer Auth integration; INTEGRATION_TEST_SQL_SERVER env var'ıyla CI yeşilini bekliyor. |
| `dotnet build` | ✓ 0 W / 0 E | Backend Release+Debug temiz. |
| `dotnet format --verify-no-changes` | ✓ | 0 fark. |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏸ Validate chat'i bekleniyor |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- **Migration:** Yok.
- **Config/env değişikliği:** Yok.
- **Docker değişikliği:** Yok.
- **Yeni proje referansı:** `Skinora.Auth.csproj` → `Skinora.Admin.csproj` (Auth modülünün AdminUserRole/AdminRole/AdminRolePermission entity'lerine erişimi için). Skinora.Admin halen yalnız Shared+Users'a bağlı, cycle yok.

## Commit & PR

- Branch: `task/T40-admin-rbac-policy`
- Commit: `b7f8657` (yapım) + `a958218` (rapor + status + memory)
- PR: [#65](https://github.com/turkerurganci/Skinora/pull/65)
- CI: izleniyor

## Known Limitations / Follow-up

- **Permission revocation propagation:** Permission silindikten sonra mevcut access token (≤ 15 dk TTL) süresine kadar geçerli kalır — JWT statelessness gereği kabul edilmiş tasarım kararı. Real-time invalidation (revocation list) T40 dışında, doc'da da talep edilmiyor.
- **401 envelope:** 07 §2.5'te 401 için spesifik error code yok; T40 yalnız 403'e dokundu. Mevcut testler 401'i status code ile kontrol ediyor; davranış değişmedi.
- **MANAGE_STEAM_RECOVERY claim:** Catalog'da var, JWT'ye emit edilebiliyor, ama hiçbir endpoint henüz bu permission'ı policy gereksinimi olarak kullanmıyor. T103 (S18 Manual Recovery wiring) tüketici tarafı.
- **AccessTokenGenerator async ctor maliyeti:** Token issuance artık DB lookup içeriyor (ilk login + her refresh). EF Core query 2 round-trip'le sınırlı (AdminUserRole join AdminRole + opsiyonel AdminRolePermission). Performans gerekirse cache layer (kullanıcı bazlı 1-2 dk TTL) sonraki sprint'te eklenebilir; T40 spec'ine göre dinamiklik > performans.

## Notlar

- **Adım -1 working tree hygiene:** Temiz (1ddcab7 sonrası uncommitted değişiklik yok).
- **Adım 0 main CI startup:** ✓ — son 3 main run hepsi `success`: 25217423267 (T39), 25217423271 (T39 second matrix), 25208725398 (T38).
- **Dış varsayım kontrolü:** Yok — T40 tamamen mevcut altyapı (T06 PermissionPolicyProvider, T39 PermissionCatalog, T24 Admin entity'leri) üzerine inşa. Yeni paket / dış API / plan tier varsayımı yok.
