using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Skinora.Shared.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class T43_AddReputationThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "Category", "CreatedAt", "DataType", "Description", "IsConfigured", "Key", "UpdatedAt", "UpdatedByAdminId", "Value" },
                values: new object[,]
                {
                    { new Guid("0aa51010-0000-0000-0000-000000000021"), "Reputation", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "Yeni hesap koruması — hesap yaşı bu eşiğin altındaysa composite reputationScore null döner ('Yeni kullanıcı').", true, "reputation.min_account_age_days", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "30" },
                    { new Guid("0aa51010-0000-0000-0000-000000000022"), "Reputation", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "int", "İstatistiksel anlamlılık — tamamlanmış işlem sayısı bu eşiğin altındaysa composite reputationScore null döner.", true, "reputation.min_completed_transactions", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "3" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000021"));

            migrationBuilder.DeleteData(
                table: "SystemSettings",
                keyColumn: "Id",
                keyValue: new Guid("0aa51010-0000-0000-0000-000000000022"));
        }
    }
}
