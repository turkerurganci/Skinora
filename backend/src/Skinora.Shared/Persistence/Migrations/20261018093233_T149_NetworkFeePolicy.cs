using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T149_NetworkFeePolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "NetworkFeeAmount",
                table: "Transactions",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            // T149 (06 §1.4) — NULL means "not fixed yet". A transaction
            // accepted before T149 has no line and never will (it is never
            // corrected afterwards, 08 §3.1a); write that as an explicit 0 so
            // every accepted row carries the value its TotalAmount was built
            // with. TotalAmount itself is untouched.
            migrationBuilder.Sql(
                "UPDATE [Transactions] SET [NetworkFeeAmount] = 0 " +
                "WHERE [AcceptedAt] IS NOT NULL AND [NetworkFeeAmount] IS NULL;");

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[,]
                {
                    { new Guid("0aa51010-0000-0000-0000-00000000004c"), "Commission", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "TRON ağ ücreti satırı — sweep'e kadar (~8 gün) TRX fiyat oynamasına karşı tampon oranı (11 §F8 K8). Satır = YUKARI_2_HANE(sweep maliyeti TRX × canlı TRX/USDT × (1 + bu oran)). 0,007 = 02 §5 örneği (8,44 TRX × 0,34 × 1,007 = 2,8901 → 2,90). 0 ≤ değer < 1.", true, "blockchain.tron.network_fee_buffer_ratio", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "0.007" },
                    { new Guid("0aa51010-0000-0000-0000-00000000004d"), "Commission", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "TRON ağ ücreti satırı YEDEĞİ (USDT) — kabul anında sweep tahmini alınamazsa (servis/zincir/kur erişilemez ya da tahmin blockchain.max_charged_gas_fee_usdt'yi aşar) satıra bu değer yazılır; kabul tahmine bloke olmaz. Satır sonradan düzeltilmez (08 §3.1a).", true, "blockchain.tron.network_fee_fallback_usdt", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "3.00" },
                    { new Guid("0aa51010-0000-0000-0000-00000000004e"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Ethereum minimum iade eşiği — net iade < gas fee × bu oran ise iade yapılmaz, admin uyarısı (02 §4.6). Canlı tahmin kullandığı için Ethereum'da yalnız tozu engeller. TRON'un karşılığı min_refund_threshold_ratio.", true, "blockchain.ethereum.min_refund_threshold_ratio", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "2.0" }
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Transactions_NetworkFeeAmount_NonNegative",
                table: "Transactions",
                sql: "NetworkFeeAmount IS NULL OR NetworkFeeAmount >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A non-zero line is part of a TotalAmount and of a deposit's
            // ExpectedAmount; dropping the column would leave totals nobody can
            // explain and refunds that cannot tell the line apart. Refuse
            // rather than lose it.
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [Transactions] WHERE [NetworkFeeAmount] > 0) " +
                "THROW 50149, 'T149_NetworkFeePolicy Down: transactions carry a network fee line; the pre-T149 schema cannot hold it.', 1;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Transactions_NetworkFeeAmount_NonNegative",
                table: "Transactions");

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000004c"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000004d"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000004e"));

            migrationBuilder.DropColumn(
                name: "NetworkFeeAmount",
                table: "Transactions");
        }
    }
}
