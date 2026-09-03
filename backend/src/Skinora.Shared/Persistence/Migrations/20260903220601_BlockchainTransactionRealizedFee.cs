using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BlockchainTransactionRealizedFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "EnergyUsageTotal",
                table: "BlockchainTransactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OriginEnergyUsage",
                table: "BlockchainTransactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RealizedFeeSun",
                table: "BlockchainTransactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[] { new Guid("0aa51010-0000-0000-0000-000000000040"), "Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "decimal", "Kullanicidan kesilebilecek gas fee ust siniri (USDT). Runtime tahmin bu degeri asarsa tahmin REDDEDILIR (kirpilmaz) ve statik fallback kesilir; admin logu duser. Gercek bir mainnet TRC-20 gonderimi ~6,4 TRX (~2 USDT) yaktigi icin varsayilan 10.0 saglikli hicbir tahmini tetiklemez — bozuk bir tahmini yakalamak icindir. 0 = tavan kapali.", true, "blockchain.max_charged_gas_fee_usdt", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "10.0" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000040"));

            migrationBuilder.DropColumn(
                name: "EnergyUsageTotal",
                table: "BlockchainTransactions");

            migrationBuilder.DropColumn(
                name: "OriginEnergyUsage",
                table: "BlockchainTransactions");

            migrationBuilder.DropColumn(
                name: "RealizedFeeSun",
                table: "BlockchainTransactions");
        }
    }
}
