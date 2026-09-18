using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemovePlatformWalletAddressSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000037"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000038"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[,]
                {
                    { new Guid("0aa51010-0000-0000-0000-000000000037"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "string", "Reconciliation karşılaştırması için hot wallet Tron adresi. 'NONE' ise hot wallet kapsamı atlanır (warn log). Production deploy bu değeri ayarlamalıdır (05 §3.3) — auth.banned_countries NONE sentinel pattern.", true, "reconciliation.hot_wallet_address", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "NONE" },
                    { new Guid("0aa51010-0000-0000-0000-000000000038"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "string", "Reconciliation karşılaştırması için cold wallet Tron adresi (opsiyonel). 'NONE' ise cold wallet kapsamı atlanır (info log). MVP'de cold transfer manuel başlatılır — ColdWalletTransfer ledger'a eşleştirilir.", true, "reconciliation.cold_wallet_address", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "NONE" }
                });
        }
    }
}
