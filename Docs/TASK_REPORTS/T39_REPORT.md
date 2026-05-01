# T39 — Admin rol ve yetki yönetimi

**Faz:** F2 | **Durum:** ⏳ Yapım bitti | **Tarih:** 2026-05-01

---

## Yapılan İşler

- **`PermissionCatalog` (07 §9.11 / 04 §8.8):** `Skinora.Admin/Application/Permissions/PermissionCatalog.cs`. 11 yetkinin (key + Türkçe label) tek source-of-truth tablosu. AD11 response'undaki `availablePermissions[]` aynen buradan okunur, AD12/AD13 input validasyonu (INVALID_PERMISSION) buradan beslenir, T40+ JWT issuance'ı `permission` claim'ini buradan üretir. Kategoriler: 5 view (FLAGS / TRANSACTIONS / STEAM_ACCOUNTS / USERS / AUDIT_LOG) + 6 manage/aksiyon (FLAGS / SETTINGS / STEAM_RECOVERY / ROLES / CANCEL_TRANSACTIONS / EMERGENCY_HOLD).

- **`MANAGE_STEAM_RECOVERY` doc-sync (07 §9.11):** 11. permission — 04 §8.8 "Steam recovery yönet" satırının string identifier'ı eksikti. 11_IMPLEMENTATION_PLAN T39 kabul kriteri "11 yetki tanımı" diyordu; 07 §9.11 ise yalnız 10 entry listeliyordu. Doküman senkronu 07 §9.11 array'ine 11. entry + 4 satır not eklendi (T39 yalnız katalog girişini sağlar; T103 S18 wiring devralır). 04 / 07 / 11 üçlüsü "11 yetki" üzerinde hemzemin.

- **Role CRUD servisi (07 §9.11–§9.14):** `Skinora.Admin/Application/Roles/`. `IAdminRoleService` arayüzü dört operasyonu sarmalar — `ListAsync` (rol summary + 11-permission catalog), `CreateAsync`, `UpdateAsync`, `DeleteAsync`. Implementasyon `AppDbContext` üstünde `AdminRole` + `AdminRolePermission` + `AdminUserRole` tablolarını okur/yazar; soft-delete query filter'ları aktif satırlara otomatik kısıtlar. Permission set güncelleme: tombstone + insert (silinen perm `IsDeleted=true,DeletedAt=now`, yeni perm yeni `Id`). Filtered unique index `(AdminRoleId, Permission) WHERE IsDeleted = 0` ile çakışmasız.

- **AssignedUserCount + Catalog projection (AD11):** Tek round-trip ile ana sorguya gömülmüş alt sorgu — her rol için `Permissions = AdminRolePermission.Where(p => p.AdminRoleId == r.Id).Select(p.Permission).ToList()` ve `AssignedUserCount = AdminUserRole.Count(ur => ur.AdminRoleId == r.Id)`. EF Core SQL Server'da subquery + correlated count'a render eder; SQLite (test) inline'a açar — her ikisi de doğrulandı.

- **Validation outcome'ları (`RoleOperationOutcome` discriminated record):** `Success(RoleDetailDto)`, `NotFound`, `NameConflict`, `InvalidPermission(string)`, `ValidationFailed(string)`. Controller `MapRoleOperationOutcome()` switch ile her outcome'u doğru status code + ApiResponse envelope'una mapler — 201 (Create), 200 (Update), 404 (NotFound), 409 (NameConflict), 400 (Invalid/Validation). Delete ayrı `RoleDeleteOutcome` (`Success`, `NotFound`, `HasUsers(int)`) — 422 ROLE_HAS_USERS `details: { assignedUserCount }` payload taşır.

- **`AdminRoleErrorCodes` sabitleri:** `ROLE_NAME_EXISTS`, `ROLE_NOT_FOUND`, `ROLE_HAS_USERS`, `INVALID_PERMISSION`, `VALIDATION_ERROR`. NotificationInboxErrorCodes konvansiyonuna birebir uyum (UPPER_SNAKE_CASE, public const string).

