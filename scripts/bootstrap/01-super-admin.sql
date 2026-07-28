-- =============================================================================
-- Skinora bootstrap 01 — super admin yetkilendirme
-- =============================================================================
-- Neden gerekli: AdminRoles / AdminUserRoles icin EF `HasData` seed'i YOKTUR
-- (bilincli — rol tanimlari kuruluma ozgudur). Ilk admin bu yuzden disaridan
-- acilir; sonrasi admin UI'dan (AD11-AD17) yonetilir.
--
-- Nasil calisir: `AdminAuthorityResolver` AdminUserRoles -> AdminRoles zincirini
-- cozer; rol `IsSuperAdmin = 1` ise JWT'ye `role = super_admin` claim'i basilir ve
-- `PermissionAuthorizationHandler` her yetki kontrolunu bypass eder. Bu yuzden
-- super admin icin AdminRolePermissions satiri gerekmez.
--
-- ON KOSUL: hedef kullanicinin Users satiri var olmali — yani Steam ile EN AZ
-- BIR KEZ giris yapilmis olmali. Kullanici yoksa script hicbir sey yazmaz ve
-- uyarir.
--
-- SONRASINDA: cikis yapip TEKRAR giris yapin. Rol JWT'ye token uretimi aninda
-- islenir; mevcut access token'da super_admin claim'i olmaz.
--
-- Calistirma (SteamID64'unuzu verin):
--   docker exec -i skinora-db /opt/mssql-tools18/bin/sqlcmd \
--     -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d Skinora \
--     -v SteamId="76561198000000000" \
--     -i /dev/stdin < scripts/bootstrap/01-super-admin.sql
--
-- Idempotent: tekrar calistirmak guvenlidir.
-- =============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @SteamId nvarchar(20) = N'$(SteamId)';
DECLARE @RoleName nvarchar(50) = N'Super Admin';
DECLARE @Now datetime2 = SYSUTCDATETIME();

DECLARE @UserId uniqueidentifier =
    (SELECT TOP 1 Id FROM Users WHERE SteamId = @SteamId AND IsDeleted = 0);

IF @UserId IS NULL
BEGIN
    RAISERROR (
        'Kullanici bulunamadi (SteamId=%s). Once Steam ile giris yapin, sonra bu script''i tekrar calistirin.',
        16, 1, @SteamId);
    RETURN;
END

BEGIN TRANSACTION;

-- --- Rol ---------------------------------------------------------------------
DECLARE @RoleId uniqueidentifier =
    (SELECT TOP 1 Id FROM AdminRoles WHERE Name = @RoleName AND IsDeleted = 0);

IF @RoleId IS NULL
BEGIN
    SET @RoleId = NEWID();
    INSERT INTO AdminRoles (Id, Name, Description, IsSuperAdmin, IsDeleted, CreatedAt, UpdatedAt)
    VALUES (@RoleId, @RoleName, N'Bootstrap super admin — tum yetkiler (bypass)', 1, 0, @Now, @Now);
    PRINT 'AdminRoles: "Super Admin" olusturuldu.';
END
ELSE
BEGIN
    -- Var olan rol super-admin degilse yukselt (yanlis kurulumdan kurtarma)
    UPDATE AdminRoles SET IsSuperAdmin = 1, UpdatedAt = @Now
    WHERE Id = @RoleId AND IsSuperAdmin = 0;
    PRINT 'AdminRoles: "Super Admin" zaten mevcut.';
END

-- --- Atama -------------------------------------------------------------------
-- UQ_AdminUserRoles_UserId_AdminRoleId yalniz IsDeleted = 0 satirlarini kapsar,
-- bu yuzden soft-delete edilmis eski bir atama varsa onu yeniden canlandiririz.
IF EXISTS (SELECT 1 FROM AdminUserRoles
           WHERE UserId = @UserId AND AdminRoleId = @RoleId AND IsDeleted = 0)
BEGIN
    PRINT 'AdminUserRoles: atama zaten mevcut, degisiklik yok.';
END
ELSE IF EXISTS (SELECT 1 FROM AdminUserRoles
                WHERE UserId = @UserId AND AdminRoleId = @RoleId AND IsDeleted = 1)
BEGIN
    UPDATE AdminUserRoles
    SET IsDeleted = 0, DeletedAt = NULL, AssignedAt = @Now, UpdatedAt = @Now
    WHERE UserId = @UserId AND AdminRoleId = @RoleId AND IsDeleted = 1;
    PRINT 'AdminUserRoles: soft-delete edilmis atama geri alindi.';
END
ELSE
BEGIN
    INSERT INTO AdminUserRoles
        (Id, UserId, AdminRoleId, AssignedAt, AssignedByAdminId, IsDeleted, CreatedAt, UpdatedAt)
    VALUES
        (NEWID(), @UserId, @RoleId, @Now, NULL, 0, @Now, @Now);
    PRINT 'AdminUserRoles: super admin atamasi eklendi.';
END

COMMIT TRANSACTION;

SELECT
    u.SteamId,
    u.SteamDisplayName,
    r.Name          AS RoleName,
    r.IsSuperAdmin,
    ur.AssignedAt
FROM AdminUserRoles ur
JOIN AdminRoles r ON r.Id = ur.AdminRoleId
JOIN Users      u ON u.Id = ur.UserId
WHERE ur.UserId = @UserId AND ur.IsDeleted = 0;

PRINT 'TAMAM. Cikis yapip tekrar giris yapin — super_admin claim''i yeni token''da gelir.';
