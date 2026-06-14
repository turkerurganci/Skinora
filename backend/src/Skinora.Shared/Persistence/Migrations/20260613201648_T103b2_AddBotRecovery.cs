using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T103b2_AddBotRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RestrictionReason",
                table: "PlatformSteamBots",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BotRecoveryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlatformSteamBotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecoveryStatus = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StatusAtRestriction = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponsibleAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AdminNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BotRecoveryItems");

            migrationBuilder.DropColumn(
                name: "RestrictionReason",
                table: "PlatformSteamBots");
        }
    }
}