- **User listing + detail + assignment (07 §9.15–§9.18):** `Skinora.Admin/Application/Users/`. `IAdminUserService` dört operasyon — `ListAsync(search, roleId, page, pageSize)`, `GetDetailAsync(steamId)`, `GetTransactionsAsync(steamId, page, pageSize)`, `AssignRoleAsync(userId, request, assigningAdminId)`. AD15 dual-mode listeleme: search yoksa yalnız aktif `AdminUserRole`'ü olan kullanıcılar (S19 admin browse), search varsa tüm aktif kullanıcılar (S19 "rol atama" workflow'u admin olmayan kullanıcıyı da bulabilsin). roleId filtresi her iki modda da çalışır.

- **`accountStatus` türetimi (AD16):** `User.IsDeleted=true` (anonimleştirilmiş) → soft-delete query filter zaten gizler (DELETED durumu 07 §9.16'da nominally var ama T36 anonimleştirme SteamId'yi `ANON_<hex>`'e değiştirdiğinden orijinal SteamId ile aramada zaten 404 döner — known limitation). Aksi → `IsDeactivated ? "DEACTIVATED" : "ACTIVE"`. Test kapsadı.

- **WalletHistory current-only (T39 known limitation):** T34 model'inde `User.DefaultPayoutAddress` + `DefaultRefundAddress` + `*ChangedAt` mevcut; ayrı `WalletAddressHistory` entity'si yok. AD16 `walletHistory[]` her bir non-null current adres için `current=true, type=seller|buyer, address, setAt=ChangedAt` döner. Geçmiş adresler T39 dışı — şema gerektirirse T93 (S20 derinlemesine) tetikler.

- **AD17 atomic re-assignment:** `AssignRoleAsync` her aktif `AdminUserRole` row'unu tombstone'lar + yeni rol için yeni row insert eder. Surrogate PK + filtered unique `(UserId, AdminRoleId) WHERE IsDeleted=0` desteğiyle aynı rol soft-delete sonrası tekrar atanabilir (06 §3.16 invariantı). `roleId == null` → tüm assignment tombstone, response `role: null, assignedAt: null`.

- **`AdminController` (8 endpoint, route prefix `api/v1/admin`):** `Skinora.API/Controllers/AdminController.cs`. Her endpoint dynamic policy `Permission:<KEY>` (T06 `PermissionPolicyProvider` + `PermissionAuthorizationHandler` üstünde): MANAGE_ROLES (AD11–AD15, AD17), VIEW_USERS (AD16, AD16b). T40 JWT'ye `permission` claim eklemediği sürece sadece SuperAdmin (PermissionAuthorizationHandler bypass) erişir — bilinçli forward-compat seçim. RateLimit: read endpoint'ler `admin-read` (120 req/dk), write endpoint'ler `admin-write` (30 req/dk). Hata envelope `ApiResponse<object>.Fail(code, msg, details, traceId)` ile manuel inşa (filter yalnız 2xx'i sarar).

- **Module entry (`AdminModule.AddAdminModule`):** `Skinora.Admin/AdminModule.cs`. `IAdminRoleService`/`IAdminUserService` scoped + `TimeProvider.System` TryAddSingleton. Program.cs `AddUsersModule` çağrısının altında `builder.Services.AddAdminModule()` tek satır kayıt.

## Etkilenen Modüller / Dosyalar

**Yeni — `backend/src/Modules/Skinora.Admin/Application/Permissions/`:**
- `PermissionCatalog.cs` — 11 permission key + Türkçe label, `Keys` sabit sınıfı, `IsKnown(key)` doğrulama, `PermissionEntry` record.

