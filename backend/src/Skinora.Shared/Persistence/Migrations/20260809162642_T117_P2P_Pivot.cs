using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <summary>
    /// T117 — P2P pivot of the transaction domain core (02 §2.1, 06 §3.5).
    /// Retires the bot custody layer (TradeOffers, PlatformSteamBots,
    /// BotRecoveryItems + the Transaction columns that pointed at them) and adds
    /// the delivery-verification and settlement columns that replace it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The column renames here are hand-written on purpose.</b> The scaffolder
    /// pairs dropped and added columns by type, not by meaning, and produced
    /// three wrong renames: <c>TradeOfferToSellerDeadline → SettlementVerifiedAt</c>,
    /// <c>TradeOfferToBuyerDeadline → SellerReadyConfirmedAt</c> and
    /// <c>ItemEscrowedAt → SellerConfirmDeadline</c>. The first is the dangerous
    /// one: it would carry an old deadline into the column the COMPLETED guard
    /// reads as proof of settlement, so every pre-existing row would look
    /// settlement-verified and become payable without the item ever being
    /// re-checked (02 §4.5.1). The correct pairing is by phase:
    /// <c>TradeOfferToSellerDeadline → SellerConfirmDeadline</c> and
    /// <c>TradeOfferToBuyerDeadline → DeliveryDeadline</c>; every settlement and
    /// delivery-evidence column is genuinely new and starts NULL.
    /// </para>
    /// <para>
    /// <b>Retired status values are not remapped.</b> Rows still carrying
    /// <c>ITEM_ESCROWED</c> / <c>TRADE_OFFER_SENT_TO_*</c> would describe a
    /// transaction whose item is physically inside a platform bot's inventory —
    /// there is no automatic mapping to a P2P state that is also true. Per the
    /// T117 acceptance criteria this migration targets a clean database; any
    /// non-empty environment must settle its in-flight custodial transactions
    /// before it is applied.
    /// </para>
    /// </remarks>
    public partial class T117_P2P_Pivot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---------- Bot custody layer removal ----------
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_PlatformSteamBots_EscrowBotId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "BotRecoveryItems");

            migrationBuilder.DropTable(
                name: "TradeOffers");

            migrationBuilder.DropTable(
                name: "PlatformSteamBots");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_EscrowBotId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "EscrowBotAssetId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "EscrowBotId",
                table: "Transactions");

            // The platform never takes custody, so there is no "escrowed at"
            // moment to record (02 §9).
            migrationBuilder.DropColumn(
                name: "ItemEscrowedAt",
                table: "Transactions");

            // ---------- Deadline renames (phase-preserving) ----------
            migrationBuilder.RenameColumn(
                name: "TradeOfferToSellerDeadline",
                table: "Transactions",
                newName: "SellerConfirmDeadline");

            migrationBuilder.RenameColumn(
                name: "TradeOfferToBuyerDeadline",
                table: "Transactions",
                newName: "DeliveryDeadline");

            // ---------- Delivery verification (02 §9.2) ----------
            migrationBuilder.AddColumn<string>(
                name: "BuyerTradeUrl",
                table: "Transactions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SellerReadyConfirmedAt",
                table: "Transactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BuyerBaselineClassCount",
                table: "Transactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerBaselineAssetIds",
                table: "Transactions",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BuyerBaselineCapturedAt",
                table: "Transactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BuyerConfirmedReceiptAt",
                table: "Transactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryVerifiedAt",
                table: "Transactions",
                type: "datetime2",
                nullable: true);

            // Stored as int, not as the global EnumToStringConverter's name:
            // [Flags] combinations would serialize as comma-joined names and
            // become impractical to filter on (06 §2.24).
            migrationBuilder.AddColumn<int>(
                name: "DeliveryEvidence",
                table: "Transactions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // ---------- Settlement (02 §4.5.1) ----------
            migrationBuilder.AddColumn<DateTime>(
                name: "PayoutEligibleAt",
                table: "Transactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SettlementVerifiedAt",
                table: "Transactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryReversedAt",
                table: "Transactions",
                type: "datetime2",
                nullable: true);

            // ---------- Indexes ----------
            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Delivery_Pending",
                table: "Transactions",
                columns: new[] { "Status", "DeliveryDeadline" },
                filter: "[Status] = 'PAYMENT_RECEIVED'");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Settlement_Pending",
                table: "Transactions",
                columns: new[] { "Status", "PayoutEligibleAt" },
                filter: "[Status] = 'ITEM_DELIVERED'");

            // One open transaction per item (02 §2.3). Delivery is verified at
            // the item-class level, so two live transactions on the same asset
            // would let an arriving item be attributed to the wrong one — and
            // pay the wrong seller.
            migrationBuilder.CreateIndex(
                name: "UQ_Transactions_SellerId_ItemAssetId_Active",
                table: "Transactions",
                columns: new[] { "SellerId", "ItemAssetId" },
                unique: true,
                filter: "[Status] <> 'COMPLETED' AND [Status] <> 'CANCELLED_TIMEOUT' AND [Status] <> 'CANCELLED_SELLER' AND [Status] <> 'CANCELLED_BUYER' AND [Status] <> 'CANCELLED_ADMIN' AND [Status] <> 'REFUNDED' AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_Delivery_Pending",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_Settlement_Pending",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "UQ_Transactions_SellerId_ItemAssetId_Active",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "BuyerTradeUrl",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SellerReadyConfirmedAt",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "BuyerBaselineClassCount",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "BuyerBaselineAssetIds",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "BuyerBaselineCapturedAt",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "BuyerConfirmedReceiptAt",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeliveryVerifiedAt",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeliveryEvidence",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "PayoutEligibleAt",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SettlementVerifiedAt",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DeliveryReversedAt",
                table: "Transactions");

            migrationBuilder.RenameColumn(
                name: "SellerConfirmDeadline",
                table: "Transactions",
                newName: "TradeOfferToSellerDeadline");

            migrationBuilder.RenameColumn(
                name: "DeliveryDeadline",
                table: "Transactions",
                newName: "TradeOfferToBuyerDeadline");

            migrationBuilder.AddColumn<DateTime>(
                name: "ItemEscrowedAt",
                table: "Transactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EscrowBotAssetId",
                table: "Transactions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EscrowBotId",
                table: "Transactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlatformSteamBots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActiveEscrowCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DailyTradeOfferCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LastHealthCheckAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RestrictionReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SteamId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSteamBots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BotRecoveryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdminNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PlatformSteamBotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecoveryStatus = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResponsibleAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StatusAtRestriction = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotRecoveryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BotRecoveryItems_PlatformSteamBots_PlatformSteamBotId",
                        column: x => x.PlatformSteamBotId,
                        principalTable: "PlatformSteamBots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BotRecoveryItems_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BotRecoveryItems_Users_ResponsibleAdminId",
                        column: x => x.ResponsibleAdminId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TradeOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PlatformSteamBotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SteamTradeOfferId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_EscrowBotId",
                table: "Transactions",
                column: "EscrowBotId");

            migrationBuilder.CreateIndex(
                name: "IX_BotRecoveryItems_PlatformSteamBotId_RecoveryStatus",
                table: "BotRecoveryItems",
                columns: new[] { "PlatformSteamBotId", "RecoveryStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_BotRecoveryItems_ResponsibleAdminId",
                table: "BotRecoveryItems",
                column: "ResponsibleAdminId");

            migrationBuilder.CreateIndex(
                name: "UQ_BotRecoveryItems_TransactionId",
                table: "BotRecoveryItems",
                column: "TransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_PlatformSteamBots_SteamId",
                table: "PlatformSteamBots",
                column: "SteamId",
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

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_PlatformSteamBots_EscrowBotId",
                table: "Transactions",
                column: "EscrowBotId",
                principalTable: "PlatformSteamBots",
                principalColumn: "Id");
        }
    }
}
