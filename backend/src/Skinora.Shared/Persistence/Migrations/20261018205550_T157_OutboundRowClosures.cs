using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T157_OutboundRowClosures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboundRowClosures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlockchainTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Network = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TxHash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProposedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecidedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundRowClosures", x => x.Id);
                    table.CheckConstraint("CK_OutboundRowClosures_Approved_DifferentAdmin", "([Status] <> 'APPROVED') OR ([DecidedByAdminId] IS NOT NULL AND [DecidedByAdminId] <> [ProposedByAdminId] AND [DecidedAt] IS NOT NULL)");
                    table.CheckConstraint("CK_OutboundRowClosures_Decision_Fields", "([Status] = 'PROPOSED' AND [DecidedByAdminId] IS NULL AND [DecidedAt] IS NULL) OR ([Status] <> 'PROPOSED' AND [DecidedByAdminId] IS NOT NULL AND [DecidedAt] IS NOT NULL)");
                    table.CheckConstraint("CK_OutboundRowClosures_Network_Ethereum", "[Network] = 'ETHEREUM'");
                    table.ForeignKey(
                        name: "FK_OutboundRowClosures_BlockchainTransactions_BlockchainTransactionId",
                        column: x => x.BlockchainTransactionId,
                        principalTable: "BlockchainTransactions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OutboundRowClosures_Users_DecidedByAdminId",
                        column: x => x.DecidedByAdminId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OutboundRowClosures_Users_ProposedByAdminId",
                        column: x => x.ProposedByAdminId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundRowClosures_DecidedByAdminId",
                table: "OutboundRowClosures",
                column: "DecidedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundRowClosures_ProposedByAdminId",
                table: "OutboundRowClosures",
                column: "ProposedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundRowClosures_Status",
                table: "OutboundRowClosures",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UQ_OutboundRowClosures_Network_TxHash_Live",
                table: "OutboundRowClosures",
                columns: new[] { "Network", "TxHash" },
                unique: true,
                filter: "[Status] <> 'REJECTED'");

            migrationBuilder.CreateIndex(
                name: "UQ_OutboundRowClosures_Row_Live",
                table: "OutboundRowClosures",
                column: "BlockchainTransactionId",
                unique: true,
                filter: "[Status] <> 'REJECTED'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The closure records are the working record of a two-person money
            // decision; rolling back must not silently drop them. The AuditLog
            // keeps the trail, but a row that still has a live closure is refused.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM OutboundRowClosures)
    THROW 50157, 'T157 Down refused: OutboundRowClosures holds closure records. Export them (and keep the AuditLog) before rolling back.', 1;");

            migrationBuilder.DropTable(
                name: "OutboundRowClosures");
        }
    }
}