**Yeni — `backend/src/Modules/Skinora.Admin/Application/Roles/`:**
- `IAdminRoleService.cs` — interface (4 operasyon).
- `AdminRoleService.cs` — EF Core impl (AppDbContext + soft-delete tombstone+insert pattern).
- `AdminRoleDtos.cs` — `RolesListResponse`, `RoleSummaryDto`, `AvailablePermissionDto`, `CreateRoleRequest`, `UpdateRoleRequest`, `RoleDetailDto`.
- `AdminRoleErrorCodes.cs` — 5 stable error code sabiti.
- `RoleOperationOutcome.cs` — discriminated `RoleOperationOutcome` (5 case) + `RoleDeleteOutcome` (3 case).

**Yeni — `backend/src/Modules/Skinora.Admin/Application/Users/`:**
- `IAdminUserService.cs` — interface (4 operasyon).
- `AdminUserService.cs` — EF Core impl + AD15 dual-mode + AD16 wallet current-only + AD17 atomic re-assignment.
- `AdminUserDtos.cs` — list/detail/profile/stats/wallet/flag/dispute/counterparty + assign request/response + `AdminAccountStatus`/`AdminWalletEntryType` sabit sınıfları.
- `AdminUserErrorCodes.cs` — `USER_NOT_FOUND`, `ROLE_NOT_FOUND`.
- `AssignRoleOutcome.cs` — discriminated outcome (3 case).

**Yeni — `backend/src/Modules/Skinora.Admin/`:**
- `AdminModule.cs` — `AddAdminModule` IServiceCollection extension.

**Yeni — `backend/src/Skinora.API/Controllers/`:**
- `AdminController.cs` — 8 endpoint (AD11–AD17), route `api/v1/admin`.

**Değişiklik — `backend/src/Skinora.API/Program.cs`:**
- `using Skinora.Admin;` + `builder.Services.AddAdminModule();` (1 yeni satır).

**Değişiklik — `Docs/07_API_DESIGN.md` §9.11:**
- `availablePermissions[]` array'ine `MANAGE_STEAM_RECOVERY` entry'si (10 → 11).
- 11 yetkinin role'ünü açıklayan 1 paragraflık not (S18 Manual Recovery Başlat / VIEW_STEAM_ACCOUNTS'tan ayrı / T103 wiring devirı).

**Yeni — `backend/tests/Skinora.API.Tests/Integration/`:**
- `AdminRolesEndpointTests.cs` — **12 integration test** (auth 401, non-admin 403, list+catalog count=11, create valid+409+400empty+400invalid_perm, update success+404, delete success+404+422ROLE_HAS_USERS).
- `AdminUsersEndpointTests.cs` — **14 integration test** (list noparams admin-only, list roleId filter, list search broaden non-admin, list 403, detail ACTIVE/DEACTIVATED/404, transactions empty/404, assign new/replace/clear/user-not-found/role-not-found).

**Migration:** **yok**. Entity'ler T24'te tanımlı, T28 InitialCreate migration'ında zaten yer alıyor.

**Package reference:** **yok**. Yeni paket gerekmedi.

## Kabul Kriterleri Kontrolü

