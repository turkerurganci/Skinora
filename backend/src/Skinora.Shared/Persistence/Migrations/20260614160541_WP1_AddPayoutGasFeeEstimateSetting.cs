using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WP1_AddPayoutGasFeeEstimateSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[] { new Guid("0aa51010-0000-0000-0000-00000000003b"), "Commission", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "WP1 MVP satıcı payout gas fee tahmini (USDT). SellerPayoutQueueJob bu değeri gas-fee koruma split'inde (02 §4.7) kullanır: gasFee komisyon×%10 eşiğini aşarsa aşan kısım satıcının alacağından düşülür (04 §7.3 örneği: 0.50 → satıcıdan 0.30). T74 energy delegation tamamlandıktan sonra runtime Energy/Bandwidth bedeli ile değiştirilir.", true, "blockchain.payout_gas_fee_estimate_usdt", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "0.50" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000003b"));
        }
    }
}
