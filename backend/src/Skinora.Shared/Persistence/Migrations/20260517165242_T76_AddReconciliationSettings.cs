using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T76_AddReconciliationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[,]
                {
                    { new Guid("0aa51010-0000-0000-0000-000000000036"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "string", "Reconciliation job cron ifadesi (05 §3.3). Default '0 3 * * *' (03:00 UTC günlük). Değiştirildikten sonra host restart gerekir (admin runtime override T96 devir).", true, "reconciliation.schedule_cron", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "0 3 * * *" },
                    { new Guid("0aa51010-0000-0000-0000-000000000037"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "string", "Reconciliation karşılaştırması için hot wallet Tron adresi. 'NONE' ise hot wallet kapsamı atlanır (warn log). Production deploy bu değeri ayarlamalıdır (05 §3.3) — auth.banned_countries NONE sentinel pattern.", true, "reconciliation.hot_wallet_address", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "NONE" },
                    { new Guid("0aa51010-0000-0000-0000-000000000038"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "string", "Reconciliation karşılaştırması için cold wallet Tron adresi (opsiyonel). 'NONE' ise cold wallet kapsamı atlanır (info log). MVP'de cold transfer manuel başlatılır — ColdWalletTransfer ledger'a eşleştirilir.", true, "reconciliation.cold_wallet_address", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "NONE" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000036"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000037"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000038"));
        }
    }
}