| # | Kriter (11 §T39) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `GET /admin/roles` → rol listesi + mevcut yetkiler | ✓ | `AdminController.ListRoles()` route `GET api/v1/admin/roles`, `[Authorize(Permission:MANAGE_ROLES)] + [RateLimit("admin-read")]`. `AdminRoleService.ListAsync` `RolesListResponse(Roles[], AvailablePermissions[])` döner; AvailablePermissions `PermissionCatalog.All` ile birebir 11 entry. Test: `ListRoles_SuperAdmin_ReturnsRolesAndCatalog` (`Assert.Equal(11, available.GetArrayLength())` + içerik kontrolü `MANAGE_STEAM_RECOVERY`/`MANAGE_ROLES`/`EMERGENCY_HOLD`). |
| 2 | `POST /admin/roles` → yeni rol oluşturma | ✓ | `CreateRole()` route `POST api/v1/admin/roles`, status 201 + `RoleDetailDto` döner. Çakışan name → 409 ROLE_NAME_EXISTS, geçersiz permission key → 400 INVALID_PERMISSION, boş name → 400 VALIDATION_ERROR. Test: `CreateRole_Valid_Returns201WithDetail`, `CreateRole_DuplicateName_Returns409RoleNameExists`, `CreateRole_EmptyName_Returns400ValidationError`, `CreateRole_UnknownPermission_Returns400InvalidPermission`. |
| 3 | `PUT /admin/roles/:id` → rol güncelleme | ✓ | `UpdateRole()` route `PUT api/v1/admin/roles/{id:guid}`, name + description + permissions replace-all (tombstone + insert). 404 ROLE_NOT_FOUND, 409 ROLE_NAME_EXISTS (başka rol aynı isimde), 400 INVALID_PERMISSION. Test: `UpdateRole_ChangesNameDescriptionAndPermissions` (DB doğrulama: yeni 2 perm aktif, eski 1 perm yok), `UpdateRole_UnknownId_Returns404RoleNotFound`. |
| 4 | `DELETE /admin/roles/:id` → rol silme (atanmış kullanıcı varsa engel) | ✓ | `DeleteRole()` route `DELETE api/v1/admin/roles/{id:guid}`, 200 başarı (rol + permissions tombstone), 404 ROLE_NOT_FOUND, 422 ROLE_HAS_USERS `details: { assignedUserCount }` payload'lı. Test: `DeleteRole_Unassigned_Returns200AndSoftDeletes` (DB `RoleIsLiveAsync` false), `DeleteRole_AssignedToUser_Returns422RoleHasUsers` (`assignedUserCount=1` doğrulandı, rol hala canlı), `DeleteRole_UnknownId_Returns404RoleNotFound`. |
| 5 | `GET /admin/users` → admin kullanıcı listesi | ✓ | `ListUsers()` route `GET api/v1/admin/users`, query `search`/`roleId`/`page`/`pageSize` kabul eder. Default + roleId modunda yalnız aktif role-assignment'lı kullanıcılar; search modunda tüm aktif kullanıcılar (rol atama workflow'u). Test: `ListUsers_NoParams_ReturnsOnlyUsersWithRoles` (3 user seed → 1 admin döner), `ListUsers_RoleIdFilter_RestrictsToRole` (2 admin + 2 rol → roleId ile filtre), `ListUsers_SearchBroadensToNonAdmins` (admin olmayan da bulunur, role=null), `ListUsers_NonAdmin_Returns403`. |
| 6 | `GET /admin/users/:steamId` → kullanıcı detay | ✓ | `GetUserDetail()` route `GET api/v1/admin/users/{steamId}`, profile + stats + walletHistory + boş flagHistory/disputeHistory/frequentCounterparties (07 §9.16 contract — empty arrays known limitation, T54/T58/T63 wire eder). `accountStatus` ACTIVE/DEACTIVATED türetimi `User.IsDeactivated`'tan. Test: `GetUserDetail_Active_ReturnsProfileAndStatus` (2 wallet entry, 3 boş array, ACTIVE), `GetUserDetail_Deactivated_ReturnsDeactivatedStatus`, `GetUserDetail_UnknownSteamId_Returns404`. |
| 7 | `PUT /admin/users/:id/role` → rol atama | ✓ | `AssignRole()` route `PUT api/v1/admin/users/{id:guid}/role`, body `{ roleId: guid? }`. Atomic rotate: tüm aktif `AdminUserRole` tombstone + yeni Insert. `roleId == null` → clear (response `role: null, assignedAt: null`). 404 USER_NOT_FOUND, 404 ROLE_NOT_FOUND. Test: `AssignRole_NewAssignment_Returns200WithRoleAndAssignedAt`, `AssignRole_ReplaceExistingRole_TombstonesPriorAssignment` (DB: 2 row tombstoned-list, 1 aktif), `AssignRole_NullRoleId_ClearsAssignment`, `AssignRole_UnknownUser_Returns404UserNotFound`, `AssignRole_UnknownRole_Returns404RoleNotFound`. |
| 8 | 11 yetki tanımı (MANAGE_FLAGS, CANCEL_TRANSACTIONS, EMERGENCY_HOLD, vb.) | ✓ | `PermissionCatalog.All` 11 entry. AD11 response `availablePermissions[].length == 11`. Doc sync: 07 §9.11 `MANAGE_STEAM_RECOVERY` ekledim — 04 §8.8 ile birebir. Test: `ListRoles_SuperAdmin_ReturnsRolesAndCatalog` `Assert.Equal(11, available.GetArrayLength())`. Yetki listesi: VIEW_FLAGS, MANAGE_FLAGS, VIEW_TRANSACTIONS, MANAGE_SETTINGS, VIEW_STEAM_ACCOUNTS, **MANAGE_STEAM_RECOVERY**, VIEW_USERS, MANAGE_ROLES, VIEW_AUDIT_LOG, CANCEL_TRANSACTIONS, EMERGENCY_HOLD. |

