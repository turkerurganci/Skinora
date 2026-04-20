using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsSuperAdmin = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalIdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ServiceName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ResultPayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LeaseExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIdempotencyRecords", x => x.Id);
                    table.CheckConstraint("CK_ExternalIdempotencyRecords_Status_Invariants", "(\"Status\" = 'in_progress' AND \"CompletedAt\" IS NULL AND \"ResultPayload\" IS NULL AND \"LeaseExpiresAt\" IS NOT NULL) OR (\"Status\" = 'completed' AND \"CompletedAt\" IS NOT NULL) OR (\"Status\" = 'failed' AND \"CompletedAt\" IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "PENDING"),
                    RetryCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                    table.CheckConstraint("CK_OutboxMessages_Status_Invariants", "([Status] = 'PENDING' AND [ProcessedAt] IS NULL) OR ([Status] = 'PROCESSED' AND [ProcessedAt] IS NOT NULL) OR ([Status] = 'DEFERRED') OR ([Status] = 'FAILED' AND [ProcessedAt] IS NULL AND [ErrorMessage] IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "PlatformSteamBots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SteamId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActiveEscrowCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    DailyTradeOfferCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LastHealthCheckAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSteamBots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemHeartbeats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    LastHeartbeat = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemHeartbeats", x => x.Id);
                    table.CheckConstraint("CK_SystemHeartbeats_Singleton", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SteamId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SteamDisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SteamAvatarUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DefaultPayoutAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DefaultRefundAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PreferredLanguage = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false, defaultValue: "en"),
                    TosAcceptedVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TosAcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MobileAuthenticatorVerified = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CompletedTransactionCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    SuccessfulTransactionRate = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    CooldownExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeactivated = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeactivatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdminRolePermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdminRoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Permission = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminRolePermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminRolePermissions_AdminRoles_AdminRoleId",
                        column: x => x.AdminRoleId,
                        principalTable: "AdminRoles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AdminUserRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdminRoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AssignedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminUserRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminUserRoles_AdminRoles_AdminRoleId",
                        column: x => x.AdminRoleId,
                        principalTable: "AdminRoles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AdminUserRoles_Users_AssignedByAdminId",
                        column: x => x.AssignedByAdminId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AdminUserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ColdWalletTransfers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Amount = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FromAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ToAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TxHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    InitiatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColdWalletTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ColdWalletTransfers_Users_InitiatedByAdminId",
                        column: x => x.InitiatedByAdminId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeviceInfo = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_RefreshTokens_ReplacedByTokenId",
                        column: x => x.ReplacedByTokenId,
                        principalTable: "RefreshTokens",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsConfigured = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DataType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UpdatedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                    table.CheckConstraint("CK_SystemSettings_DataType_Valid", "[DataType] IN ('int', 'decimal', 'bool', 'string')");
                    table.ForeignKey(
                        name: "FK_SystemSettings_Users_UpdatedByAdminId",
                        column: x => x.UpdatedByAdminId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SellerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BuyerIdentificationMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetBuyerSteamId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    InviteToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ItemAssetId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ItemClassId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ItemInstanceId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ItemName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ItemIconUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ItemExterior = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ItemType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ItemInspectLink = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EscrowBotAssetId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DeliveredBuyerAssetId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    StablecoinType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CommissionRate = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    MarketPriceAtCreation = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    SellerPayoutAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BuyerRefundAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PaymentTimeoutMinutes = table.Column<int>(type: "int", nullable: false),
                    AcceptDeadline = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TradeOfferToSellerDeadline = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaymentDeadline = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TradeOfferToBuyerDeadline = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TimeoutFrozenAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TimeoutFreezeReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TimeoutRemainingSeconds = table.Column<int>(type: "int", nullable: true),
                    IsOnHold = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    EmergencyHoldAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EmergencyHoldReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EmergencyHoldByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PreviousStatusBeforeHold = table.Column<int>(type: "int", nullable: true),
                    PaymentTimeoutJobId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TimeoutWarningJobId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TimeoutWarningSentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HasActiveDispute = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    EscrowBotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ItemEscrowedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaymentReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ItemDeliveredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.CheckConstraint("CK_Transactions_BuyerMethod_OpenLink", "(BuyerIdentificationMethod != 'OPEN_LINK') OR (InviteToken IS NOT NULL AND TargetBuyerSteamId IS NULL)");
                    table.CheckConstraint("CK_Transactions_BuyerMethod_SteamId", "(BuyerIdentificationMethod != 'STEAM_ID') OR (TargetBuyerSteamId IS NOT NULL AND InviteToken IS NULL)");
                    table.CheckConstraint("CK_Transactions_Cancel", "(Status <> 'CANCELLED_TIMEOUT' AND Status <> 'CANCELLED_SELLER' AND Status <> 'CANCELLED_BUYER' AND Status <> 'CANCELLED_ADMIN') OR (CancelledBy IS NOT NULL AND CancelReason IS NOT NULL AND CancelledAt IS NOT NULL)");
                    table.CheckConstraint("CK_Transactions_FreezeActive", "(TimeoutFrozenAt IS NULL) OR (TimeoutFreezeReason IS NOT NULL AND TimeoutRemainingSeconds IS NOT NULL)");
                    table.CheckConstraint("CK_Transactions_FreezeHold_Forward", "(TimeoutFreezeReason != 'EMERGENCY_HOLD') OR (IsOnHold = 1)");
                    table.CheckConstraint("CK_Transactions_FreezeHold_Reverse", "(IsOnHold = 0) OR (TimeoutFrozenAt IS NOT NULL AND TimeoutFreezeReason = 'EMERGENCY_HOLD')");
                    table.CheckConstraint("CK_Transactions_FreezePassive", "(TimeoutFrozenAt IS NOT NULL) OR (TimeoutFreezeReason IS NULL AND TimeoutRemainingSeconds IS NULL)");
                    table.CheckConstraint("CK_Transactions_Hold", "(IsOnHold = 0) OR (EmergencyHoldAt IS NOT NULL AND EmergencyHoldReason IS NOT NULL AND EmergencyHoldByAdminId IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Transactions_PlatformSteamBots_EscrowBotId",
                        column: x => x.EscrowBotId,
                        principalTable: "PlatformSteamBots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Transactions_Users_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Transactions_Users_EmergencyHoldByAdminId",
                        column: x => x.EmergencyHoldByAdminId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Transactions_Users_SellerId",
                        column: x => x.SellerId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserLoginLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: false),
                    DeviceFingerprint = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLoginLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserLoginLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserNotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ExternalId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotificationPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNotificationPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Disputes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpenedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SystemCheckResult = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserDescription = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AdminNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Disputes", x => x.Id);
                    table.CheckConstraint("CK_Disputes_Closed_ResolvedAt", "(Status <> 'CLOSED') OR (ResolvedAt IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Disputes_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Disputes_Users_AdminId",
                        column: x => x.AdminId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Disputes_Users_OpenedByUserId",
                        column: x => x.OpenedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FraudFlags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Scope = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AdminNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FraudFlags", x => x.Id);
                    table.CheckConstraint("CK_FraudFlags_AccountLevel_TransactionId", "(Scope <> 'ACCOUNT_LEVEL') OR (TransactionId IS NULL)");
                    table.CheckConstraint("CK_FraudFlags_Approved_ReviewedAt", "(Status <> 'APPROVED') OR (ReviewedAt IS NOT NULL AND ReviewedByAdminId IS NOT NULL)");
                    table.CheckConstraint("CK_FraudFlags_PreCreate_TransactionId", "(Scope <> 'TRANSACTION_PRE_CREATE') OR (TransactionId IS NOT NULL)");
                    table.CheckConstraint("CK_FraudFlags_Rejected_ReviewedAt", "(Status <> 'REJECTED') OR (ReviewedAt IS NOT NULL AND ReviewedByAdminId IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_FraudFlags_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FraudFlags_Users_ReviewedByAdminId",
                        column: x => x.ReviewedByAdminId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FraudFlags_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Notifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PaymentAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Address = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    HdWalletIndex = table.Column<int>(type: "int", nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ExpectedToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MonitoringStatus = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    MonitoringExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentAddresses_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SellerPayoutIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SellerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    PayoutTxHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    VerificationStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    EscalatedToAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AdminNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerPayoutIssues", x => x.Id);
                    table.CheckConstraint("CK_SellerPayoutIssues_Status_Invariants", "([VerificationStatus] = 'ESCALATED' AND [EscalatedToAdminId] IS NOT NULL) OR ([VerificationStatus] = 'RESOLVED' AND [ResolvedAt] IS NOT NULL) OR ([VerificationStatus] = 'RETRY_SCHEDULED' AND [RetryCount] > 0) OR [VerificationStatus] IN ('REPORTED', 'VERIFYING')");
                    table.ForeignKey(
                        name: "FK_SellerPayoutIssues_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SellerPayoutIssues_Users_EscalatedToAdminId",
                        column: x => x.EscalatedToAdminId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SellerPayoutIssues_Users_SellerId",
                        column: x => x.SellerId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TradeOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlatformSteamBotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SteamTradeOfferId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RespondedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeOffers", x => x.Id);
                    table.CheckConstraint("CK_TradeOffers_Accepted_RespondedAt", "(Status <> 'ACCEPTED') OR (RespondedAt IS NOT NULL)");
                    table.CheckConstraint("CK_TradeOffers_Accepted_SentAt", "(Status <> 'ACCEPTED') OR (SentAt IS NOT NULL)");
                    table.CheckConstraint("CK_TradeOffers_Declined_RespondedAt", "(Status <> 'DECLINED') OR (RespondedAt IS NOT NULL)");
                    table.CheckConstraint("CK_TradeOffers_Declined_SentAt", "(Status <> 'DECLINED') OR (SentAt IS NOT NULL)");
                    table.CheckConstraint("CK_TradeOffers_Expired_RespondedAt", "(Status <> 'EXPIRED') OR (RespondedAt IS NOT NULL)");
                    table.CheckConstraint("CK_TradeOffers_Expired_SentAt", "(Status <> 'EXPIRED') OR (SentAt IS NOT NULL)");
                    table.CheckConstraint("CK_TradeOffers_Sent_SentAt", "(Status <> 'SENT') OR (SentAt IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TradeOffers_PlatformSteamBots_PlatformSteamBotId",
                        column: x => x.PlatformSteamBotId,
                        principalTable: "PlatformSteamBots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TradeOffers_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TransactionHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreviousStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Trigger = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ActorType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdditionalData = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionHistory_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TransactionHistory_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TargetExternalId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.Id);
                    table.CheckConstraint("CK_NotificationDeliveries_Failed_LastError", "(Status <> 'FAILED') OR (LastError IS NOT NULL)");
                    table.CheckConstraint("CK_NotificationDeliveries_Sent_SentAt", "(Status <> 'SENT') OR (SentAt IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_NotificationDeliveries_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BlockchainTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentAddressId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TxHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FromAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ToAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Token = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActualTokenAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    GasFee = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BlockNumber = table.Column<long>(type: "bigint", nullable: true),
                    ConfirmationCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    RetryCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockchainTransactions", x => x.Id);
                    table.CheckConstraint("CK_BlockchainTransactions_Status_Confirmed", "(Status <> 'CONFIRMED') OR (ConfirmationCount >= 20 AND ConfirmedAt IS NOT NULL)");
                    table.CheckConstraint("CK_BlockchainTransactions_Status_Detected", "(Status <> 'DETECTED') OR (ConfirmationCount = 0)");
                    table.CheckConstraint("CK_BlockchainTransactions_Status_Failed", "(Status <> 'FAILED') OR (ConfirmedAt IS NULL)");
                    table.CheckConstraint("CK_BlockchainTransactions_Status_Pending", "(Status <> 'PENDING') OR (ConfirmationCount < 20)");
                    table.CheckConstraint("CK_BlockchainTransactions_Type_BuyerPayment", "(Type <> 'BUYER_PAYMENT') OR (PaymentAddressId IS NOT NULL AND ActualTokenAddress IS NULL)");
                    table.CheckConstraint("CK_BlockchainTransactions_Type_Outbound", "(Type NOT IN ('SELLER_PAYOUT', 'BUYER_REFUND', 'EXCESS_REFUND', 'LATE_PAYMENT_REFUND', 'INCORRECT_AMOUNT_REFUND')) OR (PaymentAddressId IS NULL AND ActualTokenAddress IS NULL)");
                    table.CheckConstraint("CK_BlockchainTransactions_Type_SpamTokenIncoming", "(Type <> 'SPAM_TOKEN_INCOMING') OR (ActualTokenAddress IS NOT NULL AND PaymentAddressId IS NOT NULL)");
                    table.CheckConstraint("CK_BlockchainTransactions_Type_WrongTokenIncoming", "(Type <> 'WRONG_TOKEN_INCOMING') OR (ActualTokenAddress IS NOT NULL AND PaymentAddressId IS NOT NULL)");
                    table.CheckConstraint("CK_BlockchainTransactions_Type_WrongTokenRefund", "(Type <> 'WRONG_TOKEN_REFUND') OR (ActualTokenAddress IS NOT NULL AND PaymentAddressId IS NULL)");
                    table.ForeignKey(
                        name: "FK_BlockchainTransactions_PaymentAddresses_PaymentAddressId",
                        column: x => x.PaymentAddressId,
                        principalTable: "PaymentAddresses",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BlockchainTransactions_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "SystemHeartbeats",
                columns: new[] { "Id", "LastHeartbeat", "UpdatedAt" },
                values: new object[] { 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[,]
                {
                    { new Guid("0aa51010-0000-0000-0000-000000000001"), "Timeout", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Alıcı kabul timeout süresi", "accept_timeout_minutes", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { new Guid("0aa51010-0000-0000-0000-000000000002"), "Timeout", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Satıcı trade offer timeout süresi", "trade_offer_seller_timeout_minutes", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { new Guid("0aa51010-0000-0000-0000-000000000003"), "Timeout", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Ödeme timeout minimum", "payment_timeout_min_minutes", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { new Guid("0aa51010-0000-0000-0000-000000000004"), "Timeout", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Ödeme timeout maksimum", "payment_timeout_max_minutes", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { new Guid("0aa51010-0000-0000-0000-000000000005"), "Timeout", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Ödeme timeout varsayılan", "payment_timeout_default_minutes", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { new Guid("0aa51010-0000-0000-0000-000000000006"), "Timeout", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Alıcı trade offer timeout süresi", "trade_offer_buyer_timeout_minutes", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { new Guid("0aa51010-0000-0000-0000-000000000007"), "Timeout", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Uyarı gönderim oranı (ör: 0.75)", "timeout_warning_ratio", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null }
                });

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[] { new Guid("0aa51010-0000-0000-0000-000000000008"), "Commission", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Komisyon oranı (%2)", true, "commission_rate", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "0.02" });

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[,]
                {
                    { new Guid("0aa51010-0000-0000-0000-000000000009"), "Limit", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Minimum işlem tutarı", "min_transaction_amount", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { new Guid("0aa51010-0000-0000-0000-00000000000a"), "Limit", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Maksimum işlem tutarı", "max_transaction_amount", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { new Guid("0aa51010-0000-0000-0000-00000000000b"), "Limit", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Eşzamanlı aktif işlem limiti", "max_concurrent_transactions", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { new Guid("0aa51010-0000-0000-0000-00000000000c"), "Limit", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Yeni hesap işlem limiti", "new_account_transaction_limit", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { new Guid("0aa51010-0000-0000-0000-00000000000d"), "Limit", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Kaç gün yeni hesap sayılır", "new_account_period_days", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { new Guid("0aa51010-0000-0000-0000-00000000000e"), "Limit", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Belirli sürede izin verilen iptal sayısı", "cancel_limit_count", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { new Guid("0aa51010-0000-0000-0000-00000000000f"), "Limit", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "İptal limit periyodu", "cancel_limit_period_hours", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { new Guid("0aa51010-0000-0000-0000-000000000010"), "Limit", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "İptal sonrası cooldown süresi", "cancel_cooldown_hours", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null }
                });

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[] { new Guid("0aa51010-0000-0000-0000-000000000011"), "Commission", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Gas fee koruma eşiği (%10)", true, "gas_fee_protection_ratio", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "0.10" });

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[,]
                {
                    { new Guid("0aa51010-0000-0000-0000-000000000012"), "Fraud", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Piyasa fiyat sapma eşiği", "price_deviation_threshold", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { new Guid("0aa51010-0000-0000-0000-000000000013"), "Fraud", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Yüksek hacim tutar eşiği", "high_volume_amount_threshold", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { new Guid("0aa51010-0000-0000-0000-000000000014"), "Fraud", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Yüksek hacim işlem sayısı eşiği", "high_volume_count_threshold", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null },
                    { new Guid("0aa51010-0000-0000-0000-000000000015"), "Fraud", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Yüksek hacim kontrol periyodu", "high_volume_period_hours", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null }
                });

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[,]
                {
                    { new Guid("0aa51010-0000-0000-0000-000000000016"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "İptal sonrası ilk 24 saat polling aralığı (saniye)", true, "monitoring_post_cancel_24h_polling_seconds", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "30" },
                    { new Guid("0aa51010-0000-0000-0000-000000000017"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "1-7 gün arası polling aralığı (saniye)", true, "monitoring_post_cancel_7d_polling_seconds", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "300" },
                    { new Guid("0aa51010-0000-0000-0000-000000000018"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "7-30 gün arası polling aralığı (saniye)", true, "monitoring_post_cancel_30d_polling_seconds", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "3600" },
                    { new Guid("0aa51010-0000-0000-0000-000000000019"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "İzleme durdurma süresi (gün)", true, "monitoring_stop_after_days", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "30" },
                    { new Guid("0aa51010-0000-0000-0000-00000000001a"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Minimum iade eşiği — iade < gas fee × bu oran ise iade yapılmaz", true, "min_refund_threshold_ratio", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "2.0" },
                    { new Guid("0aa51010-0000-0000-0000-00000000001b"), "Feature", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "bool", "Açık link yöntemi aktif mi", true, "open_link_enabled", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "false" }
                });

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[] { new Guid("0aa51010-0000-0000-0000-00000000001c"), "Wallet", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Hot wallet maksimum bakiye limiti — aşıldığında admin alert (05 §3.3)", "hot_wallet_limit", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CooldownExpiresAt", "CreatedAt", "DeactivatedAt", "DefaultPayoutAddress", "DefaultRefundAddress", "DeletedAt", "Email", "IsDeactivated", "PreferredLanguage", "SteamAvatarUrl", "SteamDisplayName", "SteamId", "SuccessfulTransactionRate", "TosAcceptedAt", "TosAcceptedVersion", "UpdatedAt" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, null, null, true, "en", null, "System", "00000000000000001", null, null, null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.CreateIndex(
                name: "UQ_AdminRolePermissions_AdminRoleId_Permission",
                table: "AdminRolePermissions",
                columns: new[] { "AdminRoleId", "Permission" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UQ_AdminRoles_Name",
                table: "AdminRoles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdminUserRoles_AdminRoleId",
                table: "AdminUserRoles",
                column: "AdminRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminUserRoles_AssignedByAdminId",
                table: "AdminUserRoles",
                column: "AssignedByAdminId");

            migrationBuilder.CreateIndex(
                name: "UQ_AdminUserRoles_UserId_AdminRoleId",
                table: "AdminUserRoles",
                columns: new[] { "UserId", "AdminRoleId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action",
                table: "AuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorId",
                table: "AuditLogs",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CreatedAt",
                table: "AuditLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockchainTransactions_FromAddress",
                table: "BlockchainTransactions",
                column: "FromAddress");

            migrationBuilder.CreateIndex(
                name: "IX_BlockchainTransactions_PaymentAddressId",
                table: "BlockchainTransactions",
                column: "PaymentAddressId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockchainTransactions_Status_Pending",
                table: "BlockchainTransactions",
                column: "Status",
                filter: "[Status] = 'PENDING'");

            migrationBuilder.CreateIndex(
                name: "IX_BlockchainTransactions_TransactionId",
                table: "BlockchainTransactions",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "UQ_BlockchainTransactions_TxHash",
                table: "BlockchainTransactions",
                column: "TxHash",
                unique: true,
                filter: "[TxHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ColdWalletTransfers_InitiatedByAdminId",
                table: "ColdWalletTransfers",
                column: "InitiatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "UQ_ColdWalletTransfers_TxHash",
                table: "ColdWalletTransfers",
                column: "TxHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_AdminId",
                table: "Disputes",
                column: "AdminId");

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_OpenedByUserId",
                table: "Disputes",
                column: "OpenedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_Status_Active",
                table: "Disputes",
                column: "Status",
                filter: "[Status] IN ('OPEN', 'ESCALATED')");

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_TransactionId",
                table: "Disputes",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "UQ_Disputes_TransactionId_Type",
                table: "Disputes",
                columns: new[] { "TransactionId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ExternalIdempotencyRecords_ServiceName_IdempotencyKey",
                table: "ExternalIdempotencyRecords",
                columns: new[] { "ServiceName", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FraudFlags_ReviewedByAdminId",
                table: "FraudFlags",
                column: "ReviewedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_FraudFlags_Status_Pending",
                table: "FraudFlags",
                column: "Status",
                filter: "[Status] = 'PENDING'");

            migrationBuilder.CreateIndex(
                name: "IX_FraudFlags_TransactionId",
                table: "FraudFlags",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_FraudFlags_UserId",
                table: "FraudFlags",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UQ_NotificationDeliveries_NotificationId_Channel",
                table: "NotificationDeliveries",
                columns: new[] { "NotificationId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CreatedAt",
                table: "Notifications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TransactionId",
                table: "Notifications",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_CreatedAt_Pending",
                table: "OutboxMessages",
                columns: new[] { "Status", "CreatedAt" },
                filter: "[Status] IN ('PENDING', 'FAILED')");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAddresses_MonitoringStatus_Active",
                table: "PaymentAddresses",
                column: "MonitoringStatus",
                filter: "[MonitoringStatus] IN ('ACTIVE','POST_CANCEL_24H','POST_CANCEL_7D','POST_CANCEL_30D')");

            migrationBuilder.CreateIndex(
                name: "UQ_PaymentAddresses_Address",
                table: "PaymentAddresses",
                column: "Address",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_PaymentAddresses_HdWalletIndex",
                table: "PaymentAddresses",
                column: "HdWalletIndex",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_PaymentAddresses_TransactionId",
                table: "PaymentAddresses",
                column: "TransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_PlatformSteamBots_SteamId",
                table: "PlatformSteamBots",
                column: "SteamId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProcessedEvents_EventId_ConsumerName",
                table: "ProcessedEvents",
                columns: new[] { "EventId", "ConsumerName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ReplacedByTokenId",
                table: "RefreshTokens",
                column: "ReplacedByTokenId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UQ_RefreshTokens_Token",
                table: "RefreshTokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SellerPayoutIssues_EscalatedToAdminId",
                table: "SellerPayoutIssues",
                column: "EscalatedToAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_SellerPayoutIssues_SellerId",
                table: "SellerPayoutIssues",
                column: "SellerId");

            migrationBuilder.CreateIndex(
                name: "IX_SellerPayoutIssues_TransactionId",
                table: "SellerPayoutIssues",
                column: "TransactionId",
                unique: true,
                filter: "[VerificationStatus] <> 'RESOLVED'");

            migrationBuilder.CreateIndex(
                name: "IX_SellerPayoutIssues_VerificationStatus_Active",
                table: "SellerPayoutIssues",
                column: "VerificationStatus",
                filter: "[VerificationStatus] <> 'RESOLVED'");

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_Category",
                table: "SystemSettings",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_UpdatedByAdminId",
                table: "SystemSettings",
                column: "UpdatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "UQ_SystemSettings_Key",
                table: "SystemSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeOffers_PlatformSteamBotId",
                table: "TradeOffers",
                column: "PlatformSteamBotId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeOffers_TransactionId",
                table: "TradeOffers",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "UQ_TradeOffers_SteamTradeOfferId",
                table: "TradeOffers",
                column: "SteamTradeOfferId",
                unique: true,
                filter: "[SteamTradeOfferId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionHistory_ActorId",
                table: "TransactionHistory",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionHistory_TransactionId",
                table: "TransactionHistory",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_BuyerId",
                table: "Transactions",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CreatedAt",
                table: "Transactions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_EmergencyHoldByAdminId",
                table: "Transactions",
                column: "EmergencyHoldByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_EscrowBotId",
                table: "Transactions",
                column: "EscrowBotId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_SellerId",
                table: "Transactions",
                column: "SellerId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Status_Active",
                table: "Transactions",
                column: "Status",
                filter: "[Status] <> 'COMPLETED' AND [Status] <> 'CANCELLED_TIMEOUT' AND [Status] <> 'CANCELLED_SELLER' AND [Status] <> 'CANCELLED_BUYER' AND [Status] <> 'CANCELLED_ADMIN'");

            migrationBuilder.CreateIndex(
                name: "UQ_Transactions_InviteToken",
                table: "Transactions",
                column: "InviteToken",
                unique: true,
                filter: "[InviteToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserLoginLogs_DeviceFingerprint",
                table: "UserLoginLogs",
                column: "DeviceFingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_UserLoginLogs_IpAddress",
                table: "UserLoginLogs",
                column: "IpAddress");

            migrationBuilder.CreateIndex(
                name: "IX_UserLoginLogs_UserId",
                table: "UserLoginLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UQ_UserNotificationPreferences_Channel_ExternalId",
                table: "UserNotificationPreferences",
                columns: new[] { "Channel", "ExternalId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [ExternalId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UQ_UserNotificationPreferences_UserId_Channel",
                table: "UserNotificationPreferences",
                columns: new[] { "UserId", "Channel" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Users_DefaultPayoutAddress",
                table: "Users",
                column: "DefaultPayoutAddress");

            migrationBuilder.CreateIndex(
                name: "IX_Users_DefaultRefundAddress",
                table: "Users",
                column: "DefaultRefundAddress");

            migrationBuilder.CreateIndex(
                name: "UQ_Users_SteamId",
                table: "Users",
                column: "SteamId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminRolePermissions");

            migrationBuilder.DropTable(
                name: "AdminUserRoles");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BlockchainTransactions");

            migrationBuilder.DropTable(
                name: "ColdWalletTransfers");

            migrationBuilder.DropTable(
                name: "Disputes");

            migrationBuilder.DropTable(
                name: "ExternalIdempotencyRecords");

            migrationBuilder.DropTable(
                name: "FraudFlags");

            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "ProcessedEvents");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "SellerPayoutIssues");

            migrationBuilder.DropTable(
                name: "SystemHeartbeats");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropTable(
                name: "TradeOffers");

            migrationBuilder.DropTable(
                name: "TransactionHistory");

            migrationBuilder.DropTable(
                name: "UserLoginLogs");

            migrationBuilder.DropTable(
                name: "UserNotificationPreferences");

            migrationBuilder.DropTable(
                name: "AdminRoles");

            migrationBuilder.DropTable(
                name: "PaymentAddresses");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "PlatformSteamBots");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
