-- =============================================================================
-- Skinora bootstrap 02 — escrow bot kaydi
-- =============================================================================
-- Neden gerekli: bot havuzu IKI yerde yasar ve ikisi de elle acilir —
--   1. Kimlik bilgileri: secrets/steam-bots.json (Steam sidecar okur, giris yapar)
--   2. Havuz kaydi:      PlatformSteamBots satiri (backend bot secimi yapar)
-- `PlatformSteamBots` icin ne EF seed'i ne de admin endpoint'i vardir (T63/T103
-- salt-okunur); satir bu script ile acilir.
--
-- Ikisi eslesmezse:
--   * JSON var, satir yok  -> `SqlBotSelectionService.SelectAsync` null doner,
--                             trade offer dispatch edilemez (islem ITEM_ESCROWED'a gecmez)
--   * Satir var, JSON yok  -> sidecar skeleton mode; backend bot secer ama
--                             sidecar "no bot session" ile reddeder
--
-- Bot secimi kapasite tabanlidir: Status='ACTIVE' botlar arasinda en dusuk
-- ActiveEscrowCount kazanir (05 §3.2).
--
-- Calistirma:
--   docker exec -i skinora-db /opt/mssql-tools18/bin/sqlcmd \
--     -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d Skinora \
--     -v BotSteamId="76561198000000000" -v BotDisplayName="Skinora Escrow Bot 1" \
--     -i /dev/stdin < scripts/bootstrap/02-register-bot.sql
--
-- Idempotent: ayni SteamId ile tekrar calistirmak sayaclari SIFIRLAMAZ, yalniz
-- gorunen adi tazeler ve botu ACTIVE'e geri alir.
-- =============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @BotSteamId    nvarchar(20)  = N'$(BotSteamId)';
DECLARE @BotDisplayName nvarchar(100) = N'$(BotDisplayName)';
DECLARE @Now datetime2 = SYSUTCDATETIME();

IF @BotSteamId IS NULL OR LEN(@BotSteamId) < 17
BEGIN
    RAISERROR ('BotSteamId gecersiz (17 haneli SteamID64 bekleniyor): %s', 16, 1, @BotSteamId);
    RETURN;
END

BEGIN TRANSACTION;

IF EXISTS (SELECT 1 FROM PlatformSteamBots WHERE SteamId = @BotSteamId AND IsDeleted = 0)
BEGIN
    -- Sayaclari KORU — canli emanet sayisini sifirlamak bot secimini bozar.
    UPDATE PlatformSteamBots
    SET DisplayName       = @BotDisplayName,
        Status            = 'ACTIVE',
        RestrictionReason = NULL,
        UpdatedAt         = @Now
    WHERE SteamId = @BotSteamId AND IsDeleted = 0;
    PRINT 'PlatformSteamBots: mevcut bot guncellendi (sayaclar korundu).';
END
ELSE
BEGIN
    -- Status nvarchar enum kolonudur — sayi degil, ad yazilir ('ACTIVE').
    INSERT INTO PlatformSteamBots
        (Id, SteamId, DisplayName, Status, RestrictionReason,
         ActiveEscrowCount, DailyTradeOfferCount, LastHealthCheckAt,
         IsDeleted, CreatedAt, UpdatedAt)
    VALUES
        (NEWID(), @BotSteamId, @BotDisplayName, 'ACTIVE', NULL,
         0, 0, @Now,
         0, @Now, @Now);
    PRINT 'PlatformSteamBots: bot eklendi.';
END

COMMIT TRANSACTION;

SELECT SteamId, DisplayName, Status, ActiveEscrowCount, DailyTradeOfferCount, LastHealthCheckAt
FROM PlatformSteamBots
WHERE IsDeleted = 0
ORDER BY CreatedAt;

PRINT 'TAMAM. secrets/steam-bots.json icindeki hesabin SteamID64''u ile ayni oldugunu dogrulayin.';
