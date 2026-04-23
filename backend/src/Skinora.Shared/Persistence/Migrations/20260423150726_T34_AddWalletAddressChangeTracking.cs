using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T34_AddWalletAddressChangeTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PayoutAddressChangedAt",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundAddressChangedAt",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[,]
                {
                    { new Guid("0aa51010-0000-0000-0000-00000000001f"), "Wallet", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Satıcı ödeme adresi değişikliği sonrası cooldown süresi (saat). Cooldown süresince yeni işlem başlatma engellenir; mevcut CREATED davetler eski snapshot adresle devam eder (02 §12.3).", true, "wallet.payout_address_cooldown_hours", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "24" },
                    { new Guid("0aa51010-0000-0000-0000-000000000020"), "Wallet", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Alıcı iade adresi değişikliği sonrası cooldown süresi (saat). Cooldown süresince yeni işlem başlatma ve işlem kabul etme engellenir (02 §12.3).", true, "wallet.refund_address_cooldown_hours", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "24" }
                });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                columns: new[] { "PayoutAddressChangedAt", "RefundAddressChangedAt" },
                values: new object[] { null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-00000000001f"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000020"));

            migrationBuilder.DropColumn(
                name: "PayoutAddressChangedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RefundAddressChangedAt",
                table: "Users");
        }
    }
}
