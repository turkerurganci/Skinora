using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T73_AddNextAttemptAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "BlockchainTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[] { new Guid("0aa51010-0000-0000-0000-000000000033"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "string", "Outbound transfer (payout/refund/sweep) retry aralıkları (dakika, CSV). Her transient failure NextAttemptAt'i listedeki sıradaki değerle ileriye iter; liste bittiğinde transfer FAILED + admin alert. Default '1,5,15' = T73 plan'ı.", true, "blockchain.transfer_retry_intervals_minutes", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "1,5,15" });

            migrationBuilder.CreateIndex(
                name: "IX_BlockchainTransactions_DispatchScan",
                table: "BlockchainTransactions",
                columns: new[] { "Status", "NextAttemptAt", "CreatedAt" },
                filter: "[Status] = 'PENDING'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BlockchainTransactions_DispatchScan",
                table: "BlockchainTransactions");

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000033"));

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "BlockchainTransactions");
        }
    }
}
