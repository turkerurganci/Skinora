using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T77_AddHotWalletMonitorSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[,]
                {
                    { new Guid("0aa51010-0000-0000-0000-000000000039"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "string", "Hot wallet bakiye monitor job'unun cron ifadesi (T77 — 05 §3.3). Default '*/15 * * * *' (her 15 dakikada bir). Değiştirildikten sonra host restart gerekir (admin runtime override T96 devir).", true, "hot_wallet.monitor_cron", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "*/15 * * * *" },
                    { new Guid("0aa51010-0000-0000-0000-00000000003a"), "Wallet", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Hot wallet TRX bakiye alt eşiği (TRX, gas için). Bu değerin altına düşerse HOT_WALLET_THRESHOLD_BREACHED audit + admin SignalR alert fırlar (T77 — 05 §3.3). MVP ölçeğinde 100 TRX ≈ 50 TRC-20 transfer gas worst-case headroom.", true, "hot_wallet.trx_balance_minimum", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "100" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000039"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000003a"));
        }
    }
}