## Doğrulama Kontrol Listesi (11 §T39)

- [x] **07 §9.11–§9.18 endpoint'leri eksiksiz mi?**
  - 9.11 (`GET /admin/roles`): Permission MANAGE_ROLES ✓, response `{ roles[], availablePermissions[] }` ✓ (11 entry).
  - 9.12 (`POST /admin/roles`): Body `{ name, description, permissions[] }` ✓, 201 + RoleDetailDto ✓, hatalar 409 ROLE_NAME_EXISTS + 400 VALIDATION_ERROR ✓.
  - 9.13 (`PUT /admin/roles/:id`): AD12 ile aynı request/response ✓ + 404 ROLE_NOT_FOUND ✓.
  - 9.14 (`DELETE /admin/roles/:id`): 200 + `data: null` ✓, 404 ROLE_NOT_FOUND ✓, 422 ROLE_HAS_USERS ✓.
  - 9.15 (`GET /admin/users`): Permission MANAGE_ROLES ✓, paginated ✓, query `search`/`roleId` ✓, items[] = `{id, steamId, displayName, avatarUrl, role}` ✓.
  - 9.16 (`GET /admin/users/:steamId`): Permission VIEW_USERS ✓, `{ profile, stats, walletHistory, flagHistory, disputeHistory, frequentCounterparties }` envelope ✓; flagHistory/disputeHistory/frequentCounterparties T54/T58/T63 forward devirli boş array (07 §9.16 contract şipşak parse edilebilsin diye known limitation — frontend T105 entegrasyonu blokesi yok).
  - 9.17 (`GET /admin/users/:steamId/transactions`): Permission VIEW_USERS ✓, paginated ✓, AD6 ile aynı yapı (T63 backing'i devralır, şu an boş PagedResult).
  - 9.18 (`PUT /admin/users/:id/role`): Permission MANAGE_ROLES ✓, body `{ roleId }` ✓, response `{ userId, role, assignedAt }` ✓, 404 USER_NOT_FOUND + 404 ROLE_NOT_FOUND ✓, `roleId: null` → clear ✓.

- [x] **Atanmış kullanıcılı rol silinemez mi?** ✓ — AD14 servisi `_db.Set<AdminUserRole>().CountAsync(ur => ur.AdminRoleId == roleId)` > 0 ise `RoleDeleteOutcome.HasUsers(count)` döner, controller 422 ROLE_HAS_USERS + `details: { assignedUserCount }` payload'la cevaplar. Test: `DeleteRole_AssignedToUser_Returns422RoleHasUsers` rol soft-delete olmadığını da doğrular (`RoleIsLiveAsync(roleId)` true).

## Test Sonuçları

**Build (lokal, Release):**
```bash
dotnet build Skinora.sln -c Release
```
→ **Build succeeded. 0 Warning(s). 0 Error(s).** ~7s.

**Format verify (lokal):**
```bash
dotnet format Skinora.sln --verify-no-changes
```
→ exit 0, değişiklik yok.

**API integration testler (lokal, Debug, no-Docker — SQLite in-memory factory):**
```bash
dotnet test tests/Skinora.API.Tests --filter "FullyQualifiedName~AdminRoles|FullyQualifiedName~AdminUsers"
```
→ **26/26 PASS** — 3s (12 AdminRoles + 14 AdminUsers).

**Skinora.API.Tests genel sweep (lokal):**
```bash
dotnet test tests/Skinora.API.Tests --filter "FullyQualifiedName!~InitialMigration&FullyQualifiedName!~EfCoreGlobal"
```
→ **212/212 PASS** — 3m9s (Docker-bağımlı 14 test filtrelendi; CI Linux runner mssql service container'la tüm 226 test koşar).

**Auth.Tests unit-only:**
```bash
dotnet test tests/Skinora.Auth.Tests --filter "FullyQualifiedName!~Integration"
```
→ **54/54 PASS** — 88ms (regresyon yok).

## Altyapı Değişiklikleri

- **Migration:** **yok**.
- **Package reference:** **yok**.
- **DI kayıtları:** 2 yeni scoped (`IAdminRoleService`, `IAdminUserService`) + `TimeProvider.System` TryAddSingleton.
- **Rate-limit policy etkisi:** `admin-read` (120/dk) ve `admin-write` (30/dk) policy'leri T07 appsettings.json'da tanımlı; T39 yeni endpoint'leri bu policy'leri kullanır.
- **Authorization policy etkisi:** `Permission:*` dynamic policy provider T06'da kuruldu; T39 endpoint'leri `Permission:MANAGE_ROLES` + `Permission:VIEW_USERS` policy isimlerini kullanır. SuperAdmin claim her permission'ı bypass eder; T40 JWT'ye `permission` claim ekleyene kadar yalnız super-admin admin endpoint'lerine erişir (bilinçli — task scope T39 sırf data + endpoint, T40 RBAC enforcement ekler).

## Known Limitations (forward devirler)

| Alan | Devir | Tetikleyici |
|---|---|---|
| AD16 `walletHistory[]` | Yalnız current `DefaultPayoutAddress` + `DefaultRefundAddress` (T34 model'inde geçmiş adres history entity'si yok) | T93 (S20 derinlemesine) öncesi karar — separate `WalletAddressHistory` entity ekle veya UI current-only kalır |
| AD16 `flagHistory[]` | Boş array | T54 (Fraud) sonrası gerçek veri |
| AD16 `disputeHistory[]` | Boş array | T58 (Dispute servis) sonrası gerçek veri |
| AD16 `frequentCounterparties[]` | Boş array | T63 (admin transactions read servis) sonrası gerçek veri |
| AD16 `accountStatus="DELETED"` | Soft-delete query filter T36 anonimleştirilmiş user'ları gizliyor; SteamId zaten `ANON_<hex>` olduğu için orijinal SteamId aramada 404 döner. DELETED yolu nominally tanımlı ama T39 scope'unda erişilemez | T93 (S20) gerekirse `IgnoreQueryFilters()` ile DELETED'a özel admin lookup (Id ile) ekler |
| AD16b transactions | Boş `PagedResult<>` | T63 backing'i ile dolar (AD6 ile aynı yapı 07 §9.17) |
| Permission claim issuance JWT'de | T40 wire eder; T39 boyunca yalnız SuperAdmin admin endpoint'lerine erişir | T40 PR'ı |
| AdminRole / AdminUserRole `AssignedByAdminId` AuditLog | T39 sadece tombstone + UpdatedAt audit pipeline'ı kullanır; merkezi AuditLog kaydı T42'de wire edilir | T42 (AuditLog servisi) |

## Notlar

- **Working tree (Adım -1):** Temiz. `git status --short` boş.
- **Startup CI check (Adım 0):** Son 3 main run hepsi `success` — `25208725398`, `25208725412` (T38 #63 squash), `25184605927` (T37 chore) — geçti.
- **Dış varsayımlar (Adım 4):**
  - **`PermissionPolicyProvider` infrastructure:** T06'da kuruldu (mevcut). T39 yalnız policy isimlerini kullanır, ek paket / kayıt gerekmedi. Doğrulama: `Skinora.Auth.Authorization.PermissionPolicyProvider.cs:28-37` `Permission:` prefix slice ile dynamic policy döndürüyor.
  - **`AdminRole`/`AdminRolePermission`/`AdminUserRole` schema:** T24'te tanımlı (mevcut). 06 §3.14–§3.16 ile birebir field kümesi. Filtered unique index'ler `(AdminRoleId, Permission) WHERE IsDeleted=0` ve `(UserId, AdminRoleId) WHERE IsDeleted=0` zaten kuruldu — tombstone+insert pattern'i çakışmasız çalışır. SQLite `HasFilter("[IsDeleted] = 0")` filter'ı kabul eder (test pass'i).
  - **`Microsoft.Extensions.DependencyInjection` paket erişimi:** Skinora.Admin csproj'u `Skinora.Users` referansı üzerinden transitive olarak alır (Notifications csproj patterni). `dotnet build` 0W/0E doğruladı.
  - **04 ↔ 07 ↔ 11 doc sync:** Yapım öncesi 04 §8.8 (11 satır) ↔ 07 §9.11 (10 entry) ↔ 11 T39 ("11 yetki") drift tespit edildi. Owner onayı + 07 §9.11'e `MANAGE_STEAM_RECOVERY` eklendi (S18 wiring T103 forward devirli, 04 ile string identifier hizalandı). Drift kapandı.

- **Auth policy stratejisi:** `[Authorize(Policy = "Permission:MANAGE_ROLES")]` compile-time const concat (`AuthPolicies.PermissionPrefix + "MANAGE_ROLES"`). T40 JWT'ye `permission` claim eklediğinde tüm endpoint'ler değişiklik yapmadan permission-aware hale gelir; bugün SuperAdmin bypass üstünde çalışıyor.

- **AD15 dual-mode tasarım kararı:** "Admin kullanıcı listesi" 04 §8.8 + 07 §9.15'te S19 hem "browse admins" hem "rol atama dropdown" use case'lerini barındırır. Default + roleId filter modu admin'leri listeler; search modu admin olmayan kullanıcıları da bulmasına izin verir (rol ataması için hedef kullanıcı arandığında). Bu davranış 07 §9.15'te explicit yazılı değil ama doğal okuma — note olarak servis docstring'inde belgelendi.

- **AssignRole `AssignedByAdminId`:** Controller `User.FindFirstValue(AuthClaimTypes.UserId)` ile caller'in user id'sini çıkarır + servise iletir. JWT'de yoksa `null` (sistem-level atama; T39'da SuperAdmin claim varsa caller user id'si çıkar). T42 AuditLog servisi bu alanı `AdminUserRole` row'undan okuyup audit kaydına iletecek.

- **Bundled-PR check (Bitiş Kapısı):** PR push'tan sonra `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+' | sort -u` yalnız `T39` döndürmeli.

- **Post-merge CI watch:** Validate chat'i merge sonrası main CI'yi izler (validate.md Adım 18).

## Commit & PR

- **Branch:** `task/T39-admin-roles`
- **Commit (kod):** _push sonrası eklenir_
- **Commit (rapor+status+memory):** _push sonrası eklenir_
- **PR:** _push sonrası eklenir_
- **CI run (task branch):** _push sonrası eklenir_
