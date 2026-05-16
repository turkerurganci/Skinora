using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T72_AddRefundGasFeeEstimate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[] { new Guid("0aa51010-0000-0000-0000-000000000032"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "T72 MVP iade gas fee tahmini (USDT). RefundDecisionService bu değeri kullanarak iade tutarının `gasFee × min_refund_threshold_ratio` eşiğini geçip geçmediğine karar verir. T74 energy delegation tamamlandıktan sonra runtime Energy/Bandwidth bedeli ile değiştirilir.", true, "blockchain.refund_gas_fee_estimate_usdt", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "2.0" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000032"));
        }
    }
}
