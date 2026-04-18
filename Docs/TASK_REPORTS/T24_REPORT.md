# T24 — AdminRole, AdminRolePermission, AdminUserRole Entity'leri

**Faz:** F1 | **Durum:** ⏳ Devam ediyor | **Tarih:** 2026-04-18

---

## Yapılan İşler
- AdminRole entity (06 §3.14): admin rol tanımları, soft delete, unfiltered unique Name
- AdminRolePermission entity (06 §3.15): rol başına yetki atamaları, soft delete, FK → AdminRole, filtered unique (AdminRoleId + Permission)
- AdminUserRole entity (06 §3.16): kullanıcı-rol eşlemesi (N:M), surrogate PK ile soft delete sonrası yeniden atama, FK → User + AdminRole + User(opt), filtered unique (UserId + AdminRoleId)
- AdminModuleDbRegistration + Program.cs module kaydı
- 20 integration test (6 AdminRole + 6 AdminRolePermission + 8 AdminUserRole)

## Etkilenen Modüller / Dosyalar

### Yeni Dosyalar
- `backend/src/Modules/Skinora.Admin/Domain/Entities/AdminRole.cs`
- `backend/src/Modules/Skinora.Admin/Domain/Entities/AdminRolePermission.cs`
- `backend/src/Modules/Skinora.Admin/Domain/Entities/AdminUserRole.cs`
- `backend/src/Modules/Skinora.Admin/Infrastructure/Persistence/AdminRoleConfiguration.cs`
- `backend/src/Modules/Skinora.Admin/Infrastructure/Persistence/AdminRolePermissionConfiguration.cs`
- `backend/src/Modules/Skinora.Admin/Infrastructure/Persistence/AdminUserRoleConfiguration.cs`
- `backend/src/Modules/Skinora.Admin/Infrastructure/Persistence/AdminModuleDbRegistration.cs`
- `backend/tests/Skinora.Admin.Tests/Integration/AdminRoleEntityTests.cs`
- `backend/tests/Skinora.Admin.Tests/Integration/AdminRolePermissionEntityTests.cs`
- `backend/tests/Skinora.Admin.Tests/Integration/AdminUserRoleEntityTests.cs`

### Değişen Dosyalar
- `backend/src/Modules/Skinora.Admin/Skinora.Admin.csproj` — Users ProjectReference eklendi
- `backend/src/Skinora.API/Program.cs` — AdminModuleDbRegistration using + registration call
- `backend/tests/Skinora.Admin.Tests/Skinora.Admin.Tests.csproj` — Users ProjectReference eklendi

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | AdminRole entity: 06 §3.14'e göre (Name, Description) | ✓ | AdminRole.cs — 3 domain field (Name, Description, IsSuperAdmin) + BaseEntity + ISoftDeletable |
| 2 | AdminRolePermission entity: 06 §3.15'e göre (Permission string) | ✓ | AdminRolePermission.cs — 2 domain field (AdminRoleId, Permission) + BaseEntity + ISoftDeletable |
| 3 | AdminUserRole entity: 06 §3.16'ya göre (AssignedByAdminId) | ✓ | AdminUserRole.cs — 4 domain field (UserId, AdminRoleId, AssignedAt, AssignedByAdminId) + BaseEntity + ISoftDeletable |
| 4 | Unique: AdminRole.Name | ✓ | UQ_AdminRoles_Name — unfiltered (soft-deleted rows also unique) |
| 5 | Unique: AdminRolePermission (AdminRoleId + Permission, filtered) | ✓ | UQ_AdminRolePermissions_AdminRoleId_Permission — `WHERE IsDeleted = 0` |
| 6 | Unique: AdminUserRole (UserId + AdminRoleId, filtered) | ✓ | UQ_AdminUserRoles_UserId_AdminRoleId — `WHERE IsDeleted = 0` |
| 7 | FK'ler: AdminRolePermission→AdminRole | ✓ | AdminRolePermissionConfiguration.cs — HasOne&lt;AdminRole&gt; |
| 8 | FK'ler: AdminUserRole→User, AdminRole, User(assigner) | ✓ | AdminUserRoleConfiguration.cs — 3x HasOne (User + AdminRole + User/AssignedByAdminId) |
| 9 | Soft delete: AdminRole, AdminRolePermission, AdminUserRole (kalıcı) | ✓ | Tüm 3 config'te HasQueryFilter(e => !e.IsDeleted), entity'ler ISoftDeletable implement ediyor |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Integration | ⏳ CI bekliyor | 20 test — CI TestContainers SQL Server, PR #28 run takipte |
| Build | ✓ 0 Error, 0 Warning | `dotnet build backend/Skinora.sln -v minimal` — full solution build başarılı |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Validator bekliyor |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |
| Main CI Check | ✓ 3/3 success (24610262089, 24610262100, 24472824093) |
| Task Branch CI | ⏳ PR #28 — CI beklemede |
| Lokal Build | ✓ 0 Warning, 0 Error |
| Lokal Test | Docker daemon unavailable (cloud env — sandbox iptables kısıtı) — CI evidence kullanılacak |
| Güvenlik | Secret sızıntısı yok, auth etkisi yok (entity layer), yeni dış bağımlılık yok (Users iç modül referansı) |
| Doküman uyumu | 06 §3.14, §3.15, §3.16, §4.1, §4.2, §5.1 — tüm field, FK, unique index birebir uyumlu |

## Altyapı Değişiklikleri
- Migration: Yok (T28'de initial migration)
- Config/env değişikliği: Yok
- Docker değişikliği: Yok

## Commit & PR
- Branch: `task/T24-admin-entities`
- Commit: `759fba6` — T24: AdminRole, AdminRolePermission, AdminUserRole entities
- PR: #28
- CI: ⏳ Beklemede

## Known Limitations / Follow-up
- Yok

## Notlar
- **Working tree:** Temiz
- **Main CI Startup Check:** 3/3 success — run 24610262089 (T23), 24610262100 (T23 docker-publish), 24472824093 (PR #26 chore)
- **Dış varsayım:** Yok — tüm bağımlılıklar (T04, T17, T18) tamamlanmış, User entity T18'de oluşturulmuş, BaseEntity + ISoftDeletable T03'te tanımlı
- **Test sayıları:** AdminRole 6 (CRUD 3 + unique 2 + soft delete 1), AdminRolePermission 6 (CRUD 1 + unique 3 + FK 1 + soft delete 1), AdminUserRole 8 (CRUD 2 + unique 2 + FK 3 + soft delete 1)
- **Name uniqueness (unfiltered):** 06 §5.1 AdminRole.Name için filter belirtilmemiş — soft-deleted rol adı aynı isimle yeniden açılamaz (Name kirlenmesi engeli). AdminRolePermission ve AdminUserRole'daki filtered unique'lerden farklı.
